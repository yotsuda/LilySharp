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
    RECORDING_DEFAULTS, Recording, ScoreBeat, audioPathFor, recordingPlan, audioReader, barAt, chartForRecording, lineUp, newRecording, parseRecording,
    recordingPathFor, recordingSecondAt, scoreBars, scoreTimeAt, serializeRecording, timeWarp, unwarpTime, warpBeats, warpFit, warpNotes, warpTime, workForRename,
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

describe('attaching a recording', () => {
    it('reads wav, mp3, flac and ogg in the webview, an m4a by the server on Windows only', () => {
        assert.equal(audioReader('a.WAV', false), 'webview');
        assert.equal(audioReader('C:\\x\\take.ogg', true), 'webview');
        assert.equal(audioReader('take.m4a', true), 'server');
        assert.match((audioReader('take.m4a', false) as { refused: string }).refused, /Windows only/);
        assert.match((audioReader('take.opus', true) as { refused: string }).refused, /not a recording/);
        assert.match((audioReader('noext', true) as { refused: string }).refused, /not a recording/);
    });

    it('goes to the chart of its own name, else the open chart in its folder, else the folder\'s only chart', () => {
        assert.equal(chartForRecording('Song.m4a', ['other.lys', 'song.lys'], 'other.lys'), 'song.lys');
        assert.equal(chartForRecording('take 3.wav', ['a.lys', 'b.lys'], 'b.lys'), 'b.lys');
        assert.equal(chartForRecording('take 3.wav', ['a.lys', 'b.lys'], 'elsewhere.lys'), undefined);
        assert.equal(chartForRecording('take 3.wav', ['a.lys']), 'a.lys');
        assert.equal(chartForRecording('take 3.wav', []), undefined);
    });

    it('takes the file beside a chart along when the chart is renamed, not over one already there', () => {
        const files = new Set(['/s/song.dettato.json']);
        assert.deepEqual(workForRename('/s/song.lys', '/s/tune.lys', p => files.has(p), () => undefined),
            [{ kind: 'move', from: '/s/song.dettato.json', to: '/s/tune.dettato.json' }]);
        files.add('/s/tune.dettato.json');
        assert.deepEqual(workForRename('/s/song.lys', '/s/tune.lys', p => files.has(p), () => undefined), []);
        assert.deepEqual(workForRename('/s/none.lys', '/s/x.lys', p => files.has(p), () => undefined), []);
    });

    it('renames the recording inside every file in its folder that names it', () => {
        const names: Record<string, string> = { '/s/chart.dettato.json': 'take.m4a', '/s/other.dettato.json': 'b.wav' };
        const work = workForRename('/s/take.m4a', '/s/final.m4a', p => p in names, p => names[p], Object.keys(names));
        assert.deepEqual(work, [{ kind: 'rename', file: '/s/chart.dettato.json', audio: 'final.m4a' }]);
        // Moved to another folder: the file can no longer name it — nothing is rewritten.
        assert.deepEqual(workForRename('/s/take.m4a', '/t/take.m4a', p => p in names, p => names[p], Object.keys(names)), []);
    });
});

describe('the recording panel', () => {
    it('plays the recording as it is, resampled at 1, when nothing is changed', () => {
        assert.deepEqual(recordingPlan(RECORDING_DEFAULTS), { engine: 'resample', rate: 1, semitones: 0, gain: 1 });
    });

    // The chart's own transpose (owner's decision 2026-11-16): the recording follows it, by pitch
    // alone — on top of the panel's, and a linked (tape) recording is stretched rather than sped up.
    it('follows the chart\'s transpose by pitch alone, linked or not', () => {
        assert.deepEqual(recordingPlan({ ...RECORDING_DEFAULTS, chart: 2 }), { engine: 'stretch', rate: 1, semitones: 2, gain: Math.pow(10, -3 / 20) });
        assert.equal(recordingPlan({ ...RECORDING_DEFAULTS, semitones: 1, chart: -3 }).semitones, -2);
        const tape = recordingPlan({ ...RECORDING_DEFAULTS, linked: true, octave: 1, chart: -2 });
        assert.equal(tape.engine, 'stretch');
        assert.ok(Math.abs(tape.rate - 2) < 1e-9);      // the panel's pitch sets the speed …
        assert.equal(tape.semitones, 10);                // … and the chart moves the pitch alone
        assert.deepEqual(recordingPlan({ ...RECORDING_DEFAULTS, linked: true, chart: 0 }), { engine: 'resample', rate: 1, semitones: 0, gain: 1 });
    });

    it('stretches for a speed or a pitch apart, turning a raised pitch down 3 dB', () => {
        const slow = recordingPlan({ ...RECORDING_DEFAULTS, speed: 0.5 });
        assert.deepEqual(slow, { engine: 'stretch', rate: 0.5, semitones: 0, gain: 1 });
        const up = recordingPlan({ ...RECORDING_DEFAULTS, octave: 1, semitones: 2, cents: -30 });
        assert.equal(up.engine, 'stretch');
        assert.ok(Math.abs(up.semitones - 13.7) < 1e-9);
        assert.ok(Math.abs(up.gain - 0.7079) < 1e-4);
        assert.equal(recordingPlan({ ...RECORDING_DEFAULTS, octave: -1 }).gain, 1);
    });

    it('resamples when pitch and speed go together: a tape set right', () => {
        const tape = recordingPlan({ ...RECORDING_DEFAULTS, cents: 30, speed: 0.5, linked: true });
        assert.equal(tape.engine, 'resample');
        assert.ok(Math.abs(tape.rate - Math.pow(2, 0.3 / 12)) < 1e-12);
        assert.equal(recordingPlan({ ...RECORDING_DEFAULTS, octave: 1, linked: true }).rate, 2);
    });

    it('keeps the settings in the panel\'s ranges', () => {
        const p = recordingPlan({ speed: 9, octave: 5, semitones: 40, cents: 900, linked: false });
        assert.equal(p.rate, 1.5);
        assert.ok(Math.abs(p.semitones - (24 + 11 + 0.5)) < 1e-9);
        assert.equal(recordingPlan({ ...RECORDING_DEFAULTS, speed: NaN }).rate, 1);
    });

    it('plays at the practice menu\'s tempo too, the pitch kept (§1 7)', () => {
        const slow = recordingPlan(RECORDING_DEFAULTS, 0.8);
        assert.equal(slow.engine, 'stretch');
        assert.equal(slow.rate, 0.8);
        assert.equal(slow.semitones, 0);
        assert.equal(recordingPlan({ ...RECORDING_DEFAULTS, speed: 0.5 }, 0.8).rate, 0.4);
        // A tape set right, at another tempo: its pitch as the tape's, its rate times the tempo — stretched.
        const tape = recordingPlan({ ...RECORDING_DEFAULTS, octave: 1, linked: true }, 0.5);
        assert.deepEqual([tape.engine, tape.rate, tape.semitones], ['stretch', 1, 12]);
        assert.deepEqual(recordingPlan(RECORDING_DEFAULTS, 1), recordingPlan(RECORDING_DEFAULTS));
        assert.equal(recordingPlan(RECORDING_DEFAULTS, NaN).rate, 1);
    });
});

describe('the score played with the recording (§1 7)', () => {
    it('lines up the score\'s bars with the recording\'s, or says it cannot', () => {
        const r = ok(FORMAT1);
        const w = timeWarp(beats(2, 3), 5.5, r)!;
        assert.deepEqual(w.score, [[0, 1.5], [1.5, 3.5], [3.5, 5.5]]);
        assert.deepEqual(w.rec, [[0.355, 1.9], [1.9, 3.96], [3.96, 6.0]]);
        assert.equal(timeWarp(beats(3, 3), 7.5, r), null);           // a bar more in the score
        assert.equal(timeWarp(beats(2, 3), 5.5, { bars: [] }), null);  // only attached: no bar placed
        assert.equal(timeWarp(null, 5.5, r), null);
        assert.equal(timeWarp(beats(2, 3), null, r), null);
    });

    it('plays the recording at the pace the score writes (owner\'s decision 2026-11-16)', () => {
        // Score 0..5.5 at its tempo; the recording places the same bars over 0.355..6.0.
        const w = timeWarp(beats(2, 3), 5.5, ok(FORMAT1))!;
        assert.ok(Math.abs(warpFit(w) - (6.0 - 0.355) / 5.5) < 1e-12);
        // A chart written at 120 (the helper's beats) over a recording at 121: the recording a
        // shade slower, 120/121 — whatever lead it has before its first bar.
        const bars = (n: number, len: number, from = 0) => Array.from({ length: n }, (_, i) => ({ start: from + i * len, end: from + (i + 1) * len }));
        const w120 = timeWarp(beats(4), 8, { bars: bars(4, 4 * 60 / 121, 1.5) })!;
        assert.ok(Math.abs(warpFit(w120) - 120 / 121) < 1e-12);
    });

    it('moves the score\'s notes onto the recording\'s seconds, bar by bar', () => {
        const w = timeWarp(beats(2, 3), 5.5, ok(FORMAT1))!;
        const near = (a: number, b: number) => assert.ok(Math.abs(a - b) < 1e-9, `${a} vs ${b}`);
        near(warpTime(w, 0), 0.355);
        near(warpTime(w, 2.5), 2.93);
        near(warpTime(w, 5.5), 6.0);
        // Before the first bar and past the last, at that bar's pace.
        near(warpTime(w, 6.5), 6.0 + 1 * (6.0 - 3.96) / 2);
        near(warpTime(w, -0.5), 0.355 - 0.5 * (1.9 - 0.355) / 1.5);
        for (const t of [-0.5, 0, 0.7, 1.5, 2.5, 3.5, 4.9, 5.5, 6.5]) {
            near(unwarpTime(w, warpTime(w, t)), t);
        }
        // A note across a bar line: its start and end each moved by its own bar.
        const [n] = warpNotes([{ T: 1, D: 1.5, P: 60 }], w);
        near(n.T, warpTime(w, 1));
        near(n.T + n.D, warpTime(w, 2.5));
        assert.equal(n.P, 60);
        assert.equal(warpNotes([{ T: 1, D: 0 }], w)[0].D, 0);
        const [b] = warpBeats([{ T: 3.5, A: true }], w);
        assert.deepEqual(b, { T: 3.96, A: true });
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
