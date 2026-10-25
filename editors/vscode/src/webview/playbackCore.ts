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

// The preview player's pure half: the server's note list (lilysharp/playback, seconds)
// turned into the MIDI events the SoundFont synth is sent, in order; a cursor that hands
// them out a little ahead of the clock; and the cursor the follow-along highlight reads.
// No DOM, no WebAudio — the node tests drive it (test/playbackCore.test.ts). The browser
// half is previewSynth.ts; docs/playback-design.md is the design.

/** One note as the server sends it (LspProtocolDtos.PlaybackNote): seconds, a MIDI
 *  pitch (fractional for a quarter tone), velocity, the source position and the printed
 *  copy's ordinal for the highlight, the oscillator patches' timbre family, and the
 *  channel and General MIDI program the .mid export plays it with. */
export interface PlaybackNote {
    T: number; D: number; P: number; V: number;
    S: number; O?: number; I?: number; C?: number; G?: number;
    /** The note's track, an index into the response's Parts. */
    N?: number;
}

/** A metronome beat as the server sends it: seconds, and whether it opens a bar. */
export interface PlaybackBeat { T: number; A: boolean }

/** How the practice menu plays the score: its tempo (1 = as written), the parts silenced
 *  (by track name), and whether the metronome clicks. */
export interface Practice { speed: number; muted: readonly string[]; click: boolean }

/** The metronome's sounds, GM drum keys: the high wood block on a bar's first beat, the low
 *  one on the others. */
export const CLICK_ACCENT = 76;
export const CLICK_BEAT = 77;

/**
 * The notes the preview plays under the practice menu (owner's request 2026-10-25): the muted
 * parts' notes left out, every time stretched to the tempo (80% plays each second in 1.25),
 * and, with the metronome on, a wood-block click on every beat. The clicks carry no source
 * position, so nothing lights for them.
 */
export function practiceNotes(notes: readonly PlaybackNote[], parts: readonly string[] | null,
    beats: readonly PlaybackBeat[] | null, practice: Practice): PlaybackNote[] {
    const muted = new Set(practice.muted);
    const k = 1 / (practice.speed > 0 ? practice.speed : 1);
    const out = notes
        .filter(n => !(parts && typeof n.N === 'number' && muted.has(parts[n.N])))
        .map(n => k === 1 ? n : { ...n, T: n.T * k, D: n.D * k });
    if (practice.click && beats) {
        for (const b of beats) {
            out.push({ T: b.T * k, D: 0.08, P: b.A ? CLICK_ACCENT : CLICK_BEAT, V: b.A ? 120 : 95,
                S: -1, O: 0, I: 9, C: DRUM_CHANNEL, G: 0, N: -1 });
        }
        out.sort((a, b) => a.T - b.T);
    }
    return out;
}

export type MidiEvent =
    | { t: number; kind: 'program'; c: number; g: number }
    | { t: number; kind: 'bend'; c: number; value: number }
    | { t: number; kind: 'on'; c: number; p: number; v: number }
    | { t: number; kind: 'off'; c: number; p: number };

/** The General MIDI drum channel (0-based). */
export const DRUM_CHANNEL = 9;
/** The pitch wheel at rest, on its 14-bit scale. */
export const BEND_CENTRE = 8192;
/** One semitone of the wheel at the synth's default range (±2 semitones over 0..16383). */
export const BEND_PER_SEMITONE = 4096;
/** Below this, a fractional pitch is the integer one (floating-point dust, not a quarter tone). */
const BEND_EPSILON = 0.01;

/**
 * The program a timbre family sounds with when no part names one (the oscillator
 * patches' families, PlaybackNote.I / MidiNote.Timbre: 0 piano, 1 flute, 2 reed,
 * 3 strings, 4 guitar, 5 bass, 6 brass, 7 organ, 8 voice, 9 drums): the nearest GM voice.
 */
export const FAMILY_PROGRAM: readonly number[] = [0, 73, 71, 48, 24, 33, 61, 19, 52, 0];

/** The channel a note plays on: the server's, else the drum channel for the drum family,
 *  else the first. */
export function channelOf(note: { C?: number; I?: number }): number {
    if (typeof note.C === 'number' && note.C >= 0 && note.C <= 15) { return note.C; }
    return note.I === 9 ? DRUM_CHANNEL : 0;
}

/** The program a note (or an audition) sounds with: the server's when it named one
 *  (0-127), else the timbre family's. */
export function programOf(program: number | undefined, timbre: number | undefined): number {
    if (typeof program === 'number' && program >= 0 && program <= 127) { return program; }
    const t = typeof timbre === 'number' ? timbre : 0;
    return FAMILY_PROGRAM[t] ?? 0;
}

/** The order of events at one instant: a note's off before its own re-strike, the
 *  programs and the bends before the notes that need them. */
const KIND_ORDER: Record<MidiEvent['kind'], number> = { off: 0, program: 1, bend: 2, on: 3 };

/**
 * The events of `notes` from `offset` (seconds into the piece) on, timed from the
 * offset. Notes already past the offset are dropped whole (re-striking half a note reads
 * worse than starting cleanly at the next onset). Each pitched channel's program is set
 * once, at the start; a quarter-tone note bends its channel's wheel for its duration and
 * centres it after (one wheel per channel: a quarter tone sounding against a plain note
 * on the same channel bends both — the .mid export has the same limit).
 */
export function toEvents(notes: readonly PlaybackNote[], offset = 0): MidiEvent[] {
    const events: MidiEvent[] = [];
    const programs = new Map<number, number>();
    for (const n of notes) {
        if (n.T < offset - 0.001) { continue; }
        const c = channelOf(n);
        if (c !== DRUM_CHANNEL && !programs.has(c)) { programs.set(c, programOf(n.G, n.I)); }
        // To the microsecond: a note's end and the next one's start that are one instant can
        // differ in the last bit (a tempo the practice menu stretched, 2026-10-25), and the off
        // sorted after the re-strike silenced it — a repeated note went missing.
        const t = micro(Math.max(0, n.T - offset));
        const off = micro(t + Math.max(0.03, n.D));
        // The key below the pitch, bent UP by the fraction — the shape the .mid export
        // writes (a key and a positive wheel), not Math.round's coin toss at .5.
        const key = Math.floor(n.P + BEND_EPSILON);
        const p = Math.max(0, Math.min(127, key));
        const frac = n.P - key;
        const v = Math.max(1, Math.min(127, Math.round(n.V)));
        if (Math.abs(frac) >= BEND_EPSILON) {
            const value = Math.max(0, Math.min(16383, Math.round(BEND_CENTRE + frac * BEND_PER_SEMITONE)));
            events.push({ t, kind: 'bend', c, value });
            events.push({ t: off, kind: 'bend', c, value: BEND_CENTRE });
        }
        events.push({ t, kind: 'on', c, p, v });
        events.push({ t: off, kind: 'off', c, p });
    }
    for (const [c, g] of programs) { events.push({ t: 0, kind: 'program', c, g }); }
    // A stable sort keeps a note's bend before its on (pushed in that order) at equal t.
    return events
        .map((e, i) => ({ e, i }))
        .sort((a, b) => a.e.t - b.e.t || KIND_ORDER[a.e.kind] - KIND_ORDER[b.e.kind] || a.i - b.i)
        .map(x => x.e);
}

/** A time to the microsecond (toEvents). */
const micro = (t: number) => Math.round(t * 1e6) / 1e6;

/** When the last note from `offset` on ends, in seconds from the piece's start. */
export function endOf(notes: readonly PlaybackNote[], offset = 0): number {
    let end = offset;
    for (const n of notes) {
        if (n.T < offset - 0.001) { continue; }
        end = Math.max(end, n.T + Math.max(0.03, n.D));
    }
    return end;
}

/**
 * The notes a Play of a stretch sounds, from 0 (owner's request 2026-10-25): the stretch the
 * score selection [lo, hi] covers (source offsets; null for the whole piece) where it FIRST
 * plays — every part sounding then, not only the selected staff's — and, with `once` (the
 * header's loop on), without the repeats inside it: a note playing a printed note again (its
 * S and O already sounded) is dropped and the time it took closed up, so the loop goes round
 * the music as written, once.
 */
export function playSpan(notes: readonly PlaybackNote[], lo: number | null, hi: number | null, once: boolean): PlaybackNote[] {
    return playSpanWindow(notes, lo, hi, once).notes;
}

/**
 * {@link playSpan}, and how long the stretch lasts — what a loop's round is. With `bars` (the
 * times the bars begin, ascending) the stretch runs from the start of the bar its first note
 * is in to the start of the bar after its last note's end, so a round is whole bars, a rest at
 * either end included (owner's report 2026-10-25); without, from its first note to the end of
 * its last.
 */
export function playSpanWindow(notes: readonly PlaybackNote[], lo: number | null, hi: number | null, once: boolean,
    bars?: readonly number[]): { notes: PlaybackNote[]; length: number } {
    const key = (n: PlaybackNote) => n.S + ':' + (n.O ?? 0);
    const sorted = [...notes].sort((a, b) => a.T - b.T);
    // A printed note's first sounding; a later one is a repeat playing it again.
    const first = new Set<PlaybackNote>();
    const seen = new Set<string>();
    for (const n of sorted) {
        if (n.S < 0 || !seen.has(key(n))) { first.add(n); }
        if (n.S >= 0) { seen.add(key(n)); }
    }
    const inSpan = (n: PlaybackNote) => lo === null || hi === null || (n.S >= lo && n.S <= hi);
    let t0 = Infinity, t1 = -Infinity;
    for (const n of sorted) {
        if (!first.has(n) || !inSpan(n)) { continue; }
        t0 = Math.min(t0, n.T);
        t1 = Math.max(t1, n.T + n.D);
    }
    if (t0 === Infinity) { return { notes: [], length: 0 }; }
    if (bars && bars.length > 0) {
        let start = -Infinity, end = Infinity;
        for (const b of bars) {
            if (b <= t0 + 1e-6) { start = b; }
            if (b >= t1 - 1e-6 && end === Infinity) { end = b; }
        }
        if (start > -Infinity) { t0 = start; }
        if (end < Infinity) { t1 = end; }
    }
    const window = sorted.filter(n => n.T >= t0 - 1e-6 && n.T < t1 - 1e-6);
    if (!once) { return { notes: window.map(n => ({ ...n, T: n.T - t0 })), length: t1 - t0 }; }

    // The repeats' time: the merged spans the dropped notes sound in.
    const gone: [number, number][] = [];
    for (const n of window) {
        if (first.has(n)) { continue; }
        const last = gone[gone.length - 1];
        if (last && n.T <= last[1] + 1e-6) { last[1] = Math.max(last[1], n.T + n.D); }
        else { gone.push([n.T, n.T + n.D]); }
    }
    const cut = (t: number) => {
        let s = 0;
        for (const [a, b] of gone) {
            if (b <= t + 1e-6) { s += b - a; }
            else if (a < t) { s += t - a; }
        }
        return s;
    };
    // A note of no printed place (a metronome click) goes with the time it falls in.
    const inGone = (t: number) => gone.some(([a, b]) => t >= a - 1e-6 && t < b - 1e-6);
    return {
        notes: window.filter(n => first.has(n) && !(n.S < 0 && inGone(n.T)))
            .map(n => ({ ...n, T: n.T - t0 - cut(n.T) })),
        length: t1 - t0 - cut(t1),
    };
}

/**
 * A loop laid out ahead (owner's report 2026-10-25): `list` (from 0) one round after another,
 * each round as long as the list sounds, until `seconds` are filled — so the player's own clock
 * times every round. Restarting at a page timer's word went wrong on the MIDI output, where
 * the browser holds the page's timers to a second and a round lost or gained up to three beats.
 */
export function loopNotes(list: readonly PlaybackNote[], seconds: number, round?: number): PlaybackNote[] {
    // The round: as given (whole bars, playSpanWindow), else as long as the list sounds.
    let length = round ?? 0;
    if (!(length > 0)) { for (const n of list) { length = Math.max(length, n.T + n.D); } }
    if (length <= 0) { return [...list]; }
    const rounds = Math.max(1, Math.min(500, Math.ceil(seconds / length)));
    const out: PlaybackNote[] = [];
    for (let r = 0; r < rounds; r++) {
        for (const n of list) { out.push(r === 0 ? n : { ...n, T: n.T + r * length }); }
    }
    return out;
}

/** The channels `events` touch — what a stop has to silence. */
export function channelsOf(events: readonly MidiEvent[]): number[] {
    const set = new Set<number>();
    for (const e of events) { set.add(e.c); }
    return [...set].sort((a, b) => a - b);
}

/**
 * Hands out the events whose time has come within a horizon: `take(upTo)` returns every
 * event not yet taken with t <= upTo, in order. The player calls it every few tens of
 * milliseconds with the clock plus a short lookahead, so the synth holds a few hundred
 * milliseconds of events and never the whole piece (the oscillator player built every
 * node of the piece up front, and a dense piece dropped out — owner's report 2026-10-11).
 */
export class Lookahead {
    private next = 0;
    constructor(private readonly events: readonly MidiEvent[]) { }

    take(upTo: number): MidiEvent[] {
        const out: MidiEvent[] = [];
        while (this.next < this.events.length && this.events[this.next].t <= upTo) {
            out.push(this.events[this.next++]);
        }
        return out;
    }

    get done(): boolean { return this.next >= this.events.length; }
    /** The time of the last event handed out so far (-1 before any). */
    get handedOutUntil(): number { return this.next === 0 ? -1 : this.events[this.next - 1].t; }
}

/** A note the follow-along highlight can light: its source position and printed copy. */
export interface SoundingNote { t: number; d: number; s: number; o: number }

export interface Sounding { pos: number; occ: number; t: number; key: string }

/**
 * The notes SOUNDING at a moment, newest onset first, one entry per printed note (a
 * quarter stays lit while the other hand's eighths change under it). Keeps a cursor into
 * the onset order, so a 50 ms tick costs the notes that started since the last one plus
 * the notes still ringing — not the whole piece, as scanning the list every tick did.
 */
export class SoundingCursor {
    private readonly notes: SoundingNote[];
    private next = 0;
    private active: SoundingNote[] = [];
    private lastKeys = '';

    constructor(notes: readonly SoundingNote[]) {
        this.notes = notes.filter(n => n.s >= 0).slice().sort((a, b) => a.t - b.t);
    }

    /** The sounding set at `elapsed`, and whether it differs from the last call's
     *  (`changed`) with an onset not lit before (`hadNew` — the scroll target). */
    at(elapsed: number): { sounding: Sounding[]; changed: boolean; hadNew: boolean } {
        while (this.next < this.notes.length && this.notes[this.next].t <= elapsed) {
            this.active.push(this.notes[this.next++]);
        }
        this.active = this.active.filter(n => elapsed < n.t + n.d);
        const sounding: Sounding[] = [];
        const seen = new Set<string>();
        for (const n of this.active) {
            const key = n.s + ':' + n.o;
            if (seen.has(key)) { continue; }
            seen.add(key);
            sounding.push({ pos: n.s, occ: n.o, t: n.t, key });
        }
        sounding.sort((a, b) => b.t - a.t);
        const keys = sounding.map(x => x.key).sort().join(',');
        const changed = keys !== this.lastKeys;
        const hadNew = changed && sounding.some(x => this.lastKeys.split(',').indexOf(x.key) < 0);
        this.lastKeys = keys;
        return { sounding, changed, hadNew };
    }
}
