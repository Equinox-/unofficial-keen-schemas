declare module "ace-code/src/theme/monokai" {}
declare module "ace-code/src/theme/textmate" {}

declare module "ace-code/src/ext/searchbox" { }
declare module "ace-code/src/ext/settings_menu" { }
declare module "ace-code/src/ext/error_marker" { }
declare module "ace-code/src/ext/prompt" { }

declare module "ace-code/src/incremental_search" { }

declare module "ace-code/src/keyboard/vscode" { }
declare module "ace-code/src/keyboard/sublime" { }
declare module "ace-code/src/keyboard/emacs" { }
declare module "ace-code/src/keyboard/vim" { }

declare module "ace-code/src/ext/themelist" {
    export interface Theme {
        name: string;
        theme: string;
        capation: string;
    }
    export const themes: Theme[];
    export const themesByName: Record<string, Theme>;
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