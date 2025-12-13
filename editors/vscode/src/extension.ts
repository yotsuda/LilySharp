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
const previewPanels = new Map<string, vscode.WebviewPanel>();
let debounceTimers = new Map<string, NodeJS.Timeout>();
const outputChannel = vscode.window.createOutputChannel('Lily# Extension');

// Track selected render per document
const selectedRenders = new Map<string, string>();

// Constants
const DEBOUNCE_DELAY_DEFAULT = 300;
const HIGHLIGHT_DISTANCE_THRESHOLD = 50;

export function activate(context: vscode.ExtensionContext) {
    outputChannel.appendLine('Lily# extension activating...');
    
    const config = vscode.workspace.getConfiguration('lilysharp');
    let serverPath = config.get<string>('serverPath');
    
    outputChannel.appendLine(`Config serverPath: "${serverPath}"`);
    
    if (!serverPath || serverPath.trim() === '') {
        serverPath = 'lilysharp-lsp';
        outputChannel.appendLine(`Using default: ${serverPath}`);
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

    const serverOptions: ServerOptions = {
        run: { command: serverPath, transport: TransportKind.stdio },
        debug: { command: serverPath, transport: TransportKind.stdio }
    };

    const clientOptions: LanguageClientOptions = {
        documentSelector: [{ scheme: 'file', language: 'lilysharp' }],
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
    client.start().then(() => {
        clientReady = true;
        outputChannel.appendLine('Language client started successfully');
    }).catch((error) => {
        outputChannel.appendLine(`Failed to start language client: ${error}`);
    });

    // Register preview commands
    context.subscriptions.push(
        vscode.commands.registerCommand('lilysharp.openPreview', () => {
            openPreview(context, vscode.ViewColumn.Active);
        }),
        vscode.commands.registerCommand('lilysharp.openPreviewToSide', () => {
            openPreview(context, vscode.ViewColumn.Beside);
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
                    const position = event.textEditor.document.offsetAt(event.selections[0].active);
                    panel.webview.postMessage({ type: 'highlightPosition', position });
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

    // Check if preview already exists for this document
    const existingPanel = previewPanels.get(uri);
    if (existingPanel) {
        existingPanel.reveal(viewColumn);
        updatePreviewContent(document, existingPanel, context);
        return;
    }

    // Create new preview panel with access to font resources
    const fileName = path.basename(document.uri.fsPath);
    const fontsUri = vscode.Uri.joinPath(context.extensionUri, 'media', 'fonts');
    
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

    panel.onDidDispose(() => {
        previewPanels.delete(uri);
        selectedRenders.delete(uri);
        const timer = debounceTimers.get(uri);
        if (timer) {
            clearTimeout(timer);
            debounceTimers.delete(uri);
        }
    });

    // Handle messages from webview
    panel.webview.onDidReceiveMessage(
        message => {
            if (message.type === 'jumpToPosition') {
                const targetEditor = vscode.window.visibleTextEditors.find(
                    e => e.document.uri.toString() === uri
                );
                if (targetEditor) {
                    const position = targetEditor.document.positionAt(message.position);
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

    // Set initial HTML structure
    panel.webview.html = getPreviewHtml();
    
    // Then load content
    updatePreviewContent(document, panel, context);
}

async function updatePreviewContent(
    document: vscode.TextDocument, 
    panel: vscode.WebviewPanel,
    context: vscode.ExtensionContext
) {
    if (!client || !clientReady) {
        return;
    }

    const uri = document.uri.toString();
    const selectedRender = selectedRenders.get(uri);
    
    // Get font URI for preview
    const fontUri = panel.webview.asWebviewUri(
        vscode.Uri.joinPath(context.extensionUri, 'media', 'fonts', 'emmentaler-20.woff2')
    );

    try {
        const response = await client.sendRequest<SvgResponse>('lilysharp/svg', {
            textDocument: { uri: uri },
            renderName: selectedRender || null,
            fontPath: fontUri.toString()
        });

        // Panel may have been disposed during async request
        if (!previewPanels.has(uri)) return;

        if (response.Error) {
            panel.webview.postMessage({ 
                type: 'updateContent', 
                error: response.Error,
                renders: response.Renders || [],
                selectedRender: selectedRender || ''
            });
        } else if (response.Svg) {
            panel.webview.postMessage({ 
                type: 'updateContent', 
                svg: response.Svg,
                renders: response.Renders || [],
                selectedRender: selectedRender || ''
            });
        }
    } catch (error) {
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

function getPreviewHtml(): string {
    return `<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <style>
        body {
            margin: 0;
            padding: 0;
            background: white;
            display: flex;
            flex-direction: column;
            min-height: 100vh;
        }
        .toolbar {
            padding: 8px 12px;
            background: #f0f0f0;
            border-bottom: 1px solid #ddd;
            display: flex;
            align-items: center;
            gap: 8px;
            flex-shrink: 0;
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
        .main-content {
            flex: 1;
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
            fill: #ff6600 !important;
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
                fill: #00ccff !important;
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
        <label for="renderSelect">Render:</label>
        <select id="renderSelect">
            <option value="">(Default)</option>
        </select>
    </div>
    <div class="main-content">
        <div class="container">
            <div id="svgContainer">
                <div class="loading">Loading preview...</div>
            </div>
        </div>
    </div>
    <div class="zoom-info" id="zoomInfo">100%</div>
    <script>
        const vscode = acquireVsCodeApi();
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

        function updateZoom() {
            svgContainer.style.transform = 'scale(' + scale + ')';
            zoomInfo.textContent = Math.round(scale * 100) + '%';
            zoomInfo.classList.add('visible');
            clearTimeout(hideTimeout);
            hideTimeout = setTimeout(() => {
                zoomInfo.classList.remove('visible');
            }, 1500);
        }

        function highlightNearestElement(cursorPos) {
            document.querySelectorAll('.highlight').forEach(el => el.classList.remove('highlight'));
            const elements = document.querySelectorAll('[data-pos]');
            let nearest = null;
            let nearestDist = Infinity;
            elements.forEach(el => {
                const pos = parseInt(el.getAttribute('data-pos'), 10);
                if (pos <= cursorPos) {
                    const dist = cursorPos - pos;
                    if (dist < nearestDist) {
                        nearestDist = dist;
                        nearest = el;
                    }
                }
            });
            if (nearest && nearestDist < HIGHLIGHT_THRESHOLD) {
                nearest.classList.add('highlight');
            }
        }

        function escapeHtml(text) {
            return text
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;');
        }

        function updateRenderSelect(renders, selectedRender) {
            const currentValue = renderSelect.value;
            renderSelect.innerHTML = '<option value="">(Default)</option>';
            
            if (renders && renders.length > 0) {
                // Filter score renders only
                const scoreRenders = renders.filter(r => r.Type === 'score');
                scoreRenders.forEach(render => {
                    const option = document.createElement('option');
                    option.value = render.Name;
                    option.textContent = render.Filename || render.Name;
                    if (render.Name === selectedRender) {
                        option.selected = true;
                    }
                    renderSelect.appendChild(option);
                });
            }
        }

        renderSelect.addEventListener('change', () => {
            vscode.postMessage({ type: 'selectRender', renderName: renderSelect.value });
        });

        window.addEventListener('message', event => {
            const message = event.data;
            switch (message.type) {
                case 'updateContent':
                    updateRenderSelect(message.renders, message.selectedRender);
                    if (message.error) {
                        svgContainer.innerHTML = '<div class="error">' + escapeHtml(message.error) + '</div>';
                    } else if (message.svg) {
                        svgContainer.innerHTML = message.svg;
                        if (lastHighlightPos >= 0) {
                            highlightNearestElement(lastHighlightPos);
                        }
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
                updateZoom();
            }
        }, { passive: false });

        document.addEventListener('click', (e) => {
            const target = e.target;
            if (target && target.hasAttribute && target.hasAttribute('data-pos')) {
                const pos = parseInt(target.getAttribute('data-pos'), 10);
                vscode.postMessage({ type: 'jumpToPosition', position: pos });
            }
        });
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