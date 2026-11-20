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

// A .lys's sound one stem at a time, for another extension to mix (docs/stems-api-design.md; owner's
// decisions 2026-11-20: Shosa refers to the .lys and mixes its sound with a mixer of its own). The
// extension's exports carry renderStems, onDidChangeStems and writeAudio.
//
// The stems are what the WAV export sounds, taken apart: the server's notes as written
// (lilysharp/export, playbackOnly), the voices the engine sings for them (singJobs), each part through
// the preview's sound font on its own (renderPartSound), and the recording beside the chart as its own
// files. With a recording whose bars are placed, the parts and the voices are moved onto its seconds
// (practisedJobs and singJobs with the recording in the practice state, as an export with the
// recording does), and the recording is untouched.

import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import type { LanguageClient } from 'vscode-languageclient/node';
import { AudioFormat, AudioJob, renderPartSound, writeMixFile } from './audioRender';
import { WAV_SAMPLE_RATE } from './audioRenderCore';
import { practisedJobs, singJobs } from './exportPreview';
import { DecodeResult, decodedRecording, fileStamp } from './recording';
import { RECORDING_DEFAULTS, audioReader, parseRecording, recordingPathFor, resolveAudio, timeWarp } from './recordingCore';
import { PREPARE_IDLE_MS } from './singerCore';
import { renderer } from './singing';
import { wavSoundFont } from './soundFont';
import { STEMS_VERSION, Stem, Stems, floatWav, mixProblem, notesByPart, stemFileName } from './stemsCore';
import { PracticeState } from './webview/playbackCore';

export interface StemsDeps {
    context: vscode.ExtensionContext;
    getClient: () => LanguageClient | undefined;
    /** Starts the language server if it is not, and resolves when it answers. */
    whenReady: () => Promise<void>;
    outputChannel: vscode.OutputChannel;
    decode: (input: string, output: string) => Promise<DecodeResult>;
}

export interface StemsOptions {
    /** Which score (`score NAME`); the first when absent, as the preview. */
    score?: string;
    /** Leave the recording beside the chart out. */
    withoutRecording?: boolean;
    signal?: AbortSignal;
    /** A stem made: how many of how many. */
    onProgress?(p: { stem: string; done: number; total: number }): void;
}

/** What the extension's exports carry for the stems (docs/stems-api-design.md). */
export interface StemsApi {
    stemsVersion: number;
    renderStems(lys: vscode.Uri, options?: StemsOptions): Promise<Stems>;
    onDidChangeStems: vscode.Event<vscode.Uri>;
    writeAudio(mix: { left: Float32Array; right: Float32Array; sampleRate: number }, options: { format: AudioFormat; path: string }): Promise<void>;
}

const isWindows = process.platform === 'win32';

/** The exports' stems, and what keeps onDidChangeStems told; the listeners go with the context. */
export function stemsApi(deps: StemsDeps): StemsApi {
    const changed = new vscode.EventEmitter<vscode.Uri>();
    deps.context.subscriptions.push(changed);
    // A .lys's sound may change when its text rests after an edit (as the voices wait for it,
    // PREPARE_IDLE_MS), and when the file beside it changes (a bar line moved, the recording).
    const timers = new Map<string, NodeJS.Timeout>();
    const soon = (uri: vscode.Uri) => {
        const key = uri.toString();
        const t = timers.get(key);
        if (t) { clearTimeout(t); }
        timers.set(key, setTimeout(() => { timers.delete(key); changed.fire(uri); }, PREPARE_IDLE_MS));
    };
    deps.context.subscriptions.push(
        vscode.workspace.onDidChangeTextDocument(e => { if (e.document.languageId === 'lilysharp' && e.contentChanges.length > 0) { soon(e.document.uri); } }),
        vscode.workspace.onDidSaveTextDocument(d => { if (d.languageId === 'lilysharp') { soon(d.uri); } }),
        { dispose: () => { for (const t of timers.values()) { clearTimeout(t); } } });
    const watcher = vscode.workspace.createFileSystemWatcher('**/*.orecchiante.json');
    const beside = (u: vscode.Uri) => soon(vscode.Uri.file(u.fsPath.replace(/\.orecchiante\.json$/i, '.lys')));
    watcher.onDidChange(beside); watcher.onDidCreate(beside); watcher.onDidDelete(beside);
    deps.context.subscriptions.push(watcher);
    return {
        stemsVersion: STEMS_VERSION,
        renderStems: (lys, options) => renderStems(deps, lys, options ?? {}),
        onDidChangeStems: changed.event,
        writeAudio: async (mix, options) => {
            const problem = mixProblem(mix ?? {}, options?.format);
            if (problem) { throw new Error(`Lily# writeAudio: ${problem}`); }
            if (!options.path) { throw new Error('Lily# writeAudio: no path'); }
            await writeMixFile(mix.left, mix.right, mix.sampleRate, options.format, options.path);
        },
    };
}

/** The response of lilysharp/export with playbackOnly (only what the stems read). */
interface PlaybacksAnswer { Success: boolean; Error: string | null; Playbacks?: AudioJob[] | null }

async function renderStems(deps: StemsDeps, lys: vscode.Uri, options: StemsOptions): Promise<Stems> {
    const log = (what: string) => deps.outputChannel.appendLine(`stems: ${path.basename(lys.fsPath)}: ${what}`);
    const stopped = () => { if (options.signal?.aborted) { throw new Error('Lily# renderStems: aborted'); } };
    await deps.whenReady();
    const client = deps.getClient();
    if (!client) { throw new Error('Lily# renderStems: the language server is not running'); }
    // The open document as it stands (unsaved edits sound too), else the file on the disk.
    const open = vscode.workspace.textDocuments.find(d => d.uri.toString() === lys.toString());
    const source: { textDocument?: { uri: string }; path?: string } = open ? { textDocument: { uri: lys.toString() } } : { path: lys.fsPath };
    const storage = vscode.Uri.joinPath(deps.context.globalStorageUri, 'stems');
    await vscode.workspace.fs.createDirectory(storage);
    const answer = await client.sendRequest<PlaybacksAnswer>('lilysharp/export', {
        ...source, format: 'wav', outputPath: path.join(storage.fsPath, 'score.wav'), playbackOnly: true, renderName: options.score ?? null,
    });
    const job = answer.Success ? answer.Playbacks?.[0] : undefined;
    if (!job) { throw new Error(`Lily# renderStems: ${answer.Error ?? 'the score gave no sound'}`); }
    stopped();
    const notes: string[] = [];
    // The recording beside the chart, and the score's bars lined up with its own.
    const recording = options.withoutRecording || lys.scheme !== 'file' ? undefined : await recordingOf(lys, deps, notes);
    stopped();
    const warp = recording && recording.bars.length > 0 ? timeWarp(job.Beats ?? null, job.BarEnd ?? null, { bars: recording.bars }) : null;
    if (recording && !warp) {
        notes.push(recording.bars.length === 0
            ? `${recording.name}: no bars are placed in it yet — the stems are on the score's seconds, without the recording`
            : `${recording.name}: its bars do not line up with the score's — the stems are on the score's seconds, without the recording`);
    }
    // As the WAV export with the recording does: a practice state that sounds the score as written
    // and the recording with it (speed 1, no count-in), so the notes and the voices go onto its seconds.
    const practice: PracticeState | undefined = warp && recording
        ? { speed: 1, muted: [], click: false, countIn: false, recording: { bars: recording.bars, sound: { ...RECORDING_DEFAULTS }, level: 1 } }
        : undefined;
    const [sung] = await singJobs(practisedJobs([job], practice), source, practice);
    stopped();
    // singJobs moves the whole file later when a first consonant would come before its start (Lead);
    // a stem says where it starts instead, so the timeline's 0 stays the score's or the recording's.
    const lead = sung.Lead ?? 0;
    const soundFont = await wavSoundFont(deps.context);
    if (!soundFont) { throw new Error('Lily# renderStems: no sound font to play the parts through'); }
    const fontStamp = (() => { try { const s = fs.statSync(soundFont); return `${soundFont}|${s.mtimeMs}|${s.size}`; } catch { return soundFont; } })();
    const parts = notesByPart(sung.Notes, sung.Parts);
    const voices = sung.Voices ?? [];
    const total = parts.length + voices.length + (warp && recording ? 1 + recording.parts.length : 0);
    let done = 0;
    const stems: Stem[] = [];
    const made = (stem: Stem) => { stems.push(stem); options.onProgress?.({ stem: stem.name, done: ++done, total }); };
    for (const part of parts) {
        stopped();
        const file = path.join(storage.fsPath, stemFileName(part.notes, fontStamp));
        if (!fs.existsSync(file)) {
            const [left, right] = await renderPartSound(part.notes, soundFont);
            fs.writeFileSync(file, floatWav(left, right, WAV_SAMPLE_RATE));
        }
        made({ kind: 'part', name: part.name, file, at: -lead });
    }
    for (const v of voices) {
        made({ kind: 'voice', name: v.name ?? '', file: v.wav, at: v.at - lead });
    }
    // A part the score sings with no voice made for it: its instrument stood in (it is a part stem).
    const unsung = (sung.Sung ?? []).filter(name => !voices.some(v => (v.name ?? '') === name || (v.name ?? '').startsWith(name + '#')));
    if (unsung.length > 0) {
        const why = (await renderer()) ? 'no voice could be sung for it (the output channel says why)' : 'no singing engine is installed (Cantoria)';
        for (const name of unsung) { notes.push(`${name}: ${why} — its instrument plays it`); }
    }
    if (warp && recording) {
        made({ kind: 'recording', name: recording.name, file: recording.file, at: 0 });
        for (const p of recording.parts) { made({ kind: 'recordingPart', name: p.name, file: p.file, at: 0 }); }
    }
    log(`${stems.length} stems on the ${warp ? 'recording' : 'score'}'s seconds${notes.length ? '; ' + notes.join('; ') : ''}`);
    return { timeline: warp && recording ? 'recording' : 'score', stems, notes };
}

/** The recording beside the chart as the stems hand it out: its file (an m4a as the WAV the server
 *  decoded, on Windows), its parts' files, and its bars; undefined with none, a reason in `notes`
 *  when it cannot be used. */
async function recordingOf(lys: vscode.Uri, deps: StemsDeps, notes: string[]): Promise<{ name: string; file: string; bars: { start: number; end: number }[]; parts: { name: string; file: string }[] } | undefined> {
    const beside = recordingPathFor(lys.fsPath);
    let text: string;
    try { text = fs.readFileSync(beside, 'utf8'); } catch { return undefined; }
    const rec = parseRecording(text);
    if ('error' in rec) { notes.push(`${path.basename(beside)}: ${rec.error}`); return undefined; }
    const audio = vscode.Uri.file(path.normalize(resolveAudio(beside, rec.audio)));
    const name = path.basename(audio.fsPath);
    const stamp = await fileStamp(audio);
    if (!stamp) { notes.push(`${name} cannot be found (${audio.fsPath})`); return undefined; }
    const how = audioReader(rec.audio, isWindows);
    if (typeof how === 'object') { notes.push(how.refused); return undefined; }
    let file = audio.fsPath;
    if (how === 'server') {
        const decoded = await decodedRecording(audio, stamp, vscode.Uri.joinPath(deps.context.globalStorageUri), deps.decode);
        if ('error' in decoded) { notes.push(`${name}: ${decoded.error}`); return undefined; }
        file = decoded.file.fsPath;
    }
    const parts: { name: string; file: string }[] = [];
    for (const p of rec.parts ?? []) {
        const f = path.normalize(resolveAudio(beside, p.file));
        if (!fs.existsSync(f)) { notes.push(`the recording's part ${p.name}: ${p.file} cannot be found`); continue; }
        if (audioReader(p.file, false) !== 'webview') { notes.push(`the recording's part ${p.name}: ${path.basename(f)} is not a wav, mp3, flac or ogg`); continue; }
        parts.push({ name: p.name, file: f });
    }
    return { name, file, bars: rec.bars.map(b => ({ start: b.start, end: b.end })), parts };
}
