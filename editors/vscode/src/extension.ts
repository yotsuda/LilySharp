import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import {
    LanguageClient,
    LanguageClientOptions,
    ServerOptions,
    TransportKind
} from 'vscode-languageclient/node';

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

// Content for the "Lily#: New Score" command — a complete, valid, recognizable
// piece (public-domain Twinkle, Twinkle) so a new file shows real notation at once
// and demonstrates relative octaves (' / ,), the |: :| repeat, and structure replay.
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

// |: B :| repeats B; the reprise of A is re-labelled "A2".
structure { A |: B :| A "A2" }

score "score" {
  staff melody
}
`;
let debounceTimers = new Map<string, NodeJS.Timeout>();
const outputChannel = vscode.window.createOutputChannel('Lily# Extension');

// Track selected render per document
const selectedRenders = new Map<string, string>();

// Constants
const DEBOUNCE_DELAY_DEFAULT = 300;
const HIGHLIGHT_DISTANCE_THRESHOLD = 50;

export function activate(context: vscode.ExtensionContext) {
    outputChannel.appendLine('Lily# extension activating...');
    outputChannel.show(true);  // Show output channel for debugging

    const config = vscode.workspace.getConfiguration('lilysharp');
    let serverPath = config.get<string>('serverPath');

    outputChannel.appendLine(`Config serverPath: "${serverPath}"`);

    // Priority: 1. User-configured path, 2. Bundled server, 3. PATH
    if (!serverPath || serverPath.trim() === '') {
        // Look for bundled server in extension directory
        const bundledServer = path.join(context.extensionPath, 'server', 'lilysharp-lsp.exe');
        if (fs.existsSync(bundledServer)) {
            serverPath = bundledServer;
            outputChannel.appendLine(`Using bundled server: ${serverPath}`);
        } else {
            // Fallback to PATH
            serverPath = 'lilysharp-lsp';
            outputChannel.appendLine(`Using PATH: ${serverPath}`);
        }
    } else {
        outputChannel.appendLine(`Using configured path: ${serverPath}`);
    }

    if (path.isAbsolute(serverPath)) {
        if (fs.existsSync(serverPath)) {
            outputChannel.appendLine(`Server executable found: ${serverPath}`);
        } else {
            outputChannel.appendLine(`ERROR: Server executable not found: ${serverPath}`);
            vscode.window.showErrorMessage(`Lily# LSP server not found: ${serverPath}`);
            return;
        }
    }

    // Determine how to run the server
    let serverCommand: string;
    let serverArgs: string[];
    let serverEnv: { [key: string]: string } | undefined;

    if (serverPath.endsWith('.exe')) {
        // For .exe, use dotnet to run the corresponding .dll
        // This ensures the correct .NET runtime is used
        const dllPath = serverPath.replace(/\.exe$/, '.dll');
        if (fs.existsSync(dllPath)) {
            // Try to find user-installed dotnet first (has newer .NET versions)
            const userDotnetPath = path.join(process.env.LOCALAPPDATA || '', 'Microsoft', 'dotnet');
            const userDotnetExe = path.join(userDotnetPath, 'dotnet.exe');

            if (fs.existsSync(userDotnetExe)) {
                serverCommand = userDotnetExe;
                serverArgs = [dllPath];
                // Set DOTNET_ROOT to ensure correct runtime is found
                serverEnv = { ...process.env, DOTNET_ROOT: userDotnetPath } as { [key: string]: string };
                outputChannel.appendLine(`Running via user dotnet: ${userDotnetExe}`);
                outputChannel.appendLine(`DOTNET_ROOT: ${userDotnetPath}`);
            } else {
                // Fallback to system dotnet
                serverCommand = 'dotnet';
                serverArgs = [dllPath];
                outputChannel.appendLine(`Running via system dotnet: ${dllPath}`);
            }
        } else {
            // Fallback to direct execution
            serverCommand = serverPath;
            serverArgs = [];
            outputChannel.appendLine(`Running directly: ${serverPath}`);
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
        outputChannelName: 'Lily# Language Server'
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
    });

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
        vscode.commands.registerCommand('lilysharp.newScore', async () => {
            outputChannel.appendLine('newScore command triggered');
            const doc = await vscode.workspace.openTextDocument({
                language: 'lilysharp',
                content: NEW_SCORE_TEMPLATE,
            });
            await vscode.window.showTextDocument(doc);
        })
    );

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

    // Watch for cursor position changes
    context.subscriptions.push(
        vscode.window.onDidChangeTextEditorSelection(event => {
            if (event.textEditor.document.languageId === 'lilysharp') {
                const uri = event.textEditor.document.uri.toString();
                const panel = previewPanels.get(uri);
                if (panel) {
                    const doc = event.textEditor.document;
                    const offset = doc.offsetAt(event.selections[0].active);
                    const text = doc.getText();
                    // Highlight only when the cursor touches a token: if it sits in a
                    // pure-whitespace gap (line indent, between tokens) send -1 so the
                    // preview clears instead of snapping to the nearest preceding grob.
                    const isWs = (c: string | undefined) =>
                        c === undefined || c === ' ' || c === '\t' || c === '\n' || c === '\r';
                    const onToken = !isWs(text[offset]) || !isWs(text[offset - 1]);
                    panel.webview.postMessage({
                        type: 'highlightPosition',
                        position: onToken ? offset : -1,
                    });
                }
            }
        })
    );

    outputChannel.appendLine('Lily# extension activated');
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
        outputChannel.appendLine(`Preview panel disposed: ${uri}`);
        previewPanels.delete(uri);
        panelReady.delete(uri);
        selectedRenders.delete(uri);
        const timer = debounceTimers.get(uri);
        if (timer) {
            clearTimeout(timer);
            debounceTimers.delete(uri);
        }
    });

    // Handle messages from webview
    panel.webview.onDidReceiveMessage(
        async message => {
            outputChannel.appendLine(`Received message from webview: ${message.type}`);
            if (message.type === 'webviewReady') {
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
    context: vscode.ExtensionContext
) {
    const uri = document.uri.toString();
    const selectedRender = selectedRenders.get(uri);

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

        if (response.Error) {
            outputChannel.appendLine(`Sending error to webview: ${response.Error}`);
            panel.webview.postMessage({
                type: 'updateContent',
                error: response.Error,
                renders: response.Renders || [],
                selectedRender: selectedRender || ''
            });
        } else if (response.Svg) {
            outputChannel.appendLine(`Sending SVG to webview (length=${response.Svg.length})`);
            panel.webview.postMessage({
                type: 'updateContent',
                svg: response.Svg,
                renders: response.Renders || [],
                selectedRender: selectedRender || ''
            });
        } else {
            outputChannel.appendLine('Response has neither error nor SVG');
        }
    } catch (error) {
        outputChannel.appendLine(`Request failed: ${error}`);
        if (previewPanels.has(uri)) {
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

    // Default filename: the selected score's name, else the source file's basename.
    const docUri = vscode.Uri.parse(uri);
    const baseDir = vscode.Uri.joinPath(docUri, '..');
    const sourceName = (docUri.path.split('/').pop() || 'score').replace(/\.lys$/i, '');
    const baseName = renderName && renderName.length > 0 ? renderName : sourceName;

    // Default name WITHOUT an extension: the dialog's "Save as type"
    // dropdown appends the chosen one. Baking .pdf into the name meant the
    // typed name won over the selected type — picking PNG still saved a PDF.
    const target = await vscode.window.showSaveDialog({
        defaultUri: vscode.Uri.joinPath(baseDir, baseName),
        filters: {
            'PDF document': ['pdf'],
            'SVG image': ['svg'],
            'PNG image': ['png'],
            'LilyPond source': ['ly'],
            'MIDI (whole piece)': ['mid', 'midi'],
        }
    });
    if (!target) {
        return;
    }

    // The chosen file's extension is the format.
    const ext = (target.fsPath.split('.').pop() || '').toLowerCase();
    const format = ext === 'mid' || ext === 'midi' ? 'midi'
        : ext === 'xml' || ext === 'musicxml' ? 'musicxml'
        : ext; // png / pdf / svg
    if (!['png', 'pdf', 'svg', 'ly', 'midi', 'musicxml'].includes(format)) {
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
                vscode.env.openExternal(target);
            } else if (choice === revealAction) {
                vscode.commands.executeCommand('revealFileInOS', target);
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
            min-width: 150px;
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
            .loading {
                color: #aaa;
            }
        }
    </style>
</head>
<body>
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
        <button id="fitPageBtn" type="button" title="Fit whole page">⊡ Page</button>
        <button id="fitWidthBtn" type="button" title="Fit width">⬌ Width</button>
        <span class="sep"></span>
        <button id="firstPageBtn" type="button" title="First page">⏮</button>
        <button id="prevPageBtn" type="button" title="Previous page">◀</button>
        <span id="pageInfo">1 / 1</span>
        <button id="nextPageBtn" type="button" title="Next page">▶</button>
        <button id="lastPageBtn" type="button" title="Last page">⏭</button>
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
        const minScale = 0.25;
        const maxScale = 4;
        const scaleStep = 0.1;

        const svgContainer = document.getElementById('svgContainer');
        const zoomInfo = document.getElementById('zoomInfo');
        const renderSelect = document.getElementById('renderSelect');
        let hideTimeout;
        let lastHighlightPos = -1;

        // Pages of the current SVG (single-page SVGs have no g.page wrappers).
        let pages = [{ top: 0, height: 0 }];
        let svgWidthPx = 0;   // the SVG's natural width in px (width attribute)
        let pxPerSpace = 10;  // px per staff-space (width / viewBox width)
        let fitMode = null;   // 'page' | 'width' | null = manual zoom
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

        function fitPage() {
            fitMode = 'page';
            const a = availSize();
            const maxPageHpx = Math.max.apply(null, pages.map(p => p.height)) * pxPerSpace;
            if (svgWidthPx > 0 && maxPageHpx > 0) {
                scale = Math.min(a.w / svgWidthPx, a.h / maxPageHpx);
            }
            updateZoom();
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

        function highlightNearestElement(cursorPos) {
            // Clear previous highlights
            clearHighlights();

            // Find nearest data-pos value
            const elements = document.querySelectorAll('[data-pos]');
            let nearestPos = -1;
            let nearestDist = Infinity;
            elements.forEach(el => {
                const pos = parseInt(el.getAttribute('data-pos'), 10);
                if (pos <= cursorPos) {
                    const dist = cursorPos - pos;
                    if (dist < nearestDist) {
                        nearestDist = dist;
                        nearestPos = pos;
                    }
                }
            });

            // Highlight ALL elements with that data-pos
            if (nearestPos >= 0 && nearestDist < HIGHLIGHT_THRESHOLD) {
                highlightPositions([nearestPos]);
                lastResolvedPos = nearestPos;
            } else {
                lastResolvedPos = -1;
            }
        }

        // Groups all elements sharing one data-pos into printed instances
        // (phrase copies): elements of ONE instance sit within a chord's
        // footprint; other copies live in other measures/systems. Bands by
        // y-gap (systems are far apart), then splits by x-gap. Returned in
        // reading order = chronological within the part's staff.
        function clusterInstances(matches) {
            const items = matches.map(el => {
                let x = 0, y = 0;
                try { const b = el.getBBox(); x = b.x; y = b.y; } catch (e) { /* non-SVG */ }
                return { el: el, x: x, y: y };
            });
            items.sort((a, b) => a.y - b.y);
            const bands = [];
            for (const it of items) {
                const band = bands[bands.length - 1];
                if (band && it.y - band.maxY <= 12) {
                    band.items.push(it);
                    if (it.y > band.maxY) band.maxY = it.y;
                } else {
                    bands.push({ items: [it], maxY: it.y });
                }
            }
            const instances = [];
            for (const band of bands) {
                band.items.sort((a, b) => a.x - b.x);
                let inst = null;
                for (const it of band.items) {
                    if (inst && it.x - inst.maxX <= 6) {
                        inst.els.push(it.el);
                        if (it.x > inst.maxX) inst.maxX = it.x;
                    } else {
                        inst = { els: [it.el], maxX: it.x };
                        instances.push(inst);
                    }
                }
            }
            return instances.map(i => i.els);
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
                let matches = Array.from(document.querySelectorAll('[data-pos="' + pos + '"]'));
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
            // One entry per score. The UNNAMED score shows as "(Default)" (value "");
            // there is no "(Default)" entry when every score is named.
            renderSelect.innerHTML = '';
            const scoreRenders = (renders || []).filter(r => r.Type === 'score');
            scoreRenders.forEach(render => {
                const option = document.createElement('option');
                const unnamed = !render.Filename;
                option.value = unnamed ? '' : render.Name;
                option.textContent = unnamed ? '(Default)' : render.Filename;
                if (option.value === (selectedRender || '')) {
                    option.selected = true;
                }
                renderSelect.appendChild(option);
            });
        }

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
            const master = audioCtx.createGain();
            master.gain.value = 0.25;
            master.connect(audioCtx.destination);
            const t0 = audioCtx.currentTime + 0.15;
            let end = 0;
            for (const n of notes) {
                const osc = audioCtx.createOscillator();
                osc.type = 'triangle';
                osc.frequency.value = 440 * Math.pow(2, (n.P - 69) / 12);
                const g = audioCtx.createGain();
                const at = t0 + n.T - offset;
                const rel = at + n.D;
                const peak = 0.9 * (n.V / 127);
                g.gain.setValueAtTime(0, at);
                g.gain.linearRampToValueAtTime(peak, at + 0.012);
                g.gain.setValueAtTime(peak, Math.max(at + 0.012, rel - 0.04));
                g.gain.linearRampToValueAtTime(0, rel);
                osc.connect(g);
                g.connect(master);
                osc.start(at);
                osc.stop(rel + 0.05);
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
            // Group onsets by time so SIMULTANEOUS notes (rh + lh striking
            // together = different source positions) light up together.
            // Each entry keeps the server-computed printed-copy ordinal (O):
            // no client-side counting, so repeats and seeks cannot drift.
            const raw = notes.filter(n => n.S >= 0).map(n => ({ t: n.T - offset, s: n.S, o: n.O || 0 }));
            raw.sort((a, b) => a.t - b.t);
            onsetList = [];
            for (const o of raw) {
                const g = onsetList[onsetList.length - 1];
                if (g && Math.abs(o.t - g.t) < 0.005) {
                    if (!g.ss.some(e => e.s === o.s)) g.ss.push({ s: o.s, o: o.o });
                } else {
                    onsetList.push({ t: o.t, ss: [{ s: o.s, o: o.o }] });
                }
            }
            onsetIdx = 0;
            let lastGroup = -1;
            playheadTimer = setInterval(() => {
                if (!audioCtx) return;
                const elapsed = audioCtx.currentTime - playStartTime;
                let gi = -1;
                while (onsetIdx < onsetList.length && onsetList[onsetIdx].t <= elapsed) {
                    gi = onsetIdx;
                    onsetIdx++;
                }
                if (gi >= 0 && gi !== lastGroup) {
                    lastGroup = gi;
                    const ss = onsetList[gi].ss;
                    clearHighlights();
                    const painted = highlightPositions(
                        ss.map(e => ({ pos: e.s, occ: e.o })));
                    lastHighlightPos = ss[0].s;
                    lastResolvedPos = ss[0].s;
                    const el = painted.length > 0 ? painted[0] : null;
                    if (el) {
                        const r = el.getBoundingClientRect();
                        const m = mainContent.getBoundingClientRect();
                        const off = r.top < m.top + 30 || r.bottom > m.bottom - 30
                                 || r.left < m.left + 30 || r.right > m.right - 30;
                        if (off) el.scrollIntoView({ block: 'center', inline: 'center', behavior: 'smooth' });
                    }
                }
            }, 50);
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

        document.getElementById('fitPageBtn').addEventListener('click', fitPage);
        document.getElementById('fitWidthBtn').addEventListener('click', fitWidth);
        document.getElementById('firstPageBtn').addEventListener('click', () => gotoPage(0));
        document.getElementById('prevPageBtn').addEventListener('click', () => gotoPage(currentPageIndex() - 1));
        document.getElementById('nextPageBtn').addEventListener('click', () => gotoPage(currentPageIndex() + 1));
        document.getElementById('lastPageBtn').addEventListener('click', () => gotoPage(pages.length - 1));
        mainContent.addEventListener('scroll', updatePageInfo);
        window.addEventListener('resize', () => {
            if (fitMode === 'page') fitPage();
            else if (fitMode === 'width') fitWidth();
        });

        window.addEventListener('message', event => {
            const message = event.data;
            console.log('Webview received message:', message.type);
            switch (message.type) {
                case 'updateContent':
                    updateRenderSelect(message.renders, message.selectedRender);
                    if (message.loading) {
                        svgContainer.innerHTML = '<div class="loading">Waiting for language server...</div>';
                    } else if (message.error) {
                        svgContainer.innerHTML = '<div class="error">' + escapeHtml(message.error) + '</div>';
                    } else if (message.svg) {
                        svgContainer.innerHTML = message.svg;
                        collectPages();
                        // Keep the chosen fit across re-renders; otherwise
                        // re-apply the current manual zoom to the fresh SVG.
                        if (fitMode === 'page') fitPage();
                        else if (fitMode === 'width') fitWidth();
                        else updateZoom();
                        if (lastHighlightPos >= 0) {
                            highlightNearestElement(lastHighlightPos);
                        }
                    }
                    break;
                case 'playbackData':
                    if (message.error || !message.notes) {
                        setPlayUi(false);
                        if (message.error) {
                            zoomInfo.textContent = 'Play: ' + message.error;
                            zoomInfo.classList.add('visible');
                            setTimeout(() => zoomInfo.classList.remove('visible'), 2500);
                        }
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
                    highlightNearestElement(message.position);
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
                clearHighlights();
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


