using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Serialization;
using DocXml.Reflection;
using Microsoft.Extensions.Logging;

namespace SchemaBuilder
{
    public sealed class XmlInfo
    {
        private readonly ILogger _log;
        private readonly HashSet<XmlTypeInfo> _generatedTypes = new HashSet<XmlTypeInfo>();
        private readonly Dictionary<Type, XmlTypeInfo> _typeLookup = new Dictionary<Type, XmlTypeInfo>();
        private readonly Dictionary<string, XmlTypeInfo> _resolvedTypes = new Dictionary<string, XmlTypeInfo>();
        private XmlAttributeOverrides _overrides = new XmlAttributeOverrides();
        private bool _needsRepair;

        public IEnumerable<XmlTypeInfo> Generated => _generatedTypes;
        public IEnumerable<XmlTypeInfo> AllTypes => _typeLookup.Values;


        public XmlTypeInfo Generate(Type type)
        {
            var info = Lookup(type);
            _generatedTypes.Add(info);
            return info;
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

        public bool TryGetTypeByXmlName(string xmlName, out XmlTypeInfo info)
        {
            Overrides();
            return _resolvedTypes.TryGetValue(xmlName, out info);
        }

        public IEnumerable<XmlTypeInfo> GetTypesByName(string typeName) => _typeLookup.Values.Where(x => x.Type.Name == typeName);

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
                _log.LogInformation($"Resolving conflict of name {conflict.Key} between {string.Join(", ", conflict.Value.Select(x => x.Type.FullName))}");
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

            // Fix up array layout types that had their contained element renamed.
            foreach (var type in _typeLookup.Values)
            foreach (var member in type.Members.Values)
                if (member.IsArrayLike && (member.Attributes.XmlElements.Count == 0 || member.Attributes.XmlArrayItems.Count > 0))
                {
                    if (member.Attributes.XmlArrayItems.Count == 0)
                        member.Attributes.XmlArrayItems.Add(new XmlArrayItemAttribute());
                    foreach (var item in member.Attributes.XmlArrayItems.OfType<XmlArrayItemAttribute>())
                    {
                        // Already explicitly named.
                        if (!string.IsNullOrEmpty(item.ElementName))
                            continue;
                        var itemType = item.Type != null ? Lookup(item.Type) : member.ReferencedTypes.Count == 1 ? member.ReferencedTypes[0] : null;
                        if (itemType != null && itemType.OriginalXmlName != itemType.XmlTypeName)
                        {
                            _log.LogInformation(
                                $"Fixing element name of implicit array element {itemType.Type.Name} in {type.Type}#{member.Member.Name} as {itemType.OriginalXmlName}");
                            item.ElementName = itemType.OriginalXmlName;
                        }
                    }
                }

            _resolvedTypes.Clear();
            _overrides = new XmlAttributeOverrides();
            foreach (var type in _typeLookup.Values)
            {
                _overrides.Add(type.Type, type.Attributes);
                _resolvedTypes.Add(type.XmlTypeName, type);
                foreach (var member in type.Members.Values)
                    _overrides.Add(type.Type, member.Member.Name, member.Attributes);
            }

            _needsRepair = false;
            return _overrides;
        }

        internal static string FirstNonEmpty(string prefer, string fallback) => string.IsNullOrEmpty(prefer) ? fallback : prefer;

        internal static readonly HashSet<string> IgnoredTypes = new HashSet<string>
        {
            "VRage.Game.MyDefinitionId",
            "VRage.ObjectBuilder.MyObjectBuilderType",
            "VRage.ObjectBuilder.MyStringHash",
            typeof(string).FullName,
            typeof(Decimal).FullName,
            typeof(Guid).FullName,
        };

        public XmlInfo(ILogger log)
        {
            _log = log;
        }
    }


    public sealed class XmlTypeInfo
    {
        public readonly Type Type;
        public readonly Dictionary<string, XmlMemberInfo> Members = new Dictionary<string, XmlMemberInfo>();
        public readonly XmlAttributes Attributes;
        public readonly string OriginalXmlName;

        public bool TryGetAttribute(string attribute, out XmlMemberInfo member)
        {
            foreach (var candidate in Members.Values)
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
            foreach (var candidate in Members.Values)
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
            OriginalXmlName = XmlTypeName;
            resolution.BindTypeInfo(type, this);
            if (!type.IsPrimitive
                && !typeof(Enum).IsAssignableFrom(type)
                && !typeof(IXmlSerializable).IsAssignableFrom(type)
                && !XmlInfo.IgnoredTypes.Contains(type.FullName))
            {
                if (type.FullName == null || type.FullName.StartsWith("System."))
                    Debug.Fail("Should not be resolving system types");
                foreach (var member in type.GetMembers(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (member.GetCustomAttribute<XmlIgnoreAttribute>() == null
                        && member.GetCustomAttribute<CompilerGeneratedAttribute>() == null
                        && (member is PropertyInfo || member is FieldInfo))
                    {
                        var name = member.Name;
                        if (!Members.TryGetValue(name, out var existing) || existing.Member.DeclaringType!.IsAssignableFrom(member.DeclaringType))
                            Members[name] = new XmlMemberInfo(resolution, member);
                    }
                }
            }
        }
    }

    public sealed class XmlMemberInfo
    {
        public readonly MemberInfo Member;
        public readonly XmlAttributes Attributes;

        public readonly bool IsPolymorphicElement;
        public readonly bool IsPolymorphicArrayItem;
        private readonly List<XmlTypeInfo> _referencedTypes = new List<XmlTypeInfo>();
        private readonly List<XmlTypeInfo> _includedTypes = new List<XmlTypeInfo>();
        private readonly HashSet<string> _elementNames = new HashSet<string>();
        public IReadOnlyCollection<string> ElementNames => _elementNames;
        public IReadOnlyList<XmlTypeInfo> ReferencedTypes => _referencedTypes;
        public IReadOnlyList<XmlTypeInfo> IncludedTypes => _includedTypes;
        public string AttributeName { get; private set; }

        public bool IsArrayLike { get; private set; }

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
            Attributes = new XmlAttributes(member);
            IsPolymorphicElement = Attributes.XmlElements.Count == 1 && IsPolymorphicSerializer(Attributes.XmlElements[0].Type);
            IsPolymorphicArrayItem = Attributes.XmlArrayItems.Count == 1 && IsPolymorphicSerializer(Attributes.XmlArrayItems[0].Type);
            if (Attributes.XmlAttribute != null)
                AttributeName = XmlInfo.FirstNonEmpty(Attributes.XmlAttribute.AttributeName, member.Name);
            else
            {
                var elements = Attributes.XmlElements;
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
            if (Attributes.XmlDefaultValue != null && memberType != null)
                Attributes.XmlDefaultValue = FixDefaultValue(Attributes.XmlDefaultValue, memberType);
            if (memberType != null
                && (SerializationProxies.ProxiesByType.TryGetValue(memberType, out var serializationProxy)
                    || SerializationProxies.ProxiesByTypeName.TryGetValue(memberType.ToNameString(), out serializationProxy)))
            {
                if (Attributes.XmlElements.Count == 0)
                    Attributes.XmlElements.Add(new XmlElementAttribute());
                Attributes.XmlElements[0].Type = serializationProxy;
                Attributes.XmlArrayItems.Clear();
                return;
            }

            CollectType(_referencedTypes, memberType);
            if (!IsPolymorphicElement && !IsPolymorphicArrayItem)
            {
                foreach (var element in Attributes.XmlElements.OfType<XmlElementAttribute>())
                    if (element.Type != null)
                        CollectType(_includedTypes, element.Type);
                foreach (var element in Attributes.XmlArrayItems.OfType<XmlArrayItemAttribute>())
                    if (element.Type != null)
                        CollectType(_includedTypes, element.Type);
            }

            void CollectType(List<XmlTypeInfo> target, Type type)
            {
                if (type.IsGenericParameter)
                    return;

                if (type.HasElementType)
                {
                    CollectType(target, type.GetElementType());
                    return;
                }

                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    CollectType(target, type.GetGenericArguments()[0]);
                    return;
                }

                if (!type.IsPrimitive && !XmlInfo.IgnoredTypes.Contains(type.FullName) && typeof(IEnumerable).IsAssignableFrom(type))
                {
                    var enumeratedType = type.GetMethods()
                        .Where(x => typeof(IEnumerator).IsAssignableFrom(x.ReturnType) && x.Name.EndsWith("GetEnumerator"))
                        .Select(x => x.ReturnType.GetProperty(nameof(IEnumerator.Current))?.PropertyType)
                        .FirstOrDefault(x => x != null);
                    if (enumeratedType != null)
                    {
                        IsArrayLike = true;
                        CollectType(target, enumeratedType);
                        return;
                    }
                }

                target.Add(resolution.Lookup(type));
            }
        }

        private static object FixDefaultValue(object value, Type memberType)
        {
            if (value.GetType() == memberType)
                return value;
            if (value is string && memberType.IsEnum)
                return value;
            var str = value.ToString();
            if (memberType == typeof(byte))
                return byte.TryParse(str, out var val) ? val : double.TryParse(str, out var dbl) ? (byte)dbl : value;
            if (memberType == typeof(sbyte))
                return sbyte.TryParse(str, out var val) ? val : double.TryParse(str, out var dbl) ? (sbyte)dbl : value;
            if (memberType == typeof(short))
                return short.TryParse(str, out var val) ? val : double.TryParse(str, out var dbl) ? (short)dbl : value;
            if (memberType == typeof(ushort))
                return ushort.TryParse(str, out var val) ? val : double.TryParse(str, out var dbl) ? (ushort)dbl : value;
            if (memberType == typeof(int))
                return int.TryParse(str, out var val) ? val : double.TryParse(str, out var dbl) ? (int)dbl : value;
            if (memberType == typeof(uint))
                return uint.TryParse(str, out var val) ? val : double.TryParse(str, out var dbl) ? (uint)dbl : value;
            if (memberType == typeof(long))
                return long.TryParse(str, out var val) ? val : double.TryParse(str, out var dbl) ? (long)dbl : value;
            if (memberType == typeof(ulong))
                return ulong.TryParse(str, out var val) ? val : double.TryParse(str, out var dbl) ? (ulong)dbl : value;
            if (memberType == typeof(float))
                return float.TryParse(str, out var val) ? val : double.TryParse(str, out var dbl) ? (float)dbl : value;
            if (memberType == typeof(double))
                return double.TryParse(str, out var val) ? val : value;

            return value;
        }
    }
}