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
using Microsoft.Extensions.Logging;

namespace SchemaBuilder
{
    public class SchemaGenerator
    {
        private readonly ILogger<SchemaGenerator> _log;
        private readonly GameManager _games;
        private readonly DocReader _docs;
        private readonly PostprocessUnordered _postprocessUnordered;

        public SchemaGenerator(GameManager games, ILogger<SchemaGenerator> log, PostprocessUnordered postprocessUnordered, DocReader docs)
        {
            _games = games;
            _log = log;
            _postprocessUnordered = postprocessUnordered;
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
            var namespaceUrl = "keen://" + name.Substring(0, name.IndexOf('-')) + "/" + name.Substring(name.IndexOf('-') + 1);
            schema.Namespaces.Add("", namespaceUrl);
            schema.TargetNamespace = namespaceUrl;

            var tempSchema = Path.GetTempFileName();
            try
            {
                WriteSchema(schema, tempSchema);

                // Write the schema with XSD 1.0 support.
                schema = postprocessArgs.Schema = ReadSchema(tempSchema);
                _postprocessUnordered.Postprocess(postprocessArgs, false);
                WriteSchema(schema, Path.Combine("schemas", name + ".xsd"));

                // Write the schema with XSD 1.1 support.
                schema = postprocessArgs.Schema = ReadSchema(tempSchema);
                _postprocessUnordered.Postprocess(postprocessArgs, true);
                var schemaAttrs = schema.UnhandledAttributes;
                Array.Resize(ref schemaAttrs, (schemaAttrs?.Length ?? 0) + 1);
                schemaAttrs[schemaAttrs.Length - 1] = new XmlDocument().CreateAttribute("vc", "minVersion", "http://www.w3.org/2007/XMLSchema-versioning");
                schemaAttrs[schemaAttrs.Length - 1].Value = "1.1";
                schema.UnhandledAttributes = schemaAttrs;
                WriteSchema(schema, Path.Combine("schemas", name + ".11.xsd"));
            }
            finally
            {
                File.Delete(tempSchema);
            }
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
                case XmlSchemaSimpleType simple:
                    Postprocess(args, simple);
                    break;
                case XmlSchemaComplexType complex:
                    Postprocess(args, complex);
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

        private void PostprocessTopLevelElement(PostprocessArgs args, XmlSchemaElement element)
        {
            if (!args.Info.TryGetTypeByXmlName(element.Name, out var xmlType)) return;
            if (xmlType.Type.FullName != null && args.Patches.TypeAliases.TryGetValue(xmlType.Type.FullName, out var alias))
            {
                element.SchemaTypeName = new XmlQualifiedName(alias.XmlName);
                element.SchemaType = null;
            }
        }

        private void Postprocess(PostprocessArgs args, XmlSchemaSimpleType type)
        {
            if (type.Name.Contains("AnimationWrapMode"))
                Debugger.Break();
            args.TypeData(type.Name, out var typeInfo, out var typePatch);

            var typeDoc = "";
            if (typeInfo != null)
                typeDoc = _docs.GetTypeComments(typeInfo.Type)?.Summary;
            if (!string.IsNullOrEmpty(typePatch?.Documentation))
                typeDoc = typePatch.Documentation;

            ProcessContent(type.Content);

            MaybeAttachDocumentation(type, typeDoc);
            return;

            void ProcessContent(XmlSchemaSimpleTypeContent content)
            {
                switch (content)
                {
                    case XmlSchemaSimpleTypeList list:
                        ProcessList(list);
                        break;
                    case XmlSchemaSimpleTypeRestriction restriction:
                        ProcessRestriction(restriction);
                        break;
                    case XmlSchemaSimpleTypeUnion union:
                        ProcessUnion(union);
                        break;
                }
            }

            void ProcessList(XmlSchemaSimpleTypeList list) => ProcessContent(list.ItemType.Content);

            void ProcessRestriction(XmlSchemaSimpleTypeRestriction restriction)
            {
                ProcessCollection<XmlSchemaFacet>(restriction.Facets, ProcessFacet);
            }

            void ProcessUnion(XmlSchemaSimpleTypeUnion union)
            {
                foreach (var member in union.BaseMemberTypes)
                    ProcessContent(member.Content);
            }

            XmlSchemaFacet ProcessFacet(XmlSchemaFacet facet)
            {
                switch (facet)
                {
                    case XmlSchemaEnumerationFacet enumeration:
                    {
                        var memberPatch = typePatch?.MemberPatch(enumeration);
                        if (memberPatch?.Delete == InheritableTrueFalse.True)
                            return null;

                        var doc = "";
                        var member = (MemberInfo)typeInfo?.Type.GetField(enumeration.Value) ?? typeInfo?.Type.GetProperty(enumeration.Value);
                        if (member != null)
                            doc = _docs.GetMemberComment(member);

                        if (!string.IsNullOrEmpty(memberPatch?.Documentation)) doc = memberPatch.Documentation;
                        MaybeAttachDocumentation(enumeration, doc);

                        return enumeration;
                    }
                    default:
                        return facet;
                }
            }
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
                    case XmlSchemaAttribute attr:
                    {
                        var memberPatch = typePatch?.MemberPatch(attr);
                        if (memberPatch?.Delete == InheritableTrueFalse.True)
                            return null;

                        var doc = "";
                        if (typeInfo != null && typeInfo.TryGetAttribute(attr.Name, out var attrMember))
                            doc = _docs.GetMemberComment(attrMember.Member);
                        switch ((memberPatch?.Optional).OrInherit(args.Patches.AllOptional))
                        {
                            case InheritableTrueFalse.True:
                                attr.Use = XmlSchemaUse.Optional;
                                break;
                            case InheritableTrueFalse.False:
                                attr.Use = XmlSchemaUse.Required;
                                break;
                            case InheritableTrueFalse.Inherit:
                            default:
                                break;
                        }

                        if (!string.IsNullOrEmpty(memberPatch?.Documentation)) doc = memberPatch.Documentation;
                        MaybeAttachDocumentation(attr, doc);
                        return item;
                    }
                    case XmlSchemaElement element:
                    {
                        var memberPatch = typePatch?.MemberPatch(element);
                        if (memberPatch?.Delete == InheritableTrueFalse.True)
                            return null;

                        var doc = "";
                        if (element.IsNillable)
                            element.MinOccurs = 0;
                        if (typeInfo != null && typeInfo.TryGetElement(element.Name, out var eleMember))
                        {
                            doc = _docs.GetMemberComment(eleMember.Member);

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

                        if (string.IsNullOrEmpty(element.SchemaTypeName?.Name))
                            Debugger.Break();

                        switch ((memberPatch?.Optional).OrInherit(args.Patches.AllOptional))
                        {
                            case InheritableTrueFalse.True:
                                element.MinOccurs = 0;
                                break;
                            case InheritableTrueFalse.False:
                                element.MinOccurs = Math.Max(element.MinOccurs, 1);
                                break;
                            case InheritableTrueFalse.Inherit:
                            default:
                                break;
                        }

                        if (!string.IsNullOrEmpty(memberPatch?.Documentation))
                            doc = memberPatch.Documentation;

                        MaybeAttachDocumentation(element, doc);
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