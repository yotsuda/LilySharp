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

// From a song to a dance (docs/song-to-dance-design.md): the third step of Shosa's way — song →
// singing → dance → video (Shosa's docs/ROADMAP.md §2, 2026-11-26). The steps are a walkthrough of
// this extension's own, and a score that has just got a song says what comes next: dance it with
// Shosa. What is decided here is apart from VS Code.

/** The extension that dances a .lys (github.com/yotsuda/Shosa). */
export const SHOSA_EXTENSION_ID = 'yotsuda.shosa';

/** Its command that puts a dance on a .lys (a Uri, or the .lys in the editor). */
export const SHOSA_ADD_DANCE = 'shosa.addDance';

/** The context that shows the walkthrough: set when it is asked for, so that installing Lily# does
 *  not open a page about dancing (as the singing walkthrough's `lilysharp.singingGuide`). */
export const DANCE_GUIDE_CONTEXT = 'lilysharp.danceGuide';

/** Whether the score has a singer: a `singer` line (docs/GRAMMAR.md SingerDecl), at the top of the
 *  file or in a score. Comments do not count. */
export function hasSinger(text: string): boolean {
    return text.replace(/\/\/[^\n]*/g, '').split('\n').some(line => /^\s*singer\b/.test(line));
}

/** Whether a change gave the score its song: a singer where there was none. A score that sang
 *  already is not told again at each change of its notes. */
export function gaveASong(before: string, after: string): boolean {
    return !hasSinger(before) && hasSinger(after);
}

/** The next step's message, for the score named. */
export function danceNextMessage(fileName: string, shosaInstalled: boolean): string {
    return `Lily#: ${fileName} has a song now. Next, a character can sing it and dance to it — `
        + (shosaInstalled ? 'Shosa\'s Add a Dance… puts a dance on it.' : 'Shosa, a free extension, puts a dance on it.');
}

/** The message's buttons, in order. */
export function danceNextActions(shosaInstalled: boolean): string[] {
    return [shosaInstalled ? 'Add a Dance…' : 'Install Shosa', 'Show the Steps'];
}
