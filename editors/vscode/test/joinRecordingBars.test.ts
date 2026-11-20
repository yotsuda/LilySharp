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

// Join Bars in Pairs in the file beside a chart (docs/join-bars-design.md §3, Orecchiante's
// request 21): bars[n] and bars[n + 1] become one, the rest numbered again.

import { describe, it } from 'node:test';
import * as assert from 'node:assert/strict';
import { Recording, joinRecordingBars, parseRecording, serializeRecording } from '../src/recordingCore';

function bar(n: number, start: number, section: string, ofSection: number, extra: Record<string, unknown> = {}) {
    const end = start + 1;
    return {
        bar: n, start, end, beats: [start, start + 0.25, start + 0.5, start + 0.75],
        section, barOfSection: ofSection, chords: [{ start, end, symbol: `C${n}`, sure: 0.9 }], ...extra,
    };
}

// A read at half the bar's length: A of four bars, B of two, A again.
const FILE = JSON.stringify({
    orecchiante: 1, audio: 'song.wav', duration: 10, form: ['A', 'B', 'A'], tuning: -12,
    bars: [
        bar(1, 0, 'A', 1, { tempo: 158 }), bar(2, 1, 'A', 2), bar(3, 2, 'A', 3), bar(4, 3, 'A', 4),
        bar(5, 4, 'B', 1), bar(6, 5, 'B', 2),
        bar(7, 6, 'A', 1, { tempo: 158 }), bar(8, 7, 'A', 2), bar(9, 8, 'A', 3), bar(10, 9, 'A', 4),
    ],
});

function ok(text: string): Recording {
    const r = parseRecording(text);
    assert.ok(!('error' in r), 'error' in r ? r.error : '');
    return r as Recording;
}

describe('joinRecordingBars', () => {
    it('joins each pair: the first start, the second end, the beats one in two, the chords in a row', () => {
        // A's two pairs, each time A is played (the language server's Played).
        const joined = joinRecordingBars(ok(FILE), [0, 2, 6, 8]);
        assert.ok(!('error' in joined));
        const r = joined as Recording;
        assert.deepEqual(r.bars.map(b => [b.start, b.end]), [[0, 2], [2, 4], [4, 5], [5, 6], [6, 8], [8, 10]]);
        const raw = r.raw.bars as Record<string, unknown>[];
        assert.deepEqual(raw[0].beats, [0, 0.5, 1, 1.5]);
        assert.deepEqual((raw[0].chords as { symbol: string }[]).map(c => c.symbol), ['C1', 'C2']);
        assert.equal(raw[0].tempo, 158);
        assert.deepEqual(raw.map(b => b.bar), [1, 2, 3, 4, 5, 6]);
        assert.deepEqual(raw.map(b => [b.section, b.barOfSection]),
            [['A', 1], ['A', 2], ['B', 1], ['B', 2], ['A', 1], ['A', 2]]);
    });

    it('writes format 2 and keeps what it does not know', () => {
        const r = joinRecordingBars(ok(FILE), [0]) as Recording;
        const back = JSON.parse(serializeRecording(r));
        assert.equal(back.orecchiante, 2);
        assert.equal(back.tuning, -12);
        assert.equal(back.bars.length, 9);
        assert.equal(back.bars[0].end, 2);
        assert.equal(back.bars[1].start, 2);
    });

    it('keeps the first bar\'s byHand, and a pickup stays bar 0', () => {
        const text = JSON.stringify({
            orecchiante: 2, audio: 'x.wav',
            bars: [
                { bar: 0, pickup: 1, start: 0, end: 0.5, section: 'Pickup', barOfSection: 1 },
                { bar: 1, start: 0.5, end: 1.5, section: 'A', barOfSection: 1, byHand: true },
                { bar: 2, start: 1.5, end: 2.5, section: 'A', barOfSection: 2, byHand: true },
                { bar: 3, start: 2.5, end: 3.5, section: 'A', barOfSection: 3 },
            ],
        });
        const r = joinRecordingBars(ok(text), [2]) as Recording;
        assert.deepEqual(r.bars, [{ start: 0, end: 0.5 }, { start: 0.5, end: 1.5, byHand: true }, { start: 1.5, end: 3.5, byHand: true }]);
        assert.deepEqual((r.raw.bars as Record<string, unknown>[]).map(b => b.bar), [0, 1, 2]);
    });

    it('a section played twice running starts its count again', () => {
        const text = JSON.stringify({
            orecchiante: 1, audio: 'x.wav',
            bars: [bar(1, 0, 'A', 1), bar(2, 1, 'A', 2), bar(3, 2, 'A', 1), bar(4, 3, 'A', 2)],
        });
        const r = joinRecordingBars(ok(text), [0, 2]) as Recording;
        assert.deepEqual((r.raw.bars as Record<string, unknown>[]).map(b => b.barOfSection), [1, 1]);
    });

    it('refuses a pair the file does not have', () => {
        const r = joinRecordingBars(ok(FILE), [9]);
        assert.ok('error' in r);
    });
});
