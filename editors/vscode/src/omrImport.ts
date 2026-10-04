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

// "Lily#: Import from Image/PDF…" (LilySharp-Omr's agreement, L1 and L7): runs the OMR reader
// on a scan or a PDF and opens what it writes — the .lys, the preview, the annotated page.
// The reader is found by the environment variable for now; fetching and installing it
// (L2-L6) waits for its first release. omrCore.ts is the editor-free half.

import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import { execFile } from 'child_process';
import {
    OMR_INPUT_EXTENSIONS, OMR_PATH_VARIABLE, OMR_PROTOCOLS, exitMeaning, launchOf, locateOmr,
    parseVersion, progressText, readArguments, runOmr, speaksProtocol,
} from './omrCore';

export interface OmrImportDeps {
    readonly output: vscode.OutputChannel;
    /** Opens the score preview of the active editor in a column. */
    readonly openPreview: (column: vscode.ViewColumn) => void;
    /** The lysc the reader re-draws with when it verifies, if the extension carries one. */
    readonly lysc: () => string | undefined;
}

/** The command: from the palette (a file picker), or the Explorer (one file or a selection). */
export async function importFromImage(
    context: vscode.ExtensionContext, deps: OmrImportDeps, uri?: vscode.Uri, uris?: vscode.Uri[]): Promise<void> {
    let inputs = (uris && uris.length > 0 ? uris : uri ? [uri] : [])
        .filter(u => OMR_INPUT_EXTENSIONS.includes(path.extname(u.fsPath).slice(1).toLowerCase() as never));
    if (inputs.length === 0) {
        const picked = await vscode.window.showOpenDialog({
            canSelectMany: true,
            openLabel: 'Read',
            filters: { 'Scans and PDF': [...OMR_INPUT_EXTENSIONS] },
        });
        if (!picked || picked.length === 0) {
            return;
        }
        inputs = picked;
    }

    const where = locateOmr(process.env, path.join(context.globalStorageUri.fsPath, 'omr'),
        process.platform, process.arch);
    if (!where) {
        vscode.window.showInformationMessage(
            `Lily#: the OMR reader is not installed. Installing it from Lily# is not available yet; `
            + `to try a reader you have, set the environment variable ${OMR_PATH_VARIABLE} to it and restart VS Code.`);
        return;
    }
    const launch = launchOf(where.path);
    deps.output.appendLine(`OMR reader (${where.source}): ${where.path}`);

    // L6 in small: the reader answers its version, and this extension speaks its protocol.
    const version = await new Promise<string | undefined>(resolve =>
        execFile(launch.command, [...launch.prefix, '--version', '--json'], { timeout: 20000, windowsHide: true },
            (err, stdout) => resolve(err ? undefined : stdout)));
    const v = version === undefined ? undefined : parseVersion(version);
    if (!v) {
        vscode.window.showErrorMessage(`Lily#: the OMR reader at ${where.path} did not answer '--version --json'.`);
        return;
    }
    if (!speaksProtocol(v.protocol)) {
        vscode.window.showErrorMessage(
            `Lily#: the OMR reader ${v.version} speaks protocol ${v.protocol}; this Lily# speaks `
            + `${OMR_PROTOCOLS.min}–${OMR_PROTOCOLS.max}. Update ${v.protocol > OMR_PROTOCOLS.max ? 'Lily#' : 'the reader'}.`);
        return;
    }

    // Next to the first input, unless that folder cannot be written: then ask.
    let outDir = path.dirname(inputs[0].fsPath);
    try {
        await fs.promises.access(outDir, fs.constants.W_OK);
    } catch {
        const folder = await vscode.window.showOpenDialog({
            canSelectFiles: false, canSelectFolders: true, openLabel: 'Write the score here',
            title: `Lily#: ${outDir} cannot be written — where should the score go?`,
        });
        if (!folder || folder.length === 0) {
            return;
        }
        outDir = folder[0].fsPath;
    }

    const name = path.basename(inputs[0].fsPath) + (inputs.length > 1 ? ` (+${inputs.length - 1})` : '');
    const args = readArguments(inputs.map(u => u.fsPath), outDir, deps.lysc());
    deps.output.appendLine(`OMR ${v.version}: read ${inputs.map(u => u.fsPath).join(', ')} → ${outDir}`);

    const result = await vscode.window.withProgress({
        location: vscode.ProgressLocation.Notification,
        title: `Lily#: reading ${name}`,
        cancellable: true,
    }, async (progress, token) => {
        const abort = new AbortController();
        token.onCancellationRequested(() => abort.abort());
        let shown = 0;
        try {
            return await runOmr(launch, args, e => {
                const text = progressText(e);
                if (e.event === 'page') {
                    const at = Math.round(100 * (e.page - 1) / Math.max(1, e.of));
                    progress.report({ message: text, increment: at - shown });
                    shown = at;
                } else if (text) {
                    progress.report({ message: text });
                }
                if (e.event === 'warning') {
                    deps.output.appendLine(`OMR: ${e.message}`);
                }
            }, line => deps.output.appendLine(line), abort.signal);
        } catch (err) {
            vscode.window.showErrorMessage(`Lily#: the OMR reader could not start: ${err}`);
            return undefined;
        }
    });
    if (!result) {
        return;
    }

    const meaning = exitMeaning(result.exitCode);
    const tail = result.errors.concat(result.stderrTail).slice(-3).join(' / ');
    switch (meaning) {
        case 'stopped':
            vscode.window.showInformationMessage(`Lily#: reading ${name} was stopped.`);
            return;
        case 'input':
            vscode.window.showErrorMessage(`Lily#: ${name} cannot be read${tail ? `: ${tail}` : '.'}`);
            return;
        case 'internal':
            vscode.window.showErrorMessage(`Lily#: the OMR reader failed (exit code ${result.exitCode}).`, 'Show Output')
                .then(choice => { if (choice === 'Show Output') { deps.output.show(true); } });
            return;
    }
    if (!result.done || !fs.existsSync(result.done.lys)) {
        vscode.window.showErrorMessage(`Lily#: the OMR reader finished without writing a score.`, 'Show Output')
            .then(choice => { if (choice === 'Show Output') { deps.output.show(true); } });
        return;
    }

    // The score, its preview beside it, and the annotated first page beside that.
    const doc = await vscode.workspace.openTextDocument(vscode.Uri.file(result.done.lys));
    await vscode.window.showTextDocument(doc, vscode.ViewColumn.One);
    deps.openPreview(vscode.ViewColumn.Two);
    const annotated = result.done.annotated?.find(p => fs.existsSync(p));
    if (annotated) {
        await vscode.commands.executeCommand('vscode.open', vscode.Uri.file(annotated),
            { viewColumn: vscode.ViewColumn.Three, preserveFocus: true });
    }

    const todos = result.done.todos ?? 0;
    const marks = todos === 0 ? 'nothing marked to check'
        : todos === 1 ? '1 mark to check (@todo)' : `${todos} marks to check (@todo)`;
    if (meaning === 'partial') {
        vscode.window.showWarningMessage(`Lily#: read ${name} — some pages could not be read; ${marks}.`, 'Show Output')
            .then(choice => { if (choice === 'Show Output') { deps.output.show(true); } });
    } else {
        vscode.window.showInformationMessage(`Lily#: read ${name} — ${marks}.`);
    }
}
