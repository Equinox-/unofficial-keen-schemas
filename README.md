# XML Schemas for Keen Games

## How to Use

- Use an XML editor that is schema-aware (Visual Studio, Rider, IntelliJ, etc)
- Determine what schema you need to use, then copy the link to the schema.  Currently supported schemas are:
  - [Medieval Engineers Vanilla](https://storage.googleapis.com/unofficial-keen-schemas/latest/medieval-vanilla.xsd)
  - [Space Engineers Vanilla](https://storage.googleapis.com/unofficial-keen-schemas/latest/space-vanilla.xsd)
- Update the XML root of your definitions to point to the schema: `<Definitions xmlns="https://storage.googleapis.com/unofficial-keen-schemas/latest/medieval-vanilla.xsd" ...>`
  - Don't remove the `xmlns:xsi` attribute
  - Some XML editors also require you to specify the schema location explicitly `xsi:schemaLocation="https://storage.googleapis.com/unofficial-keen-schemas/latest/medieval-vanilla.xsd https://storage.googleapis.com/unofficial-keen-schemas/latest/medieval-vanilla.xsd"`
- Instruct the IDE to download the schema file
  - For JetBrains IDEs, click the link, then press Alt+Enter, Fetch External Resource
  - For Visual Studio Code install the [XML Extension](https://marketplace.visualstudio.com/items?itemName=redhat.vscode-xml) 
