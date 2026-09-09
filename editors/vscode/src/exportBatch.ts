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

// The Explorer's batch export (HANDOFF §2F F-export): right-click one or more
// .lys files → the "Lily#: Export" submenu names the format → a folder → every
// score of every selected file lands there, named as `lysc --all` names them
// (the `main` score takes the file's stem, every other appends its own name).
// The same commands run from the palette on the score being edited.
//
// The preview's Export button (extension.ts exportPreview) is the other door to
// `lilysharp/export`: one open document, one score, a save dialog. This door
// sends `all: true` and a folder, and names a file on disk by path when it is
// not open — an open one goes by document so its unsaved edits are exported, as
// the preview shows them.

import * as vscode from 'vscode';
import * as path from 'path';
import type { LanguageClient } from 'vscode-languageclient/node';
import {
    EXPORT_FORMATS, ExportFormat, ExportTally,
    duplicateStems, lysTargets, samePath, summarizeExport,
} from './exportBatchCore';

export interface ExportBatchDeps {
    getClient: () => LanguageClient | undefined;
    isReady: () => boolean;
    /** Resolves once the language client has started (never rejects). */
    whenReady: () => Promise<void>;
    log: (msg: string) => void;
    /** Brings the Lily# output channel forward (the "Show Log" button). */
    showLog: () => void;
    /** Opens a folder in the OS file manager (the "Open Folder" button). */
    openFolder: (dir: string) => void;
}

interface ExportResponse {
    Success: boolean;
    OutputPath: string | null;
    OutputPaths?: string[] | null;
    Warnings?: string[] | null;
    Error: string | null;
}

export function registerExportBatch(context: vscode.ExtensionContext, deps: ExportBatchDeps): void {
    for (const format of EXPORT_FORMATS) {
        context.subscriptions.push(
            vscode.commands.registerCommand(format.command,
                (uri?: vscode.Uri, uris?: vscode.Uri[]) => exportBatch(deps, format, uri, uris)));
    }
}

// The Explorer hands a context-menu command (the clicked item, [every selected
// item]); the palette hands nothing, and then the score being edited is the one.
function candidates(uri: vscode.Uri | undefined, uris: vscode.Uri[] | undefined): string[] {
    if (uris && uris.length > 0) {
        return uris.filter(u => u.scheme === 'file').map(u => u.fsPath);
    }
    if (uri) {
        return uri.scheme === 'file' ? [uri.fsPath] : [];
    }
    const doc = vscode.window.activeTextEditor?.document;
    if (doc && doc.languageId === 'lilysharp') {
        if (doc.uri.scheme !== 'file') {
            vscode.window.showInformationMessage(
                'Lily#: save the score first — the batch export names its files after the score\'s file.');
            return [];
        }
        return [doc.uri.fsPath];
    }
    return [];
}

async function exportBatch(
    deps: ExportBatchDeps, format: ExportFormat,
    uri: vscode.Uri | undefined, uris: vscode.Uri[] | undefined,
): Promise<void> {
    deps.log(`${format.command} triggered`);
    const targets = lysTargets(candidates(uri, uris));
    if (targets.length === 0) {
        vscode.window.showInformationMessage('Lily#: select one or more .lys files to export.');
        return;
    }
    // A right-click in the Explorer with no score open is what ACTIVATES the
    // extension: the command runs the moment activate() returns, while the language
    // client is still starting. Give it its start (bounded — a server that never
    // comes up must not hang the command).
    if (!deps.isReady()) {
        await Promise.race([deps.whenReady(), new Promise<void>(r => setTimeout(r, 20000))]);
    }
    const client = deps.getClient();
    if (!client || !deps.isReady()) {
        // Typical cause: the language server was swapped/killed and the client
        // gave up restarting — a window reload brings both back.
        vscode.window.showErrorMessage(
            'Lily#: language server not running — reload the window (Developer: Reload Window).');
        return;
    }

    // Two `song.lys` from different folders write `song.pdf` twice into one folder.
    // Say so before anything is written; the writer decides.
    const collisions = duplicateStems(targets);
    if (collisions.size > 0) {
        const detail = [...collisions.entries()]
            .map(([stem, files]) => `${stem}:\n  ${files.join('\n  ')}`)
            .join('\n');
        const choice = await vscode.window.showWarningMessage(
            `Lily#: ${collisions.size} name${collisions.size === 1 ? '' : 's'} would collide in one folder — `
            + 'a later file overwrites an earlier one\'s output.',
            { modal: true, detail }, 'Export anyway');
        if (choice !== 'Export anyway') {
            return;
        }
    }

    const folders = await vscode.window.showOpenDialog({
        canSelectFiles: false,
        canSelectFolders: true,
        canSelectMany: false,
        openLabel: `Export ${format.title} here`,
        title: `Lily#: export ${targets.length} score file${targets.length === 1 ? '' : 's'} as ${format.title}`,
        defaultUri: vscode.Uri.file(path.dirname(targets[0])),
    });
    if (!folders || folders.length === 0) {
        return;
    }
    const dir = folders[0].fsPath;
    deps.log(`exportBatch: ${targets.length} file(s) → ${format.id} → ${dir}`);

    // Every output written so far, keyed the way the file system keys it, with
    // the source that wrote it — a repeat is an overwrite worth reporting.
    const written = new Map<string, string>();
    let attempted = 0, failed = 0, outputs = 0, overwritten = 0, cancelled = false;
    await vscode.window.withProgress({
        location: vscode.ProgressLocation.Notification,
        title: `Lily#: exporting ${format.title}`,
        cancellable: true,
    }, async (progress, token) => {
        for (const file of targets) {
            if (token.isCancellationRequested) {
                cancelled = true;
                break;
            }
            progress.report({
                message: `${path.basename(file)} (${attempted + 1}/${targets.length})`,
                increment: 100 / targets.length,
            });
            // Open in an editor: export the buffer, unsaved edits included (the
            // preview's meaning of the file). Otherwise the server reads the disk.
            const open = vscode.workspace.textDocuments.find(d =>
                d.uri.scheme === 'file' && !d.isClosed && samePath(d.uri.fsPath, file));
            const params = {
                textDocument: open ? { uri: open.uri.toString() } : null,
                path: open ? null : file,
                format: format.id,
                all: true,
                outputDirectory: dir,
            };
            attempted++;
            deps.log(`  ${file}${open ? ' (open editor)' : ''}`);
            try {
                const response = await client.sendRequest<ExportResponse>('lilysharp/export', params);
                if (!response.Success) {
                    failed++;
                    deps.log(`    FAILED: ${response.Error}`);
                    continue;
                }
                for (const out of response.OutputPaths ?? []) {
                    outputs++;
                    const key = process.platform === 'win32' ? out.toLowerCase() : out;
                    const earlier = written.get(key);
                    if (earlier) {
                        overwritten++;
                        deps.log(`    ${out}  (OVERWROTE the one from ${earlier})`);
                    } else {
                        deps.log(`    ${out}`);
                    }
                    written.set(key, file);
                }
                for (const warning of response.Warnings ?? []) {
                    deps.log(`    warning: ${warning}`);
                }
            } catch (err) {
                failed++;
                deps.log(`    FAILED: ${err}`);
            }
        }
    });

    const tally: ExportTally = { total: targets.length, attempted, failed, outputs, overwritten, cancelled };
    const summary = summarizeExport(tally, format.title, dir);
    deps.log(summary);
    const openAction = 'Open Folder';
    const logAction = 'Show Log';
    const show = failed > 0 || overwritten > 0
        ? vscode.window.showWarningMessage
        : vscode.window.showInformationMessage;
    const choice = await show(summary, openAction, logAction);
    if (choice === openAction) {
        deps.openFolder(dir);
    } else if (choice === logAction) {
        deps.showLog();
    }
}
