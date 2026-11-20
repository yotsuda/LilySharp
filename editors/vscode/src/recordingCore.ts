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

// The recording beside a chart (docs/recording-design.md): the file `<stem>.orecchiante.json` next to
// `<stem>.lys` says which recording goes with the chart and where in it every bar the chart plays
// is. Orecchiante writes format 1 (C:\MyProj\Orecchiante\docs\SIDECAR.md); Lily# reads it and writes
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
    /** The format's number as read: 1 (Orecchiante's reading) or 2. */
    format: number;
    /** The recording's file name, in the folder of the file beside it (no path). */
    audio: string;
    /** The recording's length in seconds, when the file says it. */
    duration?: number;
    /** Every bar the chart plays, in the order played; a pickup is bars[0]. */
    bars: RecordingBar[];
    /** The first bar is a pickup (format 1 marks it `pickup`). */
    pickup: boolean;
    /** The recording in parts, each its own file (Orecchiante's `parts`, REQUESTS 16;
     *  docs/recording-design.md §8): in the order written, as many as written, each found as
     *  `audio` is (resolveAudio). Absent when the file has none. */
    parts?: RecordingPart[];
    /** The file as parsed: items this reader does not know are written back as they were. */
    raw: Record<string, unknown>;
}

/** One part of the recording: its name (`drums`, `bass`, … — whatever the file says) and the way
 *  to its file as written. */
export interface RecordingPart {
    name: string;
    file: string;
}

/** The parts a file beside a chart names (its `parts`: an object of name to file, in order);
 *  undefined when it names none. An entry that is not a name and a file is left out. */
export function partsOf(raw: Record<string, unknown>): RecordingPart[] | undefined {
    const p = raw.parts;
    if (!p || typeof p !== 'object' || Array.isArray(p)) { return undefined; }
    const out: RecordingPart[] = [];
    for (const [name, file] of Object.entries(p as Record<string, unknown>)) {
        if (name.trim() !== '' && typeof file === 'string' && file.trim() !== '') { out.push({ name, file }); }
    }
    return out.length > 0 ? out : undefined;
}

/** The order the parts' lines are shown in (owner's decision 2026-11-18): the voice, the chord
 *  instruments, the rest, then the rhythm section. A name is matched whatever its case, with or
 *  without a plural `s` (Orecchiante writes `vocals`). */
const SHOWN_PART_ORDER = ['vocal', 'guitar', 'piano', 'other', 'bass', 'drum'];

/** The parts' names in the order their lines are shown: the known ones as SHOWN_PART_ORDER says,
 *  then the others in the file's order. (Only the lines: the file keeps its order.) */
export function shownPartOrder(names: readonly string[]): string[] {
    const rank = (name: string): number => {
        const n = name.trim().toLowerCase();
        const i = SHOWN_PART_ORDER.findIndex(k => n === k || n === k + 's');
        return i < 0 ? SHOWN_PART_ORDER.length : i;
    };
    return names.map((name, at) => ({ name, at, rank: rank(name) }))
        .sort((a, b) => a.rank - b.rank || a.at - b.at)
        .map(x => x.name);
}

/** The parts' ways written from the file's new place (it moved from `fromFile` to `toFile`), as
 *  `audio` is (audioRef): the file's raw items with `parts` rewritten; the same object when there
 *  is nothing to rewrite. */
export function rebasedParts(raw: Record<string, unknown>, fromFile: string, toFile: string): Record<string, unknown> {
    const parts = partsOf(raw);
    if (!parts) { return raw; }
    const moved: Record<string, string> = {};
    let changed = false;
    for (const p of parts) {
        moved[p.name] = audioRef(toFile, resolveAudio(fromFile, p.file));
        changed ||= moved[p.name] !== p.file;
    }
    return changed ? { ...raw, parts: moved } : raw;
}

/** The highest format number this reader knows (SIDECAR.md "Changing the format": a reader
 *  refuses a number it does not know). */
export const RECORDING_FORMAT = 2;

/** The file beside <paramref name="lysPath"/>: same folder, same stem, `.orecchiante.json`. */
export function recordingPathFor(lysPath: string): string {
    const dot = lysPath.lastIndexOf('.');
    const slash = Math.max(lysPath.lastIndexOf('/'), lysPath.lastIndexOf('\\'));
    const stem = dot > slash ? lysPath.slice(0, dot) : lysPath;
    return stem + '.orecchiante.json';
}

/** The chart a file beside a chart belongs to — `recordingPathFor`'s inverse; undefined for a
 *  name that is not `<stem>.orecchiante.json`. */
export function chartPathFor(recordingPath: string): string | undefined {
    return /\.orecchiante\.json$/i.test(recordingPath) ? recordingPath.replace(/\.orecchiante\.json$/i, '.lys') : undefined;
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

// ---------------------------------------------------------------- where the recording is
//
// The file beside a chart names its recording by `audio` (Orecchiante's request 6, owner's decision
// 2026-11-16, which retired "the file name only"): the file name when the recording is beside the
// chart; else the way to it from the chart's folder, `/`-separated (`../Music/x.mp3`); on another
// drive, the whole path (`F:/Music/x.mp3`). These are strings, not Node paths: the webview reads
// this module too.

function slashed(p: string): string {
    return p.replace(/\\/g, '/');
}

function isAbsolutePath(p: string): boolean {
    return /^[A-Za-z]:\//.test(p) || p.startsWith('/');
}

/** The folder of a path, with its last `/`. */
function folderOf(p: string): string {
    const s = slashed(p);
    return s.slice(0, s.lastIndexOf('/') + 1);
}

/** `.` and `..` taken out of a `/` path. */
function collapse(p: string): string {
    const lead = p.startsWith('//') ? '//' : p.startsWith('/') ? '/' : '';
    const out: string[] = [];
    for (const part of p.slice(lead.length).split('/')) {
        if (part === '' || part === '.') { continue; }
        if (part === '..' && out.length > 0 && out[out.length - 1] !== '..' && !/^[A-Za-z]:$/.test(out[out.length - 1])) {
            out.pop();
        } else {
            out.push(part);
        }
    }
    return lead + out.join('/');
}

/** Whether two paths name one file (Windows: case aside). */
export function samePath(a: string, b: string): boolean {
    return collapse(slashed(a)).toLowerCase() === collapse(slashed(b)).toLowerCase();
}

/** The recording a file beside a chart names, as a whole `/` path: its `audio` read from the
 *  file's own folder. */
export function resolveAudio(recordingFile: string, audio: string): string {
    const a = slashed(audio);
    return collapse(isAbsolutePath(a) || a.startsWith('//') ? a : folderOf(recordingFile) + a);
}

/** What the file at `recordingFile` writes as `audio` for the recording at `audioPath`: its name
 *  when beside it, the way to it from the file's folder, or — on another drive — its whole path. */
export function audioRef(recordingFile: string, audioPath: string): string {
    const fromPath = collapse(slashed(folderOf(recordingFile))), toPath = collapse(slashed(audioPath));
    const from = fromPath.split('/').filter(s => s !== '');
    const to = toPath.split('/').filter(s => s !== '');
    const name = to.pop() ?? '';
    const lower = (s: string) => s.toLowerCase();
    if (from.length === to.length && from.every((s, i) => lower(s) === lower(to[i]))) {
        return name;
    }
    // The drive (C:), the share (//server) or the one root (/): another one, the whole path.
    const root = (s: string) => /^[A-Za-z]:\//.test(s) ? lower(s.slice(0, 2))
        : s.startsWith('//') ? '//' + lower(s.slice(2).split('/')[0]) : '/';
    if (root(fromPath) !== root(toPath)) {
        return toPath;
    }
    let common = 0;
    while (common < from.length && common < to.length && lower(from[common]) === lower(to[common])) {
        common++;
    }
    return [...from.slice(common).map(() => '..'), ...to.slice(common), name].join('/');
}

/** What a rename in the workspace asks of the files beside charts: a chart renamed takes its
 *  file along (its `audio` written anew when the folder changed, so it still finds the recording);
 *  a recording renamed or moved is named anew inside every file that names it. Paths in, the work
 *  out — `move` the file beside a chart, or `rename` the audio a file names. */
export type RecordingRenameWork =
    | { kind: 'move'; from: string; to: string; audio?: string }
    | { kind: 'rename'; file: string; audio: string }
    | { kind: 'moveChart'; from: string; to: string };

/** `candidates` are the files beside charts that may name a renamed recording — the folder's and,
 *  as `audio` may lead elsewhere since 2026-11-16, the workspace's. */
export function workForRename(oldPath: string, newPath: string, exists: (path: string) => boolean,
    namesAudio: (recordingPath: string) => string | undefined, candidates: readonly string[] = []): RecordingRenameWork[] {
    const work: RecordingRenameWork[] = [];
    if (extensionOf(oldPath) === 'lys' && extensionOf(newPath) === 'lys') {
        const from = recordingPathFor(oldPath), to = recordingPathFor(newPath);
        if (exists(from) && !exists(to)) {
            const audio = namesAudio(from);
            const moved = audio !== undefined && !samePath(folderOf(from), folderOf(to))
                ? audioRef(to, resolveAudio(from, audio)) : undefined;
            work.push(moved !== undefined && moved !== audio ? { kind: 'move', from, to, audio: moved } : { kind: 'move', from, to });
        }
        return work;
    }
    // The file beside a chart moved or renamed: its chart follows (owner's request 2026-11-16, as
    // a copy of it brings the chart), and its way to the recording is written from the new place.
    const fromChart = chartPathFor(oldPath), toChart = chartPathFor(newPath);
    if (fromChart && toChart) {
        if (exists(fromChart) && !exists(toChart)) {
            work.push({ kind: 'moveChart', from: fromChart, to: toChart });
        }
        const audio = namesAudio(newPath);
        if (audio !== undefined && !samePath(folderOf(oldPath), folderOf(newPath))) {
            const moved = audioRef(newPath, resolveAudio(oldPath, audio));
            if (moved !== audio) {
                work.push({ kind: 'rename', file: newPath, audio: moved });
            }
        }
        return work;
    }
    // A recording renamed or moved: every file beside a chart that names it follows (a chart's
    // file is named for the chart, which need not be the recording's name).
    if ([...WEBVIEW_AUDIO, ...WINDOWS_AUDIO].includes(extensionOf(oldPath))) {
        const seen = new Set<string>();
        for (const file of candidates) {
            const key = collapse(slashed(file)).toLowerCase();
            if (seen.has(key)) { continue; }
            seen.add(key);
            const audio = namesAudio(file);
            if (audio !== undefined && exists(file) && samePath(resolveAudio(file, audio), oldPath)) {
                work.push({ kind: 'rename', file, audio: audioRef(file, newPath) });
            }
        }
    }
    return work;
}

/** A chart a copy may have come from (followCopies, recording.ts): the chart, the file beside it,
 *  and the recording that file names, as a whole path. */
export interface CopySource { chart: string; recordingFile: string; audio: string }

/**
 * Which file beside a chart a copied chart takes along (owner's request 2026-11-16: a chart copied
 * or saved under another name in VS Code takes its recording with it). VS Code says what was made,
 * not what it was made from, so `sources` are the charts with the same text that have a file
 * beside them. None: nothing to take. Several naming the same recording: any of them is that
 * recording's — the one of the same name first (a copy into another folder keeps the name), else
 * the first. Several naming different recordings: 'ambiguous' — the person is asked.
 */
export function sourceForCopy(newPath: string, sources: readonly CopySource[]): CopySource | 'ambiguous' | undefined {
    const seen = new Set<string>();
    const unique = sources.filter(s => {
        const key = collapse(slashed(s.recordingFile)).toLowerCase();
        if (seen.has(key) || samePath(s.chart, newPath)) { return false; }
        seen.add(key);
        return true;
    });
    if (unique.length === 0) {
        return undefined;
    }
    if (unique.some(s => !samePath(s.audio, unique[0].audio))) {
        return 'ambiguous';
    }
    const name = baseOf(newPath).toLowerCase();
    return unique.find(s => baseOf(s.chart).toLowerCase() === name) ?? unique[0];
}

/** The text of a file beside a chart copied from `fromFile` to `toFile`: the same, its `audio`
 *  written from the new place so it still finds the recording (a name beside the old chart becomes
 *  the way to it). A file that cannot be read is copied as it is. */
export function copiedRecordingText(text: string, fromFile: string, toFile: string): string {
    const r = parseRecording(text);
    if ('error' in r) {
        return text;
    }
    const audio = audioRef(toFile, resolveAudio(fromFile, r.audio));
    const raw = rebasedParts(r.raw, fromFile, toFile);
    if (audio === r.audio && raw === r.raw) {
        return text;
    }
    r.audio = audio;
    r.raw = raw;
    return serializeRecording(r);
}

function baseOf(p: string): string {
    const s = slashed(p);
    return s.slice(s.lastIndexOf('/') + 1);
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
    // Files written before 2026-11-14 have no `orecchiante` and are otherwise format 1 (SIDECAR.md).
    const format = o.orecchiante === undefined ? 1 : o.orecchiante;
    if (typeof format !== 'number' || !Number.isInteger(format) || format < 1) {
        return { error: `"orecchiante" is not a format number` };
    }
    if (format > RECORDING_FORMAT) {
        return { error: `format ${format} is newer than this Lily# reads (${RECORDING_FORMAT}) — update Lily#` };
    }
    // The file name beside the chart, the way to the recording from the chart's folder, or the
    // whole path (resolveAudio; Orecchiante's request 6).
    if (typeof o.audio !== 'string' || o.audio.trim() === '') {
        return { error: `"audio" must name the recording` };
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
    const parts = partsOf(o);
    return {
        format,
        audio: o.audio,
        duration: typeof o.duration === 'number' ? o.duration : undefined,
        bars,
        pickup: !!first && first.pickup !== undefined,
        ...(parts ? { parts } : {}),
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
    const out: Record<string, unknown> = { ...rec.raw, orecchiante: RECORDING_FORMAT, audio: rec.audio };
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

/**
 * Join Bars in Pairs, in the file beside the chart (docs/join-bars-design.md §3; Orecchiante's
 * request 21): each `bars[n]` and `bars[n + 1]` of `played` (the first bar played of every pair, as
 * the language server counts them) become one bar — the first's `start`, `section`, `tempo` and
 * `byHand`, the second's `end`, the beats of the two one in two (as many as the meter has: the
 * joined bar's beats), their chords one after the other. `bar` and `barOfSection` are numbered
 * again. The items this reader does not know stay as they were.
 */
export function joinRecordingBars(rec: Recording, played: readonly number[]): Recording | { error: string } {
    const raw: Record<string, unknown>[] = Array.isArray(rec.raw.bars) && rec.raw.bars.length === rec.bars.length
        ? (rec.raw.bars as Record<string, unknown>[]).map(b => ({ ...b }))
        : rec.bars.map((b, i) => ({ bar: i, start: b.start, end: b.end }));
    const bars = rec.bars.map(b => ({ ...b }));
    const sectionPlay = raw.map(b => b.barOfSection);   // to tell a section played again from one played on
    for (const n of [...new Set(played)].sort((x, y) => y - x)) {
        if (!Number.isInteger(n) || n < 0 || n + 1 >= bars.length) {
            return { error: `the file has no bars ${n} and ${n + 1} to join` };
        }
        const a = raw[n], b = raw[n + 1];
        const joined: Record<string, unknown> = { ...a, end: b.end };
        if (Array.isArray(a.beats) && Array.isArray(b.beats)) {
            joined.beats = [...a.beats, ...b.beats].filter((_, i) => i % 2 === 0);
        }
        if (Array.isArray(a.chords) && Array.isArray(b.chords)) {
            joined.chords = [...a.chords, ...b.chords];
        }
        raw.splice(n, 2, joined);
        sectionPlay.splice(n, 2, sectionPlay[n]);
        bars.splice(n, 2, bars[n].byHand
            ? { start: bars[n].start, end: bars[n + 1].end, byHand: true }
            : { start: bars[n].start, end: bars[n + 1].end });
    }
    // Numbered again: `bar` from 1 (a pickup 0), `barOfSection` from 1 in each playing of a section
    // (a playing begins where the section changes, or where the file began one again).
    let inSection = 0;
    for (let i = 0; i < raw.length; i++) {
        if ('bar' in raw[i]) {
            raw[i].bar = rec.pickup ? i : i + 1;
        }
        const again = i === 0 || raw[i].section !== raw[i - 1].section
            || (typeof sectionPlay[i] === 'number' && typeof sectionPlay[i - 1] === 'number' && (sectionPlay[i] as number) <= (sectionPlay[i - 1] as number));
        inSection = again ? 1 : inSection + 1;
        if ('barOfSection' in raw[i]) {
            raw[i].barOfSection = inSection;
        }
    }
    return { ...rec, bars, raw: { ...rec.raw, bars: raw } };
}

/** How the recording is played (docs/recording-design.md §1 3; owner's decisions 2026-11-19: the
 *  panel keeps the octave alone — its speed, semitones, cents and "pitch with speed" are gone, the
 *  tempo being the practice panel's; the file's `tuning` is not used, §3 2). */
export interface RecordingSettings {
    /** The panel's: octaves up or down (a bass line heard an octave up). */
    octave: number;
    /** The semitones the chart's own transpose moves the music by (the file's top-level
     *  `transpose` and the drawn score's, owner's decision 2026-11-16): the recording was made in
     *  the chart's written key, so it follows. Not the panel's: it comes with each picture of the
     *  chart. Absent / 0: none. */
    chart?: number;
}

export const RECORDING_DEFAULTS: RecordingSettings = { octave: 0 };

/** The settings' ranges. */
export const RECORDING_LIMITS = {
    octave: [-2, 2],
} as const;

function clamp(x: number, [lo, hi]: readonly [number, number]): number {
    return Number.isFinite(x) ? Math.min(hi, Math.max(lo, x)) : 0;
}

/** What the player does for the settings: resample (the plain source, nothing changed at all) or
 *  stretch (Signalsmith: rate and semitones apart), at what rate (recording seconds per second of
 *  sound), shifted how far, and at what gain: a sound shifted up by the stretcher peaks over its
 *  source (MEASURED 2026-11-15: +1200 cents peaked at 1.177, Lab sessions/p1283/stretch), so it is
 *  turned down 3 dB before the limiter.
 *  `tempo` is the practice panel's (§1 7: its slider works on the score and the recording, the
 *  recording's pitch kept): it is the rate. The octave and the chart's transpose move the pitch
 *  alone. */
export function recordingPlan(s: RecordingSettings, tempo = 1): { engine: 'resample' | 'stretch'; rate: number; semitones: number; gain: number } {
    const shift = clamp(s.octave, RECORDING_LIMITS.octave) * 12 + clamp(s.chart ?? 0, [-24, 24]);
    const rate = Number.isFinite(tempo) && tempo > 0 ? tempo : 1;
    if (shift === 0 && rate === 1) {
        return { engine: 'resample', rate: 1, semitones: 0, gain: 1 };
    }
    return { engine: 'stretch', rate, semitones: shift, gain: shift > 0 ? Math.pow(10, -3 / 20) : 1 };
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

/** The score's bars and the recording's, lined up bar for bar — what lets the score play with the
 *  recording (docs/recording-design.md §1 7: the score's notes moved, bar by bar, onto the
 *  recording's seconds). Null when they cannot be: no bar placed yet (a recording only attached
 *  sounds alone). Every bar must move forward in both.
 *
 *  Counts that differ (a chart a bar added or taken out, or being cut into sections) line up as far
 *  as the shorter goes, the n-th bar with the n-th (Orecchiante's request 11, the owner 2026-11-16:
 *  "71 bars of the recording to their end"; SIDECAR.md "After the chart is corrected" — before then
 *  it said not to play at all). The bars after the one added or taken out are a bar off: the person
 *  is putting that right. Past the shorter end the score goes on at its last bar's pace (carry). */
export interface TimeWarp {
    score: [number, number][];
    rec: [number, number][];
    /** The two counts when they differ — the score's bars and the recording's; absent when equal. */
    counts?: { score: number; rec: number };
}

export function timeWarp(beats: readonly ScoreBeat[] | null, scoreEnd: number | null, rec: Pick<Recording, 'bars'>): TimeWarp | null {
    if (!beats || typeof scoreEnd !== 'number' || !(scoreEnd > 0) || rec.bars.length === 0) {
        return null;
    }
    const allScore = scoreBars(beats, scoreEnd);
    const n = Math.min(allScore.length, rec.bars.length);
    if (n === 0) {
        return null;
    }
    const score = allScore.slice(0, n);
    const recBars = rec.bars.slice(0, n).map(b => [b.start, b.end] as [number, number]);
    for (let i = 0; i < n; i++) {
        if (!(score[i][1] > score[i][0]) || !(recBars[i][1] > recBars[i][0])) {
            return null;
        }
    }
    return allScore.length === rec.bars.length
        ? { score, rec: recBars }
        : { score, rec: recBars, counts: { score: allScore.length, rec: rec.bars.length } };
}

/** Moves a time from one line of bars to the other: straight inside a bar; before the first bar
 *  and after the last, at that bar's pace (a note that rings past the last bar line, the score's
 *  lead before its first bar). */
function carry(from: readonly [number, number][], to: readonly [number, number][], t: number): number {
    const last = from.length - 1;
    let n = 0;
    if (t >= from[last][0]) {
        n = last;
    } else if (t > from[0][0]) {
        let lo = 0, hi = last;
        while (lo < hi) {
            const mid = (lo + hi + 1) >> 1;
            if (from[mid][0] <= t) {
                lo = mid;
            } else {
                hi = mid - 1;
            }
        }
        n = lo;
    }
    const [f0, f1] = from[n], [t0, t1] = to[n];
    return t0 + (t - f0) / (f1 - f0) * (t1 - t0);
}

// (The recording plays at the practice tempo alone, never at the score's written pace: owner's
// decision 2026-11-20, Shosa's request 3 — the old warpFit, the median of the bars' ratios,
// stretched a recording by 0.07 % at a tempo of 100 %, and a dance timed on the recording's
// seconds drifted 245 ms by its end. The score's notes follow the recording bar by bar, its
// average pace with them; a tempo written anew no longer moves the recording.)

/** The recording's second for score time t (any t: see carry). */
export function warpTime(w: TimeWarp, t: number): number {
    return carry(w.score, w.rec, t);
}

/** The score time for the recording's second sec — warpTime's inverse. */
export function unwarpTime(w: TimeWarp, sec: number): number {
    return carry(w.rec, w.score, sec);
}

/** Notes (any object with a start T and a length D, in score seconds) on the recording's seconds:
 *  each bar's notes stretched or squeezed as the recording plays that bar. */
export function warpNotes<N extends { T: number; D: number }>(notes: readonly N[], w: TimeWarp): N[] {
    return notes.map(n => {
        const T = warpTime(w, n.T);
        return { ...n, T, D: n.D > 0 ? Math.max(0, warpTime(w, n.T + n.D) - T) : n.D };
    });
}

/** The score's beats on the recording's seconds (the metronome, the count-in and the loop's bars
 *  follow the recording too). */
export function warpBeats<B extends { T: number }>(beats: readonly B[], w: TimeWarp): B[] {
    return beats.map(b => ({ ...b, T: warpTime(w, b.T) }));
}

/** A warp as a word, the same on both sides (the extension and the page work it out apart): what
 *  a singing voice was sung on, so that the page sounds a voice only on the warp it was sung for
 *  (Shosa's request 4) — '' for none, the score's own seconds. To the tenth of a millisecond. */
export function warpKey(w: TimeWarp | null | undefined): string {
    if (!w) {
        return '';
    }
    const at = (pairs: [number, number][]) => pairs.map(([a, b]) => `${a.toFixed(4)}-${b.toFixed(4)}`).join(',');
    return `${at(w.score)}|${at(w.rec)}`;
}
