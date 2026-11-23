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
import * as fs from 'node:fs';
import * as path from 'node:path';
import {
    ORECCHIANTE_SEED_FOLDER_SETTING, SideBar, VoiceBars, caretBar, caretPhrase, itemsOf, orecchianteSeedDirName,
    orecchianteSeedOf, phraseItems, playedOf, seedPartOf, sideBarsOf, sideSectionLength,
} from '../src/orecchianteSeedCore';

// Report to Orecchiante (Orecchiante's request 23, 2026-11-23): the owner's alone, as Calamo's is.
describe('Report to Orecchiante', () => {
    const root = path.join(__dirname, '..', '..');

    // The shape Orecchiante 0.14.0 writes, and the voices lilysharp/voiceBars answers for it.
    const text = [
        'phrase hatsA { hh8 8 8 8 8 8 8 8 | }',
        'phrase kickA { bd4 4 4 4 | }',
        'phrase beatA { drums { hatsA kickA } }',
        'section A {',
        '  bass { c8 c g c | g,2 b, | }',
        '  kit { beatA | % | }',
        '}',
    ].join('\n');
    const span = (s: string): { start: number; end: number } => {
        const at = text.indexOf(s);
        return { start: at, end: at + s.length };
    };
    const bass: VoiceBars = { section: 'A', voice: "part 'bass'", bars: [span('c8 c g c'), span('g,2 b,')] };
    const kitPercent = text.indexOf('%');
    const kitAt = text.indexOf('beatA |');
    const kit: VoiceBars = {
        section: 'A', voice: "part 'kit'",
        bars: [{ start: kitAt, end: kitAt + 'beatA'.length }, { start: kitPercent, end: kitPercent + 1 }],
    };
    // Section A played twice: bars 1–2, then 5–6.
    const side: SideBar[] = sideBarsOf({
        bars: [
            { bar: 1, section: 'A', barOfSection: 1, bass: 'c8 c g c', drums: { hh: 'hh8 8 8 8 8 8 8 8', bd: 'bd4 4 4 4' } },
            { bar: 2, section: 'A', barOfSection: 2, bass: 'g,2 a,', drums: { hh: 'hh4 4 4 4' } },
            { bar: 3, section: 'B', barOfSection: 1, chords: [{ symbol: 'F' }] },
            { bar: 4, section: 'B', barOfSection: 2, chords: [{ symbol: 'C' }, { symbol: 'G' }] },
            { bar: 5, section: 'A', barOfSection: 1, bass: 'c8 c g e' },
            { bar: 6, section: 'A', barOfSection: 2 },
            'not a bar',
        ],
    });

    it('is turned on by a setting that is not contributed, and its commands are not in package.json', () => {
        const pkg = fs.readFileSync(path.join(root, 'package.json'), 'utf8');
        assert.ok(!pkg.includes(ORECCHIANTE_SEED_FOLDER_SETTING));
        assert.ok(!pkg.includes('orecchianteReport'));
        const ext = fs.readFileSync(path.join(root, 'src', 'orecchianteSeed.ts'), 'utf8');
        assert.match(ext, /if \(!seedFolder\(\) \|\| !client/);
    });

    it('reports the bass, the kit and the chords rows only', () => {
        assert.equal(seedPartOf("part 'bass'"), 'bass');
        assert.equal(seedPartOf("part 'kit'"), 'kit');
        assert.equal(seedPartOf("chords 'prog'"), 'chords');
        assert.equal(seedPartOf("part 'melody'"), undefined);
    });

    it('names every playing of a bar, and what the first was read as', () => {
        assert.equal(side.length, 6);
        assert.deepEqual(playedOf(side, 'A', 1), [1, 5]);
        assert.equal(sideSectionLength(side, 'A'), 2);
        const [second] = itemsOf(text, bass, 'bass', side, k => k === 2);
        assert.deepEqual(second, { section: 'A', barOfSection: 2, bars: [2, 6], bar: 'g,2 b,', barWhenRead: 'g,2 a,' });
        const chords = itemsOf('C | G |', { section: 'B', voice: "chords 'prog'", bars: [{ start: 0, end: 1 }, { start: 4, end: 5 }] }, 'chords', side);
        assert.deepEqual(chords.map(i => i.barWhenRead), [['F'], ['C', 'G']]);
    });

    it('gives a kit bar of a groove, or of % after one, the groove and its phrases', () => {
        const items = itemsOf(text, kit, 'kit', side);
        assert.deepEqual(items.map(i => [i.bar, i.groove, i.lines]), [
            ['beatA', 'beatA', { hatsA: 'hh8 8 8 8 8 8 8 8', kickA: 'bd4 4 4 4' }],
            ['%', 'beatA', { hatsA: 'hh8 8 8 8 8 8 8 8', kickA: 'bd4 4 4 4' }],
        ]);
        assert.deepEqual(items[0].barWhenRead, { hh: 'hh8 8 8 8 8 8 8 8', bd: 'bd4 4 4 4' });
    });

    it('finds the caret\'s bar, and a phrase a groove plays', () => {
        const at = caretBar([bass, kit], text.indexOf('b,'));
        assert.equal(at?.voice, bass);
        assert.equal(at?.barOfSection, 2);
        assert.equal(caretPhrase(text, text.indexOf('hh8')), 'hatsA');
        assert.equal(caretPhrase(text, text.indexOf('drums {')), undefined);   // a groove, not a phrase of one
        assert.deepEqual(phraseItems(text, [kit], 'kickA', side).map(i => i.barOfSection), [1, 2]);
    });

    it('names the folder and writes the record', () => {
        const when = new Date(2026, 10, 23, 15, 30, 12);
        assert.equal(orecchianteSeedDirName(when, 'C:\\m\\Like A Virgin.lys', 'bar', 'bass', { firstPlayed: 2 }),
            '20261123-153012-Like_A_Virgin-bar2-bass');
        assert.equal(orecchianteSeedDirName(when, 'x.lys', 'section', 'kit', { section: 'A2' }), '20261123-153012-x-A2-kit');
        assert.equal(orecchianteSeedDirName(when, 'x.lys', 'phrase', 'kit', { phrase: 'hatsA' }), '20261123-153012-x-hatsA-kit');
        const items = itemsOf(text, bass, 'bass', side);
        const seed = orecchianteSeedOf(when, 'section', 'bass', items, ' ok ', {
            lys: 'C:\\m\\x.lys', sideFile: 'C:\\m\\x.orecchiante.json', recording: 'F:/Music/x.mp3', section: 'A', linedUp: true,
        });
        assert.equal(seed.seed, 1);
        assert.equal(seed.note, 'ok');
        assert.equal(seed.lysCopy, 'x.lys');
        assert.equal(seed.sideFileCopy, 'x.orecchiante.json');
        assert.equal(seed.phrase, null);
        assert.equal(seed.items.length, 2);
    });
});
