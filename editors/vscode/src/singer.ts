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

// The singing engine as a process (docs/singing-design.md §5): one resident `cantilena serve`
// per extension host, spoken to in JSON lines, and the WAVs it rendered, kept by their key so a
// phrase is sung once. The engine is the user's (lilysharp.playback.singer names it, as a sound
// font is named); nothing of it is linked or shipped. No vscode here: the settings, the status
// bar and the previews are extension.ts's.

import * as cp from 'child_process';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import * as readline from 'readline';
import { CantilenaScore, SungVoiceDto, cantilenaScore, parseServeLine, serveRequestLine, voiceKey } from './singerCore';

/** A voice the engine rendered: the WAV's path and its length. */
export interface RenderedVoice { wav: string; duration: number }

/**
 * The resident engine: started on the first render, spoken to on stdin, answered on stdout one
 * line a request, in order. A request is named by its key, and the WAV by it too, so a repeat of
 * a request writes the same file. An engine that dies fails what it owed and starts again on
 * the next render.
 */
export class SingerProcess {
    private child: cp.ChildProcess | undefined;
    private readonly pending = new Map<string, { resolve: (v: RenderedVoice) => void; reject: (e: Error) => void }>();
    readonly outDir: string;

    constructor(readonly exe: string, private readonly log: (line: string) => void) {
        this.outDir = path.join(os.tmpdir(), 'lilysharp-voice');
    }

    /** Whether the engine is running now. */
    get running(): boolean { return this.child !== undefined; }

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

    /** Sings `score` in `voice` into `<outDir>/<key>.wav`; the answer when the engine has. */
    render(key: string, score: CantilenaScore, voice: string): Promise<RenderedVoice> {
        const child = this.child ?? this.start();
        const output = path.join(this.outDir, `${key}.wav`);
        return new Promise<RenderedVoice>((resolve, reject) => {
            this.pending.set(key, { resolve, reject });
            child.stdin!.write(serveRequestLine(key, score, voice, output), e => {
                if (e) { this.pending.delete(key); reject(e); }
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
 * The voices rendered so far, by key, and the engine that renders what is missing. One per
 * extension host, remade when the setting that names the engine changes.
 */
export class VoiceRenderer {
    private readonly process: SingerProcess;
    private readonly done = new Map<string, RenderedVoice>();
    private readonly inFlight = new Map<string, Promise<RenderedVoice>>();

    constructor(exe: string, log: (line: string) => void) {
        this.process = new SingerProcess(exe, log);
    }

    get exe(): string { return this.process.exe; }

    /**
     * The voice for `dto` at `speed` in `voiceName`: its key, and the rendered WAV — at once when
     * it was sung before, else when the engine has sung it. A second ask for a voice in flight
     * joins the first.
     */
    render(dto: SungVoiceDto, speed: number, voiceName: string): { key: string; rendered: Promise<RenderedVoice>; cached: boolean } {
        const score = cantilenaScore(dto, speed);
        const key = voiceKey(score, voiceName);
        const hit = this.done.get(key);
        if (hit && fs.existsSync(hit.wav)) { return { key, rendered: Promise.resolve(hit), cached: true }; }
        let rendered = this.inFlight.get(key);
        if (!rendered) {
            rendered = this.process.render(key, score, voiceName).then(v => { this.done.set(key, v); return v; });
            this.inFlight.set(key, rendered);
            rendered.finally(() => this.inFlight.delete(key)).catch(() => undefined);
        }
        return { key, rendered, cached: false };
    }

    dispose(): void {
        this.process.dispose();
        this.inFlight.clear();
    }
}
