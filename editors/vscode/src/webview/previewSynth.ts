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

// The preview's SoundFont synth — the browser half, bundled by esbuild.js into
// out/synth/preview-synth.js and loaded by the preview page as window.LysSynth beside
// spessasynth_lib's worklet processor (out/synth/spessasynth_processor.min.js). The page
// script in extension.ts (getPreviewHtml) owns the AudioContext, the Play button and the
// auditions; this file owns the synth, its sound font and the scheduling. The pure parts
// are playbackCore.ts. Design: docs/playback-design.md.
//
// The extension's tsconfig has no DOM lib (the host side runs in Node); this file alone
// runs in the page, so it pulls the DOM types in itself.
/// <reference lib="dom" />

import { WorkletSynthesizer } from 'spessasynth_lib';
import {
    BEND_CENTRE, DRUM_CHANNEL, Lookahead, MidiEvent, PlaybackNote, SoundingCursor,
    channelsOf, endOf, programOf, toEvents,
} from './playbackCore';

export type SynthState = 'loading' | 'ready' | 'failed' | 'none';
export interface SynthStatus { state: SynthState; detail?: string }

/** How far ahead of the clock events are handed to the synth. Short while the page is
 *  visible, so a Stop leaves little queued; longer while it is hidden, where the browser
 *  fires a timer about once a second. */
const HORIZON_VISIBLE_S = 0.25;
const HORIZON_HIDDEN_S = 1.5;
const PUMP_MS = 50;
/** The channel the auditions strike on (the step keys, Alt+P, the caret): the last one,
 *  which a score's parts take only past fourteen pitched sounds. */
const AUDITION_CHANNEL = 15;
const ALL_SOUND_OFF = 120;

/** The worklet module, from its URL; failing that (a host that will not hand a worklet a
 *  cross-origin module), from a blob of the same text. */
async function addProcessor(ctx: AudioContext, url: string): Promise<void> {
    try {
        await ctx.audioWorklet.addModule(url);
    } catch (first) {
        const text = await (await fetch(url)).text();
        const blob = URL.createObjectURL(new Blob([text], { type: 'text/javascript' }));
        try {
            await ctx.audioWorklet.addModule(blob);
        } catch {
            throw first;
        } finally {
            URL.revokeObjectURL(blob);
        }
    }
}

/** One playback: the events of a note list from an offset, pumped to the synth ahead
 *  of the clock, from the audio time t0. */
export class Player {
    /** Seconds into the piece at which the last note ends. */
    readonly end: number;
    private readonly look: Lookahead;
    private readonly channels: number[];
    private timer: ReturnType<typeof setInterval> | null;
    private scheduledUntil = 0;   // audio time of the last event handed out

    /** The audio time the piece's `offset` sounds at — the page's highlight clock. */
    constructor(private readonly synth: WorkletSynthesizer, private readonly ctx: AudioContext,
                notes: readonly PlaybackNote[], offset: number, readonly t0: number) {
        const events = toEvents(notes, offset);
        this.look = new Lookahead(events);
        this.channels = channelsOf(events);
        this.end = endOf(notes, offset);
        this.timer = setInterval(() => this.pump(), PUMP_MS);
        this.pump();
    }

    private pump(): void {
        if (!this.timer) { return; }
        const hidden = typeof document !== 'undefined' && document.hidden;
        const upTo = this.ctx.currentTime - this.t0 + (hidden ? HORIZON_HIDDEN_S : HORIZON_VISIBLE_S);
        for (const e of this.look.take(upTo)) {
            const time = this.t0 + e.t;
            this.scheduledUntil = Math.max(this.scheduledUntil, time);
            this.dispatch(e, time);
        }
        if (this.look.done) { clearInterval(this.timer); this.timer = null; }
    }

    private dispatch(e: MidiEvent, time: number): void {
        const at = { time };
        switch (e.kind) {
            case 'program': this.synth.programChange(e.c, e.g, at); break;
            case 'bend': this.synth.pitchWheel(e.c, e.value, at); break;
            case 'on': this.synth.noteOn(e.c, e.p, e.v, at); break;
            case 'off': this.synth.noteOff(e.c, e.p, at); break;
        }
    }

    /** Stops now, and silences what was already queued ahead of the clock: the synth
     *  keeps a time-ordered queue that stopAll does not empty, so an All Sound Off goes
     *  in behind the last queued event. Returns the audio time by which all is quiet. */
    stop(): number {
        if (this.timer) { clearInterval(this.timer); this.timer = null; }
        this.synth.stopAll(true);
        const quietAt = Math.max(this.ctx.currentTime, this.scheduledUntil) + 0.02;
        for (const c of this.channels) {
            this.synth.controllerChange(c, ALL_SOUND_OFF, 0, { time: quietAt });
            this.synth.pitchWheel(c, BEND_CENTRE, { time: quietAt });
        }
        return quietAt;
    }
}

/**
 * The synth of one preview page: a WorkletSynthesizer on the page's AudioContext, its
 * sound font fetched from the URL the extension gave, its output through a gain and a
 * soft limiter (many voices at once used to clip the oscillator player's sum). State
 * is reported through `onStatus`; `ready` says whether it can play.
 */
export class PreviewSynth {
    state: SynthState = 'loading';
    private synth: WorkletSynthesizer | null = null;
    private readonly master: GainNode;
    private bankSerial = 0;
    private bankId: string | null = null;
    private quietAt = 0;
    private player: Player | null = null;
    private readonly init: Promise<void>;

    constructor(readonly ctx: AudioContext, processorUrl: string, soundFontUrl: string,
                private readonly onStatus: (s: SynthStatus) => void) {
        this.master = ctx.createGain();
        this.master.gain.value = 0.9;
        const limiter = ctx.createDynamicsCompressor();
        limiter.threshold.value = -10;
        limiter.knee.value = 12;
        limiter.ratio.value = 12;
        limiter.attack.value = 0.002;
        limiter.release.value = 0.12;
        this.master.connect(limiter);
        limiter.connect(ctx.destination);
        this.init = this.start(processorUrl, soundFontUrl);
    }

    get ready(): boolean { return this.state === 'ready' && this.synth !== null; }

    private async start(processorUrl: string, soundFontUrl: string): Promise<void> {
        try {
            await addProcessor(this.ctx, processorUrl);
            const synth = new WorkletSynthesizer(this.ctx);
            synth.connect(this.master);
            await synth.isReady;
            this.synth = synth;
        } catch (e) {
            this.fail('the synth did not start: ' + String(e));
            return;
        }
        await this.setSoundFont(soundFontUrl);
    }

    private fail(detail: string): void {
        this.state = 'failed';
        this.onStatus({ state: 'failed', detail });
    }

    /** Loads (or replaces) the sound font; an empty URL unloads it, and the page falls
     *  back to its oscillator patches. */
    async setSoundFont(url: string): Promise<void> {
        await this.init.catch(() => { /* reported by start */ });
        if (!this.synth) { return; }
        if (!url) {
            this.state = 'none';
            this.onStatus({ state: 'none', detail: 'no sound font' });
            return;
        }
        this.state = 'loading';
        this.onStatus({ state: 'loading', detail: url });
        const serial = ++this.bankSerial;
        try {
            const started = Date.now();
            const response = await fetch(url);
            if (!response.ok) { throw new Error('HTTP ' + response.status); }
            const buffer = await response.arrayBuffer();
            if (serial !== this.bankSerial) { return; }   // another font was asked for meanwhile
            const id = 'lys-' + serial;
            await this.synth.soundBankManager.addSoundBank(buffer, id);
            if (this.bankId) { await this.synth.soundBankManager.deleteSoundBank(this.bankId); }
            this.bankId = id;
            if (serial !== this.bankSerial) { return; }
            this.state = 'ready';
            this.onStatus({
                state: 'ready',
                detail: (buffer.byteLength / 1048576).toFixed(1) + ' MB in ' + (Date.now() - started) + ' ms: ' + url,
            });
        } catch (e) {
            if (serial !== this.bankSerial) { return; }
            this.fail('the sound font did not load (' + String(e) + '): ' + url);
        }
    }

    /** Plays `notes` from `offset` seconds into the piece, starting at audio time t0 at
     *  the earliest (later if the previous playback's queue is still draining). */
    play(notes: readonly PlaybackNote[], offset: number, t0: number): Player {
        if (!this.synth) { throw new Error('the synth is not ready'); }
        this.stop();
        const start = Math.max(t0, this.quietAt + 0.05);
        this.player = new Player(this.synth, this.ctx, notes, offset, start);
        return this.player;
    }

    /** Stops the playback, if one is on. */
    stop(): void {
        if (this.player) {
            this.quietAt = this.player.stop();
            this.player = null;
        }
    }

    /** Strikes an audition — pitches held until `release` — with a program (the part's,
     *  or -1 for the timbre family's); the drum family (timbre 9, which the server gives
     *  drum notes alone) strikes the drum channel. Returns the channel struck. */
    strike(pitches: readonly number[], timbre: number, program: number, velocity = 100): number {
        if (!this.synth) { return -1; }
        const drums = timbre === 9;
        const c = drums ? DRUM_CHANNEL : AUDITION_CHANNEL;
        if (!drums) { this.synth.programChange(c, programOf(program, timbre)); }
        for (const p of pitches) { this.synth.noteOn(c, Math.max(0, Math.min(127, Math.round(p))), velocity); }
        return c;
    }

    release(channel: number, pitches: readonly number[]): void {
        if (!this.synth || channel < 0) { return; }
        for (const p of pitches) { this.synth.noteOff(channel, Math.max(0, Math.min(127, Math.round(p)))); }
    }
}

/** What the page script reaches: window.LysSynth. */
const api = {
    create(ctx: AudioContext, processorUrl: string, soundFontUrl: string,
           onStatus: (s: SynthStatus) => void): PreviewSynth {
        return new PreviewSynth(ctx, processorUrl, soundFontUrl, onStatus);
    },
    SoundingCursor,
    programOf,
};

declare global {
    interface Window { LysSynth: typeof api }
}
(window as Window).LysSynth = api;
