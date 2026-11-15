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

// "Convert Recording to Lily# (.lys)…": runs Dettato on a recording and opens the chart it writes,
// with the file beside it (`x.dettato.json`) that the preview plays the recording by
// (docs/recording-design.md §2). The menu shows on Windows whether or not Dettato is installed; run
// without it, it offers to install it (owner's decision 2026-11-15). dettatoCore.ts decides.

import * as vscode from 'vscode';
import * as cp from 'child_process';
import * as fs from 'fs';
import * as path from 'path';
import {
    DETTATO_EXTENSION_ID, DettatoLocation, chartForConversion, dettatoArgs, dettatoFailure, dettatoTooOld, locateDettato,
} from './dettatoCore';

async function dettatoExports(): Promise<unknown> {
    const ext = vscode.extensions.getExtension(DETTATO_EXTENSION_ID);
    if (!ext) {
        return undefined;
    }
    try {
        return ext.isActive ? ext.exports : await ext.activate();
    } catch {
        return undefined;
    }
}

export async function findDettato(): Promise<DettatoLocation | undefined> {
    return locateDettato(process.env, await dettatoExports());
}

/** Installs — or, installed, updates — Dettato's extension; a gallery that does not list it
 *  (offline, or not published yet) gets the search instead. */
async function installDettato(): Promise<void> {
    try {
        await vscode.commands.executeCommand('workbench.extensions.installExtension', DETTATO_EXTENSION_ID);
    } catch {
        await vscode.commands.executeCommand('workbench.extensions.search', `@id:${DETTATO_EXTENSION_ID}`);
    }
}

/** Says the reader is missing and offers its extension. */
export async function offerDettato(): Promise<void> {
    const install = 'Install Dettato';
    const choice = await vscode.window.showInformationMessage(
        'Lily#: converting a recording to a chord chart needs the Dettato extension (Preview, Windows x64). Install it?',
        install);
    if (choice === install) {
        await installDettato();
    }
}

/** Keeps `lilysharp.dettatoAvailable` true to what is installed (the name is shared with
 *  Dettato's extension, which sets it on startup: Lab sessions/p1242/dettato reply). */
export function registerDettatoContext(context: vscode.ExtensionContext, output: vscode.OutputChannel): void {
    let said: string | undefined;
    const update = async () => {
        const where = await findDettato();
        const line = where ? `Dettato reader (${where.source}${where.version ? ' ' + where.version : ''}): ${where.path}`
            : 'Dettato reader: none (Convert Recording to Lily# (.lys)… offers the extension)';
        if (line !== said) {
            said = line;
            output.appendLine(line);
        }
        await vscode.commands.executeCommand('setContext', 'lilysharp.dettatoAvailable', where !== undefined);
    };
    void update();
    context.subscriptions.push(vscode.extensions.onDidChange(() => void update()));
}

/** Runs the reader; resolves to its exit code and what it said on stderr (undefined: cancelled). */
function run(exe: string, args: string[], output: vscode.OutputChannel,
    progress: vscode.Progress<{ message?: string }>, token: vscode.CancellationToken): Promise<{ code: number | null; stderr: string } | undefined> {
    return new Promise((resolve, reject) => {
        const child = cp.spawn(exe, args, { windowsHide: true });
        let cancelled = false, stderr = '';
        const stop = token.onCancellationRequested(() => { cancelled = true; child.kill(); });
        child.stdout.on('data', (b: Buffer) => output.append(b.toString()));
        child.stderr.on('data', (b: Buffer) => {
            const text = b.toString();
            stderr += text;
            output.append(text);
            // Each stage is a line ("the beats", "the chords"): the newest is the progress.
            const lines = text.split(/\r?\n/).map(l => l.trim()).filter(l => l !== '');
            if (lines.length > 0) {
                progress.report({ message: lines[lines.length - 1] });
            }
        });
        child.on('error', e => { stop.dispose(); reject(e); });
        child.on('close', code => { stop.dispose(); resolve(cancelled ? undefined : { code, stderr }); });
    });
}

/** Converts a recording (right-clicked, or chosen) into a chart beside it and opens the chart. */
export async function convertRecording(uri: vscode.Uri | undefined, output: vscode.OutputChannel,
    openPreview?: (chart: vscode.Uri) => Thenable<unknown>): Promise<void> {
    if (process.platform !== 'win32' || process.arch !== 'x64') {
        void vscode.window.showErrorMessage('Lily#: converting a recording needs Dettato, which runs on Windows x64 only.');
        return;
    }
    const reader = await findDettato();
    if (!reader) {
        await offerDettato();
        return;
    }
    const old = dettatoTooOld(reader);
    if (old) {
        const update = 'Update Dettato';
        if (await vscode.window.showWarningMessage(`Lily#: Dettato ${reader.version} ${old}.`, update) === update) {
            await installDettato();
        }
        return;
    }
    if (!uri) {
        const picked = await vscode.window.showOpenDialog({
            title: 'Convert Recording to Lily# (.lys)', openLabel: 'Convert', canSelectMany: false,
            filters: { Recordings: [...reader.inputs] },
        });
        if (!picked || picked.length === 0) {
            return;
        }
        uri = picked[0];
    }
    const kind = path.extname(uri.fsPath).slice(1).toLowerCase();
    if (uri.scheme !== 'file' || !reader.inputs.includes(kind)) {
        void vscode.window.showErrorMessage(`Lily#: Dettato reads a recording on this computer (${reader.inputs.join(', ')}).`);
        return;
    }
    const recording = uri.fsPath;
    const chart = chartForConversion(recording);
    // A chart beside the recording may hold the person's corrections: never written over unasked.
    let replace = false;
    if (fs.existsSync(chart)) {
        const answer = await vscode.window.showWarningMessage(
            `${path.basename(chart)} is already there. Read the recording again and replace it (and its bar marks)?`,
            { modal: true }, 'Replace');
        if (answer !== 'Replace') {
            return;
        }
        replace = true;
    }
    output.appendLine(`dettato ${dettatoArgs(recording, chart, replace).join(' ')}`);
    try {
        const done = await vscode.window.withProgress(
            { location: vscode.ProgressLocation.Notification, title: `Lily#: listening to ${path.basename(recording)}`, cancellable: true },
            (progress, token) => run(reader.path, dettatoArgs(recording, chart, replace), output, progress, token));
        if (done === undefined) {
            return;
        }
        if (done.code !== 0) {
            const choice = await vscode.window.showErrorMessage(dettatoFailure(done.code, done.stderr), 'Show Log');
            if (choice === 'Show Log') {
                output.show(true);
            }
            return;
        }
        const doc = await vscode.workspace.openTextDocument(chart);
        await vscode.window.showTextDocument(doc);
        if (openPreview) {
            await openPreview(doc.uri);
        }
    } catch (e) {
        output.appendLine(String(e));
        void vscode.window.showErrorMessage(`Lily#: Dettato could not be run: ${(e as Error).message ?? e}`);
    }
}
