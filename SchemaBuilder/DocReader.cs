using System;
using System.Linq;
using System.Reflection;
using LoxSmoke.DocXml;

namespace SchemaBuilder
{
    public sealed class DocReader
    {
        private readonly DocXmlReader _docs = new DocXmlReader();

        public static Type MapType(Type type)
        {
            if (type.Assembly.IsDynamic)
                return type.GetField(SerializationProxies.OriginatingTypeField)?.GetValue(null) as Type;
            return type;
        }

        public static MemberInfo MapMember(MemberInfo member)
        {
            if (member.DeclaringType == null)
                return null;
            var type = MapType(member.DeclaringType);
            if (type == member.DeclaringType)
                return member;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
            return member switch
            {
                MethodInfo method => type.GetMethod(method.Name, flags, null, CallingConventions.Any,
                    method.GetParameters().Select(x => x.ParameterType).ToArray(), Array.Empty<ParameterModifier>()),
                PropertyInfo property => type.GetProperty(property.Name, flags),
                FieldInfo field => type.GetField(field.Name, flags),
                _ => null
            };
        }

        public TypeComments GetTypeComments(Type type)
        {
            var mapped = MapType(type);
            return mapped != null ? _docs.GetTypeComments(mapped) : null;
        }

        public string GetMemberComment(MemberInfo member)
        {
            var mapped = MapMember(member);
            return mapped != null ? _docs.GetMemberComment(mapped) : null;
        }
    }
}