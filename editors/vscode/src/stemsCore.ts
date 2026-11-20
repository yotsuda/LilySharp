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

// A .lys's sound handed out one stem at a time, for another extension to mix (docs/stems-api-design.md,
// owner's decisions 2026-11-20: Shosa refers to the .lys, plays it with a mixer of its own, and the
// recording beside the chart joins in the same way). The pure half: what the API says, the parts'
// notes apart, the files' keys and the float WAV the stems are kept in.
//
// No vscode here: the extension and the tests share it.

import { createHash } from 'crypto';
import { PlaybackNote } from './webview/playbackCore';

/** The shape's version: raised when it changes (a reader reads the ones it knows). */
export const STEMS_VERSION = 1;

export type StemKind = 'part' | 'voice' | 'recording' | 'recordingPart';

export interface Stem {
    kind: StemKind;
    /** The mixer's line: the score's part or voice, or the name in the file beside the chart's `parts`. */
    name: string;
    /** The sound's file, absolute — a WAV Lily# keeps (a part, a voice) or the recording's own file. */
    file: string;
    /** Where the file's sample 0 sounds, in the timeline's seconds; may be below 0 (a voice begins
     *  0.3 s early, for its first consonant). */
    at: number;
}

export interface Stems {
    /** 'recording': every stem on the recording's seconds (its second 0 is 0); 'score': on the score's,
     *  at its written tempo (the seconds `lysc timeline` gives). */
    timeline: 'recording' | 'score';
    stems: Stem[];
    /** What could not be made, in words (no voice can sing a line, the recording is missing, …):
     *  the rest sounds without it. */
    notes: string[];
}

/**
 * The score's notes each part plays, in the parts' order: a part's notes and its pedal (both carry
 * the part's track, N). Notes with no track (a metronome's click) are no part's. A part with nothing
 * left to play — sung only, its voice in its place — is left out.
 */
export function notesByPart(notes: readonly PlaybackNote[], parts: readonly string[] | null | undefined): { name: string; notes: PlaybackNote[] }[] {
    const out: { name: string; notes: PlaybackNote[] }[] = [];
    if (!parts) {
        return out;
    }
    const by = new Map<number, PlaybackNote[]>();
    for (const n of notes) {
        if (typeof n.N !== 'number' || n.N < 0 || n.N >= parts.length) {
            continue;
        }
        const list = by.get(n.N);
        if (list) { list.push(n); } else { by.set(n.N, [n]); }
    }
    parts.forEach((name, i) => {
        const list = by.get(i);
        if (list && list.some(n => !(n.P < 0))) {
            out.push({ name, notes: list });
        }
    });
    return out;
}

/**
 * The stems in the order the page stacks them (Shosa's request 5, owner's decision 2026-11-20: a
 * mixer's lines go as the .lys writes its score): each part where the score's rows name it
 * (`rows`, PlaybackResponse.Rows — a chord track's part is its row's name with " (chords)" after
 * it), each voice right after its part's (`melody`, `melody#2` after `melody`), or where its part
 * would stand when the part sings only; a part or voice no row names after those, in the order
 * given (a score with no rows: the parts' own order, which is the page's then). The recording and
 * its parts last, as given.
 */
export function inScoreOrder(stems: readonly Stem[], rows: readonly string[] | null | undefined): Stem[] {
    const at = new Map<string, number>();
    (rows ?? []).forEach((r, i) => { if (!at.has(r)) { at.set(r, i); } });
    const rowOf = (name: string): number | undefined => at.get(name) ?? at.get(name.replace(/ \(chords\)$/, ''));
    // (a part no row names keeps its place among such parts, and its voices go with it)
    const given = (base: string, i: number) => { const p = stems.findIndex(x => x.kind === 'part' && x.name === base); return p >= 0 ? p : i; };
    const keyed = stems.map((s, i) => {
        const recorded = s.kind === 'recording' || s.kind === 'recordingPart';
        const base = s.kind === 'voice' ? s.name.split('#')[0] : s.name;
        const row = recorded ? undefined : rowOf(base);
        return { s, group: recorded ? 2 : row === undefined ? 1 : 0, row: row ?? (recorded ? 0 : given(base, i)),
            // (a part's voices by their number: `melody` is the first, `melody#2` the second)
            voice: s.kind === 'voice' ? Number(s.name.split('#')[1] ?? 1) || 1 : 0, i };
    });
    keyed.sort((a, b) => a.group - b.group || a.row - b.row || a.voice - b.voice || a.i - b.i);
    return keyed.map(k => k.s);
}

/** The name of a part's kept WAV: the same notes through the same sound font are the same sound. */
export function stemFileName(notes: readonly PlaybackNote[], soundFont: string): string {
    const h = createHash('sha1');
    h.update(`lilysharp-stem-1\n${soundFont}\n`);
    for (const n of notes) {
        h.update(`${n.T},${n.D},${n.P},${n.V},${n.C ?? 0},${n.G ?? 0}\n`);
    }
    return `${h.digest('hex')}.wav`;
}

/** Two channels as a 32-bit float WAV: nothing clipped, nothing scaled — a part's level is the
 *  synth's as the preview hears it, so the parts keep their balance when they are summed. */
export function floatWav(left: Float32Array, right: Float32Array, sampleRate: number): Uint8Array {
    const frames = Math.min(left.length, right.length);
    const bytes = new Uint8Array(44 + frames * 8);
    const v = new DataView(bytes.buffer);
    const text = (at: number, s: string) => { for (let i = 0; i < s.length; i++) { bytes[at + i] = s.charCodeAt(i); } };
    text(0, 'RIFF'); v.setUint32(4, 36 + frames * 8, true); text(8, 'WAVE');
    text(12, 'fmt '); v.setUint32(16, 16, true);
    v.setUint16(20, 3, true);                  // IEEE float
    v.setUint16(22, 2, true);
    v.setUint32(24, sampleRate, true);
    v.setUint32(28, sampleRate * 8, true);
    v.setUint16(32, 8, true);
    v.setUint16(34, 32, true);
    text(36, 'data'); v.setUint32(40, frames * 8, true);
    for (let i = 0; i < frames; i++) {
        v.setFloat32(44 + i * 8, left[i], true);
        v.setFloat32(48 + i * 8, right[i], true);
    }
    return bytes;
}

/** The sample rates the MP3 writer takes (MPEG-1 Layer III). */
export const MP3_RATES: readonly number[] = [32000, 44100, 48000];

/** What is wrong with a mix handed to writeAudio, or null. */
export function mixProblem(mix: { left?: unknown; right?: unknown; sampleRate?: unknown }, format: string): string | null {
    if (!(mix.left instanceof Float32Array) || !(mix.right instanceof Float32Array)) {
        return 'left and right must be Float32Arrays';
    }
    if (mix.left.length !== mix.right.length) {
        return 'left and right must be as long';
    }
    const rate = mix.sampleRate;
    if (typeof rate !== 'number' || !Number.isInteger(rate) || rate < 8000 || rate > 192000) {
        return 'sampleRate must be a whole number of hertz';
    }
    if (format !== 'mp3' && format !== 'wav') {
        return `format must be "mp3" or "wav", not "${format}"`;
    }
    if (format === 'mp3' && !MP3_RATES.includes(rate)) {
        return `an MP3 is written at ${MP3_RATES.join(', ')} Hz, not ${rate}`;
    }
    return null;
}
