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
        private const int Workers = 4;
        private const bool Skip = false;

        private readonly SteamDownloader _steam;
        private readonly ILogger<GameManager> _log;

        private readonly string _rootDir;

        public GameManager(SteamDownloader steam, ILogger<GameManager> log)
        {
            _steam = steam;
            _log = log;
            _rootDir = Path.GetFullPath("./");
        }

        private const string BinariesDir = "DedicatedServer64";

        public async Task<GameInstall> RestoreGame(Game game, string branch)
        {
            var info = GameInfo.Games[game];
            branch ??= info.SteamBranch;
            _log.LogInformation($"Installing game {game}, branch {branch}");
            var installDir = Path.Combine(_rootDir, "game", game.ToString(), branch);
            if (!Skip)
                await _steam.InstallAppAsync(info.SteamDedicatedAppId, info.SteamDedicatedDepotId, branch, installDir, Workers,
                    path => path.StartsWith(BinariesDir), game.ToString());

            return new GameInstall(this, _log, game, Path.Combine(installDir, BinariesDir));
        }

        public async Task<List<PublishedFileDetails>> ResolveMods(Game game, params ulong[] ids)
        {
            var info = GameInfo.Games[game];

            // Download all mods in dependency graph.
            var all = new Dictionary<ulong, PublishedFileDetails>();
            var queued = new HashSet<ulong>(ids);
            while (queued.Count > 0)
            {
                var details = await _steam.LoadModDetails(info.SteamGameAppId, queued.ToArray());
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
            _log.LogInformation($"Installing game {game}, mod {details.title} ({details.publishedfileid})");
            var installDir = Path.Combine(_rootDir, "game", game + "-workshop", details.publishedfileid.ToString());
            if (!Skip)
                await _steam.InstallModAsync(info.SteamGameAppId, details.publishedfileid, installDir, Workers,
                    path => path.IndexOf("Data/Scripts", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            path.IndexOf("Data\\Scripts", StringComparison.OrdinalIgnoreCase) >= 0,
                    $"{game}-{details.publishedfileid}-{details.title}");
            return installDir;
        }

        public Task StartAsync(CancellationToken cancellationToken) => _steam.LoginAsync();

        public Task StopAsync(CancellationToken cancellationToken) => _steam.LogoutAsync();

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
        private readonly ILogger<GameManager> _log;
        public readonly string BinariesDir;
        private volatile List<Assembly> _dotnetAssemblies;
        private readonly Game _game;
        private readonly GameInfo _gameInfo;

        private volatile bool _compilerInit;
        private volatile GameScriptCompiler _compiler;

        public GameInstall(GameManager owner, ILogger<GameManager> log, Game game, string binariesDir)
        {
            _owner = owner;
            _log = log;
            _game = game;
            _gameInfo = GameInfo.Games[game];
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
                        _log.LogWarning(error.Value, $"Assembly {error.Key} failed to load");
                }

                return _dotnetAssemblies = assemblies;
            }
        }

        public async Task<List<ModInstall>> LoadMods(params ulong[] mods)
        {
            if (mods.Length == 0)
                return new List<ModInstall>();
            var resolved = await _owner.ResolveMods(_game, mods);
            var installed = new List<ModInstall>(resolved.Count);
            foreach (var item in resolved)
            {
                var modSources = await _owner.RestoreMod(_game, item);
                var modDependencies = installed.ToList();
                installed.Add(new ModInstall(_log,
                    this,
                    item,
                    modSources,
                    Path.Combine(modSources, "..", "binaries"),
                    modDependencies));
            }

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
                var compiler = _game switch
                {
                    Game.MedievalEngineers => new MedievalScriptCompiler(this),
                    Game.SpaceEngineers => null,
                    _ => throw new ArgumentOutOfRangeException()
                };
                if (compiler == null)
                    _log.LogWarning($"Game {_game} does not support script compilation");
                return compiler;
            }
            catch (Exception err)
            {
                _log.LogWarning(err, $"Game {_game} script compiler support is broken");
                return null;
            }
        }
    }

    public class ModInstall
    {
        private readonly ILogger<GameManager> _log;
        private readonly GameInstall _game;
        private readonly string _sourceDir;
        private readonly string _binariesDir;
        private readonly List<ModInstall> _dependencies;
        private volatile List<Assembly> _dotnetAssemblies;
        private readonly PublishedFileDetails _details;

        public ModInstall(ILogger<GameManager> log, GameInstall game,
            PublishedFileDetails details,
            string sourceDir, string binariesDir, List<ModInstall> dependencies)
        {
            _log = log;
            _game = game;
            _details = details;
            _sourceDir = sourceDir;
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

            var scriptDir = Path.Combine(_sourceDir, "Data", "Scripts");
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

            var fileName = $"mod-{_details.publishedfileid}";
            var mddFile = Path.Combine(_binariesDir, fileName + ".txt");
            var dllFile = Path.Combine(_binariesDir, fileName + ".dll");
            var docFile = Path.Combine(_binariesDir, fileName + ".xml");

            TaskAvoidance.MaybeRun(
                _log,
                mddFile,
                $"Compiling {_details.title} ({_details.publishedfileid})",
                () =>
                {
                    Directory.CreateDirectory(_binariesDir);
                    if (compiler.CompileInto(new CompilationArgs
                        {
                            AssemblyName = $"mod-{_details.publishedfileid}",
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
                _log.LogWarning($"Failed to compile {_details.title} ({_details.publishedfileid})");
                return assemblies;
            }

            try
            {
                assemblies.Add(Assembly.LoadFrom(dllFile));
            }
            catch (Exception err)
            {
                _log.LogWarning(err, $"Failed to load compilation for {_details.title} ({_details.publishedfileid})");
                return assemblies;
            }

            GameManager.HookAssemblyLoading(_binariesDir);
            return assemblies;
        }
    }
}