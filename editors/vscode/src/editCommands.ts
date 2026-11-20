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

// The commands that rewrite the score — regroup, the octaves, the string numbers, the repeat
// signs, a chord's spelling, the sections (split, join, cut), a phrase lifted out, a chord
// track, a section's key and its respelling, a harmony part — and the two that only look
// (the same bar in another part, the review with no AI). Each asks the server and applies its
// answer as one edit. They stood in extension.ts until session 1297; what they read of the
// extension's own state comes through EditCommandsDeps.

import * as vscode from 'vscode';
import type { LanguageClient } from 'vscode-languageclient/node';
import { reviewPointsOf } from './reviewCore';
import { rewriteIsStale } from './previewCore';
import { SplitSectionsChoice, modalText, splitChoiceItems } from './splitSectionsCore';
import { sameDocumentUri } from './shapeItemCore';
import { joinRecordingBars, parseRecording, recordingPathFor, serializeRecording } from './recordingCore';

export interface EditCommandsDeps {
    /** The server's client; undefined until the first score starts it. */
    getClient: () => LanguageClient | undefined;
    /** The extension's output channel. */
    outputChannel: vscode.OutputChannel;
    /** The open previews, by their document's uri. */
    previewPanels: ReadonlyMap<string, vscode.WebviewPanel>;
    /** The score each document's preview shows, where it is not the first. */
    selectedRenders: ReadonlyMap<string, string>;
}

let outputChannel: vscode.OutputChannel;
let previewPanels: EditCommandsDeps['previewPanels'];
let selectedRenders: EditCommandsDeps['selectedRenders'];
/** The server's client, or undefined while it has not been started. */
let server: () => LanguageClient | undefined = () => undefined;

/** Called once from activate: registers the commands. */
export function registerEditCommands(context: vscode.ExtensionContext, deps: EditCommandsDeps): void {
    ({ outputChannel, previewPanels, selectedRenders } = deps);
    server = deps.getClient;
    context.subscriptions.push(
        vscode.commands.registerCommand('lilysharp.regroup', () => {
            outputChannel.appendLine('regroup command triggered');
            regroup();
        }),
        // From the palette with no arguments (the active file, the first section some part
        // subdivides), or from the LYS2007 quick fix with the document and the section.
        vscode.commands.registerCommand('lilysharp.splitSectionHere',
            (uri?: string, section?: string, bars?: number, name?: string) => splitSectionHere(typeof uri === 'string' ? uri : undefined,
                typeof section === 'string' ? section : undefined, typeof bars === 'number' ? bars : undefined,
                typeof name === 'string' ? name : undefined)),
        // The preview's right-click on a bar line (owner's decision 2026-11-17): the file it shows, cut there.
        vscode.commands.registerCommand('lilysharp.previewSplitSectionHere', (m?: { lysCutAt?: number; lysCutSnap?: boolean }) => {
            const shown = [...previewPanels.entries()].find(([, p]) => p.active);
            if (!shown || typeof m?.lysCutAt !== 'number' || m.lysCutAt < 0) { return; }
            return splitSectionHere(shown[0], undefined, undefined, undefined, m.lysCutAt, !!m.lysCutSnap);
        }),
        vscode.commands.registerCommand('lilysharp.joinSections',
            (uri?: string, sections?: string[]) => joinSections(typeof uri === 'string' ? uri : undefined, Array.isArray(sections) ? sections : undefined)),
        // From the code action on a selection (the document and the selection), or from the palette.
        vscode.commands.registerCommand('lilysharp.joinBars',
            (uri?: string, start?: number, end?: number) => joinBars(typeof uri === 'string' ? uri : undefined,
                typeof start === 'number' ? start : undefined, typeof end === 'number' ? end : undefined)),
        // A file beside a chart that Join Bars in Pairs wrote, undone or redone with the chart (one
        // edit across the two files): written to the disk as well, where the preview reads it.
        vscode.workspace.onDidChangeTextDocument(e => {
            if ((e.reason === vscode.TextDocumentChangeReason.Undo || e.reason === vscode.TextDocumentChangeReason.Redo)
                && joinedRecordings.has(e.document.uri.toString()) && e.document.isDirty) {
                void e.document.save();
            }
        }),
        vscode.commands.registerCommand('lilysharp.splitSectionsToMatch',
            (uri?: string, section?: string) => splitSectionsToMatch(
                typeof uri === 'string' ? uri : undefined, typeof section === 'string' ? section : undefined)),
        vscode.commands.registerCommand('lilysharp.convertOctavesToAbsolute', () => convertOctaves(true)),
        vscode.commands.registerCommand('lilysharp.convertOctavesToRelative', () => convertOctaves(false)),
        vscode.commands.registerCommand('lilysharp.removeStringNumbers', () => removeStringNumbers()),
        vscode.commands.registerCommand('lilysharp.sameBarNextPart', () => goToSameBar('next')),
        vscode.commands.registerCommand('lilysharp.writeRepeatSignsInEveryPart', () => spreadRepeatSigns(false)),
        vscode.commands.registerCommand('lilysharp.keepRepeatSignsInOnePart', () => spreadRepeatSigns(true)),
        vscode.commands.registerCommand('lilysharp.sameBarPreviousPart', () => goToSameBar('previous')),
        vscode.commands.registerCommand('lilysharp.sameBarInPart', () => goToSameBar('pick')),
        // From the context menu at the caret, or from the code action with the document and offset.
        vscode.commands.registerCommand('lilysharp.convertShapeToNotes',
            (uri?: string, offset?: number) => rewriteChordAt('lilysharp/shapeToNotes',
                typeof uri === 'string' ? uri : undefined, typeof offset === 'number' ? offset : undefined)),
        vscode.commands.registerCommand('lilysharp.addChordDiagram',
            (uri?: string, offset?: number) => rewriteChordAt('lilysharp/addChordDiagram',
                typeof uri === 'string' ? uri : undefined, typeof offset === 'number' ? offset : undefined)),
        vscode.commands.registerCommand('lilysharp.convertNotesToChord',
            (uri?: string, offset?: number) => rewriteChordAt('lilysharp/notesToChord',
                typeof uri === 'string' ? uri : undefined, typeof offset === 'number' ? offset : undefined)),
        vscode.commands.registerCommand('lilysharp.extractPhrase', () => {
            outputChannel.appendLine('extractPhrase command triggered');
            extractPhrase();
        }),
        vscode.commands.registerCommand('lilysharp.addChordTrack', () => {
            outputChannel.appendLine('addChordTrack command triggered');
            addChordTrack(context);
        }),
        vscode.commands.registerCommand('lilysharp.guessSectionKey', () => guessSectionKey()),
        vscode.commands.registerCommand('lilysharp.reviewScore', () => reviewScoreCommand(context)),
        vscode.commands.registerCommand('lilysharp.respellNotesToKey', () => respellNotesToKey()),
        vscode.commands.registerCommand('lilysharp.writeHarmonyPart', () => writeHarmonyPart()),
        vscode.commands.registerCommand('lilysharp.writeSingerPart',
            (uri?: string, singer?: string) => writeSingerPart(
                typeof uri === 'string' ? uri : undefined, typeof singer === 'string' ? singer : undefined)),
    );
}

interface RegroupResponse {
    Success: boolean;
    NewText: string | null;
    FromGrouping: string | null;
    ToGrouping: string | null;
    Error: string | null;
}

/**
 * Converts the active .lys document between the by-section and by-part
 * layouts (toggles whichever the file currently uses) and applies the result as
 * a full-document edit.
 */
async function regroup() {
    const client = server();
    const editor = vscode.window.activeTextEditor;
    if (!editor || editor.document.languageId !== 'lilysharp') {
        vscode.window.showErrorMessage('Lily#: open a .lys file to regroup it.');
        return;
    }
    if (!client) {
        vscode.window.showErrorMessage('Lily#: language server not ready.');
        return;
    }

    const doc = editor.document;
    try {
        const response = await rewriteRequest<RegroupResponse>(doc, 'lilysharp/regroup', {
            textDocument: { uri: doc.uri.toString() }
        });
        if (!response) { return; }
        if (response.Success && response.NewText != null) {
            const fullRange = new vscode.Range(
                doc.positionAt(0), doc.positionAt(doc.getText().length));
            const edit = new vscode.WorkspaceEdit();
            edit.replace(doc.uri, fullRange, response.NewText);
            await vscode.workspace.applyEdit(edit);
            vscode.window.showInformationMessage(
                `Lily#: regrouped ${response.ToGrouping} (was ${response.FromGrouping}).`);
        } else {
            vscode.window.showErrorMessage(`Lily#: ${response.Error}`);
        }
    } catch (err) {
        vscode.window.showErrorMessage(`Lily#: regrouping failed: ${err}`);
    }
}

interface ConvertOctavesResponse {
    Success: boolean;
    NewText: string | null;
    ChangedNotes: number;
    Error: string | null;
}

/**
 * Rewrites the whole active .lys document into absolute or relative octaves. The server
 * compiles the result and compares every note before answering, so what comes back sounds
 * exactly as the file did; it is applied as one edit, so one undo restores the file.
 */
async function convertOctaves(absolute: boolean) {
    const client = server();
    const editor = vscode.window.activeTextEditor;
    if (!editor || editor.document.languageId !== 'lilysharp') {
        vscode.window.showErrorMessage('Lily#: open a .lys file to convert its octaves.');
        return;
    }
    if (!client) {
        vscode.window.showErrorMessage('Lily#: language server not ready.');
        return;
    }

    const doc = editor.document;
    const mode = absolute ? 'absolute' : 'relative';
    try {
        const response = await rewriteRequest<ConvertOctavesResponse>(doc, 'lilysharp/convertOctaves', {
            textDocument: { uri: doc.uri.toString() },
            absolute
        });
        if (!response) { return; }
        if (response.Success && response.NewText != null) {
            if (response.NewText === doc.getText()) {
                vscode.window.showInformationMessage(`Lily#: the octaves are already ${mode}.`);
                return;
            }
            const fullRange = new vscode.Range(
                doc.positionAt(0), doc.positionAt(doc.getText().length));
            const edit = new vscode.WorkspaceEdit();
            edit.replace(doc.uri, fullRange, response.NewText);
            await vscode.workspace.applyEdit(edit);
            vscode.window.showInformationMessage(
                `Lily#: octaves converted to ${mode} (${response.ChangedNotes} note(s) re-marked).`);
        } else {
            vscode.window.showErrorMessage(`Lily#: ${response.Error}`);
        }
    } catch (err) {
        vscode.window.showErrorMessage(`Lily#: octave conversion failed: ${err}`);
    }
}

interface RemoveStringNumbersResponse {
    NewText: string | null;
    Removed: number;
    Error: string | null;
}

interface SpreadRepeatSignsResponse {
    Edits: { Start: number; End: number; NewText: string }[];
    Count: number;
    Skipped: string[];
}

/**
 * Writes the document's repeat signs in every part that writes their section, or (reverse)
 * takes them back to the first part that writes each — owner's design 2026-11-14,
 * docs/inline-repeat-design.md §8: run from the palette or the context menu wherever the
 * caret is, never while typing, and with nothing underlined (a sign written in one part is
 * correct). The server finds the bar lines (lilysharp/spreadRepeatSigns, Core's
 * RepeatSignSpreader); the places it could not write are listed in the output channel.
 */
async function spreadRepeatSigns(reverse: boolean) {
    const client = server();
    const editor = vscode.window.activeTextEditor;
    if (!client || !editor || editor.document.languageId !== 'lilysharp') {
        return;
    }
    const doc = editor.document;
    try {
        const response = await client.sendRequest<SpreadRepeatSignsResponse>('lilysharp/spreadRepeatSigns', {
            textDocument: { uri: doc.uri.toString() },
            reverse,
        });
        if (response.Edits.length > 0) {
            const edit = new vscode.WorkspaceEdit();
            for (const e of response.Edits) {
                edit.replace(doc.uri, new vscode.Range(doc.positionAt(e.Start), doc.positionAt(e.End)), e.NewText);
            }
            await vscode.workspace.applyEdit(edit);
        }
        const did = reverse
            ? `took ${response.Count} repeat sign(s) out of the other parts`
            : `wrote ${response.Count} repeat sign(s) in the other parts`;
        if (response.Skipped.length === 0) {
            vscode.window.showInformationMessage(response.Count === 0
                ? 'Lily#: nothing to change - the repeat signs are already written that way.'
                : `Lily#: ${did}.`);
            return;
        }
        outputChannel.appendLine(`Repeat signs: ${response.Skipped.length} place(s) left as written:`);
        for (const place of response.Skipped) {
            outputChannel.appendLine(`  ${place}`);
        }
        const show = 'Show';
        const picked = await vscode.window.showInformationMessage(
            `Lily#: ${did}; ${response.Skipped.length} place(s) could not be written (a bar inside a phrase, or another bar line there).`,
            show);
        if (picked === show) {
            outputChannel.show(true);
        }
    } catch (err) {
        vscode.window.showErrorMessage(`Lily#: writing the repeat signs failed: ${err}`);
    }
}

interface SameBarResponse {
    Section: string | null;
    Bar: number;
    Voice: string | null;
    Places: { Voice: string; Offset: number }[];
}

/**
 * Moves the caret to the bar it is in, as another part (or chords row) of the same section
 * writes it (owner's request 2026-11-14: the same bar of two parts may stand many lines
 * apart). 'next' and 'previous' walk the section's voices in document order, round and
 * round; 'pick' asks which one when there are several. The server counts the bars
 * (lilysharp/sameBar, Core's SameBarFinder).
 */
async function goToSameBar(which: 'next' | 'previous' | 'pick') {
    const client = server();
    const editor = vscode.window.activeTextEditor;
    if (!client || !editor || editor.document.languageId !== 'lilysharp') {
        return;
    }
    const doc = editor.document;
    try {
        const response = await client.sendRequest<SameBarResponse>('lilysharp/sameBar', {
            textDocument: { uri: doc.uri.toString() },
            offset: doc.offsetAt(editor.selection.active),
        });
        if (!response.Section) {
            vscode.window.showInformationMessage("Lily#: put the caret in a section's music.");
            return;
        }
        if (response.Places.length === 0) {
            vscode.window.showInformationMessage(`Lily#: no other part writes section ${response.Section}.`);
            return;
        }
        let place = which === 'previous' ? response.Places[response.Places.length - 1] : response.Places[0];
        if (which === 'pick' && response.Places.length > 1) {
            const picked = await vscode.window.showQuickPick(
                response.Places.map(p => ({
                    label: p.Voice,
                    description: doc.lineAt(doc.positionAt(p.Offset).line).text.trim().slice(0, 60),
                    place: p,
                })),
                { placeHolder: `Bar ${response.Bar} of section ${response.Section}: go to…` });
            if (!picked) {
                return;
            }
            place = picked.place;
        }
        const at = doc.positionAt(place.Offset);
        editor.selection = new vscode.Selection(at, at);
        editor.revealRange(new vscode.Range(at, at), vscode.TextEditorRevealType.InCenterIfOutsideViewport);
        vscode.window.setStatusBarMessage(
            `Lily#: bar ${response.Bar} of section ${response.Section}, ${place.Voice}`, 3000);
    } catch (err) {
        vscode.window.showErrorMessage(`Lily#: finding the same bar failed: ${err}`);
    }
}

/**
 * Removes every written string number (`\1` … `\9`) from the active .lys document, so the tab
 * chooses every string itself. Applied as one edit, so one undo puts them all back.
 */
async function removeStringNumbers() {
    const client = server();
    const editor = vscode.window.activeTextEditor;
    if (!editor || editor.document.languageId !== 'lilysharp') {
        vscode.window.showErrorMessage('Lily#: open a .lys file to remove its string numbers.');
        return;
    }
    if (!client) {
        vscode.window.showErrorMessage('Lily#: language server not ready.');
        return;
    }

    const doc = editor.document;
    try {
        const response = await rewriteRequest<RemoveStringNumbersResponse>(doc, 'lilysharp/removeStringNumbers', {
            textDocument: { uri: doc.uri.toString() }
        });
        if (!response) { return; }
        if (response.NewText == null) {
            vscode.window.showErrorMessage(`Lily#: ${response.Error}`);
            return;
        }
        if (response.Removed === 0) {
            vscode.window.showInformationMessage('Lily#: this file writes no string numbers.');
            return;
        }
        const fullRange = new vscode.Range(doc.positionAt(0), doc.positionAt(doc.getText().length));
        const edit = new vscode.WorkspaceEdit();
        edit.replace(doc.uri, fullRange, response.NewText);
        await vscode.workspace.applyEdit(edit);
        vscode.window.showInformationMessage(
            `Lily#: removed ${response.Removed} string number(s); the tab now chooses every string.`);
    } catch (err) {
        vscode.window.showErrorMessage(`Lily#: removing string numbers failed: ${err}`);
    }
}

interface ShapeToNotesResponse {
    Start: number;
    End: number;
    Replacement: string | null;
    Error: string | null;
}

/**
 * One of the chord rewrites at the caret (or at `offset` of `uri`, from the code action):
 * `lilysharp/shapeToNotes` writes a chord(…) / diagram(…) item out as the <…> chord it plays
 * (the server works out the notes and their octave marks and checks that the file still sounds
 * the same); `lilysharp/addChordDiagram` gives a <…> chord (or a note) its @diagram(SHAPE);
 * `lilysharp/notesToChord` writes a <…> chord as the chord(…) item that sounds it. One edit, so one
 * undo takes it back.
 */
async function rewriteChordAt(method: string, uri?: string, offset?: number) {
    const client = server();
    if (!client) {
        vscode.window.showErrorMessage('Lily#: language server not ready.');
        return;
    }
    const editor = vscode.window.activeTextEditor;
    const doc = uri !== undefined
        ? vscode.workspace.textDocuments.find(d => sameDocumentUri(d.uri.toString(), uri))
        : editor?.document;
    if (!doc || doc.languageId !== 'lilysharp') {
        vscode.window.showErrorMessage('Lily#: put the caret on a chord in a .lys file.');
        return;
    }
    const at = offset ?? (editor ? doc.offsetAt(editor.selection.active) : 0);
    try {
        const response = await client.sendRequest<ShapeToNotesResponse>(method, {
            textDocument: { uri: doc.uri.toString() },
            offset: at,
        });
        if (response.Replacement == null) {
            vscode.window.showInformationMessage(`Lily#: ${response.Error}`);
            return;
        }
        const edit = new vscode.WorkspaceEdit();
        edit.replace(doc.uri, new vscode.Range(doc.positionAt(response.Start), doc.positionAt(response.End)),
            response.Replacement);
        await vscode.workspace.applyEdit(edit);
    } catch (err) {
        vscode.window.showErrorMessage(`Lily#: rewriting the chord failed: ${err}`);
    }
}

interface SplitSectionsResponse {
    Success: boolean;
    NewText: string | null;
    Plan: string | null;
    Section: string | null;
    Choices: SplitSectionsChoice[] | null;
    Error: string | null;
}

/**
 * "Split Sections to Match a Part" (owner's design, 2026-09-28): one part has cut a section into
 * several and the others still write it whole. The server finds the part to follow, cuts the
 * others at the same bars, updates the forms and checks that every cut part sounds exactly as
 * before; this side asks which part to follow only when the candidates disagree, shows the plan
 * in ONE modal confirmation, and applies it as one edit — one undo restores the file. A refusal
 * (a tie or slur across a cut, a cut mid-bar, a check that failed) is shown the same way, with
 * its reasons as the detail.
 */
async function splitSectionsToMatch(uriArg?: string, sectionArg?: string) {
    const client = server();
    const doc = uriArg
        ? vscode.workspace.textDocuments.find(d => sameDocumentUri(d.uri.toString(), uriArg))
        : vscode.window.activeTextEditor?.document;
    if (!doc || doc.languageId !== 'lilysharp') {
        vscode.window.showErrorMessage('Lily#: open a .lys file to split its sections.');
        return;
    }
    if (!client) {
        vscode.window.showErrorMessage('Lily#: language server not ready.');
        return;
    }

    const request = (section?: string, reference?: string) =>
        client!.sendRequest<SplitSectionsResponse>('lilysharp/splitSections', {
            textDocument: { uri: doc.uri.toString() },
            section: section ?? null,
            reference: reference ?? null,
        });
    try {
        const version = doc.version;
        let response = await vscode.window.withProgress(
            { location: vscode.ProgressLocation.Window, title: 'Lily#: checking the split…' },
            () => request(sectionArg));
        if (!response.Success && response.Choices && response.Choices.length > 0) {
            const pick = await vscode.window.showQuickPick(splitChoiceItems(response.Choices), {
                title: `Lily#: split section ${response.Section} to match which part?`,
                placeHolder: 'The parts subdivide it differently — pick the one the others should follow',
            });
            if (!pick) {
                return;
            }
            const section = response.Section ?? undefined;
            response = await vscode.window.withProgress(
                { location: vscode.ProgressLocation.Window, title: 'Lily#: checking the split…' },
                () => request(section, pick.part));
        }
        if (!response.Success || response.NewText == null) {
            const refusal = modalText(response.Error ?? 'The sections were not split.');
            await vscode.window.showWarningMessage(refusal.message, { modal: true, detail: refusal.detail });
            return;
        }
        const plan = modalText(response.Plan ?? '');
        const answer = await vscode.window.showInformationMessage(
            plan.message, { modal: true, detail: plan.detail }, 'Apply');
        if (answer !== 'Apply') {
            return;
        }
        if (doc.version !== version) {
            vscode.window.showWarningMessage('Lily#: the file changed while the split was being checked — run the command again.');
            return;
        }
        const fullRange = new vscode.Range(doc.positionAt(0), doc.positionAt(doc.getText().length));
        const edit = new vscode.WorkspaceEdit();
        edit.replace(doc.uri, fullRange, response.NewText);
        await vscode.workspace.applyEdit(edit);
    } catch (err) {
        vscode.window.showErrorMessage(`Lily#: splitting sections failed: ${err}`);
    }
}

/**
 * Join Sections (owner's request 2026-11-17): the sections named (the code action's), else the
 * ones the editor's selection touches — one: it and the next — become one section. The server
 * works the join out and checks the piece sounds as it did (lilysharp/joinSections); a refusal
 * says why. Applied as one edit (one undo).
 */
async function joinSections(uriArg?: string, sections?: string[]) {
    const client = server();
    const editor = vscode.window.activeTextEditor;
    const doc = uriArg
        ? vscode.workspace.textDocuments.find(d => sameDocumentUri(d.uri.toString(), uriArg))
        : editor?.document;
    if (!doc || doc.languageId !== 'lilysharp' || !client) {
        return;
    }
    const sel = editor && editor.document === doc ? editor.selection : undefined;
    try {
        const version = doc.version;
        const response = await vscode.window.withProgress(
            { location: vscode.ProgressLocation.Window, title: 'Lily#: checking the join…' },
            () => client!.sendRequest<{ Success: boolean; NewText?: string | null; Plan?: string | null; Error?: string | null; Version: number }>(
                'lilysharp/joinSections', {
                    textDocument: { uri: doc.uri.toString() },
                    sections: sections && sections.length >= 2 ? sections : null,
                    start: sel ? doc.offsetAt(sel.start) : 0,
                    end: sel ? doc.offsetAt(sel.end) : 0,
                }));
        if (!response.Success || response.NewText == null) {
            const refusal = modalText(response.Error ?? 'The sections were not joined.');
            await vscode.window.showWarningMessage(refusal.message, { modal: true, detail: refusal.detail });
            return;
        }
        if (doc.version !== version || response.Version !== version) {
            vscode.window.showWarningMessage('Lily#: the file changed while the join was being checked — run it again.');
            return;
        }
        const edit = new vscode.WorkspaceEdit();
        edit.replace(doc.uri, new vscode.Range(doc.positionAt(0), doc.positionAt(doc.getText().length)), response.NewText);
        await vscode.workspace.applyEdit(edit);
        vscode.window.setStatusBarMessage(`Lily#: ${response.Plan ?? 'joined'}`, 6000);
    } catch (err) {
        vscode.window.showErrorMessage(`Lily#: joining sections failed: ${err}`);
    }
}

/** The files beside a chart Join Bars in Pairs has written (their undo is saved too). */
const joinedRecordings = new Set<string>();

/**
 * Join Bars in Pairs (Orecchiante's request 21, owner's decisions 2026-11-21,
 * docs/join-bars-design.md): the bars of a chords row the selection touches, two by two. The
 * server works the join out and checks every note plays where it did (lilysharp/joinBars); a
 * refusal says why. The file beside the chart, `<stem>.orecchiante.json`, has the same bars
 * joined in the same edit (one undo across the two files) and is saved at once — its bars are
 * the n-th bar played, and a chart a bar shorter would play every bar after it against the
 * recording's next. Where the two did not line up before, the chart alone is joined if asked.
 */
async function joinBars(uriArg?: string, startArg?: number, endArg?: number) {
    const client = server();
    const editor = vscode.window.activeTextEditor;
    const doc = uriArg
        ? vscode.workspace.textDocuments.find(d => sameDocumentUri(d.uri.toString(), uriArg))
        : editor?.document;
    if (!doc || doc.languageId !== 'lilysharp' || !client) {
        return;
    }
    const sel = editor && editor.document === doc ? editor.selection : undefined;
    const start = startArg ?? (sel ? doc.offsetAt(sel.start) : 0);
    const end = endArg ?? (sel ? doc.offsetAt(sel.end) : 0);
    try {
        const version = doc.version;
        const response = await vscode.window.withProgress(
            { location: vscode.ProgressLocation.Window, title: 'Lily#: checking the joined bars…' },
            () => client!.sendRequest<{ Success: boolean; NewText?: string | null; Plan?: string | null; Error?: string | null;
                Played?: number[] | null; BarsPlayed: number; Version: number }>(
                'lilysharp/joinBars', {
                    textDocument: { uri: doc.uri.toString() },
                    start, end,
                    score: selectedRenders.get(doc.uri.toString()) || null,
                }));
        if (!response.Success || response.NewText == null) {
            const refusal = modalText(response.Error ?? 'The bars were not joined.');
            await vscode.window.showWarningMessage(refusal.message, { modal: true, detail: refusal.detail });
            return;
        }
        // The file beside the chart, as the editor has it if open.
        let recordingText: string | undefined;
        let recordingUri: vscode.Uri | undefined;
        if (doc.uri.scheme === 'file') {
            recordingUri = vscode.Uri.file(recordingPathFor(doc.uri.fsPath));
            const open = vscode.workspace.textDocuments.find(d => d.uri.fsPath.toLowerCase() === recordingUri!.fsPath.toLowerCase());
            try {
                recordingText = open ? open.getText() : Buffer.from(await vscode.workspace.fs.readFile(recordingUri)).toString('utf8');
            } catch {
                recordingText = undefined;   // (no file beside the chart)
            }
        }
        let joinedText: string | undefined;
        let note = '';
        if (recordingText !== undefined && recordingUri) {
            const rec = parseRecording(recordingText);
            const name = recordingUri.fsPath.split(/[\\/]/).pop();
            if ('error' in rec) {
                note = `; ${name} was left alone (${rec.error})`;
            } else if (rec.bars.length !== response.BarsPlayed) {
                const chartOnly = 'Join the Chart Only';
                const choice = await vscode.window.showWarningMessage(
                    `The chart plays ${response.BarsPlayed} bars and ${name} has ${rec.bars.length}, so which of its bars to join cannot be told. Join the chart's bars and leave ${name} as it is?`,
                    { modal: true }, chartOnly);
                if (choice !== chartOnly) {
                    return;
                }
                note = `; ${name} was left as it is`;
            } else {
                const joined = joinRecordingBars(rec, response.Played ?? []);
                if ('error' in joined) {
                    await vscode.window.showWarningMessage(`Lily#: ${name}: ${joined.error} — nothing was done.`, { modal: true });
                    return;
                }
                joinedText = serializeRecording(joined);
                note = `; ${name} too`;
            }
        }
        if (doc.version !== version || response.Version !== version) {
            vscode.window.showWarningMessage('Lily#: the file changed while the bars were being checked — run it again.');
            return;
        }
        const edit = new vscode.WorkspaceEdit();
        edit.replace(doc.uri, new vscode.Range(doc.positionAt(0), doc.positionAt(doc.getText().length)), response.NewText);
        if (joinedText !== undefined && recordingUri) {
            const target = await vscode.workspace.openTextDocument(recordingUri);
            edit.replace(recordingUri, new vscode.Range(target.positionAt(0), target.positionAt(target.getText().length)), joinedText);
        }
        if (!await vscode.workspace.applyEdit(edit)) {
            vscode.window.showWarningMessage('Lily#: the bars could not be joined (the edit was refused).');
            return;
        }
        if (joinedText !== undefined && recordingUri) {
            joinedRecordings.add(recordingUri.toString());
            await vscode.workspace.textDocuments.find(d => d.uri.toString() === recordingUri!.toString())?.save();
        }
        vscode.window.setStatusBarMessage(`Lily#: ${(response.Plan ?? 'joined').replace(/\.$/, '')}${note}.`, 8000);
    } catch (err) {
        vscode.window.showErrorMessage(`Lily#: joining bars failed: ${err}`);
    }
}

/**
 * Split Section Here (owner's request 2026-11-17): the section cut at the caret's bar line (or the
 * bar given by the code action), the rest a new section whose name is asked for (a free one
 * offered). The server cuts every part, chord row and words there and checks the piece sounds as
 * it did (lilysharp/cutSection); a refusal says why. One edit (one undo).
 */
async function splitSectionHere(uriArg?: string, section?: string, bars?: number, nameArg?: string,
    offsetArg?: number, snap = false) {
    const client = server();
    const editor = vscode.window.activeTextEditor;
    const doc = uriArg
        ? vscode.workspace.textDocuments.find(d => sameDocumentUri(d.uri.toString(), uriArg))
        : editor?.document;
    if (!doc || doc.languageId !== 'lilysharp' || !client) {
        return;
    }
    type CutAnswer = { Success: boolean; NewText?: string | null; Plan?: string | null; Error?: string | null;
        Section?: string | null; Bars: number; Suggested?: string | null; Version: number };
    const ask = (name: string | null, s?: string, b?: number) => client!.sendRequest<CutAnswer>('lilysharp/cutSection', {
        textDocument: { uri: doc.uri.toString() },
        section: s ?? null, bars: b ?? 0,
        // (the preview's right-click hands its own place: a bar's first note, or the end of its text to snap on to its bar line)
        offset: typeof offsetArg === 'number' ? offsetArg : editor && editor.document === doc ? doc.offsetAt(editor.selection.active) : 0,
        snap,
        name,
    });
    try {
        const version = doc.version;
        const where = await ask(null, section, bars);
        if (where.Error || !where.Section) {
            const refusal = modalText(where.Error ?? 'The section was not split.');
            await vscode.window.showWarningMessage(refusal.message, { modal: true, detail: refusal.detail });
            return;
        }
        // (a name handed in — a script's — is taken as it is)
        const name = nameArg ?? await vscode.window.showInputBox({
            title: `Lily#: split section ${where.Section} after bar ${where.Bars}`,
            prompt: 'The name of the new section (the bars after the cut)',
            value: where.Suggested ?? '',
            validateInput: v => /^[A-Za-z_][A-Za-z0-9_\-]*$/.test(v.trim()) ? undefined : 'A letter, then letters, digits, _ and -',
        });
        if (!name) {
            return;
        }
        const response = await vscode.window.withProgress(
            { location: vscode.ProgressLocation.Window, title: 'Lily#: checking the split…' },
            () => ask(name.trim(), where.Section ?? undefined, where.Bars));
        if (!response.Success || response.NewText == null) {
            const refusal = modalText(response.Error ?? 'The section was not split.');
            await vscode.window.showWarningMessage(refusal.message, { modal: true, detail: refusal.detail });
            return;
        }
        if (doc.version !== version || response.Version !== version) {
            vscode.window.showWarningMessage('Lily#: the file changed while the split was being checked — run it again.');
            return;
        }
        const edit = new vscode.WorkspaceEdit();
        edit.replace(doc.uri, new vscode.Range(doc.positionAt(0), doc.positionAt(doc.getText().length)), response.NewText);
        await vscode.workspace.applyEdit(edit);
        vscode.window.setStatusBarMessage(`Lily#: ${response.Plan ?? 'split'}`, 6000);
    } catch (err) {
        vscode.window.showErrorMessage(`Lily#: splitting the section failed: ${err}`);
    }
}

interface ExtractPhraseResponse {
    Success: boolean;
    NewText: string | null;
    Error: string | null;
}

/**
 * Extract-phrase refactoring: lifts the section music at the caret (or the whole
 * measures the selection touches) into a top-level `phrase NAME { … }` and
 * replaces it with the reference — the server verifies the result sounds
 * identical (MIDI compare) or refuses without changes.
 */
async function extractPhrase() {
    const client = server();
    const editor = vscode.window.activeTextEditor;
    if (!editor || editor.document.languageId !== 'lilysharp') {
        vscode.window.showErrorMessage('Lily#: open a .lys file to extract a phrase.');
        return;
    }
    if (!client) {
        vscode.window.showErrorMessage('Lily#: language server not ready.');
        return;
    }

    const name = await vscode.window.showInputBox({
        prompt: 'Name for the extracted phrase (referenced as NAME, NAME\', …)',
        value: 'theme',
        validateInput: v => /^[\p{L}_][\p{L}\p{N}_]*$/u.test(v)
            ? null : 'A phrase name is an identifier (letters, digits, _)',
    });
    if (!name) { return; }

    const doc = editor.document;
    const sel = editor.selection;
    try {
        const response = await rewriteRequest<ExtractPhraseResponse>(doc, 'lilysharp/extractPhrase', {
            textDocument: { uri: doc.uri.toString() },
            selectionStart: doc.offsetAt(sel.start),
            selectionEnd: doc.offsetAt(sel.end),
            name,
        });
        if (!response) { return; }
        if (response.Success && response.NewText != null) {
            const fullRange = new vscode.Range(
                doc.positionAt(0), doc.positionAt(doc.getText().length));
            const edit = new vscode.WorkspaceEdit();
            edit.replace(doc.uri, fullRange, response.NewText);
            await vscode.workspace.applyEdit(edit);
            vscode.window.showInformationMessage(
                `Lily#: extracted phrase '${name}' — reference it from other parts (${name}, ${name}', …).`);
        } else {
            vscode.window.showErrorMessage(`Lily#: ${response.Error}`);
        }
    } catch (err) {
        vscode.window.showErrorMessage(`Lily#: extract phrase failed: ${err}`);
    }
}

interface ChordTrackEdit {
    StartLine: number;
    StartChar: number;
    EndLine: number;
    EndChar: number;
    NewText: string;
}
interface AddChordTrackResponse {
    Edits: ChordTrackEdit[] | null;
    Error: string | null;
    Info: string | null;
}

/**
 * Adds a chords track read from the score the preview shows (owner's decisions 2026-10-26,
 * docs/harmony-analysis-design.md): the chords its parts play, a melody alone harmonized. One
 * pick, how rich the chosen chords are: what is read is the score's choice — a score of the
 * melody's staff alone harmonizes the melody (owner 2026-10-26: one menu, not a second asking
 * "the parts or the melody only"). The server writes the track and places it in that score
 * above its top staff, a row of its own. The result is a starting point to edit (Ctrl+Z takes
 * it back).
 */
async function addChordTrack(context: vscode.ExtensionContext) {
    const client = server();
    const editor = vscode.window.activeTextEditor;
    if (!editor || editor.document.languageId !== 'lilysharp') {
        vscode.window.showErrorMessage('Lily#: open a .lys file to add a chord track.');
        return;
    }
    if (!client) {
        vscode.window.showErrorMessage('Lily#: language server not ready.');
        return;
    }

    const doc = editor.document;
    const renderName = selectedRenders.get(doc.uri.toString()) || null;
    const scoreLabel = renderName ? `score ${renderName}` : 'the score the preview shows';
    // How rich the chords chosen for a melody are (owner's request 2026-10-26: the variant's
    // size, not a random seed), plainest first (owner's request 2026-10-26); the one picked
    // last is where the selection starts.
    const richnesses = [
        { label: 'Plain', richness: 'plain', detail: 'Triads only.' },
        { label: 'Standard', richness: 'standard',
          detail: 'Sevenths where they are played; a chord resolving a fifth down as its V7 (G → C as G7 → C).' },
        { label: 'Rich', richness: 'rich', detail: 'Standard, and the ii of a ii–V as IIm7 (Dm → G7 as Dm7 → G7).' },
    ];
    const last = context.globalState.get<string>('lilysharp.chordTrack.richness') ?? 'standard';
    const rich = await new Promise<typeof richnesses[number] | undefined>(resolve => {
        const quickPick = vscode.window.createQuickPick<typeof richnesses[number]>();
        quickPick.items = richnesses;
        quickPick.activeItems = richnesses.filter(r => r.richness === last);
        quickPick.title = `Add Chord Track — reads ${scoreLabel}`;
        quickPick.placeholder = 'How rich are the chords chosen where a melody sounds alone? (chords the parts play are written as played)';
        let chosen: typeof richnesses[number] | undefined;
        quickPick.onDidAccept(() => { chosen = quickPick.selectedItems[0]; quickPick.hide(); });
        quickPick.onDidHide(() => { quickPick.dispose(); resolve(chosen); });
        quickPick.show();
    });
    if (!rich) {
        return;
    }
    await context.globalState.update('lilysharp.chordTrack.richness', rich.richness);
    try {
        const response = await client.sendRequest<AddChordTrackResponse>('lilysharp/addChordTrack', {
            textDocument: { uri: doc.uri.toString() }, renderName, richness: rich.richness
        });
        if (response.Edits && response.Edits.length > 0) {
            const edit = new vscode.WorkspaceEdit();
            for (const e of response.Edits) {
                const pos = new vscode.Position(e.StartLine, e.StartChar);
                const end = new vscode.Position(e.EndLine, e.EndChar);
                edit.replace(doc.uri, new vscode.Range(pos, end), e.NewText);
            }
            await vscode.workspace.applyEdit(edit);
            const note = response.Info ? ` ${response.Info}` : '';
            vscode.window.showInformationMessage(
                `Lily#: added a chord track — a starting point, edit as needed.${note}`);
        } else {
            vscode.window.showErrorMessage(`Lily#: ${response.Error ?? 'could not add a chord track.'}`);
        }
    } catch (err) {
        vscode.window.showErrorMessage(`Lily#: add chord track failed: ${err}`);
    }
}

interface SectionKeyGuess {
    Section: string;
    CurrentKey: string | null;
    Candidates: { Key: string; Score: number }[];
}
interface GuessSectionKeyResponse {
    Sections: SectionKeyGuess[] | null;
    Error: string | null;
}
interface SetSectionKeyResponse {
    NewText: string | null;
    Error: string | null;
}

/** `fis major` → `F♯ major`, for the picker's description. */
function displayKey(key: string): string {
    const [tonic, mode] = key.split(' ');
    const accidental = tonic.endsWith('isis') ? '𝄪' : tonic.endsWith('eses') ? '𝄫'
        : tonic.endsWith('is') ? '♯' : tonic.length > 1 && tonic.endsWith('es') ? '♭' : '';
    return `${tonic[0].toUpperCase()}${accidental} ${mode}`;
}

/**
 * Asks the server for a rewrite of the whole document, and gives the answer back only if the
 * document is still the text it was asked of: undefined (said to the writer) when it changed
 * while the request was out. The answer replaces the whole file, so applied to a later text it
 * took back whatever was typed meanwhile (review of session 1291: Regroup, Convert Octaves,
 * Remove String Numbers and the others asked and replaced with no such look; Join and Split had it).
 */
async function rewriteRequest<T>(doc: vscode.TextDocument, method: string, params: object): Promise<T | undefined> {
    const client = server();
    const version = doc.version;
    const response = await client!.sendRequest<T>(method, params);
    if (rewriteIsStale(version, doc.version)) {
        vscode.window.showWarningMessage('Lily#: the file changed while this was being worked out — nothing was changed; run it again.');
        return undefined;
    }
    return response;
}

/** Replaces the whole document with the server's rewrite. */
async function replaceDocument(doc: vscode.TextDocument, text: string) {
    const fullRange = new vscode.Range(doc.positionAt(0), doc.positionAt(doc.getText().length));
    const edit = new vscode.WorkspaceEdit();
    edit.replace(doc.uri, fullRange, text);
    await vscode.workspace.applyEdit(edit);
}

/** The .lys editor in front with the server ready, or undefined after saying why not. */
function lysEditorFor(what: string): vscode.TextEditor | undefined {
    const client = server();
    const editor = vscode.window.activeTextEditor;
    if (!editor || editor.document.languageId !== 'lilysharp') {
        vscode.window.showErrorMessage(`Lily#: open a .lys file to ${what}.`);
        return undefined;
    }
    if (!client) {
        vscode.window.showErrorMessage('Lily#: language server not ready.');
        return undefined;
    }
    return editor;
}

/** The key guesses for the sections at the caret or selection, or undefined after an error. */
async function sectionKeyGuesses(editor: vscode.TextEditor): Promise<SectionKeyGuess[] | undefined> {
    const client = server();
    const doc = editor.document;
    const sel = editor.selection;
    const response = await client!.sendRequest<GuessSectionKeyResponse>('lilysharp/guessSectionKey', {
        textDocument: { uri: doc.uri.toString() },
        selectionStart: doc.offsetAt(sel.start),
        selectionEnd: doc.offsetAt(sel.end),
    });
    if (!response.Sections || response.Sections.length === 0) {
        vscode.window.showErrorMessage(`Lily#: ${response.Error ?? 'could not guess a key.'}`);
        return undefined;
    }
    return response.Sections;
}

/** The review's findings in the Problems panel; made the first time a score is reviewed. */
let reviewPoints: vscode.DiagnosticCollection | undefined;

/**
 * The review with no AI (docs/review-tool-design.md §5): its findings as Information in the Problems
 * panel, their own source, until the score changes. The compiler's errors and warnings are there already.
 */
async function reviewScoreCommand(context: vscode.ExtensionContext) {
    const client = server();
    const editor = lysEditorFor('review a score');
    if (!editor) { return; }
    const doc = editor.document;
    if (!reviewPoints) {
        const points = reviewPoints = vscode.languages.createDiagnosticCollection('Lily# review');
        context.subscriptions.push(points, vscode.workspace.onDidChangeTextDocument(e => points.delete(e.document.uri)));
    }
    try {
        const text = doc.getText();
        const resp = await client!.sendRequest<{ Report: string }>('lilysharp/reviewScore', { Text: text });
        const { compiles, points } = reviewPointsOf(resp.Report, text);
        reviewPoints.set(doc.uri, points.map(p => {
            const line = Math.min(p.line, doc.lineCount - 1);
            const diagnostic = new vscode.Diagnostic(doc.lineAt(line).range, p.message, vscode.DiagnosticSeverity.Information);
            diagnostic.source = 'Lily# review';
            return diagnostic;
        }));
        if (!compiles) {
            void vscode.window.showWarningMessage('Lily#: the score has errors to fix before it can be reviewed as music.');
        } else if (points.length === 0) {
            void vscode.window.showInformationMessage('Lily#: the review found nothing to look at.');
        } else {
            void vscode.commands.executeCommand('workbench.actions.view.problems');
        }
    } catch (e) {
        void vscode.window.showErrorMessage(`Lily#: the review failed: ${e instanceof Error ? e.message : String(e)}`);
    }
}

/**
 * Guesses the key of the section(s) at the caret or selection from their notes, and
 * offers the ranked keys in a picker; the one picked is written as the key the section
 * starts in (an existing starting key is rewritten), then the notes spelled against it
 * are offered for respelling. Escape leaves the file untouched.
 */
async function guessSectionKey() {
    const editor = lysEditorFor('guess a section\'s key');
    if (!editor) { return; }
    const doc = editor.document;
    try {
        const sections = await sectionKeyGuesses(editor);
        if (!sections) { return; }

        for (const section of sections) {
            const items = section.Candidates.map((c, i) => ({
                label: `key ${c.Key}`,
                description: [
                    displayKey(c.Key),
                    `score ${c.Score.toFixed(2)}`,
                    i === 0 ? 'best guess' : undefined,
                    c.Key === section.CurrentKey ? 'current' : undefined,
                ].filter(Boolean).join(' · '),
                key: c.Key,
            }));
            const current = section.CurrentKey ? `now ${section.CurrentKey}` : 'no key stated at its start';
            const picked = await vscode.window.showQuickPick(items, {
                title: `Lily#: key of section ${section.Section} (${current})`,
                placeHolder: 'Pick a key to write at the start of the section (Escape: leave it as it is)',
            });
            if (!picked) { continue; }

            if (picked.key !== section.CurrentKey) {
                const set = await rewriteRequest<SetSectionKeyResponse>(doc, 'lilysharp/setSectionKey', {
                    textDocument: { uri: doc.uri.toString() },
                    section: section.Section,
                    key: picked.key,
                });
                if (!set) { return; }
                if (set.NewText == null) {
                    vscode.window.showErrorMessage(`Lily#: ${set.Error ?? 'could not write the key.'}`);
                    return;
                }
                await replaceDocument(doc, set.NewText);
            }
            await offerRespell(doc, section.Section, picked.key, false);
        }
    } catch (err) {
        vscode.window.showErrorMessage(`Lily#: guess section key failed: ${err}`);
    }
}

interface RespellChange {
    Id: string;
    From: string;
    To: string;
    Count: number;
}
interface RespellToKeyResponse {
    NewText: string | null;
    Error: string | null;
    Changed: number;
}

/**
 * Offers the respellings that put a section's notes in `key` (`fis` → `ges` in G♭ major),
 * every one checked; the checked ones are applied, each note keeping its pitch. With
 * nothing to respell it says so only when asked directly (`tellIfNone`).
 */
async function offerRespell(doc: vscode.TextDocument, section: string, key: string, tellIfNone: boolean) {
    const client = server();
    const plan = await client!.sendRequest<{ Changes: RespellChange[] }>('lilysharp/planRespell', {
        textDocument: { uri: doc.uri.toString() }, section, key,
    });
    if (plan.Changes.length === 0) {
        if (tellIfNone) {
            vscode.window.showInformationMessage(`Lily#: section ${section} is already spelled for ${key}.`);
        }
        return;
    }
    const items = plan.Changes.map(c => ({
        label: `${c.From} → ${c.To}`,
        description: `${c.Count} note${c.Count === 1 ? '' : 's'}`,
        picked: true,
        id: c.Id,
    }));
    const chosen = await vscode.window.showQuickPick(items, {
        title: `Lily#: respell section ${section} for ${key} (${displayKey(key)})`,
        placeHolder: 'Uncheck what to keep as written; Enter respells the rest (Escape: none)',
        canPickMany: true,
    });
    if (!chosen || chosen.length === 0) { return; }

    const response = await rewriteRequest<RespellToKeyResponse>(doc, 'lilysharp/respellToKey', {
        textDocument: { uri: doc.uri.toString() }, section, key,
        accepted: chosen.map(c => c.id),
    });
    if (!response) { return; }
    if (response.NewText == null) {
        vscode.window.showErrorMessage(`Lily#: ${response.Error ?? 'could not respell.'}`);
        return;
    }
    await replaceDocument(doc, response.NewText);
    vscode.window.showInformationMessage(
        `Lily#: respelled ${response.Changed} note${response.Changed === 1 ? '' : 's'} in section ${section} for ${key}.`);
}

/**
 * Respells the notes of the section(s) at the caret or selection to the key each starts
 * in — or, where no key is stated, to the guessed one (the picker's title says which).
 */
async function respellNotesToKey() {
    const editor = lysEditorFor('respell notes');
    if (!editor) { return; }
    try {
        const sections = await sectionKeyGuesses(editor);
        if (!sections) { return; }
        for (const section of sections) {
            const key = section.CurrentKey && section.CurrentKey !== 'custom'
                ? section.CurrentKey : section.Candidates[0]?.Key;
            if (!key) { continue; }
            if (!section.CurrentKey) {
                vscode.window.showInformationMessage(
                    `Lily#: section ${section.Section} states no key; respelling for the guessed ${key}.`);
            }
            await offerRespell(editor.document, section.Section, key, true);
        }
    } catch (err) {
        vscode.window.showErrorMessage(`Lily#: respell notes failed: ${err}`);
    }
}


interface HarmonyPartChoices {
    Parts: string[];
    Part: string | null;
    Sections: string[];
    Taken: string[];
}

interface HarmonyPartResponse {
    NewText: string | null;
    Error: string | null;
}

/** The harmonies "Write a Harmony Part…" offers: an interval along the key, an octave, or both. */
const HARMONY_CHOICES: { label: string; description: string; interval: number; octaves: number }[] = [
    { label: 'A third above', description: 'up 3, along the key', interval: 3, octaves: 0 },
    { label: 'A third below', description: 'down 3', interval: -3, octaves: 0 },
    { label: 'A sixth above', description: 'up 6', interval: 6, octaves: 0 },
    { label: 'A sixth below', description: 'down 6', interval: -6, octaves: 0 },
    { label: 'A fourth above', description: 'up 4', interval: 4, octaves: 0 },
    { label: 'A fourth below', description: 'down 4', interval: -4, octaves: 0 },
    { label: 'A fifth above', description: 'up 5', interval: 5, octaves: 0 },
    { label: 'A fifth below', description: 'down 5', interval: -5, octaves: 0 },
    { label: 'An octave above', description: 'octave 1', interval: 0, octaves: 1 },
    { label: 'An octave below', description: 'octave -1', interval: 0, octaves: -1 },
    { label: 'A tenth above', description: 'up 10 (a third and an octave)', interval: 10, octaves: 0 },
    { label: 'A tenth below', description: 'down 10', interval: -10, octaves: 0 },
];

/**
 * "Lily#: Write a Harmony Part…" (owner's decision 2026-11-12): a new part of another part's
 * notes an interval away along the key — a second violin a third under the first, a horn an
 * octave below — written into the file beside the part it follows, to be mended by hand where
 * the interval is not the note. Asks which part (the caret's first), how far, which sections
 * and the new part's name; the server writes it (lilysharp/harmonyPart).
 */
async function writeHarmonyPart() {
    const client = server();
    const editor = lysEditorFor('write a harmony part');
    if (!editor) { return; }
    const doc = editor.document;
    try {
        const choices = await client!.sendRequest<HarmonyPartChoices>('lilysharp/harmonyPartChoices', {
            textDocument: { uri: doc.uri.toString() }, offset: doc.offsetAt(editor.selection.active),
        });
        if (choices.Parts.length === 0) {
            vscode.window.showInformationMessage('Lily#: the score has no named part to write a harmony of.');
            return;
        }
        const parts = choices.Part ? [choices.Part, ...choices.Parts.filter(p => p !== choices.Part)] : choices.Parts;
        const from = parts.length === 1 ? parts[0] : await vscode.window.showQuickPick(parts, {
            title: 'Lily#: Write a Harmony Part (1/4)', placeHolder: 'The part the harmony follows',
        });
        if (!from) { return; }
        const how = await vscode.window.showQuickPick(HARMONY_CHOICES, {
            title: `Lily#: Write a Harmony Part (2/4) — of ${from}`, placeHolder: 'How far from its notes, along the key',
            matchOnDescription: true,
        });
        if (!how) { return; }
        let sections: string[] | undefined;
        if (choices.Sections.length > 1) {
            const picked = await vscode.window.showQuickPick(
                choices.Sections.map(s => ({ label: s, picked: true })), {
                    title: 'Lily#: Write a Harmony Part (3/4)', canPickMany: true,
                    placeHolder: 'The sections it plays in; it rests in the others',
                });
            if (!picked || picked.length === 0) { return; }
            sections = picked.length === choices.Sections.length ? undefined : picked.map(p => p.label);
        }
        let suggested = from + '2';
        for (let n = 3; choices.Taken.includes(suggested); n++) { suggested = from + n; }
        const name = await vscode.window.showInputBox({
            title: 'Lily#: Write a Harmony Part (4/4)', prompt: 'The new part\'s name', value: suggested,
            validateInput: v => !/^[A-Za-z_][A-Za-z0-9_]*$/.test(v) ? 'Letters, digits and _; it begins with a letter.'
                : choices.Taken.includes(v) ? `'${v}' is in the score already.` : undefined,
        });
        if (!name) { return; }
        const response = await rewriteRequest<HarmonyPartResponse>(doc, 'lilysharp/harmonyPart', {
            textDocument: { uri: doc.uri.toString() }, from, name,
            interval: how.interval, octaves: how.octaves, sections,
        });
        if (!response) { return; }
        if (response.NewText == null) {
            vscode.window.showErrorMessage(`Lily#: ${response.Error ?? 'the harmony could not be written.'}`);
            return;
        }
        await replaceDocument(doc, response.NewText);
        vscode.window.showInformationMessage(`Lily#: wrote part ${name}, ${how.label.toLowerCase()} ${from}.`);
    } catch (err) {
        vscode.window.showErrorMessage(`Lily#: writing the harmony part failed: ${err}`);
    }
}

/**
 * "Write NAME's notes out as a part" — the quick fix on a named singer's line: what the lines
 * of that singer say (a third above in the Chorus) becomes a part called as the singer is, its
 * words a track of its own, and the lines one line that sings it (lilysharp/harmonyPart).
 */
async function writeSingerPart(uri?: string, singer?: string) {
    const client = server();
    if (!client) {
        vscode.window.showErrorMessage('Lily#: language server not ready.');
        return;
    }
    const doc = uri !== undefined
        ? vscode.workspace.textDocuments.find(d => sameDocumentUri(d.uri.toString(), uri))
        : vscode.window.activeTextEditor?.document;
    if (!doc || doc.languageId !== 'lilysharp' || !singer) {
        vscode.window.showInformationMessage('Lily#: put the caret on a named singer\'s line (singer carol … sings …) and use the light bulb.');
        return;
    }
    try {
        const response = await rewriteRequest<HarmonyPartResponse>(doc, 'lilysharp/harmonyPart', {
            textDocument: { uri: doc.uri.toString() }, singer,
        });
        if (!response) { return; }
        if (response.NewText == null) {
            vscode.window.showErrorMessage(`Lily#: ${response.Error ?? 'the notes could not be written out.'}`);
            return;
        }
        await replaceDocument(doc, response.NewText);
        vscode.window.showInformationMessage(`Lily#: wrote what ${singer} sings as part ${singer}.`);
    } catch (err) {
        vscode.window.showErrorMessage(`Lily#: writing the singer's part failed: ${err}`);
    }
}
