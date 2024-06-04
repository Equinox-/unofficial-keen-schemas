(function () {
    const editorDom = document.createElement("editor");
    document.body.appendChild(editorDom);
    editorDom.style.position = 'absolute';
    editorDom.style.inset = '0';

    import('./editor').then(editor => editor.setupEditor(editorDom));
})();