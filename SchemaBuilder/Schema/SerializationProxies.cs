using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Xml.Serialization;

namespace SchemaBuilder.Schema
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
            ["MyStringId"] = typeof(string),
            ["MyStringHash"] = typeof(string),
            ["MyStringId?"] = typeof(string),
            ["MyStringHash?"] = typeof(string),
        };

        // Creates a serialization proxy for a field serialized using MyStructXmlSerializer<structure>
        private static readonly Dictionary<Type, (Type, Dictionary<string, object>)> DefaultingStructProxies = new Dictionary<Type, (Type, Dictionary<string, object>)>();

        public const string OriginatingTypeField = "_OriginatingType";
        public static (Type, Dictionary<string, object>) CreateDefaultingStructProxy(Type structure)
        {
            if (DefaultingStructProxies.TryGetValue(structure, out var defaulting))
                return defaulting;

            var defaultValue = structure.GetFields(BindingFlags.Static | BindingFlags.Public)
                .FirstOrDefault(x => x.IsInitOnly && x.GetCustomAttributesData().Any(a => a.AttributeType.FullName == "VRage.StructDefaultAttribute"))?
                .GetValue(null);
            if (defaultValue == null)
                return DefaultingStructProxies[structure] = (structure, new Dictionary<string, object>());

            var defaults = new Dictionary<string, object>();

            var asm = AppDomain.CurrentDomain
                .DefineDynamicAssembly(new AssemblyName("DefaultingStructAsm" + structure.Name), AssemblyBuilderAccess.Run)
                .DefineDynamicModule("DefaultingStructModule" + structure.Name)
                .DefineType(structure.FullName + "WithDefaults", TypeAttributes.Public);

            foreach (var field in structure.GetFields(BindingFlags.Instance | BindingFlags.Public))
                Add(field.Name, field.FieldType, field.GetValue(defaultValue));

            foreach (var prop in structure.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                Add(prop.Name, prop.PropertyType, prop.GetValue(defaultValue));

            var originating = asm.DefineField(OriginatingTypeField, typeof(Type), FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly);

            var cctor = asm.DefineMethod(".cctor",
                MethodAttributes.Private | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName | MethodAttributes.Static,
                CallingConventions.Standard,
                typeof(void), Type.EmptyTypes);
            var ilg = cctor.GetILGenerator();
            ilg.Emit(OpCodes.Ldtoken, structure);
            ilg.Emit(OpCodes.Stsfld, originating);
            ilg.Emit(OpCodes.Ret);

            return DefaultingStructProxies[structure] = (asm.CreateType(), defaults);

            void Add(string name, Type type, object propDefault)
            {
                defaults.Add(name, propDefault);
                asm.DefineField(name, type, FieldAttributes.Public);
            }
        }
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