using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using Microsoft.Extensions.Logging;

namespace SchemaBuilder
{
    public class SchemaGenerator
    {
        private readonly ILogger<SchemaGenerator> _log;
        private readonly GameManager _games;
        private readonly DocReader _docs;

        public SchemaGenerator(GameManager games, ILogger<SchemaGenerator> log, DocReader docs)
        {
            _games = games;
            _log = log;
            _docs = docs;
        }

        public async Task Generate(string name)
        {
            _log.LogInformation($"Generating schema {name}");

            var config = SchemaConfig.Read("patches", name);

            var gameInfo = GameInfo.Games[config.Game];
            var gameInstall = await _games.RestoreGame(config.Game, config.SteamBranch);
            var modInstall = await gameInstall.LoadMods(config.Mods.ToArray());

            // Generate schema
            var info = new XmlInfo(_log, config);
            DiscoverTypes(config, info, gameInfo, gameInstall.LoadAssemblies()
                .Concat(modInstall.SelectMany(mod => mod.LoadAssemblies())));
            var schemas = GenerateInternal(info);

            // Compile schema
            schemas.Compile((sender, args) => _log.LogInformation($"Schema validation {args}"), false);
            if (schemas.Count == 0)
                throw new Exception("No schemas generated");
            if (schemas.Count > 1)
                _log.LogWarning("Generated multiple schemas, possibly causing issues");
            var schema = schemas.OrderByDescending(x => x.Elements.Count).First();

            // Run postprocessor
            var postprocessArgs = new PostprocessArgs { Info = info, Patches = config, Schema = schema };
            Postprocess(postprocessArgs);

            var ir = SchemaIrCompiler.Compile(postprocessArgs.Schema);
            SchemaIrConfig.ApplyConfig(ir, info, config);
            SchemaIrDocumentation.InjectXmlDocumentation(ir, info, _docs);
            // Write the IR file.
            WriteIr(ir, Path.Combine("schemas", name + ".json"));
            // Write the schema with XSD 1.0 support.
            WriteSchema(SchemaIrToXsd.Generate(ir, false), Path.Combine("schemas", name + ".xsd"));
            // Write the schema with XSD 1.1 support.
            WriteSchema(SchemaIrToXsd.Generate(ir, true), Path.Combine("schemas", name + ".11.xsd"));
        }

        private XmlSchema ReadSchema(string path)
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read);
            var set = new XmlSchemaSet();
            set.Add(XmlSchema.Read(stream, (_, args) => { _log.LogWarning($"Validation failure when loading schema: {args.Severity} {args.Message}"); }));
            set.Compile();
            return set.Schemas().OfType<XmlSchema>().First();
        }

        private void WriteSchema(XmlSchema schema, string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (dir != null)
                Directory.CreateDirectory(dir);
            using var stream = File.Open(path, FileMode.Create, FileAccess.Write);
            using var text = new StreamWriter(stream, Encoding.UTF8);
            schema.Write(text);
        }

        private void WriteIr(SchemaIr schema, string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (dir != null)
                Directory.CreateDirectory(dir);
            using var stream = File.Open(path, FileMode.Create, FileAccess.Write);
            JsonSerializer.Serialize(stream, schema, new JsonSerializerOptions
            {
                WriteIndented = true,
                IncludeFields = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            });
        }

        private void DiscoverTypes(
            SchemaConfig patches,
            XmlInfo info,
            GameInfo gameInfo,
            IEnumerable<Assembly> assemblies)
        {
            var polymorphicSubtypes = assemblies.SelectMany(asm => asm.GetTypes()
                    .Where(type => type.GetCustomAttributesData()
                        .Any(x => gameInfo.PolymorphicSubtypeAttribute.Contains(x.AttributeType.FullName))))
                .ToList();
            var exploredBasesFrom = new HashSet<Type>();
            var exploredBases = new HashSet<Type>();
            var polymorphicQueue = new Queue<(Type, string)>();

            foreach (var polymorphic in gameInfo.PolymorphicBaseTypes.Select(Type.GetType))
                ConsiderPolymorphicBase(polymorphic, "game config");
            ConsiderPolymorphicBasesFrom(info.Lookup(Type.GetType(gameInfo.RootType)));

            while (polymorphicQueue.Count > 0)
            {
                var explore = polymorphicQueue.Dequeue();
                ExplorePolymorphicBase(explore.Item1, explore.Item2);
                foreach (var type in info.AllTypes)
                    ConsiderPolymorphicBasesFrom(type);
            }

            return;

            void ConsiderPolymorphicBase(Type baseType, string via)
            {
                if (patches.SuppressedTypes.Contains(baseType.FullName))
                    return;
                if (exploredBases.Add(baseType))
                    polymorphicQueue.Enqueue((baseType, via));
            }

            void ConsiderPolymorphicBasesFrom(XmlTypeInfo type)
            {
                if (patches.SuppressedTypes.Contains(type.Type.FullName) || !exploredBasesFrom.Add(type.Type))
                    return;

                foreach (var member in type.Members.Values)
                {
                    if (member.ReferencedTypes.Count == 1
                        && (member.IsPolymorphicElement || member.IsPolymorphicArrayItem))
                        ConsiderPolymorphicBase(member.ReferencedTypes[0].Type, $"{type.Type.Name}#{member.Member.Name}");
                }
            }

            void ExplorePolymorphicBase(Type baseType, string via)
            {
                info.Lookup(baseType);
                var count = 0;
                foreach (var type in polymorphicSubtypes)
                    if (baseType.IsAssignableFrom(type) && !patches.SuppressedTypes.Contains(type.FullName))
                    {
                        info.Lookup(type);
                        count++;
                    }

                _log.LogInformation($"Exploring polymorphic base type {baseType.Name} via {via} found {count} types");
            }
        }

        private XmlSchemas GenerateInternal(XmlInfo info)
        {
            var overrides = info.Overrides();

            var importer = new XmlReflectionImporter(overrides);
            var schemas = new XmlSchemas();
            var exporter = new XmlSchemaExporter(schemas);

            foreach (var type in info.AllTypes)
                try
                {
                    if (type.Type.IsGenericType)
                        continue;
                    var mapping = importer.ImportTypeMapping(type.Type);
                    exporter.ExportTypeMapping(mapping);
                }
                catch (Exception err)
                {
                    _log.LogWarning(err, $"Failed to import schema for type {type.Type}");
                }

            return schemas;
        }

        private void Postprocess(PostprocessArgs args)
        {
            AddTypes(args);
            foreach (var type in args.Schema.SchemaTypes.Values)
                Postprocess(args, (XmlSchemaObject)type);
            foreach (var element in args.Schema.Elements.Values)
                Postprocess(args, (XmlSchemaObject)element);
        }


        private void Postprocess(PostprocessArgs args, XmlSchemaObject type)
        {
            switch (type)
            {
                case XmlSchemaComplexType complex:
                    PostprocessComplex(args, complex);
                    break;
                case XmlSchemaElement element:
                    PostprocessTopLevelElement(args, element);
                    break;
            }
        }

        private void AddTypes(PostprocessArgs args)
        {
            foreach (var alias in args.Patches.TypeAliases.Values)
            {
                var type = CreateAlias(alias);
                args.Schema.SchemaTypes.Add(new XmlQualifiedName(type.Name), type);
                args.Schema.Items.Add(type);
            }

            return;

            XmlSchemaSimpleType CreateAlias(TypeAlias alias)
            {
                var restriction = new XmlSchemaSimpleTypeRestriction
                {
                    BaseTypeName = alias.XmlPrimitiveType,
                };
                if (!string.IsNullOrEmpty(alias.Pattern))
                    restriction.Facets.Add(new XmlSchemaPatternFacet { Value = alias.Pattern });
                return new XmlSchemaSimpleType
                {
                    Name = alias.XmlName,
                    Content = restriction
                };
            }
        }

        private void PostprocessTopLevelElement(PostprocessArgs args, XmlSchemaElement element)
        {
            if (!args.Info.TryGetTypeByXmlName(element.Name, out var xmlType)) return;
            if (xmlType.Type.FullName != null && args.Patches.TypeAliases.TryGetValue(xmlType.Type.FullName, out var alias))
            {
                element.SchemaTypeName = new XmlQualifiedName(alias.XmlName);
                element.SchemaType = null;
            }
        }

        private const string PolymorphicArrayPrefix = "ArrayOfMyAbstractXmlSerializerOf";


        private void PostprocessComplex(PostprocessArgs args, XmlSchemaComplexType type)
        {
            args.TypeData(type.Name, out var typeInfo);

            ProcessChildren(type.Attributes);
            type.Particle = ProcessParticle(type.Particle);

            if (type.ContentModel?.Content is XmlSchemaComplexContentExtension complexExt)
            {
                ProcessChildren(complexExt.Attributes);
                complexExt.Particle = ProcessParticle(complexExt.Particle);
            }

            return;


            XmlSchemaParticle ProcessParticle(XmlSchemaParticle particle)
            {
                switch (particle)
                {
                    case XmlSchemaElement element:
                        return ProcessChild(element);
                    case XmlSchemaGroupBase group:
                        ProcessChildren(group.Items);
                        return group;
                    default:
                        return particle;
                }
            }

            void ProcessChildren(XmlSchemaObjectCollection children) => ProcessCollection<XmlSchemaObject>(children, ProcessChild);

            T ProcessChild<T>(T item) where T : XmlSchemaObject
            {
                switch (item)
                {
                    case XmlSchemaElement element:
                    {
                        if (element.IsNillable)
                            element.MinOccurs = 0;
                        if (typeInfo != null && typeInfo.TryGetElement(element.Name, out var eleMember))
                        {
                            if (eleMember.ReferencedTypes.Count == 1)
                            {
                                var singleReturnType = eleMember.ReferencedTypes[0];
                                if (singleReturnType.Type.FullName != null &&
                                    args.Patches.TypeAliases.TryGetValue(singleReturnType.Type.FullName, out var alias))
                                {
                                    element.SchemaTypeName = new XmlQualifiedName(alias.XmlName);
                                    element.SchemaType = null;
                                }
                                else if (eleMember.ReferencedTypes.Count == 1 && eleMember.IsPolymorphicElement)
                                {
                                    element.SchemaType = null;
                                    element.SchemaTypeName = new XmlQualifiedName(singleReturnType.XmlTypeName);
                                }
                            }
                        }

                        // Hack for polymorphic arrays since we can't match the generated polymorphic array back to the original member to do proper
                        // polymorphism detection.
                        if (type.Name.StartsWith(PolymorphicArrayPrefix) && element.MaxOccursString == "unbounded")
                        {
                            var rawTypeName = type.Name.Substring(PolymorphicArrayPrefix.Length);
                            var matchingTypes = args.Info.GetTypesByName(rawTypeName).ToList();
                            if (matchingTypes.Count == 1)
                            {
                                element.SchemaType = null;
                                element.SchemaTypeName = new XmlQualifiedName(matchingTypes[0].XmlTypeName);
                            }
                            else
                            {
                                _log.LogWarning($"Failed to resolve polymorphic element type {rawTypeName}");
                            }
                        }
                        return item;
                    }
                    default:
                        return item;
                }
            }
        }

        private static void ProcessCollection<T>(XmlSchemaObjectCollection items, Func<T, T> func) where T : XmlSchemaObject
        {
            for (var i = items.Count - 1; i >= 0; i--)
            {
                var original = items[i] as T;
                if (original == null)
                    continue;
                var replacement = func(original);
                if (replacement == null)
                    items.RemoveAt(i);
                else if (replacement != original)
                    items[i] = replacement;
            }
        }
    }
}