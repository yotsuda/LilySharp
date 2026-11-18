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

// Import MusicXML — the half that talks to VS Code: the file picker (or the Explorer's file), the
// request to the server, the .lys written beside the source or opened untitled, the preview, and
// the import's report. A Standard MIDI File and ABC notation (docs/abc-import-design.md) go the same way. It stood in extension.ts until
// session 1298; what it reads of the extension's own state comes through MusicXmlImportDeps.

import * as vscode from 'vscode';
import type { LanguageClient } from 'vscode-languageclient/node';

export interface MusicXmlImportDeps {
    /** The server's client as soon as it is made, up or not. */
    getClient: () => LanguageClient | undefined;
    /** The extension's output channel. */
    outputChannel: vscode.OutputChannel;
    /** Opens the preview of the active editor's score. */
    openPreview: (context: vscode.ExtensionContext, viewColumn: vscode.ViewColumn) => void;
}

let outputChannel: vscode.OutputChannel;
let openPreview: MusicXmlImportDeps['openPreview'];
let startedClient: () => LanguageClient | undefined = () => undefined;

/** Called once from activate, before anything here can be asked for. */
export function useMusicXmlImportHost(deps: MusicXmlImportDeps): void {
    ({ outputChannel, openPreview } = deps);
    startedClient = deps.getClient;
}

interface ImportedPiece {
    Name: string;
    Title: string | null;
    Lys: string | null;
    Warnings: string[];
    Error: string | null;
}

interface ImportMusicXmlResponse {
    Lys: string | null;
    Warnings: string[];
    Error: string | null;
    /** An ABC tune book: a .lys per tune (docs/abc-import-design.md §3). */
    Pieces?: ImportedPiece[] | null;
}

/**
 * Imports a MusicXML file (.xml/.musicxml/.mxl) into Lily#. From the command
 * palette it opens a file picker and drops the result in a new untitled .lys; from
 * the Explorer context menu (a uri is passed) it writes `<name>.lys` next to the
 * source. Either way it opens the preview to the side and surfaces the import
 * report (approximations/drops) as a warning — import is a faithful STARTING POINT,
 * not a byte round-trip.
 */
export async function importMusicXml(context: vscode.ExtensionContext, sourceUri?: vscode.Uri) {
    const client = startedClient();
    if (!client) {
        vscode.window.showErrorMessage('Lily#: language server not ready.');
        return;
    }

    const fromExplorer = sourceUri !== undefined;
    let fileUri = sourceUri;
    if (!fileUri) {
        const picked = await vscode.window.showOpenDialog({
            canSelectMany: false,
            openLabel: 'Import',
            // A Standard MIDI File and ABC notation go through the same request: the server tells
            // them by what they hold.
            filters: {
                'MusicXML, MIDI or ABC': ['xml', 'musicxml', 'mxl', 'mid', 'midi', 'abc'],
                'MusicXML': ['xml', 'musicxml', 'mxl'], 'MIDI': ['mid', 'midi'], 'ABC': ['abc'],
            },
        });
        if (!picked || picked.length === 0) {
            return;
        }
        fileUri = picked[0];
    }

    // The source file's name (URI paths use '/' on every OS), for the result dialogs.
    const sourceName = fileUri.path.split('/').pop() ?? 'the file';

    // Relative-octave output is opt-in via a setting (default: explicit absolute) — not in the
    // Settings UI since 2026-10-25 (settings.json only; Convert Octaves does it afterwards).
    const relativeOctave = vscode.workspace
        .getConfiguration('lilysharp')
        .get<boolean>('import.relativeOctave', false);

    try {
        const response = await client.sendRequest<ImportMusicXmlResponse>('lilysharp/importMusicXml', {
            filePath: fileUri.fsPath,
            relativeOctave,
        });
        if (response.Error || response.Lys == null) {
            vscode.window.showErrorMessage(`Lily#: import failed: ${response.Error ?? 'no output.'}`);
            return;
        }
        if (response.Pieces && response.Pieces.length > 1) {
            await writeTuneBook(context, fileUri, sourceName, response.Pieces);
            return;
        }

        let doc: vscode.TextDocument;
        if (fromExplorer) {
            // Write <name>.lys next to the source and open it.
            const targetUri = fileUri.with({
                path: fileUri.path.replace(/\.(xml|musicxml|mxl|mid|midi|abc)$/i, '') + '.lys',
            });
            await vscode.workspace.fs.writeFile(targetUri, Buffer.from(response.Lys, 'utf8'));
            doc = await vscode.workspace.openTextDocument(targetUri);
        } else {
            // Command palette: a brand-new untitled .lys.
            doc = await vscode.workspace.openTextDocument({
                language: 'lilysharp',
                content: response.Lys,
            });
        }
        await vscode.window.showTextDocument(doc);
        openPreview(context, vscode.ViewColumn.Beside);

        const warnings = response.Warnings ?? [];
        if (warnings.length > 0) {
            const n = warnings.length;
            // A single warning (e.g. "no notes; the score is empty") reads best shown
            // verbatim; several are summarized with the report behind "Show Details".
            // The "starting point, edit as needed" framing is reserved for a clean
            // import — it's misleading when nothing (or little) came through.
            const message = n === 1
                ? `Lily#: ${sourceName}: ${warnings[0]}`
                : `Lily#: imported ${sourceName} with ${n} approximations — see the import report.`;
            vscode.window.showWarningMessage(message, 'Show Details').then(choice => {
                if (choice === 'Show Details') {
                    outputChannel.appendLine(`MusicXML import report (${sourceName}):`);
                    for (const w of warnings) {
                        outputChannel.appendLine(`  - ${w}`);
                    }
                    outputChannel.show(true);
                }
            });
        } else {
            vscode.window.showInformationMessage(
                `Lily#: imported ${sourceName} — a starting point, edit as needed.`);
        }
    } catch (err) {
        vscode.window.showErrorMessage(`Lily#: import failed: ${err}`);
    }
}

/**
 * An ABC file of several tunes: a .lys per tune — a Lily# file is one piece — in a folder beside
 * the source named after it (`reels.abc` → `reels/001-speed-the-plough.lys`), from the palette as
 * from the Explorer (owner's decision 2026-11-18). The first tune opens with its preview; what each
 * tune left out goes to the output channel.
 */
async function writeTuneBook(context: vscode.ExtensionContext, source: vscode.Uri, sourceName: string, pieces: ImportedPiece[]) {
    const folderName = sourceName.replace(/\.[^.]+$/, '');
    const folder = vscode.Uri.joinPath(source, '..', folderName);
    await vscode.workspace.fs.createDirectory(folder);
    let first: vscode.Uri | undefined;
    let written = 0;
    const notes: string[] = [];
    for (const piece of pieces) {
        if (piece.Lys == null) {
            notes.push(`  ${piece.Name}: not read — ${piece.Error ?? 'no output'}`);
            continue;
        }
        const target = vscode.Uri.joinPath(folder, `${piece.Name}.lys`);
        await vscode.workspace.fs.writeFile(target, Buffer.from(piece.Lys, 'utf8'));
        first ??= target;
        written++;
        for (const w of piece.Warnings ?? []) {
            notes.push(`  ${piece.Name}: ${w}`);
        }
    }
    if (first) {
        await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(first));
        openPreview(context, vscode.ViewColumn.Beside);
    }
    const message = `Lily#: ${sourceName} holds ${pieces.length} tunes: ${written} written to ${folderName}/` +
        (written < pieces.length ? ` (${pieces.length - written} could not be read)` : '') + ' — the first is open.';
    const show = (choice: string | undefined) => {
        if (choice === 'Show Details') {
            outputChannel.appendLine(`Import report (${sourceName}):`);
            for (const line of notes) {
                outputChannel.appendLine(line);
            }
            outputChannel.show(true);
        } else if (choice === 'Reveal Folder') {
            void vscode.commands.executeCommand('revealInExplorer', folder);
        }
    };
    const buttons = notes.length > 0 ? ['Show Details', 'Reveal Folder'] : ['Reveal Folder'];
    (notes.length > 0 ? vscode.window.showWarningMessage(message, ...buttons) : vscode.window.showInformationMessage(message, ...buttons)).then(show);
}
