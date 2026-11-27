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

// The order of the mixer's Score lines (owner's decisions 2026-11-27): one order for every part of
// the file, the same whichever score is shown, and alike for every kind of music — not exact
// orchestration, only nothing the user would not expect. The Recording group's order, which a
// separated recording always has (vocals, guitar, piano, other, bass, drums):
//
//   song (a part that is sung, its singer's voice just under it) · guitars · keyboards · other ·
//   bass · drums and percussion
//
// and within a group, the order the .lys first names the parts (a declaration `part x { … }` or
// `chords x {}`; a part with none by its first block in a section, `x { … }`). "Other" is every part
// that is none of the five: its chords rows first (the accompaniment's frame, which every part reads;
// owner's decision 2026-11-27), then winds, brass, strings and parts not recognised, in that
// first-named order alone. The staves of a score keep the order its writer gave them: only the
// mixer is ordered here.
//
// No vscode here: the extension and the tests share it.

/** The track name of a chords row (MidiExporter's ChordRowTrackSuffix): `prog (chords)`. */
const CHORD_ROW_SUFFIX = ' (chords)';

/** The families a part is read as. */
export const enum MixerFamily { Song, Chords, Winds, Brass, Strings, Guitars, Keyboards, Unknown, Bass, Drums }

/** The mixer's groups, top to bottom — the Recording group's stems: vocals, guitar, piano, other,
 *  bass, drums. */
function groupOf(f: MixerFamily): number {
    switch (f) {
        case MixerFamily.Song: return 0;
        case MixerFamily.Guitars: return 1;
        case MixerFamily.Keyboards: return 2;
        case MixerFamily.Chords: return 3;      // other's head: the accompaniment's frame (owner's decision 2026-11-27)
        case MixerFamily.Bass: return 5;
        case MixerFamily.Drums: return 6;
        default: return 4;      // the rest of other: winds, brass, strings, not recognised
    }
}

/** The text with its comments and strings blanked, every offset kept. */
function blankCommentsAndStrings(text: string): string {
    return text.replace(/\/\*[\s\S]*?\*\/|\/\/[^\n]*|"(?:[^"\\\n]|\\.)*"/g, m => m.replace(/[^\n]/g, ' '));
}

const NAME = '[\\p{L}_][\\p{L}\\p{N}_]*';

/** Where each name is first written: as a declaration (`part x`, `chords x`) or a block (`x {`),
 *  whichever comes first in the file. */
export function firstAppearances(text: string): Map<string, number> {
    const clean = blankCommentsAndStrings(text);
    const out = new Map<string, number>();
    const re = new RegExp(`(?<![\\p{L}\\p{N}_])(?:(?:part|chords)\\s+(${NAME})|(${NAME})\\s*\\{)`, 'gu');
    for (const m of clean.matchAll(re)) {
        const n = m[1] ?? m[2];
        if (!out.has(n)) { out.set(n, m.index); }
    }
    return out;
}

/** What a part's declaration says of its instrument: `instrument x`, `midiInstrument "x"`, and
 *  whether its clef is percussion. Read from the declaration's own braces. */
export function partHeaders(text: string): Map<string, { instrument?: string; midi?: string; percussion: boolean }> {
    const clean = blankCommentsAndStrings(text);
    const out = new Map<string, { instrument?: string; midi?: string; percussion: boolean }>();
    const re = new RegExp(`(?<![\\p{L}\\p{N}_])part\\s+(${NAME})[^{\\n]*\\{`, 'gu');
    for (const m of clean.matchAll(re)) {
        if (out.has(m[1])) { continue; }
        // The declaration's body: to its own closing brace.
        let depth = 1, at = m.index + m[0].length;
        while (at < clean.length && depth > 0) {
            if (clean[at] === '{') { depth++; } else if (clean[at] === '}') { depth--; }
            at++;
        }
        const body = clean.slice(m.index + m[0].length, at - 1);
        const bodyStart = m.index + m[0].length;
        const instrument = /(?<![\p{L}\p{N}_-])instrument\s+([\p{L}\p{N}_-]+)/u.exec(body)?.[1];
        // (the sound's name read from the text itself: the blanked copy has no strings)
        // (the keyword alone: in the blanked copy the string is spaces, which `\s+` would swallow)
        const midiAt = /(?<![\p{L}\p{N}_])midiInstrument(?![\p{L}\p{N}_])/u.exec(body);
        const midi = midiAt ? /^\s*"([^"]*)"/.exec(text.slice(bodyStart + midiAt.index + midiAt[0].length))?.[1] : undefined;
        out.set(m[1], { instrument, midi, percussion: /(?<![\p{L}\p{N}_])clef\s+percussion\b/u.test(body) });
    }
    return out;
}

const PRESETS: Record<string, MixerFamily> = {
    soprano: MixerFamily.Song, alto: MixerFamily.Song, tenor: MixerFamily.Song, baritone: MixerFamily.Song, 'voice-bass': MixerFamily.Song,
    flute: MixerFamily.Winds, piccolo: MixerFamily.Winds, oboe: MixerFamily.Winds, clarinet: MixerFamily.Winds, 'clarinet-a': MixerFamily.Winds,
    bassoon: MixerFamily.Winds, 'soprano-sax': MixerFamily.Winds, 'alto-sax': MixerFamily.Winds, 'tenor-sax': MixerFamily.Winds, 'baritone-sax': MixerFamily.Winds,
    trumpet: MixerFamily.Brass, 'trumpet-c': MixerFamily.Brass, horn: MixerFamily.Brass, trombone: MixerFamily.Brass, tuba: MixerFamily.Brass,
    violin: MixerFamily.Strings, viola: MixerFamily.Strings, cello: MixerFamily.Strings, contrabass: MixerFamily.Strings, contrabass5: MixerFamily.Strings,
    guitar: MixerFamily.Guitars, ukulele: MixerFamily.Guitars, mandolin: MixerFamily.Guitars, banjo: MixerFamily.Guitars,
    'piano-right': MixerFamily.Keyboards, 'piano-left': MixerFamily.Keyboards,
    'organ-right': MixerFamily.Keyboards, 'organ-left': MixerFamily.Keyboards, 'organ-pedal': MixerFamily.Keyboards,
    bass: MixerFamily.Bass, bass5: MixerFamily.Bass, bass6: MixerFamily.Bass,
    timpani: MixerFamily.Drums,
};

/** A family from a word — a General MIDI sound's name or a part's own name: the first that matches,
 *  in an order that keeps `bassoon` from the basses and `contrabass` with the strings. */
export function familyOfWord(word: string): MixerFamily | undefined {
    const w = word.toLowerCase();
    const rules: [RegExp, MixerFamily][] = [
        [/vocal|vox|voice|sing|choir|chorus|aahs|oohs/, MixerFamily.Song],
        [/chord/, MixerFamily.Chords],
        [/bassoon|flute|piccolo|oboe|clarinet|sax|recorder|pipe|whistle|ocarina|shakuhachi|bagpipe/, MixerFamily.Winds],
        [/trumpet|trombone|horn|tuba|brass|cornet/, MixerFamily.Brass],
        [/contrabass|violin|viola|cello|fiddle|string|orchestra|pizzicato|tremolo|harp/, MixerFamily.Strings],
        [/guitar|gtr|ukulele|mandolin|banjo|sitar|shamisen|koto/, MixerFamily.Guitars],
        [/piano|keys|keyboard|organ|harpsichord|clav|celesta|synth|pad|lead|accordion|harmonica|pf|epiano/, MixerFamily.Keyboards],
        [/bass/, MixerFamily.Bass],
        [/drum|kit|perc|timpani|cymbal|snare|conga|bongo|tom|hats?\b|beat/, MixerFamily.Drums],
    ];
    return rules.find(([re]) => re.test(w))?.[1];
}

/** A voice of the mixer: its key, its part, and where its singer line is (null: none of its own). */
export interface MixerVoice { readonly key: string; readonly part: string; readonly singerStart?: number | null }

/** Each line's place in the one order, for sorting: parts (track names) and voices (keys) apart,
 *  as their keys can be the same word. */
export function mixerRanks(text: string, parts: readonly string[], voices: readonly MixerVoice[]):
    { parts: Record<string, number>; voices: Record<string, number> } {
    const first = firstAppearances(text);
    const headers = partHeaders(text);
    const sung = new Set(voices.map(v => v.part));
    const nameOf = (track: string) => track.endsWith(CHORD_ROW_SUFFIX) ? track.slice(0, -CHORD_ROW_SUFFIX.length) : track;
    const familyOf = (track: string): MixerFamily => {
        if (track.endsWith(CHORD_ROW_SUFFIX)) { return MixerFamily.Chords; }
        if (sung.has(track)) { return MixerFamily.Song; }
        const h = headers.get(track);
        if (h?.percussion) { return MixerFamily.Drums; }
        const preset = h?.instrument !== undefined ? PRESETS[h.instrument] : undefined;
        return preset ?? (h?.midi !== undefined ? familyOfWord(h.midi) : undefined) ?? familyOfWord(track) ?? MixerFamily.Unknown;
    };
    // (family, where the file first names it, the part before its voices, the voices by their lines, the given order)
    type Line = { kind: 'part' | 'voice'; key: string; sort: number[] };
    const lines: Line[] = parts.map((p, i) => ({
        kind: 'part', key: p, sort: [groupOf(familyOf(p)), first.get(nameOf(p)) ?? Infinity, 0, 0, i],
    }));
    voices.forEach((v, i) => {
        const partFirst = first.get(v.part);
        const singer = typeof v.singerStart === 'number' && v.singerStart >= 0 ? v.singerStart : Infinity;
        lines.push({ kind: 'voice', key: v.key, sort: [groupOf(MixerFamily.Song), partFirst ?? singer, 1, singer, parts.length + i] });
    });
    lines.sort((a, b) => {
        for (let k = 0; k < a.sort.length; k++) {
            const d = a.sort[k] - b.sort[k];
            if (d !== 0 && !Number.isNaN(d)) { return d; }
        }
        return 0;
    });
    const out = { parts: {} as Record<string, number>, voices: {} as Record<string, number> };
    lines.forEach((l, rank) => { (l.kind === 'part' ? out.parts : out.voices)[l.key] = rank; });
    return out;
}
