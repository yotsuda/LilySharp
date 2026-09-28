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

// The step keys (Ctrl+Alt+Up / Ctrl+Alt+Down) and the automatic audition — the
// half that talks to VS Code. Owner's decision, 2026-09-28.
//
// WHAT a step does and WHAT sounds are the server's (lilysharp/step,
// lilysharp/auditionAt — LilySharp.Core's NoteStepper): it has the tree, the
// voicings and the pitches the compiler plays. This file applies the edits, falls
// back to the key's own command, and forwards pitches to the preview webview, which
// is the synth (as for Alt+P). WHEN something sounds is auditionCore.ts.
//
// ⚠️ The automatic auditions never open a preview: with none open they are silent.
// The step keys do not open one either. Alt+P / Alt+M keep their own behaviour.

import * as vscode from 'vscode';
import type { LanguageClient } from 'vscode-languageclient/node';
import {
    AuditionMemory, CARET_DEBOUNCE_MS, Debouncer, TYPING_SETTLE_MS,
    auditionDurationMs, caretMoveMaySound, mayChangeATypedPitch,
} from './auditionCore';

export interface StepAuditionDeps {
    getClient: () => LanguageClient | undefined;
    isReady: () => boolean;
    /** The open preview of a document, or undefined (then nothing sounds). */
    getPreview: (uri: string) => vscode.WebviewPanel | undefined;
    log: (msg: string) => void;
}

interface StepResponse {
    Fallback: boolean;
    Edits: { Start: number; End: number; NewText: string }[];
    Message?: string | null;
    Pitches: number[];
    Timbre: number;
    Version: number;
    Error?: string | null;
}

interface AuditionAtResponse {
    Key: number;
    Kind?: string | null;
    Pitches: number[];
    Timbre: number;
    Version: number;
}

/** lilysharp.audition.enabled: gates the three automatic auditions and the step's. */
function auditionEnabled(): boolean {
    return vscode.workspace.getConfiguration('lilysharp').get<boolean>('audition.enabled', true);
}

export function registerStepAudition(context: vscode.ExtensionContext, deps: StepAuditionDeps) {
    const memory = new AuditionMemory();
    const caretWait = new Debouncer(CARET_DEBOUNCE_MS);
    const typingWait = new Debouncer(TYPING_SETTLE_MS);
    // When each document last changed — a selection change right after one is the
    // edit's own caret movement, not a move to audition.
    const lastEdit = new Map<string, number>();

    /** Sends pitches to the document's preview synth; silent without a preview. */
    const play = (uri: string, pitches: readonly number[], timbre: number) => {
        const panel = deps.getPreview(uri);
        if (!panel || pitches.length === 0) { return; }
        void panel.webview.postMessage({
            type: 'playPitches', pitches, timbre, durationMs: auditionDurationMs(pitches.length),
        });
    };

    /** Asks the server what the caret of `editor` is on, if the document is still at
     * the version the question was asked about when the answer comes back. */
    const askAt = async (editor: vscode.TextEditor): Promise<AuditionAtResponse | undefined> => {
        const client = deps.getClient();
        if (!client || !deps.isReady()) { return undefined; }
        const doc = editor.document;
        const version = doc.version;
        const offset = doc.offsetAt(editor.selection.active);
        try {
            const answer = await client.sendRequest<AuditionAtResponse>('lilysharp/auditionAt', {
                TextDocument: { uri: doc.uri.toString() }, Offset: offset,
            });
            // Moved on while the server thought: the answer is about a caret or a
            // text that no longer exists.
            if (doc.version !== version || answer.Version !== version
                || doc.offsetAt(editor.selection.active) !== offset) { return undefined; }
            return answer;
        } catch {
            return undefined;
        }
    };

    // ---- the step keys ----

    // One step at a time: a held key repeats the command faster than the server
    // answers, and each step must see the text the previous one left.
    let stepChain: Promise<void> = Promise.resolve();
    const step = (direction: 1 | -1) => {
        stepChain = stepChain.then(() => runStep(direction)).catch(err => deps.log(`step: ${err}`));
        return stepChain;
    };

    const fallback = (direction: 1 | -1) => vscode.commands.executeCommand(
        direction > 0 ? 'editor.action.insertCursorAbove' : 'editor.action.insertCursorBelow');

    const runStep = async (direction: 1 | -1) => {
        const editor = vscode.window.activeTextEditor;
        const client = deps.getClient();
        if (!editor || editor.document.languageId !== 'lilysharp' || !client || !deps.isReady()) {
            await fallback(direction);
            return;
        }
        const doc = editor.document;
        const version = doc.version;
        const response = await client.sendRequest<StepResponse>('lilysharp/step', {
            TextDocument: { uri: doc.uri.toString() },
            Selections: editor.selections.map(s => ({ Start: doc.offsetAt(s.start), End: doc.offsetAt(s.end) })),
            Direction: direction,
        });
        if (response.Fallback) {
            if (response.Error) { deps.log(`step: ${response.Error}`); }
            await fallback(direction);
            return;
        }
        // Typed into while the server thought: the offsets are about another text.
        if (doc.version !== version || response.Version !== version) { return; }
        if (response.Edits.length > 0) {
            // ONE undo step for every cursor's edit.
            const applied = await editor.edit(b => {
                for (const e of response.Edits) {
                    b.replace(new vscode.Range(doc.positionAt(e.Start), doc.positionAt(e.End)), e.NewText);
                }
            }, { undoStopBefore: true, undoStopAfter: true });
            if (!applied) { return; }
        }
        if (response.Message) {
            vscode.window.setStatusBarMessage(`Lily#: ${response.Message}`, 4000);
        }
        if (auditionEnabled() && response.Pitches.length > 0) {
            const uri = doc.uri.toString();
            play(uri, response.Pitches, response.Timbre);
            // The caret now rests on what just sounded: remember it, so the caret
            // audition does not repeat it. The key is asked for, not guessed.
            const here = await askAt(editor);
            if (here) { memory.record(uri, { key: here.Key, pitches: here.Pitches }); }
        }
    };

    context.subscriptions.push(
        vscode.commands.registerCommand('lilysharp.stepUp', () => step(1)),
        vscode.commands.registerCommand('lilysharp.stepDown', () => step(-1)),
    );

    // ---- the automatic auditions ----

    context.subscriptions.push(
        vscode.workspace.onDidChangeTextDocument(event => {
            const doc = event.document;
            if (doc.languageId !== 'lilysharp' || event.contentChanges.length === 0) { return; }
            const uri = doc.uri.toString();
            lastEdit.set(uri, Date.now());
            // An edit makes a waiting caret question moot.
            caretWait.cancel();
            if (event.reason === vscode.TextDocumentChangeReason.Undo
                || event.reason === vscode.TextDocumentChangeReason.Redo) { return; }
            if (!auditionEnabled() || !deps.getPreview(uri)) { return; }
            const editor = vscode.window.activeTextEditor;
            if (!editor || editor.document !== doc) { return; }
            if (!mayChangeATypedPitch(event.contentChanges)) {
                // A duration digit or a space after the note: nothing new to hear,
                // but a note still settling keeps settling.
                return;
            }
            typingWait.poke(async () => {
                if (vscode.window.activeTextEditor !== editor) { return; }
                const answer = await askAt(editor);
                if (answer && memory.typed(uri, { key: answer.Key, pitches: answer.Pitches })) {
                    play(uri, answer.Pitches, answer.Timbre);
                }
            });
        }),

        vscode.window.onDidChangeTextEditorSelection(event => {
            const editor = event.textEditor;
            const doc = editor.document;
            if (doc.languageId !== 'lilysharp') { return; }
            const uri = doc.uri.toString();
            const facts = {
                allEmpty: event.selections.every(s => s.isEmpty),
                msSinceEdit: Date.now() - (lastEdit.get(uri) ?? 0),
            };
            if (!caretMoveMaySound(facts)) {
                caretWait.cancel();
                return;
            }
            if (!auditionEnabled() || !deps.getPreview(uri)) { return; }
            caretWait.poke(async () => {
                // A note still settling from typing speaks for itself.
                if (typingWait.pending) { return; }
                const answer = await askAt(editor);
                if (answer && memory.caretLanded(uri, { key: answer.Key, pitches: answer.Pitches })) {
                    play(uri, answer.Pitches, answer.Timbre);
                }
            });
        }),

        vscode.workspace.onDidCloseTextDocument(doc => {
            const uri = doc.uri.toString();
            memory.forget(uri);
            lastEdit.delete(uri);
        }),
    );
}
