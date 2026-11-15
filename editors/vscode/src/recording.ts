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

// The recording beside a chart, in VS Code (docs/recording-design.md): "Attach Recording…" and the
// file beside a chart following a rename. The decisions are recordingCore's; this is the glue.

import * as vscode from 'vscode';
import * as path from 'path';
import {
    WEBVIEW_AUDIO, WINDOWS_AUDIO, audioReader, chartForRecording, newRecording, parseRecording, recordingPathFor,
    serializeRecording, workForRename,
} from './recordingCore';

const isWindows = process.platform === 'win32';

async function exists(uri: vscode.Uri): Promise<boolean> {
    try {
        await vscode.workspace.fs.stat(uri);
        return true;
    } catch {
        return false;
    }
}

async function readText(uri: vscode.Uri): Promise<string | undefined> {
    try {
        return Buffer.from(await vscode.workspace.fs.readFile(uri)).toString('utf8');
    } catch {
        return undefined;
    }
}

function activeChart(): vscode.Uri | undefined {
    const doc = vscode.window.activeTextEditor?.document;
    return doc && doc.uri.scheme === 'file' && doc.fileName.toLowerCase().endsWith('.lys') ? doc.uri : undefined;
}

/**
 * Attaches a recording to a chart: writes the file beside the chart, `<stem>.dettato.json`
 * (format 2, no bars yet — they are set by hand in the preview). From the Explorer's right-click
 * on a recording, or from the Command Palette for the chart open in the editor. The recording
 * must be in the chart's folder (the file names it without a path): one elsewhere is copied
 * there when the person agrees. Returns the file written, or undefined.
 */
export async function attachRecording(uri?: vscode.Uri, onAttached?: (chart: vscode.Uri) => void): Promise<vscode.Uri | undefined> {
    let audio = uri && uri.scheme === 'file' && !uri.fsPath.toLowerCase().endsWith('.lys') ? uri : undefined;
    let chart = uri && uri.fsPath.toLowerCase().endsWith('.lys') ? uri : undefined;

    if (audio) {
        const how = audioReader(audio.fsPath, isWindows);
        if (typeof how === 'object') {
            void vscode.window.showErrorMessage(`Lily#: ${how.refused}.`);
            return undefined;
        }
        const folder = vscode.Uri.file(path.dirname(audio.fsPath));
        const entries = await vscode.workspace.fs.readDirectory(folder);
        const charts = entries.filter(([n, t]) => t === vscode.FileType.File && n.toLowerCase().endsWith('.lys')).map(([n]) => n);
        const active = activeChart();
        const found = chartForRecording(path.basename(audio.fsPath), charts,
            active && path.dirname(active.fsPath) === folder.fsPath ? path.basename(active.fsPath) : undefined);
        if (found) {
            chart = vscode.Uri.joinPath(folder, found);
        } else if (charts.length > 0) {
            const pick = await vscode.window.showQuickPick(charts, { title: 'Attach the recording to which chart?' });
            if (!pick) {
                return undefined;
            }
            chart = vscode.Uri.joinPath(folder, pick);
        } else {
            void vscode.window.showErrorMessage('Lily#: there is no chart (.lys) beside this recording — put the recording in the chart\'s folder.');
            return undefined;
        }
    } else {
        chart ??= activeChart();
        if (!chart) {
            void vscode.window.showErrorMessage('Lily#: open the chart (.lys) to attach a recording to.');
            return undefined;
        }
        const kinds = [...WEBVIEW_AUDIO, ...(isWindows ? WINDOWS_AUDIO : [])];
        const picked = await vscode.window.showOpenDialog({
            title: `Attach a recording to ${path.basename(chart.fsPath)}`,
            defaultUri: vscode.Uri.file(path.dirname(chart.fsPath)),
            canSelectMany: false,
            filters: { Recordings: kinds },
            openLabel: 'Attach',
        });
        if (!picked || picked.length === 0) {
            return undefined;
        }
        audio = picked[0];
        const how = audioReader(audio.fsPath, isWindows);
        if (typeof how === 'object') {
            void vscode.window.showErrorMessage(`Lily#: ${how.refused}.`);
            return undefined;
        }
    }

    // The file names the recording without a path: it must sit in the chart's folder.
    const chartFolder = path.dirname(chart.fsPath);
    if (path.dirname(audio.fsPath).toLowerCase() !== chartFolder.toLowerCase()) {
        const target = vscode.Uri.file(path.join(chartFolder, path.basename(audio.fsPath)));
        if (await exists(target)) {
            void vscode.window.showErrorMessage(`Lily#: ${path.basename(target.fsPath)} is already in the chart's folder — attach that one, or rename one of them.`);
            return undefined;
        }
        const copy = 'Copy';
        const answer = await vscode.window.showInformationMessage(
            `The recording has to be in the chart's folder. Copy ${path.basename(audio.fsPath)} there?`, { modal: true }, copy);
        if (answer !== copy) {
            return undefined;
        }
        await vscode.workspace.fs.copy(audio, target);
        audio = target;
    }

    const file = vscode.Uri.file(recordingPathFor(chart.fsPath));
    const name = path.basename(audio.fsPath);
    const before = await readText(file);
    if (before !== undefined) {
        const old = parseRecording(before);
        if (!('error' in old) && old.audio === name) {
            void vscode.window.showInformationMessage(`Lily#: ${name} is already attached to ${path.basename(chart.fsPath)}.`);
            return file;
        }
        const replace = 'Replace';
        const what = 'error' in old ? `${path.basename(file.fsPath)} (unreadable: ${old.error})` : `${old.audio}, with its ${old.bars.length} bar marks`;
        const answer = await vscode.window.showWarningMessage(
            `${path.basename(chart.fsPath)} has a recording already: ${what}. Replace it with ${name}?`, { modal: true }, replace);
        if (answer !== replace) {
            return undefined;
        }
    }
    await vscode.workspace.fs.writeFile(file, Buffer.from(serializeRecording(newRecording(name)), 'utf8'));
    onAttached?.(chart);
    void vscode.window.showInformationMessage(
        `Lily#: ${name} is attached to ${path.basename(chart.fsPath)} (in ${path.basename(file.fsPath)}). Its bars are not placed yet: mark them in the preview while it plays.`);
    return file;
}

/** Keeps the files beside charts with what they name when files are renamed in VS Code: a chart
 *  takes its file along; a recording renamed is renamed inside the files that name it. */
export function followRenames(): vscode.Disposable {
    return vscode.workspace.onDidRenameFiles(async e => {
        for (const { oldUri, newUri } of e.files) {
            if (oldUri.scheme !== 'file' || newUri.scheme !== 'file') {
                continue;
            }
            const folder = path.dirname(oldUri.fsPath);
            let inFolder: string[] = [];
            const texts = new Map<string, string | undefined>();
            try {
                inFolder = (await vscode.workspace.fs.readDirectory(vscode.Uri.file(folder)))
                    .filter(([n]) => n.toLowerCase().endsWith('.dettato.json')).map(([n]) => path.join(folder, n));
                for (const f of inFolder) {
                    texts.set(f, await readText(vscode.Uri.file(f)));
                }
            } catch {
                // (a folder gone: nothing to follow)
            }
            const known = new Set<string>(inFolder);
            for (const f of [recordingPathFor(oldUri.fsPath), recordingPathFor(newUri.fsPath)]) {
                if (!texts.has(f) && await exists(vscode.Uri.file(f))) {
                    known.add(f);
                }
            }
            const work = workForRename(oldUri.fsPath, newUri.fsPath, p => known.has(p), p => {
                const r = texts.get(p) !== undefined ? parseRecording(texts.get(p)!) : undefined;
                return r && !('error' in r) ? r.audio : undefined;
            }, inFolder);
            for (const w of work) {
                try {
                    if (w.kind === 'move') {
                        await vscode.workspace.fs.rename(vscode.Uri.file(w.from), vscode.Uri.file(w.to), { overwrite: false });
                    } else {
                        const r = parseRecording(texts.get(w.file) ?? '');
                        if (!('error' in r)) {
                            r.audio = w.audio;
                            await vscode.workspace.fs.writeFile(vscode.Uri.file(w.file), Buffer.from(serializeRecording(r), 'utf8'));
                        }
                    }
                } catch (err) {
                    void vscode.window.showWarningMessage(`Lily#: could not keep ${path.basename(w.kind === 'move' ? w.from : w.file)} with the rename: ${err}`);
                }
            }
        }
    });
}
