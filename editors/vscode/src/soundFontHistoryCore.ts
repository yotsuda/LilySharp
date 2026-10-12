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

// The editor-free half of the sound fonts Lily#: Choose Sound Font… remembers (owner's request
// 2026-10-12: "once a sound font has been set, it should be easy to pick again"). No font is
// bundled, so a user who downloads one or two goes back and forth between them; the picker
// lists the ones used before beside the default and Browse.
//
// The history is the most recently used first, at most HISTORY_MAX, kept in the extension's
// globalState (not the settings: a path is this computer's, and must not travel with Settings
// Sync). A font whose file is not there when the picker opens is HIDDEN, not forgotten — a
// file on a removable or cloud drive comes back — and an old one falls off the end instead.

/** How many sound fonts the picker remembers. */
export const HISTORY_MAX = 5;

/** Whether two paths name the same file: Windows' file names ignore case, so there they are
 *  compared without it (and with either slash). */
export function samePath(a: string, b: string, ignoreCase: boolean): boolean {
    const norm = (p: string) => {
        const s = p.trim().replace(/[\\/]+/g, '/');
        return ignoreCase ? s.toLowerCase() : s;
    };
    return norm(a) === norm(b);
}

/** The history with `file` used last: moved (or added) to the front, a duplicate dropped,
 *  and cut to `max`. An empty path changes nothing. */
export function recordFont(history: readonly string[], file: string, ignoreCase: boolean,
    max: number = HISTORY_MAX): string[] {
    const f = file.trim();
    if (!f) { return [...history]; }
    return [f, ...history.filter(h => !samePath(h, f, ignoreCase))].slice(0, max);
}

/** The history without `file` (the picker's remove button; the file itself is untouched). */
export function forgetFont(history: readonly string[], file: string, ignoreCase: boolean): string[] {
    return history.filter(h => !samePath(h, file, ignoreCase));
}

/** The fonts the picker lists, in history order: those whose file is there now, except the
 *  ones the picker lists by another name (the OS's own bank, shown as the default). */
export function shownFonts(history: readonly string[], exists: (file: string) => boolean,
    ignoreCase: boolean, listedElsewhere: readonly string[] = []): string[] {
    return history.filter(h => exists(h) && !listedElsewhere.some(o => samePath(o, h, ignoreCase)));
}

/** A history read back from storage: only strings, none empty, no duplicates, at most `max` —
 *  whatever an older or hand-edited state holds. */
export function cleanHistory(stored: unknown, ignoreCase: boolean, max: number = HISTORY_MAX): string[] {
    if (!Array.isArray(stored)) { return []; }
    let out: string[] = [];
    for (const s of stored) {
        if (typeof s !== 'string' || !s.trim()) { continue; }
        if (out.some(o => samePath(o, s, ignoreCase))) { continue; }
        out = [...out, s.trim()];
    }
    return out.slice(0, max);
}
