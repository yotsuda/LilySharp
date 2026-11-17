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

// The caret's status line (owner's request 2026-10-24): while the caret rests on a setting —
// a clef, a key, an octave, a transposition, a tuning, a midiInstrument, an instrument preset,
// a staff group, a bar line, a page size — the status bar says in one line what it does. In
// the status bar, not a hover: a hover covers the text being edited. WHAT the line says is the
// server's (lilysharp/caretInfo — LilySharp.Core's NoteStepper.Describe).

import * as vscode from 'vscode';
import type { LanguageClient } from 'vscode-languageclient/node';
import { CARET_DEBOUNCE_MS, Debouncer } from './auditionCore';
import { soundingWait } from './playbackStart';

export interface CaretInfoDeps {
    getClient: () => LanguageClient | undefined;
    isReady: () => boolean;
}

interface CaretInfoResponse {
    Text?: string | null;
    Version: number;
}

export function registerCaretInfo(context: vscode.ExtensionContext, deps: CaretInfoDeps) {
    const item = vscode.window.createStatusBarItem('lilysharp.caretInfo', vscode.StatusBarAlignment.Left, 0);
    item.name = 'Lily# caret info';
    const wait = new Debouncer(CARET_DEBOUNCE_MS);
    // When each document was last edited. After an edit the line is asked for once the text has
    // rested (soundingWait, the header's notes' rule): on a note the server's answer is an export
    // and two collects of the whole score, and asked 80 ms after every keystroke it ran right
    // before that keystroke's render — MEASURED (session 1291, 59 books × 8 keystrokes): 12.8 ms
    // beside a render of 5.3. A caret moved with no edit is asked as before.
    const lastEdit = new Map<string, number>();

    const update = (editor: vscode.TextEditor | undefined) => {
        if (!editor || editor.document.languageId !== 'lilysharp' || !editor.selection.isEmpty) {
            wait.cancel();
            item.hide();
            return;
        }
        wait.poke(async () => {
            const client = deps.getClient();
            if (!client || !deps.isReady()) { item.hide(); return; }
            const doc = editor.document;
            const version = doc.version;
            const offset = doc.offsetAt(editor.selection.active);
            const uri = doc.uri.toString();
            for (let rest = soundingWait(lastEdit.get(uri), Date.now()); rest > 0;
                rest = soundingWait(lastEdit.get(uri), Date.now())) {
                await new Promise(r => setTimeout(r, rest));
            }
            // Edited or moved while it waited: that change asks for itself.
            if (vscode.window.activeTextEditor !== editor || doc.version !== version
                || doc.offsetAt(editor.selection.active) !== offset) { return; }
            let answer: CaretInfoResponse;
            try {
                answer = await client.sendRequest<CaretInfoResponse>('lilysharp/caretInfo', {
                    TextDocument: { uri: doc.uri.toString() }, Offset: offset,
                });
            } catch {
                item.hide();
                return;
            }
            // Moved on while the server thought: the answer is about another caret or text.
            if (vscode.window.activeTextEditor !== editor || doc.version !== version
                || answer.Version !== version || doc.offsetAt(editor.selection.active) !== offset) { return; }
            if (answer.Text) {
                item.text = answer.Text;
                item.tooltip = answer.Text;
                item.show();
            } else {
                item.hide();
            }
        });
    };

    context.subscriptions.push(
        item,
        vscode.window.onDidChangeTextEditorSelection(event => update(event.textEditor)),
        vscode.window.onDidChangeActiveTextEditor(editor => update(editor)),
        vscode.workspace.onDidChangeTextDocument(event => {
            if (event.document.languageId === 'lilysharp' && event.contentChanges.length > 0) {
                lastEdit.set(event.document.uri.toString(), Date.now());
            }
        }),
        vscode.workspace.onDidCloseTextDocument(doc => lastEdit.delete(doc.uri.toString())),
    );
}
