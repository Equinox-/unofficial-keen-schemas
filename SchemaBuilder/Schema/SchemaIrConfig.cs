using System;
using System.Collections.Generic;

namespace SchemaBuilder.Schema
{
    public static class SchemaIrConfig
    {
        public static void ApplyConfig(SchemaIr ir, XmlInfo info, SchemaConfig config)
        {
            foreach (var type in ir.Types)
            {
                var patch = config.TypePatch(type.Key);
                if (patch == null && info.TryGetTypeByXmlName(type.Key, out var typeInfo))
                    patch = config.TypePatch(typeInfo.Type.FullName) ?? config.TypePatch(typeInfo.Type.Name);
                ApplyType(patch, type.Value);
            }

            return;

            void ApplyType(TypePatch typeInfo, TypeIr typeIr)
            {
                if (!string.IsNullOrEmpty(typeInfo?.Documentation)) typeIr.Documentation = typeInfo.Documentation;

                switch (typeIr)
                {
                    case EnumTypeIr enumIr:
                        ApplyEnumType(enumIr);
                        break;
                    case ObjectTypeIr objIr:
                        ApplyObjectType(objIr);
                        break;
                    case PatternTypeIr _:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(typeIr), $"Type {typeIr.GetType()} not handled");
                }

                return;

                void ApplyEnumType(EnumTypeIr enumIr)
                {
                    var removing = new List<string>();
                    foreach (var item in enumIr.Items)
                    {
                        var patch = typeInfo?.EnumPatch(item.Key);
                        if (patch == null) continue;
                        if (patch.Delete == InheritableTrueFalse.True) removing.Add(item.Key);
                        if (!string.IsNullOrEmpty(item.Value.Documentation)) item.Value.Documentation = patch.Documentation;
                    }

                    foreach (var item in removing)
                        enumIr.Items.Remove(item);
                }

                void ApplyObjectType(ObjectTypeIr objIr)
                {
                    ApplyProperties(objIr.Attributes, n => typeInfo?.AttributePatch(n));
                    ApplyProperties(objIr.Elements, n => typeInfo?.ElementPatch(n));
                }

                void ApplyProperties(Dictionary<string, PropertyIr> properties, Func<string, MemberPatch> getter)
                {
                    var removing = new List<string>();
                    foreach (var prop in properties)
                    {
                        var patch = getter(prop.Key);
                        if (patch?.Delete == InheritableTrueFalse.True) removing.Add(prop.Key);
                        if (!string.IsNullOrEmpty(patch?.Documentation)) prop.Value.Documentation = patch.Documentation;
                        if (!string.IsNullOrEmpty(patch?.Sample)) prop.Value.SampleValue = patch.Sample;
                        switch ((patch?.Optional).OrInherit(config.AllOptional))
                        {
                            case InheritableTrueFalse.Inherit:
                                break;
                            case InheritableTrueFalse.True:
                                if (prop.Value.Type is ItemTypeReferenceIr item)
                                    prop.Value.Type = new OptionalTypeReferenceIr { Item = item };
                                break;
                            case InheritableTrueFalse.False:
                                if (prop.Value.Type is OptionalTypeReferenceIr opt)
                                    prop.Value.Type = opt.Item;
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }

                    foreach (var item in removing)
                        properties.Remove(item);
                }
            }
        }
    }
}