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

// The recording beside a chart (docs/recording-design.md): the file `<stem>.dettato.json` next to
// `<stem>.lys` says which recording goes with the chart and where in it every bar the chart plays
// is. Dettato writes format 1 (C:\MyProj\Dettato\docs\SIDECAR.md); Lily# reads it and writes
// format 2 — the same file, a person's hand on it allowed (`byHand` on a bar it set or confirmed).
// The .lys itself never names the recording (owner's decision 2026-11-15).
//
// No vscode here: the extension and the tests share it.

/** One bar the chart plays, in the recording: its first bar line and the next, in seconds from
 *  the recording's start. */
export interface RecordingBar {
    start: number;
    end: number;
    /** A person set or confirmed this bar's start (format 2). */
    byHand?: boolean;
}

/** The file beside a chart, as read. */
export interface Recording {
    /** The format's number as read: 1 (Dettato's reading) or 2. */
    format: number;
    /** The recording's file name, in the folder of the file beside it (no path). */
    audio: string;
    /** The recording's length in seconds, when the file says it. */
    duration?: number;
    /** Every bar the chart plays, in the order played; a pickup is bars[0]. */
    bars: RecordingBar[];
    /** The first bar is a pickup (format 1 marks it `pickup`). */
    pickup: boolean;
    /** The file as parsed: items this reader does not know are written back as they were. */
    raw: Record<string, unknown>;
}

/** The highest format number this reader knows (SIDECAR.md "Changing the format": a reader
 *  refuses a number it does not know). */
export const RECORDING_FORMAT = 2;

/** The file beside <paramref name="lysPath"/>: same folder, same stem, `.dettato.json`. */
export function recordingPathFor(lysPath: string): string {
    const dot = lysPath.lastIndexOf('.');
    const slash = Math.max(lysPath.lastIndexOf('/'), lysPath.lastIndexOf('\\'));
    const stem = dot > slash ? lysPath.slice(0, dot) : lysPath;
    return stem + '.dettato.json';
}

/** The recording's own path, from the file beside the chart and the name it holds. */
export function audioPathFor(recordingPath: string, audio: string): string {
    const slash = Math.max(recordingPath.lastIndexOf('/'), recordingPath.lastIndexOf('\\'));
    return recordingPath.slice(0, slash + 1) + audio;
}

/** The recordings the preview decodes itself (MEASURED in VS Code 1.146's webview, 2026-11-15:
 *  wav, mp3, flac, ogg), and the ones the language server decodes on Windows only (an m4a: the
 *  webview has no AAC; docs/recording-design.md §1 5). */
export const WEBVIEW_AUDIO = ['wav', 'mp3', 'flac', 'ogg'];
export const WINDOWS_AUDIO = ['m4a', 'mp4', 'aac', 'wma'];

function extensionOf(name: string): string {
    const slash = Math.max(name.lastIndexOf('/'), name.lastIndexOf('\\'));
    const dot = name.lastIndexOf('.');
    return dot > slash ? name.slice(dot + 1).toLowerCase() : '';
}

/** How a recording is read: by the webview, by the language server (Windows), or not at all
 *  (with the words to say why). */
export function audioReader(name: string, isWindows: boolean): 'webview' | 'server' | { refused: string } {
    const ext = extensionOf(name);
    if (WEBVIEW_AUDIO.includes(ext)) {
        return 'webview';
    }
    if (WINDOWS_AUDIO.includes(ext)) {
        return isWindows ? 'server' : { refused: `a .${ext} recording is read on Windows only — use a wav, mp3, flac or ogg` };
    }
    return { refused: `not a recording Lily# reads (${[...WEBVIEW_AUDIO, ...(isWindows ? WINDOWS_AUDIO : [])].join(', ')})` };
}

/** The chart a recording is attached to, when it is right-clicked: the .lys of its own stem in
 *  its folder; else the chart open in the editor when it is in that folder; else the folder's one
 *  .lys. Undefined when the person has to choose (several, or none). Names are file names. */
export function chartForRecording(audio: string, lysInFolder: readonly string[], activeLys?: string): string | undefined {
    const stem = (n: string) => n.slice(0, n.lastIndexOf('.') >= 0 ? n.lastIndexOf('.') : n.length).toLowerCase();
    const own = lysInFolder.find(n => stem(n) === stem(audio));
    if (own) {
        return own;
    }
    if (activeLys && lysInFolder.includes(activeLys)) {
        return activeLys;
    }
    return lysInFolder.length === 1 ? lysInFolder[0] : undefined;
}

/** What a rename in the workspace asks of the files beside charts: a chart renamed takes its
 *  file along; a recording renamed is renamed inside the file that names it. Paths in, the work
 *  out — `move` the file beside a chart, or `rename` the audio a file names. */
export type RecordingRenameWork =
    | { kind: 'move'; from: string; to: string }
    | { kind: 'rename'; file: string; audio: string };

export function workForRename(oldPath: string, newPath: string, exists: (path: string) => boolean,
    namesAudio: (recordingPath: string) => string | undefined, recordingsInFolder: readonly string[] = []): RecordingRenameWork[] {
    const work: RecordingRenameWork[] = [];
    const folder = (p: string) => p.slice(0, Math.max(p.lastIndexOf('/'), p.lastIndexOf('\\')) + 1);
    const name = (p: string) => p.slice(folder(p).length);
    if (extensionOf(oldPath) === 'lys' && extensionOf(newPath) === 'lys') {
        const from = recordingPathFor(oldPath), to = recordingPathFor(newPath);
        if (exists(from) && !exists(to)) {
            work.push({ kind: 'move', from, to });
        }
        return work;
    }
    // A recording renamed in its own folder: every file beside a chart there that names it follows
    // (a chart's file is named for the chart, which need not be the recording's name).
    if (folder(oldPath) === folder(newPath) && [...WEBVIEW_AUDIO, ...WINDOWS_AUDIO].includes(extensionOf(oldPath))) {
        for (const file of recordingsInFolder) {
            if (folder(file) === folder(oldPath) && exists(file) && namesAudio(file) === name(oldPath)) {
                work.push({ kind: 'rename', file, audio: name(newPath) });
            }
        }
    }
    return work;
}

/** Reads the file beside a chart. An error says why it cannot be used, in words for the user. */
export function parseRecording(text: string): Recording | { error: string } {
    let raw: unknown;
    try {
        raw = JSON.parse(text);
    } catch (e) {
        return { error: `not JSON: ${(e as Error).message}` };
    }
    if (!raw || typeof raw !== 'object' || Array.isArray(raw)) {
        return { error: 'not a JSON object' };
    }
    const o = raw as Record<string, unknown>;
    // Files written before 2026-11-14 have no `dettato` and are otherwise format 1 (SIDECAR.md).
    const format = o.dettato === undefined ? 1 : o.dettato;
    if (typeof format !== 'number' || !Number.isInteger(format) || format < 1) {
        return { error: `"dettato" is not a format number` };
    }
    if (format > RECORDING_FORMAT) {
        return { error: `format ${format} is newer than this Lily# reads (${RECORDING_FORMAT}) — update Lily#` };
    }
    if (typeof o.audio !== 'string' || o.audio === '' || /[\\/]/.test(o.audio)) {
        return { error: `"audio" must be the recording's file name, in the same folder` };
    }
    if (!Array.isArray(o.bars)) {
        return { error: `"bars" is missing` };
    }
    const bars: RecordingBar[] = [];
    for (let i = 0; i < o.bars.length; i++) {
        const b = o.bars[i] as Record<string, unknown>;
        if (!b || typeof b.start !== 'number' || typeof b.end !== 'number' || !(b.end > b.start)) {
            return { error: `bar ${i}: "start" and "end" must be seconds, "end" after "start"` };
        }
        if (i > 0 && b.start < bars[i - 1].start) {
            return { error: `bar ${i} starts before the bar before it` };
        }
        bars.push(b.byHand === true ? { start: b.start, end: b.end, byHand: true } : { start: b.start, end: b.end });
    }
    const first = o.bars[0] as Record<string, unknown> | undefined;
    return {
        format,
        audio: o.audio,
        duration: typeof o.duration === 'number' ? o.duration : undefined,
        bars,
        pickup: !!first && first.pickup !== undefined,
        raw: o,
    };
}

/** A new file beside a chart, for a recording a person attached (no bars yet: they are set by
 *  hand, docs/recording-design.md §1 6). */
export function newRecording(audio: string, duration?: number): Recording {
    return { format: RECORDING_FORMAT, audio, duration, bars: [], pickup: false, raw: {} };
}

/** The file as Lily# writes it: format 2, the items it does not know kept as they were, each
 *  bar's own items kept and `byHand` set or cleared. */
export function serializeRecording(rec: Recording): string {
    const out: Record<string, unknown> = { ...rec.raw, dettato: RECORDING_FORMAT, audio: rec.audio };
    if (rec.duration !== undefined) {
        out.duration = rec.duration;
    }
    const rawBars = Array.isArray(rec.raw.bars) ? rec.raw.bars as Record<string, unknown>[] : [];
    out.bars = rec.bars.map((b, i) => {
        const bar: Record<string, unknown> = { ...(rawBars[i] ?? { bar: i }), start: round(b.start), end: round(b.end) };
        if (b.byHand) {
            bar.byHand = true;
        } else {
            delete bar.byHand;
        }
        return bar;
    });
    return JSON.stringify(out, null, 2) + '\n';
}

function round(s: number): number {
    return Math.round(s * 10000) / 10000;
}

/** One beat of the score's playback (`lilysharp/playback`'s `Beats`): its time in seconds at
 *  the written tempo, and whether it begins a bar. */
export interface ScoreBeat { T: number; A: boolean }

/** The bars the score plays, each as its start and end in score seconds, in the order played —
 *  the n-th is the recording's bars[n]. A pickup is a bar: the score's playback gives the beat
 *  that ends it the first accent (Lily# session 1242, REQUESTS 2), so a score whose first accent
 *  is after 0 has a pickup from 0 to there. */
export function scoreBars(beats: readonly ScoreBeat[], scoreEnd: number): [number, number][] {
    const starts = beats.filter(b => b.A).map(b => b.T);
    if (starts.length === 0 || starts[0] > 1e-6) {
        starts.unshift(0);
    }
    const bars: [number, number][] = [];
    for (let i = 0; i < starts.length; i++) {
        const end = i + 1 < starts.length ? starts[i + 1] : scoreEnd;
        if (end > starts[i] + 1e-6) {
            bars.push([starts[i], end]);
        }
    }
    return bars;
}

/** Which bar played (index into scoreBars) holds score time t; -1 before the first or after the end. */
export function barAt(bars: readonly [number, number][], t: number): number {
    let lo = 0, hi = bars.length - 1, found = -1;
    while (lo <= hi) {
        const mid = (lo + hi) >> 1;
        if (bars[mid][0] <= t + 1e-9) {
            found = mid;
            lo = mid + 1;
        } else {
            hi = mid - 1;
        }
    }
    return found >= 0 && t <= bars[found][1] + 1e-9 ? found : -1;
}

/** How the score's bars and the recording's line up: the same count, or which is longer.
 *  A chart a person changed by a bar no longer lines up (SIDECAR.md "After the chart is
 *  corrected": say so and do not play a bar that may be the wrong one). */
export function lineUp(bars: readonly [number, number][], rec: Recording): { scoreBars: number; recordingBars: number; same: boolean } {
    return { scoreBars: bars.length, recordingBars: rec.bars.length, same: bars.length === rec.bars.length };
}

/** The recording's second for score time t: inside a bar the two go together in a straight line
 *  (SIDECAR.md, "Finding the seconds"). Null when the bar has no place in the recording. */
export function recordingSecondAt(bars: readonly [number, number][], rec: Recording, t: number): number | null {
    const n = barAt(bars, t);
    if (n < 0 || n >= rec.bars.length) {
        return null;
    }
    const [s0, s1] = bars[n];
    const r = rec.bars[n];
    return r.start + (t - s0) / (s1 - s0) * (r.end - r.start);
}

/** The score time for the recording's second sec — the inverse of recordingSecondAt, for a
 *  preview that follows the recording. Null outside the bars the recording places. */
export function scoreTimeAt(bars: readonly [number, number][], rec: Recording, sec: number): number | null {
    const count = Math.min(bars.length, rec.bars.length);
    for (let n = 0; n < count; n++) {
        const r = rec.bars[n];
        const last = n === count - 1;
        if (sec >= r.start - 1e-9 && (sec < r.end || (last && sec <= r.end + 1e-9))) {
            const [s0, s1] = bars[n];
            return s0 + (sec - r.start) / (r.end - r.start) * (s1 - s0);
        }
    }
    return null;
}
