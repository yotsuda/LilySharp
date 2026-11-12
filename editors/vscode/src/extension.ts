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
import { registerStaffGroupWrap, staffGroupsOffered } from './staffGroupWrap';
import { registerStepAudition } from './stepAudition';
import { registerCaretInfo } from './caretInfo';
import { registerExportBatch } from './exportBatch';
import { OmrImportDeps, importFromImage } from './omrImport';
import { registerOmrContexts } from './omrReader';
import { OmrScanDeps, hasScan, previewHoversBar, previewReady, previewShowsBar, registerScanFollow, scanBarAt, scanColumnOf, scanSideOf, scanThresholdOf, showScan } from './omrScan';
import { registerOmrAiFix } from './omrAiFix';
import { registerVerifyOnSave } from './omrVerify';
import { markdownItExtensionApi } from './markdownFence';
import { svgPostKey, pagesSummary, SvgPages, previewOnEditorShown, sourceGroupToReveal, newPreviewColumn, scoreGroupFor, sourceGroupToOpen, SourceTab, jumpOffsetFor, chordSpans, chordAtCaret } from './previewCore';
import { textFontFaceCss, textFontsRoot } from './scoreFonts';
import { PLAIN_TONES_JS } from './plainTones';
import { FOLLOW_SCROLL_CSS, FOLLOW_SCROLL_JS } from './followScroll';
import { SplitSectionsChoice, modalText, splitChoiceItems } from './splitSectionsCore';
import { onAngleChord, onShapeItem, shapeItemAt, sameDocumentUri } from './shapeItemCore';
import { TodoMark, parseStarts, todoDeletions } from './todoResolveCore';
import { cleanHistory, forgetFont, recordFont, samePath, shownFonts } from './soundFontHistoryCore';
import { AudioFormat, AudioJob, renderAudioFiles } from './audioRender';
import { VoiceTrack, decodeWav } from './audioRenderCore';
import { PracticeState, VOICE_LEAD_S, exportPlan, levelOf, practiceSummary, soundStart, voiceEntries } from './webview/playbackCore';
import { DrawFrames, FramesAnswer, ffmpegRuns, writeVideos } from './videoExport';
import { ffmpegMissing, ffmpegProgram } from './videoExportCore';
import { CantoriaExtensionBackend, SingerProcess, VoiceRenderer } from './singer';
import { onSingerGender, singerGoesOnToSings, singerLineOf, singerSettingSlot, singMarkSlot, singersWritten, singParameterDetail, singProblems, SingSettingDto, SingParameter, SingProgress, CANTORIA_EXTENSION_ID, PREPARE_IDLE_MS, PREPARE_TIMEOUT_MS, PrepareMode, PrepareQueue, StageClock, SungVoiceDto, sungKey, VOICE_RENDER_FIRST_RATE, VOICE_SAMPLE_VERSION, VoiceSampleQueue, byVoiceId, cantoriaApiOf, fitsGender, learnRenderRate, prepareModeOf, afterSingerWord, voiceMenuLabels, voiceMenuKey, voiceMenuTitle, noLanguageGuide, noVoiceGuide, printedWordsNote, singerChoices, VoiceGuide, singerLanguageColumn, singerValueAt, singsLanguage, stepVoice, sungSeconds, voiceRenderEstimateMs, voiceSampleFileName, voiceSampleKey, sampleOctave, parseVoiceSampleKey, voiceSampleScore, writesSinger } from './singerCore';

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
    const ready = panelReady.get(from);
    panelReady.delete(from);
    if (ready) { panelReady.set(to, ready); }
    // The page holds neither file's picture as far as the dedup and the page versions go.
    for (const key of [from, to]) {
        lastPostedSvg.delete(key);
        shownPagesVersion.delete(key);
    }
    cancelSvgRequest(from);
    svgRequestGeneration.delete(from);
    const timer = debounceTimers.get(from);
    if (timer) { clearTimeout(timer); debounceTimers.delete(from); }
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
// A message posted before the webview has finished loading its HTML is
// DROPPED by VS Code. The webview script posts 'webviewReady' as its last
// statement; content updates await that (with a timeout escape hatch).
const panelReady = new Map<string, { promise: Promise<void>; resolve: () => void }>();
function armPanelReady(uri: string) {
    let resolve!: () => void;
    const promise = new Promise<void>(r => { resolve = r; });
    panelReady.set(uri, { promise, resolve });
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
async function resolvePreviewTodos(starts: number[] | null): Promise<void> {
    const shown = [...previewPanels.entries()].find(([, p]) => p.active);
    if (!shown || (starts !== null && starts.length === 0)) {
        return;
    }
    const document = await vscode.workspace.openTextDocument(vscode.Uri.parse(shown[0]));
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

// Re-key a live preview from oldUri to newUri (an untitled score saved to a file):
// the panel and every per-document map entry move together so the preview keeps
// updating under the saved file's URI instead of the discarded untitled one.
function migratePreviewKey(oldUri: string, newUri: string) {
    const panel = previewPanels.get(oldUri);
    if (!panel || oldUri === newUri) { return; }
    previewPanels.delete(oldUri);
    previewPanels.set(newUri, panel);
    const ready = panelReady.get(oldUri);
    if (ready) { panelReady.delete(oldUri); panelReady.set(newUri, ready); }
    const render = selectedRenders.get(oldUri);
    if (render !== undefined) { selectedRenders.delete(oldUri); selectedRenders.set(newUri, render); }
    const posted = lastPostedSvg.get(oldUri);
    if (posted !== undefined) { lastPostedSvg.delete(oldUri); lastPostedSvg.set(newUri, posted); }
    // The server's session for the old URI is gone with its didClose; the new URI's starts
    // over and cannot recognise the version, so it is not worth carrying.
    shownPagesVersion.delete(oldUri);
    // Drop any pending debounce refresh: its callback captured the now-closed untitled
    // document, so letting it fire would render the wrong (stale) doc. A later edit to
    // the saved file schedules a fresh one under the new key.
    const timer = debounceTimers.get(oldUri);
    if (timer !== undefined) { clearTimeout(timer); debounceTimers.delete(oldUri); }
    // An in-flight svg request under the old URI can only answer about the closed
    // document — cancel it; the migrated panel's refresh starts fresh under the new key.
    cancelSvgRequest(oldUri);
    svgRequestGeneration.delete(oldUri);
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
let debounceTimers = new Map<string, NodeJS.Timeout>();
const outputChannel = vscode.window.createOutputChannel('Lily# Extension');

// Track selected render per document
const selectedRenders = new Map<string, string>();

// The last SVG (per URI) actually posted to a webview. The webview retains its content
// (retainContextWhenHidden), so re-posting a byte-identical SVG is pure waste — a toggle
// or save that recompiles to the same picture would otherwise push the whole (often
// hundreds-of-KB) string across the extension→webview channel again. Invalidated when the
// webview reloads (webviewReady) and when a panel is disposed / re-keyed.
const lastPostedSvg = new Map<string, string>();

// The page-set version (per URI) the webview holds — SvgPages.Version of the last page
// answer posted to it. Sent with the next lilysharp/svg request as shownVersion, so the
// server answers with only the pages that changed since (a delta against that version);
// the webview keeps or shifts the rest. Forgotten when the webview reloads (it holds
// nothing), when the webview asks for the whole picture (requestFull) and when a panel is
// disposed / re-keyed — a version the server does not recognise costs one full answer.
const shownPagesVersion = new Map<string, number>();

// One live lilysharp/svg request per URI. The generation stamps each request as it is
// sent; a response arriving after a newer request has been issued is DROPPED instead of
// painting an older picture over a newer one (responses can interleave now that the
// server dispatches requests off its read loop). The token source actively cancels the
// superseded request ($/cancelRequest), so the server abandons it at the render gate
// instead of spending a render on a thrown-away answer.
const svgRequestGeneration = new Map<string, number>();
const svgRequestCancellation = new Map<string, vscode.CancellationTokenSource>();

// Cancels (and disposes) the URI's in-flight svg request source, if any.
function cancelSvgRequest(uri: string) {
    const cts = svgRequestCancellation.get(uri);
    if (cts) { cts.cancel(); cts.dispose(); svgRequestCancellation.delete(uri); }
}

// Constants
// The preview's refresh debounce. `lilysharp.preview.refreshDelay` is no longer in the
// Settings UI (owner's decision 2026-10-25: a tuning value), so this is the delay unless
// settings.json still names one. It was 60.
const DEBOUNCE_DELAY_DEFAULT = 100;
const HIGHLIGHT_DISTANCE_THRESHOLD = 50;

export function activate(context: vscode.ExtensionContext) {
    outputChannel.appendLine('Lily# extension activating...');
    // The models that refused as VS Code's own, remembered from an earlier session (modelClient).
    rememberRefusalsIn(context.globalState);
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
        vscode.commands.registerCommand('lilysharp.splitSectionsToMatch',
            (uri?: string, section?: string) => splitSectionsToMatch(
                typeof uri === 'string' ? uri : undefined, typeof section === 'string' ? section : undefined)),
        vscode.commands.registerCommand('lilysharp.convertOctavesToAbsolute', () => convertOctaves(true)),
        vscode.commands.registerCommand('lilysharp.convertOctavesToRelative', () => convertOctaves(false)),
        vscode.commands.registerCommand('lilysharp.removeStringNumbers', () => removeStringNumbers()),
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
        // Audition: sound the note under the caret (held: the keybinding re-fires on
        // key-repeat, and the webview sustains until the repeats stop) or play the
        // whole measure the caret is in. The preview webview is the synth.
        vscode.commands.registerCommand('lilysharp.playNoteAtCursor', () =>
            playAtCursor(context, 'note')),
        vscode.commands.registerCommand('lilysharp.playMeasureAtCursor', () =>
            playAtCursor(context, 'measure')),
        // Preview context menu (menus/webview/context). Each one is a thin relay:
        // the state these act on — the right-clicked note, the zoom, the synth —
        // all lives in the webview, so the command only names the action.
        vscode.commands.registerCommand('lilysharp.previewPlayFromHere', () =>
            postToActivePreview('ctxPlayFromHere')),
        vscode.commands.registerCommand('lilysharp.previewStop', () =>
            postToActivePreview('ctxStop')),
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
        vscode.commands.registerCommand('lilysharp.previewResolveAllTodos', () => resolvePreviewTodos(null)));
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
        singJobs: (jobs, source, uri) => singJobs(jobs, source, uri ? practiceFor(uri) : undefined),
        practiceSummary: uri => uri ? practiceSummary(practiceFor(uri)) : '',
    });

    // Smart typing: the brackets it started with (`<` before c4 -> `<c>4`, and a
    // chord's '>' promoted to '>>' when its '<' is doubled into an arpeggio) plus
    // slurs, octave marks, beams, ties and durations — hence smartTyping, not
    // smartBrackets.
    registerSmartTyping(context, (msg: string) => outputChannel.appendLine(msg));
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
                    // Neither is in the Settings UI since 2026-10-25 (settings.json only).
                    const cfg = vscode.workspace.getConfiguration('lilysharp');
                    const autoRefresh = cfg.get<boolean>('preview.autoRefresh', true);
                    const delay = cfg.get<number>('preview.refreshDelay', DEBOUNCE_DELAY_DEFAULT);

                    if (autoRefresh) {
                        const existingTimer = debounceTimers.get(uri);
                        if (existingTimer) {
                            clearTimeout(existingTimer);
                        }
                        debounceTimers.set(uri, setTimeout(() => {
                            updatePreviewContent(event.document, panel, context);
                            debounceTimers.delete(uri);
                        }, delay));
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
function playAtCursor(context: vscode.ExtensionContext, mode: 'note' | 'measure') {
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
        const [rangeStart, rangeEnd] = measureRangeAt(doc.getText(), offset);
        panel.webview.postMessage({ type: 'playAtCursor', mode, rangeStart, rangeEnd });
    } else {
        const now = Date.now();
        const gapMs = now - lastPlayNoteFire;
        lastPlayNoteFire = now;
        panel.webview.postMessage({ type: 'playAtCursor', mode, position: offset, gapMs });
    }
}

/**
 * The source range [start, end) of the measure containing `offset`: the span
 * between the surrounding barlines (`|`), bounded by the enclosing music block
 * (`{`…`}`) so a measure never spills into another part/section. Good enough for
 * the common one-stream-per-line layout.
 */
function measureRangeAt(text: string, offset: number): [number, number] {
    const clamped = Math.max(0, Math.min(offset, text.length));
    let start = 0;
    for (let i = clamped - 1; i >= 0; i--) {
        const c = text[i];
        if (c === '|' || c === '{' || c === '}') { start = i + 1; break; }
    }
    let end = text.length;
    for (let i = clamped; i < text.length; i++) {
        const c = text[i];
        if (c === '|' || c === '{' || c === '}') { end = i; break; }
    }
    return [start, end];
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
    armPanelReady(uri);
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
            panelReady.delete(key);
            selectedRenders.delete(key);
            forgetVoices(panel);
            lastPostedSvg.delete(key);
            shownPagesVersion.delete(key);
            cancelSvgRequest(key);
            svgRequestGeneration.delete(key);
            const timer = debounceTimers.get(key);
            if (timer) {
                clearTimeout(timer);
                debounceTimers.delete(key);
            }
        }
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
    });
    panel.webview.onDidReceiveMessage(
        async message => {
            // The file the panel shows NOW: the follower turns, and an untitled save re-keys.
            const uri = panelKey(panel) ?? openedUri;
            // The notes of an AI candidate sounding now: lit on the candidate panel's score.
            if (message.type === 'candidateSounding') {
                showCandidateSounding(message.sounding);
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
                lastPostedSvg.delete(uri);
                shownPagesVersion.delete(uri);
                panelReady.get(uri)?.resolve();
                if (scanDeps) { previewReady(uri, scanDeps); }
                return;
            }
            if (message.type === 'requestFull') {
                // The webview could not apply a page delta to what it shows (its account
                // is in `why`): forget the version it was against and render again — the
                // server answers with every page.
                outputChannel.appendLine(`Webview asks for the whole picture: ${message.why}`);
                shownPagesVersion.delete(uri);
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
            if (message.type === 'requestPlayback') {
                // The webview cannot reach the LSP: fetch note events here and
                // hand them back for WebAudio scheduling.
                if (!client || !clientReady) {
                    panel.webview.postMessage({ type: 'playbackData', error: 'language server not running' });
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
                    const pb = await client.sendRequest<{ Notes?: { T: number, D: number, P: number, V: number }[], Error?: string,
                        Parts?: string[] | null, Extra?: string[] | null, Sung?: string[] | null, Played?: string[] | null,
                        Beats?: { T: number; A: boolean }[] | null; BarEnd?: number }>(
                        'lilysharp/playback', { textDocument: { uri }, renderName: selectedRenders.get(uri) || null });
                    // The tracks and the beats ride along for the practice menu's parts and metronome;
                    // the parts the score does not sound (Extra) too, off until the menu turns them on;
                    // and which parts the score sings and plays (docs/singing-design.md §3).
                    panel.webview.postMessage({ type: 'playbackData', notes: pb.Notes ?? null, error: pb.Error ?? null,
                        parts: pb.Parts ?? null, extra: pb.Extra ?? null, sung: pb.Sung ?? null, played: pb.Played ?? null,
                        beats: pb.Beats ?? null, barEnd: pb.BarEnd ?? null });
                    // The lyrics' voice, rendered now — a voice the text's rest had not yet sent to
                    // the engine goes at once — so the next Play finds it (docs/singing-design.md §5).
                    void prepareVoices(panel, uri, 'play');
                } catch (e) {
                    panel.webview.postMessage({ type: 'playbackData', error: String(e) });
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
                        voiceTitles = Object.fromEntries(singable.map(v => [sungKey(v), voiceMenuTitle(voiceLabels[sungKey(v)], v.Track, v.Part)]));
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
                    const pb = await client.sendRequest<{ Notes?: { T: number; D: number; N?: number }[] | null; Parts?: string[] | null; Extra?: string[] | null }>(
                        'lilysharp/playback', { textDocument: { uri }, renderName: selectedRenders.get(uri) || null });
                    const extraSet = new Set(pb.Extra ?? []);
                    const seconds = (pb.Notes ?? [])
                        .filter(n => !(pb.Parts && typeof n.N === 'number' && extraSet.has(pb.Parts[n.N])))
                        .reduce((end, n) => Math.max(end, n.T + n.D), 0);
                    panel.webview.postMessage({ type: 'lengthData', seconds: pb.Notes ? seconds : null });
                } catch {
                    panel.webview.postMessage({ type: 'lengthData', seconds: null });
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

async function updatePreviewContent(
    document: vscode.TextDocument,
    panel: vscode.WebviewPanel,
    context: vscode.ExtensionContext,
    retries: number = 8
) {
    const uri = document.uri.toString();
    const selectedRender = selectedRenders.get(uri);

    // A closed document can never be rendered — the server dropped it on didClose.
    // This happens for an untitled buffer just saved to a file (its URI is gone) or a
    // stale debounced refresh; skip instead of retrying a lookup that never succeeds.
    if (document.isClosed) {
        outputChannel.appendLine(`Skipping preview render for closed document ${uri}`);
        return;
    }

    outputChannel.appendLine(`updatePreviewContent called for ${uri}, clientReady=${clientReady}`);

    // Never post into a webview that has not loaded its HTML yet — VS Code
    // drops such messages silently and the panel sticks on "Loading…". The
    // 3 s race is a safety net against a webview that never reports in.
    const ready = panelReady.get(uri);
    if (ready) {
        await Promise.race([ready.promise, new Promise<void>(r => setTimeout(r, 3000))]);
    }
    // Whether the right-click menu offers the OMR doors (owner, 2026-10-24: not for a file
    // the reader did not write) — asked again with each render, as an import adds the file.
    void panel.webview.postMessage({ type: 'scanState', hasScan: hasScan(document.uri) });
    // Another document or score: its voices replace the last one's, asked for once the server
    // has answered the render below (prepareVoices; singing-design §5).
    followVoices(panel, uri);
    // The chords of the text, for Select Chord on a member right-clicked (owner's request 2026-10-27).
    void panel.webview.postMessage({ type: 'chordSpans', spans: chordSpans(document.getText()) });
    // The @todo marks of the text, for Resolve This TODO on the bar right-clicked and Resolve
    // All TODOs off the staves (owner's request 2026-10-31) — once the server is up to answer.
    void todoMarksOf(document).then(marks => {
        if (marks) {
            void panel.webview.postMessage({ type: 'todos', marks: marks.map(m => ({ start: m.Start, hostStart: m.HostStart })) });
        }
    });

    // Wait for client to be ready if not already
    if (!clientReady) {
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
    const generation = (svgRequestGeneration.get(uri) ?? 0) + 1;
    svgRequestGeneration.set(uri, generation);
    cancelSvgRequest(uri);
    const requestCancellation = new vscode.CancellationTokenSource();
    svgRequestCancellation.set(uri, requestCancellation);
    const lagProbe = new EventLoopLagProbe();
    lagProbe.start();
    try {
        // The version the webview holds when the request leaves; the answer is checked
        // against what it holds when the answer arrives (a reload in between blanks it).
        const shownVersion = shownPagesVersion.get(uri) ?? null;
        const response = await client.sendRequest<SvgResponse>('lilysharp/svg', {
            textDocument: { uri: uri },
            renderName: selectedRender || null,
            clientSentAt: requestedAt,
            pageDiff: true,
            shownVersion
        }, requestCancellation.token);
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
        if (svgRequestGeneration.get(uri) !== generation) {
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
            const held = shownPagesVersion.get(uri) ?? null;
            if (pages.BaseVersion != null && pages.BaseVersion !== held) {
                outputChannel.appendLine(`Page delta is against version ${pages.BaseVersion}, the webview holds `
                    + `${held}; asking for every page`);
                shownPagesVersion.delete(uri);
                if (retries > 0) {
                    setTimeout(() => updatePreviewContent(document, panel, context, retries - 1), 0);
                }
                return;
            }
            // A page answer is a few hundred bytes when nothing changed, so it is posted
            // every time (the picker's list and the banner ride with it); the one-string
            // dedup memory no longer describes what the webview shows.
            lastPostedSvg.delete(uri);
            shownPagesVersion.set(uri, pages.Version);
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
            if (lastPostedSvg.get(uri) === key) {
                outputChannel.appendLine(`SVG unchanged (length=${response.Svg.length}), skipping post`);
            } else {
                outputChannel.appendLine(`Sending SVG to webview (length=${response.Svg.length}`
                    + `${response.Error ? ', with error banner' : ''})`);
                lastPostedSvg.set(uri, key);
                // A one-string picture is not a version the next delta can be against.
                shownPagesVersion.delete(uri);
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
            lastPostedSvg.delete(uri);
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
        if (svgRequestGeneration.get(uri) !== generation
            || requestCancellation.token.isCancellationRequested) {
            outputChannel.appendLine(`Svg request cancelled by a newer one: ${error}`);
            return;
        }
        outputChannel.appendLine(`Request failed: ${error}`);
        if (previewPanels.has(uri)) {
            // Same reason as the error branch above: an error replaces the SVG on screen, so
            // drop the cache or the next identical render is skipped and the error persists.
            lastPostedSvg.delete(uri);
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
            const jobs = answer.Success && answer.Playbacks ? await singJobs(practisedJobs(answer.Playbacks, practice), { textDocument: { uri } }, practice) : null;
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
        const response = await client.sendRequest<RegroupResponse>('lilysharp/regroup', {
            textDocument: { uri: doc.uri.toString() }
        });
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
        const response = await client.sendRequest<ConvertOctavesResponse>('lilysharp/convertOctaves', {
            textDocument: { uri: doc.uri.toString() },
            absolute
        });
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
        const response = await client.sendRequest<RemoveStringNumbersResponse>('lilysharp/removeStringNumbers', {
            textDocument: { uri: doc.uri.toString() }
        });
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
        const response = await client.sendRequest<ExtractPhraseResponse>('lilysharp/extractPhrase', {
            textDocument: { uri: doc.uri.toString() },
            selectionStart: doc.offsetAt(sel.start),
            selectionEnd: doc.offsetAt(sel.end),
            name,
        });
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
                const set = await client!.sendRequest<SetSectionKeyResponse>('lilysharp/setSectionKey', {
                    textDocument: { uri: doc.uri.toString() },
                    section: section.Section,
                    key: picked.key,
                });
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

    const response = await client!.sendRequest<RespellToKeyResponse>('lilysharp/respellToKey', {
        textDocument: { uri: doc.uri.toString() }, section, key,
        accepted: chosen.map(c => c.id),
    });
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
            filters: { 'MusicXML': ['xml', 'musicxml', 'mxl'] },
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
                path: fileUri.path.replace(/\.(xml|musicxml|mxl)$/i, '') + '.lys',
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
        const plan = exportPlan(job.Notes, job.Parts ?? null, job.Beats ?? null, practice, job.Extra ?? null);
        return { ...job, Notes: plan.notes, Lead: plan.lead };
    });
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
                if (off >= 0) { silenced.add(off); }
            }
            if (voice.Notes.length === 0 || !sings || gain <= 0) { continue; }
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
                outputChannel.appendLine(`singer: export: the voice of ${voice.Part} was not sung — ${String(e)}; its instrument plays`);
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
        const notes = silenced.size > 0 ? job.Notes.filter(n => !(typeof n.N === 'number' && silenced.has(n.N))) : job.Notes;
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
        // At the top level, a voice taken goes on to `sings` and the list of the file's lyrics
        // (owner's request 2026-11-11): the name is closed, ` sings ` written after it and the
        // list asked again — the server's, which names the lyrics there. The closing quote is
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
                item.insertText = new vscode.SnippetString().appendText(`${slot.Quoted ? '' : '"'}${c.label}" sings `).appendTabstop(0);
                item.command = { command: 'lilysharp.singerTaken', title: 'Hear this voice, and list the lyrics', arguments: [c.label, nameColumn] };
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
        const named = /^[ \t]*singer[ \t]+(?:(male|female)[ \t]+)?(?:"([^"]*)")?/.exec(before);
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
        if (slot.kind === 'value') {
            // A number is the writer's to type; a setting that takes a NAME lists its names.
            const names = taken.find(p => p.name === slot.of)?.values ?? [];
            return names.map((name, i) => {
                const item = new vscode.CompletionItem(name, vscode.CompletionItemKind.Value);
                item.sortText = String(i).padStart(3, '0');
                return item;
            });
        }
        if (slot.kind === 'with') {
            if (taken.length === 0) { return undefined; }
            // Whose settings they are is said: a line that names no voice sings with a default one.
            const item = new vscode.CompletionItem({ label: 'with', description: voice }, vscode.CompletionItemKind.Keyword);
            item.detail = `How ${voice} sings: ${taken.map(p => p.name).join(', ')}`;
            item.insertText = 'with ';
            item.sortText = '~with';     // after what the line still needs (`on`)
            item.command = { command: 'editor.action.triggerSuggest', title: 'List the voice\'s settings' };
            return [item];
        }
        return taken.filter(p => !slot.written.includes(p.name)).map((p, i) => {
            const item = new vscode.CompletionItem({ label: p.name, description: voice }, vscode.CompletionItemKind.Property);
            item.detail = singParameterDetail(p);
            item.insertText = p.name + ' ';
            item.filterText = p.name;
            item.sortText = String(i).padStart(3, '0');
            return item;
        });
    },
};

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
        if (slot.kind === 'value') {
            const back = new vscode.CompletionItem('default', vscode.CompletionItemKind.Keyword);
            back.detail = `${slot.of} as the singer line has it again (or as the voice does by itself)`;
            const to = new vscode.CompletionItem('to', vscode.CompletionItemKind.Keyword);
            to.detail = `Arrive at the value written after it with this note, gliding from the ${slot.of} before`;
            to.insertText = 'to ';
            return [back, to];
        }
        const engine = await renderer();
        const backend = engine?.backend instanceof CantoriaExtensionBackend ? engine.backend : undefined;
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
function offerVoiceSetup(guide: VoiceGuide): void {
    if (voiceSetupOffered.has(guide.message)) { return; }
    voiceSetupOffered.add(guide.message);
    void showVoiceSetup(guide);
}

/** What would give a voice, as buttons on Cantoria's own commands. */
async function showVoiceSetup(guide: VoiceGuide): Promise<void> {
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
    return vscode.workspace.getConfiguration('lilysharp').get<string>('playback.singer', '').trim();
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
    } else {
        const ext = vscode.extensions.getExtension(CANTORIA_EXTENSION_ID);
        if (!ext) { return undefined; }
        try {
            const api = cantoriaApiOf(ext.isActive ? ext.exports : await ext.activate());
            if (!api) { outputChannel.appendLine(`singer: ${CANTORIA_EXTENSION_ID} is installed but its API is not the shape Lily# reads`); return undefined; }
            voiceRenderer = new VoiceRenderer(new CantoriaExtensionBackend(api, tellSingerNote));
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
    return prepareModeOf(vscode.workspace.getConfiguration('lilysharp').get<string>('playback.singerPrepare'));
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
function failVoice(panel: vscode.WebviewPanel, part: string, speed: number, why: string): void {
    tellSingerOnce(panel, `the voice of ${part} was not sung — ${why} (see Lily# Extension output)`);
    void panel.webview.postMessage({ type: 'voiceState', part, speed, state: 'failed', detail: why });
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
    for (const voice of active) {
        for (const warning of voice.Warnings ?? []) { tellSingerOnce(panel, `${voice.Part}: ${warning}`); }
        const id = sungKey(voice);
        const slot = `${panelId(panel)}/${id}`;
        let key: string;
        try {
            key = await engine.key(voice, speed);
        } catch (e) {
            failVoice(panel, id, speed, String(e));
            continue;
        }
        if (!current()) { return; }
        if (mine.get(id) === key) { prepareQueue.cancelSlot(slot); continue; }   // the page has this text's voice; a job of an older text can go
        mine.delete(id);
        const rests = reason === 'edit' && !turned && !engine.has(key);
        if (rests && mode === 'onPlay') {
            void panel.webview.postMessage({ type: 'voiceState', part: id, speed, state: 'waiting', estimateMs: voiceEstimateMs(voice, speed) });
            prepareQueue.cancelSlot(slot);
            continue;
        }
        void panel.webview.postMessage({ type: 'voiceState', part: id, speed, state: rests ? 'waiting' : 'preparing', estimateMs: voiceEstimateMs(voice, speed), key });
        const busy = prepareQueue.busy(slot);
        prepareQueue.schedule(slot, { panel, voice, speed, mine, key, letGo: new AbortController() }, rests ? PREPARE_IDLE_MS : 0);
        log(`${id}: ${rests ? `waits for the text to rest ${PREPARE_IDLE_MS} ms` : 'goes now'}${busy ? ', after what is being sung lets go' : ''}`);
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
    const onProgress = (p: SingProgress) => {
        told = p;
        if (!wanted()) { return; }
        void panel.webview.postMessage({ type: 'voiceProgress', part: id, key: job.key, toSing: p.toSing, sung: p.sung, msPerSecond: voiceRenderRate });
    };
    try {
        outputChannel.appendLine(`singer: ${clockNow()} ${id}: to the engine`);
        ({ key, rendered, cached } = await engine.render(voice, speed, job.letGo.signal, onProgress));
    } catch (e) {
        if (wanted()) { failVoice(panel, id, speed, String(e)); }
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
            // entries: where the voice may come in if its part is already playing (joinVoice).
            void panel.webview.postMessage({ type: 'voice', part: id, key, speed, lead: VOICE_LEAD_S, bytes: new Uint8Array(bytes), entries: voiceEntries(voice.Notes) });
            outputChannel.appendLine(`singer: ${clockNow()} ${id} ${cached ? 'from the cache' : 'sung'} (${v.duration.toFixed(1)} s at speed ${speed})`);
        } catch (e) {
            if (wanted()) { failVoice(panel, id, speed, e instanceof Error ? e.message : String(e)); }
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
    const out: CandidateVoices = { voices: [], silenced: [], waiting: [] };
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
    for (const voice of candidateSings(sung.Voices, sounds)) {
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
    };
}

/** The preview's color scheme, as the `lilysharp.preview.theme` setting spells it:
 *  `auto` (follow VS Code's theme — the webview's prefers-color-scheme tracks it),
 *  `light` or `dark`. Anything else reads as `auto`. */
function getPreviewTheme(): 'auto' | 'light' | 'dark' {
    const v = vscode.workspace.getConfiguration('lilysharp').get<string>('preview.theme', 'auto');
    return v === 'light' || v === 'dark' ? v : 'auto';
}

/** The URLs of the preview's SoundFont synth, as the webview reaches them (synthUrls). */
interface SynthUrls { script: string; processor: string; soundFont: string }

// The CSP: the page's own scripts by nonce; the synth bundle, its AudioWorklet
// processor (fetched by audioWorklet.addModule — a worklet module answers to
// script-src) and the fonts from the extension's resources; the processor's Vorbis
// decoder is WebAssembly ('wasm-unsafe-eval'), and previewSynth.ts re-adds the processor
// from a blob when the host will not hand a worklet a cross-origin module; the sound
// font is fetch()ed (connect-src).
function getPreviewHtml(fontUri: string, braceFontUri: string, cspSource: string, nonce: string,
                        theme: 'auto' | 'light' | 'dark', textFontCss: string,
                        synth: SynthUrls = { script: '', processor: '', soundFont: '' },
                        midiOutput: string = ''): string {
    return `<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; font-src ${cspSource}; script-src 'nonce-${nonce}' ${cspSource} 'wasm-unsafe-eval' blob:; connect-src ${cspSource};">
    <script nonce="${nonce}">
        // The color scheme is a class on <html>, set HERE — before the body parses —
        // so a dark preview never flashes white on open. 'auto' follows VS Code's
        // theme through prefers-color-scheme (the webview mirrors the editor theme);
        // the main script below re-applies it when the setting or the theme changes.
        (function () {
            const mode = '${theme}';
            const dark = mode === 'dark'
                || (mode === 'auto' && window.matchMedia('(prefers-color-scheme: dark)').matches);
            document.documentElement.classList.toggle('theme-dark', dark);
        })();
        // The SoundFont synth's files (docs/playback-design.md): the worklet processor
        // and the sound font ('' = none, the page then plays its oscillator patches); and
        // the MIDI output port's name ('' = the synth here), lilysharp.playback.midiOutput.
        window.lysSynthConfig = { processor: '${synth.processor}', soundFont: '${synth.soundFont}', midiOutput: ${JSON.stringify(midiOutput)} };
    </script>
    <script nonce="${nonce}" src="${synth.script}"></script>
    <style>
        @font-face {
            font-family: 'Emmentaler';
            src: url('${fontUri}') format('woff2');
        }
        @font-face {
            font-family: 'Emmentaler-Brace';
            src: url('${braceFontUri}') format('woff2');
        }
        /* The score's text faces — the ones the layout measured (scoreFonts.ts). */
        ${textFontCss}
        body {
            margin: 0;
            padding: 0;
            background: white;
            display: flex;
            flex-direction: column;
            /* The score is not selectable text. Without this, clicking a note
               starts a text selection of the nearest SVG <text> glyph (a note-
               head / accidental), which Chromium paints as a grey rectangle over
               the neighbouring note. */
            user-select: none;
            -webkit-user-select: none;
            /* HEIGHT, not min-height: min lets the body grow with the sheet,
               so the WEBVIEW scrolls and .main-content never overflows —
               scrollTop stays 0 and the page-nav buttons do nothing. Capping
               at the viewport makes .main-content the (only) scroller. */
            height: 100vh;
            overflow: hidden;
        }
        .toolbar {
            padding: 8px 12px;
            background: #f0f0f0;
            border-bottom: 1px solid #ddd;
            display: flex;
            align-items: center;
            gap: 8px;
            flex-shrink: 0;
            /* A narrow preview wraps the header onto a second row; its buttons keep their size
               (owner's request 2026-10-25: they were squeezed). */
            flex-wrap: wrap;
            row-gap: 6px;
            /* Stay pinned to the top while the sheet scrolls underneath. */
            position: sticky;
            top: 0;
            z-index: 10;
        }
        .toolbar > * { flex-shrink: 0; }
        .toolbar .group { display: flex; align-items: center; gap: 8px; }
        .toolbar label {
            font-family: system-ui, sans-serif;
            font-size: 13px;
            color: #333;
        }
        .toolbar select {
            padding: 4px 8px;
            font-size: 13px;
            border: 1px solid #ccc;
            border-radius: 4px;
            background: white;
            /* No min-width: the width is measured from the selected score name
               and set inline (fitSelectToSelection). A floor here would win over
               that inline width and put the empty gap back. */
        }
        .toolbar button {
            padding: 4px 12px;
            font-size: 13px;
            font-family: system-ui, sans-serif;
            border: 1px solid #ccc;
            border-radius: 4px;
            background: white;
            color: #333;
            cursor: pointer;
        }
        .toolbar button:hover {
            background: #e8e8e8;
        }
        .toolbar button:disabled {
            opacity: 0.5;
            cursor: default;
        }
        .toolbar .sep {
            width: 1px;
            height: 20px;
            background: #ccc;
        }
        #pageInput {
            /* No width here: updatePageInfo sizes it to the page count's digits. */
            padding: 3px 1px 3px 4px;
            font-family: system-ui, sans-serif;
            font-size: 13px;
            text-align: right;
            border: 1px solid #ccc;
            border-radius: 4px;
            background: white;
            color: #333;
        }
        #pageInput:disabled {
            opacity: 0.5;
        }
        #pageCount {
            font-family: system-ui, sans-serif;
            font-size: 13px;
            color: #333;
            white-space: nowrap;
        }
        /* Dark scheme: :root.theme-dark, not a prefers-color-scheme media query — the
           class is the lilysharp.preview.theme setting resolved (see the head script),
           which is how "always light" / "always dark" can override the editor theme. */
        :root.theme-dark .toolbar .sep { background: #555; }
        :root.theme-dark #pageCount { color: #ccc; }
        :root.theme-dark #pageInput {
            background: #3c3c3c;
            color: #ccc;
            border-color: #555;
        }
        .main-content {
            flex: 1;
            /* A flex item defaults to min-height:auto, which refuses to shrink
               below its content and pushes the whole page (and the toolbar) into a
               window-level scroll. min-height:0 lets THIS pane own the scroll. */
            min-height: 0;
            padding: 20px;
            display: flex;
            justify-content: center;
            align-items: flex-start;
            overflow: auto;
            /* The sheet is dragged to scroll (installDragScroll): an open hand over it, a closed
               one while it moves, and the pointer over what a click jumps to. */
            cursor: grab;
        }
        .main-content.dragging,
        .main-content.dragging [data-pos] {
            cursor: grabbing;
        }
        .main-content [data-pos],
        .main-content.over-bar {
            cursor: pointer;
        }
        /* The bar a click would go to (barStaffAt), its staff tinted in the colour the scan view
           lights a bar in (omrScan.ts rect.light; the owner's request 2026-10-14: the two alike). */
        #svgContainer rect.bar-staff.hover,
        #svgContainer rect.bar-staff.peer,
        #svgContainer rect.bar-staff.caret {
            fill: rgba(0, 120, 215, 0.14);
        }
        /* The bars a score selection covers (owner's request 2026-10-25: the bars lit, not the
           notes — the editor's text is not selected, so lit notes would read as if it were). */
        #svgContainer rect.bar-staff.selected {
            fill: rgba(0, 120, 215, 0.22);
        }
        .container {
            max-width: 100%;
        }
        #svgContainer {
            transform-origin: top left;
            transition: transform 0.1s ease-out;
        }
        #svgContainer svg {
            display: block;
        }
        /* The pages as sheets (owner's request 2026-10-25): the server leaves a gap of nothing
           between two pages (SvgDocumentContext.InteractivePageGap), and the pane shows dark
           through it and around the sheet — once a score is shown (a "Loading" line keeps the
           plain background it is readable on). */
        .main-content:has(#svgContainer > svg) {
            background: #2b2b2b;
        }
        /* Dark: the SVG paints its page white and the scheme inverts it (filter below), so the
           sheet is black — black around it would not show; mid grey does. */
        :root.theme-dark .main-content:has(#svgContainer > svg) {
            background: #3c3c3c;
        }
        .zoom-info {
            position: fixed;
            bottom: 10px;
            right: 10px;
            background: rgba(0,0,0,0.7);
            color: white;
            padding: 4px 8px;
            border-radius: 4px;
            font-family: monospace;
            font-size: 12px;
            opacity: 0;
            transition: opacity 0.3s;
        }
        .zoom-info.visible {
            opacity: 1;
        }
        .highlight {
            filter: drop-shadow(0 0 4px #ff6600);
        }
        /* A head or rest marked @todo wears a ring (the SVG's ellipse.todo-ring, drawn by
           the interactive SVG alone), and a memo stands in a callout box with a leader to
           the ring — both stamped data-todo, both painted BY THE SVG ITSELF (fill="none"
           and a stroke on the element): the caret highlight (highlightPositions) reads a
           shape's attributes to decide whether to recolour its fill or its stroke, and a
           ring styled only from here had no stroke attribute, so the highlight filled it
           into a disc (owner's report 2026-10-10). No paint here, then. The preview always
           shows them (owner's decision 2026-10-25: the setting that hid them is gone).
           Until 2026-10-10 the head itself was filled red (owner: "赤くつぶれる").
           The dark scheme's invert + hue-rotate(180deg) brings the red back to red. */
        /* A staff's bar an OMR reader flagged (the scan view's B4), framed as on the scan: the
           frame applyBarFlags draws round it, two staff spaces taller above and below. */
        #svgContainer path.flag-frame {
            stroke: #e53935;
            stroke-width: 2;
            stroke-dasharray: 4 3;
            vector-effect: non-scaling-stroke;
        }
        .error {
            color: #f44336;
            background: #ffebee;
            padding: 16px;
            border-radius: 4px;
            white-space: pre-wrap;
            font-family: monospace;
        }
        .loading {
            color: #666;
            font-family: system-ui, sans-serif;
        }
        /* Non-destructive error surface: an overlay pinned to the BOTTOM of the
           preview so a transient syntax error / warning while typing neither blanks
           the score nor reflows it. (As an in-flow banner above .main-content it
           shrank the scroller on every appear/disappear, jittering the sheet up and
           down.) position:fixed takes it out of flow → the sheet never moves. */
        .error-banner {
            display: none;
            position: fixed;
            left: 0;
            right: 0;
            bottom: 0;
            z-index: 20;
            color: #f44336;
            background: #ffebee;
            border-top: 1px solid rgba(244, 67, 54, 0.5);
            padding: 6px 12px;
            white-space: pre-wrap;
            font-family: monospace;
            font-size: 12px;
            max-height: 6em;
            overflow-y: auto;
        }
        .error-banner.visible {
            display: block;
        }
        /* The last good preview stays but dims while the current source is invalid. */
        #svgContainer.stale {
            opacity: 0.45;
            transition: opacity 0.15s;
        }
        :root.theme-dark body {
            background: #1e1e1e;
        }
        :root.theme-dark .toolbar {
            background: #2d2d2d;
            border-bottom-color: #444;
        }
        :root.theme-dark .toolbar label {
            color: #ccc;
        }
        :root.theme-dark .toolbar select {
            background: #3c3c3c;
            color: #ccc;
            border-color: #555;
        }
        :root.theme-dark #svgContainer svg {
            filter: invert(1) hue-rotate(180deg);
        }
        :root.theme-dark .highlight {
            filter: drop-shadow(0 0 4px #00ccff);
        }
        :root.theme-dark .error {
            background: #4a1515;
            color: #ff8a80;
        }
        :root.theme-dark .error-banner {
            background: #4a1515;
            color: #ff8a80;
            border-top-color: rgba(255, 138, 128, 0.4);
        }
        :root.theme-dark .loading {
            color: #aaa;
        }
        /* M3: floating "transform with AI" action, shown while a score note range
           is selected. */
        /* The header's AI door (owner's request 2026-10-25: always there; it names what it hands
           the AI — the whole score, or the bars selected). */
        .toolbar button.ai {
            font-weight: 600;
            background: var(--vscode-button-background);
            color: var(--vscode-button-foreground);
        }
        .toolbar button.ai:hover { background: var(--vscode-button-hoverBackground); }
        /* Pressed, it sinks (owner's request 2026-10-29: a click on ✨ showed nothing — the chat
           opens beside, out of the eye's way): shaded in, nudged down, darker. */
        .toolbar button.ai:active, .toolbar button.ai.pressed {
            box-shadow: inset 0 2px 4px rgba(0, 0, 0, 0.45);
            transform: translateY(1px);
            filter: brightness(0.85);
        }
        /* A toggle that is on looks pressed in (owner's request 2026-10-25: Range read as a plain
           button): sunk, blue-edged, tinted. */
        .toolbar button.toggle[aria-pressed="true"] {
            background: #d6e6f8; border-color: #0078d7; color: #0b4a8b;
            box-shadow: inset 0 1px 3px rgba(0, 0, 0, 0.3);
        }
        .toolbar button.toggle[aria-pressed="true"]:hover { background: #c8ddf5; }
        /* The practice menu open looks as the Range panel open does (owner's request 2026-11-02). */
        .toolbar button.practice[aria-expanded="true"] {
            background: #d6e6f8; border-color: #0078d7; color: #0b4a8b;
            box-shadow: inset 0 1px 3px rgba(0, 0, 0, 0.3);
        }
        .toolbar button.practice[aria-expanded="true"]:hover { background: #c8ddf5; }
        /* An icon button (Range: a double-headed arrow just left of an eighth note — owner's
           request 2026-10-25: stemless heads read as no note, and a quarter as a tempo) is as
           big as the emoji one (✨). */
        /* Both are one height and hold their glyph in the middle (owner's report 2026-10-25: the
           icon sat off centre). */
        .toolbar button.icon {
            display: inline-flex; align-items: center; justify-content: center;
            height: 28px; width: 42px; padding: 0; line-height: 1;
        }
        .toolbar button.icon svg { display: block; width: 15px; height: 15px; }
        /* The voice badge: a small microphone over the bottom-right corner of ▶ — no border, no
           background, not a button. Grey and pulsing while the engine sings, blue when every voice
           is ready, the error colour when one will not be sung. */
        .toolbar .play-wrap { position: relative; display: inline-flex; }
        .voice-badge {
            position: absolute; right: -5px; bottom: -5px; width: 15px; height: 15px; z-index: 1;
            display: inline-flex; align-items: center; justify-content: center;
            border-radius: 50%; background: white; padding: 1px; box-sizing: border-box; color: #888;
            cursor: pointer; line-height: 0;
        }
        /* The display rule above beats the hidden attribute's own display:none (the range panel and
           the tip say this too): without this line the badge never hid, and kept the look of its
           last state - the pulsing placeholder - on a file with no lyrics (owner's reports, sessions
           1160-1162; the host and the page had the state right all along). */
        .voice-badge[hidden] { display: none; }
        .voice-badge svg { width: 12px; height: 12px; display: block; }
        /* The voices' progress (owner's requests 2026-11-09, sessions 1178 and 1179): a thin bar under
           Play while the engine sings, running in under the microphone, its goal. Counted on what
           the engine says it has to sing anew (voiceBar), an estimate where it says nothing. Its
           right end stops a little short of the microphone's (owner's request 2026-11-11: 6px was
           a touch long). */
        .toolbar .voice-bar {
            position: absolute; left: 0; right: 8px; bottom: -5px; height: 3px;
            border-radius: 2px; background: rgba(128, 128, 128, 0.3); overflow: hidden; pointer-events: none;
        }
        .toolbar .voice-bar[hidden] { display: none; }
        .toolbar .voice-bar > span { display: block; height: 100%; width: 0; background: #0078d7; transition: width 0.15s linear; }
        :root.theme-dark .toolbar .voice-bar > span { background: #4fa3ff; }
        /* The same bar on a voice's line in the practice menu, running in under its microphone. */
        .voice-row-bar {
            position: absolute; left: 2px; right: 10px; bottom: 1px; height: 2px;
            border-radius: 1px; background: rgba(128, 128, 128, 0.3); overflow: hidden; pointer-events: none;
        }
        .voice-row-bar[hidden] { display: none; }
        .voice-row-bar > span { display: block; height: 100%; width: 0; background: #0078d7; transition: width 0.15s linear; }
        :root.theme-dark .voice-row-bar > span { background: #4fa3ff; }
        .voice-badge[data-state="waiting"] { color: #888; }
        .voice-badge[data-state="preparing"] { color: #888; animation: voice-pulse 1.2s ease-in-out infinite; }
        .voice-badge[data-state="ready"] { color: #0078d7; }
        .voice-badge[data-state="failed"] { color: var(--vscode-errorForeground, #b00020); }
        :root.theme-dark .voice-badge { background: #2d2d2d; }
        :root.theme-dark .voice-badge[data-state="ready"] { color: #4fa3ff; }
        @keyframes voice-pulse { 0%, 100% { opacity: 0.3; } 50% { opacity: 1; } }
        /* How long the score plays (owner's request 2026-10-25), beside the transport. */
        .toolbar .length {
            font-family: system-ui, sans-serif; font-size: 12px; color: #555;
            font-variant-numeric: tabular-nums;
        }
        /* The button that brings a Play's view back to the notes sounding (followScroll.ts). */
        ${FOLLOW_SCROLL_CSS}
        /* The Range panel (owner's request 2026-10-25): each part's lowest and highest note over
           the score, top right under the toolbar; a click on one shows it on the score. */
        .range-panel {
            /* Under the toolbar's 10: a panel dragged up goes behind the header, which stays whole
               (owner's request 2026-11-02). */
            position: fixed; right: 12px; z-index: 5;
            /* Its own width wherever it stands: left to the browser, a panel dragged towards the
               right edge narrowed to what was left of the window and its contents wrapped
               (owner's report 2026-11-10). */
            width: max-content; max-width: calc(100vw - 12px); box-sizing: border-box;
            background: var(--vscode-editorWidget-background, #f3f3f3);
            color: var(--vscode-editorWidget-foreground, inherit);
            border: 1px solid var(--vscode-editorWidget-border, #c8c8c8);
            border-radius: 4px; box-shadow: 0 2px 8px rgba(0, 0, 0, 0.25);
            padding: 6px 8px; font-size: 12px; max-height: 60vh; overflow: auto;
        }
        .range-panel[hidden] { display: none; }
        /* Dragged anywhere but its inputs (owner's request 2026-10-29), its buttons and cells
           still clicked; nothing selects while it moves. */
        .range-panel { cursor: move; }
        .range-panel button, .range-panel input, .range-panel td { cursor: pointer; }
        .range-panel.moving, .range-panel.moving * { user-select: none; cursor: move; }
        /* The × at the top right (owner's request 2026-10-29), kept there as the panel scrolls. */
        .range-panel .panel-close {
            float: right; position: sticky; top: 0; z-index: 1; margin: -4px -6px 0 8px;
            width: 20px; height: 20px; padding: 0; line-height: 18px; text-align: center;
            font-size: 15px; border: none; border-radius: 3px; background: transparent; color: inherit; opacity: 0.7;
        }
        .range-panel .panel-close:hover { opacity: 1; background: rgba(128, 128, 128, 0.25); }
        /* The page's tooltip, in VS Code's hover look (the browser's title tip is too small). */
        .tip {
            position: fixed; z-index: 100; max-width: 320px; pointer-events: none;
            padding: 4px 8px; border-radius: 3px;
            font-family: var(--vscode-font-family, system-ui, sans-serif);
            font-size: var(--vscode-font-size, 13px); line-height: 1.4;
            background: var(--vscode-editorHoverWidget-background, #f3f3f3);
            color: var(--vscode-editorHoverWidget-foreground, #333);
            border: 1px solid var(--vscode-editorHoverWidget-border, #c8c8c8);
            box-shadow: 0 2px 8px rgba(0, 0, 0, 0.18);
            white-space: pre-line; overflow-wrap: anywhere;
        }
        .tip[hidden] { display: none; }
        /* The practice menu (owner's request 2026-10-25): the button opens it under itself; its
           rows are the tempo, the metronome and the parts. */
        /* An icon button as Range is (owner's request 2026-11-02), wider only while it shows the
           tempo it plays at. */
        .toolbar button.practice {
            width: auto; min-width: 42px; padding: 0 8px; gap: 3px;
            font-variant-numeric: tabular-nums; font-size: 12px;
        }
        .toolbar button.practice #practiceSpeed:empty { display: none; }
        .practice-panel .prow { display: flex; align-items: flex-start; gap: 10px; padding: 4px 2px; }
        .practice-panel .plbl { width: 72px; flex-shrink: 0; opacity: 0.75; font-weight: 600; padding-top: 3px; }
        .practice-panel button {
            font: inherit; padding: 2px 8px; border: 1px solid #c8c8c8; background: #fff; color: #333;
            cursor: pointer; border-radius: 3px;
        }
        .practice-panel .tempo { display: inline-flex; align-items: center; gap: 8px; }
        .practice-panel .tempo input[type="range"] { width: 160px; margin: 0; accent-color: #0078d7; }
        .practice-panel button:disabled { opacity: 0.45; cursor: default; }
        .practice-panel .tval { width: 3.2em; text-align: right; font-variant-numeric: tabular-nums; }
        .practice-panel button[aria-pressed="true"] {
            background: #d6e6f8; border-color: #0078d7; color: #0b4a8b;
        }
        /* A part sounds while its button is pressed in; one let out is silent, its name grey (no strike-through:
           the line sat off the letters' middle in VS Code's font — owner's report 2026-10-25). */
        .practice-panel .parts { display: flex; flex-direction: column; align-items: stretch; gap: 4px; }
        /* One line a part: the toggle, then its fader (owner's request 2026-11-09). */
        .practice-panel .parts .pline { display: flex; align-items: center; gap: 8px; }
        .practice-panel .parts .pline button { flex: 1 1 auto; min-width: 0; }
        /* As long as the tempo's slider (owner's request 2026-11-09, session 1159: longer, the 200% end further right). */
        .practice-panel .parts input.fader { width: 160px; flex: 0 0 auto; margin: 0; accent-color: #0078d7; cursor: pointer; }
        .practice-panel .parts .flevel { width: 2.8em; flex: 0 0 auto; text-align: right; font-size: 11px; opacity: 0.8; font-variant-numeric: tabular-nums; }
        .practice-panel .presets { padding-top: 0; }
        .practice-panel .parts button { text-align: left; }
        .practice-panel .parts button[aria-pressed="false"] { color: #888; }
        /* A part the score does not sound (off unless pressed), and a sung voice. */
        .practice-panel .parts button.extra { font-style: italic; }
        /* Each voice's own microphone, the Play button's (owner's request 2026-11-10): its shape and its
           colours are the same rules, on the toggle's lower right. None prepared: grey, as not sung yet. */
        .practice-panel .parts button.voice { position: relative; padding-right: 16px; }
        .practice-panel .parts .voice-badge { right: -4px; bottom: -4px; }
        .range-panel table { border-collapse: collapse; }
        .range-panel th { text-align: left; font-weight: 600; opacity: 0.75; padding: 2px 8px; }
        .range-panel td { padding: 2px 8px; white-space: nowrap; }
        .range-panel td.end { cursor: pointer; border-radius: 3px; }
        .range-panel td.end:hover { background: var(--vscode-list-hoverBackground, rgba(128, 128, 128, 0.2)); }
        .range-panel td.end.cur { background: var(--vscode-list-activeSelectionBackground, rgba(0, 120, 215, 0.25)); }
        .range-panel .pitch { font-weight: 600; }
        .range-panel .where { opacity: 0.7; }
        .range-panel .note { opacity: 0.7; padding: 4px 8px 0; }
        .range-panel td.tess { vertical-align: top; }
        .range-panel .tess-chart { display: block; margin-top: 2px; }
        .range-panel .tess-bar { fill: var(--vscode-charts-blue, #1a73c8); opacity: 0.35; }
        .range-panel .tess-bar.in { opacity: 1; }
        .range-panel .tess-line { fill: var(--vscode-charts-blue, #1a73c8); }
    </style>
</head>
<!-- The preview is a rendered score, not an editable text buffer, so VS Code's
     default webview context menu (Copy / Cut / Paste) has nothing to act on:
     Cut and Paste can never apply, and Copy only ever picked up stray SVG text.
     preventDefaultContextMenuItems drops those built-in entries, leaving only
     what this extension contributes under menus/webview/context. lysPlayFrom and
     lysPlaying are the when-clause keys those items gate on; the contextmenu
     handler below refreshes them for each click. -->
<body data-vscode-context='{"preventDefaultContextMenuItems": true, "lysPlayFrom": false, "lysPlaying": false}'>
    <div class="toolbar">
        <!-- Each group wraps as one when the preview is narrow (owner's request 2026-10-25). -->
        <span class="group">
        <label for="renderSelect">Score:</label>
        <select id="renderSelect">
            <option value="">(Default)</option>
        </select>
        <button id="exportBtn" class="icon" type="button" aria-label="Export" title="Export…"><svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M2.5 9.5v4h11v-4"/><path d="M8 2v8.5"/><path d="M4.8 5.2 8 2l3.2 3.2"/></svg></button>
        </span>
        <span class="sep"></span>
        <span class="group">
        <button id="aiTransformBtn" class="ai icon" type="button" title="Ask AI in Chat (Ctrl+I)">✨</button>
        <button id="rangeBtn" class="toggle icon" type="button" aria-pressed="false" aria-label="Range" title="Each part's range"><svg viewBox="0 -.25 16.5 16.5" fill="currentColor" aria-hidden="true"><path d="M3 1.5 5.95 5.3H3.8v6.6h2.15L3 15.7.05 11.9H2.2V5.3H.05z"/><g transform="translate(.8 0)"><ellipse cx="9.4" cy="12.4" rx="2.8" ry="2" transform="rotate(-22 9.4 12.4)"/><rect x="11.35" y="1.5" width="1.05" height="10.8"/><path d="M12.4 1.5c.3 2 1.6 2.8 2.4 3.9.9 1.5 1.1 3.6.3 5.5.1-2.1-.9-3.6-2.7-4.2z"/></g></svg></button>
        </span>
        <span class="sep"></span>
        <span class="group">
        <!-- The voice badge (owner's request 2026-11-08): a small microphone over the corner of ▶ —
             hidden until the score has a sung voice; then preparing (grey, pulsing), ready, or
             failed, its title naming the parts. Not a button (the owner's first look at a ♪ button
             beside ⏹: "looks like a button but is not one; a note does not say singing, the
             instruments are notes too"): a status over the Play it speaks of, drawn as what sings.
             A click still opens the practice menu, where each voice has its own toggle and sign. -->
        <span class="play-wrap">
        <button id="playBtn" type="button" title="Play">▶</button>
        <span id="voiceBadge" class="voice-badge" role="status" hidden aria-label="Voice"><svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="6" y="1.2" width="4" height="7.6" rx="2"/><path d="M3.6 7.4a4.4 4.4 0 0 0 8.8 0"/><path d="M8 11.8v2.6M5.6 14.4h4.8"/></svg></span>
        <span id="voiceBar" class="voice-bar" hidden aria-hidden="true"><span></span></span>
        </span>
        <button id="stopBtn" type="button" title="Stop" disabled>⏹</button>
        <button id="practiceBtn" class="toggle icon practice" type="button" aria-pressed="false" aria-haspopup="true" aria-expanded="false" aria-label="Practice" title="Tempo, loop, metronome, parts"><svg viewBox="0 0 16 16" fill="currentColor" aria-hidden="true"><path fill-rule="evenodd" d="M3.6 14.8 6.3 1.4h3.4l2.7 13.4zM7.3 3 5.9 10.2h4.2L8.7 3z"/><path d="M7.7 11.2 12.4 3.9l1 .65-4.7 7.3z"/></svg><span id="practiceSpeed"></span></button>
        <span id="lengthInfo" class="length" title="Length, as played"></span>
        </span>
        <span class="sep"></span>
        <span class="group">
        <button id="firstPageBtn" type="button" title="First page">⏮</button>
        <!-- No min/max: the spinner is inverted in script (down = next page), so
             the native step must be free to go past either end (see pageInput). -->
        <input id="pageInput" type="number" value="1" title="Page" style="width: calc(1ch + 22px)">
        <span id="pageCount">/ 1</span>
        <button id="lastPageBtn" type="button" title="Last page">⏭</button>
        </span>
    </div>
    <div id="errorBanner" class="error-banner" role="alert"></div>
    <button id="followBtn" class="follow-btn" type="button" title="Scroll back to the notes sounding, and follow them again" hidden>↓ Follow</button>
    <div id="rangePanel" class="range-panel" hidden><button type="button" id="rangeClose" class="panel-close" title="Close" aria-label="Close">×</button><div id="rangeBody"></div></div>
    <div id="practicePanel" class="range-panel practice-panel" hidden>
        <button type="button" id="practiceClose" class="panel-close" title="Close" aria-label="Close">×</button>
        <div class="prow"><span class="plbl">Tempo</span><span class="tempo"><input type="range" id="tempoSlider" min="50" max="150" step="5" value="100" aria-label="Tempo"><span id="tempoValue" class="tval">100%</span><button type="button" id="tempoReset" title="Back to the tempo as written (100%)">Reset</button></span></div>
        <div class="prow"><span class="plbl">Loop</span><button type="button" id="loopBtn" class="toggle" aria-pressed="false" title="Play the selected bars, or the whole score, round and round — the score's repeats ignored">Off</button></div>
        <div class="prow"><span class="plbl">Metronome</span><button type="button" id="clickBtn" class="toggle" aria-pressed="false">Off</button></div>
        <div class="prow"><span class="plbl">Count-in</span><button type="button" id="countInBtn" class="toggle" aria-pressed="false" title="One bar of clicks before the music starts">Off</button></div>
        <div class="prow"><span class="plbl">Parts</span><span id="partsList" class="parts"></span></div>
        <div class="prow presets"><span class="plbl"></span><span class="tempo"><button type="button" id="partsReset" title="The parts as the score has them: its own on, the ones it leaves out off, every voice sung">Reset parts</button><button type="button" id="fadersReset" title="Every fader back to 100%">Reset faders</button></span></div>
    </div>
    <div class="main-content">
        <div class="container">
            <div id="svgContainer">
                <div class="loading">Loading preview...</div>
            </div>
        </div>
    </div>
    <div class="zoom-info" id="zoomInfo">100%</div>
    <script nonce="${nonce}">
        const vscode = acquireVsCodeApi();
        // Boot beacon + error relay: the FIRST statements, so the extension's
        // output channel shows whether this script ran at all, and any later
        // runtime error lands in the same log instead of a hidden devtools.
        vscode.postMessage({ type: 'webviewBoot' });
        window.addEventListener('error', (e) => {
            vscode.postMessage({ type: 'webviewError', message: String(e.message), line: e.lineno });
        });
        const HIGHLIGHT_THRESHOLD = ${HIGHLIGHT_DISTANCE_THRESHOLD};

        let scale = 1;
        // Each file's zoom and scroll while the follower shows another (keyed by URI), and
        // the one to put back when the file it turned to draws.
        const viewByFile = new Map();
        let pendingView = null;
        const minScale = 0.25;
        const maxScale = 4;
        const scaleStep = 0.1;

        const svgContainer = document.getElementById('svgContainer');
        const errorBanner = document.getElementById('errorBanner');
        const zoomInfo = document.getElementById('zoomInfo');
        const renderSelect = document.getElementById('renderSelect');

        // --- M3: select a note range on the score to drive the AI transform. A plain
        // click sets the anchor (and still jumps the editor); shift-click extends the
        // selection from the anchor. The "Transform with AI" action then maps the
        // selected source offsets to a text range in the editor (§6). ---
        const aiTransformBtn = document.getElementById('aiTransformBtn');
        let aiAnchorPos = -1;                 // last plainly-clicked note = range anchor
        let aiRangeLo = -1, aiRangeHi = -1;   // current score selection (source offsets)
        function aiClearSelection() {
            aiAnchorPos = -1; aiRangeLo = -1; aiRangeHi = -1;
            lightSelectedBars();
        }
        function aiSetSelection(a, b) {
            aiRangeLo = Math.min(a, b);
            aiRangeHi = Math.max(a, b);
            // The selection lights its BARS, not its notes (owner's request 2026-10-25): the
            // editor's text is not selected, so lit notes would read as a selection there.
            clearHighlights();
            lastHighlightRanges = null;
            lastHighlightPos = -1;
            lightSelectedBars();
        }
        // Every bar whose written stretch (data-span) meets the selection [lo, hi] is lit; none
        // when there is no selection. Re-run after a render replaces the boxes.
        function lightSelectedBars() {
            for (const r of svgContainer.querySelectorAll('rect.bar-staff.selected')) r.classList.remove('selected');
            if (aiRangeLo >= 0) {
                for (const r of svgContainer.querySelectorAll('rect.bar-staff[data-span]')) {
                    const span = r.getAttribute('data-span').split(' ').map(Number);
                    if (span.length === 2 && span[0] <= aiRangeHi && span[1] > aiRangeLo) r.classList.add('selected');
                }
            }
            updateAiButton();
        }
        // What the selection is, for the head of the AI's input box (owner's request 2026-10-25:
        // "N bars selected", as "Nothing is selected" heads it without one). Bars are counted by
        // their box (rect.bar-box, one per bar of a system), so a bar of several staves counts once.
        function selectedLabel() {
            const boxes = Array.from(svgContainer.querySelectorAll('rect.bar-box'));
            const bars = new Set();
            for (const r of svgContainer.querySelectorAll('rect.bar-staff.selected')) {
                const b = r.getBoundingClientRect();
                const cx = (b.left + b.right) / 2, cy = (b.top + b.bottom) / 2;
                const box = boxes.find(x => {
                    const c = x.getBoundingClientRect();
                    return cx >= c.left && cx <= c.right && cy >= c.top && cy <= c.bottom;
                });
                bars.add(box ? box : r);
            }
            const n = bars.size;
            return n === 0 ? 'The selection' : n === 1 ? '1 bar selected' : n + ' bars selected';
        }
        // The header's AI button is its emoji alone, "✨" (owner's request 2026-10-25); its tooltip
        // names it in a few words, and with bars selected says they go with the request. It opens
        // Copilot's chat, as Ctrl+I does (owner's requests 2026-10-29).
        function updateAiButton() {
            aiTransformBtn.title = aiRangeLo < 0
                ? 'Ask AI in Chat (Ctrl+I)'
                : 'Ask AI in Chat, about the selected bars (Ctrl+I)';
        }
        function askInChat() {
            // Held sunk a moment, so a quick click is seen to land.
            aiTransformBtn.classList.add('pressed');
            setTimeout(() => aiTransformBtn.classList.remove('pressed'), 180);
            vscode.postMessage({ type: 'askAiInChat', startPos: aiRangeLo, endPos: aiRangeHi });
        }
        aiTransformBtn.addEventListener('click', askInChat);

        function showErrorBanner(text) {
            errorBanner.textContent = text;
            errorBanner.classList.add('visible');
        }
        function hideErrorBanner() {
            errorBanner.classList.remove('visible');
            errorBanner.textContent = '';
        }
        let hideTimeout;
        let lastHighlightPos = -1;
        let lastHighlightTokenStart = -1; // start of the caret's token, so re-highlight after a render keeps the containment guard
        let lastHighlightRanges = null;   // set while a selection (not a bare cursor) is highlighted

        // Pages of the current SVG (single-page SVGs have no g.page wrappers).
        let pages = [{ top: 0, height: 0 }];
        let svgWidthPx = 0;   // the SVG's natural width in px (width attribute)
        let pxPerSpace = 10;  // px per staff-space (width / viewBox width)
        // 'width' = keep the score fitted to the pane; null = manual zoom.
        // Fit-to-width is the state a freshly opened preview starts in, so the
        // first render (and every resize after it) fits without being asked.
        let fitMode = 'width';
        const mainContent = document.querySelector('.main-content');
        const pageInput = document.getElementById('pageInput');
        const pageCount = document.getElementById('pageCount');
        // The page number last put in the box: the base the inverted spinner
        // steps from (the box's own value has already moved by then).
        let shownPageNumber = 1;
        function showPageNumber(n) {
            shownPageNumber = n;
            pageInput.value = String(n);
        }

        // Zoom by resizing the SVG element (not CSS transform): real layout
        // size keeps scrollbars, centering and page-scroll math correct.
        function updateZoom() {
            const svg = svgContainer.querySelector('svg');
            if (svg && svgWidthPx > 0) {
                svg.style.width = (svgWidthPx * scale) + 'px';
                svg.style.height = 'auto';
            }
            zoomInfo.textContent = Math.round(scale * 100) + '%';
            zoomInfo.classList.add('visible');
            clearTimeout(hideTimeout);
            hideTimeout = setTimeout(() => {
                zoomInfo.classList.remove('visible');
            }, 1500);
            updatePageInfo();
        }

        function collectPages() {
            const svg = svgContainer.querySelector('svg');
            pages = [{ top: 0, height: 0 }];
            svgWidthPx = 0;
            if (!svg) { updatePageInfo(); return; }
            const vb = (svg.getAttribute('viewBox') || '0 0 1 1').split(/ +/).map(Number);
            const vbW = vb[2] || 1, vbH = vb[3] || 1;
            svgWidthPx = parseFloat(svg.getAttribute('width')) || svg.clientWidth || 1;
            pxPerSpace = svgWidthPx / vbW;
            const gs = svg.querySelectorAll(':scope > g.page');
            pages = gs.length > 0
                ? Array.from(gs).map(g => ({
                    top: parseFloat(g.getAttribute('data-page-top')) || 0,
                    height: parseFloat(g.getAttribute('data-page-height')) || vbH
                }))
                : [{ top: 0, height: vbH }];
            updatePageInfo();
        }

        function currentPageIndex() {
            const st = mainContent.scrollTop - 10;
            let idx = 0;
            for (let i = 0; i < pages.length; i++) {
                if (pages[i].top * pxPerSpace * scale <= st + 1) idx = i;
            }
            return idx;
        }

        function updatePageInfo() {
            const n = pages.length;
            pageCount.textContent = '/ ' + n;
            // As narrow as the widest page number allows: its digits, the
            // padding and border (7px), and Chromium's spinner (about 15px).
            pageInput.style.width = 'calc(' + String(n).length + 'ch + 22px)';
            // Scrolling reports the page, but not over a number the user is
            // still typing: a focused box keeps its text until change/blur.
            if (document.activeElement !== pageInput) {
                showPageNumber(currentPageIndex() + 1);
            }
            const single = n < 2;
            pageInput.disabled = single;
            for (const id of ['firstPageBtn', 'lastPageBtn']) {
                document.getElementById(id).disabled = single;
            }
        }

        function gotoPage(i) {
            i = Math.max(0, Math.min(pages.length - 1, i));
            mainContent.scrollTop = i === 0 ? 0 : pages[i].top * pxPerSpace * scale + 20;
            updatePageInfo();
        }

        function availSize() {
            return { w: mainContent.clientWidth - 44, h: mainContent.clientHeight - 44 };
        }

        function fitWidth() {
            fitMode = 'width';
            const a = availSize();
            if (svgWidthPx > 0) scale = a.w / svgWidthPx;
            updateZoom();
        }

        function isDarkTheme() {
            return document.documentElement.classList.contains('theme-dark');
        }

        function getHighlightColor() {
            return isDarkTheme() ? '#00ccff' : '#ff6600';
        }

        // ---- color scheme ---------------------------------------------------
        // lilysharp.preview.theme, resolved onto <html class="theme-dark"> (the head
        // script set it once before paint; this keeps it current). 'auto' follows the
        // editor theme, which the webview reports through prefers-color-scheme.
        let themeMode = '${theme}';
        const darkScheme = window.matchMedia('(prefers-color-scheme: dark)');
        function applyTheme(mode) {
            if (mode === 'light' || mode === 'dark' || mode === 'auto') themeMode = mode;
            const dark = themeMode === 'dark' || (themeMode === 'auto' && darkScheme.matches);
            document.documentElement.classList.toggle('theme-dark', dark);
        }
        darkScheme.addEventListener('change', () => applyTheme(themeMode));

        // Put an attribute back to the value the SVG shipped with: re-apply the
        // saved string, or drop the attribute entirely if it never had one.
        // Removing it blindly is wrong — many grobs carry an explicit paint
        // (the section mark's box is fill="#FFFFFF", its label fill="#000000"),
        // and dropping it falls back to SVG's default black, which the dark-mode
        // invert() filter then renders as a solid white blob that never reverts.
        function restoreAttr(el, name, orig) {
            if (orig === null || orig === undefined) {
                el.removeAttribute(name);
            } else {
                el.setAttribute(name, orig);
            }
        }

        // ---- page-level update -----------------------------------------------
        // The score arrives as ONE SVG string on every render, and replacing the
        // whole of it (innerHTML) parses, lays out and paints every page in the
        // editor's own window. Measured on a five-page song: 150 ms to parse,
        // 230 ms to lay out, 80 ms to paint — half a second between a keystroke
        // and the caret whenever the preview was open (2026-09-04, owner report).
        // A keystroke changes one page, so the pages are compared as STRINGS and
        // only the changed ones are parsed again (37 ms for one page of that
        // song). A page whose drawing is the same but whose source offsets moved
        // — every element after the edit carries a new data-pos — keeps its DOM
        // and has the offsets re-stamped from the new markup, in document order
        // (8 ms). Anything this cannot account for — a different page count, a
        // changed frame around the pages, an element count that does not match
        // — falls back to the whole replacement, so the picture can never differ
        // from what innerHTML would have shown.
        const PAGE_OPEN = '<g class="page" data-page-top=';
        // A source attribute with its VALUE blanked and its NAME kept: two pages
        // that agree on this carry the attributes on the same elements in the
        // same order, which is what lets the values be re-stamped by index.
        // ⚠️ This script is the body of a TypeScript template literal, so a regex
        // backslash has to be written DOUBLED here to survive into the page: a
        // single one is the template's own escape and vanishes (a \\s written
        // singly became an s, and a \\/ a /, which ended the regex early and took
        // the whole script down — the preview stayed on "Loading", 2026-09-04).
        const SOURCE_ATTRS = /\\s(data-pos|data-alt|data-start|data-span)="[^"]*"/g;
        // The elements that carry one: a grob's data-pos, a barline's data-alt list, and a bar's
        // written place (rect.bar-staff's data-start, which a click on the bar goes to).
        // A bar's written stretch (data-span, a pair: the caret in it lights the bar) rides with it.
        const SOURCE_SELECTOR = '[data-pos], [data-alt], [data-start], [data-span]';
        let shownPages = null;      // per page: { markup, blank } of what is on screen
        let shownFrame = null;      // the markup around the pages (svg tag, style, closing)

        function forgetShownPages() { shownPages = null; shownFrame = null; }

        // The SVG string as [head, page, page, …, tail]: each page runs from its
        // opening tag to the next page's (or to </svg>), so the closing </g> and
        // the whitespace after it belong to it. null when the shape is not that.
        function splitPages(svg) {
            const close = svg.lastIndexOf('</svg>');
            const first = svg.indexOf(PAGE_OPEN);
            if (first < 0 || close < 0 || first > close) return null;
            const pages = [];
            for (let at = first; at >= 0 && at < close;) {
                const next = svg.indexOf(PAGE_OPEN, at + PAGE_OPEN.length);
                const end = next < 0 || next > close ? close : next;
                pages.push(svg.slice(at, end));
                at = next;
            }
            return { head: svg.slice(0, first), pages, tail: svg.slice(close) };
        }

        // Re-stamps one page's source offsets from the markup, whose blanked form
        // the caller has found equal to the page's: the source attributes then sit
        // on the same elements, in the same order, under the same names, so one
        // pass over the markup's attributes and one over the elements that carry
        // any line up one to one. false when they do not — a count that differs —
        // and the caller replaces the page instead.
        function restampPage(page, markup) {
            const els = page.querySelectorAll(SOURCE_SELECTOR);
            const attrs = /\\sdata-(pos|alt|start|span)="([^"]*)"/g;
            for (let k = 0; k < els.length; k++) {
                const el = els[k];
                const carried = (el.hasAttribute('data-pos') ? 1 : 0) + (el.hasAttribute('data-alt') ? 1 : 0)
                    + (el.hasAttribute('data-start') ? 1 : 0) + (el.hasAttribute('data-span') ? 1 : 0);
                for (let j = 0; j < carried; j++) {
                    const m = attrs.exec(markup);
                    if (!m) return false;
                    el.setAttribute('data-' + m[1], m[2]);
                }
            }
            return attrs.exec(markup) === null;
        }

        // A page's source offsets as drawn, in document order, against the markup the page
        // was just brought up to — what the group reconcile promises (a kept or carried group
        // re-stamped, a parsed one fresh). Reading them back costs one pass over the page's
        // offset-carrying elements, far less than the parse the reconcile saves, and it is
        // the one place a reconcile that left an offset stale can be seen: the caret and the
        // selection light whatever note carries the offset, so a stale one lights the wrong
        // note (owner report 2026-10-11: selecting a d lit the e three notes on, until the
        // preview was reopened — the server's pages were right, measured edit by edit).
        // null when they agree, else what differed first, for the output channel.
        function offsetsDisagree(page, markup) {
            const drawn = [];
            for (const el of page.querySelectorAll(SOURCE_SELECTOR)) {
                for (const a of el.attributes) {
                    if (a.name === 'data-pos' || a.name === 'data-alt' || a.name === 'data-start' || a.name === 'data-span') drawn.push(a.name + '=' + a.value);
                }
            }
            const attrs = /\\sdata-(pos|alt|start|span)="([^"]*)"/g;
            let k = 0;
            for (let m; (m = attrs.exec(markup)); k++) {
                const want = 'data-' + m[1] + '=' + m[2];
                if (drawn[k] !== want) return 'offset #' + k + ' drawn ' + (drawn[k] || 'none') + ', markup ' + want;
            }
            return k === drawn.length ? null : drawn.length + ' offsets drawn, ' + k + ' in the markup';
        }

        // After a reconcile: every changed page's offsets read back against its markup, and
        // a page that disagrees replaced from its markup — said loudly in the account, so the
        // reconcile's slip is found from the output channel the next time it happens.
        function recheckOffsets(live, livePages, markups, changed) {
            let note = '';
            for (const i of changed) {
                const why = offsetsDisagree(livePages[i], markups[i]);
                if (!why) continue;
                const fresh = parseOne(markups[i]);
                if (!fresh) return note + '; STALE OFFSETS on page ' + i + ' (' + why + '), and it did not parse alone';
                live.replaceChild(fresh, livePages[i]);
                shownPages[i] = { markup: markups[i], blank: markups[i].replace(SOURCE_ATTRS, ' $1') };
                note += '; STALE OFFSETS on page ' + i + ' after the reconcile (' + why + ') - page replaced';
            }
            return note;
        }

        // Inside a page the renderer puts each SYSTEM, and then the page's overlays
        // (slurs, lyrics, dynamics, marks — drawn after every system), in a labeled
        // group of its own (SharedRenderer, BeginLabeledGroup — interactive SVG
        // only). A page whose drawing changed is therefore compared group by
        // group, and only the group the edit touched is parsed again: a system
        // is a few kilobytes where the page is a hundred.
        const CHUNK_OPEN = /<g class="(system|overlay)">/g;
        const G_TAGS = /<g[\\s>]|<\\/g>/g;
        // A system's FRAME: the renderer draws each system in its own frame and puts it
        // where it sits with the first tag inside the group, <g transform="translate(0,Y)
        // scale(1,1)"> (IDocumentContext.SystemLocalFrames). A system that moved down
        // the page is therefore the same text under another transform, and the preview
        // sets the attribute instead of parsing the system again. The frame tag is
        // matched right after the group's open tag only — nested groups (an ossia's
        // scale) carry transforms of their own that must not be touched.
        const FRAME_TAG = /^(<g class="system">\\s*<g)( transform="[^"]*")?(>)/;
        // The chunk with its frame transform blanked, and the transform itself.
        function unframe(markup) {
            const m = FRAME_TAG.exec(markup);
            if (!m) return { text: markup, transform: null };
            return {
                text: markup.slice(0, m[1].length) + '>' + markup.slice(m[0].length),
                transform: m[2] ? m[2].slice(' transform="'.length, -1) : ''
            };
        }
        function setFrameTransform(group, transform) {
            const frame = group.firstElementChild;
            if (!frame || frame.tagName.toLowerCase() !== 'g') return false;
            if (transform) frame.setAttribute('transform', transform);
            else frame.removeAttribute('transform');
            return true;
        }

        // A page's markup as its labeled groups and the FRAME around and between
        // them — the page's own tag, the header texts, the margin group's tags,
        // the whitespace. null when a group has no close, which no page of the
        // renderer's has.
        function splitChunks(markup) {
            const chunks = [];
            let frame = '';
            let head = null, gapsBlank = true;
            let at = 0;
            CHUNK_OPEN.lastIndex = 0;
            for (let open; (open = CHUNK_OPEN.exec(markup));) {
                G_TAGS.lastIndex = open.index;
                let depth = 0, end = -1;
                for (let tag; (tag = G_TAGS.exec(markup));) {
                    depth += tag[0] === '</g>' ? -1 : 1;
                    if (depth === 0) { end = tag.index + tag[0].length; break; }
                }
                if (end < 0) return null;
                const gap = markup.slice(at, open.index);
                if (head === null) head = gap; else if (gap.trim() !== '') gapsBlank = false;
                frame += gap;
                chunks.push({ label: open[1], markup: markup.slice(open.index, end) });
                at = end;
                CHUNK_OPEN.lastIndex = end;
            }
            const tail = markup.slice(at);
            frame += tail;
            // head/tail: the frame BEFORE the first group and AFTER the last one, for a
            // comparison that does not count the groups (reflowPages); gapsBlank says the
            // text between groups is whitespace only, so head + groups + tail is the page.
            return { frame, chunks, head: head === null ? frame : head, tail, gapsBlank };
        }

        // A fresh element parsed from one group's or one page's markup, or null
        // when the markup is not exactly one element.
        function parseOne(markup) {
            const holder = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
            holder.innerHTML = markup;
            const fresh = holder.firstElementChild;
            return fresh && fresh === holder.lastElementChild ? fresh : null;
        }

        // Brings every page whose drawing changed up to the markup, group by group and
        // ACROSS the pages: the live groups of all those pages go into one pool keyed
        // by content (frame transform and source offsets blanked), and each group of
        // the new markup takes its match from the pool — the same page's same slot
        // when that is a match, else wherever it is — moved into place with the
        // transform set and the offsets re-stamped. Only a group with no match (the
        // edited system, a page's overlays) is parsed.
        // ⚠️ ONE POOL FOR ALL CHANGED PAGES, not one per page: when the page breaker
        // re-cuts the pages, a page that handed one system on and took one in keeps
        // its group COUNT, and a per-page, slot-by-slot comparison of it parsed every
        // one of its systems while the system it handed on was never in any pool —
        // the editor's own line said "carried 0, parsed 4" for exactly that keystroke
        // (2026-09-04; no backticks in this script — it is a template literal). The
        // result names what it did; a "fail" says why the pages
        // cannot be accounted for (a frame that differs beyond its offsets, a group
        // whose class does not match), and the caller then replaces those pages
        // whole, which is safe after a partial reconcile because only they took part.
        function reflowPages(livePages, split, failed) {
            const pool = new Map(); // blank -> [{ node, prev }] of the failed pages' live groups
            for (const i of failed) {
                const shown = shownPages[i];
                const before = shown.split || (shown.split = splitChunks(shown.markup));
                if (!before) return { fail: 'page ' + i + ' has no group split' };
                const nodes = livePages[i].querySelectorAll('g.system, g.overlay');
                if (nodes.length !== before.chunks.length)
                    return { fail: 'page ' + i + ' shows ' + nodes.length + ' groups, the record says ' + before.chunks.length };
                for (let k = 0; k < nodes.length; k++) {
                    const was = before.chunks[k];
                    const prev = was.unframed || (was.unframed = unframe(was.markup));
                    const blank = was.blank || (was.blank = prev.text.replace(SOURCE_ATTRS, ' $1'));
                    let list = pool.get(blank);
                    if (!list) pool.set(blank, list = []);
                    // stale: the page's offsets were shifted in place (applyPages) since
                    // this record was made, so its text no longer says which offsets
                    // the live group carries — a carried group from it is always
                    // re-stamped. Its blank form is unaffected (offsets blanked).
                    list.push({ node: nodes[k], prev, label: was.label, page: i, index: k, blank, stale: !!shown.stale });
                }
            }
            let carried = 0, parsed = 0, moved = 0, kept = 0;
            let why = '';
            // When a SYSTEM finds no match, say what the nearest unclaimed system in the
            // pool differs by — the first differing character with its neighbourhood —
            // so a mismatch is diagnosed from the output channel, not guessed at.
            let explained = 0;
            function explain(i, k, blank) {
                if (explained >= 3) return;
                let best = null, bestAt = -1;
                for (const list of pool.values())
                    for (const e of list) {
                        if (e.label !== 'system') continue;
                        let n = 0;
                        const lim = Math.min(e.blank.length, blank.length);
                        while (n < lim && e.blank[n] === blank[n]) n++;
                        if (n > bestAt) { bestAt = n; best = e; }
                    }
                if (!best) { explained++; why += (why ? ' | ' : '') + 'no unclaimed system in the pool'; return; }
                // Its own old self in the same slot is the EDITED system — expected to
                // differ, not worth a line. Anything else is a system that should have
                // matched somewhere and did not.
                if (best.page === i && best.index === k) return;
                explained++;
                const ctx = s => JSON.stringify(s.slice(Math.max(0, bestAt - 30), bestAt + 40));
                why += (why ? ' | ' : '') + 'a system on page ' + i + ' differs from page ' + best.page
                    + ' group ' + best.index + ' at ' + bestAt + '/' + blank.length + ' vs ' + best.blank.length
                    + ': new ' + ctx(blank) + ' old ' + ctx(best.blank);
            }
            for (const i of failed) {
                const markup = split.pages[i];
                const pieces = splitChunks(markup);
                const before = shownPages[i].split;
                if (!pieces) return { fail: 'page ' + i + ' does not split' };
                // The frame without its groups: the group COUNT is what changed here, so
                // the frame as a whole differs by the whitespace between groups; what must
                // agree is the text before the first group and after the last, offsets
                // blanked, with nothing but whitespace between the groups on either side.
                if (!pieces.gapsBlank || !before.gapsBlank
                    || pieces.head.replace(SOURCE_ATTRS, ' $1') !== before.head.replace(SOURCE_ATTRS, ' $1')
                    || pieces.tail !== before.tail)
                    return { fail: 'page ' + i + ' frame differs beyond its offsets' };
                const page = livePages[i];
                const existing = Array.from(page.querySelectorAll('g.system, g.overlay'));
                // The groups' parent: the margin group when there is one, else the page.
                const parent = existing.length ? existing[0].parentNode
                    : (page.querySelector(':scope > g:not(.system):not(.overlay)') || page);
                const anchor = existing.length ? existing[existing.length - 1].nextSibling : null;
                const desired = [];
                for (let k = 0; k < pieces.chunks.length; k++) {
                    const chunk = pieces.chunks[k];
                    const next = unframe(chunk.markup);
                    const blank = next.text.replace(SOURCE_ATTRS, ' $1');
                    chunk.blank = blank; chunk.unframed = next;
                    const list = pool.get(blank);
                    let node = null;
                    if (list && list.length) {
                        // The same page's same slot first — the common case is a page
                        // whose systems all stayed where they were — else any match.
                        let at = list.findIndex(e => e.page === i && e.index === k);
                        if (at < 0) at = 0;
                        const hit = list.splice(at, 1)[0];
                        if (hit.label !== chunk.label) return { fail: 'page ' + i + ': a ' + hit.label + ' matched a ' + chunk.label };
                        node = hit.node;
                        const transformed = next.transform !== hit.prev.transform;
                        if (transformed && !setFrameTransform(node, next.transform))
                            return { fail: 'page ' + i + ': a carried system has no frame' };
                        if ((hit.stale || next.text !== hit.prev.text) && !restampPage(node, chunk.markup))
                            return { fail: 'page ' + i + ': a carried group could not be re-stamped' };
                        if (node.parentNode !== parent) carried++;
                        else if (transformed) moved++;
                        else kept++;
                    } else {
                        if (chunk.label === 'system') explain(i, k, blank);
                        node = parseOne(chunk.markup);
                        if (!node) return { fail: 'page ' + i + ': a group did not parse' };
                        parsed++;
                    }
                    desired.push(node);
                }
                // Put the page's groups in the markup's order (inserting each before the
                // same anchor keeps them in sequence, and moves a node that is already
                // there), then drop the live groups the markup no longer has — they are
                // either carried elsewhere already or gone.
                const keep = new Set(desired);
                for (const node of desired) parent.insertBefore(node, anchor);
                for (const node of existing) if (!keep.has(node) && node.parentNode === parent) parent.removeChild(node);
                // The frame's own offsets (the first page's header): a whole-page
                // re-stamp is exact now that the DOM order is the markup's.
                if ((shownPages[i].stale || pieces.head !== before.head) && !restampPage(page, markup))
                    return { fail: 'page ' + i + ': the frame could not be re-stamped' };
                shownPages[i] = { markup, blank: markup.replace(SOURCE_ATTRS, ' $1'), split: pieces };
            }
            return { carried, parsed, moved, kept, why };
        }

        // Shows the svg: the pages that changed are replaced — group by group
        // where the page's groups line up — the ones that only moved in the
        // source are re-stamped, the rest are left as they are.
        // Returns a one-line account of what it did, for the output channel.
        function applySvg(svg) {
            const split = splitPages(svg);
            const live = svgContainer.querySelector('svg');
            const livePages = live ? live.querySelectorAll(':scope > g.page') : [];
            const frame = split ? split.head + split.tail : null;
            const whole = () => {
                svgContainer.innerHTML = svg;
                shownFrame = frame;
                shownPages = split
                    ? split.pages.map(markup => ({ markup, blank: markup.replace(SOURCE_ATTRS, ' $1') }))
                    : null;
            };
            if (!split || !shownPages || !live
                || livePages.length !== split.pages.length || frame !== shownFrame) {
                whole();
                return 'whole document (' + (split ? split.pages.length + ' pages' : 'no page groups') + ')';
            }
            let pagesKept = 0, pagesRestamped = 0, pagesReplaced = 0;
            const changed = []; // pages whose drawing changed — reconciled together below
            for (let i = 0; i < split.pages.length; i++) {
                const markup = split.pages[i];
                const shown = shownPages[i];
                if (markup === shown.markup) { pagesKept++; continue; }
                const blank = markup.replace(SOURCE_ATTRS, ' $1');
                // The drawing is the same: only offsets moved.
                if (blank === shown.blank && restampPage(livePages[i], markup)) {
                    shownPages[i] = { markup, blank }; pagesRestamped++; continue;
                }
                changed.push(i);
            }
            let account = '';
            if (changed.length) {
                const r = reflowPages(livePages, split, changed);
                if (r.fail) {
                    // A partial reconcile may have moved groups between the changed pages;
                    // replacing each of them from its own markup rebuilds exactly those
                    // pages, so it is safe (only they took part).
                    for (const i of changed) {
                        const fresh = parseOne(split.pages[i]);
                        if (!fresh) { whole(); return 'whole document (a page did not parse alone)'; }
                        live.replaceChild(fresh, livePages[i]);
                        const markup = split.pages[i];
                        shownPages[i] = { markup, blank: markup.replace(SOURCE_ATTRS, ' $1') };
                        pagesReplaced++;
                    }
                    account = ', replaced ' + pagesReplaced + ' (reconcile gave up: ' + r.fail + ')';
                } else {
                    account = ', reconciled ' + changed.length + ' (groups kept ' + r.kept + ', moved ' + r.moved
                        + ', carried ' + r.carried + ', parsed ' + r.parsed + (r.why ? '; ' + r.why : '') + ')'
                        + recheckOffsets(live, livePages, split.pages, changed);
                }
            }
            return 'pages kept ' + pagesKept + ', re-stamped ' + pagesRestamped + account;
        }

        // ---- page-wise answers --------------------------------------------------
        // The server can send the picture as PAGES (SvgPages): with a base version, a
        // DELTA against the picture on screen — which pages are the same, which only
        // moved their source offsets, and the markup of the ones that changed — so the
        // whole document no longer travels on every keystroke (2026-09-18: on a 1000-bar
        // book 3–12 MB of markup per keystroke, of which one page had changed). A 'same'
        // page is left alone. A 'shifted' page has every data-pos / data-alt mapped
        // through the answer's window, the arithmetic the server verified the new page
        // against (below the prefix unchanged, at or after the suffix start moved by the
        // delta); its stored markup then no longer says the right numbers, which the
        // reconcile allows for (stale). A 'changed' page goes through the same group
        // reconcile a changed page of a one-string answer does. Whatever this cannot
        // apply — no picture on screen, another page count, another frame, a shift the
        // window declines — asks the host for the whole picture (requestFull) and
        // returns null; the answer to that carries every page and replaces the document.
        function requestFull(why) {
            vscode.postMessage({ type: 'requestFull', why });
            return null;
        }
        function pagesChars(ps) {
            let n = ps.Head.length + ps.Tail.length;
            for (const item of ps.Items) n += item.Markup ? item.Markup.length : 0;
            return n;
        }
        // Maps one offset through the window; NaN when the window has no image for it.
        function shiftOffset(n, w) {
            return n < w.Prefix ? n : n >= w.SuffixStart ? n + w.Delta : NaN;
        }
        // Re-stamps every source offset the page carries through the window. false when
        // an offset lies inside the window — the server never calls such a page shifted,
        // so this is the picture and the answer disagreeing, and the caller starts over.
        function shiftPage(page, w) {
            if (!w) return false;
            for (const el of page.querySelectorAll(SOURCE_SELECTOR)) {
                for (const name of ['data-pos', 'data-start']) {
                    const pos = el.getAttribute(name);
                    if (pos === null) continue;
                    const m = shiftOffset(parseInt(pos, 10), w);
                    if (!Number.isFinite(m)) return false;
                    el.setAttribute(name, String(m));
                }
                for (const name of ['data-alt', 'data-span']) {
                    const list = el.getAttribute(name);
                    if (!list) continue;
                    const parts = list.split(' ').map(a => shiftOffset(parseInt(a, 10), w));
                    if (parts.some(m => !Number.isFinite(m))) return false;
                    el.setAttribute(name, parts.join(' '));
                }
            }
            return true;
        }
        function applyPages(ps) {
            const items = ps.Items;
            const frame = ps.Head + ps.Tail;
            const live = svgContainer.querySelector('svg');
            const livePages = live ? live.querySelectorAll(':scope > g.page') : [];
            const allMarkup = items.every(it => typeof it.Markup === 'string');
            if (!shownPages || !live || livePages.length !== items.length || frame !== shownFrame) {
                if (!allMarkup) {
                    return requestFull(!live ? 'no picture on screen'
                        : !shownPages ? 'no record of the pages shown'
                        : livePages.length !== items.length ? 'page count ' + livePages.length + ' -> ' + items.length
                        : 'the frame around the pages changed');
                }
                svgContainer.innerHTML = ps.Head + items.map(it => it.Markup).join('') + ps.Tail;
                shownFrame = frame;
                shownPages = items.map(it => ({ markup: it.Markup, blank: it.Markup.replace(SOURCE_ATTRS, ' $1') }));
                return 'whole document (' + items.length + ' pages)';
            }
            let pagesKept = 0, pagesShifted = 0, pagesReplaced = 0;
            const changed = [];
            const markups = new Array(items.length);
            for (let i = 0; i < items.length; i++) {
                const item = items[i];
                if (item.Change === 'same') { pagesKept++; continue; }
                if (item.Change === 'shifted') {
                    if (!shiftPage(livePages[i], ps.Window)) {
                        return requestFull('page ' + i + ' holds an offset the window has no image for');
                    }
                    shownPages[i].stale = true;
                    pagesShifted++;
                    continue;
                }
                if (typeof item.Markup !== 'string') {
                    return requestFull('page ' + i + ' is changed but carries no markup');
                }
                markups[i] = item.Markup;
                changed.push(i);
            }
            let account = '';
            if (changed.length) {
                const r = reflowPages(livePages, { pages: markups }, changed);
                if (r.fail) {
                    for (const i of changed) {
                        const fresh = parseOne(markups[i]);
                        if (!fresh) {
                            return requestFull('page ' + i + ' did not parse alone');
                        }
                        live.replaceChild(fresh, livePages[i]);
                        const markup = markups[i];
                        shownPages[i] = { markup, blank: markup.replace(SOURCE_ATTRS, ' $1') };
                        pagesReplaced++;
                    }
                    account = ', replaced ' + pagesReplaced + ' (reconcile gave up: ' + r.fail + ')';
                } else {
                    account = ', reconciled ' + changed.length + ' (groups kept ' + r.kept + ', moved ' + r.moved
                        + ', carried ' + r.carried + ', parsed ' + r.parsed + (r.why ? '; ' + r.why : '') + ')'
                        + recheckOffsets(live, livePages, markups, changed);
                }
            }
            return 'pages kept ' + pagesKept + ', shifted ' + pagesShifted + account;
        }

        // ---- source-position index -------------------------------------------
        // Every source offset the rendered score carries, sorted. Built once per
        // SVG and reused by the caret handlers, which run on every cursor
        // movement: reading data-pos off each drawn element there cost the whole
        // document per keystroke, and with the preview open a long score visibly
        // lagged behind the caret. A barline can also carry a data-alt list (the
        // other written bars that collapse onto it), and those offsets count too.
        let posSorted = [];        // ascending, unique
        let posElements = new Map(); // offset -> the drawn elements that carry it
        let posIndexBuilt = false;

        function invalidateSourcePositions() {
            posIndexBuilt = false;
            posSorted = [];
            posElements = new Map();
        }

        // The elements sharing one data-pos are indexed here as well, so a caret
        // movement paints its note from a Map lookup: the attribute selector it
        // used to run ('[data-pos="N"], [data-alt~="N"]') walks EVERY element of
        // the SVG, and the preview's frame runs in the editor's window — on a
        // long score each arrow key spent that walk before the caret could move
        // (owner report 2026-09-04). Two kinds of element that share a note's
        // offset are left out of the index because they must never be
        // recolored: the transparent 'nh-hit' notehead click target (a filled box
        // would show), and the white OCCLUDER behind a tab fret digit (it hides
        // the string line). A genuine boxed label (section/rehearsal) is STROKED;
        // the occluder is fill-only. It is told by its EXPLICIT fill: a barline is
        // a strokeless rect too, but drawn in the black default (no fill
        // attribute), so an unstroked rect WITHOUT a fill attribute is real ink
        // and must still highlight — only an unstroked rect that carries a fill
        // (the white mask) is the occluder. Leaving the mask in made hasBox true,
        // which coloured the box and suppressed the notehead's own highlight.
        function sourcePositions() {
            if (posIndexBuilt) return posSorted;
            const seen = new Set();
            const byPos = new Map();
            const add = (n, el) => {
                if (!Number.isFinite(n)) return;
                seen.add(n);
                if (!el) return;
                const list = byPos.get(n);
                if (list) list.push(el); else byPos.set(n, [el]);
            };
            const svg = svgContainer.querySelector('svg');
            if (svg) {
                for (const el of svg.querySelectorAll('[data-pos], [data-alt]')) {
                    const paintable = !el.classList.contains('nh-hit')
                        && !(el.tagName.toLowerCase() === 'rect'
                            && !el.getAttribute('stroke') && el.getAttribute('fill'));
                    const keep = paintable ? el : null;
                    add(parseInt(el.getAttribute('data-pos'), 10), keep);
                    const alt = el.getAttribute('data-alt');
                    if (alt) {
                        for (const a of alt.split(' ')) add(parseInt(a, 10), keep);
                    }
                }
            }
            posSorted = Array.from(seen).sort((a, b) => a - b);
            posElements = byPos;
            posIndexBuilt = true;
            return posSorted;
        }

        // The elements currently painted, so clearing them costs those elements
        // and not a '.highlight' query over the whole SVG.
        let highlighted = [];

        function clearHighlights() {
            const els = highlighted;
            highlighted = [];
            els.forEach(el => {
                el.classList.remove('highlight');
                // Only restore what we actually recolored (origStroke/origFill set);
                // a boxed label's text is left untouched on highlight, so leave its
                // fill alone here too — undefined means "never recolored".
                if (el.__origStroke !== undefined) {
                    restoreAttr(el, 'stroke', el.__origStroke);
                    el.__origStroke = undefined;
                }
                if (el.__origFill !== undefined) {
                    restoreAttr(el, 'fill', el.__origFill);
                    el.__origFill = undefined;
                }
                // Restore the original z-order (DOM position) that was changed
                // when this element was raised on highlight.
                if (el.__origParent) {
                    el.__origParent.insertBefore(el, el.__origNextSibling);
                    el.__origParent = null;
                    el.__origNextSibling = null;
                }
            });
        }

        // A caret highlight is painted once per frame, from the LAST message: a
        // held arrow key sends a caret position per repeat, and painting each
        // one — recolor, raise, restore — costs a style and layout pass of the
        // whole SVG in the editor's own window. Only the position the caret has
        // now is worth a frame.
        let pendingHighlight = null;
        let highlightFrame = 0;
        function scheduleHighlight(paint) {
            pendingHighlight = paint;
            if (highlightFrame) return;
            highlightFrame = requestAnimationFrame(() => {
                highlightFrame = 0;
                const paintNow = pendingHighlight;
                pendingHighlight = null;
                if (paintNow) paintNow();
            });
        }

        function highlightNearestElement(cursorPos, tokenStart) {
            // Clear previous highlights
            clearHighlights();
            if (cursorPos < 0) { lastResolvedPos = -1; return; }

            // The note the caret is on = the nearest one AT or before the caret that also
            // lies within the caret's own token (data-pos >= tokenStart). The tokenStart
            // guard is what rejects the nearest PRECEDING note when the caret sits on a
            // barline / keyword / gap — that token holds no note, so nothing highlights.
            //
            // Answered from the source-position index, not by reading the attributes
            // of every drawn element: this runs on EVERY cursor movement, and the
            // scan is what made a long score feel stuck whenever the preview was open.
            sourcePositions();
            const floor = (typeof tokenStart === 'number' && tokenStart >= 0) ? tokenStart : 0;
            let lo = 0, hi = posSorted.length - 1, nearestPos = -1;
            while (lo <= hi) {
                const mid = (lo + hi) >> 1;
                if (posSorted[mid] <= cursorPos) { nearestPos = posSorted[mid]; lo = mid + 1; }
                else { hi = mid - 1; }
            }
            // The greatest position at or before the caret is the nearest one; if even
            // that lies before the caret's own token, no note belongs to this caret.
            if (nearestPos < floor) { nearestPos = -1; }
            const nearestDist = nearestPos >= 0 ? cursorPos - nearestPos : Infinity;

            // Highlight ALL elements with that data-pos
            if (nearestPos >= 0 && nearestDist < HIGHLIGHT_THRESHOLD) {
                highlightPositions([nearestPos]);
                lastResolvedPos = nearestPos;
            } else {
                lastResolvedPos = -1;
            }
        }

        // Highlight every note whose source position falls inside a selected range
        // (an editor selection, possibly several ranges for a multi-cursor select).
        function highlightRange(ranges) {
            clearHighlights();
            // Over the index's distinct positions rather than every drawn element:
            // a chord's heads, dots and accidentals all carry the same offset, so
            // the element list is several times longer than the answer needs.
            sourcePositions();
            const positions = [];
            for (const pos of posSorted) {
                for (let i = 0; i < ranges.length; i++) {
                    if (pos >= ranges[i][0] && pos < ranges[i][1]) {
                        positions.push(pos);
                        break;
                    }
                }
            }
            if (positions.length > 0) {
                highlightPositions(positions);
            }
            lastResolvedPos = -1;   // a range has no single resolved note
        }

        // The marks the scan view's slider hides (*Marks below*), hidden here too (owner's
        // request 2026-10-12): every piece of a mark carries its key in data-todo, so one
        // stylesheet hides the ring, the memo and its leader of each key, and keeps doing so
        // across re-renders. The music glyph a mark rings (text.music) is never hidden.
        function hideTodos(keys) {
            let style = document.getElementById('hiddenTodos');
            if (!style) {
                style = document.createElement('style');
                style.id = 'hiddenTodos';
                document.head.appendChild(style);
            }
            style.textContent = hiddenTodoCss(keys);
        }
        function hiddenTodoCss(keys) {
            const rules = [];
            for (const key of keys) {
                const k = '[data-todo="' + String(key).replace(/["\\\\]/g, '') + '"]';
                rules.push('#svgContainer path' + k, '#svgContainer ellipse' + k, '#svgContainer rect' + k,
                    '#svgContainer line' + k, '#svgContainer text' + k + ':not(.music)');
            }
            return rules.length === 0 ? '' : rules.join(', ') + ' { display: none; }';
        }

        // Drag the sheet to scroll it (owner request 2026-10-12). A press becomes a drag only
        // once the mouse has moved SLOP pixels, so a click on a note still jumps to the editor
        // (or seeks, or extends the selection), and the click a drag ends with is swallowed
        // here, in the capture phase, before the document's click handler sees it. Only the
        // left button of a mouse with no modifier: shift-click selects, a touch or pen
        // scrolls natively, and a press on a scroll bar or a control is left alone.
        function installDragScroll(scroller, doc) {
            const SLOP = 4;
            let press = null;
            let dragged = false;
            scroller.addEventListener('pointerdown', (e) => {
                // Every click starts with a press, so a flag a drag left behind (its click
                // never came) cannot swallow the next real click.
                dragged = false;
                press = null;
                if (e.pointerType !== 'mouse' || e.button !== 0
                    || e.shiftKey || e.ctrlKey || e.metaKey || e.altKey) return;
                const r = scroller.getBoundingClientRect();
                if (e.clientX - r.left >= scroller.clientLeft + scroller.clientWidth
                    || e.clientY - r.top >= scroller.clientTop + scroller.clientHeight) return;
                if (e.target && e.target.closest && e.target.closest('button, input, select, textarea, a')) return;
                press = { id: e.pointerId, x: e.clientX, y: e.clientY, left: scroller.scrollLeft, top: scroller.scrollTop };
            });
            scroller.addEventListener('pointermove', (e) => {
                if (press === null || e.pointerId !== press.id) return;
                const dx = e.clientX - press.x, dy = e.clientY - press.y;
                if (!dragged) {
                    if (Math.abs(dx) < SLOP && Math.abs(dy) < SLOP) return;
                    dragged = true;
                    scroller.setPointerCapture(e.pointerId);
                    scroller.classList.add('dragging');
                }
                scroller.scrollLeft = press.left - dx;
                scroller.scrollTop = press.top - dy;
            });
            const release = (e) => {
                if (press === null || e.pointerId !== press.id) return;
                press = null;
                scroller.classList.remove('dragging');
            };
            scroller.addEventListener('pointerup', release);
            scroller.addEventListener('pointercancel', (e) => { release(e); dragged = false; });
            doc.addEventListener('click', (e) => {
                if (!dragged) return;
                dragged = false;
                e.stopImmediatePropagation();
                e.preventDefault();
            }, true);
        }

        // A bar of a staff is a click target too (owner request 2026-10-14, after the OMR scan
        // view's bars): a click that lands on no data-pos element but inside a bar goes to where
        // that bar is written — the server's rect.bar-staff boxes carry it (data-start). They
        // take no pointer events, so a note keeps its own click and the sheet its drag; the box
        // is found here by its geometry. A click beside a staff (a lyric line, a chord name, the
        // gap to the next staff) takes the nearest staff of the same bar, up to two staff
        // heights away.
        // A bar's notes as a selection: the first and last data-pos inside its written stretch
        // (data-span), or its start when it holds none.
        // What ▶ plays of a score selection: the WHOLE bars it lights (owner's report
        // 2026-10-25: a selection made from a note in bar 1 to a note in bar 4 lit four bars and
        // played from that note to that one — three beats short at either end of the loop).
        function selectedBarsSpan() {
            let lo = Infinity, hi = -Infinity;
            for (const r of svgContainer.querySelectorAll('rect.bar-staff.selected[data-span]')) {
                const span = r.getAttribute('data-span').split(' ').map(Number);
                if (span.length !== 2 || isNaN(span[0]) || isNaN(span[1])) continue;
                lo = Math.min(lo, span[0]);
                hi = Math.max(hi, span[1] - 1);
            }
            return lo <= hi ? [lo, hi] : [aiRangeLo, aiRangeHi];
        }
        function barNoteRange(r) {
            const span = (r.getAttribute('data-span') || '').split(' ').map(Number);
            const start = parseInt(r.getAttribute('data-start'), 10);
            let lo = -1, hi = -1;
            if (span.length === 2 && !isNaN(span[0]) && !isNaN(span[1])) {
                for (const n of svgContainer.querySelectorAll('[data-pos]')) {
                    const p = parseInt(n.getAttribute('data-pos'), 10);
                    if (p >= span[0] && p < span[1]) {
                        if (lo < 0 || p < lo) lo = p;
                        if (p > hi) hi = p;
                    }
                }
            }
            return lo >= 0 ? [lo, hi] : (isNaN(start) ? null : [start, start]);
        }
        // Extends the selection from the anchor to a bar: through its last note when the bar
        // lies after the anchor, from its first when before.
        function extendSelectionToBar(r) {
            const range = barNoteRange(r);
            if (!range) return;
            if (aiAnchorPos < 0) aiAnchorPos = range[0];
            aiSetSelection(aiAnchorPos, range[1] >= aiAnchorPos ? range[1] : range[0]);
        }
        function barStaffAt(clientX, clientY) {
            let best = null, bestDist = Infinity;
            for (const r of svgContainer.querySelectorAll('rect.bar-staff')) {
                const b = r.getBoundingClientRect();
                if (clientX < b.left || clientX > b.right) continue;
                const d = clientY < b.top ? b.top - clientY : clientY > b.bottom ? clientY - b.bottom : 0;
                if (d <= 2 * b.height && d < bestDist) { best = r; bestDist = d; }
            }
            return best;
        }
        let hoveredBar = null;
        function hoverBar(r) {
            if (r === hoveredBar) return;
            if (hoveredBar) hoveredBar.classList.remove('hover');
            hoveredBar = r;
            if (r) r.classList.add('hover');
            mainContent.classList.toggle('over-bar', r !== null);
            // …and a scan view of the score lights the same bar (omrScan previewHoversBar).
            vscode.postMessage({ type: 'hoverBar', start: r ? parseInt(r.getAttribute('data-start'), 10) : null });
        }
        // The bar the editor's caret is in stays lit (owner's request 2026-10-14, as the scan view
        // lights it): every bar whose written stretch (data-span: its first note to the bar line
        // closing it) holds the caret — just after that bar line included — or the start of the
        // token under it; each printed copy of it. Kept across renders (the caret is re-applied
        // with the bar flags), and moved by an edit's caret movement. A caret before a bar's first
        // note on the line that bar starts on — on 'voice {', 'tuplet 3/2 {', 'break', the
        // indentation — lights that bar too (owner's report 2026-10-23: a bar that opens with
        // 'voice {' lit only once the caret reached its first note): when no bar holds the caret,
        // the first bar to start after it before the line ends (lineEnd, sent with the caret).
        let caretAt = -1, caretToken = -1, caretLineEnd = -1;
        function lightCaretBar(caret, tokenStart, lineEnd) {
            if (Number.isInteger(caret)) {
                caretAt = caret;
                caretToken = Number.isInteger(tokenStart) ? tokenStart : -1;
                caretLineEnd = Number.isInteger(lineEnd) ? lineEnd : -1;
            }
            for (const r of svgContainer.querySelectorAll('rect.bar-staff.caret')) r.classList.remove('caret');
            if (caretAt < 0) return;
            let lit = false, next = Infinity;
            const spans = [];
            for (const r of svgContainer.querySelectorAll('rect.bar-staff[data-span]')) {
                const span = r.getAttribute('data-span').split(' ');
                const from = parseInt(span[0], 10), to = parseInt(span[1], 10);
                spans.push([r, from]);
                if ((caretAt >= from && caretAt <= to + 1) || (caretToken >= from && caretToken <= to)) { r.classList.add('caret'); lit = true; }
                else if (from > caretAt && from <= caretLineEnd && from < next) next = from;
            }
            if (!lit && next !== Infinity) {
                for (const [r, from] of spans) if (from === next) r.classList.add('caret');
            }
        }
        // The scan view's bar under the mouse, lit here: every bar whose first note is written
        // on the line the reader wrote it on, [from, to] (omrScan lineOfScanBar); none without.
        function lightPeerBars(from, to) {
            for (const r of svgContainer.querySelectorAll('rect.bar-staff.peer')) r.classList.remove('peer');
            if (!Number.isInteger(from) || !Number.isInteger(to)) return;
            for (const r of svgContainer.querySelectorAll('rect.bar-staff')) {
                const s = parseInt(r.getAttribute('data-start'), 10);
                if (s >= from && s <= to) r.classList.add('peer');
            }
        }
        function installBarHover() {
            let pending = null;
            svgContainer.addEventListener('mousemove', (e) => {
                pending = e;
                requestAnimationFrame(() => {
                    if (pending === null) return;
                    const ev = pending;
                    pending = null;
                    const t = ev.target;
                    const onNote = t && t.hasAttribute && t.hasAttribute('data-pos');
                    hoverBar(onNote || ev.buttons !== 0 || ev.shiftKey || mainContent.classList.contains('dragging')
                        ? null : barStaffAt(ev.clientX, ev.clientY));
                });
            });
            svgContainer.addEventListener('mouseleave', () => { pending = null; hoverBar(null); });
        }

        // Groups all elements sharing one data-pos into printed instances
        // (phrase copies): elements of ONE instance sit within a chord's
        // footprint; other copies live in other measures/systems. Bands by
        // y-gap (systems are far apart), then splits by x-gap. Returned in
        // reading order = chronological within the part's staff.
        function clusterInstances(matches) {
            // DOM order IS drawing order (measure by measure, system by
            // system) - i.e. chronological within the part's staff. Keep it,
            // and only SPLIT when the next element sits in another bar, or far
            // from the previous one in its bar (a phrase written twice in one
            // bar). The earlier y-band sort could merge two systems through
            // ledger-note ys and then x-sorting interleaved their copies out of
            // time order.
            // ⚠️ WHERE IT IS DRAWN ON THE PAGE, NOT ITS NEIGHBOUR IN THE DOM, makes
            // a copy (owner's reports 2026-10-29: in "form { A B A }" playing A lit
            // both copies, and a song's playback shook the view between pages).
            // getBBox is in the element's own frame and every system has its own,
            // so copies on two systems at one x stood 0 apart and were one copy;
            // and a page draws some heads again in its overlay group after its
            // systems (a hi-hat's), so the DOM is not in time order. A copy is the
            // elements in one bar-box (in time order, the DOM's order of the bars)
            // within 6 staff spaces of each other across; the old gaps stay for a
            // page with no bar-boxes.
            const bars = Array.from(svgContainer.querySelectorAll('rect.bar-box'));
            if (bars.length === 0) return clusterByGaps(matches);
            const boxes = bars.map(r => r.getBoundingClientRect());
            let unit = 1;
            try { const b0 = bars[0].getBBox(); if (b0.width > 0) unit = boxes[0].width / b0.width; } catch (e) { /* non-SVG */ }
            const byBar = new Map();
            const loose = [];
            let last = null;
            for (const el of matches) {
                const b = el.getBoundingClientRect();
                const x = b.left + 0.5, y = (b.top + b.bottom) / 2;
                // The bar whose column holds it, the nearest of them up or down: a box
                // spans the staff lines only, and a note under the staff is outside it.
                let bar = -1, nearest = Infinity;
                for (let i = 0; i < boxes.length; i++) {
                    const r = boxes[i];
                    if (x < r.left || x > r.right) continue;
                    const d = y < r.top ? r.top - y : y > r.bottom ? y - r.bottom : 0;
                    if (d < nearest) { nearest = d; bar = i; }
                }
                // In no bar's column (a mark beyond the staves): with the element before it.
                if (bar < 0) { if (last) last.els.push(el); else loose.push(el); continue; }
                let list = byBar.get(bar);
                if (!list) byBar.set(bar, list = []);
                let inst = list.find(c => Math.abs(c.x - b.left) <= 6 * unit);
                if (!inst) { inst = { x: b.left, els: [] }; list.push(inst); }
                inst.els.push(el);
                last = inst;
            }
            const instances = [];
            for (const bar of Array.from(byBar.keys()).sort((p, q) => p - q)) {
                for (const c of byBar.get(bar).sort((p, q) => p.x - q.x)) instances.push(c.els);
            }
            if (loose.length > 0) {
                if (instances.length > 0) instances[0].unshift.apply(instances[0], loose);
                else instances.push(loose);
            }
            return instances;
        }
        // The copies told apart by gaps in DOM order: a page drawn without bar-boxes.
        function clusterByGaps(matches) {
            const instances = [];
            let inst = null;
            let prev = null;
            for (const el of matches) {
                let x = 0, y = 0;
                try { const b = el.getBBox(); x = b.x; y = b.y; } catch (e) { /* non-SVG */ }
                if (!inst || Math.abs(x - prev.x) > 6 || Math.abs(y - prev.y) > 12) {
                    inst = [];
                    instances.push(inst);
                }
                inst.push(el);
                prev = { x: x, y: y };
            }
            return instances;
        }

        // Paints every element of every given data-pos (playback lights the
        // WHOLE onset: rh and lh notes striking together are different
        // source positions). Does NOT clear existing highlights.
        // Entries may be numbers (paint ALL copies - editor click sync) or
        // { pos, occ }: a phrase's every expansion shares ONE source
        // position, so copy #occ in document order (= chronological within
        // the part's staff) is the note actually sounding now.
        function highlightPositions(positions) {
            const color = getHighlightColor();
            const painted = [];
            for (const entry of positions) {
                const pos = typeof entry === 'object' ? entry.pos : entry;
                const occ = typeof entry === 'object' ? entry.occ : -1;
                // Every drawn element carrying this offset — as its data-pos OR as
                // a data-alt member (a barline that several written bars collapse
                // onto lights from any of their offsets) — from the index, which
                // already leaves out the click targets and occluders that must
                // never be recolored (see sourcePositions).
                sourcePositions();
                let matches = (posElements.get(pos) || []).slice();
                if (occ >= 0 && matches.length > 1) {
                    // Pick the occ-th printed INSTANCE — a chord's every head
                    // (plus dots/accidentals) shares one data-pos, so slicing
                    // by raw element index lit a single head and left the
                    // rest of the chord dark. Cluster by geometry instead.
                    const instances = clusterInstances(matches);
                    matches = instances[Math.min(occ, instances.length - 1)] || matches;
                }
                painted.push.apply(painted, matches);
                highlighted.push.apply(highlighted, matches);
                // A boxed mark (section/rehearsal) is a <rect> with a <text> label on
                // top. Recoloring both to the highlight color hides the label, so when
                // the group has a box we recolor the box only and leave its text its
                // own color (still raised above the box, so it stays readable).
                const hasBox = matches.some(el => el.tagName.toLowerCase() === 'rect');
                matches.forEach(el => {
                    el.classList.add('highlight');
                    const tag = el.tagName.toLowerCase();
                    // Save the shipped paint once (guard against re-highlight
                    // clobbering it with the highlight color) so clear can restore
                    // the real value, not SVG's default.
                    if (tag === 'line' || (el.getAttribute('fill') === 'none' && el.getAttribute('stroke'))) {
                        // A line, or an outline-only shape (a chord diagram's open-string
                        // ring): recolor the stroke — filling a ring would turn it into a dot.
                        if (el.__origStroke === undefined) el.__origStroke = el.getAttribute('stroke');
                        el.setAttribute('stroke', color);
                    } else if (tag === 'text' && hasBox) {
                        // leave the label text's fill untouched — readable on the box
                    } else {
                        if (el.__origFill === undefined) el.__origFill = el.getAttribute('fill');
                        el.setAttribute('fill', color);
                    }
                    // SVG has no z-index — z-order is document order. The stem
                    // (and beam) are drawn after the notehead, so a recolored
                    // head would be partly covered by the black stem. Raise the
                    // highlighted element to the end of its group so it paints
                    // on top; remember its slot to restore on clear.
                    if (!el.__origParent && el.parentNode) {
                        el.__origParent = el.parentNode;
                        el.__origNextSibling = el.nextSibling;
                        el.parentNode.appendChild(el);
                    }
                });
            }
            return painted;
        }

        function escapeHtml(text) {
            return text
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;');
        }

        function updateRenderSelect(renders, selectedRender) {
            // One entry per score. Display the LABEL (the score's name, or (Default) for the
            // unnamed one). The VALUE is the output name / preview selector: empty for the
            // unnamed score, which exports to the source .lys filename.
            renderSelect.innerHTML = '';
            const scoreRenders = (renders || []).filter(r => r.Type === 'score');
            scoreRenders.forEach(render => {
                const option = document.createElement('option');
                option.value = render.Filename;
                option.textContent = render.Name;
                if (option.value === (selectedRender || '')) {
                    option.selected = true;
                }
                renderSelect.appendChild(option);
            });
            fitSelectToSelection();
        }

        // The select is sized to its CONTENT. Left to itself a native <select>
        // reserves room for its widest option, so one long score name leaves a
        // wide empty gap next to every short one.
        //
        // The width is measured by an off-screen twin rather than computed from
        // the text: a select's box is text + padding + border + the dropdown
        // arrow Chromium draws itself, and only Chromium knows what that arrow
        // costs. The twin carries the same class (so the same font and padding)
        // and is left at its auto width, which IS the width that fits its
        // options exactly. Absolutely positioned, so it never disturbs the
        // toolbar it has to live in to inherit those styles.
        const selectSizer = document.createElement('select');
        selectSizer.setAttribute('aria-hidden', 'true');
        selectSizer.tabIndex = -1;
        selectSizer.style.cssText =
            'position:absolute;visibility:hidden;pointer-events:none;top:0;left:0;width:auto;';
        document.querySelector('.toolbar').appendChild(selectSizer);

        // Width the twin settles on once it holds exactly these option labels.
        function widthFor(labels) {
            selectSizer.innerHTML = '';
            for (const label of labels) {
                const o = document.createElement('option');
                o.textContent = label;
                selectSizer.appendChild(o);
            }
            return selectSizer.offsetWidth;
        }

        function fitSelectToSelection() {
            const opt = renderSelect.options[renderSelect.selectedIndex];
            renderSelect.style.width = widthFor([opt ? opt.textContent : '']) + 'px';
        }

        // The native popup takes its width from the element, so the list would be
        // clipped to the (short) closed width. Widening just before it opens —
        // mousedown and the keyboard open keys both precede the popup — gives the
        // list room for the longest name; it shrinks back on close.
        function fitSelectToWidest() {
            const labels = Array.from(renderSelect.options).map(o => o.textContent);
            renderSelect.style.width = widthFor(labels) + 'px';
        }

        renderSelect.addEventListener('mousedown', fitSelectToWidest);
        renderSelect.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') {
                fitSelectToSelection();   // closed without choosing
            } else if (e.key === 'Enter' || e.key === ' ' || e.key === 'ArrowDown' || e.key === 'ArrowUp') {
                fitSelectToWidest();
            }
        });
        // change = a choice was made, blur = the popup went away with the focus.
        renderSelect.addEventListener('change', fitSelectToSelection);
        renderSelect.addEventListener('blur', fitSelectToSelection);
        // Fit the placeholder too: the first score list only arrives with the
        // first render, and until then the box would sit at its auto width.
        fitSelectToSelection();

        renderSelect.addEventListener('change', () => {
            vscode.postMessage({ type: 'selectRender', renderName: renderSelect.value });
        });

        // A narrow preview wraps the header's groups onto rows: a separator whose two groups
        // stand on different rows would dangle at a row's end, so it hides.
        (() => {
            const bar = document.querySelector('.toolbar');
            const tidy = () => {
                // One at a row's start leaves the layout (the row above had no room for it, so
                // it has none for the group either); one at a row's end only goes unseen.
                for (const sep of bar.querySelectorAll(':scope > .sep')) {
                    sep.style.display = '';
                    sep.style.visibility = '';
                }
                for (const sep of bar.querySelectorAll(':scope > .sep')) {
                    const a = sep.previousElementSibling, b = sep.nextElementSibling;
                    if (!a || !b || Math.abs(a.offsetTop - b.offsetTop) <= 4) continue;
                    if (Math.abs(sep.offsetTop - b.offsetTop) <= 4) sep.style.display = 'none';
                    else sep.style.visibility = 'hidden';
                }
            };
            if (window.ResizeObserver) new ResizeObserver(tidy).observe(bar);
            tidy();
        })();
        // The page's own tooltip (owner's report 2026-10-25: the browser's, which a title
        // shows, was too small to read and no CSS reaches it): the same words, in VS Code's
        // hover font and colours. A title — an attribute, or an SVG <title> child (the
        // tessitura chart's) — moves to data-tip when the pointer first comes over it, so the
        // browser's never shows; one set again later (updateAiButton) moves again. A red microphone
        // says what went wrong at once, without the wait (owner's request 2026-11-09, session 1191).
        let refreshTip = () => {};
        (() => {
            const tip = document.createElement('div');
            tip.className = 'tip';
            tip.hidden = true;
            document.body.appendChild(tip);
            let timer = null, shownFor = null;
            const tipOf = (el) => {
                for (let e = el; e && e !== document.body; e = e.parentNode) {
                    if (!e.getAttribute) continue;
                    if (e.hasAttribute('title')) {
                        e.setAttribute('data-tip', e.getAttribute('title'));
                        e.removeAttribute('title');
                    }
                    // Not the score's own: a drawing's <title> names the drawing, not a control.
                    const t = !svgContainer.contains(e) && e.querySelector && e.querySelector(':scope > title');
                    if (t) { e.setAttribute('data-tip', t.textContent); t.remove(); }
                    if (e.hasAttribute('data-tip') && e.getAttribute('data-tip')) return e;
                }
                return null;
            };
            const hide = () => { clearTimeout(timer); timer = null; tip.hidden = true; shownFor = null; };
            const show = (el) => {
                tip.textContent = el.getAttribute('data-tip');
                tip.hidden = false;
                const r = el.getBoundingClientRect();
                const w = tip.offsetWidth, h = tip.offsetHeight;
                let x = Math.min(Math.max(4, r.left + r.width / 2 - w / 2), window.innerWidth - w - 4);
                let y = r.bottom + 6;
                if (y + h > window.innerHeight - 4) y = Math.max(4, r.top - h - 6);
                tip.style.left = x + 'px';
                tip.style.top = y + 'px';
                shownFor = el;
            };
            const atOnce = (el) => el.dataset && el.dataset.state === 'failed' && el.classList.contains('voice-badge');
            document.addEventListener('pointerover', (ev) => {
                const el = tipOf(ev.target);
                if (el === shownFor) return;
                hide();
                if (!el) return;
                if (atOnce(el)) { show(el); return; }
                timer = setTimeout(() => show(el), 500);
            });
            // The element's words changed under a resting pointer, which sends no pointerover.
            refreshTip = (el) => {
                const showing = shownFor === el;
                const over = !el.hidden && el.matches(':hover') && (showing || atOnce(el));
                if (showing || over) hide();
                if (over && tipOf(el) === el) show(el);
            };
            document.addEventListener('pointerdown', hide, true);
            document.addEventListener('keydown', hide, true);
            document.addEventListener('scroll', hide, true);
            window.addEventListener('blur', hide);
            document.documentElement.addEventListener('pointerleave', hide);
        })();
        const exportBtn = document.getElementById('exportBtn');
        exportBtn.addEventListener('click', () => {
            vscode.postMessage({ type: 'export', renderName: renderSelect.value });
        });

        // ---- Playback and audition ----
        // The sound is the SoundFont synth (out/synth/preview-synth.js = window.LysSynth,
        // spessasynth_lib's worklet; docs/playback-design.md), fed the server's note list a
        // few hundred milliseconds ahead of the clock; the oscillator patches further down
        // are the fallback while no sound font is loaded. ONE AudioContext for the page,
        // made at boot and never closed. The browser's autoplay policy lets it run only
        // once the user has clicked or typed in THIS document (the editor's keys do not
        // count): until then it stays 'suspended' — so it is resumed from the first gesture
        // here and asked to resume at every strike, and if it is still not running shortly
        // after, the host is told once (audioBlocked) and the status bar says to click the
        // preview. The Play button is a click, so it always gets through.
        let audioCtx = null;
        let audioBlockedTold = false;
        function audioContext() {
            if (!audioCtx || audioCtx.state === 'closed') { audioCtx = new AudioContext(); }
            return audioCtx;
        }
        function audio() {
            const ctx = audioContext();
            if (ctx.state !== 'running') {
                ctx.resume().catch(() => { /* reported below */ });
                setTimeout(() => {
                    if (audioCtx && audioCtx.state !== 'running' && !audioBlockedTold) {
                        audioBlockedTold = true;
                        vscode.postMessage({ type: 'audioBlocked', state: audioCtx.state });
                    }
                }, 300);
            }
            return ctx;
        }
        function resumeAudioOnGesture() {
            if (audioCtx && audioCtx.state === 'suspended') {
                audioCtx.resume().then(() => { audioBlockedTold = false; }).catch(() => { /* noop */ });
            }
        }
        window.addEventListener('pointerdown', resumeAudioOnGesture, true);
        window.addEventListener('keydown', resumeAudioOnGesture, true);

        function flashInfo(text, ms) {
            zoomInfo.textContent = text;
            zoomInfo.classList.add('visible');
            setTimeout(() => zoomInfo.classList.remove('visible'), ms || 2500);
        }

        // The SoundFont synth, created once at boot so the font is parsed before the first
        // Play (a suspended context loads it fine). window.lysSynthConfig carries the URLs
        // the extension gave this page (the head script); no bundle or no processor URL
        // means no synth, and the page plays its oscillator patches.
        let sfSynth = null;
        let noSynthTold = false;
        function soundFontSynth() {
            if (sfSynth) { return sfSynth; }
            const cfg = window.lysSynthConfig;
            if (!window.LysSynth || !cfg || !cfg.processor) {
                if (!noSynthTold) {
                    noSynthTold = true;
                    vscode.postMessage({ type: 'synthStatus', state: 'failed', detail: !window.LysSynth
                        ? 'the synth bundle did not load (window.LysSynth is undefined)' : 'no processor URL' });
                }
                return null;
            }
            sfSynth = window.LysSynth.create(audioContext(), cfg.processor, cfg.soundFont || '', (status) => {
                vscode.postMessage({ type: 'synthStatus', state: status.state, detail: status.detail || '' });
                if (status.state === 'failed') {
                    flashInfo('Sound: ' + (status.detail || 'the sound font did not load'), 4000);
                }
            });
            return sfSynth;
        }
        setTimeout(() => {
            try { soundFontSynth(); }
            catch (e) { vscode.postMessage({ type: 'synthStatus', state: 'failed', detail: String(e) }); }
        }, 0);
        // lilysharp.playback.soundFont changed: the extension sends the new URL ('' = none).
        function setSoundFont(uri) {
            if (!window.lysSynthConfig) { window.lysSynthConfig = {}; }
            window.lysSynthConfig.soundFont = uri || '';
            const sf = soundFontSynth();
            if (sf) { sf.setSoundFont(uri || ''); }
        }
        // MIDI out (docs/playback-design.md §6): the name of the port the sounds go to —
        // lilysharp.playback.midiOutput — or '' for the synth here. The page still decides WHAT
        // plays (the note list, the offset, an audition's pitches); the server is the port
        // and answers with the wall-clock time of the playback's zero, which the highlight
        // follows (Date.now() is the same clock). A changed setting stops what plays.
        let midiOutput = (window.lysSynthConfig && window.lysSynthConfig.midiOutput) || '';
        function setMidiOutput(name) {
            stopPlayback();
            stopHeldNote();
            midiOutput = name || '';
        }

        // ---- The singing voice (docs/singing-design.md §5): the host renders each sung part's
        // words through the singing engine and hands the WAV here (the 'voice' message); a Play
        // starts the buffer at the same clock as the synth's notes, through a limiter of its own,
        // IN PLACE OF the part's instrument (owner's decision 2026-11-08: one sound a part — a
        // part a lyrics row sings is a singer, and its instrument is the stand-in while there is
        // no voice: no engine, kana missing, not rendered yet, or the menu's ♪ switched to the
        // instrument). A voice is for one practice speed; at another the instrument plays until
        // the host has sung it anew. A stretch's voice stops where the stretch ends and a loop
        // sings it round after round (LysSynth.voiceCues); a candidate's notes play without the
        // voice (voicePlan null).
        const voices = new Map();           // part -> { buffer, speed, key }
        let voiceSources = [];
        // Where the voice stands for the played list: null = no voice; { pieces: null } the whole
        // piece from its 0; { pieces, rounds } a stretch — the score's times it plays, in order
        // (spanOf) — laid out rounds times (the loop's, looped).
        let voicePlan = { pieces: null, rounds: 1 };
        let voiceOut = null;
        function voiceOutput(ctx) {
            if (!voiceOut) {
                const gain = ctx.createGain();
                gain.gain.value = 0.9;
                const limiter = ctx.createDynamicsCompressor();
                limiter.threshold.value = -10; limiter.knee.value = 12; limiter.ratio.value = 12;
                limiter.attack.value = 0.002; limiter.release.value = 0.12;
                gain.connect(limiter);
                limiter.connect(ctx.destination);
                voiceOut = gain;
            }
            return voiceOut;
        }
        // A chosen voice's short phrase: the next one cuts it off (session 1170).
        let voiceSampleSource = null;
        function playVoiceSample(message) {
            const got = Date.now();
            const bytes = message.bytes;
            if (!bytes || !bytes.buffer) { return; }
            const copy = bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength);
            const ctx = audio();
            ctx.decodeAudioData(copy).then(buffer => {
                const decoded = Date.now();
                if (voiceSampleSource) { try { voiceSampleSource.stop(); } catch (e) { /* ended */ } }
                voiceSampleSource = null;
                // Not while the page's audio may not run (owner's report 2026-11-11: a voice taken
                // before the preview had been clicked said nothing, and its phrase came out over
                // the song at the next Play). A source started on a suspended context is not
                // dropped: its start is the context's frozen time, so it sounds the moment a
                // click — the Play button's — lets the context run. The phrase is for the moment
                // the voice is chosen; later it is noise. audio() has asked the context to resume
                // and tells the host when it will not (the status bar says to click the preview).
                // Nor over a Play that began while the phrase was on its way.
                if (ctx.state !== 'running' || playClock !== null || midiPlaying) {
                    vscode.postMessage({ type: 'webviewPerf', text: 'voice sample ' + message.id + ': not said ('
                        + (ctx.state !== 'running' ? 'AudioContext ' + ctx.state : 'the score is playing') + ')' });
                    return;
                }
                const src = ctx.createBufferSource();
                src.buffer = buffer;
                src.connect(voiceOutput(ctx));
                src.start();
                voiceSampleSource = src;
                const started = Date.now();
                vscode.postMessage({ type: 'webviewPerf', text: 'voice sample ' + message.id
                    + ': delivery ' + (got - message.sentAt) + ' ms, decode ' + (decoded - got) + ' ms, start ' + (started - decoded) + ' ms, '
                    + (started - message.pressedAt) + ' ms from the key; AudioContext ' + ctx.state
                    + ', output latency ' + Math.round(((ctx.baseLatency || 0) + (ctx.outputLatency || 0)) * 1000) + ' ms' });
            }, e => {
                vscode.postMessage({ type: 'webviewPerf', text: 'voice sample could not be decoded: ' + e });
            });
        }
        function setVoice(message) {
            const bytes = message.bytes;
            if (!bytes || !bytes.buffer) { return; }
            if (!sungNowHas(message.part)) { return; }  // a voice of a part this score does not sing (session 1160)
            const copy = bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength);
            audioContext().decodeAudioData(copy).then(buffer => {
                // The buffer begins lead seconds before the score's 0 (VOICE_LEAD_S), with the
                // first syllable's consonant in that lead: pre is how much of it sounds before the
                // beat — what a Play from the top lets the instruments wait for (startPlayback).
                // (No backticks in this script: it lives in a template literal.)
                const lead = message.lead || 0;
                const s = lead > 0 && window.LysSynth && window.LysSynth.soundStart
                    ? window.LysSynth.soundStart(buffer.getChannelData(0), buffer.sampleRate, lead) : null;
                const sungBefore = sungTracks();
                voices.set(message.part, { buffer, speed: message.speed || 1, key: message.key, lead, pre: s === null ? 0 : Math.max(0, lead - s) });
                setVoiceState(message.part, 'ready');
                joinVoice(message.part, message.entries, sungBefore);
            }, e => {
                vscode.postMessage({ type: 'webviewPerf', text: 'voice: ' + message.part + ' could not be decoded: ' + e });
                setVoiceState(message.part, 'failed', 'the WAV could not be decoded');
            });
        }
        // pending: another document's voices are on their way (the host asked for them) — the
        // microphone stays grey as "preparing" until 'voiceParts' names them (or names none).
        // (No backticks in this script: it lives in a template literal.)
        // The placeholder lives until the host names the parts (voiceParts) or says it cannot
        // (voiceSettled) - and at most PLACEHOLDER_MS, whatever happened to that answer (owner's
        // report 2026-11-09, session 1161: a file with no lyrics opened with the microphone
        // pulsing for ever; the server answers in milliseconds, so a placeholder older than this
        // is a lost answer, not a slow one).
        const PLACEHOLDER_MS = 10000;
        let placeholderTimer = null;
        function clearVoices(pending) {
            stopVoices(); voices.clear(); voiceStates.clear();
            sungNow = null;
            if (placeholderTimer) { clearTimeout(placeholderTimer); placeholderTimer = null; }
            if (pending) {
                voiceStates.set('', { state: 'preparing' });
                placeholderTimer = setTimeout(() => {
                    placeholderTimer = null;
                    if (voiceStates.has('')) {
                        vscode.postMessage({ type: 'webviewPerf', text: 'voice: the placeholder outlived its answer (' + PLACEHOLDER_MS + ' ms) - settled' });
                        settleVoices();
                    }
                }, PLACEHOLDER_MS);
            }
            renderVoiceBadge();
        }
        // The parts the score sings now ('voiceParts', each time the host asks lilysharp/sung): a
        // voice or a state of any other part is from an earlier score or an earlier text of this
        // one, and goes — so a score with no lyrics shows no microphone. Kept (sungNow) so that a
        // voice or a state arriving late for any other part is refused too (session 1160: the
        // host's late 'preparing' after a switch to a file with no lyrics pulsed for ever).
        let sungNow = null;     // null: not named yet since the last clearVoices
        function keepVoices(parts) {
            const keep = new Set(parts || []);
            sungNow = keep;
            if (placeholderTimer) { clearTimeout(placeholderTimer); placeholderTimer = null; }
            for (const part of [...voices.keys()]) { if (!keep.has(part)) { voices.delete(part); } }
            for (const part of [...voiceStates.keys()]) { if (!keep.has(part)) { voiceStates.delete(part); } }
            renderVoiceBadge();
        }
        function sungNowHas(part) { return sungNow === null || sungNow.has(part); }
        // The pane's sign of the voices (owner's request 2026-11-08): what the host says of each
        // (preparing, failed — the 'voiceState' message) and what this page decoded (ready), shown
        // as one badge beside the transport (playbackCore.voiceBadge) and as a sign on each
        // voice's toggle in the practice menu.
        const voiceStates = new Map();      // voice key -> { state, detail }
        let menuVoiceKeys = [];             // the voices the practice menu lists (partsData)
        let menuVoiceNames = {};            // what its lines call them
        function setVoiceState(part, state, detail, estimateMs, singing, key) {
            if (!sungNowHas(part)) { return; }      // a part this score does not sing: a late word of another text
            if (state === 'ready' || state === 'failed') { voiceBarFrom = Date.now(); }     // the next one's time begins
            // A voice told waiting or preparing is of a text that is gone (the host compares the
            // keys): the buffer goes with it, so a Play meanwhile plays the instrument, not the
            // old words (session 1148).
            if (state === 'waiting' || state === 'preparing') { voices.delete(part); }
            const before = voiceStates.get(part) || {};
            // What the engine said of the render (told) stays while the word is of the same render
            // (its key): a Play pressed meanwhile repeats "preparing", and the count must not go back.
            const told = state === 'preparing' && before.told && key && before.told.key === key ? before.told : undefined;
            voiceStates.set(part, { state, detail, estimateMs: estimateMs || before.estimateMs, singing: window.LysSynth.voiceStillSinging(before.singing, state, singing), told });
            renderVoiceBadge();
        }
        // The engine's own count of a voice it is singing (the host's 'voiceProgress'; owner's
        // request 2026-11-11): the seconds to sing ANEW and how many are done. The bar is counted
        // on these, not on the score's length.
        function setVoiceTold(message) {
            const s = voiceStates.get(message.part);
            if (!s || s.state !== 'preparing' || !sungNowHas(message.part)) { return; }
            const now = Date.now();
            const same = s.told && s.told.key === message.key && message.sung > 0;
            s.told = { key: message.key, toSing: message.toSing, sung: message.sung, at: now, firstAt: same ? s.told.firstAt : now, msPerSecond: message.msPerSecond || 0 };
            s.singing = true;
            renderVoiceBadge();
        }
        // The bar under Play, for as long as the engine is singing a voice (voiceBarShown).
        let voiceBarTimer = null, voiceBarStarted = 0, voiceBarFrom = 0;
        // The voices this showing of the bar counts (voiceRound: the ones being sung now, not the
        // ones that were ready before it came up), and how far it has been drawn: it never goes back.
        let voiceBarRound = [], voiceBarDrawn = 0;
        function voiceBarParts() {
            return [...voiceStates].map(([part, s]) => ({ part, state: s.state, singing: s.singing, estimateMs: s.estimateMs, told: s.told }));
        }
        function paintVoiceBar() {
            const bar = document.getElementById('voiceBar');
            const now = Date.now();
            bar.hidden = false;
            const parts = voiceBarParts();
            voiceBarRound = window.LysSynth.voiceRound(voiceBarRound, parts, true);
            voiceBarDrawn = Math.max(voiceBarDrawn, window.LysSynth.voiceBar(parts.filter(p => voiceBarRound.includes(p.part)), now, now - voiceBarFrom));
            bar.firstElementChild.style.width = Math.round(voiceBarDrawn * 100) + '%';
            paintVoiceRows();
        }
        // Each voice's own bar in the practice menu, for as long as the engine is singing it.
        function paintVoiceRows() {
            const states = voiceBarParts();
            for (const btn of document.querySelectorAll('#partsList button.voice')) {
                const bar = btn.querySelector('.voice-row-bar');
                if (!bar) { continue; }
                const done = window.LysSynth.voiceRowProgress(states, btn.dataset.part, Date.now() - voiceBarFrom, Date.now());
                bar.hidden = done === null;
                bar.firstElementChild.style.width = done === null ? '0' : Math.round(done * 100) + '%';
            }
        }
        function renderVoiceBar(singing) {
            const bar = document.getElementById('voiceBar');
            if (!singing) {
                if (voiceBarTimer) { clearInterval(voiceBarTimer); voiceBarTimer = null; }
                voiceBarStarted = 0;
                voiceBarRound = [];
                voiceBarDrawn = 0;
                bar.hidden = true;
                bar.firstElementChild.style.width = '0';
                return;
            }
            if (!voiceBarStarted) {
                voiceBarStarted = voiceBarFrom = Date.now();
                voiceBarRound = window.LysSynth.voiceRound([], voiceBarParts(), false);
                voiceBarDrawn = 0;
            }
            if (!voiceBarTimer) { voiceBarTimer = setInterval(paintVoiceBar, 100); }
            paintVoiceBar();
        }
        // The host asked for the voices and got none to name (the text has an error, the server
        // is not up): the nameless placeholder of clearVoices goes, what is known stays.
        function settleVoices() {
            if (placeholderTimer) { clearTimeout(placeholderTimer); placeholderTimer = null; }
            voiceStates.delete('');
            renderVoiceBadge();
        }
        function renderVoiceBadge() {
            const badge = document.getElementById('voiceBadge');
            const all = [...voiceStates].map(([part, s]) => ({ part: menuVoiceNames[part] || part, state: s.state, detail: s.detail }));
            const b = window.LysSynth.voiceBadge(all);
            renderVoiceBar(window.LysSynth.voiceBarShown([...voiceStates.values()]));
            badge.hidden = !b;
            if (b) {
                badge.dataset.state = b.state;
                badge.title = b.title;
                // (The page script lives in a template literal: a backslash-n here would be a real newline.)
                badge.setAttribute('aria-label', b.title.split(String.fromCharCode(10))[0]);
            } else {
                // Nothing to say: no state left on the element either (session 1162).
                delete badge.dataset.state;
                badge.removeAttribute('title');
                badge.removeAttribute('data-tip');
                badge.setAttribute('aria-label', 'Voice');
            }
            refreshTip(badge);
            for (const btn of document.querySelectorAll('#partsList button.voice')) {
                const s = voiceStates.get(btn.dataset.part);
                const mic = btn.querySelector('.voice-badge');
                if (mic && s) { mic.dataset.state = s.state; } else if (mic) { delete mic.dataset.state; }
                const base = btn.dataset.title || '';
                btn.title = s ? base + (s.state === 'ready' ? ' — ready' : s.state === 'preparing' ? ' — being sung…' : s.state === 'waiting' ? ' — not sung yet' : ' — not sung' + (s.detail ? ': ' + s.detail : '')) : base;
            }
            paintVoiceRows();
        }
        // The badge sits on the corner of the Play button and takes the clicks that land there: they
        // are Play's (owner's report 2026-11-09, session 1149: a Play on that corner opened the
        // practice menu, which the badge used to open, instead of playing). The badge is a status,
        // not a button; its title says what it knows.
        document.getElementById('voiceBadge').addEventListener('click', () => document.getElementById('playBtn').click());
        // A part's voices, one for each singer line: the part's name for the first, part#2 and on for the rest.
        function voiceKeysOf(part) { return [...voices.keys()].filter(k => k === part || k.startsWith(part + '#')); }
        // The voices that sound now, each with its own switch and fader. A voice whose fader stands
        // at 0 is as switched off: its instrument stands in. The score's own voice sings unless
        // switched off; one the score does not sing only when the menu turned it on (voicesOn,
        // session 1163).
        function soundingVoices() {
            const keys = [];
            if (voicePlan === null || !rawParts) { return keys; }
            for (const name of rawParts) {
                for (const key of voiceKeysOf(name)) {
                    const v = voices.get(key);
                    const own = !rawSung || rawSung.includes(name);
                    const on = own ? !practice.voicesOff.includes(key) : practice.voicesOn.includes(key);
                    if (Math.abs(v.speed - practice.speed) < 1e-6 && on && levelIn(practice.voiceLevels, key) > 0) { keys.push(key); }
                }
            }
            return keys;
        }
        // The tracks a voice stands in for now: a part with at least one voice sounding.
        function sungTracks() {
            const set = new Set();
            if (!rawParts) { return set; }
            const sounding = soundingVoices();
            rawParts.forEach((name, i) => { if (sounding.some(k => k === name || k.startsWith(name + '#'))) { set.add(i); } });
            return set;
        }
        // Every voice the menu lists for a part, whether its sound has come yet.
        function menuVoicesOf(part) {
            const listed = menuVoiceKeys.filter(k => k === part || k.startsWith(part + '#'));
            return listed.length > 0 ? listed : [part];
        }
        function stopVoices() {
            for (const s of voiceSources) { try { s.stop(); } catch (e) { /* ended */ } }
            voiceSources = [];
        }
        // A voice that lands while its part already plays on the instrument standing in for it
        // (owner's request 2026-11-11) comes in at the next note it sings - no rest is waited
        // for (the first build did; the owner's song had few). The instrument is struck no
        // more from there (the tracks that sounded without the voice and would not with it,
        // instrumentSounds), and the voice is cut in a consonant's worth before the beat, or
        // with what room a rest gives, fading in over a moment so that a vowel cut into does
        // not click. entries are the sung notes, in the score's seconds (voiceEntries); the
        // list's time is the score's at the voice's speed, after a count-in.
        // Left to the next Play: a stretch or a loop (their voices are cut by voiceCues), the
        // MIDI output and the plain tones (their instruments cannot be taken back), a Play with
        // no sung note left. (No backticks in this script: it lives in a template literal.)
        //
        // ANOTHER voice of the part may be singing already (owner's report 2026-11-11: the singer
        // changed while the score played - the new voice was ready and the old one sang on to
        // the end): it stops where the new one comes in, and no instrument is taken back, since
        // none stood in. The score was edited under the Play then (playEdited), so the voice is
        // the new text's and the Play the old one's: it is cut in only where it still fits -
        // every note it sings beginning with a note of its part in the list played. A singer
        // changed leaves the notes as they were; a rhythm rewritten waits for the next Play.
        let playingNotes = null;    // the list startPlayback sounds
        let playEdited = false;     // the score changed since that Play began
        function voiceFitsPlay(key, list) {
            if (!playEdited) { return true; }
            const track = rawParts ? rawParts.indexOf(key.split('#')[0]) : -1;
            const onsets = playingNotes.filter(n => n.N === track && !(n.P < 0)).map(n => n.T);
            if (onsets.length === 0) { return false; }
            return list.every(e => e.at < playOffset - 1e-6 || onsets.some(t => Math.abs(t - e.at) < 0.002));
        }
        function joinVoice(key, entries, sungBefore) {
            if (playClock === null || !playingNotes || midiPlaying || loopList || !voicePlan || voicePlan.pieces
                || !audioCtx || !window.LysSynth || !window.LysSynth.nextVoiceEntry || !window.LysSynth.voiceEntryEarly) { return; }
            if (!soundingVoices().includes(key)) { return; }
            const v = voices.get(key);
            const list = (entries || []).map(e => ({ at: countInFix + e.at / v.speed, room: e.room / v.speed }));
            if (!voiceFitsPlay(key, list)) { return; }
            const replaced = voiceSources.filter(s => s.voiceKey === key);
            const sungAfter = sungTracks();
            const tracks = new Set();
            for (const n of (replaced.length > 0 ? [] : playingNotes)) {
                if (typeof n.N === 'number' && !tracks.has(n.N) && instrumentSounds(n.N, sungBefore) && !instrumentSounds(n.N, sungAfter)) { tracks.add(n.N); }
            }
            if (tracks.size > 0 && !sfPlayer) { return; }
            const now = playOffset + playClock();
            const entry = window.LysSynth.nextVoiceEntry(list, 0, now, sfPlayer ? playOffset + sfPlayer.handedOutUntil : -1);
            if (!entry) { return; }
            const lead = v.lead || 0;
            const early = window.LysSynth.voiceEntryEarly(entry, lead, entry.at - now);
            const t0 = audioCtx.currentTime - playClock();
            const when = t0 + (entry.at - playOffset) - early;
            // The menu's fader for the voice (at most 1 here, as startVoices), faded in.
            const level = Math.min(1, levelIn(practice.voiceLevels, key));
            const g = audioCtx.createGain();
            g.gain.setValueAtTime(0, when);
            g.gain.linearRampToValueAtTime(level, when + 0.03);
            g.connect(voiceOutput(audioCtx));
            const src = audioCtx.createBufferSource();
            src.buffer = v.buffer;
            src.connect(g);
            src.start(when, lead + (entry.at - countInFix) - early);
            src.voiceKey = key;
            // The voice that sang this part until now ends as the new one fades in.
            for (const old of replaced) { try { old.stop(when + 0.03); } catch (e) { /* ended */ } }
            voiceSources.push(src);
            if (tracks.size > 0) { sfPlayer.silence(playingNotes.filter(n => tracks.has(n.N) && n.T >= entry.at - 1e-6)); }
            flashInfo('The voice of ' + (menuVoiceNames[key] || key) + ' comes in', 3000);
        }
        // Starts every voice so that the list's time listTime sounds at the context's time when:
        // after a count-in (countInFix), each stretch of the score the list plays cut from the
        // buffer (voiceCues) — a selected stretch stops at its end, a loop's rounds each sing it again.
        // How long the instruments wait for the voices' consonants before the beat on a Play from
        // the score's 0 with no count-in (the count-in gives the room itself; a start inside the
        // score has the previous syllable before it, not a consonant in silence).
        function voicePreroll(offset, countFix) {
            if ((countFix || 0) > 0 || offset > 0 || voicePlan === null) { return 0; }
            if (voicePlan.pieces && !(voicePlan.pieces.length > 0 && voicePlan.pieces[0][0] < 1e-6)) { return 0; }
            let pre = 0;
            for (const key of soundingVoices()) { pre = Math.max(pre, voices.get(key).pre || 0); }
            return pre;
        }
        // Whether track i's instrument sounds in this Play (session 1165): a part the score only
        // sings keeps its instrument off unless the menu turned it on, standing in only while its
        // voice is not there (not sung now: off, not ready, or its fader at 0); every other part's
        // instrument sounds (the muted and the extras left off are already out of the list).
        function instrumentSounds(i, sung) {
            if (!rawParts || typeof i !== 'number' || i < 0) { return true; }
            const name = rawParts[i];
            const sungOnly = rawSung && rawSung.includes(name) && !(rawPlayed && rawPlayed.includes(name));
            if (!sungOnly) { return true; }
            if (practice.instrumentsOn.includes(name)) { return true; }
            if (menuVoicesOf(name).every(k => practice.voicesOff.includes(k))) { return false; }    // both switched off: silence
            return !sung.has(i);      // the stand-in, until the voice is there
        }
        // Where the voices end in the list's time: the whole buffer after the count-in's lead, or
        // the plan's pieces laid out their rounds (for a Play with no note left to end on).
        function voicesEnd() {
            let end = 0;
            if (voicePlan === null) { return 0; }
            for (const key of soundingVoices()) {
                const v = voices.get(key);
                const seconds = v.buffer.duration - (v.lead || 0);
                if (!voicePlan.pieces) { end = Math.max(end, countInFix + seconds); }
                else {
                    const length = voicePlan.pieces.reduce((s, p) => s + (p[1] - p[0]), 0);
                    end = Math.max(end, countInFix + Math.min(seconds, length) * Math.max(1, voicePlan.rounds || 1));
                }
            }
            return end;
        }
        function startVoices(ctx, when, listTime, room) {
            stopVoices();
            const sounding = soundingVoices();
            if (sounding.length === 0) { return; }
            if (!window.LysSynth || !window.LysSynth.voiceCues || !window.LysSynth.withVoiceLead) {
                vscode.postMessage({ type: 'webviewPerf', text: 'voice: the synth script is an old build (no voiceCues / withVoiceLead) - reload the window' });
                return;
            }
            for (const key of sounding) {
                const v = voices.get(key);
                const lead = v.lead || 0;
                const cues = window.LysSynth.voiceCues(listTime, countInFix, voicePlan.pieces, voicePlan.rounds, v.buffer.duration - lead);
                // The menu's fader for the voice: a gain of its own before the voices' output.
                const level = levelIn(practice.voiceLevels, key);
                let out = voiceOutput(ctx);
                if (level < 1) {
                    const g = ctx.createGain();
                    g.gain.value = level;
                    g.connect(out);
                    out = g;
                }
                for (const cue of window.LysSynth.withVoiceLead(cues, lead, v.pre || 0, room || 0)) {
                    const src = ctx.createBufferSource();
                    src.buffer = v.buffer;
                    src.connect(out);
                    if (cue.duration === undefined) { src.start(when + cue.at, cue.offset); }
                    else { src.start(when + cue.at, cue.offset, cue.duration); }
                    src.voiceKey = key;     // which voice sings here, for one that replaces it (joinVoice)
                    voiceSources.push(src);
                }
            }
        }

        let playingOscs = [];
        let playEndTimer = null;
        let playheadTimer = null;
        let relaying = false;       // the current playback is a candidate's, followed for its panel
        let tickWatch = null;       // the follow-along tick's evenness, for the log (followAlong)
        let followTick = null;      // the follow-along's tick while it runs (the host's followTick calls it too)
        let sfPlayer = null;       // the SoundFont player of the current playback
        let midiPlaying = false;    // the current playback is the server's, on the MIDI output
        let midiPlayPending = null; // a Play sent to the server, awaiting its start (generation, notes, offset)
        let playbackNotes = null;   // last received event list — enables click-to-seek
        // A Play with bars selected plays them (owner's request 2026-10-25): the selection's
        // source range, kept until the notes come. The header's loop plays the stretch (or the
        // whole score) round and round, its repeats ignored — loopList is what goes round.
        let pendingSpan = null;
        let pendingFromHere = false;    // the menu's Play from Here: from the note, never the loop
        let loopOn = false;
        let loopList = null;
        let pendingStartPos = -1;   // play-button start point (the highlighted note)
        let lastResolvedPos = -1;   // the note the current highlight actually resolved to

        // ---- The Range panel (owner's request 2026-10-25): each part's lowest and highest note
        // (lilysharp/ranges — what Play sounds), to check a part's range and find a singer's top
        // note at once. A click on one shows it on the score and puts the editor's caret on it; a
        // pitch written in several places goes to the next on each click. Asked again after
        // every render while open.
        const rangeBtn = document.getElementById('rangeBtn');
        const rangePanel = document.getElementById('rangePanel');
        const rangeBody = document.getElementById('rangeBody');   // what the panel says, under its ×
        let rangeParts = null;      // the last answer: [{ Name, Low, High }], each end { Pitch, Name, Positions }
        let rangeError = null;
        const rangeAt = {};         // 'part|Low' / 'part|High' -> the index of the place shown
        let rangeCur = null;        // the key of the end shown last
        function refreshRanges() {
            if (!rangePanel.hidden) vscode.postMessage({ type: 'requestRanges' });
        }
        rangeBtn.addEventListener('click', () => {
            rangePanel.hidden = !rangePanel.hidden;
            rangeBtn.setAttribute('aria-pressed', String(!rangePanel.hidden));
            if (rangePanel.hidden) return;
            raisePanel(rangePanel);
            if (rangePanel.dataset.moved) clampPanel(rangePanel);
            else rangePanel.style.top = (document.querySelector('.toolbar').getBoundingClientRect().bottom + 6) + 'px';
            if (!rangeParts && !rangeError) rangeBody.textContent = 'Reading the ranges…';
            refreshRanges();
        });
        // Its × closes it, as the Range button does (owner's request 2026-10-29).
        document.getElementById('rangeClose').addEventListener('click', () => {
            rangePanel.hidden = true;
            rangeBtn.setAttribute('aria-pressed', 'false');
        });
        // The bar a note is drawn in: the bar box over its x nearest it in y (a high note stands
        // above its staff's box).
        function barOfPos(pos) {
            sourcePositions();
            const el = (posElements.get(pos) || [])[0];
            if (!el) return null;
            const b = el.getBoundingClientRect();
            const cx = (b.left + b.right) / 2, cy = (b.top + b.bottom) / 2;
            let best = null, bestGap = Infinity;
            for (const r of svgContainer.querySelectorAll('rect.bar-box[data-bar]')) {
                const k = r.getBoundingClientRect();
                if (cx < k.left || cx > k.right) continue;
                const gap = cy < k.top ? k.top - cy : cy > k.bottom ? cy - k.bottom : 0;
                if (gap < bestGap) { best = r; bestGap = gap; }
            }
            return best ? best.getAttribute('data-bar') : null;
        }
        function renderRanges() {
            rangeBody.textContent = '';
            if (rangeError) { rangeBody.textContent = 'Range: ' + rangeError; return; }
            if (!rangeParts) return;
            if (rangeParts.length === 0) { rangeBody.textContent = 'No pitched notes in this score.'; return; }
            const table = document.createElement('table');
            const head = document.createElement('tr');
            for (const h of ['Part', 'Lowest', 'Highest', 'Tessitura']) {
                const th = document.createElement('th');
                th.textContent = h;
                head.appendChild(th);
            }
            table.appendChild(head);
            for (const p of rangeParts) {
                const tr = document.createElement('tr');
                const name = document.createElement('td');
                name.textContent = p.Name;
                tr.appendChild(name);
                tr.appendChild(rangeCell(p, 'Low'));
                tr.appendChild(rangeCell(p, 'High'));
                tr.appendChild(tessituraCell(p));
                table.appendChild(tr);
            }
            rangeBody.appendChild(table);
            const note = document.createElement('div');
            note.className = 'note';
            note.textContent = 'Sounding pitches · C4 = middle C · tessitura: where four-fifths of the time is';
            rangeBody.appendChild(note);
        }
        // The tessitura (owner's request 2026-10-25): where the part spends four-fifths of its
        // sounding time, named, over a small chart of that time on each semitone from its lowest
        // note to its highest — one hue, the tessitura's bars full and underlined, the rest
        // faint. Each column says its note and share on hover.
        const PITCH_NAMES = ['C', 'C♯', 'D', 'E♭', 'E', 'F', 'F♯', 'G', 'A♭', 'A', 'B♭', 'B'];
        const pitchName = (m) => PITCH_NAMES[((m % 12) + 12) % 12] + (Math.floor(m / 12) - 1);
        function tessituraCell(p) {
            const td = document.createElement('td');
            td.className = 'tess';
            const w = p.Weights || [];
            const label = document.createElement('div');
            label.textContent = p.Tessitura || '';
            td.appendChild(label);
            if (w.length === 0) return td;
            const ns = 'http://www.w3.org/2000/svg';
            const bar = Math.max(2, Math.min(5, Math.floor(180 / w.length) - 2)), gap = w.length > 40 ? 1 : 2, h = 22;
            const max = Math.max(...w);
            const low = Math.floor(p.Low.Pitch);
            const svg = document.createElementNS(ns, 'svg');
            svg.setAttribute('width', String(w.length * (bar + gap)));
            svg.setAttribute('height', String(h + 4));
            svg.setAttribute('class', 'tess-chart');
            w.forEach((v, i) => {
                const m = low + i, x = i * (bar + gap);
                const inside = m >= p.TessituraLow && m <= p.TessituraHigh;
                const bh = v > 0 ? Math.max(2, Math.round((v / max) * h)) : 0;
                if (bh > 0) {
                    const r = document.createElementNS(ns, 'rect');
                    r.setAttribute('x', String(x)); r.setAttribute('y', String(h - bh));
                    r.setAttribute('width', String(bar)); r.setAttribute('height', String(bh));
                    r.setAttribute('rx', '1');
                    r.setAttribute('class', inside ? 'tess-bar in' : 'tess-bar');
                    svg.appendChild(r);
                }
                // The column's hover target: its whole height, wider than the bar.
                const hit = document.createElementNS(ns, 'rect');
                hit.setAttribute('x', String(x - gap / 2)); hit.setAttribute('y', '0');
                hit.setAttribute('width', String(bar + gap)); hit.setAttribute('height', String(h + 4));
                hit.setAttribute('fill', 'transparent');
                const t = document.createElementNS(ns, 'title');
                t.textContent = pitchName(m) + ': ' + Math.round(v * 100) + '% of the time';
                hit.appendChild(t);
                svg.appendChild(hit);
            });
            const a = (p.TessituraLow - low) * (bar + gap), b = (p.TessituraHigh - low) * (bar + gap) + bar;
            const line = document.createElementNS(ns, 'rect');
            line.setAttribute('x', String(a)); line.setAttribute('y', String(h + 2));
            line.setAttribute('width', String(Math.max(bar, b - a))); line.setAttribute('height', '2');
            line.setAttribute('class', 'tess-line');
            svg.appendChild(line);
            td.appendChild(svg);
            return td;
        }
        function rangeCell(p, end) {
            const e = p[end];
            const key = p.Name + '|' + end;
            const n = e.Positions.length;
            const i = Math.min(rangeAt[key] || 0, Math.max(0, n - 1));
            const td = document.createElement('td');
            td.className = 'end' + (key === rangeCur ? ' cur' : '');
            const pitch = document.createElement('span');
            pitch.className = 'pitch';
            pitch.textContent = (end === 'High' ? '▲ ' : '▼ ') + e.Name;
            const bar = n > 0 ? barOfPos(e.Positions[i]) : null;
            const where = document.createElement('span');
            where.className = 'where';
            where.textContent = (bar ? '  bar ' + bar : '') + (n > 1 ? '  ' + (i + 1) + '/' + n : '');
            td.append(pitch, where);
            td.title = n > 1 ? 'Written in ' + n + ' places — click for the next' : 'Show it on the score';
            td.addEventListener('click', () => showRangeEnd(e, key));
            return td;
        }
        function showRangeEnd(e, key) {
            const n = e.Positions.length;
            if (n === 0) return;
            // The first click shows the place shown last; the next ones go on to the next place.
            if (rangeCur === key) rangeAt[key] = ((rangeAt[key] || 0) + 1) % n;
            rangeCur = key;
            const pos = e.Positions[Math.min(rangeAt[key] || 0, n - 1)];
            clearHighlights();
            const painted = highlightPositions([pos]);
            lastHighlightPos = pos;
            if (painted[0]) painted[0].scrollIntoView({ block: 'center', inline: 'center', behavior: 'smooth' });
            vscode.postMessage({ type: 'jumpToPosition', position: pos });
            renderRanges();
        }

        function setPlayUi(playing) {
            // The host's follower waits for the music to stop before it turns to another file.
            if (playing !== !document.getElementById('stopBtn').disabled) {
                vscode.postMessage({ type: 'playState', playing });
            }
            document.getElementById('playBtn').disabled = playing;
            document.getElementById('stopBtn').disabled = !playing;
        }

        function stopPlayback() {
            playGeneration++;   // a Play waiting for the sound font is off
            stopVoices();
            clearTimeout(playEndTimer);
            playEndTimer = null;
            clearInterval(playheadTimer);
            playheadTimer = null;
            follow.stop();
            followTick = null;
            playClock = null;
            playingNotes = null;
            if (tickWatch && tickWatch.ticks > 0) {
                vscode.postMessage({ type: 'webviewPerf', text: 'follow-along: ' + tickWatch.ticks + ' ticks, longest gap '
                    + Math.round(tickWatch.longest) + ' ms' + (tickWatch.hidden ? ', page hidden' : '') });
            }
            tickWatch = null;
            if (relaying) {
                relaying = false;
                vscode.postMessage({ type: 'candidateSounding', sounding: [] });
            }
            clearHighlights();
            lastHighlightPos = -1;
            // No note is highlighted once playback ends (naturally or via Stop),
            // so forget the resolved start point too — otherwise it stays pinned
            // to the last sounded note and the next Play resumes from the end
            // instead of restarting from the top. Cursor sync re-sets it when the
            // caret lands on a note again.
            lastResolvedPos = -1;
            if (sfPlayer) {
                sfPlayer = null;
                if (sfSynth) { sfSynth.stop(); }
            }
            midiPlayPending = null;
            if (midiPlaying) {
                midiPlaying = false;
                vscode.postMessage({ type: 'midiStop' });
            }
            for (const o of playingOscs) {
                try { o.stop(); } catch (e) { /* already ended */ }
            }
            playingOscs = [];
            setPlayUi(false);
        }

        // A Play struck while the sound font is still loading waits for it a few
        // seconds — the click just resumed the context, so the worklet's processor and
        // the bank's parsing, which wait for the audio thread, are now under way — and
        // plays the oscillator patches only if it does not come. The generation makes a
        // Stop or another Play in the meantime cancel the wait.
        let playGeneration = 0;
        function startPlayback(notes, offset, plainTones, here, countFix) {
            offset = offset || 0;
            stopPlayback();
            // A voice's phrase still sounding gives way to the score (playVoiceSample).
            if (voiceSampleSource) { try { voiceSampleSource.stop(); } catch (e) { /* ended */ } voiceSampleSource = null; }
            countInFix = countFix || 0;
            // A list with no note still plays when a voice sings (owner's report 2026-11-09,
            // session 1164: the part's button off and its voice on - the voice alone - gave
            // silence): the part's button is the instrument's, the voice is its own toggle's.
            notes = notes || [];
            const voiced = sungTracks().size > 0;
            if (notes.length === 0 && !voiced) { return; }
            // Seek = re-schedule from the offset; notes already past it are
            // dropped (mid-note tails too — re-striking half a note reads
            // worse than starting cleanly at the next onset).
            notes = window.LysSynth ? window.LysSynth.fromOffset(notes, offset) : notes.filter(n => n.T >= offset - 0.001);
            if (notes.length === 0 && !voiced) { return; }
            // What sounds is what the menu has on: the instruments whose buttons are on, the voices
            // whose notes are on - each on its own (session 1165; until then a voice took the
            // instrument's place). The score's intent is the DEFAULT: a part it only sings (a
            // singer's staff, no staff without words) has its instrument off unless the menu
            // turned it on (instrumentsOn) - but the instrument stands in while the voice is not
            // there yet (owner's decision 2026-11-08: a Play never waits). The highlight follows
            // the whole list.
            const sung = sungTracks();
            const played = notes.filter(n => instrumentSounds(n.N, sung));
            if (midiOutput && !here && played.length > 0) {
                // The MIDI output: the server plays the list from the offset and answers
                // (midiStarted) with the clock the highlight follows; a port that is not
                // there comes back as an error, and the list plays here instead. (Nothing for
                // the port - the voices alone - plays here.)
                const gen = ++playGeneration;
                midiPlayPending = { gen, notes, offset };
                setPlayUi(true);
                vscode.postMessage({ type: 'midiPlay', port: midiOutput, notes: played, offset });
                return;
            }
            const ctx = audio();
            const sf = soundFontSynth();
            if (sf && sf.state === 'loading' && !plainTones) {
                const gen = ++playGeneration;
                setPlayUi(true);
                flashInfo('Loading the sound font…', 6000);
                sf.whenReady(8000).then(ok => {
                    if (gen !== playGeneration) { return; }
                    if (!ok) { flashInfo('The sound font is not ready: plain tones this time'); }
                    startPlayback(notes, offset, !ok, here, countFix);
                });
                return;
            }
            // From the score's 0: the instruments wait for the voices' first consonant (voicePreroll).
            const pre = voicePreroll(offset, countFix);
            let t0 = ctx.currentTime + 0.15 + pre;
            // The whole list's end, and the voices' (a voice alone has no note to end on).
            let end = Math.max(notes.reduce((e, n) => Math.max(e, n.T + n.D), 0), voicesEnd());
            if (sf && sf.ready && !plainTones) {
                if (played.length > 0) {
                    sfPlayer = sf.play(played, offset, t0);
                    t0 = sfPlayer.t0;
                    end = Math.max(end, sfPlayer.end);
                }
            } else {
                end = Math.max(end, startOscillators(ctx, played, offset, t0));
            }
            // The sung parts' voices, in the instruments' place, from the same clock.
            startVoices(ctx, t0, offset, pre);
            setPlayUi(true);
            // A loop goes round when the last note ends; a plain playback lets it ring a moment.
            playEndTimer = setTimeout(playEnded, (end - offset + (loopList ? 0 : 0.5)) * 1000);
            playOffset = offset;
            playingNotes = notes;
            playEdited = false;
            playClock = () => audioCtx ? audioCtx.currentTime - t0 : 0;
            followAlong(notes, offset, playClock);
        }

        // The server's answer to a midiPlay: started (the wall-clock zero and the end), or
        // not — then the same list plays here, once, with the reason shown.
        function midiStarted(message) {
            const pending = midiPlayPending;
            if (!pending || pending.gen !== playGeneration) { return; }   // stopped, or another Play, meanwhile
            midiPlayPending = null;
            if (!message.ok) {
                flashInfo('MIDI output: ' + (message.error || 'did not start') + ' — playing here', 5000);
                startPlayback(pending.notes, pending.offset, false, true);
                return;
            }
            midiPlaying = true;
            const t0Ms = message.t0Ms;
            // The voice plays here, from where the port's playback stands now (the round trip's jitter).
            if (sungTracks().size > 0) {
                const ctx = audio();
                startVoices(ctx, ctx.currentTime + 0.05, pending.offset + (Date.now() - t0Ms) / 1000 + 0.05, 0);
            }
            playEndTimer = setTimeout(playEnded, (message.end - pending.offset + (loopList ? 0 : 0.5)) * 1000);
            playOffset = pending.offset;
            playClock = () => (Date.now() - t0Ms) / 1000;
            followAlong(pending.notes, pending.offset, playClock);
        }

        // Follow-along: light the notes SOUNDING now (a quarter stays lit while the other
        // hand's eighths change under it) — each entry carries the server-side printed-copy
        // ordinal (O), so repeats and seeks cannot drift — and keep the newest onset in
        // view without fighting manual scrolling more than needed. The cursor
        // (playbackCore.ts) walks the onsets once, so a tick costs what started since.
        // elapsed() is the playback's clock in seconds from the offset: the audio context's
        // for the synth here, the wall clock from the server's zero for the MIDI output.
        // Another text's notes (an AI candidate, playNotes) carry their own offsets as R: those
        // are followed for the candidate panel, which lights them on its score — here nothing.
        // Whether the view follows the notes sounding (owner's request 2026-11-11): a scroll of
        // the user's own that leaves them out of the view ends it, and the Follow button, the
        // notes scrolled back into view, or the next Play brings it back (followScroll.ts).
        ${FOLLOW_SCROLL_JS}
        const follow = installFollowScroll(mainContent, document.getElementById('followBtn'));
        follow.onResume = (els) => { if (els.length > 0) follow.auto(() => keepSoundingInView(els)); };
        function followAlong(notes, offset, elapsed) {
            const Cursor = window.LysSynth ? window.LysSynth.SoundingCursor : null;
            if (!Cursor) { return; }
            const relay = notes.some(n => n.R !== undefined);
            relaying = relay;
            if (!relay) follow.start();
            const cursor = new Cursor(notes.map(n => ({ t: n.T - offset, d: n.D, s: relay ? n.R : n.S, o: n.O || 0 })));
            // How evenly the 50 ms tick came (owner's report 2026-10-25: one eighth note lit in
            // four — the tick a second apart, as a browser throttles a page it thinks hidden):
            // the longest gap and whether the page said it was hidden, logged when it stops.
            tickWatch = { last: performance.now(), longest: 0, ticks: 0, hidden: document.hidden };
            // Run by the page's own timer and by the host's followTick, whichever comes: a
            // throttled timer leaves it to the host's (see the host's followTicker).
            const tick = () => {
                const now = performance.now();
                if (now - tickWatch.last < 25 && tickWatch.ticks > 0) return;
                tickWatch.longest = Math.max(tickWatch.longest, now - tickWatch.last);
                tickWatch.last = now;
                tickWatch.ticks++;
                tickWatch.hidden = tickWatch.hidden || document.hidden;
                const r = cursor.at(elapsed());
                if (!r.changed) return;
                if (relay) {
                    vscode.postMessage({ type: 'candidateSounding', sounding: r.sounding.map(x => ({ pos: x.pos, occ: x.occ })) });
                    return;
                }
                clearHighlights();
                const painted = highlightPositions(r.sounding);
                if (r.sounding.length > 0) {
                    lastHighlightPos = r.sounding[0].pos;
                    lastResolvedPos = r.sounding[0].pos;
                }
                if (painted.length > 0) follow.sounding(painted);
                if (r.hadNew && painted.length > 0 && follow.follows()) follow.auto(() => keepSoundingInView(painted));
            };
            followTick = tick;
            playheadTimer = setInterval(tick, 50);
        }

        // Keeps what sounds now in view, the notes of every part TOGETHER (owner's report
        // 2026-10-29: a song's system — chords, voice, words, guitar, bass, drums — was taller
        // than half the view, and following one note at a time centred the voice, then the
        // drums it pushed off, then the voice: the view shook up and down). Their union is
        // centred when it fits the view; when it does not, its top is kept near the view's top,
        // moved only when that top is out of sight — so a voice's rest does not move it.
        function keepSoundingInView(els) {
            let top = Infinity, bottom = -Infinity, left = Infinity, right = -Infinity;
            for (const el of els) {
                const b = el.getBoundingClientRect();
                if (b.width === 0 && b.height === 0) continue;
                top = Math.min(top, b.top); bottom = Math.max(bottom, b.bottom);
                left = Math.min(left, b.left); right = Math.max(right, b.right);
            }
            if (top === Infinity) return;
            const m = mainContent.getBoundingClientRect();
            const margin = 30;
            const fits = bottom - top <= m.height - 2 * margin;
            const offV = fits
                ? top < m.top + margin || bottom > m.bottom - margin
                : top < m.top + margin || top > m.bottom - 2 * margin;
            const offH = right - left <= m.width - 2 * margin
                ? left < m.left + margin || right > m.right - margin
                : left < m.left + margin || left > m.right - 2 * margin;
            if (!offV && !offH) return;
            const dy = !offV ? 0 : fits ? (top + bottom) / 2 - (m.top + m.bottom) / 2 : top - (m.top + margin);
            const dx = !offH ? 0 : (right - left <= m.width - 2 * margin)
                ? (left + right) / 2 - (m.left + m.right) / 2 : left - (m.left + margin);
            mainContent.scrollBy({ top: dy, left: dx, behavior: 'smooth' });
        }

        // The fallback: oscillator patches, one node pair per note, built up front (fine
        // for the short pieces this plays while no sound font is loaded). Timbre families
        // (note.I): waveform + envelope approximations; pluck = exponential decay. The sum
        // goes through a limiter — many voices at once used to clip. Returns when the last
        // note ends, in seconds from the piece's start.
        ${PLAIN_TONES_JS}

        // ---- Audition: sound the caret's note (held) or its whole measure ----
        // The caret's note rings while the key is held. VS Code has no key-up event,
        // so a held key arrives as an OS key-repeat of the play command; each fire
        // re-arms a watchdog, and the note stops once the repeats cease. The strike is
        // the SoundFont synth's (the part's program, PlaybackNote.G / the server's
        // Program) when it is ready, else an oscillator of the timbre family's wave.
        let heldOscs = [];
        let heldPitches = [];       // ringing on the SoundFont synth (or the MIDI output), on heldChannel
        let heldChannel = -1;
        let heldMidi = false;       // the strike went to the MIDI output: the release goes there too
        let heldKey = '';           // what rings, so a fast repeat sustains instead of re-striking
        let heldWatchdog = null;
        let pendingAudition = null; // audition awaiting a fresh note list
        const AUD_WAVE = ['triangle','sine','square','sawtooth','triangle','sine','sawtooth','square','sine','triangle'];

        function stopHeldNote() {
            if (heldWatchdog) { clearTimeout(heldWatchdog); heldWatchdog = null; }
            if (heldMidi) {
                heldMidi = false;
                if (heldPitches.length) { vscode.postMessage({ type: 'midiRelease', channel: heldChannel, pitches: heldPitches }); }
            } else if (heldPitches.length && sfSynth) { sfSynth.release(heldChannel, heldPitches); }
            heldPitches = []; heldChannel = -1;
            if (heldOscs.length && audioCtx) {
                const t = audioCtx.currentTime;
                for (const o of heldOscs) {
                    try {
                        o.g.gain.cancelScheduledValues(t);
                        o.g.gain.setTargetAtTime(0.0001, t, 0.05);
                        o.osc.stop(t + 0.3);
                    } catch (e) { /* already ended */ }
                }
            }
            heldOscs = []; heldKey = '';
        }

        // strumS > 0: the pitches one after another in the order given, that many seconds apart
        // (a strum, playPitches), on the AUDIO clock — or, for the MIDI output, on the extension
        // host's — never on this page's timers: a hidden preview fires those about once a second,
        // and the strings after the first came out together as a chord (owner's report 2026-10-24).
        // apart (with a strum): each string stops as the next one sounds, so they never ring as a
        // chord — a bass's (owner's request 2026-10-24: a low chord is muddy).
        function holdNotes(pitches, timbre, program, gapMs, strumS, apart) {
            const strum = strumS > 0 ? strumS : 0;
            const separate = strum > 0 && !!apart;
            const key = pitches.slice().sort((a, b) => a - b).join(',') + '/' + timbre + '/' + program;
            // Fresh press (or a different note) re-attacks; a fast repeat of the SAME
            // note leaves the tone ringing and only re-arms the watchdog.
            const fresh = (heldOscs.length === 0 && heldPitches.length === 0) || key !== heldKey || gapMs > 700;
            if (fresh) {
                stopHeldNote();
                if (midiOutput) {
                    // The server strikes on its audition channel (the drums on theirs) with
                    // the part's program; the release names the same channel.
                    heldMidi = true;
                    heldChannel = timbre === 9 ? 9 : 15;
                    heldPitches = pitches.slice();
                    vscode.postMessage({ type: 'midiStrike', port: midiOutput, pitches: heldPitches, timbre: timbre, program: program,
                        gapMs: Math.round(strum * 1000), apart: separate });
                } else {
                    const ctx = audio();
                    const sf = soundFontSynth();
                    if (sf && sf.ready) {
                        heldChannel = sf.strike(pitches, timbre, program, 100, strum, separate);
                        heldPitches = pitches.slice();
                    } else {
                        const master = oscillatorOutput(ctx, 0.22);
                        const now = ctx.currentTime;
                        pitches.forEach((p, i) => {
                            const t0 = now + i * strum;
                            const osc = ctx.createOscillator();
                            osc.type = AUD_WAVE[timbre] || 'triangle';
                            osc.frequency.value = 440 * Math.pow(2, (p - 69) / 12);
                            const g = ctx.createGain();
                            // Silent until its onset: a string released before it starts (the
                            // release cancels the ramp) must not sound at the node's default gain.
                            g.gain.value = 0;
                            g.gain.setValueAtTime(0, t0);
                            g.gain.linearRampToValueAtTime(0.9, t0 + 0.02);
                            if (separate && i + 1 < pitches.length) {
                                // Apart: fades as the next string sounds.
                                const next = t0 + strum;
                                g.gain.setTargetAtTime(0.0001, next, 0.03);
                                osc.stop(next + 0.3);
                            }
                            osc.connect(g); g.connect(master);
                            osc.start(t0);
                            heldOscs.push({ osc, g });
                        });
                    }
                }
                heldKey = key;
            }
            // Long window on a fresh press (bridge the OS repeat delay), short once
            // the repeats are flowing (a snappy release when the key goes up).
            if (heldWatchdog) { clearTimeout(heldWatchdog); }
            heldWatchdog = setTimeout(stopHeldNote, fresh ? 700 : 220);
        }

        function auditionNote(position, gapMs) {
            if (!playbackNotes) { return; }
            // The note token nearest the caret (smallest |S - position|).
            let best = null, bestD = Infinity;
            for (const n of playbackNotes) {
                if (n.S < 0) { continue; }
                const d = Math.abs(n.S - position);
                if (d < bestD) { bestD = d; best = n; }
            }
            if (!best) { return; }
            const pitches = playbackNotes.filter(n => n.S === best.S).map(n => n.P);
            if (pitches.length) {
                holdNotes(pitches, best.I || 0, typeof best.G === 'number' ? best.G : -1, gapMs);
            }
        }

        function auditionMeasure(start, end) {
            if (!playbackNotes) { return; }
            const sub = playbackNotes.filter(n => n.S >= start && n.S < end);
            if (sub.length === 0) { return; }
            let minT = Infinity, maxEnd = 0;
            for (const n of sub) { if (n.T < minT) { minT = n.T; } if (n.T + n.D > maxEnd) { maxEnd = n.T + n.D; } }
            // The sung voice is cut to the bar too (owner's report 2026-11-09, session 1150: Alt+M
            // sang the first bar's words — the voice plan was the last Play's, the whole piece from
            // its 0, while the instruments played the bar moved to 0). One piece: the bar's stretch
            // of the score's time, as a Play with bars selected plays (spanPieces).
            voicePlan = { pieces: [[minT, maxEnd]], rounds: 1 };
            // Reschedule the measure to start at t=0, then reuse the full scheduler.
            startPlayback(sub.map(n => Object.assign({}, n, { T: n.T - minT })), 0);
        }

        function runAudition(a) {
            if (a.mode === 'measure') { auditionMeasure(a.rangeStart, a.rangeEnd); }
            else { auditionNote(a.position, a.gapMs); }
        }

        // Arbitrary pitches for a fixed time: the step keys and the automatic
        // audition (the caret landing on a note, a typed note, an @chord's shape).
        // The extension sends the compiler's MIDI numbers and the part's program, so
        // no note list is needed here. Same voice as the held audition: a fresh strike
        // (the huge gap), then released after durationMs instead of waiting for key-repeats.
        // strumMs > 0: the pitches one after another IN THE ORDER GIVEN, that far apart — a
        // tuning's open strings string after string (owner's request 2026-10-24), each joining the
        // ones already ringing (holdNotes times them). Not sorted: a re-entrant ukulele's high 4th
        // string comes first.
        // apart: each string stops as the next sounds (a bass's — holdNotes).
        function playPitches(pitches, timbre, program, durationMs, strumMs, apart) {
            if (!pitches || pitches.length === 0) { return; }
            stopHeldNote();
            const prog = typeof program === 'number' ? program : -1;
            holdNotes(pitches, timbre || 0, prog, 1e9, pitches.length > 1 ? (strumMs || 0) / 1000 : 0, !!apart);
            if (heldWatchdog) { clearTimeout(heldWatchdog); }
            heldWatchdog = setTimeout(stopHeldNote, durationMs || 450);
        }

        document.getElementById('playBtn').addEventListener('click', () => {
            setPlayUi(true); // immediate feedback while the request runs
            // A highlighted note (editor sync / preview click) is the start
            // point: play from its first onset instead of the top. Use the
            // RESOLVED note position - lastHighlightPos may be a raw editor
            // cursor offset that matches no playback event.
            pendingStartPos = lastResolvedPos;
            pendingSpan = aiRangeLo >= 0 ? selectedBarsSpan() : null;
            vscode.postMessage({ type: 'requestPlayback' });
        });
        document.getElementById('stopBtn').addEventListener('click', () => {
            loopList = null;
            stopPlayback();
        });
        const loopBtn = document.getElementById('loopBtn');
        loopBtn.addEventListener('click', () => {
            loopOn = !loopOn;
            // Off: what plays ends where it ends. On while playing: from the next Play.
            if (!loopOn) loopList = null;
            showPractice();
        });
        // A loop's rounds are laid out ahead, ten minutes of them (loopNotes): the player's
        // clock times each round, not a page timer the browser may hold to a second.
        function looped(list) {
            if (!window.LysSynth || !window.LysSynth.loopNotes) {
                vscode.postMessage({ type: 'webviewPerf', text: 'loop: the synth script is an old build (no loopNotes) - reload the window' });
                return list;
            }
            const rounds = window.LysSynth.loopNotes(list, 600, loopRound);
            loopRoundCount = list.length ? rounds.length / list.length : 1;
            vscode.postMessage({ type: 'webviewPerf', text: 'loop: ' + (list.length ? rounds.length / list.length : 0)
                + ' rounds of ' + (loopRound || 0).toFixed(3) + ' s, ' + rounds.length + ' notes' });
            return rounds;
        }
        let loopRoundCount = 1;     // how many rounds looped() laid out last — the voice sings each
        // The stretch ▶ plays, from its bar's start to the next bar's (playSpanWindow), and how
        // long it lasts — the loop's round. The bars are the metronome's accented beats, in the
        // practice menu's tempo.
        let loopRound = 0;
        function spanOf(notes, span, once) {
            const bars = rawBeats ? rawBeats.filter(b => b.A).map(b => b.T / practice.speed) : undefined;
            // And where the last bar ends (the server's closing bar line), the last bar's end.
            if (bars && typeof rawBarEnd === 'number' && rawBarEnd > 0) bars.push(rawBarEnd / practice.speed);
            const w = window.LysSynth.playSpanWindow
                ? window.LysSynth.playSpanWindow(notes, span ? span[0] : null, span ? span[1] : null, once, bars)
                : { notes: window.LysSynth.playSpan(notes, span ? span[0] : null, span ? span[1] : null, once), length: 0 };
            loopRound = w.length;
            spanStart = w.start || 0;
            spanPieces = w.pieces || [[spanStart, spanStart + loopRound]];
            return w.notes;
        }
        let spanStart = 0;          // where the stretch spanOf cut starts in the score's time (the count-in's bar)
        let spanPieces = null;      // the score's times the stretch plays, in order — the voice is cut from them
        // A count-in before what the user starts (practice.countIn): one bar of clicks, the music
        // moved after it (LysSynth.countIn). Null when off or there is no bar to count. Only a Play
        // counts in — a tempo change, the loop's next round and a seek go on at once.
        function counted(notes, start, origin) {
            if (!practice.countIn || !rawBeats || !window.LysSynth || !window.LysSynth.countIn) return null;
            return window.LysSynth.countIn(notes, start, origin,
                rawBeats.map(b => ({ T: b.T / practice.speed, A: b.A })));
        }
        let countInFix = 0;         // the played list's clock minus the score's, while a count-in plays
        // Where a playback that ran to its end goes: round again while looping, else it stops.
        function playEnded() {
            const list = loopOn ? loopList : null;
            if (!list) { stopPlayback(); return; }
            startPlayback(looped(list), 0);
            loopList = list;
        }

        // ---- The practice menu (owner's request 2026-10-25): a button that drops down the
        // controls a practice session wants and the header has no room for — the tempo (a slider,
        // 50–150%), the loop, the metronome, the parts on and off (a toggle each). A change while
        // playing goes on from the same place.
        // extraOn: the parts the score does not sound that the menu turned on; voicesOff: the sung
        // lyrics it turned off (owner's request 2026-11-08: both beside the parts).
        // levels / voiceLevels: the faders beside the parts and the voices (owner's request
        // 2026-11-09), part -> 0..1; a part at 1 is not listed.
        // voicesOn: the file's other singable lyrics the menu turned on (the score does not sing
        // them itself; session 1163).
        // instrumentsOn: the instruments turned on for parts the score only sings (session 1165).
        const practice = { speed: 1, muted: [], click: false, countIn: false, extraOn: [], voicesOff: [], voicesOn: [], instrumentsOn: [], levels: {}, voiceLevels: {} };
        // A fader runs 0 to 200% (owner's request 2026-11-09, session 1157): levelIn clamps to that.
        const FADER_MAX = 2;
        function levelIn(levels, name) {
            const v = levels[name];
            return typeof v === 'number' ? Math.min(FADER_MAX, Math.max(0, v)) : 1;
        }
        // What each line of the Parts list shows, refreshed after a reset (partsReset / fadersReset).
        let partRefreshers = [];
        // The document and score the page shows (updateContent's scoreKey), and the one the Parts
        // list was built for: the list is asked again when they differ while the menu is open
        // (owner's report 2026-11-09, session 1158: the menu kept the last score's parts after a
        // switch). Not on every render - a parts list costs the server a playback export - so a
        // part added by an edit shows on the next opening; an edit that changes the voices' lines
        // asks again too (voiceLinesChanged).
        let lastScoreKey = null, partsKey = null;
        function requestParts() {
            partsKey = lastScoreKey;
            vscode.postMessage({ type: 'requestParts' });
        }
        let rawNotes = null, rawParts = null, rawBeats = null, rawBarEnd = null, rawExtra = null;  // the server's last answer, as written
        let rawSung = null, rawPlayed = null;    // the parts the score sings, and plays (a part may be both: two staves)
        let lastSpan;               // what plays: undefined the whole score, null/[lo, hi] a playSpan
        let lastOnce = false;
        let playOffset = 0, playClock = null;   // where the playback stands (startPlayback / midiStarted)
        function practiced(notes) {
            return window.LysSynth && window.LysSynth.practiceNotes
                ? window.LysSynth.practiceNotes(notes, rawParts, rawBeats, practice, rawExtra)
                : notes;
        }
        const practiceBtn = document.getElementById('practiceBtn');
        const practicePanel = document.getElementById('practicePanel');
        const clickBtn = document.getElementById('clickBtn');
        const countInBtn = document.getElementById('countInBtn');
        const tempoSlider = document.getElementById('tempoSlider');
        function showPractice() {
            const speedOn = practice.speed !== 1;
            document.getElementById('practiceSpeed').textContent = speedOn ? Math.round(practice.speed * 100) + '%' : '';
            // Pressed in while anything differs from the score as written.
            practiceBtn.setAttribute('aria-pressed', String(speedOn || practice.click || practice.countIn || practice.muted.length > 0
                || practice.extraOn.length > 0 || practice.voicesOff.length > 0 || practice.voicesOn.length > 0 || practice.instrumentsOn.length > 0 || loopOn
                || Object.keys(practice.levels).length > 0 || Object.keys(practice.voiceLevels).length > 0));
            // The loop lives here too (owner's request 2026-10-25), an On/Off as the metronome's.
            loopBtn.setAttribute('aria-pressed', String(loopOn));
            loopBtn.textContent = loopOn ? 'On' : 'Off';
            tempoSlider.value = String(Math.round(practice.speed * 100));
            document.getElementById('tempoValue').textContent = Math.round(practice.speed * 100) + '%';
            document.getElementById('tempoReset').disabled = !speedOn;
            clickBtn.setAttribute('aria-pressed', String(practice.click));
            clickBtn.textContent = practice.click ? 'On' : 'Off';
            countInBtn.setAttribute('aria-pressed', String(practice.countIn));
            countInBtn.textContent = practice.countIn ? 'On' : 'Off';
            // The two resets under the parts (owner's request 2026-11-09): live while there is something to put back.
            document.getElementById('partsReset').disabled = practice.muted.length === 0 && practice.extraOn.length === 0 && practice.voicesOff.length === 0 && practice.voicesOn.length === 0 && practice.instrumentsOn.length === 0;
            document.getElementById('fadersReset').disabled = Object.keys(practice.levels).length === 0 && Object.keys(practice.voiceLevels).length === 0;
            // The exports play as the menu does, the loop aside (owner's request 2026-11-07): the
            // extension keeps this page's menu for them (previewPractice).
            vscode.postMessage({ type: 'practiceState', practice: {
                speed: practice.speed, muted: practice.muted.slice(), click: practice.click, countIn: practice.countIn,
                extraOn: practice.extraOn.slice(), voicesOff: practice.voicesOff.slice(), voicesOn: practice.voicesOn.slice(),
                instrumentsOn: practice.instrumentsOn.slice(),
                levels: Object.assign({}, practice.levels), voiceLevels: Object.assign({}, practice.voiceLevels) } });
        }
        function practiceChanged(oldSpeed) {
            showPractice();
            const playing = !document.getElementById('stopBtn').disabled;
            if (!playing || relaying || !rawNotes || !playClock) return;
            const at = Math.max(0, playOffset + playClock() - countInFix) * oldSpeed / practice.speed;
            playbackNotes = practiced(rawNotes);
            if (lastSpan === undefined) { voicePlan = { pieces: null, rounds: 1 }; startPlayback(playbackNotes, at); return; }
            const list = spanOf(playbackNotes, lastSpan, lastOnce);
            loopList = loopOn ? list : null;
            const toPlay = loopOn ? looped(list) : list;
            voicePlan = { pieces: spanPieces, rounds: loopOn ? loopRoundCount : 1 };
            startPlayback(toPlay, at);
            loopList = loopOn ? list : null;
        }
        function renderParts(message) {
            const list = document.getElementById('partsList');
            list.textContent = '';
            partRefreshers = [];
            const parts = message.parts || [], voices = message.voices || [], extra = message.extra || [];
            if (parts.length + voices.length + extra.length === 0) { list.textContent = '—'; return; }
            // One toggle each, pressed in while it sounds; the lists are by name, so a part muted
            // in another score stays muted.
            const without = (names, name) => names.filter(n => n !== name);
            // One line a part: its toggle, and to its right a fader (owner's request 2026-11-09)
            // - 0 to 100%, the instrument's velocity or the voice's gain. The number follows the
            // thumb; the sound changes when it is let go (as the tempo's does), 100 means "as
            // written" and is not kept.
            const toggle = (label, title, isOn, flip, cls, part, levels, levelKey) => {
                const line = document.createElement('span');
                line.className = 'pline';
                const b = document.createElement('button');
                b.type = 'button';
                b.textContent = label;
                if (cls && cls.split(' ').includes('voice')) {
                    // The very drawing the Play button's badge is (renderVoiceBadge sets its state).
                    const mic = document.createElement('span');
                    mic.className = 'voice-badge';
                    mic.setAttribute('aria-hidden', 'true');
                    mic.appendChild(document.querySelector('#voiceBadge svg').cloneNode(true));
                    b.appendChild(mic);
                    const bar = document.createElement('span');
                    bar.className = 'voice-row-bar';
                    bar.hidden = true;
                    bar.setAttribute('aria-hidden', 'true');
                    bar.appendChild(document.createElement('span'));
                    b.appendChild(bar);
                }
                if (title) { b.title = title; b.dataset.title = title; }
                if (cls) for (const c of cls.split(' ')) b.classList.add(c);
                if (part) b.dataset.part = part;
                b.setAttribute('aria-pressed', String(isOn()));
                b.addEventListener('click', () => {
                    flip();
                    b.setAttribute('aria-pressed', String(isOn()));
                    practiceChanged(practice.speed);
                });
                line.appendChild(b);
                const fader = document.createElement('input');
                fader.type = 'range'; fader.min = '0'; fader.max = String(FADER_MAX * 100); fader.step = '1';
                fader.className = 'fader';
                // The number beside the thumb while it is not at 100 (session 1156: a click on the
                // slider's track puts the thumb where the click lands, and a thumb at the far left
                // was easy to miss - the part then sounded as good as silent); a double-click is 100.
                const num = document.createElement('span');
                num.className = 'flevel';
                const sayLevel = () => {
                    fader.title = 'Volume ' + fader.value + '% (double-click: 100%)';
                    fader.setAttribute('aria-label', levelKey + ' volume ' + fader.value + '%');
                    num.textContent = fader.value === '100' ? '' : fader.value + '%';
                };
                fader.value = String(Math.round(levelIn(levels, levelKey) * 100));
                sayLevel();
                fader.addEventListener('input', sayLevel);
                fader.addEventListener('change', () => {
                    const v = Number(fader.value);
                    if (v === 100) { delete levels[levelKey]; } else { levels[levelKey] = v / 100; }
                    sayLevel();
                    practiceChanged(practice.speed);
                });
                fader.addEventListener('dblclick', () => { fader.value = '100'; fader.dispatchEvent(new Event('change')); });
                line.appendChild(fader);
                line.appendChild(num);
                list.appendChild(line);
                partRefreshers.push(() => {
                    b.setAttribute('aria-pressed', String(isOn()));
                    fader.value = String(Math.round(levelIn(levels, levelKey) * 100));
                    sayLevel();
                });
            };
            // The score's parts (owner's request 2026-10-25). A part the score only sings (a
            // singer's staff) has its instrument off by default - the voice sings - and on when
            // pressed, both then sounding (session 1165: the part's button is the instrument's,
            // the voice is the note's, each on its own).
            const sungOnly = new Set(message.sungOnly || []);
            for (const name of parts) {
                if (sungOnly.has(name)) {
                    toggle(name, 'Sung by its voice - press for its instrument too', () => practice.instrumentsOn.includes(name),
                        // (A mute left by another score, where this part is played, would keep it silent: it goes.)
                        () => { practice.muted = without(practice.muted, name); practice.instrumentsOn = practice.instrumentsOn.includes(name) ? without(practice.instrumentsOn, name) : practice.instrumentsOn.concat([name]); },
                        '', '', practice.levels, name);
                } else {
                    toggle(name, '', () => !practice.muted.includes(name),
                        () => { practice.muted = practice.muted.includes(name) ? without(practice.muted, name) : practice.muted.concat([name]); },
                        '', '', practice.levels, name);
                }
            }
            // The sung lyrics, one a part with a voice, beside the parts (owner's request
            // 2026-11-08; docs/singing-design.md §5): on, the part is sung; off, its instrument
            // plays in the voice's place. Every singable lyrics of the file is listed (owner's
            // request 2026-11-09, session 1163): the ones this score sings itself first, then in
            // italics the ones it does not, off until pressed - pressed, the voice sings in the
            // part's instrument's place.
            const inScore = new Set(message.voicesInScore || voices);
            // What each voice's line says (the lists stay by part): who sings, and on hover what.
            const voiceLabels = message.voiceLabels || {};
            const voiceTitles = message.voiceTitles || {};
            menuVoiceKeys = voices;
            menuVoiceNames = voiceLabels;
            const voiceLabel = name => voiceLabels[name] || name;
            const voiceTitle = name => voiceTitles[name] || voiceLabel(name);
            for (const name of voices) {
                if (!inScore.has(name)) continue;
                toggle(voiceLabel(name), voiceTitle(name) + ': its voice, on or off (the part button beside it is its instrument)', () => !practice.voicesOff.includes(name),
                    () => { practice.voicesOff = practice.voicesOff.includes(name) ? without(practice.voicesOff, name) : practice.voicesOff.concat([name]); },
                    'voice', name, practice.voiceLevels, name);
            }
            for (const name of voices) {
                if (inScore.has(name)) continue;
                toggle(voiceLabel(name), voiceTitle(name) + ': this score does not sing it - silent unless pressed; pressed, the voice sings in place of the instrument', () => practice.voicesOn.includes(name),
                    () => { practice.voicesOn = practice.voicesOn.includes(name) ? without(practice.voicesOn, name) : practice.voicesOn.concat([name]); },
                    'voice extra', name, practice.voiceLevels, name);
            }
            // The file's other parts, which this score does not sound: off until pressed (owner's
            // request 2026-11-08).
            for (const name of extra) {
                toggle(name, 'Not in this score — silent unless pressed', () => practice.extraOn.includes(name),
                    () => { practice.extraOn = practice.extraOn.includes(name) ? without(practice.extraOn, name) : practice.extraOn.concat([name]); },
                    'extra', '', practice.levels, name);
            }
            renderVoiceBadge();     // the voices' signs on their fresh toggles
        }
        function closePractice() {
            practicePanel.hidden = true;
            practiceBtn.setAttribute('aria-expanded', 'false');
        }
        practiceBtn.addEventListener('click', () => {
            if (!practicePanel.hidden) { closePractice(); return; }
            const b = practiceBtn.getBoundingClientRect();
            practicePanel.hidden = false;
            raisePanel(practicePanel);
            if (practicePanel.dataset.moved) clampPanel(practicePanel);
            else {
                practicePanel.style.top = (document.querySelector('.toolbar').getBoundingClientRect().bottom + 6) + 'px';
                practicePanel.style.right = 'auto';
                practicePanel.style.left = Math.max(6, Math.min(b.left, window.innerWidth - practicePanel.offsetWidth - 6)) + 'px';
            }
            practiceBtn.setAttribute('aria-expanded', 'true');
            requestParts();
        });
        // It closes as the Range panel does, by its × and its button, and stays open beside the
        // score (owner's request 2026-11-02: a click on the score closed it).
        document.getElementById('practiceClose').addEventListener('click', closePractice);

        // The Range and practice panels move where they are dragged (owner's request
        // 2026-10-29), inside the preview: what goes past its edge is cut off there, and a panel
        // keeps PANEL_KEEP pixels in sight on all four sides alike, the top one being the toolbar's
        // lower edge, behind which it goes (owner's request 2026-11-02), so it can always be taken
        // back. Once moved it stays where it was put, also when opened again.
        const PANEL_KEEP = 48;
        function clampPanel(panel) {
            if (panel.hidden || !panel.dataset.moved) return;
            const r = panel.getBoundingClientRect();
            const under = document.querySelector('.toolbar').getBoundingClientRect().bottom;
            const left = Math.min(Math.max(r.left, PANEL_KEEP - r.width), window.innerWidth - PANEL_KEEP);
            const top = Math.min(Math.max(r.top, under + PANEL_KEEP - r.height), window.innerHeight - PANEL_KEEP);
            panel.style.right = 'auto';
            panel.style.left = left + 'px';
            panel.style.top = top + 'px';
        }
        // The one pressed or opened last stands in front of the other (owner's request
        // 2026-11-02: the practice panel always did, being later in the page). Both stay under
        // the toolbar's z-index.
        function raisePanel(panel) {
            for (const p of [rangePanel, practicePanel]) p.style.zIndex = p === panel ? '6' : '5';
        }
        // A press anywhere but an input starts it, once it has gone 4 px: a click on a
        // button or a cell stays a click, and the click a drag ends with does nothing.
        function installPanelDrag(panel) {
            let drag = null, dragged = false;
            panel.addEventListener('pointerdown', (e) => {
                raisePanel(panel);
                dragged = false;
                if (e.button !== 0 || e.target.closest('input, select, textarea')) return;
                const r = panel.getBoundingClientRect();
                drag = { id: e.pointerId, x: e.clientX, y: e.clientY, left: r.left, top: r.top, moving: false };
            });
            panel.addEventListener('pointermove', (e) => {
                if (!drag || e.pointerId !== drag.id) return;
                const dx = e.clientX - drag.x, dy = e.clientY - drag.y;
                if (!drag.moving) {
                    if (Math.abs(dx) + Math.abs(dy) < 4) return;
                    drag.moving = true;
                    panel.setPointerCapture(e.pointerId);
                    panel.classList.add('moving');
                }
                panel.dataset.moved = '1';
                panel.style.right = 'auto';
                panel.style.left = (drag.left + dx) + 'px';
                panel.style.top = (drag.top + dy) + 'px';
                clampPanel(panel);
                e.preventDefault();
            });
            const end = (e) => {
                if (!drag || e.pointerId !== drag.id) return;
                dragged = drag.moving;
                drag = null;
                panel.classList.remove('moving');
            };
            panel.addEventListener('pointerup', end);
            panel.addEventListener('pointercancel', end);
            panel.addEventListener('click', (e) => {
                if (!dragged) return;
                dragged = false;
                e.stopPropagation();
                e.preventDefault();
            }, true);
        }
        installPanelDrag(rangePanel);
        installPanelDrag(practicePanel);
        window.addEventListener('resize', () => { clampPanel(rangePanel); clampPanel(practicePanel); });
        // The slider says its value as it moves and sets the tempo where it is let go: a
        // playback restarts once, not at every step of a drag.
        function setSpeed(percent) {
            const old = practice.speed;
            practice.speed = percent / 100;
            if (old !== practice.speed) practiceChanged(old);
        }
        tempoSlider.addEventListener('input', () => {
            document.getElementById('tempoValue').textContent = tempoSlider.value + '%';
        });
        tempoSlider.addEventListener('change', () => setSpeed(Number(tempoSlider.value)));
        document.getElementById('tempoReset').addEventListener('click', () => setSpeed(100));
        // The parts as the score shown has them (owner's request 2026-11-09): nothing muted, the
        // parts it leaves out off, every voice sung. The lists are by name across files, so this
        // is also the way out of a part silenced in another file.
        document.getElementById('partsReset').addEventListener('click', () => {
            practice.muted = []; practice.extraOn = []; practice.voicesOff = []; practice.voicesOn = []; practice.instrumentsOn = [];
            for (const refresh of partRefreshers) refresh();
            practiceChanged(practice.speed);
        });
        // Every fader back to 100% — the objects stay (the lines hold them), their entries go.
        document.getElementById('fadersReset').addEventListener('click', () => {
            for (const k of Object.keys(practice.levels)) delete practice.levels[k];
            for (const k of Object.keys(practice.voiceLevels)) delete practice.voiceLevels[k];
            for (const refresh of partRefreshers) refresh();
            practiceChanged(practice.speed);
        });
        clickBtn.addEventListener('click', () => {
            practice.click = !practice.click;
            practiceChanged(practice.speed);
        });
        // The count-in (owner's choice 2026-10-26): from the next Play on, nothing restarts.
        countInBtn.addEventListener('click', () => {
            practice.countIn = !practice.countIn;
            showPractice();
        });
        showPractice();

        document.getElementById('firstPageBtn').addEventListener('click', () => gotoPage(0));
        document.getElementById('lastPageBtn').addEventListener('click', () => gotoPage(pages.length - 1));
        // change = spinner click, Enter, or blur after typing. A blank or
        // non-number entry snaps back to the page actually in view.
        function gotoPageNumber(v) {
            const i = Math.max(0, Math.min(pages.length - 1, v - 1));
            gotoPage(i);
            showPageNumber(i + 1);
        }
        pageInput.addEventListener('change', () => {
            const v = parseInt(pageInput.value, 10);
            if (!Number.isFinite(v)) { showPageNumber(currentPageIndex() + 1); return; }
            gotoPageNumber(v);
        });
        // The spinner reads top-to-bottom like the score: down = next page. A
        // native step (spinner click, wheel) arrives as an input event with no
        // inputType — typing always has one — so undo its direction here.
        pageInput.addEventListener('input', (e) => {
            const v = parseInt(pageInput.value, 10);
            if (!Number.isFinite(v)) return;
            // Typed digits are the base a spinner click right after steps from.
            if (e.inputType) { shownPageNumber = v; return; }
            gotoPageNumber(shownPageNumber - (v - shownPageNumber));
        });
        pageInput.addEventListener('keydown', (e) => {
            if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
                e.preventDefault();
                gotoPageNumber(shownPageNumber + (e.key === 'ArrowDown' ? 1 : -1));
            } else if (e.key === 'Enter') pageInput.blur();
            else if (e.key === 'Escape') {
                showPageNumber(currentPageIndex() + 1);
                pageInput.blur();
            }
        });
        pageInput.addEventListener('blur', updatePageInfo);
        mainContent.addEventListener('scroll', updatePageInfo);
        window.addEventListener('resize', () => {
            if (fitMode === 'width') fitWidth();
        });

        // The scan view's link (LilySharp-Omr proposal B4): the bar at the top of the
        // score goes out as the preview scrolls (the server's bar-box rects carry the
        // printed number), a bar the scan asks for is brought to the top, and the marks
        // the reader flagged are framed. A scroll asked for here is not reported back.
        // A mark frames what its box on the scan covers (owner's request 2026-10-17: the two
        // views frame the same thing; until then the whole system's bar was framed): its
        // lines ([from, to] of each part-bar it covers, as the scan's hover lights them —
        // lightPeerBars) frame every staff's bar whose first note is written on one of them;
        // a mark the scan does not know (no lines) frames the one staff's bar whose written
        // stretch (data-span) holds where it is written (at).
        // The frame itself is a shape of its own beside the staff's bar (path.flag-frame), the
        // bar's X range and two staff spaces taller above and below — the box the reader draws
        // round a part-bar on the scan (LilySharp-Omr Sidecar.AddPage: the staff's top line less
        // two spaces to its bottom line plus two; a staff is four spaces), so the two views
        // frame alike (owner's request 2026-10-17: the roomier frame is the easier to see).
        // An @todo memo is set over its staff's top line (under the bottom one), inside those two
        // spaces, so an edge of the frame can run through its words: the edge breaks there, a
        // quarter space clear of the memo's words and underline, and the frame keeps its size
        // (owner's report and decision 2026-10-27, tabi-13.lys: "枠線の y は狭めないでほしい").
        // The memo's leader may cross an edge; it runs down to the ring inside the frame anyway.
        let flaggedMarks = [];
        let barQuietUntil = 0, barReportTimer = null;
        function memoBoxes() {
            const boxes = [];
            for (const e of svgContainer.querySelectorAll('text[data-todo]:not(.music), line[data-todo]')) {
                if (e.tagName === 'line' && e.getAttribute('y1') !== e.getAttribute('y2')) continue; // a leader
                const b = e.getBoundingClientRect();
                if (b.width > 0 || b.height > 0) boxes.push(b);
            }
            return boxes;
        }
        function applyBarFlags() {
            for (const f of svgContainer.querySelectorAll('.flag-frame')) f.remove();
            let memos = null;
            // What is left of [from, to] once the gaps are taken out.
            const pieces = (from, to, gaps) => {
                let left = [[from, to]];
                for (const [a, b] of gaps)
                    left = left.flatMap(([p, q]) => b <= p || a >= q ? [[p, q]] : [[p, a], [b, q]].filter(([u, v]) => v > u));
                return left;
            };
            for (const r of svgContainer.querySelectorAll('rect.bar-staff')) {
                const start = parseInt(r.getAttribute('data-start'), 10);
                const span = r.hasAttribute('data-span') ? r.getAttribute('data-span').split(' ').map(v => parseInt(v, 10)) : null;
                const flagged = flaggedMarks.some(m => m.lines && m.lines.length > 0
                    ? m.lines.some(l => start >= l[0] && start <= l[1])
                    : span !== null && m.at >= span[0] && m.at <= span[1]);
                if (!flagged) continue;
                const x = Number(r.getAttribute('x')), y = Number(r.getAttribute('y'));
                const w = Number(r.getAttribute('width')), h = Number(r.getAttribute('height'));
                const top = y - h / 2, bottom = y + 1.5 * h, right = x + w;
                // Each edge's gaps: where a memo's box, a quarter space wider all round, lies across it.
                const gaps = { top: [], bottom: [], left: [], right: [] };
                const ctm = r.getScreenCTM();
                if (ctm && (memos ??= memoBoxes()).length > 0) {
                    // The memos in the bar rect's own coordinates (the bar rects are a system's,
                    // the memos the page's).
                    const inv = ctm.inverse(), pad = h / 16;
                    for (const b of memos) {
                        const p1 = new DOMPoint(b.left, b.top).matrixTransform(inv);
                        const p2 = new DOMPoint(b.right, b.bottom).matrixTransform(inv);
                        const mLeft = Math.min(p1.x, p2.x) - pad, mRight = Math.max(p1.x, p2.x) + pad;
                        const mTop = Math.min(p1.y, p2.y) - pad, mBottom = Math.max(p1.y, p2.y) + pad;
                        if (mRight <= x || mLeft >= right || mBottom <= top || mTop >= bottom) continue;
                        if (mTop < top && top < mBottom) gaps.top.push([mLeft, mRight]);
                        if (mTop < bottom && bottom < mBottom) gaps.bottom.push([mLeft, mRight]);
                        if (mLeft < x && x < mRight) gaps.left.push([mTop, mBottom]);
                        if (mLeft < right && right < mRight) gaps.right.push([mTop, mBottom]);
                    }
                }
                const d = [
                    ...pieces(x, right, gaps.top).map(([a, b]) => 'M ' + a + ' ' + top + ' H ' + b),
                    ...pieces(top, bottom, gaps.right).map(([a, b]) => 'M ' + right + ' ' + a + ' V ' + b),
                    ...pieces(x, right, gaps.bottom).map(([a, b]) => 'M ' + a + ' ' + bottom + ' H ' + b),
                    ...pieces(top, bottom, gaps.left).map(([a, b]) => 'M ' + x + ' ' + a + ' V ' + b),
                ].join(' ');
                const f = document.createElementNS('http://www.w3.org/2000/svg', 'path');
                f.setAttribute('class', 'flag-frame');
                f.setAttribute('d', d);
                f.setAttribute('fill', 'none');
                f.setAttribute('pointer-events', 'none');
                r.parentNode.insertBefore(f, r.nextSibling);
            }
        }
        // The bar at the top edge of the view, and how much of it is cut off above that edge
        // (0: its top is at the edge; 0.5: half of it is out of sight; negative: it starts
        // below the edge). The scan view puts its own copy of the bar at the same cut, so the
        // two panes are cut alike at the top (owner's request 2026-10-17).
        function topVisibleBar() {
            const view = mainContent.getBoundingClientRect();
            let best = null, bestTop = Infinity, bestLeft = Infinity, cut = 0;
            for (const r of svgContainer.querySelectorAll('rect.bar-box')) {
                const b = r.getBoundingClientRect();
                if (b.bottom <= view.top + 4 || b.top >= view.bottom || b.height === 0) continue;
                const top = Math.round(Math.max(b.top, view.top));
                if (top < bestTop || (top === bestTop && b.left < bestLeft)) {
                    best = r; bestTop = top; bestLeft = b.left; cut = (view.top - b.top) / b.height;
                }
            }
            return best ? { bar: best, cut } : null;
        }
        // The scan view and this preview scroll together by the bar at the top of each, cut
        // alike (cut, see topVisibleBar) — except at the very top, which is its own place:
        // scrolled all the way up, a view says so (top: true) and the other goes all the way up
        // too, title and all, instead of putting the first bar at its top edge and hiding the
        // title (owner's request 2026-10-17).
        function showBar(bar, top, cut) {
            barQuietUntil = Date.now() + 400;
            if (top) {
                mainContent.scrollTop = 0;
                return;
            }
            const r = svgContainer.querySelector('rect.bar-box[data-bar="' + bar + '"]');
            if (!r) return;
            const b = r.getBoundingClientRect();
            mainContent.scrollTop += b.top - mainContent.getBoundingClientRect().top + (cut || 0) * b.height;
        }
        mainContent.addEventListener('scroll', () => {
            if (Date.now() < barQuietUntil) return;
            clearTimeout(barReportTimer);
            barReportTimer = setTimeout(() => {
                if (mainContent.scrollTop <= 0) {
                    vscode.postMessage({ type: 'visibleBar', top: true });
                    return;
                }
                const t = topVisibleBar();
                if (t) vscode.postMessage({ type: 'visibleBar', bar: Number(t.bar.dataset.bar), cut: t.cut });
            }, 150);
        });

        window.addEventListener('message', event => {
            const message = event.data;
            console.log('Webview received message:', message.type);

            switch (message.type) {
                case 'setTheme':
                    applyTheme(message.theme);
                    break;
                case 'hideTodos':
                    hideTodos(message.keys || []);
                    break;
                case 'showBar':
                    showBar(message.bar, message.top === true, typeof message.cut === 'number' ? message.cut : 0);
                    break;
                case 'markBars':
                    flaggedMarks = Array.isArray(message.marks) ? message.marks : [];
                    applyBarFlags();
                    break;
                case 'peerBars':
                    lightPeerBars(message.from, message.to);
                    break;
                case 'retarget':
                    // The follower turns to another file: put this one's zoom and scroll
                    // away, start the new one blank, and give it back its own view when
                    // its picture arrives (pendingView, in updateContent).
                    viewByFile.set(message.from, { scale, fitMode, scrollTop: mainContent.scrollTop });
                    pendingView = viewByFile.get(message.to) || { scale: 1, fitMode: 'width', scrollTop: 0 };
                    stopPlayback();
                    pendingAudition = null;
                    playbackNotes = null;
                    pendingStartPos = -1;
                    lastResolvedPos = -1;
                    lastHighlightPos = -1;
                    lastHighlightTokenStart = -1;
                    lastHighlightRanges = null;
                    flaggedMarks = [];
                    hideErrorBanner();
                    svgContainer.classList.remove('stale');
                    svgContainer.innerHTML = '<div class="loading">Loading preview...</div>';
                    forgetShownPages();
                    invalidateSourcePositions();
                    collectPages();
                    break;
                case 'scanState':
                    docHasScan = !!message.hasScan;
                    break;
                case 'chordSpans':
                    chordSpanList = Array.isArray(message.spans) ? message.spans : [];
                    break;
                case 'todos':
                    todoMarks = Array.isArray(message.marks) ? message.marks : [];
                    break;
                case 'updateContent': {
                    updateRenderSelect(message.renders, message.selectedRender);
                    if (message.scoreKey) {
                        lastScoreKey = message.scoreKey;
                        if (!practicePanel.hidden && partsKey !== lastScoreKey) requestParts();
                    }
                    const hasPreview = !!svgContainer.querySelector('svg');
                    if (message.loading) {
                        // Don't replace an existing preview with a spinner — keep
                        // showing the last score while the server (re)starts.
                        if (!hasPreview) {
                            svgContainer.innerHTML = '<div class="loading">Waiting for language server...</div>';
                            forgetShownPages();
                            invalidateSourcePositions();
                        }
                    } else if (message.svg || message.pages) {
                        // A score arrived — it is CURRENT, so never dimmed. It may
                        // still carry an error banner: a file with parse errors
                        // renders best-effort (the bad parts are simply dropped).
                        // It comes as one string (an older server) or as pages — a
                        // delta against the picture on screen, or every page.
                        if (message.error) {
                            showErrorBanner(message.error);
                        } else {
                            hideErrorBanner();
                        }
                        svgContainer.classList.remove('stale');
                        const applyStart = performance.now();
                        const applied = message.pages ? applyPages(message.pages) : applySvg(message.svg);
                        const applyEnd = performance.now();
                        if (applied === null) {
                            // The delta could not be applied to what is shown; the
                            // whole picture has been asked for and will replace it.
                            break;
                        }
                        invalidateSourcePositions();
                        // The score changed: the cached note list is stale, so the
                        // next audition / Play refetches fresh events. A Play still sounding
                        // is the old text's: a voice of the new one is cut into it only where
                        // it still fits (joinVoice, voiceFitsPlay).
                        playbackNotes = null;
                        playEdited = true;
                        collectPages();
                        applyBarFlags();
                        lightCaretBar();
                        refreshRanges();
                        vscode.postMessage({ type: 'requestLength' });
                        // Keep the fit across re-renders (this is also what fits
                        // the FIRST render of a freshly opened preview); otherwise
                        // re-apply the current manual zoom to the fresh SVG.
                        if (pendingView) {
                            // The first picture after a turn: the view this file had.
                            scale = pendingView.scale;
                            fitMode = pendingView.fitMode;
                        }
                        if (fitMode === 'width') fitWidth();
                        else updateZoom();
                        if (pendingView) {
                            mainContent.scrollTop = pendingView.scrollTop;
                            pendingView = null;
                            updatePageInfo();
                        }
                        if (lastHighlightRanges) {
                            highlightRange(lastHighlightRanges);
                        } else if (lastHighlightPos >= 0) {
                            highlightNearestElement(lastHighlightPos, lastHighlightTokenStart);
                        }
                        lightSelectedBars();
                        const settled = performance.now();
                        const chars = message.pages ? pagesChars(message.pages) : message.svg.length;
                        vscode.postMessage({ type: 'webviewPerf', text: 'update: ' + applied
                            + ' in ' + (applyEnd - applyStart).toFixed(1) + ' ms, then pages/fit/highlight '
                            + (settled - applyEnd).toFixed(1) + ' ms (' + chars + ' chars)' });
                    } else if (message.error) {
                        // Nothing could render at all. Keep the last good preview,
                        // DIM it, and show the error in a banner. Only when nothing
                        // has rendered yet do we show the error in place (there is
                        // nothing to preserve).
                        if (hasPreview) {
                            svgContainer.classList.add('stale');
                            showErrorBanner(message.error);
                        } else {
                            hideErrorBanner();
                            svgContainer.innerHTML = '<div class="error">' + escapeHtml(message.error) + '</div>';
                            forgetShownPages();
                            invalidateSourcePositions();
                        }
                    }
                    break;
                }
                case 'playAtCursor': {
                    // The synth needs a current note list. If it's stale (an edit
                    // cleared it) or absent, fetch it and play once it arrives — but
                    // only one fetch in flight, so a held key's repeats don't pile up.
                    if (playbackNotes) {
                        runAudition(message);
                    } else {
                        const inFlight = !!pendingAudition;
                        pendingAudition = message;
                        if (!inFlight) { vscode.postMessage({ type: 'requestPlayback' }); }
                    }
                    break;
                }
                case 'playPitches':
                    playPitches(message.pitches, message.timbre, message.program, message.durationMs, message.strumMs || 0, !!message.apart);
                    break;
                // Another text's notes — an AI candidate under review (owner's request
                // 2026-10-25) — played as the Play button plays this score's, from the top. They
                // are not this page's: the cached note list is left alone, and their source
                // positions (R) are the candidate's, followed for its panel — nothing here lights up.
                case 'followTick':
                    if (followTick) followTick();
                    break;
                case 'voiceLinesChanged':
                    if (!practicePanel.hidden) requestParts();
                    break;
                case 'partsData':
                    renderParts(message);
                    // Its width is known once filled: keep it inside the window.
                    if (!practicePanel.hidden && practicePanel.dataset.moved) clampPanel(practicePanel);
                    else if (!practicePanel.hidden) {
                        const b = practiceBtn.getBoundingClientRect();
                        practicePanel.style.left = Math.max(6, Math.min(b.left, window.innerWidth - practicePanel.offsetWidth - 6)) + 'px';
                    }
                    break;
                case 'lengthData': {
                    const s = message.seconds;
                    const len = document.getElementById('lengthInfo');
                    len.textContent = typeof s === 'number' && s > 0
                        ? Math.floor(Math.round(s) / 60) + ':' + String(Math.round(s) % 60).padStart(2, '0')
                        : '';
                    break;
                }
                case 'rangesData':
                    rangeParts = message.parts || null;
                    rangeError = message.error || null;
                    renderRanges();
                    break;
                case 'playNotes':
                    loopList = null;
                    voicePlan = null;       // another text's notes: no voice of this score's
                    // Pressed on another panel, so no click here lets the sound through: said at
                    // every silent press, not once.
                    audioBlockedTold = false;
                    startPlayback(message.notes, 0);
                    break;
                case 'setSoundFont':
                    setSoundFont(message.uri);
                    break;
                case 'setMidiOutput':
                    setMidiOutput(message.name);
                    break;
                case 'voice':
                    setVoice(message);
                    break;
                case 'playVoiceSample':
                    playVoiceSample(message);
                    break;
                case 'voiceState':
                    setVoiceState(message.part, message.state, message.detail, message.estimateMs, message.singing, message.key);
                    break;
                case 'voiceProgress':
                    setVoiceTold(message);
                    break;
                case 'voiceParts':
                    keepVoices(message.parts);
                    break;
                case 'clearVoices':
                    clearVoices(!!message.pending);
                    break;
                case 'voiceSettled':
                    settleVoices();
                    break;
                case 'midiStarted':
                    midiStarted(message);
                    break;
                // Context-menu actions. Play reuses the Play button's path
                // exactly (fetch the events, then start at pendingStartPos), so
                // the right-clicked note resolves to an onset the same way a
                // highlighted note does.
                case 'ctxPlayFromHere':
                    if (ctxPlayPos >= 0) {
                        pendingStartPos = ctxPlayPos;
                        // From the note, as asked — not the selection, not round the loop.
                        pendingSpan = null;
                        pendingFromHere = true;
                        setPlayUi(true);
                        vscode.postMessage({ type: 'requestPlayback' });
                    }
                    break;
                case 'ctxStop':
                    stopPlayback();
                    break;
                case 'ctxFitWidth':
                    fitWidth();
                    break;
                case 'ctxResetZoom':
                    fitMode = null; // back to manual zoom, at 1:1
                    scale = 1;
                    updateZoom();
                    break;
                case 'ctxSelectBar':
                    // The bar the menu was opened on becomes the selection and the anchor
                    // Shift+click / Shift+drag extend from.
                    if (ctxBarRange) {
                        aiAnchorPos = ctxBarRange[0];
                        aiSetSelection(ctxBarRange[0], ctxBarRange[1]);
                    }
                    break;
                case 'ctxClearSelection':
                    aiClearSelection();
                    clearHighlights();
                    break;
                case 'playbackData':
                    if (message.error || !message.notes) {
                        setPlayUi(false);
                        pendingAudition = null;
                        if (message.error) {
                            zoomInfo.textContent = 'Play: ' + message.error;
                            zoomInfo.classList.add('visible');
                            setTimeout(() => zoomInfo.classList.remove('visible'), 2500);
                        }
                    } else if (pendingAudition) {
                        // These notes were fetched for an audition, not the Play button.
                        playbackNotes = message.notes;
                        const a = pendingAudition; pendingAudition = null;
                        runAudition(a);
                    } else if ((pendingSpan || (loopOn && !pendingFromHere)) && window.LysSynth && window.LysSynth.playSpan) {
                        // The selected bars, where they first play — and, looping, the stretch
                        // (or the whole score) as written once, round and round.
                        rawNotes = message.notes; rawParts = message.parts || null; rawBeats = message.beats || null; rawBarEnd = message.barEnd || null; rawExtra = message.extra || null; rawSung = message.sung || null; rawPlayed = message.played || null;
                        playbackNotes = practiced(rawNotes);
                        const span = pendingSpan;
                        pendingSpan = null;
                        pendingStartPos = -1;
                        pendingFromHere = false;
                        lastSpan = span;
                        lastOnce = loopOn;
                        const list = spanOf(playbackNotes, span, loopOn);
                        loopList = loopOn ? list : null;
                        const toPlay = loopOn ? looped(list) : list;
                        voicePlan = { pieces: spanPieces, rounds: loopOn ? loopRoundCount : 1 };
                        const c = counted(toPlay, 0, spanStart);
                        if (c) startPlayback(c.notes, 0, false, false, c.lead);
                        else startPlayback(toPlay, 0);
                    } else {
                        // The practice menu's tempo, parts and metronome, from the notes as written.
                        rawNotes = message.notes; rawParts = message.parts || null; rawBeats = message.beats || null; rawBarEnd = message.barEnd || null; rawExtra = message.extra || null; rawSung = message.sung || null; rawPlayed = message.played || null;
                        playbackNotes = practiced(rawNotes);
                        pendingSpan = null;
                        pendingFromHere = false;
                        lastSpan = undefined;
                        loopList = null;
                        voicePlan = { pieces: null, rounds: 1 };
                        let startAt = 0;
                        if (pendingStartPos >= 0) {
                            let first = Infinity;
                            for (const n of playbackNotes) {
                                if (n.S === pendingStartPos && n.T < first) first = n.T;
                            }
                            if (first === Infinity) {
                                // Nothing plays exactly there (mark, rest…):
                                // start at the next sounding event after it.
                                let bestS = Infinity;
                                for (const n of playbackNotes) {
                                    if (n.S >= pendingStartPos && (n.S < bestS || (n.S === bestS && n.T < first))) {
                                        bestS = n.S;
                                        first = n.T;
                                    }
                                }
                            }
                            if (first !== Infinity) startAt = first;
                        }
                        pendingStartPos = -1;
                        const c = counted(playbackNotes, startAt, 0);
                        if (c) startPlayback(c.notes, 0, false, false, c.lead - startAt);
                        else startPlayback(playbackNotes, startAt);
                    }
                    break;
                case 'highlightPosition':
                    lightCaretBar(message.caret, message.tokenStart, message.lineEnd);
                    lastHighlightPos = message.position;
                    lastHighlightTokenStart = message.tokenStart;
                    lastHighlightRanges = null;
                    scheduleHighlight(() => highlightNearestElement(message.position, message.tokenStart));
                    break;
                case 'highlightRange':
                    lightCaretBar(message.caret, -1);
                    lastHighlightRanges = message.ranges;
                    lastHighlightPos = -1;
                    scheduleHighlight(() => highlightRange(message.ranges));
                    break;
            }
        });

        installDragScroll(mainContent, document);

        // Resizes the sheet keeping the sheet point under (x, y) — the pointer — where it is: the
        // point's share of the sheet's box before is where it stands after, and the scroller moves
        // by the difference. (Owner's report 2026-11-11: on a lower page of a long score Ctrl+wheel
        // slid the view back toward the pages before — the sheet grew under a scroll offset that
        // stayed, so the same offset was an earlier place.)
        function zoomKeeping(scroller, sheetRect, resize, x, y) {
            const b = sheetRect();
            const fx = b.width > 0 ? (x - b.left) / b.width : 0;
            const fy = b.height > 0 ? (y - b.top) / b.height : 0;
            resize();
            const a = sheetRect();
            scroller.scrollLeft += a.left + fx * a.width - x;
            scroller.scrollTop += a.top + fy * a.height - y;
        }

        document.addEventListener('wheel', (e) => {
            if (e.ctrlKey) {
                e.preventDefault();
                const delta = e.deltaY > 0 ? -scaleStep : scaleStep;
                const next = Math.min(maxScale, Math.max(minScale, scale + delta));
                fitMode = null; // manual zoom overrides a fit mode
                const sheet = svgContainer.querySelector('svg');
                const resize = () => { scale = next; updateZoom(); };
                if (sheet && next !== scale) zoomKeeping(mainContent, () => sheet.getBoundingClientRect(), resize, e.clientX, e.clientY);
                else resize();
            }
        }, { passive: false });

        // During playback a click on a played note SEEKS there instead of jumping the editor
        // (listening mode). The clicked printed COPY — the first of the data-pos's instances
        // (in document order) holding an element isCopy accepts — maps to the onset whose
        // server-side ordinal (O) matches. False when nothing is playing or no onset is the
        // position's. Clears the active highlight FIRST: it raises its element, corrupting
        // the document order.
        function seekToCopy(pos, isCopy) {
            if (playheadTimer === null || !playbackNotes) return false;
            clearHighlights();
            const copies = Array.from(document.querySelectorAll('[data-pos="' + pos + '"]'));
            const instances = clusterInstances(copies);
            let occ = 0;
            for (let k = 0; k < instances.length; k++) {
                if (instances[k].some(isCopy)) { occ = k; break; }
            }
            let best = null;
            for (const n of playbackNotes) {
                if (n.S !== pos) continue;
                if ((n.O || 0) === occ && (best === null || n.T < best.T)) best = n;
            }
            if (best === null) {
                for (const n of playbackNotes) {
                    if (n.S === pos && (best === null || n.T < best.T)) best = n;
                }
            }
            if (best === null) return false;
            startPlayback(playbackNotes, best.T);
            return true;
        }

        installBarHover();

        // What a click means (owner's request 2026-11-03: the upper half of a chord's lower note went
        // to the note above it). A head's click box answers beyond its edges and the next head of a
        // chord lies over it: of the boxes there, the one whose middle is nearest. Anything else drawn
        // gives way only to a box it truly lies over, a mark's text answering for its whole em box.
        // Anything undrawn is what was pressed.
        function clickTargetOf(pressed, stackAtPointer, x, y) {
            const isBox = (el) => !!el && !!el.classList && el.classList.contains('nh-hit');
            const drawn = !!pressed && !!pressed.hasAttribute && pressed.hasAttribute('data-pos');
            if (!isBox(pressed) && !drawn) return pressed;
            return giveWay(pressed, isBox(pressed), stackAtPointer().filter(isBox), x, y);
        }
        function giveWay(pressed, onABox, boxes, x, y) {
            if (onABox) return boxes.length === 0 ? pressed : nearestTo(boxes, x, y);
            const inside = boxes.filter((el) => {
                const r = el.getBoundingClientRect();
                return x >= r.left && x <= r.left + r.width && y >= r.top && y <= r.top + r.height;
            });
            return inside.length === 0 ? pressed : nearestTo(inside, x, y);
        }
        // Of these elements, the one whose middle is nearest the point.
        function nearestTo(els, x, y) {
            const off = (el) => {
                const r = el.getBoundingClientRect();
                return Math.hypot(r.left + r.width / 2 - x, r.top + r.height / 2 - y);
            };
            return els.reduce((a, b) => (off(b) < off(a) ? b : a));
        }
        // At the press: a click's coordinates are whole pixels and a pointer's are not, which on a
        // head three pixels tall can be the next note.
        let pressedAt = null;
        window.addEventListener('pointerdown', (e) => { pressedAt = { x: e.clientX, y: e.clientY }; }, true);
        const noteUnder = (e) => {
            const x = pressedAt ? pressedAt.x : e.clientX, y = pressedAt ? pressedAt.y : e.clientY;
            return clickTargetOf(e.target, () => document.elementsFromPoint(x, y), x, y);
        };

        // Shift+drag across bars selects them (owner's request 2026-10-25). A plain drag still
        // scrolls the sheet (installDragScroll leaves a shifted press alone); the click a
        // Shift+drag ends with is swallowed, so it does not extend the selection once more.
        let shiftDrag = null, shiftDragged = false;
        svgContainer.addEventListener('pointerdown', (e) => {
            shiftDragged = false;
            shiftDrag = null;
            if (e.pointerType !== 'mouse' || e.button !== 0 || !e.shiftKey) return;
            const t = noteUnder(e);
            const pos = t && t.hasAttribute && t.hasAttribute('data-pos') ? parseInt(t.getAttribute('data-pos'), 10) : NaN;
            const bar = barStaffAt(e.clientX, e.clientY);
            const range = !isNaN(pos) ? [pos, pos] : bar ? barNoteRange(bar) : null;
            if (!range) return;
            e.preventDefault();   // no native text selection over the sheet
            shiftDrag = { bar, range };
        });
        svgContainer.addEventListener('pointermove', (e) => {
            if (!shiftDrag || !(e.buttons & 1)) return;
            const cur = barStaffAt(e.clientX, e.clientY);
            if (!cur || cur === shiftDrag.bar) return;
            const r = barNoteRange(cur);
            if (!r) return;
            shiftDragged = true;
            const forward = r[1] >= shiftDrag.range[0];
            aiAnchorPos = forward ? shiftDrag.range[0] : shiftDrag.range[1];
            aiSetSelection(aiAnchorPos, forward ? r[1] : r[0]);
        });
        window.addEventListener('pointerup', () => { shiftDrag = null; });
        document.addEventListener('click', (e) => {
            if (!shiftDragged) return;
            shiftDragged = false;
            e.stopPropagation();
            e.preventDefault();
        }, true);

        document.addEventListener('click', (e) => {
            const target = noteUnder(e);
            if (target && target.hasAttribute && target.hasAttribute('data-pos')) {
                const pos = parseInt(target.getAttribute('data-pos'), 10);
                // M3: shift-click extends a score selection from the anchor — it never
                // seeks/jumps. A prior plain click set the anchor; fall back to this
                // note if none is set yet.
                if (e.shiftKey) {
                    if (aiAnchorPos < 0) { aiAnchorPos = pos; }
                    aiSetSelection(aiAnchorPos, pos);
                    return;
                }
                // Plain click: this note becomes the anchor; any prior score
                // selection is dropped.
                aiAnchorPos = pos;
                aiRangeLo = -1; aiRangeHi = -1;
                lightSelectedBars();
                // During playback a click on a played note SEEKS there instead
                // of jumping the editor (listening mode). Non-note grobs
                // (barlines, marks) fall through to the normal click.
                if (seekToCopy(pos, (copy) => copy === target)) return;
                vscode.postMessage({ type: 'jumpToPosition', position: pos });
            } else if (e.shiftKey && !e.ctrlKey && !e.metaKey && !e.altKey
                && target && svgContainer.contains(target) && barStaffAt(e.clientX, e.clientY)) {
                // Shift+click on a bar extends the selection to the whole bar (owner's request
                // 2026-10-25: several bars selectable on the score, AI or not).
                extendSelectionToBar(barStaffAt(e.clientX, e.clientY));
            } else if (!e.shiftKey && !e.ctrlKey && !e.metaKey && !e.altKey
                && target && svgContainer.contains(target) && barStaffAt(e.clientX, e.clientY)) {
                // A bar: its first note's place in the editor — or, while playing, the
                // bar's own copy of that note (a section played twice prints it twice).
                const bar = barStaffAt(e.clientX, e.clientY);
                const pos = parseInt(bar.getAttribute('data-start'), 10);
                lastHighlightPos = -1;
                aiClearSelection();
                aiAnchorPos = (barNoteRange(bar) || [pos])[0];
                const box = bar.getBoundingClientRect();
                const inBar = (copy) => {
                    const b = copy.getBoundingClientRect();
                    const cx = (b.left + b.right) / 2, cy = (b.top + b.bottom) / 2;
                    return cx >= box.left && cx <= box.right
                        && cy >= box.top - box.height && cy <= box.bottom + box.height;
                };
                if (seekToCopy(pos, inBar)) return;
                clearHighlights();
                vscode.postMessage({ type: 'jumpToPosition', position: pos });
            } else if (target && mainContent.contains(target)) {
                // Clicked empty space / a non-clickable grob: just drop the
                // highlight. Don't move the editor cursor (no message sent), and
                // forget the position so a re-render doesn't bring it back.
                // Only on the sheet: a header button (▶ plays the selection — owner's report
                // 2026-10-25) or a panel over the score keeps the selection.
                lastHighlightPos = -1;
                aiClearSelection();
                clearHighlights();
            }
        });

        // Right-click: publish what the menu needs to know about THIS click —
        // whether it landed on a note (data-pos, same test the click handler
        // uses) and whether playback is running. VS Code reads
        // data-vscode-context off the element chain in its own contextmenu
        // listener, so this one runs in the CAPTURE phase to get there first.
        let ctxNotePos = -1;
        let ctxBarRange = null;   // the bar right-clicked, as its first and last note (ctxSelectBar)
        // Where the menu's Play from Here starts: the note right-clicked, else the first note of
        // the bar right-clicked (owner's request 2026-10-26: the menu on a bar too) — a rest
        // there starts at the next sounding note, as the Play path resolves any start.
        let ctxPlayPos = -1;
        let docHasScan = false;   // the file has an OMR side file (scanState, with each render)
        // The chords of the text, [its "<", its ">"] in order (chordSpans, with each render):
        // Select Chord on a member right-clicked puts the caret past the ">" (owner's request
        // 2026-10-27: where a chord's octave marks and decorations are written).
        let chordSpanList = [];
        // The @todo marks of the text, {start, hostStart} (todos, with each render): Resolve This
        // TODO on a bar that holds one, Resolve All TODOs off the staves (owner's request
        // 2026-10-31). A bar's marks are the ones whose host is written in its span (data-span).
        let todoMarks = [];
        function chordEndAt(pos) {
            let lo = 0, hi = chordSpanList.length - 1;
            while (lo <= hi) {
                const mid = (lo + hi) >> 1, s = chordSpanList[mid];
                if (pos <= s[0]) hi = mid - 1;
                else if (pos >= s[1]) lo = mid + 1;
                else return s[1] + 1;
            }
            return -1;
        }
        document.addEventListener('contextmenu', (e) => {
            const t = noteUnder(e);
            ctxNotePos = (t && t.hasAttribute && t.hasAttribute('data-pos'))
                ? parseInt(t.getAttribute('data-pos'), 10)
                : -1;
            // The bar under the pointer (owner's request 2026-10-24: Fix OMR Marks with AI and
            // Edit Bars Beside the Scan on the bar right-clicked): the staff box of a bar that
            // holds the point, where its text starts — else the note's own offset. The menu's
            // command gets it as its argument (lysAt).
            let ctxBarAt = -1;
            let ctxTodoStarts = [];
            for (const r of document.querySelectorAll('rect.bar-staff')) {
                const b = r.getBoundingClientRect();
                if (e.clientX >= b.left && e.clientX <= b.right && e.clientY >= b.top && e.clientY <= b.bottom) {
                    const s = parseInt(r.getAttribute('data-start'), 10);
                    if (!isNaN(s)) { ctxBarAt = s; }
                    const span = (r.getAttribute('data-span') || '').split(' ').map(Number);
                    // The bar's span runs from its first ITEM — past a group's "<<" — so a mark
                    // is the bar's by where the MARK is written (owner's report 2026-10-31:
                    // "<< c e g >>@todo" offered nothing), else by its host.
                    if (span.length === 2 && span[1] > span[0]) {
                        ctxTodoStarts = todoMarks.filter(m => (m.start >= span[0] && m.start < span[1])
                                || (m.hostStart >= span[0] && m.hostStart < span[1]))
                            .map(m => m.start).sort((a, b) => a - b);
                    }
                    if (!isNaN(s)) { break; }
                }
            }
            // What the menu's Change … with AI works on, and says it does (owner's requests
            // 2026-10-24 and 2026-10-25: Ctrl+I's door on the score, everywhere on it): the score
            // selection when there is one, else the note under the pointer, else the bar under
            // it, else — off the staves — the whole score, as Ctrl+I on an empty line does.
            ctxBarRange = null;
            for (const r of document.querySelectorAll('rect.bar-staff')) {
                const b = r.getBoundingClientRect();
                if (e.clientX >= b.left && e.clientX <= b.right && e.clientY >= b.top && e.clientY <= b.bottom) {
                    ctxBarRange = barNoteRange(r);
                    if (ctxBarRange) break;
                }
            }
            let ctxAiKind = 'score', ctxAiFrom = -1, ctxAiTo = -1, ctxAiSelected = '';
            if (aiRangeLo >= 0) {
                ctxAiKind = 'selection'; ctxAiFrom = aiRangeLo; ctxAiTo = aiRangeHi;
                ctxAiSelected = selectedLabel();
            } else if (ctxNotePos >= 0) {
                ctxAiKind = 'note'; ctxAiFrom = ctxAiTo = ctxNotePos;
                ctxAiSelected = '1 note selected';
            } else if (ctxBarRange) {
                ctxAiKind = 'bar'; ctxAiFrom = ctxBarRange[0]; ctxAiTo = ctxBarRange[1];
                ctxAiSelected = '1 bar selected';
            }
            if (ctxBarAt < 0) ctxBarAt = ctxNotePos;
            ctxPlayPos = ctxNotePos >= 0 ? ctxNotePos : ctxBarRange ? ctxBarRange[0] : -1;
            const ctxChordEnd = ctxNotePos >= 0 ? chordEndAt(ctxNotePos) : -1;
            document.body.dataset.vscodeContext = JSON.stringify({
                preventDefaultContextMenuItems: true,
                // The OMR doors only for a file with a scan (owner, 2026-10-24); the scan
                // state comes with each render (scanState).
                lysOnBar: docHasScan && ctxBarAt >= 0,
                lysAt: ctxBarAt,
                lysAiKind: ctxAiKind,
                lysAiFrom: ctxAiFrom,
                lysAiTo: ctxAiTo,
                lysAiSelected: ctxAiSelected,
                lysOnStaff: ctxBarRange !== null,
                lysHasSel: aiRangeLo >= 0,
                lysPlayFrom: ctxPlayPos >= 0,
                lysOnChord: ctxChordEnd >= 0,
                lysChordEnd: ctxChordEnd,
                lysBarTodo: ctxTodoStarts.length > 0,
                lysTodoStarts: ctxTodoStarts.join(','),
                lysHasTodos: todoMarks.length > 0,
                // The Stop button's enabled state is the playback flag that also
                // covers the optimistic window between Play and the first events.
                lysPlaying: !document.getElementById('stopBtn').disabled
            });
        }, true);

        // M3 keyboard: Ctrl/Cmd+I asks the AI in Copilot's chat, as the header button does (owner's
        // request 2026-10-29) — about the bars selected, else the file; Escape clears the selection.
        window.addEventListener('keydown', (e) => {
            if ((e.ctrlKey || e.metaKey) && (e.key === 'i' || e.key === 'I')) {
                e.preventDefault(); askInChat();
            } else if (e.key === 'Escape') {
                if (aiRangeLo >= 0) { aiClearSelection(); clearHighlights(); }
            }
        });

        // Everything above is registered — tell the extension it is now safe
        // to post content (messages before this point would have been lost).
        vscode.postMessage({ type: 'webviewReady' });
    </script>
</body>
</html>`;
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
    debounceTimers.forEach(timer => clearTimeout(timer));
    debounceTimers.clear();
    // The singing engine, and its own server with it.
    voiceRenderer?.dispose();
    voiceRenderer = undefined;

    if (!client) {
        return undefined;
    }
    return client.stop();
}


