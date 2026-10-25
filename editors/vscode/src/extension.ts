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
import { registerAiTransform, showCandidateSounding } from './aiTransform';
import { registerAiComplete } from './aiComplete';
import { pickAiModel } from './modelClient';
import { registerSmartTyping } from './smartTyping';
import { registerStepAudition } from './stepAudition';
import { registerCaretInfo } from './caretInfo';
import { registerExportBatch } from './exportBatch';
import { importFromImage } from './omrImport';
import { registerOmrContexts } from './omrReader';
import { OmrScanDeps, hasScan, previewHoversBar, previewReady, previewShowsBar, registerScanFollow, scanBarAt, scanColumnOf, scanThresholdOf, showScan } from './omrScan';
import { registerOmrAiFix } from './omrAiFix';
import { registerVerifyOnSave } from './omrVerify';
import { markdownItExtensionApi } from './markdownFence';
import { svgPostKey, pagesSummary, SvgPages, previewOnEditorShown, sourceGroupToReveal, newPreviewColumn, scoreGroupFor, sourceGroupToOpen, SourceTab, jumpOffsetFor } from './previewCore';
import { textFontFaceCss, textFontsRoot } from './scoreFonts';
import { SplitSectionsChoice, modalText, splitChoiceItems } from './splitSectionsCore';
import { cleanHistory, forgetFont, recordFont, samePath, shownFonts } from './soundFontHistoryCore';

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
// The follower: the one preview that is not locked. It turns to whichever .lys comes to the
// front, the Markdown preview's way (owner's decision 2026-10-12), so opening another score
// reuses the pane instead of adding a tab. Every other panel is locked to its own file.
let followerPanel: vscode.WebviewPanel | undefined;
// Panels whose page is playing: the follower turns only once the music stops, to the last
// .lys that came to the front meanwhile (pendingFollow) — a turn would cut the piece off.
const playingPanels = new Set<vscode.WebviewPanel>();
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
// That is what the first comment line below points at. The second points at the step
// keys (owner's request 2026-10-16: a writer who finds typing hard changes a note, the
// tempo, the key or the clef with them), and replaced the preview's Ctrl+K V. They are
// the only lines here that are not score.
//
// ⚠️ Two spaces, no tabs: 545 of the 548 tracked .lys files that indent at all use
// two spaces and not one uses a tab (measured 2026-08-23). The formatter cannot
// settle this — it takes the indent from the client's formatting options.
const NEW_SCORE_TEMPLATE = `// New to Lily#? Type "lesson" on a new line and pick lesson-01.
// Put the caret on a note, the tempo, the key or the clef and press Ctrl+Shift+Up/Down or Ctrl+Shift+PageUp/PageDown.
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
        initializationOptions: serverSettings(config)
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
        // `lilysharp.playback.midiOutput` through a picker of the machine's MIDI output ports
        // (the server lists them); the open previews pick it up through onDidChangeConfiguration.
        vscode.commands.registerCommand('lilysharp.chooseMidiOutput', () => chooseMidiOutput()),
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
        vscode.commands.registerCommand('lilysharp.extractPhrase', () => {
            outputChannel.appendLine('extractPhrase command triggered');
            extractPhrase();
        }),
        vscode.commands.registerCommand('lilysharp.addChordTrack', () => {
            outputChannel.appendLine('addChordTrack command triggered');
            addChordTrack();
        }),
        vscode.commands.registerCommand('lilysharp.guessSectionKey', () => guessSectionKey()),
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
            importFromImage(context, {
                output: outputChannel,
                openPreview: column => openPreview(context, column),
                lysc: bundledLysc,
                showScan: lys => showScan(context, deps, lys, vscode.ViewColumn.Three),
            }, uri, uris)),
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
        log: (msg: string) => outputChannel.appendLine(msg),
        // A candidate's Play (owner's request 2026-10-25) sounds through the document's preview.
        playOnPreview: (uri: vscode.Uri, notes: unknown[]) => {
            const panel = previewPanels.get(uri.toString());
            if (!panel) { return false; }
            void panel.webview.postMessage({ type: 'playNotes', notes });
            return true;
        },
        stopOnPreview: (uri: vscode.Uri) => {
            void previewPanels.get(uri.toString())?.webview.postMessage({ type: 'ctxStop' });
        },
        renderNameOf: (uri: vscode.Uri) => selectedRenders.get(uri.toString()) || undefined,
    };
    registerAiTransform(context, aiDeps);
    // Second mode: validated ghost-text "next measure" completion (opt-in).
    registerAiComplete(context, aiDeps);
    // Third: the bars an OMR reader marked, rewritten on request and reviewed beside the scan
    // (docs/omr-ai-fix-design.md). The scan view's slider says which bars are checked at first.
    // Its menu door in the preview and the scan view: the bar the right-click was on, the
    // preview's as an offset in the file it shows, the scan view's as a bar of its side file.
    registerOmrAiFix(context, {
        ...aiDeps, scanThreshold: scanThresholdOf, lysc: bundledLysc, scanColumn: scanColumnOf,
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
    // The score's selection from its menu: the bar right-clicked selected (Shift+click or
    // Shift+drag extends it), and the selection cleared.
    context.subscriptions.push(
        vscode.commands.registerCommand('lilysharp.previewSelectBar', () => postToActivePreview('ctxSelectBar')),
        vscode.commands.registerCommand('lilysharp.previewClearSelection', () => postToActivePreview('ctxClearSelection')));
    // The Settings screen's "Choose a model…" links name the feature ('edit' | 'ghost'), so the
    // list of features is skipped; the palette asks which.
    context.subscriptions.push(vscode.commands.registerCommand('lilysharp.selectAiModel',
        (use?: unknown) => pickAiModel(use === 'edit' || use === 'ghost' ? use : undefined)));

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
    });

    // Smart typing: the brackets it started with (`<` before c4 -> `<c>4`, and a
    // chord's '>' promoted to '>>' when its '<' is doubled into an arpeggio) plus
    // slurs, octave marks, beams, ties and durations — hence smartTyping, not
    // smartBrackets.
    registerSmartTyping(context, (msg: string) => outputChannel.appendLine(msg));

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
    context.subscriptions.push(
        vscode.window.onDidChangeActiveTextEditor(onEditorShown),
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
                    return;
                }
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
            if (message.type === 'requestPlayback') {
                // The webview cannot reach the LSP: fetch note events here and
                // hand them back for WebAudio scheduling.
                if (!client || !clientReady) {
                    panel.webview.postMessage({ type: 'playbackData', error: 'language server not running' });
                    return;
                }
                try {
                    // Name the score the preview is showing, so Play sounds ITS parts and
                    // form: with `score p2 { staff p2 }` picked, p1 stayed audible
                    // until the server was told which score (user report 2026-09-29).
                    const pb = await client.sendRequest<{ Notes?: { T: number, D: number, P: number, V: number }[], Error?: string,
                        Parts?: string[] | null, Beats?: { T: number; A: boolean }[] | null }>(
                        'lilysharp/playback', { textDocument: { uri }, renderName: selectedRenders.get(uri) || null });
                    // The tracks and the beats ride along for the practice menu's parts and metronome.
                    panel.webview.postMessage({ type: 'playbackData', notes: pb.Notes ?? null, error: pb.Error ?? null,
                        parts: pb.Parts ?? null, beats: pb.Beats ?? null });
                } catch (e) {
                    panel.webview.postMessage({ type: 'playbackData', error: String(e) });
                }
                return;
            }
            // The practice menu's part switches (owner's request 2026-10-25): the tracks of the
            // score shown that sound a note, in score order.
            if (message.type === 'requestParts') {
                if (!client || !clientReady) { return; }
                try {
                    const pb = await client.sendRequest<{ Notes?: { N?: number }[] | null; Parts?: string[] | null }>(
                        'lilysharp/playback', { textDocument: { uri }, renderName: selectedRenders.get(uri) || null });
                    const sounding = new Set((pb.Notes ?? []).map(n => n.N));
                    const parts = (pb.Parts ?? []).filter((_, i) => sounding.has(i));
                    panel.webview.postMessage({ type: 'partsData', parts });
                } catch {
                    panel.webview.postMessage({ type: 'partsData', parts: [] });
                }
                return;
            }
            // The header's length (owner's request 2026-10-25): how long the score shown plays,
            // repeats and tempo changes included — when its last note ends.
            if (message.type === 'requestLength') {
                if (!client || !clientReady) { return; }
                try {
                    const pb = await client.sendRequest<{ Notes?: { T: number; D: number }[] | null }>(
                        'lilysharp/playback', { textDocument: { uri }, renderName: selectedRenders.get(uri) || null });
                    const seconds = (pb.Notes ?? []).reduce((end, n) => Math.max(end, n.T + n.D), 0);
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
                const r = await midiOutRequest<{ Started?: boolean; StartedAtMs?: number; EndSeconds?: number; Error?: string | null }>(
                    'lilysharp/midiOut/play', { port: message.port, notes: message.notes, offset: message.offset });
                if (r.Error) { reportMidiOutError(message.port, r.Error); }
                panel.webview.postMessage({
                    type: 'midiStarted', ok: !!r.Started, t0Ms: r.StartedAtMs ?? 0, end: r.EndSeconds ?? 0, error: r.Error ?? null,
                });
                return;
            }
            if (message.type === 'midiStop') {
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
                await exportPreview(uri, message.renderName);
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
            } else if (message.type === 'aiTransformFromScore') {
                // M3: a note range selected ON THE SCORE maps to a text range, which
                // becomes the editor selection, then the same AI transform runs — so
                // the loop is identical whether the selection started in text or score.
                await aiTransformFromScore(uri, message.startPos, message.endPos, message.selected);
            } else if (message.type === 'aiWholeScore') {
                // The header's AI button (or Ctrl+I) with nothing selected on the score: the
                // editor's selection when there is one (owner's report 2026-10-25: notes selected
                // in the editor were answered with "Nothing is selected"), else the whole score.
                await aiTransformFromPreviewHeader(uri);
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
                selectedRender: drawnRender
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
                    selectedRender: drawnRender
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
                selectedRender: drawnRender
            });
        } else {
            outputChannel.appendLine('Response has neither error nor SVG');
        }
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
    if (!['png', 'pdf', 'svg', 'midi', 'musicxml', 'vsqx', 'ly'].includes(format)) {
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

    try {
        const response = await client.sendRequest<ExportResponse>('lilysharp/export', {
            textDocument: { uri },
            format,
            outputPath: target.fsPath,
            todoMarks,
            renderName: renderName || null
        });

        if (response.Success) {
            const openAction = 'Open File';
            const revealAction = 'Reveal in Explorer';
            const choice = await vscode.window.showInformationMessage(
                `Exported to ${response.OutputPath}`, openAction, revealAction);
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
        ? vscode.workspace.textDocuments.find(d => d.uri.toString() === uriArg)
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
 * Auto-harmonizes the active melody: asks the server for a diatonic chords part
 * and applies the returned edits (inserts a `chords harmony { }` block and wires
 * it to the melody staff). The result is a starting point to edit.
 */
async function addChordTrack() {
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
    try {
        const response = await client.sendRequest<AddChordTrackResponse>('lilysharp/addChordTrack', {
            textDocument: { uri: doc.uri.toString() }
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
                `Lily#: added a diatonic chord track — a starting point, edit as needed.${note}`);
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

/** The whole score from the preview: its editor brought forward, then Ctrl+I with `whole`. */
async function aiTransformFromPreviewHeader(uri: string): Promise<void> {
    // The editor showing the file keeps its selection while the preview has the focus.
    const editor = vscode.window.visibleTextEditors.find(e => e.document.uri.toString() === uri && !e.selection.isEmpty);
    if (!editor) {
        await aiTransformWholeScore(uri);
        return;
    }
    await vscode.window.showTextDocument(editor.document, { viewColumn: editor.viewColumn, preserveFocus: false });
    const lines = editor.selection.end.line - editor.selection.start.line + 1;
    await vscode.commands.executeCommand('lilysharp.aiTransform',
        { selected: lines === 1 ? 'The text selected in the editor' : `${lines} lines selected in the editor` });
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
 *  file on this machine (a missing one is logged and the default used); else a font under
 *  media/soundfont, which a release does not carry (a developer's own build may); else the
 *  General MIDI bank the OS ships (Windows' gm.dls, macOS's DLS bank). Null when there is
 *  none — Linux without a font set: the preview then plays its oscillator patches.
 *  Owner's decision 2026-10-12: no font in the VSIX; the OS's bank by default, and a better
 *  one is the user's to install (the editor page names three and how). */
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

/** What plays when the setting is empty, named as the picker shows it: a font under
 *  media/soundfont (a developer's local build — a release carries none), else the OS's bank.
 *  ONE answer for the player and the picker: the picker said "gm.dls" while a local build
 *  played its MuseScore_General, which then had no name in the list (owner's report
 *  2026-10-12). */
function defaultSoundFont(extensionUri: vscode.Uri): { label: string; path: string } | null {
    const dir = vscode.Uri.joinPath(extensionUri, 'media', 'soundfont');
    try {
        const files = fs.readdirSync(dir.fsPath).filter(f => /\.(sf2|sf3|dls)$/i.test(f)).sort();
        if (files.length > 0) {
            return { label: `${files[0]} (this build's own, under media/soundfont)`, path: path.join(dir.fsPath, files[0]) };
        }
    } catch { /* none here — a release carries none */ }
    return osSoundFonts()[0] ?? null;
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
        #svgContainer rect.flag-frame {
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
        /* A toggle that is on looks pressed in (owner's request 2026-10-25: Range read as a plain
           button): sunk, blue-edged, tinted. */
        .toolbar button.toggle[aria-pressed="true"] {
            background: #d6e6f8; border-color: #0078d7; color: #0b4a8b;
            box-shadow: inset 0 1px 3px rgba(0, 0, 0, 0.3);
        }
        .toolbar button.toggle[aria-pressed="true"]:hover { background: #c8ddf5; }
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
        /* How long the score plays (owner's request 2026-10-25), beside the transport. */
        .toolbar .length {
            font-family: system-ui, sans-serif; font-size: 12px; color: #555;
            font-variant-numeric: tabular-nums;
        }
        /* The Range panel (owner's request 2026-10-25): each part's lowest and highest note over
           the score, top right under the toolbar; a click on one shows it on the score. */
        .range-panel {
            position: fixed; right: 12px; z-index: 20;
            background: var(--vscode-editorWidget-background, #f3f3f3);
            color: var(--vscode-editorWidget-foreground, inherit);
            border: 1px solid var(--vscode-editorWidget-border, #c8c8c8);
            border-radius: 4px; box-shadow: 0 2px 8px rgba(0, 0, 0, 0.25);
            padding: 6px 8px; font-size: 12px; max-height: 60vh; overflow: auto;
        }
        .range-panel[hidden] { display: none; }
        /* The practice menu (owner's request 2026-10-25): the button opens it under itself; its
           rows are the tempo, the metronome and the parts. */
        .toolbar button.practice {
            display: inline-flex; align-items: center; gap: 3px; height: 28px; padding: 0 8px;
            line-height: 1; font-variant-numeric: tabular-nums; font-size: 12px;
        }
        .toolbar button.practice svg { display: block; width: 15px; height: 15px; }
        .toolbar button.practice svg.caret { width: 8px; height: 8px; opacity: 0.7; }
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
        /* A part sounds while its button is pressed in; one let out is silent. */
        .practice-panel .parts { display: flex; flex-direction: column; align-items: stretch; gap: 4px; }
        .practice-panel .parts button { text-align: left; }
        .practice-panel .parts button[aria-pressed="false"] { color: #888; text-decoration: line-through; }
        .range-panel table { border-collapse: collapse; }
        .range-panel th { text-align: left; font-weight: 600; opacity: 0.75; padding: 2px 8px; }
        .range-panel td { padding: 2px 8px; white-space: nowrap; }
        .range-panel td.end { cursor: pointer; border-radius: 3px; }
        .range-panel td.end:hover { background: var(--vscode-list-hoverBackground, rgba(128, 128, 128, 0.2)); }
        .range-panel td.end.cur { background: var(--vscode-list-activeSelectionBackground, rgba(0, 120, 215, 0.25)); }
        .range-panel .pitch { font-weight: 600; }
        .range-panel .where { opacity: 0.7; }
        .range-panel .note { opacity: 0.7; padding: 4px 8px 0; }
    </style>
</head>
<!-- The preview is a rendered score, not an editable text buffer, so VS Code's
     default webview context menu (Copy / Cut / Paste) has nothing to act on:
     Cut and Paste can never apply, and Copy only ever picked up stray SVG text.
     preventDefaultContextMenuItems drops those built-in entries, leaving only
     what this extension contributes under menus/webview/context. lysNote and
     lysPlaying are the when-clause keys those items gate on; the contextmenu
     handler below refreshes them for each click. -->
<body data-vscode-context='{"preventDefaultContextMenuItems": true, "lysNote": false, "lysPlaying": false}'>
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
        <button id="aiTransformBtn" class="ai icon" type="button" title="Ask AI (Ctrl+I)">✨</button>
        <button id="rangeBtn" class="toggle icon" type="button" aria-pressed="false" aria-label="Range" title="Each part's range"><svg viewBox="0 -.25 16.5 16.5" fill="currentColor" aria-hidden="true"><path d="M3 1.5 5.95 5.3H3.8v6.6h2.15L3 15.7.05 11.9H2.2V5.3H.05z"/><g transform="translate(.8 0)"><ellipse cx="9.4" cy="12.4" rx="2.8" ry="2" transform="rotate(-22 9.4 12.4)"/><rect x="11.35" y="1.5" width="1.05" height="10.8"/><path d="M12.4 1.5c.3 2 1.6 2.8 2.4 3.9.9 1.5 1.1 3.6.3 5.5.1-2.1-.9-3.6-2.7-4.2z"/></g></svg></button>
        </span>
        <span class="sep"></span>
        <span class="group">
        <button id="playBtn" type="button" title="Play">▶</button>
        <button id="stopBtn" type="button" title="Stop" disabled>⏹</button>
        <button id="practiceBtn" class="toggle practice" type="button" aria-pressed="false" aria-haspopup="true" aria-expanded="false" aria-label="Practice" title="Tempo, loop, metronome, parts"><svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M4 14.5h8L9.6 2H6.4z"/><path d="M8 11.5l3.6-6.5"/><path d="M5.2 10.5h5.6"/></svg><span id="practiceSpeed"></span><svg class="caret" viewBox="0 0 8 8" fill="currentColor" aria-hidden="true"><path d="M1 2.5h6L4 6z"/></svg></button>
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
    <div id="rangePanel" class="range-panel" hidden></div>
    <div id="practicePanel" class="range-panel practice-panel" hidden>
        <div class="prow"><span class="plbl">Tempo</span><span class="tempo"><input type="range" id="tempoSlider" min="50" max="150" step="5" value="100" aria-label="Tempo"><span id="tempoValue" class="tval">100%</span><button type="button" id="tempoReset" title="Back to the tempo as written (100%)">Reset</button></span></div>
        <div class="prow"><span class="plbl">Loop</span><button type="button" id="loopBtn" class="toggle" aria-pressed="false" title="Play the selected bars, or the whole score, round and round — the score's repeats ignored">Off</button></div>
        <div class="prow"><span class="plbl">Metronome</span><button type="button" id="clickBtn" class="toggle" aria-pressed="false">Off</button></div>
        <div class="prow"><span class="plbl">Parts</span><span id="partsList" class="parts"></span></div>
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
        // names it in a few words, and with bars selected says they are a hint for the AI.
        function updateAiButton() {
            aiTransformBtn.title = aiRangeLo < 0
                ? 'Ask AI (Ctrl+I)'
                : 'Ask AI, the selected bars as a hint (Ctrl+I)';
        }
        function aiSubmitSelection() {
            if (aiRangeLo < 0) {
                vscode.postMessage({ type: 'aiWholeScore' });
                return;
            }
            vscode.postMessage({ type: 'aiTransformFromScore', startPos: aiRangeLo, endPos: aiRangeHi,
                selected: selectedLabel() });
        }
        aiTransformBtn.addEventListener('click', aiSubmitSelection);

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
            // and only SPLIT when the next element sits far from the
            // previous one (another measure or another system). The earlier
            // y-band sort could merge two systems through ledger-note ys and
            // then x-sorting interleaved their copies out of time order.
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

        let playingOscs = [];
        let playEndTimer = null;
        let playheadTimer = null;
        let relaying = false;       // the current playback is a candidate's, followed for its panel
        let tickWatch = null;       // the follow-along tick's evenness, for the log (followAlong)
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
            rangePanel.style.top = (document.querySelector('.toolbar').getBoundingClientRect().bottom + 6) + 'px';
            if (!rangeParts && !rangeError) rangePanel.textContent = 'Reading the ranges…';
            refreshRanges();
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
            rangePanel.textContent = '';
            if (rangeError) { rangePanel.textContent = 'Range: ' + rangeError; return; }
            if (!rangeParts) return;
            if (rangeParts.length === 0) { rangePanel.textContent = 'No pitched notes in this score.'; return; }
            const table = document.createElement('table');
            const head = document.createElement('tr');
            for (const h of ['Part', 'Lowest', 'Highest']) {
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
                table.appendChild(tr);
            }
            rangePanel.appendChild(table);
            const note = document.createElement('div');
            note.className = 'note';
            note.textContent = 'Sounding pitches · C4 = middle C';
            rangePanel.appendChild(note);
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
            clearTimeout(playEndTimer);
            playEndTimer = null;
            clearInterval(playheadTimer);
            playheadTimer = null;
            playClock = null;
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
        function startPlayback(notes, offset, plainTones, here) {
            offset = offset || 0;
            stopPlayback();
            if (!notes || notes.length === 0) { return; }
            // Seek = re-schedule from the offset; notes already past it are
            // dropped (mid-note tails too — re-striking half a note reads
            // worse than starting cleanly at the next onset).
            notes = notes.filter(n => n.T >= offset - 0.001);
            if (notes.length === 0) { return; }
            if (midiOutput && !here) {
                // The MIDI output: the server plays the list from the offset and answers
                // (midiStarted) with the clock the highlight follows; a port that is not
                // there comes back as an error, and the list plays here instead.
                const gen = ++playGeneration;
                midiPlayPending = { gen, notes, offset };
                setPlayUi(true);
                vscode.postMessage({ type: 'midiPlay', port: midiOutput, notes, offset });
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
                    startPlayback(notes, offset, !ok);
                });
                return;
            }
            let t0 = ctx.currentTime + 0.15;
            let end;
            if (sf && sf.ready && !plainTones) {
                sfPlayer = sf.play(notes, offset, t0);
                t0 = sfPlayer.t0;
                end = sfPlayer.end;
            } else {
                end = startOscillators(ctx, notes, offset, t0);
            }
            setPlayUi(true);
            // A loop goes round when the last note ends; a plain playback lets it ring a moment.
            playEndTimer = setTimeout(playEnded, (end - offset + (loopList ? 0 : 0.5)) * 1000);
            playOffset = offset;
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
        function followAlong(notes, offset, elapsed) {
            const Cursor = window.LysSynth ? window.LysSynth.SoundingCursor : null;
            if (!Cursor) { return; }
            const relay = notes.some(n => n.R !== undefined);
            relaying = relay;
            const cursor = new Cursor(notes.map(n => ({ t: n.T - offset, d: n.D, s: relay ? n.R : n.S, o: n.O || 0 })));
            // How evenly the 50 ms tick came (owner's report 2026-10-25: one eighth note lit in
            // four — the tick a second apart, as a browser throttles a page it thinks hidden):
            // the longest gap and whether the page said it was hidden, logged when it stops.
            tickWatch = { last: performance.now(), longest: 0, ticks: 0, hidden: document.hidden };
            playheadTimer = setInterval(() => {
                const now = performance.now();
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
                const el = r.hadNew && painted.length > 0 ? painted[0] : null;
                if (el) {
                    const rect = el.getBoundingClientRect();
                    const m = mainContent.getBoundingClientRect();
                    const off = rect.top < m.top + 30 || rect.bottom > m.bottom - 30
                             || rect.left < m.left + 30 || rect.right > m.right - 30;
                    if (off) el.scrollIntoView({ block: 'center', inline: 'center', behavior: 'smooth' });
                }
            }, 50);
        }

        // The fallback: oscillator patches, one node pair per note, built up front (fine
        // for the short pieces this plays while no sound font is loaded). Timbre families
        // (note.I): waveform + envelope approximations; pluck = exponential decay. The sum
        // goes through a limiter — many voices at once used to clip. Returns when the last
        // note ends, in seconds from the piece's start.
        const PATCHES = [
            { w: 'triangle', a: 0.010, s: 0.55, r: 0.05, g: 1.00, pluck: true,  tau: 0.9  }, // 0 piano-ish
            { w: 'sine',     a: 0.050, s: 0.90, r: 0.06, g: 1.15, pluck: false, tau: 0    }, // 1 flute
            { w: 'square',   a: 0.030, s: 0.75, r: 0.05, g: 0.45, pluck: false, tau: 0    }, // 2 clarinet/reed
            { w: 'sawtooth', a: 0.080, s: 0.85, r: 0.09, g: 0.55, pluck: false, tau: 0    }, // 3 strings
            { w: 'triangle', a: 0.005, s: 0.40, r: 0.04, g: 1.00, pluck: true,  tau: 0.35 }, // 4 guitar
            { w: 'sine',     a: 0.008, s: 0.45, r: 0.05, g: 1.30, pluck: true,  tau: 0.5  }, // 5 bass
            { w: 'sawtooth', a: 0.020, s: 0.90, r: 0.06, g: 0.50, pluck: false, tau: 0    }, // 6 brass
            { w: 'square',   a: 0.040, s: 1.00, r: 0.05, g: 0.35, pluck: false, tau: 0    }, // 7 organ
            { w: 'sine',     a: 0.060, s: 0.90, r: 0.08, g: 1.10, pluck: false, tau: 0    }, // 8 voice
            { drum: true }                                                                     // 9 drums (noise)
        ];
        function oscillatorOutput(ctx, gain) {
            const master = ctx.createGain();
            master.gain.value = gain;
            const limiter = ctx.createDynamicsCompressor();
            limiter.threshold.value = -10;
            limiter.knee.value = 12;
            limiter.ratio.value = 12;
            limiter.attack.value = 0.002;
            limiter.release.value = 0.12;
            master.connect(limiter);
            limiter.connect(ctx.destination);
            return master;
        }
        function startOscillators(ctx, notes, offset, t0) {
            const master = oscillatorOutput(ctx, 0.25);
            let end = 0;
            // Shared white-noise buffer for the drum patch (timbre 9).
            let noiseBuf = null;
            const getNoise = () => {
                if (!noiseBuf) {
                    noiseBuf = ctx.createBuffer(1, ctx.sampleRate, ctx.sampleRate);
                    const d = noiseBuf.getChannelData(0);
                    for (let i = 0; i < d.length; i++) { d[i] = Math.random() * 2 - 1; }
                }
                return noiseBuf;
            };
            for (const n of notes) {
                const p = PATCHES[n.I] || PATCHES[0];
                if (p.drum) {
                    // GM percussion approximation: filtered noise burst.
                    // Kick (<45): lowpass thump; snare-ish (45-47 + 38-40):
                    // bandpass; cymbals/hats (42+, 49+): highpass sizzle.
                    const src = ctx.createBufferSource();
                    src.buffer = getNoise();
                    const filt = ctx.createBiquadFilter();
                    let tau = 0.06;
                    if (n.P <= 36) { filt.type = 'lowpass'; filt.frequency.value = 180; tau = 0.10; }
                    else if (n.P === 38 || n.P === 39 || n.P === 40) { filt.type = 'bandpass'; filt.frequency.value = 1800; filt.Q.value = 0.8; tau = 0.09; }
                    else if (n.P >= 49 && n.P !== 51) { filt.type = 'highpass'; filt.frequency.value = 5000; tau = 0.45; } // crash etc ring
                    else if (n.P === 46) { filt.type = 'highpass'; filt.frequency.value = 6500; tau = 0.25; } // open hat
                    else if (n.P >= 41 && n.P <= 48 && n.P !== 42 && n.P !== 44) { filt.type = 'bandpass'; filt.frequency.value = 300 + (n.P - 41) * 90; filt.Q.value = 1.2; tau = 0.12; } // toms
                    else { filt.type = 'highpass'; filt.frequency.value = 7500; tau = 0.05; } // closed/pedal hat, ride tick
                    const dg = ctx.createGain();
                    const dat = t0 + n.T - offset;
                    const dpk = 0.9 * (n.V / 127);
                    dg.gain.setValueAtTime(0, dat);
                    dg.gain.linearRampToValueAtTime(dpk, dat + 0.004);
                    dg.gain.setTargetAtTime(0.0001, dat + 0.004, tau);
                    src.connect(filt); filt.connect(dg); dg.connect(master);
                    src.start(dat);
                    const dstop = dat + Math.min(n.D, tau * 6) + 0.05;
                    src.stop(dstop);
                    playingOscs.push(src);
                    if (n.T + n.D > end) end = n.T + n.D;
                    continue;
                }
                const osc = ctx.createOscillator();
                osc.type = p.w;
                osc.frequency.value = 440 * Math.pow(2, (n.P - 69) / 12);
                const g = ctx.createGain();
                const at = t0 + n.T - offset;
                const rel = at + n.D;
                const peak = 0.9 * (n.V / 127) * p.g;
                g.gain.setValueAtTime(0, at);
                g.gain.linearRampToValueAtTime(peak, at + p.a);
                if (p.pluck) {
                    // Plucked/struck: exponential decay through the note,
                    // then a faster decay as the release.
                    g.gain.setTargetAtTime(peak * 0.15, at + p.a, p.tau);
                    g.gain.setTargetAtTime(0.0001, rel, 0.03);
                } else {
                    // Sustained: hold, ease to the sustain level, release.
                    g.gain.setValueAtTime(peak, Math.max(at + p.a, rel - 0.06));
                    g.gain.linearRampToValueAtTime(peak * p.s, Math.max(at + p.a, rel - 0.02));
                    g.gain.linearRampToValueAtTime(0.0001, rel + p.r);
                }
                osc.connect(g);
                g.connect(master);
                osc.start(at);
                osc.stop(rel + p.r + 0.05);
                playingOscs.push(osc);
                if (n.T + n.D > end) end = n.T + n.D;
            }
            return end;
        }

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
            let minT = Infinity;
            for (const n of sub) { if (n.T < minT) { minT = n.T; } }
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
            pendingSpan = aiRangeLo >= 0 ? [aiRangeLo, aiRangeHi] : null;
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
        // Where a playback that ran to its end goes: round again while looping, else it stops.
        function playEnded() {
            const list = loopOn ? loopList : null;
            if (!list) { stopPlayback(); return; }
            startPlayback(list, 0);
            loopList = list;
        }

        // ---- The practice menu (owner's request 2026-10-25): a button that drops down the
        // controls a practice session wants and the header has no room for — the tempo (a slider,
        // 50–150%), the loop, the metronome, the parts on and off (a toggle each). A change while
        // playing goes on from the same place.
        const practice = { speed: 1, muted: [], click: false };
        let rawNotes = null, rawParts = null, rawBeats = null;  // the server's last answer, as written
        let lastSpan;               // what plays: undefined the whole score, null/[lo, hi] a playSpan
        let lastOnce = false;
        let playOffset = 0, playClock = null;   // where the playback stands (startPlayback / midiStarted)
        function practiced(notes) {
            return window.LysSynth && window.LysSynth.practiceNotes
                ? window.LysSynth.practiceNotes(notes, rawParts, rawBeats, practice)
                : notes;
        }
        const practiceBtn = document.getElementById('practiceBtn');
        const practicePanel = document.getElementById('practicePanel');
        const clickBtn = document.getElementById('clickBtn');
        const tempoSlider = document.getElementById('tempoSlider');
        function showPractice() {
            const speedOn = practice.speed !== 1;
            document.getElementById('practiceSpeed').textContent = speedOn ? Math.round(practice.speed * 100) + '%' : '';
            // Pressed in while anything differs from the score as written.
            practiceBtn.setAttribute('aria-pressed', String(speedOn || practice.click || practice.muted.length > 0 || loopOn));
            // The loop lives here too (owner's request 2026-10-25), an On/Off as the metronome's.
            loopBtn.setAttribute('aria-pressed', String(loopOn));
            loopBtn.textContent = loopOn ? 'On' : 'Off';
            tempoSlider.value = String(Math.round(practice.speed * 100));
            document.getElementById('tempoValue').textContent = Math.round(practice.speed * 100) + '%';
            document.getElementById('tempoReset').disabled = !speedOn;
            clickBtn.setAttribute('aria-pressed', String(practice.click));
            clickBtn.textContent = practice.click ? 'On' : 'Off';
        }
        function practiceChanged(oldSpeed) {
            showPractice();
            const playing = !document.getElementById('stopBtn').disabled;
            if (!playing || relaying || !rawNotes || !playClock) return;
            const at = (playOffset + playClock()) * oldSpeed / practice.speed;
            playbackNotes = practiced(rawNotes);
            if (lastSpan === undefined) { startPlayback(playbackNotes, at); return; }
            const list = window.LysSynth.playSpan(playbackNotes, lastSpan ? lastSpan[0] : null, lastSpan ? lastSpan[1] : null, lastOnce);
            loopList = loopOn ? list : null;
            startPlayback(list, at);
            loopList = loopOn ? list : null;
        }
        function renderParts(names) {
            const list = document.getElementById('partsList');
            list.textContent = '';
            if (!names || names.length === 0) { list.textContent = '—'; return; }
            // One toggle a part, pressed in while it sounds (owner's request 2026-10-25). A part
            // muted in another score stays muted: the list is by name.
            for (const name of names) {
                const b = document.createElement('button');
                b.type = 'button';
                b.textContent = name;
                b.setAttribute('aria-pressed', String(!practice.muted.includes(name)));
                b.addEventListener('click', () => {
                    const on = practice.muted.includes(name);
                    practice.muted = on ? practice.muted.filter(n => n !== name) : practice.muted.concat([name]);
                    b.setAttribute('aria-pressed', String(on));
                    practiceChanged(practice.speed);
                });
                list.appendChild(b);
            }
        }
        function closePractice() {
            practicePanel.hidden = true;
            practiceBtn.setAttribute('aria-expanded', 'false');
        }
        practiceBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            if (!practicePanel.hidden) { closePractice(); return; }
            const b = practiceBtn.getBoundingClientRect();
            practicePanel.hidden = false;
            practicePanel.style.top = (document.querySelector('.toolbar').getBoundingClientRect().bottom + 6) + 'px';
            practicePanel.style.right = 'auto';
            practicePanel.style.left = Math.max(6, Math.min(b.left, window.innerWidth - practicePanel.offsetWidth - 6)) + 'px';
            practiceBtn.setAttribute('aria-expanded', 'true');
            vscode.postMessage({ type: 'requestParts' });
        });
        practicePanel.addEventListener('click', (e) => e.stopPropagation());
        document.addEventListener('click', () => { if (!practicePanel.hidden) closePractice(); });
        document.addEventListener('keydown', (e) => { if (e.key === 'Escape' && !practicePanel.hidden) closePractice(); });
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
        clickBtn.addEventListener('click', () => {
            practice.click = !practice.click;
            practiceChanged(practice.speed);
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
        // The frame itself is a rect of its own beside the staff's bar (rect.flag-frame), the
        // bar's X range and two staff spaces taller above and below — the box the reader draws
        // round a part-bar on the scan (LilySharp-Omr Sidecar.AddPage: the staff's top line less
        // two spaces to its bottom line plus two; a staff is four spaces), so the two views
        // frame alike (owner's request 2026-10-17: the roomier frame is the easier to see).
        let flaggedMarks = [];
        let barQuietUntil = 0, barReportTimer = null;
        function applyBarFlags() {
            for (const f of svgContainer.querySelectorAll('rect.flag-frame')) f.remove();
            for (const r of svgContainer.querySelectorAll('rect.bar-staff')) {
                const start = parseInt(r.getAttribute('data-start'), 10);
                const span = r.hasAttribute('data-span') ? r.getAttribute('data-span').split(' ').map(v => parseInt(v, 10)) : null;
                const flagged = flaggedMarks.some(m => m.lines && m.lines.length > 0
                    ? m.lines.some(l => start >= l[0] && start <= l[1])
                    : span !== null && m.at >= span[0] && m.at <= span[1]);
                if (!flagged) continue;
                const x = Number(r.getAttribute('x')), y = Number(r.getAttribute('y'));
                const w = Number(r.getAttribute('width')), h = Number(r.getAttribute('height'));
                const f = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
                f.setAttribute('class', 'flag-frame');
                f.setAttribute('x', x);
                f.setAttribute('y', y - h / 2);
                f.setAttribute('width', w);
                f.setAttribute('height', 2 * h);
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
                case 'updateContent': {
                    updateRenderSelect(message.renders, message.selectedRender);
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
                        // next audition / Play refetches fresh events.
                        playbackNotes = null;
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
                case 'partsData':
                    renderParts(message.parts);
                    // Its width is known once filled: keep it inside the window.
                    if (!practicePanel.hidden) {
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
                    startPlayback(message.notes, 0);
                    break;
                case 'setSoundFont':
                    setSoundFont(message.uri);
                    break;
                case 'setMidiOutput':
                    setMidiOutput(message.name);
                    break;
                case 'midiStarted':
                    midiStarted(message);
                    break;
                // Context-menu actions. Play reuses the Play button's path
                // exactly (fetch the events, then start at pendingStartPos), so
                // the right-clicked note resolves to an onset the same way a
                // highlighted note does.
                case 'ctxPlayFromHere':
                    if (ctxNotePos >= 0) {
                        pendingStartPos = ctxNotePos;
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
                        rawNotes = message.notes; rawParts = message.parts || null; rawBeats = message.beats || null;
                        playbackNotes = practiced(rawNotes);
                        const span = pendingSpan;
                        pendingSpan = null;
                        pendingStartPos = -1;
                        pendingFromHere = false;
                        lastSpan = span;
                        lastOnce = loopOn;
                        const list = window.LysSynth.playSpan(playbackNotes, span ? span[0] : null, span ? span[1] : null, loopOn);
                        loopList = loopOn ? list : null;
                        startPlayback(list, 0);
                    } else {
                        // The practice menu's tempo, parts and metronome, from the notes as written.
                        rawNotes = message.notes; rawParts = message.parts || null; rawBeats = message.beats || null;
                        playbackNotes = practiced(rawNotes);
                        pendingSpan = null;
                        pendingFromHere = false;
                        lastSpan = undefined;
                        loopList = null;
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
                        startPlayback(playbackNotes, startAt);
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

        document.addEventListener('wheel', (e) => {
            if (e.ctrlKey) {
                e.preventDefault();
                const delta = e.deltaY > 0 ? -scaleStep : scaleStep;
                scale = Math.min(maxScale, Math.max(minScale, scale + delta));
                fitMode = null; // manual zoom overrides a fit mode
                updateZoom();
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

        // Shift+drag across bars selects them (owner's request 2026-10-25). A plain drag still
        // scrolls the sheet (installDragScroll leaves a shifted press alone); the click a
        // Shift+drag ends with is swallowed, so it does not extend the selection once more.
        let shiftDrag = null, shiftDragged = false;
        svgContainer.addEventListener('pointerdown', (e) => {
            shiftDragged = false;
            shiftDrag = null;
            if (e.pointerType !== 'mouse' || e.button !== 0 || !e.shiftKey) return;
            const t = e.target;
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
            const target = e.target;
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
        let docHasScan = false;   // the file has an OMR side file (scanState, with each render)
        document.addEventListener('contextmenu', (e) => {
            const t = e.target;
            ctxNotePos = (t && t.hasAttribute && t.hasAttribute('data-pos'))
                ? parseInt(t.getAttribute('data-pos'), 10)
                : -1;
            // The bar under the pointer (owner's request 2026-10-24: Fix OMR Marks with AI and
            // Edit Bars Beside the Scan on the bar right-clicked): the staff box of a bar that
            // holds the point, where its text starts — else the note's own offset. The menu's
            // command gets it as its argument (lysAt).
            let ctxBarAt = -1;
            for (const r of document.querySelectorAll('rect.bar-staff')) {
                const b = r.getBoundingClientRect();
                if (e.clientX >= b.left && e.clientX <= b.right && e.clientY >= b.top && e.clientY <= b.bottom) {
                    const s = parseInt(r.getAttribute('data-start'), 10);
                    if (!isNaN(s)) { ctxBarAt = s; break; }
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
                lysNote: ctxNotePos >= 0,
                // The Stop button's enabled state is the playback flag that also
                // covers the optimistic window between Play and the first events.
                lysPlaying: !document.getElementById('stopBtn').disabled
            });
        }, true);

        // M3 keyboard: Ctrl/Cmd+I asks the AI, as the header button does — about the score
        // selection, or the whole score when nothing is selected; Escape clears the selection.
        window.addEventListener('keydown', (e) => {
            if ((e.ctrlKey || e.metaKey) && (e.key === 'i' || e.key === 'I')) {
                e.preventDefault(); aiSubmitSelection();
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

    if (!client) {
        return undefined;
    }
    return client.stop();
}


