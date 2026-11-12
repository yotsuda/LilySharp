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

import { VoiceEntry } from './webview/playbackCore';

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
    // Whole reply wrapped in a fenced block -> take the inner content. The language tag is any word
    // (owner's trial 2026-10-29: "```Lily#" left its '#' as the file's first line, and the piece
    // went back as one that does not compile).
    const fence = t.match(/^```[^\s`]*[ \t]*\n([\s\S]*?)\n?```$/);
    if (fence) {
        t = fence[1];
    } else {
        // Or just strip a leading ```lang and a trailing ``` if present unbalanced.
        t = t.replace(/^```[^\s`]*[ \t]*\n?/, '').replace(/\n?```\s*$/, '');
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
export function toCandidateEdit<S extends SpanLike>(snapshot: S, reply: string): CandidateEdit<S> | ReplyProblem | null {
    // The note is the musician's, not the file's (splitNote); `reply` stays whole for the history.
    const raw = splitNote(reply).rest;
    const orig = snapshot.origFullText;
    const edits = readEdits(raw);
    if (edits.length > 0) {
        const applied = applyEdits(orig, edits);
        if (typeof applied === 'string') {
            return { problem: applied, reply };
        }
        return applied.spans.length === 0
            ? { snap: snapshot, text: snapshot.origSelectedText, spans: [], reply, whole: true }
            : reduceToSpan(snapshot, applied.text, applied.spans, reply);
    }
    const file = raw.match(/<file>[ \t]*\r?\n?([\s\S]*?)\r?\n?[ \t]*<\/file>/);
    if (!file) {
        const text = sameNewlines(withoutMarkers(cleanCandidate(raw)), orig);
        if (text.length === 0) {
            return null;
        }
        const lo = snapshot.startOffset;
        const spans = [{ beforeLo: lo, beforeHi: snapshot.endOffset, afterLo: lo, afterHi: lo + text.length }];
        return { snap: snapshot, text, spans, reply, whole: false };
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
    return reduceToSpan(snapshot, full, null, reply);
}

/**
 * The musician's note a reply may carry, `<note>…</note>` (owner's request 2026-10-25: a word
 * from the AI about what it did), and the reply without it — what the edit is read from. A
 * composer's `<plan>…</plan>` (its working notes before the music, owner's request 2026-10-27)
 * is neither: it goes first, unread.
 */
export function splitNote(raw: string): { note: string | null; rest: string } {
    raw = withoutPlan(raw);
    const m = raw.match(/<note>([\s\S]*?)<\/note>/);
    if (!m) {
        return { note: null, rest: raw };
    }
    const note = m[1].trim();
    return { note: note.length > 0 ? note : null, rest: raw.slice(0, m.index) + raw.slice(m.index! + m[0].length) };
}

/** The reply without its `<plan>` blocks — an unclosed one (a reply cut off in its plan) to the end. */
export function withoutPlan(raw: string): string {
    return raw.replace(/<plan>[\s\S]*?(<\/plan>|$)/g, '');
}

/** The `<plan>` a reply opens with, trimmed; null when it has none. */
export function planOf(raw: string): string | null {
    const m = raw.match(/<plan>([\s\S]*?)(<\/plan>|$)/);
    return m && m[1].trim().length > 0 ? m[1].trim() : null;
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
            // A new piece in place of the score is the whole new file, not edits quoting the old
            // one (owner's report 2026-10-26: "discard the score and compose a quartet" failed
            // three times on <old> texts the model could not copy).
            return `Edit ${i + 1}: its <old> text is not in the file. Copy it exactly from the file — every `
                + 'character, spaces included; the selection markers are not part of the file. If the request '
                + 'replaces the score with a new one, reply with the whole new file between <file> and </file> instead.';
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
/**
 * Whether a request to compose asks for ONE staff: a lead sheet, a melody alone, or a solo for one
 * instrument (owner's rule 2026-10-27: no single staff unless it is asked for — anything else has
 * its accompaniment).
 */
export function asksForOneStaff(request: string): boolean {
    return /lead[\s-]*sheet|リードシート|melody (alone|only)|just (a|the) melody|only (a|the) melody|メロディ(だけ|のみ)|旋律(だけ|のみ)|\bsolo\b|ソロ|独奏|無伴奏|unaccompanied/i.test(request);
}

/** How many parts a file declares (`part NAME …`). */
export function partCount(text: string): number {
    return new Set([...text.matchAll(/^[ \t]*part[ \t]+([A-Za-z_][\w-]*)/gm)].map(m => m[1])).size;
}

/** A file's top-level `title "…"`, or null when it writes none (or an empty one). The server's
 *  reading is CompositionChecks.TitleOf; this one is the editor's, for a composed piece. */
export function titleOf(text: string): string | null {
    const m = text.match(/^[ \t]*title[ \t]+"((?:[^"\\]|\\.)*)"/m);
    return m && m[1].trim().length > 0 ? m[1].trim() : null;
}

/** A title that names no piece: a template's or a tool's placeholder (as CompositionChecks). */
export function isPlaceholderTitle(title: string): boolean {
    return /^(new piece|new score|untitled|title|piece|my piece|song|composition|無題|新しい曲|タイトル)$/i.test(title.trim());
}

/** A model's answer to "a title for this piece" as a title: one line, no quotes or label around it. */
export function cleanTitle(reply: string): string | null {
    let t = (reply.split(/\r?\n/).find(l => l.trim().length > 0) ?? '').trim();
    t = t.replace(/^(title|タイトル)\s*[:：]\s*/i, '').replace(/^["'「『“]+|["'」』”]+$/g, '').trim();
    t = t.replace(/"/g, '”');
    return t.length > 0 && t.length <= 80 && !isPlaceholderTitle(t) ? t : null;
}

/** `text` with `title` as its title: the title line it has replaced, or a new one at the top
 *  (after leading comment lines). */
export function withTitle(text: string, title: string): string {
    const line = `title "${title}"`;
    const existing = /^[ \t]*title[ \t]+"(?:[^"\\]|\\.)*"[^\r\n]*/m;
    if (existing.test(text)) {
        return text.replace(existing, line);
    }
    const eol = text.includes('\r\n') ? '\r\n' : '\n';
    const lines = text.split(/\r?\n/);
    let at = 0;
    while (at < lines.length && /^\s*\/\//.test(lines[at])) {
        at++;
    }
    lines.splice(at, 0, line);
    return lines.join(eol);
}

/**
 * A selection `start`..`end` of a text carried into that text with `spans` changed (each span
 * `beforeLo..beforeHi` of the old text became `afterLo..afterHi` of the new): an end before a
 * change stays, one after it moves with it, and one inside it goes to the change's edge — the
 * selection widens to the whole of a change it touches the inside of.
 */
export function carrySelection(spans: readonly ChangedSpan[], start: number, end: number): { start: number; end: number } {
    const sorted = [...spans].sort((a, b) => a.beforeLo - b.beforeLo);
    const carry = (offset: number, isEnd: boolean) => {
        let delta = 0;
        for (const s of sorted) {
            if (offset < s.beforeLo || (offset === s.beforeLo && (!isEnd || s.beforeHi > s.beforeLo))) {
                break;
            }
            if (offset >= s.beforeHi) {
                delta = s.afterHi - s.beforeHi;
                continue;
            }
            return isEnd ? s.afterHi : s.afterLo;
        }
        return offset + delta;
    };
    const s = carry(start, false);
    return { start: s, end: Math.max(s, carry(end, true)) };
}

export function wholeFileEdit<S extends SpanLike>(snapshot: S, full: string): CandidateEdit<S> {
    return reduceToSpan(snapshot, full, null, '');
}

/**
 * The places a candidate changes, word by word: inside each span the AI replaced, the words both
 * sides share (a longest common subsequence) are left out, so a note copied through unchanged is
 * not lit as changed (owner's report 2026-10-25: a rewritten stretch lit every note in it). A
 * side with nothing left is an empty range at the place — lit nowhere. A span too long to compare
 * stays whole past the words its ends share.
 */
export function changedWords(before: string, after: string, spans: readonly ChangedSpan[]): ChangedSpan[] {
    const out: ChangedSpan[] = [];
    for (const s of spans) {
        const a = words(before, s.beforeLo, s.beforeHi);
        const b = words(after, s.afterLo, s.afterHi);
        let p = 0;
        while (p < a.length && p < b.length && a[p].w === b[p].w) p++;
        let q = 0;
        while (q < a.length - p && q < b.length - p && a[a.length - 1 - q].w === b[b.length - 1 - q].w) q++;
        const A = a.slice(p, a.length - q);
        const B = b.slice(p, b.length - q);
        // Where an empty side's range stands: at the next word it keeps, else the span's end.
        const beforeAt = (i: number) => A[i]?.lo ?? a[a.length - q]?.lo ?? s.beforeHi;
        const afterAt = (j: number) => B[j]?.lo ?? b[b.length - q]?.lo ?? s.afterHi;
        const run = (i0: number, i1: number, j0: number, j1: number) => {
            if (i0 === i1 && j0 === j1) return;
            out.push({
                beforeLo: i0 < i1 ? A[i0].lo : beforeAt(i0), beforeHi: i0 < i1 ? A[i1 - 1].hi : beforeAt(i0),
                afterLo: j0 < j1 ? B[j0].lo : afterAt(j0), afterHi: j0 < j1 ? B[j1 - 1].hi : afterAt(j0),
            });
        };
        const n = A.length, m = B.length;
        if (n * m > 4_000_000) {
            run(0, n, 0, m);
            continue;
        }
        // lcs[i * (m + 1) + j]: the longest common subsequence of A[i..] and B[j..].
        const w = m + 1;
        const lcs = new Uint32Array((n + 1) * w);
        for (let i = n - 1; i >= 0; i--) {
            for (let j = m - 1; j >= 0; j--) {
                lcs[i * w + j] = A[i].w === B[j].w
                    ? lcs[(i + 1) * w + j + 1] + 1
                    : Math.max(lcs[(i + 1) * w + j], lcs[i * w + j + 1]);
            }
        }
        let i = 0, j = 0, i0 = 0, j0 = 0;
        while (i < n || j < m) {
            if (i < n && j < m && A[i].w === B[j].w && lcs[i * w + j] === lcs[(i + 1) * w + j + 1] + 1) {
                run(i0, i, j0, j);
                i0 = ++i;
                j0 = ++j;
            } else if (j >= m || (i < n && lcs[(i + 1) * w + j] >= lcs[i * w + j + 1])) {
                i++;
            } else {
                j++;
            }
        }
        run(i0, n, j0, m);
    }
    return out;
}

/**
 * after[lo, hi) — the candidate's replacement text — as HTML with its changes marked, from the
 * changed runs (changedWords): what it dropped of `before` in <del>, what it wrote in <ins>, as the
 * OMR fix panel prints its proposals (owner's request 2026-10-25). The spaces a run reaches back
 * over stay plain.
 */
export function diffMarkup(before: string, after: string, lo: number, hi: number, changed: readonly ChangedSpan[]): string {
    const esc = (s: string) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    let html = '';
    let at = lo;
    for (const s of [...changed].sort((x, y) => x.afterLo - y.afterLo)) {
        if (s.afterHi < at || s.afterLo > hi) continue;
        // A run's lead may reach back before the text (its indentation): clamped to it.
        const from = Math.max(s.afterLo, at);
        const to = Math.min(s.afterHi, hi);
        const start = from + /^\s*/.exec(after.slice(from, to))![0].length;
        html += esc(after.slice(at, start));
        const gone = before.slice(s.beforeLo, s.beforeHi).trimStart();
        if (gone.length > 0) html += `<del>${esc(gone)}</del>`;
        if (gone.length > 0 && start < to) html += ' ';
        if (start < to) html += `<ins>${esc(after.slice(start, to))}</ins>`;
        at = Math.max(at, to);
    }
    return html + esc(after.slice(at, hi));
}

/** The candidate's whole text — the file as Accept would leave it — with its changes marked
 *  (diffMarkup over all of it; owner's request 2026-10-26). */
export function wholeTextMarkup(before: string, after: string, changed: readonly ChangedSpan[]): string {
    return diffMarkup(before, after, 0, after.length, changed);
}

/**
 * The words (runs of non-space) of text[lo, hi), each from the spaces before it: a drawn note's
 * data-pos is its node's FULL-span start — those spaces, and for the first thing on a line its
 * indentation (as omrAiFixCore.rangesOf has it).
 */
function words(text: string, lo: number, hi: number): { w: string; lo: number; hi: number }[] {
    const out: { w: string; lo: number; hi: number }[] = [];
    const re = /\S+/g;
    const part = text.slice(lo, hi);
    for (let m = re.exec(part); m; m = re.exec(part)) {
        let lead = lo + m.index;
        while (lead > 0 && /\s/.test(text[lead - 1])) lead--;
        out.push({ w: m[0], lo: lead, hi: lo + m.index + m[0].length });
    }
    return out;
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
/**
 * Whether the instructions fit the model at all, with room for the file and the answer (owner's
 * request 2026-10-29: the grammar alone is some 30 000 tokens, and a model listed on this machine
 * takes 12 000 — it was sent the request regardless). Unknown limits are taken to fit.
 */
export function promptFits(maxInputTokens: number | undefined, systemTokens: number): boolean {
    return !(maxInputTokens && maxInputTokens > 0) || systemTokens + 12_000 <= Math.floor(maxInputTokens * 0.9);
}

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
    // Nor a model VS Code keeps for one job of its own: `copilot-dictation-cleanup-nano` is
    // listed, small by its name — the ghost's automatic pick — and refuses every extension
    // ("only available to VS Code core"; owner's report 2026-10-25: the ghost never showed).
    // Nor Copilot's "Auto" (id `auto`), which hands each request to a model of Copilot's choosing
    // under another model's family name: picked for its input limit, it composed as
    // "copilot/claude-fable-5.1" and answered as something else (owner's report 2026-10-27 — the
    // composer it wrote was "OpenAI", then "Auto"). Chosen by name, it is still used.
    const chat = models.filter(m => m.family.trim() !== '' && m.vendor !== 'copilotcli' && !isCoreTaskModel(m) && !isAutoModel(m));
    const copilot = chat.filter(m => m.vendor === 'copilot');
    const base = copilot.length > 0 ? copilot : chat.length > 0 ? chat : [...models];
    const small = (m: M) => /mini|nano|lite|haiku|flash|small/i.test(`${m.family} ${m.id}`);
    const pool = base.some(m => small(m) === wantSmall) ? base.filter(m => small(m) === wantSmall) : base;
    return [...pool].sort((a, b) => (b.maxInputTokens ?? 0) - (a.maxInputTokens ?? 0))[0];
}

/** A door that picks the model per request (Copilot's "Auto"): no one model answers through it. */
export function isAutoModel(m: Pick<ModelLike, 'id'>): boolean {
    return m.id.trim().toLowerCase() === 'auto';
}

/**
 * The models Copilot sent an extension's requests to since `since`, in order, from its own log
 * (GitHub Copilot Chat.log: "2026-10-29 09:19:38.601 [info] ccreq:92b4ed0a.copilotmd | success |
 * gpt-6-luna | 55133ms | [copilotLanguageModelWrapper]"). Copilot's Auto does not tell the API
 * which model answered, and the log does (owner's request 2026-10-29: the composed piece's first
 * line said "copilot/auto"). The log's times are local, and a line is written as its request ends.
 */
export function servedModelsIn(log: string, since: Date): string[] {
    const served: string[] = [];
    const line = /^(\d{4})-(\d\d)-(\d\d) (\d\d):(\d\d):(\d\d)\.(\d{3}) \[info\] ccreq:\S+ \| success \| (\S+) \| \d+ms \| \[copilotLanguageModelWrapper\]/;
    for (const text of log.split(/\r?\n/)) {
        const m = line.exec(text);
        if (!m) {
            continue;
        }
        const at = new Date(+m[1], +m[2] - 1, +m[3], +m[4], +m[5], +m[6], +m[7]);
        if (at.getTime() >= since.getTime() && served[served.length - 1] !== m[8]) {
            served.push(m[8]);
        }
    }
    return served;
}

/** A model VS Code lists but keeps for a task of its own (dictation clean-up and the like),
 *  which answers an extension with an error. Known by its name; one that slips through is
 *  dropped when it refuses (modelClient's refusal list). */
export function isCoreTaskModel(m: ModelLike): boolean {
    return /dictation|cleanup/i.test(`${m.id} ${m.family}`);
}

/** Whether an error is the connection's, not the model's or Lily#'s — the request went and the line
 *  broke (owner's report 2026-10-29: "net::ERR_HTTP2_PROTOCOL_ERROR" two minutes into a piece). */
export function isConnectionError(message: string): boolean {
    return /net::ERR_|ECONNRESET|ETIMEDOUT|ECONNREFUSED|socket hang up|network connection|firewall/i.test(message);
}

/** A length of time as a person reads it: "24 s", "2 min 24 s". */
export function spokenDuration(ms: number): string {
    const s = Math.max(0, Math.round(ms / 1000));
    return s < 60 ? `${s} s` : `${Math.floor(s / 60)} min${s % 60 ? ` ${s % 60} s` : ''}`;
}

/**
 * What a failed AI request says, and whether it offers Try Again (owner's request 2026-10-29: the
 * connection's own English, "Please check your firewall rules…", said nothing of what happened).
 * A broken connection says when it broke and offers to ask again; for a piece, a shorter one breaks
 * less often. Anything else says the error as it came.
 */
export function aiFailure(kind: 'compose' | 'ask', message: string, elapsedMs: number): { text: string; retry: boolean } {
    // The service ended the request with no answer (owner's report 2026-10-29: "compose with AI
    // failed: Response contained no choices." — Copilot's log: gpt-6-luna, 164 s, status unknown).
    // Nothing in the request made it so; the same request asked again may well be answered.
    if (isEmptyReply(message)) {
        const after = spokenDuration(elapsedMs);
        return {
            text: kind === 'compose'
                ? `Lily#: the AI ended the request after ${after} without writing anything back — nothing in the request `
                  + 'caused it. Try again — or ask for a shorter piece, which takes the AI less time to write.'
                : `Lily#: the AI ended the request after ${after} without an answer. Try again.`,
            retry: true,
        };
    }
    if (!isConnectionError(message)) {
        return { text: `Lily#: ${kind === 'compose' ? 'compose with AI' : 'Ask AI'} failed: ${message}`, retry: false };
    }
    const after = spokenDuration(elapsedMs);
    return {
        text: kind === 'compose'
            ? `Lily#: the connection to the AI broke off after ${after}, before the piece was written. `
              + 'Try again — or ask for a shorter piece, which takes the AI less time to write.'
            : `Lily#: the connection to the AI broke off after ${after}. Try again.`,
        retry: true,
    };
}

/**
 * The lines a composed piece's warnings are on, each with a `@todo` carrying the warnings' words at
 * the item nearest where they point (owner's request 2026-10-29: a newer version that renders is
 * worth more than an older one with no warnings — shown, and its warnings marked on the score).
 * Lines with nothing to hang a mark on are left out; each line's mark is to be tried on its own,
 * for a line that takes none (a lyric line) would not compile with one. `mark` places the mark
 * (omrAiFixCore.withTodoMark, passed in: that module imports this one).
 */
export function warningTodoLines(text: string, warnings: readonly { Line: number; Char: number; Message: string }[],
    mark: (written: string, bar: string, memo: string) => string | undefined): { line: number; marked: string }[] {
    const lines = text.split('\n');
    const byLine = new Map<number, { Char: number; Message: string }[]>();
    for (const w of warnings) {
        if (w.Line >= 0 && w.Line < lines.length) {
            (byLine.get(w.Line) ?? byLine.set(w.Line, []).get(w.Line)!).push(w);
        }
    }
    const out: { line: number; marked: string }[] = [];
    for (const [line, ws] of byLine) {
        const cr = lines[line].endsWith('\r') ? '\r' : '';
        const raw = lines[line].slice(0, lines[line].length - cr.length);
        const col = Math.min(raw.length, Math.max(0, Math.min(...ws.map(w => w.Char))));
        const words = ws.map(w => w.Message.split(/:\s/)[0]).join(' / ');
        const memo = words.length > 120 ? `${words.slice(0, 119)}…` : words;
        const marked = mark(raw.slice(0, col) + '@todo' + raw.slice(col), raw, memo);
        if (marked !== undefined && marked !== raw) {
            out.push({ line, marked: marked + cr });
        }
    }
    return out;
}

/**
 * The text with a `lyrics NAME` row in each score that shows the part a lyrics track sings but not
 * the track — put on its own line under the part's `staff` item (owner's report 2026-10-29: a
 * composed song's words were written in every section and drawn nowhere). A score that does not
 * show the part is left as it is. Returns the names of the tracks given a row.
 */
export function withLyricsRows(text: string): { text: string; added: string[] } {
    const tracks = new Map<string, string>();   // track → the part it sings
    for (const m of text.matchAll(/^[ \t]*singer\b[^\n]*?\bsings[ \t]+(\w+)[ \t]+on[ \t]+(\w+)/gm)) {
        if (!tracks.has(m[1])) { tracks.set(m[1], m[2]); }
    }
    // …and the part before the words (`singer … on lead sings words`, since 2026-11-12).
    for (const m of text.matchAll(/^[ \t]*singer\b[^\n]*?\bon[ \t]+(\w+)[ \t]+sings[ \t]+(\w+)/gm)) {
        if (!tracks.has(m[2])) { tracks.set(m[2], m[1]); }
    }
    const added: string[] = [];
    if (tracks.size === 0) {
        return { text, added };
    }
    let out = text;
    // Each score block, from its `score` keyword to its closing brace (scores nest groups, not scores).
    for (let from = 0; ;) {
        const head = /^[ \t]*score\b[^{\n]*\{/m.exec(out.slice(from));
        if (!head) { break; }
        const open = from + head.index + head[0].length - 1;
        let depth = 0, close = -1;
        for (let i = open; i < out.length; i++) {
            if (out[i] === '{') { depth++; }
            else if (out[i] === '}' && --depth === 0) { close = i; break; }
        }
        if (close < 0) { break; }
        let body = out.slice(open, close);
        for (const [track, part] of tracks) {
            if (new RegExp(`\\blyrics\\s+${track}\\b`).test(body)) { continue; }
            const staff = new RegExp(`^([ \\t]*)staff\\s+${part}\\b[^\\n]*$`, 'm').exec(body);
            if (!staff) { continue; }
            const at = staff.index + staff[0].length;
            const cr = staff[0].endsWith('\r') ? '\r' : '';
            const lineEnd = cr ? at - 1 : at;
            body = body.slice(0, lineEnd) + `${cr}\n${staff[1]}lyrics ${track}` + body.slice(lineEnd);
            if (!added.includes(track)) { added.push(track); }
        }
        out = out.slice(0, open) + body + out.slice(close);
        from = open + body.length + 1;
    }
    return { text: out, added };
}

/**
 * The proposed text with the comment lines that open the file as it stands, where the proposal left
 * them out (owner's request 2026-10-29: a piece proposed in Copilot's chat dropped "// Composed in
 * Copilot's chat" and "// Asked: …"). Comments the proposal keeps, anywhere, are not added again.
 */
export function withLeadingComments(current: string, proposed: string): string {
    const lines = current.split(/\r?\n/);
    const head: string[] = [];
    for (const line of lines) {
        if (/^\s*\/\//.test(line)) { head.push(line); continue; }
        if (line.trim() === '' && head.length > 0) { continue; }
        break;
    }
    const missing = head.filter(l => !proposed.includes(l.trim()));
    if (missing.length === 0) {
        return proposed;
    }
    const eol = proposed.includes('\r\n') ? '\r\n' : '\n';   // the proposal's own line ends
    return missing.join(eol) + eol + eol + proposed.replace(/^\s*\r?\n/, '');
}

/**
 * The model that called Lily#'s proposeScore in a Copilot CLI session, by its events log
 * (~/.copilot/session-state/<id>/events.jsonl): its display name where the log gives one, else its id;
 * undefined when no such call is in it. The tool is not told which model calls it, and a model set
 * to "auto" does not know which it is (owner's request 2026-10-30: the header to name the model).
 */
export function copilotModelOf(events: string): string | undefined {
    let id: string | undefined;
    const names = new Map<string, string>();
    for (const line of events.split(/\r?\n/)) {
        if (!line.includes('"tool.execution_start"') && !line.includes('modelDisplayName')) { continue; }
        let e: { type?: string; data?: { toolName?: string; model?: string; usage?: { model?: string; modelDisplayName?: string } } };
        try { e = JSON.parse(line); } catch { continue; }
        if (e.type === 'tool.execution_start' && /proposeScore$/.test(e.data?.toolName ?? '') && e.data?.model) {
            id = e.data.model;
        }
        const usage = e.data?.usage;
        if (usage?.model && usage.modelDisplayName) { names.set(usage.model, usage.modelDisplayName); }
    }
    return id === undefined ? undefined : names.get(id) ?? id;
}

/**
 * When the musician last spoke in a Copilot CLI session, by its events log (the newest `user.message`
 * event's timestamp, in ms); undefined when the log has none. A proposal after the chat wrote a new
 * piece is the same request's going on while nothing has been said since (owner's request
 * 2026-11-01: a composed piece opened in the editor, and a panel over it at once — the chat had
 * proposed its piece again, unasked, into the file no longer empty).
 */
export function lastUserMessageAt(events: string): number | undefined {
    let at: number | undefined;
    for (const line of events.split(/\r?\n/)) {
        if (!line.includes('"user.message"')) { continue; }
        let e: { type?: string; timestamp?: string };
        try { e = JSON.parse(line); } catch { continue; }
        if (e.type !== 'user.message' || !e.timestamp) { continue; }
        const t = Date.parse(e.timestamp);
        if (!Number.isNaN(t) && (at === undefined || t > at)) { at = t; }
    }
    return at;
}

/** How to read the review's report, put in front of it (docs/review-tool-design.md §3). */
export const REVIEW_READING = [
    'How to read this report on the score:',
    '- The person may be a beginner. Speak in plain words and explain each musical term the first time you use it.',
    '- If `compiles` is false, the piece cannot be read as music yet: help with `errors` first, and say that the rest of the review waits for them.',
    '- Start with what works (`strengths`). Then raise at most five points, mistakes first, grouped by theme (the key, the rhythm, the parts, the notation). Offer the rest if the person wants them. The compiler\'s `warnings` count among the mistakes.',
    '- For each point: where it is (part and bar; `where.bars` count bars as the form plays them, so a section that returns has later numbers), what you see, why it matters (`why`, in your own words), and what to do (the advice at the end of `what`). `what` is written for a composing AI and uses Lily#\'s own terms: say it for this person.',
    '- Check `fine_when` against the piece and drop a point that does not apply. Where only the person\'s intent can decide, put it as a question. Put a finding of `low` confidence as a question too.',
    '- These are counts, not judgements. The style of the piece decides: jazz uses notes outside the key, a chorale moves its voices together, a minimalist piece repeats its figure.',
    '- Do not change the score. Offer to make the fix, and use lilysharp_proposeScore once the person agrees.',
].join('\n');

/** The music check's finding of a tune held against its harmony, and how many times (owner's
 *  decision 2026-11-01: twelve held clashes were kept "as colour"; this many or more is wrong notes,
 *  turned down from the chat every time). */
export function heldClashesOf(findings: string[]): { finding: string; count: number } | undefined {
    for (const finding of findings) {
        const m = /^[^,]+, the melody: (\d+) times a note held on the beat stands a semitone from the harmony/.exec(finding);
        if (m) { return { finding, count: Number(m[1]) }; }
    }
    return undefined;
}

/** The music check's findings of two singers in octaves or unison over a share of the moves they
 *  make together that is no passage: a fifth of them or more (owner's decision 2026-11-01: told of
 *  30 in 131, a chorus kept them "for ease of singing"), and short of a piece sung in unison, four
 *  fifths, which a request may mean. */
export function singersInOneLineOf(findings: string[]): string[] {
    return findings.filter(f => {
        const m = /^\S+ and \S+ move in octaves or unison (\d+) times of the (\d+) they move together/.exec(f);
        if (!m) { return false; }
        const share = Number(m[1]) / Number(m[2]);
        return share >= 0.2 && share < 0.8;
    });
}

/**
 * The findings that a melody is no tune at all: one note value and in leaps (`melody.no-tune`). A
 * revision that keeps it goes back once more (owner's decision 2026-11-02: 雨の窓辺 kept both points
 * "for the rain", its note saying it had added short notes, its rhythm unchanged in every bar).
 */
export function noTuneOf(findings: string[]): string[] {
    return findings.filter(f => /, the melody: no tune — /.test(f));
}

/** Held clashes this many or more: turned down, not told (heldClashesOf). */
export const CLASHES_TURNED_DOWN = 8;

/** The compiler's warnings of a chord no hand can hold on a fretted instrument (LYS1055; a bowed
 *  string's "cannot be bowed" is another matter). Owner's decision 2026-11-01: a solo guitar's 14 were
 *  written in, the chat saying only that some chords are hard to hold. */
export function unfingerableOf<T extends { Code?: string | null; Message: string }>(diags: readonly T[]): T[] {
    return diags.filter(d => d.Code === 'LYS1055' && d.Message.includes('cannot be fingered'));
}

/** Chords no hand can hold this many or more: turned down, not told (unfingerableOf). */
export const UNFINGERABLE_TURNED_DOWN = 8;

/** The text with the model named in its "// Composed in Copilot's chat" line (copilotModelOf). */
export function withModelNamed(text: string, model: string): string {
    return text.replace(/^(\s*\/\/ Composed in Copilot's chat)(?: \([^)\r\n]*\))?,/m, `$1 (${model}),`);
}

/** Whether a file holds no music yet — only comments, blank lines and the opening `octave` / `pitch`
 *  lines: a proposal for it is a new piece (the composition check is for one). */
export function isEmptyScore(text: string): boolean {
    return text.split(/\r?\n/).every(l => /^\s*(\/\/.*|octave\s+\w+|pitch\s+\w+)?\s*$/.test(l));
}

/** The request a piece composed in Copilot's chat says it was written for — its header's
 *  `// Asked: …` line, among the comments it opens with — or undefined for a text that does not
 *  open with that header. */
export function chatPieceRequest(text: string): string | undefined {
    const head: string[] = [];
    for (const line of text.split(/\r?\n/)) {
        if (/^\s*\/\//.test(line)) { head.push(line.trim()); continue; }
        if (line.trim() === '' && head.length === 0) { continue; }
        break;
    }
    if (!head.some(l => l.startsWith("// Composed in Copilot's chat"))) { return undefined; }
    const asked = head.find(l => l.startsWith('// Asked:'));
    return asked === undefined ? undefined : asked.slice('// Asked:'.length).trim();
}

/**
 * True when a proposal is a NEW piece composed in the chat — its header names a request — sent to
 * a file that holds another score (owner's request 2026-11-07: asked to compose with a score open,
 * the chat proposed the new piece over it). A file that is empty takes it, and so does the piece's
 * own file: a revision keeps the request it was composed for.
 */
export function newPieceOverScore(current: string, proposed: string): boolean {
    const asked = chatPieceRequest(proposed);
    if (asked === undefined || isEmptyScore(current)) { return false; }
    return chatPieceRequest(current) !== asked;
}

/** The choices a starting point is drawn from (compositionHint). The key is its tonic alone, major
 *  or minor as the music asks (owner's report 2026-10-29: two "bright waltzes" drew C minor and F♯
 *  minor, set the draw aside as the wrong mode, and fell back on G major both times). */
const HINT_TONICS = ['C', 'G', 'D', 'A', 'E', 'B', 'F♯', 'D♭', 'A♭', 'E♭', 'B♭', 'F'];

/** A drawn tonic's minor as it is written: D♭ minor and A♭ minor have eight and seven flats, a
 *  double flat in the signature, so their minors are C♯ and G♯ (owner's review 2026-10-30: drawn
 *  D♭, a rock trio was written in D♭ minor, its every A a B𝄫). */
const MINOR_SPELLING: Record<string, string> = { 'D♭': 'C♯', 'A♭': 'G♯' };
const minorOf = (tonic: string) => MINOR_SPELLING[tonic] ?? tonic;
// Weighted: 4/4 most of the time, 3/4 seldom (owner's request 2026-10-29: rock and pop are nearly
// always in 4/4; a three is to come only now and then).
const HINT_METERS = ['4/4', '4/4', '4/4', '4/4', '4/4', '4/4', '6/8', '12/8', '2/4', '3/4'];
const HINT_FORMS = ['A–B–A', 'two halves (A–B)', 'a rondo (A–B–A–C–A)', 'a theme and two variations',
    'an introduction, A–B–A and a coda', 'one section that grows from start to end'];
const HINT_OPENINGS = ['the bass alone for a bar or two', 'a pickup into the first bar', 'the theme in the lowest part',
    'a soft chord held, the melody entering over it', 'everyone at once, loud', 'a short introduction on the accompaniment\'s figure'];

/**
 * Compiler diagnostics as lines for the AI, one per line of the source and message — the same
 * message on the same line said once, with how many times (owner's report 2026-10-29: "this note
 * sounds D2, below the range of the Guitar" came back 28 times, one a note, burying the rest).
 * The first fix any of them offers goes with it.
 */
export function diagLines<D extends { Line: number; Message: string }>(diags: D[], fixOf: (d: D) => string): string {
    const groups = new Map<string, { line: number; message: string; count: number; fix: string }>();
    for (const d of diags) {
        const key = `${d.Line}\u0000${d.Message}`;
        const g = groups.get(key) ?? { line: d.Line, message: d.Message, count: 0, fix: '' };
        g.count++;
        g.fix ||= fixOf(d);
        groups.set(key, g);
    }
    return [...groups.values()]
        .map(g => `  - line ${g.line + 1}${g.count > 1 ? ` (${g.count} times)` : ''}: ${g.message}${g.fix}`)
        .join('\n');
}

/**
 * What to say when a new piece's "// The AI:" line is not in the language of its "// Asked:" line —
 * told by their scripts, Latin or not (owner's report 2026-10-29: asked in Japanese, the chat
 * described its piece in English). Undefined when they agree, or a line is missing.
 */
export function headerLanguageNote(text: string): string | undefined {
    const asked = /^\/\/ Asked: (.*)$/m.exec(text)?.[1];
    const said = /^\/\/ The AI: (.*)$/m.exec(text)?.[1];
    if (!asked?.trim() || !said?.trim()) {
        return undefined;
    }
    const otherScript = (s: string) => /[^\p{Script=Latin}\p{Script=Common}\p{Script=Inherited}]/u.test(s);
    return otherScript(asked) === otherScript(said) ? undefined
        : 'The header\'s "// The AI:" line is not in the language of the request on its "// Asked:" line: write it in '
          + 'the request\'s language.';
}

/** The synth a candidate panel plays with (AiTransformDeps.synthFor). */
export interface CandidateSynth { script: string; processor: string; soundFont: string; midiOutput: string }

/** Whether a candidate panel's page has the synth: its bundle, its processor and a sound font. */
export function hasSynth(synth: CandidateSynth | undefined): boolean {
    return !!synth && synth.script.length > 0 && synth.processor.length > 0 && synth.soundFont.length > 0;
}

/** Whether a candidate panel sounds its own Play on that synth: it has one, and no MIDI output is
 *  chosen (the port then plays, asked of the server by the host). */
export function playsItself(synth: CandidateSynth | undefined): boolean {
    return hasSynth(synth) && synth!.midiOutput.length === 0;
}

/** A voice a candidate's Play sings: its part, the WAV, and how long the WAV runs before the
 *  score's 0 (the first consonant's room, VOICE_LEAD_S). */
export interface CandidateVoice {
    part: string; bytes: Uint8Array; lead: number;
    /** The track whose instrument the voice replaces (candidateSilencedTrack), or -1. */
    track: number;
    /** Where the voice may come in while its part is already playing (voiceEntries). */
    entries: VoiceEntry[];
}

/** What the engine has for a candidate's Play (AiTransformDeps.voicesFor): the voices there now,
 *  the tracks (indices in the playback's Parts) whose instrument gives way to them, and the parts
 *  whose voice is still being sung — their instruments play until it is there (the panel takes
 *  it in after the part's next rest, or the next Play sings it). `notSung`: the voices nobody
 *  installed can sing, by key - their sections are the instrument's where another line of the
 *  part sings (singerCore.leftToTheInstrument). */
export interface CandidateVoices { voices: CandidateVoice[]; silenced: number[]; waiting: string[]; notSung?: string[] }

/** Which parts a played text sings and plays, as `lilysharp/playbackText` answers. */
export interface CandidateSounds { Parts?: string[] | null; Sung?: string[] | null; Played?: string[] | null }

/**
 * Of a text's voices (`lilysharp/sung`), the ones a candidate's Play sings: the score's own, with
 * notes, of a part the score sings. The practice menu's switches are the preview's and are not read
 * here: the panel plays the score as written.
 */
export function candidateSings<V extends { Part: string; InScore?: boolean; Notes: readonly unknown[] }>(
    voices: readonly V[], sounds: CandidateSounds): V[] {
    const sung = new Set(sounds.Sung ?? []);
    return voices.filter(v => v.Notes.length > 0 && v.InScore !== false && sung.has(v.Part));
}

/** The track whose instrument a part's voice replaces, or -1: a part the score only sings (one it
 *  plays too sounds both ways — the export's rule, extension.ts). */
export function candidateSilencedTrack(part: string, sounds: CandidateSounds): number {
    if (!(sounds.Sung ?? []).includes(part) || (sounds.Played ?? []).includes(part)) { return -1; }
    return (sounds.Parts ?? []).indexOf(part);
}

/** What the panel says of the voices not there yet, or undefined with none waiting. */
export function candidateWaitingNote(waiting: readonly string[]): string | undefined {
    if (waiting.length === 0) { return undefined; }
    return waiting.length === 1
        ? `The voice of ${waiting[0]} is being sung: its instrument plays until it is ready.`
        : `The voices of ${waiting.join(', ')} are being sung: their instruments play until they are ready.`;
}
/**
 * What each part of a paged guide is about, a line a part: its headings (`#`, `##`). For the first
 * part of the grammar (owner's run 2026-11-11: to add a strings pad to a score, the chat read all
 * fourteen parts of it, 100 KB and over a minute, each part having said "read the next" and none
 * what the next held).
 */
export function guideContents(pages: string[]): string {
    return pages.map((page, i) => {
        const heads = [...page.matchAll(/^#{1,2} +(.+)$/gm)].map(m => m[1].replace(/`/g, '').replace(/\s*\(.*$/, '').trim()).filter(h => h.length > 0);
        return `- part ${i + 1}: ${heads.length > 0 ? heads.join('; ') : '(goes on)'}`;
    }).join('\n');
}

/**
 * A guide cut into parts of at most `limit` UTF-8 bytes, at blank lines where it can be (a
 * paragraph, a list item, a code block whole), else at line ends. For Copilot's chat (owner's
 * report 2026-10-29): its agent keeps a tool's answer over about 8 KB in a temp file and reads that
 * back by its own means — once with PowerShell, asking the musician "Run in terminal?" mid-compose.
 * The first part may be given a smaller limit, to leave room for what goes before it.
 */
export function guidePages(text: string, limit: number, firstLimit: number = limit): string[] {
    const bytes = (s: string) => Buffer.byteLength(s, 'utf8');
    const pages: string[] = [];
    let page = '';
    const add = (piece: string, sep: string) => {
        if (page && bytes(page) + bytes(sep) + bytes(piece) > (pages.length === 0 ? firstLimit : limit)) {
            pages.push(page);
            page = '';
        }
        page = page ? page + sep + piece : piece;
    };
    // A code block holding a blank line is one piece: cut at that line it reaches the model as
    // two halves of an example, the second not even marked as one.
    const blocks: string[] = [];
    let inFence = false;
    for (const piece of text.replace(/\r\n/g, '\n').split(/\n{2,}/)) {
        if (inFence) { blocks[blocks.length - 1] += '\n\n' + piece; }
        else { blocks.push(piece); }
        if ((piece.match(/```/g) ?? []).length % 2 === 1) { inFence = !inFence; }
    }
    for (const block of blocks) {
        if (bytes(block) <= Math.min(limit, firstLimit)) {
            add(block, '\n\n');
            continue;
        }
        // Too long to keep whole: line by line, from a page of its own.
        if (page) {
            pages.push(page);
            page = '';
        }
        for (const line of block.split('\n')) {
            add(line, '\n');
        }
    }
    if (page.trim()) {
        pages.push(page);
    }
    return pages;
}

/**
 * A starting point drawn at random, for a piece to begin somewhere other than the model's habit
 * (owner's request 2026-10-29: six "dramatic string quartets" were all D minor, 4/4, A–B–A, and
 * opened on the same A–D–F–E–D). A hint, not a rule: the request comes first, and the model takes
 * what suits the piece. `random` gives numbers in [0, 1) (Math.random; a test passes its own).
 */
export function compositionHint(random: () => number = Math.random): string {
    const pick = <T>(xs: readonly T[]) => xs[Math.min(xs.length - 1, Math.floor(random() * xs.length))];
    const tonic = pick(HINT_TONICS);
    const spelled = minorOf(tonic) === tonic ? '' : ` (its minor written as ${minorOf(tonic)} minor)`;
    // Followed wherever the request is silent, and not spoken of (owner's decision 2026-10-30: told to
    // take what suits, the AI left the drawn key for G or F in every gentle piano piece; told to say
    // why, it would speak to the musician of a starting point they never saw).
    return `A starting point for this piece, drawn at random so that not every piece begins alike — write the piece `
        + `so wherever the request itself says nothing of it: whatever the request says (a key, a meter, a form, a length, `
        + `a style) comes first, and a band's music — rock, pop — keeps its 4/4 whatever meter is drawn; it is yours `
        + `alone, unseen by the musician — do not speak of it: `
        + `the key on ${tonic}, major or minor as the music asks${spelled}; ${pick(HINT_METERS)}; ${pick(HINT_FORMS)}; `
        + `opening with ${pick(HINT_OPENINGS)}.`;
}

/** The tonic a starting point drew ("E", "F♯", "B♭"), or undefined for a text with none. */
export function hintTonic(hint: string): string | undefined {
    return /the key on ([A-G][♯♭]?), major or minor/.exec(hint)?.[1];
}

/**
 * Said with a new piece's first check when its key is not on the tonic drawn for it, and the
 * request names no key (owner's decision 2026-10-30: the drawn key was left for G or F in every
 * gentle piano piece). Said to the AI alone: the musician never saw the draw. Enharmonic keys
 * (G♭ for F♯) are the same key.
 */
export function drawnKeyNote(text: string, tonic: string | undefined): string | undefined {
    const key = /^[ \t]*key[ \t]+([a-g](?:is|es|s)*)[ \t]+\w+/m.exec(text);
    if (!tonic || !key) {
        return undefined;
    }
    const steps: Record<string, number> = { c: 0, d: 2, e: 4, f: 5, g: 7, a: 9, b: 11 };
    const name = key[1];
    const written = steps[name[0]] + (name.match(/is/g)?.length ?? 0) - (name.slice(1).replace(/is/g, '').match(/e?s/g)?.length ?? 0);
    const drawn = steps[tonic[0].toLowerCase()] + (tonic.includes('♯') ? 1 : 0) - (tonic.includes('♭') ? 1 : 0);
    if ((written - drawn) % 12 === 0) {
        return undefined;
    }
    const asked = /^\/\/ Asked:(.*)$/m.exec(text)?.[1] ?? '';
    if (/\b[A-G][#♯b♭]?[- ]?(major|minor|dur|moll)\b|[ハニホヘトイロ][嬰変]?[長短]調|[A-G][#♯b♭]?(メジャー|マイナー)|\bkey\b|調で/i.test(asked)) {
        return undefined;
    }
    return `The key: write the piece on ${tonic} (${tonic} major or ${minorOf(tonic)} minor, as the music asks), the starting point drawn for `
        + 'it — the request names no key.';
}

/** Whether an error says the service answered with nothing ("Response contained no choices"). */
export function isEmptyReply(message: string): boolean {
    return /contained no choices|empty response|no response (was )?returned/i.test(message);
}

/** Whether an error says the model is not an extension's to use. */
export function isCoreOnlyRefusal(message: string): boolean {
    return /only available to VS Code core/i.test(message);
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
export function startTimeAt(notes: { S?: number; T?: number }[], pos: number): number;
/**
 * …or from a selected BAR, whose written stretch is [pos, end) (owner's request 2026-10-25):
 * the first time a note written in it sounds — a bar opening on a rest starts on its first
 * note — else as from `pos` alone.
 */
export function startTimeAt(notes: { S?: number; T?: number }[], pos: number, end: number): number;
export function startTimeAt(notes: { S?: number; T?: number }[], pos: number, end?: number): number {
    if (end !== undefined) {
        let first: number | undefined;
        for (const n of notes) {
            const s = n.S ?? -1, t = n.T ?? 0;
            if (s >= pos && s < end && (first === undefined || t < first)) { first = t; }
        }
        if (first !== undefined) { return first; }
    }
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

/**
 * For a note outside its instrument's range (LYS1052): the note as the model wrote it, and the
 * same note an octave nearer the range — " — you wrote `c,8`: write `c8`". Owner's report
 * 2026-10-27: "this note sounds C1, below the range of the Bass" came back thirteen times through
 * every repair; the sounding pitch alone does not say which written note it is, nor which way its
 * marks go in a part with an anchor and a transposition of its own. "" for any other report.
 */
export function rangeFixOf(d: { Code: string | null; Offset: number; Message: string }, text: string): string {
    if (d.Code !== 'LYS1052') {
        return '';
    }
    const m = /^([a-g](?:isis|eses|is|es|s)?)([,']*)(\S*)/.exec(text.slice(d.Offset));
    if (!m) {
        return '';
    }
    const [, name, marks, rest] = m;
    const tail = rest.split(/[\s<>|~(){}\[\]]/)[0];
    const up = /\bbelow the range\b/.test(d.Message);
    const moved = up ? (marks.includes(',') ? marks.slice(1) : marks + "'") : (marks.includes("'") ? marks.slice(1) : marks + ',');
    return ` — you wrote \`${name}${marks}${tail}\`: write it an octave ${up ? 'higher' : 'lower'}, \`${name}${moved}${tail}\``;
}

/** A change to a piece's draft: `old`, found once in the draft, becomes `new`. */
export interface DraftEdit {
    old: string;
    new: string;
}

/** What the chat hands the draft tool: text to append, edits to make, or both; `restart` begins again. */
export interface DraftInput {
    file: string;
    text?: string;
    edits?: DraftEdit[];
    restart?: boolean;
}

/**
 * A new piece's draft after one call of the draft tool (owner's report 2026-11-08: a choir piece
 * written in one reply ran past ten minutes, was cut off three times and lost each time — the piece
 * is written into a draft a part at a time, unseen and unchecked, and proposed whole at the end).
 * The edits first, each `old` found exactly once in the draft as it stands — else nothing changes and
 * the reason goes back — then `text` appended, on a line of its own.
 */
export function applyDraft(current: string | undefined, input: DraftInput): { text: string } | { error: string } {
    const edits = Array.isArray(input.edits) ? input.edits : [];
    const append = typeof input.text === 'string' ? input.text : '';
    if (edits.length === 0 && append.length === 0) {
        return { error: 'Nothing changed: give `text` to add to the draft, or `edits` to change it.' };
    }
    let draft = input.restart ? '' : current ?? '';
    for (const [i, edit] of edits.entries()) {
        if (!edit || typeof edit.old !== 'string' || typeof edit.new !== 'string' || edit.old.length === 0) {
            return { error: `Nothing changed: edit ${i + 1} needs \`old\` (text in the draft) and \`new\`.` };
        }
        const at = draft.indexOf(edit.old);
        if (at < 0) {
            return { error: `Nothing changed: edit ${i + 1}'s \`old\` is not in the draft — copy it exactly as the draft has it.` };
        }
        if (draft.indexOf(edit.old, at + 1) >= 0) {
            return { error: `Nothing changed: edit ${i + 1}'s \`old\` is in the draft more than once — give a longer one, found once.` };
        }
        draft = draft.slice(0, at) + edit.new + draft.slice(at + edit.old.length);
    }
    if (append.length > 0) {
        draft = draft.length === 0 || draft.endsWith('\n') ? draft + append : `${draft}\n${append}`;
        if (!draft.endsWith('\n')) {
            draft += '\n';
        }
    }
    return { text: draft };
}

/**
 * What a draft holds so far, for the chat, and its next step alone (owner's trials 2026-11-08: told
 * the whole way at the start, a model composed the whole piece in thought before its plan and was cut
 * off — each answer now says only what comes next, and the step stays short): mended after a
 * proposal, propose it again; no section yet, the first; form and score in, propose it; else the next
 * section of the plan, its section notes first.
 */
export function draftSummary(name: string, draft: string, mended = false): string {
    const lines = draft.split('\n').filter(l => l.trim().length > 0).length;
    const sections = [...draft.matchAll(/^\s*section\s+([A-Za-z_][\w-]*)/gm)].map(m => m[1]);
    const form = /^\s*form\b/m.test(draft);
    const score = /^\s*score\s*\{/m.test(draft);
    const holds = `Draft of ${name}: ${lines} lines`
        + (sections.length > 0 ? `, sections ${sections.join(', ')}` : ', no section yet')
        + `; form ${form ? 'written' : 'not yet'}, score ${score ? 'written' : 'not yet'}. `
        + 'Not shown to the musician and not compiled. ';
    // The form names its sections: those not drafted yet are named.
    const formLine = /^\s*form\s*\{([^}]*)\}/m.exec(draft)?.[1] ?? '';
    const missing = [...new Set(formLine.split(/\s+/).map(s => s.replace(/^~/, '')).filter(s => /^[A-Za-z_][\w-]*$/.test(s)))]
        .filter(s => !sections.includes(s));
    let next: string;
    if (mended) {
        next = 'Next: propose it again with lilysharp_proposeScore, this file and `draft: true`.';
    } else if (form && score && missing.length === 0 && sections.length > 0) {
        next = 'The piece looks whole. Next: propose it with lilysharp_proposeScore, giving this file and `draft: true` — no text.';
    } else if (missing.length > 0) {
        next = `Next: section ${missing[0]} alone — its section notes in your reply (its chords bar by bar, its phrases' notes`
            + `, its words written out and counted in a song), then that section with lilysharp_draftScore. Think of no other section in this step.`;
    } else if (sections.length === 0) {
        next = 'Next: the first section of your plan alone — its section notes in your reply (its chords bar by bar, its '
            + 'phrases\' notes, its words written out and counted in a song), then that section with lilysharp_draftScore. '
            + 'Think of no other section in this step.';
    } else {
        next = 'Next: the section your plan has after these, alone — its section notes in your reply, then that section with '
            + 'lilysharp_draftScore; think of no other section in this step. When every section is in, add the form and the score.';
    }
    // The limit said again where a section's thinking begins (owner's request 2026-11-08).
    const limit = next.includes('section notes') ? ' Answer within five minutes, thinking included: a longer think is cut off by the connection and lost.' : '';
    return holds + next + limit + ' Mend what is there with `edits`.';
}

/** Added to what proposing a draft says, where it asks for the piece again: mend the draft, not a whole new text. */
export const FROM_DRAFT_NOTE = '\n\nThis was proposed from your draft. Where this says to call lilysharp_proposeScore again with the '
    + 'whole text, mend the draft instead with lilysharp_draftScore\'s `edits` — only what changes, not the piece again — '
    + 'and call lilysharp_proposeScore with `draft: true`.';


/** A change to a score, proposed as an edit: `old`, as lilysharp_readScore shows it, becomes `new` —
 *  found once, or with `all` wherever it stands. */
export interface ScoreEdit {
    old: string;
    new: string;
    all?: boolean;
}

/**
 * A score after the chat's edits (owner's report 2026-11-11: thirty chords of a guitar part, eight
 * different ones, were changed by writing all 435 lines of the file again — three minutes of a
 * twelve-minute answer). Each `old` is text of the score as the reading tool showed it; it must be
 * there exactly once, or any number of times with `all`. The edits apply in order, each to what the
 * one before left. Nothing changes unless all apply: the reason goes back instead.
 *
 * With `part`, only that part's text is changed — its own block, its music in each section and
 * the phrases it calls, what lilysharp_readScore shows of it (owner's report 2026-11-11: having
 * read the guitar alone, the chat read the whole score in two pages before sending `all: true`
 * edits, to see that no other part held the same chord).
 */
export function applyScoreEdits(base: string, edits: ScoreEdit[], part?: string): { text: string; changed: number } | { error: string } {
    const eol = base.includes('\r\n') ? '\r\n' : '\n';
    const sameEol = (s: string) => s.replace(/\r?\n/g, eol);
    let text = base;
    let changed = 0;
    const scoped = typeof part === 'string' && part.trim().length > 0 ? part.trim() : undefined;
    if (scoped !== undefined) {
        if (scorePartSpans(base, scoped) === undefined) {
            return { error: `Nothing shown: the score has no part \`${scoped}\` for the edits to keep to.` };
        }
        for (const [i, edit] of edits.entries()) {
            if (!edit || typeof edit.old !== 'string' || typeof edit.new !== 'string' || edit.old.length === 0) {
                return { error: `Nothing shown: edit ${i + 1} needs \`old\` (text of the score as lilysharp_readScore shows it) and \`new\`.` };
            }
            const old = sameEol(edit.old);
            const neu = sameEol(edit.new);
            // The part's text as the edits before left it: a place counts when it lies wholly in it.
            const spans = scorePartSpans(text, scoped) ?? [];
            const places: number[] = [];
            for (let at = text.indexOf(old); at >= 0; at = text.indexOf(old, at + old.length)) {
                if (spans.some(([a, z]) => a <= at && at + old.length <= z)) { places.push(at); }
            }
            if (places.length === 0) {
                return { error: `Nothing shown: edit ${i + 1}'s \`old\` is not in part \`${scoped}\` — copy it exactly as lilysharp_readScore `
                    + 'shows that part (the notes in the octaves shown there), a whole chord or a whole line at a time.' };
            }
            if (places.length > 1 && edit.all !== true) {
                return { error: `Nothing shown: edit ${i + 1}'s \`old\` is in part \`${scoped}\` ${places.length} times — give \`all: true\` to change every one, `
                    + 'or more of its line, found once.' };
            }
            for (const at of places.reverse()) { text = text.slice(0, at) + neu + text.slice(at + old.length); }
            changed += places.length;
        }
        return text === base ? { error: 'Nothing shown: the edits leave the score as it is.' } : { text, changed };
    }
    for (const [i, edit] of edits.entries()) {
        if (!edit || typeof edit.old !== 'string' || typeof edit.new !== 'string' || edit.old.length === 0) {
            return { error: `Nothing shown: edit ${i + 1} needs \`old\` (text of the score as lilysharp_readScore shows it) and \`new\`.` };
        }
        const old = sameEol(edit.old);
        const neu = sameEol(edit.new);
        let count = 0;
        for (let at = text.indexOf(old); at >= 0; at = text.indexOf(old, at + old.length)) { count++; }
        if (count === 0) {
            return { error: `Nothing shown: edit ${i + 1}'s \`old\` is not in the score — copy it exactly as lilysharp_readScore shows it `
                + '(the notes in the octaves shown there), a whole chord or a whole line at a time.' };
        }
        if (count > 1 && edit.all !== true) {
            return { error: `Nothing shown: edit ${i + 1}'s \`old\` is in the score ${count} times — give \`all: true\` to change every one, `
                + 'or more of its line, found once.' };
        }
        text = text.split(old).join(neu);
        changed += count;
    }
    return text === base ? { error: 'Nothing shown: the edits leave the score as it is.' } : { text, changed };
}

/** A `{ … }` block of a score: its header (the line's text before the brace), where it stands and
 *  the blocks inside it. */
interface ScoreBlock { header: string; open: number; close: number; inner: ScoreBlock[] }

/** The blocks of a score's text, comments and strings skipped: top level, and one level inside. */
function scoreBlocks(text: string): ScoreBlock[] {
    const top: ScoreBlock[] = [];
    const stack: ScoreBlock[] = [];
    // Where the statement a brace opens began: after the last `{`, `}` or line break before it.
    let from = 0;
    for (let i = 0; i < text.length; i++) {
        const c = text[i];
        if (c === '/' && text[i + 1] === '/') {
            const end = text.indexOf('\n', i);
            i = end < 0 ? text.length : end;
            from = i + 1;
        } else if (c === '"') {
            const end = text.indexOf('"', i + 1);
            i = end < 0 ? text.length : end;
        } else if (c === '\n') {
            from = i + 1;
        } else if (c === '{') {
            const block: ScoreBlock = { header: text.slice(from, i).trim(), open: i, close: text.length, inner: [] };
            if (stack.length === 0) { top.push(block); } else if (stack.length === 1) { stack[0].inner.push(block); }
            stack.push(block);
            from = i + 1;
        } else if (c === '}') {
            const block = stack.pop();
            if (block) { block.close = i; }
            from = i + 1;
        }
    }
    return top;
}

/** Where a part's text stands in a score, as offsets: its own block, its block in each section
 *  and the named blocks (phrases) it calls — what {@link scorePartView} shows of it. Undefined
 *  when the score has no such part. */
export function scorePartSpans(text: string, part: string): [number, number][] | undefined {
    const name = part.trim();
    if (!/^[A-Za-z_][\w-]*$/.test(name)) {
        return undefined;
    }
    const isPart = (b: ScoreBlock) => new RegExp(`^part\\s+${name}\\b`).test(b.header);
    const isMine = (b: ScoreBlock) => b.header === name;
    const blocks = scoreBlocks(text);
    const own = blocks.filter(b => isPart(b) || b.inner.some(isMine));
    if (own.length === 0) {
        return undefined;
    }
    const music = own.map(b => isPart(b) ? text.slice(b.open, b.close) : b.inner.filter(isMine).map(i => text.slice(i.open, i.close)).join(' ')).join(' ');
    const spans: [number, number][] = [];
    // A block with its header: from after the brace or line break before it.
    const from = (open: number) => 1 + Math.max(text.lastIndexOf('\n', open - 1), text.lastIndexOf('{', open - 1), text.lastIndexOf('}', open - 1));
    for (const b of blocks) {
        const m = /^([a-z]\w*)\s+([A-Za-z_]\w*)$/.exec(b.header);
        const called = m !== null && !/^(part|section|score|form|lyrics|chords|layout|paper|fonts|header|singer)$/.test(m[1])
            && new RegExp(`(^|[^\\w'])${m[2]}(?![\\w'])`).test(music);
        if (isPart(b) || called) {
            spans.push([from(b.open), b.close + 1]);
        } else {
            for (const inner of b.inner.filter(isMine)) { spans.push([from(inner.open), inner.close + 1]); }
        }
    }
    return spans;
}

/**
 * One part's share of a score, for the chat to read without the rest (owner's report 2026-11-11:
 * to change the guitar, the chat read a 12.8 KB score kept in a temp file thirteen times over):
 * the lines before the first block (title, tempo, meter, key), the part's own block, its block in
 * each section — the section's other lines left out, marked `…` — and the named blocks (phrases)
 * it calls. Each block is headed `[lines a-b]`, where it stands in the file. Undefined when the
 * score has no such part.
 */
export function scorePartView(text: string, part: string): string | undefined {
    const name = part.trim();
    if (!/^[A-Za-z_][\w-]*$/.test(name)) {
        return undefined;
    }
    const lines = text.split('\n');
    const starts: number[] = [];
    for (let at = 0, i = 0; i < lines.length; at += lines[i].length + 1, i++) { starts.push(at); }
    const lineOf = (offset: number) => {
        let lo = 0, hi = starts.length - 1;
        while (lo < hi) { const mid = (lo + hi + 1) >> 1; if (starts[mid] <= offset) { lo = mid; } else { hi = mid - 1; } }
        return lo;
    };
    const isPart = (b: ScoreBlock) => new RegExp(`^part\\s+${name}\\b`).test(b.header);
    const isMine = (b: ScoreBlock) => b.header === name;
    const blocks = scoreBlocks(text);
    const own = blocks.filter(b => isPart(b) || b.inner.some(isMine));
    if (own.length === 0) {
        return undefined;
    }
    // What the part's music calls: the named blocks whose name stands in it as a word.
    const music = own.map(b => isPart(b) ? text.slice(b.open, b.close) : b.inner.filter(isMine).map(i => text.slice(i.open, i.close)).join(' ')).join(' ');
    const called = (b: ScoreBlock) => {
        const m = /^([a-z]\w*)\s+([A-Za-z_]\w*)$/.exec(b.header);
        return m !== null && !/^(part|section|score|form|lyrics|chords|layout|paper|fonts|header|singer)$/.test(m[1])
            && new RegExp(`(^|[^\\w'])${m[2]}(?![\\w'])`).test(music);
    };
    const out: string[] = [];
    // Whole blocks that follow one another stand under one heading.
    let run: { a: number; z: number; at: number } | undefined;
    const whole = (a: number, z: number) => {
        const body = lines.slice(a, z + 1).join('\n');
        if (run && run.z + 1 === a) {
            run.z = z;
            out[run.at] = `[lines ${run.a + 1}-${z + 1}]`;
            out.push(body);
        } else {
            run = { a, z, at: out.length };
            out.push(a === z ? `[line ${a + 1}]` : `[lines ${a + 1}-${z + 1}]`, body);
        }
    };
    const first = blocks.length > 0 ? lineOf(blocks[0].open) : lines.length;
    const head = lines.slice(0, first).join('\n').replace(/\s+$/, '');
    if (head.length > 0) { out.push(`[lines 1-${first}]`, head); }
    for (const b of blocks) {
        const a = lineOf(b.open), z = lineOf(b.close);
        if (isPart(b) || called(b)) {
            whole(a, z);
        } else if (b.inner.some(isMine)) {
            run = undefined;
            out.push(`[lines ${a + 1}-${z + 1}]`, lines[a]);
            let at = a + 1;
            for (const inner of b.inner.filter(isMine)) {
                const ia = lineOf(inner.open), iz = lineOf(inner.close);
                if (ia > at) { out.push('  …'); }
                out.push(lines.slice(Math.max(ia, at), iz + 1).join('\n'));
                at = iz + 1;
            }
            if (z > at) { out.push('  …'); }
            if (z >= at) { out.push(lines[z]); }
        }
    }
    return out.join('\n');
}

/** A text in parts of at most `maxBytes` (UTF-8), cut at line ends — at an empty line where one
 *  stands in the part's second half — so that each is read whole in a tool's answer. */
export function scorePages(text: string, maxBytes: number): string[] {
    const size = (s: string) => Buffer.byteLength(s, 'utf8');
    if (size(text) <= maxBytes) {
        return [text];
    }
    const pages: string[] = [];
    let page: string[] = [];
    let bytes = 0;
    for (const line of text.split('\n')) {
        const n = size(line) + 1;
        if (page.length > 0 && bytes + n > maxBytes) {
            let cut = page.length;
            for (let k = page.length - 1; k > page.length / 2; k--) {
                if (page[k].trim().length === 0) { cut = k + 1; break; }
            }
            pages.push(page.slice(0, cut).join('\n'));
            page = page.slice(cut);
            bytes = page.reduce((sum, l) => sum + size(l) + 1, 0);
        }
        page.push(line);
        bytes += n;
    }
    if (page.length > 0) { pages.push(page.join('\n')); }
    return pages;
}
