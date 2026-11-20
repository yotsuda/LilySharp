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

// Orecchiante, the reader that writes a chord chart (.lys) from a recording, as Lily# calls it
// (Orecchiante's docs/repro/lilysharp-requests-2026-11-14-recording-menu.md §2a and Lily#'s reply,
// Lab sessions/p1242/orecchiante/lilysharp-reply-2026-11-14.md "REQUESTS 1"). Like Calamo it ships as its
// own extension; Lily# has the menu (owner's decisions 2026-11-14 and 2026-11-15). No vscode here.

import * as fs from 'fs';

/** The extension that carries the reader; its `activate` returns {@link OrecchianteExports}. */
export const ORECCHIANTE_EXTENSION_ID = 'yotsuda.orecchiante';

/** The environment variable that points at a reader by hand (development). */
export const ORECCHIANTE_PATH_VARIABLE = 'LILYSHARP_ORECCHIANTE_PATH';

/** The protocols this Lily# speaks, and the oldest reader it is written for (0.2.0: the
 *  `orecchiante <recording> -o <x.lys> [--force]` call, its exit codes and its stderr; 0.11.0:
 *  the name — Dettato became Orecchiante, its request 20 of 2026-11-20, `bin/orecchiante.exe` and
 *  `<stem>.orecchiante.json`). */
export const ORECCHIANTE_PROTOCOLS = { min: 1, max: 1 } as const;
export const ORECCHIANTE_MIN_VERSION = '0.11.0';

/** The recordings 0.2.0 reads (its `inputs`). The Explorer's right-click is written from this
 *  list, statically; the command reads the installed reader's own list. */
export const ORECCHIANTE_INPUTS = ['wav', 'mp3', 'm4a', 'mp4', 'aac', 'flac', 'wma'] as const;

/** What the reader's extension exports (undefined off Windows x64 or when its `bin` is broken). */
export interface OrecchianteExports {
    readonly path: string;
    readonly version: string;
    readonly protocol: number;
    readonly inputs?: readonly string[];
}

export interface OrecchianteLocation {
    readonly path: string;
    readonly source: 'environment' | 'extension';
    /** The extension's version and protocol (none for a reader named by the variable). */
    readonly version?: string;
    readonly protocol?: number;
    readonly inputs: readonly string[];
}

/** The reader: ① the environment variable, ② the extension's exported path when it is on disk. */
// (The extension's answer is not named `exports`: compiled to CommonJS, that name would hide the
// module's own exports, where ORECCHIANTE_PATH_VARIABLE lives — the variable was then never read.)
export function locateOrecchiante(env: Record<string, string | undefined>, fromExtension: unknown,
    exists: (p: string) => boolean = fs.existsSync): OrecchianteLocation | undefined {
    const fromEnv = env[ORECCHIANTE_PATH_VARIABLE]?.trim();
    if (fromEnv) {
        return { path: fromEnv, source: 'environment', inputs: ORECCHIANTE_INPUTS };
    }
    const e = fromExtension as Partial<OrecchianteExports> | undefined;
    if (e && typeof e.path === 'string' && e.path.trim() !== '' && exists(e.path.trim())) {
        return {
            path: e.path.trim(), source: 'extension',
            version: typeof e.version === 'string' ? e.version : undefined,
            protocol: typeof e.protocol === 'number' ? e.protocol : undefined,
            inputs: Array.isArray(e.inputs) && e.inputs.every(x => typeof x === 'string') ? e.inputs.map(x => x.toLowerCase()) : ORECCHIANTE_INPUTS,
        };
    }
    return undefined;
}

function compareVersions(a: string, b: string): number {
    const pa = a.split('.').map(Number), pb = b.split('.').map(Number);
    for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
        const d = (pa[i] ?? 0) - (pb[i] ?? 0);
        if (d !== 0) {
            return d;
        }
    }
    return 0;
}

/** What `orecchiante --version --json` answered: `{"version":"0.2.0","protocol":1,"inputs":[…]}`. */
export interface OrecchianteVersion { version: string; protocol: number; inputs?: string[] }

export function parseOrecchianteVersion(stdout: string): OrecchianteVersion | undefined {
    try {
        const o = JSON.parse(stdout.trim());
        if (typeof o?.version !== 'string' || typeof o?.protocol !== 'number') {
            return undefined;
        }
        const inputs = Array.isArray(o.inputs) && o.inputs.every((x: unknown) => typeof x === 'string')
            ? (o.inputs as string[]).map(x => x.toLowerCase()) : undefined;
        return { version: o.version, protocol: o.protocol, inputs };
    } catch {
        return undefined;
    }
}

/** What to do with the reader's answer — as Calamo's is judged (omrImport.ts): a protocol newer
 *  than this Lily# speaks stops with "Update Lily#"; an older protocol offers the reader's update
 *  and stops; a version under the floor offers the update and goes on when it is declined. */
export function judgeOrecchiante(v: OrecchianteVersion):
    { kind: 'ok' } | { kind: 'updateLilySharp'; text: string } | { kind: 'offerUpdate'; text: string; goesOn: boolean } {
    if (v.protocol > ORECCHIANTE_PROTOCOLS.max) {
        return { kind: 'updateLilySharp', text: `Orecchiante ${v.version} speaks protocol ${v.protocol}; this Lily# speaks ${ORECCHIANTE_PROTOCOLS.min}–${ORECCHIANTE_PROTOCOLS.max}. Update Lily#.` };
    }
    if (v.protocol < ORECCHIANTE_PROTOCOLS.min) {
        return { kind: 'offerUpdate', goesOn: false, text: `Orecchiante ${v.version} speaks protocol ${v.protocol}; this Lily# speaks ${ORECCHIANTE_PROTOCOLS.min}–${ORECCHIANTE_PROTOCOLS.max}. Update Orecchiante?` };
    }
    if (compareVersions(v.version, ORECCHIANTE_MIN_VERSION) < 0) {
        return { kind: 'offerUpdate', goesOn: true, text: `Orecchiante ${v.version} is older than this Lily# is written for (${ORECCHIANTE_MIN_VERSION} or newer). Update it? The conversion goes on with this one otherwise.` };
    }
    return { kind: 'ok' };
}

/** The call: the recording, the chart to write, and `--force` only when the person agreed to
 *  replace a chart already there (Lily# asks first; it does not read exit code 2 as "there"). */
export function orecchianteArgs(recording: string, chart: string, replace: boolean): string[] {
    return replace ? [recording, '-o', chart, '--force'] : [recording, '-o', chart];
}

/** The question before a chart already there is read again: it, and the bars a person placed by
 *  hand in the file beside it, which the reading writes over (Orecchiante's note of 2026-11-16: reading
 *  again with the person's lines as its input is Orecchiante's D4, to be agreed with format 2). */
export function replaceQuestion(chartName: string, byHand: number): string {
    const marks = byHand > 0 ? `, and the ${byHand} bar${byHand === 1 ? '' : 's'} you placed by hand in the recording` : '';
    return `${chartName} is already there. Replace it${marks ? marks + ',' : ''} with Orecchiante's reading, or write the reading beside it to compare?`;
}

/** Where "Write Beside" puts Orecchiante's reading of a recording whose chart is already there — a
 *  chart a person may have written by ear, to be compared, not replaced (owner's decision
 *  2026-11-16): `song (Orecchiante).lys`, else `song (Orecchiante 2).lys`, … — the first not there, with
 *  its own file beside it (`song (Orecchiante).orecchiante.json`, the same recording). */
export function besideChart(chart: string, exists: (p: string) => boolean): string {
    const dot = chart.toLowerCase().endsWith('.lys') ? chart.length - 4 : chart.length;
    const stem = chart.slice(0, dot);
    for (let n = 1; ; n++) {
        const candidate = `${stem} (Orecchiante${n === 1 ? '' : ' ' + n}).lys`;
        if (!exists(candidate) && !exists(candidate.slice(0, -4) + '.orecchiante.json')) {
            return candidate;
        }
    }
}

/** The chart a recording is written to: beside it, its own name, `.lys`. */
export function chartForConversion(recording: string): string {
    const slash = Math.max(recording.lastIndexOf('/'), recording.lastIndexOf('\\'));
    const dot = recording.lastIndexOf('.');
    return (dot > slash ? recording.slice(0, dot) : recording) + '.lys';
}

/** What to tell the person when the reader did not write the chart, in the words Calamo's failures
 *  use (omrImport.ts): the recording could not be read (exit 1, 2) with the reason the reader gave
 *  on its last line (`orecchiante: <reason>`), or the reader itself failed (exit 3, or anything else) —
 *  that one with Show Output. */
export function orecchianteFailure(code: number | null, stderr: string, name: string): { text: string; showOutput: boolean } {
    const lines = stderr.split(/\r?\n/).map(l => l.trim()).filter(l => l !== '');
    // The reader says `orecchiante: <name> was not read: <reason>`; the name is already ours (Orecchiante's
    // note of 2026-11-16: it was said twice).
    const reason = lines.length > 0 ? lines[lines.length - 1].replace(/^orecchiante:\s*/, '').replace(/^.*? was not read:\s*/, '') : '';
    if (code === 1 || code === 2) {
        return { text: `Lily#: ${name} cannot be read${reason ? `: ${reason}` : '.'}`, showOutput: false };
    }
    return { text: `Lily#: the Orecchiante reader failed (exit code ${code})${reason ? `: ${reason}` : '.'}`, showOutput: true };
}
