using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;

namespace SchemaBuilder.Data
{
    public class DataExtractor
    {
        private readonly ILogger<DataExtractor> _log;
        private readonly GameManager _games;

        public DataExtractor(GameManager games, ILogger<DataExtractor> log)
        {
            _games = games;
            _log = log;
        }

        public async Task Generate(string name)
        {
            _log.LogInformation("Generating data {Name}", name);

            var config = DataConfig.Read("Config/Data", name);

            var gameInstall = await _games.RestoreGame(config.Game, config.SteamBranch);
            var modInstall = await gameInstall.LoadMods(config.Mods, mod => !config.ExcludeMods.Contains(mod));

            // Load the game
            gameInstall.LoadAssemblies();
            
            // Load the mods
            foreach (var mod in modInstall)
                mod.LoadAssemblies();
            
            // Compile data extractor.
            var dataExtractor = LoadExtractor(gameInstall, modInstall);

            var dataPath = Path.Combine("data", name + ".txt");
            Directory.CreateDirectory(Path.GetDirectoryName(dataPath)!);
            using var outStream = File.Open(dataPath, FileMode.Create, FileAccess.Write);
            using var outWriter = new StreamWriter(outStream);
            // ReSharper disable once AccessToDisposedClosure
            dataExtractor(
                line => outWriter.WriteLine(line),
                gameInstall.ContentDir,
                modInstall.Select(mod => Tuple.Create(mod.Details.publishedfileid, mod.ContentDir)).ToArray());
            await outWriter.FlushAsync();
        }

        private delegate void RunExtractor(Action<string> writeLine, string gameContent, Tuple<ulong, string>[] modContent);

        private RunExtractor LoadExtractor(GameInstall install, IReadOnlyList<ModInstall> mods)
        {
            var extractorName = install.Game switch
            {
                Game.MedievalEngineers => "DataExtractorMedieval",
                Game.SpaceEngineers => "DataExtractorSpace",
                _ => throw new ArgumentOutOfRangeException()
            };
            var scripts = Directory.GetFiles(extractorName, "*.cs");
            if (scripts.Length == 0)
                throw new Exception($"No data extractor script files in {extractorName}");
            var sharedScripts = Directory.GetFiles("DataExtractorShared", "*.cs");

            var parseOptions = new CSharpParseOptions().WithPreprocessorSymbols(mods.Select(x => "MOD_" + x.Details.publishedfileid));

            var syntaxTrees = scripts
                .Concat(sharedScripts)
                .Select(path =>
                {
                    using var stream = File.OpenRead(path);
                    var text = SourceText.From(stream);
                    return CSharpSyntaxTree.ParseText(text, parseOptions, path);
                });

            var references = install.LoadAssemblies()
                .Concat(mods.SelectMany(mod => mod.LoadAssemblies()))
                .Concat(AppDomain.CurrentDomain.GetAssemblies())
                .Where(asm => !asm.IsDynamic)
                .Select(asm => asm.Location)
                .Distinct()
                .Select(loc => MetadataReference.CreateFromFile(loc))
                .ToList();

            var compilation = CSharpCompilation.Create(extractorName,
                syntaxTrees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Debug, platform: Platform.X64));
            using var assemblyStream = new MemoryStream();
            using var pdbStream = new MemoryStream();
            var result = compilation.Emit(assemblyStream, pdbStream);
            if (!result.Success)
            {
                foreach (var diagnostic in result.Diagnostics)
                    _log.Log(diagnostic.Severity switch
                        {
                            DiagnosticSeverity.Hidden => LogLevel.Trace,
                            DiagnosticSeverity.Info => LogLevel.Information,
                            DiagnosticSeverity.Warning => LogLevel.Warning,
                            DiagnosticSeverity.Error => LogLevel.Error,
                            _ => throw new ArgumentOutOfRangeException()
                        }, $"{diagnostic.GetMessage()}: {diagnostic.Location}");
                throw new Exception("Failed to compile");
            }

            assemblyStream.Seek(0, SeekOrigin.Begin);
            pdbStream.Seek(0, SeekOrigin.Begin);

            var assembly = Assembly.Load(assemblyStream.ToArray(), pdbStream.ToArray());
            var entrypoint = assembly.GetType(extractorName + ".Entrypoint");
            if (entrypoint == null)
                throw new Exception("Entrypoint missing in assembly");
            var method = entrypoint.GetMethod("Run", typeof(RunExtractor).GetMethod(nameof(RunExtractor.Invoke))!
                .GetParameters().Select(x => x.ParameterType).ToArray());
            if (method == null)
                throw new Exception("Entrypoint is missing a Main method");
            return (log, gameContent, modContent) => method.Invoke(null, new object[] { log, gameContent, modContent });
        }
    }
}