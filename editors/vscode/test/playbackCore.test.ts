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
import {
    BEND_CENTRE, DRUM_CHANNEL, FAMILY_PROGRAM, Lookahead, PlaybackNote, SoundingCursor,
    channelOf, channelsOf, endOf, programOf, toEvents,
} from '../src/webview/playbackCore';

const note = (t: number, d: number, p: number, extra: Partial<PlaybackNote> = {}): PlaybackNote =>
    ({ T: t, D: d, P: p, V: 90, S: 10 * t, O: 0, I: 0, C: 0, G: 0, ...extra });

describe('the events a note list becomes (docs/playback-design.md §2)', () => {
    it('sets each pitched channel\'s program once, first, and plays drums with none', () => {
        const events = toEvents([
            note(0, 1, 60, { C: 0, G: 40 }),
            note(0, 1, 36, { C: DRUM_CHANNEL, I: 9, G: 0 }),
            note(1, 1, 64, { C: 1, G: 73 }),
            note(2, 1, 62, { C: 0, G: 40 }),
        ]);
        const programs = events.filter(e => e.kind === 'program');
        assert.deepEqual(programs.map(e => [e.c, (e as { g: number }).g]), [[0, 40], [1, 73]]);
        assert.deepEqual(events.slice(0, 2).map(e => e.kind), ['program', 'program']);
        assert.ok(!events.some(e => e.kind === 'program' && e.c === DRUM_CHANNEL));
    });

    it('orders a note\'s off before a re-strike of the same pitch at the same instant', () => {
        const events = toEvents([note(0, 1, 60), note(1, 1, 60)]);
        const at1 = events.filter(e => e.t === 1).map(e => e.kind);
        assert.deepEqual(at1, ['off', 'on']);
    });

    it('drops the notes before the offset whole and re-times the rest from it', () => {
        const events = toEvents([note(0, 1, 60), note(1, 1, 62), note(2, 1, 64)], 1);
        const ons = events.filter(e => e.kind === 'on') as { t: number; p: number }[];
        assert.deepEqual(ons.map(o => [o.t, o.p]), [[0, 62], [1, 64]]);
        assert.equal(endOf([note(0, 1, 60), note(1, 1, 62), note(2, 1, 64)], 1), 3);
    });

    it('bends the wheel for a quarter tone and centres it after', () => {
        const events = toEvents([note(0, 1, 60.5)]);
        const bends = events.filter(e => e.kind === 'bend') as { t: number; value: number }[];
        assert.deepEqual(bends.map(b => [b.t, b.value]), [[0, BEND_CENTRE + 2048], [1, BEND_CENTRE]]);
        // The bend precedes the on it is for; the on strikes the key below, bent up.
        const kinds = events.map(e => e.kind);
        assert.ok(kinds.indexOf('bend') < kinds.indexOf('on'));
        assert.equal((events.find(e => e.kind === 'on') as { p: number }).p, 60);
        assert.ok(!toEvents([note(0, 1, 60.001)]).some(e => e.kind === 'bend'), 'dust is no quarter tone');
        assert.equal((toEvents([note(0, 1, 60.999)]).find(e => e.kind === 'on') as { p: number }).p, 61);
    });

    it('clamps velocity to 1..127 and pitch to the keyboard', () => {
        const [on] = toEvents([note(0, 1, 200, { V: 0 })]).filter(e => e.kind === 'on') as { p: number; v: number }[];
        assert.equal(on.p, 127);
        assert.equal(on.v, 1);
    });

    it('names the channels a stop must silence', () => {
        assert.deepEqual(channelsOf(toEvents([note(0, 1, 60, { C: 3 }), note(0, 1, 36, { C: 9 })])), [3, 9]);
    });
});

describe('the channel and the program of a note', () => {
    it('are the server\'s, else the drum channel for the drum family, else the family\'s voice', () => {
        assert.equal(channelOf({ C: 4 }), 4);
        assert.equal(channelOf({ I: 9 }), DRUM_CHANNEL);
        assert.equal(channelOf({ I: 3 }), 0);
        assert.equal(programOf(40, 3), 40);
        assert.equal(programOf(-1, 3), FAMILY_PROGRAM[3]);
        assert.equal(programOf(undefined, undefined), 0);
        assert.equal(FAMILY_PROGRAM.length, 10);
    });
});

describe('the lookahead', () => {
    it('hands out the events up to the horizon, each once, in order', () => {
        const look = new Lookahead(toEvents([note(0, 1, 60), note(1, 1, 62), note(2, 1, 64)]));
        const first = look.take(0.25);
        assert.deepEqual(first.map(e => e.kind), ['program', 'on']);
        assert.deepEqual(look.take(0.25), []);
        assert.deepEqual(look.take(1.1).map(e => [e.t, e.kind]), [[1, 'off'], [1, 'on']]);
        assert.equal(look.handedOutUntil, 1);
        assert.ok(!look.done);
        look.take(10);
        assert.ok(look.done);
    });
});

describe('the follow-along cursor', () => {
    const notes = [
        { t: 0, d: 2, s: 5, o: 0 },     // a half note
        { t: 0, d: 1, s: 9, o: 0 },     // an eighth under it…
        { t: 1, d: 1, s: 12, o: 0 },    // …then another
        { t: 1, d: 1, s: 12, o: 0 },    // the same printed note, played twice (a repeat)
        { t: 3, d: 1, s: -1, o: 0 },    // nothing printed: never lit
    ];

    it('lights what sounds, newest first, one entry per printed note', () => {
        const cursor = new SoundingCursor(notes);
        const a = cursor.at(0.5);
        assert.deepEqual(a.sounding.map(x => x.pos), [5, 9]);
        assert.ok(a.changed && a.hadNew);
        const b = cursor.at(0.7);
        assert.ok(!b.changed && !b.hadNew);
        const c = cursor.at(1.5);
        assert.deepEqual(c.sounding.map(x => x.pos), [12, 5]);   // the half still lit under the new eighth
        assert.ok(c.changed && c.hadNew);
        const d = cursor.at(2.5);
        assert.deepEqual(d.sounding, []);
        assert.ok(d.changed && !d.hadNew);
        assert.deepEqual(cursor.at(3.5).sounding, []);
    });

    it('reports a change with nothing new when a note merely ends', () => {
        const cursor = new SoundingCursor([{ t: 0, d: 1, s: 1, o: 0 }, { t: 0, d: 2, s: 2, o: 0 }]);
        cursor.at(0.5);
        const r = cursor.at(1.5);
        assert.ok(r.changed);
        assert.ok(!r.hadNew);
        assert.deepEqual(r.sounding.map(x => x.pos), [2]);
    });
});
