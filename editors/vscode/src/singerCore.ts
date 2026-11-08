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

/** One note of `lilysharp/sung`, in seconds: its syllable `L` (null on a melisma), `M` a melisma
 *  carrying the previous syllable's vowel on, `B` a breath after it, `S` the source offset. */
export interface SungNoteDto { T: number; D: number; P: number; L?: string | null; M?: boolean; B?: boolean; S?: number }

/** A part's words on its notes, as the server answers `lilysharp/sung`. */
export interface SungVoiceDto { Part: string; Language: string; Notes: SungNoteDto[]; Warnings?: string[] | null }

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
            const start = round(n.T * k), end = round((n.T + n.D) * k);
            const note: CantilenaNote = { start, duration: round(end - start), pitch: n.P };
            if (n.M) { note.melisma = true; } else if (n.L) { note.lyric = n.L; }
            if (n.B) { note.breath = true; }
            return note;
        }),
    };
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
export function serveRequestLine(id: string, score: CantilenaScore, output: string): string {
    return JSON.stringify({ id, score, output }) + '\n';
}
