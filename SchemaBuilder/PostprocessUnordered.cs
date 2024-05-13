using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Schema;
using Microsoft.Extensions.Logging;

namespace SchemaBuilder
{
    public sealed class PostprocessUnordered
    {
        private readonly ILogger<PostprocessUnordered> _log;

        public PostprocessUnordered(ILogger<PostprocessUnordered> log)
        {
            _log = log;
        }

        public void Postprocess(PostprocessArgs args, bool allowXsd11)
        {
            // Compute type trees.
            var typesWithSubtypes = new HashSet<XmlQualifiedName>();
            var baseTypes = new UnionFind();
            foreach (var type in args.Schema.SchemaTypes.Values.OfType<XmlSchemaComplexType>())
                baseTypes.Insert(type.QualifiedName);
            foreach (var type in args.Schema.SchemaTypes.Values.OfType<XmlSchemaComplexType>())
            {
                var baseType = (type.ContentModel?.Content as XmlSchemaComplexContentExtension)?.BaseTypeName;
                if (baseType == null) continue;
                typesWithSubtypes.Add(baseType);
                baseTypes.Union(type.QualifiedName, baseType);
            }

            // Determine type tree size
            var treeSize = new Dictionary<XmlQualifiedName, int>();
            foreach (var type in args.Schema.SchemaTypes.Values.OfType<XmlSchemaComplexType>())
            {
                var treeKey = baseTypes.Find(type.QualifiedName);
                treeSize[treeKey] = (treeSize.TryGetValue(treeKey, out var size) ? size : 0) + 1;
            }

            // Determine if type trees can be made unordered.
            var treeUnordered = new Dictionary<XmlQualifiedName, bool>();
            foreach (var type in args.Schema.SchemaTypes.Values.OfType<XmlSchemaComplexType>())
            {
                var treeKey = baseTypes.Find(type.QualifiedName);
                if (treeUnordered.TryGetValue(treeKey, out var okay) && !okay)
                    continue;
                var shouldMake = ShouldMakeUnordered(args, type, treeSize[treeKey] == 1, allowXsd11);
                treeUnordered[treeKey] = shouldMake;
            }

            // Determine if type trees have been severed.
            var typeSevered = new HashSet<XmlQualifiedName>();
            var treeSevered = new HashSet<XmlQualifiedName>();
            foreach (var type in args.Schema.SchemaTypes.Values.OfType<XmlSchemaComplexType>())
            {
                var treeKey = baseTypes.Find(type.QualifiedName);
                if (treeUnordered[treeKey])
                    continue;
                args.TypeData(type.Name, out _, out var typePatch);
                var unorderedRequest = (typePatch?.Unordered).OrInherit(args.Patches.AllUnordered);
                if (unorderedRequest == InheritableTrueFalseAggressive.Aggressive && ShouldMakeUnordered(args, type, true, allowXsd11))
                {
                    treeSevered.Add(treeKey);
                    typeSevered.Add(type.QualifiedName);
                }
            }

            // Actually make the types unordered if possible.
            foreach (var type in args.Schema.SchemaTypes.Values.OfType<XmlSchemaComplexType>())
                if (treeUnordered[baseTypes.Find(type.QualifiedName)] || typeSevered.Contains(type.QualifiedName))
                    MakeUnorderedType(args, type, allowXsd11);

            // If there are referenced to severed type trees they need to be erased.
            var severedBaseTypes = new HashSet<XmlQualifiedName>();
            foreach (var withSubtypes in typesWithSubtypes)
                if (treeSevered.Contains(baseTypes.Find(withSubtypes)))
                    severedBaseTypes.Add(withSubtypes);
            if (severedBaseTypes.Count > 0)
                foreach (var type in args.Schema.SchemaTypes.Values.OfType<XmlSchemaComplexType>())
                    EraseSeveredPolymorphicTypes(type, severedBaseTypes);
        }

        private sealed class UnionFind
        {
            private readonly Dictionary<XmlQualifiedName, XmlQualifiedName> _dictionary = new Dictionary<XmlQualifiedName, XmlQualifiedName>();

            public void Insert(XmlQualifiedName name) => _dictionary.Add(name, name);

            public void Union(XmlQualifiedName left, XmlQualifiedName right)
            {
                left = Find(left);
                right = Find(right);
                // Dumb union operation that ignores rank/size, but this code can be slow so whatever.
                _dictionary[left] = right;
            }

            public XmlQualifiedName Find(XmlQualifiedName name)
            {
                var root = name;
                while (true)
                {
                    var parent = _dictionary[root];
                    if (parent.Equals(root))
                        return root;
                    var grandparent = _dictionary[parent];
                    // Path halving.
                    _dictionary[root] = grandparent;
                    root = grandparent;
                }
            }
        }

        private bool ShouldMakeUnordered(PostprocessArgs args, XmlSchemaComplexType type, bool isIsolatedType, bool allowXsd11)
        {
            args.TypeData(type.Name, out _, out var typePatch);
            var unorderedRequest = (typePatch?.Unordered).OrInherit(args.Patches.AllUnordered);
            var wantsUnordered = unorderedRequest switch
            {
                InheritableTrueFalseAggressive.Inherit => false,
                InheritableTrueFalseAggressive.True => isIsolatedType,
                InheritableTrueFalseAggressive.Aggressive => isIsolatedType,
                InheritableTrueFalseAggressive.False => false,
                _ => throw new ArgumentOutOfRangeException()
            };
            var allowAggression = isIsolatedType && unorderedRequest == InheritableTrueFalseAggressive.Aggressive;
            if (!ShouldMakeUnorderedParticle(type.Particle))
                return false;

            return type.ContentModel?.Content switch
            {
                XmlSchemaComplexContentExtension ext => ShouldMakeUnorderedParticle(ext.Particle),
                XmlSchemaComplexContentRestriction _ => false,
                XmlSchemaSimpleContentExtension _ => false,
                XmlSchemaSimpleContentRestriction _ => false,
                null => true,
                _ => throw new ArgumentOutOfRangeException()
            };

            bool ShouldMakeUnorderedParticle(XmlSchemaParticle particle)
            {
                return particle switch
                {
                    XmlSchemaAny _ => false,
                    XmlSchemaAll _ => true,
                    XmlSchemaChoice choice => wantsUnordered && choice.Items.Count == 1 && IsCompatibleWithAll(choice.Items[0], false),
                    XmlSchemaElement element => wantsUnordered && IsCompatibleWithAll(element, false),
                    XmlSchemaSequence sequence => wantsUnordered && sequence.Items.OfType<XmlSchemaObject>()
                        .All(x => IsCompatibleWithAll(x, allowAggression && sequence.Items.Count > 1)),
                    XmlSchemaGroupBase _ => false,
                    XmlSchemaGroupRef _ => false,
                    null => true,
                    _ => throw new ArgumentOutOfRangeException(nameof(particle))
                };
            }

            bool IsCompatibleWithAll(XmlSchemaObject x, bool aggressive)
            {
                return x is XmlSchemaElement e && (allowXsd11 || ((e.MinOccurs == 0 || e.MinOccurs == 1) && (aggressive || e.MaxOccurs == 1)));
            }
        }

        private IEnumerable<XmlSchemaAnnotated> ElementsAndAttributes(PostprocessArgs args, XmlSchemaComplexType type)
        {
            var attributes = new HashSet<XmlQualifiedName>();
            var elements = new HashSet<XmlQualifiedName>();
            while (true)
            {
                foreach (var child in Particle(type.Particle))
                    if (elements.Add(child.QualifiedName))
                        yield return child;
                foreach (var attr in type.AttributeUses.Values.OfType<XmlSchemaAttribute>())
                    if (attributes.Add(attr.QualifiedName))
                        yield return attr;
                if (type.ContentModel?.Content is XmlSchemaComplexContentExtension complexExt)
                {
                    foreach (var child in Particle(complexExt.Particle))
                        if (elements.Add(child.QualifiedName))
                            yield return child;
                    if (args.Schema.SchemaTypes[complexExt.BaseTypeName] is XmlSchemaComplexType baseType)
                    {
                        type = baseType;
                        continue;
                    }
                }

                yield break;

                IEnumerable<XmlSchemaElement> Particle(XmlSchemaParticle particle) =>
                    particle switch
                    {
                        XmlSchemaAll all => all.Items.OfType<XmlSchemaElement>(),
                        XmlSchemaChoice choice => choice.Items.OfType<XmlSchemaElement>(),
                        XmlSchemaElement element => new[] { element },
                        XmlSchemaSequence sequence => sequence.Items.OfType<XmlSchemaElement>(),
                        null => Array.Empty<XmlSchemaElement>(),
                        _ => throw new ArgumentOutOfRangeException(nameof(particle), particle.GetType().FullName)
                    };
            }
        }

        private void MakeUnorderedType(PostprocessArgs args, XmlSchemaComplexType type, bool allowXsd11)
        {
            if (allowXsd11)
            {
                // XSD 1.1 allows inheritance of unordered particles.
                type.Particle = MakeUnorderedParticle(type.Particle);

                if (type.ContentModel?.Content is XmlSchemaComplexContentExtension complexExt)
                    complexExt.Particle = MakeUnorderedParticle(complexExt.Particle);
                return;
            }

            type.Attributes.Clear();
            var elements = new XmlSchemaAll();
            foreach (var obj in ElementsAndAttributes(args, type).OrderBy(a => a switch
                     {
                         XmlSchemaAttribute attr => attr.QualifiedName.ToString(),
                         XmlSchemaElement el => el.QualifiedName.ToString(),
                         _ => throw new ArgumentOutOfRangeException(nameof(a))
                     }))
                switch (obj)
                {
                    case XmlSchemaElement el:
                        elements.Items.Add(MakeUnorderedElement(el));
                        break;
                    case XmlSchemaAttribute attr:
                        type.Attributes.Add(attr);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(obj));
                }

            type.Particle = elements;
            type.ContentModel = null;

            return;


            XmlSchemaAll MakeUnorderedParticle(XmlSchemaParticle particle) => particle switch
            {
                XmlSchemaAll all => all,
                XmlSchemaChoice choice => new XmlSchemaAll { Items = { MakeUnorderedElement((XmlSchemaElement)choice.Items[0]) } },
                XmlSchemaElement element => new XmlSchemaAll { Items = { MakeUnorderedElement(element) } },
                XmlSchemaSequence sequence => MakeUnorderedSequence(sequence),
                null => null,
                _ => throw new ArgumentOutOfRangeException(nameof(particle))
            };

            XmlSchemaAll MakeUnorderedSequence(XmlSchemaSequence sequence)
            {
                var all = new XmlSchemaAll();
                foreach (var item in sequence.Items)
                    all.Items.Add(MakeUnorderedElement((XmlSchemaElement)item));
                return all;
            }

            XmlSchemaElement MakeUnorderedElement(XmlSchemaElement element)
            {
                if (!allowXsd11 && element.MaxOccurs > 1)
                    element.MaxOccurs = 1;
                return element;
            }
        }

        private void EraseSeveredPolymorphicTypes(XmlSchemaComplexType type, HashSet<XmlQualifiedName> erase)
        {
            ProcessParticle(type.Particle);
            if (type.ContentModel?.Content is XmlSchemaComplexContentExtension complexExt)
                ProcessParticle(complexExt.Particle);
            return;

            void ProcessParticle(XmlSchemaParticle particle)
            {
                switch (particle)
                {
                    case null:
                        break;
                    case XmlSchemaAny _:
                        break;
                    case XmlSchemaElement element:
                        ProcessElement(element);
                        break;
                    case XmlSchemaGroupBase group:
                        foreach (var el in group.Items.OfType<XmlSchemaParticle>())
                            ProcessParticle(el);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(particle), particle.GetType().FullName);
                }
            }

            void ProcessElement(XmlSchemaElement element)
            {
                if (!erase.Contains(element.SchemaTypeName)) return;
                element.SchemaTypeName = new XmlQualifiedName("anyType", "http://www.w3.org/2001/XMLSchema");
            }
        }
    }
}