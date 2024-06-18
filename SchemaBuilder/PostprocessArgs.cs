using System.Xml.Schema;
using SchemaBuilder.Schema;

namespace SchemaBuilder
{
    public sealed class PostprocessArgs
    {
        public XmlInfo Info;
        public SchemaConfig Patches;
        public XmlSchema Schema;

        public void TypeData(string typeName, out XmlTypeInfo typeInfo)
        {
            Info.TryGetTypeByXmlName(typeName, out typeInfo);
        }
    }
}