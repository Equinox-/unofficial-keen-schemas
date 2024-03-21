using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace SchemaBuilder
{
    public static class SerializationProxies
    {
        // Guid imports another schema that screws with VS Code, so proxy it to a different type manually.
        public static readonly IReadOnlyDictionary<Type, Type> ProxiesByType = new Dictionary<Type, Type>
        {
            [typeof(Guid[])] = typeof(GuidArrayProxy),
            [typeof(List<Guid>)] = typeof(GuidArrayProxy),
        };

        public static readonly IReadOnlyDictionary<string, Type> ProxiesByTypeName = new Dictionary<string, Type>
        {
            ["SerializableDictionary<Guid, string>"] = typeof(StringStringSerializableDictionaryProxy),
        };
    }

    public class GuidArrayProxy
    {
        [XmlElement("guid")]
        public List<string> Guids;
    }

    public class StringStringSerializableDictionaryProxy
    {
        public struct Entry
        {
            public string Key;
            public string Value;
        }

        [XmlArray("dictionary")]
        [XmlArrayItem("item")]
        public Entry[] DictionaryEntryProp;
    }
}