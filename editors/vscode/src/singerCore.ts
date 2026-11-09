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

// The singing voice's pure half (docs/singing-design.md §5): the server's sung voice turned
// into the score the singing engine (Cantoria) reads, the key a rendered voice is cached by,
// and the lines of `cantoria serve`'s wire. No vscode, no process here: test/singerCore.test.ts
// drives it under node.

import { createHash } from 'crypto';
import { VOICE_LEAD_S } from './webview/playbackCore';

/** One note of `lilysharp/sung`, in seconds: its syllable `L` (null on a melisma), `M` a melisma
 *  carrying the previous syllable's vowel on, `B` a breath after it, `S` the source offset. */
export interface SungNoteDto { T: number; D: number; P: number; L?: string | null; M?: boolean; B?: boolean; S?: number; V?: number | null; E?: number | null; I?: boolean; H?: boolean }

/** A part's words on its notes, as the server answers `lilysharp/sung`. */
/** `InScore`: the score shown sings this part itself; false for a voice the file's other lyrics could sing (off until the menu turns it on; session 1163). */
export interface SungVoiceDto { Part: string; Language: string; Notes: SungNoteDto[]; Warnings?: string[] | null; InScore?: boolean; Singer?: string | null; Gender?: string | null }

/** The score Cantoria reads (its README): seconds, MIDI pitch, the syllable as pronounced. */
export interface CantoriaNote { start: number; duration: number; pitch: number; lyric?: string; melisma?: boolean; breath?: boolean; level?: number; levelEnd?: number; inhale?: boolean; hyphen?: boolean }
export interface CantoriaScore { sampleRate: number; language: string; notes: CantoriaNote[] }

/** The rate the voice is asked for — NEUTRINO's own, so nothing is resampled on the way. */
export const VOICE_SAMPLE_RATE = 48000;

/** The Cantoria extension, the engine's own home on the Marketplace (docs/singing-design.md §5). */
export const CANTORIA_EXTENSION_ID = 'yotsuda.cantoria';

/** A voice the Cantoria extension lists: `id` is `engine/name` (`neutrino/MERROW`), what its
 *  `sing` takes and its setting holds. */
export interface CantoriaVoice { id: string; engine: string; name: string; label?: string; language?: string; languages?: string[]; gender?: string; licence?: string }

/** Whether a voice says it sings `language`: its list of languages, else its one. */
export function singsLanguage(voice: Pick<CantoriaVoice, 'language' | 'languages'>, language: string): boolean {
    return voice.languages?.includes(language) ?? voice.language === language;
}

/** What that extension's `activate` returns, as far as Lily# reads it (its README, "For extension
 *  authors"). `status().voice` is the setting's voice as `engine/name`; `voices()` came with
 *  Cantoria 0.2 and an older extension has none. */
export interface CantoriaApi {
    readonly version: string;
    readonly apiVersion: number;
    status(): Promise<{ ready: boolean; voice: string; problems: string[] }>;
    voices?(): Promise<CantoriaVoice[]>;
    sing(score: CantoriaScore, options?: { voice?: string }): Promise<{ wav: string; duration: number; cached?: boolean }>;
    release(): void;
    /** The voice a `.lys` file's `singer` string comes to on this machine, and what to tell the
     *  writer when it was not the one asked for. Cantoria 0.4 on. */
    resolveSinger?(singer: string, options?: { language?: string; gender?: string }): Promise<{ voice: string; note?: string }>;
}

/** Whether a `singer`'s written gender lets this voice be offered: no gender written, a voice that names none, or the same one. */
export function fitsGender(voice: Pick<CantoriaVoice, 'gender'>, gender?: string | null): boolean {
    return !gender || !voice.gender || voice.gender === gender;
}

const LANGUAGE_CODES: Record<string, string> = { japanese: 'ja', english: 'en', chinese: 'zh', korean: 'ko' };

/** The answer of the chat's voice list (owner's request 2026-11-09, session 1202): the names a score can
 *  write, narrowed by provider, gender and language. As in the completion, a voice that does not state its
 *  gender stays when a gender is asked for, and the answer says so. */
export function voiceListText(voices: readonly CantoriaVoice[], filter: { provider?: string; gender?: string; language?: string }): string {
    const provider = (filter.provider ?? '').trim().toLowerCase();
    const gender = (filter.gender ?? '').trim().toLowerCase();
    const written = (filter.language ?? '').trim().toLowerCase();
    const language = LANGUAGE_CODES[written] ?? written;
    if (gender && gender !== 'male' && gender !== 'female') { return `"${filter.gender}" is no gender of a voice: ask for male or female.`; }
    if (voices.length === 0) { return 'No singing voice is available on this machine. A score can still be written without a singer.'; }
    const providers = [...new Set(voices.map(v => v.engine))].sort();
    if (provider && !providers.includes(provider)) { return `No provider named "${filter.provider}" has voices here. There are: ${providers.join(', ')}.`; }
    const found = byVoiceId(voices.filter(v => (!provider || v.engine === provider) && fitsGender(v, gender) && (!language || singsLanguage(v, language))));
    const asked = [provider, gender, language].filter(Boolean).join(', ');
    if (found.length === 0) { return `No voice matches (${asked}). ${voices.length} voices are available with no condition.`; }
    const lines = found.map(v => {
        const languages = v.languages ?? (v.language ? [v.language] : []);
        return [`${v.id}:`, v.label && v.label !== v.name ? v.label : undefined, v.gender ?? 'gender not stated',
            languages.length > 0 ? `sings ${languages.join(', ')}` : 'languages not stated', v.licence].filter(Boolean).join(' · ').replace(': · ', ': ');
    });
    return [`${found.length} voice${found.length === 1 ? '' : 's'}${asked ? ` (${asked})` : ''}:`, ...lines, '',
        'Write a name after singer on a part, a lyrics track or a score\'s lyrics row: singer "neutrino/MERROW". Names with commas between them sing the part together: singer "a/b","c/d". A voice sings only the languages it lists.'].join('\n');
}

/** The commands Cantoria offers for finding a voice, and where the first engine is to be had. */
export const VOICE_SETUP_ACTIONS: readonly { label: string; command?: string; url?: string }[] = [
    { label: 'Choose NEUTRINO Folder…', command: 'cantoria.chooseNeutrinoFolder' },
    { label: 'Choose DiffSinger Folder…', command: 'cantoria.chooseVoicesFolder' },
    { label: 'Choose VOICEVOX Folder…', command: 'cantoria.chooseVoicevoxFolder' },
    { label: 'Get NEUTRINO', url: 'https://studio-neutrino.com/' },
];

/** What the editor says when no singing voice is installed: with the Cantoria extension, that an engine
 *  is missing; without it, that the extension is the first step. */
export function noVoiceGuide(cantoria: boolean): { label: string; detail: string; documentation: string; message: string } {
    const engines = '[NEUTRINO](https://studio-neutrino.com/), [DiffSinger](https://github.com/openvpi/DiffSinger) or [VOICEVOX](https://voicevox.hiroshiba.jp/)';
    return cantoria
        ? {
            label: 'No singing voice is installed',
            detail: 'Set up a voice…',
            documentation: `Cantoria has no voice to sing with. Install ${engines}, then choose its folder: take this line for the buttons.\n\nThe score can still be written: a name here is sung once that voice is installed.`,
            message: 'Lily#: no singing voice is installed. Cantoria sings with NEUTRINO, DiffSinger or VOICEVOX: install one, then choose its folder.',
        }
        : {
            label: 'No singing voice: Cantoria is not installed',
            detail: 'Install Cantoria…',
            documentation: `Voices are sung by the Cantoria extension, with ${engines}. Take this line to install the extension, then choose an engine's folder there.\n\nThe score can still be written: a name here is sung once that voice is installed.`,
            message: 'Lily#: singing a score with a voice needs the Cantoria extension.',
        };
}

/** What the status bar says, once, of a part with words in the score and no voice asked for them. */
export function printedWordsNote(part: string): string {
    return `the words of ${part} are printed, not sung: write singer on the part, or give it a voice (instrument soprano)`;
}

/** One choice between a `singer`'s quotes. */
export interface SingerChoice { label: string; detail: string }

/**
 * What the editor offers between a `singer`'s quotes (owner's request 2026-11-09, session 1168:
 * only the names right for the language): the installed voices that sing `language`. A voice that
 * does not say what it sings is left out; with `gender` (`singer male "…"`, sessions 1173 and
 * 1174), so is one that says it is the other.
 */
export function singerChoices(voices: readonly CantoriaVoice[], language: string, gender?: string | null): SingerChoice[] {
    return byVoiceId(voices.filter(v => singsLanguage(v, language) && fitsGender(v, gender)))
        .map(v => ({ label: v.id, detail: v.label && v.label !== v.name ? v.label : v.name }));
}

/** Whether a score names who sings: a `singer` followed by a name or a gender, off a comment. */
export function writesSinger(text: string): boolean {
    return text.split('\n').some(line => /(?<![\w-])singer[ \t]+(?:"|(?:male|female)(?![\w-]))/.test(line.split('//')[0]));
}

/** Whether a line's text before the caret ends with `singer ` or `singer male `: a space that opens the list of voices. */
export function afterSingerWord(before: string): boolean {
    return /(?<![\w-])singer[ \t]+(?:(?:male|female)[ \t]+)?(?:(?:"[^"]*"[ \t]*,[ \t]*)+)?$/.test(before) && !before.includes('//');
}

/** The name of a `singer "…","…"` the caret stands on (off the names, on the word or a comma: the one before it, else the first): where it is on the line. */
export function singerValueAt(line: string, character: number): { start: number; end: number; value: string } | undefined {
    for (const m of line.matchAll(/(?<![\w-])singer[ \t]+(?:(?:male|female)[ \t]+)?("[^"]*"(?:[ \t]*,[ \t]*"[^"]*")*)/g)) {
        if (character < m.index || character > m.index + m[0].length) { continue; }
        const from = m.index + m[0].length - m[1].length;
        const names = [...m[1].matchAll(/"([^"]*)"/g)].map(n => ({ start: from + n.index + 1, end: from + n.index + 1 + n[1].length, value: n[1] }));
        return [...names].reverse().find(n => n.start - 1 <= character) ?? names[0];
    }
    return undefined;
}

/** Where on the line to ask what language a `singer`'s words are in, wherever on the `singer` the caret
 *  is (owner's report 2026-11-09, session 1200: just past the closing quote the answer was none, and a voice
 *  of several languages said its first one's phrase): inside the name, or at the end of a bare gender. */
export function singerLanguageColumn(line: string, character: number): number {
    const named = singerValueAt(line, character);
    if (named) { return named.start; }
    for (const m of line.matchAll(/(?<![\w-])singer[ \t]+(?:male|female)(?![\w-])/g)) {
        if (character >= m.index && character <= m.index + m[0].length) { return m.index + m[0].length; }
    }
    return character;
}

/**
 * What the `singer` that holds `from`..`to` on this line writes, for hearing it (owner's request
 * 2026-11-09, session 1180): its name (several: joined as the engine takes them, to be heard together),
 * its gender if it has no name, '' for `singer ""`. Undefined off one, in a comment and inside another string.
 */
export function singerWrittenAt(line: string, from: number, to: number): string | undefined {
    const list = '"[^"]*"(?:[ \\t]*,[ \\t]*"[^"]*")*';
    const names = (written: string | undefined) => [...(written ?? '').matchAll(/"([^"]*)"/g)].map(n => n[1]).filter(Boolean).join('+');
    for (const m of line.matchAll(new RegExp(`(?<![\\w-])singer[ \\t]+(?:(male|female)(?![\\w-])(?:[ \\t]+(${list}))?|(${list}))`, 'g'))) {
        const before = line.slice(0, m.index);
        if (before.includes('//') || (before.split('"').length - 1) % 2 === 1) { continue; }
        if (from >= m.index && to <= m.index + m[0].length) { return names(m[2]) || names(m[3]) || m[1] || ''; }
    }
    return undefined;
}

/**
 * The voice a step key moves to (owner's requests 2026-11-09, sessions 1170, 1172, 1173 and 1188):
 * Up (`direction` 1) is the voice above in the whole list, Down the one below, from one engine into
 * the next; with `acrossEngines` (PageUp `direction` 1, upward too), the first voice of the engine above or below. Neither goes round.
 * A `singer` that names none of `voices` (a role, an empty string) enters the list at the end the key
 * moves away from. Undefined when there is nowhere else to go.
 */
export function stepVoice(voices: readonly Pick<CantoriaVoice, 'id' | 'engine'>[], current: string, direction: 1 | -1, acrossEngines: boolean): string | undefined {
    if (voices.length === 0) { return undefined; }
    const here = voices.find(v => v.id === current);
    if (acrossEngines) {
        const engines = [...new Set(voices.map(v => v.engine))];
        if (engines.length < 2) { return undefined; }
        const engine = engines[here ? engines.indexOf(here.engine) - direction : direction > 0 ? engines.length - 1 : 0];
        return engine === undefined ? undefined : voices.find(v => v.engine === engine)?.id;
    }
    if (!here) { return (direction > 0 ? voices[voices.length - 1] : voices[0]).id; }
    return voices[voices.findIndex(v => v.id === current) - direction]?.id;
}

/** Voices in the order they are offered and stepped through: by name, so by engine first. */
export function byVoiceId<T extends { id: string }>(voices: readonly T[]): T[] {
    return [...voices].sort((a, b) => a.id.localeCompare(b.id, 'en'));
}

/** What a render costs before its length counts: the bar's estimate (session 1178). */
const VOICE_RENDER_BASE_MS = 1500;
/** A first guess at the engine's speed, in milliseconds per second sung, until a render is timed. */
export const VOICE_RENDER_FIRST_RATE = 100;

/** How many seconds a voice lasts at `speed`, from its first note's start at 0. */
export function sungSeconds(dto: Pick<SungVoiceDto, 'Notes'>, speed: number): number {
    return dto.Notes.reduce((end, n) => Math.max(end, n.T + n.D), 0) / speed;
}

/** How long the engine should take over `seconds` sung, at its `rate`. */
export function voiceRenderEstimateMs(seconds: number, rate: number): number {
    return VOICE_RENDER_BASE_MS + seconds * rate;
}

/** The engine's speed after a render of `seconds` took `elapsedMs`, the earlier speed counting half. */
export function learnRenderRate(rate: number, elapsedMs: number, seconds: number): number {
    if (seconds <= 0) { return rate; }
    const measured = Math.max(0, elapsedMs - VOICE_RENDER_BASE_MS) / seconds;
    return Math.min(3000, Math.max(10, (rate + measured) / 2));
}

/** Bumped when the phrase below changes: the samples kept on disk are named with it. */
export const VOICE_SAMPLE_VERSION = 2;
/** The same for the English phrase, on its own so that a change to it leaves the Japanese samples. */
export const VOICE_SAMPLE_EN_VERSION = 3;

/** The time each stage of hearing a voice took, for the output channel (owner's request 2026-11-09, session 1171). */
export class StageClock {
    private readonly stages: string[] = [];
    private last: number;

    constructor(readonly startedAt: number, private readonly now: () => number = Date.now) { this.last = startedAt; }

    mark(stage: string): void {
        const t = this.now();
        this.stages.push(`${stage} ${t - this.last} ms`);
        this.last = t;
    }

    toString(): string { return this.stages.join(', '); }
}

/** A sample's file name: letters of any script kept, so that two VOICEVOX voices, whose names are
 *  kana, do not come to one file. */
export function voiceSampleFileName(key: string): string {
    return `${key.replace(/[^\p{L}\p{N}_.-]+/gu, '_')}-${key.endsWith('@en') ? VOICE_SAMPLE_EN_VERSION : VOICE_SAMPLE_VERSION}.wav`;
}

/** What a voice's sample is kept under: Japanese under the voice alone, as the samples already sung are,
 *  and another language after an `@`, since one voice now says a phrase in each language it sings. */
export function voiceSampleKey(id: string, language?: string): string {
    return language && language !== 'ja' ? `${id}@${language}` : id;
}

/** The short phrase a voice is heard by when it is chosen, an octave lower for a male voice (owner's
 *  request 2026-11-09, session 1177); undefined for a language it has no words for. */
export function voiceSampleScore(language: string, gender?: string): CantoriaScore | undefined {
    const octave = gender === 'male' ? -12 : 0;
    if (language === 'en') {
        // Five syllables to the five of こんにちわ, on the same notes (owner's decision 2026-11-09, session 1201).
        return { sampleRate: VOICE_SAMPLE_RATE, language, notes: [
            { start: 0.3, duration: 0.25, pitch: 60 + octave, lyric: 'hel', hyphen: true },
            { start: 0.55, duration: 0.25, pitch: 62 + octave, lyric: 'lo' },
            { start: 0.8, duration: 0.25, pitch: 64 + octave, lyric: 'how' },
            { start: 1.05, duration: 0.25, pitch: 65 + octave, lyric: 'are' },
            { start: 1.3, duration: 0.5, pitch: 67 + octave, lyric: 'you' },
        ] };
    }
    if (language !== 'ja') { return undefined; }
    const notes: CantoriaNote[] = [
        { start: 0.3, duration: 0.25, pitch: 60, lyric: 'こ' },
        { start: 0.55, duration: 0.25, pitch: 62, lyric: 'ん' },
        { start: 0.8, duration: 0.25, pitch: 64, lyric: 'に' },
        { start: 1.05, duration: 0.25, pitch: 65, lyric: 'ち' },
        { start: 1.3, duration: 0.5, pitch: 67, lyric: 'わ' },     // as it is said: a は here would be sung ha
    ];
    return { sampleRate: VOICE_SAMPLE_RATE, language, notes: notes.map(n => ({ ...n, pitch: n.pitch + octave })) };
}

/**
 * The samples still to sing, one at a time so the engine is never held long from a score's own
 * voices: `ask` goes to the front and says when its sample is there, `warm` waits behind. A
 * sample that could not be sung is not warmed again; an `ask` tries it again.
 */
export class VoiceSampleQueue {
    private readonly waiting: string[] = [];
    private readonly callers = new Map<string, ((ok: boolean) => void)[]>();
    private readonly failed = new Set<string>();
    private running = false;
    private current: string | undefined;

    constructor(private readonly has: (id: string) => boolean, private readonly render: (id: string) => Promise<void>,
        private readonly tell?: (id: string, error: unknown) => void) {}

    warm(ids: readonly string[]): void {
        for (const id of ids) {
            if (!this.has(id) && !this.failed.has(id) && id !== this.current && !this.waiting.includes(id)) { this.waiting.push(id); }
        }
        void this.pump();
    }

    ask(id: string): Promise<boolean> {
        if (this.has(id)) { return Promise.resolve(true); }
        return new Promise<boolean>(resolve => {
            this.callers.set(id, [...(this.callers.get(id) ?? []), resolve]);
            if (id !== this.current) {
                const at = this.waiting.indexOf(id);
                if (at >= 0) { this.waiting.splice(at, 1); }
                this.waiting.unshift(id);
            }
            void this.pump();
        });
    }

    private async pump(): Promise<void> {
        if (this.running) { return; }
        this.running = true;
        for (let id = this.waiting.shift(); id !== undefined; id = this.waiting.shift()) {
            this.current = id;
            let ok = false;
            try {
                await this.render(id);
                ok = true;
                this.failed.delete(id);
            } catch (e) {
                this.failed.add(id);
                this.tell?.(id, e);
            }
            this.current = undefined;
            for (const resolve of this.callers.get(id) ?? []) { resolve(ok); }
            this.callers.delete(id);
        }
        this.running = false;
    }
}

/** The API from an extension's exports, or undefined when they are not that shape. */
export function cantoriaApiOf(exports: unknown): CantoriaApi | undefined {
    const e = exports as Partial<CantoriaApi> | undefined;
    return e && typeof e.sing === 'function' && typeof e.status === 'function' && e.apiVersion === 1 ? e as CantoriaApi : undefined;
}

const round = (x: number): number => Math.round(x * 1e4) / 1e4;

/**
 * The engine's score of a voice at the practice menu's `speed`: the notes' seconds divided by
 * it (a voice cannot be stretched after the fact without its pitch moving, so a speed change
 * is a render of its own), a melisma with no lyric, a breath where the server put one.
 */
export function cantoriaScore(voice: SungVoiceDto, speed: number): CantoriaScore {
    const k = 1 / (speed > 0 ? speed : 1);
    return {
        sampleRate: VOICE_SAMPLE_RATE,
        language: voice.Language || 'ja',
        notes: voice.Notes.map(n => {
            // Rounded at the note's ends, not its length: a start and a duration rounded apart can
            // put the next note's start a ten-thousandth before this one's end (16ths at 132 are
            // repeating decimals), and the engine refuses overlapping notes (owner's report
            // 2026-11-08, 雪やこんこ: "note 22 at 6.364 s starts before note 21 ends (6.364 s)").
            // Moved later by VOICE_LEAD_S, the room for the first syllable's consonant before the
            // beat (the WAV's time VOICE_LEAD_S is the score's 0; docs/singing-design.md §3).
            const start = round(n.T * k + VOICE_LEAD_S), end = round((n.T + n.D) * k + VOICE_LEAD_S);
            const note: CantoriaNote = { start, duration: round(end - start), pitch: n.P };
            if (n.M) { note.melisma = true; } else if (n.L) { note.lyric = n.L; }
            if (n.B) { note.breath = true; }
            if (n.V != null) { note.level = n.V; }
            if (n.E != null) { note.levelEnd = n.E; }
            if (n.I) { note.inhale = true; }
            if (n.H) { note.hyphen = true; }
            return note;
        }),
    };
}

/** Milliseconds the text must rest before an edited voice is sent to the engine (owner's decision
 *  2026-11-08, session 1148): typing never asks the engine; a pause does. */
export const PREPARE_IDLE_MS = 1500;

/** How long the engine may take over one voice before the preview gives it up as `failed`
 *  (the microphone pulsed for ever over an engine that never answered; a long score on the
 *  CPU's DiffSinger takes tens of seconds, so two minutes). */
export const PREPARE_TIMEOUT_MS = 120_000;

/** When an edited voice is prepared: as the text rests (`afterEdit`, the default), or only when
 *  Play is pressed (`onPlay`: for a slow engine — the CPU's DiffSinger — where every pause would
 *  cost seconds of rendering; that Play plays the instruments). The setting `lilysharp.playback.singerPrepare`. */
export type PrepareMode = 'afterEdit' | 'onPlay';
export function prepareModeOf(setting: unknown): PrepareMode {
    return setting === 'onPlay' ? 'onPlay' : 'afterEdit';
}

/** The timers a PrepareQueue runs on — the host's, or a test's. */
export interface PrepareTimers { setTimeout(fn: () => void, ms: number): unknown; clearTimeout(handle: unknown): void }

/**
 * One job at a time per slot, the newest winning (docs/singing-design.md §5 "When the voice is
 * prepared"): a job scheduled with a delay waits that long after the LAST schedule of its slot
 * (typing resets it); one scheduled while another runs waits its turn, and only the newest one
 * waiting runs then — the engine sings each phrase at most twice over an edit, not once per
 * keystroke. `cancel` forgets a slot's waiting job (a preview turned to another document).
 */
export class PrepareQueue<J> {
    private readonly slots = new Map<string, { timer?: unknown; waiting?: J; running: boolean }>();

    constructor(private readonly run: (job: J) => Promise<void>,
        private readonly timers: PrepareTimers = { setTimeout: (fn, ms) => setTimeout(fn, ms), clearTimeout: h => clearTimeout(h as ReturnType<typeof setTimeout>) }) {}

    schedule(slot: string, job: J, delayMs: number): void {
        let s = this.slots.get(slot);
        if (!s) { this.slots.set(slot, s = { running: false }); }
        if (s.timer !== undefined) { this.timers.clearTimeout(s.timer); s.timer = undefined; }
        s.waiting = job;
        if (delayMs > 0) { s.timer = this.timers.setTimeout(() => { s.timer = undefined; this.fire(slot); }, delayMs); }
        else { this.fire(slot); }
    }

    /** True while a newer job of `slot` waits (its timer, or its turn): a result landing now is of an older text. */
    hasWaiting(slot: string): boolean {
        const s = this.slots.get(slot);
        return !!s && s.waiting !== undefined;
    }

    cancel(slotPrefix: string): void {
        for (const [slot, s] of this.slots) {
            if (!slot.startsWith(slotPrefix)) { continue; }
            if (s.timer !== undefined) { this.timers.clearTimeout(s.timer); s.timer = undefined; }
            s.waiting = undefined;
        }
    }

    /** Forgets one slot's waiting job, this slot alone (`p1/melody`, not `p1/melody2`). */
    cancelSlot(slot: string): void {
        const s = this.slots.get(slot);
        if (!s) { return; }
        if (s.timer !== undefined) { this.timers.clearTimeout(s.timer); s.timer = undefined; }
        s.waiting = undefined;
    }

    private fire(slot: string): void {
        const s = this.slots.get(slot);
        if (!s || s.running || s.waiting === undefined) { return; }
        const job = s.waiting;
        s.waiting = undefined;
        s.running = true;
        this.run(job).catch(() => undefined).then(() => {
            s.running = false;
            if (s.waiting !== undefined && s.timer === undefined) { this.fire(slot); }
        });
    }
}

/** What a rendered voice is cached and named by: the voice and the score's bytes, so the same
 *  phrase at the same speed in the same voice is sung once per machine. */
export function voiceKey(score: CantoriaScore, voice: string): string {
    return createHash('sha1').update(voice).update('\n').update(JSON.stringify(score)).digest('hex');
}

/** One line of `cantoria serve`'s stdout. */
export interface ServeAnswer {
    ready?: boolean; voices?: string[];
    id?: string; wav?: string; duration?: number; sampleRate?: number; seconds?: number; error?: string;
}

/** The answer a line holds, or null for a line that is not one (the engine's own chatter). */
export function parseServeLine(line: string): ServeAnswer | null {
    const text = line.trim();
    if (!text.startsWith('{')) { return null; }
    try {
        const parsed: unknown = JSON.parse(text);
        return parsed && typeof parsed === 'object' ? parsed as ServeAnswer : null;
    } catch {
        return null;
    }
}

/** One request of `cantoria serve`'s stdin: the score, where the WAV goes, and the engine's
 *  default voice (its `--voice`, else MERROW) — Lily# names none. */
export function serveRequestLine(id: string, score: CantoriaScore, output: string, voice?: string): string {
    return JSON.stringify(voice ? { id, score, voice, output } : { id, score, output }) + '\n';
}
