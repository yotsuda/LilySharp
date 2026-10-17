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
    /** The file the page was read FROM when `image` is the reader's own copy of it — a PDF or a
     *  multi-page TIFF, resolved — and which of its pages (from 1). Absent for a single image:
     *  there `image` is the original itself. */
    readonly source?: string;
    readonly sourcePage?: number;
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
    /** The bar's staff alone, top line to bottom line, mapped as `box` is — the reader's `staff`,
     *  written since LilySharp-Omr's "side file: staff" (Lily#'s proposal of 2026-10-14); absent from an older
     *  side file (barStaff then takes the box's middle half). */
    readonly staff?: Box;
}

export interface ScanTodo {
    readonly key: string;
    readonly measure?: number;
    readonly part?: string;
    readonly page: number;
    readonly box: Box;
    readonly memo?: string;
    /** How likely the place needs no edit, 0..1 (the reader's `confidence`, every todo since
     *  LilySharp-Omr's reply of 2026-10-12); absent from an older reader. */
    readonly confidence?: number;
    /** The reader left the mark out of the .lys (`inSource: false`: above its threshold, or a
     *  bar the check found right) — kept here to be seen faintly, never in F8. */
    readonly quiet?: boolean;
    /** A bar-length mark's parts that are off (the side file's `parts[].part`): the box covers
     *  their bars, and the preview frames the same (coverageOf). */
    readonly parts?: readonly string[];
    /** The part-bars the mark covers, named outright (`bars`, proposed to the reader 2026-10-17;
     *  a part left out means every part of that bar). Preferred over `parts` / `part` when present. */
    readonly bars?: readonly { readonly measure: number; readonly part?: string }[];
}

export interface ScanSideFile {
    readonly pages: readonly ScanPage[];
    readonly measures: readonly ScanBar[];
    readonly todos: readonly ScanTodo[];
}

/** The original a page was read from — the PDF or TIFF it came out of, else the image itself. */
export function originalOf(page: ScanPage): string {
    return page.source ?? page.image;
}

/** How a page names its original: `score.pdf p.3` for a page of a PDF or TIFF, `page1.jpg`
 *  for an image read as it is. */
export function pageLabel(page: ScanPage): string {
    const name = path.basename(originalOf(page));
    return page.source ? `${name} p.${page.sourcePage ?? page.page}` : name;
}

/**
 * The scan view's tab: the originals the pages were read from, each once in the order they
 * come (LilySharp-Omr's proposal 2026-10-12, the owner's report: "Scan: score.lys" named the
 * score, not what is shown) — `Scan: score.pdf`, `Scan: page1.jpg, page2.jpg`,
 * `Scan: page1.jpg +2`; with no page, the .lys as before.
 */
export function scanTitle(pages: readonly ScanPage[], lysPath: string): string {
    const seen = new Set<string>(), names: string[] = [];
    for (const p of pages) {
        const original = originalOf(p);
        if (!seen.has(original.toLowerCase())) {
            seen.add(original.toLowerCase());
            names.push(path.basename(original));
        }
    }
    if (names.length === 0) {
        return `Scan: ${path.basename(lysPath)}`;
    }
    return names.length <= 2 ? `Scan: ${names.join(', ')}` : `Scan: ${names[0]} +${names.length - 1}`;
}

/** The scan view's slider starts here: the marks below it show, the rest hide. 0.8 is the
 *  reader's own default threshold (`omr read --todo-threshold`), so at first the view shows
 *  what the .lys carries (LilySharp-Omr's reply 2026-10-12 §3). */
export const SCAN_DEFAULT_THRESHOLD = 0.8;

/** How a mark is drawn: an outline only, heavier and darker the less sure the reader is —
 *  `width` in screen pixels, `opacity` 0..1. A mark with no confidence draws as the least
 *  sure (it is not to be overlooked); a quiet one is a faint hairline whatever its value. */
export function todoStroke(todo: Pick<ScanTodo, 'confidence' | 'quiet'>): { width: number; opacity: number } {
    if (todo.quiet) {
        return { width: 1, opacity: 0.35 };
    }
    const doubt = 1 - Math.min(1, Math.max(0, todo.confidence ?? 0));
    // 1 to 2.5 px: the reader's own marks sit at 0..0.3, and wider than that hid the heads and
    // lines under them (LilySharp-Omr's proposal 2026-10-12-mark-stroke) — the doubt shows in the
    // darkness more than the width.
    return { width: Math.round((1 + 1.5 * doubt) * 10) / 10, opacity: Math.round((0.35 + 0.65 * doubt) * 100) / 100 };
}

/** Whether the slider at `threshold` shows a mark: one below it, or one with no confidence —
 *  and every mark at the slider's top (1), as the view's own filter has it. */
export function shownAt(todo: Pick<ScanTodo, 'confidence'>, threshold: number): boolean {
    return todo.confidence === undefined || threshold >= 1 || todo.confidence < threshold;
}

/** The keys of the marks the slider at `threshold` hides — what the score preview hides too,
 *  so the two show the same marks (owner's request 2026-10-12). Each key once. */
export function hiddenTodoKeys(todos: readonly Pick<ScanTodo, 'key' | 'confidence'>[], threshold: number): string[] {
    return [...new Set(todos.filter(t => !shownAt(t, threshold)).map(t => t.key))];
}

/** The boxes the scan frames a mark with: one per part-bar it covers when it covers more than
 *  one on its page (the preview frames each staff's bar on its own, and the two views frame
 *  alike — owner's report 2026-10-17: "元スキャンではパートふたつを囲む大きな枠になるが、lys 譜面は
 *  パートごとに別の枠"), else its own box — a note's heads, a part's bar — as the reader drew it. */
export function frameBoxesOf(todo: ScanTodo, side: ScanSideFile): Box[] {
    const covered = coverageOf(todo, side).filter(i => side.measures[i].page === todo.page);
    return covered.length >= 2 ? covered.map(i => side.measures[i].box) : [todo.box];
}

/** A mark the preview frames (B4): the part-bars it covers on the scan (`bars`, indices into the
 *  side file's measures — coverageOf; the preview frames the staff's bar written on each one's
 *  line, so the two views frame the same thing, owner's request 2026-10-17) and where it is
 *  written (`at`, its host's offset — what the preview frames when the side file does not know
 *  the mark: the one staff's bar whose written stretch holds it). */
export interface FlaggedMark { readonly at: number; readonly bars: readonly number[] }

/** The marks the preview frames: those the slider shows — a mark whose key is hidden frames
 *  nothing, one the side file does not know (written by hand) is always shown. Each place
 *  once, in the order met (owner's report 2026-10-12: the frames stayed when the slider hid
 *  their marks). */
export function flaggedMarks(todos: readonly ServerTodo[], side: ScanSideFile, hiddenKeys: readonly string[]): FlaggedMark[] {
    const hidden = new Set(hiddenKeys);
    const seen = new Set<number>();
    const marks: FlaggedMark[] = [];
    for (const t of todos) {
        if ((t.Key && hidden.has(t.Key)) || seen.has(t.HostStart)) {
            continue;
        }
        seen.add(t.HostStart);
        const known = t.Key ? side.todos.find(s => s.key === t.Key) : undefined;
        marks.push({ at: t.HostStart, bars: known ? coverageOf(known, side) : [] });
    }
    return marks;
}

/** The side file's marks the .lys no longer carries — taken out once checked (owner's request
 *  2026-10-12: a resolved @todo takes its frame off the scan too). A quiet mark was never in
 *  the .lys, so its absence says nothing and it is never resolved. Each key once. */
export function resolvedTodoKeys(todos: readonly Pick<ScanTodo, 'key' | 'quiet'>[], inSource: readonly ServerTodo[]): string[] {
    const present = new Set(inSource.map(t => t.Key).filter((k): k is string => !!k));
    return [...new Set(todos.filter(t => !t.quiet && !present.has(t.key)).map(t => t.key))];
}
/**
 * The staff of a bar on the scan, which the scan tints as the preview tints a bar from its top
 * line to its bottom one (owner's request 2026-10-14: the two views alike; the click still takes
 * the whole box): the reader's `staff` where it writes one, else the box's middle half — an older
 * reader drew a part's bar two staff spaces past its outer lines (`Sidecar.AddPage`:
 * `st.Top - 2 * s` .. `st.Bottom + 2 * s`) and a staff is four spaces tall.
 */
export function barStaff(bar: Pick<ScanBar, 'box' | 'staff'>): Box {
    if (bar.staff) {
        return bar.staff;
    }
    const box = bar.box, quarter = (box[3] - box[1]) / 4;
    return [box[0], box[1] + quarter, box[2], box[3] - quarter];
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
            const annotated = str(p?.annotated), source = str(p?.source), sourcePage = int(p?.sourcePage);
            pages.push({
                page, image: resolve(image), width, height,
                ...(annotated ? { annotated: resolve(annotated) } : {}),
                ...(source ? { source: resolve(source) } : {}),
                ...(source && sourcePage !== undefined && sourcePage > 0 ? { sourcePage } : {}),
            });
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
                ...(isBox(m?.staff) ? { staff: m.staff } : {}),
            });
        }
    }
    const todos: ScanTodo[] = [];
    for (const t of list(root.todos)) {
        const key = str(t?.key), page = int(t?.page);
        const parts = list(t?.parts).map(p => str(p?.part)).filter((p): p is string => !!p);
        const bars = list(t?.bars).filter(b => int(b?.measure) !== undefined)
            .map(b => ({ measure: b.measure as number, ...(str(b?.part) ? { part: b.part as string } : {}) }));
        if (key && page !== undefined && isBox(t?.box)) {
            todos.push({
                key, page, box: t.box,
                ...(int(t?.measure) !== undefined ? { measure: t.measure } : {}),
                ...(str(t?.part) ? { part: t.part } : {}),
                ...(str(t?.memo) ? { memo: t.memo } : {}),
                ...(typeof t?.confidence === 'number' && Number.isFinite(t.confidence) ? { confidence: t.confidence } : {}),
                ...(t?.inSource === false ? { quiet: true } : {}),
                ...(parts.length > 0 ? { parts } : {}),
                ...(bars.length > 0 ? { bars } : {}),
            });
        }
    }
    return { pages, measures, todos };
}

/** The part-bars (indices into `side.measures`) a mark covers — what its box on the scan is the
 *  union of (LilySharp-Omr Sidecar.BoxOf), and so what the preview frames too (owner's request
 *  2026-10-17: the two views frame the same thing): the ones it names (`bars`), else a length
 *  mark's parts that are off (`parts`), else its own part, else — a mark on the whole bar, its
 *  stems or tuplets — every part of the bar. [] for a mark with no bar. */
export function coverageOf(todo: Pick<ScanTodo, 'measure' | 'part' | 'parts' | 'bars'>, side: ScanSideFile): number[] {
    const wanted: { measure: number; part?: string; parts?: readonly string[] }[] = todo.bars
        ? todo.bars.map(b => ({ measure: b.measure, part: b.part }))
        : todo.measure === undefined ? []
        : todo.parts ? [{ measure: todo.measure, parts: todo.parts }]
        : [{ measure: todo.measure, part: todo.part }];
    const covered: number[] = [];
    side.measures.forEach((m, i) => {
        if (wanted.some(w => w.measure === m.measure
            && (w.parts ? m.part !== undefined && w.parts.includes(m.part) : w.part === undefined || w.part === m.part))) {
            covered.push(i);
        }
    });
    return covered;
}

/** One reading the reader offers for a mark: `text` replaces the marked item (without the mark). */
export interface OmrCandidate { readonly label?: string; readonly text: string }

/** What the side file says about a keyed mark, beyond its box (B6: the AI transform reads it). */
export interface OmrMarkFacts {
    readonly confidence?: number;
    readonly candidates: readonly OmrCandidate[];
}

/** Each keyed todo's confidence and candidates, from the side file's text; empty when it is
 *  not a side file this reads. A todo need not have a box for this (a bar the reader could not
 *  place still has its readings). */
export function markFactsOf(text: string): Map<string, OmrMarkFacts> {
    const facts = new Map<string, OmrMarkFacts>();
    let root: any;
    try {
        root = JSON.parse(text.replace(/^﻿/, ''));
    } catch {
        return facts;
    }
    const version = int(root?.version);
    if (version === undefined || version < SCAN_SIDE_FILE_VERSIONS.min || version > SCAN_SIDE_FILE_VERSIONS.max
        || !Array.isArray(root.todos)) {
        return facts;
    }
    for (const t of root.todos) {
        const key = str(t?.key);
        if (!key || facts.has(key)) { continue; }
        const candidates = (Array.isArray(t.candidates) ? t.candidates : [])
            .filter((c: any) => str(c?.text))
            .map((c: any) => (str(c.label) ? { label: c.label, text: c.text } : { text: c.text }));
        const confidence = typeof t.confidence === 'number' && Number.isFinite(t.confidence) ? t.confidence : undefined;
        facts.set(key, confidence === undefined ? { candidates } : { confidence, candidates });
    }
    return facts;
}

/** A mark as the language server lists it (`lilysharp/todos`, PascalCase on the wire), the
 *  fields this reads. */
export interface ServerTodo {
    readonly Key?: string | null;
    readonly HostStart: number;
    readonly HostEnd: number;
    /** The bar the page prints for it, null when no score plays it. */
    readonly Measure?: number | null;
}

/** What the caret answers on the scan: the mark it is on, else its bar. */
export interface ScanTarget {
    readonly kind: 'todo' | 'bar';
    readonly page: number;
    readonly box: Box;
    /** Kind 'bar': the part of the box the scan lights, the bar's staff (barStaff). */
    readonly staff?: Box;
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

/** A bar of the side file (its index in `measures`) and the bar the page prints for its line. */
export interface PrintedBar { readonly index: number; readonly printed?: number | null }

/**
 * The side file's bar to show for a bar the page prints (B4: the scan follows the score as it
 * scrolls): the first bar whose line prints as that bar, else — a bar the page has and the
 * reader has not, the second half of one the reader wrote too long — the last one before it.
 */
export function scanBarForPrinted(bars: readonly PrintedBar[], printed: number): number | undefined {
    let before: PrintedBar | undefined;
    for (const b of bars) {
        if (b.printed === null || b.printed === undefined) { continue; }
        if (b.printed === printed) { return b.index; }
        if (b.printed < printed && (!before || b.printed > before.printed!)) { before = b; }
    }
    return before?.index;
}

/** The bar written on the line [lineStart, lineEnd], as a target on the scan. The LAST one
 *  there: a deleted line's anchor falls onto the start of the line after it, ahead of that
 *  line's own. */
export function barOnLine(anchors: readonly BarAnchor[], lineStart: number, lineEnd: number): ScanTarget | undefined {
    const a = anchorOnLine(anchors, lineStart, lineEnd);
    return a ? { kind: 'bar', page: a.bar.page, box: a.bar.box, staff: barStaff(a.bar), label: String(a.bar.measure) } : undefined;
}

/** The anchor of the bar written on the line [lineStart, lineEnd] — barOnLine's choice. */
export function anchorOnLine(anchors: readonly BarAnchor[], lineStart: number, lineEnd: number): BarAnchor | undefined {
    let a: BarAnchor | undefined;
    for (const x of anchors) {
        if (x.offset >= lineStart && x.offset <= lineEnd) { a = x; }
    }
    return a;
}

/** The line holding `offset` in `text`: [its start, its end before the line break]. */
export function lineAround(text: string, offset: number): [number, number] {
    const at = Math.max(0, Math.min(offset, text.length));
    const start = text.lastIndexOf('\n', at - 1) + 1;
    let end = text.indexOf('\n', at);
    if (end < 0) { end = text.length; }
    if (end > start && text[end - 1] === '\r') { end--; }
    return [start, end];
}

/**
 * The two views light each other's bar under the mouse (owner's request 2026-10-14), matched
 * through the .lys: the reader writes a part's bar on a line of its own, and a bar of the
 * preview carries where its first note is written (rect.bar-staff's data-start). The scan's bar
 * (its index in `side.measures`) for a preview bar that starts at `start`: the bar written on
 * that line, or undefined.
 */
export function scanBarForStart(side: ScanSideFile, anchors: readonly BarAnchor[], text: string, start: number): number | undefined {
    const [from, to] = lineAround(text, start);
    const a = anchorOnLine(anchors, from, to);
    const index = a ? side.measures.indexOf(a.bar) : -1;
    return index >= 0 ? index : undefined;
}

/** …and back: the line a scan bar is written on, [start, end] — the preview lights each of its
 *  bars whose first note is on it (every printed copy of it). undefined for a bar with no line. */
export function lineOfScanBar(side: ScanSideFile, anchors: readonly BarAnchor[], text: string, index: number): [number, number] | undefined {
    const bar = side.measures[index];
    const a = bar && anchors.find(x => x.bar === bar);
    return a ? lineAround(text, a.offset) : undefined;
}
