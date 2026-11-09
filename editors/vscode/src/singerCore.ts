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
// into the score the singing engine (Cantilena) reads, the key a rendered voice is cached by,
// and the lines of `cantilena serve`'s wire. No vscode, no process here: test/singerCore.test.ts
// drives it under node.

import { createHash } from 'crypto';
import { VOICE_LEAD_S } from './webview/playbackCore';

/** One note of `lilysharp/sung`, in seconds: its syllable `L` (null on a melisma), `M` a melisma
 *  carrying the previous syllable's vowel on, `B` a breath after it, `S` the source offset. */
export interface SungNoteDto { T: number; D: number; P: number; L?: string | null; M?: boolean; B?: boolean; S?: number }

/** A part's words on its notes, as the server answers `lilysharp/sung`. */
/** `InScore`: the score shown sings this part itself; false for a voice the file's other lyrics could sing (off until the menu turns it on; session 1163). */
export interface SungVoiceDto { Part: string; Language: string; Notes: SungNoteDto[]; Warnings?: string[] | null; InScore?: boolean; Singer?: string | null }

/** The score Cantilena reads (its README): seconds, MIDI pitch, the syllable as pronounced. */
export interface CantilenaNote { start: number; duration: number; pitch: number; lyric?: string; melisma?: boolean; breath?: boolean }
export interface CantilenaScore { sampleRate: number; language: string; notes: CantilenaNote[] }

/** The rate the voice is asked for — NEUTRINO's own, so nothing is resampled on the way. */
export const VOICE_SAMPLE_RATE = 48000;

/** The Cantilena extension, the engine's own home on the Marketplace (docs/singing-design.md §5). */
export const CANTILENA_EXTENSION_ID = 'yotsuda.cantilena';

/** A voice the Cantilena extension lists: `id` is `engine/name` (`neutrino/MERROW`), what its
 *  `sing` takes and its setting holds. */
export interface CantilenaVoice { id: string; engine: string; name: string; label?: string; language?: string; licence?: string }

/** What that extension's `activate` returns, as far as Lily# reads it (its README, "For extension
 *  authors"). `status().voice` is the setting's voice as `engine/name`; `voices()` came with
 *  Cantilena 0.2 and an older extension has none. */
export interface CantilenaApi {
    readonly version: string;
    readonly apiVersion: number;
    status(): Promise<{ ready: boolean; voice: string; problems: string[] }>;
    voices?(): Promise<CantilenaVoice[]>;
    sing(score: CantilenaScore, options?: { voice?: string }): Promise<{ wav: string; duration: number; cached?: boolean }>;
    release(): void;
    /** The voice a `.lys` file's `singer` string comes to on this machine, and what to tell the
     *  writer when it was not the one asked for. Cantilena 0.4 on. */
    resolveSinger?(singer: string): Promise<{ voice: string; note?: string }>;
    /** The roles this machine casts (`cantilena.voices`): role or voice → voice. */
    cast?(): Promise<Record<string, string>>;
}

/** One choice between a `singer`'s quotes. */
export interface SingerChoice { label: string; detail: string; isRole: boolean }

/**
 * What the editor offers between a `singer`'s quotes (owner's request 2026-11-09, session 1168:
 * only the names right for the language): the installed voices that sing `language`, then the
 * roles cast to one. A voice that does not say what it sings is left out.
 */
export function singerChoices(voices: readonly CantilenaVoice[], cast: Readonly<Record<string, string>>, language: string): SingerChoice[] {
    const sings = voices.filter(v => v.language === language);
    const out: SingerChoice[] = sings.map(v => ({ label: v.id, detail: v.label && v.label !== v.name ? v.label : v.name, isRole: false }));
    for (const [role, bound] of Object.entries(cast)) {
        if (role.includes('/')) { continue; }
        const voice = sings.find(v => v.id === bound || v.name === bound);
        if (voice) { out.push({ label: role, detail: `role: ${voice.id}`, isRole: true }); }
    }
    return out;
}

/** The API from an extension's exports, or undefined when they are not that shape. */
export function cantilenaApiOf(exports: unknown): CantilenaApi | undefined {
    const e = exports as Partial<CantilenaApi> | undefined;
    return e && typeof e.sing === 'function' && typeof e.status === 'function' && e.apiVersion === 1 ? e as CantilenaApi : undefined;
}

const round = (x: number): number => Math.round(x * 1e4) / 1e4;

/**
 * The engine's score of a voice at the practice menu's `speed`: the notes' seconds divided by
 * it (a voice cannot be stretched after the fact without its pitch moving, so a speed change
 * is a render of its own), a melisma with no lyric, a breath where the server put one.
 */
export function cantilenaScore(voice: SungVoiceDto, speed: number): CantilenaScore {
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
            const note: CantilenaNote = { start, duration: round(end - start), pitch: n.P };
            if (n.M) { note.melisma = true; } else if (n.L) { note.lyric = n.L; }
            if (n.B) { note.breath = true; }
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
export function voiceKey(score: CantilenaScore, voice: string): string {
    return createHash('sha1').update(voice).update('\n').update(JSON.stringify(score)).digest('hex');
}

/** One line of `cantilena serve`'s stdout. */
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

/** One request of `cantilena serve`'s stdin: the score, where the WAV goes, and the engine's
 *  default voice (its `--voice`, else MERROW) — Lily# names none. */
export function serveRequestLine(id: string, score: CantilenaScore, output: string, voice?: string): string {
    return JSON.stringify(voice ? { id, score, voice, output } : { id, score, output }) + '\n';
}
