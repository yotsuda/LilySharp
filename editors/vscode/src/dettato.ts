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

// "Convert Recording to Lily# (.lys)… (Preview)" — Dettato is a Preview, as Calamo's menu says
// "(Beta)" (owner's decision 2026-11-15): runs Dettato on a recording and opens the chart it writes,
// with the file beside it (`x.dettato.json`) that the preview plays the recording by
// (docs/recording-design.md §2). The menu shows on Windows whether or not Dettato is installed; run
// without it, it offers to install it (owner's decision 2026-11-15). dettatoCore.ts decides.

import * as vscode from 'vscode';
import * as cp from 'child_process';
import * as fs from 'fs';
import * as path from 'path';
import {
    DETTATO_EXTENSION_ID, DettatoLocation, DettatoVersion, chartForConversion, dettatoArgs, dettatoFailure, judgeDettato,
    locateDettato, parseDettatoVersion, replaceQuestion,
} from './dettatoCore';
import { parseRecording, recordingPathFor } from './recordingCore';

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

/** Says the reader is missing and offers its extension (as offerOmrExtension does for Calamo). */
export async function offerDettato(): Promise<void> {
    const install = 'Install Dettato';
    const choice = await vscode.window.showInformationMessage(
        'Lily#: converting a recording needs the Dettato extension (Preview, Windows). Install it?',
        install);
    if (choice === install) {
        await installDettato();
    }
}

/** Offers the reader's update (offerOmrUpdate's twin). True when the person took it. */
async function offerDettatoUpdate(text: string): Promise<boolean> {
    const update = 'Update Dettato';
    if (await vscode.window.showWarningMessage(`Lily#: ${text}`, update) !== update) {
        return false;
    }
    await installDettato();
    return true;
}

/** Once per install: the reader is a Preview, so its marks are where to look first (noteOmrBeta's twin). */
async function noteDettatoPreview(context: vscode.ExtensionContext): Promise<void> {
    const key = 'dettato.previewNoted';
    if (context.globalState.get<boolean>(key)) {
        return;
    }
    await context.globalState.update(key, true);
    void vscode.window.showInformationMessage(
        'Lily#: Dettato is a Preview. It marks every chord it is unsure of with @todo — '
        + 'check those against the recording (the preview plays it) before trusting the chart.');
}

/** Keeps `lilysharp.dettatoAvailable` true to what is installed (the name is shared with
 *  Dettato's extension, which sets it on startup: Lab sessions/p1242/dettato reply). */
export function registerDettatoContext(context: vscode.ExtensionContext, output: vscode.OutputChannel): void {
    let said: string | undefined;
    const update = async () => {
        const where = await findDettato();
        const line = where ? `Dettato reader (${where.source}${where.version ? ' ' + where.version : ''}): ${where.path}`
            : 'Dettato reader: none (Convert Recording to Lily# (.lys)… (Preview) offers the extension)';
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

/** Asks the reader its version (`--version --json`), as Calamo is asked. */
function askVersion(exe: string): Promise<DettatoVersion | undefined> {
    return new Promise(resolve => cp.execFile(exe, ['--version', '--json'], { timeout: 20000, windowsHide: true },
        (err, stdout) => resolve(err ? undefined : parseDettatoVersion(String(stdout)))));
}

/**
 * Converts a recording (right-clicked, or chosen) into a chart and opens it. The steps and the
 * words follow "Convert Image/PDF to Lily# (.lys)…" (omrImport.ts importFromImage): the reader
 * first (offered when missing, before any file is chosen), its version asked, the folder beside
 * the recording unless it cannot be written, the progress cancellable, the same failures.
 */
export async function convertRecording(context: vscode.ExtensionContext, uri: vscode.Uri | undefined,
    output: vscode.OutputChannel, openPreview?: (chart: vscode.Uri) => Thenable<unknown>): Promise<void> {
    const reader = await findDettato();
    if (!reader) {
        await offerDettato();
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
    output.appendLine(`Dettato reader (${reader.source}): ${reader.path}`);
    const v = await askVersion(reader.path);
    if (!v) {
        void vscode.window.showErrorMessage(`Lily#: the Dettato reader at ${reader.path} did not answer '--version --json'.`);
        return;
    }
    const judged = judgeDettato(v);
    if (judged.kind === 'updateLilySharp') {
        void vscode.window.showErrorMessage(`Lily#: ${judged.text}`);
        return;
    }
    if (judged.kind === 'offerUpdate' && (await offerDettatoUpdate(judged.text) || !judged.goesOn)) {
        return;
    }
    const inputs = v.inputs ?? [...reader.inputs];
    const kind = path.extname(uri.fsPath).slice(1).toLowerCase();
    if (uri.scheme !== 'file' || !inputs.includes(kind)) {
        void vscode.window.showErrorMessage(`Lily#: Dettato reads a recording on this computer (${inputs.join(', ')}).`);
        return;
    }
    const recording = uri.fsPath;
    // Beside the recording, unless that folder cannot be written: then ask (as Calamo's read does).
    let chart = chartForConversion(recording);
    try {
        await fs.promises.access(path.dirname(chart), fs.constants.W_OK);
    } catch {
        const folder = await vscode.window.showOpenDialog({
            canSelectFiles: false, canSelectFolders: true, openLabel: 'Write the chart here',
            title: `Lily#: ${path.dirname(chart)} cannot be written — where should the chart go?`,
        });
        if (!folder || folder.length === 0) {
            return;
        }
        chart = path.join(folder[0].fsPath, path.basename(chart));
    }
    // A chart beside the recording may hold the person's corrections: never written over unasked.
    let replace = false;
    if (fs.existsSync(chart)) {
        // The bars a person placed by hand are named: the reading writes over them (Dettato's note
        // of 2026-11-16; reading again around them is its D4, with format 2).
        let byHand = 0;
        try {
            const rec = parseRecording(fs.readFileSync(recordingPathFor(chart), 'utf8'));
            byHand = 'error' in rec ? 0 : rec.bars.filter(b => b.byHand).length;
        } catch {
            // (no file beside it)
        }
        const answer = await vscode.window.showWarningMessage(
            replaceQuestion(path.basename(chart), byHand), { modal: true }, 'Replace');
        if (answer !== 'Replace') {
            return;
        }
        replace = true;
    }
    const name = path.basename(recording);
    void noteDettatoPreview(context);
    output.appendLine(`Dettato ${v.version}: dettato ${dettatoArgs(recording, chart, replace).join(' ')}`);
    let done: { code: number | null; stderr: string } | undefined;
    try {
        done = await vscode.window.withProgress(
            { location: vscode.ProgressLocation.Notification, title: `Lily#: reading ${name}`, cancellable: true },
            (progress, token) => run(reader.path, dettatoArgs(recording, chart, replace), output, progress, token));
    } catch (e) {
        void vscode.window.showErrorMessage(`Lily#: the Dettato reader could not start: ${(e as Error).message ?? e}`);
        return;
    }
    if (done === undefined) {
        void vscode.window.showInformationMessage(`Lily#: reading ${name} was stopped.`);
        return;
    }
    if (done.code !== 0 || !fs.existsSync(chart)) {
        const failure = done.code !== 0 ? dettatoFailure(done.code, done.stderr, name)
            : { text: 'Lily#: the Dettato reader finished without writing a chart.', showOutput: true };
        if (failure.showOutput) {
            void vscode.window.showErrorMessage(failure.text, 'Show Output')
                .then(choice => { if (choice === 'Show Output') { output.show(true); } });
        } else {
            void vscode.window.showErrorMessage(failure.text);
        }
        return;
    }
    // A chart written in another folder (the recording's could not be written): the file beside it
    // names the recording without a path, so the recording goes there too, or the preview cannot
    // find it (Dettato's note of 2026-11-16; docs/recording-design.md §1 1: no path in the file).
    if (path.dirname(chart).toLowerCase() !== path.dirname(recording).toLowerCase()) {
        const copy = path.join(path.dirname(chart), path.basename(recording));
        try {
            if (!fs.existsSync(copy)) {
                await fs.promises.copyFile(recording, copy);
                output.appendLine(`copied ${recording} → ${copy} (the preview plays the recording beside the chart)`);
            }
        } catch (e) {
            void vscode.window.showWarningMessage(`Lily#: ${path.basename(recording)} could not be copied beside the chart, so the preview cannot play it: ${(e as Error).message ?? e}`);
        }
    }
    // The chart and its preview beside it (as Calamo's read opens the score).
    const doc = await vscode.workspace.openTextDocument(chart);
    await vscode.window.showTextDocument(doc, vscode.ViewColumn.One);
    if (openPreview) {
        await openPreview(doc.uri);
    }
}
