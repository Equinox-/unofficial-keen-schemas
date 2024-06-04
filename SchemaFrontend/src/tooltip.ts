import { Ace } from "ace-code";
import { HoverTooltip } from "ace-code/src/tooltip";

export interface AceTooltip {
    range: Ace.Range,
    content: string
}
export class AceTooltips {
    private tooltips: AceTooltip[];

    constructor(editor: Ace.Editor) {
        this.tooltips = [];
        const driver = new HoverTooltip(editor.container);
        driver.setDataProvider((e, _editor) => {
            const tooltip = this.findTooltip(e.getDocumentPosition());
            if (tooltip) {
                const dom = document.createElement('div');
                dom.innerHTML = tooltip.content;
                driver.showForRange(editor, tooltip.range, dom, e);
            } else {
                driver.hide(e);
            }
        });
        driver.addToEditor(editor);
    }

    findTooltip(pos: Ace.Position): AceTooltip | undefined {
        for (const tooltip of this.tooltips) {
            if (tooltip.range.contains(pos.row, pos.column)) {
                return tooltip;
            }
        }
        return null;
    }

    addTooltip(tooltip: AceTooltip) {
        this.tooltips.push(tooltip);
    }
}