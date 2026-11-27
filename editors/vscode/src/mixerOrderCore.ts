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

// The order of the mixer's Score lines (owner's request 2026-11-27): as the .lys first names each
// one, not as the score stacks its staves. A part or chords row by its declaration
// (`part vocal { … }`, `chords prog {}`); one with no declaration by its first block in a section
// (`kit { … }`); a voice by its `singer` line, else just after its part. So
//
//   chords prog {}
//   part vocal { instrument tenor  lyrics words }
//   singer on vocal
//   part bass { instrument bass }
//   part kit { clef percussion }
//
// lists prog, vocal, vocal's voice, bass, kit.
//
// No vscode here: the extension and the tests share it.

/** The track name of a chords row (MidiExporter's ChordRowTrackSuffix): `prog (chords)`. */
const CHORD_ROW_SUFFIX = ' (chords)';

/** The text with its comments and strings blanked, every offset kept. */
function blankCommentsAndStrings(text: string): string {
    return text.replace(/\/\*[\s\S]*?\*\/|\/\/[^\n]*|"(?:[^"\\\n]|\\.)*"/g, m => m.replace(/[^\n]/g, ' '));
}

/** Where each name is first written: as a declaration (`part x`, `chords x`) or a block (`x {`),
 *  whichever comes first in the file. */
export function firstAppearances(text: string): Map<string, number> {
    const clean = blankCommentsAndStrings(text);
    const out = new Map<string, number>();
    const name = '[\\p{L}_][\\p{L}\\p{N}_]*';
    const re = new RegExp(`(?<![\\p{L}\\p{N}_])(?:(?:part|chords)\\s+(${name})|(${name})\\s*\\{)`, 'gu');
    for (const m of clean.matchAll(re)) {
        const n = m[1] ?? m[2];
        if (!out.has(n)) { out.set(n, m.index); }
    }
    return out;
}

/** A voice of the mixer: its key, its part, and where its singer line is (null: none of its own). */
export interface MixerVoice { readonly key: string; readonly part: string; readonly singerStart?: number | null }

/** Each line's place, for sorting: parts (track names) and voices (keys) apart, as their keys can
 *  be the same word. A name the file does not write keeps no place (it sorts after, in its order). */
export function mixerRanks(text: string, parts: readonly string[], voices: readonly MixerVoice[]):
    { parts: Record<string, number>; voices: Record<string, number> } {
    const first = firstAppearances(text);
    const partRank = (track: string): number | undefined =>
        first.get(track.endsWith(CHORD_ROW_SUFFIX) ? track.slice(0, -CHORD_ROW_SUFFIX.length) : track);
    const out = { parts: {} as Record<string, number>, voices: {} as Record<string, number> };
    for (const p of parts) {
        const r = partRank(p);
        if (r !== undefined) { out.parts[p] = r; }
    }
    for (const v of voices) {
        // (just after its part: the part's place and a half, before whatever the next character starts)
        const r = typeof v.singerStart === 'number' && v.singerStart >= 0 ? v.singerStart
            : partRank(v.part) !== undefined ? partRank(v.part)! + 0.5 : undefined;
        if (r !== undefined) { out.voices[v.key] = r; }
    }
    return out;
}
