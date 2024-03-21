using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace SchemaBuilder
{
    [XmlRoot("Patches")]
    public class PatchFile
    {
        [XmlElement]
        public bool AllOptional;
        
        [XmlElement("Type")]
        public List<TypePatch> Types = new List<TypePatch>();

        public TypePatch TypePatch(string name) => Types.FirstOrDefault(x => x.Name == name);

        public static readonly XmlSerializer Serializer = new XmlSerializer(typeof(PatchFile));

        public static PatchFile Read(string path)
        {
            using var stream = File.OpenRead(path);
            var cfg = (PatchFile)Serializer.Deserialize(stream);
            return cfg;
        }
    }

    public class TypePatch
    {
        [XmlAttribute]
        public string Name;

        [XmlElement]
        public string Documentation;
        
        [XmlElement("Member")]
        public List<MemberPatch> Members = new List<MemberPatch>();

        public MemberPatch MemberPatch(string name) => Members.FirstOrDefault(x => x.Name == name);
    }

    public class MemberPatch
    {
        [XmlAttribute]
        public string Name;

        [XmlElement]
        public string Documentation;

        [XmlAttribute]
        public bool MakeOptional;

        [XmlAttribute]
        public bool MakeRequired;
    }
}