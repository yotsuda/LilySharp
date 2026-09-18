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
/**
 * The picture as pages (the server's SvgPages, 2026-09-18): the frame around them and one
 * item per page. With BaseVersion, a DELTA against the picture of that version, which this
 * client said the webview holds: a 'same' page carries no markup, a 'shifted' page carries
 * none either and the webview maps every data-pos / data-alt of the page it holds through
 * Window, and only a 'changed' page carries its markup. Without BaseVersion every page
 * carries its markup, and Head + markup… + Tail is the one-string picture.
 */
export interface SvgPages {
    Version: number;
    BaseVersion?: number | null;
    Head: string;
    Tail: string;
    Window?: { Prefix: number; SuffixStart: number; Delta: number } | null;
    Items: SvgPageItem[];
}

export interface SvgPageItem {
    Change: 'same' | 'shifted' | 'changed';
    Markup?: string | null;
}

/** One line on a page answer for the output channel: how many pages did what, and how
 *  many characters travelled — the number that says whether a keystroke shipped one page
 *  or the whole book. */
export function pagesSummary(pages: SvgPages): string {
    let same = 0, shifted = 0, changed = 0, chars = 0;
    for (const item of pages.Items) {
        if (item.Change === 'same') { same++; } else if (item.Change === 'shifted') { shifted++; } else { changed++; }
        chars += item.Markup ? item.Markup.length : 0;
    }
    return `${pages.Items.length} (${pages.BaseVersion != null ? 'delta v' + pages.BaseVersion + '->' : 'full '}v${pages.Version}`
        + `: same ${same}, shifted ${shifted}, changed ${changed}; ${chars + pages.Head.length + pages.Tail.length} chars)`;
}

export function svgPostKey(
    svg: string, error: string | null | undefined,
    renders: readonly RenderEntry[] | null | undefined, drawnRender: string): string {
    const list = (renders || []).map(r => `${r.Type}${r.Name}${r.Filename}`).join('');
    return svg + '\n\n' + (error ?? '') + '\n\n' + list + '\n' + drawnRender;
}
