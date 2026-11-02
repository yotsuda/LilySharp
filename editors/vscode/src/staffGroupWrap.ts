// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

import * as vscode from 'vscode';
import { STAFF_GROUPS, staffGroupWrap } from './staffGroupWrapCore';

// The CARRYING-OUT half (staffGroupWrapCore.ts decides). A language server is never told of a
// selection, so this is the extension's own completion provider. VS Code replaces the whole
// selection with a picked item, whatever the item's range, and filters on the word in that
// range: an empty range keeps these in the list when the caret stands after `melody`.
//
// Cost: asked on every completion request, it leaves on an empty selection before reading
// anything, and a selection can only be made and then completed with Ctrl+Space, a typed
// key replacing it.

/** The selection in `document`, when an editor holds exactly one and it is not empty. */
function selectionIn(document: vscode.TextDocument) {
    const editor = vscode.window.activeTextEditor;
    return editor && editor.document === document && editor.selections.length === 1 && !editor.selection.isEmpty
        ? editor : undefined;
}

/** Whether the list is these groups alone (grandStaff takes every row any of them does, so
 * it answers for all): the language server's items are for a caret and
 * know nothing of the selection, and its `melody` after `staff melody|`, matching the word
 * there, would stand above grandStaff. Its answer is not asked for. */
export function staffGroupsOffered(document: vscode.TextDocument): boolean {
    const editor = selectionIn(document);
    return !!editor && staffGroupWrap(document.getText(), document.offsetAt(editor.selection.start),
        document.offsetAt(editor.selection.end), STAFF_GROUPS[0], '') !== null;
}

export function registerStaffGroupWrap(context: vscode.ExtensionContext) {
    context.subscriptions.push(vscode.languages.registerCompletionItemProvider({ language: 'lilysharp' }, {
        provideCompletionItems(document, position) {
            const editor = selectionIn(document);
            if (!editor) { return undefined; }
            const text = document.getText();
            const lo = document.offsetAt(editor.selection.start);
            const hi = document.offsetAt(editor.selection.end);
            const { insertSpaces, tabSize } = editor.options;
            const tab = insertSpaces === false ? '\t' : ' '.repeat(typeof tabSize === 'number' ? tabSize : 2);
            const items: vscode.CompletionItem[] = [];
            STAFF_GROUPS.forEach((group, i) => {
                const wrap = staffGroupWrap(text, lo, hi, group, tab);
                if (!wrap) { return; }
                const item = new vscode.CompletionItem(group, vscode.CompletionItemKind.Snippet);
                item.detail = 'Wrap the selected staves';
                item.insertText = new vscode.SnippetString().appendText(wrap.head).appendTabstop(0).appendText(wrap.tail);
                item.keepWhitespace = true;
                item.range = new vscode.Range(position, position);
                item.sortText = `!${i}`;
                item.preselect = i === 0;
                items.push(item);
            });
            return items.length > 0 ? items : undefined;
        },
    }));
}
