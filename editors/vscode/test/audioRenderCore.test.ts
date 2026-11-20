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
import { PlaybackNote, toEvents } from '../src/webview/playbackCore';
import {
    MP3_FRAME, Mp3Encoder, OfflineSynth, RENDER_BLOCK, WAV_PEAK, WAV_TAIL_S,
    MP3_ENCODER_DELAY, crc16, encodeMp3, fileLevel, frameCount, mp3Frames, renderNotes, toInt16, withGaplessTag,
} from '../src/audioRenderCore';

/** The server's pitch for a sustain-pedal change (PlaybackNote.PedalPitch; isPedal reads P < 0). */
const PEDAL = -1;

const note = (t: number, d: number, p: number, extra: Partial<PlaybackNote> = {}): PlaybackNote =>
    ({ T: t, D: d, P: p, V: 90, S: 10 * t, O: 0, I: 0, C: 0, G: 0, ...extra });

/** A synth that writes down what it is told and, for each block, the frame it starts at. */
class RecordingSynth implements OfflineSynth {
    readonly calls: string[] = [];
    private frame = 0;
    noteOn(c: number, p: number, v: number) { this.calls.push(`${this.frame} on ${c} ${p} ${v}`); }
    noteOff(c: number, p: number) { this.calls.push(`${this.frame} off ${c} ${p}`); }
    programChange(c: number, g: number) { this.calls.push(`${this.frame} program ${c} ${g}`); }
    controllerChange(c: number, cc: number, v: number) { this.calls.push(`${this.frame} cc ${c} ${cc} ${v}`); }
    pitchWheel(c: number, v: number) { this.calls.push(`${this.frame} bend ${c} ${v}`); }
    process(left: Float32Array, right: Float32Array, start: number, count: number) {
        assert.equal(start, this.frame, 'blocks follow one another');
        left.fill(0.5, start, start + count);
        right.fill(-0.25, start, start + count);
        this.frame += count;
    }
}

// The WAV is the preview's Play written down (owner's request 2026-11-07): the same events
// (toEvents) handed to the same synth, each before the block it falls in.
describe('the WAV render', () => {
    const rate = 1000;   // a round rate, so a time is a frame count to read off

    it('gives the synth the preview\'s events, each at the block it falls in', async () => {
        const notes = [note(0, 0.5, 60), note(0.5, 0.25, 64, { C: 1, G: 40 })];
        const synth = new RecordingSynth();
        await renderNotes(synth, notes, rate);
        // The events are toEvents' own, in its order; each lands on the first block boundary at or
        // after its frame (RENDER_BLOCK frames to a block).
        const at = (seconds: number) => Math.ceil(Math.round(seconds * rate) / RENDER_BLOCK) * RENDER_BLOCK;
        const expected = toEvents(notes, 0).map(e => {
            const f = at(e.t);
            switch (e.kind) {
                case 'program': return `${f} program ${e.c} ${e.g}`;
                case 'on': return `${f} on ${e.c} ${e.p} ${e.v}`;
                case 'off': return `${f} off ${e.c} ${e.p}`;
                case 'pedal': return `${f} cc ${e.c} 64 ${e.v}`;
                case 'bend': return `${f} bend ${e.c} ${e.value}`;
            }
        });
        assert.deepEqual(synth.calls, expected);
    });

    it('turns a pedal change into the sustain controller', async () => {
        const notes = [note(0, 1, 60), { ...note(0.2, 0, PEDAL), V: 127, S: -1 }];
        const synth = new RecordingSynth();
        await renderNotes(synth, notes, rate);
        assert.ok(synth.calls.some(c => / cc 0 64 127$/.test(c)), synth.calls.join('\n'));
    });

    it('runs to the last note\'s end and the tail', async () => {
        const notes = [note(0, 1, 60), note(1, 1.5, 62)];
        const [left, right] = await renderNotes(new RecordingSynth(), notes, rate);
        assert.equal(left.length, frameCount(notes, rate));
        assert.equal(left.length, Math.ceil((2.5 + WAV_TAIL_S) * rate));
        assert.equal(right.length, left.length);
    });

    it('pauses between stretches of a long render', async () => {
        let pauses = 0;
        await renderNotes(new RecordingSynth(), [note(0, 10, 60)], rate, async () => { pauses++; }, 2000);
        assert.equal(pauses, Math.floor(frameCount([note(0, 10, 60)], rate) / 2048));
    });
});

describe('the WAV level', () => {
    it('puts the loudest sample at -1 dBFS, a quiet piece raised', () => {
        const left = Float32Array.from([0.05, -0.1]), right = Float32Array.from([0.02, 0.04]);
        fileLevel(left, right);
        assert.ok(Math.abs(Math.abs(left[1]) - WAV_PEAK) < 1e-6);
    });

    it('scales the whole piece as one, a loud one lowered, so the balance is kept', () => {
        const left = Float32Array.from([2, 0.5]), right = Float32Array.from([-1, 0]);
        fileLevel(left, right);
        assert.ok(Math.abs(left[0] - WAV_PEAK) < 1e-6);
        assert.ok(Math.abs(left[1] / left[0] - 0.25) < 1e-6);
        assert.ok(Math.abs(right[0] / left[0] - -0.5) < 1e-6);
    });

    it('leaves silence alone', () => {
        const left = new Float32Array(4), right = new Float32Array(4);
        assert.equal(fileLevel(left, right), 1);
        assert.deepEqual([...left], [0, 0, 0, 0]);
    });
});

// The MP3 export (owner's request 2026-11-07): the same sound, encoded.
describe('the MP3 writing', () => {
    it('turns the samples into 16-bit ones, past either end held there', () => {
        assert.deepEqual([...toInt16(Float32Array.from([0, 0.5, -1, 1, 2, -3]))], [0, 16384, -32767, 32767, 32767, -32767]);
    });

    /** An encoder that writes down the stretches it is fed and answers one byte per stretch. */
    class RecordingEncoder implements Mp3Encoder {
        readonly fed: number[] = [];
        flushed = 0;
        encodeBuffer(left: Int16Array, right?: Int16Array): Uint8Array {
            assert.equal(right?.length, left.length, 'both channels, the same length');
            this.fed.push(left.length);
            return Uint8Array.from([left.length % 251]);
        }
        flush(): Uint8Array { this.flushed++; return Uint8Array.from([255, 254]); }
    }

    it('feeds the encoder whole MP3 frames a stretch at a time, flushes once and joins the pieces', async () => {
        const n = MP3_FRAME * 5 + 100;
        const encoder = new RecordingEncoder();
        let pauses = 0;
        const out = await encodeMp3(encoder, new Float32Array(n), new Float32Array(n), async () => { pauses++; }, MP3_FRAME * 2);
        assert.deepEqual(encoder.fed, [MP3_FRAME * 2, MP3_FRAME * 2, MP3_FRAME + 100]);
        assert.equal(encoder.flushed, 1);
        assert.equal(pauses, 2, 'between stretches, not after the last');
        assert.deepEqual([...out], [...encoder.fed.map(f => f % 251), 255, 254]);
    });

    // Shosa's request 3 (2026-11-20): lamejs writes no tag, and every MP3 export decoded 1105
    // samples (25.1 ms) late. The tag says the encoder's delay and the padding, and a gapless decoder
    // gives back the samples as they went in (measured with ffmpeg and Chromium, Lab p1339/align).
    describe('the gapless tag', () => {
        /** MPEG-1 Layer III frames at 192 kb/s, 44.1 kHz, joint stereo; `padded` marks the ones a byte longer. */
        const stream = (padded: boolean[]) => {
            const frames = padded.map((p, i) => {
                const f = new Uint8Array(626 + (p ? 1 : 0));
                f.set([0xff, 0xfb, 0xb0 | (p ? 2 : 0), 0x44]);
                f.fill(i + 1, 4);
                return f;
            });
            const out = new Uint8Array(frames.reduce((n, f) => n + f.length, 0));
            let at = 0;
            for (const f of frames) { out.set(f, at); at += f.length; }
            return out;
        };

        it('counts the frames', () => {
            assert.deepEqual(mp3Frames(stream([false, true, false])), { count: 3, kbps: 192, sampleRate: 44100 });
            assert.equal(mp3Frames(Uint8Array.from([1, 2, 3, 4])), null);
            assert.equal(mp3Frames(stream([false]).subarray(0, 600)), null, 'a frame cut short');
        });

        it('puts an Info frame ahead with the delay, the padding and the CRC LAME writes', () => {
            const s = stream([false, true, false, false]);
            const input = 4 * MP3_FRAME - MP3_ENCODER_DELAY - 1000;
            const out = withGaplessTag(s, input);
            assert.equal(out.length, 626 + s.length);
            assert.deepEqual([...out.subarray(626)], [...s], 'the stream after it, untouched');
            assert.deepEqual([...out.subarray(0, 4)], [0xff, 0xfb, 0xb0, 0x44], 'the stream\'s own header, unpadded');
            const text = (from: number, n: number) => String.fromCharCode(...out.subarray(from, from + n));
            const u32 = (at: number) => ((out[at] << 24) | (out[at + 1] << 16) | (out[at + 2] << 8) | out[at + 3]) >>> 0;
            assert.equal(text(36, 4), 'Info');
            assert.equal(u32(40), 0x0f);
            assert.equal(u32(44), 4, 'the frames of the music');
            assert.equal(u32(48), out.length, 'the bytes of the file');
            assert.equal(text(156, 9), 'LAME3.100');
            const delays = (out[177] << 16) | (out[178] << 8) | out[179];
            assert.deepEqual([delays >> 12, delays & 4095], [MP3_ENCODER_DELAY, 1000]);
            assert.equal((out[190] << 8) | out[191], crc16(out, 0, 190));
            // CRC-16/ARC's check value.
            assert.equal(crc16(Uint8Array.from('123456789', c => c.charCodeAt(0)), 0, 9), 0xbb3d);
        });

        it('leaves a stream alone when it cannot say the padding', () => {
            const s = stream([false]);
            assert.equal(withGaplessTag(s, MP3_FRAME * 2), s, 'more samples than the frames hold');
            assert.equal(withGaplessTag(Uint8Array.from([1, 2]), 10).length, 2);
        });
    });
});
