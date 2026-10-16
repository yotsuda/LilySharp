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
 * none either and the webview maps every data-pos / data-alt / data-start of the page it holds through
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

/**
 * Where a click on the preview puts the caret, from the clicked grob's data-pos. A grob's
 * data-pos is its node's full-span start: for the first thing on a line that is the leading
 * indentation, for a text (a title, a composer) the opening quote, and for a mark written
 * with an `@` — an articulation, a dynamic, an `@chord` — the `@` itself. The caret goes past
 * each of those to the thing clicked. Past the `@` because the step keys read a caret just
 * before it as the NOTE's (it is also just after the note's duration), so a click on a fermata
 * followed by Ctrl+Shift+Up moved the pitch instead of the fermata (user report 2026-10-17).
 * Never across a line break.
 */
export function jumpOffsetFor(text: string, pos: number): number {
    let offset = pos;
    while (offset < text.length && (text[offset] === ' ' || text[offset] === '\t')) {
        offset++;
    }
    if (text[offset] === '"' || text[offset] === '@') {
        offset++;
    }
    return offset;
}

export function svgPostKey(
    svg: string, error: string | null | undefined,
    renders: readonly RenderEntry[] | null | undefined, drawnRender: string): string {
    const list = (renders || []).map(r => `${r.Type}${r.Name}${r.Filename}`).join('');
    return svg + '\n\n' + (error ?? '') + '\n\n' + list + '\n' + drawnRender;
}

/** What the editor that just came to the front is, as the preview's follower reads it. */
export interface ShownEditor {
    languageId: string;
    scheme: string;
    /** A diff or merge view shows it (git's working-tree side is a file: too). */
    inDiff: boolean;
    /** A preview of its own is open (a locked one, or the follower already on it). */
    hasOwnPreview: boolean;
    /** The follower — the one preview that is not locked — is open. */
    hasFollower: boolean;
    /** lilysharp.preview.openOnOpen. */
    openOnOpen: boolean;
    /** It has not been in front since it was opened (a tab switch back to it is not an opening). */
    firstShow: boolean;
    /** The writer closed this file's preview this session. */
    dismissed: boolean;
    /** The follower is gone because the writer LOCKED it (to compare), not because they closed
     *  a preview: then a tab switch opens the second preview too. */
    followerLocked: boolean;
}

/**
 * What an editor coming to the front does to the previews (owner's decision 2026-10-12): the
 * follower turns to it, like the Markdown preview; with no follower, OPENING a .lys opens one
 * beside it. Switching back to a tab is not an opening — a writer who closed the preview is
 * not handed it again for every tab click — and neither is a diff, a git: side or a file
 * whose preview the writer closed. New Score's untitled buffer counts as an opening.
 */
export function previewOnEditorShown(e: ShownEditor): 'none' | 'follow' | 'open' {
    if (e.languageId !== 'lilysharp' || e.hasOwnPreview) {
        return 'none';
    }
    // A file, or an untitled score (New Score) — never a git: side or another scheme's copy.
    if (e.scheme !== 'file' && e.scheme !== 'untitled') {
        return 'none';
    }
    if (e.hasFollower) {
        return 'follow';
    }
    return e.openOnOpen && !e.inDiff && (e.firstShow || e.followerLocked) && !e.dismissed ? 'open' : 'none';
}

/**
 * The column a new preview opened beside its file goes to, or undefined for VS Code's own
 * "beside": with a locked preview on screen it opens in a new group just right of the
 * rightmost one, so the two stand side by side to be compared instead of stacking as tabs
 * (owner's decision 2026-10-12 — a preview is locked to be compared). A column past the last
 * group makes VS Code create that group.
 */
export function newPreviewColumn(visibleLockedColumns: readonly number[]): number | undefined {
    return visibleLockedColumns.length === 0 ? undefined : Math.max(...visibleLockedColumns) + 1;
}

/** One editor group, as far as keeping scores out of the previews' groups needs it. */
export interface GroupInfo {
    column: number;
    /** A Lily# preview is one of its tabs. */
    hasPreview: boolean;
}

/**
 * Where a .lys just opened in `openedColumn` moves to, or undefined to leave it: a score opened
 * into a preview's group would cover the preview (owner's request 2026-10-12), so it goes to the
 * leftmost group with no preview in it, or — every group holding one — to a new group LEFT of the
 * preview's (owner's request: the score on the left, its picture on the right).
 */
export function scoreGroupFor(openedColumn: number, groups: readonly GroupInfo[]): number | 'left' | undefined {
    if (!groups.some(g => g.column === openedColumn && g.hasPreview)) {
        return undefined;
    }
    const free = groups.filter(g => !g.hasPreview).map(g => g.column);
    return free.length > 0 ? Math.min(...free) : 'left';
}

/** One editor tab, as far as revealing a preview's source needs it. */
export interface SourceTab {
    column: number;
    uri: string;
    /** The front tab of its group (already on screen). */
    active: boolean;
}

/**
 * The editor group whose tab of `uri` comes to the front when a preview of it is brought
 * forward, or undefined to leave the tabs alone: the source is not open (a closed file is not
 * reopened), it is already in front somewhere, or it lives only in the preview's own group —
 * where bringing it forward would cover the preview just picked.
 */
export function sourceGroupToReveal(tabs: readonly SourceTab[], previewColumn: number | undefined, uri: string): number | undefined {
    const elsewhere = tabs.filter(t => t.uri === uri && t.column !== previewColumn);
    if (elsewhere.length === 0 || elsewhere.some(t => t.active)) {
        return undefined;
    }
    return elsewhere[0].column;
}

/**
 * Where a click on a preview shows its score when no editor shows it (owner's request
 * 2026-10-14: the .lys closed, its preview left open, a click there opens it again): the group
 * that holds a tab of it, behind others, and no preview; else where a score opened into a
 * preview's group goes (scoreGroupFor) — the leftmost group with no preview, else a new group
 * left of the preview's. Never a preview's group, where it would cover the preview.
 */
export function sourceGroupToOpen(tabs: readonly SourceTab[], groups: readonly GroupInfo[], uri: string): number | 'left' {
    const previews = new Set(groups.filter(g => g.hasPreview).map(g => g.column));
    const tab = tabs.find(t => t.uri === uri && !previews.has(t.column));
    if (tab) {
        return tab.column;
    }
    const free = groups.filter(g => !g.hasPreview).map(g => g.column);
    return free.length > 0 ? Math.min(...free) : 'left';
}
