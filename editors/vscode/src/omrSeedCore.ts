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

// "Report Misread to OMR" (owner's request 2026-10-29): a bar the owner says the reader got wrong,
// kept in a folder for OMR to grow training scores from (LilySharp-Omr
// docs/repro/lilysharp-requests-2026-10-29-seed-from-fixed-bars.md). The owner's alone: the row's
// button shows only when `lilysharp.omr.seedFolder` is written in settings.json by hand — the
// setting is not contributed, so no one else sees it (docs/RELEASING.md: the seed tooling never
// ships). An Accept is never a report: the owner tries the panel's buttons freely, and only a bar
// reported on purpose becomes a seed. OMR never trains on the scan and the bar themselves — they
// are seeds for generated scores, and the scan's bar is for its evaluation alone.

/** The setting under `lilysharp` that names the seed folder; not in package.json on purpose. */
export const SEED_FOLDER_SETTING = 'omr.seedFolder';

/** The bar reported, as the panel knows it. */
export interface SeedBar {
    /** The .lys, its path. */
    readonly lys: string;
    /** The reader's bar number and part (its side file's), else the score's. */
    readonly measure?: number;
    readonly part?: string | null;
    /** The 1-based line of the bar in the .lys. */
    readonly line: number;
    /** The bar's line as the .lys has it at the report. */
    readonly bar: string;
    /** Where it is on the scan, when the side file says. */
    readonly crop?: { readonly page: number; readonly box: readonly number[] };
    /** The page's image and the side file, their paths, when there are. */
    readonly pageImage?: string;
    readonly sideFile?: string;
}

/** seed.json: what OMR reads. The copies beside it are named by their fields. */
export interface SeedRecord {
    readonly seed: 1;
    readonly reported: string;
    /** What the owner said was wrong ('' when nothing was typed). */
    readonly note: string;
    /** The .lys where it is: read it again when the seed is used, for the bar may be fixed later. */
    readonly lys: string;
    /** The .lys as it was at the report, copied beside seed.json. */
    readonly lysCopy: string;
    readonly measure: number | null;
    readonly part: string | null;
    readonly line: number;
    readonly bar: string;
    readonly scan: {
        readonly page: number;
        readonly box: readonly number[];
        readonly image?: string;
        readonly imageCopy?: string;
        /** The bar cut out of the page, as the panel shows it. */
        readonly crop?: string;
        readonly sideFile?: string;
        readonly sideFileCopy?: string;
    } | null;
}

/** The base name of a path, either separator. */
function baseName(p: string): string {
    return p.split(/[\\/]/).pop() ?? p;
}

/** A name safe in a folder name: letters, digits, `-` and `_` only. */
function safe(s: string): string {
    return s.replace(/[^\p{L}\p{N}_-]+/gu, '_').replace(/^_+|_+$/g, '') || 'x';
}

/** The report's folder in the seed folder: when, the .lys, the bar — "20261029-153012-largo-bar12-right". */
export function seedDirName(when: Date, bar: SeedBar): string {
    const p = (n: number) => String(n).padStart(2, '0');
    const stamp = `${when.getFullYear()}${p(when.getMonth() + 1)}${p(when.getDate())}-${p(when.getHours())}${p(when.getMinutes())}${p(when.getSeconds())}`;
    const where = bar.measure !== undefined ? `bar${bar.measure}` : `line${bar.line}`;
    return [stamp, safe(baseName(bar.lys).replace(/\.lys$/i, '')), where, ...(bar.part ? [safe(bar.part)] : [])].join('-');
}

/** The record, given which copies were written (a copy that could not be made is left out). */
export function seedRecordOf(when: Date, bar: SeedBar, note: string,
    copies: { readonly image?: boolean; readonly crop?: boolean; readonly sideFile?: boolean }): SeedRecord {
    const ext = bar.pageImage ? (/\.[^.\\/]+$/.exec(bar.pageImage)?.[0] ?? '') : '';
    return {
        seed: 1,
        reported: when.toISOString(),
        note: note.trim(),
        lys: bar.lys,
        lysCopy: baseName(bar.lys),
        measure: bar.measure ?? null,
        part: bar.part ?? null,
        line: bar.line,
        bar: bar.bar,
        scan: bar.crop ? {
            page: bar.crop.page,
            box: [...bar.crop.box],
            ...(bar.pageImage ? { image: bar.pageImage } : {}),
            ...(copies.image && bar.pageImage ? { imageCopy: `page-${bar.crop.page}${ext}` } : {}),
            ...(copies.crop ? { crop: 'bar.png' } : {}),
            ...(bar.sideFile ? { sideFile: bar.sideFile } : {}),
            ...(copies.sideFile && bar.sideFile ? { sideFileCopy: baseName(bar.sideFile) } : {}),
        } : null,
    };
}
