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
            Postprocess(info, schema);
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

        private void Postprocess(XmlInfo info, XmlSchema schema)
        {
            foreach (var type in schema.SchemaTypes.Values)
                Postprocess(info, (XmlSchemaObject)type);
        }

        private void Postprocess(XmlInfo info, XmlSchemaObject type)
        {
            switch (type)
            {
                case XmlSchemaSimpleType simple:
                    Postprocess(info, simple);
                    break;
                case XmlSchemaComplexType complex:
                    Postprocess(info, complex);
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

        private void Postprocess(XmlInfo info, XmlSchemaSimpleType type)
        {
            info.TryGetType(type.Name, out var typeInfo);
            if (typeInfo != null)
                MaybeAttachDocumentation(type, _docs.GetTypeComments(typeInfo.Type)?.Summary);
        }

        private const string PolymorphicArrayPrefix = "ArrayOfMyAbstractXmlSerializerOf";

        private void Postprocess(XmlInfo info, XmlSchemaComplexType type)
        {
            info.TryGetType(type.Name, out var typeInfo);
            if (typeInfo != null)
                MaybeAttachDocumentation(type, _docs.GetTypeComments(typeInfo.Type)?.Summary);

            foreach (var attribute in type.Attributes)
                if (attribute is XmlSchemaAttribute attr && typeInfo != null && typeInfo.TryGetAttribute(attr.Name, out var attrMember))
                    MaybeAttachDocumentation(attr, _docs.GetMemberComment(attrMember.Member));


            XmlSchemaParticle particle;
            if (type.ContentModel?.Content is XmlSchemaComplexContentExtension complexExt)
                particle = complexExt.Particle;
            else
                particle = type.Particle;

            foreach (var item in particle switch
                     {
                         XmlSchemaElement element => new[] { element },
                         XmlSchemaSequence sequence => sequence.Items.OfType<XmlSchemaObject>(),
                         _ => Enumerable.Empty<XmlSchemaObject>()
                     })
            {
                switch (item)
                {
                    case XmlSchemaElement element:
                        if (element.IsNillable)
                            element.MinOccurs = 0;
                        if (typeInfo != null && typeInfo.TryGetElement(element.Name, out var eleMember))
                        {
                            MaybeAttachDocumentation(element, _docs.GetMemberComment(eleMember.Member));
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

                        break;
                }
            }
        }
    }
}