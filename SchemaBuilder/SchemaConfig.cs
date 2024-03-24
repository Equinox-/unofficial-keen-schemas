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
    public class SchemaConfig : IInheritable<SchemaConfig>
    {
        private readonly Dictionary<string, TypePatch> _types = new Dictionary<string, TypePatch>();
        private readonly Dictionary<string, TypeAlias> _typeAliases = new Dictionary<string, TypeAlias>();

        [XmlElement("Include")]
        public List<string> Include = new List<string>();

        [XmlElement]
        public Game? GameOptional;

        [XmlElement]
        public Game Game
        {
            get => GameOptional ?? throw new Exception("Schema does not specify a game");
            set => GameOptional = value;
        }

        [XmlElement]
        public string SteamBranch;

        [XmlElement("Mod")]
        public readonly HashSet<ulong> Mods = new HashSet<ulong>();

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
        public InheritableTrueFalseAggressive AllUnordered;

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

        public static readonly XmlSerializer Serializer = new XmlSerializer(typeof(SchemaConfig));

        public void InheritFrom(SchemaConfig other)
        {
            GameOptional ??= other.GameOptional;
            if (GameOptional != null && other.GameOptional != null && Game != other.Game)
                throw new Exception($"Attempting to inherit from schema for {other.Game} but this schema is for {Game}");
            SteamBranch ??= other.SteamBranch;
            AllOptional = AllOptional.OrInherit(other.AllOptional);
            AllUnordered = AllUnordered.OrInherit(other.AllUnordered);
            foreach (var suppressed in other.SuppressedTypes)
                SuppressedTypes.Add(suppressed);
            foreach (var mod in other.Mods)
                Mods.Add(mod);

            _types.OrInherit(other._types);

            foreach (var alias in other._typeAliases)
                if (!_typeAliases.ContainsKey(alias.Key))
                    _typeAliases.Add(alias.Key, alias.Value);
        }

        public static SchemaConfig Read(string dir, string name)
        {
            var context = new Stack<string>();
            var loadedFiles = new HashSet<string>();
            var loaded = new List<SchemaConfig>();

            ReadRecursive(name);

            var final = loaded[0];
            for (var i = 1; i < loaded.Count; i++)
                final.InheritFrom(loaded[i]);

            if (final.GameOptional == null)
                throw new Exception($"Schema tree {name} does not specify a game ({string.Join(", ", loadedFiles)})");
            if (final.SteamBranch == null)
                throw new Exception($"Schema tree {name} does not specify a steam branch ({string.Join(", ", loadedFiles)})");
            return final;

            void ReadRecursive(string file)
            {
                if (file.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    file = file.Substring(0, file.Length - 4);
                if (!loadedFiles.Add(file))
                    return;

                context.Push(Path.GetFileNameWithoutExtension(file));
                try
                {
                    SchemaConfig cfg;
                    try
                    {
                        using var stream = File.OpenRead(Path.Combine(dir, file + ".xml"));
                        cfg = (SchemaConfig)Serializer.Deserialize(stream);
                    }
                    catch (Exception err)
                    {
                        throw new Exception($"Failed to load config file {file} via {string.Join(", ", context)}", err);
                    }

                    loaded.Add(cfg);
                    foreach (var included in cfg.Include)
                        ReadRecursive(included);
                }
                finally
                {
                    context.Pop();
                }
            }
        }
    }

    public enum Game
    {
        [XmlEnum("medieval-engineers")]
        MedievalEngineers,

        [XmlEnum("space-engineers")]
        SpaceEngineers,
    }

    public interface IInheritable<in T>
    {
        /// <summary>
        /// Updates this instance with default values from the other instance.
        /// This instance takes priority.
        /// </summary>
        void InheritFrom(T other);
    }


    public static class PatchExt
    {
        public static T OrInherit<T>(this T self, T super) where T : struct => ((T?)self).OrInherit(super);

        public static T OrInherit<T>(this T? self, T super) where T : struct => self switch
        {
            null => super,
            InheritableTrueFalse.Inherit => super,
            InheritableTrueFalseAggressive.Inherit => super,
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

    public enum InheritableTrueFalseAggressive
    {
        [XmlEnum("inherit")]
        Inherit,

        [XmlEnum("true")]
        True,

        [XmlEnum("aggressive")]
        Aggressive,

        [XmlEnum("false")]
        False,
    }

    public class TypePatch : IInheritable<TypePatch>
    {
        private readonly Dictionary<string, MemberPatch> _members = new Dictionary<string, MemberPatch>();

        [XmlAttribute]
        public string Name;

        [XmlAttribute]
        public InheritableTrueFalseAggressive Unordered;

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

        public void InheritFrom(TypePatch other)
        {
            Unordered = Unordered.OrInherit(other.Unordered);
            Documentation ??= other.Documentation;
            _members.OrInherit(other._members);
        }
    }

    public class MemberPatch : IInheritable<MemberPatch>
    {
        [XmlAttribute]
        public string Name;

        [XmlAttribute]
        public InheritableTrueFalse Delete;

        [XmlAttribute]
        public InheritableTrueFalse Optional;

        [XmlElement]
        public string Documentation;

        public void InheritFrom(MemberPatch other)
        {
            Delete = Delete.OrInherit(other.Delete);
            Optional = Optional.OrInherit(other.Optional);
            Documentation ??= other.Documentation;
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