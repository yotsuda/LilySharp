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
    );
}
