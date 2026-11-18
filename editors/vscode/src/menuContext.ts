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
import { onAngleChord, onShapeItem } from './shapeItemCore';
import { caretMenuContext, fileMenuContext, selectionSpansSections } from './menuContextCore';

/** A menu context key, set only when it changes (setContext is a round trip to the workbench). */
const menuContextNow = new Map<string, boolean>();
function setMenuContext(key: string, value: boolean): void {
    if (menuContextNow.get(key) === value) { return; }
    menuContextNow.set(key, value);
    void vscode.commands.executeCommand('setContext', key, value);
}

/**
 * The menus' context keys, kept as the caret moves and the file is edited: what the caret is
 * on (a chord(...) or diagram(...) item, a <...> chord, a bar line, a section's head) and
 * what the file holds for the Clean Up menu.
 */
export function registerMenuContexts(context: vscode.ExtensionContext): void {
    // "Convert to Chord Notes" stands in the context menu only on a chord(…) / diagram(…) item,
    // "Add Guitar Diagram" only on a <…> chord.
    let wasOnShapeItem = false, wasOnAngleChord = false;
    context.subscriptions.push(
        vscode.window.onDidChangeTextEditorSelection(event => {
            const doc = event.textEditor.document;
            const at = event.selections[0]?.active;
            const line = doc.languageId === 'lilysharp' && at !== undefined ? doc.lineAt(at.line).text : undefined;
            const onItem = line !== undefined && onShapeItem(line, at!.character);
            const onChord = line !== undefined && !onItem && onAngleChord(line, at!.character);
            if (onItem !== wasOnShapeItem) {
                wasOnShapeItem = onItem;
                void vscode.commands.executeCommand('setContext', 'lilysharp.onShapeItem', onItem);
            }
            if (onChord !== wasOnAngleChord) {
                wasOnAngleChord = onChord;
                void vscode.commands.executeCommand('setContext', 'lilysharp.onAngleChord', onChord);
            }
            // The menus' caret context (owner's decision 2026-11-17, menuContextCore): a bar line or a
            // break (Split Section Here), a section's head or a selection across heads (Join Sections).
            const sel = event.selections[0];
            const where = line !== undefined ? caretMenuContext(line, at!.character) : { onBarLine: false, onSectionHead: false };
            const spans = line !== undefined && sel !== undefined && !sel.isEmpty
                && selectionSpansSections(doc.getText(), doc.offsetAt(sel.start), doc.offsetAt(sel.end));
            setMenuContext('lilysharp.onBarLine', where.onBarLine && (sel?.isEmpty ?? true));
            setMenuContext('lilysharp.canJoinSections', (where.onSectionHead && (sel?.isEmpty ?? true)) || spans);
        }));
    // What the file holds for the Clean Up menu (owner's decision 2026-11-17): read when the active
    // editor changes and as it is edited, a moment after the last keystroke.
    let fileContextTimer: ReturnType<typeof setTimeout> | undefined;
    const updateFileContext = () => {
        const doc = vscode.window.activeTextEditor?.document;
        const marks = doc && doc.languageId === 'lilysharp' ? fileMenuContext(doc.getText())
            : { hasOrMarks: false, hasTodos: false, hasStringNumbers: false };
        setMenuContext('lilysharp.hasOrMarks', marks.hasOrMarks);
        setMenuContext('lilysharp.hasTodos', marks.hasTodos);
        setMenuContext('lilysharp.hasStringNumbers', marks.hasStringNumbers);
    };
    updateFileContext();
    context.subscriptions.push(
        vscode.window.onDidChangeActiveTextEditor(() => updateFileContext()),
        vscode.workspace.onDidChangeTextDocument(e => {
            if (e.document !== vscode.window.activeTextEditor?.document) { return; }
            if (fileContextTimer) { clearTimeout(fileContextTimer); }
            fileContextTimer = setTimeout(updateFileContext, 400);
        }));
}
