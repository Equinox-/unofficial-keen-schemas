using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SchemaService.SteamUtils;
using SteamKit2.Internal;

namespace SchemaBuilder
{
    public class GameManager : IHostedService
    {
        // Data files required for definition loading.
        private static readonly string[] DataFileExtensions = { ".mwm", ".sbc", ".resx", ".xml" };

        private static bool IsDataFile(string path)
        {
            foreach (var ext in DataFileExtensions)
                if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private const int Workers = 4;
        private const bool Skip = false;

        private readonly SteamDownloader _steamInternal;
        private readonly ILogger<GameManager> _log;

        private readonly string _rootDir;

        public GameManager(SteamDownloader steam, ILogger<GameManager> log)
        {
            _steamInternal = steam;
            _log = log;
            _rootDir = Path.GetFullPath("./");
        }

        private const string ContentDir = "Content";
        private const string BinariesDir = "DedicatedServer64";

        public async Task<GameInstall> RestoreGame(Game game, string branch)
        {
            var info = GameInfo.Games[game];
            branch ??= info.SteamBranch;
            var installDir = Path.Combine(_rootDir, "game", game.ToString(), branch);
            if (!Skip)
                await RunWithRetry(steam => steam.InstallAppAsync(info.SteamDedicatedAppId, info.SteamDedicatedDepotId, branch, installDir,
                    path => path.StartsWith(BinariesDir) || IsDataFile(path), game.ToString()));

            return new GameInstall(this, _log, game, Path.Combine(installDir, ContentDir), Path.Combine(installDir, BinariesDir));
        }

        public async Task<List<PublishedFileDetails>> ResolveMods(Game game, params ulong[] ids)
        {
            var info = GameInfo.Games[game];

            // Download all mods in dependency graph.
            var all = new Dictionary<ulong, PublishedFileDetails>();
            var queued = new HashSet<ulong>(ids);
            while (queued.Count > 0)
            {
                var details = await RunWithRetry(steam => steam.LoadModDetails(info.SteamGameAppId, queued.ToArray()));
                if (details.Count < queued.Count)
                    throw new Exception($"Failed to load details for mod(s) {string.Join(", ", queued.Where(x => !details.ContainsKey(x)))}");
                queued.Clear();
                foreach (var mod in details)
                {
                    if (mod.Value.publishedfileid != mod.Key)
                        throw new Exception("Mod reported wrong published file ID");
                    all.Add(mod.Value.publishedfileid, mod.Value);
                    foreach (var dep in mod.Value.children)
                        queued.Add(dep.publishedfileid);
                }

                foreach (var loaded in all.Keys)
                    queued.Remove(loaded);
            }

            // Sort the mods.
            var order = new List<PublishedFileDetails>();
            while (all.Count > 0)
            {
                var rank = new List<(PublishedFileDetails, int)>();
                foreach (var item in all.Values)
                {
                    var missing = 0;
                    foreach (var dep in item.children)
                        if (all.ContainsKey(dep.publishedfileid))
                            missing++;
                    rank.Add((item, missing));
                }

                rank.Sort((a, b) => a.Item2.CompareTo(b.Item2));

                var minRank = rank[0].Item2;
                var firstNonCycle = rank.FindIndex(a => a.Item2 > minRank);
                if (firstNonCycle > 0)
                    rank.RemoveRange(firstNonCycle, rank.Count - firstNonCycle);

                if (minRank > 0)
                    _log.LogWarning($"Mod dependency cycle encountered: {string.Join(", ", rank.Select(x => $"{x.Item1.title}({x.Item1.publishedfileid})"))}");

                foreach (var item in rank)
                {
                    all.Remove(item.Item1.publishedfileid);
                    order.Add(item.Item1);
                }
            }

            return order;
        }

        public async Task<string> RestoreMod(Game game, PublishedFileDetails details)
        {
            var info = GameInfo.Games[game];
            var installDir = Path.Combine(_rootDir, "game", game + "-workshop", details.publishedfileid.ToString());
            if (!Skip)
                await RunWithRetry(steam => steam.InstallModAsync(info.SteamGameAppId, details.publishedfileid, installDir,
                    path => path.IndexOf("Data/Scripts", StringComparison.OrdinalIgnoreCase) >= 0
                            || path.IndexOf("Data\\Scripts", StringComparison.OrdinalIgnoreCase) >= 0
                            || IsDataFile(path),
                    $"{game}-{details.publishedfileid}-{details.title}"));
            return installDir;
        }

        private async Task<T> RunWithRetry<T>(Func<SteamDownloader, Task<T>> action)
        {
            try
            {
                return await action(_steamInternal);
            }
            catch
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                return await action(_steamInternal);
            }
        }

        public Task StartAsync(CancellationToken cancellationToken) => RunWithRetry(steam => steam.LoginAsync());

        public Task StopAsync(CancellationToken cancellationToken) => RunWithRetry(async steam =>
        {
            await steam.LogoutAsync();
            return 0;
        });

        internal static void HookAssemblyLoading(string binaries)
        {
            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            {
                var asm = new AssemblyName(args.Name);
                var bin = Path.Combine(binaries, asm.Name + ".dll");
                return File.Exists(bin) ? Assembly.LoadFrom(bin) : null;
            };
        }
    }

    public class GameInstall
    {
        private readonly GameManager _owner;
        private readonly ILogger _log;
        public readonly string BinariesDir, ContentDir;
        private volatile List<Assembly> _dotnetAssemblies;
        private readonly GameInfo _gameInfo;

        private volatile bool _compilerInit;
        private volatile GameScriptCompiler _compiler;

        public Game Game { get; }

        public GameInstall(GameManager owner, ILogger log, Game game, string contentDir, string binariesDir)
        {
            _owner = owner;
            _log = log;
            Game = game;
            _gameInfo = GameInfo.Games[game];
            ContentDir = contentDir;
            BinariesDir = binariesDir;
        }

        public List<Assembly> LoadAssemblies()
        {
            if (_dotnetAssemblies != null)
                return _dotnetAssemblies;
            lock (this)
            {
                if (_dotnetAssemblies != null)
                    return _dotnetAssemblies;
                GameManager.HookAssemblyLoading(BinariesDir);
                var assemblies = new List<Assembly>();
                var failures = new Dictionary<string, Exception>();
                foreach (var assembly in Directory.GetFiles(BinariesDir, "*.dll", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileNameWithoutExtension(assembly);
                    if (!_gameInfo.BinaryPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    try
                    {
                        var asm = Assembly.Load(name);
                        // Ensure the types get loaded.
                        asm.GetTypes();
                        assemblies.Add(asm);
                    }
                    catch (Exception err)
                    {
                        // ignore assembly load errors, they usually just mean that the assembly is not dotnet.
                        failures.Add(assembly, err);
                    }
                }

                if (assemblies.Count == 0)
                {
                    _log.LogError("All assemblies failed to load!");
                    foreach (var error in failures)
                        _log.LogWarning(error.Value, "Assembly {Assembly} failed to load", error.Key);
                }

                return _dotnetAssemblies = assemblies;
            }
        }

        public async Task<List<ModInstall>> LoadMods(IEnumerable<ulong> mods, Predicate<ulong> filter = null)
        {
            var modsCopy = mods.ToArray();
            if (modsCopy.Length == 0)
                return new List<ModInstall>();
            var resolved = await _owner.ResolveMods(Game, modsCopy);
            var installed = new List<ModInstall>(resolved.Count);
            _log.LogInformation("Installing {Count} mods", modsCopy.Length);
            const int batchSize = 8;
            for (var i = 0; i < resolved.Count; i += batchSize)
            {
                var batch = new List<Task<ModInstall>>(batchSize);
                for (var j = i; j < Math.Min(resolved.Count, i + batchSize); j++)
                {
                    var item = resolved[j];
                    if (filter != null && !filter(item.publishedfileid)) continue; 
                    batch.Add(Task.Run(async () =>
                    {
                        var modSources = await _owner.RestoreMod(Game, item);
                        var modDependencies = installed.ToList();
                        return new ModInstall(_log,
                            this,
                            item,
                            modSources,
                            Path.Combine(modSources, "..", "binaries"),
                            modDependencies);
                    }));
                }
                foreach (var task in batch)
                    installed.Add(await task);
            }
            _log.LogInformation("Installed {Count} mods", modsCopy.Length);
            return installed;
        }

        public GameScriptCompiler ScriptCompiler
        {
            get
            {
                if (_compilerInit)
                    return _compiler;
                lock (this)
                {
                    if (_compilerInit)
                        return _compiler;
                    _compiler = CreateCompiler();
                    _compilerInit = true;
                    return _compiler;
                }
            }
        }


        private GameScriptCompiler CreateCompiler()
        {
            LoadAssemblies();
            try
            {
                var compiler = Game switch
                {
                    Game.MedievalEngineers => new MedievalScriptCompiler(this),
                    Game.SpaceEngineers => null,
                    _ => throw new ArgumentOutOfRangeException()
                };
                if (compiler == null)
                    _log.LogWarning($"Game {Game} does not support script compilation");
                return compiler;
            }
            catch (Exception err)
            {
                _log.LogWarning(err, $"Game {Game} script compiler support is broken");
                return null;
            }
        }
    }

    public class ModInstall
    {
        private readonly ILogger _log;
        private readonly GameInstall _game;
        public readonly string ContentDir;
        private readonly string _binariesDir;
        private readonly List<ModInstall> _dependencies;
        private volatile List<Assembly> _dotnetAssemblies;

        public PublishedFileDetails Details { get; }

        public ModInstall(
            ILogger log,
            GameInstall game,
            PublishedFileDetails details,
            string contentDir,
            string binariesDir,
            List<ModInstall> dependencies)
        {
            _log = log;
            _game = game;
            Details = details;
            ContentDir = contentDir;
            _binariesDir = binariesDir;
            _dependencies = dependencies;
        }

        public IReadOnlyList<Assembly> LoadAssemblies()
        {
            if (_dotnetAssemblies != null)
                return _dotnetAssemblies;
            lock (this)
            {
                if (_dotnetAssemblies != null)
                    return _dotnetAssemblies;

                return _dotnetAssemblies = LoadAssembliesInternal();
            }
        }

        private List<Assembly> LoadAssembliesInternal()
        {
            var assemblies = new List<Assembly>();

            var compiler = _game.ScriptCompiler;
            if (compiler == null)
                return new List<Assembly>();

            var scriptDir = Path.Combine(ContentDir, "Data", "Scripts");
            if (!Directory.Exists(scriptDir))
                return new List<Assembly>();

            var scriptFiles = Directory.GetFiles(
                scriptDir,
                "*.cs",
                SearchOption.AllDirectories);
            if (scriptFiles.Length == 0)
                return new List<Assembly>();

            var references = new List<Assembly>();
            foreach (var dep in _dependencies)
                references.AddRange(dep.LoadAssemblies());

            var fileName = $"mod-{Details.publishedfileid}";
            var mddFile = Path.Combine(_binariesDir, fileName + ".txt");
            var dllFile = Path.Combine(_binariesDir, fileName + ".dll");
            var docFile = Path.Combine(_binariesDir, fileName + ".xml");

            TaskAvoidance.MaybeRun(
                _log,
                mddFile,
                $"Compiling {Details.title} ({Details.publishedfileid})",
                () =>
                {
                    Directory.CreateDirectory(_binariesDir);
                    if (compiler.CompileInto(new CompilationArgs
                        {
                            AssemblyName = $"mod-{Details.publishedfileid}",
                            ScriptFiles = scriptFiles,
                            References = references
                        }, dllFile, docFile)) return;
                    File.Delete(dllFile);
                    File.Delete(docFile);
                },
                inputFiles: _game.LoadAssemblies()
                    .Concat(references)
                    .Select(x => x.Location)
                    .Concat(scriptFiles).ToArray(),
                outputFiles: new[] { dllFile, docFile });
            Directory.CreateDirectory(_binariesDir);


            if (!File.Exists(dllFile))
            {
                _log.LogWarning($"Failed to compile {Details.title} ({Details.publishedfileid})");
                return assemblies;
            }

            try
            {
                assemblies.Add(Assembly.LoadFrom(dllFile));
            }
            catch (Exception err)
            {
                _log.LogWarning(err, $"Failed to load compilation for {Details.title} ({Details.publishedfileid})");
                return assemblies;
            }

            GameManager.HookAssemblyLoading(_binariesDir);
            return assemblies;
        }
    }
}