# XML Schemas for Keen Games

## Setup

- Determine what editor you want to use from the [Editors](#editors) section
- Determine what schema you want to use from the [Schemas](#schemas) section
- Add the URI of the schema file (ex `<Definitions xsi:noNamespaceSchemaLocation="SCHEMA_URI" ...`)
    - Determine which schema file to use by matching against what XSD Version your editor supports
    - If your editor **does** support https:// schemas right click the XSD link, pick "Copy Link"
    - If your editor **doesn't** support https:// schemas right click the XSD link, pick "Save Link As", save the schema to disk, and construct a file
      URI pointing to the schema (ex `file:///C:/Users/username/Downloads/medieval-vanilla.xsd`)
    - This URI, `file:///C:/.../medieval-vanila.xsd` or `https://storage.googleapis.com/.../medieval-vanilla.xsd`, is the SCHEMA_URI.

## Schemas

| Game               | Variant                                   | XSD 1.0 URL                                                                                   | XSD 1.1 URL                                                                                      |
|--------------------|-------------------------------------------|-----------------------------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------|
| Medieval Engineers | Vanilla                                   | [XSD 1.0](https://storage.googleapis.com/unofficial-keen-schemas/latest/medieval-vanilla.xsd) | [XSD 1.1](https://storage.googleapis.com/unofficial-keen-schemas/latest/medieval-vanilla.11.xsd) |
| Medieval Engineers | Equinox Core<br>Rails Core<br>PAX Scripts | [XSD 1.0](https://storage.googleapis.com/unofficial-keen-schemas/latest/medieval-modded.xsd)  | [XSD 1.1](https://storage.googleapis.com/unofficial-keen-schemas/latest/medieval-modded.11.xsd)  |
| Space Engineers    | Vanilla                                   | [XSD 1.0](https://storage.googleapis.com/unofficial-keen-schemas/latest/space-vanilla.xsd)    | [XSD 1.1](https://storage.googleapis.com/unofficial-keen-schemas/latest/space-vanilla.11.xsd)    |

## Editors

| Editor             | Requirements                                                                           | XSD Version | https:// Schemas |
|--------------------|----------------------------------------------------------------------------------------|-------------|------------------|
| Visual Studio Code | [XML Extension](https://marketplace.visualstudio.com/items?itemName=redhat.vscode-xml) | XSD 1.0     | ✅                |
| Rider & IntelliJ   | None                                                                                   | XSD 1.0     | ✅                |
| Visual Studio      | None                                                                                   | XSD 1.0     | ❌                |

