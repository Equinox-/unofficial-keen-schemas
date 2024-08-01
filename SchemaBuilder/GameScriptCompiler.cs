using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;

namespace SchemaBuilder
{
    public class CompilationArgs
    {
        public string AssemblyName;
        public IReadOnlyList<string> ScriptFiles;
        public IReadOnlyList<Assembly> References;
    }

    public abstract class GameScriptCompiler
    {
        protected abstract CompilationWithAnalyzers CreateCompilation(CompilationArgs args, List<string> messages);
        protected abstract bool CheckAfterEmit(EmitResult emitResult, CompilationWithAnalyzers compilation, List<string> messages);

        public bool CompileInto(CompilationArgs args, string dllFile, string docFile, List<string> messages)
        {
            var compilation = CreateCompilation(args, messages);
            using var dllStream = File.Open(dllFile, FileMode.Create, FileAccess.Write);
            using var docStream = File.Open(docFile, FileMode.Create, FileAccess.Write);
            var emit = compilation.Compilation.Emit(dllStream, xmlDocumentationStream: docStream);
            return emit.Success & CheckAfterEmit(emit, compilation, messages);
        }
    }

    public class MedievalScriptCompiler : GameScriptCompiler
    {
        private readonly Task<object> _compilerInstance;
        private readonly ConstructorInfo _scriptTypeCtor;
        private readonly MethodInfo _enumerableOfTypeScript;
        private readonly ConstructorInfo _listOfMessageCtor;
        private readonly FieldInfo _diagnosticAnalyzer;
        private readonly MethodInfo _createCompilation;
        private readonly MethodInfo _analyzeDiagnostics;
        private readonly FieldInfo _messageText;

        public class ConfigDeserializationProxy
        {
            public object System;
        }

        private static object ReadConfig(Type configType, string path)
        {
            var proxy = typeof(ConfigDeserializationProxy);
            var overrides = new XmlAttributeOverrides();
            overrides.Add(proxy, new XmlAttributes { XmlRoot = new XmlRootAttribute("VRage.Core") });
            overrides.Add(proxy, nameof(ConfigDeserializationProxy.System), new XmlAttributes
            {
                XmlElements = { new XmlElementAttribute("System", configType) }
            });
            var configSerializer = new XmlSerializer(proxy, overrides);
            using var configStream = File.OpenRead(path);
            return (configSerializer.Deserialize(configStream) as ConfigDeserializationProxy)?.System ?? throw new Exception("Failed to load system config");
        }

        private static IEnumerable<string> CreateCompilationSymbols()
        {
            var gameType = Type.GetType("Medieval.MyMedievalGame, MedievalEngineers.Game") ?? throw new Exception("Failed to find game type");
            var versionField = gameType.GetField("ME_VERSION") ?? throw new Exception("Failed to find version field");
            var version = (Version)versionField.GetValue(null);
            const string versionKey = "VRAGE_VERSION";
            yield return $"{versionKey}_{version.Major}";
            yield return $"{versionKey}_{version.Major}_{version.Minor}";
            yield return $"{versionKey}_{version.Major}_{version.Minor}_{version.Build}";
            yield return $"{versionKey}_{version.Major}_{version.Minor}_{version.Build}_{version.Revision}";
        }

        public MedievalScriptCompiler(GameInstall install)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            var fileSystemType = Type.GetType("VRage.FileSystem.MyFileSystem, VRage.Library") ??
                                 throw new Exception("Failed to find file system type");
            var messageType = Type.GetType("VRage.Scripting.MyScriptCompiler+Message, VRage.Scripting") ??
                              throw new Exception("Failed to find script compiler message type");
            var scriptCompiler = Type.GetType("VRage.Scripting.MyScriptCompiler, VRage.Scripting") ??
                                 throw new Exception("Failed to find script compiler type");
            var configType = Type.GetType("VRage.Scripting.MyScriptCompilerConfig, VRage.Scripting") ??
                             throw new Exception("Failed to find script compiler config type");
            var scriptType = Type.GetType("VRage.Scripting.Script, VRage.Scripting") ?? throw new Exception("Failed to find script type");

            var configInstance = ReadConfig(configType, Path.Combine(install.BinariesDir, "scripting.config"));

            var fileSystemExePath = fileSystemType.GetField("ExePath") ?? throw new Exception("File system exe path not found");
            fileSystemExePath.SetValue(null, install.BinariesDir);

            var addConditionalCompilation = scriptCompiler.GetMethod("AddConditionalCompilationSymbols", new[] { typeof(string[]) })
                                            ?? throw new Exception("Failed to find add conditional compilation symbols");

            _compilerInstance = Task.Run(() =>
            {
                var compiler = Activator.CreateInstance(scriptCompiler, configInstance);
                addConditionalCompilation.Invoke(compiler, new object[] { CreateCompilationSymbols().ToArray() });
                return compiler;
            });

            _scriptTypeCtor = scriptType.GetConstructor(new[]
            {
                typeof(string), // name 
                typeof(string) // code
            }) ?? throw new Exception("Failed to find script constructor");
            _enumerableOfTypeScript = (typeof(Enumerable)
                    .GetMethod(nameof(Enumerable.OfType)) ?? throw new Exception("Failed to find Enumerable.OfType<Script>"))
                .MakeGenericMethod(scriptType);

            _diagnosticAnalyzer = scriptCompiler.GetField("m_whitelistDiagnosticAnalyzer", flags) ?? throw new Exception("Failed to find whitelist analyzer");
            _createCompilation = scriptCompiler.GetMethod("CreateCompilation", flags, null, new[]
            {
                typeof(string), // assembly name
                typeof(IEnumerable<>).MakeGenericType(scriptType), // scripts
                typeof(IEnumerable<Assembly>), // references 
                typeof(bool), // include debug information
            }, Array.Empty<ParameterModifier>()) ?? throw new Exception("Failed to find create compilation");
            _analyzeDiagnostics = scriptCompiler.GetMethod("AnalyzeDiagnostics", flags, null, new[]
            {
                typeof(ImmutableArray<Diagnostic>),
                typeof(List<>).MakeGenericType(messageType),
                typeof(bool).MakeByRefType()
            }, Array.Empty<ParameterModifier>()) ?? throw new Exception("Failed to find analyze diagnostics");
            _listOfMessageCtor = typeof(List<>).MakeGenericType(messageType)
                                     .GetConstructor(Type.EmptyTypes)
                                 ?? throw new Exception("Failed to find List<Message> ctor");
            _messageText = messageType.GetField("Text") ?? throw new Exception("Failed to find Message.Text");
        }

        protected override CompilationWithAnalyzers CreateCompilation(CompilationArgs args, List<string> messages)
        {
            var compiler = _compilerInstance.Result;
            var rawCompilation = (CSharpCompilation)_createCompilation.Invoke(compiler, new[]
            {
                args.AssemblyName,
                _enumerableOfTypeScript.Invoke(null, new object[]
                {
                    args.ScriptFiles.Select(path => _scriptTypeCtor.Invoke(new object[] { path, File.ReadAllText(path) }))
                }),
                args.References,
                false /* no debug info*/
            });
            return rawCompilation.WithAnalyzers(ImmutableArray.Create(
                (DiagnosticAnalyzer)_diagnosticAnalyzer.GetValue(compiler)
            ));
        }

        protected override bool CheckAfterEmit(EmitResult emitResult, CompilationWithAnalyzers compilation, List<string> messagesOut)
        {
            var compiler = _compilerInstance.Result;
            var messages = _listOfMessageCtor.Invoke(Array.Empty<object>());
            var args = new[] { emitResult.Diagnostics, messages, emitResult.Success };
            _analyzeDiagnostics.Invoke(compiler, args);
            args[0] = compilation.GetAllDiagnosticsAsync().Result;
            _analyzeDiagnostics.Invoke(compiler, args);
            foreach (var msg in (IEnumerable) messages)
                messagesOut.Add(_messageText.GetValue(msg).ToString());
            return (bool)args[2];
        }
    }
}