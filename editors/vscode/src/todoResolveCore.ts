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

/**
 * The preview's right-click doors on `@todo` marks (owner's request 2026-10-31): on a bar that
 * holds one, *Resolve This TODO (remove @todo)*; off the staves, *Resolve All TODOs*. The marks
 * come from the language server (lilysharp/todos) with each render; the page tells the menu
 * which of them the bar right-clicked holds, and the command deletes those marks' text — the
 * same edit as the editor's quick fix (LilySharpLanguageServer.ResolveTodoActions).
 */

/** One `@todo` as lilysharp/todos lists it: the mark's own span and its host's start. */
export interface TodoMark {
    readonly Start: number;
    readonly End: number;
    readonly HostStart: number;
}

/** The marks written in the bar's span [from, to) — what a right-click on that bar resolves —
 *  by the start of each mark, in source order. The mark's own place decides, its host's as a
 *  fallback: the page's bar span starts at the bar's first ITEM, which for a `<< c e g >>@todo`
 *  is the `c` past the `<<` the group (the host) starts at (owner's report 2026-10-31). */
export function todoStartsInBar(marks: readonly TodoMark[], from: number, to: number): number[] {
    if (!(to > from)) { return []; }
    const inBar = (at: number) => at >= from && at < to;
    return marks.filter(m => inBar(m.Start) || inBar(m.HostStart)).map(m => m.Start).sort((a, b) => a - b);
}

/** The page writes the bar's marks into the menu's context as one string ("12,45"): the
 *  context keys are read by `when` clauses too, where a string is the safe shape. */
export function joinStarts(starts: readonly number[]): string {
    return starts.join(',');
}

/** …and the command reads them back; anything that is not a list of offsets is none. */
export function parseStarts(value: unknown): number[] {
    if (typeof value !== 'string' || value === '') { return []; }
    const out: number[] = [];
    for (const part of value.split(',')) {
        const n = Number(part);
        if (!Number.isInteger(n) || n < 0) { return []; }
        out.push(n);
    }
    return out;
}

/** The spans to delete: every mark (starts null), or the marks that begin at one of `starts`
 *  — read off the server's CURRENT list, so a mark that moved or went since the right-click is
 *  not cut blind. Right to left, so each deletion leaves the earlier offsets as they are. */
export function todoDeletions(marks: readonly TodoMark[], starts: readonly number[] | null): { start: number; end: number }[] {
    const wanted = starts === null ? null : new Set(starts);
    return marks
        .filter(m => m.End > m.Start && (wanted === null || wanted.has(m.Start)))
        .map(m => ({ start: m.Start, end: m.End }))
        .sort((a, b) => b.start - a.start);
}
