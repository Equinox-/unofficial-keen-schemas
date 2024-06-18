using System;
using System.Reflection;
using System.Xml;
using System.Xml.Schema;

namespace SchemaBuilder.Schema
{
    public static class XmlReflection
    {
        private static readonly MethodInfo AddToCollection = typeof(XmlSchemaObjectTable).GetMethod("Add",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            CallingConventions.Any,
            new[] { typeof(XmlQualifiedName), typeof(XmlSchemaObject) },
            Array.Empty<ParameterModifier>());

        public static void Add(this XmlSchemaObjectTable collection, XmlQualifiedName name, XmlSchemaObject obj)
        {
            AddToCollection.Invoke(collection, new object[] { name, obj });
        }
    }
}