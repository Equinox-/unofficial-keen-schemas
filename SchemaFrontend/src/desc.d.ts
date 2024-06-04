declare module "ace-code/src/theme/monokai" {
    export const cssClass: string;
}

declare module "ace-code/src/tooltip" {
    import { Ace } from "ace-code";

    export interface AceMouseEvent {
        getDocumentPosition(): Ace.Position;
    }

    export class HoverTooltip {
        constructor(parentNode: HTMLElement);
        addToEditor(editor: Ace.Editor): void;
        removeFromEditor(editor: Ace.Editor): void;
        setDataProvider(value: (e: AceMouseEvent, editor: Ace.Editor) => void): void;
        showForRange(editor: Ace.Editor, range: Ace.Range, domNode: HTMLElement, startingEvent: AceMouseEvent): void;
        hide(startingEvent: AceMouseEvent): void;
    }
}