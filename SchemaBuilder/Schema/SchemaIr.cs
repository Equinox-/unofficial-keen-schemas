using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

// ReSharper disable NonReadonlyMemberInGetHashCode

namespace SchemaBuilder.Schema
{
    public sealed class SchemaIr
    {
        [JsonPropertyName("types")]
        public Dictionary<string, TypeIr> Types = new Dictionary<string, TypeIr>();

        [JsonPropertyName("rootElements")]
        public Dictionary<string, PropertyIr> RootElements = new Dictionary<string, PropertyIr>();
    }

    public abstract class BaseElementIr
    {
        [JsonPropertyName("documentation")]
        public string Documentation;
    }

    [JsonDerivedType(typeof(ObjectTypeIr), "object")]
    [JsonDerivedType(typeof(EnumTypeIr), "enum")]
    [JsonDerivedType(typeof(PatternTypeIr), "pattern")]
    public abstract class TypeIr : BaseElementIr
    {
    }

    public sealed class ObjectTypeIr : TypeIr
    {
        [JsonPropertyName("base")]
        public CustomTypeReferenceIr BaseType;

        [JsonPropertyName("elements")]
        public Dictionary<string, PropertyIr> Elements = new Dictionary<string, PropertyIr>();

        [JsonPropertyName("attributes")]
        public Dictionary<string, PropertyIr> Attributes = new Dictionary<string, PropertyIr>();

        [JsonPropertyName("content")]
        public PrimitiveTypeReferenceIr Content;
    }

    public sealed class PropertyIr : BaseElementIr
    {
        [JsonPropertyName("type")]
        public TypeReferenceIr Type;

        [JsonPropertyName("default")]
        public string DefaultValue;

        [JsonPropertyName("sample")]
        public string SampleValue;
    }

    public sealed class EnumTypeIr : TypeIr
    {
        [JsonPropertyName("flags")]
        public bool Flags;

        [JsonPropertyName("items")]
        public Dictionary<string, EnumValueIr> Items = new Dictionary<string, EnumValueIr>();
    }

    public sealed class PatternTypeIr : TypeIr
    {
        [JsonPropertyName("type")]
        public PrimitiveTypeIr Type;

        [JsonPropertyName("pattern")]
        public string Pattern;
    }

    public sealed class EnumValueIr : BaseElementIr
    {
    }

    [JsonDerivedType(typeof(PrimitiveTypeReferenceIr), "primitive")]
    [JsonDerivedType(typeof(CustomTypeReferenceIr), "custom")]
    [JsonDerivedType(typeof(ArrayTypeReferenceIr), "array")]
    [JsonDerivedType(typeof(OptionalTypeReferenceIr), "optional")]
    public abstract class TypeReferenceIr : IEquatable<TypeReferenceIr>
    {
        public bool Equals(TypeReferenceIr other) => Equals((object)other);
    }

    [JsonDerivedType(typeof(PrimitiveTypeReferenceIr), "primitive")]
    [JsonDerivedType(typeof(CustomTypeReferenceIr), "custom")]
    public abstract class ItemTypeReferenceIr : TypeReferenceIr
    {
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PrimitiveTypeIr
    {
        String,
        Boolean,
        Double,
        Integer,
    }

    public sealed class PrimitiveTypeReferenceIr : ItemTypeReferenceIr, IEquatable<PrimitiveTypeReferenceIr>
    {
        [JsonPropertyName("type")]
        public PrimitiveTypeIr Type;

        public bool Equals(PrimitiveTypeReferenceIr other) => other != null && Type == other.Type;

        public override bool Equals(object obj) => ReferenceEquals(this, obj) || obj is PrimitiveTypeReferenceIr other && Equals(other);

        public override int GetHashCode() => (int)Type;

        public override string ToString() => $"Primitive[{Type}]";
    }

    public sealed class CustomTypeReferenceIr : ItemTypeReferenceIr, IEquatable<CustomTypeReferenceIr>
    {
        [JsonPropertyName("name")]
        public string Name;

        public bool Equals(CustomTypeReferenceIr other) => other != null && Name == other.Name;

        public override bool Equals(object obj) => ReferenceEquals(this, obj) || obj is CustomTypeReferenceIr other && Equals(other);

        public override int GetHashCode() => Name?.GetHashCode() ?? 0;

        public override string ToString() => $"Custom[{Name}]";
    }

    public sealed class ArrayTypeReferenceIr : TypeReferenceIr, IEquatable<ArrayTypeReferenceIr>
    {
        [JsonPropertyName("item")]
        public ItemTypeReferenceIr Item;

        [JsonPropertyName("wrapperElement")]
        public string WrapperElement;

        public bool Equals(ArrayTypeReferenceIr other) => other != null && Equals(Item, other.Item) && WrapperElement == other.WrapperElement;

        public override bool Equals(object obj) => ReferenceEquals(this, obj) || obj is ArrayTypeReferenceIr other && Equals(other);

        public override int GetHashCode() => ((Item?.GetHashCode() ?? 0) * 397) ^ (WrapperElement?.GetHashCode() ?? 0);

        public override string ToString() => $"Array[Item={Item}, Wrapper={WrapperElement}]";
    }

    public sealed class OptionalTypeReferenceIr : TypeReferenceIr, IEquatable<OptionalTypeReferenceIr>
    {
        [JsonPropertyName("item")]
        public ItemTypeReferenceIr Item;

        public bool Equals(OptionalTypeReferenceIr other) => other != null && Equals(Item, other.Item);

        public override bool Equals(object obj) => ReferenceEquals(this, obj) || obj is OptionalTypeReferenceIr other && Equals(other);

        public override int GetHashCode() => Item?.GetHashCode() ?? 0;

        public override string ToString() => $"Optional[{Item}]";
    }
}