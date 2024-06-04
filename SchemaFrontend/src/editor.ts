import * as ace from "ace-code";
import * as ace_modelist from "ace-code/src/ext/modelist";
import * as ace_themelist from "ace-code/src/ext/themelist";
import { AceTooltips } from "./tooltip";
import { XmlBuilder, RecommendedLineWidth } from "./xml";
import { SchemaIr } from "./ir";
import { generateExample } from "./generate";
import { locationParameters, setLocationParameter } from "./util";

export function decorateXmlFolding(module: { Mode: new () => ace.Ace.SyntaxMode }): { Mode: new () => ace.Ace.SyntaxMode } {
    class Mode extends module.Mode {
        constructor() {
            super();
            const fold = this.foldingRules;
            const foldWidget = fold.getFoldWidget;
            const foldRange = fold.getFoldWidgetRange;
            const foldWidgetStart = /^\s*<!--\s*#region\b/
            const foldWidgetContinue = /^\s*<!--\s*#(end)?region\b/

            fold.getFoldWidget = function (
                session: ace.Ace.EditSession, foldStyle: string, row: number) {
                const original = foldWidget.call(this, session, foldStyle, row);
                if (original) return original;
                const line = session.getLine(row);
                if (foldWidgetStart.test(line))
                    return "start";
                return undefined;
            };
            fold.getFoldWidgetRange = function (
                session: ace.Ace.EditSession, foldStyle: string, row: number, forceMultiline?: boolean) {
                const original = foldRange.call(this, session, foldStyle, row, forceMultiline);
                if (original) return original;

                let line = session.getLine(row);
                if (!foldWidgetStart.test(line))
                    return undefined;

                var startColumn = line.search(/\s*$/);
                var maxRow = session.getLength();
                var startRow = row;
                var depth = 1;
                while (++row < maxRow) {
                    line = session.getLine(row);
                    var m = foldWidgetContinue.exec(line);
                    if (!m)
                        continue;
                    if (m[1])
                        depth--;
                    else
                        depth++;

                    if (!depth)
                        break;
                }

                var endRow = row;
                if (endRow > startRow) {
                    return new ace.Range(startRow, startColumn, endRow, line.length);
                }
                return undefined;
            };
        }
    }
    return { Mode: Mode };
}

function editorParameters(): { schema?: string, path?: string[] } {
    const params = locationParameters();
    return { schema: params.schema?.length == 1 ? params.schema[0] : null, path: params.path };
}

export function setupEditor(editorDom: HTMLElement) {
    ace.config.setModuleLoader('ace/ext/searchbox', () => import('ace-code/src/ext/searchbox'));
    ace.config.setModuleLoader('ace/ext/settings_menu', () => import('ace-code/src/ext/settings_menu'));
    ace.config.setModuleLoader('ace/ext/error_marker', () => import('ace-code/src/ext/error_marker'));
    ace.config.setModuleLoader('ace/ext/prompt', () => import('ace-code/src/ext/prompt'));
    ace.config.setModuleLoader('ace/mode/xml', () => import('ace-code/src/mode/xml').then(decorateXmlFolding));
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

    let initialSelection = locationParameters().selection;

    editor.session.selection.on('changeSelection', function () {
        setLocationParameter('selection', editor.selection.isEmpty() ? [] : [btoa(JSON.stringify(editor.selection.toJSON()))]);
    });

    let parameters: ReturnType<typeof editorParameters> = {};
    let schemaPromise: Promise<SchemaIr> | undefined = null;
    function commitParameters(newParameters: ReturnType<typeof editorParameters>): void {
        const schema = newParameters?.schema;
        const path = newParameters?.path;
        if (schema != null) {
            const schemaPath = "https://storage.googleapis.com/unofficial-keen-schemas/latest/" + schema;
            const schemaChanged = parameters.schema != schema;
            if (schemaChanged)
                schemaPromise = fetch(schemaPath + ".json").then(response => response.json()).then(json => json as SchemaIr);

            if (path?.length >= 1 && (parameters.path?.join("->") != path.join("->") || schemaChanged)) {
                schemaPromise.then(ir => {
                    const root = path[path.length - 1].split('@', 2);
                    const stripPrefix = "MyObjectBuilder_";
                    document.title = root.length == 2 ? root[1].startsWith(stripPrefix) ? root[1].substring(stripPrefix.length) : root[1] : root[0];
                    const builder = new XmlBuilder({ editor, tooltips, schema: schemaPath + ".xsd" });
                    generateExample(ir, builder, path);
                    if (initialSelection?.length == 1) {
                        editor.selection.fromJSON(JSON.parse(atob(initialSelection[0])));
                        initialSelection = null;
                    } else {
                        setLocationParameter('selection', []);
                    }
                }).catch(err => {
                    tooltips.clearTooltips();
                    console.warn('Failed to load schema IR', err);
                    editor.setValue('Failed to load schema IR:\n' + err);
                });
            } else {
                tooltips.clearTooltips();
                editor.setValue('Set a path parameter');
            }
        } else {
            tooltips.clearTooltips();
            editor.setValue('Set a schema parameter');
        }
        parameters = newParameters;
    }
    window.addEventListener('hashchange', () => commitParameters(editorParameters()));
    commitParameters(editorParameters());
}