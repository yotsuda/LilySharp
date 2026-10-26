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
    CLICK_ACCENT, CLICK_BEAT, channelOf, channelsOf, countIn, endOf, loopNotes, playSpan, playSpanWindow, practiceNotes,
    programOf, toEvents,
} from '../src/webview/playbackCore';

const note = (t: number, d: number, p: number, extra: Partial<PlaybackNote> = {}): PlaybackNote =>
    ({ T: t, D: d, P: p, V: 90, S: 10 * t, O: 0, I: 0, C: 0, G: 0, ...extra });

// A count-in (owner's choice 2026-10-26): one bar of clicks, the music moved after it so the
// bar line falls where the count ends — as a player counts in.
describe('the count-in', () => {
    // 4/4 at one second a beat: bar lines at 0, 4, 8, 12.
    const beats = Array.from({ length: 12 }, (_, i) => ({ T: i, A: i % 4 === 0 }));
    const tune = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11].map(t => note(t, 1, 60 + t, { S: 100 + t }));
    const clicksOf = (ns: PlaybackNote[]) => ns.filter(n => n.S < 0).map(n => [n.T, n.P]);
    const firstNote = (ns: PlaybackNote[]) => ns.filter(n => n.S >= 0)[0];

    it('counts one bar of the meter, its first beat accented, and starts the music after it', () => {
        const c = countIn(tune, 0, 0, beats)!;
        assert.deepEqual(clicksOf(c.notes), [[0, CLICK_ACCENT], [1, CLICK_BEAT], [2, CLICK_BEAT], [3, CLICK_BEAT]]);
        assert.equal(c.lead, 4);
        assert.deepEqual([firstNote(c.notes).S, firstNote(c.notes).T], [100, 4]);
        assert.equal(c.notes.filter(n => n.S >= 0).length, tune.length);
    });

    it('from inside a bar, counts "1, 2" and comes in on 3', () => {
        // Play from Here on the note at beat 3 of bar 2 (t = 6): the music enters at 2, under
        // the count's third click.
        const c = countIn(tune, 6, 0, beats)!;
        assert.equal(c.lead, 2);
        assert.deepEqual([firstNote(c.notes).S, firstNote(c.notes).T], [106, 2]);
        assert.equal(clicksOf(c.notes).length, 4);
        assert.ok(c.notes.every(n => n.S < 0 || n.S >= 106), 'nothing before the start plays');
    });

    it('sounds a pickup over the count\'s last beat', () => {
        // A one-beat pickup: the first bar line is at 1.
        const pickup = Array.from({ length: 9 }, (_, i) => ({ T: i, A: i % 4 === 1 }));
        const c = countIn(tune.slice(0, 9), 0, 0, pickup)!;
        assert.equal(c.lead, 3);
        assert.deepEqual(clicksOf(c.notes).map(x => x[0]), [0, 1, 2, 3]);
        assert.equal(clicksOf(c.notes)[0][1], CLICK_ACCENT);
    });

    it('counts a stretch from where it stands in the score, in its own meter', () => {
        // Selected bars that start at 8 (the list's 0): a 3/4 bar there is counted in three.
        const mixed = [...beats.slice(0, 8), { T: 8, A: true }, { T: 9, A: false }, { T: 10, A: false }, { T: 11, A: true }];
        const stretch = tune.slice(8).map(n => ({ ...n, T: n.T - 8 }));
        const c = countIn(stretch, 0, 8, mixed)!;
        assert.equal(c.lead, 3);
        assert.deepEqual(clicksOf(c.notes).map(x => x[0]), [0, 1, 2]);
    });

    it('does nothing where there is no bar to count', () => {
        assert.equal(countIn(tune, 0, 0, [{ T: 0, A: true }]), null);
        assert.equal(countIn(tune, 0, 0, []), null);
    });
});

// Play of a selected stretch, and the loop that ignores repeats (owner's request 2026-10-25).
describe('the stretch a Play sounds', () => {
    // A bar A (written at 0..3) played twice — a repeat — then a bar B (10..13), one second a
    // quarter; a bass holds a whole note under each bar (A's at 100, B's at 110).
    const melody = (s0: number, t0: number) => [0, 1, 2, 3].map(i => note(t0 + i, 1, 60 + i, { S: s0 + i }));
    const piece: PlaybackNote[] = [
        ...melody(0, 0), note(0, 4, 36, { S: 100 }),
        ...melody(0, 4), note(4, 4, 36, { S: 100 }),
        ...melody(10, 8), note(8, 4, 41, { S: 110 }),
    ];
    const at = (ns: PlaybackNote[]) => ns.map(n => [n.S, n.T]);

    it('plays the selected bar where it first plays, every part with it, from 0', () => {
        assert.deepEqual(at(playSpan(piece, 10, 13, false)), [[10, 0], [110, 0], [11, 1], [12, 2], [13, 3]]);
        assert.deepEqual(at(playSpan(piece, 2, 3, false)), [[2, 0], [3, 1]]);
    });

    it('with the loop on, goes through the music once: a repeat is dropped and its time closed up', () => {
        const once = playSpan(piece, null, null, true);
        assert.deepEqual(at(once), [[0, 0], [100, 0], [1, 1], [2, 2], [3, 3], [10, 4], [110, 4], [11, 5], [12, 6], [13, 7]]);
        // Without it, the whole piece is the piece.
        assert.equal(playSpan(piece, null, null, false).length, piece.length);
    });

    it('drops the metronome clicks of a dropped repeat with it', () => {
        const clicks = [0, 2, 4, 6, 8, 10].map(t => note(t, 0.1, 76, { S: -1, C: 9 }));
        const once = playSpan([...piece, ...clicks], null, null, true).filter(n => n.S < 0);
        assert.deepEqual(once.map(n => n.T), [0, 2, 4, 6]);
    });

    it('lays a loop\'s rounds out ahead, each as long as the stretch sounds', () => {
        // Restarted at a page timer, a round on the MIDI output lost or gained up to three
        // beats (owner's report 2026-10-25): the timer was held to a second.
        const bar = playSpan(piece, 10, 13, true);            // four quarters, 0..4
        const rounds = loopNotes(bar, 10);
        assert.equal(rounds.length, bar.length * 3);
        assert.deepEqual(rounds.filter(n => n.S === 10).map(n => n.T), [0, 4, 8]);
        assert.deepEqual(loopNotes([], 10), []);
    });

    it('makes a round whole bars, a rest at either end included', () => {
        // A bar (4 s, bars at 0, 4, 8) whose notes sound only on its beats 2 and 3: the round is
        // the bar, from its start, not the notes' second and a half.
        const restful = [note(5, 1, 60, { S: 20 }), note(6, 0.5, 62, { S: 21 })];
        const w = playSpanWindow(restful, 20, 21, true, [0, 4, 8]);
        assert.equal(w.length, 4);
        assert.deepEqual(w.notes.map(n => n.T), [1, 2]);
        // Without the bars, from the first note to the last one's end, as before.
        assert.equal(playSpanWindow(restful, 20, 21, true).length, 1.5);
    });

    it('keeps a chord row\'s chord whole, and never pulls the notes onto one moment', () => {
        // A chord row sounds its chord's members from one place: taken for a repeat's notes, its
        // second and third were dropped and every note fell to 0 (owner's report 2026-10-25,
        // samples/morning-light.lys looped).
        const chordRow = [0, 4].flatMap(t => [55, 59, 62].map(p => note(t, 4, p, { S: 900 + t })));
        const tune = [0, 1, 2, 3, 4, 5, 6, 7].map(t => note(t, 1, 67 + t, { S: 10 + t }));
        const w = playSpanWindow([...chordRow, ...tune], null, null, true, [0, 4, 8]);
        assert.equal(w.notes.length, chordRow.length + tune.length);
        assert.deepEqual(w.notes.filter(n => n.S < 900).map(n => n.T), [0, 1, 2, 3, 4, 5, 6, 7]);
        assert.equal(w.length, 8);
    });

    it('plays nothing for a selection with no notes', () => {
        assert.deepEqual(playSpan(piece, 50, 60, false), []);
    });
});

// The practice menu: tempo, parts on and off, the metronome (owner's request 2026-10-25).
describe('the notes the practice menu plays', () => {
    const parts = ['Tempo', 'sop', 'alt'];
    const notes = [note(0, 1, 72, { N: 1 }), note(0, 1, 64, { N: 2 }), note(1, 1, 74, { N: 1 })];
    const beats = [{ T: 0, A: true }, { T: 0.5, A: false }];
    const as = { speed: 1, muted: [] as string[], click: false };

    it('plays the score as it is with nothing set', () => {
        assert.deepEqual(practiceNotes(notes, parts, beats, as), notes);
    });

    it('leaves a muted part out', () => {
        assert.deepEqual(practiceNotes(notes, parts, beats, { ...as, muted: ['alt'] }).map(n => n.P), [72, 74]);
    });

    it('stretches every time to the tempo', () => {
        const slow = practiceNotes(notes, parts, beats, { ...as, speed: 0.5 });
        assert.deepEqual(slow.map(n => [n.T, n.D]), [[0, 2], [0, 2], [2, 2]]);
    });

    it('keeps a repeated note sounding at any tempo: its off before the re-strike', () => {
        // Two C5 eighths at 162 bpm (a user's file, 2026-10-25): stretched to 150%, the first's
        // end came a bit after the second's start, and the off silenced the second.
        const two: PlaybackNote[] = [
            { T: 4.259255, D: 0.1851850000000006, P: 72, V: 80, S: 371, O: 0, I: 3, C: 0, G: 40, N: 1 },
            { T: 4.44444, D: 0.1851849999999997, P: 72, V: 80, S: 375, O: 0, I: 3, C: 0, G: 40, N: 1 },
        ];
        for (const speed of [0.5, 0.8, 1.2, 1.5]) {
            const ev = toEvents(practiceNotes(two, parts, null, { ...as, speed }))
                .filter(e => e.kind === 'on' || e.kind === 'off');
            assert.deepEqual(ev.map(e => e.kind), ['on', 'off', 'on', 'off'], `at ${speed}`);
        }
    });

    it('clicks every beat with the metronome on, the bar\'s first louder, lighting nothing', () => {
        const clicks = practiceNotes(notes, parts, beats, { ...as, click: true, speed: 0.5 }).filter(n => n.C === DRUM_CHANNEL);
        assert.deepEqual(clicks.map(n => [n.T, n.P, n.S]), [[0, 76, -1], [1, 77, -1]]);
        assert.ok(clicks[0].V > clicks[1].V);
    });
});

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
