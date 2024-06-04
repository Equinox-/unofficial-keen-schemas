using System;
using System.Reflection;

namespace SchemaBuilder
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
                        if (string.IsNullOrEmpty(item.Value.Documentation))
                        {
                            var member = (MemberInfo)typeInfo?.Type.GetField(item.Key) ?? typeInfo?.Type.GetProperty(item.Key);
                            if (member == null) return;
                            item.Value.Documentation = docs.GetMemberComment(member);
                        }
                }

                void InjectObjectType(ObjectTypeIr objIr)
                {
                    foreach (var member in typeInfo.Members)
                    {
                        var doc = docs.GetMemberComment(member.Value.Member);
                        if (string.IsNullOrEmpty(doc)) continue;
                        if (objIr.Attributes.TryGetValue(member.Key, out var attr) && string.IsNullOrEmpty(attr.Documentation))
                            attr.Documentation = doc;
                        if (objIr.Elements.TryGetValue(member.Key, out var element) && string.IsNullOrEmpty(element.Documentation))
                            element.Documentation = doc;
                    }
                }
            }
        }
    }
}