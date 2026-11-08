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
import { WAV_TAIL_S, decodeWav, fileSeconds, mixIn } from '../src/audioRenderCore';
import { PlaybackNote, exportPlan } from '../src/webview/playbackCore';

// The sung voices mixed into a WAV, MP3 or MP4 export (docs/singing-design.md §5): the engine's
// WAV read, resampled to the file's rate and added from the count-in's lead, the file grown to
// hold it.

/** A 16-bit PCM WAV of `samples` (−1..1) at `rate`, `channels` copies of each. */
function wav16(samples: number[], rate: number, channels = 1): Uint8Array {
    const data = new Uint8Array(samples.length * channels * 2);
    const view = new DataView(data.buffer);
    samples.forEach((s, i) => { for (let c = 0; c < channels; c++) { view.setInt16((i * channels + c) * 2, Math.round(s * 32767), true); } });
    const header = new Uint8Array(44);
    const h = new DataView(header.buffer);
    const tag = (at: number, s: string) => { for (let i = 0; i < 4; i++) { header[at + i] = s.charCodeAt(i); } };
    tag(0, 'RIFF'); h.setUint32(4, 36 + data.length, true); tag(8, 'WAVE');
    tag(12, 'fmt '); h.setUint32(16, 16, true); h.setUint16(20, 1, true); h.setUint16(22, channels, true);
    h.setUint32(24, rate, true); h.setUint32(28, rate * channels * 2, true); h.setUint16(32, channels * 2, true); h.setUint16(34, 16, true);
    tag(36, 'data'); h.setUint32(40, data.length, true);
    const out = new Uint8Array(44 + data.length);
    out.set(header); out.set(data, 44);
    return out;
}

describe('the engine\'s WAV, read', () => {
    it('is 16-bit PCM at its own rate, several channels averaged to one', () => {
        const mono = decodeWav(wav16([0, 0.5, -0.5, 1], 48000));
        assert.equal(mono.sampleRate, 48000);
        assert.deepEqual([...mono.samples].map(s => Math.round(s * 100) / 100), [0, 0.5, -0.5, 1]);
        const stereo = decodeWav(wav16([0.5, -0.25], 44100, 2));
        assert.equal(stereo.samples.length, 2);
        assert.ok(Math.abs(stereo.samples[0] - 0.5) < 1e-3);
    });

    it('refuses what is not a WAV', () => {
        assert.throws(() => decodeWav(new Uint8Array([1, 2, 3])), /not a WAV/);
    });
});

describe('a voice mixed into the file', () => {
    it('is added to both channels from its offset, resampled to the file\'s rate', () => {
        // A 2-sample voice at 48 kHz into a 4 kHz file: every 12th source sample, linearly between.
        const voice = { sampleRate: 48000, samples: new Float32Array([0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4]) };
        const rate = 4000;
        const left = new Float32Array(rate * 3), right = new Float32Array(rate * 3);
        left[0] = 0.1;
        const [l, r] = mixIn(left, right, voice, 0.5, rate);
        assert.equal(l.length, rate * 3, 'the file was long enough: not grown');
        assert.ok(Math.abs(l[0] - 0.1) < 1e-6, 'what was there stays');
        const start = Math.round(0.5 * rate);
        assert.ok(Math.abs(l[start] - 0.4) < 1e-6 && Math.abs(r[start] - 0.4) < 1e-6, 'the voice starts at its offset');
        assert.equal(l[start - 1], 0);
        assert.equal(l[start + 2], 0, 'two source frames at 48 kHz are two frames at 4 kHz');
    });

    it('leaves out the head of a voice that begins before the file, and starts the rest at 0 (session 1153)', () => {
        // The engine's WAV begins 0.3 s before the score's 0 (VOICE_LEAD_S): at −0.3, its first
        // 0.3 s go, and the sample at 0.3 s of the voice is the file's first.
        const rate = 1000;
        const samples = new Float32Array(1000);
        for (let i = 0; i < samples.length; i++) { samples[i] = i / 1000; }   // 0.000, 0.001, … a ramp
        const [l] = mixIn(new Float32Array(2000), new Float32Array(2000), { sampleRate: rate, samples }, -0.3, rate);
        assert.ok(Math.abs(l[0] - 0.3) < 1e-6, 'the file starts 0.3 s into the voice');
        assert.ok(Math.abs(l[699] - 0.999) < 1e-6, 'the voice\'s last sample lands 0.7 s in');
        assert.equal(l[700], 0, 'nothing after it');
        assert.equal(l.length, 700 + WAV_TAIL_S * rate, 'the file runs to the voice\'s end and the tail, not to the head left out');
    });

    it('grows the file when the voice and its tail run past the notes', () => {
        const rate = 1000;
        const voice = { sampleRate: 1000, samples: new Float32Array(2500).fill(0.2) };
        const [l, r] = mixIn(new Float32Array(1000), new Float32Array(1000), voice, 1, rate);
        assert.equal(l.length, Math.round((1 + 2.5 + WAV_TAIL_S) * rate));
        assert.equal(r.length, l.length);
        assert.ok(Math.abs(l[3000] - 0.2) < 1e-6);
        assert.equal(l[l.length - 1], 0, 'the tail is silent');
    });

    it('counts into the file\'s length', () => {
        const notes: PlaybackNote[] = [{ T: 0, D: 1, P: 60, V: 80, S: 0 }];
        assert.equal(fileSeconds(notes, null), 1 + WAV_TAIL_S);
        assert.equal(fileSeconds(notes, [{ wav: 'x', at: 0.5, duration: 4 }]), 4.5 + WAV_TAIL_S);
        assert.equal(fileSeconds(notes, [{ wav: 'x', at: 0, duration: 0.5 }]), 1 + WAV_TAIL_S);
    });
});

describe('where the music\'s zero stands in an export', () => {
    const parts = ['Tempo', 'sop'];
    const notes: PlaybackNote[] = [{ T: 0, D: 1, P: 72, V: 80, S: 1, N: 1 }, { T: 1, D: 1, P: 74, V: 80, S: 5, N: 1 }];
    const beats = [{ T: 0, A: true }, { T: 0.5, A: false }, { T: 1, A: true }, { T: 1.5, A: false }, { T: 2, A: true }];

    it('is 0 without a count-in, and the count-in\'s lead with one — the voice starts there', () => {
        assert.equal(exportPlan(notes, parts, beats, undefined).lead, 0);
        assert.equal(exportPlan(notes, parts, beats, { speed: 1, muted: [], click: false, countIn: false }).lead, 0);
        const counted = exportPlan(notes, parts, beats, { speed: 1, muted: [], click: false, countIn: true });
        assert.equal(counted.lead, 1, 'one bar of two beats');
        assert.equal(counted.notes.filter(n => n.S >= 0)[0].T, 1, 'the first note moved after the count');
    });
});
