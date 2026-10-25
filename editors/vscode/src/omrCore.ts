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

// The editor-free half of "Lily#: Convert Image/PDF to Lily# (.lys)…" (omrImport.ts holds the half that
// talks to VS Code). The OMR reader (LilySharp-Omr) is a SEPARATE PROGRAM: this runs it as a
// child process and reads the files it writes, nothing more — no OMR code comes in here
// (LilySharp-Omr docs/repro/lilysharp-omr-distribution-2026-10-05.md, the agreement this
// follows: §3.2 the CLI, §4 L1 where the reader is, L7 the run). Nothing here imports vscode,
// so `npm test` runs it.

import { spawn } from 'child_process';
import * as fs from 'fs';

/** The CLI-and-output agreement versions this extension speaks (the reader's `protocol`). */
export const OMR_PROTOCOLS = { min: 1, max: 1 } as const;

/** The oldest Scan Import this Lily# is written for. A reader answering a lower `--version` is
 *  offered its update when a scan is read (the Marketplace keeps the extension current on its
 *  own — VS Code's auto-update, not duplicated here; this is only Lily#'s own floor). Raised with
 *  OMR_PROTOCOLS when a release leans on a newer reader (RELEASING.md §2). 0.4.0 since Lily#
 *  0.14.0: Fix OMR Marks with AI checks a proposal with `omr verify --candidate --bars` and reads
 *  the reader's `readings`, both new in Scan Import 0.4.0 (the protocol is unchanged). */
export const OMR_MIN_VERSION = '0.4.0';

/** The environment variable that points at a reader by hand (development; L1 ①). */
export const OMR_PATH_VARIABLE = 'LILYSHARP_OMR_PATH';

/** The VS Code extension that carries the reader (LilySharp-Omr's reply of 2026-10-12 §3: a
 *  separate, Windows-only, Beta extension; its `activate` returns {@link OmrExtensionExports}). */
export const OMR_EXTENSION_ID = 'yotsuda.lilysharp-omr';

/** What the reader's extension exports: where its `omr.exe` is, and what `omr --version --json`
 *  answered when it was packed. `undefined` off Windows x64 or when its `bin` is broken. */
export interface OmrExtensionExports {
    readonly path: string;
    readonly version: string;
    readonly protocol: number;
}

/** The reader's path from the extension's exports, or undefined when they are not that shape. */
export function readerOfExports(exports: unknown): string | undefined {
    const e = exports as Partial<OmrExtensionExports> | undefined;
    return typeof e?.path === 'string' && e.path.trim() !== '' ? e.path.trim() : undefined;
}

/** The input files the reader takes, lower case, without the dot. */
export const OMR_INPUT_EXTENSIONS = ['png', 'jpg', 'jpeg', 'bmp', 'tif', 'tiff', 'pdf'] as const;

/** The reader's build for this machine: `win-x64`, `osx-arm64`, `linux-x64`, or undefined. */
export function omrRid(platform: string, arch: string): string | undefined {
    const os = platform === 'win32' ? 'win' : platform === 'darwin' ? 'osx' : platform === 'linux' ? 'linux' : undefined;
    return os && (arch === 'x64' || arch === 'arm64') ? `${os}-${arch}` : undefined;
}

/** Where the reader is, and how it was found. */
export interface OmrLocation {
    readonly path: string;
    readonly source: 'environment' | 'extension';
}

/**
 * Finds the reader: ① the environment variable (a reader under development), ② the reader's
 * extension ({@link OMR_EXTENSION_ID}; `fromExtension` is its exported path, when it exists on
 * disk), else undefined (the caller offers the extension). Not a setting on purpose (the
 * agreement §1: the reader's place is not shown in Settings). A third place — a copy under
 * Lily#'s own storage, from the download plan of 2026-10-05 — was dropped on 2026-10-17: nothing
 * wrote there once the reader became an extension, and a copy left there would have stayed old
 * outside the Marketplace's updates.
 */
export function locateOmr(
    env: Record<string, string | undefined>, fromExtension?: string,
    exists: (p: string) => boolean = fs.existsSync): OmrLocation | undefined {
    const fromEnv = env[OMR_PATH_VARIABLE]?.trim();
    if (fromEnv) {
        return { path: fromEnv, source: 'environment' };
    }
    if (fromExtension && exists(fromExtension)) {
        return { path: fromExtension, source: 'extension' };
    }
    return undefined;
}

/** Orders "a.b.c" versions numerically. */
export function compareVersions(a: string, b: string): number {
    const pa = a.split('.').map(Number), pb = b.split('.').map(Number);
    for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
        const d = (pa[i] ?? 0) - (pb[i] ?? 0);
        if (d !== 0) {
            return d;
        }
    }
    return 0;
}

/** How to start a reader at a path: a framework-dependent `.dll` runs under `dotnet`. */
export function launchOf(readerPath: string): { command: string; prefix: string[] } {
    return readerPath.toLowerCase().endsWith('.dll')
        ? { command: 'dotnet', prefix: [readerPath] }
        : { command: readerPath, prefix: [] };
}

/** The arguments of `omr read` (§3.2), as an ARRAY — never a shell string: paths carry spaces
 *  and Japanese. `verify` asks the reader to engrave what it read and compare it with the scan
 *  right away (`--verify`), so the first look at the score already has the comparison's marks;
 *  a reader with no lysc to engrave with says so as a warning and skips it. */
export function readArguments(inputs: readonly string[], outDir: string, lysc?: string, verify = false): string[] {
    const args = ['read', ...inputs, '--out', outDir, '--progress', 'json'];
    if (verify) {
        args.push('--verify');
    }
    if (lysc) {
        args.push('--lysc', lysc);
    }
    return args;
}

/** What `omr verify` is asked beyond the .lys (LilySharp-Omr's reply of 2026-10-23 to the AI
 *  fix's proposal): `candidate`, the text to compare in place of the .lys's own (an unsaved
 *  proposal, written to a temp file; the side file and the pages stay the .lys's — and the
 *  reader then writes nothing); `bars`, the reader's bar numbers to report a `bar` event for;
 *  `noWrite`, leave the side file and the marked pages as they are. */
export interface VerifyOptions {
    readonly candidate?: string;
    readonly bars?: readonly number[];
    readonly noWrite?: boolean;
}

/** The arguments of `omr verify` (B5, proposed to LilySharp-Omr 2026-10-05): engrave the
 *  .lys again, compare it bar by bar with the pages its side file lists, and write the side
 *  file's todos anew — the .lys itself is not touched. An ARRAY, as readArguments'. */
export function verifyArguments(lys: string, lysc?: string, options: VerifyOptions = {}): string[] {
    const args = ['verify', lys];
    if (options.candidate) {
        args.push('--candidate', options.candidate);
    }
    if (options.bars && options.bars.length > 0) {
        args.push('--bars', options.bars.join(','));
    }
    if (options.noWrite) {
        args.push('--no-write');
    }
    args.push('--progress', 'json');
    if (lysc) {
        args.push('--lysc', lysc);
    }
    return args;
}

/** What `omr --version --json` answers. */
export interface OmrVersion {
    readonly version: string;
    readonly protocol: number;
    readonly inputs?: readonly string[];
    /** The commands it has beyond `read` (`verify`); absent in a reader that has only `read`. */
    readonly commands?: readonly string[];
    /** What its commands can do beyond the agreement's floor (`verify-bars`: `omr verify
     *  --candidate --bars --no-write` with a `bar` event); absent before 2026-10-23. */
    readonly features?: readonly string[];
}

/** Whether the reader says it has `omr verify` (B5 asks it only then). */
export function canVerify(v: OmrVersion): boolean {
    return Array.isArray(v.commands) && v.commands.includes('verify');
}

/** Whether the reader's `verify` takes a candidate text and reports bars (the AI fix's third
 *  gate asks it only then; docs/omr-ai-fix-design.md §3.2 ③). */
export function canVerifyBars(v: OmrVersion): boolean {
    return canVerify(v) && Array.isArray(v.features) && v.features.includes('verify-bars');
}

/** Reads `omr --version --json`'s output, or undefined when it is not that. */
export function parseVersion(stdout: string): OmrVersion | undefined {
    try {
        const v = JSON.parse(stdout.trim());
        return typeof v?.version === 'string' && Number.isInteger(v?.protocol) ? v as OmrVersion : undefined;
    } catch {
        return undefined;
    }
}

/** Whether this extension speaks the reader's protocol. */
export function speaksProtocol(protocol: number): boolean {
    return protocol >= OMR_PROTOCOLS.min && protocol <= OMR_PROTOCOLS.max;
}

/** Whether the reader's version is below {@link OMR_MIN_VERSION}. A version that does not start
 *  with numbers (a development build's) is not called old. */
export function readerIsOld(v: OmrVersion): boolean {
    const numbers = /^\d+(\.\d+)*/.exec(v.version.trim());
    return numbers !== null && compareVersions(numbers[0], OMR_MIN_VERSION) < 0;
}

/** One line of `--progress json` (§3.2). */
export type OmrEvent =
    | { event: 'start'; pages: number }
    | { event: 'page'; page: number; of: number }
    | { event: 'warning'; message: string }
    /** A step of the reading begun (asked of LilySharp-Omr 2026-10-26): `layout` a page's staves
     *  looked for before the reading proper, `read` the reading proper, `verify` the reading drawn
     *  again and held against the pages, `write` the files written. A reader without them still
     *  reads: the steps are then told from `start`, `page` and the end (ReadProgress). */
    | { event: 'stage'; stage: string; page?: number; of?: number }
    /** One part-bar of `verify --bars` (the reply of 2026-10-23): `differs` when the comparison
     *  marks anything there — a tie or a slur too, so `confidence` (the todo's, 1 for no mark)
     *  says how much; `part` null for the bar's stems and tuplets as a whole. */
    | { event: 'bar'; measure: number; part: string | null; differs: boolean; confidence: number; memo?: string }
    | { event: 'done'; lys: string; json?: string; annotated?: string[]; todos?: number; quiet?: number;
        candidate?: string; bars?: number; differing?: number }
    | { event: 'error'; code?: string; message: string };

/** Reads one stdout line, or undefined for a line that is not an event (ignored, not fatal). */
export function parseEvent(line: string): OmrEvent | undefined {
    const text = line.trim();
    if (!text.startsWith('{')) {
        return undefined;
    }
    try {
        const e = JSON.parse(text);
        switch (e?.event) {
            case 'start': return Number.isInteger(e.pages) ? e : undefined;
            case 'page': return Number.isInteger(e.page) && Number.isInteger(e.of) ? e : undefined;
            case 'warning': return typeof e.message === 'string' ? e : undefined;
            case 'stage': return typeof e.stage === 'string'
                && (e.page === undefined || Number.isInteger(e.page)) && (e.of === undefined || Number.isInteger(e.of))
                ? { event: 'stage', stage: e.stage, ...(e.page !== undefined ? { page: e.page } : {}), ...(e.of !== undefined ? { of: e.of } : {}) }
                : undefined;
            case 'bar': return Number.isInteger(e.measure) && typeof e.differs === 'boolean'
                ? {
                    event: 'bar', measure: e.measure, part: typeof e.part === 'string' ? e.part : null, differs: e.differs,
                    confidence: typeof e.confidence === 'number' && Number.isFinite(e.confidence) ? e.confidence : (e.differs ? 0 : 1),
                    ...(typeof e.memo === 'string' ? { memo: e.memo } : {}),
                }
                : undefined;
            case 'done': return typeof e.lys === 'string' ? e : undefined;
            case 'error': return typeof e.message === 'string' ? e : undefined;
            default: return undefined;
        }
    } catch {
        return undefined;
    }
}

/** What an exit code means (§3.2): 0 read, 1 some pages unread (a .lys is written), 2 the
 *  input, 3 the reader itself. */
export function exitMeaning(code: number | null): 'ok' | 'partial' | 'input' | 'internal' | 'stopped' {
    switch (code) {
        case 0: return 'ok';
        case 1: return 'partial';
        case 2: return 'input';
        case null: return 'stopped';
        default: return 'internal';
    }
}

/** Elapsed time as m:ss (h:mm:ss from an hour). */
export function clockOf(ms: number): string {
    const s = Math.max(0, Math.floor(ms / 1000));
    const two = (n: number) => String(n).padStart(2, '0');
    return s >= 3600 ? `${Math.floor(s / 3600)}:${two(Math.floor(s / 60) % 60)}:${two(s % 60)}` : `${Math.floor(s / 60)}:${two(s % 60)}`;
}

type ReadStep = 'open' | 'layout' | 'read' | 'verify' | 'write';

/**
 * Where a reading is, from its events, for the progress notification: the step, the page, the
 * time so far, and — from the pace of the pages done — about how long the step has left.
 * A `page` event comes when that page is read, so the page being read is the next one; with no
 * `stage` events (a reader before 2026-10-26) the time from `start` to the first page is the
 * staves looked for on every page, and after the last page the check and the files.
 */
export class ReadProgress {
    private step: ReadStep = 'open';
    private pages = 0;
    private done = 0;                       // pages through the current step (layout or read)
    private stepFrom?: number;              // when the current step began, when known
    private firstAt?: number;               // when its first page was done
    private lastAt?: number;                // when its last page was done

    constructor(private readonly started: number, private readonly verifies: boolean) { }

    on(e: OmrEvent, now: number): void {
        switch (e.event) {
            case 'start':
                this.pages = e.pages;
                this.enter('layout', now);
                break;
            case 'stage':
                if (e.of !== undefined) { this.pages = e.of; }
                if (e.stage === 'layout' || e.stage === 'read' || e.stage === 'verify' || e.stage === 'write') {
                    if (e.stage !== this.step) { this.enter(e.stage, now); }
                    // (a layout page begun: the ones before it are done)
                    if (e.stage === 'layout' && e.page !== undefined && e.page - 1 > this.done) { this.pageDone(e.page - 1, now); }
                }
                break;
            case 'page':
                this.pages = e.of;
                if (this.step !== 'read') { this.enter('read', undefined); }
                this.pageDone(e.page, now);
                if (e.page >= e.of) { this.enter(this.verifies ? 'verify' : 'write', now); }
                break;
        }
    }

    private enter(step: ReadStep, now: number | undefined): void {
        this.step = step;
        this.done = 0;
        this.stepFrom = now;
        this.firstAt = this.lastAt = undefined;
    }

    private pageDone(page: number, now: number): void {
        this.done = page;
        this.firstAt ??= now;
        this.lastAt = now;
    }

    /** About how long the step's pages have left, or undefined while there is no pace yet. */
    private left(now: number): number | undefined {
        if (this.lastAt === undefined || this.pages <= this.done) {
            return undefined;
        }
        // (the pace: from the step's start, or between its pages when its start is not known)
        const each = this.stepFrom !== undefined ? (this.lastAt - this.stepFrom) / this.done
            : this.done >= 2 ? (this.lastAt - this.firstAt!) / (this.done - 1) : undefined;
        return each === undefined ? undefined : Math.max(0, (this.pages - this.done) * each - (now - this.lastAt));
    }

    text(now: number): string {
        const of = this.pages === 1 ? '1 page' : `${this.pages} pages`;
        const at = Math.min(this.done + 1, this.pages);
        const what = (() => {
            switch (this.step) {
                case 'open': return 'opening the pages…';
                case 'layout': return this.done > 0 ? `finding the staves: page ${at} of ${this.pages}…` : `finding the staves on ${of}…`;
                case 'read': return `reading page ${at} of ${this.pages}…`;
                case 'verify': return 'checking the reading against the pages…';
                case 'write': return 'writing the score…';
            }
        })();
        const left = this.step === 'layout' || this.step === 'read' ? this.left(now) : undefined;
        return `${what} ${clockOf(now - this.started)}${left !== undefined ? ` (about ${clockOf(left + 999)} to go for the ${this.step === 'layout' ? 'staves' : 'pages'})` : ''}`;
    }

    /** How far along, 0 to 100: the staves to 40, the pages to 95, then the check and the files. */
    percent(): number {
        const part = this.pages > 0 ? this.done / this.pages : 0;
        switch (this.step) {
            case 'open': return 0;
            case 'layout': return Math.round(40 * part);
            case 'read': return 40 + Math.round(55 * part);
            case 'verify': return 95;
            case 'write': return 98;
        }
    }
}

/** The result of one run. */
export interface OmrRunResult {
    readonly exitCode: number | null;
    readonly done?: Extract<OmrEvent, { event: 'done' }>;
    readonly errors: string[];
    readonly warnings: string[];
    /** The last lines the reader wrote to stderr (for an error message). */
    readonly stderrTail: string[];
}

/**
 * Runs the reader to the end: stdout's lines are events (`onEvent`), stderr is passed through
 * line by line (`onLog`, the output channel), and aborting the signal stops the process.
 */
export function runOmr(
    launch: { command: string; prefix: string[] }, args: readonly string[],
    onEvent: (e: OmrEvent) => void, onLog: (line: string) => void,
    signal?: AbortSignal): Promise<OmrRunResult> {
    return new Promise((resolve, reject) => {
        const child = spawn(launch.command, [...launch.prefix, ...args], { windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
        let done: Extract<OmrEvent, { event: 'done' }> | undefined;
        const errors: string[] = [], warnings: string[] = [], tail: string[] = [];
        const lines = (stream: NodeJS.ReadableStream, each: (line: string) => void) => {
            let rest = '';
            stream.setEncoding('utf8');
            stream.on('data', (chunk: string) => {
                rest += chunk;
                let nl: number;
                while ((nl = rest.indexOf('\n')) >= 0) {
                    each(rest.slice(0, nl).replace(/\r$/, ''));
                    rest = rest.slice(nl + 1);
                }
            });
            stream.on('end', () => { if (rest) { each(rest); } });
        };
        lines(child.stdout!, line => {
            const e = parseEvent(line);
            if (!e) {
                return;
            }
            if (e.event === 'done') { done = e; }
            if (e.event === 'error') { errors.push(e.message); }
            if (e.event === 'warning') { warnings.push(e.message); }
            onEvent(e);
        });
        lines(child.stderr!, line => {
            onLog(line);
            tail.push(line);
            if (tail.length > 5) { tail.shift(); }
        });
        const stop = () => child.kill();
        signal?.addEventListener('abort', stop, { once: true });
        child.on('error', err => { signal?.removeEventListener('abort', stop); reject(err); });
        child.on('close', code => {
            signal?.removeEventListener('abort', stop);
            resolve({ exitCode: signal?.aborted ? null : code, done, errors, warnings, stderrTail: tail });
        });
    });
}
