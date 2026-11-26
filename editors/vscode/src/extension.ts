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
import {
    LanguageClient,
    LanguageClientOptions,
    ServerOptions,
    TransportKind
} from 'vscode-languageclient/node';
import { registerAiTransform, showCandidateSounding, tellCandidateAudioBlocked } from './aiTransform';
import { registerAiComplete } from './aiComplete';
import { registerSongToDance } from './songToDance';
import { findCopilotLogFrom, pickAiModel, rememberRefusalsIn } from './modelClient';
import { registerSmartTyping } from './smartTyping';
import { registerPageText } from './pageText';
import { registerStaffGroupWrap, staffGroupsOffered } from './staffGroupWrap';
import { registerStepAudition } from './stepAudition';
import { registerCaretInfo } from './caretInfo';
import { registerExportBatch } from './exportBatch';
import { OmrImportDeps, importFromImage } from './omrImport';
import { DecodeResult, attachRecording, followCopies, followRenames, keepRecordingSettings, sendNoRecording, sendRecording, setRecordingSilent, useRecordingSettingsStore } from './recording';
import { TimedNote, soundingOf, soundingPartsOf, soundingWait, startSecondsAt } from './playbackStart';
import { convertRecording, findOrecchiante, registerOrecchianteContext } from './orecchiante';
import { registerOrecchianteReport } from './orecchianteSeed';
import { newScoreStarts } from './menuContextCore';
import { recordingPathFor } from './recordingCore';
import { findOmr, registerOmrContexts } from './omrReader';
import { OmrScanDeps, hasScan, previewHoversBar, previewReady, previewShowsBar, registerScanFollow, scanBarAt, scanColumnOf, scanSideOf, scanThresholdOf, showScan } from './omrScan';
import { registerOmrAiFix } from './omrAiFix';
import { registerVerifyOnSave } from './omrVerify';
import { markdownItExtensionApi } from './markdownFence';
import { PreviewSession } from './previewSession';
import { svgPostKey, pagesSummary, SvgPages, previewOnEditorShown, sourceGroupToReveal, newPreviewColumn, scoreGroupFor, sourceGroupToOpen, SourceTab, jumpOffsetFor, chordSpans, caretHighlight, soundingLight, SoundingSession, auditionBarRange, messageChanged } from './previewCore';
import { textFontFaceCss } from './scoreFonts';
import { getPreviewHtml } from './previewHtml';
import { stemsApi } from './stems';

import { TodoMark, parseStarts, todoDeletions } from './todoResolveCore';
import { renderAudioFiles } from './audioRender';
import { PracticeState, practiceSummary } from './webview/playbackCore';
import { writeVideos } from './videoExport';
import { registerSinging, deactivateSinging, installCantoria, upgradeCantoria, checkCantoriaUpgrade, firstSongFile, hearSinger, stepSingerAt, renderer, disposeSinger, menuVoices, missingVoices, followVoices, forgetVoices, prepareVoices, candidateVoices } from './singing';
import { useSoundFontLog, rememberSoundFont, wavSoundFont, chooseSoundFont, previewResourceRoots, synthUrls } from './soundFont';
import { useMidiOutHost, midiOutputSetting, midiOutRequest, reportMidiOutError, chooseMidiOutput } from './midiOut';
import { useExportHost, openInDefaultApp, exportPreview, practisedJobs, recordingRendered, recordJobs, singJobs, drawFramesFor, videoFfmpeg } from './exportPreview';
import { useAiEntryHost, aiTransformWholeScore, aiTransformFromScore, askAiInChat, noteSelectionEnd } from './aiEntry';
import { useMusicXmlImportHost, importMusicXml } from './musicXmlImport';
import { NEW_SCORE_TEMPLATE, newScoreTemplateUri, uriExists, newScoreTemplate, newScoreTitleRange, applyScoreTemplate } from './newScore';
import { registerEditCommands } from './editCommands';
import { registerMenuContexts } from './menuContext';
import { resolveServerLaunch } from './serverLaunch';
import { leftToTheInstrument, SungVoiceDto, sungKey, voiceMenuLabels, voiceMenuKey, voiceMenuTitleOf } from './singerCore';

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
// The LONGEST a change waits for the next one before the preview is refreshed. The wait itself is
// as long as the last incremental refreshes of that preview took (PreviewSession.refreshDelay,
// owner's decision 2026-11-18): 20-odd ms on a score of a few pages, this on one too slow to
// follow the hand, and on a preview just opened. `lilysharp.preview.refreshDelay` is no longer in
// the Settings UI (owner's decision 2026-10-25: a tuning value), so this is the ceiling unless
// settings.json still names one. It was the wait itself until session 1302 (and 60 before).
const DEBOUNCE_DELAY_DEFAULT = 100;
// The wait last said in the output channel, by panel: said again when it has changed.
const saidRefreshDelay = new WeakMap<vscode.WebviewPanel, number>();
// The longest a change waits for the preview while more changes keep coming (a held key): the
// debounce above restarts on each change, this does not (owner's report 2026-11-16).
const PREVIEW_MAX_WAIT = 120;
// When each document was last edited: the header's notes wait for the text to rest (soundingWait).
const lastEditAt = new Map<string, number>();

/** Opens the walkthrough's first song as a new document (firstSongFile says which: the language
 *  the voices sing, and the whole song or its opening). */
async function openFirstSong(context: vscode.ExtensionContext, whole?: boolean): Promise<void> {
    const sample = await vscode.workspace.fs.readFile(vscode.Uri.joinPath(context.extensionUri, 'walkthrough', await firstSongFile(whole)));
    const doc = await vscode.workspace.openTextDocument({ language: 'lilysharp', content: Buffer.from(sample).toString('utf8') });
    await vscode.window.showTextDocument(doc);
}

export function activate(context: vscode.ExtensionContext) {
    outputChannel.appendLine('Lily# extension activating...');
    // The models that refused as VS Code's own, remembered from an earlier session (modelClient).
    rememberRefusalsIn(context.globalState);
    // The recording panel's settings, kept per chart and recording (recording.ts).
    useRecordingSettingsStore(context.workspaceState);
    // The preview's sound font and the MIDI output (soundFont.ts, midiOut.ts): what they say
    // goes to the output channel, and the MIDI output asks the server.
    useSoundFontLog(outputChannel);
    useMidiOutHost({ getClient: () => client, isReady: () => clientReady, ensureClientReady, outputChannel });
    // The preview's Export (exportPreview.ts).
    useExportHost({ getClient: () => client, isReady: () => clientReady, outputChannel, previewPanels, practiceFor });
    // The MusicXML and MIDI import (musicXmlImport.ts).
    useMusicXmlImportHost({ getClient: () => client, outputChannel, openPreview });
    // The ways into the AI from a score (aiEntry.ts): a score with no tab open is opened as a
    // click on its preview opens it.
    useAiEntryHost({
        reopenSource: async uri => {
            const panel = previewPanels.get(uri);
            return panel ? openPreviewSource(panel, uri) : undefined;
        },
    });
    // The practice panel's folds, per file.
    panelFoldsStore = context.workspaceState;
    // Copilot Chat's log, which names the model Copilot's Auto answered with (modelClient).
    findCopilotLogFrom(context.logUri);

    const config = vscode.workspace.getConfiguration('lilysharp');
    // Where the server is and how it is run (serverLaunch.ts).
    const launch = resolveServerLaunch(context, config, outputChannel);
    if (!launch) {
        return;
    }

    const runOptions = {
        command: launch.command,
        args: launch.args,
        transport: TransportKind.stdio,
        options: launch.env ? { env: launch.env } : undefined
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
            const fonts = e.affectsConfiguration('lilysharp.fonts.folders');
            if ((!fonts && !e.affectsConfiguration('lilysharp.editing.flatSpelling')) || !client || !clientReady) {
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
    registerOrecchianteContext(context, outputChannel);

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
    // Check on save against the scan (B5, lilysharp.scans.verifyOnSave).
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
            if (e.affectsConfiguration('lilysharp.playback.MIDIOutput')) {
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
        // `lilysharp.playback.MIDIOutput` through a picker of the machine's MIDI output ports
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
        // The steps for each engine (owner's request 2026-11-12): what to download, where to unpack
        // it, which Cantoria command to run. The no-voice messages carry the same command. Since
        // 2026-11-18 they are a walkthrough of this extension's own (package.json "singing"), its
        // buttons Cantoria's commands, and not a page of the site: the way to a first voice does
        // not hang on a link. The walkthrough is hidden until it is asked for (its `when`), so
        // that installing Lily# does not open a page about singing; where VS Code cannot open
        // it, the engines' page is shown as it is written.
        vscode.commands.registerCommand('lilysharp.setUpSingingVoice', async () => {
            await vscode.commands.executeCommand('setContext', 'lilysharp.singingGuide', true);
            // The first step is "Upgrade" where the Cantoria installed is not the newest (owner's
            // request 2026-11-18): asked of the gallery here, each time the steps are opened.
            await checkCantoriaUpgrade();
            try {
                await vscode.commands.executeCommand('workbench.action.openWalkthrough', `${context.extension.id}#singing`, false);
            } catch {
                await vscode.commands.executeCommand('markdown.showPreview', vscode.Uri.joinPath(context.extensionUri, 'walkthrough', 'engines.md'));
            }
        }),
        vscode.commands.registerCommand('lilysharp.installCantoria', () => installCantoria()),
        vscode.commands.registerCommand('lilysharp.upgradeCantoria', () => upgradeCantoria()),
        // The walkthrough's last step: a short song with its words and a singer line, as a new
        // document, to play in the preview — in a language the voices installed sing (firstSongFile).
        // The whole song where the computer has an NVIDIA GPU, its opening alone where not (a
        // voice on a processor takes minutes over half a minute of song); the two commands
        // after it are the step's way to the other one.
        vscode.commands.registerCommand('lilysharp.openFirstSong', () => openFirstSong(context)),
        vscode.commands.registerCommand('lilysharp.openFirstSongWhole', () => openFirstSong(context, true)),
        vscode.commands.registerCommand('lilysharp.openFirstSongShort', () => openFirstSong(context, false)),
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
        // File > New File…'s one Lily# entry (owner's decision 2026-11-26): a second picker, as
        // New File… cannot hold a submenu (menuContextCore.newScoreStarts).
        vscode.commands.registerCommand('lilysharp.newScoreMenu', async () => {
            const onWindows = process.platform === 'win32';
            const [omr, orecchiante] = onWindows ? [undefined, undefined] : await Promise.all([findOmr(context), findOrecchiante()]);
            const editTemplate: vscode.QuickInputButton = { iconPath: new vscode.ThemeIcon('gear'), tooltip: 'Edit New Score Template' };
            type Item = vscode.QuickPickItem & { command?: string };
            const items: Item[] = newScoreStarts(onWindows, omr !== undefined, orecchiante !== undefined).map(s => s.command
                ? { label: s.label, description: s.description, detail: s.detail, command: s.command, buttons: s.template ? [editTemplate] : undefined }
                : { label: s.label, kind: vscode.QuickPickItemKind.Separator });
            const pick = vscode.window.createQuickPick<Item>();
            pick.title = 'New Lily# Score';
            pick.placeholder = 'Where the piece starts';
            pick.items = items;
            const run = (command: string) => { pick.hide(); void vscode.commands.executeCommand(command); };
            pick.onDidTriggerItemButton(() => run('lilysharp.editNewScoreTemplate'));
            pick.onDidAccept(() => {
                const command = pick.selectedItems[0]?.command;
                if (command) { run(command); }
            });
            pick.onDidHide(() => pick.dispose());
            pick.show();
        }),
        vscode.commands.registerCommand('lilysharp.importMusicXml', (uri?: vscode.Uri) => {
            outputChannel.appendLine('importMusicXml command triggered');
            importMusicXml(context, uri);
        }),
        // The recording beside a chart (docs/recording-design.md): attached by a file beside the
        // chart, never by the .lys; renames keep the two together.
        vscode.commands.registerCommand('lilysharp.attachRecording', (uri?: vscode.Uri) =>
            attachRecording(uri instanceof vscode.Uri ? uri : undefined)),
        // Orecchiante writes the chart (and the file beside it) from a recording; without it the
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
        vscode.commands.registerCommand('lilysharp.previewStop', () =>
            postToActivePreview('ctxStop')),
        // The A-B repeat's right-click (docs/ab-repeat-design.md §1): the section right-clicked
        // is the webview's to know.
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
    // From a song to a dance (docs/song-to-dance-design.md): the walkthrough and its commands.
    registerSongToDance(context);
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

    // The singing voice (singing.ts): the `singer` lists, the settings a voice takes and their
    // warnings, the voice samples, and the voices each preview holds.
    registerSinging(context, {
        getClient: () => client,
        isReady: () => clientReady,
        outputChannel,
        previewPanels,
        previewPractice,
        selectedRenders,
        settings,
    });

    // The commands that rewrite the score (editCommands.ts): the grouping, the octaves, the sections,
    // a section's key, a chord track, a harmony part.
    registerEditCommands(context, {
        getClient: () => client,
        outputChannel,
        previewPanels,
        selectedRenders,
    });

    // Report to Orecchiante (orecchianteSeed.ts, Orecchiante's request 23): the owner's alone — its
    // code actions show only with lilysharp.orecchiante.seedFolder written by hand.
    registerOrecchianteReport(context, () => client, outputChannel);

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
                    const ceiling = cfg.get<number>('preview.refreshDelay', DEBOUNCE_DELAY_DEFAULT);
                    // As long as a refresh of this score takes, the setting at most (refreshDelay).
                    const delay = sessionOf(panel).refreshDelay(ceiling);

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
    // open opens a preview beside it (previewOnEditorShown; lilysharp.preview.openWithScore).
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
            openOnOpen: vscode.workspace.getConfiguration('lilysharp').get<boolean>('preview.openWithScore', true),
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

    // The menus' context keys: what the caret is on, what the file holds (menuContext.ts).
    registerMenuContexts(context);

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
                    // The caret alone: its chord, its chord(...) item or its token (caretHighlight).
                    panel.webview.postMessage(caretHighlight(doc.lineAt(active.line).text, active.character, doc.offsetAt(active)));
                }
            }
        })
    );

    outputChannel.appendLine('Lily# extension activated');

    // The Markdown lily# fence: VS Code's Markdown preview asks for this object
    // (contributes.markdown.markdownItPlugins) and wires the plugin into its engine.
    // Another extension reads the same object (vscode.extensions.getExtension('yotsuda.lilysharp')
    // .exports): `lyscPath` is the lysc beside the bundled server, for a program that reads songs
    // with it (`lysc timeline`/`notes`; Shosa's request 1, 2026-11-20). It checks what that lysc
    // speaks with `lysc --version --json`; undefined when this build bundles none.
    return {
        ...markdownItExtensionApi({
            getClient: () => client,
            isReady: () => clientReady,
            whenReady: ensureClientReady,
            log: (msg: string) => outputChannel.appendLine(msg),
        }),
        lyscPath: bundledLysc(),
        // The score's sound one stem at a time — its parts, its voices, the recording beside it —
        // for another extension to mix (docs/stems-api-design.md; Shosa's mixer, owner 2026-11-20).
        ...stemsApi({
            context, getClient: () => (clientReady ? client : undefined), whenReady: ensureClientReady,
            outputChannel, decode: decodeRecordingOnServer,
        }),
    };
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
    // The same answer's parts that sound a note go to the page with the length: the practice menu,
    // open, asks for its Parts list again when one is new (owner's report 2026-11-20) — read off
    // this export, which every refresh pays for already, so the render pays nothing for it. The
    // score's parts only: a part added to the file and not to its `score` (an extra) shows when
    // the menu is opened again — listing the extras here costs the server a second export at every
    // rest, for an edit that comes rarely (owner's decision 2026-11-20).
    let soundingCache: { key: string; notes: Promise<TimedNote[] | null | undefined>; parts: Promise<string[] | null | undefined> } | undefined;
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
                    const answer = (async () => {
                        for (let wait = soundingWait(lastEditAt.get(uri), Date.now()); wait > 0;
                            wait = soundingWait(lastEditAt.get(uri), Date.now())) {
                            await new Promise(r => setTimeout(r, wait));
                        }
                        if (versionNow() !== version) { return undefined; }
                        // (soundingOnly: the parts the score leaves out are dropped below anyway,
                        // and listing them costs the server a second export)
                        const pb = await client!.sendRequest<{ Notes?: TimedNote[] | null; Parts?: string[] | null; Extra?: string[] | null }>(
                            'lilysharp/playback', { textDocument: { uri }, renderName, soundingOnly: true });
                        if (!pb.Notes) { return null; }
                        const notes = soundingOf(pb.Notes, pb.Parts, pb.Extra);
                        return { notes, parts: soundingPartsOf(notes, pb.Parts) };
                    })();
                    const notes = answer.then(a => a === undefined ? undefined : a ? a.notes : null);
                    const parts = answer.then(a => a === undefined ? undefined : a ? a.parts : null);
                    // (a failed fetch is not kept: the next asks again)
                    answer.catch(() => { if (soundingCache?.key === key) { soundingCache = undefined; } });
                    parts.catch(() => undefined);
                    soundingCache = { key, notes, parts };
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
            if (message.type === 'webviewBoot') {
                // The page's script ran its first statement: a blank preview without this line is
                // a script that never started (CSP, a syntax error), with it something later.
                outputChannel.appendLine('Preview page started');
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
                // do not count). The webview repeats it at most every 5 s while the block lasts.
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
                // What an incremental update took of the page: half of what the next refresh
                // waits by (the other is the request's round trip, updatePreviewContent).
                if (typeof message.took === 'number' && message.whole === false) {
                    sessionOf(panel).pageTook(message.took);
                }
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
                // …and on the recording's seconds while it sounds with the score (Shosa's request
                // 4): turned on or off, or a bar line moved in it, the voice is sung again.
                const barsOf = (p: PracticeState | undefined) => p?.recording ? JSON.stringify(p.recording.bars) : '';
                if (now.speed !== was?.speed || barsOf(now) !== barsOf(was)) { void prepareVoices(panel, uri, 'speed'); }
                else if (onNow !== onBefore) { void prepareVoices(panel, uri, 'play'); }
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
            // A silent recording turned on in the mixer: its file forgets "silent" (docs/accompaniment-design.md).
            if (message.type === 'recordingSilent') {
                if (typeof message.chart === 'string') {
                    const chart = vscode.Uri.parse(message.chart);
                    if (chart.scheme === 'file') { void setRecordingSilent(chart.fsPath, message.silent === true); }
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
                    // (the score's parts that sound, from the same answer: soundingCache)
                    const parts = await soundingCache?.parts;
                    if (parts) { panel.webview.postMessage({ type: 'soundingParts', parts }); }
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

/** A file beside a chart written or removed (Attach Recording…, the bar marks, Orecchiante): the
 *  chart's open preview hears of it at once. */
function watchRecordings(context: vscode.ExtensionContext): vscode.Disposable {
    const watcher = vscode.workspace.createFileSystemWatcher('**/*.orecchiante.json');
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

/** Where the practice panel's folds are kept: the workspace's state. */
let panelFoldsStore: vscode.Memento | undefined;

/** The practice panel's folds kept for `uri` (owner's request 2026-11-17: per file; a file whose
 *  panel was never opened has none, and opens all folded): group key → folded. The key is the file's path, case aside, or the URI of a file not on disk. */
function panelFoldsKey(uri: string): string {
    const u = vscode.Uri.parse(uri);
    return 'lilysharp.panelFolds:' + (u.scheme === 'file' ? path.resolve(u.fsPath).toLowerCase() : uri);
}
function panelFoldsFor(uri: string): Record<string, boolean> | null {
    const kept = panelFoldsStore?.get<unknown>(panelFoldsKey(uri));
    if (!kept || typeof kept !== 'object') { return null; }
    const out: Record<string, boolean> = {};
    for (const [k, v] of Object.entries(kept as Record<string, unknown>)) { if (typeof v === 'boolean') { out[k] = v; } }
    return out;
}
function keepPanelFolds(uri: string, folds: unknown): void {
    if (!panelFoldsStore || !folds || typeof folds !== 'object') { return; }
    const out: Record<string, boolean> = {};
    for (const [k, v] of Object.entries(folds as Record<string, unknown>)) { if (typeof v === 'boolean' && k.length < 32) { out[k] = v; } }
    void panelFoldsStore.update(panelFoldsKey(uri), out);
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
        // (These two follow the file and the workspace, not the text: posted when they changed —
        // postIfChanged — and not with every keystroke's refresh.)
        postIfChanged(panel, { type: 'scanState', hasScan: hasScan(document.uri) });
        // The file's practice panel's folds (null: never opened for this file — it opens all folded).
        postIfChanged(panel, { type: 'panelFolds', chart: uri, folds: panelFoldsFor(uri) });
        // Another document or score: its voices replace the last one's, asked for once the server
        // has answered the render below (prepareVoices; singing-design §5).
        followVoices(panel, uri);
        // The chords of the text, for Select Chord on a member right-clicked (owner's request 2026-10-27).
        void panel.webview.postMessage({ type: 'chordSpans', spans: chordSpans(document.getText()) });
        // The recording beside the chart (docs/recording-design.md): sent when it or the chart changed.
        // A document with no file (untitled) has none: said, or the last chart's stays (owner's report 2026-11-20).
        if (document.uri.scheme === 'file') {
            void sendRecording(panel, document.uri, context.globalStorageUri, decodeRecordingOnServer);
        } else {
            sendNoRecording(panel, document.uri);
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
            // A delta that is shown: what an incremental refresh of this score costs, which the
            // next change's wait is read off (PreviewSession.refreshDelay).
            if (pages.BaseVersion != null) {
                session.tookRoundTrip(Date.now() - requestedAt);
                const ceiling = settings().get<number>('preview.refreshDelay', DEBOUNCE_DELAY_DEFAULT);
                const wait = session.refreshDelay(ceiling);
                if (saidRefreshDelay.get(panel) !== wait) {
                    saidRefreshDelay.set(panel, wait);
                    outputChannel.appendLine(`Preview refresh waits ${wait} ms after a change`
                        + ` (a refresh costs ${session.refreshCost === undefined ? 'not known yet' : Math.round(session.refreshCost) + ' ms'}, the ceiling is ${ceiling})`);
                }
            }
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
            flatSpelling: config.get<string>('editing.flatSpelling', 'full')
        },
        fonts: {
            paths: config.get<string[]>('fonts.folders', [])
        }
    };
}

export function deactivate(): Thenable<void> | undefined {
    // Clear all debounce timers
    previewPanels.forEach(panel => previewSessions.get(panel)?.cancelRefresh());
    // The singing engine, and its own server with it.
    deactivateSinging();

    if (!client) {
        return undefined;
    }
    return client.stop();
}


