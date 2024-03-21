# XML Schemas for Keen Games

## How to Use

- Use an XML editor that is schema-aware (Visual Studio, Rider, IntelliJ, etc)
- Determine what schema you need to use.  Currently supported schemas are:
  - [Medieval Engineers Vanilla](https://storage.googleapis.com/unofficial-keen-schemas/latest/medieval-vanilla.xsd)
  - [Space Engineers Vanilla](https://storage.googleapis.com/unofficial-keen-schemas/latest/space-vanilla.xsd)
- Update the XML root of your definitions to point to the schema: `<Definitions xmlns="https://storage.googleapis.com/unofficial-keen-schemas/latest/medieval-vanilla.xsd" ...>`
- Instruct the IDE to download the schema file (click the link, then press Alt+Enter, Fetch External Resource for JetBrains IDEs)
