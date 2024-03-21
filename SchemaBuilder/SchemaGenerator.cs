using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using LoxSmoke.DocXml;
using Microsoft.Extensions.Logging;

namespace SchemaBuilder
{
    public class SchemaGenerator
    {
        private readonly ILogger<SchemaGenerator> _log;
        private readonly GameManager _games;
        private readonly DocXmlReader _docs;

        public SchemaGenerator(GameManager games, ILogger<SchemaGenerator> log)
        {
            _games = games;
            _log = log;
            _docs = new DocXmlReader();
        }

        public async Task Generate(Configuration cfg)
        {
            _log.LogInformation($"Generating schema {cfg.Name}");

            var gameInfo = GameInfo.Games[cfg.Game];
            var gameBinaries = await _games.RestoreGame(cfg.Game, cfg.GameBranch);

            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            {
                var bin = Path.Combine(gameBinaries, args.Name + ".dll");
                if (File.Exists(bin))
                    return Assembly.LoadFrom(bin);
                return null;
            };

            var patches = PatchFile.Read(Path.Combine("patches", cfg.Name + ".xml"));

            var info = new XmlInfo(_log);
            DiscoverTypes(info, gameInfo, Directory.GetFiles(gameBinaries, "*.dll", SearchOption.TopDirectoryOnly));
            info.Generate(Type.GetType(gameInfo.RootType));

            var schemas = GenerateInternal(info).OrderByDescending(x => x.Elements.Count)
                .ToList();
            if (schemas.Count == 0)
                throw new Exception("No schemas generated");
            var schema = schemas[0];
            if (schemas.Count > 1)
                _log.LogWarning("Generated multiple schemas, possibly causing issues");
            Postprocess(new PostprocessArgs { Info = info, Patches = patches }, schema);
            var namespaceUrl = "https://storage.googleapis.com/unofficial-keen-schemas/latest/" + cfg.Name + ".xsd";
            schema.Namespaces.Add("", namespaceUrl);
            schema.TargetNamespace = namespaceUrl;

            Directory.CreateDirectory("schemas");
            using var stream = File.Open(Path.Combine("schemas", cfg.Name + ".xsd"), FileMode.Create, FileAccess.Write);
            using var text = new StreamWriter(stream, Encoding.UTF8);
            schema.Write(text);
        }

        private void DiscoverTypes(
            XmlInfo info,
            GameInfo gameInfo,
            IEnumerable<string> assemblies)
        {
            var polymorphicSubtypes = assemblies.SelectMany(asmName =>
            {
                try
                {
                    var asm = Assembly.Load(Path.GetFileNameWithoutExtension(asmName));
                    return asm.GetTypes()
                        .Where(type => type.GetCustomAttributesData()
                            .Any(x => gameInfo.PolymorphicSubtypeAttribute.Contains(x.AttributeType.FullName)));
                }
                catch
                {
                    // ignore assembly load errors.
                    return Type.EmptyTypes;
                }
            }).ToList();
            var exploredBasesFrom = new HashSet<Type>();
            var exploredBases = new HashSet<Type>();
            var polymorphicQueue = new Queue<(Type, string)>();

            foreach (var polymorphic in gameInfo.PolymorphicBaseTypes.Select(Type.GetType))
                ConsiderPolymorphicBase(polymorphic, "game config");
            ConsiderPolymorphicBasesFrom(info.Generate(Type.GetType(gameInfo.RootType)));

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
                if (exploredBases.Add(baseType))
                    polymorphicQueue.Enqueue((baseType, via));
            }

            void ConsiderPolymorphicBasesFrom(XmlTypeInfo type)
            {
                if (gameInfo.SuppressedTypes.Contains(type.Type.FullName))
                    return;
                if (!exploredBasesFrom.Add(type.Type))
                    return;
                    
                foreach (var member in type.Members.Values)
                {
                    if (member.ReferencedTypes.Count == 1
                        && (member.IsPolymorphicElement || member.IsPolymorphicArrayItem))
                    {
                        ConsiderPolymorphicBase(member.ReferencedTypes[0].Type, $"{type.Type.Name}#{member.Member.Name}");
                    }
                }
            }

            void ExplorePolymorphicBase(Type baseType, string via)
            {
                _log.LogInformation($"Exploring polymorphic base type {baseType.Name} via {via}");
                info.Generate(baseType);
                foreach (var type in polymorphicSubtypes)
                    if (baseType.IsAssignableFrom(type) && !gameInfo.SuppressedTypes.Contains(type.FullName))
                        info.Generate(type);
            }
        }

        private XmlSchemas GenerateInternal(XmlInfo info)
        {
            var overrides = info.Overrides();

            var importer = new XmlReflectionImporter(overrides);
            var schemas = new XmlSchemas();
            var exporter = new XmlSchemaExporter(schemas);

            foreach (var type in info.Generated)
                try
                {
                    var mapping = importer.ImportTypeMapping(type.Type);
                    exporter.ExportTypeMapping(mapping);
                }
                catch (Exception err)
                {
                    _log.LogWarning(err, $"Failed to import schema for type {type.Type}");
                }

            schemas.Compile((sender, args) => _log.LogInformation($"Schema validation {args}"), false);
            return schemas;
        }

        private class PostprocessArgs
        {
            public XmlInfo Info;
            public PatchFile Patches;

            public void TypeData(string typeName, out XmlTypeInfo typeInfo, out TypePatch typePatch)
            {
                Info.TryGetTypeByXmlName(typeName, out typeInfo);
                typePatch = Patches.TypePatch(typeName);
                if (typePatch == null && typeInfo != null)
                    typePatch = Patches.TypePatch(typeInfo.Type.FullName);
            }
        }

        private void Postprocess(PostprocessArgs args, XmlSchema schema)
        {
            foreach (var type in schema.SchemaTypes.Values)
                Postprocess(args, (XmlSchemaObject)type);
        }

        private void Postprocess(PostprocessArgs args, XmlSchemaObject type)
        {
            switch (type)
            {
                case XmlSchemaSimpleType simple:
                    Postprocess(args, simple);
                    break;
                case XmlSchemaComplexType complex:
                    Postprocess(args, complex);
                    break;
            }
        }

        private static void MaybeAttachDocumentation(XmlSchemaAnnotated target, string comment)
        {
            if (string.IsNullOrWhiteSpace(comment))
                return;
            target.Annotation ??= new XmlSchemaAnnotation();
            target.Annotation.Items.Add(new XmlSchemaDocumentation
            {
                Markup = new XmlNode[]
                {
                    new XmlDocument().CreateTextNode(comment)
                }
            });
        }

        private void Postprocess(PostprocessArgs args, XmlSchemaSimpleType type)
        {
            args.TypeData(type.Name, out var typeInfo, out var typePatch);

            var typeDoc = "";
            if (typeInfo != null)
                typeDoc = _docs.GetTypeComments(typeInfo.Type)?.Summary;
            if (!string.IsNullOrEmpty(typePatch?.Documentation))
                typeDoc = typePatch.Documentation;

            MaybeAttachDocumentation(type, typeDoc);
        }

        private const string PolymorphicArrayPrefix = "ArrayOfMyAbstractXmlSerializerOf";

        private void Postprocess(PostprocessArgs args, XmlSchemaComplexType type)
        {
            args.TypeData(type.Name, out var typeInfo, out var typePatch);

            var typeDoc = "";
            if (typeInfo != null)
                typeDoc = _docs.GetTypeComments(typeInfo.Type)?.Summary;
            if (!string.IsNullOrEmpty(typePatch?.Documentation))
                typeDoc = typePatch.Documentation;

            MaybeAttachDocumentation(type, typeDoc);

            foreach (var attribute in type.Attributes)
                ProcessChild(attribute);
            ProcessParticle(type.Particle);

            if (type.ContentModel?.Content is XmlSchemaComplexContentExtension complexExt)
            {
                foreach (var attribute in complexExt.Attributes)
                    ProcessChild(attribute);
                ProcessParticle(complexExt.Particle);
            }


            void ProcessParticle(XmlSchemaParticle particle)
            {
                foreach (var item in particle switch
                         {
                             XmlSchemaElement element => new[] { element },
                             XmlSchemaSequence sequence => sequence.Items.OfType<XmlSchemaObject>(),
                             _ => Enumerable.Empty<XmlSchemaObject>()
                         })
                    ProcessChild(item);
            }

            void ProcessChild(XmlSchemaObject item)
            {
                switch (item)
                {
                    case XmlSchemaAttribute attr:
                    {
                        var doc = "";
                        if (typeInfo != null && typeInfo.TryGetAttribute(attr.Name, out var attrMember))
                            doc = _docs.GetMemberComment(attrMember.Member);

                        var memberPatch = typePatch?.MemberPatch(attr.Name);
                        if (memberPatch?.MakeRequired ?? false)
                            attr.Use = XmlSchemaUse.Required;
                        else if ((memberPatch?.MakeOptional ?? false) || args.Patches.AllOptional)
                            attr.Use = XmlSchemaUse.Optional;
                        if (!string.IsNullOrEmpty(memberPatch?.Documentation)) doc = memberPatch.Documentation;
                        MaybeAttachDocumentation(attr, doc);
                        break;
                    }
                    case XmlSchemaElement element:
                    {
                        var doc = "";
                        if (element.IsNillable)
                            element.MinOccurs = 0;
                        if (typeInfo != null && typeInfo.TryGetElement(element.Name, out var eleMember))
                        {
                            doc = _docs.GetMemberComment(eleMember.Member);
                            if (eleMember.IsPolymorphicElement && eleMember.ReferencedTypes.Count == 1)
                            {
                                element.SchemaType = null;
                                element.SchemaTypeName = new XmlQualifiedName(eleMember.ReferencedTypes[0].XmlTypeName);
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


                        var memberPatch = typePatch?.MemberPatch(element.Name);
                        if (memberPatch?.MakeRequired ?? false)
                            element.MinOccurs = Math.Max(element.MinOccurs, 1);
                        else if ((memberPatch?.MakeOptional ?? false) || args.Patches.AllOptional)
                            element.MinOccurs = 0;
                        if (!string.IsNullOrEmpty(memberPatch?.Documentation)) doc = memberPatch.Documentation;

                        MaybeAttachDocumentation(element, doc);
                        break;
                    }
                }
            }
        }
    }
}