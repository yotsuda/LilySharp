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

// "Convert Recording to Lily# (.lys)… (Preview)" — Orecchiante is a Preview (owner's decision 2026-11-15;
// Calamo's menu says "(Preview)" too since 2026-11-18): runs Orecchiante on a recording and opens the chart it writes,
// with the file beside it (`x.orecchiante.json`) that the preview plays the recording by
// (docs/recording-design.md §2). The menu shows on Windows whether or not Orecchiante is installed; run
// without it, it offers to install it (owner's decision 2026-11-15). orecchianteCore.ts decides.

import * as vscode from 'vscode';
import * as cp from 'child_process';
import * as fs from 'fs';
import * as path from 'path';
import {
    ORECCHIANTE_EXTENSION_ID, OrecchianteLocation, OrecchianteVersion, chartForConversion, orecchianteArgs, orecchianteFailure, judgeOrecchiante,
    besideChart, locateOrecchiante, parseOrecchianteVersion, replaceQuestion,
    LyricsList, LyricsSong, canFetchLyrics, lyricsArgs, lyricsIdArgs, lyricsListArgs, lyricsOutcome, lyricsPathFor, lyricsQuestion,
    lyricsSearchStart, lyricsSongItem, parseLyricsList,
} from './orecchianteCore';
import { parseRecording, recordingPathFor } from './recordingCore';
import { markJustConverted } from './recording';
import { importFilters } from './fileFilters';

async function orecchianteExports(): Promise<unknown> {
    const ext = vscode.extensions.getExtension(ORECCHIANTE_EXTENSION_ID);
    if (!ext) {
        return undefined;
    }
    try {
        return ext.isActive ? ext.exports : await ext.activate();
    } catch {
        return undefined;
    }
}

export async function findOrecchiante(): Promise<OrecchianteLocation | undefined> {
    return locateOrecchiante(process.env, await orecchianteExports());
}

/** Installs — or, installed, updates — Orecchiante's extension; a gallery that does not list it
 *  (offline, or not published yet) gets the search instead. */
async function installOrecchiante(): Promise<void> {
    try {
        await vscode.commands.executeCommand('workbench.extensions.installExtension', ORECCHIANTE_EXTENSION_ID);
    } catch {
        await vscode.commands.executeCommand('workbench.extensions.search', `@id:${ORECCHIANTE_EXTENSION_ID}`);
    }
}

/** Says the reader is missing and offers its extension (as offerOmrExtension does for Calamo). */
export async function offerOrecchiante(): Promise<void> {
    const install = 'Install Orecchiante';
    const choice = await vscode.window.showInformationMessage(
        'Lily#: converting a recording needs the Orecchiante extension (Preview, Windows). Install it?',
        install);
    if (choice === install) {
        await installOrecchiante();
    }
}

/** Offers the reader's update (offerOmrUpdate's twin). True when the person took it. */
async function offerOrecchianteUpdate(text: string): Promise<boolean> {
    const update = 'Update Orecchiante';
    if (await vscode.window.showWarningMessage(`Lily#: ${text}`, update) !== update) {
        return false;
    }
    await installOrecchiante();
    return true;
}

/** Once per install: the reader is a Preview, so its marks are where to look first (noteOmrBeta's twin). */
async function noteOrecchiantePreview(context: vscode.ExtensionContext): Promise<void> {
    const key = 'orecchiante.previewNoted';
    if (context.globalState.get<boolean>(key)) {
        return;
    }
    await context.globalState.update(key, true);
    void vscode.window.showInformationMessage(
        'Lily#: Orecchiante is a Preview. It marks every chord it is unsure of with @todo — '
        + 'check those against the recording (the preview plays it) before trusting the chart.');
}

/** Keeps `lilysharp.orecchianteAvailable` true to what is installed (the name is shared with
 *  Orecchiante's extension, which sets it on startup: Lab sessions/p1242/orecchiante reply). */
export function registerOrecchianteContext(context: vscode.ExtensionContext, output: vscode.OutputChannel): void {
    let said: string | undefined;
    const update = async () => {
        const where = await findOrecchiante();
        const line = where ? `Orecchiante reader (${where.source}${where.version ? ' ' + where.version : ''}): ${where.path}`
            : 'Orecchiante reader: none (Convert Recording to Lily# (.lys)… (Preview) offers the extension)';
        if (line !== said) {
            said = line;
            output.appendLine(line);
        }
        await vscode.commands.executeCommand('setContext', 'lilysharp.orecchianteAvailable', where !== undefined);
    };
    void update();
    context.subscriptions.push(vscode.extensions.onDidChange(() => void update()));
}

/** Runs the reader; resolves to its exit code and what it said on stderr (undefined: cancelled). */
/** The environment the reader runs in: with `LILYSHARP_LYSC_PATH`, the lysc this extension carries
 *  (Orecchiante's request 9, 2026-11-16 — `lysc shape` gives the first chord's default diagram, so the
 *  chart carries one without Orecchiante keeping a table of shapes). An environment variable rather
 *  than an argument: a reader that does not know it is not stopped by it. */
function readerEnv(lysc: string | undefined): NodeJS.ProcessEnv {
    return lysc ? { ...process.env, LILYSHARP_LYSC_PATH: lysc } : process.env;
}

function run(exe: string, args: string[], output: vscode.OutputChannel,
    progress: vscode.Progress<{ message?: string }>, token: vscode.CancellationToken,
    lysc?: string): Promise<{ code: number | null; stderr: string; stdout: string } | undefined> {
    return new Promise((resolve, reject) => {
        const child = cp.spawn(exe, args, { windowsHide: true, env: readerEnv(lysc) });
        let cancelled = false, stderr = '', stdout = '';
        const stop = token.onCancellationRequested(() => { cancelled = true; child.kill(); });
        child.stdout.on('data', (b: Buffer) => { stdout += b.toString(); output.append(b.toString()); });
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
        child.on('close', code => { stop.dispose(); resolve(cancelled ? undefined : { code, stderr, stdout }); });
    });
}

/** Asks the reader its version (`--version --json`), as Calamo is asked. */
function askVersion(exe: string): Promise<OrecchianteVersion | undefined> {
    return new Promise(resolve => cp.execFile(exe, ['--version', '--json'], { timeout: 20000, windowsHide: true },
        (err, stdout) => resolve(err ? undefined : parseOrecchianteVersion(String(stdout)))));
}

/** The lysc beside the language server in this extension, or undefined when it is not there. */
function bundledLysc(context: vscode.ExtensionContext): string | undefined {
    const p = path.join(context.extensionPath, 'server', process.platform === 'win32' ? 'lysc.exe' : 'lysc');
    return fs.existsSync(p) ? p : undefined;
}

/** Whether a version string ("0.3.1") is at least major.minor.patch. */
function versionAtLeast(version: string | undefined, major: number, minor: number, patch: number): boolean {
    const m = /^(\d+)\.(\d+)\.(\d+)/.exec(version ?? '');
    if (!m) { return false; }
    const [a, b, c] = [Number(m[1]), Number(m[2]), Number(m[3])];
    return a !== major ? a > major : b !== minor ? b > minor : c >= patch;
}

/** Puts words the person chose (an `.lrc`, or a `.txt` a line a line) beside the recording as `<name>.lrc`,
 *  where the conversion reads them. False when it could not be written. */
async function copyLyrics(recording: string, output: vscode.OutputChannel): Promise<boolean> {
    const picked = await vscode.window.showOpenDialog({
        title: `Lily#: the words of ${path.basename(recording)}`, openLabel: 'Use These Words', canSelectMany: false,
        defaultUri: vscode.Uri.file(path.dirname(recording)),
        filters: { 'Lyrics': ['lrc', 'txt'] },
    });
    if (!picked || picked.length === 0) {
        return false;
    }
    const lrc = lyricsPathFor(recording);
    try {
        await fs.promises.copyFile(picked[0].fsPath, lrc);
        output.appendLine(`copied ${picked[0].fsPath} → ${lrc} (the conversion reads the words beside the recording)`);
        return true;
    } catch (e) {
        void vscode.window.showWarningMessage(`Lily#: the words could not be put beside the recording (${lrc}): ${(e as Error).message ?? e}. The chart is written without them.`);
        return false;
    }
}

/**
 * Before a conversion, the words of the song (Orecchiante's request 25, 2026-11-26): words already beside
 * the recording (`<name>.lrc`) are read by the conversion itself, unasked; otherwise, with a reader that can
 * fetch them, the person is asked first — nothing is sent unless they choose to. The words found are named
 * and may be changed for another song; none found, the songs LRCLIB has are listed to choose from, searched
 * again by other words (chooseLyrics; Orecchiante's request 28). A file of their own may be chosen too.
 * False only when the person cancelled the conversion.
 */
async function prepareLyrics(reader: OrecchianteLocation, v: OrecchianteVersion, recording: string,
    output: vscode.OutputChannel): Promise<boolean> {
    const lrc = lyricsPathFor(recording);
    if (fs.existsSync(lrc)) {
        output.appendLine(`words beside the recording: ${lrc}`);
        return true;
    }
    const setting = vscode.workspace.getConfiguration('lilysharp.recordings');
    if (!canFetchLyrics(v) || setting.get<string>('fetchLyrics', 'ask') === 'never') {
        return true;
    }
    const name = path.basename(recording);
    const fetch = 'Fetch Lyrics', file = 'Choose a File…', without = 'Without Lyrics', never = 'Never Ask';
    const answer = await vscode.window.showInformationMessage(
        lyricsQuestion(name), { modal: true }, fetch, file, without, never);
    if (answer === undefined) {
        return false;
    }
    if (answer === never) {
        await setting.update('fetchLyrics', 'never', vscode.ConfigurationTarget.Global);
        void vscode.window.showInformationMessage('Lily#: the words will not be offered again (the setting Recordings › Fetch Lyrics brings the question back).');
        return true;
    }
    if (answer === without) {
        return true;
    }
    if (answer === file) {
        await copyLyrics(recording, output);
        return true;
    }
    const done = await askLRCLIB(reader, v, lyricsArgs(recording), `fetching the words of ${name} from LRCLIB`, output);
    if (done === undefined) {
        return true;
    }
    const outcome = lyricsOutcome(done.code, done.stderr);
    if (outcome.kind === 'found') {
        // Asked, not only said: the entry shows a cover version or a live take taken by mistake, and the
        // conversion reads the words as soon as it starts (Orecchiante's request 28, §4).
        const use = 'Use These Words', another = 'Choose Another…';
        const choice = await vscode.window.showInformationMessage(
            `Words from LRCLIB${outcome.entry ? `: ${outcome.entry}` : ''} — saved beside the recording as ${path.basename(lrc)}.`,
            { modal: true }, use, another);
        if (choice === undefined) {
            return false;
        }
        return choice === use ? true : chooseLyrics(reader, v, recording, output, 'choose another song', true);
    }
    if (outcome.kind === 'notFound') {
        return chooseLyrics(reader, v, recording, output, outcome.reason || 'LRCLIB has no lyrics for this song by its name and length');
    }
    sayNoLyrics(outcome, output);
    return true;
}

/** Says why the chart is written without lyrics: LRCLIB not reached, or the reader failed. */
function sayNoLyrics(outcome: { kind: string; reason?: string }, output: vscode.OutputChannel): void {
    if (outcome.kind === 'unreached') {
        void vscode.window.showWarningMessage(`Lily#: LRCLIB could not be reached${outcome.reason ? ` (${outcome.reason})` : ''}. The chart is written without lyrics.`);
        return;
    }
    void vscode.window.showWarningMessage(`Lily#: the words could not be fetched${outcome.reason ? `: ${outcome.reason}` : '.'} The chart is written without lyrics.`, 'Show Output')
        .then(choice => { if (choice === 'Show Output') { output.show(true); } });
}

/** One call of `orecchiante lyrics`, with a cancellable progress; undefined when cancelled or it could not
 *  run (said: the chart is written without lyrics). */
async function askLRCLIB(reader: OrecchianteLocation, v: OrecchianteVersion, args: string[], doing: string,
    output: vscode.OutputChannel): Promise<{ code: number | null; stderr: string; stdout: string } | undefined> {
    output.appendLine(`Orecchiante ${v.version}: orecchiante ${args.join(' ')}`);
    try {
        return await vscode.window.withProgress(
            { location: vscode.ProgressLocation.Notification, title: `Lily#: ${doing}`, cancellable: true },
            (progress, token) => run(reader.path, args, output, progress, token));
    } catch (e) {
        void vscode.window.showWarningMessage(`Lily#: the words could not be fetched (${(e as Error).message ?? e}). The chart is written without them.`);
        return undefined;
    }
}

type LyricsPick = vscode.QuickPickItem & { song?: LyricsSong; act?: 'search' | 'file' | 'without' };

/**
 * The songs LRCLIB has, to choose from (Orecchiante's request 28): by the recording's title and artist first,
 * then by any words the person searches with, as often as they like. Every song is listed whatever its length,
 * the difference from the recording shown; songs LRCLIB has no words for are left out. The songs come from the
 * same service the person agreed to at the first question. `replacing`: words fetched already lie beside the
 * recording (Choose Another…) — Without Lyrics takes them away. False only when the person stopped the conversion.
 */
async function chooseLyrics(reader: OrecchianteLocation, v: OrecchianteVersion, recording: string,
    output: vscode.OutputChannel, why: string, replacing = false): Promise<boolean> {
    const name = path.basename(recording);
    let query: string | undefined;
    let list: LyricsList | undefined;
    for (;;) {
        const done = await askLRCLIB(reader, v, lyricsListArgs(recording, query),
            query ? `searching LRCLIB for “${query}”` : `listing the songs LRCLIB has for ${name}`, output);
        if (done === undefined) {
            return true;
        }
        const got = done.code === 0 ? parseLyricsList(done.stdout) : undefined;
        if (!got) {
            sayNoLyrics(lyricsOutcome(done.code === 0 ? 2 : done.code, done.stderr), output);
            return true;
        }
        list = got;
        const songs = list.songs.filter(s => s.words);
        const looked = lyricsSearchStart(list);
        const items: LyricsPick[] = [
            ...songs.map(song => ({ ...lyricsSongItem(song, list?.seconds), song })),
            { label: '', kind: vscode.QuickPickItemKind.Separator },
            { label: '$(search) Search with Other Words…', description: 'title, artist or album — not the words of the song', act: 'search' },
            { label: '$(file) Choose a File…', description: '.lrc or .txt', act: 'file' },
            { label: '$(close) Without Lyrics', act: 'without' },
        ];
        for (;;) {
            const picked = await vscode.window.showQuickPick(items, {
                title: `Lily#: the words of ${name} — ${query === undefined ? why : `searched for “${query}”`} (Esc stops the conversion)`,
                placeHolder: songs.length === 0
                    ? `LRCLIB has no songs with words for “${looked}” — search with other words`
                    : `${songs.length} song${songs.length === 1 ? '' : 's'} LRCLIB has for “${looked}”, those as long as the recording first`,
                matchOnDescription: true, ignoreFocusOut: true,
            });
            if (picked === undefined) {
                return false;
            }
            if (picked.act === 'without') {
                if (replacing) {
                    fs.rmSync(lyricsPathFor(recording), { force: true });
                    output.appendLine(`removed ${lyricsPathFor(recording)} (without lyrics)`);
                }
                return true;
            }
            if (picked.act === 'file') {
                await copyLyrics(recording, output);
                return true;
            }
            if (picked.act === 'search') {
                const words = await vscode.window.showInputBox({
                    title: 'Lily#: search LRCLIB with other words',
                    prompt: 'Words of the title, the artist or the album (LRCLIB does not search the words of songs). Esc goes back to the list.',
                    value: looked, ignoreFocusOut: true,
                });
                if (words === undefined || words.trim() === '') {
                    continue;
                }
                query = words.trim();
                break;
            }
            const song = picked.song!;
            const wrote = await askLRCLIB(reader, v, lyricsIdArgs(recording, song.id), `fetching the words of ${song.title} from LRCLIB`, output);
            if (wrote === undefined) {
                return true;
            }
            const outcome = lyricsOutcome(wrote.code, wrote.stderr);
            if (outcome.kind === 'found') {
                void vscode.window.showInformationMessage(`Lily#: words from LRCLIB${outcome.entry ? `: ${outcome.entry}` : ''} — saved beside the recording as ${path.basename(lyricsPathFor(recording))}.`);
                return true;
            }
            if (outcome.kind === 'notFound') {
                void vscode.window.showWarningMessage(`Lily#: ${outcome.reason || 'LRCLIB has no words for that song'} — choose another.`);
                continue;
            }
            sayNoLyrics(outcome, output);
            return true;
        }
    }
}

/**
 * Converts a recording (right-clicked, or chosen) into a chart and opens it. The steps and the
 * words follow "Convert Image/PDF to Lily# (.lys)…" (omrImport.ts importFromImage): the reader
 * first (offered when missing, before any file is chosen), its version asked, the folder asked
 * (the recording's offered first), the progress cancellable, the same failures.
 */
export async function convertRecording(context: vscode.ExtensionContext, uri: vscode.Uri | undefined,
    output: vscode.OutputChannel, openPreview?: (chart: vscode.Uri) => Thenable<unknown>): Promise<void> {
    const reader = await findOrecchiante();
    if (!reader) {
        await offerOrecchiante();
        return;
    }
    if (!uri) {
        const picked = await vscode.window.showOpenDialog({
            title: 'Convert Recording to Lily# (.lys)', openLabel: 'Convert', canSelectMany: false,
            filters: importFilters('Recordings', reader.inputs),
        });
        if (!picked || picked.length === 0) {
            return;
        }
        uri = picked[0];
    }
    output.appendLine(`Orecchiante reader (${reader.source}): ${reader.path}`);
    const v = await askVersion(reader.path);
    if (!v) {
        void vscode.window.showErrorMessage(`Lily#: the Orecchiante reader at ${reader.path} did not answer '--version --json'.`);
        return;
    }
    const judged = judgeOrecchiante(v);
    if (judged.kind === 'updateLilySharp') {
        void vscode.window.showErrorMessage(`Lily#: ${judged.text}`);
        return;
    }
    if (judged.kind === 'offerUpdate' && (await offerOrecchianteUpdate(judged.text) || !judged.goesOn)) {
        return;
    }
    const inputs = v.inputs ?? [...reader.inputs];
    const kind = path.extname(uri.fsPath).slice(1).toLowerCase();
    if (uri.scheme !== 'file' || !inputs.includes(kind)) {
        void vscode.window.showErrorMessage(`Lily#: Orecchiante reads a recording on this computer (${inputs.join(', ')}).`);
        return;
    }
    const recording = uri.fsPath;
    // The folder is asked every time, the recording's offered first (owner's decision 2026-11-16,
    // Orecchiante's request 6: a chart and its file beside the recording was awkward to keep).
    let chart = chartForConversion(recording);
    for (;;) {
        const folder = await vscode.window.showOpenDialog({
            canSelectFiles: false, canSelectFolders: true, canSelectMany: false, openLabel: 'Write the Chart Here',
            defaultUri: vscode.Uri.file(path.dirname(recording)),
            title: `Lily#: where should the chart of ${path.basename(recording)} go?`,
        });
        if (!folder || folder.length === 0) {
            return;
        }
        try {
            await fs.promises.access(folder[0].fsPath, fs.constants.W_OK);
        } catch {
            void vscode.window.showErrorMessage(`Lily#: ${folder[0].fsPath} cannot be written — choose another folder.`);
            continue;
        }
        chart = path.join(folder[0].fsPath, path.basename(chart));
        break;
    }
    // A chart beside the recording may hold the person's corrections: never written over unasked.
    let replace = false;
    if (fs.existsSync(chart)) {
        // The bars a person placed by hand are named: the reading writes over them (Orecchiante's note
        // of 2026-11-16; reading again around them is its D4, with format 2).
        let byHand = 0;
        try {
            const rec = parseRecording(fs.readFileSync(recordingPathFor(chart), 'utf8'));
            byHand = 'error' in rec ? 0 : rec.bars.filter(b => b.byHand).length;
        } catch {
            // (no file beside it)
        }
        // Or beside it: a chart written by ear, to be compared with the reading, not replaced
        // (owner's decision 2026-11-16).
        const answer = await vscode.window.showWarningMessage(
            replaceQuestion(path.basename(chart), byHand), { modal: true }, 'Replace', 'Write Beside');
        if (answer === 'Write Beside') {
            chart = besideChart(chart, fs.existsSync);
        } else if (answer === 'Replace') {
            replace = true;
        } else {
            return;
        }
    }
    if (!await prepareLyrics(reader, v, recording, output)) {
        return;
    }
    const name = path.basename(recording);
    void noteOrecchiantePreview(context);
    output.appendLine(`Orecchiante ${v.version}: orecchiante ${orecchianteArgs(recording, chart, replace).join(' ')}`);
    let done: { code: number | null; stderr: string } | undefined;
    try {
        done = await vscode.window.withProgress(
            { location: vscode.ProgressLocation.Notification, title: `Lily#: reading ${name}`, cancellable: true },
            (progress, token) => run(reader.path, orecchianteArgs(recording, chart, replace), output, progress, token, bundledLysc(context)));
    } catch (e) {
        void vscode.window.showErrorMessage(`Lily#: the Orecchiante reader could not start: ${(e as Error).message ?? e}`);
        return;
    }
    if (done === undefined) {
        void vscode.window.showInformationMessage(`Lily#: reading ${name} was stopped.`);
        return;
    }
    if (done.code !== 0 || !fs.existsSync(chart)) {
        const failure = done.code !== 0 ? orecchianteFailure(done.code, done.stderr, name)
            : { text: 'Lily#: the Orecchiante reader finished without writing a chart.', showOutput: true };
        if (failure.showOutput) {
            void vscode.window.showErrorMessage(failure.text, 'Show Output')
                .then(choice => { if (choice === 'Show Output') { output.show(true); } });
        } else {
            void vscode.window.showErrorMessage(failure.text);
        }
        return;
    }
    // A chart written in another folder: Orecchiante 0.3.0 and later write the way to the recording in
    // the file beside the chart (`audio`, read by recordingCore.resolveAudio — Orecchiante's request 6),
    // and the recording stays where it is. An older reader names it without a path, so the
    // recording goes beside the chart, or the preview cannot find it.
    if (path.dirname(chart).toLowerCase() !== path.dirname(recording).toLowerCase() && !versionAtLeast(v.version, 0, 3, 0)) {
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
    // The chart and its preview beside it (as Calamo's read opens the score) — with the recording on,
    // so Play sounds it at once (owner's request 2026-11-16; recording.ts justConverted).
    markJustConverted(chart);
    const doc = await vscode.workspace.openTextDocument(chart);
    await vscode.window.showTextDocument(doc, vscode.ViewColumn.One);
    if (openPreview) {
        await openPreview(doc.uri);
    }
}
