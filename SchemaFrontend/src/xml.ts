import { Ace, Range } from "ace-code";
import { AceTooltips } from "./tooltip";

export interface XmlBuilderOptions {
    editor: Ace.Editor,
    tooltips?: AceTooltips,
    schema: string
}
const IndentChunk: string = '  ';
const RecommendedLineWidth = 120;
type XmlBuilderState = 'Prologue' | 'ElementBody' | 'ElementHeader';

export class XmlBuilder {
    private options: XmlBuilderOptions;
    private state: XmlBuilderState;
    private path: string[];

    private get editor(): Ace.Editor {
        return this.options.editor;
    }

    private get session(): Ace.EditSession {
        return this.editor.session;
    }

    private get position(): Ace.Position {
        const row = this.session.getLength() - 1;
        const column = this.session.getLine(row).length;
        return this.session.doc.pos(row, column);
    }

    private get lineLength(): number {
        return this.position.column;
    }

    constructor(options: XmlBuilderOptions) {
        this.options = options;
        this.session.setValue("<?xml version='1.0' encoding='UTF-8'?>\n");
        this.state = 'Prologue';
        this.path = [];
    }

    openElement(tag: string, ...attributes: [string, string][]) {
        this.startElement(tag);
        for (const [key, value] of attributes) {
            this.writeAttribute(key, value);
        }
    }

    startElement(tag: string, documentation?: string) {
        this.assertState('ElementBody', 'ElementHeader', 'Prologue');
        if (this.state == 'ElementHeader') {
            this.append('>\n');
        } else if (this.state == 'ElementBody') {
            if (this.lineLength > 0)
                this.append('\n');
            this.state = 'ElementBody';
        }
        this.appendIndent();
        const start = this.position;
        this.append('<' + tag);
        if (documentation != null) {
            this.tooltip(start, 1 + tag.length, documentation);
        }
        const wasPrologue = this.state == 'Prologue';
        this.path.push(tag);
        this.state = 'ElementHeader';
        if (wasPrologue) {
            this.writeAttribute('xmlns:xsi', 'http://www.w3.org/2001/XMLSchema-instance');
            this.writeAttribute('xsi:noNamespaceSchemaLocation', this.options.schema);
        }
    }

    writeContent(value: string) {
        this.assertState('ElementHeader', 'ElementBody');
        if (this.state == 'ElementHeader') {
            this.append('>');
            this.state = 'ElementBody';
        }
        this.append(XmlBuilder.escape(value));
    }

    writeAttribute(key: string, value: string, documentation?: string) {
        this.assertState('ElementHeader');
        const attrChunk = key + '="' + XmlBuilder.escape(value) + '"';
        if (this.lineLength + 1 + attrChunk.length >= RecommendedLineWidth) {
            this.append('\n' + IndentChunk.repeat(this.path.length + 2));
        } else {
            this.append(' ');
        }
        const start = this.position;
        this.append(attrChunk);
        if (documentation != null) {
            this.tooltip(start, key.length, documentation);
        }
    }

    closeElement() {
        switch (this.state) {
            case 'ElementHeader':
                this.append(' />\n');
                this.path.pop();
                break;
            case 'ElementBody':
                const tag = this.path.pop();
                if (this.lineLength == 0)
                    this.appendIndent();
                this.append('</' + tag + '>\n');
                break;
            default:
                throw new Error('Expected state in ElementHeader, ElementBody was ' + this.state);
        }
        this.state = this.path.length == 0 ? 'Prologue' : 'ElementBody';
    }

    private appendIndent() {
        this.append(IndentChunk.repeat(this.path.length));
    }

    private append(content: string) {
        const row = this.session.getLength() - 1;
        const column = this.session.getLine(row).length;
        this.session.insert({ row, column }, content);
    }

    private assertState(...states: XmlBuilderState[]) {
        for (const okay of states) {
            if (okay == this.state) {
                return;
            }
        }
        throw new Error('Expected state in ' + states.join(', ') + ' was ' + this.state);
    }

    private tooltip(start: Ace.Position, length: number, content: string) {
        const range = Range.fromPoints(start, { row: start.row, column: start.column + length });
        this.options.tooltips?.addTooltip({
            range,
            content
        });
    }

    private static escape(value: string) {
        value = value.replace('&', '&amp;');
        value = value.replace('<', '&lt;');
        value = value.replace('>', '&gt;');
        value = value.replace('"', '&quot;');
        value = value.replace('\'', '&apos;');
        return value;
    }
}
