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

// The recording beside a chart, played in the preview (docs/recording-design.md §4): from a
// second, in a loop, slower or faster with its pitch kept, its pitch moved by octaves, semitones
// and cents — or pitch and speed together, as a tape's speed is set right. The plan is
// recordingCore's (recordingPlan); this is the sound.
//
// Resampling is the plain buffer source (no stretcher: nothing of the sound is lost);
// stretching is Signalsmith Stretch's Web release (MIT, out/synth/SignalsmithStretch.mjs),
// imported by its webview URL. Both go through a gain and a limiter.
/// <reference lib="dom" />

import { RecordingSettings, recordingPlan } from '../recordingCore';

/** The node Signalsmith Stretch's Web release hands back (its README). */
interface StretchNode extends AudioNode {
    addBuffers(channels: Float32Array[]): Promise<number>;
    dropBuffers(): Promise<unknown>;
    schedule(change: { active?: boolean; input?: number; rate?: number; semitones?: number; output?: number;
        loopStart?: number; loopEnd?: number }): void;
}

type StretchFactory = (ctx: BaseAudioContext) => Promise<StretchNode>;

/** The module, from its URL; failing that (a host that will not import a module across origins),
 *  from a blob of the same text. */
async function importStretch(url: string): Promise<StretchFactory> {
    try {
        return (await import(/* @vite-ignore */ url)).default as StretchFactory;
    } catch (first) {
        const text = await (await fetch(url)).text();
        const blob = URL.createObjectURL(new Blob([text], { type: 'text/javascript' }));
        try {
            return (await import(/* @vite-ignore */ blob)).default as StretchFactory;
        } catch {
            throw first;
        }
    }
}

export class RecordingPlayer {
    private buffer: AudioBuffer | null = null;
    private stretchFactory: Promise<StretchFactory> | null = null;
    private stretch: StretchNode | null = null;
    private stretchHas: AudioBuffer | null = null;
    private source: AudioBufferSourceNode | null = null;
    private readonly gain: GainNode;
    /** Where the sound now playing began: the context time, the recording's second, its rate. */
    private anchor: { at: number; sec: number; rate: number } | null = null;
    private loop: { from: number; to: number } | null = null;
    private endTimer: ReturnType<typeof setTimeout> | null = null;
    private generation = 0;
    /** Called when the recording plays to its end (not on a stop). */
    onEnded: (() => void) | null = null;

    constructor(private readonly ctx: AudioContext, private readonly stretchUrl: string) {
        this.gain = ctx.createGain();
        const limiter = ctx.createDynamicsCompressor();
        limiter.threshold.value = -1;
        limiter.knee.value = 0;
        limiter.ratio.value = 20;
        limiter.attack.value = 0.002;
        limiter.release.value = 0.1;
        this.gain.connect(limiter);
        limiter.connect(ctx.destination);
    }

    /** The recording's length in seconds, once loaded. */
    get duration(): number {
        return this.buffer?.duration ?? 0;
    }

    get loaded(): boolean {
        return this.buffer !== null;
    }

    get playing(): boolean {
        return this.anchor !== null;
    }

    /** Decodes the recording's file (wav, mp3, flac, ogg — or the float WAV the language server
     *  wrote for an m4a). Resolves to its length; rejects with the browser's words. */
    async load(bytes: Uint8Array | ArrayBuffer): Promise<number> {
        this.stop();
        const copy = bytes instanceof ArrayBuffer ? bytes.slice(0)
            : (bytes.buffer as ArrayBuffer).slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength);
        this.buffer = await this.ctx.decodeAudioData(copy);
        this.stretchHas = null;
        return this.buffer.duration;
    }

    unload(): void {
        this.stop();
        this.buffer = null;
        this.stretchHas = null;
    }

    /** Plays in a loop between two seconds, or straight on (null). Takes effect at once. */
    setLoop(loop: { from: number; to: number } | null, settings: RecordingSettings): void {
        this.loop = loop && loop.to > loop.from + 0.05 ? loop : null;
        if (this.anchor) {
            const at = this.position() ?? 0;
            void this.play(this.loop && (at < this.loop.from || at >= this.loop.to) ? this.loop.from : at, settings);
        }
    }

    /** The recording's second now sounding, or null when nothing plays. */
    position(): number | null {
        if (!this.anchor) {
            return null;
        }
        const raw = this.anchor.sec + Math.max(0, this.ctx.currentTime - this.anchor.at) * this.anchor.rate;
        const loop = this.loop;
        if (loop && raw >= loop.to) {
            return loop.from + ((raw - loop.from) % (loop.to - loop.from));
        }
        return Math.min(raw, this.duration);
    }

    /** Starts the recording at its second `from` with the panel's settings. */
    async play(from: number, settings: RecordingSettings): Promise<void> {
        const buffer = this.buffer;
        if (!buffer) {
            return;
        }
        this.stopSound();
        const generation = ++this.generation;
        const plan = recordingPlan(settings);
        const sec = Math.min(Math.max(0, from), Math.max(0, buffer.duration - 0.01));
        this.gain.gain.value = plan.gain;
        const loop = this.loop;
        if (plan.engine === 'resample') {
            const src = this.ctx.createBufferSource();
            src.buffer = buffer;
            src.playbackRate.value = plan.rate;
            if (loop) {
                src.loop = true;
                src.loopStart = loop.from;
                src.loopEnd = loop.to;
            }
            src.connect(this.gain);
            const at = this.ctx.currentTime + 0.03;
            src.start(at, sec);
            this.source = src;
            this.anchor = { at, sec, rate: plan.rate };
        } else {
            const node = await this.stretchNode(buffer);
            if (generation !== this.generation) {
                return;
            }
            const at = this.ctx.currentTime + 0.05;
            node.schedule({
                active: true, input: sec, rate: plan.rate, semitones: plan.semitones, output: at,
                loopStart: loop ? loop.from : 0, loopEnd: loop ? loop.to : 0,
            });
            this.anchor = { at, sec, rate: plan.rate };
        }
        if (!loop) {
            const left = (buffer.duration - sec) / plan.rate;
            this.endTimer = setTimeout(() => {
                if (generation === this.generation) {
                    this.stop();
                    this.onEnded?.();
                }
            }, left * 1000 + 100);
        }
    }

    /** The panel changed while playing: go on from the second now sounding. */
    async update(settings: RecordingSettings): Promise<void> {
        const at = this.position();
        if (at !== null) {
            await this.play(at, settings);
        }
    }

    stop(): void {
        this.generation++;
        this.stopSound();
    }

    private stopSound(): void {
        if (this.endTimer) {
            clearTimeout(this.endTimer);
            this.endTimer = null;
        }
        if (this.source) {
            try { this.source.stop(); } catch { /* (not started) */ }
            this.source.disconnect();
            this.source = null;
        }
        this.stretch?.schedule({ active: false });
        this.anchor = null;
    }

    private async stretchNode(buffer: AudioBuffer): Promise<StretchNode> {
        this.stretchFactory ??= importStretch(this.stretchUrl);
        if (!this.stretch) {
            this.stretch = await (await this.stretchFactory)(this.ctx);
            this.stretch.connect(this.gain);
        }
        if (this.stretchHas !== buffer) {
            await this.stretch.dropBuffers();
            const channels: Float32Array[] = [];
            for (let c = 0; c < buffer.numberOfChannels; c++) {
                channels.push(buffer.getChannelData(c));
            }
            await this.stretch.addBuffers(channels);
            this.stretchHas = buffer;
        }
        return this.stretch;
    }
}
