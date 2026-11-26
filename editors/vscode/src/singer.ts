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

// The singing engine behind the preview's voice (docs/singing-design.md §5): the Cantoria
// extension's API — it carries the program and holds the voices and the engines' folders; a
// developer's build of the program is its setting cantoria.developer.program. It gives a WAV per voice,
// kept by the key of its score so a phrase is sung once. No vscode here: the extension lookup,
// the status bar and the previews are singing.ts's.

import * as fs from 'fs';
import { CantoriaApi, CantoriaScore, SingParameter, SingProgress, SungVoiceDto, cantoriaScore, languageRefusal, planAsked, sungKey, unsingableIn, voiceKey } from './singerCore';

/** A voice the engine rendered: the WAV's path and its length. */
/** `notes`: what the engine had to say of the score it sang (a setting the voice does not take). */
export interface RenderedVoice { wav: string; duration: number; notes?: string[] }

/** What sings: the extension's API, or the program here. */
export interface VoiceBackend {
    /** For the log: `extension 0.1.0`, or the program's path. */
    readonly name: string;
    /** The voice the engine will use for the file's `singer` (none written: its default), for the cache key.
     *  `language`: the words', for a `singer male`, whose voice must sing them. */
    voiceName(singer?: string | null, language?: string, gender?: string | null): Promise<string>;
    /** Sings `score` in `voice` (from voiceName); `key` names the file where the backend lets the caller name it.
     *  `signal` lets go of a render no longer wanted: the engine stops at its next phrase.
     *  `onProgress` hears how far it is, from an engine that says (SingProgress). */
    sing(key: string, score: CantoriaScore, voice: string, signal?: AbortSignal, onProgress?: (progress: SingProgress) => void): Promise<RenderedVoice>;
    dispose(): void;
}

/** The Cantoria extension as the backend: its `sing` keeps the WAVs under its own storage. */
export class CantoriaExtensionBackend implements VoiceBackend {
    readonly name: string;
    /** `note`: tells the writer a `singer` came to another voice than it asked for. */
    constructor(readonly api: CantoriaApi, private readonly note?: (text: string) => void) { this.name = `the Cantoria extension ${api.version}`; }
    async voiceName(singer?: string | null, language?: string, gender?: string | null): Promise<string> {
        if (this.api.resolveSinger) {
            const r = await this.api.resolveSinger(singer ?? '', { language, gender: gender ?? undefined });
            if (r.note) { this.note?.(r.note); }
            return r.voice;
        }
        return singer?.includes('/') ? singer : (await this.api.status()).voice;
    }
    async sing(_key: string, score: CantoriaScore, voice: string, signal?: AbortSignal, onProgress?: (progress: SingProgress) => void): Promise<RenderedVoice> {
        const v = await this.api.sing(score, { voice, signal, onProgress });
        return { wav: v.wav, duration: v.duration, notes: (v as { notes?: string[] }).notes };
    }
    /**
     * The voices of `voices` nobody installed can sing, by key, each with why (owner's decision
     * 2026-11-13). Cantoria's own answer where it has one (`plan`, asked once for the lot; nothing
     * is sung or loaded for it). An older Cantoria has none: then, as far as Lily# can tell by
     * itself — the voice each singer comes to says what it sings, and the words' language is not
     * among it. A question that fails says nothing: the voices go to the engine as before.
     */
    async unsingable(voices: readonly SungVoiceDto[]): Promise<Map<string, string>> {
        if (voices.length === 0) { return new Map(); }
        if (typeof this.api.plan === 'function') {
            return unsingableIn(await this.api.plan({ asked: planAsked(voices) }), voices);
        }
        const out = new Map<string, string>();
        if (!this.api.resolveSinger || !this.api.voices) { return out; }
        const listed = await this.api.voices();
        for (const v of voices) {
            const voice = (await this.api.resolveSinger(v.Singer ?? '', { language: v.Language, gender: v.Gender ?? undefined })).voice;
            const why = languageRefusal(voice, v.Language, listed);
            if (why) { out.set(sungKey(v), why); }
        }
        return out;
    }
    /** Whether this Cantoria says what a voice takes (after 0.4.0): an older one's silence is not "nothing". */
    get listsParameters(): boolean { return typeof this.api.parameters === 'function'; }
    /** The settings `voice` takes on a singer line's `with …`; none from an older Cantoria. */
    async parameters(voice: string): Promise<SingParameter[]> {
        // `transpose` is never a singer line's setting (Lily# refuses it: its own up / octave
        // move a voice's notes), so a Cantoria that still offers NEUTRINO's is not passed on.
        return this.api.parameters ? (await this.api.parameters(voice)).filter(p => p.name !== 'transpose') : [];
    }
    dispose(): void { /* the extension's engine is its own to keep */ }
}

/** How long an answer to "who would sing these voices" stands for the same voices: long enough
 *  for a run of edits, short enough that a voice installed meanwhile is found by the next Play. */
export const PLAN_KEPT_MS = 20000;

/**
 * The voices rendered so far, by key, and the backend that renders what is missing. One per
 * extension host, remade when the extensions installed change.
 */
export class VoiceRenderer {
    private readonly done = new Map<string, RenderedVoice>();
    private readonly inFlight = new Map<string, Promise<RenderedVoice>>();
    private readonly hearing = new Map<string, Set<(progress: SingProgress) => void>>();

    constructor(readonly backend: VoiceBackend) {}

    get name(): string { return this.backend.name; }

    /** True while the engine is singing a score's voice for this renderer. */
    get singing(): boolean { return this.inFlight.size > 0; }

    /** The key `render` would give `dto` at `speed`: the score's bytes and the voice's name — what
     *  tells one text's voice from another's without asking the engine (prepareVoices). */
    async key(dto: SungVoiceDto, speed: number): Promise<string> {
        return voiceKey(cantoriaScore(dto, speed), await this.backend.voiceName(dto.Singer, dto.Language, dto.Gender));
    }

    /** The voices nobody installed can sing, by key, each with why (CantoriaExtensionBackend.unsingable).
     *  None from a program named in the setting (a developer's build), nor when the question fails:
     *  what cannot be sung is then said by the engine as it refuses it. */
    async unsingable(voices: readonly SungVoiceDto[], now: number = Date.now()): Promise<Map<string, string>> {
        if (!(this.backend instanceof CantoriaExtensionBackend)) { return new Map(); }
        // The same question within PLAN_KEPT_MS is answered from the last answer: the preview asks
        // on every edit, and Cantoria may wait two seconds for a VOICEVOX that is not running.
        const question = JSON.stringify(planAsked(voices));
        if (this.planned && this.planned.question === question && now - this.planned.at < PLAN_KEPT_MS) { return this.planned.answer; }
        try {
            const answer = await this.backend.unsingable(voices);
            this.planned = { question, at: now, answer };
            return answer;
        } catch {
            return new Map();
        }
    }
    private planned: { question: string; at: number; answer: Map<string, string> } | undefined;

    /** Called when the engine begins on a voice, with its key (the host shows the bar by it). */
    onSinging: ((key: string) => void) | undefined;

    /** Called with what the engine says of the voice of `key` as it sings it (SingProgress). */
    onProgress: ((key: string, progress: SingProgress) => void) | undefined;

    /** True while the engine is singing the voice of `key`. */
    isSinging(key: string): boolean { return this.inFlight.has(key); }

    /** True when the voice of `key` was sung before and its WAV is still there. */
    has(key: string): boolean {
        const hit = this.done.get(key);
        return !!hit && fs.existsSync(hit.wav);
    }

    /**
     * The voice for `dto` at `speed`: its key, and the rendered WAV — at once when it was sung
     * before, else when the engine has sung it. A second ask for a voice in flight joins the first.
     * `signal` lets go of the render when a newer text has come.
     */
    async render(dto: SungVoiceDto, speed: number, signal?: AbortSignal, onProgress?: (progress: SingProgress) => void): Promise<{ key: string; rendered: Promise<RenderedVoice>; cached: boolean }> {
        const score = cantoriaScore(dto, speed);
        const voice = await this.backend.voiceName(dto.Singer, dto.Language, dto.Gender);
        const key = voiceKey(score, voice);
        const hit = this.done.get(key);
        if (hit && fs.existsSync(hit.wav)) { return { key, rendered: Promise.resolve(hit), cached: true }; }
        let rendered = this.inFlight.get(key);
        if (!rendered) {
            // Whoever asks a render in flight hears how far it is too (hearing), from where it is.
            const heard = new Set<(progress: SingProgress) => void>();
            this.hearing.set(key, heard);
            this.onSinging?.(key);
            const asked: Promise<RenderedVoice> = this.backend.sing(key, score, voice, signal, p => { this.onProgress?.(key, p); for (const hear of heard) { hear(p); } })
                .then(v => { this.done.set(key, v); return v; });
            rendered = asked;
            this.inFlight.set(key, asked);
            asked.finally(() => {
                if (this.inFlight.get(key) === asked) { this.inFlight.delete(key); }
                if (this.hearing.get(key) === heard) { this.hearing.delete(key); }
            }).catch(() => undefined);
        }
        if (onProgress) { this.hearing.get(key)?.add(onProgress); }
        return { key, rendered, cached: false };
    }

    dispose(): void {
        this.backend.dispose();
        this.inFlight.clear();
    }
}
