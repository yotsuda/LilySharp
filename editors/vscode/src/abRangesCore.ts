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

// The A-B repeat's eight kept ranges (docs/ab-repeat-design.md §1, step 5): the strips under the
// practice menu. Kept per file in VS Code's workspace state as plain JSON, so they can move to the
// recording's `.dettato.json` later. A range is its bars as printed: measure indices from 0 (the
// page's `data-m`, PlaybackNote.M), both included, and the bar numbers printed on them when kept
// (for a reader of the JSON; the page goes by the indices). Browser-safe: the preview's script
// bundle (LysSynth) draws the strips with it, the extension keeps them with it.

/** How many strips there are (owner's decision 2026-11-17). */
export const AB_SLOTS = 8;

/** One kept range. */
export interface AbRange {
    /** The first bar, a measure index from 0. */
    from: number;
    /** The last bar, included. */
    to: number;
    /** The bar numbers printed on them when kept. */
    fromBar?: number;
    toBar?: number;
    /** The name given, if any (else the strip says the bar numbers). */
    name?: string;
}

function wholeNumber(v: unknown): number | undefined {
    return typeof v === 'number' && Number.isInteger(v) && v >= 0 ? v : undefined;
}

/** One range as kept, or null for anything that is not one (an empty strip). */
export function normalizeAbRange(v: unknown): AbRange | null {
    if (!v || typeof v !== 'object') { return null; }
    const o = v as Record<string, unknown>;
    const from = wholeNumber(o.from), to = wholeNumber(o.to);
    if (from === undefined || to === undefined || to < from) { return null; }
    const out: AbRange = { from, to };
    const fromBar = typeof o.fromBar === 'number' && Number.isInteger(o.fromBar) ? o.fromBar : undefined;
    const toBar = typeof o.toBar === 'number' && Number.isInteger(o.toBar) ? o.toBar : undefined;
    if (fromBar !== undefined) { out.fromBar = fromBar; }
    if (toBar !== undefined) { out.toBar = toBar; }
    const name = typeof o.name === 'string' ? o.name.trim().slice(0, 80) : '';
    if (name) { out.name = name; }
    return out;
}

/** The eight strips from whatever was kept (or sent): always AB_SLOTS entries, null where empty. */
export function normalizeAbRanges(v: unknown): (AbRange | null)[] {
    const list = Array.isArray(v) ? v : [];
    const out: (AbRange | null)[] = [];
    for (let i = 0; i < AB_SLOTS; i++) { out.push(normalizeAbRange(list[i])); }
    return out;
}

/** What a strip says: its name, else its printed bar numbers ("13–16", one bar "13"). */
export function abRangeLabel(r: AbRange, printed?: (m: number) => number | undefined): string {
    if (r.name) { return r.name; }
    const a = printed?.(r.from) ?? r.fromBar ?? r.from + 1;
    const b = printed?.(r.to) ?? r.toBar ?? r.to + 1;
    return a === b ? String(a) : `${a}–${b}`;
}

/** Where a strip's colour lies on the whole score's length (`bars` measures): left and width in percent. */
export function abRangeBand(r: AbRange, bars: number): { left: number; width: number } | null {
    if (bars <= 0 || r.from >= bars) { return null; }
    const to = Math.min(r.to, bars - 1);
    return { left: r.from / bars * 100, width: (to - r.from + 1) / bars * 100 };
}
