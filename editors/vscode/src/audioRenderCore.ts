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

// The WAV and MP3 exports' sound, as the preview plays it (owner's requests 2026-11-07: "the WAV
// exporter sounds as the preview does", then an MP3 export beside it). The preview turns the
// server's note list into MIDI events (playbackCore.toEvents) and hands them to spessasynth on
// the audio clock; this hands the SAME events to the same synth, offline, block by block. The
// synth and the MP3 encoder are given from outside (audioRender.ts builds spessasynth_core's
// processor and lamejs's encoder), so the scheduling and the framing are tested here without a
// sound font or an encoder.

import { MidiEvent, PlaybackNote, endOf, toEvents } from './webview/playbackCore';

/** Stereo at 44.1 kHz, as `lysc wav` writes (16-bit in a WAV). */
export const WAV_SAMPLE_RATE = 44100;
/** Seconds after the last note ends, so it rings out (`lysc wav` keeps two). */
export const WAV_TAIL_S = 2;
/** The synth's block: spessasynth's worklet renders 128 frames at a time and applies the events
 *  due at a block's start, so the offline render steps the same way. */
export const RENDER_BLOCK = 128;
/** Where the file's loudest sample is put: −1 dBFS, the usual headroom for a file a player or
 *  an encoder takes (an MP3's decoded peaks overshoot the PCM's a little). */
export const WAV_PEAK = Math.pow(10, -1 / 20);
/** The MP3's bit rate: constant, and well above where a piano's decay starts to swirl. */
export const MP3_KBPS = 192;
/** The frames an MP3 frame holds (MPEG-1 Layer III); the encoder is fed whole multiples. */
export const MP3_FRAME = 1152;
const SUSTAIN = 64;

/** What the render needs of a synth — spessasynth_core's SpessaSynthProcessor has these. */
export interface OfflineSynth {
    noteOn(channel: number, key: number, velocity: number): void;
    noteOff(channel: number, key: number): void;
    programChange(channel: number, program: number): void;
    controllerChange(channel: number, controller: number, value: number): void;
    pitchWheel(channel: number, value: number): void;
    process(left: Float32Array, right: Float32Array, startIndex: number, sampleCount: number): void;
}

/** Hands one event to the synth, as the preview's Player.dispatch does on the audio clock. */
export function dispatch(synth: OfflineSynth, e: MidiEvent): void {
    switch (e.kind) {
        case 'program': synth.programChange(e.c, e.g); break;
        case 'bend': synth.pitchWheel(e.c, e.value); break;
        case 'pedal': synth.controllerChange(e.c, SUSTAIN, e.v); break;
        case 'on': synth.noteOn(e.c, e.p, e.v); break;
        case 'off': synth.noteOff(e.c, e.p); break;
    }
}

/** How many frames the file holds: the last note's end and the tail. */
export function frameCount(notes: readonly PlaybackNote[], sampleRate = WAV_SAMPLE_RATE): number {
    return Math.ceil((endOf(notes) + WAV_TAIL_S) * sampleRate);
}

/**
 * Plays `notes` through `synth` from the start and returns the two channels. Every event is
 * given to the synth before the block it falls in is rendered. `pause`, when given, is awaited
 * every `pauseEvery` frames, so a long piece does not hold the extension host for its whole
 * render.
 */
export async function renderNotes(synth: OfflineSynth, notes: readonly PlaybackNote[],
    sampleRate = WAV_SAMPLE_RATE, pause?: () => Promise<void>, pauseEvery = sampleRate * 4,
): Promise<[Float32Array, Float32Array]> {
    const events = toEvents(notes, 0);
    const total = frameCount(notes, sampleRate);
    const left = new Float32Array(total), right = new Float32Array(total);
    let next = 0, sincePause = 0;
    for (let filled = 0; filled < total; ) {
        while (next < events.length && Math.round(events[next].t * sampleRate) <= filled) {
            dispatch(synth, events[next++]);
        }
        const n = Math.min(RENDER_BLOCK, total - filled);
        synth.process(left, right, filled, n);
        filled += n;
        sincePause += n;
        if (pause && sincePause >= pauseEvery) { sincePause = 0; await pause(); }
    }
    return [left, right];
}

/**
 * The file's level: the whole piece scaled as one, its loudest sample at {@link WAV_PEAK}, so the
 * sound and the balance of the parts are the preview's and only the volume is the file's own.
 * The synth's raw output is quiet (a piano piece peaks some 17 dB down), and the preview's level
 * is whatever the listener's volume makes it, so its master gain is not copied; nor is its soft
 * limiter (previewSynth.ts), which a piece scaled as one never needs. Returns the gain applied
 * (1 for silence).
 */
export function fileLevel(left: Float32Array, right: Float32Array): number {
    let peak = 0;
    for (let i = 0; i < left.length; i++) {
        peak = Math.max(peak, Math.abs(left[i]), Math.abs(right[i]));
    }
    if (peak === 0) { return 1; }
    const gain = WAV_PEAK / peak;
    for (let i = 0; i < left.length; i++) {
        left[i] *= gain;
        right[i] *= gain;
    }
    return gain;
}

/** One channel as 16-bit samples, what the MP3 encoder takes: −1..1 onto −32767..32767, past
 *  either end held there. */
export function toInt16(samples: Float32Array): Int16Array {
    const out = new Int16Array(samples.length);
    for (let i = 0; i < samples.length; i++) {
        const s = Math.max(-1, Math.min(1, samples[i]));
        out[i] = Math.round(s * 32767);
    }
    return out;
}

/** What the MP3 writing needs of an encoder — lamejs's Mp3Encoder has these. */
export interface Mp3Encoder {
    encodeBuffer(left: Int16Array, right?: Int16Array): Uint8Array;
    flush(): Uint8Array;
}

/**
 * The two channels encoded as one MP3 stream: fed to the encoder a stretch of whole MP3 frames at
 * a time, then flushed, the pieces joined. `pause`, when given, is awaited between stretches, as
 * the render does.
 */
export async function encodeMp3(encoder: Mp3Encoder, left: Float32Array, right: Float32Array,
    pause?: () => Promise<void>, framesPerStretch = MP3_FRAME * 64,
): Promise<Uint8Array> {
    const l = toInt16(left), r = toInt16(right);
    const pieces: Uint8Array[] = [];
    for (let at = 0; at < l.length; at += framesPerStretch) {
        const end = Math.min(l.length, at + framesPerStretch);
        const piece = encoder.encodeBuffer(l.subarray(at, end), r.subarray(at, end));
        if (piece.length > 0) { pieces.push(piece); }
        if (pause && end < l.length) { await pause(); }
    }
    const last = encoder.flush();
    if (last.length > 0) { pieces.push(last); }
    const out = new Uint8Array(pieces.reduce((n, p) => n + p.length, 0));
    let o = 0;
    for (const p of pieces) { out.set(p, o); o += p.length; }
    return out;
}
