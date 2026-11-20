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

/** A note of `lilysharp/playback`: its onset and length in the score's seconds, and where it is
 *  written (S, a source offset). */
export interface TimedNote { T: number; D: number; S?: number; N?: number }

/**
 * Where Play would start, in the score's seconds (the header's position while nothing plays —
 * owner's request 2026-11-16): the preview's own rule for a Play from the caret. `exact`: the
 * note the caret resolved to — its first onset (the earliest of its copies a repeat plays).
 * Otherwise, or when that note sounds nowhere (a mark, a rest), the first onset of the nearest
 * note written at or after `position`. 0 for no place (the top of the score) and for nothing
 * after it.
 */
export function startSecondsAt(notes: readonly TimedNote[], position: number, exact: boolean): number {
    if (!(position >= 0)) {
        return 0;
    }
    let first = Infinity;
    if (exact) {
        for (const n of notes) {
            if (n.S === position && n.T < first) {
                first = n.T;
            }
        }
    }
    if (first === Infinity) {
        let bestS = Infinity;
        for (const n of notes) {
            const s = n.S;
            if (typeof s === 'number' && s >= position && (s < bestS || (s === bestS && n.T < first))) {
                bestS = s;
                first = n.T;
            }
        }
    }
    return first === Infinity ? 0 : first;
}

/** How long the text rests after an edit before the header's notes are asked for again. */
export const SOUNDING_IDLE_MS = 300;

/**
 * How long to wait yet before asking for the sounding notes (the header's length and its position
 * while stopped): until the text has rested SOUNDING_IDLE_MS since its last edit. 0: ask now — a
 * text never edited, or one that has rested. The notes are an export of the whole score, and
 * asked with every refresh they ran beside the next keystroke's render (review of session 1291).
 */
export function soundingWait(lastEditAt: number | undefined, now: number): number {
    return lastEditAt === undefined ? 0 : Math.max(0, lastEditAt + SOUNDING_IDLE_MS - now);
}

/** The notes that sound: the parts the score leaves out (`extra`, by name) dropped. */
export function soundingOf(notes: readonly TimedNote[], parts: readonly string[] | null | undefined,
    extra: readonly string[] | null | undefined): TimedNote[] {
    const left = new Set(extra ?? []);
    return notes.filter(n => !(parts && typeof n.N === 'number' && left.has(parts[n.N])));
}

/** The parts that sound a note (`N` indexes `parts`), in score order: a `part { }` with no music
 *  yet is not among them. */
export function soundingPartsOf(notes: readonly TimedNote[], parts: readonly string[] | null | undefined): string[] {
    if (!parts) { return []; }
    const sounding = new Set(notes.map(n => n.N));
    return parts.filter((p, i) => sounding.has(i));
}
