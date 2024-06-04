export interface SchemaIr {
    types: { [typeName: string]: Type }
    rootElements: { [elementName: string]: Property }
}

export type Type = ObjectType | EnumType | PatternType;

export interface Documented {
    documentation?: string
}

export interface ObjectType extends Documented {
    $type: 'object',
    base?: CustomTypeRef,
    elements: { [elementName: string]: Property },
    attributes: { [attributeName: string]: Property },
    content?: ItemTypeRef
}

export interface Property extends Documented {
    type: TypeRef,
    default?: string,
    sample?: string
}

export interface EnumType extends Documented {
    $type: 'enum',
    flags: boolean,
    items: { [itemName: string]: EnumValue }
}

export interface EnumValue extends Documented { }

export interface PatternType extends Documented {
    $type: 'pattern',
    type: PrimitiveTypeRef,
    pattern: string
}

export type ItemTypeRef = PrimitiveTypeRef | CustomTypeRef;
export type TypeRef = ArrayTypeRef | OptionalTypeRef | ItemTypeRef;

export type PrimitiveType = 'String' | 'Boolean' | 'Double' | 'Integer';

export interface PrimitiveTypeRef {
    $type: 'primitive',
    type: PrimitiveType
}

export interface CustomTypeRef {
    $type: 'custom',
    name: string,
}

export interface ArrayTypeRef {
    $type: 'array',
    item: ItemTypeRef,
    wrapperElement?: string
}

export interface OptionalTypeRef {
    $type: 'optional',
    item: ItemTypeRef
}