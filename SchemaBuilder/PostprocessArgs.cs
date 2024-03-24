using System.Xml.Schema;

namespace SchemaBuilder
{
    public sealed class PostprocessArgs
    {
        public XmlInfo Info;
        public SchemaConfig Patches;
        public XmlSchema Schema;

        public void TypeData(string typeName, out XmlTypeInfo typeInfo, out TypePatch typePatch)
        {
            Info.TryGetTypeByXmlName(typeName, out typeInfo);
            typePatch = Patches.TypePatch(typeName);
            if (typePatch == null && typeInfo != null)
                typePatch = Patches.TypePatch(typeInfo.Type.FullName);
            if (typePatch != null || typeInfo == null)
                return;
            var mapped = DocReader.MapType(typeInfo.Type);
            if (mapped != null)
                typePatch = Patches.TypePatch(mapped.FullName);
        }
    }
}