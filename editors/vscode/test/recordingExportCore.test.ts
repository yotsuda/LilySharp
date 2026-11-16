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
import { recordedExportPlan } from '../src/recordingExportCore';
import { PlaybackBeat, PlaybackNote, PracticeState, exportPlan } from '../src/webview/playbackCore';
import { RecordingTrack, WAV_TAIL_S, fileSeconds, mixInStereo } from '../src/audioRenderCore';
import { RECORDING_DEFAULTS } from '../src/recordingCore';

// Two bars of 4/4 at 120: a bar a second, beats every half second.
const BEATS: PlaybackBeat[] = [0, 0.5, 1, 1.5].map(t => ({ T: t, A: t === 0 || t === 1 }));
const note = (t: number): PlaybackNote => ({ T: t, D: 0.5, P: 60, V: 100, S: 0, O: 0, I: 0, C: 0, G: 0, N: 0 });
const NOTES = [note(0), note(1)];
const PRACTICE: PracticeState = { speed: 1, muted: [], click: false, countIn: false };
// The recording: a second of intro, then each bar two seconds — the band played at half the speed written.
const ALONG = { bars: [{ start: 1, end: 3 }, { start: 3, end: 5 }], sound: { ...RECORDING_DEFAULTS }, level: 1 };

describe('an export with the recording beside the chart', () => {
    it('is exportPlan itself when no recording sounds with the score', () => {
        const plan = recordedExportPlan(NOTES, null, BEATS, 2, PRACTICE);
        assert.deepEqual(plan.notes, exportPlan(NOTES, null, BEATS, PRACTICE).notes);
        assert.equal(plan.recording, null);
    });

    it('moves the notes onto the recording at its rate, its second 0 at the file\'s 0', () => {
        const plan = recordedExportPlan(NOTES, null, BEATS, 2, { ...PRACTICE, recording: ALONG });
        // The score's tempo sets the pace (warpFit = 2: the recording is played twice as fast),
        // so bar 1 (the recording's second 1) sounds at 0.5 s and bar 2 (second 3) at 1.5 s.
        assert.ok(plan.recording);
        assert.equal(plan.recording.tempo, 2);
        assert.equal(plan.lead, 0);
        const music = plan.notes.filter(n => n.C !== 9);
        assert.deepEqual(music.map(n => +n.T.toFixed(6)), [0.5, 1.5]);
        assert.deepEqual(music.map(n => +n.D.toFixed(6)), [0.5, 0.5]);
    });

    it('keeps the count-in before both: the notes and the recording start after the lead', () => {
        const plan = recordedExportPlan(NOTES, null, BEATS, 2, { ...PRACTICE, countIn: true, recording: ALONG });
        assert.ok(plan.recording);
        assert.ok(plan.lead > 0, `lead ${plan.lead}`);
        const music = plan.notes.filter(n => n.C !== 9);
        assert.deepEqual(music.map(n => +(n.T - plan.lead).toFixed(6)), [0.5, 1.5]);
    });

    it('follows the practice tempo on top of the score\'s', () => {
        const plan = recordedExportPlan(NOTES, null, BEATS, 2, { ...PRACTICE, speed: 0.5, recording: ALONG });
        assert.ok(plan.recording);
        assert.equal(plan.recording.tempo, 1);
        assert.deepEqual(plan.notes.map(n => +n.T.toFixed(6)), [1, 3]);
    });

    it('leaves the recording out when no bar is placed in it', () => {
        const plan = recordedExportPlan(NOTES, null, BEATS, 2, { ...PRACTICE, recording: { ...ALONG, bars: [] } });
        assert.equal(plan.recording, null);
        assert.deepEqual(plan.notes, exportPlan(NOTES, null, BEATS, PRACTICE).notes);
    });
});

describe('the recording mixed into a file', () => {
    const track = (at: number, gain = 1): RecordingTrack => ({
        left: Float32Array.from([0.1, 0.2, 0.3, 0.4]), right: Float32Array.from([-0.1, -0.2, -0.3, -0.4]),
        sampleRate: 4, at, gain,
    });

    it('adds its two channels apart from its second, at its gain', () => {
        const [l, r] = mixInStereo(new Float32Array(8), new Float32Array(8), track(0.5, 2), 4);
        assert.deepEqual(Array.from(l).map(v => +v.toFixed(6)), [0, 0, 0.2, 0.4, 0.6, 0.8, 0, 0]);
        assert.deepEqual(Array.from(r).map(v => +v.toFixed(6)), [0, 0, -0.2, -0.4, -0.6, -0.8, 0, 0]);
    });

    it('grows the file to hold it to its end', () => {
        const [l] = mixInStereo(new Float32Array(2), new Float32Array(2), track(1), 4);
        assert.equal(l.length, 8);
        assert.equal(+l[7].toFixed(6), 0.4);
    });

    it('runs the file on to the recording\'s end', () => {
        const notes = [{ T: 0, D: 1, P: 60, V: 100, S: 0, O: 0, I: 0, C: 0, G: 0, N: 0 }];
        assert.equal(fileSeconds(notes, null), 1 + WAV_TAIL_S);
        assert.equal(fileSeconds(notes, null, 4, { ...track(10), sampleRate: 4 }), 11);
    });
});
