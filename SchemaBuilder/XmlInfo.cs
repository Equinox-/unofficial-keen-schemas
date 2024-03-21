using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;

namespace SchemaBuilder
{
    public sealed class XmlInfo
    {
        private readonly List<XmlTypeInfo> _generatedTypes = new List<XmlTypeInfo>();
        private readonly Dictionary<Type, XmlTypeInfo> _typeLookup = new Dictionary<Type, XmlTypeInfo>();
        private readonly Dictionary<string, XmlTypeInfo> _resolvedTypes = new Dictionary<string, XmlTypeInfo>();
        private XmlAttributeOverrides _overrides = new XmlAttributeOverrides();
        private bool _needsRepair;

        public IEnumerable<XmlTypeInfo> Generated => _generatedTypes;


        public void Generate(Type type)
        {
            _generatedTypes.Add(Lookup(type));
        }

        public XmlTypeInfo Lookup(Type type)
        {
            var baseType = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
            if (type.IsGenericType)
            {
                foreach (var param in type.GetGenericArguments())
                    if (!param.IsGenericParameter)
                        Lookup(param);
            }

            if (_typeLookup.TryGetValue(baseType, out var info))
                return info;
            _needsRepair = true;
            return new XmlTypeInfo(this, baseType);
        }

        internal void BindTypeInfo(Type type, XmlTypeInfo info) => _typeLookup.Add(type, info);

        public bool TryGetType(string xmlName, out XmlTypeInfo info)
        {
            Overrides();
            return _resolvedTypes.TryGetValue(xmlName, out info);
        }

        public XmlAttributeOverrides Overrides()
        {
            if (!_needsRepair)
                return _overrides;

            var nameConflicts = new Dictionary<string, List<XmlTypeInfo>>();
            foreach (var type in _typeLookup.Values)
            {
                var attrs = type.Attributes;

                var baseTypeName = attrs.XmlType?.TypeName;
                if (string.IsNullOrEmpty(baseTypeName))
                    baseTypeName = type.Type.Name;

                if (!nameConflicts.TryGetValue(baseTypeName, out var conflicts))
                    nameConflicts.Add(baseTypeName, conflicts = new List<XmlTypeInfo>());
                conflicts.Add(type);
            }

            foreach (var conflict in nameConflicts)
            {
                if (conflict.Value.Count <= 1) continue;
                foreach (var type in conflict.Value)
                {
                    var name = conflict.Key;
                    var declaring = type.Type.DeclaringType;
                    while (declaring != null)
                    {
                        name = declaring.Name + "_" + name;
                        declaring = declaring.DeclaringType;
                    }

                    var attrs = type.Attributes;
                    attrs.XmlType ??= new XmlTypeAttribute();
                    attrs.XmlType.TypeName = name;
                }
            }

            _resolvedTypes.Clear();
            _overrides = new XmlAttributeOverrides();
            foreach (var type in _typeLookup.Values)
            {
                _overrides.Add(type.Type, type.Attributes);
                _resolvedTypes.Add(type.XmlTypeName, type);
            }

            _needsRepair = false;
            return _overrides;
        }

        internal static string FirstNonEmpty(string prefer, string fallback) => string.IsNullOrEmpty(prefer) ? fallback : prefer;
    }


    public sealed class XmlTypeInfo
    {
        private static readonly HashSet<string> IgnoredTypes = new HashSet<string>
        {
            "VRage.Game.MyDefinitionId",
            "VRage.ObjectBuilder.MyObjectBuilderType",
            "VRage.ObjectBuilder.MyStringHash",
            typeof(string).FullName,
            typeof(Decimal).FullName,
            typeof(Guid).FullName,
        };

        public readonly Type Type;
        public readonly List<XmlMemberInfo> Members = new List<XmlMemberInfo>();
        public readonly XmlAttributes Attributes;

        public bool TryGetAttribute(string attribute, out XmlMemberInfo member)
        {
            foreach (var candidate in Members)
                if (candidate.AttributeName == attribute)
                {
                    member = candidate;
                    return true;
                }

            member = default;
            return false;
        }

        public bool TryGetElement(string element, out XmlMemberInfo member)
        {
            foreach (var candidate in Members)
                if (candidate.ElementNames.Contains(element))
                {
                    member = candidate;
                    return true;
                }

            member = default;
            return false;
        }

        public string XmlTypeName => XmlInfo.FirstNonEmpty(Attributes.XmlType?.TypeName, Type.Name);

        public XmlTypeInfo(XmlInfo resolution, Type type)
        {
            Type = type;
            Attributes = new XmlAttributes(type);
            resolution.BindTypeInfo(type, this);
            if (!type.IsPrimitive
                && !typeof(IXmlSerializable).IsAssignableFrom(type)
                && !IgnoredTypes.Contains(type.FullName))
            {
                Debug.Assert(type.FullName != null && !type.FullName.StartsWith("System."));
                foreach (var member in type.GetMembers(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (member.GetCustomAttribute<XmlIgnoreAttribute>() == null
                        && member.GetCustomAttribute<CompilerGeneratedAttribute>() == null
                        && (member is PropertyInfo || member is FieldInfo))
                        Members.Add(new XmlMemberInfo(resolution, member));
                }
            }
        }
    }

    public sealed class XmlMemberInfo
    {
        public readonly MemberInfo Member;
        public readonly bool IsPolymorphicElement;
        public readonly bool IsPolymorphicArrayItem;
        private readonly List<XmlTypeInfo> _referencedTypes = new List<XmlTypeInfo>();
        private readonly HashSet<string> _elementNames = new HashSet<string>();
        public IReadOnlyCollection<string> ElementNames => _elementNames;
        public IReadOnlyList<XmlTypeInfo> ReferencedTypes => _referencedTypes;
        public string AttributeName { get; private set; }

        private static bool IsPolymorphicSerializer(Type type)
        {
            while (type != null)
            {
                if (type.Name.StartsWith("MyAbstractXmlSerializer"))
                    return true;
                type = type.BaseType;
            }

            return false;
        }

        public XmlMemberInfo(XmlInfo resolution, MemberInfo member)
        {
            Member = member;
            var attributes = new XmlAttributes(member);
            IsPolymorphicElement = attributes.XmlElements.Count == 1 && IsPolymorphicSerializer(attributes.XmlElements[0].Type);
            IsPolymorphicArrayItem = attributes.XmlArrayItems.Count == 1 && IsPolymorphicSerializer(attributes.XmlArrayItems[0].Type);
            if (attributes.XmlAttribute != null)
                AttributeName = XmlInfo.FirstNonEmpty(attributes.XmlAttribute.AttributeName, member.Name);
            else
            {
                var elements = attributes.XmlElements;
                for (var i = 0; i < elements.Count; i++)
                    _elementNames.Add(XmlInfo.FirstNonEmpty(elements[i].ElementName, member.Name));
                if (_elementNames.Count == 0)
                    _elementNames.Add(member.Name);
            }

            var memberType = member switch
            {
                PropertyInfo prop => prop.PropertyType,
                FieldInfo field => field.FieldType,
                _ => null
            };
            CollectType(memberType);


            void CollectType(Type type)
            {
                if (type.IsGenericParameter)
                    return;

                if (type.HasElementType)
                {
                    CollectType(type.GetElementType());
                    return;
                }

                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    CollectType(type.GetGenericArguments()[0]);
                    return;
                }

                if (typeof(IEnumerable).IsAssignableFrom(type))
                {
                    var enumeratedType = type.GetMethods()
                        .Where(x => typeof(IEnumerator).IsAssignableFrom(x.ReturnType) && x.Name.EndsWith("GetEnumerator"))
                        .Select(x => x.ReturnType.GetProperty(nameof(IEnumerator.Current))?.PropertyType)
                        .FirstOrDefault(x => x != null);
                    if (enumeratedType != null)
                    {
                        CollectType(enumeratedType);
                        return;
                    }
                }

                _referencedTypes.Add(resolution.Lookup(type));
            }
        }
    }
}