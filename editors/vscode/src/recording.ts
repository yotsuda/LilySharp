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
    CopySource, WEBVIEW_AUDIO, WINDOWS_AUDIO, audioReader, audioRef, chartForRecording, chartPathFor, copiedRecordingText, newRecording,
    parseRecording, recordingPathFor, resolveAudio, samePath, serializeRecording, sourceForCopy, workForRename,
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
 * on a recording, or from the Command Palette for the chart open in the editor. A recording in
 * another folder stays where it is: the file names the way to it (audioRef; owner's decision
 * 2026-11-16, which retired the copy). Returns the file written, or undefined.
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
            void vscode.window.showErrorMessage('Lily#: there is no chart (.lys) beside this recording — open the chart and run "Attach Recording…" there to choose it.');
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

    const file = vscode.Uri.file(recordingPathFor(chart.fsPath));
    const name = path.basename(audio.fsPath);
    const before = await readText(file);
    if (before !== undefined) {
        const old = parseRecording(before);
        if (!('error' in old) && samePath(resolveAudio(file.fsPath, old.audio), audio.fsPath)) {
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
    await vscode.workspace.fs.writeFile(file, Buffer.from(serializeRecording(newRecording(audioRef(file.fsPath, audio.fsPath))), 'utf8'));
    onAttached?.(chart);
    void vscode.window.showInformationMessage(
        `Lily#: ${name} is attached to ${path.basename(chart.fsPath)} (in ${path.basename(file.fsPath)}). Its bars are not placed yet: mark them in the preview while it plays.`);
    return file;
}

/** What the language server says of lilysharp/decodeRecording. */
export interface DecodeResult { Ok: boolean; Error?: string | null; Seconds?: number }

const sentKeys = new WeakMap<vscode.WebviewPanel, string>();

async function statKey(uri: vscode.Uri): Promise<string | undefined> {
    try {
        const s = await vscode.workspace.fs.stat(uri);
        return `${s.mtime}:${s.size}`;
    } catch {
        return undefined;
    }
}

/** Charts just written from their recording (Convert Recording to Lily#), by path: their preview
 *  starts with the recording ON — Play sounds it with the score (owner's request 2026-11-16: right
 *  after the chart is made from the recording, the recording is what one wants to hear). Once: the
 *  mark goes when the sound is sent, and the practice menu's toggle is the reader's after that. */
const justConverted = new Set<string>();

function chartKey(chart: string): string {
    return path.resolve(chart).toLowerCase();
}

/** Marks `chart` as just written from its recording (see justConverted). */
export function markJustConverted(chart: string): void {
    justConverted.add(chartKey(chart));
}

/**
 * Tells the preview of `chart` about its recording: none, why it cannot be used, or its bars and
 * its sound (the file's bytes; for an m4a, the float WAV the language server decoded into the
 * extension's storage — never beside the chart). Sent again only when the file beside the chart,
 * the recording or the chart shown changed (a preview re-renders on every keystroke).
 */
export async function sendRecording(panel: vscode.WebviewPanel, chart: vscode.Uri, storage: vscode.Uri,
    decode: (input: string, output: string) => Promise<DecodeResult>, force = false): Promise<void> {
    const file = vscode.Uri.file(recordingPathFor(chart.fsPath));
    const fileKey = await statKey(file);
    const text = fileKey ? await readText(file) : undefined;
    const rec = text !== undefined ? parseRecording(text) : undefined;
    const audio = rec && !('error' in rec) ? vscode.Uri.file(path.normalize(resolveAudio(file.fsPath, rec.audio))) : undefined;
    const audioKey = audio ? await statKey(audio) : undefined;
    const key = `${chart.toString()}\n${fileKey}\n${audioKey}`;
    if (!force && sentKeys.get(panel) === key) {
        return;
    }
    sentKeys.set(panel, key);
    const post = (m: Record<string, unknown>) => void panel.webview.postMessage({ type: 'recording', chart: chart.toString(), ...m });
    if (!rec) {
        post({ none: true });
        return;
    }
    if ('error' in rec) {
        post({ error: `${path.basename(file.fsPath)}: ${rec.error}` });
        return;
    }
    const shown = path.basename(audio!.fsPath);
    const base = { name: shown, bars: rec.bars, pickup: rec.pickup, settings: recordingSettingsFor(chart.toString(), shown) ?? null };
    if (!audio || !audioKey) {
        // Moved, renamed outside VS Code, or on a drive not here: say where it was looked for, and
        // let it be found again (Dettato's request 6).
        post({ ...base, error: `${shown} cannot be found (${audio!.fsPath})` });
        offerLocate(chart, audio!.fsPath, key);
        return;
    }
    const how = audioReader(rec.audio, isWindows);
    if (typeof how === 'object') {
        post({ ...base, error: how.refused });
        return;
    }
    try {
        let bytes: Uint8Array;
        if (how === 'webview') {
            bytes = await vscode.workspace.fs.readFile(audio);
        } else {
            // The decoded WAV, kept in the extension's storage under the recording's path and stamp.
            const dir = vscode.Uri.joinPath(storage, 'recordings');
            await vscode.workspace.fs.createDirectory(dir);
            const id = Buffer.from(audio.fsPath.toLowerCase()).toString('base64url').slice(-48);
            const cached = vscode.Uri.joinPath(dir, `${id}-${audioKey.replace(':', '-')}.wav`);
            if (!(await exists(cached))) {
                post({ ...base, preparing: true });
                const r = await decode(audio.fsPath, cached.fsPath);
                if (!r.Ok) {
                    post({ ...base, error: r.Error ?? 'the recording could not be decoded' });
                    return;
                }
            }
            bytes = await vscode.workspace.fs.readFile(cached);
        }
        if (sentKeys.get(panel) !== key) {
            return;   // (something newer was sent meanwhile)
        }
        // A plain Uint8Array, as the voices are sent: a Node Buffer does not cross postMessage as one.
        // A chart just made from this recording starts with it on (justConverted), once.
        const turnOn = justConverted.delete(chartKey(chart.fsPath));
        post({ ...base, bytes: new Uint8Array(bytes), ...(turnOn ? { turnOn: true } : {}) });
    } catch (e) {
        post({ ...base, error: String(e) });
    }
}

/** Where the recording panel's settings are kept, per chart and recording (owner's decision
 *  2026-11-16: its octave, semitones, cents, speed and link are the recording's, kept for the next
 *  time — nothing the writer edits by hand). Set once by the extension (workspaceState). */
let settingsStore: vscode.Memento | undefined;

/** Gives the panel's settings their store (see settingsStore). */
export function useRecordingSettingsStore(store: vscode.Memento): void {
    settingsStore = store;
}

function settingsKey(chart: string, audio: string): string {
    return `lilysharp.recordingSettings:${chartKey(vscode.Uri.parse(chart).fsPath)}|${audio.toLowerCase()}`;
}

/** The panel settings kept for `audio` beside `chart`, or undefined. */
export function recordingSettingsFor(chart: string, audio: string): Record<string, unknown> | undefined {
    const kept = settingsStore?.get<unknown>(settingsKey(chart, audio));
    return kept && typeof kept === 'object' ? kept as Record<string, unknown> : undefined;
}

/** Keeps the panel's settings for `audio` beside `chart` (the panel says them as they change): only
 *  the panel's own numbers, never the chart's transpose, which comes with each picture. */
export function keepRecordingSettings(chart: string, audio: string, settings: unknown): void {
    if (!settingsStore || !settings || typeof settings !== 'object') { return; }
    const s = settings as Record<string, unknown>;
    const kept: Record<string, number | boolean> = {};
    for (const k of ['speed', 'octave', 'semitones', 'cents'] as const) {
        if (typeof s[k] === 'number' && Number.isFinite(s[k])) { kept[k] = s[k] as number; }
    }
    if (typeof s.linked === 'boolean') { kept.linked = s.linked; }
    void settingsStore.update(settingsKey(chart, audio), kept);
}

/** The recordings already asked about, so a preview re-rendering does not ask again. */
const locateAsked = new Set<string>();

/** Says a chart's recording cannot be found, and offers to find it: the file beside the chart then
 *  names the one chosen, its bars kept. */
function offerLocate(chart: vscode.Uri, lookedFor: string, key: string): void {
    if (locateAsked.has(key)) { return; }
    locateAsked.add(key);
    const locate = 'Locate Recording…';
    void vscode.window.showWarningMessage(
        `Lily#: the recording of ${path.basename(chart.fsPath)} cannot be found: ${lookedFor}.`, locate)
        .then(async choice => {
            if (choice !== locate) { return; }
            const kinds = [...WEBVIEW_AUDIO, ...(isWindows ? WINDOWS_AUDIO : [])];
            const picked = await vscode.window.showOpenDialog({
                title: `Where is ${path.basename(lookedFor)}?`, canSelectMany: false,
                defaultUri: vscode.Uri.file(path.dirname(chart.fsPath)), filters: { Recordings: kinds }, openLabel: 'Use This Recording',
            });
            if (!picked || picked.length === 0) { return; }
            const file = vscode.Uri.file(recordingPathFor(chart.fsPath));
            const text = await readText(file);
            const rec = text !== undefined ? parseRecording(text) : undefined;
            if (!rec || 'error' in rec) { return; }
            rec.audio = audioRef(file.fsPath, picked[0].fsPath);
            await vscode.workspace.fs.writeFile(file, Buffer.from(serializeRecording(rec), 'utf8'));
            locateAsked.delete(key);
        });
}

/** Forgets what was sent to a preview, so the next update sends the recording again. */
export function forgetSentRecording(panel: vscode.WebviewPanel): void {
    sentKeys.delete(panel);
}

/**
 * A chart copied in VS Code takes the file beside it along, and a file beside a chart copied takes
 * its chart (owner's request 2026-11-16: a chart copied to another folder played without its
 * recording). The Explorer's copy and paste and File > Save As both raise onDidCreateFiles
 * (MEASURED in VS Code 1.146, Lab sessions/p1286/e2e/run-ev.js); a copy outside VS Code raises
 * nothing and is not followed.
 *
 * VS Code says what was made, not what from: the source is the chart with the same text that has
 * a file beside it — an open chart as it stood just before (a Save As writes the editor's text,
 * edits and all) or a chart of the workspace on the disk (sourceForCopy). Charts that name
 * different recordings are asked about, not guessed.
 */
export function followCopies(): vscode.Disposable {
    let before = new Map<string, string>();
    const willCreate = vscode.workspace.onWillCreateFiles(() => {
        before = new Map(vscode.workspace.textDocuments
            .filter(d => d.uri.scheme === 'file' && d.fileName.toLowerCase().endsWith('.lys'))
            .map(d => [d.uri.fsPath, d.getText()]));
    });
    const didCreate = vscode.workspace.onDidCreateFiles(async e => {
        const open = before;
        for (const file of e.files) {
            if (file.scheme !== 'file') {
                continue;
            }
            try {
                const lower = file.fsPath.toLowerCase();
                if (lower.endsWith('.lys')) {
                    await chartCopied(file.fsPath, open);
                } else if (lower.endsWith('.dettato.json')) {
                    await recordingFileCopied(file.fsPath);
                }
            } catch (err) {
                void vscode.window.showWarningMessage(`Lily#: could not bring the recording along with ${path.basename(file.fsPath)}: ${err}`);
            }
        }
    });
    return vscode.Disposable.from(willCreate, didCreate);
}

/** The text of a file, the way charts are compared: line ends aside (a Save As writes the editor's). */
function sameText(a: string, b: string): boolean {
    return a.replace(/\r\n/g, '\n') === b.replace(/\r\n/g, '\n');
}

async function chartCopied(chart: string, open: ReadonlyMap<string, string>): Promise<void> {
    const beside = recordingPathFor(chart);
    if (await exists(vscode.Uri.file(beside))) {
        return;     // (copied together with the chart: recordingFileCopied sets its way to the recording)
    }
    // A Save As makes the file before it writes the text (MEASURED: empty when created is said), so
    // a text that matches no open chart is read again a moment later — the disk's or the new editor's.
    let text = await readText(vscode.Uri.file(chart));
    const openMatch = (t: string | undefined) => t !== undefined && [...open.values()].some(o => sameText(o, t));
    for (let i = 0; i < 10 && !openMatch(text); i++) {
        await new Promise(r => setTimeout(r, 200));
        text = vscode.workspace.textDocuments.find(d => samePath(d.uri.fsPath, chart))?.getText() ?? await readText(vscode.Uri.file(chart));
    }
    if (text === undefined || text === '') {
        return;
    }
    const charts = new Set<string>();
    for (const [p, t] of open) {
        if (sameText(t, text)) { charts.add(p); }
    }
    for (const u of await vscode.workspace.findFiles('**/*.lys', '**/node_modules/**', 2000)) {
        if (!charts.has(u.fsPath) && !samePath(u.fsPath, chart)) {
            const t = await readText(u);
            if (t !== undefined && sameText(t, text)) { charts.add(u.fsPath); }
        }
    }
    const sources: CopySource[] = [];
    const texts = new Map<string, string>();
    for (const c of charts) {
        const f = recordingPathFor(c);
        const t = await readText(vscode.Uri.file(f));
        const r = t !== undefined ? parseRecording(t) : undefined;
        if (t !== undefined && r && !('error' in r)) {
            sources.push({ chart: c, recordingFile: f, audio: resolveAudio(f, r.audio) });
            texts.set(f, t);
        }
    }
    let source = sourceForCopy(chart, sources);
    if (source === 'ambiguous') {
        const pick = await vscode.window.showQuickPick(
            [...sources.map(s => ({ label: path.basename(s.chart), description: s.chart, detail: `Recording: ${s.audio}`, source: s })),
                { label: 'No recording', description: '', detail: 'Leave the copy without one', source: undefined }],
            { title: `Lily#: ${path.basename(chart)} is a copy of more than one chart with a recording — which recording goes with it?` });
        source = pick?.source;
    }
    if (!source) {
        return;
    }
    await vscode.workspace.fs.writeFile(vscode.Uri.file(beside),
        Buffer.from(copiedRecordingText(texts.get(source.recordingFile)!, source.recordingFile, beside), 'utf8'));
    void vscode.window.showInformationMessage(`Lily#: the recording came along — ${path.basename(beside)} written beside ${path.basename(chart)} (from ${source.recordingFile}).`);
}

async function recordingFileCopied(file: string): Promise<void> {
    const text = await readText(vscode.Uri.file(file));
    if (text === undefined) {
        return;
    }
    // The file it was copied from: the same text, beside a chart.
    let from: string | undefined;
    for (const u of await vscode.workspace.findFiles('**/*.dettato.json', '**/node_modules/**', 2000)) {
        if (samePath(u.fsPath, file)) { continue; }
        const t = await readText(u);
        const chart = chartPathFor(u.fsPath);
        if (t !== undefined && t === text && chart && await exists(vscode.Uri.file(chart))) {
            if (path.basename(u.fsPath).toLowerCase() === path.basename(file).toLowerCase() || from === undefined) { from = u.fsPath; }
        }
    }
    if (!from) {
        return;
    }
    // Its way to the recording, from the new place.
    const moved = copiedRecordingText(text, from, file);
    if (moved !== text) {
        await vscode.workspace.fs.writeFile(vscode.Uri.file(file), Buffer.from(moved, 'utf8'));
    }
    // And its chart, unless one is there already (copied together).
    const chart = chartPathFor(file)!, fromChart = chartPathFor(from)!;
    if (!(await exists(vscode.Uri.file(chart)))) {
        await vscode.workspace.fs.copy(vscode.Uri.file(fromChart), vscode.Uri.file(chart), { overwrite: false });
        void vscode.window.showInformationMessage(`Lily#: the chart came along — ${path.basename(chart)} copied beside ${path.basename(file)} (from ${fromChart}).`);
    }
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
            // A recording may be named from anywhere (audio leads to it, Dettato's request 6): every
            // file beside a chart in the workspace may name it.
            const isRecording = [...WEBVIEW_AUDIO, ...WINDOWS_AUDIO].includes(path.extname(oldUri.fsPath).slice(1).toLowerCase());
            if (isRecording) {
                try {
                    for (const u of await vscode.workspace.findFiles('**/*.dettato.json', '**/node_modules/**', 2000)) {
                        if (!texts.has(u.fsPath)) {
                            inFolder.push(u.fsPath);
                            texts.set(u.fsPath, await readText(u));
                        }
                    }
                } catch {
                    // (no workspace: the folder's files only)
                }
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
                        // Into another folder: the way to the recording is written from there.
                        const r = w.audio !== undefined ? parseRecording(texts.get(w.from) ?? '') : undefined;
                        if (r && !('error' in r)) {
                            r.audio = w.audio!;
                            await vscode.workspace.fs.writeFile(vscode.Uri.file(w.to), Buffer.from(serializeRecording(r), 'utf8'));
                        }
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
