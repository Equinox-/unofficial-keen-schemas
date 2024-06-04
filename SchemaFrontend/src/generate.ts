import { ItemTypeRef, ObjectType, PrimitiveType, Property, SchemaIr, Type, TypeRef } from "./ir";
import { XmlBuilder } from "./xml";

function typeByName(ir: SchemaIr, name: string): Type {
    const type = ir.types[name];
    if (type == null)
        throw new Error('Type is not in the schema ' + name);
    return type;
}

function constrainedTypeByName<T extends Type['$type']>(ir: SchemaIr, name: string, ...constraints: T[]): Type & { $type: T } {
    const type = typeByName(ir, name);
    if (constraints.indexOf(type.$type as T) == -1)
        throw new Error('Type ' + name + ' must be one of [' + constraints.join(', ') + '] but was ' + type.$type);
    return type as any;
}

export function generateExample(ir: SchemaIr, builder: XmlBuilder, path: string[]) {
    let type: ObjectType = { $type: 'object', elements: ir.rootElements, attributes: {} };
    let openedElements = 0;
    for (const element of path) {
        const [name, customTypeName] = element.split('@', 2);
        const property = type.elements[name];
        if (property == null) {
            throw new Error('Navigating path ' + path.join(' -> ') + ' at ' + element + ', element is missing');
        }
        builder.startElement(name, property.documentation);
        openedElements++;

        let itemType: ItemTypeRef;
        switch (property.type.$type) {
            case "array":
                itemType = property.type.item;
                if (property.type.wrapperElement != null) {
                    builder.startElement(property.type.wrapperElement);
                    openedElements++;
                }
                break;
            case "optional":
                itemType = property.type.item;
                break;
            case 'custom':
            case 'primitive':
                itemType = property.type;
                break;
        }

        if (customTypeName != null) {
            builder.writeAttribute('xsi:type', customTypeName);
            type = constrainedTypeByName(ir, customTypeName, 'object');
            continue;
        }

        if (itemType.$type != 'custom') {
            throw new Error('Navigating path ' + path.join(' -> ') + ' at ' + element + ', element is not an object');
        }
        type = constrainedTypeByName(ir, itemType.name, 'object');
    }
    generateObjectContents(ir, builder, type);
    for (let i = 0; i < openedElements; i++) {
        builder.closeElement();
    }
}

const SampleOmit: string = '__omit__';

function propertyDocumentation(prop: Property): string | undefined {
    let doc: string | undefined = null;
    if (prop.documentation != null) {
        if (doc != null) doc += '<br>';
        doc = (doc ?? '') + prop.documentation;
    }
    if (prop.type.$type == 'array' && prop.type.wrapperElement == null) {
        if (doc != null) doc += '<br>';
        doc = (doc ?? '') + '<b>Repeatable</b>';
    } else if (prop.type.$type == 'optional') {
        if (doc != null) doc += '<br>';
        doc = (doc ?? '') + '<b>Optional</b>';
    } else {
        if (doc != null) doc += '<br>';
        doc = (doc ?? '') + '<b>Required</b>';
    }
    return doc;
}

function generateObjectContents(ir: SchemaIr, builder: XmlBuilder, type: ObjectType) {
    const types = [type];
    while (types[types.length - 1].base != null) {
        types.push(constrainedTypeByName(ir, types[types.length - 1].base.name, 'object'));
    }
    types.reverse();

    for (const [name, attr] of types.flatMap(ty => Object.entries(ty.attributes))) {
        if (attr.sample == SampleOmit)
            continue;
        builder.writeAttribute(name, attr.sample ?? attr.default ?? generateAttributeContents(ir, attr.type), propertyDocumentation(attr));
    }

    if (type.content != null) {
        builder.writeContent(generatePrimitiveContents(type.content.type));
    }

    for (const [name, element] of types.flatMap(ty => Object.entries(ty.elements))) {
        if (element.sample == SampleOmit)
            continue;
        builder.startElement(name, propertyDocumentation(element));
        if (element.sample != null) {
            builder.writeContent(element.sample);
        }
        else if (element.default != null) {
            builder.writeContent(element.default);
        } else {
            generateTypeRefContents(ir, builder, element.type);
        }
        builder.closeElement();
    }
}

function generatePrimitiveContents(type: PrimitiveType): string {
    switch (type) {
        case "String":
            return 'abc';
        case "Boolean":
            return 'true';
        case "Double":
            return '123.456';
        case "Integer":
            return '123';
    }
}

function generateAttributeContents(ir: SchemaIr, typeRef: TypeRef): string {
    let itemType: ItemTypeRef;
    switch (typeRef.$type) {
        case "array":
        case "optional":
            itemType = typeRef.item;
            break;
        case 'custom':
        case 'primitive':
            itemType = typeRef;
            break;
    }
    switch (itemType.$type) {
        case "primitive":
            return generatePrimitiveContents(itemType.type);
        case "custom":
            const referenced = constrainedTypeByName(ir, itemType.name, 'pattern', 'enum');
            switch (referenced.$type) {
                case "pattern":
                    return generateAttributeContents(ir, referenced.type);
                case "enum":
                    const values = Object.keys(referenced.items);
                    return referenced.flags && values.length >= 2 ? values[0] + ' ' + values[1] : values[0];
            }
    }
}

function generateTypeRefContents(ir: SchemaIr, builder: XmlBuilder, typeRef: TypeRef) {
    let openedElements = 0;
    let itemType: ItemTypeRef;
    switch (typeRef.$type) {
        case "array":
            itemType = typeRef.item;
            if (typeRef.wrapperElement != null) {
                builder.startElement(typeRef.wrapperElement);
                openedElements++;
            }
            break;
        case "optional":
            itemType = typeRef.item;
            break;
        case 'custom':
        case 'primitive':
            itemType = typeRef;
            break;
    }
    switch (itemType.$type) {
        case "primitive":
            builder.writeContent(generatePrimitiveContents(itemType.type));
            break;
        case "custom":
            generateObjectContents(ir, builder, constrainedTypeByName(ir, itemType.name, 'object'));
            break;
    }

    for (let i = 0; i < openedElements; i++) {
        builder.closeElement();
    }
}