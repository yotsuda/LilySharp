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

// The step keys (Ctrl+Shift+Up / Down by a semitone, Ctrl+Shift+PageUp / PageDown by an
// octave mark) and the automatic audition — the half that talks to VS Code. Owner's
// decisions, 2026-09-28 and 2026-10-15.
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
import { jumpOffsetFor } from './previewCore';
import {
    AuditionMemory, CARET_DEBOUNCE_MS, chromaticPositions, Debouncer, SOUNDS_WHEN_CHANGED, TYPING_SETTLE_MS,
    auditionDurationMs, caretMoveMaySound, mayChangeATypedPitch, mayChangeAnInstrument, reselectAfterStep,
    stepFallbackCommand, strumGapMs,
} from './auditionCore';

export interface StepAuditionDeps {
    getClient: () => LanguageClient | undefined;
    isReady: () => boolean;
    /** The open preview of a document, or undefined (then nothing sounds). */
    getPreview: (uri: string) => vscode.WebviewPanel | undefined;
    log: (msg: string) => void;
}

interface StepResponse {
    /** The moved note's accidental, when asked: WantAccidentals. */
    Accidentals?: Array<{ Glyph: string, Dx: number, FontSize: number }>;
    Fallback: boolean;
    Edits: { Start: number; End: number; NewText: string }[];
    Message?: string | null;
    Pitches: number[];
    Timbre: number;
    /** The part's General MIDI program for the preview's SoundFont synth; -1 (or absent,
     *  from an older server) when no part names one — the synth then voices the timbre. */
    Program?: number;
    Version: number;
    Error?: string | null;
    /** "absolute" / "relative": the step was on an `octave absolute` / `octave relative`,
     *  and the editor runs Convert Octaves to Absolute / Relative instead of applying edits. */
    ConvertOctaves?: 'absolute' | 'relative' | null;
    /** Strike the pitches one after another in the order given (a tuning's open strings). */
    Strum?: boolean;
    /** With Strum: each stops as the next sounds, never a chord (a bass's strings). */
    Apart?: boolean;
}

interface AuditionAtResponse {
    Key: number;
    Kind?: string | null;
    Pitches: number[];
    Timbre: number;
    Program?: number;
    Version: number;
    Strum?: boolean;
    Apart?: boolean;
}

/** lilysharp.audition.enabled: gates the three automatic auditions and the step's. */
function auditionEnabled(): boolean {
    return vscode.workspace.getConfiguration('lilysharp').get<boolean>('audition.enabled', true);
}

/** lilysharp.chordShapes.includeStretch (owner's decision 2026-09-28): an @chord also
 * steps through stretch shapes (fretted frets five apart). Sent with every step and
 * audition request (IncludeStretch; the server reads a missing one as false). Not in the
 * Settings UI since 2026-10-25 — settings.json only. */
function includeStretch(): boolean {
    return vscode.workspace.getConfiguration('lilysharp').get<boolean>('chordShapes.includeStretch', false);
}

/** What a drag in the preview steps (owner's requests 2026-11-02 and 03), the keys' pairs: 'position'
 * a note's staff positions, each to the key's tone there (Ctrl+Shift+Up / Down); 'octave' its
 * octave (Ctrl+Shift+PageUp / PageDown); 'mark' a dynamic's or an articulation's name; 'placement'
 * that mark's .up / .down. */
export type NoteDragMode = 'position' | 'octave' | 'chromatic' | 'mark' | 'placement';

/** A drag of `steps`, up when positive, from what has `data-pos` `position`. */
export interface NoteDragSteps {
    /** Sounds where the note would land. The last asked for wins: a drag outruns the server. */
    hear(doc: vscode.TextDocument, position: number, steps: number, mode: NoteDragMode): Promise<void>;
    /** The mark's word now and as it would be: nothing is written. Undefined for a stale question. */
    markWords(doc: vscode.TextDocument, position: number, steps: number, mode: NoteDragMode): Promise<{ from: string, to: string, positions?: number } | undefined>;
    /** Writes it, one edit and one undo step; false when it writes nothing. */
    write(doc: vscode.TextDocument, position: number, steps: number, mode: NoteDragMode): Promise<boolean>;
    /** The accidental the note would be drawn with where the drag has it; nothing is written. */
    accidentals(doc: vscode.TextDocument, position: number, steps: number, mode: NoteDragMode, renderName: string | undefined): Promise<Array<{ Glyph: string, Dx: number, FontSize: number }>>;
    /** A tempo's number where a drag has it, written at once, all of one drag one undo. */
    tempoLive(doc: vscode.TextDocument, position: number, value: number): void;
    /** The drag let go, or cancelled with its first number written back. */
    tempoEnd(doc: vscode.TextDocument): void;
}

export function registerStepAudition(context: vscode.ExtensionContext, deps: StepAuditionDeps): NoteDragSteps {
    const memory = new AuditionMemory();
    const caretWait = new Debouncer(CARET_DEBOUNCE_MS);
    const typingWait = new Debouncer(TYPING_SETTLE_MS);
    // When each document last changed — a selection change right after one is the
    // edit's own caret movement, not a move to audition.
    const lastEdit = new Map<string, number>();

    /** Sends pitches to the document's preview synth; silent without a preview. */
    const play = (uri: string, pitches: readonly number[], timbre: number, program?: number, strum?: boolean,
        apart?: boolean) => {
        const panel = deps.getPreview(uri);
        if (!panel || pitches.length === 0) { return; }
        void panel.webview.postMessage({
            type: 'playPitches', pitches, timbre, program: program ?? -1,
            durationMs: auditionDurationMs(pitches.length, strum, apart),
            strumMs: strumGapMs(strum),
            apart: !!(strum && apart),
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
                IncludeStretch: includeStretch(),
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
    const step = (direction: 1 | -1, semitone: boolean) => {
        stepChain = stepChain.then(() => runStep(direction, semitone)).catch(err => deps.log(`step: ${err}`));
        return stepChain;
    };

    // Nothing steppable: the key's own command (stepFallbackCommand) — Add Cursor Above /
    // Below on Linux for the semitone keys, Move Editor Left / Right for the octave keys.
    const fallback = async (direction: 1 | -1, semitone: boolean) => {
        const command = stepFallbackCommand(direction, process.platform, semitone);
        if (command) { await vscode.commands.executeCommand(command); }
    };

    const runStep = async (direction: 1 | -1, semitone: boolean) => {
        const editor = vscode.window.activeTextEditor;
        const client = deps.getClient();
        if (!editor || editor.document.languageId !== 'lilysharp' || !client || !deps.isReady()) {
            await fallback(direction, semitone);
            return;
        }
        const doc = editor.document;
        const version = doc.version;
        const response = await client.sendRequest<StepResponse>('lilysharp/step', {
            TextDocument: { uri: doc.uri.toString() },
            Selections: editor.selections.map(s => ({ Start: doc.offsetAt(s.start), End: doc.offsetAt(s.end) })),
            Direction: direction,
            IncludeStretch: includeStretch(),
            Semitone: semitone,
        });
        if (response.Fallback) {
            if (response.Error) { deps.log(`step: ${response.Error}`); }
            await fallback(direction, semitone);
            return;
        }
        // Typed into while the server thought: the offsets are about another text.
        if (doc.version !== version || response.Version !== version) { return; }
        if (response.ConvertOctaves) {
            // An `octave absolute` / `octave relative`: the whole file converts, every note
            // keeping its pitch — the Convert Octaves command, with its own messages.
            await vscode.commands.executeCommand(response.ConvertOctaves === 'absolute'
                ? 'lilysharp.convertOctavesToAbsolute' : 'lilysharp.convertOctavesToRelative');
            return;
        }
        if (response.Edits.length > 0) {
            // A selection the step replaced exactly — a length or a chord name it stepped (owner's
            // request 2026-10-24) — is selected again on the new text, so the next press steps it
            // again (reselectAfterStep).
            const before = editor.selections.map(s => ({ Start: doc.offsetAt(s.start), End: doc.offsetAt(s.end) }));
            // ONE undo step for every cursor's edit.
            const applied = await editor.edit(b => {
                for (const e of response.Edits) {
                    b.replace(new vscode.Range(doc.positionAt(e.Start), doc.positionAt(e.End)), e.NewText);
                }
            }, { undoStopBefore: true, undoStopAfter: true });
            if (!applied) { return; }
            const again = reselectAfterStep(before, response.Edits);
            if (again) {
                editor.selections = again.map(r => new vscode.Selection(doc.positionAt(r.Start), doc.positionAt(r.End)));
            }
        }
        if (response.Message) {
            vscode.window.setStatusBarMessage(`Lily#: ${response.Message}`, 4000);
        }
        if (auditionEnabled() && response.Pitches.length > 0) {
            const uri = doc.uri.toString();
            play(uri, response.Pitches, response.Timbre, response.Program, response.Strum, response.Apart);
            // The caret now rests on what just sounded: remember it, so the caret
            // audition does not repeat it. The key is asked for, not guessed.
            const here = await askAt(editor);
            if (here) { memory.record(uri, { key: here.Key, pitches: here.Pitches, program: here.Program }); }
        }
    };

    const stepsAsked = (doc: vscode.TextDocument, offset: number, steps: number, mode: NoteDragMode, holdOthers: boolean) => ({
            TextDocument: { uri: doc.uri.toString() },
            Selections: [{ Start: offset, End: offset }],
            Direction: steps > 0 ? 1 : -1,
            ...(mode === 'position' ? { StaffPositions: Math.abs(steps) } : { Count: Math.abs(steps) }),
            IncludeStretch: false,
            Semitone: mode === 'position' || mode === 'mark',
            HoldOthers: holdOthers,
            Chromatic: mode === 'chromatic',
    });
    const askSteps = (doc: vscode.TextDocument, offset: number, steps: number, mode: NoteDragMode, holdOthers = false) =>
        deps.getClient()!.sendRequest<StepResponse>('lilysharp/step', stepsAsked(doc, offset, steps, mode, holdOthers));
    /** The word of what a drag steps at `offset`: a mark's name, any shape and placement; a chords-row
     * entry, a tempo's number. */
    const markWordAt = (text: string, offset: number) => /^!?[\w#+/-]+(?:\([^)]*\))?(?:\.(?:up|down))?/.exec(text.slice(offset))?.[0] ?? '';
    /** A pitch's name alone: no octave marks, no duration. */
    const pitchNameAt = (text: string, offset: number) => /^[a-z]+/.exec(text.slice(offset))?.[0] ?? '';
    let lastHeard = 0, lastWords = 0;
    // One at a time, in order: an edit waits for the last, and each replaces what that one wrote.
    let tempoQueue: Promise<unknown> = Promise.resolve();
    let tempoDrag: { start: number, length: number, written: boolean } | undefined;
    const writeTempo = async (doc: vscode.TextDocument, number: string, undoStopAfter: boolean) => {
        const drag = tempoDrag!;
        const range = new vscode.Range(doc.positionAt(drag.start), doc.positionAt(drag.start + drag.length));
        const editor = vscode.window.visibleTextEditors.find(e => e.document === doc);
        if (editor) {
            await editor.edit(b => b.replace(range, number), { undoStopBefore: !drag.written, undoStopAfter });
        } else {
            const edit = new vscode.WorkspaceEdit();
            edit.replace(doc.uri, range, number);
            await vscode.workspace.applyEdit(edit);
        }
        drag.length = number.length;
        drag.written = true;
    };
    const noteDrag: NoteDragSteps = {
        accidentals: async (doc, position, steps, mode, renderName) => {
            if (!deps.getClient() || !deps.isReady() || steps === 0) { return []; }
            const offset = jumpOffsetFor(doc.getText(), position);
            const response = await deps.getClient()!.sendRequest<StepResponse>('lilysharp/step', {
                ...stepsAsked(doc, offset, steps, mode, true),
                WantAccidentals: true,
                RenderName: renderName ?? null,
            });
            return response.Accidentals ?? [];
        },
        tempoLive: (doc, position, value) => {
            tempoQueue = tempoQueue.then(async () => {
                if (!tempoDrag) {
                    const digits = /^\d+/.exec(doc.getText().slice(position));
                    if (!digits) { return; }
                    tempoDrag = { start: position, length: digits[0].length, written: false };
                    // The number is what it is all for: on the screen, if it is not.
                    const at = doc.positionAt(position);
                    vscode.window.visibleTextEditors.find(e => e.document === doc)
                        ?.revealRange(new vscode.Range(at, at), vscode.TextEditorRevealType.InCenterIfOutsideViewport);
                }
                await writeTempo(doc, String(value), false);
            }).catch(() => undefined);
        },
        tempoEnd: (doc) => {
            tempoQueue = tempoQueue.then(async () => {
                const drag = tempoDrag;
                if (drag?.written) {
                    // The same number again, for the stop that closes the undo.
                    await writeTempo(doc, doc.getText(new vscode.Range(doc.positionAt(drag.start), doc.positionAt(drag.start + drag.length))), true);
                }
                tempoDrag = undefined;
            }).catch(() => { tempoDrag = undefined; });
        },
        markWords: async (doc, position, steps, mode) => {
            if (!deps.getClient() || !deps.isReady()) { return undefined; }
            const asked = ++lastWords;
            const text = doc.getText();
            const offset = jumpOffsetFor(text, position);
            const response = await askSteps(doc, offset, steps, mode);
            if (asked !== lastWords) { return undefined; }
            let after = text;
            for (const e of [...response.Edits].sort((a, b) => b.Start - a.Start)) {
                after = after.slice(0, e.Start) + e.NewText + after.slice(e.End);
            }
            if (mode === 'chromatic') {
                const from = pitchNameAt(text, offset), to = pitchNameAt(after, offset);
                return { from, to, positions: chromaticPositions(from, to, steps) };
            }
            return { from: markWordAt(text, offset), to: markWordAt(after, offset) };
        },
        hear: async (doc, position, steps, mode) => {
            if (!deps.getClient() || !deps.isReady() || !auditionEnabled()) { return; }
            const asked = ++lastHeard;
            const offset = jumpOffsetFor(doc.getText(), position);
            const answer = steps === 0
                ? await deps.getClient()!.sendRequest<AuditionAtResponse>('lilysharp/auditionAt', {
                    TextDocument: { uri: doc.uri.toString() }, Offset: offset, IncludeStretch: false,
                })
                : await askSteps(doc, offset, steps, mode);
            if (asked === lastHeard) { play(doc.uri.toString(), answer.Pitches, answer.Timbre, answer.Program); }
        },
        write: async (doc, position, steps, mode) => {
            if (!deps.getClient() || !deps.isReady() || steps === 0) { return false; }
            const version = doc.version;
            // A dragged note moves alone (owner's decision 2026-11-03), where the keys' notes after
            // it follow: what the hand saw move is what moves.
            const moved = mode === 'position' || mode === 'octave' || mode === 'chromatic';
            const response = await askSteps(doc, jumpOffsetFor(doc.getText(), position), steps, mode, moved);
            if (response.Message) { vscode.window.setStatusBarMessage(`Lily#: ${response.Message}`, 4000); }
            if (response.Fallback || response.ConvertOctaves || response.Edits.length === 0
                || doc.version !== version || response.Version !== version) { return false; }
            const edit = new vscode.WorkspaceEdit();
            for (const e of response.Edits) {
                edit.replace(doc.uri, new vscode.Range(doc.positionAt(e.Start), doc.positionAt(e.End)), e.NewText);
            }
            return vscode.workspace.applyEdit(edit);
        },
    };

    context.subscriptions.push(
        vscode.commands.registerCommand('lilysharp.stepSemitoneUp', () => step(1, true)),
        vscode.commands.registerCommand('lilysharp.stepSemitoneDown', () => step(-1, true)),
        vscode.commands.registerCommand('lilysharp.stepUp', () => step(1, false)),
        vscode.commands.registerCommand('lilysharp.stepDown', () => step(-1, false)),
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
            const pitch = mayChangeATypedPitch(event.contentChanges);
            const instrument = mayChangeAnInstrument(event.contentChanges,
                doc.lineAt(event.contentChanges[0].range.start.line).text);
            if (!pitch && !instrument) {
                // A duration digit or a space after the note: nothing new to hear,
                // but a note still settling keeps settling.
                return;
            }
            typingWait.poke(async () => {
                if (vscode.window.activeTextEditor !== editor) { return; }
                const answer = await askAt(editor);
                if (!answer || !(SOUNDS_WHEN_CHANGED.has(answer.Kind ?? '') ? instrument : pitch)) { return; }
                if (memory.typed(uri, { key: answer.Key, pitches: answer.Pitches, program: answer.Program })) {
                    play(uri, answer.Pitches, answer.Timbre, answer.Program, answer.Strum, answer.Apart);
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
                // An instrument or a tuning sounds when it changes, not when the caret passes it.
                if (answer && !SOUNDS_WHEN_CHANGED.has(answer.Kind ?? '')
                    && memory.caretLanded(uri, { key: answer.Key, pitches: answer.Pitches, program: answer.Program })) {
                    play(uri, answer.Pitches, answer.Timbre, answer.Program);
                }
            });
        }),

        vscode.workspace.onDidCloseTextDocument(doc => {
            const uri = doc.uri.toString();
            memory.forget(uri);
            lastEdit.delete(uri);
        }),
    );

    return noteDrag;
}
