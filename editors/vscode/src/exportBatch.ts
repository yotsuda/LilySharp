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
// score of every selected file lands there, named as `lysc` names them
// (the unnamed score takes the file's stem, every other appends its own name).
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
import type { AudioFormat, AudioJob } from './audioRender';
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
    /** The sound font a WAV or MP3 export plays through — the preview's, else the one the
     *  writer picks; undefined when they cancel. */
    wavSoundFont: () => Promise<string | undefined>;
    /** Renders the files the server named (their notes) through a sound font, as the preview
     *  plays them, and writes them as WAV or MP3 (audioRender.ts). */
    renderAudio: (jobs: AudioJob[], soundFont: string, format: AudioFormat) => Promise<void>;
    /** The ffmpeg an MP4 export runs, once it is known to run; undefined (the writer told how
     *  to get it) when it does not. */
    ffmpeg: () => Promise<string | undefined>;
    /** Writes the MP4s the server named: the pictures it draws for their notes, their sound,
     *  joined by ffmpeg (videoExport.ts); `source` names the file as the export named it. */
    renderVideo: (jobs: AudioJob[], soundFont: string, ffmpeg: string,
        source: { textDocument?: { uri: string }; path?: string }) => Promise<void>;
    /** The jobs with their notes under the practice menu of the preview showing `uri` — as
     *  written for a file no preview shows (undefined: not open at all). */
    practisedJobs: (jobs: AudioJob[], uri: string | undefined) => AudioJob[];
    /** The jobs with the sung voices the singing engine renders for them (docs/singing-design.md
     *  §5), a sung-only part's notes out of the instruments' list — as the preview's Export; the
     *  jobs as they are when there is no engine. `source` names the file as the export did. */
    singJobs: (jobs: AudioJob[], source: { textDocument?: { uri: string }; path?: string }, uri: string | undefined) => Promise<AudioJob[]>;
    /** That menu in words for the log; empty when it plays the score as written. */
    practiceSummary: (uri: string | undefined) => string;
}

interface ExportResponse {
    Success: boolean;
    OutputPath: string | null;
    OutputPaths?: string[] | null;
    Warnings?: string[] | null;
    /** A WAV, MP3 or MP4 asked for with playbackOnly: each file and its notes, for the editor to
     *  render. */
    Playbacks?: AudioJob[] | null;
    Error: string | null;
}

/** The formats whose sound the editor renders, rather than the server writing them. */
const isAudio = (id: string): id is AudioFormat => id === 'wav' || id === 'mp3';
/** The formats the editor writes from the server's notes: the sounds and the video. */
const editorWrites = (id: string) => isAudio(id) || id === 'mp4';

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

    // A video needs ffmpeg, and a WAV, an MP3 or a video a font, before anything else is asked:
    // without them, no export.
    let ffmpeg: string | undefined;
    if (format.id === 'mp4') {
        ffmpeg = await deps.ffmpeg();
        if (!ffmpeg) {
            return;
        }
    }
    let soundFont: string | undefined;
    if (editorWrites(format.id)) {
        soundFont = await deps.wavSoundFont();
        if (!soundFont) {
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
                // A WAV, an MP3 or an MP4: the server names the files and gives their notes (and
                // draws a video's pictures); the editor renders them through the preview's synth
                // and font below.
                playbackOnly: editorWrites(format.id),
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
                if (soundFont && response.Playbacks) {
                    // As the preview showing the file plays it, the loop aside (owner's request
                    // 2026-11-07); a file no preview shows, as written.
                    const openUri = open?.uri.toString();
                    const source = open ? { textDocument: { uri: open.uri.toString() } } : { path: file };
                    // …and sung, where the score sings (owner's report 2026-11-09: this door
                    // wrote the instruments alone while the preview's Export sang).
                    const jobs = await deps.singJobs(deps.practisedJobs(response.Playbacks, openUri), source, openUri);
                    const practised = deps.practiceSummary(openUri);
                    if (practised) { deps.log(`    played ${practised}`); }
                    for (const job of jobs) {
                        if (job.Voices && job.Voices.length > 0) { deps.log(`    sung: ${job.Voices.length} voice(s) in ${path.basename(job.OutputPath)}`); }
                    }
                    if (isAudio(format.id)) {
                        await deps.renderAudio(jobs, soundFont, format.id);
                    } else if (ffmpeg && format.id === 'mp4') {
                        await deps.renderVideo(jobs, soundFont, ffmpeg, source);
                    }
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
