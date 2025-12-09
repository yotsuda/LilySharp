import * as path from 'path';
import * as vscode from 'vscode';
import * as fs from 'fs';
import {
    LanguageClient,
    LanguageClientOptions,
    ServerOptions,
    TransportKind
} from 'vscode-languageclient/node';

let client: LanguageClient;
let previewPanel: vscode.WebviewPanel | undefined;
let debounceTimer: NodeJS.Timeout | undefined;
const outputChannel = vscode.window.createOutputChannel('Lilysharp Extension');

export function activate(context: vscode.ExtensionContext) {
    outputChannel.appendLine('Lilysharp extension activating...');
    
    const config = vscode.workspace.getConfiguration('lilysharp');
    let serverPath = config.get<string>('serverPath');
    
    outputChannel.appendLine(`Config serverPath: "${serverPath}"`);
    
    // If no custom path, try to find in PATH or use bundled
    if (!serverPath || serverPath.trim() === '') {
        serverPath = 'lilysharp-lsp';
        outputChannel.appendLine(`Using default: ${serverPath}`);
    }
    
    // Check if file exists (for absolute paths)
    if (path.isAbsolute(serverPath)) {
        if (fs.existsSync(serverPath)) {
            outputChannel.appendLine(`Server executable found: ${serverPath}`);
        } else {
            outputChannel.appendLine(`ERROR: Server executable not found: ${serverPath}`);
            vscode.window.showErrorMessage(`Lilysharp LSP server not found: ${serverPath}`);
            return;
        }
    }

    const serverOptions: ServerOptions = {
        run: {
            command: serverPath,
            transport: TransportKind.stdio
        },
        debug: {
            command: serverPath,
            transport: TransportKind.stdio
        }
    };

    const clientOptions: LanguageClientOptions = {
        documentSelector: [{ scheme: 'file', language: 'lilysharp' }],
        synchronize: {
            fileEvents: vscode.workspace.createFileSystemWatcher('**/*.lys')
        },
        outputChannelName: 'Lilysharp Language Server'
    };

    client = new LanguageClient(
        'lilysharp',
        'Lilysharp Language Server',
        serverOptions,
        clientOptions
    );

    outputChannel.appendLine('Starting language client...');
    client.start().then(() => {
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
            if (event.document.languageId === 'lilysharp' && previewPanel) {
                const config = vscode.workspace.getConfiguration('lilysharp');
                const autoRefresh = config.get<boolean>('preview.autoRefresh', true);
                const delay = config.get<number>('preview.refreshDelay', 300);
                
                if (autoRefresh) {
                    // Debounce the refresh
                    if (debounceTimer) {
                        clearTimeout(debounceTimer);
                    }
                    debounceTimer = setTimeout(() => {
                        refreshPreview(event.document);
                    }, delay);
                }
            }
        })
    );

    // Watch for active editor changes
    context.subscriptions.push(
        vscode.window.onDidChangeActiveTextEditor(editor => {
            if (editor && editor.document.languageId === 'lilysharp' && previewPanel) {
                refreshPreview(editor.document);
            }
        })
    );
    
    outputChannel.appendLine('Lilysharp extension activated');
}

function openPreview(context: vscode.ExtensionContext, viewColumn: vscode.ViewColumn) {
    const editor = vscode.window.activeTextEditor;
    if (!editor || editor.document.languageId !== 'lilysharp') {
        vscode.window.showWarningMessage('No Lilysharp file is open');
        return;
    }

    if (previewPanel) {
        previewPanel.reveal(viewColumn);
        refreshPreview(editor.document);
        return;
    }

    previewPanel = vscode.window.createWebviewPanel(
        'lilysharpPreview',
        'Lilysharp Preview',
        viewColumn,
        {
            enableScripts: true,
            retainContextWhenHidden: true
        }
    );

    previewPanel.onDidDispose(() => {
        previewPanel = undefined;
    });

    // Handle messages from webview (click to jump)
    previewPanel.webview.onDidReceiveMessage(
        message => {
            if (message.type === 'jumpToPosition') {
                const targetEditor = vscode.window.visibleTextEditors.find(
                    e => e.document.languageId === 'lilysharp'
                );
                if (targetEditor) {
                    const position = targetEditor.document.positionAt(message.position);
                    targetEditor.selection = new vscode.Selection(position, position);
                    targetEditor.revealRange(
                        new vscode.Range(position, position),
                        vscode.TextEditorRevealType.InCenter
                    );
                    // Focus the editor
                    vscode.window.showTextDocument(targetEditor.document, targetEditor.viewColumn);
                }
            }
        },
        undefined,
        context.subscriptions
    );

    refreshPreview(editor.document);
}

async function refreshPreview(document: vscode.TextDocument) {
    if (!previewPanel || !client) {
        return;
    }

    try {
        const response = await client.sendRequest<SvgResponse>('lilysharp/svg', {
            textDocument: { uri: document.uri.toString() }
        });

        if (response.Error) {
            previewPanel.webview.html = getErrorHtml(response.Error);
        } else if (response.Svg) {
            previewPanel.webview.html = getPreviewHtml(response.Svg);
        }
    } catch (error) {
        previewPanel.webview.html = getErrorHtml(`Failed to generate preview: ${error}`);
    }
}

interface SvgResponse {
    Svg: string | null;
    Error: string | null;
}

function getPreviewHtml(svg: string): string {
    return `<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <style>
        body {
            margin: 0;
            padding: 20px;
            background: white;
            display: flex;
            justify-content: center;
            align-items: flex-start;
            min-height: 100vh;
        }
        .container {
            max-width: 100%;
            overflow: auto;
        }
        svg {
            display: block;
            transform-origin: top left;
            transition: transform 0.1s ease-out;
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
        @media (prefers-color-scheme: dark) {
            body {
                background: #1e1e1e;
            }
            svg {
                filter: invert(1) hue-rotate(180deg);
            }
        }
    </style>
</head>
<body>
    <div class="container">
        ${svg}
    </div>
    <div class="zoom-info" id="zoomInfo">100%</div>
    <script>
        const vscode = acquireVsCodeApi();
        let scale = 1;
        const minScale = 0.25;
        const maxScale = 4;
        const scaleStep = 0.1;
        const svg = document.querySelector('svg');
        const zoomInfo = document.getElementById('zoomInfo');
        let hideTimeout;

        function updateZoom() {
            if (svg) {
                svg.style.transform = 'scale(' + scale + ')';
            }
            zoomInfo.textContent = Math.round(scale * 100) + '%';
            zoomInfo.classList.add('visible');
            clearTimeout(hideTimeout);
            hideTimeout = setTimeout(() => {
                zoomInfo.classList.remove('visible');
            }, 1500);
        }

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

function getErrorHtml(error: string): string {
    return `<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8">
    <style>
        body {
            margin: 20px;
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
        }
        .error {
            color: #f44336;
            background: #ffebee;
            padding: 16px;
            border-radius: 4px;
            white-space: pre-wrap;
            font-family: monospace;
        }
        @media (prefers-color-scheme: dark) {
            body {
                background: #1e1e1e;
                color: #fff;
            }
            .error {
                background: #4a1515;
                color: #ff8a80;
            }
        }
    </style>
</head>
<body>
    <div class="error">${escapeHtml(error)}</div>
</body>
</html>`;
}

function escapeHtml(text: string): string {
    return text
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}

export function deactivate(): Thenable<void> | undefined {
    if (debounceTimer) {
        clearTimeout(debounceTimer);
    }
    if (!client) {
        return undefined;
    }
    return client.stop();
}
