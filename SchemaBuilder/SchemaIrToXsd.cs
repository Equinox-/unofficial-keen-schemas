using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Schema;

namespace SchemaBuilder
{
    public static class SchemaPrimitiveToXsd
    {
        internal const string XsNamespace = "http://www.w3.org/2001/XMLSchema";
        internal static readonly XmlQualifiedName AnyType = Xs("anyType");
        private static XmlQualifiedName Xs(string name) => new XmlQualifiedName(name, XsNamespace);

        private static readonly IReadOnlyDictionary<XmlQualifiedName, PrimitiveTypeIr> FromXsdTable = new Dictionary<XmlQualifiedName, PrimitiveTypeIr>
        {
            [Xs("string")] = PrimitiveTypeIr.String,
            [Xs("boolean")] = PrimitiveTypeIr.Boolean,
            [Xs("float")] = PrimitiveTypeIr.Double,
            [Xs("double")] = PrimitiveTypeIr.Double,
            [Xs("decimal")] = PrimitiveTypeIr.Double,
            [Xs("integer")] = PrimitiveTypeIr.Integer,
            [Xs("positiveInteger")] = PrimitiveTypeIr.Integer,
            [Xs("nonPositiveInteger")] = PrimitiveTypeIr.Integer,
            [Xs("negativeInteger")] = PrimitiveTypeIr.Integer,
            [Xs("nonNegativeInteger")] = PrimitiveTypeIr.Integer,
            [Xs("byte")] = PrimitiveTypeIr.Integer,
            [Xs("short")] = PrimitiveTypeIr.Integer,
            [Xs("int")] = PrimitiveTypeIr.Integer,
            [Xs("long")] = PrimitiveTypeIr.Integer,
            [Xs("unsignedByte")] = PrimitiveTypeIr.Integer,
            [Xs("unsignedShort")] = PrimitiveTypeIr.Integer,
            [Xs("unsignedInt")] = PrimitiveTypeIr.Integer,
            [Xs("unsignedLong")] = PrimitiveTypeIr.Integer,
        };

        private static readonly IReadOnlyDictionary<PrimitiveTypeIr, XmlQualifiedName> ToXsdTable = new Dictionary<PrimitiveTypeIr, XmlQualifiedName>
        {
            [PrimitiveTypeIr.String] = Xs("string"),
            [PrimitiveTypeIr.Boolean] = Xs("boolean"),
            [PrimitiveTypeIr.Double] = Xs("double"),
            [PrimitiveTypeIr.Integer] = Xs("long"),
        };

        public static bool TryPrimitiveIrFromXsd(this XmlQualifiedName name, out PrimitiveTypeIr ir) => FromXsdTable.TryGetValue(name, out ir);
        public static XmlQualifiedName ToXsd(this PrimitiveTypeIr ir) => ToXsdTable[ir];
    }

    public static class SchemaIrToXsd
    {
        public static XmlSchema Generate(SchemaIr ir, bool xsd11)
        {
            var state = new State(ir);
            var dest = new XmlSchema();
            dest.Namespaces.Add("xs", SchemaPrimitiveToXsd.XsNamespace);

            foreach (var type in ir.Types)
            {
                var compiled = type.Value switch
                {
                    EnumTypeIr enumType => (XmlSchemaType)State.GenerateEnum(enumType),
                    ObjectTypeIr objectType => state.GenerateObject(objectType, xsd11),
                    PatternTypeIr patternType => State.GeneratePattern(patternType),
                    _ => throw new ArgumentOutOfRangeException(nameof(type), $"Unsupported IR type {type.Key} {type.Value.GetType().Name}")
                };
                compiled.Name = type.Key;
                dest.Items.Add(compiled);
            }

            foreach (var element in ir.RootElements)
            {
                var xsd = state.CompileElement(element);
                xsd.MinOccursString = xsd.MaxOccursString = null;
                dest.Items.Add(xsd);
            }

            if (xsd11)
            {
                var schemaAttrs = dest.UnhandledAttributes;
                Array.Resize(ref schemaAttrs, (schemaAttrs?.Length ?? 0) + 1);
                schemaAttrs[schemaAttrs.Length - 1] = new XmlDocument().CreateAttribute("vc", "minVersion", "http://www.w3.org/2007/XMLSchema-versioning");
                schemaAttrs[schemaAttrs.Length - 1].Value = "1.1";
                dest.UnhandledAttributes = schemaAttrs;
            }

            return dest;
        }

        private sealed class State
        {
            private readonly SchemaIr _ir;
            private readonly HashSet<string> _polymorphicTypes = new HashSet<string>();

            public State(SchemaIr ir)
            {
                _ir = ir;
                foreach (var type in ir.Types.Values)
                    if (type is ObjectTypeIr { BaseType: { } } obj)
                        _polymorphicTypes.Add(obj.BaseType.Name);
            }

            internal XmlSchemaComplexType GenerateObject(ObjectTypeIr obj, bool xsd11)
            {
                var result = new XmlSchemaComplexType();
                XmlSchemaObjectCollection attributesOut;

                #region Flatten hierarchy when using XSD 1.0

                Dictionary<string, PropertyIr> flatAttributes;
                Dictionary<string, PropertyIr> flatElements;
                if (obj.BaseType == null || xsd11)
                {
                    flatAttributes = obj.Attributes;
                    flatElements = obj.Elements;
                }
                else
                {
                    flatAttributes = new Dictionary<string, PropertyIr>();
                    flatElements = new Dictionary<string, PropertyIr>();
                    var search = obj;
                    while (search != null)
                    {
                        foreach (var attr in search.Attributes)
                            if (!flatAttributes.ContainsKey(attr.Key))
                                flatAttributes.Add(attr.Key, attr.Value);
                        foreach (var element in search.Elements)
                            if (!flatElements.ContainsKey(element.Key))
                                flatElements.Add(element.Key, element.Value);
                        search = search.BaseType != null ? (ObjectTypeIr)_ir.Types[search.BaseType.Name] : null;
                    }
                }

                #endregion

                if (obj.Content != null)
                {
                    #region Content

                    if (obj.Elements.Count > 0)
                        throw new Exception("Object type containing content must not have elements");
                    if (obj.BaseType != null)
                        throw new Exception("Object type containing content must not have a base type");
                    HandleSimpleTypeReference(obj.Content, out var contentType, out _);
                    var content = new XmlSchemaSimpleContentExtension { BaseTypeName = contentType };
                    attributesOut = content.Attributes;
                    result.ContentModel = new XmlSchemaSimpleContent { Content = content };

                    #endregion
                }
                else
                {
                    #region Elements

                    XmlSchemaParticle elementsOut;
                    var compiledElements = flatElements.Select(CompileElement).ToList();
                    if (xsd11 || !compiledElements.Any(x => x.MaxOccurs > 1))
                    {
                        // Use all if there are no repeated elements or if this is an XSD 1.1 schema.
                        var all = new XmlSchemaAll();
                        foreach (var element in compiledElements)
                            all.Items.Add(element);
                        elementsOut = all;
                    }
                    else
                    {
                        // Otherwise use a repeated choice.
                        var choice = new XmlSchemaChoice { MinOccurs = 0, MaxOccursString = "unbounded" };
                        foreach (var element in compiledElements)
                        {
                            element.MinOccursString = element.MaxOccursString = null;
                            choice.Items.Add(element);
                        }

                        elementsOut = choice;
                    }

                    #endregion

                    #region Inheritance

                    if (obj.BaseType != null && xsd11)
                    {
                        var content = new XmlSchemaComplexContentExtension
                        {
                            BaseTypeName = new XmlQualifiedName(obj.BaseType.Name),
                        };
                        attributesOut = content.Attributes;
                        content.Particle = elementsOut;
                        result.ContentModel = new XmlSchemaComplexContent { Content = content };
                    }
                    else
                    {
                        attributesOut = result.Attributes;
                        result.Particle = elementsOut;
                    }

                    #endregion
                }

                #region Attributes

                foreach (var attr in flatAttributes)
                {
                    var xsd = new XmlSchemaAttribute
                    {
                        Name = attr.Key,
                        DefaultValue = attr.Value.DefaultValue,
                    };
                    HandleSimpleTypeReference(attr.Value.Type, out var xml, out var optional);
                    xsd.SchemaTypeName = xml;
                    xsd.Use = optional ? XmlSchemaUse.Optional : XmlSchemaUse.Required;
                    attributesOut.Add(WithDocumentation(xsd, attr.Value));
                }

                #endregion

                return WithDocumentation(result, obj);
            }

            internal XmlSchemaElement CompileElement(KeyValuePair<string, PropertyIr> property)
            {
                var element = CompileElementType(property.Key, property.Value.Type);
                element.DefaultValue = property.Value.DefaultValue;
                return WithDocumentation(element, property.Value);

                XmlSchemaElement CompileElementType(string name, TypeReferenceIr type)
                {
                    switch (type)
                    {
                        case OptionalTypeReferenceIr optional:
                            // ReSharper disable once TailRecursiveCall
                            var optionalElement = CompileElementType(name, optional.Item);
                            optionalElement.MinOccurs = 0;
                            return optionalElement;
                        case ArrayTypeReferenceIr { WrapperElement: { } } wrappedArray:
                            var nestedElement = CompileElementType(wrappedArray.WrapperElement, wrappedArray.Item);
                            if (nestedElement.MaxOccurs > 1)
                                throw new Exception("Arrays can't contain repeated elements");
                            nestedElement.MinOccurs = 0;
                            nestedElement.MaxOccursString = "unbounded";
                            return new XmlSchemaElement
                            {
                                Name = name,
                                SchemaType = new XmlSchemaComplexType
                                {
                                    Particle = new XmlSchemaSequence
                                    {
                                        Items = { nestedElement }
                                    }
                                },
                                MinOccurs = 0,
                                MaxOccurs = 1,
                            };
                        case ArrayTypeReferenceIr array:
                            var repeatedElement = CompileElementType(name, array.Item);
                            repeatedElement.MinOccurs = 0;
                            repeatedElement.MaxOccursString = "unbounded";
                            return repeatedElement;
                        default:
                            HandleSimpleTypeReference(type, out var xmlType, out var isOptional);
                            var xsd = new XmlSchemaElement
                            {
                                Name = name,
                                SchemaTypeName = xmlType
                            };
                            if (isOptional)
                                xsd.MinOccurs = 0;
                            return xsd;
                    }
                }
            }

            internal static XmlSchemaSimpleType GenerateEnum(EnumTypeIr enumeration)
            {
                var content = new XmlSchemaSimpleTypeRestriction { BaseTypeName = PrimitiveTypeIr.String.ToXsd() };
                foreach (var value in enumeration.Items)
                    content.Facets.Add(WithDocumentation(new XmlSchemaEnumerationFacet { Value = value.Key }, value.Value));
                var simple = new XmlSchemaSimpleType { Content = content };
                if (enumeration.Flags)
                    simple = new XmlSchemaSimpleType { Content = new XmlSchemaSimpleTypeList { ItemType = simple } };
                return WithDocumentation(simple, enumeration);
            }

            internal static XmlSchemaSimpleType GeneratePattern(PatternTypeIr pattern) => WithDocumentation(new XmlSchemaSimpleType
            {
                Content = new XmlSchemaSimpleTypeRestriction
                {
                    BaseTypeName = pattern.Type.ToXsd(),
                    Facets = { new XmlSchemaPatternFacet { Value = pattern.Pattern } }
                }
            }, pattern);

            private void HandleSimpleTypeReference(TypeReferenceIr typeRef, out XmlQualifiedName xmlType, out bool isOptional)
            {
                isOptional = false;
                switch (typeRef)
                {
                    case CustomTypeReferenceIr custom:
                        xmlType = _polymorphicTypes.Contains(custom.Name) ? SchemaPrimitiveToXsd.AnyType : new XmlQualifiedName(custom.Name);
                        break;
                    case OptionalTypeReferenceIr optional:
                        HandleSimpleTypeReference(optional.Item, out xmlType, out isOptional);
                        isOptional = true;
                        break;
                    case PrimitiveTypeReferenceIr primitive:
                        xmlType = primitive.Type.ToXsd();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(typeRef), $"Failed to convert XML type reference {typeRef} to simple type");
                }
            }

            private static T WithDocumentation<T>(T annotated, BaseElementIr baseElement) where T : XmlSchemaAnnotated
            {
                if (string.IsNullOrEmpty(baseElement.Documentation))
                    return annotated;
                annotated.Annotation ??= new XmlSchemaAnnotation();
                annotated.Annotation.Items.Add(new XmlSchemaDocumentation
                {
                    Markup = new XmlNode[] { new XmlDocument().CreateTextNode(baseElement.Documentation) }
                });
                return annotated;
            }
        }
    }
}