import * as ace from "ace-code";
import * as ace_modelist from "ace-code/src/ext/modelist";
import * as ace_themelist from "ace-code/src/ext/themelist";
import { AceTooltips } from "./tooltip";
import { XmlBuilder, RecommendedLineWidth } from "./xml";
import { SchemaIr } from "./ir";
import { generateExample } from "./generate";
import { setLocationParameter } from "./util";

export function setupEditor(editorDom: HTMLElement, schema: string, path: string[], selection?: string) {
    ace.config.setModuleLoader('ace/ext/searchbox', () => import('ace-code/src/ext/searchbox'));
    ace.config.setModuleLoader('ace/ext/settings_menu', () => import('ace-code/src/ext/settings_menu'));
    ace.config.setModuleLoader('ace/ext/error_marker', () => import('ace-code/src/ext/error_marker'));
    ace.config.setModuleLoader('ace/ext/prompt', () => import('ace-code/src/ext/prompt'));
    ace.config.setModuleLoader('ace/mode/xml', () => import('ace-code/src/mode/xml'));
    ace.config.setModuleLoader('ace/theme/monokai', () => import('ace-code/src/theme/monokai'));
    ace.config.setModuleLoader('ace/theme/textmate', () => import('ace-code/src/theme/textmate'));
    ace.config.setModuleLoader('ace/keyboard/vscode', () => import('ace-code/src/keyboard/vscode'));
    ace.config.setModuleLoader('ace/keyboard/sublime', () => import('ace-code/src/keyboard/sublime'));
    ace.config.setModuleLoader('ace/keyboard/emacs', () => import('ace-code/src/keyboard/emacs'));
    ace.config.setModuleLoader('ace/keyboard/vim', () => import('ace-code/src/keyboard/vim'));

    ace.config.setLoader((module) => {
        throw new Error('refusing to lazy load module that was not registered: ' + module)
    });

    const editor = ace.edit(editorDom);
    editor.setReadOnly(true);
    editor.setPrintMarginColumn(RecommendedLineWidth);
    const tooltips = new AceTooltips(editor);

    editor.session.setMode('ace/mode/xml');
    editor.setTheme('ace/theme/monokai');
    ace_modelist.modes.splice(0, ace_modelist.modes.length, ace_modelist.modesByName['xml']);
    ace_themelist.themes.splice(0, ace_themelist.themes.length, ace_themelist.themesByName['monokai'], ace_themelist.themesByName['textmate']);

    const initialSelection = selection;
    editor.session.selection.on('changeSelection', function () {
        setLocationParameter('selection', editor.selection.isEmpty() ? [] : [btoa(JSON.stringify(editor.selection.toJSON()))]);
    });

    const schemaPath = "https://storage.googleapis.com/unofficial-keen-schemas/latest/" + schema;
    fetch(schemaPath + ".json")
        .then(response => response.json())
        .then(json => json as SchemaIr)
        .then(ir => {
            const builder = new XmlBuilder({ editor, tooltips, schema: schemaPath + ".xsd" });
            generateExample(ir, builder, path);
            if (initialSelection != null) {
                editor.selection.fromJSON(JSON.parse(atob(initialSelection)));
            }
        })
        .catch(err => {
            console.warn("Failed to load schema IR for " + schema[0], err);
        });
}