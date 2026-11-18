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

// The ways into the AI from a score: the notes picked on the preview (or the whole score) made
// the editor's selection and handed to the transform command (aiTransform.ts), and Ask AI in
// Chat, which puts the place the request is about in the chat's input (chatPointerCore.ts). It
// stood in extension.ts until session 1298; the one thing it asks of the extension — a closed
// score opened again beside its preview — comes through AiEntryDeps.

import * as vscode from 'vscode';
import * as path from 'path';
import { chatPointer } from './chatPointerCore';

export interface AiEntryDeps {
    /** Opens the score a preview shows when no tab of it is open, where a click on that preview
     *  opens it; undefined when no preview shows `uri` or the score is gone. */
    reopenSource: (uri: string) => Promise<vscode.TextEditor | undefined>;
}

let reopenSource: AiEntryDeps['reopenSource'] = () => Promise.resolve(undefined);

/** Called once from activate, before anything here can be asked for. */
export function useAiEntryHost(deps: AiEntryDeps): void {
    ({ reopenSource } = deps);
}

/**
 * Shows `doc` where it already is: in its own editor when that is the active one, else in the
 * editor that shows it, else in the group that holds a tab of it.
 *
 * Asked from a preview, the active group is the PREVIEW's, and that is where showTextDocument
 * opens a document when it is given no column — over the preview. The extension then moves that
 * tab back to the scores' group (moveScoreTab), but the selection the caller makes on the editor
 * it was handed is made on the tab that is about to be closed: the editor that stays showed no
 * selection (seen in a real VS Code, session 1298). With no tab of it anywhere — the score
 * closed, its preview left open — it is opened as a click on that preview opens it (reopenSource:
 * a group of scores, never the preview's), and only a score with no preview either is left to
 * VS Code's choice of a column.
 */
async function showSource(doc: vscode.TextDocument, preserveFocus: boolean): Promise<vscode.TextEditor> {
    const key = doc.uri.toString();
    const active = vscode.window.activeTextEditor;
    const viewColumn = active?.document === doc ? active.viewColumn
        : vscode.window.visibleTextEditors.find(e => e.document === doc)?.viewColumn
        ?? vscode.window.tabGroups.all.find(g => g.tabs.some(t =>
            t.input instanceof vscode.TabInputText && t.input.uri.toString() === key))?.viewColumn;
    if (viewColumn === undefined) {
        const reopened = await reopenSource(key);
        if (reopened) {
            return reopened;
        }
    }
    return vscode.window.showTextDocument(doc, { viewColumn, preserveFocus });
}

export async function aiTransformWholeScore(uri: string): Promise<void> {
    const doc = vscode.workspace.textDocuments.find(d => d.uri.toString() === uri);
    if (!doc) {
        vscode.window.showErrorMessage('Lily#: open the .lys file to change it with AI.');
        return;
    }
    await showSource(doc, false);
    await vscode.commands.executeCommand('lilysharp.aiTransform', { whole: true });
}

/**
 * M3 bridge: a note range selected on the score preview (start/end are the
 * `data-pos` source offsets of the first and last selected notes) is turned into a
 * text selection in the editor, then the shared `lilysharp.aiTransform` command runs
 * against it — so a score-origin selection drives the exact same transform loop as a
 * text selection (§6).
 */
export async function aiTransformFromScore(uri: string, startPos: number, endPos: number, selected?: string): Promise<void> {
    const doc = vscode.workspace.textDocuments.find(d => d.uri.toString() === uri);
    if (!doc) {
        vscode.window.showErrorMessage('Lily#: open the .lys file to transform its notes.');
        return;
    }
    const text = doc.getText();

    // A grob's data-pos sits on its token's leading indentation; nudge the START
    // forward over horizontal whitespace so the selection begins on the note itself.
    let start = Math.max(0, Math.min(startPos, text.length));
    while (start < text.length && (text[start] === ' ' || text[start] === '\t')) {
        start++;
    }
    // The END data-pos is the LAST selected note's token start; extend it over that
    // note (pitch + accidentals + octave marks + duration + dots) and any trailing
    // @annotations so the replaced fragment ends cleanly after the note.
    const end = noteSelectionEnd(text, endPos);

    if (end <= start) {
        vscode.window.showErrorMessage('Lily#: could not map the selected notes to a text range.');
        return;
    }

    const range = new vscode.Range(doc.positionAt(start), doc.positionAt(end));
    const editor = await showSource(doc, false);
    editor.selection = new vscode.Selection(range.start, range.end);
    editor.revealRange(range, vscode.TextEditorRevealType.InCenter);
    await vscode.commands.executeCommand('lilysharp.aiTransform', { selected });
}

/**
 * "Ask AI in Chat" (owner's request 2026-10-29: Copilot's chat is the better place to talk the
 * music over): the place the request is about put in the chat's input, unsent, for the musician to
 * finish and send — the file alone, or with the notes or bars picked (on the score or in the
 * editor) their lines, section, part and bars (chatPointerCore). The picked text is made the
 * editor's selection too, which the chat takes in as it stands.
 */
export async function askAiInChat(doc: vscode.TextDocument, from?: number, to?: number): Promise<void> {
    const text = doc.getText();
    let range: { from: number; to: number } | undefined;
    if (from !== undefined && to !== undefined && from >= 0 && to >= from) {
        let start = Math.min(from, text.length);
        while (start < text.length && (text[start] === ' ' || text[start] === '\t')) {
            start++;
        }
        const end = Math.max(start, Math.min(to, text.length));
        if (end > start) {
            range = { from: start, to: end };
            const editor = await showSource(doc, true);
            const r = new vscode.Range(doc.positionAt(start), doc.positionAt(end));
            editor.selection = new vscode.Selection(r.start, r.end);
            editor.revealRange(r, vscode.TextEditorRevealType.InCenterIfOutsideViewport);
        }
    }
    // The score by its whole path, the one Lily#'s tools know it by (owner's request 2026-10-29: two
    // scores of one name in two folders, and the name alone did not say which). An unsaved score is
    // not on the disk for the chat to read, nor a saved one's changes since (owner's request, the
    // same day): the tool to read it with is named too.
    const name = doc.uri.scheme === 'file'
        ? doc.uri.fsPath + (doc.isDirty ? ' (changes not saved — read it with #readScore)' : '')
        : doc.isUntitled && path.isAbsolute(doc.uri.fsPath) ? `${doc.uri.fsPath} (not saved — read it with #readScore)`
        : path.basename(doc.uri.path);
    const query = chatPointer(name, text, range);
    try {
        await vscode.commands.executeCommand('workbench.action.chat.open', { query, isPartialQuery: true, mode: 'agent' });
    } catch {
        try {
            await vscode.commands.executeCommand('workbench.action.chat.open', { query, isPartialQuery: true });
        } catch (err) {
            void vscode.window.showErrorMessage(`Lily#: Copilot's chat could not be opened (${err}). Is GitHub Copilot Chat installed and signed in?`);
        }
    }
}

/**
 * End offset of the note token that begins at (or just after) `pos`: skips leading
 * whitespace, consumes the note/duration token, then any trailing `@annotations`
 * attached to it. Used to turn a last-note data-pos into a clean fragment end.
 */
export function noteSelectionEnd(text: string, pos: number): number {
    let i = Math.max(0, Math.min(pos, text.length));
    while (i < text.length && /\s/.test(text[i])) {
        i++;
    }
    if (text[i] === '[') {
        // A chord [c e g]4: consume to the matching ']' then its trailing duration.
        i++;
        while (i < text.length && text[i] !== ']') {
            i++;
        }
        if (i < text.length) {
            i++; // include ']'
        }
        while (i < text.length && /[0-9.]/.test(text[i])) {
            i++;
        }
    } else {
        while (i < text.length && /[A-Za-z0-9'.,]/.test(text[i])) {
            i++;
        }
    }
    // Absorb trailing "@annotation" tokens (e.g. @staccato, @f) on the same note.
    for (;;) {
        let k = i;
        while (k < text.length && (text[k] === ' ' || text[k] === '\t')) {
            k++;
        }
        if (k < text.length && text[k] === '@') {
            k++;
            while (k < text.length && /[A-Za-z0-9_-]/.test(text[k])) {
                k++;
            }
            i = k;
        } else {
            break;
        }
    }
    return i;
}
