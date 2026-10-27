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
    Box, OmrMarkFacts, OmrReading, ScanBar, ScanSideFile, ServerTodo, lineAround, shownAt,
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
    /** The memos a person wrote, not the reader: a mark without a key, or a keyed mark whose
     *  words differ from the reader's own in the side file (owner, 2026-10-24: took a proposal,
     *  saw it was still wrong, wrote what was left and asked again). */
    readonly notes?: readonly string[];
    /** The bar the page prints and the part, from the server, when a score plays it. */
    readonly measure?: number | null;
    readonly part?: string | null;
    /** The reader's confidence in the line: the least sure of its marks; undefined when a mark
     *  gives none (an older reader, or a mark written by hand) — the least sure of all. */
    readonly confidence?: number;
    /** Where the bar is on the scan: the mark's box, else its bar's — and, when the box is much
     *  wider than the bars beside it on its system, about how many bars it spans (`wideBy`: the
     *  reader may have missed a barline, owner's report 2026-10-27). */
    readonly crop?: { readonly page: number; readonly box: Box; readonly wideBy?: number };
    /** The readings the reader offers for the marks (its `candidates`), in its order. */
    readonly candidates: readonly { readonly label?: string; readonly text: string }[];
    /** The reader's other readings of one event of the bar (its `readings`, M3), in its order. */
    readonly readings: readonly OmrReading[];
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
        const notes = [...new Set(group
            .filter(t => !!t.Memo && (!t.Key || (side !== undefined && side.todos.find(s => s.key === t.Key)?.memo !== t.Memo)))
            .map(t => t.Memo!))];
        const confidences = keys.map(k => side?.todos.find(s => s.key === k)?.confidence ?? facts?.get(k)?.confidence);
        const confidence = keys.length === 0 || confidences.some(c => c === undefined)
            ? undefined
            : Math.min(...(confidences as number[]));
        const line = text.slice(0, lineStart).split('\n').length - 1;
        const placed = group.find(t => t.Measure !== null && t.Measure !== undefined);
        const candidates = keys.flatMap(k => facts?.get(k)?.candidates ?? []);
        const readings = keys.flatMap(k => facts?.get(k)?.readings ?? []);
        const crop = cropOf(keys, line, side);
        targets.push({
            line, start, end, written: text.slice(start, end), keys, memos,
            ...(notes.length > 0 ? { notes } : {}),
            measure: placed?.Measure ?? group[0].Measure, part: placed?.Part ?? group[0].Part,
            ...(confidence === undefined ? {} : { confidence }),
            ...(crop ? { crop } : {}),
            candidates, readings,
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
    const crop = boxOf(keys, line, side);
    if (!crop) {
        return undefined;
    }
    const wideBy = widthAmongNeighbours(crop.page, crop.box, side);
    return wideBy ? { ...crop, wideBy } : crop;
}

/**
 * About how many bars wide a box is, beside the other bars of its system on the page (the same
 * page, the same height on it): its width over theirs (the median), rounded, when it is 1.7 times
 * theirs or more — a bar the reader merged with its neighbour, the barline between missed (owner's
 * report 2026-10-27: tabi bar 32, 422 px among bars of 211–218, two bars' music on one line).
 * Undefined when it is not, or there is nothing to compare.
 */
export function widthAmongNeighbours(page: number, box: Box, side: ScanSideFile): number | undefined {
    const height = box[3] - box[1];
    const widths = side.measures
        .filter(m => m.page === page && Math.abs(m.box[1] - box[1]) < height / 2
            && !(m.box[0] === box[0] && m.box[2] === box[2]))
        .map(m => m.box[2] - m.box[0])
        .sort((a, b) => a - b);
    if (widths.length < 2) {
        return undefined;
    }
    const typical = widths[Math.floor(widths.length / 2)];
    const ratio = (box[2] - box[0]) / typical;
    return ratio >= 1.7 ? Math.round(ratio) : undefined;
}

function boxOf(keys: readonly string[], line: number, side: ScanSideFile): { page: number; box: Box } | undefined {
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

/**
 * The bars on the given 0-based lines, as targets with no mark of their own — for opening bars
 * beside the scan without the AI (owner's request 2026-10-24). A line counts when the side file
 * pins a bar to it, or when it holds a bar line `|` outside a comment; the bar number, the part
 * and the crop come from the side file when there is one. In line order, each line once.
 */
export function lineTargets(text: string, lines: readonly number[], side?: ScanSideFile): FixTarget[] {
    const all = text.split('\n');
    const starts: number[] = [];
    for (let i = 0, at = 0; i < all.length; i++) { starts.push(at); at += all[i].length + 1; }
    const targets: FixTarget[] = [];
    for (const line of [...new Set(lines)].sort((a, b) => a - b)) {
        if (line < 0 || line >= all.length) {
            continue;
        }
        const raw = all[line].replace(/\r$/, '');
        const bar = side?.measures.find(m => m.line === line + 1);
        if (!bar && !/\|/.test(raw.replace(/\/\/.*$/, ''))) {
            continue;
        }
        const lead = raw.length - raw.trimStart().length;
        const written = raw.trim();
        if (written.length === 0) {
            continue;
        }
        const start = starts[line] + lead;
        const crop = cropOf([], line, side);
        targets.push({
            line, start, end: start + written.length, written, keys: [], memos: [],
            measure: bar?.measure ?? null, part: bar?.part ?? null,
            ...(crop ? { crop } : {}),
            candidates: [], readings: [],
        });
    }
    return targets;
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
        'WHAT DECIDES THE ANSWER, in this order:',
        '1. The scanned page. The right line is the one that, engraved, looks like the page: the same notes and chords,',
        '   rests, durations, dots, ties, tuplets and the same number of voices. When a picture comes with the task,',
        '   read the whole bar again from it before you look at the reader\'s text. Then compare event by event:',
        '   for every chord, count its noteheads on the page and read each one\'s line or space and accidental. A',
        '   head set on the other side of the stem (two notes a second apart) and a head on a ledger line are the',
        '   ones a reader drops most. The bar was marked for a reason: answer "unchanged" only when every event',
        '   matches the page head for head, and say so in the reason.',
        '2. The meter. The right line fills it — but filling it is a check on a reading of the page, not a goal of',
        '   its own: a line that fills the meter while drawing something the page does not show is wrong.',
        '',
        // How to read a bar, point by point (owner's request 2026-10-27: what sounds together, what
        // joins the notes, which voice a note is in; with the pitch, the symbols a reader confuses
        // and the bars around it, which the owner chose from the proposals).
        'HOW TO READ THE BAR, point by point:',
        '- WHEN each event sounds. Heads stacked on one stem (whole notes: on one vertical line) sound together and are',
        '  one chord <…>; heads one after another along the staff sound one after another. A rest takes its place in time',
        '  as a note does. With two voices, line their events up by where they stand across the bar — an event right',
        '  above another starts with it — and each voice fills the meter on its own.',
        '- What JOINS the events. A curve between two heads of the SAME pitch is a tie: ~ after the first. Between',
        '  different pitches it is a slur: ( after the first note, ) after the last. Open and close each where the page',
        '  does: one that runs on into the next bar is left open here; a slur that came in from the bar before closes',
        '  here with ) alone; a tie that came in needs nothing here. A beam groups the notes it spans; Lily# beams by the',
        '  meter, so write [ ] (c8[ d e f]) only where the page groups notes otherwise.',
        '- Which VOICE each note is in. In voice { … } { … } the first line\'s stems go up (the upper voice), the',
        '  second\'s down. Give each note to the voice its stem points to — stem up: the first line, stem down: the',
        '  second — even where the voices cross; a rest set high belongs to the upper voice, one set low to the lower;',
        '  a head with stems both ways is in both lines. A chord on one stem is one event of that voice.',
        '- The PITCH of each head. Count its ledger lines, and its line or space from the clef in force (a clef change in',
        '  the bar holds from where it stands; an 8va line moves the notes under it). A Lily# note name is the pitch',
        '  itself, not what the key signature makes of it: in G major an F sharp is fis, an F natural f. An accidental',
        '  holds to the end of the bar on its line or space (the F after an F sharp in that bar is fis too, unless a',
        '  natural cancels it); with no accidental, the key signature holds.',
        '- The symbols a reader confuses. A small 3 (5, 6 …) by a group is a tuplet, tuplet 3/2 { … } — a reader often',
        '  drops it, and the bar then looks too long. A dot to the right of a head is a duration dot; one above or below',
        '  it is staccato (@staccato). A whole rest hangs under a line, a half rest sits on one. Small notes are grace',
        '  notes, grace { … }, and take no time in the bar.',
        '- The bars around it. A tie at the end of the bar before means this bar starts on that pitch; a slur, a beam',
        '  or a tuplet the bar before leaves open goes on here. A bar usually keeps the voices of the bars around it,',
        '  and the other parts in the same bar often share its rhythm: use them to check the beats, not to copy notes.',
        '',
        'Everything else is a clue to WHERE the reader went wrong, never to what the bar is: the reader\'s text, its',
        'notes on the bar, its other readings and the compiler\'s messages. The reader\'s note names the symptom it',
        'measured (say, a voice that comes up short), and its guess at the cause is often wrong. Keep nothing of the',
        'reader\'s text only because it is there — its voices, its chords, its spacer rests (s), its octaves:',
        '- If the page shows one voice where the reader wrote `voice { … } { … }`, write one voice.',
        '- Never pad a bar with spacer rests (s), extra dots or longer notes to make the count come out. A missing',
        '  beat is almost always a note the reader dropped, misread or put in the wrong voice or chord.',
        '- Making the compiler\'s error go away is not the aim; a line can compile and still be wrong.',
        '',
        'OUTPUT CONTRACT (strict — the answer is read by a program). Reply with ONE JSON object and nothing else:',
        '  {"bar": "<the whole line, rewritten>", "reason": "<one sentence>", "confidence": <0 to 1>}',
        '- "bar" is the complete replacement for the marked line: one line of Lily#, ending with its bar line "|" —',
        '  several bars, each closed by its own "|", when the line holds bars the reader merged (a missed barline).',
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
    /** The person's edit of the bar so far, when they ask the AI from a row they are editing
     *  (owner's request 2026-10-24). */
    readonly attempt?: string;
}

export function taskPromptOf(input: TaskInput): string {
    const { head, target, neighbours, diagnostics } = input;
    const parts: string[] = [];
    if (input.image) {
        parts.push('The picture attached to this message is the scanned page around the marked bar — the bar the reader');
        parts.push('wrote the marked line from, with a little of its neighbours. The picture is what the composer wrote;');
        parts.push('the text below is only what the reader made of it. Read the notes, rests, dots, ties, beams and');
        parts.push('tuplets from the picture, and count the beats. Read how many voices the bar has from the picture');
        parts.push('too (stems up and down at once, rests set above or below the notes) — not from the reader\'s text.');
        // The picture is cut from the reader's box, and the box can be wrong (owner's report
        // 2026-10-27: a box two bars wide, the barline between them missed): the text is matched
        // to the page, never the page cut to fit the text.
        parts.push('The picture is cut from the reader\'s box for the bar, and that box can be wrong: it may show more than');
        parts.push('one bar. Compare the text with every bar in the picture. If the text is ONE of them, correct that bar');
        parts.push('alone. If the text holds the music of SEVERAL — a barline the reader missed, so the line runs a whole');
        parts.push('bar or more too long — write each as a bar of its own on the one line, every bar closed by its bar line.');
        if (input.target.crop?.wideBy) {
            parts.push(`Lily#'s measure of the page: this bar's box is about ${input.target.crop.wideBy} times as wide as the bars beside it`);
            parts.push('on its system — it probably holds that many bars, the barline between them missed by the reader.');
        }
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
    // The bar without its marks: a @todo("…") inside the music is the reader's guess sitting
    // where the notes are, and a model reads it as part of the bar to keep or patch around
    // (owner, 2026-10-24: a note marked "the upper voice is … short" came back padded with s32.).
    // The notes go below, labelled as what they are.
    parts.push(`The marked line to rewrite (${labelOf(target)}), as the reader wrote it:`);
    parts.push('<bar>');
    parts.push(oneLine(stripTodoMarks(target.written)));
    parts.push('</bar>');
    // A person's words are not the reader's guess: they looked at the page — often after an
    // earlier answer that was still wrong — and say what is left (owner, 2026-10-24: "beat 4 is
    // two notes short" was passed as the reader's doubt, and the model answered "unchanged").
    const notes = target.notes ?? [];
    if (notes.length > 0) {
        parts.push('');
        parts.push('What a PERSON who compared this line with the page wrote about it (in any language). Trust it: the');
        parts.push('line as written is wrong in that way. Find that place on the page and fix it; do not answer "unchanged":');
        for (const n of notes) { parts.push(`  - ${n}`); }
    }
    if (input.attempt) {
        parts.push('');
        parts.push('A PERSON comparing the line with the page has started to rewrite it as below. It may be unfinished or');
        parts.push('partly wrong; keep what it gets right, and check the rest against the page:');
        parts.push('<attempt>');
        parts.push(oneLine(stripTodoMarks(input.attempt)));
        parts.push('</attempt>');
    }
    const reader = target.memos.filter(m => !notes.includes(m));
    if (reader.length > 0) {
        parts.push('');
        parts.push('What the reader noted about it — the symptom it measured; its guess at the cause may be wrong:');
        for (const m of reader) { parts.push(`  - ${m}`); }
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
    if (target.readings.length > 0) {
        parts.push('');
        parts.push('Other readings of ONE event of this bar that the reader\'s detector half-supports (as written -> as it');
        parts.push('might be; "-" is no event; evidence 0..1 is the detector\'s; "fills" means every voice then fills the meter):');
        for (const r of target.readings) {
            const facts = [r.why, r.evidence === undefined ? null : `evidence ${r.evidence.toFixed(2)}`,
                r.fills === undefined ? null : r.fills ? 'fills the bar' : 'does not fill the bar'].filter(Boolean);
            parts.push(`  - ${r.was} -> ${r.now} (${facts.join(', ')})`);
        }
        parts.push('They are hints from the page, not answers: take one only if the picture and the beats agree with it.');
    }
    if (diagnostics.length > 0) {
        parts.push('');
        parts.push('What the Lily# compiler reports on this line as it stands (a clue to where, not to what):');
        for (const d of diagnostics) { parts.push(`  - ${d}`); }
    }
    parts.push('');
    if (input.image) {
        parts.push('Write the line the picture shows: every note, chord, rest and voice of the bar as the page has it,');
        parts.push('filling the meter because the page does. Rewrite as much of the line as the picture needs, voices');
        parts.push('and chords included; the other bars are not yours to change. Answer with the JSON object only.');
    } else {
        parts.push('No scan of this bar comes with the task. Change it only where the clues above leave one reading that fills');
        parts.push('the meter and is plausible music; never fill it with spacer rests (s) or extra dots. If more than one');
        parts.push('reading fits, answer "bar": null. The other bars are not yours to change. Answer with the JSON object only.');
    }
    return parts.join('\n');
}

/** The follow-up when a proposal fails a gate: what failed, and the same contract. */
export function repairPromptOf(problems: readonly string[]): string {
    return [
        'That line does not pass:',
        ...problems.map(p => `  - ${p}`),
        'Do not patch the line to silence this — no spacer rests (s), extra dots or longer notes added to make the',
        'count come out. Read the bar from the page again; the failure says where your reading and the page part.',
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
    /** The model wrote the line back as it is: it reads the bar as right. Taking such a row
     *  removes the line's @todo marks and nothing else (resolvedLine; owner's idea 2026-10-23). */
    readonly unchanged?: boolean;
}

/** The written line with its @todo marks taken out and nothing else changed — what taking an
 *  "unchanged" answer writes. */
export function resolvedLine(written: string): string {
    return stripTodoMarks(written);
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
        return { bar: null, reason: reason ? `unchanged — ${reason}` : 'unchanged', confidence, unchanged: true };
    }
    return { bar, reason, confidence };
}

/** One piece of a line in the panel's word diff: its text, whether a space came before it in
 *  its line, and whether it is in both lines, only the written one, or only the proposal. */
export interface DiffPiece {
    readonly text: string;
    readonly space: boolean;
    readonly kind: 'same' | 'del' | 'add';
    /** Where the piece starts in its line. */
    readonly start: number;
    /** Where the spaces before it start (its start when it is glued to the piece before). */
    readonly lead: number;
    /** A changed piece told character by character against the one it replaces (`a16` → `a,16`:
     *  only the `,` is new) — how the text prints it; the pictures still paint the whole note. */
    readonly parts?: readonly { readonly text: string; readonly kind: 'same' | 'del' | 'add' }[];
}

/**
 * The offsets in the document of the pieces of one kind, as [from, to) — `base` being where the
 * line's text starts there, `indent` how far its indentation reaches before that: what the panel
 * paints in the pictures. A drawn note's `data-pos` is its node's FULL-span start — the spaces
 * before it, and for the first thing on a line its indentation (previewCore.jumpOffsetFor) — so
 * each range reaches back over the spaces before its piece.
 */
export function rangesOf(pieces: readonly DiffPiece[], kind: 'del' | 'add', base: number, indent = 0): [number, number][] {
    return pieces.filter(p => p.kind === kind)
        .map(p => [base + p.lead - (p.lead === 0 ? indent : 0), base + p.start + p.text.length]);
}

/** The line as the panel prints it: a @todo mark as the bare word, its key and memo left out
 *  (owner's request 2026-10-23: the memo is already in the bar's head). Display only. */
export function shortTodos(text: string): string {
    return text.replace(TODO_MARK, m => /\.(up|down)$/.test(m) ? `@todo${m.slice(m.lastIndexOf('.'))}` : '@todo');
}

/** A note, rest or spacer at the start of a piece: its pitch, octave marks, duration and dots
 *  (and a full-bar rest's `*N`) — what a mark is glued after. */
const ITEM_HEAD = /^(?:[a-g](?:is|es|s)*|[rsR])[',]*(?:\d+\.*)?(?:\*\d+(?:\/\d+)?)?/;
/** What may follow an item in its piece: a tie, a slur, a beam, a mark, a brace. */
const ITEM_GLUE = /^(?:$|[~()[\]@{}|\\^_-])/;
/** Words whose next pieces are pitches that are not items (`key d major`, `transpose c d`). */
const PITCH_WORDS: Readonly<Record<string, number>> = { key: 1, transpose: 2 };

/**
 * Where each music item of a line ends — a note, a rest, a spacer or a chord, with its duration
 * and dots, before what is glued after it — the places a `@todo` can go (the grammar takes the
 * mark before or after a tie, slur or beam alike). `spacer` marks an `s`.
 */
export function itemEndsOf(line: string): { at: number; spacer: boolean }[] {
    const ends: { at: number; spacer: boolean }[] = [];
    let skip = 0;
    for (let i = 0; i < line.length;) {
        if (/\s/.test(line[i])) { i++; continue; }
        if (line[i] === '<' && line[i + 1] !== '<') {
            // A chord: its members are not items; the chord ends after '>' and its duration.
            const close = line.indexOf('>', i);
            if (close < 0) { break; }
            const tail = /^\d*\.*/.exec(line.slice(close + 1))![0];
            ends.push({ at: close + 1 + tail.length, spacer: false });
            i = close + 1 + tail.length;
            continue;
        }
        let j = i;
        while (j < line.length && !/\s/.test(line[j])) { j++; }
        const piece = line.slice(i, j);
        const lead = /^[{(]*/.exec(piece)![0].length;
        const head = ITEM_HEAD.exec(piece.slice(lead));
        if (skip > 0) {
            skip--;
        } else if (PITCH_WORDS[piece] !== undefined) {
            skip = PITCH_WORDS[piece];
        } else if (head && ITEM_GLUE.test(piece.slice(lead + head[0].length))) {
            ends.push({ at: i + lead + head[0].length, spacer: piece[lead] === 's' });
        }
        i = j;
    }
    return ends;
}

/**
 * The bar to write with a `@todo` still on it (owner's request 2026-10-24: a proposal that is
 * better but not right yet is taken, and the bar stays marked): at the item nearest to where the
 * written line's first mark was, a note before a spacer; the mark's key is kept, so the scan still
 * knows the place, and its memo is the person's words (none: the mark alone), `.up` / `.down`
 * kept. Undefined when the bar has no item to hang it on.
 */
export function withTodoMark(written: string, bar: string, memo: string): string | undefined {
    const first = new RegExp(TODO_MARK.source).exec(written);
    const at = first ? first.index : 0;
    const key = first ? /^@todo\(\s*([A-Za-z][\w-]*)/.exec(first[0])?.[1] : undefined;
    const side = first ? /\.(?:up|down)$/.exec(first[0])?.[0] ?? '' : '';
    const ends = itemEndsOf(bar);
    const notes = ends.filter(e => !e.spacer);
    const pool = notes.length > 0 ? notes : ends;
    if (pool.length === 0) {
        return undefined;
    }
    const near = pool.reduce((best, e) => Math.abs(e.at - at) < Math.abs(best.at - at) ? e : best);
    const words = memo.replace(/\s+/g, ' ').trim();
    const args = [key, words ? `"${words.replace(/\\/g, '\\\\').replace(/"/g, '\\"')}"` : undefined].filter(Boolean);
    const mark = `@todo${args.length ? `(${args.join(' ')})` : ''}${side}`;
    return bar.slice(0, near.at) + mark + bar.slice(near.at);
}

/** A line's pieces: a @todo mark whole (its memo has spaces), an annotation (`@staccato`), and
 *  any other run of characters between spaces and `@`s — a note, `|`, `{`, `tuplet`, `3/2`. */
const PIECE = /@todo(?:\((?:[^()"]|"(?:[^"\\]|\\.)*")*\))?(?:\.(?:up|down))?|@[^\s@]+|[^\s@]+/g;

/** A slur, a beam or a tie glued to a note: its own piece, so a note keeps matching itself when a
 *  mark between them goes (`g,8@todo(…)( a,8` against `g,8( a,8`: the owner's Largo, 2026-10-24). */
const GLUE = /[()[\]~]|[^()[\]~]+/g;

function piecesOf(line: string): { text: string; space: boolean; start: number; lead: number }[] {
    const out: { text: string; space: boolean; start: number; lead: number }[] = [];
    let m: RegExpExecArray | null;
    let end = 0;
    PIECE.lastIndex = 0;
    while ((m = PIECE.exec(line))) {
        const space = m.index > 0 && /\s/.test(line[m.index - 1]);
        if (m[0].startsWith('@')) {
            out.push({ text: m[0], space, start: m.index, lead: end });
            end = m.index + m[0].length;
            continue;
        }
        let g: RegExpExecArray | null;
        GLUE.lastIndex = 0;
        let first = true;
        while ((g = GLUE.exec(m[0]))) {
            const start = m.index + g.index;
            out.push({ text: g[0], space: first && space, start, lead: end });
            end = start + g[0].length;
            first = false;
        }
    }
    return out;
}

/**
 * The written line and the proposal, piece by piece (the longest common run kept, the rest
 * marked): what the panel prints under its pictures, the removed pieces struck through in red
 * and the added ones in green — the two whole lines were long and hard to compare (owner's
 * report 2026-10-23).
 */
export function wordDiff(written: string, proposal: string): { before: DiffPiece[]; after: DiffPiece[] } {
    const a = piecesOf(written), b = piecesOf(proposal);
    const n = a.length, m = b.length;
    const lcs: number[][] = Array.from({ length: n + 1 }, () => new Array<number>(m + 1).fill(0));
    for (let i = n - 1; i >= 0; i--) {
        for (let j = m - 1; j >= 0; j--) {
            lcs[i][j] = a[i].text === b[j].text ? lcs[i + 1][j + 1] + 1 : Math.max(lcs[i + 1][j], lcs[i][j + 1]);
        }
    }
    const before: DiffPiece[] = [], after: DiffPiece[] = [];
    let i = 0, j = 0;
    while (i < n && j < m) {
        if (a[i].text === b[j].text) {
            before.push({ ...a[i], kind: 'same' });
            after.push({ ...b[j], kind: 'same' });
            i++; j++;
        } else if (lcs[i + 1][j] >= lcs[i][j + 1]) {
            before.push({ ...a[i++], kind: 'del' });
        } else {
            after.push({ ...b[j++], kind: 'add' });
        }
    }
    while (i < n) { before.push({ ...a[i++], kind: 'del' }); }
    while (j < m) { after.push({ ...b[j++], kind: 'add' }); }
    return refined(before, after);
}

/**
 * The changed pieces told character by character (owner's report 2026-10-24: `a16` → `a,16`
 * struck the whole `a16` and greened the whole `a,16`; only the `,` is new). Between the same
 * two kept pieces, as many removed as added pieces are paired in order, and a pair that starts
 * with the same character gets `parts`. Marks (`@…`) are printed shortened, so stay whole.
 */
function refined(before: DiffPiece[], after: DiffPiece[]): { before: DiffPiece[]; after: DiffPiece[] } {
    const runs = (pieces: DiffPiece[]): number[][] => {
        const out: number[][] = [[]];
        pieces.forEach((p, k) => p.kind === 'same' ? out.push([]) : out[out.length - 1].push(k));
        return out;
    };
    const dels = runs(before), adds = runs(after);
    for (let r = 0; r < dels.length && r < adds.length; r++) {
        if (dels[r].length !== adds[r].length) {
            continue;
        }
        dels[r].forEach((di, k) => {
            const ai = adds[r][k];
            const x = before[di].text, y = after[ai].text;
            if (x.startsWith('@') || y.startsWith('@')) {
                return;
            }
            // The same note name (or rest, or bar line) changed in its octave, accidental,
            // duration or dots (owner, 2026-10-24: `b4` → `b'8` is "4" out, "'8" in); another
            // note name is another note, whole.
            if (x[0] === y[0]) {
                const split = charDiff(x, y);
                before[di] = { ...before[di], parts: split.x };
                after[ai] = { ...after[ai], parts: split.y };
            }
        });
    }
    return { before, after };
}

/** Two pieces character by character: the longest common run kept, the rest removed / added. */
function charDiff(x: string, y: string): {
    common: number;
    x: { text: string; kind: 'same' | 'del' }[];
    y: { text: string; kind: 'same' | 'add' }[];
} {
    const n = x.length, m = y.length;
    const lcs: number[][] = Array.from({ length: n + 1 }, () => new Array<number>(m + 1).fill(0));
    for (let i = n - 1; i >= 0; i--) {
        for (let j = m - 1; j >= 0; j--) {
            lcs[i][j] = x[i] === y[j] ? lcs[i + 1][j + 1] + 1 : Math.max(lcs[i + 1][j], lcs[i][j + 1]);
        }
    }
    const xs: { text: string; kind: 'same' | 'del' }[] = [], ys: { text: string; kind: 'same' | 'add' }[] = [];
    const put = <K extends string>(list: { text: string; kind: K }[], c: string, kind: K): void => {
        const last = list[list.length - 1];
        if (last && last.kind === kind) { last.text += c; } else { list.push({ text: c, kind }); }
    };
    let i = 0, j = 0;
    while (i < n && j < m) {
        if (x[i] === y[j]) {
            put(xs, x[i++], 'same'); put(ys, y[j++], 'same');
        } else if (lcs[i + 1][j] >= lcs[i][j + 1]) {
            put(xs, x[i++], 'del');
        } else {
            put(ys, y[j++], 'add');
        }
    }
    while (i < n) { put(xs, x[i++], 'del'); }
    while (j < m) { put(ys, y[j++], 'add'); }
    return { common: lcs[0][0], x: xs, y: ys };
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
    /** The reader's comparison of the proposal with the scan (gate ③, M3): true when the bar
     *  shows no difference worth a note, false when it does; undefined when it was not run (no
     *  reader with `verify-bars`, no side file, the setting off, or the first two gates failed). */
    readonly verify?: boolean;
    /** What failed, in the compiler's words — what the model is told. */
    readonly problems: readonly string[];
}

/** A proposal below this is shown, but not taken by "Accept all". */
export const SURE_ENOUGH = 0.5;

/** A `bar` event of the reader's `verify --bars` (omrCore.OmrEvent), the fields the gate reads. */
export interface BarCheck {
    readonly measure: number;
    readonly part: string | null;
    readonly differs: boolean;
    readonly confidence: number;
    readonly memo?: string;
}

/** A difference the reader is at least this sure needs no edit does not fail the gate: a tie, a
 *  slur, a dot it saw on the scan alone, a blank bar's rest are reported as differences at
 *  0.6–0.9 (the reply of 2026-10-23), and are not what a bar's rewrite is about. */
export const VERIFY_SURE = 0.8;

/** The reader's own number for the bar written on the target's line (`measures[].line`), and the
 *  part it read it as — what `verify --bars` and its `bar` events go by. The reader's count parts
 *  from the page's printed numbers after a bar it wrote too long, so the server's `Measure` is
 *  only the fallback. */
export function readerBarOf(target: FixTarget, side?: ScanSideFile): { measure: number; part: string | null } | undefined {
    const bar = side?.measures.find(m => m.line === target.line + 1);
    if (bar) {
        return { measure: bar.measure, part: bar.part ?? null };
    }
    return target.measure === null || target.measure === undefined ? undefined : { measure: target.measure, part: target.part ?? null };
}

/**
 * Gate ③'s verdict from the reader's `bar` events: the checks of the bar's own part and of the
 * bar as a whole (`part: null`, its stems and tuplets) must each show no difference, or one the
 * reader is VERIFY_SURE of. Undefined when the reader reported nothing for the bar (the gate
 * stays "—"). The memos of the failing checks are what the model is told.
 */
export function verifyVerdict(checks: readonly BarCheck[], bar: { measure: number; part: string | null }): { ok: boolean; memos: string[] } | undefined {
    const mine = checks.filter(c => c.measure === bar.measure && (c.part === null || bar.part === null || c.part === bar.part));
    if (mine.length === 0) {
        return undefined;
    }
    const failing = mine.filter(c => c.differs && c.confidence < VERIFY_SURE);
    return { ok: failing.length === 0, memos: failing.map(c => c.memo ?? `bar ${c.measure}${c.part ? ` (${c.part})` : ''}: differs from the scan`) };
}

/** The gate with the reader's verdict folded in: `verify` set, its memos among the problems. */
export function withVerify(gate: GateVerdict, verdict: { ok: boolean; memos: string[] } | undefined): GateVerdict {
    if (!verdict) {
        return gate;
    }
    return { ...gate, verify: verdict.ok, problems: verdict.ok ? gate.problems : [...gate.problems, ...verdict.memos.map(m => `the scan says: ${m}`)] };
}

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

/** Whether "Accept all" takes a row: every gate that ran passed, and the model is sure enough. */
export function acceptableByAll(gate: GateVerdict, confidence: number | null): boolean {
    return gate.compile && gate.length && gate.verify !== false && (confidence === null || confidence >= SURE_ENOUGH);
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

/** A crop whose longer side is under this is enlarged by a whole factor (at most 3) towards
 *  MAX_CROP_SIDE: a bar of a 300 dpi page is about 500 px wide, its noteheads some 20 px, and a
 *  model reading it at that size misses a head set beside the stem (owner's Chopin Op. 28/4,
 *  bar 4, 2026-10-24: the F of two F-G# seconds, dropped by the reader, and passed as right). */
export const MIN_CROP_SIDE = 1000;

/**
 * The rectangle of an image (cropRect's, in its pixels), sampled down by a whole factor when a
 * side exceeds `maxSide`, up by one when the longer side is under `minSide` (scanCropPng passes
 * MIN_CROP_SIDE) — nearest neighbour either way, which keeps a thin stem a stem; the model is not
 * shown anything the page does not have.
 */
export function cropRgba(image: Rgba, rect: { x: number; y: number; width: number; height: number }, maxSide = MAX_CROP_SIDE, minSide = 0): Rgba {
    const x0 = Math.max(0, Math.floor(rect.x)), y0 = Math.max(0, Math.floor(rect.y));
    const x1 = Math.min(image.width, Math.ceil(rect.x + rect.width)), y1 = Math.min(image.height, Math.ceil(rect.y + rect.height));
    const w = Math.max(1, x1 - x0), h = Math.max(1, y1 - y0);
    const step = Math.max(1, Math.ceil(Math.max(w, h) / maxSide));
    const up = step === 1 && Math.max(w, h) < minSide ? Math.max(1, Math.min(3, Math.floor(maxSide / Math.max(w, h)))) : 1;
    const ow = Math.max(1, Math.floor(w / step)) * up, oh = Math.max(1, Math.floor(h / step)) * up;
    const out = new Uint8Array(ow * oh * 4);
    for (let y = 0; y < oh; y++) {
        const sy = y0 + Math.floor(y / up) * step;
        for (let x = 0; x < ow; x++) {
            const si = (sy * image.width + x0 + Math.floor(x / up) * step) * 4, oi = (y * ow + x) * 4;
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
    const crop = cropRgba(page, cropRect(box, page, margin), MAX_CROP_SIDE, MIN_CROP_SIDE);
    return { png: encodePng(crop), width: crop.width, height: crop.height };
}

/** The line a bar of the webview is cropped to, from the preview's `rect.bar-staff` boxes: the
 *  first whose written stretch (`data-span`, from..to) meets [start, end). Exported for the
 *  test; the webview's script carries the same rule in JavaScript. */
export function barBoxFor(boxes: readonly { from: number; to: number }[], start: number, end: number): number {
    return boxes.findIndex(b => b.from < end && b.to >= start);
}
