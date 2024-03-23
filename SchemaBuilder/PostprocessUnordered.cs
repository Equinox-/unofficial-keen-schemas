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
            var baseTypes = new UnionFind();
            foreach (var type in args.Schema.SchemaTypes.Values.OfType<XmlSchemaComplexType>())
                baseTypes.Insert(type.QualifiedName);
            foreach (var type in args.Schema.SchemaTypes.Values.OfType<XmlSchemaComplexType>())
            {
                var baseType = (type.ContentModel?.Content as XmlSchemaComplexContentExtension)?.BaseTypeName;
                if (baseType != null)
                    baseTypes.Union(type.QualifiedName, baseType);
            }

            // Determine if type trees can be made unordered.
            var treeUnordered = new Dictionary<XmlQualifiedName, bool>();
            foreach (var type in args.Schema.SchemaTypes.Values.OfType<XmlSchemaComplexType>())
            {
                var treeKey = baseTypes.Find(type.QualifiedName);
                if (treeUnordered.TryGetValue(treeKey, out var okay) && !okay)
                    continue;
                treeUnordered[treeKey] = ShouldMakeUnordered(args, type, allowXsd11);
            }

            // Actually make the types unordered if possible.
            foreach (var type in args.Schema.SchemaTypes.Values.OfType<XmlSchemaComplexType>())
                if (treeUnordered[baseTypes.Find(type.QualifiedName)])
                    MakeUnorderedType(type);
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

        private bool ShouldMakeUnordered(PostprocessArgs args, XmlSchemaComplexType type, bool allowXsd11)
        {
            args.TypeData(type.Name, out _, out var typePatch);
            var wantsUnordered = (typePatch?.Unordered).OrInherit(args.Patches.AllUnordered);
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
                    XmlSchemaChoice choice => wantsUnordered && choice.Items.Count == 1 && IsCompatibleWithAll(choice.Items[0]),
                    XmlSchemaElement element => wantsUnordered && IsCompatibleWithAll(element),
                    XmlSchemaSequence sequence => wantsUnordered && sequence.Items.OfType<XmlSchemaObject>().All(IsCompatibleWithAll),
                    XmlSchemaGroupBase _ => false,
                    XmlSchemaGroupRef _ => false,
                    null => true,
                    _ => throw new ArgumentOutOfRangeException(nameof(particle))
                };
            }

            bool IsCompatibleWithAll(XmlSchemaObject x)
            {
                return x is XmlSchemaElement e && (allowXsd11 || ((e.MinOccurs == 0 || e.MinOccurs == 1) && e.MaxOccurs == 1));
            }
        }


        private void MakeUnorderedType(XmlSchemaComplexType type)
        {
            type.Particle = MakeUnorderedParticle(type.Particle);

            if (type.ContentModel?.Content is XmlSchemaComplexContentExtension complexExt)
                complexExt.Particle = MakeUnorderedParticle(complexExt.Particle);
            return;


            XmlSchemaParticle MakeUnorderedParticle(XmlSchemaParticle particle) => particle switch
            {
                XmlSchemaAll all => all,
                XmlSchemaChoice choice => new XmlSchemaAll { Items = { choice.Items[0] } },
                XmlSchemaElement element => new XmlSchemaAll { Items = { element } },
                XmlSchemaSequence sequence => MakeUnorderedSequence(sequence),
                null => null,
                _ => throw new ArgumentOutOfRangeException(nameof(particle))
            };

            XmlSchemaParticle MakeUnorderedSequence(XmlSchemaSequence sequence)
            {
                var all = new XmlSchemaAll();
                foreach (var item in sequence.Items)
                    all.Items.Add(item);
                return all;
            }
        }
    }
}