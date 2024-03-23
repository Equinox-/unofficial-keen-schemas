using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace SchemaBuilder
{
    [XmlRoot("Patches")]
    public class PatchFile
    {
        private readonly Dictionary<string, TypePatch> _types = new Dictionary<string, TypePatch>();
        private readonly Dictionary<string, TypeAlias> _typeAliases = new Dictionary<string, TypeAlias>();

        public IReadOnlyDictionary<string, TypeAlias> TypeAliases => _typeAliases;

        /// <summary>
        /// Should all possible elements be converted to optional.
        /// </summary>
        [XmlElement]
        public InheritableTrueFalse AllOptional;

        /// <summary>
        /// Should all possible elements be converted to unordered.
        /// </summary>
        [XmlElement]
        public bool AllUnordered;

        /// <summary>
        /// Types to suppress discovering of subtypes from and suppress discovery of.
        /// If the types are directly referenced by other types, they will still be included.
        /// </summary>
        [XmlElement("SuppressedType")]
        public HashSet<string> SuppressedTypes = new HashSet<string>();

        [XmlElement("Type")]
        public TypePatch[] ForXmlTypes
        {
            get => _types.Values.ToArray();
            set
            {
                _types.Clear();
                if (value == null) return;
                foreach (var type in value)
                    _types.Add(type.Name, type);
            }
        }

        [XmlElement("TypeAlias")]
        public TypeAlias[] ForXmlTypeAliases
        {
            get => _typeAliases.Values.ToArray();
            set
            {
                _typeAliases.Clear();
                if (value == null) return;
                foreach (var alias in value)
                    _typeAliases.Add(alias.CSharpType, alias);
            }
        }

        public TypePatch TypePatch(string name) => _types.TryGetValue(name, out var patch) ? patch : null;

        public static readonly XmlSerializer Serializer = new XmlSerializer(typeof(PatchFile));

        public static PatchFile Read(string path)
        {
            using var stream = File.OpenRead(path);
            var cfg = (PatchFile)Serializer.Deserialize(stream);
            return cfg;
        }
    }

    public static class PatchExt
    {
        public static bool OrInherit(this InheritableTrueFalse self, bool super) => ((InheritableTrueFalse?)self).OrInherit(super);

        public static bool OrInherit(this InheritableTrueFalse? self, bool super) =>
            self.OrInherit(super ? InheritableTrueFalse.True : InheritableTrueFalse.False) == InheritableTrueFalse.True;

        public static InheritableTrueFalse OrInherit(this InheritableTrueFalse self, InheritableTrueFalse super) => ((InheritableTrueFalse?)self).OrInherit(super);

        public static InheritableTrueFalse OrInherit(this InheritableTrueFalse? self, InheritableTrueFalse super) => self switch
        {
            null => super,
            InheritableTrueFalse.Inherit => super,
            InheritableTrueFalse.True => InheritableTrueFalse.True,
            InheritableTrueFalse.False => InheritableTrueFalse.False,
            _ => throw new ArgumentOutOfRangeException(nameof(self), self, null)
        };
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

    public class TypePatch
    {
        private readonly Dictionary<string, MemberPatch> _members = new Dictionary<string, MemberPatch>();

        [XmlAttribute]
        public string Name;

        [XmlAttribute]
        public InheritableTrueFalse Unordered;

        [XmlElement]
        public string Documentation;

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

        public MemberPatch MemberPatch(XmlSchemaAttribute attribute) => MemberPatch("attribute:", attribute.Name);

        public MemberPatch MemberPatch(XmlSchemaEnumerationFacet facet) => MemberPatch("enum:", facet.Value);

        public MemberPatch MemberPatch(XmlSchemaElement element) => MemberPatch("element:", element.Name);

        private MemberPatch MemberPatch(string prefix, string name)
        {
            if (_members.TryGetValue(prefix + name, out var patch))
                return patch;
            return _members.TryGetValue(name, out patch) ? patch : null;
        }
    }

    public class MemberPatch
    {
        [XmlAttribute]
        public string Name;

        [XmlAttribute]
        public bool Delete;

        [XmlAttribute]
        public InheritableTrueFalse Optional;

        [XmlElement]
        public string Documentation;
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
}