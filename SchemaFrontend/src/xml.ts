import { Ace, Range } from "ace-code";
import { AceTooltips } from "./tooltip";

export interface XmlBuilderOptions {
    editor: Ace.Editor,
    tooltips?: AceTooltips,
    schema: string
}
const IndentChunk: string = '  ';
export const RecommendedLineWidth = 120;
type XmlBuilderState = 'Prologue' | 'ElementBody' | 'ElementHeader';

export class XmlBuilder {
    private options: XmlBuilderOptions;
    private state: XmlBuilderState;
    private stack: { tag: string, documentation?: string }[];

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
        this.stack = [];
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
        const ref = this.position;
        this.append('<' + tag);
        if (documentation != null) {
            this.tooltip(ref, 1, tag.length, documentation);
        }
        const wasPrologue = this.state == 'Prologue';
        this.stack.push({ tag, documentation });
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
            this.append('\n' + IndentChunk.repeat(this.stack.length + 2));
        } else {
            this.append(' ');
        }
        const ref = this.position;
        this.append(attrChunk);
        if (documentation != null) {
            this.tooltip(ref, 0, key.length, documentation);
        }
    }

    closeElement() {
        switch (this.state) {
            case 'ElementHeader':
                this.append(' />\n');
                this.stack.pop();
                break;
            case 'ElementBody':
                const { tag, documentation } = this.stack.pop();
                if (this.lineLength == 0)
                    this.appendIndent();
                const ref = this.position;
                this.append('</' + tag + '>\n');
                if (documentation != null) {
                    this.tooltip(ref, 2, tag.length, documentation);
                }
                break;
            default:
                throw new Error('Expected state in ElementHeader, ElementBody was ' + this.state);
        }
        this.state = this.stack.length == 0 ? 'Prologue' : 'ElementBody';
    }

    private appendIndent() {
        this.append(IndentChunk.repeat(this.stack.length));
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

    private tooltip(ref: Ace.Position, start: number, length: number, content: string) {
        const range = Range.fromPoints({ row: ref.row, column: ref.column + start }, { row: ref.row, column: ref.column + start + length });
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
