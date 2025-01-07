# XML Schemas for Keen Games

## Setup

- Determine what editor you want to use from the [Editors](#editors) section
- Determine what schema you want to use from the [Schemas](#schemas) section
- Add the URI of the schema file (ex `<Definitions xsi:noNamespaceSchemaLocation="SCHEMA_URI" ...`)
    - Determine which schema file to use by matching against what XSD Version your editor supports
    - If your editor **does** support https:// schemas right-click the XSD link, pick "Copy Link"
    - If your editor **doesn't** support https:// schemas right-click the XSD link, pick "Save Link As", save the schema to disk, and construct a file
      URI pointing to the schema (ex `file:///C:/Users/username/Downloads/medieval-vanilla.xsd`)
    - This URI, `file:///C:/.../medieval-vanila.xsd` or `https://storage.googleapis.com/.../medieval-vanilla.xsd`, is the SCHEMA_URI.

## Schemas

| Game               | Variant                                   | XSD 1.0 URL                                                                                   | XSD 1.1 URL                                                                                      |
|--------------------|-------------------------------------------|-----------------------------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------|
| Medieval Engineers | Vanilla                                   | [XSD 1.0](https://storage.googleapis.com/unofficial-keen-schemas/latest/medieval-vanilla.xsd) | [XSD 1.1](https://storage.googleapis.com/unofficial-keen-schemas/latest/medieval-vanilla.11.xsd) |
| Medieval Engineers | Equinox Core<br>Rails Core<br>PAX Scripts | [XSD 1.0](https://storage.googleapis.com/unofficial-keen-schemas/latest/medieval-modded.xsd)  | [XSD 1.1](https://storage.googleapis.com/unofficial-keen-schemas/latest/medieval-modded.11.xsd)  |
| Space Engineers    | Vanilla                                   | [XSD 1.0](https://storage.googleapis.com/unofficial-keen-schemas/latest/space-vanilla.xsd)    | [XSD 1.1](https://storage.googleapis.com/unofficial-keen-schemas/latest/space-vanilla.11.xsd)    |

## Editors

| Editor             | Requirements                                  | XSD Version | https:// Schemas |
|--------------------|-----------------------------------------------|-------------|------------------|
| Visual Studio Code | See [Instructions](#visual-studio-code-setup) | XSD 1.0     | ✅                |
| Rider & IntelliJ   | None                                          | XSD 1.0     | ✅                |
| Visual Studio      | None                                          | XSD 1.0     | ❌                |

### Visual Studio Code Setup
- Install the [XML Extension](https://marketplace.visualstudio.com/items?itemName=redhat.vscode-xml)
- Open a `.sbc` file that has been [set up](#setup).
- Open the command palette, (Ctrl-Shift-P by default) and run "Revalidate all opened XML files". Remember this command,
  as it is also used to update the schema files from the above links.
- Check that the setup works correctly by hovering the `<TypeId>` element or the "Type" attribute of `<Id Type="..." />` element.
  If everything is working, documentation should show up when hovering.
