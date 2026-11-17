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
import * as fs from 'fs';
import * as path from 'path';
import * as cp from 'child_process';
import * as os from 'os';
import {
    LanguageClient,
    LanguageClientOptions,
    ServerOptions,
    TransportKind
} from 'vscode-languageclient/node';
import { registerAiTransform, showCandidateSounding, tellCandidateAudioBlocked } from './aiTransform';
import { CandidateSounds, CandidateVoice, CandidateVoices, candidateSilencedTrack, candidateSings } from './aiTransformCore';
import { registerAiComplete } from './aiComplete';
import { findCopilotLogFrom, pickAiModel, rememberRefusalsIn } from './modelClient';
import { reviewPointsOf } from './reviewCore';
import { chatPointer } from './chatPointerCore';
import { registerSmartTyping } from './smartTyping';
import { registerPageText } from './pageText';
import { registerStaffGroupWrap, staffGroupsOffered } from './staffGroupWrap';
import { registerStepAudition } from './stepAudition';
import { registerCaretInfo } from './caretInfo';
import { registerExportBatch } from './exportBatch';
import { OmrImportDeps, importFromImage } from './omrImport';
import { DecodeResult, attachRecording, followCopies, followRenames, keepRecordingSettings, sendRecording, useRecordingSettingsStore } from './recording';
import { TimedNote, soundingOf, soundingWait, startSecondsAt } from './playbackStart';
import { convertRecording, registerDettatoContext } from './dettato';
import { recordingPathFor } from './recordingCore';
import { normalizeAbRanges } from './abRangesCore';
import { registerOmrContexts } from './omrReader';
import { OmrScanDeps, hasScan, previewHoversBar, previewReady, previewShowsBar, registerScanFollow, scanBarAt, scanColumnOf, scanSideOf, scanThresholdOf, showScan } from './omrScan';
import { registerOmrAiFix } from './omrAiFix';
import { registerVerifyOnSave } from './omrVerify';
import { markdownItExtensionApi } from './markdownFence';
import { PreviewSession } from './previewSession';
import { svgPostKey, pagesSummary, SvgPages, previewOnEditorShown, sourceGroupToReveal, newPreviewColumn, scoreGroupFor, sourceGroupToOpen, SourceTab, jumpOffsetFor, chordSpans, chordAtCaret, soundingLight, SoundingSession, auditionBarRange, messageChanged, rewriteIsStale } from './previewCore';
import { textFontFaceCss, textFontsRoot } from './scoreFonts';
import { getPreviewHtml, SynthUrls } from './previewHtml';
import { SplitSectionsChoice, modalText, splitChoiceItems } from './splitSectionsCore';
import { onAngleChord, onShapeItem, shapeItemAt, sameDocumentUri } from './shapeItemCore';
import { caretMenuContext, fileMenuContext, selectionSpansSections } from './menuContextCore';

/** A menu context key, set only when it changes (setContext is a round trip to the workbench). */
const menuContextNow = new Map<string, boolean>();
function setMenuContext(key: string, value: boolean): void {
    if (menuContextNow.get(key) === value) { return; }
    menuContextNow.set(key, value);
    void vscode.commands.executeCommand('setContext', key, value);
}
import { TodoMark, parseStarts, todoDeletions } from './todoResolveCore';
import { cleanHistory, forgetFont, recordFont, samePath, shownFonts } from './soundFontHistoryCore';
import { AudioFormat, AudioJob, renderAudioFiles } from './audioRender';
import { PREVIEW_MASTER, VoiceTrack, WAV_SAMPLE_RATE, decodeWav } from './audioRenderCore';
import { recordedExportPlan } from './recordingExportCore';
import { PracticeState, VOICE_LEAD_S, exportPlan, levelOf, practiceSummary, soundStart, voiceEntries } from './webview/playbackCore';
import { DrawFrames, FramesAnswer, ffmpegRuns, writeVideos } from './videoExport';
import { ffmpegMissing, ffmpegProgram } from './videoExportCore';
import { CantoriaExtensionBackend, SingerProcess, VoiceRenderer } from './singer';
import { VOICE_INSTALL_GUIDE, leftToTheInstrument, SINGER_NAME, SINGER_LANGUAGE, onSingerGender, singValueChoices, singerGoesOnToSings, singerLineOf, singerSettingSlot, singMarkSlot, singersWritten, singParameterDetail, singProblems, SingSettingDto, SingParameter, SingProgress, CANTORIA_EXTENSION_ID, PREPARE_IDLE_MS, PREPARE_TIMEOUT_MS, PrepareMode, PrepareQueue, StageClock, SungVoiceDto, sungKey, VOICE_RENDER_FIRST_RATE, VOICE_SAMPLE_VERSION, VoiceSampleQueue, byVoiceId, cantoriaApiOf, fitsGender, learnRenderRate, prepareModeOf, afterSingerWord, voiceMenuLabels, voiceMenuKey, voiceMenuTitleOf, noLanguageGuide, noVoiceGuide, printedWordsNote, singerChoices, VoiceGuide, singerLanguageColumn, singerValueAt, singsLanguage, stepVoice, sungSeconds, voiceRenderEstimateMs, voiceSampleFileName, voiceSampleKey, sampleOctave, parseVoiceSampleKey, voiceSampleScore, writesSinger, unsingableGuide, unsingableReason } from './singerCore';
import { VoiceProblems, engineFailureProblem, engineSaid, singerFailureText, voiceWho, wordsProblems } from './singerCore';

// True if `cmd` resolves on PATH (used to give a clear error when the
// framework-dependent dev server needs `dotnet` but it is not installed).
function commandExists(cmd: string): boolean {
    const probe = process.platform === 'win32' ? 'where' : 'which';
    try {
        return cp.spawnSync(probe, [cmd], { stdio: 'ignore' }).status === 0;
    } catch {
        return false;
    }
}

let client: LanguageClient;
let clientReady = false;
let clientReadyPromise: Promise<void>;
// Starts the language client ONCE, on the first need for it — a .lys opened, a lys
// fence in a Markdown preview to draw, an export — see activate(). The extension
// activates on Markdown too (for the fence), and a .md with no score in it must
// not cost a .NET process.
let startClient: () => void = () => { /* set in activate */ };
function ensureClientReady(): Promise<void> {
    startClient();
    return clientReadyPromise;
}
const previewPanels = new Map<string, vscode.WebviewPanel>();
// Each preview page's practice menu (tempo, parts, metronome, count-in), as the page last said:
// the WAV, MP3 and MP4 exports of the file a page shows play under it (owner's request
// 2026-11-07). A page's, not a file's — the follower keeps its menu as it turns to another file.
const previewPractice = new WeakMap<vscode.WebviewPanel, PracticeState>();
/** The practice menu of the preview showing `uri`; undefined when none shows it. */
function practiceFor(uri: string): PracticeState | undefined {
    const panel = previewPanels.get(uri);
    return panel ? previewPractice.get(panel) : undefined;
}
// The follower: the one preview that is not locked. It turns to whichever .lys comes to the
// front, the Markdown preview's way (owner's decision 2026-10-12), so opening another score
// reuses the pane instead of adding a tab. Every other panel is locked to its own file.
let followerPanel: vscode.WebviewPanel | undefined;
// Panels whose page is playing: the follower turns only once the music stops, to the last
// .lys that came to the front meanwhile (pendingFollow) — a turn would cut the piece off.
const playingPanels = new Set<vscode.WebviewPanel>();
// The preview whose Play the MIDI output is sounding. The server plays that list on a thread of its
// own, so a closed preview takes nothing with it: closing this one stops it (owner's request
// 2026-11-02), and closing another leaves it playing.
let midiPlaybackPanel: vscode.WebviewPanel | undefined;
// The text of what a preview is playing, lit in the editor (owner's request 2026-11-15: as
// Shosa's editor lights the word being performed). Each playing panel's file, and the version
// of its text when the first note was lit: the notes are those of the text Play read, so an
// edit since has moved every offset after it and nothing more is lit until the next Play.
let soundingMark: vscode.TextEditorDecorationType | undefined;
const soundingLit = new Map<vscode.WebviewPanel, SoundingSession>();

/**
 * Lights, in every visible editor of `uri`, the text of the notes sounding at `positions`
 * (their source offsets, as the preview's follow-along has them), and nothing with none.
 * `ended`: the playback stopped — the next one starts from the text as it then is.
 */
function showSoundingSource(panel: vscode.WebviewPanel, uri: string | undefined, positions: readonly number[], ended: boolean): void {
    const mark = soundingMark ??= vscode.window.createTextEditorDecorationType({
        backgroundColor: new vscode.ThemeColor('editor.findMatchHighlightBackground'),
        borderRadius: '3px',
    });
    const editorsOf = (u: string | undefined) => vscode.window.visibleTextEditors.filter(e => e.document.uri.toString() === u);
    const clear = (u: string | undefined) => { for (const e of editorsOf(u)) { e.setDecorations(mark, []); } };
    const lit = soundingLit.get(panel);
    if (lit && lit.uri !== uri) {
        clear(lit.uri);   // the follower turned to another file while this one was lit
    }
    const editors = editorsOf(uri);
    if (!uri || editors.length === 0) {
        if (ended) { soundingLit.delete(panel); }
        return;
    }
    const doc = editors[0].document;
    const on = vscode.workspace.getConfiguration('lilysharp').get<boolean>('playback.highlightSource', true);
    const { session, ranges } = soundingLight(lit, uri, doc.version, () => doc.getText(), positions, ended, on);
    if (session) { soundingLit.set(panel, session); } else { soundingLit.delete(panel); }
    const lights = ranges.map(([start, end]) => new vscode.Range(doc.positionAt(start), doc.positionAt(end)));
    for (const e of editors) { e.setDecorations(mark, lights); }
}
let pendingFollow: string | undefined;
// Files whose preview the writer closed this session: opening them again opens none.
const dismissedPreviews = new Set<string>();
// The follower went away by being LOCKED (to compare), not closed: the next .lys in front opens a
// second preview even on a tab switch (previewOnEditorShown). Any preview closing clears it.
let followerLocked = false;
// The URI a panel shows now — the map's key, which a follow or an untitled save moves.
function panelKey(panel: vscode.WebviewPanel): string | undefined {
    for (const [key, value] of previewPanels) {
        if (value === panel) { return key; }
    }
    return undefined;
}
function previewTitle(uri: string, panel: vscode.WebviewPanel): string {
    const name = path.basename(vscode.Uri.parse(uri).fsPath);
    return panel === followerPanel ? `Preview: ${name}` : `Preview: ${name} (locked)`;
}

// Turn a preview to another document (the follower following): the panel and its per-file
// state move to the new key, the page puts the old file's zoom and scroll away and draws the
// new one. The old file's picked score stays in selectedRenders for when it comes back.
function retargetPreview(panel: vscode.WebviewPanel, document: vscode.TextDocument, context: vscode.ExtensionContext) {
    const from = panelKey(panel);
    const to = document.uri.toString();
    if (from === undefined || from === to || previewPanels.has(to)) { return; }
    // A turn asked for by name overrides one waiting for the music to stop.
    pendingFollow = undefined;
    previewPanels.delete(from);
    previewPanels.set(to, panel);
    // The page holds neither file's picture as far as the dedup and the page versions go.
    sessionOf(panel).turned(false);
    panel.title = previewTitle(to, panel);
    outputChannel.appendLine(`Preview follows ${from} -> ${to}`);
    void panel.webview.postMessage({ type: 'retarget', from, to });
    updatePreviewContent(document, panel, context);
    if (scanDeps) { previewReady(to, scanDeps); }
}

// Lock or unlock the preview in front: an unlocked one becomes the follower, and the previous
// follower is locked to the file it shows — there is one follower at a time.
function setActivePreviewLocked(lock: boolean) {
    const panel = [...previewPanels.values()].find(p => p.active);
    if (!panel) { return; }
    const previous = followerPanel;
    if (lock) {
        if (followerPanel === panel) { followerPanel = undefined; followerLocked = true; }
    } else {
        followerPanel = panel;
        followerLocked = false;
    }
    for (const p of [panel, previous]) {
        const key = p && panelKey(p);
        if (p && key) { p.title = previewTitle(key, p); }
    }
    void vscode.commands.executeCommand('setContext', 'lilysharp.previewLocked', lock);
}

function isPreviewTab(tab: vscode.Tab): boolean {
    return tab.input instanceof vscode.TabInputWebview && tab.input.viewType.endsWith('lilysharpPreview');
}

// Move a score just opened in a preview's group (`from`) to `to`: shown there first, with the
// caret it had, then its tab in the preview's group closed — the document stays open in the
// other group, so nothing asks to be saved, an untitled New Score included.
// 'left' makes a new group left of the preview's (the group the tab just opened in is the active
// one); the groups' columns shift by one then, so the tab to close is found as the one sitting
// beside a preview, not by its old column.
async function moveScoreTab(uri: vscode.Uri, from: number, to: number | 'left', isPreview: boolean) {
    const key = uri.toString();
    const old = vscode.window.visibleTextEditors.find(e => e.document.uri.toString() === key && e.viewColumn === from);
    const isScoreTab = (t: vscode.Tab) => t.input instanceof vscode.TabInputText && t.input.uri.toString() === key;
    try {
        const doc = await vscode.workspace.openTextDocument(uri);
        if (to === 'left') {
            await vscode.commands.executeCommand('workbench.action.newGroupLeft');
        }
        await vscode.window.showTextDocument(doc, {
            viewColumn: to === 'left' ? vscode.ViewColumn.Active : to,
            selection: old?.selection,
            preview: isPreview,
        });
        for (const group of vscode.window.tabGroups.all) {
            const tab = group.tabs.some(isPreviewTab) ? group.tabs.find(isScoreTab) : undefined;
            if (tab) {
                await vscode.window.tabGroups.close(tab, true);
            }
        }
        outputChannel.appendLine(`Moved ${key} out of the preview's group ${from} to ${to}`);
    } catch (err) {
        outputChannel.appendLine(`Could not move ${key} out of the preview's group: ${err}`);
    }
}

// A preview brought to the front brings its source's tab forward in another group — only a
// tab already open there and behind others (sourceGroupToReveal), with the focus left on the
// preview. The source coming forward cannot turn anything back: it has its own preview.
function revealPreviewSource(panel: vscode.WebviewPanel) {
    const key = panelKey(panel);
    if (!key) { return; }
    const tabs: SourceTab[] = [];
    for (const group of vscode.window.tabGroups.all) {
        for (const tab of group.tabs) {
            if (tab.input instanceof vscode.TabInputText) {
                tabs.push({ column: group.viewColumn, uri: tab.input.uri.toString(), active: tab.isActive });
            }
        }
    }
    const column = sourceGroupToReveal(tabs, panel.viewColumn, key);
    if (column !== undefined) {
        void vscode.window.showTextDocument(vscode.Uri.parse(key), { viewColumn: column, preserveFocus: true });
    }
}
// A click on a preview whose score no editor shows (owner's request 2026-10-14: the .lys closed,
// the preview left open): the score is opened again — in the group that holds a tab of it, else
// where a score opened beside a preview goes (sourceGroupToOpen) — and the click lands there as
// in an open one. An untitled score that was closed is gone: nothing to open, as before.
async function openPreviewSource(panel: vscode.WebviewPanel, key: string): Promise<vscode.TextEditor | undefined> {
    const uri = vscode.Uri.parse(key);
    if (uri.scheme === 'untitled' && !vscode.workspace.textDocuments.some(d => d.uri.toString() === key)) {
        return undefined;
    }
    const tabs: SourceTab[] = [];
    for (const group of vscode.window.tabGroups.all) {
        for (const tab of group.tabs) {
            if (tab.input instanceof vscode.TabInputText) {
                tabs.push({ column: group.viewColumn, uri: tab.input.uri.toString(), active: tab.isActive });
            }
        }
    }
    const groups = vscode.window.tabGroups.all.map(g => ({ column: g.viewColumn, hasPreview: g.tabs.some(isPreviewTab) }));
    const target = sourceGroupToOpen(tabs, groups, key);
    try {
        const doc = await vscode.workspace.openTextDocument(uri);
        if (target === 'left') {
            // The preview was clicked, so its group is the active one.
            panel.reveal(panel.viewColumn, false);
            await vscode.commands.executeCommand('workbench.action.newGroupLeft');
        }
        outputChannel.appendLine(`Preview click: reopened ${key} in ${target}`);
        // A tab of its own, not VS Code's preview tab: the score is reopened to be worked on.
        return await vscode.window.showTextDocument(doc, { viewColumn: target === 'left' ? vscode.ViewColumn.Active : target, preview: false });
    } catch (err) {
        outputChannel.appendLine(`Preview click: could not reopen ${key}: ${err}`);
        return undefined;
    }
}

// The scan view's hooks (omrScan.ts, set in activate): a preview reports the bar it scrolled to.
let scanDeps: OmrScanDeps | undefined;
// What the host keeps of each preview panel (previewSession.ts): whether its page has loaded
// — a message posted before the webview has finished loading its HTML is DROPPED by VS Code;
// the webview script posts 'webviewReady' as its last statement, and content updates await
// that (with a timeout escape hatch) — the refresh waiting, the request on its way, and what
// the page shows. The panel's, not the document's: a panel that turns to another file
// (retargetPreview, migratePreviewKey) takes it along.
const previewSessions = new WeakMap<vscode.WebviewPanel, PreviewSession>();
function sessionOf(panel: vscode.WebviewPanel): PreviewSession {
    let session = previewSessions.get(panel);
    if (!session) { previewSessions.set(panel, session = new PreviewSession()); }
    return session;
}

// The `lilysharp` settings as last read, for the places a keystroke reaches (the refresh's
// delay, the singer's): read again when a setting changes (activate), not on each keystroke.
let lilysharpSettings: vscode.WorkspaceConfiguration | undefined;
function settings(): vscode.WorkspaceConfiguration {
    return lilysharpSettings ??= vscode.workspace.getConfiguration('lilysharp');
}

// The document a preview renders — REOPENED when its editor tab has been closed.
//
// ⚠️ Closing the last editor of a file disposes its TextDocument (and the language
// client sends didClose with it) while the PREVIEW stays on screen. Every path that
// looked the document up by walking `workspace.textDocuments` therefore found nothing
// and did nothing AT ALL: picking another score moved the picker and left the picture
// on the first one, with no banner, no dimmed score and no line in the log. That is
// indistinguishable, on screen, from "the preview will not switch scores" — and it is
// measured, not supposed: with the source tab closed the picker went to "Take 2" and
// the drawing stayed byte-for-byte the first score's (2026-09-07, driven over CDP).
// Reopening is silent (no editor is shown), it re-registers the document with the
// server, and updatePreviewContent already retries the "Document not found" window
// while that didOpen is in flight.
//
// An UNTITLED buffer is the one that cannot come back: its content lived in the editor
// that closed, and `openTextDocument` on that URI would hand back a NEW EMPTY document
// — a blank score drawn as if it were the writer's. That case says so instead.
async function previewDocument(uri: string): Promise<vscode.TextDocument | undefined> {
    const open = vscode.workspace.textDocuments.find(d => d.uri.toString() === uri);
    if (open && !open.isClosed) {
        return open;
    }
    const parsed = vscode.Uri.parse(uri);
    if (parsed.scheme !== 'file') {
        outputChannel.appendLine(`Preview source is gone (unsaved buffer closed): ${uri}`);
        vscode.window.showWarningMessage(
            'Lily#: this preview\'s source was an unsaved editor and it has been closed, '
            + 'so the score cannot be re-rendered. Reopen the file and press Ctrl+K V.');
        return undefined;
    }
    try {
        outputChannel.appendLine(`Reopening the preview's source (its editor was closed): ${uri}`);
        return await vscode.workspace.openTextDocument(parsed);
    } catch (err) {
        outputChannel.appendLine(`Cannot reopen ${uri}: ${err}`);
        vscode.window.showWarningMessage(
            `Lily#: the previewed file could not be reopened, so the score cannot be re-rendered (${err}).`);
        return undefined;
    }
}

// The preview the user is acting on: a context-menu command fires while its own
// webview holds focus, so the ACTIVE panel is the one that must receive it. With
// several previews open, posting to any other would act on the wrong score.
function postToActivePreview(type: string) {
    for (const panel of previewPanels.values()) {
        if (panel.active) {
            panel.webview.postMessage({ type });
            return;
        }
    }
}

/** The `@todo` marks of a document as the server lists them now (lilysharp/todos), or null when
 *  the server cannot say (not ready, or the list is of another version of the text). */
async function todoMarksOf(document: vscode.TextDocument): Promise<TodoMark[] | null> {
    if (!client || !clientReady) {
        return null;
    }
    try {
        const resp = await client.sendRequest<{ Todos?: TodoMark[]; Version: number; Error?: string | null }>(
            'lilysharp/todos', { textDocument: { uri: document.uri.toString() } });
        if (resp.Error || resp.Version !== document.version) {
            return null;
        }
        return resp.Todos ?? [];
    } catch {
        return null;
    }
}

/** The preview's Resolve This TODO / Resolve All TODOs (owner's request 2026-10-31): the marks
 *  that begin at `starts` — every mark when null — deleted from the shown document, as the
 *  editor's quick fix deletes one. The marks are re-read from the server first, so a mark that
 *  moved or went since the right-click is left alone. */
async function resolvePreviewTodos(starts: number[] | null, inEditor = false): Promise<void> {
    // (inEditor: the editor's Clean Up menu — the file in the active editor, owner's decision 2026-11-17)
    const editorDoc = vscode.window.activeTextEditor?.document;
    const shown = [...previewPanels.entries()].find(([, p]) => p.active);
    if (inEditor ? !editorDoc || editorDoc.languageId !== 'lilysharp' : !shown || (starts !== null && starts.length === 0)) {
        return;
    }
    const document = inEditor ? editorDoc! : await vscode.workspace.openTextDocument(vscode.Uri.parse(shown![0]));
    const marks = await todoMarksOf(document);
    if (!marks) {
        void vscode.window.showInformationMessage('The @todo marks could not be read — try again once the preview has rendered.');
        return;
    }
    const deletions = todoDeletions(marks, starts);
    if (deletions.length === 0) {
        return;
    }
    const edit = new vscode.WorkspaceEdit();
    for (const d of deletions) {
        edit.delete(document.uri, new vscode.Range(document.positionAt(d.start), document.positionAt(d.end)));
    }
    await vscode.workspace.applyEdit(edit);
}

/** Remove All @or (owner's request 2026-11-17): every `@or(...)` of the file deleted at once, each
 *  chord left as written — the score in the editor, else the one the active preview shows. The
 *  marks are read from the server (lilysharp/orMarks) for the text as it is now. */
async function removeAllOrMarks(): Promise<void> {
    let document = vscode.window.activeTextEditor?.document;
    if (!document || document.languageId !== 'lilysharp') {
        const shown = [...previewPanels.entries()].find(([, p]) => p.active);
        document = shown ? await vscode.workspace.openTextDocument(vscode.Uri.parse(shown[0])) : undefined;
    }
    if (!document || document.languageId !== 'lilysharp' || !client || !clientReady) {
        return;
    }
    let marks: { Start: number; End: number }[] | null = null;
    try {
        const resp = await client.sendRequest<{ Marks?: { Start: number; End: number }[]; Version: number; Error?: string | null }>(
            'lilysharp/orMarks', { textDocument: { uri: document.uri.toString() } });
        marks = !resp.Error && resp.Version === document.version ? (resp.Marks ?? []) : null;
    } catch {
        marks = null;
    }
    if (marks === null) {
        void vscode.window.showInformationMessage('The @or marks could not be read — try again in a moment.');
        return;
    }
    if (marks.length === 0) {
        vscode.window.setStatusBarMessage('Lily#: no @or in this file.', 4000);
        return;
    }
    const edit = new vscode.WorkspaceEdit();
    for (const m of marks) {
        edit.delete(document.uri, new vscode.Range(document.positionAt(m.Start), document.positionAt(m.End)));
    }
    await vscode.workspace.applyEdit(edit);
    vscode.window.setStatusBarMessage(`Lily#: removed ${marks.length} @or — the chords written stay.`, 5000);
}

// Re-key a live preview from oldUri to newUri (an untitled score saved to a file):
// the panel and every per-document map entry move together so the preview keeps
// updating under the saved file's URI instead of the discarded untitled one.
function migratePreviewKey(oldUri: string, newUri: string) {
    const panel = previewPanels.get(oldUri);
    if (!panel || oldUri === newUri) { return; }
    previewPanels.delete(oldUri);
    previewPanels.set(newUri, panel);
    const render = selectedRenders.get(oldUri);
    if (render !== undefined) { selectedRenders.delete(oldUri); selectedRenders.set(newUri, render); }
    // The page still shows the picture (it is not posted again), but: the server's session for
    // the old URI is gone with its didClose, and the new URI's starts over and cannot recognise
    // the pages' version; a refresh still waiting captured the now-closed untitled document,
    // so letting it fire would render the wrong (stale) doc — a later edit to the saved file
    // schedules a fresh one; and a request on its way can only answer about the closed document.
    sessionOf(panel).turned(true);
}

// Content for the "Lily#: New Score" command (File ▸ New File… ▸ Lily# Score).
//
// The SMALLEST complete piece: one part, one section, one score — a blank page a
// writer overwrites, not a worked example to delete. It used to be the whole of
// Twinkle, Twinkle with lyrics, verses, a |: :| repeat and six paragraphs of
// teaching comments, which is a fine thing to READ and a poor thing to start from:
// the first act on a new file was deleting 40 lines.
//
// The worked examples did not go away — they are the `template-…` completions
// (Ctrl+Space at the top level), which replace the file rather than joining it.
// The first comment line below names Ctrl+Space, the key that lists them and everything
// else, and how to start lesson 1 with it; the second the step keys and what each pair
// does (owner's requests 2026-10-16 — a writer who finds typing hard changes a note, the
// tempo, the key or the clef with them; it replaced the preview's Ctrl+K V — and
// 2026-10-26: the very first stage introduces Ctrl+Space and the step keys, as lesson 1's
// head does). They are the only lines here that are not score.
//
// ⚠️ Two spaces, no tabs: 545 of the 548 tracked .lys files that indent at all use
// two spaces and not one uses a tab (measured 2026-08-23). The formatter cannot
// settle this — it takes the indent from the client's formatting options.
const NEW_SCORE_TEMPLATE = `// New to Lily#? Ctrl+Space lists what you can write. On a new line type "lesson", Ctrl+Space, pick lesson-01.
// On a note: Ctrl+Shift+Up/Down = the next note of the key, Ctrl+Shift+PageUp/PageDown = an octave (on a tempo, key or clef too).
title "Untitled"

tempo 100
time 4/4
key c major

part melody {
  clef treble
  section A { c'4 d e f | g2 g | }
}

form { A }

score {
  staff melody
}
`;
const outputChannel = vscode.window.createOutputChannel('Lily# Extension');

// Track selected render per document (the document's, not a panel's: a file the follower
// turned away from keeps its picked score for when it comes back)
const selectedRenders = new Map<string, string>();

// (The rest of what a preview keeps is its panel's PreviewSession: the picture last posted —
// the webview retains its content (retainContextWhenHidden), so re-posting a byte-identical
// SVG is pure waste, often hundreds of KB across the extension→webview channel — the pages'
// version the webview holds, sent with the next lilysharp/svg request as shownVersion so the
// server answers with only the pages that changed since; and the one live request, stamped
// so that a response arriving after a newer request has been issued is DROPPED instead of
// painting an older picture over a newer one (responses can interleave now that the server
// dispatches requests off its read loop) and actively cancelled ($/cancelRequest), so the
// server abandons it at the render gate instead of spending a render on a thrown-away answer.)

// Constants
// The preview's refresh debounce. `lilysharp.preview.refreshDelay` is no longer in the
// Settings UI (owner's decision 2026-10-25: a tuning value), so this is the delay unless
// settings.json still names one. It was 60.
const DEBOUNCE_DELAY_DEFAULT = 100;
// The longest a change waits for the preview while more changes keep coming (a held key): the
// debounce above restarts on each change, this does not (owner's report 2026-11-16).
const PREVIEW_MAX_WAIT = 120;
// When each document was last edited: the header's notes wait for the text to rest (soundingWait).
const lastEditAt = new Map<string, number>();

export function activate(context: vscode.ExtensionContext) {
    outputChannel.appendLine('Lily# extension activating...');
    // The models that refused as VS Code's own, remembered from an earlier session (modelClient).
    rememberRefusalsIn(context.globalState);
    // The recording panel's settings, kept per chart and recording (recording.ts).
    useRecordingSettingsStore(context.workspaceState);
    // The A-B repeat's eight kept ranges, per file (docs/ab-repeat-design.md, step 5).
    abRangesStore = context.workspaceState;
    // Copilot Chat's log, which names the model Copilot's Auto answered with (modelClient).
    findCopilotLogFrom(context.logUri);

    const config = vscode.workspace.getConfiguration('lilysharp');
    // A server built by hand: settings.json only, not in the Settings UI since 2026-10-25
    // (as `lilysharp.trace.server`, which the language client reads by itself).
    let serverPath = config.get<string>('serverPath');

    outputChannel.appendLine(`Config serverPath: "${serverPath}"`);

    const serverDir = path.join(context.extensionPath, 'server');
    const apphostName = process.platform === 'win32' ? 'lilysharp-lsp.exe' : 'lilysharp-lsp';

    // Priority: 1. User-configured path, 2. bundled apphost, 3. bundled dll, 4. PATH.
    if (!serverPath || serverPath.trim() === '') {
        const bundledApphost = path.join(serverDir, apphostName);
        const bundledDll = path.join(serverDir, 'lilysharp-lsp.dll');
        if (fs.existsSync(bundledApphost)) {
            serverPath = bundledApphost;
            outputChannel.appendLine(`Using bundled server: ${serverPath}`);
        } else if (fs.existsSync(bundledDll)) {
            serverPath = bundledDll;
            outputChannel.appendLine(`Using bundled server (dll): ${serverPath}`);
        } else {
            serverPath = 'lilysharp-lsp';
            outputChannel.appendLine(`Using PATH: ${serverPath}`);
        }
    } else {
        outputChannel.appendLine(`Using configured path: ${serverPath}`);
    }

    if (path.isAbsolute(serverPath) && !fs.existsSync(serverPath)) {
        outputChannel.appendLine(`ERROR: Server executable not found: ${serverPath}`);
        vscode.window.showErrorMessage(`Lily# LSP server not found: ${serverPath}`);
        return;
    }

    // A self-contained deployment ships its own .NET runtime next to the apphost
    // (marked by coreclr), so the apphost is run DIRECTLY — no system .NET needed.
    // A framework-dependent build (local dev) is run via `dotnet <dll>` instead; if
    // `dotnet` is then missing we surface a clear, actionable error.
    const runtimeLib = process.platform === 'win32' ? 'coreclr.dll'
        : process.platform === 'darwin' ? 'libcoreclr.dylib' : 'libcoreclr.so';
    const selfContained = fs.existsSync(path.join(serverDir, runtimeLib));
    const bundledApphostPath = path.join(serverDir, apphostName);

    let serverCommand: string;
    let serverArgs: string[];
    let serverEnv: { [key: string]: string } | undefined;

    if (selfContained && fs.existsSync(bundledApphostPath)) {
        // Self-contained: launch the apphost directly.
        serverCommand = bundledApphostPath;
        serverArgs = [];
        outputChannel.appendLine(`Running self-contained apphost: ${serverCommand}`);
    } else if (serverPath.endsWith('.exe') || serverPath.endsWith('.dll')) {
        // Framework-dependent: run the .dll via dotnet (an .exe maps to its .dll).
        const dllPath = serverPath.endsWith('.dll') ? serverPath : serverPath.replace(/\.exe$/, '.dll');
        const userDotnetPath = path.join(process.env.LOCALAPPDATA || '', 'Microsoft', 'dotnet');
        const userDotnetExe = path.join(userDotnetPath, 'dotnet.exe');
        if (fs.existsSync(userDotnetExe)) {
            serverCommand = userDotnetExe;
            serverArgs = [dllPath];
            serverEnv = { ...process.env, DOTNET_ROOT: userDotnetPath } as { [key: string]: string };
            outputChannel.appendLine(`Running via user dotnet: ${userDotnetExe}`);
        } else if (commandExists('dotnet')) {
            serverCommand = 'dotnet';
            serverArgs = [dllPath];
            outputChannel.appendLine(`Running via system dotnet: ${dllPath}`);
        } else {
            const msg = 'Lily#: the language server needs the .NET runtime, which was not found. '
                + 'Install .NET, or reinstall the platform-specific Lily# build (it bundles its own runtime).';
            outputChannel.appendLine(`ERROR: ${msg}`);
            vscode.window.showErrorMessage(msg, 'Get .NET').then(pick => {
                if (pick === 'Get .NET') {
                    vscode.env.openExternal(vscode.Uri.parse('https://dotnet.microsoft.com/download'));
                }
            });
            return;
        }
    } else {
        serverCommand = serverPath;
        serverArgs = [];
    }

    const runOptions = {
        command: serverCommand,
        args: serverArgs,
        transport: TransportKind.stdio,
        options: serverEnv ? { env: serverEnv } : undefined
    };

    const serverOptions: ServerOptions = {
        run: runOptions,
        debug: runOptions
    };

    const clientOptions: LanguageClientOptions = {
        // 'untitled' so a brand-new, unsaved score (e.g. from "Lily#: New Score") is
        // synced to the server and the preview can render it before it's saved to disk.
        documentSelector: [
            { scheme: 'file', language: 'lilysharp' },
            { scheme: 'untitled', language: 'lilysharp' }
        ],
        synchronize: {
            fileEvents: vscode.workspace.createFileSystemWatcher('**/*.lys')
        },
        outputChannelName: 'Lily# Language Server',
        // Applied when the server starts; change takes effect on reload.
        initializationOptions: serverSettings(config),
        middleware: {
            provideCompletionItem: (document, position, context, token, next) =>
                staffGroupsOffered(document) ? [] : next(document, position, context, token),
        },
    };

    client = new LanguageClient(
        'lilysharp',
        'Lily# Language Server',
        serverOptions,
        clientOptions
    );

    let clientStartRequested = false;
    let resolveClientReady: () => void = () => { /* replaced below */ };
    clientReadyPromise = new Promise<void>(resolve => { resolveClientReady = resolve; });
    startClient = () => {
        if (clientStartRequested) {
            return;
        }
        clientStartRequested = true;
        outputChannel.appendLine('Starting language client...');
        client.start().then(() => {
        clientReady = true;
        outputChannel.appendLine('Language client started successfully');

        // Get and display server version
        client.sendRequest<string>('lilysharp/version').then(version => {
            outputChannel.appendLine(`Language server version: ${version}`);
        }).catch(err => {
            outputChannel.appendLine(`Failed to get server version: ${err}`);
        });

        // Update any open preview panels now that client is ready — through
        // previewDocument for the same reason the picker goes through it: a preview
        // whose editor tab is closed would otherwise sit on its old picture with
        // nothing said, and a server restart is exactly when it should come back.
        previewPanels.forEach((panel, uri) => {
            outputChannel.appendLine(`Updating preview for ${uri}`);
            void previewDocument(uri).then(doc => {
                if (doc) {
                    updatePreviewContent(doc, panel, context);
                }
            });
        });
        resolveClientReady();
    }).catch((error) => {
        outputChannel.appendLine(`Failed to start language client: ${error}`);
        vscode.window.showErrorMessage(
            'Lily#: the language server failed to start — live diagnostics and preview are unavailable.',
            'Show Log'
        ).then(pick => { if (pick === 'Show Log') { outputChannel.show(); } });
        // Resolved, not rejected: the waiters check clientReady and say so themselves.
        resolveClientReady();
    });
    };

    // A score already open (the usual activation, onLanguage:lilysharp) starts the
    // client now; a Markdown activation waits for the first .lys or lily# fence.
    if (vscode.workspace.textDocuments.some(d => d.languageId === 'lilysharp')) {
        startClient();
    }
    context.subscriptions.push(
        vscode.workspace.onDidOpenTextDocument(d => {
            if (d.languageId === 'lilysharp') {
                startClient();
            }
        })
    );

    // Push completion.flatSpelling changes to the server so they apply LIVE. The
    // server seeds the value from initializationOptions at start; without this a
    // change would only take effect after a window reload. The notification shape
    // mirrors initializationOptions ({ completion: { flatSpelling } }).
    context.subscriptions.push(
        // (First of the listeners: the ones after it read what settings() hands out.)
        vscode.workspace.onDidChangeConfiguration(e => {
            if (e.affectsConfiguration('lilysharp')) { lilysharpSettings = undefined; }
        }),
        vscode.workspace.onDidChangeConfiguration(e => {
            const fonts = e.affectsConfiguration('lilysharp.fontPaths');
            if ((!fonts && !e.affectsConfiguration('lilysharp.completion.flatSpelling')) || !client || !clientReady) {
                return;
            }
            // The whole settings object every time: the server reads each key it finds.
            client.sendNotification('workspace/didChangeConfiguration', {
                settings: serverSettings(vscode.workspace.getConfiguration('lilysharp'))
            });
            // A music font found in a new folder changes the picture: draw the previews again.
            if (fonts) {
                previewPanels.forEach((panel, uri) => {
                    void previewDocument(uri).then(doc => {
                        if (doc) {
                            updatePreviewContent(doc, panel, context);
                        }
                    });
                });
            }
        })
    );

    // "Convert Image/PDF to Lily# (.lys)…": in the palette on Windows, where the reader's extension exists
    // (it offers that extension when the reader is missing); in the Explorer's menu only where a
    // reader is — kept current as extensions come and go (omrReader.ts).
    registerOmrContexts(context, outputChannel);
    registerDettatoContext(context, outputChannel);

    // "Show Original Scan" (LilySharp-Omr proposal B3) is offered on a .lys an OMR reader
    // wrote, i.e. one with its side file beside it.
    const deps: OmrScanDeps = {
        output: outputChannel,
        postToPreview: (lys, message) => void previewPanels.get(lys.toString())?.webview.postMessage(message),
        client: async () => {
            try {
                await ensureClientReady();
                return client;
            } catch {
                return undefined;
            }
        },
    };
    const updateHasScan = () => void vscode.commands.executeCommand('setContext', 'lilysharp.hasScan',
        hasScan(vscode.window.activeTextEditor?.document.uri));
    updateHasScan();
    scanDeps = deps;
    registerScanFollow(context, deps);
    // The lysc the reader engraves with when it verifies: the one beside the bundled server, if
    // any (else the reader looks beside itself, then on PATH).
    const bundledLysc = () => {
        const p = path.join(context.extensionPath, 'server', process.platform === 'win32' ? 'lysc.exe' : 'lysc');
        return fs.existsSync(p) ? p : undefined;
    };
    // Check on save against the scan (B5, lilysharp.omr.verifyOnSave).
    registerVerifyOnSave(context, { output: outputChannel, lysc: bundledLysc });
    const omrImportDeps: OmrImportDeps = {
        output: outputChannel,
        openPreview: column => openPreview(context, column),
        lysc: bundledLysc,
        showScan: lys => showScan(context, deps, lys, vscode.ViewColumn.Three),
    };
    // Copilot's chat reads a scan through the same command (owner's request 2026-10-29: "turn this
    // image into a score" asked in the chat): the reader's offers, progress and messages are the
    // command's; the chat is told the score it wrote, and that its misreadings are mended beside
    // the scan (lilysharp_fixScanBars), not by the chat.
    const lm = vscode.lm as unknown as { registerTool?: typeof vscode.lm.registerTool };
    if (typeof lm.registerTool === 'function') {
        context.subscriptions.push(lm.registerTool<{ files: string[] }>('lilysharp_readScan', {
            prepareInvocation: options => ({
                invocationMessage: `Reading ${(options.input?.files ?? []).map(f => path.basename(f)).join(', ') || 'the scan'} into a score…`,
            }),
            invoke: async options => {
                const files = (options.input?.files ?? []).filter(f => typeof f === 'string' && fs.existsSync(f));
                if (files.length === 0) {
                    return new vscode.LanguageModelToolResult([new vscode.LanguageModelTextPart(
                        'Nothing read: give `files`, the absolute paths of the images or PDFs (the pages in order) — none was found.')]);
                }
                const uris = files.map(f => vscode.Uri.file(f));
                const read = await importFromImage(context, omrImportDeps, uris[0], uris);
                return new vscode.LanguageModelToolResult([new vscode.LanguageModelTextPart(read
                    ? `Read: ${read.lys}${read.partial ? ' (some pages could not be read)' : ''} — ${read.marks}. It is open in the `
                      + 'editor, its score beside it and the scan beside that. The reader marked what it doubts with @todo. A '
                      + 'misreading is mended beside the scan, not by you: for bars the musician names, open them there with '
                      + 'lilysharp_fixScanBars; else tell them to right-click a bar on the score and choose Edit Bars Beside the Scan.'
                    : 'Not read: Lily# said why in a message (the reader may need installing — Lily# offered it — or the read '
                      + 'was stopped or failed). Tell the musician to look at that message.')]);
            },
        }));
    }
    context.subscriptions.push(vscode.window.onDidChangeActiveTextEditor(updateHasScan));

    // Push preview.theme changes to every open preview, so the setting takes effect
    // without reopening (a new panel reads it when its HTML is built).
    context.subscriptions.push(
        vscode.workspace.onDidChangeConfiguration(e => {
            if (e.affectsConfiguration('lilysharp.playback.soundFont')) {
                // Remembered for Choose Sound Font…'s list — set through it or by hand.
                void rememberSoundFont(context,
                    vscode.workspace.getConfiguration('lilysharp').get<string>('playback.soundFont', '').trim());
                // The new font's folder has to be a resource root before the page can
                // fetch it; the options can be reset on a live webview.
                for (const panel of previewPanels.values()) {
                    panel.webview.options = {
                        enableScripts: true,
                        localResourceRoots: previewResourceRoots(context.extensionUri),
                    };
                    panel.webview.postMessage({
                        type: 'setSoundFont', uri: synthUrls(panel.webview, context.extensionUri).soundFont,
                    });
                }
            }
            if (e.affectsConfiguration('lilysharp.playback.midiOutput')) {
                // The port's name goes to every preview (each decides where its next sound
                // goes); the server's playback, if one is on, stops, and the port it held is
                // closed when the setting is emptied.
                const name = midiOutputSetting();
                for (const panel of previewPanels.values()) {
                    panel.webview.postMessage({ type: 'setMidiOutput', name });
                }
                midiPlaybackPanel = undefined;
                void midiOutRequest('lilysharp/midiOut/stop', {});
            }
            if (e.affectsConfiguration('lilysharp.playback.singer')) {
                // Another engine: the previews forget what was rendered and the engine is let go
                // (one starts again on the next notes).
                disposeSinger();
            }
            if (!e.affectsConfiguration('lilysharp.preview.theme')) {
                return;
            }
            const theme = getPreviewTheme();
            for (const panel of previewPanels.values()) {
                panel.webview.postMessage({ type: 'setTheme', theme });
            }
        })
    );

    // Register preview commands
    context.subscriptions.push(
        // `lilysharp.playback.soundFont` through a picker and a file dialog — the settings
        // UI gives a string no browse button, so its description links here (owner's
        // request 2026-10-11). The choice is written to the user settings; the open
        // previews pick it up through onDidChangeConfiguration.
        vscode.commands.registerCommand('lilysharp.chooseSoundFont', () => chooseSoundFont(context)),
        // `lilysharp.playback.midiOutput` through a picker of the machine's MIDI output ports
        // (the server lists them); the open previews pick it up through onDidChangeConfiguration.
        vscode.commands.registerCommand('lilysharp.chooseMidiOutput', () => chooseMidiOutput()),
        // The Cantoria extension installed or removed: the engine is looked for again on the next notes.
        vscode.extensions.onDidChange(() => disposeSinger()),
        vscode.commands.registerCommand('lilysharp.openPreview', () => {
            outputChannel.appendLine('openPreview command triggered');
            openPreview(context, vscode.ViewColumn.Active);
        }),
        vscode.commands.registerCommand('lilysharp.openPreviewToSide', () => {
            outputChannel.appendLine('openPreviewToSide command triggered');
            openPreview(context, vscode.ViewColumn.Beside);
        }),
        // The section CodeLens's click (the server's LilySharpLanguageServer.CodeLens.cs): its
        // arguments arrive as plain JSON, so they are turned into VS Code's own types here and
        // every layer of the section opens in the references peek. Not in package.json's
        // `commands`: it is the lens's, not the palette's.
        vscode.commands.registerCommand('lilysharp.showSectionLayers',
            (uri: string, position: { line: number; character: number },
             locations: Array<{ uri: string; range: { start: { line: number; character: number }; end: { line: number; character: number } } }>) => {
                if (!uri || !position || !locations || locations.length === 0) {
                    return;
                }
                const pos = (p: { line: number; character: number }) => new vscode.Position(p.line, p.character);
                const locs = locations.map(l => new vscode.Location(
                    vscode.Uri.parse(l.uri), new vscode.Range(pos(l.range.start), pos(l.range.end))));
                return vscode.commands.executeCommand(
                    'editor.action.showReferences', vscode.Uri.parse(uri), pos(position), locs);
            }),
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
        // The steps for each engine, on the site (owner's request 2026-11-12): what to download, where
        // to unpack it, which Cantoria command to run. The no-voice messages carry the same link.
        vscode.commands.registerCommand('lilysharp.voiceInstallGuide',
            () => vscode.env.openExternal(vscode.Uri.parse(VOICE_INSTALL_GUIDE))),
        vscode.commands.registerCommand('lilysharp.writeSingerPart',
            (uri?: string, singer?: string) => writeSingerPart(
                typeof uri === 'string' ? uri : undefined, typeof singer === 'string' ? singer : undefined)),
        vscode.commands.registerCommand('lilysharp.newScore', async () => {
            outputChannel.appendLine('newScore command triggered');
            const doc = await vscode.workspace.openTextDocument({
                language: 'lilysharp',
                content: await newScoreTemplate(context),
            });
            const editor = await vscode.window.showTextDocument(doc);
            // Land on the one word in the template that is a placeholder rather than
            // score: selected, so the first keystroke names the piece instead of having
            // to find "Untitled" and pick it out from between the quotes.
            const title = newScoreTitleRange(doc);
            if (title) {
                editor.selection = new vscode.Selection(title.start, title.end);
                editor.revealRange(title);
            }
        }),
        // The template New Score starts from is the writer's to change (owner's request
        // 2026-10-25, in place of a paper-size setting): Edit opens the writer's copy — made
        // from the built-in one the first time — and Reset goes back to the built-in one.
        vscode.commands.registerCommand('lilysharp.editNewScoreTemplate', async () => {
            const file = newScoreTemplateUri(context);
            if (!(await uriExists(file))) {
                await vscode.workspace.fs.createDirectory(context.globalStorageUri);
                await vscode.workspace.fs.writeFile(file, Buffer.from(NEW_SCORE_TEMPLATE, 'utf8'));
            }
            await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(file));
            vscode.window.setStatusBarMessage('Lily#: New Score starts from this file once it is saved.', 5000);
        }),
        vscode.commands.registerCommand('lilysharp.resetNewScoreTemplate', async () => {
            const file = newScoreTemplateUri(context);
            if (!(await uriExists(file))) {
                vscode.window.showInformationMessage('Lily#: New Score already uses the built-in template.');
                return;
            }
            const reset = 'Reset';
            if (await vscode.window.showWarningMessage('Lily#: delete your New Score template and use the built-in one?',
                { modal: true }, reset) !== reset) {
                return;
            }
            await vscode.workspace.fs.delete(file);
            vscode.window.showInformationMessage('Lily#: New Score uses the built-in template again.');
        }),
        // A score template is a WHOLE FILE — title, parts, form, score — so the two
        // `template-…` completions cannot simply insert at the caret: dropping a complete
        // second piece into a file that already has one produces duplicate globals, and
        // that is what they did until this command existed. The LSP item accepts as a
        // no-op edit (it re-types the word being completed, unchanged) and names this
        // command; everything the template needs travels in the arguments, so the server
        // stays the one home for the text and a new template needs no change here.
        // Not in contributes.commands: it takes arguments, so the Command Palette has
        // nothing sensible to hand it.
        vscode.commands.registerCommand('lilysharp.applyScoreTemplate',
            (label: string, template: string, ...range: number[]) => {
                outputChannel.appendLine(`applyScoreTemplate command triggered (${label})`);
                return applyScoreTemplate(label, template, range);
            }),
        vscode.commands.registerCommand('lilysharp.importImage', (uri?: vscode.Uri, uris?: vscode.Uri[]) =>
            importFromImage(context, omrImportDeps, uri, uris)),
        vscode.commands.registerCommand('lilysharp.showScan', (uri?: vscode.Uri) =>
            showScan(context, deps, uri instanceof vscode.Uri ? uri : undefined)),
        vscode.commands.registerCommand('lilysharp.importMusicXml', (uri?: vscode.Uri) => {
            outputChannel.appendLine('importMusicXml command triggered');
            importMusicXml(context, uri);
        }),
        // The recording beside a chart (docs/recording-design.md): attached by a file beside the
        // chart, never by the .lys; renames keep the two together.
        vscode.commands.registerCommand('lilysharp.attachRecording', (uri?: vscode.Uri) =>
            attachRecording(uri instanceof vscode.Uri ? uri : undefined)),
        // Dettato writes the chart (and the file beside it) from a recording; without it the
        // command offers its extension (owner's decision 2026-11-15).
        vscode.commands.registerCommand('lilysharp.convertRecording', (uri?: vscode.Uri) =>
            convertRecording(context, uri instanceof vscode.Uri ? uri : undefined, outputChannel,
                () => vscode.commands.executeCommand('lilysharp.openPreviewToSide'))),
        followRenames(),
        // …and a chart copied (or saved under another name) takes it along (owner's request 2026-11-16).
        followCopies(),
        watchRecordings(context),
        // Audition: sound the note under the caret (held: the keybinding re-fires on
        // key-repeat, and the webview sustains until the repeats stop) or play the
        // whole measure the caret is in. The preview webview is the synth.
        vscode.commands.registerCommand('lilysharp.playNoteAtCursor', () =>
            playAtCursor(context, 'note')),
        // (fresh: the text just changed under the caret — an @or turned by Ctrl+Shift+PageUp — so the
        // page's note list is the old text's and is fetched again)
        vscode.commands.registerCommand('lilysharp.playMeasureAtCursor', (opts?: { fresh?: boolean }) =>
            playAtCursor(context, 'measure', !!(opts && opts.fresh))),
        // Preview context menu (menus/webview/context). Each one is a thin relay:
        // the state these act on — the right-clicked note, the zoom, the synth —
        // all lives in the webview, so the command only names the action.
        vscode.commands.registerCommand('lilysharp.previewPlayFromHere', () =>
            postToActivePreview('ctxPlayFromHere')),
        vscode.commands.registerCommand('lilysharp.previewStop', () =>
            postToActivePreview('ctxStop')),
        // The A-B repeat's right-click (docs/ab-repeat-design.md §1): the bar line or the
        // section right-clicked is the webview's to know.
        vscode.commands.registerCommand('lilysharp.previewAbA', () =>
            postToActivePreview('ctxAbA')),
        vscode.commands.registerCommand('lilysharp.previewAbB', () =>
            postToActivePreview('ctxAbB')),
        vscode.commands.registerCommand('lilysharp.previewAbSection', () =>
            postToActivePreview('ctxAbSection')),
        vscode.commands.registerCommand('lilysharp.previewFitWidth', () =>
            postToActivePreview('ctxFitWidth')),
        vscode.commands.registerCommand('lilysharp.previewResetZoom', () =>
            postToActivePreview('ctxResetZoom'))
    );

    // AI collaborative editing: select → prompt → validate → decide-on-score → apply.
    const aiDeps = {
        extensionUri: context.extensionUri,
        getClient: () => client,
        isReady: () => clientReady,
        ensureReady: ensureClientReady,
        log: (msg: string) => outputChannel.appendLine(msg),
        // A candidate's Play (owner's request 2026-10-25) sounds through the document's preview.
        playOnPreview: (uri: vscode.Uri, notes: unknown[]) => {
            const panel = previewPanels.get(uri.toString());
            if (!panel) { return false; }
            void panel.webview.postMessage({ type: 'playNotes', notes });
            return true;
        },
        previewColumnOf: (uri: vscode.Uri) => previewPanels.get(uri.toString())?.viewColumn,
        // A candidate's panel plays itself (owner's decision 2026-11-11): the synth's files as its
        // page reaches them, where they are read from, and the MIDI output, which stays the preview's.
        synthRoots: () => previewResourceRoots(context.extensionUri),
        synthFor: (webview: vscode.Webview) => ({ ...synthUrls(webview, context.extensionUri), midiOutput: midiOutputSetting() }),
        // …and with a MIDI output chosen, the port plays it, asked here: no preview is needed for it.
        midiPlay: async (port: string, notes: unknown[]) => {
            const r = await midiOutRequest<{ Started?: boolean; StartedAtMs?: number; EndSeconds?: number; Error?: string | null }>(
                'lilysharp/midiOut/play', { port, notes, offset: 0 });
            if (r.Error) { reportMidiOutError(port, r.Error); }
            outputChannel.appendLine(`MIDI out (candidate): ${notes.length} notes, to ${Number(r.EndSeconds ?? 0).toFixed(3)} s`);
            return { started: !!r.Started, t0Ms: r.StartedAtMs ?? 0, end: r.EndSeconds ?? 0, error: r.Error ?? null };
        },
        midiStop: () => { void midiOutRequest('lilysharp/midiOut/stop', {}); },
        // …and sings (owner's request 2026-11-11): the voices the engine has for the text played.
        voicesFor: candidateVoices,
        stopOnPreview: (uri: vscode.Uri) => {
            void previewPanels.get(uri.toString())?.webview.postMessage({ type: 'ctxStop' });
        },
        renderNameOf: (uri: vscode.Uri) => selectedRenders.get(uri.toString()) || undefined,
        showLog: () => outputChannel.show(true),
        // The lysc beside the bundled server, for Compose with AI in Chat to tell the agent of.
        lysc: bundledLysc,
    };
    registerAiTransform(context, aiDeps);
    // Second mode: validated ghost-text "next measure" completion (opt-in).
    registerAiComplete(context, aiDeps);
    // Third: the bars an OMR reader marked, rewritten on request and reviewed beside the scan
    // (docs/omr-ai-fix-design.md). The scan view's slider says which bars are checked at first.
    // Its menu door in the preview and the scan view: the bar the right-click was on, the
    // preview's as an offset in the file it shows, the scan view's as a bar of its side file.
    registerOmrAiFix(context, {
        ...aiDeps, scanThreshold: scanThresholdOf, lysc: bundledLysc, scanColumn: scanColumnOf, scanSide: scanSideOf,
        barAtMenu: m => {
            if (m.webview === 'lilysharpScan') {
                return Number.isInteger(m.lysBar) && m.lysBar! >= 0 ? scanBarAt(m.lysBar!) : undefined;
            }
            if (m.webview === 'lilysharpPreview' && Number.isInteger(m.lysAt) && m.lysAt! >= 0) {
                const shown = [...previewPanels.entries()].find(([, p]) => p.active);
                return shown ? { uri: vscode.Uri.parse(shown[0]), at: m.lysAt! } : undefined;
            }
            return undefined;
        },
    });
    // Ctrl+I's door in the preview's right-click menu (owner's request 2026-10-24): the score
    // selection, else the note or the bar right-clicked, made the editor's selection, then the
    // same transform (aiTransformFromScore).
    // One door, four captions (owner's request 2026-10-25: the menu everywhere on the score, saying
    // what it will change): the selection, the note or the bar right-clicked, or — off the staves
    // — the whole score, as Ctrl+I on an empty line does in the editor.
    const previewAi = async (m?: { lysAiKind?: string; lysAiFrom?: number; lysAiTo?: number; lysAiSelected?: string }) => {
        const shown = [...previewPanels.entries()].find(([, p]) => p.active);
        if (!shown || !m) {
            return;
        }
        if (m.lysAiKind === 'score') {
            await aiTransformWholeScore(shown[0]);
            return;
        }
        if (!Number.isInteger(m.lysAiFrom) || m.lysAiFrom! < 0) {
            return;
        }
        await aiTransformFromScore(shown[0], m.lysAiFrom!, m.lysAiTo ?? m.lysAiFrom!, m.lysAiSelected);
    };
    for (const id of ['lilysharp.previewAiTransform', 'lilysharp.previewAiTransformNote',
        'lilysharp.previewAiTransformBar', 'lilysharp.previewAiTransformScore']) {
        context.subscriptions.push(vscode.commands.registerCommand(id, previewAi));
    }
    // Ask AI in Chat (owner's request 2026-10-29): the same place the menu's Change … with AI
    // works on — the selection, the note or the bar right-clicked, else the whole file — put in
    // Copilot's chat input, unsent; from the editor, its selection, else the file.
    context.subscriptions.push(
        vscode.commands.registerCommand('lilysharp.previewAskAiInChat', async (m?: { lysAiKind?: string; lysAiFrom?: number; lysAiTo?: number }) => {
            const shown = [...previewPanels.entries()].find(([, p]) => p.active);
            if (!shown) {
                return;
            }
            const doc = vscode.workspace.textDocuments.find(d => d.uri.toString() === shown[0])
                ?? await vscode.workspace.openTextDocument(vscode.Uri.parse(shown[0]));
            const picked = m && m.lysAiKind !== 'score' && Number.isInteger(m.lysAiFrom) && m.lysAiFrom! >= 0;
            const to = picked ? noteSelectionEnd(doc.getText(), m!.lysAiTo ?? m!.lysAiFrom!) : undefined;
            await askAiInChat(doc, picked ? m!.lysAiFrom : undefined, to);
        }),
        vscode.commands.registerCommand('lilysharp.askAiInChat', async () => {
            const editor = vscode.window.activeTextEditor;
            if (!editor || editor.document.languageId !== 'lilysharp') {
                vscode.window.showInformationMessage('Lily#: open a .lys file to ask about it in the chat.');
                return;
            }
            const s = editor.selection;
            await askAiInChat(editor.document, s.isEmpty ? undefined : editor.document.offsetAt(s.start),
                s.isEmpty ? undefined : editor.document.offsetAt(s.end));
        }));
    // The score's selection from its menu: the bar right-clicked selected (Shift+click or
    // Shift+drag extends it), and the selection cleared.
    context.subscriptions.push(
        vscode.commands.registerCommand('lilysharp.previewSelectBar', () => postToActivePreview('ctxSelectBar')),
        vscode.commands.registerCommand('lilysharp.previewClearSelection', () => postToActivePreview('ctxClearSelection')),
        // Select Chord on a member right-clicked (owner's request 2026-10-27): the caret just past
        // the chord's ">" — where its octave marks and decorations go, and where the preview
        // lights it whole (chordAtCaret).
        vscode.commands.registerCommand('lilysharp.previewSelectChord', async (m?: { lysChordEnd?: number }) => {
            const shown = [...previewPanels.entries()].find(([, p]) => p.active);
            if (!shown || !m || !Number.isInteger(m.lysChordEnd) || m.lysChordEnd! < 0) {
                return;
            }
            const [uri, panel] = shown;
            const editor = vscode.window.visibleTextEditors.find(e => e.document.uri.toString() === uri)
                ?? await openPreviewSource(panel, uri);
            if (!editor) {
                return;
            }
            const position = editor.document.positionAt(m.lysChordEnd!);
            editor.selection = new vscode.Selection(position, position);
            editor.revealRange(new vscode.Range(position, position), vscode.TextEditorRevealType.InCenterIfOutsideViewport);
            await vscode.window.showTextDocument(editor.document, editor.viewColumn);
        }),
        // The @todo doors (owner's request 2026-10-31): on a bar that holds a mark, Resolve This
        // TODO takes the bar's marks out; off the staves, Resolve All TODOs takes every one out —
        // the quick fix's edit, applied from the page (todoResolveCore).
        vscode.commands.registerCommand('lilysharp.previewResolveTodo', (m?: { lysTodoStarts?: string }) =>
            resolvePreviewTodos(parseStarts(m?.lysTodoStarts))),
        vscode.commands.registerCommand('lilysharp.previewResolveAllTodos', () => resolvePreviewTodos(null)),
        vscode.commands.registerCommand('lilysharp.resolveAllTodos', () => resolvePreviewTodos(null, true)),
        vscode.commands.registerCommand('lilysharp.removeAllOrMarks', () => removeAllOrMarks()));
    // The Settings screen's "Choose a model…" links name the feature ('edit' | 'ghost'), so the
    // list of features is skipped; the palette asks which.
    context.subscriptions.push(vscode.commands.registerCommand('lilysharp.selectAiModel',
        (use?: unknown) => pickAiModel(use === 'edit' || use === 'ghost' ? use : undefined)));

    context.subscriptions.push(vscode.languages.registerCompletionItemProvider({ language: 'lilysharp' }, singerCompletion, '"', ' ', ','));
    context.subscriptions.push(vscode.languages.registerCompletionItemProvider({ language: 'lilysharp' }, singerSettingCompletion, ' '));
    context.subscriptions.push(vscode.languages.registerCompletionItemProvider({ language: 'lilysharp' }, singMarkCompletion, '(', ' '));
    // …and what a voice does not take is marked where it is written (markSingProblems).
    context.subscriptions.push(singDiagnostics,
        vscode.workspace.onDidOpenTextDocument(scheduleSingProblems),
        vscode.workspace.onDidChangeTextDocument(e => scheduleSingProblems(e.document)),
        vscode.workspace.onDidCloseTextDocument(d => singDiagnostics.delete(d.uri)),
        voiceDiagnostics,
        vscode.workspace.onDidCloseTextDocument(d => { voiceProblems.forget(d.uri.toString()); voiceDiagnostics.delete(d.uri); }),
        vscode.extensions.onDidChange(() => { for (const d of vscode.workspace.textDocuments) { scheduleSingProblems(d); } }));
    for (const d of vscode.workspace.textDocuments) { scheduleSingProblems(d); }
    voiceSamplesDir = path.join(context.globalStorageUri.fsPath, 'voice-samples');
    context.subscriptions.push(vscode.commands.registerCommand('lilysharp.hearSinger', hearSinger));
    context.subscriptions.push(vscode.commands.registerCommand('lilysharp.singerTaken', singerTaken));
    context.subscriptions.push(vscode.commands.registerCommand('lilysharp.setUpVoices', setUpVoices));

    // The Explorer's batch export: right-click one or more .lys → the format
    // submenu → a folder → every score of every file, named as `lysc` names
    // them. The preview's Export button (exportPreview below) stays the one-score door.
    registerExportBatch(context, {
        getClient: () => client,
        isReady: () => clientReady,
        whenReady: ensureClientReady,
        log: (msg: string) => outputChannel.appendLine(msg),
        showLog: () => outputChannel.show(true),
        openFolder: (dir: string) => openInDefaultApp(dir),
        wavSoundFont: () => wavSoundFont(context),
        renderAudio: (jobs, soundFont, format) => renderAudioFiles(jobs, soundFont, format),
        ffmpeg: () => videoFfmpeg(),
        renderVideo: (jobs, soundFont, ffmpeg, source) => writeVideos(jobs, soundFont, ffmpeg, drawFramesFor(source)),
        practisedJobs: (jobs, uri) => practisedJobs(jobs, uri ? practiceFor(uri) : undefined),
        // The sung voices, as the preview's Export mixes them (owner's report 2026-11-09, session
        // 1153: the Explorer's Export as MP4 carried no voice).
        // …and the recording beside the chart, as the preview showing it sounds it (recording-design §7).
        singJobs: async (jobs, source, uri) => recordJobs(await singJobs(jobs, source, uri ? practiceFor(uri) : undefined), uri),
        practiceSummary: uri => uri ? practiceSummary(practiceFor(uri)) : '',
    });

    // Smart typing: the brackets it started with (`<` before c4 -> `<c>4`, and a
    // chord's '>' promoted to '>>' when its '<' is doubled into an arpeggio) plus
    // slurs, octave marks, beams, ties and durations — hence smartTyping, not
    // smartBrackets.
    registerSmartTyping(context, (msg: string) => outputChannel.appendLine(msg));
    // A page text's body is Markdown: Enter continues its lists, and an asterisk, an
    // underscore or a code mark typed over a selection surrounds it (pageText.ts).
    registerPageText(context, (msg: string) => outputChannel.appendLine(msg));
    registerStaffGroupWrap(context);

    // The step keys (Ctrl+Shift+Up / Down: a note's semitone, Ctrl+Shift+PageUp / PageDown:
    // its octave mark — owner's decision 2026-10-15; both: a chord's shape) and the automatic
    // audition (the caret landing on a note, a note typed, a step) — owner's decision
    // 2026-09-28. The server computes both; the
    // preview webview is the synth, and with no preview open they are silent.
    registerStepAudition(context, {
        getClient: () => client,
        isReady: () => clientReady,
        getPreview: (uri: string) => previewPanels.get(uri),
        log: (msg: string) => outputChannel.appendLine(msg),
        stepSinger: stepSingerAt,
        hearSinger,
        isPlaying: (uri: string) => { const panel = previewPanels.get(uri); return !!panel && playingPanels.has(panel); },
    });

    // The caret's status line (owner's request 2026-10-24): what the setting under the caret
    // does, in the status bar rather than a hover that covers the text.
    registerCaretInfo(context, { getClient: () => client, isReady: () => clientReady });

    // Watch for document changes
    context.subscriptions.push(
        vscode.workspace.onDidChangeTextDocument(event => {
            if (event.document.languageId === 'lilysharp') {
                const uri = event.document.uri.toString();
                const panel = previewPanels.get(uri);
                if (panel) {
                    lastEditAt.set(uri, Date.now());
                    // Neither is in the Settings UI since 2026-10-25 (settings.json only).
                    const cfg = settings();
                    const autoRefresh = cfg.get<boolean>('preview.autoRefresh', true);
                    const delay = cfg.get<number>('preview.refreshDelay', DEBOUNCE_DELAY_DEFAULT);

                    if (autoRefresh) {
                        // …but never later than PREVIEW_MAX_WAIT after the first change still
                        // waiting (owner's report 2026-11-16): a held step key (Ctrl+Shift+Up on
                        // `tempo 130`) edits every ~30 ms, each edit pushed the refresh back again,
                        // and the preview stood still until the key was let go. Now it follows the
                        // held key, a render each PREVIEW_MAX_WAIT, and the last edit still renders.
                        sessionOf(panel).refreshAfter(delay, PREVIEW_MAX_WAIT, Date.now(),
                            () => updatePreviewContent(event.document, panel, context));
                    }
                }
            }
        })
    );

    // Keep the preview alive across an untitled -> file SAVE. Saving an untitled score
    // CLOSES the untitled document and OPENS/activates a new file: document, orphaning a
    // preview keyed by the old untitled URI. Content can't be relied on to pair them (a
    // format-on-save reindents/rewrites the buffer), so pair by RECENCY: when a lilysharp
    // file becomes the active editor (or is saved) right after an untitled preview closed,
    // re-key that preview onto it.
    const closedUntitledPreviews = new Map<string, number>(); // untitled uri -> close time
    const adoptRecentOrphanPreview = (doc: vscode.TextDocument | undefined) => {
        if (!doc || doc.uri.scheme !== 'file' || doc.languageId !== 'lilysharp') { return; }
        const newUri = doc.uri.toString();
        if (previewPanels.has(newUri) || closedUntitledPreviews.size === 0) { return; }
        const now = Date.now();
        let best: string | undefined;
        let bestTime = 0;
        for (const [oldUri, closedAt] of closedUntitledPreviews) {
            if (now - closedAt > 4000 || !previewPanels.has(oldUri)) { closedUntitledPreviews.delete(oldUri); continue; }
            if (closedAt >= bestTime) { best = oldUri; bestTime = closedAt; }
        }
        if (!best) { return; }
        closedUntitledPreviews.delete(best);
        migratePreviewKey(best, newUri);
        const panel = previewPanels.get(newUri);
        if (panel) {
            panel.title = previewTitle(newUri, panel);
            // Refresh under the saved file; updatePreviewContent retries if the server
            // has not registered it yet, so this cannot flash "Document not found".
            updatePreviewContent(doc, panel, context);
        }
        outputChannel.appendLine(`Preview migrated ${best} -> ${newUri}`);
    };
    context.subscriptions.push(
        vscode.workspace.onDidCloseTextDocument(closedDoc => {
            const oldUri = closedDoc.uri.toString();
            if (closedDoc.uri.scheme === 'untitled' && previewPanels.has(oldUri)) {
                closedUntitledPreviews.set(oldUri, Date.now());
                outputChannel.appendLine(`Untitled preview closed; pending migrate: ${oldUri}`);
            }
        }),
        // The saved file becomes the active editor (and fires a save) right after; either
        // one adopts the just-closed untitled preview.
        vscode.window.onDidChangeActiveTextEditor(editor => adoptRecentOrphanPreview(editor?.document)),
        vscode.workspace.onDidSaveTextDocument(adoptRecentOrphanPreview)
    );

    // A .lys coming to the front: the follower turns to it, and opening one with no follower
    // open opens a preview beside it (previewOnEditorShown; lilysharp.preview.openOnOpen).
    // Registered AFTER the orphan adoption above: a just-saved untitled score must take its
    // own preview with it before this decides the file has none.
    const shownDocs = new Set<string>();
    const onEditorShown = (editor: vscode.TextEditor | undefined) => {
        if (!editor) { return; }
        const doc = editor.document;
        const uri = doc.uri.toString();
        const firstShow = !shownDocs.has(uri);
        shownDocs.add(uri);
        const tab = vscode.window.tabGroups.activeTabGroup.activeTab;
        const action = previewOnEditorShown({
            languageId: doc.languageId,
            scheme: doc.uri.scheme,
            inDiff: !!tab && tab.input instanceof vscode.TabInputTextDiff,
            hasOwnPreview: previewPanels.has(uri),
            hasFollower: followerPanel !== undefined,
            openOnOpen: vscode.workspace.getConfiguration('lilysharp').get<boolean>('preview.openOnOpen', true),
            firstShow,
            dismissed: dismissedPreviews.has(uri),
            followerLocked,
        });
        if (action === 'follow' && followerPanel) {
            if (playingPanels.has(followerPanel)) {
                pendingFollow = uri;
            } else {
                retargetPreview(followerPanel, doc, context);
            }
        } else if (action === 'open') {
            openPreview(context, vscode.ViewColumn.Beside, true);
        }
    };
    // A score whose last tab is closed is opened afresh next time: its preview comes again, though
    // the writer closed it while the score was open (owner's report 2026-10-29: a score saved and
    // closed, opened again from a link in the chat, came without its preview). The document
    // itself lingers after its tab (onDidCloseTextDocument comes minutes later), so the tabs say it.
    const forgetClosedScores = (e: vscode.TabChangeEvent) => {
        const open = new Set(vscode.window.tabGroups.all.flatMap(g => g.tabs)
            .map(t => t.input instanceof vscode.TabInputText ? t.input.uri.toString() : undefined));
        for (const tab of e.closed) {
            const uri = tab.input instanceof vscode.TabInputText ? tab.input.uri.toString() : undefined;
            if (uri && !open.has(uri)) {
                shownDocs.delete(uri);
                dismissedPreviews.delete(uri);
            }
        }
    };
    context.subscriptions.push(
        vscode.window.onDidChangeActiveTextEditor(onEditorShown),
        vscode.window.tabGroups.onDidChangeTabs(forgetClosedScores),
        vscode.workspace.onDidCloseTextDocument(d => shownDocs.delete(d.uri.toString())),
        vscode.commands.registerCommand('lilysharp.lockPreview', () => setActivePreviewLocked(true)),
        vscode.commands.registerCommand('lilysharp.unlockPreview', () => setActivePreviewLocked(false))
    );
    // The score the window opened with (a restored session has no preview to bring back).
    onEditorShown(vscode.window.activeTextEditor);

    // A .lys opened into a preview's group would cover the preview (owner's request 2026-10-12):
    // it moves to a group of scores (scoreGroupFor). A tab OPENED there only — a score the
    // writer dragged in, or switched to among that group's tabs, stays where they put it.
    context.subscriptions.push(vscode.window.tabGroups.onDidChangeTabs(e => {
        for (const tab of e.opened) {
            if (!(tab.input instanceof vscode.TabInputText)) { continue; }
            const uri = tab.input.uri;
            if (uri.scheme !== 'file' && uri.scheme !== 'untitled') { continue; }
            const doc = vscode.workspace.textDocuments.find(d => d.uri.toString() === uri.toString());
            if (doc ? doc.languageId !== 'lilysharp' : !uri.path.toLowerCase().endsWith('.lys')) { continue; }
            const groups = vscode.window.tabGroups.all.map(g => ({ column: g.viewColumn, hasPreview: g.tabs.some(isPreviewTab) }));
            const target = scoreGroupFor(tab.group.viewColumn, groups);
            if (target !== undefined) {
                void moveScoreTab(uri, tab.group.viewColumn, target, tab.isPreview);
            }
        }
    }));

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

    // Watch for cursor position changes
    context.subscriptions.push(
        vscode.window.onDidChangeTextEditorSelection(event => {
            if (event.textEditor.document.languageId === 'lilysharp') {
                const uri = event.textEditor.document.uri.toString();
                const panel = previewPanels.get(uri);
                if (panel) {
                    const doc = event.textEditor.document;

                    // A non-empty selection highlights EVERY note whose position falls
                    // inside a selected range (block/multi-cursor selections send more
                    // than one range).
                    const ranges = event.selections
                        .filter(s => !s.isEmpty)
                        .map(s => [doc.offsetAt(s.start), doc.offsetAt(s.end)]);
                    if (ranges.length > 0) {
                        panel.webview.postMessage({ type: 'highlightRange', ranges, caret: doc.offsetAt(event.selections[0].active) });
                        return;
                    }

                    const active = event.selections[0].active;
                    const offset = doc.offsetAt(active);
                    // The caret's LINE is all that is read: a token never crosses a line
                    // break, and this runs on every caret movement — the whole document
                    // (getText) was being rebuilt for each arrow key on a long score.
                    const line = doc.lineAt(active.line).text;
                    const col = active.character;
                    // A caret on a chord as a whole — its "<", just past its ">", or what is
                    // glued after it — lights every head of it (owner's request 2026-10-27), as
                    // the step keys move it whole there.
                    const chord = chordAtCaret(line, col);
                    if (chord) {
                        const lineStart = offset - col;
                        panel.webview.postMessage({ type: 'highlightRange', ranges: [[lineStart + chord[0], lineStart + chord[1] + 1]], caret: offset });
                        return;
                    }
                    // A caret anywhere on a chord(…) / diagram(…) item — its word, inside its
                    // parentheses, on its duration — lights the item's notes (owner's request
                    // 2026-10-31): the token under the caret alone (`133211)4`) holds no note.
                    const item = shapeItemAt(line, col);
                    if (item) {
                        const lineStart = offset - col;
                        panel.webview.postMessage({ type: 'highlightRange', ranges: [[lineStart + item[0], lineStart + item[1]]], caret: offset });
                        return;
                    }
                    const isWs = (c: string | undefined) =>
                        c === undefined || c === ' ' || c === '\t' || c === '\n' || c === '\r';
                    // The caret's note is the one whose source lies INSIDE the
                    // whitespace-delimited token under the caret. Send that token's start so
                    // the preview highlights a note only when it belongs to THAT token —
                    // never the nearest PRECEDING note when the caret is on a barline,
                    // keyword, brace or a gap (their token holds no note, so the preview
                    // clears). A caret in pure whitespace corresponds to no note.
                    let tokenStart = -1;
                    if (!isWs(line[col]) || !isWs(line[col - 1])) {
                        let startCol = col;
                        while (startCol > 0 && !isWs(line[startCol - 1])) { startCol--; }
                        tokenStart = offset - (col - startCol);
                    }
                    panel.webview.postMessage({
                        type: 'highlightPosition',
                        position: tokenStart >= 0 ? offset : -1,
                        tokenStart,
                        // The caret itself, wherever it is: its bar stays lit (lightCaretBar).
                        caret: offset,
                        // Where its line ends: a caret before a bar's first note on that line
                        // (on `voice {`) lights the bar too.
                        lineEnd: offset - col + line.length,
                    });
                }
            }
        })
    );

    outputChannel.appendLine('Lily# extension activated');

    // The Markdown lily# fence: VS Code's Markdown preview asks for this object
    // (contributes.markdown.markdownItPlugins) and wires the plugin into its engine.
    return markdownItExtensionApi({
        getClient: () => client,
        isReady: () => clientReady,
        whenReady: ensureClientReady,
        log: (msg: string) => outputChannel.appendLine(msg),
    });
}

// The clock time of the last play-note fire, so the webview can tell a fresh
// key-press from an OS key-repeat of the same held key (both arrive as separate
// command invocations — VS Code has no key-up event).
let lastPlayNoteFire = 0;

/**
 * Audition through the preview webview's WebAudio synth: the caret's note
 * (mode 'note', sustained while the key is held via key-repeat) or the whole
 * measure the caret sits in (mode 'measure'). The synth lives in the preview, so
 * if no preview is open this first press only opens it — press again to play.
 */
function playAtCursor(context: vscode.ExtensionContext, mode: 'note' | 'measure', fresh = false) {
    const editor = vscode.window.activeTextEditor;
    if (!editor || editor.document.languageId !== 'lilysharp') { return; }
    const doc = editor.document;
    const panel = previewPanels.get(doc.uri.toString());
    if (!panel) {
        openPreview(context, vscode.ViewColumn.Beside);
        return;
    }
    const offset = doc.offsetAt(editor.selection.active);
    if (mode === 'measure') {
        const [rangeStart, rangeEnd] = auditionBarRange(doc.getText(), offset);
        panel.webview.postMessage({ type: 'playAtCursor', mode, rangeStart, rangeEnd, fresh });
    } else {
        const now = Date.now();
        const gapMs = now - lastPlayNoteFire;
        lastPlayNoteFire = now;
        panel.webview.postMessage({ type: 'playAtCursor', mode, position: offset, gapMs });
    }
}

// `preserveFocus`: the automatic opening (a .lys just opened) leaves the caret in the editor.
function openPreview(context: vscode.ExtensionContext, viewColumn: vscode.ViewColumn, preserveFocus = false) {
    const editor = vscode.window.activeTextEditor;
    if (!editor || editor.document.languageId !== 'lilysharp') {
        vscode.window.showWarningMessage('No Lily# file is open');
        return;
    }

    const document = editor.document;
    const uri = document.uri.toString();
    outputChannel.appendLine(`Opening preview for: ${uri}`);

    // Check if preview already exists for this document
    const existingPanel = previewPanels.get(uri);
    if (existingPanel) {
        outputChannel.appendLine('Revealing existing panel');
        existingPanel.reveal(viewColumn, preserveFocus);
        updatePreviewContent(document, existingPanel, context);
        return;
    }
    // Asked for by name: a preview the writer closed before is wanted again.
    dismissedPreviews.delete(uri);

    // Reuse an ORPHANED untitled preview (its buffer was saved to this file but the
    // automatic migration missed) rather than opening a second window: re-key it here.
    if (document.uri.scheme === 'file') {
        for (const key of previewPanels.keys()) {
            if (!key.startsWith('untitled:') || vscode.workspace.textDocuments.some(d => d.uri.toString() === key)) { continue; }
            migratePreviewKey(key, uri);
            const adopted = previewPanels.get(uri);
            if (adopted) {
                adopted.title = previewTitle(uri, adopted);
                adopted.reveal(viewColumn);
                updatePreviewContent(document, adopted, context);
                outputChannel.appendLine(`Reused orphaned preview ${key} -> ${uri}`);
                return;
            }
        }
    }

    // The follower turns to this file rather than a second preview opening beside it. Asked
    // for by name it does so even while playing: the writer chose to leave the piece.
    if (followerPanel) {
        retargetPreview(followerPanel, document, context);
        followerPanel.reveal(viewColumn, preserveFocus);
        return;
    }

    // Beside a locked preview on screen, not stacked on it: that one is kept to be compared.
    // (With no follower open, every preview there is is locked.)
    if (viewColumn === vscode.ViewColumn.Beside) {
        const locked = [...previewPanels.values()]
            .filter(p => p.visible && p.viewColumn !== undefined)
            .map(p => p.viewColumn as number);
        viewColumn = newPreviewColumn(locked) ?? viewColumn;
    }

    // Create new preview panel with access to the fonts, the synth and the sound font
    outputChannel.appendLine('Creating new preview panel');
    const panel = vscode.window.createWebviewPanel(
        'lilysharpPreview',
        '',
        { viewColumn, preserveFocus },
        {
            enableScripts: true,
            retainContextWhenHidden: true,
            localResourceRoots: previewResourceRoots(context.extensionUri)
        }
    );

    previewPanels.set(uri, panel);
    previewSessions.set(panel, new PreviewSession());
    // With no follower open, the new preview is it.
    followerPanel = panel;
    followerLocked = false;
    panel.title = previewTitle(uri, panel);
    if (panel.active) {
        void vscode.commands.executeCommand('setContext', 'lilysharp.previewLocked', false);
    }
    // Brought to the front: the title bar's lock shows this panel's state, and its source's
    // tab comes forward in another group (revealPreviewSource).
    let wasActive = panel.active;
    panel.onDidChangeViewState(() => {
        if (!panel.active) {
            wasActive = false;
            return;
        }
        if (wasActive) { return; }
        wasActive = true;
        void vscode.commands.executeCommand('setContext', 'lilysharp.previewLocked', panel !== followerPanel);
        revealPreviewSource(panel);
    });

    panel.onDidDispose(() => {
        if (followerPanel === panel) { followerPanel = undefined; }
        followerLocked = false;
        playingPanels.delete(panel);
        // Find this panel's CURRENT key by value: it may have been re-keyed when an
        // untitled score was saved to a file (see onDidCloseTextDocument), so the URI
        // captured here at creation can be stale — deleting by it would leak the entry.
        for (const [key, value] of previewPanels) {
            if (value !== panel) { continue; }
            outputChannel.appendLine(`Preview panel disposed: ${key}`);
            dismissedPreviews.add(key);
            previewPanels.delete(key);
            selectedRenders.delete(key);
            forgetVoices(panel);
        }
        previewSessions.get(panel)?.dispose();
        previewSessions.delete(panel);
    });

    // Handle messages from webview
    const openedUri = uri;
    // A strum's strings still to strike on the MIDI output (midiStrike with gapMs): timed HERE,
    // where Node's timers keep time, not in the page, whose timers a hidden preview fires about
    // once a second — the strings after the first came out together as a chord (owner's report
    // 2026-10-24). A release drops the ones not struck yet.
    let midiStrumTimers: ReturnType<typeof setTimeout>[] = [];
    // The follow-along's beat while the page plays (owner's report 2026-10-25): a page that
    // sounds nothing itself — the MIDI output plays — has its timers throttled to one a second
    // by the browser, so the highlight lit one eighth note in four. A message wakes the page
    // whatever its timers do; this one comes every 50 ms from here, where no one throttles.
    let followTicker: ReturnType<typeof setInterval> | undefined;
    const stopFollowTicker = () => {
        if (followTicker) { clearInterval(followTicker); followTicker = undefined; }
    };
    panel.onDidDispose(stopFollowTicker);
    panel.onDidDispose(() => {
        for (const t of midiStrumTimers) { clearTimeout(t); }
        midiStrumTimers = [];
        if (midiPlaybackPanel === panel) {
            midiPlaybackPanel = undefined;
            void midiOutRequest('lilysharp/midiOut/stop', {});
        }
        // A preview closed while it played leaves no text lit.
        showSoundingSource(panel, soundingLit.get(panel)?.uri, [], true);
    });
    // The sounding notes of the text and score this panel shows, kept per version: the header's
    // length and its position while stopped read them (a caret move must not export again).
    // Asked once the text has rested (soundingWait): `undefined` is an answer the text has
    // moved on from while it waited — the next refresh asks again, and nothing is posted.
    let soundingCache: { key: string; notes: Promise<TimedNote[] | null | undefined> } | undefined;
    panel.webview.onDidReceiveMessage(
        async message => {
            // The file the panel shows NOW: the follower turns, and an untitled save re-keys.
            const uri = panelKey(panel) ?? openedUri;
            const soundingNotes = (): Promise<TimedNote[] | null | undefined> => {
                const versionNow = () => vscode.workspace.textDocuments.find(d => d.uri.toString() === uri)?.version ?? -1;
                const version = versionNow();
                const renderName = selectedRenders.get(uri) || null;
                const key = `${uri}\n${version}\n${renderName ?? ''}`;
                if (soundingCache?.key !== key) {
                    const notes = (async () => {
                        for (let wait = soundingWait(lastEditAt.get(uri), Date.now()); wait > 0;
                            wait = soundingWait(lastEditAt.get(uri), Date.now())) {
                            await new Promise(r => setTimeout(r, wait));
                        }
                        if (versionNow() !== version) { return undefined; }
                        // (soundingOnly: the parts the score leaves out are dropped below anyway,
                        // and listing them costs the server a second export)
                        const pb = await client!.sendRequest<{ Notes?: TimedNote[] | null; Parts?: string[] | null; Extra?: string[] | null }>(
                            'lilysharp/playback', { textDocument: { uri }, renderName, soundingOnly: true });
                        return pb.Notes ? soundingOf(pb.Notes, pb.Parts, pb.Extra) : null;
                    })();
                    // (a failed fetch is not kept: the next asks again)
                    notes.catch(() => { if (soundingCache?.key === key) { soundingCache = undefined; } });
                    soundingCache = { key, notes };
                }
                return soundingCache.notes;
            };
            // The notes of an AI candidate sounding now: lit on the candidate panel's score.
            if (message.type === 'candidateSounding') {
                showCandidateSounding(message.sounding);
                return;
            }
            // The notes sounding now, for the editor to light their text — not logged: every
            // change of what sounds.
            if (message.type === 'soundingSource') {
                showSoundingSource(panel, uri,
                    Array.isArray(message.positions) ? message.positions.filter((p: unknown) => Number.isInteger(p)) : [],
                    message.ended === true);
                return;
            }
            if (message.type === 'playState') {
                // Not logged: every Play and every stop.
                if (message.playing) {
                    playingPanels.add(panel);
                    followTicker ??= setInterval(() => void panel.webview.postMessage({ type: 'followTick' }), 50);
                    return;
                }
                stopFollowTicker();
                playingPanels.delete(panel);
                const target = pendingFollow;
                pendingFollow = undefined;
                const doc = target === undefined || panel !== followerPanel ? undefined
                    : vscode.workspace.textDocuments.find(d => d.uri.toString() === target && !d.isClosed);
                if (doc) {
                    retargetPreview(panel, doc, context);
                }
                return;
            }
            if (message.type === 'hoverBar') {
                // The bar under the mouse, for a scan view beside it to light — not logged.
                if (scanDeps) {
                    void previewHoversBar(uri, Number.isInteger(message.start) ? message.start : null, scanDeps);
                }
                return;
            }
            if (message.type === 'visibleBar') {
                // Every settled scroll — not logged. At the very top the preview says so
                // instead of naming its first bar, and the scan goes to its own top.
                if (scanDeps && message.top === true) {
                    void previewShowsBar(uri, 'top', scanDeps);
                } else if (scanDeps && Number.isInteger(message.bar)) {
                    void previewShowsBar(uri, message.bar, scanDeps, typeof message.cut === 'number' ? message.cut : 0);
                }
                return;
            }
            outputChannel.appendLine(`Received message from webview: ${message.type}`);
            if (message.type === 'webviewReady') {
                // A (re)loaded webview is blank — drop the dedup memory so the next render
                // always re-posts, even if the SVG matches what a previous instance showed,
                // and the page version, so the next answer carries every page.
                sessionOf(panel).pageLoaded();
                postedToPage.delete(panel);
                if (scanDeps) { previewReady(uri, scanDeps); }
                return;
            }
            if (message.type === 'requestFull') {
                // The webview could not apply a page delta to what it shows (its account
                // is in `why`): forget the version it was against and render again — the
                // server answers with every page.
                outputChannel.appendLine(`Webview asks for the whole picture: ${message.why}`);
                sessionOf(panel).shownPagesVersion = undefined;
                const doc = await previewDocument(uri);
                if (doc) {
                    updatePreviewContent(doc, panel, context);
                }
                return;
            }
            if (message.type === 'webviewError') {
                outputChannel.appendLine(
                    `WEBVIEW ERROR: ${message.message} (line ${message.line})`);
                return;
            }
            if (message.type === 'audioBlocked') {
                // The webview's synth is not allowed to run until the user has clicked or
                // typed in the preview (the browser's autoplay policy; the editor's keys
                // do not count). Said once per blocked spell — the webview resets the flag
                // when sound gets through.
                outputChannel.appendLine(`Audio blocked in the preview (AudioContext ${message.state}): click the preview once`);
                vscode.window.setStatusBarMessage('Lily#: click the preview once to enable its sound', 8000);
                // A candidate's Play is pressed on its own panel: said there too, where the eyes are.
                tellCandidateAudioBlocked();
                return;
            }
            if (message.type === 'synthStatus') {
                // The SoundFont synth's account of itself: loading, ready (the font's size
                // and load time), failed (why — the page falls back to oscillator tones),
                // or none (no font configured or bundled).
                outputChannel.appendLine(`Preview synth ${message.state}: ${message.detail}`);
                if (message.state === 'failed') {
                    vscode.window.setStatusBarMessage(`Lily#: the preview's sound font did not load (see the Lily# Extension output)`, 8000);
                }
                return;
            }
            if (message.type === 'webviewPerf') {
                // The preview's own account of one update — what it swapped and
                // how long the swap took in the page's thread — beside the
                // request timing logged by updatePreviewContent, so a slow
                // update can be laid at the server's door or the page's.
                outputChannel.appendLine(`PREVIEW ${message.text}`);
                return;
            }
            if (message.type === 'recordingRendered') {
                // A piece of the recording an export asked the page to render (recordJobs).
                recordingRendered(message);
                return;
            }
            if (message.type === 'practiceState') {
                // The page's practice menu, for the exports of the file it shows.
                const was = previewPractice.get(panel);
                const now = message.practice as PracticeState;
                previewPractice.set(panel, now);
                // A new tempo: the voice is sung again at it (docs/singing-design.md §5). A voice
                // the score does not sing turned on (voicesOn, session 1163): asked for at once.
                const onBefore = (was?.voicesOn ?? []).join('\n'), onNow = (now.voicesOn ?? []).join('\n');
                if (now.speed !== was?.speed) { void prepareVoices(panel, uri, 'speed'); }
                else if (onNow !== onBefore) { void prepareVoices(panel, uri, 'play'); }
                return;
            }
            // A strip of the A-B repeat kept, renamed or cleared: the file's eight, kept.
            if (message.type === 'abRanges') {
                if (typeof message.chart === 'string') { keepAbRanges(message.chart, message.ranges); }
                return;
            }
            if (message.type === 'panelFolds') {
                if (typeof message.chart === 'string') { keepPanelFolds(message.chart, message.folds); }
                return;
            }
            // The recording panel's settings changed: kept for this recording (recording.ts).
            if (message.type === 'recordingSettings') {
                if (typeof message.chart === 'string' && typeof message.name === 'string') {
                    keepRecordingSettings(message.chart, message.name, message.settings);
                }
                return;
            }
            if (message.type === 'requestPlayback') {
                // The webview cannot reach the LSP: fetch note events here and
                // hand them back for WebAudio scheduling.
                if (!client || !clientReady) {
                    panel.webview.postMessage({ type: 'playbackData', seq: message.seq, error: 'language server not running' });
                    return;
                }
                // Another file, or another score of it: the page's voices are the last one's, kept
                // by PART NAME, and a Play right after the switch would sing them over the new notes
                // (owner's report 2026-11-08: the previous .lys's voice). Cleared before the notes
                // go out — the messages arrive in order — so this Play plays the instruments, and
                // prepareVoices below sends the new file's voices as they are ready. (The render path
                // does the same the moment the preview turns to another document: followVoices.)
                followVoices(panel, uri);
                try {
                    // Name the score the preview is showing, so Play sounds ITS parts and
                    // form: with `score p2 { staff p2 }` picked, p1 stayed audible
                    // until the server was told which score (user report 2026-09-29).
                    const pb = await client.sendRequest<{ Notes?: { T: number, D: number, P: number, V: number, N?: number, U?: boolean, W?: string[] | null }[], Error?: string,
                        Parts?: string[] | null, Extra?: string[] | null, Sung?: string[] | null, Played?: string[] | null,
                        Beats?: { T: number; A: boolean }[] | null; BarEnd?: number; Lights?: { T: number, D: number, P: number, V: number }[] | null }>(
                        'lilysharp/playback', { textDocument: { uri }, renderName: selectedRenders.get(uri) || null });
                    // The tracks and the beats ride along for the practice menu's parts and metronome;
                    // the parts the score does not sound (Extra) too, off until the menu turns them on;
                    // and which parts the score sings and plays (docs/singing-design.md §3).
                    // A line of a part that is not sung here (nobody installed can sing it) leaves
                    // its sections to the instrument where another line of the part sings.
                    // The editor's text's own lights (rests, the form's names, the words: Lights) go
                    // among the notes, so the page's timing — tempo, A-B, loop, the recording's bars —
                    // reaches them as it does the notes; nothing sounds them, the score draws none.
                    const notes = pb.Notes ? leftToTheInstrument(pb.Notes, missingVoices.get(panel) ?? []).concat(pb.Lights ?? []) : null;
                    // (seq: the page's request, said back — it plays only the answer to its latest)
                    panel.webview.postMessage({ type: 'playbackData', seq: message.seq, notes, error: pb.Error ?? null,
                        parts: pb.Parts ?? null, extra: pb.Extra ?? null, sung: pb.Sung ?? null, played: pb.Played ?? null,
                        beats: pb.Beats ?? null, barEnd: pb.BarEnd ?? null });
                    // The lyrics' voice, rendered now — a voice the text's rest had not yet sent to
                    // the engine goes at once — so the next Play finds it (docs/singing-design.md §5).
                    void prepareVoices(panel, uri, 'play');
                } catch (e) {
                    panel.webview.postMessage({ type: 'playbackData', seq: message.seq, error: String(e) });
                }
                return;
            }
            // The practice menu's part switches (owner's request 2026-10-25): the tracks of the
            // score shown that sound a note, in score order; then (owner's request 2026-11-08) the
            // sung lyrics, one a part with a voice, and the file's parts the score does not sound.
            if (message.type === 'requestParts') {
                if (!client || !clientReady) { return; }
                try {
                    const pb = await client.sendRequest<{ Notes?: { N?: number }[] | null; Parts?: string[] | null; Extra?: string[] | null; Sung?: string[] | null; Played?: string[] | null }>(
                        'lilysharp/playback', { textDocument: { uri }, renderName: selectedRenders.get(uri) || null });
                    const sounding = new Set((pb.Notes ?? []).map(n => n.N));
                    const extraSet = new Set(pb.Extra ?? []);
                    const parts = (pb.Parts ?? []).filter((p, i) => sounding.has(i) && !extraSet.has(p));
                    const extra = (pb.Parts ?? []).filter((p, i) => sounding.has(i) && extraSet.has(p));
                    // The parts the score only sings (a singer's staff, no staff without words): their
                    // instrument is off by default, the voice sings (session 1165).
                    const played = new Set(pb.Played ?? []);
                    const sungOnly = (pb.Sung ?? []).filter(p => !played.has(p));
                    // Every singable lyrics of the file (session 1163), and which of them the
                    // score shown sings itself — the others are off until the menu turns them on.
                    let voices: string[] = [], voicesInScore: string[] = [], voiceLabels: Record<string, string> = {}, voiceTitles: Record<string, string> = {};
                    if (await renderer()) {
                        const sung = await client.sendRequest<{ Voices?: SungVoiceDto[] | null }>(
                            'lilysharp/sung', { textDocument: { uri }, renderName: selectedRenders.get(uri) || null, allLyrics: true });
                        const singable = (sung.Voices ?? []).filter(v => v.Notes.length > 0);
                        voices = singable.map(sungKey);
                        voicesInScore = singable.filter(v => v.InScore !== false).map(sungKey);
                        voiceLabels = voiceMenuLabels(singable);
                        voiceTitles = Object.fromEntries(singable.map(v => [sungKey(v), voiceMenuTitleOf(v, voiceLabels[sungKey(v)])]));
                        menuVoices.set(panel, voiceMenuKey(singable));
                    }
                    panel.webview.postMessage({ type: 'partsData', parts, extra, voices, voicesInScore, voiceLabels, voiceTitles, sungOnly });
                } catch {
                    panel.webview.postMessage({ type: 'partsData', parts: [], extra: [], voices: [] });
                }
                return;
            }
            // The header's length (owner's request 2026-10-25): how long the score shown plays,
            // repeats and tempo changes included — when its last note ends (the parts the score
            // does not sound left out).
            if (message.type === 'requestLength') {
                if (!client || !clientReady) { return; }
                try {
                    const notes = await soundingNotes();
                    if (notes === undefined) { return; }
                    const seconds = (notes ?? []).reduce((end, n) => Math.max(end, n.T + n.D), 0);
                    panel.webview.postMessage({ type: 'lengthData', seconds: notes ? seconds : null });
                } catch {
                    panel.webview.postMessage({ type: 'lengthData', seconds: null });
                }
                return;
            }
            // Where Play would start, in the score's seconds, for the header while nothing plays
            // (owner's request 2026-11-16): the note the caret resolved to (exact: its first
            // onset, as Play's start), else the first one sounding at or after the place given.
            // Read off the length's notes (soundingNotes, kept per text and score).
            if (message.type === 'requestPositionTime') {
                if (!client || !clientReady) { return; }
                try {
                    const notes = await soundingNotes();
                    if (notes === undefined) { return; }
                    panel.webview.postMessage({ type: 'positionTime', seq: message.seq,
                        seconds: notes ? startSecondsAt(notes, message.position, !!message.exact) : null });
                } catch {
                    panel.webview.postMessage({ type: 'positionTime', seq: message.seq, seconds: null });
                }
                return;
            }
            // The Range panel (owner's request 2026-10-25): each part's lowest and highest note
            // in the score shown, and where each is written.
            if (message.type === 'requestRanges') {
                if (!client || !clientReady) {
                    panel.webview.postMessage({ type: 'rangesData', error: 'language server not running' });
                    return;
                }
                try {
                    const r = await client.sendRequest<{ Parts?: unknown[] | null; Error?: string | null }>(
                        'lilysharp/ranges', { textDocument: { uri }, renderName: selectedRenders.get(uri) || null });
                    panel.webview.postMessage({ type: 'rangesData', parts: r.Parts ?? null, error: r.Error ?? null });
                } catch (e) {
                    panel.webview.postMessage({ type: 'rangesData', error: String(e) });
                }
                return;
            }
            // MIDI out (docs/playback-design.md §6): the page decides what plays and the
            // server is the port. A play answers when the playback's zero is on the wall
            // clock, which the page's highlight then follows; a play the server could not
            // start (the port is not there) is reported, and the page plays its own synth.
            if (message.type === 'midiPlay') {
                midiPlaybackPanel = panel;
                const r = await midiOutRequest<{ Started?: boolean; StartedAtMs?: number; EndSeconds?: number; Error?: string | null }>(
                    'lilysharp/midiOut/play', { port: message.port, notes: message.notes, offset: message.offset });
                if (r.Error) { reportMidiOutError(message.port, r.Error); }
                outputChannel.appendLine(`MIDI out: ${(message.notes ?? []).length} notes from ${Number(message.offset ?? 0).toFixed(3)} s`
                    + `, to ${Number(r.EndSeconds ?? 0).toFixed(3)} s`);
                panel.webview.postMessage({
                    type: 'midiStarted', ok: !!r.Started, t0Ms: r.StartedAtMs ?? 0, end: r.EndSeconds ?? 0, error: r.Error ?? null,
                });
                return;
            }
            if (message.type === 'midiStop') {
                if (midiPlaybackPanel === panel) { midiPlaybackPanel = undefined; }
                void midiOutRequest('lilysharp/midiOut/stop', {});
                return;
            }
            if (message.type === 'midiStrike') {
                const strike = async (pitches: number[]) => {
                    const r = await midiOutRequest<{ Channel?: number; Error?: string | null }>('lilysharp/midiOut/strike',
                        { port: message.port, pitches, timbre: message.timbre, program: message.program });
                    if (r.Error) { reportMidiOutError(message.port, r.Error); }
                };
                const pitches: number[] = message.pitches ?? [];
                if (message.gapMs > 0 && pitches.length > 1) {
                    for (const t of midiStrumTimers) { clearTimeout(t); }
                    // Apart (a bass's strings): the string before stops as each one sounds, on the
                    // channel the page releases (the audition's, the drums' for the drum family).
                    const channel = message.timbre === 9 ? 9 : 15;
                    midiStrumTimers = pitches.slice(1).map((p, i) => setTimeout(() => {
                        if (message.apart) {
                            void midiOutRequest('lilysharp/midiOut/release', { channel, pitches: [pitches[i]] });
                        }
                        void strike([p]);
                    }, (i + 1) * message.gapMs));
                    await strike([pitches[0]]);
                } else {
                    await strike(pitches);
                }
                return;
            }
            if (message.type === 'midiRelease') {
                for (const t of midiStrumTimers) { clearTimeout(t); }
                midiStrumTimers = [];
                void midiOutRequest('lilysharp/midiOut/release', { channel: message.channel, pitches: message.pitches });
                return;
            }
            if (message.type === 'export') {
                await exportPreview(context, uri, message.renderName);
            } else if (message.type === 'jumpToPosition') {
                const targetEditor = vscode.window.visibleTextEditors.find(
                    e => e.document.uri.toString() === uri
                ) ?? await openPreviewSource(panel, uri);
                if (targetEditor) {
                    // A grob's data-pos is its node's full-span start — the leading
                    // indentation, a title's opening quote, a mark's `@` — and the caret
                    // goes past it to the thing clicked (jumpOffsetFor).
                    const doc = targetEditor.document;
                    // A note of a staff Lily# made (a score's staff NAME: what that singer
                    // sings) stands past the file's own text: there is no place to go to.
                    if (typeof message.position === 'number' && message.position >= doc.getText().length) {
                        return;
                    }
                    const position = doc.positionAt(jumpOffsetFor(doc.getText(), message.position));
                    targetEditor.selection = new vscode.Selection(position, position);
                    targetEditor.revealRange(
                        new vscode.Range(position, position),
                        vscode.TextEditorRevealType.InCenter
                    );
                    vscode.window.showTextDocument(targetEditor.document, targetEditor.viewColumn);
                }
            } else if (message.type === 'selectRender') {
                selectedRenders.set(uri, message.renderName);
                // ...through previewDocument, so a preview whose editor tab was closed
                // still answers: it used to find no document and return, which looked
                // exactly like a picker that does not switch scores.
                const doc = await previewDocument(uri);
                if (doc) {
                    updatePreviewContent(doc, panel, context);
                }
            } else if (message.type === 'askAiInChat') {
                // The header's ✨ and Ctrl+I (owner's requests 2026-10-29): Copilot's chat, the bars selected on
                // the score as the pointer, else the file — put in its input, unsent.
                const doc = await previewDocument(uri);
                if (doc) {
                    const picked = Number.isInteger(message.startPos) && message.startPos >= 0;
                    await askAiInChat(doc, picked ? message.startPos : undefined,
                        picked ? noteSelectionEnd(doc.getText(), message.endPos ?? message.startPos) : undefined);
                }
            }
        },
        undefined,
        context.subscriptions
    );

    // Get font URIs for HTML (must use webview URI for security). The brace
    // font (Emmentaler-Brace) is a SEPARATE face used for grand-staff/group
    // braces; without it the brace glyph renders blank in the preview.
    const fontUri = panel.webview.asWebviewUri(
        vscode.Uri.joinPath(context.extensionUri, 'media', 'fonts', 'emmentaler-20.woff2')
    );
    const braceFontUri = panel.webview.asWebviewUri(
        vscode.Uri.joinPath(context.extensionUri, 'media', 'fonts', 'emmentaler-brace.woff2')
    );

    // Set initial HTML structure with font
    outputChannel.appendLine('Setting webview HTML');
    panel.webview.html = getPreviewHtml(fontUri.toString(), braceFontUri.toString(), panel.webview.cspSource, getNonce(), getPreviewTheme(),
        textFontFaceCss(panel.webview, context.extensionUri), synthUrls(panel.webview, context.extensionUri),
        midiOutputSetting());

    // Then load content
    outputChannel.appendLine('Calling updatePreviewContent');
    updatePreviewContent(document, panel, context);
}

/** An m4a the preview cannot decode, decoded by the language server (Windows; recording-design §1 5). */
async function decodeRecordingOnServer(input: string, output: string): Promise<DecodeResult> {
    if (!clientReady) {
        await Promise.race([clientReadyPromise, new Promise(r => setTimeout(r, 15000))]);
    }
    if (!clientReady) {
        return { Ok: false, Error: 'the language server is not running' };
    }
    return client.sendRequest<DecodeResult>('lilysharp/decodeRecording', { Input: input, Output: output });
}

/** A file beside a chart written or removed (Attach Recording…, the bar marks, Dettato): the
 *  chart's open preview hears of it at once. */
function watchRecordings(context: vscode.ExtensionContext): vscode.Disposable {
    const watcher = vscode.workspace.createFileSystemWatcher('**/*.dettato.json');
    const changed = (file: vscode.Uri) => {
        for (const [uri, panel] of previewPanels) {
            const chart = vscode.Uri.parse(uri);
            if (chart.scheme === 'file' && recordingPathFor(chart.fsPath).toLowerCase() === file.fsPath.toLowerCase()) {
                void sendRecording(panel, chart, context.globalStorageUri, decodeRecordingOnServer);
            }
        }
    };
    return vscode.Disposable.from(watcher, watcher.onDidChange(changed), watcher.onDidCreate(changed), watcher.onDidDelete(changed));
}

/** Where the A-B repeat's kept ranges live (docs/ab-repeat-design.md §1: the strips under the
 *  practice menu, kept per file in the workspace — the A-B the flags set is the page's and not kept). */
let abRangesStore: vscode.Memento | undefined;

/** The workspace-state key of a file's ranges: its path, case aside, or the URI of a file not on disk. */
function abRangesKey(uri: string): string {
    const u = vscode.Uri.parse(uri);
    return 'lilysharp.abRanges:' + (u.scheme === 'file' ? path.resolve(u.fsPath).toLowerCase() : uri);
}

/** The eight strips kept for `uri`, empty ones null. */
function abRangesFor(uri: string) {
    return normalizeAbRanges(abRangesStore?.get<unknown>(abRangesKey(uri)));
}

/** Keeps the eight strips the page sent for `uri` (all empty: forgotten). */
function keepAbRanges(uri: string, ranges: unknown): void {
    if (!abRangesStore) { return; }
    const kept = normalizeAbRanges(ranges);
    void abRangesStore.update(abRangesKey(uri), kept.some(r => r) ? kept : undefined);
}

/** The practice panel's folds kept for `uri` (owner's request 2026-11-17: per file; a file whose
 *  panel was never opened has none, and opens all folded): group key → folded, `_sp` → Speed
 *  and pitch open. Kept beside the A-B ranges, in the workspace's state. */
function panelFoldsKey(uri: string): string {
    return abRangesKey(uri).replace(/^lilysharp\.abRanges:/, 'lilysharp.panelFolds:');
}
function panelFoldsFor(uri: string): Record<string, boolean> | null {
    const kept = abRangesStore?.get<unknown>(panelFoldsKey(uri));
    if (!kept || typeof kept !== 'object') { return null; }
    const out: Record<string, boolean> = {};
    for (const [k, v] of Object.entries(kept as Record<string, unknown>)) { if (typeof v === 'boolean') { out[k] = v; } }
    return out;
}
function keepPanelFolds(uri: string, folds: unknown): void {
    if (!abRangesStore || !folds || typeof folds !== 'object') { return; }
    const out: Record<string, boolean> = {};
    for (const [k, v] of Object.entries(folds as Record<string, unknown>)) { if (typeof v === 'boolean' && k.length < 32) { out[k] = v; } }
    void abRangesStore.update(panelFoldsKey(uri), out);
}

/** The last message of each type a page was posted (messageChanged); forgotten when it reloads. */
const postedToPage = new WeakMap<vscode.WebviewPanel, Map<string, string>>();

/** Posts `message` to the page unless it holds the same one of that type already. */
function postIfChanged(panel: vscode.WebviewPanel, message: { type: string; [key: string]: unknown }): void {
    let kept = postedToPage.get(panel);
    if (!kept) { postedToPage.set(panel, kept = new Map()); }
    if (messageChanged(kept, message)) {
        void panel.webview.postMessage(message);
    }
}

async function updatePreviewContent(
    document: vscode.TextDocument,
    panel: vscode.WebviewPanel,
    context: vscode.ExtensionContext,
    retries: number = 8
) {
    const uri = document.uri.toString();
    const selectedRender = selectedRenders.get(uri);
    const session = sessionOf(panel);

    // A closed document can never be rendered — the server dropped it on didClose.
    // This happens for an untitled buffer just saved to a file (its URI is gone) or a
    // stale debounced refresh; skip instead of retrying a lookup that never succeeds.
    if (document.isClosed) {
        outputChannel.appendLine(`Skipping preview render for closed document ${uri}`);
        return;
    }

    outputChannel.appendLine(`updatePreviewContent called for ${uri}, clientReady=${clientReady}`);

    // What the page is told beside the picture. With the server up it runs AFTER the render
    // request has left (below): until session 1294 all of it — a file looked for, the
    // workspace's state read twice, the whole text scanned for its chords, the recording's
    // file looked at — stood between the keystroke and the request (review of session 1291, P5).
    const besideThePicture = async () => {
        // Never post into a webview that has not loaded its HTML yet — VS Code
        // drops such messages silently and the panel sticks on "Loading…". The
        // 3 s race is a safety net against a webview that never reports in; a page that
        // has reported in is not waited for (each refresh left a 3 s timer running).
        if (!session.pageHasLoaded) {
            await Promise.race([session.loaded, new Promise<void>(r => setTimeout(r, 3000))]);
        }
        // Whether the right-click menu offers the OMR doors (owner, 2026-10-24: not for a file
        // the reader did not write) — asked again with each render, as an import adds the file.
        // (These three follow the file and the workspace, not the text: posted when they changed —
        // postIfChanged — and not with every keystroke's refresh.)
        postIfChanged(panel, { type: 'scanState', hasScan: hasScan(document.uri) });
        // The A-B repeat's kept ranges of this file (the page draws its eight strips from them).
        postIfChanged(panel, { type: 'abRanges', chart: uri, ranges: abRangesFor(uri) });
        // …and its practice panel's folds (null: never opened for this file — it opens all folded).
        postIfChanged(panel, { type: 'panelFolds', chart: uri, folds: panelFoldsFor(uri) });
        // Another document or score: its voices replace the last one's, asked for once the server
        // has answered the render below (prepareVoices; singing-design §5).
        followVoices(panel, uri);
        // The chords of the text, for Select Chord on a member right-clicked (owner's request 2026-10-27).
        void panel.webview.postMessage({ type: 'chordSpans', spans: chordSpans(document.getText()) });
        // The recording beside the chart (docs/recording-design.md): sent when it or the chart changed.
        if (document.uri.scheme === 'file') {
            void sendRecording(panel, document.uri, context.globalStorageUri, decodeRecordingOnServer);
        }
    };
    // Wait for client to be ready if not already
    const serverWasUp = clientReady;
    if (!clientReady) {
        // (Nothing to send a request to yet: the page hears the rest first, as it always did.)
        await besideThePicture();
        outputChannel.appendLine('Client not ready, sending loading message');
        panel.webview.postMessage({
            type: 'updateContent',
            loading: true,
            renders: [],
            selectedRender: ''
        });

        outputChannel.appendLine('Waiting for clientReadyPromise...');
        try {
            await clientReadyPromise;
            outputChannel.appendLine('clientReadyPromise resolved');
        } catch (err) {
            outputChannel.appendLine(`clientReadyPromise rejected: ${err}`);
            panel.webview.postMessage({
                type: 'updateContent',
                error: 'Language server failed to start',
                renders: [],
                selectedRender: ''
            });
            return;
        }
    }

    if (!client) {
        outputChannel.appendLine('ERROR: client is null');
        panel.webview.postMessage({
            type: 'updateContent',
            error: 'Language server not available',
            renders: [],
            selectedRender: ''
        });
        return;
    }

    // The selection is logged with the request and the answer: "the preview will not
    // switch scores" is a report about these two words, and without them in the log the
    // question cannot be settled from a session that already happened.
    outputChannel.appendLine(`Sending lilysharp/svg request (score ${JSON.stringify(selectedRender ?? null)})...`);
    const requestedAt = Date.now();
    // Stamp this request and cancel the previous one still in flight: the server
    // collapses a queued burst to the newest ticket by itself, but only a client-side
    // generation can order the RESPONSES — without it, a slow old answer arriving
    // after a fast new one would paint the older picture last.
    // The panel turned to another document while this call waited (for the server to start):
    // the request on its way is that document's, and is not this call's to cancel.
    if (previewPanels.get(uri) !== panel) {
        outputChannel.appendLine(`The preview no longer shows ${uri}`);
        return;
    }
    const requestCancellation = new vscode.CancellationTokenSource();
    const generation = session.beginRequest(requestCancellation);
    const lagProbe = new EventLoopLagProbe();
    lagProbe.start();
    try {
        // The version the webview holds when the request leaves; the answer is checked
        // against what it holds when the answer arrives (a reload in between blanks it).
        const shownVersion = session.shownPagesVersion ?? null;
        const rendering = client.sendRequest<SvgResponse>('lilysharp/svg', {
            textDocument: { uri: uri },
            renderName: selectedRender || null,
            clientSentAt: requestedAt,
            pageDiff: true,
            shownVersion
        }, requestCancellation.token);
        // The @todo marks of the text, for Resolve This TODO on the bar right-clicked and Resolve
        // All TODOs off the staves (owner's request 2026-10-31). Asked AFTER the render request has
        // left: it was written to the connection ahead of it on every refresh (review of session 1291).
        const marksAsked = todoMarksOf(document);
        // …and so is everything else the page is told. The answers are posted after it: the
        // page has loaded by then, and hears these before the picture, as before.
        const beside = serverWasUp
            ? besideThePicture().catch(e => outputChannel.appendLine(`Beside the picture: ${e}`))
            : Promise.resolve();
        void Promise.all([marksAsked, beside]).then(([marks]) => {
            if (marks && previewPanels.get(uri) === panel) {
                void panel.webview.postMessage({ type: 'todos', marks: marks.map(m => ({ start: m.Start, hostStart: m.HostStart })) });
            }
        });
        const [response] = await Promise.all([rendering, beside]);
        const hostLag = lagProbe.stop();

        // The round trip, split where the server's clock says it went: a stall shows up
        // as a large `to server` (transport or the server's dispatch loop), `queued`
        // (its thread pool), `lock` (an older render of the same selection still
        // running) or `back` (the return leg, incl. this extension host's event loop)
        // — not as `render`, which is the engine's own time.
        const t = response.Timing;
        const ms = (v: number | null | undefined) => v == null ? '-' : Math.round(v).toString();
        const split = t
            ? `: to server ${ms(t.ReceivedAfterMs)}, queued ${ms(t.QueuedMs)}, expand ${ms(t.ExpandMs)},`
              + ` lock ${ms(t.LockWaitMs)}, render ${ms(t.RenderMs)}, server total ${ms(t.TotalServerMs)},`
              + ` back ${ms(Date.now() - t.ServerSentAt)}; last diagnostics ${ms(t.DiagnosticsLastMs)},`
              + ` last didChange ${ms(t.LastDidChangeMs)}, pool ${t.ThreadPoolThreads ?? '-'} threads`
              + ` / ${t.PendingWorkItems ?? '-'} queued`
            : '';
        const pagesAccount = response.Pages ? `, pages=${pagesSummary(response.Pages)}` : '';
        outputChannel.appendLine(`Got response: error=${response.Error}, hasSvg=${!!response.Svg}${pagesAccount}`
            + `, drew score ${JSON.stringify(response.SelectedRender ?? null)}`
            + ` (${Date.now() - requestedAt} ms round trip${split}; host lag max ${hostLag} ms)`);

        // A newer request owns the preview now — its response paints, this one drops.
        if (!session.isLatest(generation)) {
            outputChannel.appendLine('Dropping svg response: a newer request was issued');
            return;
        }
        // The server latest-wins machinery skipped this render (a newer request had
        // arrived server-side); the newer response carries the picture.
        if (response.Superseded) {
            outputChannel.appendLine('Dropping svg response: superseded server-side');
            return;
        }

        // Panel may have been disposed during async request
        if (!previewPanels.has(uri)) {
            outputChannel.appendLine('Panel was disposed during request');
            return;
        }

        if (response.Error === 'Document not found' && retries > 0 && previewPanels.has(uri)) {
            // The language server has not registered this document yet — e.g. a file
            // JUST saved from an untitled buffer, whose didOpen is still in flight.
            // Retry briefly so the preview self-heals instead of flashing an error the
            // user has to clear with Ctrl+K V.
            outputChannel.appendLine(`Document not tracked yet, retrying (${retries} left): ${uri}`);
            setTimeout(() => updatePreviewContent(document, panel, context, retries - 1), 150);
            return;
        }
        // What the picker must show is the score the server DREW, not the one this
        // request asked for: a selection the document no longer has (a block renamed or
        // removed since it was picked) resolves to the default score, and a picker still
        // naming the old one would make the preview look stuck on the default score with
        // nothing to say why. Older servers do not answer this; then the request stands.
        // What the drawn score transposes the music by: a recording beside the chart follows it.
        panel.webview.postMessage({ type: 'chartTranspose', semitones: response.ChartTranspose ?? 0 });
        const drawnRender = response.SelectedRender ?? selectedRender ?? '';
        if (drawnRender !== (selectedRender ?? '')) {
            outputChannel.appendLine(`Score ${JSON.stringify(selectedRender ?? null)} is not in this`
                + ` document; drew ${JSON.stringify(drawnRender)} instead`);
            selectedRenders.set(uri, drawnRender);
        }
        if (response.Pages) {
            // The picture as pages (SvgPages): a delta against the version the webview
            // holds carries only the changed pages' markup, a full answer every page's.
            // The delta is applied to what the webview holds NOW: if that is no longer the
            // version the server built it against (the webview reloaded, or a request
            // that forgot the version raced this one), the delta is unusable — ask again
            // with no version, which gets every page.
            const pages = response.Pages;
            const held = session.shownPagesVersion ?? null;
            if (pages.BaseVersion != null && pages.BaseVersion !== held) {
                outputChannel.appendLine(`Page delta is against version ${pages.BaseVersion}, the webview holds `
                    + `${held}; asking for every page`);
                session.shownPagesVersion = undefined;
                if (retries > 0) {
                    setTimeout(() => updatePreviewContent(document, panel, context, retries - 1), 0);
                }
                return;
            }
            // A page answer is a few hundred bytes when nothing changed, so it is posted
            // every time (the picker's list and the banner ride with it); the one-string
            // dedup memory no longer describes what the webview shows.
            session.lastPostedSvg = undefined;
            session.shownPagesVersion = pages.Version;
            outputChannel.appendLine(`Sending pages to webview (${pagesSummary(pages)}`
                + `${response.Error ? ', with error banner' : ''})`);
            panel.webview.postMessage({
                type: 'updateContent',
                pages,
                error: response.Error ?? undefined,
                renders: response.Renders || [],
                selectedRender: drawnRender,
                scoreKey: `${uri}\n${drawnRender}`     // the pages path too (session 1161: it was left out in 1158)
            });
        } else if (response.Svg) {
            // The response may carry an error TOO: a file with parse errors still
            // renders best-effort (the bad parts are dropped), the score shows
            // un-dimmed, and the banner carries the message. The dedup key folds
            // the banner text in so an error-text change still posts.
            // Skip the post when both are identical to what the webview already
            // shows — this is what spares a rapid edit/toggle/save burst from
            // re-shipping the same large SVG. A different render selection
            // compiles to a different SVG, so equality already implies the render
            // — but NOT the picker's list: a `score { }` pasted below the one being
            // drawn changes nothing in the picture, and the picker only learns of
            // it from this message (user report 2026-09-11: a pasted score did not
            // appear in the preview until some later edit moved the picture). The
            // list and the drawn name are folded into the key; a post whose SVG is
            // unchanged is cheap on the webview side (the page markup compares
            // equal and every page is kept).
            const key = svgPostKey(response.Svg, response.Error, response.Renders, drawnRender);
            if (session.lastPostedSvg === key) {
                outputChannel.appendLine(`SVG unchanged (length=${response.Svg.length}), skipping post`);
            } else {
                outputChannel.appendLine(`Sending SVG to webview (length=${response.Svg.length}`
                    + `${response.Error ? ', with error banner' : ''})`);
                session.lastPostedSvg = key;
                // A one-string picture is not a version the next delta can be against.
                session.shownPagesVersion = undefined;
                panel.webview.postMessage({
                    type: 'updateContent',
                    svg: response.Svg,
                    error: response.Error ?? undefined,
                    renders: response.Renders || [],
                    selectedRender: drawnRender,
                    // Which document and score this is: the practice menu's parts follow it while
                    // the menu is open (owner's report 2026-11-09, session 1158).
                    scoreKey: `${uri}\n${drawnRender}`
                });
            }
        } else if (response.Error) {
            outputChannel.appendLine(`Sending error to webview: ${response.Error}`);
            // Nothing rendered: the webview keeps its last good score DIMMED with
            // the error banner. Forget the cached SVG so the next successful render
            // is always posted — otherwise, fixing an error back to a picture
            // identical to the pre-error one hits the "unchanged, skip" path above
            // and the error stays on screen forever.
            session.lastPostedSvg = undefined;
            panel.webview.postMessage({
                type: 'updateContent',
                error: response.Error,
                renders: response.Renders || [],
                selectedRender: drawnRender,
                scoreKey: `${uri}\n${drawnRender}`
            });
        } else {
            outputChannel.appendLine('Response has neither error nor SVG');
        }
        // The sung voice follows the text (owner's request 2026-11-08, session 1148): after each
        // render the host learns which parts' voices are of an earlier text, drops them from the
        // page (the microphone turns grey), and sends them to the engine once the text has rested
        // PREPARE_IDLE_MS — or, with the setting at onPlay, at the next Play (singing-design §5).
        void prepareVoices(panel, uri, 'edit');
    } catch (error) {
        // Our own cancellation (a newer request took over, or the panel/URI went
        // away): expected, and the newer request repaints — no error banner.
        if (!session.isLatest(generation)
            || requestCancellation.token.isCancellationRequested) {
            outputChannel.appendLine(`Svg request cancelled by a newer one: ${error}`);
            return;
        }
        outputChannel.appendLine(`Request failed: ${error}`);
        if (previewPanels.has(uri)) {
            // Same reason as the error branch above: an error replaces the SVG on screen, so
            // drop the cache or the next identical render is skipped and the error persists.
            session.lastPostedSvg = undefined;
            panel.webview.postMessage({
                type: 'updateContent',
                error: `Failed to generate preview: ${error}`,
                renders: [],
                selectedRender: ''
            });
        }
    } finally {
        lagProbe.stop();
    }
}

interface ExportResponse {
    Success: boolean;
    OutputPath: string | null;
    Error: string | null;
    /** A WAV, MP3 or MP4 asked for with playbackOnly: the file and the notes (and an MP4's
     *  pictures), for the editor to render. */
    Playbacks?: AudioJob[] | null;
}

// Opening/revealing an exported file goes to the OS directly rather than through
// vscode.env.openExternal / the revealFileInOS command, because both of those
// stringify the URI on the way out: openExternal hands the shell
// `uri.toString()`, so 日本語.pdf arrives as %E6%97%A5%E6%9C%AC%E8%AA%9E.pdf and
// nothing opens (microsoft/vscode#83610), and revealFileInOS reveals whichever
// editor has focus when it is driven from code instead of the explorer
// (microsoft/vscode#105666). Spawning the platform's opener passes the path as an
// argv entry, which no encoding step can touch. Note `cmd /c start` is NOT an
// option here — cmd's OEM code page mangles non-ASCII names by itself.
// `verbatim` hands Windows the command line EXACTLY as written instead of letting
// Node quote each argument. Needed by one caller only — see revealInFileManager.
function spawnOpener(cmd: string, args: string[], verbatim = false) {
    try {
        const child = cp.spawn(cmd, args, {
            detached: true,
            stdio: 'ignore',
            windowsVerbatimArguments: verbatim,
        });
        // explorer.exe exits 1 even when it succeeded, so only a spawn failure
        // (missing binary) is worth reporting.
        child.on('error', err => outputChannel.appendLine(
            `open failed: ${cmd} ${args.join(' ')} — ${err}`));
        child.unref();
    } catch (err) {
        outputChannel.appendLine(`open failed: ${cmd} ${args.join(' ')} — ${err}`);
    }
}

function openInDefaultApp(file: string) {
    if (process.platform === 'win32') {
        spawnOpener('explorer.exe', [file]);
    } else if (process.platform === 'darwin') {
        spawnOpener('open', [file]);
    } else {
        spawnOpener('xdg-open', [file]);
    }
}

function revealInFileManager(file: string) {
    // A file that was never written under the dialog's name (multi-page PNG
    // export splits into BASE-page1.png, BASE-page2.png, …) still has a folder
    // worth showing; selecting a missing path just lands Explorer on Documents.
    const exists = fs.existsSync(file);
    const dir = path.dirname(file);
    if (process.platform === 'win32') {
        // `/select,` and the path are ONE argument — a space between them makes
        // Explorer ignore the path and open the user's Documents folder.
        // ⚠️ AND SO DOES NODE'S ARGUMENT QUOTING, which is the bug this spelling
        // exists to avoid. explorer.exe does not use the C runtime's argv parser: it
        // reads the raw command line. Node quotes any argument containing a space, so
        // a path like `C:\My Scores\a.pdf` was handed over as
        //     explorer.exe "/select,C:\My Scores\a.pdf"
        // with the switch INSIDE the quotes — Explorer does not recognise that as
        // /select at all and falls back to the user's Documents folder, which is
        // exactly the "sometimes" in the report: paths without a space worked, paths
        // with one did not. The form Explorer wants keeps the switch bare and quotes
        // only the path, so the command line has to be written by hand:
        //     explorer.exe /select,"C:\My Scores\a.pdf"
        // MEASURED both ways on 2026-08-28 by reading back what opened, through
        // Shell.Application's window list rather than by eye: the quoted-whole form
        // landed on Documents, this one on the file's own folder, and a Japanese path
        // with a space (`日本語 フォルダ\楽譜 テスト.txt`) also arrives intact — the
        // non-ASCII half of this function's contract still holds, since a verbatim
        // command line is still UTF-16 all the way to CreateProcessW.
        // ⚠️ A Windows filename cannot contain `"`, so wrapping the path in quotes
        // here cannot be broken by the path itself, and nothing goes through a shell.
        // ⚠️ THE FALLBACK BRANCH STAYS ORDINARY: a bare folder path is a normal
        // argument to Explorer and Node's quoting is exactly right for it (measured
        // too — `explorer.exe "C:\with space"` opens that folder).
        if (exists) {
            spawnOpener('explorer.exe', [`/select,"${file}"`], true);
        } else {
            spawnOpener('explorer.exe', [dir]);
        }
    } else if (process.platform === 'darwin') {
        spawnOpener('open', exists ? ['-R', file] : [dir]);
    } else {
        // No portable "select this file" on Linux; the folder is the best we do.
        spawnOpener('xdg-open', [dir]);
    }
}

// Export the currently-previewed score: open the save dialog straight away and let
// its "Save as type" dropdown choose the format; the saved file's extension decides
// what the language server generates. Every format honours the selected score:
// SVG/PNG/PDF draw it; MIDI, MusicXML and the LilyPond source write ITS form (and MIDI
// its parts), as `lysc <format> --score NAME` does.
async function exportPreview(
    context: vscode.ExtensionContext,
    uri: string,
    renderName: string | undefined
) {
    outputChannel.appendLine('exportPreview: opening save dialog (clientReady=' + clientReady + ')');
    if (!client || !clientReady) {
        // Typical cause: the language server was swapped/killed and the client
        // gave up restarting — a window reload brings both back.
        vscode.window.showErrorMessage(
            'Lily#: language server not running — reload the window (Developer: Reload Window).');
        return;
    }

    // Default filename (matches `lysc svg`): the unnamed score writes the
    // source file's basename; every other score appends its name to it —
    // song.lys + `score sub` → song-sub.
    const docUri = vscode.Uri.parse(uri);
    // An unsaved (untitled:) score has no folder on disk, so its parent URI keeps
    // the untitled scheme and showSaveDialog cannot anchor to it — the simple file
    // dialog then falls back to browsing the home directory (".. / agents / …")
    // instead of offering a save target. Anchor an untitled export at a real folder
    // (the workspace root, else the user's home) and take the name from the
    // untitled label; a saved score keeps opening the dialog in its own folder.
    const onDisk = docUri.scheme === 'file';
    const rawName = (onDisk ? docUri.path.split('/').pop() : docUri.path) || 'score';
    const sourceName = rawName.replace(/\.lys$/i, '');
    const baseName = renderName && renderName.length > 0
        ? `${sourceName}-${renderName}`
        : sourceName;
    const baseDir = onDisk
        ? vscode.Uri.joinPath(docUri, '..')
        : (vscode.workspace.workspaceFolders?.[0]?.uri ?? vscode.Uri.file(os.homedir()));

    // Default name WITHOUT an extension: the dialog's "Save as type"
    // dropdown appends the chosen one. Baking .pdf into the name meant the
    // typed name won over the selected type — picking PNG still saved a PDF.
    const target = await vscode.window.showSaveDialog({
        defaultUri: vscode.Uri.joinPath(baseDir, baseName),
        // Ordered by kind: rendered images, then the score as source/interchange,
        // then audio/performance.
        filters: {
            'PDF document': ['pdf'],
            'SVG image': ['svg'],
            'PNG image': ['png'],
            'LilyPond source': ['ly'],
            'MusicXML': ['xml', 'musicxml'],
            'MIDI': ['mid', 'midi'],
            'WAV audio (as the preview plays it)': ['wav'],
            'MP3 audio (as the preview plays it)': ['mp3'],
            'MP4 video (the pages, each note lit as it sounds)': ['mp4'],
            'VOCALOID sequence (vocal + lyrics)': ['vsqx'],
        }
    });
    if (!target) {
        return;
    }

    // The chosen file's extension is the format.
    const ext = (target.fsPath.split('.').pop() || '').toLowerCase();
    const format = ext === 'mid' || ext === 'midi' ? 'midi'
        : ext === 'xml' || ext === 'musicxml' ? 'musicxml'
        : ext; // png / pdf / svg / ly
    if (!['png', 'pdf', 'svg', 'midi', 'musicxml', 'vsqx', 'ly', 'wav', 'mp3', 'mp4'].includes(format)) {
        vscode.window.showErrorMessage(`Lily#: unsupported export type ".${ext}".`);
        return;
    }

    // The preview always shows the @todo marks; a picture asks whether to draw them (owner's
    // decision 2026-10-25, in place of the preview's setting) — only when the file has any.
    let todoMarks = false;
    const text = vscode.workspace.textDocuments.find(d => d.uri.toString() === uri)?.getText() ?? '';
    if (['png', 'pdf', 'svg'].includes(format) && /@todo\b/.test(text)) {
        const without = 'Without the @todo marks', withMarks = 'With the @todo marks';
        const pick = await vscode.window.showQuickPick([
            { label: without, description: 'the finished page' },
            { label: withMarks, description: 'red rings and memos, as the preview shows them' },
        ], { title: 'Lily#: this score has @todo marks', placeHolder: 'Draw them in the export?' });
        if (!pick) {
            return;
        }
        todoMarks = pick.label === withMarks;
    }

    // A WAV or an MP3 is the score as the preview plays it: the server gives the notes
    // (playbackOnly) and the editor renders them through the preview's synth and sound font
    // (audioRender.ts).
    // An MP4 is the same sound with the score's pages, the notes lit as they sound: the server
    // draws the pictures, and the editor joins them to the sound with the user's ffmpeg
    // (videoExport.ts). All three play under the preview's practice menu, the loop aside — its
    // tempo, parts, metronome and count-in (owner's request 2026-11-07; exportNotes).
    const practice = practiceFor(uri);
    const audio: AudioFormat | undefined = format === 'wav' || format === 'mp3' ? format : undefined;
    const video = format === 'mp4';
    let ffmpeg: string | undefined;
    if (video) {
        ffmpeg = await videoFfmpeg();
        if (!ffmpeg) {
            return;
        }
    }
    let soundFont: string | undefined;
    if (audio || video) {
        soundFont = await wavSoundFont(context);
        if (!soundFont) {
            return;
        }
    }

    try {
        const request = async () => {
            const answer = await client!.sendRequest<ExportResponse>('lilysharp/export', {
                textDocument: { uri },
                format,
                outputPath: target.fsPath,
                todoMarks,
                playbackOnly: audio !== undefined || video,
                renderName: renderName || null
            });
            const jobs = answer.Success && answer.Playbacks
                ? await recordJobs(await singJobs(practisedJobs(answer.Playbacks, practice), { textDocument: { uri } }, practice), uri) : null;
            if (jobs && audio && soundFont) {
                await renderAudioFiles(jobs, soundFont, audio);
            } else if (jobs && video && soundFont && ffmpeg) {
                await writeVideos(jobs, soundFont, ffmpeg, drawFramesFor({ textDocument: { uri } }));
            }
            return answer;
        };
        // Rendering the sound takes as long as the synth needs for the whole piece (and a video
        // its pictures and ffmpeg); the pictures and files are quick enough to need no notice.
        const response = soundFont
            ? await vscode.window.withProgress({
                location: vscode.ProgressLocation.Notification,
                title: video
                    ? `Lily#: drawing the video and rendering its sound through ${path.basename(soundFont)}…`
                    : `Lily#: rendering the ${(audio ?? 'wav').toUpperCase()} through ${path.basename(soundFont)}…`,
            }, request)
            : await request();

        if (response.Success) {
            const openAction = 'Open File';
            const revealAction = 'Reveal in Explorer';
            const practised = soundFont ? practiceSummary(practice) : '';
            const through = soundFont ? ` (played through ${path.basename(soundFont)}${practised ? ', ' + practised : ''})` : '';
            const choice = await vscode.window.showInformationMessage(
                `Exported to ${response.OutputPath}${through}`, openAction, revealAction);
            if (choice === openAction) {
                if (fs.existsSync(target.fsPath)) {
                    openInDefaultApp(target.fsPath);
                } else {
                    // Multi-page PNG never writes the single name the dialog
                    // collected, so there is nothing to hand the default app —
                    // show the folder holding the pages instead.
                    revealInFileManager(target.fsPath);
                }
            } else if (choice === revealAction) {
                revealInFileManager(target.fsPath);
            }
        } else {
            vscode.window.showErrorMessage(`Lily# export failed: ${response.Error}`);
        }
    } catch (err) {
        vscode.window.showErrorMessage(`Lily# export failed: ${err}`);
    }
}

/**
 * The text INSIDE the quotes of the document's `title "…"` line, or null if it has none.
 *
 * Read out of the document rather than counted off the template constant: the offset of a
 * hard-coded line and column is a second spelling of the template's shape, and the two
 * would drift the first time a line moved above it. A title with nothing between its quotes
 * returns null — selecting a zero-width range would look exactly like nothing happened.
 */
/** Where the writer's own New Score template is kept (Edit New Score Template). */
function newScoreTemplateUri(context: vscode.ExtensionContext): vscode.Uri {
    return vscode.Uri.joinPath(context.globalStorageUri, 'new-score-template.lys');
}

async function uriExists(uri: vscode.Uri): Promise<boolean> {
    try {
        await vscode.workspace.fs.stat(uri);
        return true;
    } catch {
        return false;
    }
}

/** What New Score starts from: the writer's template when there is one with anything in it,
 *  else the built-in one. */
async function newScoreTemplate(context: vscode.ExtensionContext): Promise<string> {
    try {
        const own = Buffer.from(await vscode.workspace.fs.readFile(newScoreTemplateUri(context))).toString('utf8');
        if (own.trim().length > 0) {
            return own;
        }
    } catch {
        // none yet: the built-in one
    }
    return NEW_SCORE_TEMPLATE;
}

function newScoreTitleRange(doc: vscode.TextDocument): vscode.Range | null {
    const match = /^title "([^"]*)"/m.exec(doc.getText());
    if (!match || match[1].length === 0) {
        return null;
    }
    const at = match.index + match[0].indexOf('"') + 1;
    return new vscode.Range(doc.positionAt(at), doc.positionAt(at + match[1].length));
}

/**
 * Put a score template into the editor, as the WHOLE file.
 *
 * The `template-…` completion items name this command (see the registration above).
 * Accepting one of those items does not edit the document — it re-types the word the
 * writer was completing, unchanged — so everything the writer had is still there when
 * the question below is asked, and answering "no" leaves it that way.
 *
 * `range` is that re-typed word (startLine, startChar, endLine, endChar), and it is
 * excluded from the "is there anything to lose here?" test: a brand-new file where the
 * writer typed `temp` to find the item is empty, and asking permission to clear an empty
 * file is a dialog with no question in it. The server omits the four numbers when it has
 * no caret position (its unit tests call it that way), which just makes the test read
 * the whole document.
 */
async function applyScoreTemplate(label: string, template: string, range: number[]) {
    const editor = vscode.window.activeTextEditor;
    if (!editor || editor.document.languageId !== 'lilysharp') {
        vscode.window.showErrorMessage('Lily#: open a .lys file to apply a score template.');
        return;
    }

    const doc = editor.document;
    const fullRange = new vscode.Range(doc.positionAt(0), doc.positionAt(doc.getText().length));

    let existing = doc.getText();
    if (range.length === 4) {
        const typed = new vscode.Range(range[0], range[1], range[2], range[3]);
        existing = doc.getText(new vscode.Range(fullRange.start, typed.start))
                 + doc.getText(new vscode.Range(typed.end, fullRange.end));
    }

    if (existing.trim().length > 0) {
        const replace = 'Replace';
        const choice = await vscode.window.showWarningMessage(
            `Lily#: replace the whole file with the "${label}" template?`,
            {
                modal: true,
                detail: 'A score template is a complete piece, so it takes the place of what '
                      + 'is here rather than joining it. Everything currently in this file is '
                      + 'discarded (Ctrl+Z brings it back).'
            },
            replace);
        if (choice !== replace) {
            return;
        }
    }

    const edit = new vscode.WorkspaceEdit();
    edit.replace(doc.uri, fullRange, template);
    const ok = await vscode.workspace.applyEdit(edit);
    if (!ok) {
        vscode.window.showErrorMessage('Lily#: the score template could not be applied.');
        return;
    }
    // The caret is left wherever the replaced text put it — park it at the top of the
    // template, which is where a writer starts reading (and editing) a fresh score.
    const start = new vscode.Position(0, 0);
    editor.selection = new vscode.Selection(start, start);
    editor.revealRange(new vscode.Range(start, start));
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

/**
 * Removes every written string number (`\1` … `\9`) from the active .lys document, so the tab
 * chooses every string itself. Applied as one edit, so one undo puts them all back.
 */
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

async function removeStringNumbers() {
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

/**
 * Split Section Here (owner's request 2026-11-17): the section cut at the caret's bar line (or the
 * bar given by the code action), the rest a new section whose name is asked for (a free one
 * offered). The server cuts every part, chord row and words there and checks the piece sounds as
 * it did (lilysharp/cutSection); a refusal says why. One edit (one undo).
 */
async function splitSectionHere(uriArg?: string, section?: string, bars?: number, nameArg?: string,
    offsetArg?: number, snap = false) {
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

/**
 * Guesses the key of the section(s) at the caret or selection from their notes, and
 * offers the ranked keys in a picker; the one picked is written as the key the section
 * starts in (an existing starting key is rewritten), then the notes spelled against it
 * are offered for respelling. Escape leaves the file untouched.
 */
let reviewPoints: vscode.DiagnosticCollection | undefined;

/**
 * The review with no AI (docs/review-tool-design.md §5): its findings as Information in the Problems
 * panel, their own source, until the score changes. The compiler's errors and warnings are there already.
 */
async function reviewScoreCommand(context: vscode.ExtensionContext) {
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

interface ImportMusicXmlResponse {
    Lys: string | null;
    Warnings: string[];
    Error: string | null;
}

/**
 * Imports a MusicXML file (.xml/.musicxml/.mxl) into Lily#. From the command
 * palette it opens a file picker and drops the result in a new untitled .lys; from
 * the Explorer context menu (a uri is passed) it writes `<name>.lys` next to the
 * source. Either way it opens the preview to the side and surfaces the import
 * report (approximations/drops) as a warning — import is a faithful STARTING POINT,
 * not a byte round-trip.
 */
async function importMusicXml(context: vscode.ExtensionContext, sourceUri?: vscode.Uri) {
    if (!client) {
        vscode.window.showErrorMessage('Lily#: language server not ready.');
        return;
    }

    const fromExplorer = sourceUri !== undefined;
    let fileUri = sourceUri;
    if (!fileUri) {
        const picked = await vscode.window.showOpenDialog({
            canSelectMany: false,
            openLabel: 'Import',
            // A Standard MIDI File goes through the same request: the server tells it by its header.
            filters: { 'MusicXML or MIDI': ['xml', 'musicxml', 'mxl', 'mid', 'midi'], 'MusicXML': ['xml', 'musicxml', 'mxl'], 'MIDI': ['mid', 'midi'] },
        });
        if (!picked || picked.length === 0) {
            return;
        }
        fileUri = picked[0];
    }

    // The source file's name (URI paths use '/' on every OS), for the result dialogs.
    const sourceName = fileUri.path.split('/').pop() ?? 'the file';

    // Relative-octave output is opt-in via a setting (default: explicit absolute) — not in the
    // Settings UI since 2026-10-25 (settings.json only; Convert Octaves does it afterwards).
    const relativeOctave = vscode.workspace
        .getConfiguration('lilysharp')
        .get<boolean>('import.relativeOctave', false);

    try {
        const response = await client.sendRequest<ImportMusicXmlResponse>('lilysharp/importMusicXml', {
            filePath: fileUri.fsPath,
            relativeOctave,
        });
        if (response.Error || response.Lys == null) {
            vscode.window.showErrorMessage(`Lily#: import failed: ${response.Error ?? 'no output.'}`);
            return;
        }

        let doc: vscode.TextDocument;
        if (fromExplorer) {
            // Write <name>.lys next to the source and open it.
            const targetUri = fileUri.with({
                path: fileUri.path.replace(/\.(xml|musicxml|mxl|mid|midi)$/i, '') + '.lys',
            });
            await vscode.workspace.fs.writeFile(targetUri, Buffer.from(response.Lys, 'utf8'));
            doc = await vscode.workspace.openTextDocument(targetUri);
        } else {
            // Command palette: a brand-new untitled .lys.
            doc = await vscode.workspace.openTextDocument({
                language: 'lilysharp',
                content: response.Lys,
            });
        }
        await vscode.window.showTextDocument(doc);
        openPreview(context, vscode.ViewColumn.Beside);

        const warnings = response.Warnings ?? [];
        if (warnings.length > 0) {
            const n = warnings.length;
            // A single warning (e.g. "no notes; the score is empty") reads best shown
            // verbatim; several are summarized with the report behind "Show Details".
            // The "starting point, edit as needed" framing is reserved for a clean
            // import — it's misleading when nothing (or little) came through.
            const message = n === 1
                ? `Lily#: ${sourceName}: ${warnings[0]}`
                : `Lily#: imported ${sourceName} with ${n} approximations — see the import report.`;
            vscode.window.showWarningMessage(message, 'Show Details').then(choice => {
                if (choice === 'Show Details') {
                    outputChannel.appendLine(`MusicXML import report (${sourceName}):`);
                    for (const w of warnings) {
                        outputChannel.appendLine(`  - ${w}`);
                    }
                    outputChannel.show(true);
                }
            });
        } else {
            vscode.window.showInformationMessage(
                `Lily#: imported ${sourceName} — a starting point, edit as needed.`);
        }
    } catch (err) {
        vscode.window.showErrorMessage(`Lily#: import failed: ${err}`);
    }
}

async function aiTransformWholeScore(uri: string): Promise<void> {
    const doc = vscode.workspace.textDocuments.find(d => d.uri.toString() === uri);
    if (!doc) {
        vscode.window.showErrorMessage('Lily#: open the .lys file to change it with AI.');
        return;
    }
    await vscode.window.showTextDocument(doc, { preserveFocus: false });
    await vscode.commands.executeCommand('lilysharp.aiTransform', { whole: true });
}

/**
 * M3 bridge: a note range selected on the score preview (start/end are the
 * `data-pos` source offsets of the first and last selected notes) is turned into a
 * text selection in the editor, then the shared `lilysharp.aiTransform` command runs
 * against it — so a score-origin selection drives the exact same transform loop as a
 * text selection (§6).
 */
async function aiTransformFromScore(uri: string, startPos: number, endPos: number, selected?: string): Promise<void> {
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
    const editor = await vscode.window.showTextDocument(doc, { preserveFocus: false });
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
async function askAiInChat(doc: vscode.TextDocument, from?: number, to?: number): Promise<void> {
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
            const editor = await vscode.window.showTextDocument(doc, { preserveFocus: true });
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
function noteSelectionEnd(text: string, pos: number): number {
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

interface RenderInfo {
    Name: string;
    Type: string;
    Filename: string;
}

interface SvgResponse {
    // The whole picture as one string — null when Pages carries it instead (servers
    // from 2026-09-18 on, asked with pageDiff), and on an error.
    Svg: string | null;
    Pages?: SvgPages | null;
    Error: string | null;
    Renders: RenderInfo[] | null;
    // WHICH score was drawn (a RenderInfo.Filename). Normally the one asked for; when the
    // request named no score of this document — a selection left over from an edit that
    // renamed or removed the block — the server draws the default one and says so here, and
    // the picker follows, so the mismatch is visible instead of a picture that looks stuck
    // on the default score. Absent on servers older than 2026-09-06.
    SelectedRender?: string | null;
    // True when the server's latest-wins machinery skipped this render because a
    // newer request for the same (document, render name) had already arrived —
    // no Svg, no Error; the newer response carries the picture. Absent on servers
    // older than 2026-08-27.
    Superseded?: boolean;
    // The semitones the drawn score moves the music by (the file's top-level transpose and the
    // score's own): a recording beside the chart is played moved by them.
    ChartTranspose?: number;
    // Where the server spent this request's time (absent on servers older than
    // 2026-09-04). Same machine, so the client's clock and ServerSentAt compare.
    Timing?: {
        ReceivedAfterMs: number | null;   // client send -> dispatch-thread receipt
        QueuedMs: number;                 // receipt -> thread-pool pickup
        ExpandMs: number;                 // `using` expansion + render-block scan
        LockWaitMs: number;               // waiting for the selection's render gate
        RenderMs: number;                 // the incremental render
        TotalServerMs: number;            // receipt -> response handed to RPC
        ServerSentAt: number;             // server clock, Unix ms
        DiagnosticsLastMs: number;        // the last diagnostics pass (off-thread)
        LastDidChangeMs?: number;         // the last didChange handler, on the dispatch thread
        ThreadPoolThreads?: number;
        PendingWorkItems?: number;
    };
}

// The extension host's event-loop lag while a request is in flight: the largest gap,
// in ms, between a 10 ms timer's due time and when it actually ran. A large value is
// this process (any extension, or VS Code's own work here) being busy — the reason a
// response can sit unread and a request unsent, which the server's clock cannot see.
class EventLoopLagProbe {
    private timer: NodeJS.Timeout | undefined;
    private last = Date.now();
    maxLag = 0;
    start(): void {
        this.last = Date.now();
        this.timer = setInterval(() => {
            const now = Date.now();
            this.maxLag = Math.max(this.maxLag, now - this.last - 10);
            this.last = now;
        }, 10);
    }
    stop(): number {
        if (this.timer) { clearInterval(this.timer); this.timer = undefined; }
        return Math.max(0, this.maxLag);
    }
}

// Webview scripts must be nonce-allowed: newer VS Code builds reject
// script-src 'unsafe-inline' outright — the preview script then never runs
// and the panel sits on "Loading preview…" while the extension happily posts
// SVG into the void.
// https://code.visualstudio.com/api/extension-guides/webview#content-security-policy
function getNonce(): string {
    let text = '';
    const possible = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
    for (let i = 0; i < 32; i++) {
        text += possible.charAt(Math.floor(Math.random() * possible.length));
    }
    return text;
}

/** Where the preview's SoundFont synth lives: the bundle and the worklet processor
 *  esbuild.js writes to out/synth (docs/playback-design.md). */
function synthRoot(extensionUri: vscode.Uri): vscode.Uri {
    return vscode.Uri.joinPath(extensionUri, 'out', 'synth');
}

/** The sound font the preview plays with: `lilysharp.playback.soundFont` when it names a
 *  file on this machine (a missing one is logged and the default used); else the Lily# Sound
 *  Font extension's, which Lily# brings with it (packagedSoundFont); else the General MIDI bank
 *  the OS ships (Windows' gm.dls, macOS's DLS bank). Null when there is none — Linux with
 *  neither: the preview then plays its oscillator patches.
 *  Owner's decisions: no font in THIS package (2026-10-12), and a good one from the start in a
 *  package of its own (2026-11-11). */
function soundFontFile(extensionUri: vscode.Uri): { file: vscode.Uri; root: vscode.Uri } | null {
    const setting = vscode.workspace.getConfiguration('lilysharp').get<string>('playback.soundFont', '').trim();
    if (setting) {
        if (fs.existsSync(setting)) {
            return { file: vscode.Uri.file(setting), root: vscode.Uri.file(path.dirname(setting)) };
        }
        outputChannel.appendLine(`lilysharp.playback.soundFont: ${setting} does not exist; using the default sound`);
    }
    const fallback = defaultSoundFont(extensionUri);
    return fallback ? { file: vscode.Uri.file(fallback.path), root: vscode.Uri.file(path.dirname(fallback.path)) } : null;
}

/** What plays when the setting is empty, named as the picker shows it: the Lily# Sound Font
 *  extension's font when that is installed (packagedSoundFont), else the OS's bank.
 *  ONE answer for the player and the picker: the picker said "gm.dls" while a local build
 *  played its MuseScore_General, which then had no name in the list (owner's report
 *  2026-10-12).
 *  A developer's build used to read a font under its own media/soundfont first. That is gone
 *  (owner's decision 2026-11-11): with the Sound Font extension there, it served nothing a
 *  setting does not, and it kept a developer's machine off the road every user's takes. */
function defaultSoundFont(_extensionUri: vscode.Uri): { label: string; path: string } | null {
    return packagedSoundFont() ?? osSoundFonts()[0] ?? null;
}

/** The Lily# Sound Font extension (editors/vscode-soundfont): MuseScore_General.sf3 as its authors
 *  published it (MIT), in a package of its own. Owner's decision 2026-11-11, revising the one of
 *  2026-10-12 (no font in the VSIX; the OS's bank by default): a thin sound makes the whole
 *  product look thin, so a good one is to be there from the start — but not in THIS package,
 *  which is downloaded whole at every update: a font does not change, and in its own package it
 *  is downloaded once. */
const SOUND_FONT_EXTENSION = 'yotsuda.lilysharp-soundfont';

/** The font that extension carries, when it is installed: what plays by default then, ahead of
 *  the OS's bank. */
function packagedSoundFont(): { label: string; path: string } | null {
    const packaged = vscode.extensions.getExtension(SOUND_FONT_EXTENSION);
    if (!packaged) { return null; }
    const dir = path.join(packaged.extensionPath, 'soundfont');
    try {
        const files = fs.readdirSync(dir).filter(f => /\.(sf2|sf3|dls)$/i.test(f)).sort();
        if (files.length > 0) {
            return { label: `${files[0]} (the Lily# Sound Font extension)`, path: path.join(dir, files[0]) };
        }
    } catch { /* installed without its font: the OS's bank */ }
    return null;
}

/** The General MIDI banks an OS ships for its own synth, where present: Windows' Microsoft GS
 *  Wavetable bank and macOS's DLSMusicDevice bank (both Roland GS, DLS; small and dated, but
 *  there with no download). Offered by the picker; never copied or redistributed. */
function osSoundFonts(): { label: string; path: string }[] {
    const candidates = process.platform === 'win32'
        ? [{ label: 'Windows: gm.dls (the Microsoft GS Wavetable bank, 3 MB)',
             path: path.join(process.env.SystemRoot ?? 'C:\\Windows', 'System32', 'drivers', 'gm.dls') }]
        : process.platform === 'darwin'
            ? [{ label: 'macOS: gs_instruments.dls (the DLSMusicDevice bank)',
                 path: '/System/Library/Components/CoreAudio.component/Contents/Resources/gs_instruments.dls' }]
            : [];
    return candidates.filter(c => fs.existsSync(c.path));
}

/** Where the sound fonts used before are kept: this computer's, so globalState rather than the
 *  settings (a path must not travel with Settings Sync). soundFontHistoryCore.ts says why. */
const SOUND_FONT_HISTORY_KEY = 'lilysharp.soundFontHistory';
/** Windows' and (by default) macOS's file names ignore case. */
const PATHS_IGNORE_CASE = process.platform === 'win32' || process.platform === 'darwin';

function soundFontHistory(context: vscode.ExtensionContext): string[] {
    return cleanHistory(context.globalState.get(SOUND_FONT_HISTORY_KEY), PATHS_IGNORE_CASE);
}

/** Remembers a font that is there now as the one used last (the setting written by the
 *  picker or by hand, through onDidChangeConfiguration). */
async function rememberSoundFont(context: vscode.ExtensionContext, file: string): Promise<void> {
    if (!file || !fs.existsSync(file)) { return; }
    await context.globalState.update(SOUND_FONT_HISTORY_KEY, recordFont(soundFontHistory(context), file, PATHS_IGNORE_CASE));
}

/** The jobs of a WAV, MP3 or MP4 export with their notes as Play sounds them under `practice`
 *  (exportNotes; as written when no preview holds a menu for the file). */
function practisedJobs(jobs: readonly AudioJob[], practice: PracticeState | undefined): AudioJob[] {
    return jobs.map(job => {
        // With the recording the page sounds with the score: the notes on its seconds, and how it
        // plays (docs/recording-design.md §7); recordJobs renders it once the voices are in.
        const plan = recordedExportPlan(job.Notes, job.Parts ?? null, job.Beats ?? null, job.BarEnd, practice, job.Extra ?? null);
        if (practice?.recording && !plan.recording) {
            outputChannel.appendLine(`recording: export: its bars do not line up with ${path.basename(job.OutputPath)}'s; written without it`
                + ` (the score ends at ${job.BarEnd ?? '?'} s, ${job.Beats?.length ?? 0} beats; the recording places ${practice.recording.bars.length} bars)`);
        }
        return { ...job, Notes: plan.notes, Lead: plan.lead, RecordingPlay: plan.recording };
    });
}

/** The renders of the recording a page was asked for (renderRecording), by request id: the
 *  pieces as they arrive, and what to call once the last has. */
const recordingRenders = new Map<number, { left: Float32Array[]; right: Float32Array[]; done: (r: { left: Float32Array; right: Float32Array; sampleRate: number } | { error: string }) => void }>();
let recordingRenderId = 0;

/** A piece of a recording render from a page (recordingRendered). */
function recordingRendered(message: { id: number; error?: string; index?: number; pieces?: number; length?: number; sampleRate?: number; left?: unknown; right?: unknown }): void {
    const render = recordingRenders.get(message.id);
    if (!render) { return; }
    if (message.error) {
        recordingRenders.delete(message.id);
        render.done({ error: message.error });
        return;
    }
    render.left[message.index ?? 0] = floats(message.left);
    render.right[message.index ?? 0] = floats(message.right);
    const pieces = message.pieces ?? 1;
    if (render.left.filter(p => p).length < pieces) { return; }
    recordingRenders.delete(message.id);
    const join = (parts: Float32Array[]) => {
        const all = new Float32Array(message.length ?? parts.reduce((n, p) => n + p.length, 0));
        let at = 0;
        for (const p of parts) { all.set(p.subarray(0, Math.max(0, all.length - at)), at); at += p.length; }
        return all;
    };
    render.done({ left: join(render.left), right: join(render.right), sampleRate: message.sampleRate ?? WAV_SAMPLE_RATE });

    // A typed array as the webview sent it: kept as one, or (an older host) an object of indices.
    function floats(v: unknown): Float32Array {
        if (v instanceof Float32Array) { return v; }
        if (ArrayBuffer.isView(v)) { return new Float32Array(v.buffer.slice(v.byteOffset, v.byteOffset + v.byteLength)); }
        if (v && typeof v === 'object') { return Float32Array.from(Object.values(v as Record<string, number>)); }
        return new Float32Array(0);
    }
}

/**
 * The recording beside the chart, mixed into each job that plays with it (RecordingPlay): the
 * preview showing `uri` renders it whole with its own player, as it sounds with the score
 * (recordingPlayer.render), from the count-in's lead on (docs/recording-design.md §7). A page that
 * cannot (closed meanwhile, the recording gone): the file without it, said in the output channel.
 */
async function recordJobs(jobs: readonly AudioJob[], uri: string | undefined): Promise<AudioJob[]> {
    const panel = uri ? previewPanels.get(uri) : undefined;
    const out: AudioJob[] = [];
    for (const job of jobs) {
        const play = job.RecordingPlay;
        if (!play) { out.push(job); continue; }
        if (!panel) {
            outputChannel.appendLine(`recording: export: no preview shows ${uri ?? 'the file'}; ${path.basename(job.OutputPath)} is written without the recording`);
            out.push(job);
            continue;
        }
        const id = ++recordingRenderId;
        const rendered = await new Promise<{ left: Float32Array; right: Float32Array; sampleRate: number } | { error: string }>(resolve => {
            recordingRenders.set(id, { left: [], right: [], done: resolve });
            void panel.webview.postMessage({ type: 'renderRecording', id, tempo: play.tempo });
            // A page that never answers (closed while rendering): give up after a long while.
            setTimeout(() => { if (recordingRenders.delete(id)) { resolve({ error: 'the preview did not answer' }); } }, 10 * 60 * 1000);
        });
        if ('error' in rendered) {
            outputChannel.appendLine(`recording: export: ${path.basename(job.OutputPath)} is written without the recording: ${rendered.error}`);
            out.push(job);
            continue;
        }
        outputChannel.appendLine(`recording: export: ${path.basename(job.OutputPath)} with the recording (${(rendered.left.length / rendered.sampleRate).toFixed(1)} s, tempo ×${play.tempo.toFixed(3)})`);
        out.push({ ...job, Recording: {
            left: rendered.left, right: rendered.right, sampleRate: rendered.sampleRate,
            at: job.Lead ?? 0, gain: play.level / PREVIEW_MASTER,
        } });
    }
    return out;
}

/**
 * The sung voices of each export (docs/singing-design.md §5): for every part the job's score
 * sings — not muted, its ♪ on — the engine's WAV at the practice speed, to be mixed in from the
 * count-in's lead; where the score does not also play the part, its notes leave the
 * instruments' list (a video still lights them: `Lit`). No engine, or a voice that fails: the
 * instruments as before, the failure in the output channel.
 */
async function singJobs(jobs: readonly AudioJob[], source: { textDocument?: { uri: string }; path?: string },
    practice: PracticeState | undefined): Promise<AudioJob[]> {
    const engine = await renderer();
    if (!engine || !client || !clientReady) { return [...jobs]; }
    const speed = practice?.speed ?? 1;
    // The part's button is the instrument's (exportPlan drops a muted part's notes); the voice is
    // its own toggle's (session 1164) - so a muted part with its voice on is sung alone.
    const silentVoices = new Set(practice?.voicesOff ?? []);
    const out: AudioJob[] = [];
    for (const job of jobs) {
        let sung: { Voices?: SungVoiceDto[] | null; PrintedWords?: string[] | null; Error?: string | null };
        try {
            // The open document, or a file on the disk the folder export names (as lilysharp/export).
            sung = await client.sendRequest('lilysharp/sung', { ...source, renderName: job.Score || null, allLyrics: true });
        } catch (e) {
            outputChannel.appendLine(`singer: export: lilysharp/sung failed: ${String(e)}`);
            out.push(job);
            continue;
        }
        const sungParts = new Set(job.Sung ?? []), playedParts = new Set(job.Played ?? []);
        const voicesOn = new Set(practice?.voicesOn ?? []);
        const voices: VoiceTrack[] = [];
        const silenced = new Set<number>();
        const allOffTracks = new Set<number>();     // silenced because the menu switched every voice off: their unsung notes too
        // A line nobody installed can sing is not asked of the engine: it is said, and its part's
        // instrument stays in the mix (owner's decision 2026-11-13).
        const unsingable = await engine.unsingable((sung.Voices ?? []).filter(v => v.Notes.length > 0));
        const notSung = new Set<string>();      // the voices asked to sing that did not: nobody can, or the engine failed
        for (const voice of sung.Voices ?? []) {
            // The menu's fader for the voice (owner's request 2026-11-09): its gain in the mix; at 0 it is as switched off.
            const key = sungKey(voice);
            const gain = levelOf(practice?.voiceLevels, key);
            // The score's own voice unless the menu switched it off; one the score does not sing
            // only when the menu turned it on (voicesOn, session 1163).
            const sings = voice.InScore !== false ? sungParts.has(voice.Part) && !silentVoices.has(key) : voicesOn.has(key) && !silentVoices.has(key);
            // A sung-only part with all its voices switched off and its instrument not turned on: silent, as in Play.
            const allOff = (sung.Voices ?? []).filter(v => v.Part === voice.Part).every(v => silentVoices.has(sungKey(v)));
            if (allOff && sungParts.has(voice.Part) && !playedParts.has(voice.Part) && !(practice?.instrumentsOn ?? []).includes(voice.Part)) {
                const off = job.Parts?.indexOf(voice.Part) ?? -1;
                if (off >= 0) { silenced.add(off); allOffTracks.add(off); }
            }
            if (voice.Notes.length === 0 || !sings || gain <= 0) { continue; }
            const nobody = unsingable.get(key);
            if (nobody !== undefined) { tellUnsingable(key, nobody); notSung.add(key); continue; }
            try {
                const { rendered } = await engine.render(voice, speed);
                const v = await rendered;
                // The WAV begins VOICE_LEAD_S before the score's 0 (the first consonant's room).
                voices.push({ wav: v.wav, at: (job.Lead ?? 0) - VOICE_LEAD_S, duration: v.duration, gain });
                // A part the score only sings keeps its instrument out of the mix unless the menu
                // turned it on (instrumentsOn; session 1165) - a part it plays, or the menu's own
                // voice of a played part, sounds both ways.
                const track = job.Parts?.indexOf(voice.Part) ?? -1;
                const sungOnly = sungParts.has(voice.Part) && !playedParts.has(voice.Part);
                if (track >= 0 && sungOnly && !(practice?.instrumentsOn ?? []).includes(voice.Part)) { silenced.add(track); }
            } catch (e) {
                const refused = unsingableReason(e);
                if (refused) { tellUnsingable(key, refused); }
                else { outputChannel.appendLine(`singer: export: ${singerFailureText(key, engineSaid(e), voice.WarningPlace)}`); }
                notSung.add(key);
            }
        }
        // A consonant before the beat (in the lead) that would fall before the file's start: the
        // whole file — notes, lit notes, the count-in's lead, the voices — moves later by what the
        // earliest such sound needs, so it is heard (docs/singing-design.md §3). Silence in the
        // lead is not waited for.
        let pre = 0;
        for (const v of voices) {
            try {
                const d = decodeWav(fs.readFileSync(v.wav));
                const s = soundStart(d.samples, d.sampleRate, VOICE_LEAD_S);
                if (s !== null) { pre = Math.max(pre, -(v.at + s)); }
            } catch { /* a WAV the render will refuse too */ }
        }
        pre = Math.max(0, pre);
        const later = <T extends { T: number }>(list: T[]): T[] => pre > 0 ? list.map(n => ({ ...n, T: n.T + pre })) : list;
        // A note in a section none of the part's voices sings (U) is the instrument's: it stays.
        // So is one in a section whose every singer was not sung here, while another line of its part was.
        const written = leftToTheInstrument(job.Notes, notSung);
        const notes = silenced.size > 0 ? written.filter(n => !(typeof n.N === 'number' && silenced.has(n.N) && !(n.U && !allOffTracks.has(n.N)))) : job.Notes;
        out.push({
            ...job, Voices: voices.map(v => ({ ...v, at: v.at + pre })), Lit: later(job.Notes),
            Notes: later(notes), Lead: (job.Lead ?? 0) + pre,
        });
    }
    return out;
}

/** Asks the server for a video's pictures (lilysharp/videoFrames), the file named as the export
 *  named it — the open document, or a path on the disk. */
function drawFramesFor(source: { textDocument?: { uri: string }; path?: string }): DrawFrames {
    return (job, seconds) => {
        if (!client) { return Promise.resolve({ Error: 'language server not running' }); }
        return client.sendRequest<FramesAnswer>('lilysharp/videoFrames', {
            ...source, score: job.Score === undefined ? null : job.Score, notes: job.Lit ?? job.Notes, seconds });
    };
}

/** The ffmpeg an MP4 export runs (lilysharp.export.ffmpegPath, else `ffmpeg` on the PATH), once
 *  it is known to run; else the writer is told how to get it, and undefined. Asked before the
 *  server draws anything. */
async function videoFfmpeg(): Promise<string | undefined> {
    const program = ffmpegProgram(vscode.workspace.getConfiguration('lilysharp').get<string>('export.ffmpegPath', ''));
    if (await ffmpegRuns(program)) {
        return program;
    }
    const open = 'Open Setting';
    if (await vscode.window.showErrorMessage(ffmpegMissing(program), open) === open) {
        await vscode.commands.executeCommand('workbench.action.openSettings', 'lilysharp.export.ffmpegPath');
    }
    return undefined;
}

/** The sound font a WAV, MP3 or MP4 export plays the score through: the one the preview plays
 *  (soundFontFile — the setting, else the Sound Font extension's, else the OS's bank), so the file sounds
 *  as Play does (owner's request 2026-11-07; until then the server's synth rendered it and read
 *  `.sf2` only, so the default gm.dls and a `.sf3` were refused). Only when the preview has none
 *  (Linux with no font set: it plays its oscillator patches, which no file can) does the writer
 *  pick one — a font used before (the sound font history) or a file — and it joins the history,
 *  so the next WAV export offers it first. The preview's setting is left alone. Undefined when
 *  they cancel. */
async function wavSoundFont(context: vscode.ExtensionContext): Promise<string | undefined> {
    const plays = soundFontFile(context.extensionUri);
    if (plays) {
        return plays.file.fsPath;
    }
    const readable = (f: string) => /\.(sf2|sf3|dls)$/i.test(f) && fs.existsSync(f);
    const why = 'A WAV or MP3 export plays the score through the preview\'s sound font, and the preview has none';
    const used = soundFontHistory(context).filter(readable);
    let chosen: string | undefined;
    if (used.length > 0) {
        type Pick = vscode.QuickPickItem & { file?: string };
        const picked = await vscode.window.showQuickPick<Pick>([
            ...used.map(f => ({ label: path.basename(f), detail: f, file: f })),
            { label: 'Browse for a sound font…', detail: 'See "Sound fonts" on the Lily# site for three free ones' },
        ], { title: 'Lily#: the sound font to render the sound with', placeHolder: why });
        if (!picked) {
            return undefined;
        }
        chosen = picked.file;
    }
    if (!chosen) {
        if (used.length === 0) {
            const browse = 'Choose a Sound Font…';
            if (await vscode.window.showInformationMessage(`Lily#: ${why}.`, { modal: true,
                detail: 'The editor page of the Lily# site names three free sound fonts and how to install them.' },
                browse) !== browse) {
                return undefined;
            }
        }
        const files = await vscode.window.showOpenDialog({
            title: 'Choose a sound font for the export',
            canSelectMany: false,
            filters: { 'Sound font (.sf2, .sf3, .dls)': ['sf2', 'sf3', 'dls'] },
        });
        if (!files || files.length === 0) {
            return undefined;
        }
        chosen = files[0].fsPath;
    }
    await rememberSoundFont(context, chosen);
    return chosen;
}

/** The picker behind `Lily#: Choose Sound Font…` and the setting's link: the default, named
 *  for what plays (defaultSoundFont — the OS's bank, or a local build's own font, with the OS's
 *  bank then listed by its name), the fonts used before whose files are there now
 *  (each with a button that takes it off the list), or a file of the user's (a dialog filtered
 *  to .sf2 / .sf3 / .dls), written to the user settings as `lilysharp.playback.soundFont`.
 *  The files are looked for when the picker opens, not at start-up: only the picker needs the
 *  answer, and a drive plugged in since then counts (owner's request 2026-10-12). */
async function chooseSoundFont(context: vscode.ExtensionContext): Promise<void> {
    const config = vscode.workspace.getConfiguration('lilysharp');
    const current = config.get<string>('playback.soundFont', '').trim();
    await rememberSoundFont(context, current);   // a font set before the list existed
    type Pick = vscode.QuickPickItem & { action: 'default' | 'browse' | 'path'; path?: string };
    const def = defaultSoundFont(context.extensionUri);
    const os = osSoundFonts()[0];
    const playsDefault = !current || !fs.existsSync(current) || (!!def && samePath(current, def.path, PATHS_IGNORE_CASE));
    const isCurrent = (f: string) => !playsDefault && samePath(f, current, PATHS_IGNORE_CASE);
    // The OS's bank by its own name when the default is something else (a local build's font).
    const osApart = os && def && !samePath(os.path, def.path, PATHS_IGNORE_CASE) ? os : undefined;
    const forget: vscode.QuickInputButton = {
        iconPath: new vscode.ThemeIcon('trash'), tooltip: 'Remove from this list (the file stays where it is)' };
    const build = (): Pick[] => [
        { label: def ? `Default — ${def.label}` : 'Default — simple tones', description: playsDefault ? '(current)' : '',
          detail: !def ? 'This system has no General MIDI bank of its own: browse for a sound font for real instruments'
                : def === os ? 'The General MIDI bank this computer already has (empties the setting)'
                : 'What plays when no font is chosen (empties the setting)',
          action: 'default' },
        ...(osApart ? [{ label: osApart.label, description: isCurrent(osApart.path) ? '(current)' : '',
                         detail: osApart.path, action: 'path' as const, path: osApart.path }] : []),
        ...shownFonts(soundFontHistory(context), f => fs.existsSync(f), PATHS_IGNORE_CASE,
                      [def?.path, os?.path].filter((p): p is string => !!p))
            .map<Pick>(f => ({
                label: path.basename(f), description: isCurrent(f) ? '(current)' : '',
                detail: f, action: 'path', path: f, buttons: [forget] })),
        { label: 'Browse for a file…', detail: 'An .sf2, .sf3 or .dls file on this machine — see "Sound fonts" on the Lily# site for three free ones', action: 'browse' },
    ];
    const qp = vscode.window.createQuickPick<Pick>();
    qp.title = 'Lily#: the sound font the preview plays with';
    qp.placeholder = 'Each part plays the General MIDI instrument its midiInstrument or preset names';
    qp.items = build();
    const picked = await new Promise<Pick | undefined>(resolve => {
        qp.onDidAccept(() => { resolve(qp.selectedItems[0]); qp.hide(); });
        qp.onDidHide(() => resolve(undefined));
        qp.onDidTriggerItemButton(async e => {
            if (!e.item.path) { return; }
            await context.globalState.update(SOUND_FONT_HISTORY_KEY,
                forgetFont(soundFontHistory(context), e.item.path, PATHS_IGNORE_CASE));
            qp.items = build();
        });
        qp.show();
    });
    qp.dispose();
    if (!picked) { return; }
    let value: string | undefined;
    if (picked.action === 'default') {
        value = '';
    } else if (picked.action === 'path') {
        value = picked.path;
    } else {
        const chosen = await vscode.window.showOpenDialog({
            title: 'Choose a sound font',
            canSelectMany: false,
            filters: { 'Sound fonts': ['sf2', 'sf3', 'dls'], 'All files': ['*'] },
            defaultUri: current ? vscode.Uri.file(path.dirname(current)) : undefined,
        });
        if (!chosen || chosen.length === 0) { return; }
        value = chosen[0].fsPath;
    }
    await config.update('playback.soundFont', value, vscode.ConfigurationTarget.Global);
    vscode.window.setStatusBarMessage(
        value ? `Lily#: the preview plays ${path.basename(value)}` : 'Lily#: the preview plays the default sound', 5000);
}

// ---- MIDI out (docs/playback-design.md §6) ----

/** `lilysharp.playback.midiOutput`: the name of the MIDI output port the sounds go to, or ''
 *  for the preview's own synth. */
function midiOutputSetting(): string {
    return vscode.workspace.getConfiguration('lilysharp').get<string>('playback.midiOutput', '').trim();
}

/** The lilysharp/midiOut requests go one after another: the server dispatches requests
 *  concurrently, and a Stop must not overtake the Play it follows. No server: an error
 *  answer, as the server would give. */
let midiOutChain: Promise<unknown> = Promise.resolve();
function midiOutRequest<T extends { Error?: string | null }>(method: string, params: unknown): Promise<T> {
    const next = midiOutChain.then(async (): Promise<T> => {
        if (!client || !clientReady) { return { Error: 'language server not running' } as T; }
        try {
            return await client.sendRequest<T>(method, params);
        } catch (e) {
            return { Error: String(e) } as T;
        }
    });
    midiOutChain = next.catch(() => undefined);
    return next;
}

/** A port that is not there, or refused, is said once per port in the status bar and
 *  every time in the output channel; the preview then plays its own synth. */
const midiOutErrorsTold = new Set<string>();
function reportMidiOutError(port: string, error: string): void {
    outputChannel.appendLine(`MIDI output "${port}": ${error}`);
    if (midiOutErrorsTold.has(port)) { return; }
    midiOutErrorsTold.add(port);
    vscode.window.setStatusBarMessage(`Lily#: ${error} — playing in the preview (see Lily# Extension output)`, 8000);
}

// ---- The singing voice (docs/singing-design.md §5) ----

/** A file's `singer` came to another voice than it asked for: said once a session. */
const singerNotesTold = new Set<string>();
function tellSingerNote(text: string): void {
    if (singerNotesTold.has(text)) { return; }
    singerNotesTold.add(text);
    outputChannel.appendLine(`singer: ${text}`);
    vscode.window.setStatusBarMessage(`Lily#: ${text}`, 8000);
}

/**
 * At a `singer`'s name, the names right for the words' language (owner's request
 * 2026-11-09, session 1168): the server says whether the caret is in one and what the language
 * is (lilysharp/singerSlot), the Cantoria extension what is installed (singerChoices).
 * The quotes' inside is replaced whole, so a name with a slash completes as one. Before any quote
 * (`singer |`, owner's report 2026-11-09, session 1196) a name goes in with its quotes, the caret
 * between them as if it had been typed there, and `male` and `female` are offered too.
 */
const singerCompletion: vscode.CompletionItemProvider = {
    async provideCompletionItems(document, position, _token, context) {
        if (!client || !clientReady) { return undefined; }
        if ((context.triggerCharacter === ' ' || context.triggerCharacter === ',') && !afterSingerWord(document.lineAt(position.line).text.slice(0, position.character))) { return undefined; }
        const slot = await client.sendRequest<{ InSlot: boolean; Language?: string | null; AnyLanguage?: boolean; Gender?: string | null; Quoted?: boolean }>('lilysharp/singerSlot',
            { textDocument: { uri: document.uri.toString() }, offset: document.offsetAt(position) });
        if (!slot.InSlot || !slot.Language) { return undefined; }
        const engine = await renderer();
        const api = engine?.backend instanceof CantoriaExtensionBackend ? engine.backend.api : undefined;
        const line = document.lineAt(position.line).text;
        const open = line.lastIndexOf('"', position.character - 1);
        const close = line.indexOf('"', position.character);
        // At the top level, a voice taken goes on to `on` and the list of the file's parts
        // (owner's request 2026-11-11, and the part before the words since 2026-11-12): the name
        // is closed, ` on ` written after it and the list asked again — the server's, which
        // names the parts there, and after the part what the line may say next. The closing quote is
        // then part of what the item replaces.
        const goesOn = singerGoesOnToSings(document.getText(new vscode.Range(0, 0, position.line, 0)), line,
            line.slice(slot.Quoted && close >= 0 ? close + 1 : position.character));
        const range = slot.Quoted
            ? new vscode.Range(position.line, open + 1, position.line, close >= 0 ? close + (goesOn ? 1 : 0) : position.character)
            : new vscode.Range(position.line, position.character - (/[^\s,"]*$/.exec(line.slice(0, position.character))?.[0].length ?? 0), position.line, position.character);
        // Where the name stands once written: what hearing it asks the words' language by.
        const nameColumn = slot.Quoted ? open + 1 : range.start.character + 1;
        if (!api?.voices) {
            offerCantoria();
            return vscode.extensions.getExtension(CANTORIA_EXTENSION_ID) || singerSetting() ? undefined : [noVoiceItem(document, range, noVoiceGuide(false))];
        }
        const voices = await api.voices();
        // Words not named yet and written in several languages: every voice (docs/lyrics-singer-design.md §7).
        const language = slot.AnyLanguage ? undefined : slot.Language!;
        samples().warm(voices.filter(v => !language || singsLanguage(v, language)).map(v => voiceSampleKey(v.id, sampleOctave(v.gender, slot.Gender), slot.Language!)));
        const guide = voices.length === 0 ? noVoiceGuide(true) : !language || voices.some(v => singsLanguage(v, language)) ? undefined : noLanguageGuide(language, voices);
        if (guide) { offerVoiceSetup(guide); }
        const items = singerChoices(voices, language, slot.Gender).map((c, i) => {
            const item = new vscode.CompletionItem(c.label, vscode.CompletionItemKind.Value);
            item.detail = c.detail;
            item.range = range;
            item.filterText = c.label;
            item.sortText = String(i).padStart(3, '0');
            if (goesOn) {
                item.insertText = new vscode.SnippetString().appendText(`${slot.Quoted ? '' : '"'}${c.label}" on `).appendTabstop(0);
                item.command = { command: 'lilysharp.singerTaken', title: 'Hear this voice, and list the parts', arguments: [c.label, nameColumn] };
            } else {
                if (!slot.Quoted) { item.insertText = new vscode.SnippetString(`"${c.label}$0"`); }
                item.command = { command: 'lilysharp.hearSinger', title: 'Hear this voice', arguments: [c.label] };
            }
            return item;
        });
        if (!slot.Quoted && !slot.Gender) {
            for (const gender of ['female', 'male']) {
                const item = new vscode.CompletionItem(gender, vscode.CompletionItemKind.Keyword);
                item.detail = `any ${gender} voice, or name one`;
                item.range = range;
                item.sortText = ` ${gender}`;
                item.insertText = new vscode.SnippetString(`${gender} "$0"`);
                item.command = { command: 'editor.action.triggerSuggest', title: 'List the voices' };
                items.push(item);
            }
        }
        if (guide) { items.unshift(noVoiceItem(document, range, guide)); }
        return items;
    },
};

/**
 * The list on a singer line's settings (owner's decisions 2026-11-12: `singer … with breathiness
 * 0.3 gender -0.2`): the word `with` where the line has none yet, and after it the settings the
 * line's own voice takes, as the voice's engine names them — each with what it does and its range
 * (Cantoria's `parameters`). Nothing is listed that the voice does not take, and what the line
 * has already set is left out. A value is the writer's to type: its range is in the name's line.
 */
const singerSettingCompletion: vscode.CompletionItemProvider = {
    async provideCompletionItems(document, position) {
        const before = document.lineAt(position.line).text.slice(0, position.character);
        const slot = singerSettingSlot(before);
        if (!slot) { return undefined; }
        const engine = await renderer();
        const backend = engine?.backend instanceof CantoriaExtensionBackend ? engine.backend : undefined;
        if (!backend) { return undefined; }
        // The line's voice: the name it writes, else the gender it says, else the default voice.
        // ⚠️ As the score's own voice is found (VoiceRenderer): by the words' language and the part's
        // gender too, or a line that names no voice lists another voice's settings than the one
        // that sings it.
        const named = new RegExp(String.raw`^[ \t]*singer[ \t]+${SINGER_NAME}(?:(male|female)[ \t]+)?${SINGER_LANGUAGE}(?:"([^"]*)")?`).exec(before);
        const lineText = document.lineAt(position.line).text;
        const asked = new vscode.Position(position.line, singerLanguageColumn(lineText, singerLineOf(lineText)?.askColumn ?? position.character));
        const where = client && clientReady ? await client.sendRequest<{ Language?: string | null; Gender?: string | null }>('lilysharp/singerSlot',
            { textDocument: { uri: document.uri.toString() }, offset: document.offsetAt(asked) }).then(s => s, () => undefined) : undefined;
        let taken: SingParameter[];
        let voice: string;
        try {
            voice = await backend.voiceName(named?.[2] || named?.[1] || null, where?.Language ?? undefined, where?.Gender);
            taken = await backend.parameters(voice);
        } catch (e) {
            outputChannel.appendLine(`singer: the voice's settings could not be listed — ${String(e)}`);
            return undefined;
        }
        // `neutrino/MERROW` → neutrino: the engine the settings are of.
        const engineName = voice.includes('/') ? voice.slice(0, voice.indexOf('/')) : voice;
        if (slot.kind === 'value') {
            // A setting that takes a NAME lists its names; a number, values across its range
            // to begin from (owner's request 2026-11-12, after `with transpose `).
            const of = taken.find(p => p.name === slot.of);
            if (!of) { return undefined; }
            if (of.word) {
                return (of.values ?? []).map((name, i) => {
                    const item = new vscode.CompletionItem(name, vscode.CompletionItemKind.Value);
                    item.sortText = String(i).padStart(3, '0');
                    return item;
                });
            }
            return singValueItems(of, document, position);
        }
        if (slot.kind === 'with') {
            if (taken.length === 0) { return undefined; }
            // Whose settings they are is said — by the engine, which is what decides them
            // (owner's decision 2026-11-12: the voice's whole name is more than is needed).
            const item = new vscode.CompletionItem({ label: 'with', description: engineName }, vscode.CompletionItemKind.Keyword);
            item.detail = `How the voice sings: ${taken.map(p => p.name).join(', ')}`;
            item.insertText = 'with ';
            item.sortText = '~with';     // after what the line still needs (`on`)
            item.command = { command: 'editor.action.triggerSuggest', title: 'List the voice\'s settings' };
            return [item];
        }
        return taken.filter(p => !slot.written.includes(p.name)).map((p, i) => {
            const item = new vscode.CompletionItem({ label: p.name, description: engineName }, vscode.CompletionItemKind.Property);
            item.detail = singParameterDetail(p);
            item.insertText = p.name + ' ';
            item.filterText = p.name;
            item.sortText = String(i).padStart(3, '0');
            item.command = { command: 'editor.action.triggerSuggest', title: 'List values to begin from' };
            return item;
        });
    },
};

/** The values a number setting is begun from, least to most; the one that changes nothing is
 *  where the list opens (singValueChoices). */
function singValueItems(of: SingParameter, document: vscode.TextDocument, position: vscode.Position): vscode.CompletionItem[] {
    // What is written of the value so far, its sign included: a `-` is no part of a word to the
    // editor, and an item taken after one would stand behind it (`--3`).
    const begun = /[^\s(]*$/.exec(document.lineAt(position.line).text.slice(0, position.character))?.[0].length ?? 0;
    const range = new vscode.Range(position.line, position.character - begun, position.line, position.character);
    return singValueChoices(of).map((c, i) => {
        const item = new vscode.CompletionItem(c.text, vscode.CompletionItemKind.Value);
        item.range = range;
        item.detail = c.detail;
        item.sortText = String(i).padStart(3, '0');
        item.filterText = c.text;
        item.preselect = c.isDefault;
        return item;
    });
}

/**
 * The list inside a note's `@sing(…)` (owner's decisions 2026-11-12): the settings that can change
 * inside the music — every one but those that are one value for the voice; one value a phrase
 * steps where a phrase begins, and its line says so — that any voice the file's singer lines name
 * takes, each with what it does and its range; and where a value is expected, the two words a
 * value may be: `default` (the singer line's value again) and `to` (arrive at the value that
 * follows, gliding).
 */
const singMarkCompletion: vscode.CompletionItemProvider = {
    async provideCompletionItems(document, position) {
        const slot = singMarkSlot(document.lineAt(position.line).text.slice(0, position.character));
        if (!slot) { return undefined; }
        const engine = await renderer();
        const backend = engine?.backend instanceof CantoriaExtensionBackend ? engine.backend : undefined;
        if (slot.kind === 'value') {
            const back = new vscode.CompletionItem('default', vscode.CompletionItemKind.Keyword);
            back.detail = `${slot.of} as the singer line has it again (or as the voice does by itself)`;
            back.sortText = '~1';
            const to = new vscode.CompletionItem('to', vscode.CompletionItemKind.Keyword);
            to.detail = `Arrive at the value written after it with this note, gliding from the ${slot.of} before`;
            to.insertText = 'to ';
            to.sortText = '~2';
            // …and before the two words, values across the setting's range to begin from.
            let numbers: vscode.CompletionItem[] = [];
            if (backend) {
                const singers = singersWritten(document.getText());
                for (const who of singers.length > 0 ? singers : ['']) {
                    try {
                        const of = (await backend.parameters(await backend.voiceName(who || null))).find(p => p.name === slot.of);
                        if (of) { numbers = singValueItems(of, document, position); break; }
                    } catch { /* that voice's settings could not be listed: the two words stand alone */ }
                }
            }
            return [...numbers, back, to];
        }
        if (!backend) { return undefined; }
        const taken = new Map<string, SingParameter>();
        const singers = singersWritten(document.getText());
        for (const who of singers.length > 0 ? singers : ['']) {
            try {
                for (const p of await backend.parameters(await backend.voiceName(who || null))) {
                    if (p.scope !== 'voice' && !taken.has(p.name)) { taken.set(p.name, p); }
                }
            } catch (e) {
                outputChannel.appendLine(`singer: the settings of ${who || 'the default voice'} could not be listed — ${String(e)}`);
            }
        }
        return [...taken.values()].filter(p => !slot.written.includes(p.name)).map((p, i) => {
            const item = new vscode.CompletionItem(p.name, vscode.CompletionItemKind.Property);
            item.detail = singParameterDetail(p);
            item.insertText = p.name + ' ';
            item.sortText = String(i).padStart(3, '0');
            item.command = { command: 'editor.action.triggerSuggest', title: 'List values to begin from' };
            return item;
        });
    },
};

/**
 * A voice's settings marked where they are written (owner's decisions 2026-11-12): what the
 * document's singer lines and notes set (`lilysharp/singSettings`), against what the voices'
 * engine says each takes (Cantoria's `parameters`) — a name it does not take, a value out of its
 * range, a setting that cannot change inside the music. The server does not know the engines and
 * the engine does not know the document, so the two are joined here, a moment after the text
 * rests. Nothing is marked without a Cantoria that says what a voice takes: its silence would
 * read as "takes nothing". A document that sets nothing asks no engine.
 */
const singDiagnostics = vscode.languages.createDiagnosticCollection('Lily# singer');
const singTimers = new Map<string, ReturnType<typeof setTimeout>>();
function scheduleSingProblems(document: vscode.TextDocument): void {
    if (document.languageId !== 'lilysharp') { return; }
    const key = document.uri.toString();
    const pending = singTimers.get(key);
    if (pending) { clearTimeout(pending); }
    singTimers.set(key, setTimeout(() => { singTimers.delete(key); void markSingProblems(document); }, 700));
}
async function markSingProblems(document: vscode.TextDocument, tries = 0): Promise<void> {
    if (document.isClosed) { return; }
    // A file open before the server is up (the window's start): asked again when it is, a few times.
    if (!client || !clientReady) {
        if (tries < 5) { setTimeout(() => void markSingProblems(document, tries + 1), 2000); }
        return;
    }
    const version = document.version;
    let written: { Settings?: SingSettingDto[] | null; Singers?: (string | null)[] | null };
    try {
        written = await client.sendRequest('lilysharp/singSettings', { textDocument: { uri: document.uri.toString() } });
    } catch {
        return;
    }
    if (document.version !== version) { return; }
    if (!written.Settings || written.Settings.length === 0) { singDiagnostics.delete(document.uri); return; }
    const engine = await renderer();
    const backend = engine?.backend instanceof CantoriaExtensionBackend && engine.backend.listsParameters ? engine.backend : undefined;
    if (!backend) { singDiagnostics.delete(document.uri); return; }
    const takenBy = new Map<string, SingParameter[]>();
    try {
        const singers = written.Singers && written.Singers.length > 0 ? written.Singers : [null];
        for (const who of singers) { takenBy.set(who ?? '', await backend.parameters(await backend.voiceName(who))); }
    } catch (e) {
        // A voice that is not installed, an engine that did not start: nothing can be said of the settings.
        outputChannel.appendLine(`singer: the voices' settings could not be checked — ${String(e)}`);
        singDiagnostics.delete(document.uri);
        return;
    }
    if (document.version !== version) { return; }
    singDiagnostics.set(document.uri, singProblems(written.Settings, takenBy).map(p => {
        const d = new vscode.Diagnostic(new vscode.Range(document.positionAt(p.start), document.positionAt(p.end)), p.message, vscode.DiagnosticSeverity.Warning);
        d.source = 'Lily# singer';
        return d;
    }));
}

/** The first line of the `singer` list when no voice is installed (owner's request 2026-11-10): it
 *  says so and inserts nothing, the text under it kept, and taking it goes on to the setup. */
function noVoiceItem(document: vscode.TextDocument, range: vscode.Range, guide: VoiceGuide): vscode.CompletionItem {
    const typed = document.getText(range);
    const item = new vscode.CompletionItem(guide.label, vscode.CompletionItemKind.Issue);
    item.detail = guide.detail;
    item.documentation = new vscode.MarkdownString(guide.documentation);
    item.range = range;
    item.filterText = typed;
    item.insertText = typed;
    item.sortText = '  ';
    item.preselect = true;
    item.command = { command: 'lilysharp.setUpVoices', title: guide.detail, arguments: [guide] };
    return item;
}

/** Where a chosen voice's sample is kept, across sessions, so a voice heard once comes at once. */
let voiceSamplesDir: string | undefined;
let voiceSampleQueue: VoiceSampleQueue | undefined;
/** The voice last asked to be heard: a sample landing after another was chosen stays silent. */
let voiceSampleWanted = '';

/** A voice's sample; one sung with a line's settings (its key has a `!`) in a folder of its own,
 *  which keeps the newest few: every value a setting is stepped through is one more. */
const TRIED_SAMPLES_KEPT = 40;
function voiceSampleFile(id: string): string {
    return id.includes('!')
        ? path.join(voiceSamplesDir ?? '', 'tried', voiceSampleFileName(id))
        : path.join(voiceSamplesDir ?? '', voiceSampleFileName(id));
}
/** Lets go of the oldest samples sung with settings, past TRIED_SAMPLES_KEPT. */
function trimTriedSamples(): void {
    const folder = path.join(voiceSamplesDir ?? '', 'tried');
    try {
        const files = fs.readdirSync(folder).map(name => ({ file: path.join(folder, name), at: fs.statSync(path.join(folder, name)).mtimeMs }))
            .sort((a, b) => b.at - a.at);
        for (const old of files.slice(TRIED_SAMPLES_KEPT)) { try { fs.unlinkSync(old.file); } catch { /* in use */ } }
    } catch { /* no such folder yet */ }
}

function samples(): VoiceSampleQueue {
    return voiceSampleQueue ??= new VoiceSampleQueue(id => fs.existsSync(voiceSampleFile(id)), singVoiceSample,
        (id, e) => outputChannel.appendLine(`singer: the sample of ${id} was not sung: ${String(e)}`));
}

async function singVoiceSample(key: string): Promise<void> {
    const engine = await renderer();
    const backend = engine?.backend instanceof CantoriaExtensionBackend ? engine.backend : undefined;
    if (!backend?.api.voices) { throw new Error('no engine that lists its voices'); }
    const { id, language: asked, octave, sing } = parseVoiceSampleKey(key);
    const first = id.split('+')[0].trim();      // voices singing together: the first says the language
    const voice = (await backend.api.voices()).find(v => v.id === first);
    const language = asked ?? voice?.language;
    const score = language ? voiceSampleScore(language, octave) : undefined;
    if (!score) { throw new Error(`no sample phrase for the language ${language ?? 'it does not name'}`); }
    // The phrase as the line says the voice sings (its `with …`): what a setting does is heard.
    if (sing && Object.keys(sing).length > 0) { score.sing = sing; }
    const v = await backend.sing(`sample-${key}`, score, id);
    for (const note of v.notes ?? []) { tellSingerSetting(note); }
    await fs.promises.mkdir(path.dirname(voiceSampleFile(key)), { recursive: true });
    await fs.promises.copyFile(v.wav, voiceSampleFile(key));
    if (key.includes('!')) { trimTriedSamples(); }
}

/** Lets the document's preview say a short phrase in `id` (with no preview open it is silent, as every audition is). */
async function auditionVoice(uri: string, id: string, clock: StageClock, language?: string, lineGender?: string | null,
    sing?: Record<string, number | string>): Promise<void> {
    // Voices singing together: the first says the octave, as it says the language.
    const first = id.split('+')[0].trim();
    const engine = await renderer();
    const voice = engine?.backend instanceof CantoriaExtensionBackend ? (await engine.backend.api.voices?.())?.find(v => v.id === first) : undefined;
    const key = voiceSampleKey(id, sampleOctave(voice?.gender, lineGender), language, sing);
    voiceSampleWanted = key;
    const songAtStart = voiceRenderer?.singing ? 'being sung' : 'not being sung';
    const say = (end: string) => outputChannel.appendLine(`singer: hear ${id}: ${clock}${end} (the song was ${songAtStart} at the start)`);
    if (!vscode.workspace.getConfiguration('lilysharp').get<boolean>('audition.enabled', true)) { say(', auditions are off'); return; }
    if (!previewPanels.get(uri)) { say(', no preview is open'); return; }
    const onDisk = fs.existsSync(voiceSampleFile(key));
    // A sample with the line's settings takes the place of the ones of that voice still waiting:
    // stepping a value asks one a step, and only the last is wanted.
    const ready = await samples().ask(key, sing ? other => other.includes('!') && other.slice(0, other.indexOf('!')) === key.slice(0, key.indexOf('!')) : undefined);
    clock.mark(onDisk ? 'sample on disk' : 'sample sung');
    if (!ready) { say(', the sample could not be sung'); return; }
    if (voiceSampleWanted !== key) { say(', another voice was chosen meanwhile'); return; }
    const panel = previewPanels.get(uri);
    if (!panel) { say(', the preview closed meanwhile'); return; }
    const bytes = await fs.promises.readFile(voiceSampleFile(key));
    clock.mark('read');
    if (voiceSampleWanted !== key) { say(', another voice was chosen meanwhile'); return; }
    say('');
    void panel.webview.postMessage({ type: 'playVoiceSample', bytes: new Uint8Array(bytes), id, pressedAt: clock.startedAt, sentAt: Date.now() });
}

/** A name taken from the `singer` list at the top level (owner's request 2026-11-11): ` sings ` is
 *  written after it, so the list is asked again — the file's lyrics — and the voice is heard
 *  meanwhile, its words' language asked where the name stands (`nameColumn`), not at the caret. */
async function singerTaken(label: unknown, nameColumn: unknown): Promise<void> {
    void vscode.commands.executeCommand('editor.action.triggerSuggest');
    await hearSinger(label, nameColumn);
}

/** A name taken from the `singer` list: heard at once. A gender or a role is heard as the default voice it comes to. */
async function hearSinger(label: unknown, nameColumn?: unknown): Promise<void> {
    const editor = vscode.window.activeTextEditor;
    if (typeof label !== 'string' || !editor) { return; }
    const clock = new StageClock(Date.now());
    const engine = await renderer();
    if (!engine) { offerCantoria(); return; }
    clock.mark('engine');
    const voice = await engine.backend.voiceName(label);
    clock.mark('name');
    const caret = editor.selection.active;
    const lineText = editor.document.lineAt(caret.line).text;
    // The line as it stands: how it says the voice sings (its `with …`) is heard with the voice,
    // and with the caret in those settings the words' language is asked at the voice's name.
    const line = singerLineOf(lineText);
    const inSettings = line && line.withAt >= 0 && caret.character >= line.withAt;
    const asked = new vscode.Position(caret.line, singerLanguageColumn(lineText,
        typeof nameColumn === 'number' ? nameColumn : inSettings ? line.askColumn : caret.character));
    const slot = client && clientReady ? await client.sendRequest<{ Language?: string | null; Gender?: string | null }>('lilysharp/singerSlot',
        { textDocument: { uri: editor.document.uri.toString() }, offset: editor.document.offsetAt(asked) }).then(s => s, () => undefined) : undefined;
    await auditionVoice(editor.document.uri.toString(), voice, clock, slot?.Language ?? undefined, slot?.Gender,
        line && Object.keys(line.sing).length > 0 ? line.sing : undefined);
}

/**
 * The step keys on a `singer "…"` (owner's requests 2026-11-09, sessions 1170 and 1188): Ctrl+Shift+Up /
 * Down the voice above or below in the whole list, Ctrl+Shift+PageUp / PageDown the first voice of
 * another engine, among the voices that sing the words' language. The chosen voice says its phrase, and the score's own
 * voice follows from the edit (prepareVoices). False when the caret is not on one.
 */
async function stepSingerAt(editor: vscode.TextEditor, direction: 1 | -1, byVoice: boolean, pressedAt: number): Promise<boolean> {
    if (!client || !clientReady || editor.selections.length !== 1) { return false; }
    const doc = editor.document;
    const version = doc.version;
    const line = editor.selection.active.line;
    // On the line's `male` / `female` the keys change that word (the server's step).
    if (onSingerGender(doc.lineAt(line).text, editor.selection.active.character)) { return false; }
    const at = singerValueAt(doc.lineAt(line).text, editor.selection.active.character);
    if (!at) { return false; }
    const clock = new StageClock(pressedAt);
    clock.mark('key queue');
    const slot = await client.sendRequest<{ InSlot: boolean; Language?: string | null; Gender?: string | null }>('lilysharp/singerSlot',
        { textDocument: { uri: doc.uri.toString() }, offset: doc.offsetAt(new vscode.Position(line, at.start)) });
    clock.mark('server');
    if (!slot.InSlot || !slot.Language) { return false; }
    const engine = await renderer();
    clock.mark('engine');
    const api = engine?.backend instanceof CantoriaExtensionBackend ? engine.backend.api : undefined;
    if (!api?.voices) {
        offerCantoria();
        vscode.window.setStatusBarMessage('Lily#: the Cantoria extension lists the voices, and it is not there', 4000);
        return true;
    }
    const sings = (await api.voices()).filter(v => singsLanguage(v, slot.Language!));
    samples().warm(sings.map(v => voiceSampleKey(v.id, sampleOctave(v.gender, slot.Gender), slot.Language!)));
    const voices = sings.filter(v => fitsGender(v, slot.Gender));
    clock.mark('voices');
    const next = stepVoice(byVoiceId(voices), at.value, direction, !byVoice);
    if (!next) {
        vscode.window.setStatusBarMessage(byVoice ? `Lily#: ${at.value} is the end of the voices that sing ${slot.Language}`
            : `Lily#: no further provider has voices for ${slot.Language} that way`, 4000);
        return true;
    }
    if (doc.version !== version) { return true; }     // typed into meanwhile: the place is about another text
    const applied = await editor.edit(b => b.replace(new vscode.Range(line, at.start, line, at.end), next), { undoStopBefore: true, undoStopAfter: true });
    if (!applied) { return true; }
    clock.mark('edit');
    const caret = new vscode.Position(line, at.start + next.length);
    editor.selection = new vscode.Selection(caret, caret);
    const label = voices.find(v => v.id === next)?.label;
    vscode.window.setStatusBarMessage(`Lily#: singer ${next}${label ? ` (${label})` : ''}`, 4000);
    void auditionVoice(doc.uri.toString(), next, clock, slot.Language, slot.Gender);
    return true;
}

/** Whether this session has already said that Cantoria is needed. */
let cantoriaOffered = false;

/** Says, once a session, that voices need the Cantoria extension, and offers to install it (owner's
 *  request 2026-11-09, session 1205). Only where a voice was asked for: lyrics alone say nothing, since
 *  most people writing them mean them for the page. */
function offerCantoria(): void {
    if (cantoriaOffered || vscode.extensions.getExtension(CANTORIA_EXTENSION_ID)) { return; }
    cantoriaOffered = true;
    const install = 'Install Cantoria';
    void vscode.window.showInformationMessage(noVoiceGuide(false).message, install).then(async choice => {
        if (choice === install) { await installCantoria(); }
    });
}

async function installCantoria(): Promise<void> {
    try {
        await vscode.commands.executeCommand('workbench.extensions.installExtension', CANTORIA_EXTENSION_ID);
    } catch {
        // Not reachable from here (offline, or a gallery that does not list it): show the search.
        await vscode.commands.executeCommand('workbench.extensions.search', `@id:${CANTORIA_EXTENSION_ID}`);
    }
}

/** What this session has already said about voices that cannot be listed. */
const voiceSetupOffered = new Set<string>();

/** Says, once a session for each thing to say, that no voice can be listed. The `singer` list says it every time. */
function offerVoiceSetup(guide: Pick<VoiceGuide, 'message' | 'actions'>): void {
    if (voiceSetupOffered.has(guide.message)) { return; }
    voiceSetupOffered.add(guide.message);
    void showVoiceSetup(guide);
}

/** What would give a voice, as buttons on Cantoria's own commands. */
async function showVoiceSetup(guide: Pick<VoiceGuide, 'message' | 'actions'>): Promise<void> {
    const choice = await vscode.window.showInformationMessage(guide.message, ...guide.actions.map(a => a.label));
    const action = guide.actions.find(a => a.label === choice);
    if (action?.command) { await vscode.commands.executeCommand(action.command); }
    else if (action?.url) { void vscode.env.openExternal(vscode.Uri.parse(action.url)); }
}

/** The `singer` list's guide, taken: what it said, with its buttons, or else what a voice needs first. */
async function setUpVoices(guide: VoiceGuide): Promise<void> {
    if (vscode.extensions.getExtension(CANTORIA_EXTENSION_ID)) { await showVoiceSetup(guide); } else { await installCantoria(); }
}

/** `lilysharp.playback.singer`: the path of a `cantoria` program to run here (a developer's
 *  build), or '' — the usual case — for the Cantoria extension when it is installed. */
function singerSetting(): string {
    return settings().get<string>('playback.singer', '').trim();
}

/** The one engine of this host, found on the first voice asked for — the setting's program,
 *  else the Cantoria extension (CANTORIA_EXTENSION_ID, activated here; activating it starts
 *  no process) — and let go when the setting or the extensions change, and on deactivate.
 *  Undefined when there is neither: the lyrics are not sung and nothing is said. */
let voiceRenderer: VoiceRenderer | undefined;
let voiceRendererFor: string | undefined;   // the setting the renderer was made for
async function renderer(): Promise<VoiceRenderer | undefined> {
    const exe = singerSetting();
    if (voiceRenderer && voiceRendererFor === exe) { return voiceRenderer; }
    disposeSinger();
    if (exe) {
        voiceRenderer = new VoiceRenderer(new SingerProcess(exe, line => outputChannel.appendLine(line)));
        voiceRenderer.onSinging = singingStarted;
        voiceRenderer.onProgress = singingProgress;
    } else {
        const ext = vscode.extensions.getExtension(CANTORIA_EXTENSION_ID);
        if (!ext) { return undefined; }
        try {
            const api = cantoriaApiOf(ext.isActive ? ext.exports : await ext.activate());
            if (!api) { outputChannel.appendLine(`singer: ${CANTORIA_EXTENSION_ID} is installed but its API is not the shape Lily# reads`); return undefined; }
            voiceRenderer = new VoiceRenderer(new CantoriaExtensionBackend(api, tellSingerNote));
            voiceRenderer.onSinging = singingStarted;
        voiceRenderer.onProgress = singingProgress;
        } catch (e) {
            outputChannel.appendLine(`singer: ${CANTORIA_EXTENSION_ID} did not activate: ${String(e)}`);
            return undefined;
        }
    }
    voiceRendererFor = exe;
    // The extension's voice is named with it (`engine/name`); a program's is its own default.
    const voice = voiceRenderer.backend instanceof CantoriaExtensionBackend ? await voiceRenderer.backend.voiceName().catch(() => '') : '';
    outputChannel.appendLine(`singer: ${voiceRenderer.name}${voice ? `, voice ${voice}` : ''}`);
    return voiceRenderer;
}
function disposeSinger(): void {
    voiceRenderer?.dispose();
    voiceRenderer = undefined;
    voiceRendererFor = undefined;
    for (const panel of previewPanels.values()) {
        postedVoices.delete(panel);
        prepareQueue.cancel(`${panelId(panel)}/`);
        panel.webview.postMessage({ type: 'clearVoices' });
    }
}

/** `lilysharp.playback.singerPrepare`: when a voice whose text changed goes to the engine. */
function prepareModeSetting(): PrepareMode {
    return prepareModeOf(settings().get<string>('playback.singerPrepare'));
}

/** What each preview's practice menu last listed of the voices (voiceMenuKey). */
const menuVoices = new WeakMap<vscode.WebviewPanel, string>();
/** The voices each preview holds, part → key: a voice already there is not sent again. */
const postedVoices = new WeakMap<vscode.WebviewPanel, Map<string, string>>();
/** The document and score each preview's voices are of (`uri\nrender`): another one empties them first. */
const voiceDocs = new WeakMap<vscode.WebviewPanel, string>();
/** The previews that just turned to another document: its voices go to the engine at once, not after the rest an edit gets. */
const voiceTurned = new WeakSet<vscode.WebviewPanel>();
/** The parts each preview's score sings now (the last `voiceParts`): a voice landing for any
 *  other part is of a text that sang it and is dropped (owner's report 2026-11-09, session 1152:
 *  the lyrics row commented out, the microphone stayed — the engine's answer to the earlier text
 *  arrived after the page had emptied, and lit it again). Unknown until the first answer. */
const sungParts = new WeakMap<vscode.WebviewPanel, Set<string>>();
/** The voices of each preview's score that are NOT sung here, by key: a line nobody installed can
 *  sing (prepareVoices), a voice the engine failed (failVoice). A Play's notes leave their sections
 *  to the instrument where another line of the part sings (leftToTheInstrument; found 2026-11-13:
 *  they stayed silent). Of the preview's document and score: emptied with its voices. */
const missingVoices = new WeakMap<vscode.WebviewPanel, Set<string>>();
/** A number per preview, for the prepare queue's slots (`<id>/<part>`). */
const panelIds = new WeakMap<vscode.WebviewPanel, number>();
let nextPanelId = 0;
function panelId(panel: vscode.WebviewPanel): number {
    let id = panelIds.get(panel);
    if (id === undefined) { panelIds.set(panel, id = ++nextPanelId); }
    return id;
}

/**
 * Keeps a preview's voices to the document and score it shows. When they change, what the page
 * holds is the last document's (kept by part name — two files with a `melody` would share a
 * buffer): the page is told to drop it and to show the microphone as PREPARING until the new
 * document's voices are named (owner's report 2026-11-08, session 1146: after the switch the
 * microphone stayed blue); the caller's prepareVoices then asks for the new voices, which go
 * to the engine at once (voiceTurned). Answers whether anything changed.
 */
function followVoices(panel: vscode.WebviewPanel, uri: string): boolean {
    const voiceDoc = `${uri}\n${selectedRenders.get(uri) || ''}`;
    if (voiceDocs.get(panel) === voiceDoc) { return false; }
    voiceDocs.set(panel, voiceDoc);
    postedVoices.delete(panel);
    sungParts.delete(panel);
    missingVoices.delete(panel);
    voiceTurned.add(panel);
    prepareQueue.cancel(`${panelId(panel)}/`);
    void panel.webview.postMessage({ type: 'clearVoices', pending: true });
    return true;
}
/** A preview closed: nothing more is sung for it, and a voice in flight is dropped on landing. */
function forgetVoices(panel: vscode.WebviewPanel): void {
    voiceDocs.delete(panel);
    postedVoices.delete(panel);
    sungParts.delete(panel);
    missingVoices.delete(panel);
    voiceTurned.delete(panel);
    prepareQueue.cancel(`${panelId(panel)}/`);
}
/** What was said once per preview (a warning of the server's, a voice that was not sung). */
const singerTold = new WeakMap<vscode.WebviewPanel, Set<string>>();
/** What the engine said of a singer line's settings, shown once a session each: a setting written
 *  and not sung is a mistake in the score, so it is a warning and not a line of the status bar. */
const singerSettingsTold = new Set<string>();
function tellSingerSetting(text: string): void {
    outputChannel.appendLine(`singer: ${text}`);
    if (singerSettingsTold.has(text)) { return; }
    singerSettingsTold.add(text);
    void vscode.window.showWarningMessage(`Lily#: ${text}`);
}

function tellSingerOnce(panel: vscode.WebviewPanel, text: string): void {
    outputChannel.appendLine(`singer: ${text}`);
    let told = singerTold.get(panel);
    if (!told) { singerTold.set(panel, told = new Set()); }
    if (told.has(text)) { return; }
    told.add(text);
    vscode.window.setStatusBarMessage(`Lily#: ${text}`, 8000);
}

/** The pane's voice badge (owner's request 2026-11-08): a voice that will not be sung, with the
 *  engine's sentence (`ready` is the page's own, when it has decoded the WAV). */
function failVoice(panel: vscode.WebviewPanel, part: string, speed: number, why: string, voice?: SungVoiceDto): void {
    forgetAskedVoice(panel, part);
    // The words themselves cannot be sung by what is installed (a language no voice sings): that
    // is the writer's to know, not a render gone wrong - said as tellUnsingable says it.
    const unsingable = unsingableReason(why);
    if (unsingable) { tellUnsingable(part, unsingable); }
    else {
        // The engine FAILED on a voice it agreed to sing (owner, 2026-11-13, kaze.lys: the alto's
        // "Failed to load phrase data" was a line of the status bar, gone in eight seconds, that
        // named no line of the file and no cause): a warning on the voice's `singer` line in the
        // Problems panel - which line, what happened in plain words, where to look when the
        // server could tell, the engine's own words last - there until the voice is sung.
        const uri = voiceDocs.get(panel)?.split('\n')[0];
        const document = uri ? vscode.workspace.textDocuments.find(d => d.uri.toString() === uri) : undefined;
        const text = document?.getText() ?? '';
        const sentence = singerFailureText(voice ? voiceWho(voice, text) : part, why, voice?.WarningPlace);
        outputChannel.appendLine(`singer: ${clockNow()} ${sentence}`);
        if (uri && document && voice) {
            voiceProblems.set(uri, 'engine', part, [engineFailureProblem(voice, why, text)]);
            showVoiceProblems(uri);
        }
        tellSingerOnce(panel, `${part} is not sung, and its instrument plays — why is in the Problems panel`);
    }
    missingVoices.get(panel)?.add(part);
    void panel.webview.postMessage({ type: 'voiceState', part, speed, state: 'failed', detail: unsingable ?? why });
}

/** What is wrong with each document's voices now (singerCore.VoiceProblems), and its warnings in
 *  the Problems panel: the engine's failure on a voice at its `singer` line, the syllables a voice
 *  will not sing at their first note. A collection of its own: the settings' warnings
 *  (singDiagnostics) are set whole for a document and would take these away. */
const voiceProblems = new VoiceProblems();
const voiceDiagnostics = vscode.languages.createDiagnosticCollection('Lily# voices');
function showVoiceProblems(uri: string): void {
    const document = vscode.workspace.textDocuments.find(d => d.uri.toString() === uri);
    if (!document) { voiceDiagnostics.delete(vscode.Uri.parse(uri)); return; }
    const range = (start: number, end: number) => new vscode.Range(document.positionAt(start), document.positionAt(end));
    voiceDiagnostics.set(document.uri, voiceProblems.of(uri).map(p => {
        const d = new vscode.Diagnostic(range(p.start, p.end), p.message, vscode.DiagnosticSeverity.Warning);
        d.source = 'Lily# singer';
        if (p.related) {
            d.relatedInformation = [new vscode.DiagnosticRelatedInformation(
                new vscode.Location(document.uri, range(p.related.start, p.related.end)), p.related.message)];
        }
        return d;
    }));
}

/**
 * A singer line nobody installed can sing (owner's decisions 2026-11-13: a Latin alto played with
 * no voice, and only a line of the status bar - gone in eight seconds - said why): a line in the
 * output channel each time, and ONE notification a session for each reason, with the way to a
 * voice that could sing it. The reason is Cantoria's own (its plan, or what its sing refused
 * with). The part's instrument plays: the page stands it in wherever a voice is not there.
 */
function tellUnsingable(voice: string, reason: string): void {
    outputChannel.appendLine(`singer: ${clockNow()} ${voice} is not sung: ${reason}; its part's instrument plays`);
    offerVoiceSetup(unsingableGuide(reason));
}

/** The engine's speed as last timed, and when it last answered: the bar's estimates (session 1178). */
let voiceRenderRate = VOICE_RENDER_FIRST_RATE;
let voiceLastLanded = 0;

/** How long the engine should take over this voice at `speed`, for the bar under Play. */
function voiceEstimateMs(voice: SungVoiceDto, speed: number): number {
    return voiceRenderEstimateMs(sungSeconds(voice, speed), voiceRenderRate);
}

/** Why the voices are asked for: the text was rendered (`edit`), Play was pressed, the practice speed changed. */
type PrepareReason = 'edit' | 'play' | 'speed';

/** One voice to render for one preview; `mine` is the preview's postedVoices at the time (another map means the engine or the document changed). */
interface PrepareJob { panel: vscode.WebviewPanel; voice: SungVoiceDto; speed: number; mine: Map<string, string>; key: string; letGo: AbortController }

/** The engine's work, one job at a time per preview and part, the newest text winning
 *  (singerCore.PrepareQueue). A voice of an older text being sung is let go of as soon as a newer
 *  one comes: the engine finishes the phrase it is on and moves on, keeping what it sang (owner's
 *  decision 2026-11-10). A Play over the text already being sung changes nothing. */
const prepareQueue = new PrepareQueue<PrepareJob>(job => renderVoice(job), undefined,
    (running, newer) => { if (running.key !== newer.key) { running.letGo.abort(); } });

/** The clock for the voice lines of the output, which say when and why a voice waited. */
const clockNow = () => new Date().toTimeString().slice(0, 8);

/**
 * Keeps the voices of the score `panel` shows up to its text (owner's request 2026-11-08,
 * session 1148: the microphone said ready over a voice of the old words, and pulsed while
 * nothing was being sung). Asks the server for the sung voices (`lilysharp/sung`), names their
 * parts to the page (`voiceParts`), and for each compares the key its score and the engine's
 * voice would give with the key of the voice the page holds: the same — nothing to do; another —
 * the page drops the old voice and shows the part as WAITING, and the render is queued: at once
 * after a Play, a speed change or a turn to another document, or when the engine already has
 * the voice; after PREPARE_IDLE_MS of rest when the text was edited (typing restarts the clock,
 * and the engine never works under the keystrokes); not at all, until Play, with
 * `lilysharp.playback.singerPrepare` at onPlay. Nothing is asked when the server cannot answer
 * (the text has an error: the voices the page has stay as they are, the placeholder of a turn is
 * settled), and everything is dropped when there is no engine.
 */
async function prepareVoices(panel: vscode.WebviewPanel, uri: string, reason: PrepareReason): Promise<void> {
    // The preview may have turned since the caller looked (a render that corrected the score's
    // name, selectedRenders): the keys are made to agree here, so a drift never leaves the
    // page's placeholder unanswered (session 1161).
    followVoices(panel, uri);
    const voiceDoc = `${uri}\n${selectedRenders.get(uri) || ''}`;
    const current = () => voiceDocs.get(panel) === voiceDoc;   // false once the preview turned or closed meanwhile
    const file = path.basename(vscode.Uri.parse(uri).fsPath);
    // One line per ask in the output channel: what was asked and how it ended, so a microphone
    // that stays can be read back (owner's report 2026-11-09, session 1161).
    const log = (what: string) => outputChannel.appendLine(`singer: ${clockNow()} prepare (${reason}) ${file}: ${what}`);
    const settled = () => { if (current()) { void panel.webview.postMessage({ type: 'voiceSettled' }); } };
    if (!client || !clientReady) { log('the server is not up'); settled(); return; }
    const engine = await renderer();
    if (!current()) { log('the preview turned meanwhile'); return; }
    if (!engine) {
        log('no engine: the voices are dropped');
        if (reason === 'play' && writesSinger(vscode.workspace.textDocuments.find(d => d.uri.toString() === uri)?.getText() ?? '')) { offerCantoria(); }
        postedVoices.delete(panel);
        void panel.webview.postMessage({ type: 'clearVoices' });
        return;
    }
    let sung: { Voices?: SungVoiceDto[] | null; PrintedWords?: string[] | null; Error?: string | null };
    try {
        sung = await client.sendRequest('lilysharp/sung', { textDocument: { uri }, renderName: selectedRenders.get(uri) || null, allLyrics: true });
    } catch (e) {
        log(`lilysharp/sung failed: ${String(e)}`);
        settled();
        return;
    }
    if (!current()) { log('the preview turned meanwhile'); return; }
    if (sung.Error || !sung.Voices) { log(`no answer (${sung.Error ?? 'no voices'}): the states stay`); settled(); return; }
    // The menu's lines follow the text: a `singer` line rewritten renames its voice there. Rarely
    // true, so the costly parts list is asked only then.
    const listed = voiceMenuKey(sung.Voices.filter(v => v.Notes.length > 0));
    if (menuVoices.has(panel) && menuVoices.get(panel) !== listed) {
        menuVoices.set(panel, listed);
        void panel.webview.postMessage({ type: 'voiceLinesChanged' });
    }
    if (reason === 'play') { for (const part of sung.PrintedWords ?? []) { tellSingerOnce(panel, printedWordsNote(part)); } }
    // Which parts sing now: the score's own, and the file's other singable lyrics the menu turned
    // on (voicesOn, session 1163). The page drops the voices and the badge's states of any other
    // (a score or a file with no lyrics empties it — owner's report 2026-11-08: the microphone
    // stayed on after the lyrics were gone).
    const voicesOn = new Set(previewPractice.get(panel)?.voicesOn ?? []);
    const active = sung.Voices.filter(v => v.Notes.length > 0 && (v.InScore !== false || voicesOn.has(sungKey(v))));
    const parts = new Set(active.map(sungKey));
    log(parts.size === 0 ? 'no sung part' : `sung: ${[...parts].join(', ')}`);
    void panel.webview.postMessage({ type: 'voiceParts', parts: [...parts] });
    sungParts.set(panel, parts);
    const speed = previewPractice.get(panel)?.speed ?? 1;
    let posted = postedVoices.get(panel);
    if (!posted) { postedVoices.set(panel, posted = new Map()); }
    const mine = posted;
    // A part sung no more (its words gone): its voice is forgotten here as the page forgets it,
    // and a job of its earlier text, waiting or singing, lands nowhere (renderVoice's wanted).
    for (const part of [...mine.keys()]) {
        if (!parts.has(part)) { mine.delete(part); prepareQueue.cancelSlot(`${panelId(panel)}/${part}`); }
    }
    const turned = voiceTurned.delete(panel);
    const mode = prepareModeSetting();
    // The Problems panel follows the text: a voice the score sings no more takes its warnings
    // away, and each voice's words are said anew (a syllable that is not kana, at its note).
    const shownText = vscode.workspace.textDocuments.find(d => d.uri.toString() === uri)?.getText();
    let problemsChanged = voiceProblems.keep(uri, parts);
    for (const voice of active) {
        for (const warning of voice.Warnings ?? []) { tellSingerOnce(panel, `${voice.Part}: ${warning}`); }
        if (shownText !== undefined && voiceProblems.set(uri, 'words', sungKey(voice), wordsProblems(voice, shownText))) { problemsChanged = true; }
    }
    if (problemsChanged) { showVoiceProblems(uri); }
    // Who would sing, asked before anything is sung (Cantoria's plan): a line nobody installed
    // can sing never goes to the engine - it is said (tellUnsingable), shown as not sung on its
    // line of the practice menu, and its part's instrument stands in, having no voice.
    const unsingable = await engine.unsingable(active);
    if (!current()) { return; }
    // …and where another line of its part sings, its sections are the instrument's in the next
    // Play's notes (missingVoices): anew each time, a voice that failed before being asked again below.
    missingVoices.set(panel, new Set(unsingable.keys()));
    for (const voice of active) {
        const id = sungKey(voice);
        const slot = `${panelId(panel)}/${id}`;
        const nobody = unsingable.get(id);
        if (nobody !== undefined) {
            mine.delete(id);
            prepareQueue.cancelSlot(slot);
            tellUnsingable(id, nobody);
            void panel.webview.postMessage({ type: 'voiceState', part: id, speed, state: 'failed', detail: nobody });
            continue;
        }
        let key: string;
        try {
            key = await engine.key(voice, speed);
        } catch (e) {
            failVoice(panel, id, speed, engineSaid(e), voice);
            continue;
        }
        if (!current()) { return; }
        if (mine.get(id) === key) {     // the page has this text's voice; a job of an older text can go
            prepareQueue.cancelSlot(slot);
            if (voiceProblems.set(uri, 'engine', id, [])) { showVoiceProblems(uri); }     // …and a failure on another text is not this one's
            continue;
        }
        mine.delete(id);
        const rests = reason === 'edit' && !turned && !engine.has(key);
        if (rests && mode === 'onPlay') {
            void panel.webview.postMessage({ type: 'voiceState', part: id, speed, state: 'waiting', estimateMs: voiceEstimateMs(voice, speed) });
            prepareQueue.cancelSlot(slot);
            continue;
        }
        // The engine may ALREADY be singing this very text for a job that a newer prepare has
        // since replaced (the file opens, and a second prepare follows the first within a second):
        // the replaced job says nothing more to the page, and the one that waits finds the voice
        // sung and never says "singing" either - the microphone pulsed for 24 s with no bar
        // (owner's report 2026-11-13, kaze.lys). So the page is told by the KEY: here if the
        // engine is on it now, and by singingStarted when it begins.
        askVoice(panel, id, key, speed, voiceEstimateMs(voice, speed));
        void panel.webview.postMessage({ type: 'voiceState', part: id, speed, state: rests ? 'waiting' : 'preparing', estimateMs: voiceEstimateMs(voice, speed), key, singing: !rests && engine.isSinging(key) ? true : undefined });
        const busy = prepareQueue.busy(slot);
        prepareQueue.schedule(slot, { panel, voice, speed, mine, key, letGo: new AbortController() }, rests ? PREPARE_IDLE_MS : 0);
        log(`${id}: ${rests ? `waits for the text to rest ${PREPARE_IDLE_MS} ms` : 'goes now'}${busy ? ', after what is being sung lets go' : ''}`);
    }
}

/** The voice each preview's part is waiting for, by the engine's key (askVoice): who to tell when the engine begins on it. */
const askedVoices = new Map<string, { panel: vscode.WebviewPanel; part: string; speed: number; estimateMs: number }[]>();

function askVoice(panel: vscode.WebviewPanel, part: string, key: string, speed: number, estimateMs: number): void {
    forgetAskedVoice(panel, part);
    const asked = askedVoices.get(key) ?? [];
    asked.push({ panel, part, speed, estimateMs });
    askedVoices.set(key, asked);
}

/** The part's voice has landed, failed, or is asked anew: nobody waits for the old key. */
function forgetAskedVoice(panel: vscode.WebviewPanel, part: string): void {
    for (const [key, asked] of askedVoices) {
        const left = asked.filter(a => !(a.panel === panel && a.part === part));
        if (left.length === 0) { askedVoices.delete(key); } else if (left.length !== asked.length) { askedVoices.set(key, left); }
    }
}

/** The engine has begun on `key` (VoiceRenderer.onSinging): every preview waiting for that voice shows its bar. */
function singingStarted(key: string): void {
    for (const a of askedVoices.get(key) ?? []) {
        if (!(sungParts.get(a.panel)?.has(a.part) ?? true)) { continue; }
        try {
            void a.panel.webview.postMessage({ type: 'voiceState', part: a.part, speed: a.speed, state: 'preparing', estimateMs: a.estimateMs, singing: true, key });
        } catch { /* the preview is gone */ }
    }
}

/**
 * What the engine says of the voice of `key` as it sings it (VoiceRenderer.onProgress): to every
 * preview waiting for THAT voice, whichever job asked the engine (owner's decision 2026-11-13: the
 * count went to the asking job's page only, and a job replaced meanwhile kept it to itself, so
 * the bar ran on its estimate). A part that has since asked for another text waits under another
 * key and hears nothing of this one.
 */
function singingProgress(key: string, p: SingProgress): void {
    for (const a of askedVoices.get(key) ?? []) {
        if (!(sungParts.get(a.panel)?.has(a.part) ?? true)) { continue; }
        try {
            void a.panel.webview.postMessage({ type: 'voiceProgress', part: a.part, key, toSing: p.toSing, sung: p.sung, msPerSecond: voiceRenderRate });
        } catch { /* the preview is gone */ }
    }
}

/**
 * One queued voice: rendered by the engine unless rendered before, and handed to the page as WAV
 * bytes (`voice`), with `preparing` on the badge while the engine sings and `failed` when it
 * cannot, or when it has not answered in PREPARE_TIMEOUT_MS. A voice whose preview turned to
 * another document, whose part the score sings no more, or whose part has a newer text waiting
 * in the queue, is dropped on landing.
 */
async function renderVoice(job: PrepareJob): Promise<void> {
    const { panel, voice, speed, mine } = job;
    const id = sungKey(voice);
    const slot = `${panelId(panel)}/${id}`;
    const wanted = () => !job.letGo.signal.aborted && postedVoices.get(panel) === mine && !prepareQueue.hasWaiting(slot)
        && (sungParts.get(panel)?.has(id) ?? true);
    const engine = voiceRenderer;
    if (!engine || !wanted()) { return; }
    let key: string, rendered: Promise<{ wav: string; duration: number; notes?: string[] }>, cached: boolean;
    // What the engine says of this render (owner's request 2026-11-11): the seconds of music it
    // has to sing ANEW - it keeps every phrase it sang, so after an edit that is the phrases
    // changed - and how many are done. The page's bar is counted on these; `msPerSecond` is the
    // pace learnt so far, for the time before the first phrase is in. The key is the job's own,
    // which the page holds its count by.
    let told: SingProgress | undefined;
    // (The page hears it by the voice's key, from singingProgress: a job a newer prepare has
    // replaced goes on being sung for the one that waits, and the bar must still be counted.
    // Here it is kept for the pace learnt below.)
    const onProgress = (p: SingProgress) => { told = p; };
    try {
        outputChannel.appendLine(`singer: ${clockNow()} ${id}: to the engine`);
        ({ key, rendered, cached } = await engine.render(voice, speed, job.letGo.signal, onProgress));
    } catch (e) {
        if (wanted()) { failVoice(panel, id, speed, engineSaid(e), voice); }
        return;
    }
    // Asked again after the await: the preview may have turned to a file with no lyrics
    // meanwhile, whose empty `voiceParts` had already gone out — a `preparing` landing after it
    // kept the microphone pulsing for ever (owner's report 2026-11-09, session 1160).
    if (!wanted()) { return; }
    if (!cached) { void panel.webview.postMessage({ type: 'voiceState', part: id, speed, state: 'preparing', estimateMs: voiceEstimateMs(voice, speed), singing: true, key: job.key }); }
    const askedAt = Date.now();
    let timer: ReturnType<typeof setTimeout> | undefined;
    const timeout = new Promise<never>((_, reject) => {
        timer = setTimeout(() => reject(new Error(`the engine gave no answer in ${PREPARE_TIMEOUT_MS / 1000} s`)), PREPARE_TIMEOUT_MS);
    });
    const singing = (async () => {
        try {
            const v = await Promise.race([rendered, timeout]);
            // What the engine had to say of the score: a setting of the singer line the voice
            // does not take is not sung, and the writer is told which - once, where it is seen.
            for (const note of v.notes ?? []) { tellSingerSetting(note); }
            if (!cached) {
                // The engine sings one at a time: this voice's time began when the last one landed.
                const now = Date.now();
                // The pace is learnt on what was SUNG: the engine's own count where it gave one
                // (a render that laid kept phrases and sang two seconds says nothing of its pace
                // over the whole score, and one that sang next to nothing teaches nothing).
                const sungSecondsNow = told ? told.toSing : v.duration;
                if (!told || told.toSing >= 1) {
                    voiceRenderRate = learnRenderRate(voiceRenderRate, now - Math.max(askedAt, voiceLastLanded), sungSecondsNow);
                }
                voiceLastLanded = now;
            }
            if (!wanted()) { return; }
            const bytes = await fs.promises.readFile(v.wav);
            if (!wanted()) { return; }
            mine.set(id, key);
            // Sung: what was said of the engine's failure on this voice is over.
            const sungUri = voiceDocs.get(panel)?.split('\n')[0];
            if (sungUri && voiceProblems.set(sungUri, 'engine', id, [])) { showVoiceProblems(sungUri); }
            // entries: where the voice may come in if its part is already playing (joinVoice).
            forgetAskedVoice(panel, id);
            void panel.webview.postMessage({ type: 'voice', part: id, key, speed, lead: VOICE_LEAD_S, bytes: new Uint8Array(bytes), entries: voiceEntries(voice.Notes) });
            outputChannel.appendLine(`singer: ${clockNow()} ${id} ${cached ? 'from the cache' : 'sung'} (${v.duration.toFixed(1)} s at speed ${speed})`);
        } catch (e) {
            if (wanted()) { failVoice(panel, id, speed, engineSaid(e), voice); }
        } finally {
            if (timer !== undefined) { clearTimeout(timer); }
        }
    })();
    if (!cached) { vscode.window.setStatusBarMessage(`Lily#: singing ${voice.Part}…`, singing); }
    await singing;
}

/** How long a candidate's Play waits for a voice the renderer does not hold: the engine's own
 *  store answers within it (a voice sung in an earlier session), a voice to be sung does not. */
const CANDIDATE_VOICE_WAIT_MS = 600;

/**
 * The voices of a text for a candidate panel's Play (AiTransformDeps.voicesFor; owner's request
 * 2026-11-11). A voice is kept by its notes, its words and its singer, so a candidate that left a
 * sung part alone finds the voice the preview had sung — at once, with no work for the engine. A
 * part the candidate changed is sung now, at the score's own speed; its instrument stands in for
 * this Play and `onSung` says when the voice is there. The practice menu's switches are the
 * preview's and are not read: the panel plays the score as written. With no engine, no server or a
 * text that sings nothing, nothing is asked.
 */
async function candidateVoices(text: string, uri: vscode.Uri, renderName: string | undefined, sounds: CandidateSounds,
    onSung: (voice: CandidateVoice) => void): Promise<CandidateVoices> {
    const notSung: string[] = [];
    const out: CandidateVoices = { voices: [], silenced: [], waiting: [], notSung };
    if (!client || !clientReady || (sounds.Sung ?? []).length === 0) { return out; }
    const engine = await renderer();
    if (!engine) { return out; }
    const log = (what: string) => outputChannel.appendLine(`singer: ${clockNow()} candidate: ${what}`);
    let sung: { Voices?: SungVoiceDto[] | null; Error?: string | null };
    try {
        sung = await client.sendRequest('lilysharp/sung', { textDocument: { uri: uri.toString() }, text, renderName: renderName ?? null });
    } catch (e) {
        log(`lilysharp/sung failed: ${String(e)}`);
        return out;
    }
    if (sung.Error || !sung.Voices) { log(`no voices (${sung.Error ?? 'none'})`); return out; }
    const silenced = new Set<number>();
    // The voice as the panel takes it: its WAV, the instrument it replaces, and the notes after a
    // rest, where it may come in while its part already plays.
    const handed = async (voice: SungVoiceDto, wav: string): Promise<CandidateVoice> => ({
        part: voice.Part, bytes: new Uint8Array(await fs.promises.readFile(wav)), lead: VOICE_LEAD_S,
        track: candidateSilencedTrack(voice.Part, sounds), entries: voiceEntries(voice.Notes),
    });
    const candidates = candidateSings(sung.Voices, sounds);
    const unsingable = await engine.unsingable(candidates);
    for (const voice of candidates) {
        // Nobody installed can sing it: its instrument plays, and the writer is told as the preview tells it.
        const nobody = unsingable.get(sungKey(voice));
        if (nobody !== undefined) { tellUnsingable(sungKey(voice), nobody); notSung.push(sungKey(voice)); continue; }
        try {
            const { rendered, cached } = await engine.render(voice, 1);
            let late: ReturnType<typeof setTimeout> | undefined;
            const v = cached ? await rendered : await Promise.race([
                rendered,
                new Promise<undefined>(resolve => { late = setTimeout(() => resolve(undefined), CANDIDATE_VOICE_WAIT_MS); }),
            ]);
            if (late !== undefined) { clearTimeout(late); }
            if (!v) {
                out.waiting.push(voice.Part);
                log(`${voice.Part}: to the engine; its instrument plays this time`);
                rendered.then(async sungNow => { log(`${voice.Part}: sung (${sungNow.duration.toFixed(1)} s)`); onSung(await handed(voice, sungNow.wav)); })
                    .catch(e => log(`${voice.Part} was not sung — ${String(e)}`));
                continue;
            }
            const there = await handed(voice, v.wav);
            out.voices.push(there);
            if (there.track >= 0) { silenced.add(there.track); }
            log(`${voice.Part}: ${cached ? 'the voice the preview had' : 'from the engine\'s store'} (${v.duration.toFixed(1)} s)`);
        } catch (e) {
            log(`${voice.Part} was not sung — ${String(e)}; its instrument plays`);
        }
    }
    out.silenced = [...silenced];
    return out;
}

/** The picker behind `Lily#: Choose MIDI Output…` and the setting's link: the preview's
 *  own synth (empties the setting), or one of the machine's MIDI output ports as the server
 *  lists them now. Written to the user settings as `lilysharp.playback.midiOutput`. */
async function chooseMidiOutput(): Promise<void> {
    const current = midiOutputSetting();
    type Pick = vscode.QuickPickItem & { name: string };
    const items: Pick[] = [{
        label: 'The preview\'s own synth', description: current ? '' : '(current)',
        detail: 'The sound font (Lily#: Choose Sound Font…) — empties the setting', name: '',
    }];
    startClient();   // no .lys open yet: the server starts for the list
    await clientReadyPromise;
    const ports = await midiOutRequest<{ Ports?: string[]; Error?: string | null }>('lilysharp/midiOut/ports', {});
    if (ports.Error) {
        outputChannel.appendLine(`MIDI outputs: ${ports.Error}`);
        items.push({ label: 'No MIDI output could be listed', detail: ports.Error, name: current, alwaysShow: true });
    } else {
        for (const name of ports.Ports ?? []) {
            items.push({ label: name, description: name === current ? '(current)' : '',
                detail: /GS Wavetable/i.test(name) ? 'Windows\' own General MIDI synth (plays with no setup)'
                    : /loopMIDI|IAC/i.test(name) ? 'A virtual port: whatever listens on it (a DAW, a VST host) plays'
                    : undefined, name });
        }
        if (current && !(ports.Ports ?? []).includes(current)) {
            items.push({ label: current, description: '(current — not there now)', name: current });
        }
        if ((ports.Ports ?? []).length === 0) {
            items.push({ label: 'This machine lists no MIDI output', detail: 'Install a virtual port (loopMIDI on Windows; an IAC bus in Audio MIDI Setup on a Mac) or plug an interface in', name: current, alwaysShow: true });
        }
    }
    const picked = await vscode.window.showQuickPick(items, {
        title: 'Lily#: where the preview\'s sounds go',
        placeHolder: 'Each part keeps its channel and sends its General MIDI program; the drums are on channel 10',
    });
    if (!picked || picked.name === current) { return; }
    await vscode.workspace.getConfiguration('lilysharp').update('playback.midiOutput', picked.name, vscode.ConfigurationTarget.Global);
    midiOutErrorsTold.delete(picked.name);
    vscode.window.setStatusBarMessage(
        picked.name ? `Lily#: the preview plays through ${picked.name}` : 'Lily#: the preview plays its own synth', 5000);
}

/** The preview webview's resource roots: the music fonts, the text fonts, the synth
 *  and the sound font's folder. */
function previewResourceRoots(extensionUri: vscode.Uri): vscode.Uri[] {
    const roots = [
        vscode.Uri.joinPath(extensionUri, 'media', 'fonts'),
        textFontsRoot(extensionUri),
        synthRoot(extensionUri),
    ];
    const sf = soundFontFile(extensionUri);
    if (sf) { roots.push(sf.root); }
    return roots;
}

/** The synth's files as the page's webview reaches them. */
function synthUrls(webview: vscode.Webview, extensionUri: vscode.Uri): SynthUrls {
    const root = synthRoot(extensionUri);
    const sf = soundFontFile(extensionUri);
    return {
        script: webview.asWebviewUri(vscode.Uri.joinPath(root, 'preview-synth.js')).toString(),
        processor: webview.asWebviewUri(vscode.Uri.joinPath(root, 'spessasynth_processor.min.js')).toString(),
        soundFont: sf ? webview.asWebviewUri(sf.file).toString() : '',
        stretch: webview.asWebviewUri(vscode.Uri.joinPath(root, 'SignalsmithStretch.mjs')).toString(),
    };
}

/** The preview's color scheme, as the `lilysharp.preview.theme` setting spells it:
 *  `auto` (follow VS Code's theme — the webview's prefers-color-scheme tracks it),
 *  `light` or `dark`. Anything else reads as `auto`. */
function getPreviewTheme(): 'auto' | 'light' | 'dark' {
    const v = vscode.workspace.getConfiguration('lilysharp').get<string>('preview.theme', 'auto');
    return v === 'light' || v === 'dark' ? v : 'auto';
}

/**
 * The settings the language server reads, in the shape it reads them — sent as
 * initializationOptions at start and as didChangeConfiguration afterwards:
 * completion.flatSpelling, and fontPaths (the folders this machine keeps SMuFL music
 * fonts in; a score names its font only).
 */
function serverSettings(config: vscode.WorkspaceConfiguration) {
    return {
        completion: {
            flatSpelling: config.get<string>('completion.flatSpelling', 'full')
        },
        fonts: {
            paths: config.get<string[]>('fontPaths', [])
        }
    };
}

export function deactivate(): Thenable<void> | undefined {
    // Clear all debounce timers
    previewPanels.forEach(panel => previewSessions.get(panel)?.cancelRefresh());
    // The singing engine, and its own server with it.
    voiceRenderer?.dispose();
    voiceRenderer = undefined;

    if (!client) {
        return undefined;
    }
    return client.stop();
}


