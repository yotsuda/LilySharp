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

// The editor-free half of "Lily#: Import from Image/PDF…" (omrImport.ts holds the half that
// talks to VS Code). The OMR reader (LilySharp-Omr) is a SEPARATE PROGRAM: this runs it as a
// child process and reads the files it writes, nothing more — no OMR code comes in here
// (LilySharp-Omr docs/repro/lilysharp-omr-distribution-2026-10-05.md, the agreement this
// follows: §3.2 the CLI, §4 L1 where the reader is, L7 the run). Nothing here imports vscode,
// so `npm test` runs it.

import { spawn } from 'child_process';
import * as fs from 'fs';
import * as path from 'path';

/** The CLI-and-output agreement versions this extension speaks (the reader's `protocol`). */
export const OMR_PROTOCOLS = { min: 1, max: 1 } as const;

/** The environment variable that points at a reader by hand (development; L1 ①). */
export const OMR_PATH_VARIABLE = 'LILYSHARP_OMR_PATH';

/** The input files the reader takes, lower case, without the dot. */
export const OMR_INPUT_EXTENSIONS = ['png', 'jpg', 'jpeg', 'tif', 'tiff', 'pdf'] as const;

/** The reader's build for this machine: `win-x64`, `osx-arm64`, `linux-x64`, or undefined. */
export function omrRid(platform: string, arch: string): string | undefined {
    const os = platform === 'win32' ? 'win' : platform === 'darwin' ? 'osx' : platform === 'linux' ? 'linux' : undefined;
    return os && (arch === 'x64' || arch === 'arm64') ? `${os}-${arch}` : undefined;
}

/** The reader's executable name on a platform. */
export function omrExecutableName(platform: string): string {
    return platform === 'win32' ? 'omr.exe' : 'omr';
}

/** Where the reader is, and how it was found. */
export interface OmrLocation {
    readonly path: string;
    readonly source: 'environment' | 'installed';
}

/**
 * Finds the reader (L1): ① the environment variable, ② the newest version installed under
 * `<installRoot>/<version>/<rid>/`, else undefined (the caller offers the download, L2).
 * Not a setting on purpose (the agreement §1: the reader's place is not shown in Settings).
 */
export function locateOmr(
    env: Record<string, string | undefined>, installRoot: string | undefined,
    platform: string, arch: string, exists: (p: string) => boolean = fs.existsSync,
    list: (dir: string) => string[] = d => fs.readdirSync(d)): OmrLocation | undefined {
    const fromEnv = env[OMR_PATH_VARIABLE]?.trim();
    if (fromEnv) {
        return { path: fromEnv, source: 'environment' };
    }
    const rid = omrRid(platform, arch);
    if (!installRoot || !rid || !exists(installRoot)) {
        return undefined;
    }
    const versions = list(installRoot).filter(v => /^\d+\.\d+\.\d+$/.test(v)).sort(compareVersions).reverse();
    for (const version of versions) {
        const exe = path.join(installRoot, version, rid, omrExecutableName(platform));
        if (exists(exe)) {
            return { path: exe, source: 'installed' };
        }
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
 *  and Japanese. */
export function readArguments(inputs: readonly string[], outDir: string, lysc?: string): string[] {
    const args = ['read', ...inputs, '--out', outDir, '--progress', 'json'];
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

/** One line of `--progress json` (§3.2). */
export type OmrEvent =
    | { event: 'start'; pages: number }
    | { event: 'page'; page: number; of: number }
    | { event: 'warning'; message: string }
    | { event: 'done'; lys: string; json?: string; annotated?: string[]; todos?: number }
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

/** The notification's text for a progress event, or undefined to leave it as it is. */
export function progressText(e: OmrEvent): string | undefined {
    switch (e.event) {
        case 'start': return e.pages === 1 ? 'reading 1 page…' : `reading ${e.pages} pages…`;
        case 'page': return `page ${e.page} of ${e.of}…`;
        default: return undefined;
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
