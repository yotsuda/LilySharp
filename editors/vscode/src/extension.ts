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
import { registerAiTransform } from './aiTransform';
import { registerAiComplete } from './aiComplete';
import { registerSmartTyping } from './smartTyping';

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
const previewPanels = new Map<string, vscode.WebviewPanel>();
// A message posted before the webview has finished loading its HTML is
// DROPPED by VS Code. The webview script posts 'webviewReady' as its last
// statement; content updates await that (with a timeout escape hatch).
const panelReady = new Map<string, { promise: Promise<void>; resolve: () => void }>();
function armPanelReady(uri: string) {
    let resolve!: () => void;
    const promise = new Promise<void>(r => { resolve = r; });
    panelReady.set(uri, { promise, resolve });
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
    // Drop any pending debounce refresh: its callback captured the now-closed untitled
    // document, so letting it fire would render the wrong (stale) doc. A later edit to
    // the saved file schedules a fresh one under the new key.
    const timer = debounceTimers.get(oldUri);
    if (timer !== undefined) { clearTimeout(timer); debounceTimers.delete(oldUri); }
}

// Content for the "Lily#: New Score" command — a complete, valid, recognizable
// piece (public-domain Twinkle, Twinkle) so a new file shows real notation at once
// and demonstrates relative octaves (' / ,), the |: :| repeat, and form replay.
// (The same content is offered as a `score` snippet for files that already exist.)
const NEW_SCORE_TEMPLATE = `// Twinkle, Twinkle, Little Star (public domain).
// Relative octave (the default): each note sits in the octave nearest the
// previous one. Add ' to jump up an octave (the leap to G is g'), , to jump down (g,).
title "Twinkle, Twinkle, Little Star"
composer "Jane Taylor"

tempo 100
time 4/4
key c major

// A single part can hold its sections inline (part-major). For several parts,
// section-major often reads better (see the grand-staff template); the editor's
// "Lily#: Convert Layout" command switches between the two.
part melody {
  clef treble
  section A { c4 c g' g | a a g2 | f4 f e e | d d c2 | }
  section B { g'4 g f f | e e d2 | }
}

// Lyrics align one syllable per note. A trailing '-' hyphenates within a word
// (Twin- kle), '|' marks a bar. Plain words repeat every time a section is sung;
// to sing DIFFERENT words each pass, number the verses with [1. …] [2. …] (a
// leading ~ as in [~1. …] hides the printed stanza number) — here B's |: :|
// repeat is sung "Up above…" then "Like a diamond…".
// The block is NAMED ('verse'); the score attaches it under a staff with
// 'staff … with lyrics verse'. (Give a second lyrics block a different name.)
lyrics verse {
  section A { Twin- kle twin- kle | lit- tle star | How I won- der | what you are | }
  section B {
    [~1. Up a- bove the | world so high |]
    [~2. Like a dia- mond | in the sky |]
  }
}

// |: B :| repeats B; the reprise of A is re-labelled "A2".
form main { A |: B :| A "A2" }

score main {
  staff melody with lyrics verse
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

// Constants
const DEBOUNCE_DELAY_DEFAULT = 60;
const HIGHLIGHT_DISTANCE_THRESHOLD = 50;

export function activate(context: vscode.ExtensionContext) {
    outputChannel.appendLine('Lily# extension activating...');

    const config = vscode.workspace.getConfiguration('lilysharp');
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
        initializationOptions: {
            completion: {
                flatSpelling: config.get<string>('completion.flatSpelling', 'full')
            }
        }
    };

    client = new LanguageClient(
        'lilysharp',
        'Lily# Language Server',
        serverOptions,
        clientOptions
    );

    outputChannel.appendLine('Starting language client...');
    clientReadyPromise = client.start().then(() => {
        clientReady = true;
        outputChannel.appendLine('Language client started successfully');

        // Get and display server version
        client.sendRequest<string>('lilysharp/version').then(version => {
            outputChannel.appendLine(`Language server version: ${version}`);
        }).catch(err => {
            outputChannel.appendLine(`Failed to get server version: ${err}`);
        });

        // Update any open preview panels now that client is ready
        previewPanels.forEach((panel, uri) => {
            outputChannel.appendLine(`Updating preview for ${uri}`);
            const doc = vscode.workspace.textDocuments.find(d => d.uri.toString() === uri);
            if (doc) {
                updatePreviewContent(doc, panel, context);
            }
        });
    }).catch((error) => {
        outputChannel.appendLine(`Failed to start language client: ${error}`);
        vscode.window.showErrorMessage(
            'Lily#: the language server failed to start — live diagnostics and preview are unavailable.',
            'Show Log'
        ).then(pick => { if (pick === 'Show Log') { outputChannel.show(); } });
    });

    // Push completion.flatSpelling changes to the server so they apply LIVE. The
    // server seeds the value from initializationOptions at start; without this a
    // change would only take effect after a window reload. The notification shape
    // mirrors initializationOptions ({ completion: { flatSpelling } }).
    context.subscriptions.push(
        vscode.workspace.onDidChangeConfiguration(e => {
            if (!e.affectsConfiguration('lilysharp.completion.flatSpelling') || !client || !clientReady) {
                return;
            }
            const cfg = vscode.workspace.getConfiguration('lilysharp');
            client.sendNotification('workspace/didChangeConfiguration', {
                settings: {
                    completion: {
                        flatSpelling: cfg.get<string>('completion.flatSpelling', 'full')
                    }
                }
            });
        })
    );

    // Register preview commands
    context.subscriptions.push(
        vscode.commands.registerCommand('lilysharp.openPreview', () => {
            outputChannel.appendLine('openPreview command triggered');
            openPreview(context, vscode.ViewColumn.Active);
        }),
        vscode.commands.registerCommand('lilysharp.openPreviewToSide', () => {
            outputChannel.appendLine('openPreviewToSide command triggered');
            openPreview(context, vscode.ViewColumn.Beside);
        }),
        vscode.commands.registerCommand('lilysharp.convertLayout', () => {
            outputChannel.appendLine('convertLayout command triggered');
            convertLayout();
        }),
        vscode.commands.registerCommand('lilysharp.extractPhrase', () => {
            outputChannel.appendLine('extractPhrase command triggered');
            extractPhrase();
        }),
        vscode.commands.registerCommand('lilysharp.addChordTrack', () => {
            outputChannel.appendLine('addChordTrack command triggered');
            addChordTrack();
        }),
        vscode.commands.registerCommand('lilysharp.newScore', async () => {
            outputChannel.appendLine('newScore command triggered');
            const doc = await vscode.workspace.openTextDocument({
                language: 'lilysharp',
                content: NEW_SCORE_TEMPLATE,
            });
            await vscode.window.showTextDocument(doc);
        }),
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
    };
    registerAiTransform(context, aiDeps);
    // Second mode: validated ghost-text "next measure" completion (opt-in).
    registerAiComplete(context, aiDeps);

    // Smart typing: the brackets it started with (`<` before c4 -> `<c>4`, and a
    // chord's '>' promoted to '>>' when its '<' is doubled into an arpeggio) plus
    // slurs, octave marks, beams, ties and durations — hence smartTyping, not
    // smartBrackets.
    registerSmartTyping(context, (msg: string) => outputChannel.appendLine(msg));

    // Watch for document changes
    context.subscriptions.push(
        vscode.workspace.onDidChangeTextDocument(event => {
            if (event.document.languageId === 'lilysharp') {
                const uri = event.document.uri.toString();
                const panel = previewPanels.get(uri);
                if (panel) {
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
            panel.title = `Preview: ${path.basename(doc.uri.fsPath)}`;
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
                        panel.webview.postMessage({ type: 'highlightRange', ranges });
                        return;
                    }

                    const offset = doc.offsetAt(event.selections[0].active);
                    const text = doc.getText();
                    const isWs = (c: string | undefined) =>
                        c === undefined || c === ' ' || c === '\t' || c === '\n' || c === '\r';
                    // The caret's note is the one whose source lies INSIDE the
                    // whitespace-delimited token under the caret. Send that token's start so
                    // the preview highlights a note only when it belongs to THAT token —
                    // never the nearest PRECEDING note when the caret is on a barline,
                    // keyword, brace or a gap (their token holds no note, so the preview
                    // clears). A caret in pure whitespace corresponds to no note.
                    let tokenStart = -1;
                    if (!isWs(text[offset]) || !isWs(text[offset - 1])) {
                        tokenStart = offset;
                        while (tokenStart > 0 && !isWs(text[tokenStart - 1])) { tokenStart--; }
                    }
                    panel.webview.postMessage({
                        type: 'highlightPosition',
                        position: tokenStart >= 0 ? offset : -1,
                        tokenStart,
                    });
                }
            }
        })
    );

    outputChannel.appendLine('Lily# extension activated');
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

function openPreview(context: vscode.ExtensionContext, viewColumn: vscode.ViewColumn) {
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
        existingPanel.reveal(viewColumn);
        updatePreviewContent(document, existingPanel, context);
        return;
    }

    // Reuse an ORPHANED untitled preview (its buffer was saved to this file but the
    // automatic migration missed) rather than opening a second window: re-key it here.
    if (document.uri.scheme === 'file') {
        for (const key of previewPanels.keys()) {
            if (!key.startsWith('untitled:') || vscode.workspace.textDocuments.some(d => d.uri.toString() === key)) { continue; }
            migratePreviewKey(key, uri);
            const adopted = previewPanels.get(uri);
            if (adopted) {
                adopted.title = `Preview: ${path.basename(document.uri.fsPath)}`;
                adopted.reveal(viewColumn);
                updatePreviewContent(document, adopted, context);
                outputChannel.appendLine(`Reused orphaned preview ${key} -> ${uri}`);
                return;
            }
        }
    }

    // Create new preview panel with access to font resources
    const fileName = path.basename(document.uri.fsPath);
    const fontsUri = vscode.Uri.joinPath(context.extensionUri, 'media', 'fonts');

    outputChannel.appendLine('Creating new preview panel');
    const panel = vscode.window.createWebviewPanel(
        'lilysharpPreview',
        `Preview: ${fileName}`,
        viewColumn,
        {
            enableScripts: true,
            retainContextWhenHidden: true,
            localResourceRoots: [fontsUri]
        }
    );

    previewPanels.set(uri, panel);
    armPanelReady(uri);

    panel.onDidDispose(() => {
        // Find this panel's CURRENT key by value: it may have been re-keyed when an
        // untitled score was saved to a file (see onDidCloseTextDocument), so the URI
        // captured here at creation can be stale — deleting by it would leak the entry.
        for (const [key, value] of previewPanels) {
            if (value !== panel) { continue; }
            outputChannel.appendLine(`Preview panel disposed: ${key}`);
            previewPanels.delete(key);
            panelReady.delete(key);
            selectedRenders.delete(key);
            lastPostedSvg.delete(key);
            const timer = debounceTimers.get(key);
            if (timer) {
                clearTimeout(timer);
                debounceTimers.delete(key);
            }
        }
    });

    // Handle messages from webview
    panel.webview.onDidReceiveMessage(
        async message => {
            outputChannel.appendLine(`Received message from webview: ${message.type}`);
            if (message.type === 'webviewReady') {
                // A (re)loaded webview is blank — drop the dedup memory so the next render
                // always re-posts, even if the SVG matches what a previous instance showed.
                lastPostedSvg.delete(uri);
                panelReady.get(uri)?.resolve();
                return;
            }
            if (message.type === 'webviewError') {
                outputChannel.appendLine(
                    `WEBVIEW ERROR: ${message.message} (line ${message.line})`);
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
                    const pb = await client.sendRequest<{ Notes?: { T: number, D: number, P: number, V: number }[], Error?: string }>(
                        'lilysharp/playback', { textDocument: { uri } });
                    panel.webview.postMessage({ type: 'playbackData', notes: pb.Notes ?? null, error: pb.Error ?? null });
                } catch (e) {
                    panel.webview.postMessage({ type: 'playbackData', error: String(e) });
                }
                return;
            }
            if (message.type === 'export') {
                await exportPreview(uri, message.renderName);
            } else if (message.type === 'jumpToPosition') {
                const targetEditor = vscode.window.visibleTextEditors.find(
                    e => e.document.uri.toString() === uri
                );
                if (targetEditor) {
                    // A grob's data-pos is its node's full-span start, which sits on
                    // the leading indentation of its line, so a raw jump lands at
                    // column 1. Nudge forward over horizontal whitespace to land on
                    // the symbol itself (stop at the newline so we never cross lines).
                    const doc = targetEditor.document;
                    const text = doc.getText();
                    let offset = message.position;
                    while (offset < text.length && (text[offset] === ' ' || text[offset] === '\t')) {
                        offset++;
                    }
                    // A text grob (title / composer / ...) carries its STRING token's
                    // start, which is the opening quote. Step over it so the caret
                    // lands on the text that was clicked, not on the delimiter.
                    if (text[offset] === '"') {
                        offset++;
                    }
                    const position = doc.positionAt(offset);
                    targetEditor.selection = new vscode.Selection(position, position);
                    targetEditor.revealRange(
                        new vscode.Range(position, position),
                        vscode.TextEditorRevealType.InCenter
                    );
                    vscode.window.showTextDocument(targetEditor.document, targetEditor.viewColumn);
                }
            } else if (message.type === 'selectRender') {
                selectedRenders.set(uri, message.renderName);
                const doc = vscode.workspace.textDocuments.find(d => d.uri.toString() === uri);
                if (doc) {
                    updatePreviewContent(doc, panel, context);
                }
            } else if (message.type === 'aiTransformFromScore') {
                // M3: a note range selected ON THE SCORE maps to a text range, which
                // becomes the editor selection, then the same AI transform runs — so
                // the loop is identical whether the selection started in text or score.
                await aiTransformFromScore(uri, message.startPos, message.endPos);
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
    panel.webview.html = getPreviewHtml(fontUri.toString(), braceFontUri.toString(), panel.webview.cspSource, getNonce());

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

    outputChannel.appendLine('Sending lilysharp/svg request...');
    try {
        const response = await client.sendRequest<SvgResponse>('lilysharp/svg', {
            textDocument: { uri: uri },
            renderName: selectedRender || null
        });

        outputChannel.appendLine(`Got response: error=${response.Error}, hasSvg=${!!response.Svg}`);

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
        if (response.Svg) {
            // The response may carry an error TOO: a file with parse errors still
            // renders best-effort (the bad parts are dropped), the score shows
            // un-dimmed, and the banner carries the message. The dedup key folds
            // the banner text in so an error-text change still posts.
            // Skip the post when both are identical to what the webview already
            // shows — this is what spares a rapid edit/toggle/save burst from
            // re-shipping the same large SVG. A different render selection
            // compiles to a different SVG, so equality already implies the render.
            const key = response.Svg + '\n\n' + (response.Error ?? '');
            if (lastPostedSvg.get(uri) === key) {
                outputChannel.appendLine(`SVG unchanged (length=${response.Svg.length}), skipping post`);
            } else {
                outputChannel.appendLine(`Sending SVG to webview (length=${response.Svg.length}`
                    + `${response.Error ? ', with error banner' : ''})`);
                lastPostedSvg.set(uri, key);
                panel.webview.postMessage({
                    type: 'updateContent',
                    svg: response.Svg,
                    error: response.Error ?? undefined,
                    renders: response.Renders || [],
                    selectedRender: selectedRender || ''
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
                selectedRender: selectedRender || ''
            });
        } else {
            outputChannel.appendLine('Response has neither error nor SVG');
        }
    } catch (error) {
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
function spawnOpener(cmd: string, args: string[]) {
    try {
        const child = cp.spawn(cmd, args, { detached: true, stdio: 'ignore' });
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
        spawnOpener('explorer.exe', exists ? [`/select,${file}`] : [dir]);
    } else if (process.platform === 'darwin') {
        spawnOpener('open', exists ? ['-R', file] : [dir]);
    } else {
        // No portable "select this file" on Linux; the folder is the best we do.
        spawnOpener('xdg-open', [dir]);
    }
}

// Export the currently-previewed score: open the save dialog straight away and let
// its "Save as type" dropdown choose the format; the saved file's extension decides
// what the language server generates. SVG/PNG/PDF honour the selected score; MIDI
// and MusicXML export the whole piece.
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

    // Default filename (matches `lysc svg --all`): the `main` score writes the
    // source file's basename; every other score appends its name to it —
    // song.lys + `score sub` → song-sub, + `score sub "custom"` → song-custom.
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
            'MIDI (whole piece)': ['mid', 'midi'],
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

    try {
        const response = await client.sendRequest<ExportResponse>('lilysharp/export', {
            textDocument: { uri },
            format,
            outputPath: target.fsPath,
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

interface ConvertLayoutResponse {
    Success: boolean;
    NewText: string | null;
    FromLayout: string | null;
    ToLayout: string | null;
    Error: string | null;
}

/**
 * Converts the active .lys document between the section-major and part-major
 * layouts (toggles whichever the file currently uses) and applies the result as
 * a full-document edit.
 */
async function convertLayout() {
    const editor = vscode.window.activeTextEditor;
    if (!editor || editor.document.languageId !== 'lilysharp') {
        vscode.window.showErrorMessage('Lily#: open a .lys file to convert its layout.');
        return;
    }
    if (!client) {
        vscode.window.showErrorMessage('Lily#: language server not ready.');
        return;
    }

    const doc = editor.document;
    try {
        const response = await client.sendRequest<ConvertLayoutResponse>('lilysharp/convertLayout', {
            textDocument: { uri: doc.uri.toString() }
        });
        if (response.Success && response.NewText != null) {
            const fullRange = new vscode.Range(
                doc.positionAt(0), doc.positionAt(doc.getText().length));
            const edit = new vscode.WorkspaceEdit();
            edit.replace(doc.uri, fullRange, response.NewText);
            await vscode.workspace.applyEdit(edit);
            vscode.window.showInformationMessage(
                `Lily#: converted layout ${response.FromLayout} → ${response.ToLayout}.`);
        } else {
            vscode.window.showErrorMessage(`Lily#: ${response.Error}`);
        }
    } catch (err) {
        vscode.window.showErrorMessage(`Lily#: layout conversion failed: ${err}`);
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
        prompt: 'Name for the extracted phrase (referenced as NAME, NAME\'(3), …)',
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
                `Lily#: extracted phrase '${name}' — reference it from other parts (${name}, ${name}'(3), …).`);
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

    // Relative-octave output is opt-in via a setting (default: explicit absolute).
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

/**
 * M3 bridge: a note range selected on the score preview (start/end are the
 * `data-pos` source offsets of the first and last selected notes) is turned into a
 * text selection in the editor, then the shared `lilysharp.aiTransform` command runs
 * against it — so a score-origin selection drives the exact same transform loop as a
 * text selection (§6).
 */
async function aiTransformFromScore(uri: string, startPos: number, endPos: number): Promise<void> {
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
    await vscode.commands.executeCommand('lilysharp.aiTransform');
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
    Svg: string | null;
    Error: string | null;
    Renders: RenderInfo[] | null;
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

function getPreviewHtml(fontUri: string, braceFontUri: string, cspSource: string, nonce: string): string {
    return `<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; font-src ${cspSource}; script-src 'nonce-${nonce}';">
    <style>
        @font-face {
            font-family: 'Emmentaler';
            src: url('${fontUri}') format('woff2');
        }
        @font-face {
            font-family: 'Emmentaler-Brace';
            src: url('${braceFontUri}') format('woff2');
        }
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
            /* Stay pinned to the top while the sheet scrolls underneath. */
            position: sticky;
            top: 0;
            z-index: 10;
        }
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
        #pageInfo {
            font-family: system-ui, sans-serif;
            font-size: 13px;
            color: #333;
            min-width: 44px;
            text-align: center;
        }
        @media (prefers-color-scheme: dark) {
            .toolbar .sep { background: #555; }
            #pageInfo { color: #ccc; }
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
        @media (prefers-color-scheme: dark) {
            body {
                background: #1e1e1e;
            }
            .toolbar {
                background: #2d2d2d;
                border-bottom-color: #444;
            }
            .toolbar label {
                color: #ccc;
            }
            .toolbar select {
                background: #3c3c3c;
                color: #ccc;
                border-color: #555;
            }
            #svgContainer svg {
                filter: invert(1) hue-rotate(180deg);
            }
            .highlight {
                filter: drop-shadow(0 0 4px #00ccff);
            }
            .error {
                background: #4a1515;
                color: #ff8a80;
            }
            .error-banner {
                background: #4a1515;
                color: #ff8a80;
                border-top-color: rgba(255, 138, 128, 0.4);
            }
            .loading {
                color: #aaa;
            }
        }
        /* M3: floating "transform with AI" action, shown while a score note range
           is selected. */
        .ai-fab {
            position: fixed;
            top: 52px;
            right: 18px;
            z-index: 20;
            padding: 7px 14px;
            font-family: system-ui, sans-serif;
            font-size: 13px;
            font-weight: 600;
            border: none;
            border-radius: 6px;
            background: #6f42c1;
            color: #fff;
            cursor: pointer;
            box-shadow: 0 2px 8px rgba(0, 0, 0, 0.25);
        }
        .ai-fab:hover { background: #7e4fd0; }
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
        <label for="renderSelect">Score:</label>
        <select id="renderSelect">
            <option value="">(Default)</option>
        </select>
        <button id="exportBtn" type="button">Export…</button>
        <span class="sep"></span>
        <button id="playBtn" type="button" title="Play">▶</button>
        <button id="stopBtn" type="button" title="Stop" disabled>⏹</button>
        <span class="sep"></span>
        <button id="firstPageBtn" type="button" title="First page">⏮</button>
        <button id="prevPageBtn" type="button" title="Previous page">◀</button>
        <span id="pageInfo">1 / 1</span>
        <button id="nextPageBtn" type="button" title="Next page">▶</button>
        <button id="lastPageBtn" type="button" title="Last page">⏭</button>
    </div>
    <div id="errorBanner" class="error-banner" role="alert"></div>
    <div class="main-content">
        <div class="container">
            <div id="svgContainer">
                <div class="loading">Loading preview...</div>
            </div>
        </div>
    </div>
    <div class="zoom-info" id="zoomInfo">100%</div>
    <button id="aiTransformBtn" class="ai-fab" type="button" style="display:none"
            title="Transform the selected notes with AI (Ctrl+I)">✨ Transform with AI</button>
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
            aiTransformBtn.style.display = 'none';
        }
        function aiSetSelection(a, b) {
            aiRangeLo = Math.min(a, b);
            aiRangeHi = Math.max(a, b);
            aiTransformBtn.style.display = 'block';
            // Light the selection (inclusive of the end note's own data-pos).
            highlightRange([[aiRangeLo, aiRangeHi + 1]]);
            lastHighlightRanges = [[aiRangeLo, aiRangeHi + 1]];
            lastHighlightPos = -1;
        }
        function aiSubmitSelection() {
            if (aiRangeLo < 0) return;
            vscode.postMessage({ type: 'aiTransformFromScore', startPos: aiRangeLo, endPos: aiRangeHi });
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
        const pageInfo = document.getElementById('pageInfo');

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
            pageInfo.textContent = (currentPageIndex() + 1) + ' / ' + n;
            const single = n < 2;
            for (const id of ['firstPageBtn', 'prevPageBtn', 'nextPageBtn', 'lastPageBtn']) {
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

        function getHighlightColor() {
            return window.matchMedia('(prefers-color-scheme: dark)').matches ? '#00ccff' : '#ff6600';
        }

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

        // ---- source-position index -------------------------------------------
        // Every source offset the rendered score carries, sorted. Built once per
        // SVG and reused by the caret handlers, which run on every cursor
        // movement: reading data-pos off each drawn element there cost the whole
        // document per keystroke, and with the preview open a long score visibly
        // lagged behind the caret. A barline can also carry a data-alt list (the
        // other written bars that collapse onto it), and those offsets count too.
        let posSorted = [];        // ascending, unique
        let posIndexBuilt = false;

        function invalidateSourcePositions() {
            posIndexBuilt = false;
            posSorted = [];
        }

        function sourcePositions() {
            if (posIndexBuilt) return posSorted;
            const seen = new Set();
            const svg = svgContainer.querySelector('svg');
            if (svg) {
                for (const el of svg.querySelectorAll('[data-pos]')) {
                    const primary = parseInt(el.getAttribute('data-pos'), 10);
                    if (Number.isFinite(primary)) seen.add(primary);
                    const alt = el.getAttribute('data-alt');
                    if (alt) {
                        for (const a of alt.split(' ')) {
                            const n = parseInt(a, 10);
                            if (Number.isFinite(n)) seen.add(n);
                        }
                    }
                }
            }
            posSorted = Array.from(seen).sort((a, b) => a - b);
            posIndexBuilt = true;
            return posSorted;
        }

        function clearHighlights() {
            document.querySelectorAll('.highlight').forEach(el => {
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
                // Skip two kinds of rect that share a note's data-pos but must
                // never be recolored:
                //  - the transparent 'nh-hit' notehead click target (a filled
                //    box would show), and
                //  - the white OCCLUDER behind a tab fret digit (it hides the
                //    string line). A genuine boxed label (section/rehearsal) is
                //    STROKED; the occluder is fill-only. Match it by its EXPLICIT
                //    fill: a barline is a strokeless rect too, but it is drawn in
                //    the black default (no fill attribute), so an unstroked rect
                //    WITHOUT a fill attribute is real ink (a barline) and must
                //    still highlight — only an unstroked rect that carries a fill
                //    (the white mask) is the occluder to skip. Leaving the mask in
                //    made hasBox true, which coloured the box and suppressed the
                //    notehead's own highlight.
                // Match the primary data-pos OR any data-alt member (a barline that
                // several written bars collapse onto lights from any of their offsets).
                let matches = Array.from(document.querySelectorAll(
                        '[data-pos="' + pos + '"], [data-alt~="' + pos + '"]'))
                    .filter(el => !el.classList.contains('nh-hit'))
                    .filter(el => !(el.tagName.toLowerCase() === 'rect'
                        && !el.getAttribute('stroke') && el.getAttribute('fill')));
                if (occ >= 0 && matches.length > 1) {
                    // Pick the occ-th printed INSTANCE — a chord's every head
                    // (plus dots/accidentals) shares one data-pos, so slicing
                    // by raw element index lit a single head and left the
                    // rest of the chord dark. Cluster by geometry instead.
                    const instances = clusterInstances(matches);
                    matches = instances[Math.min(occ, instances.length - 1)] || matches;
                }
                painted.push.apply(painted, matches);
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
                    if (tag === 'line') {
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
            // One entry per score. Display the LABEL (the basename, or the form name
            // such as main). The VALUE is the export basename / preview selector:
            // empty for the main form with no basename, which exports to the source
            // .lys filename.
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

        const exportBtn = document.getElementById('exportBtn');
        exportBtn.addEventListener('click', () => {
            vscode.postMessage({ type: 'export', renderName: renderSelect.value });
        });

        // ---- WebAudio playback (simple triangle synth; events from the LSP) ----
        let audioCtx = null;
        let playingOscs = [];
        let playEndTimer = null;
        let playheadTimer = null;
        let onsetList = [];
        let onsetIdx = 0;
        let playStartTime = 0;
        let playbackNotes = null; // last received event list — enables click-to-seek
        let pendingStartPos = -1;   // play-button start point (the highlighted note)
        let lastResolvedPos = -1;   // the note the current highlight actually resolved to

        function setPlayUi(playing) {
            document.getElementById('playBtn').disabled = playing;
            document.getElementById('stopBtn').disabled = !playing;
        }

        function stopPlayback() {
            clearTimeout(playEndTimer);
            playEndTimer = null;
            clearInterval(playheadTimer);
            playheadTimer = null;
            onsetList = [];
            onsetIdx = 0;
            clearHighlights();
            lastHighlightPos = -1;
            // No note is highlighted once playback ends (naturally or via Stop),
            // so forget the resolved start point too — otherwise it stays pinned
            // to the last sounded note and the next Play resumes from the end
            // instead of restarting from the top. Cursor sync re-sets it when the
            // caret lands on a note again.
            lastResolvedPos = -1;
            for (const o of playingOscs) {
                try { o.stop(); } catch (e) { /* already ended */ }
            }
            playingOscs = [];
            if (audioCtx) {
                try { audioCtx.close(); } catch (e) { /* noop */ }
                audioCtx = null;
            }
            setPlayUi(false);
        }

        function startPlayback(notes, offset) {
            offset = offset || 0;
            stopPlayback();
            if (!notes || notes.length === 0) { return; }
            // Seek = re-schedule from the offset; notes already past it are
            // dropped (mid-note tails too — re-striking half a note reads
            // worse than starting cleanly at the next onset).
            notes = notes.filter(n => n.T >= offset - 0.001);
            if (notes.length === 0) { return; }
            audioCtx = new AudioContext();
            if (audioCtx.state === 'suspended') { audioCtx.resume(); }
            // Timbre families (note.I): waveform + envelope approximations —
            // the webview has no access to OS synths or soundfonts (CSP), so
            // instruments are voiced, not sampled. pluck = exponential decay.
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
            const master = audioCtx.createGain();
            master.gain.value = 0.25;
            master.connect(audioCtx.destination);
            const t0 = audioCtx.currentTime + 0.15;
            let end = 0;
            // Shared white-noise buffer for the drum patch (timbre 9).
            let noiseBuf = null;
            const getNoise = () => {
                if (!noiseBuf) {
                    noiseBuf = audioCtx.createBuffer(1, audioCtx.sampleRate, audioCtx.sampleRate);
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
                    const src = audioCtx.createBufferSource();
                    src.buffer = getNoise();
                    const filt = audioCtx.createBiquadFilter();
                    let tau = 0.06;
                    if (n.P <= 36) { filt.type = 'lowpass'; filt.frequency.value = 180; tau = 0.10; }
                    else if (n.P === 38 || n.P === 39 || n.P === 40) { filt.type = 'bandpass'; filt.frequency.value = 1800; filt.Q.value = 0.8; tau = 0.09; }
                    else if (n.P >= 49 && n.P !== 51) { filt.type = 'highpass'; filt.frequency.value = 5000; tau = 0.45; } // crash etc ring
                    else if (n.P === 46) { filt.type = 'highpass'; filt.frequency.value = 6500; tau = 0.25; } // open hat
                    else if (n.P >= 41 && n.P <= 48 && n.P !== 42 && n.P !== 44) { filt.type = 'bandpass'; filt.frequency.value = 300 + (n.P - 41) * 90; filt.Q.value = 1.2; tau = 0.12; } // toms
                    else { filt.type = 'highpass'; filt.frequency.value = 7500; tau = 0.05; } // closed/pedal hat, ride tick
                    const dg = audioCtx.createGain();
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
                const osc = audioCtx.createOscillator();
                osc.type = p.w;
                osc.frequency.value = 440 * Math.pow(2, (n.P - 69) / 12);
                const g = audioCtx.createGain();
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
            setPlayUi(true);
            playEndTimer = setTimeout(stopPlayback, (end - offset + 0.5) * 1000);

            // Follow-along: at each onset highlight the notation being played
            // (the note's own data-pos - an exact match, so the existing
            // click-sync highlighter lights the right head), and keep it in
            // view without fighting manual scrolling more than needed.
            playStartTime = t0;
            // Highlight = the set of notes SOUNDING now (a quarter stays lit
            // while the other hand's eighths change under it), not just the
            // latest onset group. Entries carry the server-side printed-copy
            // ordinal (O), so repeats and seeks cannot drift.
            const sched = notes.filter(n => n.S >= 0)
                .map(n => ({ t: n.T - offset, d: n.D, s: n.S, o: n.O || 0 }));
            let activeKeys = '';
            playheadTimer = setInterval(() => {
                if (!audioCtx) return;
                const elapsed = audioCtx.currentTime - playStartTime;
                const sounding = [];
                const seen = new Set();
                for (const n of sched) {
                    if (n.t > elapsed || elapsed >= n.t + n.d) continue;
                    const key = n.s + ':' + n.o;
                    if (seen.has(key)) continue;
                    seen.add(key);
                    sounding.push({ pos: n.s, occ: n.o, t: n.t, key: key });
                }
                // Newest onset first: painted[0] is the scroll target.
                sounding.sort((a, b) => b.t - a.t);
                const keys = sounding.map(x => x.key).sort().join(',');
                if (keys === activeKeys) return;
                const hadNew = sounding.some(x => activeKeys.indexOf(x.key) < 0);
                activeKeys = keys;
                clearHighlights();
                const painted = highlightPositions(sounding);
                if (sounding.length > 0) {
                    lastHighlightPos = sounding[0].pos;
                    lastResolvedPos = sounding[0].pos;
                }
                const el = hadNew && painted.length > 0 ? painted[0] : null;
                if (el) {
                    const r = el.getBoundingClientRect();
                    const m = mainContent.getBoundingClientRect();
                    const off = r.top < m.top + 30 || r.bottom > m.bottom - 30
                             || r.left < m.left + 30 || r.right > m.right - 30;
                    if (off) el.scrollIntoView({ block: 'center', inline: 'center', behavior: 'smooth' });
                }
            }, 50);
        }

        // ---- Audition: sound the caret's note (held) or its whole measure ----
        // The caret's note rings while the key is held. VS Code has no key-up event,
        // so a held key arrives as an OS key-repeat of the play command; each fire
        // re-arms a watchdog, and the note stops once the repeats cease.
        let heldCtx = null;
        let heldOscs = [];
        let heldPitchKey = '';     // pitches currently ringing, so a repeat sustains
        let heldWatchdog = null;
        let pendingAudition = null; // audition awaiting a fresh note list
        const AUD_WAVE = ['triangle','sine','square','sawtooth','triangle','sine','sawtooth','square','sine','triangle'];

        function stopHeldNote() {
            if (heldWatchdog) { clearTimeout(heldWatchdog); heldWatchdog = null; }
            if (!heldCtx) return;
            const t = heldCtx.currentTime;
            for (const o of heldOscs) {
                try {
                    o.g.gain.cancelScheduledValues(t);
                    o.g.gain.setTargetAtTime(0.0001, t, 0.05);
                    o.osc.stop(t + 0.3);
                } catch (e) { /* already ended */ }
            }
            const ctx = heldCtx;
            heldCtx = null; heldOscs = []; heldPitchKey = '';
            setTimeout(() => { try { ctx.close(); } catch (e) { /* noop */ } }, 400);
        }

        function holdNotes(pitches, timbre, gapMs) {
            const key = pitches.slice().sort((a, b) => a - b).join(',');
            // Fresh press (or a different note) re-attacks; a fast repeat of the SAME
            // note leaves the tone ringing and only re-arms the watchdog.
            const fresh = !heldCtx || key !== heldPitchKey || gapMs > 700;
            if (fresh) {
                stopHeldNote();
                heldCtx = new AudioContext();
                if (heldCtx.state === 'suspended') { heldCtx.resume(); }
                const master = heldCtx.createGain();
                master.gain.value = 0.22;
                master.connect(heldCtx.destination);
                const t0 = heldCtx.currentTime;
                for (const p of pitches) {
                    const osc = heldCtx.createOscillator();
                    osc.type = AUD_WAVE[timbre] || 'triangle';
                    osc.frequency.value = 440 * Math.pow(2, (p - 69) / 12);
                    const g = heldCtx.createGain();
                    g.gain.setValueAtTime(0, t0);
                    g.gain.linearRampToValueAtTime(0.9, t0 + 0.02);
                    osc.connect(g); g.connect(master);
                    osc.start(t0);
                    heldOscs.push({ osc, g });
                }
                heldPitchKey = key;
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
            if (pitches.length) { holdNotes(pitches, best.I || 0, gapMs); }
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

        document.getElementById('playBtn').addEventListener('click', () => {
            setPlayUi(true); // immediate feedback while the request runs
            // A highlighted note (editor sync / preview click) is the start
            // point: play from its first onset instead of the top. Use the
            // RESOLVED note position - lastHighlightPos may be a raw editor
            // cursor offset that matches no playback event.
            pendingStartPos = lastResolvedPos;
            vscode.postMessage({ type: 'requestPlayback' });
        });
        document.getElementById('stopBtn').addEventListener('click', stopPlayback);

        document.getElementById('firstPageBtn').addEventListener('click', () => gotoPage(0));
        document.getElementById('prevPageBtn').addEventListener('click', () => gotoPage(currentPageIndex() - 1));
        document.getElementById('nextPageBtn').addEventListener('click', () => gotoPage(currentPageIndex() + 1));
        document.getElementById('lastPageBtn').addEventListener('click', () => gotoPage(pages.length - 1));
        mainContent.addEventListener('scroll', updatePageInfo);
        window.addEventListener('resize', () => {
            if (fitMode === 'width') fitWidth();
        });

        window.addEventListener('message', event => {
            const message = event.data;
            console.log('Webview received message:', message.type);
            switch (message.type) {
                case 'updateContent': {
                    updateRenderSelect(message.renders, message.selectedRender);
                    const hasPreview = !!svgContainer.querySelector('svg');
                    if (message.loading) {
                        // Don't replace an existing preview with a spinner — keep
                        // showing the last score while the server (re)starts.
                        if (!hasPreview) {
                            svgContainer.innerHTML = '<div class="loading">Waiting for language server...</div>';
                            invalidateSourcePositions();
                        }
                    } else if (message.svg) {
                        // A score arrived — it is CURRENT, so never dimmed. It may
                        // still carry an error banner: a file with parse errors
                        // renders best-effort (the bad parts are simply dropped).
                        if (message.error) {
                            showErrorBanner(message.error);
                        } else {
                            hideErrorBanner();
                        }
                        svgContainer.classList.remove('stale');
                        svgContainer.innerHTML = message.svg;
                        invalidateSourcePositions();
                        // The score changed: the cached note list is stale, so the
                        // next audition / Play refetches fresh events.
                        playbackNotes = null;
                        collectPages();
                        // Keep the fit across re-renders (this is also what fits
                        // the FIRST render of a freshly opened preview); otherwise
                        // re-apply the current manual zoom to the fresh SVG.
                        if (fitMode === 'width') fitWidth();
                        else updateZoom();
                        if (lastHighlightRanges) {
                            highlightRange(lastHighlightRanges);
                        } else if (lastHighlightPos >= 0) {
                            highlightNearestElement(lastHighlightPos, lastHighlightTokenStart);
                        }
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
                // Context-menu actions. Play reuses the Play button's path
                // exactly (fetch the events, then start at pendingStartPos), so
                // the right-clicked note resolves to an onset the same way a
                // highlighted note does.
                case 'ctxPlayFromHere':
                    if (ctxNotePos >= 0) {
                        pendingStartPos = ctxNotePos;
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
                    } else {
                        playbackNotes = message.notes;
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
                    lastHighlightPos = message.position;
                    lastHighlightTokenStart = message.tokenStart;
                    lastHighlightRanges = null;
                    highlightNearestElement(message.position, message.tokenStart);
                    break;
                case 'highlightRange':
                    lastHighlightRanges = message.ranges;
                    lastHighlightPos = -1;
                    highlightRange(message.ranges);
                    break;
            }
        });

        document.addEventListener('wheel', (e) => {
            if (e.ctrlKey) {
                e.preventDefault();
                const delta = e.deltaY > 0 ? -scaleStep : scaleStep;
                scale = Math.min(maxScale, Math.max(minScale, scale + delta));
                fitMode = null; // manual zoom overrides a fit mode
                updateZoom();
            }
        }, { passive: false });

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
                aiTransformBtn.style.display = 'none';
                // During playback a click on a played note SEEKS there instead
                // of jumping the editor (listening mode). Non-note grobs
                // (barlines, marks) fall through to the normal click.
                if (playheadTimer !== null && playbackNotes) {
                    // The clicked printed COPY (its instance index in document
                    // order) maps to the onset whose server-side ordinal (O)
                    // matches. Clear the active highlight FIRST: it raises
                    // its element, corrupting the document order.
                    clearHighlights();
                    const copies = Array.from(document.querySelectorAll('[data-pos="' + pos + '"]'));
                    const instances = clusterInstances(copies);
                    let occ = 0;
                    for (let k = 0; k < instances.length; k++) {
                        if (instances[k].indexOf(target) >= 0) { occ = k; break; }
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
                    if (best !== null) {
                        startPlayback(playbackNotes, best.T);
                        return;
                    }
                }
                vscode.postMessage({ type: 'jumpToPosition', position: pos });
            } else {
                // Clicked empty space / a non-clickable grob: just drop the
                // highlight. Don't move the editor cursor (no message sent), and
                // forget the position so a re-render doesn't bring it back.
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
        document.addEventListener('contextmenu', (e) => {
            const t = e.target;
            ctxNotePos = (t && t.hasAttribute && t.hasAttribute('data-pos'))
                ? parseInt(t.getAttribute('data-pos'), 10)
                : -1;
            document.body.dataset.vscodeContext = JSON.stringify({
                preventDefaultContextMenuItems: true,
                lysNote: ctxNotePos >= 0,
                // The Stop button's enabled state is the playback flag that also
                // covers the optimistic window between Play and the first events.
                lysPlaying: !document.getElementById('stopBtn').disabled
            });
        }, true);

        // M3 keyboard: Ctrl/Cmd+I submits the score selection; Escape clears it.
        window.addEventListener('keydown', (e) => {
            if ((e.ctrlKey || e.metaKey) && (e.key === 'i' || e.key === 'I')) {
                if (aiRangeLo >= 0) { e.preventDefault(); aiSubmitSelection(); }
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

export function deactivate(): Thenable<void> | undefined {
    // Clear all debounce timers
    debounceTimers.forEach(timer => clearTimeout(timer));
    debounceTimers.clear();

    if (!client) {
        return undefined;
    }
    return client.stop();
}


