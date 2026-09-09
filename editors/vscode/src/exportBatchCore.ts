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

// The editor-free half of the Explorer batch export (exportBatch.ts holds the
// half that talks to VS Code): which of the right-clicked files are scores,
// which of them would write over each other in one folder, and the one line the
// completion toast says. Nothing here imports vscode, so `npm test` runs it.

import * as path from 'path';

/** One entry of the Explorer's export submenu — a format the server writes. */
export interface ExportFormat {
    /** The server's format word (`lilysharp/export` Format). */
    readonly id: string;
    /** The VS Code command that exports the selection as this format. */
    readonly command: string;
    /** What the menu calls it. */
    readonly title: string;
}

/**
 * Every format the submenu offers, in menu order: the page first, then the score
 * as source or interchange, then sound — the order the preview's save dialog
 * lists them in. The commands are declared in package.json under the same ids.
 */
export const EXPORT_FORMATS: readonly ExportFormat[] = [
    { id: 'pdf', command: 'lilysharp.exportPdf', title: 'PDF' },
    { id: 'svg', command: 'lilysharp.exportSvg', title: 'SVG' },
    { id: 'png', command: 'lilysharp.exportPng', title: 'PNG' },
    { id: 'ly', command: 'lilysharp.exportLy', title: 'LilyPond' },
    { id: 'musicxml', command: 'lilysharp.exportMusicXml', title: 'MusicXML' },
    { id: 'midi', command: 'lilysharp.exportMidi', title: 'MIDI' },
    { id: 'vsqx', command: 'lilysharp.exportVsqx', title: 'VOCALOID' },
];

/** True when the two paths name the same file on this platform. */
export function samePath(a: string, b: string, caseInsensitive = process.platform === 'win32'): boolean {
    return caseInsensitive ? a.toLowerCase() === b.toLowerCase() : a === b;
}

/**
 * The scores among the paths the Explorer handed over, in selection order and
 * without repeats. A multi-selection can hold anything — folders, a README, the
 * same file twice when the clicked item is also selected — and only `.lys` files
 * (any letter case) are exported.
 */
export function lysTargets(candidates: readonly string[], caseInsensitive = process.platform === 'win32'): string[] {
    const targets: string[] = [];
    for (const candidate of candidates) {
        if (!/\.lys$/i.test(candidate)) {
            continue;
        }
        if (targets.some(t => samePath(t, candidate, caseInsensitive))) {
            continue;
        }
        targets.push(candidate);
    }
    return targets;
}

/**
 * The file stems that more than one target shares, each with the files that
 * share it. Every output is named from its source's stem (`song.lys` → `song.pdf`,
 * `song-sub.pdf`), so two `song.lys` from different folders exported into ONE
 * folder write over each other — the batch says so before it writes anything.
 * Stems compare the way the destination file system does (case-folded on Windows).
 */
export function duplicateStems(targets: readonly string[], caseInsensitive = process.platform === 'win32'): Map<string, string[]> {
    const byStem = new Map<string, string[]>();
    for (const target of targets) {
        const stem = path.basename(target).replace(/\.lys$/i, '');
        const key = caseInsensitive ? stem.toLowerCase() : stem;
        const list = byStem.get(key);
        if (list) {
            list.push(target);
        } else {
            byStem.set(key, [target]);
        }
    }
    for (const [key, list] of byStem) {
        if (list.length < 2) {
            byStem.delete(key);
        }
    }
    return byStem;
}

/** What the batch did, as its completion toast reports it. */
export interface ExportTally {
    /** Files the user selected. */
    readonly total: number;
    /** Files the server was asked to export (fewer than total when cancelled). */
    readonly attempted: number;
    /** Files the server refused (a syntax error, a missing file). */
    readonly failed: number;
    /** Output files written, every score of every file. */
    readonly outputs: number;
    /** Outputs a later file wrote over an earlier one's. */
    readonly overwritten: number;
    /** The user stopped the batch before every file was reached. */
    readonly cancelled: boolean;
}

/** The one-line summary of a batch, for the completion toast. */
export function summarizeExport(t: ExportTally, formatTitle: string, folder: string): string {
    const parts = [`${t.outputs} ${formatTitle} file${t.outputs === 1 ? '' : 's'} written to ${folder}`];
    if (t.failed > 0) {
        parts.push(`${t.failed} of ${t.attempted} score file${t.attempted === 1 ? '' : 's'} failed`);
    }
    if (t.overwritten > 0) {
        parts.push(`${t.overwritten} overwritten by a later file with the same name`);
    }
    if (t.cancelled) {
        parts.push(`stopped after ${t.attempted} of ${t.total}`);
    }
    return `Lily#: ${parts.join('; ')}.`;
}
