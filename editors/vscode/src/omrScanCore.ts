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

// The editor-free half of "Lily#: Show Original Scan" (LilySharp-Omr proposal B3; omrScan.ts
// is the half that talks to VS Code): reading the side file the OMR reader writes beside the
// .lys it read (x.omr.json, the proposal's D), and choosing the box on the scan that answers
// a caret. Lily# only reads the file; a field it does not know is left alone, and a file it
// cannot read is no file. Nothing here imports vscode, so `npm test` runs it.

import * as path from 'path';

/** The side file's `version`s this reads (as LilySharp.Lsp.OmrSideFile does). */
export const SCAN_SIDE_FILE_VERSIONS = { min: 1, max: 1 } as const;

/** A box on a page: [left, top, right, bottom] in the pixels of the image as loaded. */
export type Box = readonly [number, number, number, number];

export interface ScanPage {
    readonly page: number;
    /** The image file, resolved against the side file's folder. */
    readonly image: string;
    readonly width: number;
    readonly height: number;
    /** The reader's annotated copy of the page, resolved, when it wrote one. */
    readonly annotated?: string;
}

export interface ScanBar {
    /** The reader's bar number — ITS count of the bars it wrote, which is not always the
     *  number the page prints (an overfull bar is two bars on the page). */
    readonly measure: number;
    readonly part?: string;
    /** The line of the .lys the reader wrote the bar on, from 1, when it says. */
    readonly line?: number;
    readonly page: number;
    readonly box: Box;
}

export interface ScanTodo {
    readonly key: string;
    readonly measure?: number;
    readonly part?: string;
    readonly page: number;
    readonly box: Box;
    readonly memo?: string;
}

export interface ScanSideFile {
    readonly pages: readonly ScanPage[];
    readonly measures: readonly ScanBar[];
    readonly todos: readonly ScanTodo[];
}

/** The side file of a .lys: `x.lys` → `x.omr.json`. */
export function sideFileOf(lysPath: string): string {
    return lysPath.replace(/\.lys$/i, '') + '.omr.json';
}

/** The image types a webview draws (TIFF is not one: Chromium has no decoder for it). */
const DRAWABLE = /\.(png|jpe?g|gif|bmp|webp)$/i;

/** The file a page is shown from: its image when a webview draws it, else the reader's
 *  annotated copy, else undefined (the page is listed but cannot be shown). */
export function shownImage(page: ScanPage): string | undefined {
    if (DRAWABLE.test(page.image)) {
        return page.image;
    }
    return page.annotated && DRAWABLE.test(page.annotated) ? page.annotated : undefined;
}

function isBox(v: unknown): v is Box {
    return Array.isArray(v) && v.length === 4 && v.every(n => typeof n === 'number' && Number.isFinite(n))
        && v[2] > v[0] && v[3] > v[1];
}

const str = (v: unknown): string | undefined => typeof v === 'string' && v.length > 0 ? v : undefined;
const int = (v: unknown): number | undefined => Number.isInteger(v) ? v as number : undefined;

/**
 * Reads the side file's text, or undefined when it is not one this reads. Entries without a
 * page and a box are dropped (they cannot be drawn); relative image paths are taken from the
 * side file's folder.
 */
export function parseSideFile(text: string, sideFilePath: string): ScanSideFile | undefined {
    let root: any;
    try {
        root = JSON.parse(text.replace(/^﻿/, ''));
    } catch {
        return undefined;
    }
    const version = int(root?.version);
    if (version === undefined || version < SCAN_SIDE_FILE_VERSIONS.min || version > SCAN_SIDE_FILE_VERSIONS.max) {
        return undefined;
    }
    const dir = path.dirname(sideFilePath);
    const resolve = (p: string) => path.isAbsolute(p) ? p : path.join(dir, p);
    const list = (v: unknown): any[] => Array.isArray(v) ? v : [];

    const pages: ScanPage[] = [];
    for (const p of list(root.pages)) {
        const page = int(p?.page), image = str(p?.image), width = int(p?.width), height = int(p?.height);
        if (page !== undefined && image && width && height && width > 0 && height > 0) {
            const annotated = str(p?.annotated);
            pages.push({ page, image: resolve(image), width, height, ...(annotated ? { annotated: resolve(annotated) } : {}) });
        }
    }
    const measures: ScanBar[] = [];
    for (const m of list(root.measures)) {
        const measure = int(m?.measure), page = int(m?.page);
        if (measure !== undefined && page !== undefined && isBox(m?.box)) {
            measures.push({
                measure, page, box: m.box,
                ...(str(m?.part) ? { part: m.part } : {}),
                ...(int(m?.line) !== undefined && m.line > 0 ? { line: m.line } : {}),
            });
        }
    }
    const todos: ScanTodo[] = [];
    for (const t of list(root.todos)) {
        const key = str(t?.key), page = int(t?.page);
        if (key && page !== undefined && isBox(t?.box)) {
            todos.push({
                key, page, box: t.box,
                ...(int(t?.measure) !== undefined ? { measure: t.measure } : {}),
                ...(str(t?.part) ? { part: t.part } : {}),
                ...(str(t?.memo) ? { memo: t.memo } : {}),
            });
        }
    }
    return { pages, measures, todos };
}

/** A mark as the language server lists it (`lilysharp/todos`, PascalCase on the wire), the
 *  fields this reads. */
export interface ServerTodo {
    readonly Key?: string | null;
    readonly HostStart: number;
    readonly HostEnd: number;
}

/** What the caret answers on the scan: the mark it is on, else its bar. */
export interface ScanTarget {
    readonly kind: 'todo' | 'bar';
    readonly page: number;
    readonly box: Box;
    /** The mark's key (kind 'todo'), or the bar's number (kind 'bar'). */
    readonly label: string;
}

/** The keyed mark whose note, rest or chord holds the caret, if the side file has its box.
 *  A mark goes by its KEY, which travels with the note through any edit. */
export function todoTarget(side: ScanSideFile, todos: readonly ServerTodo[], offset: number): ScanTarget | undefined {
    for (const t of todos) {
        if (t.Key && offset >= t.HostStart && offset <= t.HostEnd) {
            const box = side.todos.find(s => s.key === t.Key);
            if (box) {
                return { kind: 'todo', page: box.page, box: box.box, label: box.key };
            }
        }
    }
    return undefined;
}

/** A bar of the side file pinned to where it is written in the .lys. */
export interface BarAnchor {
    /** Where the bar's line starts, kept through the edits since (shiftAnchors). */
    offset: number;
    readonly bar: ScanBar;
}

/**
 * Pins each bar the side file places on a line to that line's start in `text` — the .lys as
 * the reader wrote it. A bar is the READER'S bar, so this goes by the line it wrote and not by
 * a bar number: the page's numbers part from the reader's after any bar the reader wrote too
 * long (measured on its Chopin Op.28/4: one more from bar 19 on), and those are the bars a
 * reader of the scan is fixing.
 */
export function anchorsOf(side: ScanSideFile, text: string): BarAnchor[] {
    const starts = [0];
    for (let i = 0; i < text.length; i++) {
        if (text.charCodeAt(i) === 10) { starts.push(i + 1); }
    }
    return side.measures
        .filter(m => m.line !== undefined && m.line <= starts.length)
        .map(m => ({ offset: starts[m.line! - 1], bar: m }))
        .sort((a, b) => a.offset - b.offset);
}

/** One edit, as the editor reports it: `removed` characters at `offset` (in the text before
 *  the edit) replaced by `inserted` characters. */
export interface TextEdit { readonly offset: number; readonly removed: number; readonly inserted: number }

/**
 * Carries the anchors through one batch of edits (an editor's change event: its edits do not
 * overlap and each is placed in the text before the batch, so they apply from the last). An
 * anchor after an edit moves by it; one inside a replaced span goes to the span's start.
 */
export function shiftAnchors(anchors: BarAnchor[], edits: readonly TextEdit[]): void {
    for (const e of [...edits].sort((a, b) => b.offset - a.offset)) {
        const end = e.offset + e.removed, delta = e.inserted - e.removed;
        for (const a of anchors) {
            if (a.offset >= end) { a.offset += delta; }
            else if (a.offset > e.offset) { a.offset = e.offset; }
        }
    }
}

/** The bar written on the line [lineStart, lineEnd], as a target on the scan. The LAST one
 *  there: a deleted line's anchor falls onto the start of the line after it, ahead of that
 *  line's own. */
export function barOnLine(anchors: readonly BarAnchor[], lineStart: number, lineEnd: number): ScanTarget | undefined {
    let a: BarAnchor | undefined;
    for (const x of anchors) {
        if (x.offset >= lineStart && x.offset <= lineEnd) { a = x; }
    }
    return a ? { kind: 'bar', page: a.bar.page, box: a.bar.box, label: String(a.bar.measure) } : undefined;
}
