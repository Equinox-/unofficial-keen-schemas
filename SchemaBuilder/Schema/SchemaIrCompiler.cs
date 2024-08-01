using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml;
using System.Xml.Schema;

namespace SchemaBuilder.Schema
{
    public static class SchemaIrCompiler
    {
        public static SchemaIr Compile(XmlSchema schema)
        {
            var ir = new SchemaIr();

            foreach (var type in schema.SchemaTypes.Values.OfType<XmlSchemaType>())
            {
                ir.Types.Add(type.Name, CompileType(type));
            }

            foreach (var element in schema.Elements.Values.OfType<XmlSchemaElement>())
                ir.RootElements.Add(element.Name, CompileElement(element));

            // Resolve generated ArrayOf* types.
            PostprocessArrayTypes(ir);
            return ir;
        }

        private static TypeIr CompileType(XmlSchemaType type) => type switch
        {
            XmlSchemaComplexType complex => CompileComplexType(complex),
            XmlSchemaSimpleType simple => CompileSimpleType(simple),
            _ => throw new ArgumentOutOfRangeException(nameof(type), $"Unsupported type {type.GetType()}")
        };

        private static TypeIr CompileComplexType(XmlSchemaComplexType type)
        {
            var result = new ObjectTypeIr();
            AttachDocumentation(result, type);

            IndexParticle(type.Particle);
            IndexAttributes(type.Attributes);

            switch (type.ContentModel?.Content)
            {
                case XmlSchemaComplexContentExtension complexExt:
                    IndexParticle(complexExt.Particle);
                    IndexAttributes(complexExt.Attributes);
                    result.BaseType = (CustomTypeReferenceIr)CompileTypeReference(complexExt.BaseTypeName);
                    break;
                case XmlSchemaSimpleContentExtension simpleExt:
                    IndexAttributes(simpleExt.Attributes);
                    result.Content = (PrimitiveTypeReferenceIr)CompileTypeReference(simpleExt.BaseTypeName);
                    break;
            }

            return result;

            void IndexParticle(XmlSchemaParticle particle)
            {
                switch (particle)
                {
                    case XmlSchemaElement element:
                        result.Elements.Add(element.Name, CompileElement(element));
                        break;
                    case XmlSchemaGroupBase group:
                        foreach (var child in group.Items.OfType<XmlSchemaParticle>())
                            IndexParticle(child);
                        break;
                }
            }

            void IndexAttributes(XmlSchemaObjectCollection attributes)
            {
                foreach (var attribute in attributes.OfType<XmlSchemaAttribute>())
                    result.Attributes.Add(attribute.Name, CompileAttribute(attribute));
            }
        }

        private static PropertyIr CompileElement(XmlSchemaElement element)
        {
            var itemType = CompileTypeReference(element.SchemaTypeName);
            var result = new PropertyIr
            {
                DefaultValue = element.DefaultValue
            };
            AttachDocumentation(result, element);
            if (element.MaxOccurs > 1)
                result.Type = new ArrayTypeReferenceIr { Item = itemType };
            else if (element.MinOccurs == 0)
                result.Type = new OptionalTypeReferenceIr { Item = itemType };
            else
                result.Type = itemType;
            return result;
        }

        private static PropertyIr CompileAttribute(XmlSchemaAttribute attribute)
        {
            var itemType = CompileTypeReference(attribute.SchemaTypeName);
            var result = new PropertyIr
            {
                DefaultValue = attribute.DefaultValue,
                Type = attribute.Use switch
                {
                    XmlSchemaUse.Optional => new OptionalTypeReferenceIr { Item = itemType },
                    _ => itemType
                }
            };
            AttachDocumentation(result, attribute);
            return result;
        }

        private static TypeIr CompileSimpleType(XmlSchemaSimpleType type)
        {
            return AttachDocumentation(CompileSimpleTypeContent(type.Content), type);

            TypeIr CompileSimpleTypeContent(XmlSchemaSimpleTypeContent content) => content switch
            {
                XmlSchemaSimpleTypeRestriction restriction => CompileSimpleTypeRestriction(restriction),
                XmlSchemaSimpleTypeList list => CompileSimpleTypeList(list),
                _ => throw new ArgumentOutOfRangeException(nameof(type), $"Unsupported simple type {type.Name} with content {content.GetType()}")
            };

            TypeIr CompileSimpleTypeRestriction(XmlSchemaSimpleTypeRestriction restriction)
            {
                if (!restriction.BaseTypeName.TryPrimitiveIrFromXsd(out var primitiveTypeIr) || primitiveTypeIr != PrimitiveTypeIr.String)
                    throw new Exception($"Type {type} is a restriction of unsupported base type {restriction.BaseTypeName}.");
                var isPrimitive = restriction.BaseTypeName.TryPrimitiveIrFromXsd(out var primitiveType);
                var enumeration = restriction.Facets.OfType<XmlSchemaEnumerationFacet>()
                    .ToDictionary(x => x.Value, x => AttachDocumentation(new EnumValueIr(), x));
                if (enumeration.Count > 0)
                    return new EnumTypeIr { Items = enumeration };
                var pattern = restriction.Facets.OfType<XmlSchemaPatternFacet>().ToList();
                if (pattern.Count == 1 && isPrimitive)
                    return new PatternTypeIr { Type = primitiveType, Pattern = pattern[0].Value };
                throw new ArgumentOutOfRangeException(nameof(type),
                    $"Unsupported simple type {type.Name} restrictions {restriction.Facets.OfType<object>().Select(x => x.GetType().Name).ToList()}");
            }

            TypeIr CompileSimpleTypeList(XmlSchemaSimpleTypeList list)
            {
                var content = CompileSimpleTypeContent(list.ItemType.Content);
                switch (content)
                {
                    case EnumTypeIr enumType:
                        enumType.Flags = true;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(content), $"Can't create list {type.Name} with content {content.GetType()}");
                }

                return content;
            }
        }

        private static ItemTypeReferenceIr CompileTypeReference(XmlQualifiedName typeName)
        {
            if (typeName.TryPrimitiveIrFromXsd(out var primitive))
                return new PrimitiveTypeReferenceIr { Type = primitive };
            return new CustomTypeReferenceIr { Name = typeName.Name };
        }

        private static void PostprocessArrayTypes(SchemaIr ir)
        {
            var replacements = new Dictionary<string, ArrayTypeReferenceIr>();
            foreach (var type in ir.Types)
                if (TryResolveWrappedArrayType(type.Key, type.Value, out var array))
                    replacements.Add(type.Key, array);
            foreach (var array in replacements.Keys)
                ir.Types.Remove(array);

            ReplaceTypeReferences(ir, typeRef =>
            {
                if (typeRef is CustomTypeReferenceIr custom && replacements.TryGetValue(custom.Name, out var replacement))
                    return replacement;
                return typeRef;
            });
        }

        private static bool TryResolveWrappedArrayType(string typeName, TypeIr type, out ArrayTypeReferenceIr resolved)
        {
            if (typeName.StartsWith("ArrayOf")
                && type is ObjectTypeIr obj
                && obj.Attributes.Count == 0
                && obj.Elements.Count == 1
                && obj.Elements.First() is var onlyProp
                && onlyProp.Value.Type is ArrayTypeReferenceIr { WrapperElement: null } singleArray)
            {
                resolved = new ArrayTypeReferenceIr
                {
                    Item = singleArray.Item,
                    WrapperElement = onlyProp.Key,
                };
                return true;
            }

            resolved = null;
            return false;
        }

        private static void ReplaceTypeReferences(SchemaIr ir, Func<TypeReferenceIr, TypeReferenceIr> replacement)
        {
            foreach (var type in ir.Types.Values)
                ReplaceForType(type);
            foreach (var element in ir.RootElements.Values)
                element.Type = FixRef(element.Type);
            return;

            void ReplaceForType(TypeIr type)
            {
                switch (type)
                {
                    case EnumTypeIr _:
                    case PatternTypeIr _:
                        break;
                    case ObjectTypeIr obj:
                        foreach (var prop in obj.Elements.Values)
                            FixProp(prop);
                        foreach (var prop in obj.Attributes.Values)
                            FixProp(prop);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(type));
                }
            }

            void FixProp(PropertyIr prop) => prop.Type = FixRef(prop.Type);

            TypeReferenceIr FixRef(TypeReferenceIr reference)
            {
                switch (replacement(reference))
                {
                    case ArrayTypeReferenceIr array:
                        array.Item = (ItemTypeReferenceIr)FixRef(array.Item);
                        return array;
                    case OptionalTypeReferenceIr optional:
                        var fixedRef = FixRef(optional.Item);
                        if (!(fixedRef is ItemTypeReferenceIr fixedItem))
                            return fixedRef;
                        optional.Item = fixedItem;
                        return optional;
                    case PrimitiveTypeReferenceIr primitive:
                        return primitive;
                    case CustomTypeReferenceIr custom:
                        return custom;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(reference));
                }
            }
        }

        private static T AttachDocumentation<T>(T ir, XmlSchemaAnnotated annotated) where T : BaseElementIr
        {
            if (!(annotated.Annotation?.Items?.Count > 0)) return ir;
            foreach (var item in annotated.Annotation.Items.OfType<XmlSchemaDocumentation>())
            foreach (var node in item.Markup.OfType<XmlText>())
                ir.Documentation = (ir.Documentation ?? "") + node.Value;
            return ir;
        }
    }
}