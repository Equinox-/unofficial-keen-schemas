using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;

namespace SchemaBuilder.Schema
{
    [XmlRoot("SchemaConfig")]
    public class SchemaConfig : ConfigBase<SchemaConfig>
    {
        [XmlIgnore]
        public Dictionary<string, TypePatch> Types { get; } = new Dictionary<string, TypePatch>();

        [XmlIgnore]
        public Dictionary<string, TypeAlias> TypeAliases { get; } = new Dictionary<string, TypeAlias>();

        /// <summary>
        /// Should all possible elements be converted to optional.
        /// </summary>
        [XmlElement]
        public InheritableTrueFalse AllOptional;

        /// <summary>
        /// Types to suppress discovering of subtypes from and suppress discovery of.
        /// If the types are directly referenced by other types, they will still be included.
        /// </summary>
        [XmlElement("SuppressedType")]
        public HashSet<string> SuppressedTypes = new HashSet<string>();

        [XmlElement("FromWiki")]
        public List<SchemaConfigFromWiki> FromWiki = new List<SchemaConfigFromWiki>();

        [XmlElement("Type")]
        public TypePatch[] ForXmlTypes
        {
            get => Types.Values.ToArray();
            set
            {
                Types.Clear();
                if (value == null) return;
                foreach (var type in value)
                    Types.Add(type.Name, type);
            }
        }

        [XmlElement("TypeAlias")]
        public TypeAlias[] ForXmlTypeAliases
        {
            get => TypeAliases.Values.ToArray();
            set
            {
                TypeAliases.Clear();
                if (value == null) return;
                foreach (var alias in value)
                    TypeAliases.Add(alias.CSharpType, alias);
            }
        }

        public override void InheritFrom(SchemaConfig other)
        {
            base.InheritFrom(other);
            AllOptional = AllOptional.OrInherit(other.AllOptional);
            foreach (var suppressed in other.SuppressedTypes)
                SuppressedTypes.Add(suppressed);

            Types.OrInherit(other.Types);

            foreach (var alias in other.TypeAliases)
                if (!TypeAliases.ContainsKey(alias.Key))
                    TypeAliases.Add(alias.Key, alias.Value);

            if (other.FromWiki.Count > 0)
                FromWiki.InsertRange(0, other.FromWiki);
        }

        public TypePatch TypePatch(string name, bool create = false)
        {
            if (Types.TryGetValue(name, out var patch)) return patch;
            if (!create) return null;
            patch = new TypePatch { Name = name };
            Types.Add(name, patch);
            return patch;
        }
    }


    public static class PatchExt
    {
        public static T OrInherit<T>(this T self, T super) where T : struct => ((T?)self).OrInherit(super);

        public static T OrInherit<T>(this T? self, T super) where T : struct => self switch
        {
            null => super,
            InheritableTrueFalse.Inherit => super,
            _ => self.Value,
        };

        public static void OrInherit<TK, TV>(this Dictionary<TK, TV> self, Dictionary<TK, TV> other) where TV : IInheritable<TV>
        {
            foreach (var item in other)
                if (self.TryGetValue(item.Key, out var existing))
                    existing.InheritFrom(item.Value);
                else
                    self.Add(item.Key, item.Value);
        }
    }

    public enum InheritableTrueFalse
    {
        [XmlEnum("inherit")]
        Inherit,

        [XmlEnum("true")]
        True,

        [XmlEnum("false")]
        False,
    }

    public abstract class DocumentedPatch
    {
        private string _documentation;

        [XmlElement(nameof(Documentation))]
        public string Documentation
        {
            get => _documentation;
            set => _documentation = CleanDocumentation(value);
        }

        [XmlAttribute(nameof(Documentation))]
        public string DocumentationAttribute
        {
            get => _documentation;
            set => _documentation = CleanDocumentation(value);
        }

        private static string CleanDocumentation(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;
            var lines = value.Split('\n')
                .SkipWhile(string.IsNullOrWhiteSpace)
                .ToList();
            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[lines.Count - 1]))
                lines.RemoveAt(lines.Count - 1);
            if (lines.Count == 0)
                return null;

            var whitespace = int.MaxValue;
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                var i = 0;
                while (line[i] == ' ' || line[i] == '\t')
                    i++;
                if (i < whitespace)
                    whitespace = i;
            }

            return string.Join("\n", lines.Select(x => string.IsNullOrWhiteSpace(x) ? "" : x.Substring(whitespace)));
        }
    }

    public class TypePatch : DocumentedPatch, IInheritable<TypePatch>
    {
        private readonly Dictionary<string, MemberPatch> _members = new Dictionary<string, MemberPatch>();

        [XmlAttribute]
        public string Name;

        [XmlElement("Member")]
        public MemberPatch[] ForXmlMembers
        {
            get => _members.Values.ToArray();
            set
            {
                _members.Clear();
                foreach (var member in value)
                    _members.Add(member.Name, member);
            }
        }

        public MemberPatch AttributePatch(string name, bool create = false) => MemberPatch("attribute:", name, create);

        public MemberPatch EnumPatch(string name, bool create = false) => MemberPatch("enum:", name, create);

        public MemberPatch ElementPatch(string name, bool create = false)
        {
            return MemberPatch("element:", name, create);
        }

        private MemberPatch MemberPatch(string prefix, string name, bool create)
        {
            if (_members.TryGetValue(prefix + name, out var patch))
                return patch;
            if (_members.TryGetValue(name, out patch))
                return patch;
            if (!create) return null;
            patch = new MemberPatch { Name = prefix + name };
            _members.Add(patch.Name, patch);
            return patch;
        }

        public void InheritFrom(TypePatch other)
        {
            Documentation ??= other.Documentation;
            _members.OrInherit(other._members);
        }
    }

    public class MemberPatch : DocumentedPatch, IInheritable<MemberPatch>
    {
        [XmlAttribute]
        public string Name;

        [XmlAttribute]
        public InheritableTrueFalse Delete;

        [XmlAttribute]
        public InheritableTrueFalse Optional;

        public const string HiddenSampleValue = "__omit__";

        [XmlAttribute]
        public bool HideSample
        {
            get => Sample == HiddenSampleValue;
            set => Sample = HiddenSampleValue;
        }

        [XmlElement(nameof(Sample))]
        public string Sample;

        [XmlAttribute(nameof(Sample))]
        public string SampleAttribute
        {
            get => Sample;
            set => Sample = value;
        }

        public void InheritFrom(MemberPatch other)
        {
            Delete = Delete.OrInherit(other.Delete);
            Optional = Optional.OrInherit(other.Optional);
            HideSample = HideSample.OrInherit(other.HideSample);
            Documentation ??= other.Documentation;
            Sample ??= other.Sample;
        }
    }

    public class TypeAlias
    {
        [XmlAttribute]
        public string CSharpType;

        private string _xmlName;

        [XmlAttribute]
        public string XmlName
        {
            get => _xmlName ??= CSharpType?.Substring(Math.Max(CSharpType.LastIndexOf('+'), CSharpType.LastIndexOf('.')) + 1);
            set => _xmlName = value;
        }

        [XmlAttribute]
        public TypeAliasPrimitiveType XmlPrimitive;

        [XmlIgnore]
        public Type CSharpPrimitiveType => XmlPrimitive switch
        {
            TypeAliasPrimitiveType.String => typeof(string),
            TypeAliasPrimitiveType.Int => typeof(int),
            TypeAliasPrimitiveType.Float => typeof(float),
            _ => throw new ArgumentOutOfRangeException()
        };

        [XmlIgnore]
        public XmlQualifiedName XmlPrimitiveType => new XmlQualifiedName(XmlPrimitive switch
        {
            TypeAliasPrimitiveType.String => "string",
            TypeAliasPrimitiveType.Int => "int",
            TypeAliasPrimitiveType.Float => "float",
            _ => throw new ArgumentOutOfRangeException()
        }, "http://www.w3.org/2001/XMLSchema");

        [XmlAttribute]
        public string Pattern;

        public enum TypeAliasPrimitiveType
        {
            [XmlEnum("string")]
            String,

            [XmlEnum("int")]
            Int,

            [XmlEnum("float")]
            Float,
        }
    }

    public class SchemaConfigFromWiki
    {
        [XmlElement]
        public string Api;

        [XmlElement("Page")]
        public List<FromWikiPage> Pages = new List<FromWikiPage>();

        [XmlElement("CssInline")]
        public List<CssInline> CssInlines = new List<CssInline>();

        public class CssInline
        {
            [XmlAttribute]
            public string XPath;

            [XmlAttribute]
            public string Class
            {
                get => default;
                set => XPath = $".//*[contains(@class, '{value}')]";
            }

            [XmlAttribute]
            public string Style;
        }

        public class FromWikiPage
        {
            [XmlAttribute]
            public string Source;

            [XmlAttribute]
            public string Type;

            [XmlAttribute]
            public string RegexFromTemplate;
        }
    }
}