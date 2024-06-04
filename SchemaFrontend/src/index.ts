import { locationParameters } from "./util";

(function () {
    const editorDom = document.createElement("editor");
    document.body.appendChild(editorDom);
    editorDom.style.position = 'absolute';
    editorDom.style.left = '0';
    editorDom.style.right = '0';
    editorDom.style.top = '0';
    editorDom.style.bottom = '0';

    const { schema, path, selection } = locationParameters();
    if (schema?.length != 1 || !(path?.length >= 1)) {
        editorDom.innerText = 'Schema and path must be defined.';
        return;
    }
    const root = path[path.length - 1].split('@', 2);
    const stripPrefix = "MyObjectBuilder_";
    document.title = root.length == 2 ? root[1].startsWith(stripPrefix) ? root[1].substring(stripPrefix.length) : root[1] : root[0];

    import('./editor').then(editor => editor.setupEditor(editorDom, schema[0], path, selection != null ? selection[0] : undefined));
})();