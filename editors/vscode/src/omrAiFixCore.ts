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

// The editor-free half of "Lily#: Fix OMR marks with AI…" (docs/omr-ai-fix-design.md; omrAiFix.ts
// is the half that talks to VS Code): which bars are to be fixed and in what order, what the
// model is shown for one bar, how its answer is read, and the gates a proposal passes before
// anyone sees it. Nothing here imports vscode, so `npm test` runs it.

import * as jpeg from 'jpeg-js';
import { PNG } from 'pngjs';
import {
    Box, OmrMarkFacts, ScanBar, ScanSideFile, ServerTodo, lineAround, shownAt,
} from './omrScanCore';
import { cleanCandidate } from './aiTransformCore';

/** A mark as the language server lists it, the fields this reads (lilysharp/todos). */
export interface FixableTodo extends ServerTodo {
    readonly Memo?: string | null;
    readonly Part?: string | null;
}

/** One bar to fix: a LINE of the .lys — an OMR reader writes a part's bar on a line of its own,
 *  and so a bar's marks all sit on one line; the line is what the model rewrites and what is
 *  replaced. */
export interface FixTarget {
    /** The 0-based line. */
    readonly line: number;
    /** [start, end) of the line's text without its indentation and line break — the replaced span. */
    readonly start: number;
    readonly end: number;
    /** The bar as written, marks included. */
    readonly written: string;
    /** The keys of the marks on the line (a mark written by hand has none). */
    readonly keys: readonly string[];
    readonly memos: readonly string[];
    /** The bar the page prints and the part, from the server, when a score plays it. */
    readonly measure?: number | null;
    readonly part?: string | null;
    /** The reader's confidence in the line: the least sure of its marks; undefined when a mark
     *  gives none (an older reader, or a mark written by hand) — the least sure of all. */
    readonly confidence?: number;
    /** Where the bar is on the scan: the mark's box, else its bar's. */
    readonly crop?: { readonly page: number; readonly box: Box };
    /** The readings the reader offers for the marks (its `candidates`), in its order. */
    readonly candidates: readonly { readonly label?: string; readonly text: string }[];
}

/**
 * The bars to fix, one per line that carries a mark, the least sure first (then by line). Only
 * the server's marks count — a mark the side file lists but the .lys no longer carries is
 * resolved. `side` and `facts` (parseSideFile, markFactsOf) add the confidence, the crop and the
 * readings; without them a bar still has its memo.
 */
export function fixTargets(
    text: string, todos: readonly FixableTodo[], side?: ScanSideFile, facts?: ReadonlyMap<string, OmrMarkFacts>): FixTarget[] {
    const byLine = new Map<number, FixableTodo[]>();
    for (const t of todos) {
        const [start] = lineAround(text, t.HostStart);
        const group = byLine.get(start);
        if (group) { group.push(t); } else { byLine.set(start, [t]); }
    }
    const targets: FixTarget[] = [];
    for (const [lineStart, group] of byLine) {
        const [, lineEnd] = lineAround(text, lineStart);
        let start = lineStart, end = lineEnd;
        while (start < end && /\s/.test(text[start])) { start++; }
        while (end > start && /\s/.test(text[end - 1])) { end--; }
        if (start >= end) {
            continue;
        }
        const keys = group.map(t => t.Key).filter((k): k is string => !!k);
        const memos = [...new Set(group.map(t => t.Memo).filter((m): m is string => !!m))];
        const confidences = keys.map(k => side?.todos.find(s => s.key === k)?.confidence ?? facts?.get(k)?.confidence);
        const confidence = keys.length === 0 || confidences.some(c => c === undefined)
            ? undefined
            : Math.min(...(confidences as number[]));
        const line = text.slice(0, lineStart).split('\n').length - 1;
        const placed = group.find(t => t.Measure !== null && t.Measure !== undefined);
        const candidates = keys.flatMap(k => facts?.get(k)?.candidates ?? []);
        const crop = cropOf(keys, line, side);
        targets.push({
            line, start, end, written: text.slice(start, end), keys, memos,
            measure: placed?.Measure ?? group[0].Measure, part: placed?.Part ?? group[0].Part,
            ...(confidence === undefined ? {} : { confidence }),
            ...(crop ? { crop } : {}),
            candidates,
        });
    }
    return targets.sort((a, b) => doubtOf(a) - doubtOf(b) || a.line - b.line);
}

/** Lower is less sure: a target with no confidence first. */
function doubtOf(t: FixTarget): number {
    return t.confidence === undefined ? -1 : t.confidence;
}

/** Where the bar is on the scan: a bar mark's box (the part-bars it covers), else the bar written
 *  on the line (`measures[].line`), else a note mark's box — a note mark boxes the heads alone, and
 *  the model, like the person, wants the bar around them. */
function cropOf(keys: readonly string[], line: number, side?: ScanSideFile): FixTarget['crop'] | undefined {
    if (!side) {
        return undefined;
    }
    const marks = keys.map(k => side.todos.find(s => s.key === k)).filter((t): t is NonNullable<typeof t> => !!t);
    const barMark = marks.find(t => t.kind !== 'note');
    if (barMark) {
        return { page: barMark.page, box: barMark.box };
    }
    const bar = side.measures.find(m => m.line === line + 1);
    if (bar) {
        return { page: bar.page, box: bar.box };
    }
    return marks.length > 0 ? { page: marks[0].page, box: marks[0].box } : undefined;
}

/** Whether a target is checked by default: the scan view's slider shows its marks (shownAt). */
export function checkedByDefault(target: FixTarget, threshold: number): boolean {
    return shownAt({ confidence: target.confidence }, threshold);
}

/** The targets whose line lies in [from, to] (0-based lines, inclusive). */
export function targetsInLines(targets: readonly FixTarget[], from: number, to: number): FixTarget[] {
    return targets.filter(t => t.line >= from && t.line <= to);
}

/** `bar 22 (p1)`, `bar 22`, or `line 43` when no score plays the line. */
export function labelOf(target: FixTarget): string {
    if (target.measure === null || target.measure === undefined) {
        return `line ${target.line + 1}`;
    }
    return target.part ? `bar ${target.measure} (${target.part})` : `bar ${target.measure}`;
}

// ---------------------------------------------------------------------------------------------
// What the model is shown
// ---------------------------------------------------------------------------------------------

/** The bars around the target: the same part's bar before and after, and the other parts' bars
 *  at the same place — the clues to the meter the bar must fill. */
export interface BarNeighbours {
    readonly before?: string;
    readonly after?: string;
    readonly others: readonly { readonly part: string; readonly text: string }[];
}

/** The text of the 0-based line, trimmed. */
export function lineText(text: string, line: number): string | undefined {
    const lines = text.split('\n');
    return line >= 0 && line < lines.length ? lines[line].replace(/\r$/, '').trim() : undefined;
}

/** How many other parts' bars are shown at most. */
const MAX_OTHER_PARTS = 8;

/**
 * The neighbours from the side file's bars (each pinned to its line) when it has them, else
 * the lines next to the target when they look like bars (they end a bar with `|`).
 */
export function neighboursOf(text: string, target: FixTarget, side?: ScanSideFile): BarNeighbours {
    const here = side?.measures.find(m => m.line === target.line + 1);
    if (side && here && here.part !== undefined) {
        const at = (m: ScanBar | undefined) => m?.line !== undefined ? lineText(text, m.line - 1) : undefined;
        const same = (m: ScanBar) => m.part === here.part;
        const others = side.measures
            .filter(m => m.measure === here.measure && m.part !== undefined && m.part !== here.part && m.line !== undefined)
            .slice(0, MAX_OTHER_PARTS)
            .map(m => ({ part: m.part!, text: lineText(text, m.line! - 1) ?? '' }))
            .filter(o => o.text.length > 0);
        return {
            before: at(side.measures.find(m => same(m) && m.measure === here.measure - 1)),
            after: at(side.measures.find(m => same(m) && m.measure === here.measure + 1)),
            others,
        };
    }
    const barLike = (s: string | undefined) => s && /\|\s*$/.test(s) ? s : undefined;
    return {
        before: barLike(lineText(text, target.line - 1)),
        after: barLike(lineText(text, target.line + 1)),
        others: [],
    };
}

/** How much of the file's head (where the title, meter, key and parts are declared) is shown. */
export const HEAD_CHARS = 2500;

/** The declarations before the music: the lines up to the first section, phrase, form or score. */
export function headOf(text: string): string {
    const lines = text.split('\n');
    const head: string[] = [];
    for (const raw of lines) {
        const line = raw.replace(/\r$/, '');
        if (/^\s*(section|phrase|form|score)\b/.test(line)) {
            break;
        }
        head.push(line);
    }
    return head.join('\n').trim().slice(0, HEAD_CHARS);
}

/** A diagnostic as `lilysharp/checkCandidate` reports it (CandidateDiagnostic, PascalCase). */
export interface Diag {
    readonly Line: number;
    readonly Offset: number;
    readonly Length: number;
    readonly Severity: string;
    readonly Message: string;
    readonly Code: string | null;
}

/** The compiler's reports on the target's line, as the prompt lists them. */
export function diagnosticsOn(diags: readonly Diag[], target: FixTarget): string[] {
    return diags
        .filter(d => (d.Severity === 'error' || d.Severity === 'warning')
            && d.Offset <= target.end && d.Offset + Math.max(1, d.Length) > target.start)
        .map(d => `${d.Severity}${d.Code ? ` ${d.Code}` : ''}: ${d.Message}`);
}

export function systemPromptOf(grammar: string): string {
    return [
        'You correct one bar of a musical score written in Lily#, a text music-notation language, that an OMR',
        'reader (optical music recognition) read from a scanned page. Lily# is NOT LilyPond; obey the grammar below.',
        '',
        '<lilysharp-grammar>',
        grammar,
        '</lilysharp-grammar>',
        '',
        'OUTPUT CONTRACT (strict — the answer is read by a program). Reply with ONE JSON object and nothing else:',
        '  {"bar": "<the whole line, rewritten>", "reason": "<one sentence>", "confidence": <0 to 1>}',
        '- "bar" is the complete replacement for the marked line: one line of Lily#, ending with its bar line "|".',
        '  Keep the words at the start of the line that are not music (break, pageBreak, key …, time …, a tempo) as',
        '  they are. Never write @todo in it.',
        '- When you cannot tell what the bar should be, answer {"bar": null, "reason": "<why>", "confidence": 0}.',
        '  A bar you only guess at is worse than none: the reader\'s own doubt stays on the page for a person.',
        '- No commentary, no Markdown, no code fences, no LilyPond (\\relative, \\new Staff, \\repeat …).',
    ].join('\n');
}

/** The reader counts a bar in ticks: 480 a quarter note. */
const TICKS_NOTE = 'The reader counts durations in ticks, 480 a quarter note: "1760/1920 ticks" means the bar as written '
    + 'holds 1760 of the 1920 the meter asks; "voices 1920+480/1920" lists each voice of the bar.';

export interface TaskInput {
    readonly head: string;
    readonly target: FixTarget;
    readonly neighbours: BarNeighbours;
    readonly diagnostics: readonly string[];
    /** The message carries the scan of the bar (M2): the prompt says what the picture is. */
    readonly image?: boolean;
}

export function taskPromptOf(input: TaskInput): string {
    const { head, target, neighbours, diagnostics } = input;
    const parts: string[] = [];
    if (input.image) {
        parts.push('The picture attached to this message is the scanned page around the marked bar — the bar the reader');
        parts.push('wrote the marked line from, with a little of its neighbours. The picture is what the composer wrote;');
        parts.push('the text below is only what the reader made of it. Read the notes, rests, dots, ties, beams and');
        parts.push('tuplets from the picture, and count the beats.');
        parts.push('');
    }
    parts.push('The declarations of the file (its meter, key, parts and octave mode):');
    parts.push('<declarations>');
    parts.push(head);
    parts.push('</declarations>');
    parts.push('');
    if (neighbours.before || neighbours.after || neighbours.others.length > 0) {
        parts.push('The bars around the marked one, as the reader wrote them (they may be wrong too):');
        if (neighbours.before) { parts.push(`  the bar before, same part: ${neighbours.before}`); }
        if (neighbours.after) { parts.push(`  the bar after, same part: ${neighbours.after}`); }
        for (const o of neighbours.others) { parts.push(`  the same bar in part ${o.part}: ${o.text}`); }
        parts.push('');
    }
    parts.push(`The marked line to rewrite (${labelOf(target)}):`);
    parts.push('<bar>');
    parts.push(target.written);
    parts.push('</bar>');
    if (target.memos.length > 0) {
        parts.push('');
        parts.push('What the reader noted about it:');
        for (const m of target.memos) { parts.push(`  - ${m}`); }
        parts.push(TICKS_NOTE);
    }
    if (target.confidence !== undefined) {
        parts.push(`The reader's confidence that the line needs no change: ${target.confidence.toFixed(2)}.`);
    }
    if (target.candidates.length > 0) {
        parts.push('');
        parts.push('Other readings the reader offers for the marked item:');
        for (const c of target.candidates) { parts.push(`  - ${c.label ? `${c.text} (${c.label})` : c.text}`); }
    }
    if (diagnostics.length > 0) {
        parts.push('');
        parts.push('What the Lily# compiler reports on this line as it stands:');
        for (const d of diagnostics) { parts.push(`  - ${d}`); }
    }
    parts.push('');
    parts.push('Rewrite this one line so that it fills the meter exactly and keeps what the page most likely shows:');
    parts.push('check the number of notes and rests, their durations, dots, ties and tuplets. Change as little as');
    parts.push('the problem needs; the other bars are not yours to change. Answer with the JSON object only.');
    return parts.join('\n');
}

/** The follow-up when a proposal fails a gate: the compiler's words, and the same contract. */
export function repairPromptOf(problems: readonly string[]): string {
    return [
        'That line does not pass. The Lily# compiler reports:',
        ...problems.map(p => `  - ${p}`),
        'Answer again with the JSON object only: a corrected "bar", or "bar": null if you cannot tell.',
    ].join('\n');
}

// ---------------------------------------------------------------------------------------------
// Reading the answer
// ---------------------------------------------------------------------------------------------

/** What the model proposes for one bar. */
export interface Proposal {
    /** The line to write, or null: no proposal. */
    readonly bar: string | null;
    readonly reason: string;
    /** 0..1, or null when the model gave none. */
    readonly confidence: number | null;
}

/** A `@todo` mark as written — bare, keyed, with a memo, with `.up` / `.down`. */
const TODO_MARK = /@todo(?:\((?:[^()"]|"(?:[^"\\]|\\.)*")*\))?(?:\.(?:up|down))?/g;

/** The text without its `@todo` marks (a proposal carries none — the contract says so, and a
 *  mark the model copied would keep the bar flagged after it was fixed). */
export function stripTodoMarks(text: string): string {
    return text.replace(TODO_MARK, '');
}

/** One line: runs of whitespace, line breaks included, become one space. */
function oneLine(text: string): string {
    return text.replace(/\s+/g, ' ').trim();
}

/**
 * Reads the model's reply: the JSON object it was asked for, with or without fences or words
 * around it. Null when there is no object to read. A "bar" that is empty, or the written line
 * again (marks aside), is no proposal; one missing its bar line gets the written line's.
 */
export function parseProposal(raw: string, written: string): Proposal | null {
    const text = cleanCandidate(raw);
    const open = text.indexOf('{'), close = text.lastIndexOf('}');
    if (open < 0 || close <= open) {
        return null;
    }
    let obj: any;
    try {
        obj = JSON.parse(text.slice(open, close + 1));
    } catch {
        return null;
    }
    if (!obj || typeof obj !== 'object') {
        return null;
    }
    const reason = typeof obj.reason === 'string' ? obj.reason.trim() : '';
    const confidence = typeof obj.confidence === 'number' && Number.isFinite(obj.confidence)
        ? Math.min(1, Math.max(0, obj.confidence)) : null;
    if (typeof obj.bar !== 'string') {
        return { bar: null, reason: reason || 'no proposal', confidence };
    }
    let bar = oneLine(stripTodoMarks(obj.bar));
    if (bar.length === 0) {
        return { bar: null, reason: reason || 'no proposal', confidence };
    }
    const was = oneLine(written);
    if (/\|$/.test(was) && !/\|$/.test(bar)) {
        bar += ' |';
    }
    if (bar === was || bar === oneLine(stripTodoMarks(written))) {
        return { bar: null, reason: reason ? `unchanged — ${reason}` : 'unchanged', confidence };
    }
    return { bar, reason, confidence };
}

/** The file with the target's line replaced by `bar`. */
export function spliceBar(text: string, target: FixTarget, bar: string): string {
    return text.slice(0, target.start) + bar + text.slice(target.end);
}

// ---------------------------------------------------------------------------------------------
// The gates (design §3.2)
// ---------------------------------------------------------------------------------------------

/** The codes of a bar that does not fill its meter: too short (LYS2001), too long (LYS2002). */
export const LENGTH_CODES: ReadonlySet<string> = new Set(['LYS2001', 'LYS2002']);

export interface GateVerdict {
    /** The file with the proposal has no error it did not have (none in the span, no more in all). */
    readonly compile: boolean;
    /** The proposed bar is neither too short nor too long. */
    readonly length: boolean;
    /** What failed, in the compiler's words — what the model is told. */
    readonly problems: readonly string[];
}

/** A proposal below this is shown, but not taken by "Accept all". */
export const SURE_ENOUGH = 0.5;

/**
 * Judges a proposal spliced in at `span` by the compiler's reports on the whole file before
 * (`baseline`) and after (`candidate`). As the AI transform does, only what the proposal makes
 * worse counts: an error elsewhere that was already there is not its fault.
 */
export function gateOf(baseline: readonly Diag[], candidate: readonly Diag[], span: { start: number; length: number }): GateVerdict {
    const lo = span.start, hi = span.start + span.length;
    const inSpan = (d: Diag) => d.Offset >= lo && d.Offset <= hi;
    const errors = candidate.filter(d => d.Severity === 'error');
    const errorsIn = errors.filter(inSpan);
    const more = errors.length > baseline.filter(d => d.Severity === 'error').length;
    const compileProblems = errorsIn.length > 0 ? errorsIn : more ? errors.filter(d => d.Offset >= lo) : [];
    const lengthProblems = candidate.filter(d => d.Code !== null && LENGTH_CODES.has(d.Code) && inSpan(d));
    const say = (d: Diag) => `${d.Code ? `${d.Code}: ` : ''}${d.Message}`;
    return {
        compile: compileProblems.length === 0 && !more,
        length: lengthProblems.length === 0,
        problems: [...new Set([...compileProblems, ...lengthProblems].map(say))],
    };
}

/** Whether "Accept all" takes a row: both gates passed and the model sure enough. */
export function acceptableByAll(gate: GateVerdict, confidence: number | null): boolean {
    return gate.compile && gate.length && (confidence === null || confidence >= SURE_ENOUGH);
}

// ---------------------------------------------------------------------------------------------
// The scan crop, the estimate, the apply
// ---------------------------------------------------------------------------------------------

/** How much of the bar's height is added around its box on the scan. */
export const CROP_MARGIN = 0.3;

/** The rectangle of the page shown for a box: the box with CROP_MARGIN of its height around it,
 *  kept on the page — as `x y width height`, the viewBox of an <svg> drawing the page image. */
export function cropRect(box: Box, page: { width: number; height: number }, margin = CROP_MARGIN): { x: number; y: number; width: number; height: number } {
    const pad = (box[3] - box[1]) * margin;
    const x0 = Math.max(0, box[0] - pad), y0 = Math.max(0, box[1] - pad);
    const x1 = Math.min(page.width, box[2] + pad), y1 = Math.min(page.height, box[3] + pad);
    return { x: x0, y: y0, width: Math.max(1, x1 - x0), height: Math.max(1, y1 - y0) };
}

/** Seconds a bar takes when nothing has been measured yet ("1 小節 2〜4 秒", design §2.2). */
export const DEFAULT_SECONDS_PER_BAR = 3;

/** How many of the last bars' times the estimate averages. */
export const TIMING_SAMPLES = 20;

/** The mean of the samples, or undefined with none. */
export function averageOf(samples: readonly number[]): number | undefined {
    return samples.length === 0 ? undefined : samples.reduce((a, b) => a + b, 0) / samples.length;
}

/** The samples with one more, the oldest dropped past TIMING_SAMPLES. */
export function withSample(samples: readonly number[], seconds: number): number[] {
    return [...samples, seconds].slice(-TIMING_SAMPLES);
}

/** "12 bars · about 40 s" — the estimate before the run, from the measured average or the default. */
export function estimateOf(bars: number, secondsPerBar: number | undefined): string {
    const per = secondsPerBar ?? DEFAULT_SECONDS_PER_BAR;
    const total = Math.round(bars * per);
    const time = total >= 90 ? `${Math.round(total / 60)} min` : `${total} s`;
    return `${bars} ${bars === 1 ? 'bar' : 'bars'} · about ${time}${secondsPerBar === undefined ? ' (2–4 s a bar)' : ''}`;
}

/** The targets in batches of at most `limit` (the setting `lilysharp.ai.omrFixBatch`). */
export function batchesOf<T>(items: readonly T[], limit: number): T[][] {
    const size = Math.max(1, Math.floor(limit));
    const out: T[][] = [];
    for (let i = 0; i < items.length; i += size) {
        out.push(items.slice(i, i + size));
    }
    return out;
}

/**
 * Where a target's written line is in the document NOW: its own offsets while the text there
 * is unchanged, else the one place the line still occurs (it moved), else -1 (it is gone or
 * ambiguous — the row is not applied).
 */
export function anchorOf(current: string, target: FixTarget): number {
    if (current.slice(target.start, target.end) === target.written) {
        return target.start;
    }
    const first = current.indexOf(target.written);
    return first >= 0 && current.indexOf(target.written, first + 1) < 0 ? first : -1;
}

// ---------------------------------------------------------------------------------------------
// The scan of a bar, for the model (M2: design §3.1 ④)
// ---------------------------------------------------------------------------------------------

/** Pixels, four bytes each (RGBA), rows top to bottom. */
export interface Rgba { readonly width: number; readonly height: number; readonly data: Uint8Array; }

/** The longest side a crop is sent at: more than this is more tokens, not more notes. */
export const MAX_CROP_SIDE = 1568;

/** The image the reader read, decoded: a PNG or a JPEG (the formats the reader keeps its pages
 *  in; a TIFF or a PDF it copies to one of them). Undefined for anything else. */
export function decodeImage(bytes: Uint8Array): Rgba | undefined {
    try {
        if (bytes.length > 8 && bytes[0] === 0x89 && bytes[1] === 0x50 && bytes[2] === 0x4E && bytes[3] === 0x47) {
            const png = PNG.sync.read(Buffer.from(bytes.buffer, bytes.byteOffset, bytes.byteLength));
            return { width: png.width, height: png.height, data: new Uint8Array(png.data.buffer, png.data.byteOffset, png.data.byteLength) };
        }
        if (bytes.length > 3 && bytes[0] === 0xFF && bytes[1] === 0xD8) {
            const img = jpeg.decode(bytes, { useTArray: true, formatAsRGBA: true });
            return { width: img.width, height: img.height, data: img.data };
        }
    } catch {
        // not an image this reads
    }
    return undefined;
}

/**
 * The rectangle of an image (cropRect's, in its pixels), sampled down by a whole factor when a
 * side exceeds `maxSide` — nearest neighbour, which keeps a thin stem a stem; the model is not
 * shown anything the page does not have.
 */
export function cropRgba(image: Rgba, rect: { x: number; y: number; width: number; height: number }, maxSide = MAX_CROP_SIDE): Rgba {
    const x0 = Math.max(0, Math.floor(rect.x)), y0 = Math.max(0, Math.floor(rect.y));
    const x1 = Math.min(image.width, Math.ceil(rect.x + rect.width)), y1 = Math.min(image.height, Math.ceil(rect.y + rect.height));
    const w = Math.max(1, x1 - x0), h = Math.max(1, y1 - y0);
    const step = Math.max(1, Math.ceil(Math.max(w, h) / maxSide));
    const ow = Math.max(1, Math.floor(w / step)), oh = Math.max(1, Math.floor(h / step));
    const out = new Uint8Array(ow * oh * 4);
    for (let y = 0; y < oh; y++) {
        const sy = y0 + y * step;
        for (let x = 0; x < ow; x++) {
            const si = (sy * image.width + x0 + x * step) * 4, oi = (y * ow + x) * 4;
            out[oi] = image.data[si]; out[oi + 1] = image.data[si + 1]; out[oi + 2] = image.data[si + 2]; out[oi + 3] = 255;
        }
    }
    return { width: ow, height: oh, data: out };
}

/** The pixels as a PNG file. */
export function encodePng(image: Rgba): Uint8Array {
    const png = new PNG({ width: image.width, height: image.height });
    png.data = Buffer.from(image.data.buffer, image.data.byteOffset, image.data.byteLength);
    const out = PNG.sync.write(png);
    return new Uint8Array(out.buffer, out.byteOffset, out.byteLength);
}

/** The crop of a page around a box (cropRect) as a PNG, for the model; undefined when the page
 *  cannot be decoded. */
export function scanCropPng(page: Rgba, box: Box, margin = CROP_MARGIN): { png: Uint8Array; width: number; height: number } {
    const crop = cropRgba(page, cropRect(box, page, margin));
    return { png: encodePng(crop), width: crop.width, height: crop.height };
}

/** The line a bar of the webview is cropped to, from the preview's `rect.bar-staff` boxes: the
 *  first whose written stretch (`data-span`, from..to) meets [start, end). Exported for the
 *  test; the webview's script carries the same rule in JavaScript. */
export function barBoxFor(boxes: readonly { from: number; to: number }[], start: number, end: number): number {
    return boxes.findIndex(b => b.from < end && b.to >= start);
}
