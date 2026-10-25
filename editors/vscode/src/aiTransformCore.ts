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

// The editor-free half of the AI transform: reading the model's reply into the edit it
// makes. No `vscode` here, so `npm test` drives it under plain node.

/** The markers the prompt puts around the selection inside the file it shows the model. */
export const SEL_OPEN = '⟦SELECTION⟧';
export const SEL_CLOSE = '⟦/SELECTION⟧';

/** The part of a snapshot the reply is read against. */
export interface SpanLike {
    origFullText: string;
    startOffset: number;
    endOffset: number;
    origSelectedText: string;
}

/** One place a candidate changes: [beforeLo, beforeHi) of the original file became
 *  [afterLo, afterHi) of the candidate file. */
export interface ChangedSpan { beforeLo: number; beforeHi: number; afterLo: number; afterHi: number; }

/**
 * One candidate, as the edit it makes: `snap` is the span of the ORIGINAL file it replaces
 * (with that span's text as `origSelectedText`), `text` what goes there. A selection-only
 * reply keeps the user's selection as the span; edits elsewhere (or a whole-file reply) are
 * reduced to the one span from the first change to the last, so the apply guard works on one
 * shape. `spans` are the changes themselves — what the panel lights, and what validation and
 * the octave check look at, so the untouched music between two edits is not judged as changed.
 */
export interface CandidateEdit<S extends SpanLike> {
    snap: S;
    text: string;
    spans: ChangedSpan[];
    /** The model's reply as it came, for the conversation history. */
    reply: string;
    /** The model edited the file (edits or a whole file), not only the selection. */
    whole: boolean;
}

/** A reply that could not be read as an edit — an `<old>` not in the file, or in it twice —
 *  with what to tell the model. */
export interface ReplyProblem { problem: string; reply: string; }

export function isProblem<S extends SpanLike>(r: CandidateEdit<S> | ReplyProblem | null): r is ReplyProblem {
    return r !== null && 'problem' in r;
}

/** Strips code fences / stray commentary the model may add despite the contract. */
export function cleanCandidate(raw: string): string {
    let t = raw.trim();
    // Whole reply wrapped in a fenced block -> take the inner content.
    const fence = t.match(/^```[a-zA-Z0-9]*\s*\n([\s\S]*?)\n?```$/);
    if (fence) {
        t = fence[1];
    } else {
        // Or just strip a leading ```lang and a trailing ``` if present unbalanced.
        t = t.replace(/^```[a-zA-Z0-9]*\s*\n?/, '').replace(/\n?```\s*$/, '');
    }
    return t.replace(/\s+$/, '').replace(/^\n+/, '');
}

/** The model writes `\n`; a CRLF file gets its own line ends back, or every line would differ. */
export function sameNewlines(text: string, like: string): string {
    return like.includes('\r\n') ? text.replace(/\r?\n/g, '\r\n') : text;
}

function withoutMarkers(text: string): string {
    return text.split(SEL_OPEN).join('').split(SEL_CLOSE).join('');
}

/**
 * Reads the model's reply. A change reaching outside the selection — a new part needs a `part`
 * line, a block in the section and a score row, and a selection-only reply could only answer
 * "harmonize in a new part" with `voice { }` or not at all (owner report, 2026-09-26) — comes
 * as edits, `<edit><old>…</old><new>…</new></edit>` each, the old text copied from the file
 * and found there exactly once. They used to come as the whole file in `<file>…</file>`, still
 * read: slow and costly to write out for a long score, a slip anywhere in the copy was a change,
 * and a file sent in part came back without the parts the model never saw (2026-10-25).
 * Anything else is the replacement for the selection. null when there is nothing in it.
 */
export function toCandidateEdit<S extends SpanLike>(snapshot: S, raw: string): CandidateEdit<S> | ReplyProblem | null {
    const orig = snapshot.origFullText;
    const edits = readEdits(raw);
    if (edits.length > 0) {
        const applied = applyEdits(orig, edits);
        if (typeof applied === 'string') {
            return { problem: applied, reply: raw };
        }
        return applied.spans.length === 0
            ? { snap: snapshot, text: snapshot.origSelectedText, spans: [], reply: raw, whole: true }
            : reduceToSpan(snapshot, applied.text, applied.spans, raw);
    }
    const file = raw.match(/<file>[ \t]*\r?\n?([\s\S]*?)\r?\n?[ \t]*<\/file>/);
    if (!file) {
        const text = sameNewlines(withoutMarkers(cleanCandidate(raw)), orig);
        if (text.length === 0) {
            return null;
        }
        const lo = snapshot.startOffset;
        const spans = [{ beforeLo: lo, beforeHi: snapshot.endOffset, afterLo: lo, afterHi: lo + text.length }];
        return { snap: snapshot, text, spans, reply: raw, whole: false };
    }
    let full = sameNewlines(withoutMarkers(cleanCandidate(file[1])), orig);
    if (full.trim().length === 0) {
        return null;
    }
    // The file's final newline is not the model's to drop.
    const eol = orig.includes('\r\n') ? '\r\n' : '\n';
    if (orig.endsWith('\n') && !full.endsWith('\n')) {
        full += eol;
    }
    return reduceToSpan(snapshot, full, null, raw);
}

/** One `<edit>` of a reply, as written. */
export interface ReplyEdit { old: string; new: string; }

/** The `<edit><old>…</old><new>…</new></edit>` blocks of a reply, in order. A tag's own line
 *  break (after `<old>`, before `</old>`) is the layout of the reply, not part of the text. */
export function readEdits(raw: string): ReplyEdit[] {
    const inner = (s: string) => s.replace(/^[ \t]*\r?\n/, '').replace(/\r?\n[ \t]*$/, '');
    const out: ReplyEdit[] = [];
    for (const m of raw.matchAll(/<edit>\s*<old>([\s\S]*?)<\/old>\s*<new>([\s\S]*?)<\/new>\s*<\/edit>/g)) {
        out.push({ old: inner(m[1]), new: inner(m[2]) });
    }
    return out;
}

function occurrences(text: string, part: string): number[] {
    const at: number[] = [];
    for (let i = text.indexOf(part); i >= 0; i = text.indexOf(part, i + 1)) at.push(i);
    return at;
}

/**
 * Applies a reply's edits to the file: each old text must be in it exactly once (a copy that
 * differs only by the spaces at its ends is taken too, its new text trimmed alike), and no two
 * may overlap. Returns the changed file and where it changed, or what to tell the model.
 */
export function applyEdits(orig: string, edits: ReplyEdit[]): { text: string; spans: ChangedSpan[] } | string {
    const found: { lo: number; hi: number; text: string; n: number }[] = [];
    for (let i = 0; i < edits.length; i++) {
        let old = sameNewlines(withoutMarkers(edits[i].old), orig);
        let neu = sameNewlines(withoutMarkers(edits[i].new), orig);
        if (old.trim().length === 0) {
            return `Edit ${i + 1} has an empty <old>. Copy the text it replaces — or, to insert, a line next to `
                + 'the place, repeated in <new> with the insertion.';
        }
        let at = occurrences(orig, old);
        if (at.length === 0 && old.trim() !== old) {
            old = old.trim();
            neu = neu.trim();
            at = occurrences(orig, old);
        }
        if (at.length === 0) {
            return `Edit ${i + 1}: its <old> text is not in the file. Copy it exactly from the file — every `
                + 'character, spaces included; the selection markers are not part of the file.';
        }
        if (at.length > 1) {
            return `Edit ${i + 1}: its <old> text occurs ${at.length} times in the file. Include enough of the `
                + 'surrounding text to make it unique.';
        }
        if (old !== neu) {
            found.push({ lo: at[0], hi: at[0] + old.length, text: neu, n: i + 1 });
        }
    }
    found.sort((a, b) => a.lo - b.lo);
    for (let i = 1; i < found.length; i++) {
        if (found[i].lo < found[i - 1].hi) {
            return `Edits ${found[i - 1].n} and ${found[i].n} overlap. Merge them into one edit.`;
        }
    }
    let text = '';
    let from = 0;
    const spans: ChangedSpan[] = [];
    for (const f of found) {
        text += orig.slice(from, f.lo);
        spans.push({ beforeLo: f.lo, beforeHi: f.hi, afterLo: text.length, afterHi: text.length + f.text.length });
        text += f.text;
        from = f.hi;
    }
    return { text: text + orig.slice(from), spans };
}

/** The changed file as one span of the original — from its first difference to its last —
 *  with `spans` the places it changed (null: that one span). */
function reduceToSpan<S extends SpanLike>(snapshot: S, full: string, spans: ChangedSpan[] | null, raw: string): CandidateEdit<S> {
    const orig = snapshot.origFullText;
    let lo = 0;
    const max = Math.min(orig.length, full.length);
    while (lo < max && orig[lo] === full[lo]) lo++;
    let tail = 0;
    while (tail < max - lo && orig[orig.length - 1 - tail] === full[full.length - 1 - tail]) tail++;
    const snap: S = {
        ...snapshot,
        startOffset: lo,
        endOffset: orig.length - tail,
        origSelectedText: orig.slice(lo, orig.length - tail),
    };
    const text = full.slice(lo, full.length - tail);
    const one = [{ beforeLo: lo, beforeHi: orig.length - tail, afterLo: lo, afterHi: lo + text.length }];
    return { snap, text, spans: spans ?? one, reply: raw, whole: true };
}

/** The whole file `full` as the edit it makes to the snapshot's file: the one span where they
 *  differ (the AI's candidate brought back to relative octaves, say). */
export function wholeFileEdit<S extends SpanLike>(snapshot: S, full: string): CandidateEdit<S> {
    return reduceToSpan(snapshot, full, null, '');
}

/** One replacement the server made to a text (lilysharp/aiOctaves), in that text's offsets. */
export interface TextChange { Start: number; End: number; Text: string; }

/**
 * Where offset `o` of a text lands once `changes` (in order, not overlapping) are made to it: a
 * change wholly before it — an insertion AT it too — moves it by its growth; one around it puts
 * it at the same distance into the new text, clamped to it. Carries the user's selection into
 * the file rewritten in absolute octaves, whose changes are marks inside notes and one inserted
 * `octave absolute` line.
 */
export function carryOffset(changes: readonly TextChange[], o: number): number {
    let delta = 0;
    for (const c of changes) {
        if (c.End <= o) {
            delta += c.Text.length - (c.End - c.Start);
        } else if (c.Start < o) {
            return c.Start + delta + Math.min(o - c.Start, c.Text.length);
        } else {
            break;
        }
    }
    return o + delta;
}

/** A rough token count, on the safe side: ASCII about three characters a token, anything else
 *  (lyrics in kana, accented names) one each. */
export function estimateTokens(text: string): number {
    let ascii = 0;
    let other = 0;
    for (let i = 0; i < text.length; i++) {
        if (text.charCodeAt(i) < 128) ascii++; else other++;
    }
    return Math.ceil(ascii / 3) + other;
}

/** The input a model is taken to have when it does not say. */
export const DEFAULT_INPUT_TOKENS = 32_000;

/**
 * How many tokens of the model's input the file may take: what is left of it — a tenth kept
 * back — after the system prompt (the grammar, about 25K tokens) and a reserve for the rest of
 * the conversation (the instruction, the facts, the repair rounds). Never under 4K, the file's
 * share the fixed 12,000-character window used to give.
 */
export function documentBudget(maxInputTokens: number | undefined, systemTokens: number): number {
    const max = maxInputTokens && maxInputTokens > 0 ? maxInputTokens : DEFAULT_INPUT_TOKENS;
    return Math.max(4_000, Math.floor(max * 0.9) - systemTokens - 12_000);
}

/** Characters kept from the file's head when it is sent in part (the key, meter and parts). */
const DOC_HEAD_CHARS = 2500;

/**
 * The file as the model is shown it: whole, with the selection marked, when it fits `budget`
 * tokens (owner's decision 2026-10-25: the AI may change any place, so it sees every place) —
 * else its head, where the key, meter and parts are declared, and a window around the selection.
 * `complete` says which, for the input box and the prompt. Without a selection (`whole`) the
 * file is shown unmarked.
 */
export function documentContext(snapshot: SpanLike, budget: number, whole: boolean): { text: string; complete: boolean } {
    const t = snapshot.origFullText;
    const marked = whole ? t : t.slice(0, snapshot.startOffset) + SEL_OPEN
        + t.slice(snapshot.startOffset, snapshot.endOffset) + SEL_CLOSE + t.slice(snapshot.endOffset);
    if (estimateTokens(marked) <= budget) {
        return { text: marked, complete: true };
    }
    // Characters for the budget, counted as if every one were a token — on the safe side.
    const chars = Math.max(DOC_HEAD_CHARS * 2, budget);
    const selStart = whole ? 0 : snapshot.startOffset;
    const selEnd = whole ? 0 : snapshot.endOffset + SEL_OPEN.length + SEL_CLOSE.length;
    if (whole || selEnd - selStart > chars - DOC_HEAD_CHARS) {
        return { text: marked.slice(0, chars) + '\n… (rest of the file omitted) …', complete: false };
    }
    const half = Math.floor((chars - DOC_HEAD_CHARS - (selEnd - selStart)) / 2);
    const from = Math.max(DOC_HEAD_CHARS, selStart - half);
    const to = Math.min(marked.length, selEnd + half);
    const text = marked.slice(0, DOC_HEAD_CHARS)
        + (from > DOC_HEAD_CHARS ? '\n… (lines omitted) …\n' : '')
        + marked.slice(from, to)
        + (to < marked.length ? '\n… (rest of the file omitted) …' : '');
    return { text, complete: false };
}

/** A resolved pitch as the compiler reports it (`lilysharp/factsForRange`, `pitchesForText`). */
export interface ResolvedPitch { Offset: number; Written: string; Resolved: string; }

/**
 * The MIDI number of a resolved spelling — "C4" = 60, "F#3", "Bb5", "Ex4", "Dbb2" — or null
 * for anything else. The spelling is MeasureCollector.FormatPitch's.
 */
export function pitchToMidi(resolved: string): number | null {
    const m = resolved.match(/^([A-G])(x|#|bb|b)?(-?\d+)$/);
    if (!m) {
        return null;
    }
    const base = { C: 0, D: 2, E: 4, F: 5, G: 7, A: 9, B: 11 }[m[1] as 'C'];
    const alt = m[2] === 'x' ? 2 : m[2] === '#' ? 1 : m[2] === 'bb' ? -2 : m[2] === 'b' ? -1 : 0;
    return (Number(m[3]) + 1) * 12 + base + alt;
}

/** How far outside the selection's own register a note may land before it is questioned. */
export const OCTAVE_SLACK = 12;

/**
 * The candidate's notes that land more than an octave outside the register of the notes it
 * replaces. A relative-octave slip is valid Lily# and draws no diagnostic, so the compiler
 * alone never caught one: the first answer to "harmonize a third above" wrote its harmony in
 * the wrong octave and compiled cleanly (owner report, 2026-09-26). A note this flags is not
 * necessarily wrong — "an octave and a half lower" is a real request — so the caller asks the
 * model to confirm rather than refusing. Empty when the selection had no pitches to measure by.
 */
export function octaveOutliers(original: readonly ResolvedPitch[], candidate: readonly ResolvedPitch[]): ResolvedPitch[] {
    const own = original.map(p => pitchToMidi(p.Resolved)).filter((n): n is number => n !== null);
    if (own.length === 0) {
        return [];
    }
    const lo = Math.min(...own) - OCTAVE_SLACK;
    const hi = Math.max(...own) + OCTAVE_SLACK;
    return candidate.filter(p => {
        const n = pitchToMidi(p.Resolved);
        return n !== null && (n < lo || n > hi);
    });
}

/** True when the edit changes nothing — a reply identical to what it would replace. */
export function isUnchanged<S extends SpanLike>(edit: CandidateEdit<S>): boolean {
    return edit.text === edit.snap.origSelectedText;
}

/** The fields of a VS Code chat model the automatic choice reads (vscode.LanguageModelChat). */
export interface ModelLike {
    readonly id: string;
    readonly vendor: string;
    readonly family: string;
    readonly maxInputTokens?: number;
}

/**
 * The model to use when the user has not picked one (modelClient.autoPick). Only a chat model
 * that answers a plain request is a candidate: one with a family, from GitHub Copilot when it
 * offers any. The owner's VS Code also lists `copilotcli` — the Copilot CLI agent's door, with no
 * family and the largest input limit — which answers a plain request with nothing in a tenth of
 * a second; picked by its input limit alone, it was the automatic model and every bar of "Fix
 * OMR Marks with AI…" came back empty (owner's report 2026-10-23). Then, as before: the small
 * ones (mini, nano, lite, haiku, flash) passed over for a deliberate request and preferred for
 * ghost completion, the rest by how much input they take.
 */
export function pickModel<M extends ModelLike>(models: readonly M[], wantSmall: boolean): M | undefined {
    // (copilotcli has a family now — "auto" — and still answers nothing: never a chat model)
    const chat = models.filter(m => m.family.trim() !== '' && m.vendor !== 'copilotcli');
    const copilot = chat.filter(m => m.vendor === 'copilot');
    const base = copilot.length > 0 ? copilot : chat.length > 0 ? chat : [...models];
    const small = (m: M) => /mini|nano|lite|haiku|flash|small/i.test(`${m.family} ${m.id}`);
    const pool = base.some(m => small(m) === wantSmall) ? base.filter(m => small(m) === wantSmall) : base;
    return [...pool].sort((a, b) => (b.maxInputTokens ?? 0) - (a.maxInputTokens ?? 0))[0];
}

/** A `@todo` mark inside the selection, as the prompt shows it. */
export interface MarkInSelection {
    /** The 0-based line of its note, rest or chord. */
    readonly line: number;
    /** The item as written, mark included (`fis'8@todo(o1203 "F# or F?")`). */
    readonly written: string;
    readonly key?: string | null;
    readonly memo?: string | null;
}

/** What an OMR reader's side file says about a keyed mark (omrScanCore.markFactsOf). */
export interface MarkReadings {
    readonly confidence?: number;
    readonly candidates: readonly { readonly label?: string; readonly text: string }[];
}

/**
 * The marks left to check inside the selection, for the prompt (LilySharp-Omr proposal B6):
 * each one's memo and — when an OMR reader wrote the file — how sure it was and the readings it
 * offers, so "fix the doubtful notes" can choose among them by what makes sense musically.
 * Null when the selection carries no mark.
 */
export function marksContext(marks: readonly MarkInSelection[], readings: ReadonlyMap<string, MarkReadings>): string | null {
    if (marks.length === 0) {
        return null;
    }
    const lines = marks.map(m => {
        const r = m.key ? readings.get(m.key) : undefined;
        const facts: string[] = [];
        if (m.memo) { facts.push(`memo: ${m.memo}`); }
        if (r?.confidence !== undefined) { facts.push(`reader's confidence ${r.confidence.toFixed(2)}`); }
        if (r && r.candidates.length > 0) {
            facts.push('readings: ' + r.candidates.map(c => c.label ? `${c.text} (${c.label})` : c.text).join(', '));
        }
        return `  - line ${m.line + 1}: ${m.written}${facts.length ? ' — ' + facts.join('; ') : ''}`;
    });
    return [
        'Marks left to check in the selection (@todo; an OMR reader writes them where it was unsure of the page):',
        ...lines,
        'A reading replaces the marked item. Where you resolve a mark — with one of its readings, or as the',
        'music around it requires — write the item without its @todo; keep the @todo of every mark you leave.',
    ].join('\n');
}

/**
 * When a candidate played from the source offset `pos` starts (a click on a note of the AI
 * candidate's score — owner's request 2026-10-25): the first time a note written AT it sounds,
 * else the first time the nearest note written before it sounds (a click on a rest, a clef or a
 * mark), else 0. A phrase played several times is heard from its first play.
 */
export function startTimeAt(notes: { S?: number; T?: number }[], pos: number): number {
    let exact: number | undefined;
    let before: { s: number; t: number } | undefined;
    for (const n of notes) {
        const s = n.S ?? -1, t = n.T ?? 0;
        if (s < 0) { continue; }
        if (s === pos) { exact = exact === undefined ? t : Math.min(exact, t); }
        else if (s < pos && (!before || s > before.s || (s === before.s && t < before.t))) { before = { s, t }; }
    }
    return exact ?? before?.t ?? 0;
}