using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;

namespace SchemaBuilder.Schema
{
    public static class SchemaIrDocumentation
    {
        public static void InjectXmlDocumentation(SchemaIr ir, XmlInfo info, DocReader docs)
        {
            foreach (var type in ir.Types)
                if (info.TryGetTypeByXmlName(type.Key, out var typeInfo))
                    InjectType(typeInfo, type.Value);
            return;

            void InjectType(XmlTypeInfo typeInfo, TypeIr typeIr)
            {
                if (string.IsNullOrEmpty(typeIr.Documentation))
                    typeIr.Documentation = docs.GetTypeComments(typeInfo.Type)?.Summary;

                switch (typeIr)
                {
                    case EnumTypeIr enumIr:
                        InjectEnumType(enumIr);
                        break;
                    case ObjectTypeIr objIr:
                        InjectObjectType(objIr);
                        break;
                    case PatternTypeIr _:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(typeIr), $"Type {typeIr.GetType()} not handled");
                }

                return;

                void InjectEnumType(EnumTypeIr enumIr)
                {
                    foreach (var item in enumIr.Items)
                    {
                        var member = (MemberInfo)typeInfo?.Type.GetField(item.Key) ?? typeInfo?.Type.GetProperty(item.Key);
                        if (member == null) continue;
                        var doc = docs.GetMemberComment(member);
                        if (doc == null) continue;
                        if (string.IsNullOrEmpty(item.Value.Documentation)) item.Value.Documentation = doc.Summary;
                    }
                }

                void InjectObjectType(ObjectTypeIr objIr)
                {
                    foreach (var member in typeInfo.Members)
                    {
                        var memberInfo = member.Value.Member;
                        var doc = docs.GetMemberComment(memberInfo);
                        if (objIr.Attributes.TryGetValue(member.Value.AttributeName ?? member.Key, out var attr))
                            Inject(attr);
                        var elementNames = member.Value.ElementNames;
                        foreach (var elementName in elementNames.Count == 0 ? new[] { member.Key } : elementNames)
                            if (objIr.Elements.TryGetValue(elementName, out var element))
                                Inject(element);

                        continue;

                        void Inject(PropertyIr prop)
                        {
                            if (prop.DefaultValue == null
                                && prop.Type is OptionalTypeReferenceIr { Item: PrimitiveTypeReferenceIr primitiveType })
                            {
                                if (DefaultValueFromCtor.TryGetInitializerValue(memberInfo, out var defaultValue))
                                {
                                    // Default value is whatever the property is initialized to in the constructor.
                                    prop.DefaultValue = defaultValue is bool val ? (val ? "true" : "false") : defaultValue.ToString();
                                }
                                else
                                {
                                    // Default value is default(T).
                                    prop.DefaultValue = primitiveType.Type switch
                                    {
                                        PrimitiveTypeIr.String => null,
                                        PrimitiveTypeIr.Boolean => "false",
                                        PrimitiveTypeIr.Double => "0.0",
                                        PrimitiveTypeIr.Integer => "0",
                                        _ => throw new ArgumentOutOfRangeException()
                                    };
                                }
                            }

                            if (string.IsNullOrEmpty(prop.Documentation) && !string.IsNullOrEmpty(doc?.Summary))
                                prop.Documentation = doc.Summary;
                            else if (memberInfo.GetCustomAttribute<DescriptionAttribute>() is { } descAttr
                                     && !string.IsNullOrEmpty(descAttr.Description))
                                prop.Documentation = descAttr.Description;

                            if (string.IsNullOrEmpty(prop.SampleValue) && !string.IsNullOrEmpty(doc?.Example))
                                prop.SampleValue = doc.Example;
                        }
                    }
                }
            }
        }
    }
}