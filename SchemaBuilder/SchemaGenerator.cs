using System;
using System.Collections.Generic;
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

            var info = new XmlInfo();
            info.Generate(Type.GetType(gameInfo.RootType));

            var inheritingFrom = gameInfo.PolymorphicBaseTypes.Select(Type.GetType).ToList();

            foreach (var bin in Directory.GetFiles(gameBinaries, "*.dll", SearchOption.TopDirectoryOnly))
                try
                {
                    var asm = Assembly.Load(Path.GetFileNameWithoutExtension(bin));
                    foreach (var type in asm.GetTypes())
                        if (type.GetCustomAttributesData().Any(x => gameInfo.PolymorphicSubtypeAttribute.Contains(x.AttributeType.FullName))
                            && inheritingFrom.Any(x => x.IsAssignableFrom(type)))
                            info.Generate(type);
                }
                catch
                {
                    // ignore assembly load errors.
                }

            var schema = GenerateInternal(info)
                .OrderByDescending(x => x.Elements.Count)
                .First();
            Postprocess(new PostprocessArgs { Info = info, Patches = patches }, schema);
            schema.TargetNamespace = "https://storage.googleapis.com/unofficial-keen-schemas/latest/" + cfg.Name + ".xsd";

            Directory.CreateDirectory("schemas");
            using var stream = File.OpenWrite(Path.Combine("schemas", cfg.Name + ".xsd"));
            using var text = new StreamWriter(stream, Encoding.UTF8);
            schema.Write(text);
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
            args.Info.TryGetType(type.Name, out var typeInfo);
            var typePatch = args.Patches.TypePatch(type.Name);

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
            args.Info.TryGetType(type.Name, out var typeInfo);
            var typePatch = args.Patches.TypePatch(type.Name);

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
                        if (memberPatch?.MakeOptional ?? false)
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
                            element.SchemaType = null;
                            element.SchemaTypeName = new XmlQualifiedName(type.Name.Substring(PolymorphicArrayPrefix.Length));
                        }


                        var memberPatch = typePatch?.MemberPatch(element.Name);
                        if (memberPatch?.MakeOptional ?? false) element.MinOccurs = 0;
                        if (!string.IsNullOrEmpty(memberPatch?.Documentation)) doc = memberPatch.Documentation;

                        MaybeAttachDocumentation(element, doc);
                        break;
                    }
                }
            }
        }
    }
}