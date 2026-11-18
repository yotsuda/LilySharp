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

// The preview's Export — the half that talks to VS Code: the save dialog whose file type is the
// format, the request to the server, and for a WAV, MP3 or MP4 the jobs as Play sounds them
// (the practice menu's tempo and parts, the sung voices, the recording beside the chart), then
// the file opened or shown in its folder. The Explorer's batch export (exportBatch.ts) is
// handed the same jobs' makers. It stood in extension.ts until session 1297; what it reads of
// the extension's own state comes through ExportDeps.

import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import * as cp from 'child_process';
import * as os from 'os';
import type { LanguageClient } from 'vscode-languageclient/node';
import { AudioFormat, AudioJob, renderAudioFiles } from './audioRender';
import { PREVIEW_MASTER, VoiceTrack, WAV_SAMPLE_RATE, decodeWav } from './audioRenderCore';
import { recordedExportPlan } from './recordingExportCore';
import { PracticeState, VOICE_LEAD_S, levelOf, practiceSummary, soundStart } from './webview/playbackCore';
import { DrawFrames, FramesAnswer, ffmpegRuns, writeVideos } from './videoExport';
import { ffmpegMissing, ffmpegProgram } from './videoExportCore';
import { renderer, tellUnsingable } from './singing';
import { wavSoundFont } from './soundFont';
import { leftToTheInstrument, SungVoiceDto, sungKey, unsingableReason } from './singerCore';
import { engineSaid, singerFailureText } from './singerCore';

export interface ExportDeps {
    getClient: () => LanguageClient | undefined;
    isReady: () => boolean;
    /** The extension's output channel. */
    outputChannel: vscode.OutputChannel;
    /** The open previews, by their document's uri. */
    previewPanels: ReadonlyMap<string, vscode.WebviewPanel>;
    /** What the practice menu of the preview showing `uri` is set to, if one shows it. */
    practiceFor: (uri: string) => PracticeState | undefined;
}

let outputChannel: vscode.OutputChannel;
let previewPanels: ExportDeps['previewPanels'];
let practiceFor: ExportDeps['practiceFor'];
/** The server's client once it is up, else undefined. */
let server: () => LanguageClient | undefined = () => undefined;
/** The server's client as soon as it is made, up or not. */
let startedClient: () => LanguageClient | undefined = () => undefined;

/** Called once from activate, before anything here can be asked for. */
export function useExportHost(deps: ExportDeps): void {
    ({ outputChannel, previewPanels, practiceFor } = deps);
    startedClient = deps.getClient;
    server = () => { const client = deps.getClient(); return client && deps.isReady() ? client : undefined; };
}

interface ExportResponse {
    Success: boolean;
    OutputPath: string | null;
    Error: string | null;
    /** A WAV, MP3 or MP4 asked for with playbackOnly: the file and the notes (and an MP4's
     *  pictures), for the editor to render. */
    Playbacks?: AudioJob[] | null;
}

// Opening/revealing an exported file goes to the OS directly rather than through
// vscode.env.openExternal / the revealFileInOS command, because both of those
// stringify the URI on the way out: openExternal hands the shell
// `uri.toString()`, so 日本語.pdf arrives as %E6%97%A5%E6%9C%AC%E8%AA%9E.pdf and
// nothing opens (microsoft/vscode#83610), and revealFileInOS reveals whichever
// editor has focus when it is driven from code instead of the explorer
// (microsoft/vscode#105666). Spawning the platform's opener passes the path as an
// argv entry, which no encoding step can touch. Note `cmd /c start` is NOT an
// option here — cmd's OEM code page mangles non-ASCII names by itself.
// `verbatim` hands Windows the command line EXACTLY as written instead of letting
// Node quote each argument. Needed by one caller only — see revealInFileManager.
function spawnOpener(cmd: string, args: string[], verbatim = false) {
    try {
        const child = cp.spawn(cmd, args, {
            detached: true,
            stdio: 'ignore',
            windowsVerbatimArguments: verbatim,
        });
        // explorer.exe exits 1 even when it succeeded, so only a spawn failure
        // (missing binary) is worth reporting.
        child.on('error', err => outputChannel.appendLine(
            `open failed: ${cmd} ${args.join(' ')} — ${err}`));
        child.unref();
    } catch (err) {
        outputChannel.appendLine(`open failed: ${cmd} ${args.join(' ')} — ${err}`);
    }
}

export function openInDefaultApp(file: string) {
    if (process.platform === 'win32') {
        spawnOpener('explorer.exe', [file]);
    } else if (process.platform === 'darwin') {
        spawnOpener('open', [file]);
    } else {
        spawnOpener('xdg-open', [file]);
    }
}

function revealInFileManager(file: string) {
    // A file that was never written under the dialog's name (multi-page PNG
    // export splits into BASE-page1.png, BASE-page2.png, …) still has a folder
    // worth showing; selecting a missing path just lands Explorer on Documents.
    const exists = fs.existsSync(file);
    const dir = path.dirname(file);
    if (process.platform === 'win32') {
        // `/select,` and the path are ONE argument — a space between them makes
        // Explorer ignore the path and open the user's Documents folder.
        // ⚠️ AND SO DOES NODE'S ARGUMENT QUOTING, which is the bug this spelling
        // exists to avoid. explorer.exe does not use the C runtime's argv parser: it
        // reads the raw command line. Node quotes any argument containing a space, so
        // a path like `C:\My Scores\a.pdf` was handed over as
        //     explorer.exe "/select,C:\My Scores\a.pdf"
        // with the switch INSIDE the quotes — Explorer does not recognise that as
        // /select at all and falls back to the user's Documents folder, which is
        // exactly the "sometimes" in the report: paths without a space worked, paths
        // with one did not. The form Explorer wants keeps the switch bare and quotes
        // only the path, so the command line has to be written by hand:
        //     explorer.exe /select,"C:\My Scores\a.pdf"
        // MEASURED both ways on 2026-08-28 by reading back what opened, through
        // Shell.Application's window list rather than by eye: the quoted-whole form
        // landed on Documents, this one on the file's own folder, and a Japanese path
        // with a space (`日本語 フォルダ\楽譜 テスト.txt`) also arrives intact — the
        // non-ASCII half of this function's contract still holds, since a verbatim
        // command line is still UTF-16 all the way to CreateProcessW.
        // ⚠️ A Windows filename cannot contain `"`, so wrapping the path in quotes
        // here cannot be broken by the path itself, and nothing goes through a shell.
        // ⚠️ THE FALLBACK BRANCH STAYS ORDINARY: a bare folder path is a normal
        // argument to Explorer and Node's quoting is exactly right for it (measured
        // too — `explorer.exe "C:\with space"` opens that folder).
        if (exists) {
            spawnOpener('explorer.exe', [`/select,"${file}"`], true);
        } else {
            spawnOpener('explorer.exe', [dir]);
        }
    } else if (process.platform === 'darwin') {
        spawnOpener('open', exists ? ['-R', file] : [dir]);
    } else {
        // No portable "select this file" on Linux; the folder is the best we do.
        spawnOpener('xdg-open', [dir]);
    }
}

// Export the currently-previewed score: open the save dialog straight away and let
// its "Save as type" dropdown choose the format; the saved file's extension decides
// what the language server generates. Every format honours the selected score:
// SVG/PNG/PDF draw it; MIDI, MusicXML and the LilyPond source write ITS form (and MIDI
// its parts), as `lysc <format> --score NAME` does.
export async function exportPreview(
    context: vscode.ExtensionContext,
    uri: string,
    renderName: string | undefined
) {
    const client = server();
    outputChannel.appendLine('exportPreview: opening save dialog (clientReady=' + !!client + ')');
    if (!client) {
        // Typical cause: the language server was swapped/killed and the client
        // gave up restarting — a window reload brings both back.
        vscode.window.showErrorMessage(
            'Lily#: language server not running — reload the window (Developer: Reload Window).');
        return;
    }

    // Default filename (matches `lysc svg`): the unnamed score writes the
    // source file's basename; every other score appends its name to it —
    // song.lys + `score sub` → song-sub.
    const docUri = vscode.Uri.parse(uri);
    // An unsaved (untitled:) score has no folder on disk, so its parent URI keeps
    // the untitled scheme and showSaveDialog cannot anchor to it — the simple file
    // dialog then falls back to browsing the home directory (".. / agents / …")
    // instead of offering a save target. Anchor an untitled export at a real folder
    // (the workspace root, else the user's home) and take the name from the
    // untitled label; a saved score keeps opening the dialog in its own folder.
    const onDisk = docUri.scheme === 'file';
    const rawName = (onDisk ? docUri.path.split('/').pop() : docUri.path) || 'score';
    const sourceName = rawName.replace(/\.lys$/i, '');
    const baseName = renderName && renderName.length > 0
        ? `${sourceName}-${renderName}`
        : sourceName;
    const baseDir = onDisk
        ? vscode.Uri.joinPath(docUri, '..')
        : (vscode.workspace.workspaceFolders?.[0]?.uri ?? vscode.Uri.file(os.homedir()));

    // Default name WITHOUT an extension: the dialog's "Save as type"
    // dropdown appends the chosen one. Baking .pdf into the name meant the
    // typed name won over the selected type — picking PNG still saved a PDF.
    const target = await vscode.window.showSaveDialog({
        defaultUri: vscode.Uri.joinPath(baseDir, baseName),
        // Ordered by kind: rendered images, then the score as source/interchange,
        // then audio/performance.
        filters: {
            'PDF document': ['pdf'],
            'SVG image': ['svg'],
            'PNG image': ['png'],
            'LilyPond source': ['ly'],
            'MusicXML': ['xml', 'musicxml'],
            'MIDI': ['mid', 'midi'],
            'WAV audio (as the preview plays it)': ['wav'],
            'MP3 audio (as the preview plays it)': ['mp3'],
            'MP4 video (the pages, each note lit as it sounds)': ['mp4'],
            'VOCALOID sequence (vocal + lyrics)': ['vsqx'],
        }
    });
    if (!target) {
        return;
    }

    // The chosen file's extension is the format.
    const ext = (target.fsPath.split('.').pop() || '').toLowerCase();
    const format = ext === 'mid' || ext === 'midi' ? 'midi'
        : ext === 'xml' || ext === 'musicxml' ? 'musicxml'
        : ext; // png / pdf / svg / ly
    if (!['png', 'pdf', 'svg', 'midi', 'musicxml', 'vsqx', 'ly', 'wav', 'mp3', 'mp4'].includes(format)) {
        vscode.window.showErrorMessage(`Lily#: unsupported export type ".${ext}".`);
        return;
    }

    // The preview always shows the @todo marks; a picture asks whether to draw them (owner's
    // decision 2026-10-25, in place of the preview's setting) — only when the file has any.
    let todoMarks = false;
    const text = vscode.workspace.textDocuments.find(d => d.uri.toString() === uri)?.getText() ?? '';
    if (['png', 'pdf', 'svg'].includes(format) && /@todo\b/.test(text)) {
        const without = 'Without the @todo marks', withMarks = 'With the @todo marks';
        const pick = await vscode.window.showQuickPick([
            { label: without, description: 'the finished page' },
            { label: withMarks, description: 'red rings and memos, as the preview shows them' },
        ], { title: 'Lily#: this score has @todo marks', placeHolder: 'Draw them in the export?' });
        if (!pick) {
            return;
        }
        todoMarks = pick.label === withMarks;
    }

    // A WAV or an MP3 is the score as the preview plays it: the server gives the notes
    // (playbackOnly) and the editor renders them through the preview's synth and sound font
    // (audioRender.ts).
    // An MP4 is the same sound with the score's pages, the notes lit as they sound: the server
    // draws the pictures, and the editor joins them to the sound with the user's ffmpeg
    // (videoExport.ts). All three play under the preview's practice menu, the loop aside — its
    // tempo, parts, metronome and count-in (owner's request 2026-11-07; exportNotes).
    const practice = practiceFor(uri);
    const audio: AudioFormat | undefined = format === 'wav' || format === 'mp3' ? format : undefined;
    const video = format === 'mp4';
    let ffmpeg: string | undefined;
    if (video) {
        ffmpeg = await videoFfmpeg();
        if (!ffmpeg) {
            return;
        }
    }
    let soundFont: string | undefined;
    if (audio || video) {
        soundFont = await wavSoundFont(context);
        if (!soundFont) {
            return;
        }
    }

    try {
        const request = async () => {
            const answer = await client!.sendRequest<ExportResponse>('lilysharp/export', {
                textDocument: { uri },
                format,
                outputPath: target.fsPath,
                todoMarks,
                playbackOnly: audio !== undefined || video,
                renderName: renderName || null
            });
            const jobs = answer.Success && answer.Playbacks
                ? await recordJobs(await singJobs(practisedJobs(answer.Playbacks, practice), { textDocument: { uri } }, practice), uri) : null;
            if (jobs && audio && soundFont) {
                await renderAudioFiles(jobs, soundFont, audio);
            } else if (jobs && video && soundFont && ffmpeg) {
                await writeVideos(jobs, soundFont, ffmpeg, drawFramesFor({ textDocument: { uri } }));
            }
            return answer;
        };
        // Rendering the sound takes as long as the synth needs for the whole piece (and a video
        // its pictures and ffmpeg); the pictures and files are quick enough to need no notice.
        const response = soundFont
            ? await vscode.window.withProgress({
                location: vscode.ProgressLocation.Notification,
                title: video
                    ? `Lily#: drawing the video and rendering its sound through ${path.basename(soundFont)}…`
                    : `Lily#: rendering the ${(audio ?? 'wav').toUpperCase()} through ${path.basename(soundFont)}…`,
            }, request)
            : await request();

        if (response.Success) {
            const openAction = 'Open File';
            const revealAction = 'Reveal in Explorer';
            const practised = soundFont ? practiceSummary(practice) : '';
            const through = soundFont ? ` (played through ${path.basename(soundFont)}${practised ? ', ' + practised : ''})` : '';
            const choice = await vscode.window.showInformationMessage(
                `Exported to ${response.OutputPath}${through}`, openAction, revealAction);
            if (choice === openAction) {
                if (fs.existsSync(target.fsPath)) {
                    openInDefaultApp(target.fsPath);
                } else {
                    // Multi-page PNG never writes the single name the dialog
                    // collected, so there is nothing to hand the default app —
                    // show the folder holding the pages instead.
                    revealInFileManager(target.fsPath);
                }
            } else if (choice === revealAction) {
                revealInFileManager(target.fsPath);
            }
        } else {
            vscode.window.showErrorMessage(`Lily# export failed: ${response.Error}`);
        }
    } catch (err) {
        vscode.window.showErrorMessage(`Lily# export failed: ${err}`);
    }
}

/** The jobs of a WAV, MP3 or MP4 export with their notes as Play sounds them under `practice`
 *  (exportNotes; as written when no preview holds a menu for the file). */
export function practisedJobs(jobs: readonly AudioJob[], practice: PracticeState | undefined): AudioJob[] {
    return jobs.map(job => {
        // With the recording the page sounds with the score: the notes on its seconds, and how it
        // plays (docs/recording-design.md §7); recordJobs renders it once the voices are in.
        const plan = recordedExportPlan(job.Notes, job.Parts ?? null, job.Beats ?? null, job.BarEnd, practice, job.Extra ?? null);
        if (practice?.recording && !plan.recording) {
            outputChannel.appendLine(`recording: export: its bars do not line up with ${path.basename(job.OutputPath)}'s; written without it`
                + ` (the score ends at ${job.BarEnd ?? '?'} s, ${job.Beats?.length ?? 0} beats; the recording places ${practice.recording.bars.length} bars)`);
        }
        return { ...job, Notes: plan.notes, Lead: plan.lead, RecordingPlay: plan.recording };
    });
}

/** The renders of the recording a page was asked for (renderRecording), by request id: the
 *  pieces as they arrive, and what to call once the last has. */
const recordingRenders = new Map<number, { left: Float32Array[]; right: Float32Array[]; done: (r: { left: Float32Array; right: Float32Array; sampleRate: number } | { error: string }) => void }>();
let recordingRenderId = 0;

/** A piece of a recording render from a page (recordingRendered). */
export function recordingRendered(message: { id: number; error?: string; index?: number; pieces?: number; length?: number; sampleRate?: number; left?: unknown; right?: unknown }): void {
    const render = recordingRenders.get(message.id);
    if (!render) { return; }
    if (message.error) {
        recordingRenders.delete(message.id);
        render.done({ error: message.error });
        return;
    }
    render.left[message.index ?? 0] = floats(message.left);
    render.right[message.index ?? 0] = floats(message.right);
    const pieces = message.pieces ?? 1;
    if (render.left.filter(p => p).length < pieces) { return; }
    recordingRenders.delete(message.id);
    const join = (parts: Float32Array[]) => {
        const all = new Float32Array(message.length ?? parts.reduce((n, p) => n + p.length, 0));
        let at = 0;
        for (const p of parts) { all.set(p.subarray(0, Math.max(0, all.length - at)), at); at += p.length; }
        return all;
    };
    render.done({ left: join(render.left), right: join(render.right), sampleRate: message.sampleRate ?? WAV_SAMPLE_RATE });

    // A typed array as the webview sent it: kept as one, or (an older host) an object of indices.
    function floats(v: unknown): Float32Array {
        if (v instanceof Float32Array) { return v; }
        if (ArrayBuffer.isView(v)) { return new Float32Array(v.buffer.slice(v.byteOffset, v.byteOffset + v.byteLength)); }
        if (v && typeof v === 'object') { return Float32Array.from(Object.values(v as Record<string, number>)); }
        return new Float32Array(0);
    }
}

/**
 * The recording beside the chart, mixed into each job that plays with it (RecordingPlay): the
 * preview showing `uri` renders it whole with its own player, as it sounds with the score
 * (recordingPlayer.render), from the count-in's lead on (docs/recording-design.md §7). A page that
 * cannot (closed meanwhile, the recording gone): the file without it, said in the output channel.
 */
export async function recordJobs(jobs: readonly AudioJob[], uri: string | undefined): Promise<AudioJob[]> {
    const panel = uri ? previewPanels.get(uri) : undefined;
    const out: AudioJob[] = [];
    for (const job of jobs) {
        const play = job.RecordingPlay;
        if (!play) { out.push(job); continue; }
        if (!panel) {
            outputChannel.appendLine(`recording: export: no preview shows ${uri ?? 'the file'}; ${path.basename(job.OutputPath)} is written without the recording`);
            out.push(job);
            continue;
        }
        const id = ++recordingRenderId;
        const rendered = await new Promise<{ left: Float32Array; right: Float32Array; sampleRate: number } | { error: string }>(resolve => {
            recordingRenders.set(id, { left: [], right: [], done: resolve });
            void panel.webview.postMessage({ type: 'renderRecording', id, tempo: play.tempo });
            // A page that never answers (closed while rendering): give up after a long while.
            setTimeout(() => { if (recordingRenders.delete(id)) { resolve({ error: 'the preview did not answer' }); } }, 10 * 60 * 1000);
        });
        if ('error' in rendered) {
            outputChannel.appendLine(`recording: export: ${path.basename(job.OutputPath)} is written without the recording: ${rendered.error}`);
            out.push(job);
            continue;
        }
        outputChannel.appendLine(`recording: export: ${path.basename(job.OutputPath)} with the recording (${(rendered.left.length / rendered.sampleRate).toFixed(1)} s, tempo ×${play.tempo.toFixed(3)})`);
        out.push({ ...job, Recording: {
            left: rendered.left, right: rendered.right, sampleRate: rendered.sampleRate,
            at: job.Lead ?? 0, gain: play.level / PREVIEW_MASTER,
        } });
    }
    return out;
}

/**
 * The sung voices of each export (docs/singing-design.md §5): for every part the job's score
 * sings — not muted, its ♪ on — the engine's WAV at the practice speed, to be mixed in from the
 * count-in's lead; where the score does not also play the part, its notes leave the
 * instruments' list (a video still lights them: `Lit`). No engine, or a voice that fails: the
 * instruments as before, the failure in the output channel.
 */
export async function singJobs(jobs: readonly AudioJob[], source: { textDocument?: { uri: string }; path?: string },
    practice: PracticeState | undefined): Promise<AudioJob[]> {
    const engine = await renderer();
    const client = server();
    if (!engine || !client) { return [...jobs]; }
    const speed = practice?.speed ?? 1;
    // The part's button is the instrument's (exportPlan drops a muted part's notes); the voice is
    // its own toggle's (session 1164) - so a muted part with its voice on is sung alone.
    const silentVoices = new Set(practice?.voicesOff ?? []);
    const out: AudioJob[] = [];
    for (const job of jobs) {
        let sung: { Voices?: SungVoiceDto[] | null; PrintedWords?: string[] | null; Error?: string | null };
        try {
            // The open document, or a file on the disk the folder export names (as lilysharp/export).
            sung = await client.sendRequest('lilysharp/sung', { ...source, renderName: job.Score || null, allLyrics: true });
        } catch (e) {
            outputChannel.appendLine(`singer: export: lilysharp/sung failed: ${String(e)}`);
            out.push(job);
            continue;
        }
        const sungParts = new Set(job.Sung ?? []), playedParts = new Set(job.Played ?? []);
        const voicesOn = new Set(practice?.voicesOn ?? []);
        const voices: VoiceTrack[] = [];
        const silenced = new Set<number>();
        const allOffTracks = new Set<number>();     // silenced because the menu switched every voice off: their unsung notes too
        // A line nobody installed can sing is not asked of the engine: it is said, and its part's
        // instrument stays in the mix (owner's decision 2026-11-13).
        const unsingable = await engine.unsingable((sung.Voices ?? []).filter(v => v.Notes.length > 0));
        const notSung = new Set<string>();      // the voices asked to sing that did not: nobody can, or the engine failed
        for (const voice of sung.Voices ?? []) {
            // The menu's fader for the voice (owner's request 2026-11-09): its gain in the mix; at 0 it is as switched off.
            const key = sungKey(voice);
            const gain = levelOf(practice?.voiceLevels, key);
            // The score's own voice unless the menu switched it off; one the score does not sing
            // only when the menu turned it on (voicesOn, session 1163).
            const sings = voice.InScore !== false ? sungParts.has(voice.Part) && !silentVoices.has(key) : voicesOn.has(key) && !silentVoices.has(key);
            // A sung-only part with all its voices switched off and its instrument not turned on: silent, as in Play.
            const allOff = (sung.Voices ?? []).filter(v => v.Part === voice.Part).every(v => silentVoices.has(sungKey(v)));
            if (allOff && sungParts.has(voice.Part) && !playedParts.has(voice.Part) && !(practice?.instrumentsOn ?? []).includes(voice.Part)) {
                const off = job.Parts?.indexOf(voice.Part) ?? -1;
                if (off >= 0) { silenced.add(off); allOffTracks.add(off); }
            }
            if (voice.Notes.length === 0 || !sings || gain <= 0) { continue; }
            const nobody = unsingable.get(key);
            if (nobody !== undefined) { tellUnsingable(key, nobody); notSung.add(key); continue; }
            try {
                const { rendered } = await engine.render(voice, speed);
                const v = await rendered;
                // The WAV begins VOICE_LEAD_S before the score's 0 (the first consonant's room).
                voices.push({ wav: v.wav, at: (job.Lead ?? 0) - VOICE_LEAD_S, duration: v.duration, gain });
                // A part the score only sings keeps its instrument out of the mix unless the menu
                // turned it on (instrumentsOn; session 1165) - a part it plays, or the menu's own
                // voice of a played part, sounds both ways.
                const track = job.Parts?.indexOf(voice.Part) ?? -1;
                const sungOnly = sungParts.has(voice.Part) && !playedParts.has(voice.Part);
                if (track >= 0 && sungOnly && !(practice?.instrumentsOn ?? []).includes(voice.Part)) { silenced.add(track); }
            } catch (e) {
                const refused = unsingableReason(e);
                if (refused) { tellUnsingable(key, refused); }
                else { outputChannel.appendLine(`singer: export: ${singerFailureText(key, engineSaid(e), voice.WarningPlace)}`); }
                notSung.add(key);
            }
        }
        // A consonant before the beat (in the lead) that would fall before the file's start: the
        // whole file — notes, lit notes, the count-in's lead, the voices — moves later by what the
        // earliest such sound needs, so it is heard (docs/singing-design.md §3). Silence in the
        // lead is not waited for.
        let pre = 0;
        for (const v of voices) {
            try {
                const d = decodeWav(fs.readFileSync(v.wav));
                const s = soundStart(d.samples, d.sampleRate, VOICE_LEAD_S);
                if (s !== null) { pre = Math.max(pre, -(v.at + s)); }
            } catch { /* a WAV the render will refuse too */ }
        }
        pre = Math.max(0, pre);
        const later = <T extends { T: number }>(list: T[]): T[] => pre > 0 ? list.map(n => ({ ...n, T: n.T + pre })) : list;
        // A note in a section none of the part's voices sings (U) is the instrument's: it stays.
        // So is one in a section whose every singer was not sung here, while another line of its part was.
        const written = leftToTheInstrument(job.Notes, notSung);
        const notes = silenced.size > 0 ? written.filter(n => !(typeof n.N === 'number' && silenced.has(n.N) && !(n.U && !allOffTracks.has(n.N)))) : job.Notes;
        out.push({
            ...job, Voices: voices.map(v => ({ ...v, at: v.at + pre })), Lit: later(job.Notes),
            Notes: later(notes), Lead: (job.Lead ?? 0) + pre,
        });
    }
    return out;
}

/** Asks the server for a video's pictures (lilysharp/videoFrames), the file named as the export
 *  named it — the open document, or a path on the disk. */
export function drawFramesFor(source: { textDocument?: { uri: string }; path?: string }): DrawFrames {
    return (job, seconds) => {
        const client = startedClient();
        if (!client) { return Promise.resolve({ Error: 'language server not running' }); }
        return client.sendRequest<FramesAnswer>('lilysharp/videoFrames', {
            ...source, score: job.Score === undefined ? null : job.Score, notes: job.Lit ?? job.Notes, seconds });
    };
}

/** The ffmpeg an MP4 export runs (lilysharp.export.ffmpegPath, else `ffmpeg` on the PATH), once
 *  it is known to run; else the writer is told how to get it, and undefined. Asked before the
 *  server draws anything. */
export async function videoFfmpeg(): Promise<string | undefined> {
    const program = ffmpegProgram(vscode.workspace.getConfiguration('lilysharp').get<string>('export.ffmpegPath', ''));
    if (await ffmpegRuns(program)) {
        return program;
    }
    const open = 'Open Setting';
    if (await vscode.window.showErrorMessage(ffmpegMissing(program), open) === open) {
        await vscode.commands.executeCommand('workbench.action.openSettings', 'lilysharp.export.ffmpegPath');
    }
    return undefined;
}
