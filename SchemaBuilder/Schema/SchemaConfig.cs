﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;

namespace SchemaBuilder.Schema
{
    [XmlRoot("SchemaConfig")]
    public class SchemaConfig : ConfigBase<SchemaConfig>
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
        public override void InheritFrom(SchemaConfig other)
        {
            base.InheritFrom(other);
            AllOptional = AllOptional.OrInherit(other.AllOptional);
            foreach (var suppressed in other.SuppressedTypes)
                SuppressedTypes.Add(suppressed);

            _types.OrInherit(other._types);

            foreach (var alias in other._typeAliases)
                if (!_typeAliases.ContainsKey(alias.Key))
                    _typeAliases.Add(alias.Key, alias.Value);
        }

        public TypePatch TypePatch(string name) => _types.TryGetValue(name, out var patch) ? patch : null;
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

        public MemberPatch AttributePatch(string name) => MemberPatch("attribute:", name);

        public MemberPatch EnumPatch(string name) => MemberPatch("enum:", name);

        public MemberPatch ElementPatch(string name) => MemberPatch("element:", name);

        private MemberPatch MemberPatch(string prefix, string name)
        {
            if (_members.TryGetValue(prefix + name, out var patch))
                return patch;
            return _members.TryGetValue(name, out patch) ? patch : null;
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
}