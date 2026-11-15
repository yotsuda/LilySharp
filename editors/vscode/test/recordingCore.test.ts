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
    Recording, ScoreBeat, audioPathFor, barAt, lineUp, newRecording, parseRecording, recordingPathFor,
    recordingSecondAt, scoreBars, scoreTimeAt, serializeRecording,
} from '../src/recordingCore';

// Dettato's own example (SIDECAR.md), cut to its first bars.
const FORMAT1 = JSON.stringify({
    dettato: 1, audio: 'song.wav', duration: 53.52, tempo: { note: '4', bpm: 117 }, time: '4/4', key: 'F# major',
    doubt: 0.84, form: ['Pickup', 'A'],
    bars: [
        { bar: 0, pickup: 3, start: 0.355, end: 1.9, section: 'Pickup', barOfSection: 1, chords: [{ start: 0.355, end: 1.9, symbol: 'C#7' }] },
        { bar: 1, start: 1.9, end: 3.96, section: 'A', barOfSection: 1, chords: [{ start: 1.9, end: 3.96, symbol: 'F#/A#' }] },
        { bar: 2, start: 3.96, end: 6.0, section: 'A', barOfSection: 2, chords: [{ start: 3.96, end: 6.0, symbol: 'B' }] },
    ],
});

function ok(text: string): Recording {
    const r = parseRecording(text);
    assert.ok(!('error' in r), 'error' in r ? r.error : '');
    return r as Recording;
}

/** 4/4 at 120: a beat is 0.5 s, a bar 2 s. `pickupBeats` beats before the first full bar. */
function beats(bars: number, pickupBeats = 0): ScoreBeat[] {
    const out: ScoreBeat[] = [];
    for (let k = 0; k < pickupBeats; k++) {
        out.push({ T: k * 0.5, A: false });
    }
    for (let b = 0; b < bars; b++) {
        for (let k = 0; k < 4; k++) {
            out.push({ T: pickupBeats * 0.5 + b * 2 + k * 0.5, A: k === 0 });
        }
    }
    return out;
}

describe('the file beside a chart', () => {
    it('is the chart\'s stem with .dettato.json, and names the recording in its own folder', () => {
        assert.equal(recordingPathFor('C:\\songs\\My Song.lys'), 'C:\\songs\\My Song.dettato.json');
        assert.equal(recordingPathFor('/a.b/song'), '/a.b/song.dettato.json');
        assert.equal(audioPathFor('C:\\songs\\My Song.dettato.json', 'take 1.m4a'), 'C:\\songs\\take 1.m4a');
    });

    it('reads Dettato\'s format 1, a pickup among its bars', () => {
        const r = ok(FORMAT1);
        assert.equal(r.format, 1);
        assert.equal(r.audio, 'song.wav');
        assert.equal(r.duration, 53.52);
        assert.equal(r.pickup, true);
        assert.deepEqual(r.bars.map(b => [b.start, b.end]), [[0.355, 1.9], [1.9, 3.96], [3.96, 6.0]]);
    });

    it('takes a file with no format number as format 1 (written before 2026-11-14)', () => {
        const r = ok(JSON.stringify({ audio: 'x.wav', bars: [{ start: 0, end: 2 }] }));
        assert.equal(r.format, 1);
        assert.equal(r.pickup, false);
    });

    it('refuses a newer format, a path for the audio, and bars out of order', () => {
        assert.match((parseRecording(JSON.stringify({ dettato: 3, audio: 'x.wav', bars: [] })) as { error: string }).error, /newer/);
        assert.match((parseRecording(JSON.stringify({ dettato: 1, audio: 'sub/x.wav', bars: [] })) as { error: string }).error, /file name/);
        assert.match((parseRecording(JSON.stringify({ dettato: 1, audio: 'x.wav', bars: [{ start: 2, end: 4 }, { start: 1, end: 2 }] })) as { error: string }).error, /before/);
        assert.match((parseRecording(JSON.stringify({ dettato: 1, audio: 'x.wav', bars: [{ start: 2, end: 2 }] })) as { error: string }).error, /after/);
        assert.match((parseRecording('{ nope') as { error: string }).error, /JSON/);
    });

    it('is written back as format 2, keeping what it does not know and setting byHand', () => {
        const r = ok(FORMAT1);
        r.bars[1] = { start: 1.95, end: 3.96, byHand: true };
        const back = JSON.parse(serializeRecording(r));
        assert.equal(back.dettato, 2);
        assert.equal(back.key, 'F# major');
        assert.deepEqual(back.form, ['Pickup', 'A']);
        assert.equal(back.bars[1].start, 1.95);
        assert.equal(back.bars[1].byHand, true);
        assert.equal(back.bars[1].chords[0].symbol, 'F#/A#');
        assert.equal(back.bars[0].pickup, 3);
        assert.equal('byHand' in back.bars[0], false);
        // …and reads back the same.
        const again = ok(serializeRecording(r));
        assert.equal(again.format, 2);
        assert.deepEqual(again.bars, r.bars);
        assert.equal(again.pickup, true);
    });

    it('starts empty for a recording a person attached', () => {
        const back = JSON.parse(serializeRecording(newRecording('take.mp3', 120.5)));
        assert.deepEqual(back, { dettato: 2, audio: 'take.mp3', duration: 120.5, bars: [] });
    });
});

describe('the score\'s bars and the recording\'s seconds', () => {
    it('counts the score\'s bars by the accented beats, a pickup as bar 0', () => {
        assert.deepEqual(scoreBars(beats(2), 4), [[0, 2], [2, 4]]);
        // A 3-beat pickup: the first accent is where it ends (session 1242).
        assert.deepEqual(scoreBars(beats(2, 3), 5.5), [[0, 1.5], [1.5, 3.5], [3.5, 5.5]]);
    });

    it('finds the bar a time is in', () => {
        const bars = scoreBars(beats(3), 6);
        assert.equal(barAt(bars, 0), 0);
        assert.equal(barAt(bars, 1.99), 0);
        assert.equal(barAt(bars, 2), 1);
        assert.equal(barAt(bars, 6), 2);
        assert.equal(barAt(bars, 6.5), -1);
        assert.equal(barAt(bars, -0.1), -1);
    });

    it('maps a time inside a bar in a straight line, each bar its own tempo', () => {
        const r = ok(FORMAT1);
        const bars = scoreBars(beats(2, 3), 5.5);
        assert.equal(recordingSecondAt(bars, r, 0), 0.355);
        assert.ok(Math.abs(recordingSecondAt(bars, r, 1.5)! - 1.9) < 1e-9);
        // Halfway through bar 1 (score 1.5..3.5) is halfway through 1.9..3.96.
        assert.ok(Math.abs(recordingSecondAt(bars, r, 2.5)! - 2.93) < 1e-9);
        assert.ok(Math.abs(scoreTimeAt(bars, r, 2.93)! - 2.5) < 1e-9);
        assert.ok(Math.abs(scoreTimeAt(bars, r, 6.0)! - 5.5) < 1e-9);
        assert.equal(scoreTimeAt(bars, r, 0.1), null);
        assert.equal(scoreTimeAt(bars, r, 7), null);
    });

    it('says when the chart and the recording no longer line up, and places no bar past the recording', () => {
        const r = ok(FORMAT1);
        const bars = scoreBars(beats(3, 3), 7.5);
        assert.deepEqual(lineUp(bars, r), { scoreBars: 4, recordingBars: 3, same: false });
        assert.equal(recordingSecondAt(bars, r, 6.0), null);
        assert.deepEqual(lineUp(scoreBars(beats(2, 3), 5.5), r).same, true);
    });
});
