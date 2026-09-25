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

/**
 * One candidate, as the edit it makes: `snap` is the span of the ORIGINAL file it replaces
 * (with that span's text as `origSelectedText`), `text` what goes there. A selection-only
 * reply keeps the user's selection as the span; a whole-file reply is reduced to the span
 * where it differs from the file, so validation, the before/after highlight and the apply
 * guard all work on one shape.
 */
export interface CandidateEdit<S extends SpanLike> {
    snap: S;
    text: string;
    /** The model's reply as it came, for the conversation history. */
    reply: string;
    /** The model returned the whole file (a change reaching outside the selection). */
    whole: boolean;
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
 * Reads the model's reply: the whole file inside `<file>…</file>` when the change reaches
 * outside the selection — a new part needs a `part` line, a block in the section and a
 * score row, and a selection-only reply could only answer "harmonize in a new part" with
 * `voice { }` or not at all (owner report, 2026-09-26) — else the replacement for the
 * selection. null when there is nothing in it.
 */
export function toCandidateEdit<S extends SpanLike>(snapshot: S, raw: string): CandidateEdit<S> | null {
    const file = raw.match(/<file>[ \t]*\r?\n?([\s\S]*?)\r?\n?[ \t]*<\/file>/);
    if (!file) {
        const text = sameNewlines(withoutMarkers(cleanCandidate(raw)), snapshot.origFullText);
        return text.length === 0 ? null : { snap: snapshot, text, reply: raw, whole: false };
    }
    const orig = snapshot.origFullText;
    let full = sameNewlines(withoutMarkers(cleanCandidate(file[1])), orig);
    if (full.trim().length === 0) {
        return null;
    }
    // The file's final newline is not the model's to drop.
    const eol = orig.includes('\r\n') ? '\r\n' : '\n';
    if (orig.endsWith('\n') && !full.endsWith('\n')) {
        full += eol;
    }
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
    return { snap, text: full.slice(lo, full.length - tail), reply: raw, whole: true };
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
