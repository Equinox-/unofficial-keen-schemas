import "ace-code/src/theme/monokai";
import { SchemaIr } from "./ir";
import { locationParameters } from "./util";
import { XmlBuilder } from "./xml";
import { generateExample } from "./generate";
import { AceTooltips } from "./tooltip";

(function () {
    const editorDom = document.createElement("editor");
    document.body.appendChild(editorDom);
    editorDom.style.position = 'absolute';
    editorDom.style.left = '0';
    editorDom.style.right = '0';
    editorDom.style.top = '0';
    editorDom.style.bottom = '0';

    const { schema, path } = locationParameters();
    if (schema?.length != 1 || !(path?.length >= 1)) {
        editorDom.innerText = 'Schema and path must be defined.';
        return;
    }
    const root = path[path.length - 1].split('@', 2);
    const stripPrefix = "MyObjectBuilder_";
    document.title = root.length == 2 ? root[1].startsWith(stripPrefix) ? root[1].substring(stripPrefix.length) : root[1] : root[0];

    import('ace-code').then(ace => {
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

        const schemaPath = "https://storage.googleapis.com/unofficial-keen-schemas/latest/" + schema[0];
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
    });
})();