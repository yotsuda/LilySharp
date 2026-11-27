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

import { describe, it } from 'node:test';
import * as assert from 'node:assert/strict';
import { MixerVoice, familyOfWord, firstAppearances, mixerRanks, partHeaders } from '../src/mixerOrderCore';

/** The lines as the mixer draws them: parts and voices (as `voice:key`) in rank order. */
function drawn(text: string, parts: string[], voices: MixerVoice[] = []): string[] {
    const ranks = mixerRanks(text, parts, voices);
    return [...parts.map(p => ({ k: p, r: ranks.parts[p] })), ...voices.map(v => ({ k: 'voice:' + v.key, r: ranks.voices[v.key] }))]
        .sort((a, b) => a.r - b.r).map(l => l.k);
}

// One order for every part of the file, alike for every kind of music (owner's decisions 2026-11-27).
describe('The mixer lists the parts in one order', () => {
    it('reads where each name is first written, and a declaration\'s instrument, leaving comments and strings', () => {
        const text = '// part notThis { }\nchords prog {}\npart vocal { instrument tenor  midiInstrument "choir aahs" }\n'
            + 'part kit { clef percussion }\nsection A {\n  piano { title "organ {" c1 | }\n}\n';
        const first = firstAppearances(text);
        assert.equal(first.has('notThis'), false);
        assert.equal(first.has('organ'), false);
        assert.ok(first.get('prog')! < first.get('vocal')! && first.get('kit')! < first.get('piano')!);
        const h = partHeaders(text);
        assert.deepEqual(h.get('vocal'), { instrument: 'tenor', midi: 'choir aahs', percussion: false });
        assert.deepEqual(h.get('kit'), { instrument: undefined, midi: undefined, percussion: true });
        assert.equal(h.has('notThis'), false);
    });

    it('puts a song and its voice first, then as the Recording group: guitar, piano, other, bass, drums', () => {
        // (the owner's file: the chords row first in the text, the song still first in the mixer;
        // the chords row and the saxophone are "other", in the file's order)
        const text = 'chords prog {}\npart vocal { instrument tenor  lyrics words }\nsinger on vocal\n'
            + 'part bass { instrument bass }\n\npart kit { clef percussion }\npart gtr { instrument guitar }\n'
            + 'part keys { midiInstrument "electric piano 1" }\npart sax { instrument alto-sax }\n';
        const voices = [{ key: 'vocal', part: 'vocal', singerStart: text.indexOf('singer') }];
        // (as the playback lists them: the staves' order, the chords row last)
        assert.deepEqual(drawn(text, ['kit', 'bass', 'keys', 'gtr', 'sax', 'vocal', 'prog (chords)'], voices),
            ['vocal', 'voice:vocal', 'gtr', 'keys', 'prog (chords)', 'sax', 'bass', 'kit']);
    });

    it('puts the chords rows at the head of "other", the rest of it in the file\'s order', () => {
        const text = 'part str { instrument violin }\npart sax { instrument alto-sax }\nchords prog {}\nchords alt {}\npart thing { }\n';
        assert.deepEqual(drawn(text, ['thing', 'sax', 'str', 'alt (chords)', 'prog (chords)']),
            ['prog (chords)', 'alt (chords)', 'str', 'sax', 'thing']);
    });

    it('keeps the file\'s order within a group, and the same order whichever score lists the parts', () => {
        const text = 'part v2 { instrument violin }\npart va { instrument viola }\npart v1 { instrument violin }\n'
            + 'part fl { instrument flute }\npart pno { instrument piano-right }\n';
        const all = ['pno', 'v1', 'fl', 'va', 'v2'];
        assert.deepEqual(drawn(text, all), ['pno', 'v2', 'va', 'v1', 'fl']);
        assert.deepEqual(drawn(text, [...all].reverse()), ['pno', 'v2', 'va', 'v1', 'fl']);
    });

    it('reads a part with no instrument by its sound or its name; one it cannot read is "other"', () => {
        const text = 'part lead { midiInstrument "overdriven guitar" }\npart thing { }\npart bassline { }\npart drums { }\n'
            + 'part fag { midiInstrument "bassoon" }\n';
        assert.deepEqual(drawn(text, ['drums', 'bassline', 'thing', 'lead', 'fag']), ['lead', 'thing', 'fag', 'bassline', 'drums']);
        assert.deepEqual(partHeaders(text).get('fag'), { instrument: undefined, midi: 'bassoon', percussion: false });
        assert.equal(familyOfWord('contrabass'), familyOfWord('violin'));
        assert.notEqual(familyOfWord('bassoon'), familyOfWord('bass'));
    });

    it('keeps each voice under its part, the voices of one part in their singer lines\' order', () => {
        const text = 'part sop { instrument soprano }\npart alt { instrument alto }\nsinger b on alt\nsinger a on sop\n';
        const voices = [
            { key: 'alt', part: 'alt', singerStart: text.indexOf('singer b') },
            { key: 'sop', part: 'sop', singerStart: text.indexOf('singer a') },
            { key: 'carol', part: 'carol__1', singerStart: null },
        ];
        assert.deepEqual(drawn(text, ['alt', 'sop'], voices), ['sop', 'voice:sop', 'alt', 'voice:alt', 'voice:carol']);
    });
});
