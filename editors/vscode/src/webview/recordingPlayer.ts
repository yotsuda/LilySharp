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

type StretchFactory = (ctx: BaseAudioContext, options?: AudioWorkletNodeOptions) => Promise<StretchNode>;

/** A stretcher for `channels` channels (Signalsmith's default is two). */
function stretchOptions(channels: number): AudioWorkletNodeOptions {
    return { numberOfInputs: 1, numberOfOutputs: 1, outputChannelCount: [channels] };
}

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
    /** The fader's level (the practice menu's Recording line, docs/recording-design.md §1 7). */
    private readonly level: GainNode;
    /** The practice menu's tempo the sound now playing was started with (update keeps it). */
    private tempo = 1;
    /** Where the sound now playing began: the context time, the recording's second, its rate. */
    private anchor: { at: number; sec: number; rate: number } | null = null;
    private loop: { from: number; to: number } | null = null;
    private endTimer: ReturnType<typeof setTimeout> | null = null;
    private generation = 0;
    /** Called when the recording plays to its end (not on a stop). */
    onEnded: (() => void) | null = null;

    // The recording's parts (Dettato's `parts`, docs/recording-design.md §8): ONE buffer, channel i
    // part i, played through one stretcher (or one resampling source) of that many channels, so
    // the parts keep together to the sample; each channel then has its own gain, and they add up
    // in the recording's gain. Played only when a part is off or not at 100% (partLevels): all of
    // them as recorded, the recording itself plays (the separation's error left out).
    private parts: AudioBuffer | null = null;
    private partNames: string[] = [];
    /** Each part's level, as partNames: 0 off, 1 as recorded, up to 2 (its fader). */
    private partLevels: number[] = [];
    private partsStretch: StretchNode | null = null;
    private partsStretchHas: AudioBuffer | null = null;
    private partsStretchChannels = 0;
    private partsSplit: ChannelSplitterNode | null = null;
    private partGains: GainNode[] = [];
    /** What the sound now playing plays: the parts (true) or the recording. */
    private playingParts = false;

    constructor(private readonly ctx: AudioContext, private readonly stretchUrl: string) {
        this.gain = ctx.createGain();
        this.level = ctx.createGain();
        const limiter = ctx.createDynamicsCompressor();
        limiter.threshold.value = -1;
        limiter.knee.value = 0;
        limiter.ratio.value = 20;
        limiter.attack.value = 0.002;
        limiter.release.value = 0.1;
        this.gain.connect(limiter);
        limiter.connect(this.level);
        this.level.connect(ctx.destination);
    }

    /** The fader: 0–2 (0 to 200 %), at once. */
    setLevel(level: number): void {
        this.level.gain.value = Number.isFinite(level) ? Math.min(2, Math.max(0, level)) : 1;
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
        this.dropParts();   // (another recording's parts come after it, recordingParts)
        return this.buffer.duration;
    }

    unload(): void {
        this.stop();
        this.buffer = null;
        this.stretchHas = null;
        this.dropParts();
    }

    /** The parts' names, as loaded (in the file's order). */
    get partsLoaded(): readonly string[] {
        return this.partNames;
    }

    /**
     * Decodes the recording's parts into one buffer of as many channels (a part in two channels is
     * made one). A part that cannot be decoded is left out; resolves to the names of the ones in,
     * with the reasons for the others. Their levels start as recorded.
     */
    async loadParts(list: readonly { name: string; bytes: Uint8Array | ArrayBuffer }[]): Promise<{ names: string[]; failed: string[] }> {
        const decoded: { name: string; data: Float32Array }[] = [];
        const failed: string[] = [];
        for (const p of list) {
            try {
                const copy = p.bytes instanceof ArrayBuffer ? p.bytes.slice(0)
                    : (p.bytes.buffer as ArrayBuffer).slice(p.bytes.byteOffset, p.bytes.byteOffset + p.bytes.byteLength);
                const b = await this.ctx.decodeAudioData(copy);
                let data = b.getChannelData(0);
                if (b.numberOfChannels > 1) {
                    data = data.slice();
                    for (let c = 1; c < b.numberOfChannels; c++) {
                        const other = b.getChannelData(c);
                        for (let i = 0; i < data.length; i++) { data[i] += other[i]; }
                    }
                    for (let i = 0; i < data.length; i++) { data[i] /= b.numberOfChannels; }
                }
                decoded.push({ name: p.name, data });
            } catch (e) {
                failed.push(`${p.name}: ${(e as Error)?.message ?? e}`);
            }
        }
        const wasPlaying = this.playingParts && this.anchor !== null;
        this.dropParts();
        if (decoded.length > 0) {
            const length = Math.max(...decoded.map(d => d.data.length));
            const buffer = this.ctx.createBuffer(decoded.length, length, this.ctx.sampleRate);
            decoded.forEach((d, c) => buffer.getChannelData(c).set(d.data));
            this.parts = buffer;
            this.partNames = decoded.map(d => d.name);
            this.partLevels = decoded.map(() => 1);
        }
        if (wasPlaying) { this.stop(); }
        return { names: this.partNames, failed };
    }

    /**
     * Each part's level by name (0 off, 1 as recorded, up to 2); a name not given is as recorded.
     * Takes effect at once while the parts play. True when what is to play has changed between
     * the parts and the recording itself while sounding — the caller plays it again from here.
     */
    setPartLevels(levels: Readonly<Record<string, number>>): boolean {
        this.partLevels = this.partNames.map(n => {
            const v = levels[n];
            return typeof v === 'number' && Number.isFinite(v) ? Math.min(2, Math.max(0, v)) : 1;
        });
        this.partGains.forEach((g, i) => { g.gain.value = this.partLevels[i] ?? 1; });
        return this.anchor !== null && this.usesParts() !== this.playingParts;
    }

    /** Whether the parts play (a part off or not at 100 %), not the recording itself. */
    usesParts(): boolean {
        return this.parts !== null && this.partLevels.some(l => l !== 1);
    }

    private dropParts(): void {
        if (this.playingParts) { this.stopSound(); }
        this.parts = null;
        this.partNames = [];
        this.partLevels = [];
        this.partsStretchHas = null;
        void this.partsStretch?.dropBuffers();
        this.partsSplit?.disconnect();
        for (const g of this.partGains) { g.disconnect(); }
        this.partsSplit = null;
        this.partGains = [];
    }

    /** The parts' way in on the page's context: a splitter, a gain a channel, into the recording's gain. */
    private partsInput(): ChannelSplitterNode {
        const n = this.parts!.numberOfChannels;
        if (!this.partsSplit || this.partGains.length !== n) {
            this.partsSplit?.disconnect();
            for (const g of this.partGains) { g.disconnect(); }
            this.partsSplit = this.ctx.createChannelSplitter(n);
            this.partGains = [];
            for (let c = 0; c < n; c++) {
                const g = this.ctx.createGain();
                g.gain.value = this.partLevels[c] ?? 1;
                this.partsSplit.connect(g, c);
                g.connect(this.gain);
                this.partGains.push(g);
            }
        }
        return this.partsSplit;
    }

    /** Plays in a loop between two seconds, or straight on (null). Takes effect at once. */
    setLoop(loop: { from: number; to: number } | null, settings: RecordingSettings): void {
        this.loop = loop && loop.to > loop.from + 0.05 ? loop : null;
        if (this.anchor) {
            const at = this.position() ?? 0;
            void this.play(this.loop && (at < this.loop.from || at >= this.loop.to) ? this.loop.from : at, settings, { tempo: this.tempo });
        }
    }

    /** Sets the loop for the next play without restarting what plays. */
    presetLoop(loop: { from: number; to: number } | null): void {
        this.loop = loop && loop.to > loop.from + 0.05 ? loop : null;
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

    /** Starts the recording at its second `from` with the panel's settings — at the practice
     *  menu's `tempo`, and at the context time `at` when given (with the score: the score's own
     *  clock, so the two stay together; a stretcher not ready by then starts late, and the
     *  second it starts from moves on by as much). */
    async play(from: number, settings: RecordingSettings, when: { tempo?: number; at?: number } = {}): Promise<void> {
        const buffer = this.buffer;
        if (!buffer) {
            return;
        }
        this.stopSound();
        const generation = ++this.generation;
        this.tempo = when.tempo ?? 1;
        const plan = recordingPlan(settings, this.tempo);
        let sec = Math.min(Math.max(0, from), Math.max(0, buffer.duration - 0.01));
        // The context time it starts at, and the second it then starts from.
        const startAt = (soonest: number): number => {
            if (when.at === undefined || when.at >= soonest) {
                return when.at ?? soonest;
            }
            sec = Math.min(sec + (soonest - when.at) * plan.rate, Math.max(0, buffer.duration - 0.01));
            return soonest;
        };
        this.gain.gain.value = plan.gain;
        const loop = this.loop;
        // The parts in the recording's place when one is off or not as recorded (§8).
        const parts = this.usesParts() ? this.parts! : null;
        this.playingParts = parts !== null;
        if (plan.engine === 'resample') {
            const src = this.ctx.createBufferSource();
            src.buffer = parts ?? buffer;
            src.playbackRate.value = plan.rate;
            if (loop) {
                src.loop = true;
                src.loopStart = loop.from;
                src.loopEnd = loop.to;
            }
            src.connect(parts ? this.partsInput() : this.gain);
            const at = startAt(this.ctx.currentTime + 0.03);
            src.start(at, sec);
            this.source = src;
            this.anchor = { at, sec, rate: plan.rate };
        } else {
            const node = parts ? await this.partsStretchNode(parts) : await this.stretchNode(buffer);
            if (generation !== this.generation) {
                return;
            }
            const at = startAt(this.ctx.currentTime + 0.05);
            node.schedule({
                active: true, input: sec, rate: plan.rate, semitones: plan.semitones, output: at,
                loopStart: loop ? loop.from : 0, loopEnd: loop ? loop.to : 0,
            });
            this.anchor = { at, sec, rate: plan.rate };
        }
        if (!loop) {
            const left = (buffer.duration - sec) / plan.rate + (this.anchor!.at - this.ctx.currentTime);
            this.endTimer = setTimeout(() => {
                if (generation === this.generation) {
                    this.stop();
                    this.onEnded?.();
                }
            }, left * 1000 + 100);
        }
    }

    /**
     * The whole recording as {@link play} sounds it from its second 0 at the practice menu's
     * `tempo` — the same engine, rate, shift, gain and limiter, the fader left out — rendered
     * offline, for an export to mix in (docs/recording-design.md §7). Null when nothing is
     * loaded. The stretcher is started a little ahead (`pad`) so its latency is spent before the
     * recording's 0, and that much is cut off the front: the result's frame 0 is the recording's
     * second 0.
     *
     * ⚠️ AT THE DECODED BUFFER'S OWN RATE (the page's context's), which the result says: the
     * stretcher reads its input as samples at its context's rate, so a 48 kHz buffer rendered at
     * 44.1 kHz played 48/44.1 slow — MEASURED 2026-11-16 (Lab sessions/p1286/e2e): the clicks of
     * a recording to be played at ×1.125 landed as at ×1.034. The export resamples (mixInStereo).
     */
    async render(settings: RecordingSettings, tempo: number): Promise<{ left: Float32Array; right: Float32Array; sampleRate: number } | null> {
        const buffer = this.buffer;
        if (!buffer) {
            return null;
        }
        const sampleRate = buffer.sampleRate;
        const plan = recordingPlan(settings, tempo);
        const stretched = plan.engine !== 'resample';
        const pad = stretched ? 0.5 : 0;
        const frames = Math.ceil((buffer.duration / plan.rate + pad + 0.25) * sampleRate);
        const ctx = new OfflineAudioContext(2, frames, sampleRate);
        const gain = ctx.createGain();
        gain.gain.value = plan.gain;
        const limiter = ctx.createDynamicsCompressor();
        limiter.threshold.value = -1;
        limiter.knee.value = 0;
        limiter.ratio.value = 20;
        limiter.attack.value = 0.002;
        limiter.release.value = 0.1;
        gain.connect(limiter);
        limiter.connect(ctx.destination);
        // The parts as they sound now (§8): through a splitter and a gain each, as play does.
        const parts = this.usesParts() ? this.parts! : null;
        let into: AudioNode = gain;
        if (parts) {
            const split = ctx.createChannelSplitter(parts.numberOfChannels);
            for (let c = 0; c < parts.numberOfChannels; c++) {
                const g = ctx.createGain();
                g.gain.value = this.partLevels[c] ?? 1;
                split.connect(g, c);
                g.connect(gain);
            }
            into = split;
        }
        const source = parts ?? buffer;
        if (!stretched) {
            const src = ctx.createBufferSource();
            src.buffer = source;
            src.playbackRate.value = plan.rate;
            src.connect(into);
            src.start(0, 0);
        } else {
            this.stretchFactory ??= importStretch(this.stretchUrl);
            const node = await (await this.stretchFactory)(ctx, parts ? stretchOptions(parts.numberOfChannels) : undefined);
            node.connect(into);
            const channels: Float32Array[] = [];
            for (let c = 0; c < source.numberOfChannels; c++) {
                channels.push(source.getChannelData(c).slice());
            }
            await node.addBuffers(channels);
            // Awaited (the worklet answers every call): the change is in place before the render starts.
            await Promise.resolve(node.schedule({ active: true, input: 0, rate: plan.rate, semitones: plan.semitones, output: pad }));
        }
        const out = await ctx.startRendering();
        const skip = Math.round(pad * sampleRate);
        const length = Math.max(0, Math.min(out.length - skip, Math.ceil(buffer.duration / plan.rate * sampleRate)));
        const left = out.getChannelData(0).slice(skip, skip + length);
        const right = (out.numberOfChannels > 1 ? out.getChannelData(1) : out.getChannelData(0)).slice(skip, skip + length);
        return { left, right, sampleRate };
    }

    /** The panel changed while playing: go on from the second now sounding. */
    async update(settings: RecordingSettings): Promise<void> {
        const at = this.position();
        if (at !== null) {
            await this.play(at, settings, { tempo: this.tempo });
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
        this.partsStretch?.schedule({ active: false });
        this.anchor = null;
    }

    /** The parts' stretcher: as many channels as parts, into the splitter (partsInput). */
    private async partsStretchNode(parts: AudioBuffer): Promise<StretchNode> {
        this.stretchFactory ??= importStretch(this.stretchUrl);
        const n = parts.numberOfChannels;
        if (!this.partsStretch || this.partsStretchChannels !== n) {
            this.partsStretch?.disconnect();
            this.partsStretch = await (await this.stretchFactory)(this.ctx, stretchOptions(n));
            this.partsStretchChannels = n;
            this.partsStretchHas = null;
        }
        this.partsStretch.disconnect();
        this.partsStretch.connect(this.partsInput());
        if (this.partsStretchHas !== parts) {
            await this.partsStretch.dropBuffers();
            await this.partsStretch.addBuffers(Array.from({ length: n }, (_, c) => parts.getChannelData(c)));
            this.partsStretchHas = parts;
        }
        return this.partsStretch;
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
