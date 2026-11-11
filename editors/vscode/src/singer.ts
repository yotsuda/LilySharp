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

// The singing engine behind the preview's voice (docs/singing-design.md §5), two ways: the
// Cantoria extension's API (the usual way — it carries the program and holds the voice and
// NEUTRINO's folder), or a `cantoria` program named in lilysharp.playback.singer, run here as
// one resident `cantoria serve` spoken to in JSON lines (a developer's build). Both give a WAV
// per voice, kept by the key of its score so a phrase is sung once. No vscode here: the
// settings, the extension lookup, the status bar and the previews are extension.ts's.

import * as cp from 'child_process';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import * as readline from 'readline';
import { CantoriaApi, CantoriaScore, SingProgress, SungVoiceDto, cantoriaScore, parseServeLine, serveCancelLine, serveRequestLine, voiceKey } from './singerCore';

/** A voice the engine rendered: the WAV's path and its length. */
export interface RenderedVoice { wav: string; duration: number }

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
        return { wav: v.wav, duration: v.duration };
    }
    dispose(): void { /* the extension's engine is its own to keep */ }
}

/**
 * A program named in the setting, as the backend: started on the first render, spoken to on
 * stdin, answered on stdout one line a request, in order. A request is named by its key, and
 * the WAV by it too, so a repeat of a request writes the same file. An engine that dies fails
 * what it owed and starts again on the next render.
 */
export class SingerProcess implements VoiceBackend {
    private child: cp.ChildProcess | undefined;
    private readonly pending = new Map<string, { resolve: (v: RenderedVoice) => void; reject: (e: Error) => void; onProgress?: (progress: SingProgress) => void }>();
    private requests = 0;
    readonly outDir: string;
    readonly name: string;

    constructor(readonly exe: string, private readonly log: (line: string) => void) {
        this.outDir = path.join(os.tmpdir(), 'lilysharp-voice');
        this.name = exe;
    }

    /** Whether the engine is running now. */
    get running(): boolean { return this.child !== undefined; }

    voiceName(singer?: string | null): Promise<string> { return Promise.resolve(singer?.includes('/') ? singer : 'default'); }

    private start(): cp.ChildProcess {
        fs.mkdirSync(this.outDir, { recursive: true });
        const child = cp.spawn(this.exe, ['serve', '--out-dir', this.outDir], { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
        this.child = child;
        readline.createInterface({ input: child.stdout! }).on('line', line => this.answer(line));
        readline.createInterface({ input: child.stderr! }).on('line', line => this.log(`singer: ${line}`));
        child.on('error', e => this.gone(child, `the singer could not start (${this.exe}): ${e.message}`));
        child.on('exit', (code, signal) => this.gone(child, `the singer exited (${signal ?? code})`));
        return child;
    }

    private answer(line: string): void {
        const answer = parseServeLine(line);
        if (!answer) { this.log(`singer: ${line}`); return; }
        if (answer.ready) { this.log(`singer ready: ${(answer.voices ?? []).join(', ') || 'no voice installed'}`); return; }
        if (answer.progress) {
            this.pending.get(answer.progress.id)?.onProgress?.({ toSing: answer.progress.toSing, sung: answer.progress.sung });
            return;
        }
        const waiting = answer.id ? this.pending.get(answer.id) : undefined;
        if (!waiting || !answer.id) { return; }
        this.pending.delete(answer.id);
        if (answer.error || !answer.wav) { waiting.reject(new Error(answer.error ?? 'the singer answered no WAV')); }
        else { waiting.resolve({ wav: answer.wav, duration: answer.duration ?? 0 }); }
    }

    private gone(child: cp.ChildProcess, why: string): void {
        if (this.child !== child) { return; }   // an earlier engine's exit, after a restart or dispose
        this.child = undefined;
        this.log(why);
        for (const waiting of this.pending.values()) { waiting.reject(new Error(why)); }
        this.pending.clear();
    }

    /** Sings `score` into `<outDir>/<key>.wav`; the answer when the engine has. Each request has
     *  its own id: a render let go of and asked again is another request. */
    sing(key: string, score: CantoriaScore, voice: string, signal?: AbortSignal, onProgress?: (progress: SingProgress) => void): Promise<RenderedVoice> {
        const child = this.child ?? this.start();
        const output = path.join(this.outDir, `${key}.wav`);
        const id = `${key}.${++this.requests}`;
        return new Promise<RenderedVoice>((resolve, reject) => {
            this.pending.set(id, { resolve, reject, onProgress });
            signal?.addEventListener('abort', () => {
                if (!this.pending.delete(id)) { return; }
                reject(new Error('cancelled'));
                try { child.stdin!.write(serveCancelLine(id)); } catch { /* gone */ }
            }, { once: true });
            child.stdin!.write(serveRequestLine(id, score, output, voice === 'default' ? undefined : voice), e => {
                if (e) { this.pending.delete(id); reject(e); }
            });
        });
    }

    /** Asks the engine to shut down (its own server with it) and lets go of it. */
    dispose(): void {
        const child = this.child;
        if (!child) { return; }
        this.gone(child, 'the singer was let go');
        try {
            child.stdin!.write('{"command":"shutdown"}\n');
            child.stdin!.end();
        } catch { /* already gone */ }
        const timer = setTimeout(() => { try { child.kill(); } catch { /* gone */ } }, 3000);
        child.on('exit', () => clearTimeout(timer));
    }
}

/**
 * The voices rendered so far, by key, and the backend that renders what is missing. One per
 * extension host, remade when the setting that names the engine, or the extensions installed,
 * change.
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
            const asked: Promise<RenderedVoice> = this.backend.sing(key, score, voice, signal, p => { for (const hear of heard) { hear(p); } })
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
