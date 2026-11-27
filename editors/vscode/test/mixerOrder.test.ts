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
import { firstAppearances, mixerRanks } from '../src/mixerOrderCore';

// The mixer's Score lines in the order the .lys first names them (owner's request 2026-11-27).
describe('The mixer lists the score as the file names it', () => {
    const text = [
        '// part notThis { }   a comment names nothing',
        'chords prog {}',
        'part vocal { instrument tenor  lyrics words }',
        'singer on vocal',
        'part bass { instrument bass }',
        '',
        'part kit { clef percussion }',
        'section A {',
        '  kit { bd4 4 4 4 | }',
        '  bass { c1 | }',
        '  piano { title "organ {" c1 | }',
        '  vocal { c1 | }',
        '  strings { c1 | }',
        '}',
    ].join('\n');
    const order = (ranks: Record<string, number>, names: string[]) =>
        [...names].sort((a, b) => (ranks[a] ?? Infinity) - (ranks[b] ?? Infinity));

    it('ranks a part by its declaration, else by its first block in a section', () => {
        const first = firstAppearances(text);
        assert.equal(first.has('notThis'), false);   // a comment
        assert.equal(first.has('organ'), false);     // a string
        assert.ok(first.get('prog')! < first.get('vocal')! && first.get('vocal')! < first.get('bass')! && first.get('bass')! < first.get('kit')!);
        assert.ok(first.get('kit')! < first.get('piano')! && first.get('piano')! < first.get('strings')!);
        assert.equal(first.get('kit'), text.indexOf('part kit'));   // the declaration, not the later block
    });

    it('puts the score order of the staves into the file order, a voice at its singer line', () => {
        // (the score order: the staves as the score stacks them, the chord row last — MidiExporter)
        const parts = ['kit', 'strings', 'bass', 'vocal', 'piano', 'prog (chords)'];
        const voices = [{ key: 'vocal', part: 'vocal', singerStart: text.indexOf('singer on vocal') }];
        const ranks = mixerRanks(text, parts, voices);
        const lines = [...parts.map(p => ({ k: p, r: ranks.parts[p] })), ...voices.map(v => ({ k: 'voice:' + v.key, r: ranks.voices[v.key] }))]
            .sort((a, b) => a.r - b.r).map(l => l.k);
        assert.deepEqual(lines, ['prog (chords)', 'vocal', 'voice:vocal', 'bass', 'kit', 'piano', 'strings']);
    });

    it('puts a voice with no singer line of its own just after its part, and one it cannot place last', () => {
        const ranks = mixerRanks(text, ['vocal', 'bass'], [
            { key: 'bass', part: 'bass', singerStart: null },
            { key: 'carol', part: 'carol__1' },
        ]);
        assert.ok(ranks.voices.bass > ranks.parts.bass && ranks.voices.bass < text.indexOf('part kit'));
        assert.equal('carol' in ranks.voices, false);
        assert.deepEqual(order(ranks.parts, ['ghost', 'bass', 'vocal']), ['vocal', 'bass', 'ghost']);
    });
});
