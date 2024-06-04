import * as ace from "ace-code";
import { AceTooltips } from "./tooltip";
import { XmlBuilder } from "./xml";
import { SchemaIr } from "./ir";
import { generateExample } from "./generate";

export function setupEditor(editorDom: HTMLElement, schema: string, path: string[]) {
    const editor = ace.edit(editorDom);
    editor.setReadOnly(true);
    const tooltips = new AceTooltips(editor);

    import('ace-code/src/mode/xml').then(xml => editor.session.setMode(new xml.Mode()));
    import('ace-code/src/theme/monokai').then(theme => {
        editor.setStyle(theme.cssClass);
        const styleRef = document.getElementById(theme.cssClass);
        // Move to end so the theme takes priority.
        styleRef.parentElement.appendChild(styleRef);
    });

    const schemaPath = "https://storage.googleapis.com/unofficial-keen-schemas/latest/" + schema;
    fetch(schemaPath + ".json")
        .then(response => response.json())
        .then(json => json as SchemaIr)
        .then(ir => {
            const builder = new XmlBuilder({ editor, tooltips, schema: schemaPath + ".xsd" });
            generateExample(ir, builder, path);
        })
        .catch(err => {
            console.warn("Failed to load schema IR for " + schema[0], err);
        });
}