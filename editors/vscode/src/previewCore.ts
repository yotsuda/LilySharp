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

// Pure pieces of the preview host (no vscode import), so they can be tested with
// node:test like the other *Core modules.

/** One entry of the preview's score picker, as the server names it. */
export interface RenderEntry {
    Name: string;
    Type: string;
    Filename: string;
}

/**
 * The identity of what one svg response would put in front of the user: the picture,
 * the error banner, the picker's list and the drawn score. The host skips the post to
 * the webview when this equals the last one it sent — that is what spares a rapid
 * edit/toggle/save burst from re-shipping the same large SVG.
 *
 * ⚠️ The key used to be the SVG and the error text alone, on the argument that a
 * different render selection compiles to a different SVG. True of the SELECTION, not of
 * the LIST: a `score { }` pasted below the one being drawn changes nothing in the
 * picture, and the picker learns of it only from this message — so a pasted score did
 * not appear until some later edit happened to move the picture (user report,
 * 2026-09-11). A post whose SVG is unchanged is cheap on the webview side (the page
 * markup compares equal and every page is kept).
 */
export function svgPostKey(
    svg: string, error: string | null | undefined,
    renders: readonly RenderEntry[] | null | undefined, drawnRender: string): string {
    const list = (renders || []).map(r => `${r.Type}${r.Name}${r.Filename}`).join('');
    return svg + '\n\n' + (error ?? '') + '\n\n' + list + '\n' + drawnRender;
}
