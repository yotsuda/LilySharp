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

// Where the OMR reader is, as VS Code sees it (LilySharp-Omr docs/repro/
// lilysharp-handoff-2026-10-12.md §3 and its reply): the reader ships as its OWN extension
// (OMR_EXTENSION_ID, Windows x64, Beta). Lily# asks that extension where its omr.exe is, offers
// to install it when it is missing, and shows the scan commands again the moment it arrives —
// with no reload. omrCore.ts holds the order the places are tried in.

import * as vscode from 'vscode';
import * as path from 'path';
import { OMR_EXTENSION_ID, OmrLocation, locateOmr, omrRid, readerOfExports } from './omrCore';

/** The reader's path the installed extension exports, or undefined (not installed, not this
 *  platform, or its activation failed). Activating it starts no process (the reply §3). */
export async function readerFromExtension(): Promise<string | undefined> {
    const ext = vscode.extensions.getExtension(OMR_EXTENSION_ID);
    if (!ext) {
        return undefined;
    }
    try {
        return readerOfExports(ext.isActive ? ext.exports : await ext.activate());
    } catch {
        return undefined;
    }
}

/** Finds the reader: the environment variable, the extension, then the global storage. */
export async function findOmr(context: vscode.ExtensionContext): Promise<OmrLocation | undefined> {
    return locateOmr(process.env, path.join(context.globalStorageUri.fsPath, 'omr'),
        process.platform, process.arch, undefined, undefined, await readerFromExtension());
}

/** Whether this machine can run the reader at all: it is published for Windows x64 only. */
export function omrPlatformSupported(): boolean {
    return omrRid(process.platform, process.arch) === 'win-x64';
}

/** Says the reader is missing and offers its extension. */
export async function offerOmrExtension(): Promise<void> {
    const install = 'Install Scan Import';
    const choice = await vscode.window.showInformationMessage(
        'Lily#: reading a scan or a PDF needs the Lily# Scan Import extension (Beta, Windows). Install it?',
        install);
    if (choice !== install) {
        return;
    }
    try {
        await vscode.commands.executeCommand('workbench.extensions.installExtension', OMR_EXTENSION_ID);
    } catch {
        // Not reachable from here (offline, or a gallery that does not list it): show the search.
        await vscode.commands.executeCommand('workbench.extensions.search', `@id:${OMR_EXTENSION_ID}`);
    }
}

/** Keeps the `lilysharp.omrAvailable` context (the Explorer's right-click) true to what is
 *  installed, now and whenever an extension is installed or removed. The palette's item does
 *  not wait for it: a context set here exists only once Lily# has activated (a .lys opened),
 *  so the palette reads VS Code's own `isWindows` instead (owner's report 2026-10-12: no item
 *  before a .lys was opened). ⚠️ The NAME `lilysharp.omrAvailable` is shared: the reader's
 *  extension sets it to true on startup so the right-click shows before Lily# runs
 *  (LilySharp-Omr docs/repro/lilysharp-implemented-2026-10-12.md §2), and this overwrites it
 *  with Lily#'s own answer. Renaming it breaks that. */
export function registerOmrContexts(context: vscode.ExtensionContext, output: vscode.OutputChannel): void {
    let said: string | undefined;
    const update = async () => {
        const where = await findOmr(context);
        // One line whenever the answer changes: where "Import from Image/PDF…" will run.
        const line = where ? `OMR reader (${where.source}): ${where.path}`
            : `OMR reader: none${omrPlatformSupported() ? ' (Import from Image/PDF… offers the extension)' : ''}`;
        if (line !== said) {
            said = line;
            output.appendLine(line);
        }
        await vscode.commands.executeCommand('setContext', 'lilysharp.omrAvailable', where !== undefined);
    };
    void update();
    context.subscriptions.push(vscode.extensions.onDidChange(() => void update()));
}

/** Once per install: the reader is a Beta, so its marks are where to look first. */
export async function noteOmrBeta(context: vscode.ExtensionContext): Promise<void> {
    const key = 'omr.betaNoted';
    if (context.globalState.get<boolean>(key)) {
        return;
    }
    await context.globalState.update(key, true);
    void vscode.window.showInformationMessage(
        'Lily#: Scan Import is a Beta. It marks every place it is unsure of with @todo — '
        + 'check those against the scan (Show Original Scan) before trusting the score.');
}
