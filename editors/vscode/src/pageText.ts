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
import { TextBody, listEnter, onListItem, surroundPlan, surrounds, textBodies, textBodyAt } from './pageTextCore';

// The CARRYING-OUT half of the page text's editing aids (owner's decision 2026-11-13). What
// Enter and a surrounding character do, and what a page text IS, lives in pageTextCore.ts,
// which never sees an editor. This file hears the keys and applies the answer.
//
// ⚠️ Everything here is held to the BODY of a page text, and asks so synchronously, of the
// document's own lines, at the moment it acts: the context key the Enter binding waits on
// is set a round trip behind the caret, so the command asks again and hands an Enter that
// is not its own back to VS Code.

/** A document's page texts, read once for each version of it — and only when asked: a
 * keystroke in music that needs nothing from here reads no line. */
const cache = new Map<string, { version: number, bodies: TextBody[] }>();

function bodiesOf(doc: vscode.TextDocument): TextBody[] {
    const key = doc.uri.toString();
    const known = cache.get(key);
    if (known?.version === doc.version) { return known.bodies; }
    const bodies = textBodies(doc.lineCount, i => doc.lineAt(i).text);
    cache.set(key, { version: doc.version, bodies });
    return bodies;
}

/** Whether `line` is in the body of a page text, where nothing is Lily#: the music's typing
 * aids (smartTyping.ts) ask before they rewrite anything, and leave the key as typed. */
export function inPageText(doc: vscode.TextDocument, line: number): boolean {
    return textBodyAt(bodiesOf(doc), line) !== undefined;
}

/** `lilysharp.typingAids.enabled` switches these off with the music's (rule 31). */
let aidsEnabled = true;
const readAidsEnabled = () =>
    vscode.workspace.getConfiguration('lilysharp').get<boolean>('typingAids.enabled', true);

/** The last selection made inside a page text, for a character that replaces it: a change
 * event says how much went, never what (as smartTyping's rule 34 keeps one for the music). */
const SELECTION_LINES = 200;
let lastSelection: { key: string, lo: number, text: string } | undefined;

/** True while the surround's own follow-up edit is being applied. */
let applying = false;

export function registerPageText(context: vscode.ExtensionContext, log: (msg: string) => void) {
    aidsEnabled = readAidsEnabled();
    let wasOnListItem = false;
    const setOnListItem = (on: boolean) => {
        if (on === wasOnListItem) { return; }
        wasOnListItem = on;
        void vscode.commands.executeCommand('setContext', 'lilysharp.inTextList', on);
    };

    context.subscriptions.push(
        vscode.workspace.onDidChangeConfiguration(e => {
            if (e.affectsConfiguration('lilysharp.typingAids.enabled')) { aidsEnabled = readAidsEnabled(); }
        }),
        vscode.workspace.onDidCloseTextDocument(doc => cache.delete(doc.uri.toString())),
        vscode.window.onDidChangeTextEditorSelection(e => {
            const doc = e.textEditor.document;
            if (doc.languageId !== 'lilysharp' || e.textEditor !== vscode.window.activeTextEditor) { return; }
            const s = e.selections[0];
            // The line is asked first: only a caret on a line that reads as a list item has
            // the document's fences looked for at all.
            setOnListItem(e.selections.length === 1 && s.isEmpty
                && onListItem(doc.lineAt(s.active.line).text, s.active.character)
                && inPageText(doc, s.active.line));
            if (e.selections.length === 1 && !s.isEmpty && s.end.line - s.start.line <= SELECTION_LINES
                && inPageText(doc, s.start.line) && inPageText(doc, s.end.line)) {
                lastSelection = { key: doc.uri.toString(), lo: doc.offsetAt(s.start), text: doc.getText(s) };
            }
        }),
        vscode.window.onDidChangeActiveTextEditor(editor => {
            const doc = editor?.document;
            const at = editor?.selection;
            setOnListItem(doc?.languageId === 'lilysharp' && at !== undefined && at.isEmpty
                && editor!.selections.length === 1
                && onListItem(doc.lineAt(at.active.line).text, at.active.character)
                && inPageText(doc, at.active.line));
        }),
        // `*`, `_` or a backtick typed over a selection in a page text surrounds it. VS Code
        // has replaced the selection by the time this hears of it (the surrounding pairs are
        // the document's language's, and in music these three mean other things), so the
        // words are written back after the character, with the character again, in the
        // keystroke's own undo step.
        vscode.workspace.onDidChangeTextDocument(event => {
            const doc = event.document;
            if (doc.languageId !== 'lilysharp' || applying || !aidsEnabled) { return; }
            if (event.reason === vscode.TextDocumentChangeReason.Undo
                || event.reason === vscode.TextDocumentChangeReason.Redo) { return; }
            const change = event.contentChanges.length === 1 ? event.contentChanges[0] : undefined;
            if (!change || change.rangeLength === 0 || !surrounds(change.text)) { return; }
            const s = lastSelection;
            lastSelection = undefined;
            const editor = vscode.window.activeTextEditor;
            if (!editor || editor.document !== doc || editor.selections.length !== 1) { return; }
            if (s?.key !== doc.uri.toString() || s.lo !== change.rangeOffset || s.text.length !== change.rangeLength) { return; }
            // Asked of the document as it stands now: the character is on the selection's first
            // line, and a text's body is still a body with one line's words gone.
            if (!inPageText(doc, change.range.start.line)) { return; }
            const plan = surroundPlan(change.text, s.text);
            if (!plan) { return; }
            const at = doc.positionAt(change.rangeOffset + 1);
            applying = true;
            editor.edit(b => b.insert(at, plan.insert), { undoStopBefore: false, undoStopAfter: true })
                .then(ok => {
                    applying = false;
                    if (!ok) { return; }
                    editor.selection = new vscode.Selection(at, doc.positionAt(change.rangeOffset + 1 + plan.select));
                }, () => { applying = false; });
            log('pageText: ' + JSON.stringify(change.text) + ' typed over a selection -> surrounded');
        }),
        // Enter in a list of a page text. The binding's `when` holds it to a caret the
        // selection listener has seen on a list item in a text; what it finds here decides.
        vscode.commands.registerCommand('lilysharp.textEnter', () => {
            const editor = vscode.window.activeTextEditor;
            const plain = () => vscode.commands.executeCommand('type', { text: '\n' });
            if (!editor || editor.document.languageId !== 'lilysharp' || !aidsEnabled
                || editor.selections.length !== 1 || !editor.selection.isEmpty) { return plain(); }
            const doc = editor.document;
            const at = editor.selection.active;
            const body = textBodyAt(bodiesOf(doc), at.line);
            const plan = body ? listEnter(body, at.line, at.character, i => doc.lineAt(i).text) : null;
            if (!plan) { return plain(); }
            // One edit with the caret left at its end: a snippet with no tabstop ends there,
            // and keepWhitespace keeps the new line's indentation as written.
            log('pageText: ' + plan.what);
            return editor.insertSnippet(new vscode.SnippetString().appendText(plan.text),
                new vscode.Range(at.line, plan.from, at.line, plan.to),
                { undoStopBefore: true, undoStopAfter: true, keepWhitespace: true });
        }),
    );
}
