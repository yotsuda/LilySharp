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

// Check on save (LilySharp-Omr proposal B5): when `lilysharp.omr.verifyOnSave` is on and a
// saved .lys has an OMR reader's side file beside it, the reader engraves the .lys again and
// compares it with the scanned pages (`omr verify`, omrCore.verifyArguments), writing the side
// file's todos anew. Nothing else is needed here: the scan view reloads a side file written
// again, and the candidates' quick fixes read it on every request. The .lys is never touched.

import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import { execFile } from 'child_process';
import {
    OmrVersion, canVerify, exitMeaning, launchOf, locateOmr, parseVersion, runOmr, verifyArguments,
} from './omrCore';
import { sideFileOf } from './omrScanCore';

export interface OmrVerifyDeps {
    readonly output: vscode.OutputChannel;
    /** The lysc the reader engraves with, if the extension carries one. */
    readonly lysc: () => string | undefined;
}

/** Wires the save. Call once from activate. */
export function registerVerifyOnSave(context: vscode.ExtensionContext, deps: OmrVerifyDeps): void {
    const running = new Map<string, AbortController>();
    const versions = new Map<string, Promise<OmrVersion | undefined>>();
    let saidNoVerify = false;

    context.subscriptions.push(vscode.workspace.onDidSaveTextDocument(async doc => {
        if (doc.uri.scheme !== 'file' || !/\.lys$/i.test(doc.uri.fsPath)
            || !vscode.workspace.getConfiguration('lilysharp').get<boolean>('omr.verifyOnSave', false)
            || !fs.existsSync(sideFileOf(doc.uri.fsPath))) {
            return;
        }
        const where = locateOmr(process.env, path.join(context.globalStorageUri.fsPath, 'omr'),
            process.platform, process.arch);
        if (!where) {
            return;
        }
        const launch = launchOf(where.path);
        if (!versions.has(where.path)) {
            versions.set(where.path, new Promise(resolve =>
                execFile(launch.command, [...launch.prefix, '--version', '--json'], { timeout: 20000, windowsHide: true },
                    (err, stdout) => resolve(err ? undefined : parseVersion(stdout)))));
        }
        const v = await versions.get(where.path);
        if (!v || !canVerify(v)) {
            if (!saidNoVerify) {
                saidNoVerify = true;
                deps.output.appendLine(`OMR: the reader at ${where.path} has no 'verify'; checking on save is skipped.`);
            }
            return;
        }

        // A save while the last one is still being checked: that check is stale.
        const key = doc.uri.toString();
        running.get(key)?.abort();
        const abort = new AbortController();
        running.set(key, abort);
        const name = path.basename(doc.uri.fsPath);
        const status = vscode.window.setStatusBarMessage(`$(sync~spin) Lily#: checking ${name} against the scan…`);
        try {
            const result = await runOmr(launch, verifyArguments(doc.uri.fsPath, deps.lysc()), () => { },
                line => deps.output.appendLine(line), abort.signal);
            const meaning = exitMeaning(result.exitCode);
            if (meaning === 'ok' || meaning === 'partial') {
                const n = result.done?.todos;
                vscode.window.setStatusBarMessage(n === undefined ? `Lily#: ${name} checked against the scan`
                    : `Lily#: ${name} checked — ${n === 1 ? '1 place' : `${n} places`} to look at`, 5000);
            } else if (meaning !== 'stopped') {
                deps.output.appendLine(`OMR: verify ${name} failed (exit code ${result.exitCode}): `
                    + result.errors.concat(result.stderrTail).slice(-3).join(' / '));
            }
        } catch (err) {
            deps.output.appendLine(`OMR: verify could not start: ${err}`);
        } finally {
            status.dispose();
            if (running.get(key) === abort) {
                running.delete(key);
            }
        }
    }));
}
