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

import { shapeItemAt } from './shapeItemCore';

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
/**
 * Whether a rewrite of the whole document, asked of version `asked`, is no longer the document's
 * (`now`, its version when the answer came): replacing the file with it would take back what was
 * typed while the request was out.
 */
export function rewriteIsStale(asked: number, now: number): boolean {
    return asked !== now;
}

/**
 * Whether `message` differs from the last one of its type posted to a page (`kept`: type →
 * the message as JSON), and remembers it. For what the preview is told with every refresh
 * though it follows the file and the workspace, not the text (scanState, abRanges, panelFolds):
 * a page that holds the same message is not sent it again (review of session 1291). `kept`
 * is forgotten when the page reloads.
 */
export function messageChanged(kept: Map<string, string>, message: { type: string; [key: string]: unknown }): boolean {
    const json = JSON.stringify(message);
    if (kept.get(message.type) === json) {
        return false;
    }
    kept.set(message.type, json);
    return true;
}

/**
 * Every chord `<…>` of `text`, as [its `<`, its `>`] offsets, in order — for the preview's
 * Select Chord on a member right-clicked (owner's request 2026-10-27). A chord is on one line;
 * `<<`/`>>` (simultaneous music), `\<`/`\>` (hairpins), and what comments and strings hold
 * are not chords.
 */
export function chordSpans(text: string): [number, number][] {
    const spans: [number, number][] = [];
    for (let i = 0; i < text.length; i++) {
        const c = text[i];
        if (c === '/' && text[i + 1] === '/') {
            while (i < text.length && text[i] !== '\n') { i++; }
        } else if (c === '/' && text[i + 1] === '*') {
            const end = text.indexOf('*/', i + 2);
            i = end < 0 ? text.length : end + 1;
        } else if (c === '"') {
            i++;
            while (i < text.length && text[i] !== '"' && text[i] !== '\n') { if (text[i] === '\\') { i++; } i++; }
        } else if (c === '<' && text[i + 1] === '<') {
            i++;
        } else if (c === '<' && text[i - 1] !== '\\') {
            const close = chordCloseFrom(text, i);
            if (close >= 0) {
                spans.push([i, close]);
                i = close;
            }
        }
    }
    return spans;
}

/** The `>` closing the chord whose `<` is at `open`, on its line, or -1. */
function chordCloseFrom(text: string, open: number): number {
    for (let j = open + 1; j < text.length; j++) {
        const c = text[j];
        if (c === '\n' || c === '<' || c === '{' || c === '}' || c === '|' || c === '"') {
            return -1;
        }
        if (c === '>') {
            return text[j + 1] === '>' || text[j - 1] === '\\' ? -1 : j;
        }
    }
    return -1;
}

/**
 * The source range [start, end) of the bar Play Current Measure (Alt+M) plays for a caret at
 * `offset`: the span between the surrounding bar lines (`|`), bounded by the enclosing block
 * (`{`…`}`) so a bar never spills into another part or section. A caret PAST a line's last bar
 * line — on its `break`, after a comment, at the line's end — means the bar it follows: the line's
 * part of that span has nothing to play, and the span would otherwise run on to the next line's
 * first bar, or, on a block's last line, to its `}` with nothing in it (owner's report
 * 2026-11-17: Alt+M in a chords { } row sometimes played nothing).
 */
export function auditionBarRange(text: string, offset: number): [number, number] {
    const span = (at: number): [number, number] => {
        let start = 0;
        for (let i = at - 1; i >= 0; i--) {
            const c = text[i];
            if (c === '|' || c === '{' || c === '}') { start = i + 1; break; }
        }
        let end = text.length;
        for (let i = at; i < text.length; i++) {
            const c = text[i];
            if (c === '|' || c === '{' || c === '}') { end = i; break; }
        }
        return [start, end];
    };
    const at = Math.max(0, Math.min(offset, text.length));
    const [start, end] = span(at);
    const lineStart = text.lastIndexOf('\n', at - 1) + 1;
    let lineEnd = text.indexOf('\n', at);
    if (lineEnd < 0) { lineEnd = text.length; }
    const onLine = text.slice(Math.max(start, lineStart), Math.min(end, lineEnd));
    const music = onLine.replace(/\/\/.*$/, '').replace(/\b(break|pageBreak|noBreak)\b/g, '').trim();
    // Nothing of the bar on the caret's line, a bar line before the caret on it and none after it
    // there: the bar before. (`| |` on one line is an empty bar, and stays the one meant.)
    const closedOnLine = end < lineEnd && text[end] === '|';
    return music.length === 0 && !closedOnLine && start > lineStart && text[start - 1] === '|' ? span(start - 1) : [start, end];
}

/**
 * The chord a caret on `line` at `col` points at AS A WHOLE, as [its `<`, its `>`] columns: on
 * its `<` (`|<c e g>`), just past its `>` (`<c e g>|`, where its octave marks and decorations are
 * written), or in what is written glued after the `>` (duration, dots, `@` marks) — as the step
 * keys take it (NoteStepper: the chord, not a member). Undefined elsewhere — on a member, a space.
 * The preview lights the whole chord there (owner's request 2026-10-27).
 */
export function chordAtCaret(line: string, col: number): [number, number] | undefined {
    if (line[col] === '<' && line[col + 1] !== '<' && line[col - 1] !== '\\') {
        const close = chordCloseFrom(line, col);
        return close >= 0 ? [col, close] : undefined;
    }
    // Back over what is glued to the caret's left, to a `>`.
    let k = col - 1;
    while (k >= 0 && line[k] !== '>' && line[k] !== ' ' && line[k] !== '\t' && line[k] !== '<') { k--; }
    if (k < 0 || line[k] !== '>' || line[k - 1] === '>' || line[k - 1] === '\\' || line[k + 1] === '>') {
        return undefined;
    }
    for (let j = k - 1; j >= 0; j--) {
        if (line[j] === '<') {
            return line[j - 1] === '<' || line[j - 1] === '\\' || chordCloseFrom(line, j) !== k ? undefined : [j, k];
        }
        if (line[j] === '>' || line[j] === '{' || line[j] === '}' || line[j] === '|' || line[j] === '"') {
            return undefined;
        }
    }
    return undefined;
}

/** What the preview is told when the caret (no selection) lands: the notes to light, or the place alone. */
export type CaretHighlight =
    | { type: 'highlightRange'; ranges: [number, number][]; caret: number }
    | { type: 'highlightPosition'; position: number; tokenStart: number; caret: number; lineEnd: number };

/**
 * The preview's light for a caret at column `col` of `line`, `offset` into the file. The caret's
 * LINE is all that is read: a token never crosses a line break, and this runs on every caret
 * movement — the whole document was being rebuilt for each arrow key on a long score.
 */
export function caretHighlight(line: string, col: number, offset: number): CaretHighlight {
    const lineStart = offset - col;
    // A caret on a chord as a whole — its "<", just past its ">", or what is
    // glued after it — lights every head of it (owner's request 2026-10-27), as
    // the step keys move it whole there.
    const chord = chordAtCaret(line, col);
    if (chord) {
        return { type: 'highlightRange', ranges: [[lineStart + chord[0], lineStart + chord[1] + 1]], caret: offset };
    }
    // A caret anywhere on a chord(…) / diagram(…) item — its word, inside its
    // parentheses, on its duration — lights the item's notes (owner's request
    // 2026-10-31): the token under the caret alone (`133211)4`) holds no note.
    const item = shapeItemAt(line, col);
    if (item) {
        return { type: 'highlightRange', ranges: [[lineStart + item[0], lineStart + item[1]]], caret: offset };
    }
    const isWs = (c: string | undefined) =>
        c === undefined || c === ' ' || c === '\t' || c === '\n' || c === '\r';
    // The caret's note is the one whose source lies INSIDE the
    // whitespace-delimited token under the caret. Send that token's start so
    // the preview highlights a note only when it belongs to THAT token —
    // never the nearest PRECEDING note when the caret is on a barline,
    // keyword, brace or a gap (their token holds no note, so the preview
    // clears). A caret in pure whitespace corresponds to no note.
    let tokenStart = -1;
    if (!isWs(line[col]) || !isWs(line[col - 1])) {
        let startCol = col;
        while (startCol > 0 && !isWs(line[startCol - 1])) { startCol--; }
        tokenStart = offset - (col - startCol);
    }
    return {
        type: 'highlightPosition',
        position: tokenStart >= 0 ? offset : -1,
        tokenStart,
        // The caret itself, wherever it is: its bar stays lit (lightCaretBar).
        caret: offset,
        // Where its line ends: a caret before a bar's first note on that line
        // (on `voice {`) lights the bar too.
        lineEnd: lineStart + line.length,
    };
}

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

/**
 * The text of the thing that SOUNDS at `pos` — a playback note's source offset, which is its
 * node's full-span start (the leading spaces included) — as [start, end) offsets, for the
 * editor to light while the preview plays it (owner's request 2026-11-15, as Shosa's editor
 * lights the word being performed). Undefined for an offset past the text (a note of a staff
 * Lily# made) or one with nothing written at it.
 *
 * A chord is the whole of it with its duration (`<c' e' g'>4.`), a broken chord too
 * (`<< c e g >>2`); anything else is the word standing there — a note with its octave marks,
 * duration and dots (`cis''8.`), a drum's name, a chord row's symbol (`F#m7/E`) — up to the
 * first space, bar line, `@` mark, tie, slur or beam.
 */
export function soundingRange(text: string, pos: number): [number, number] | undefined {
    if (!Number.isInteger(pos) || pos < 0 || pos >= text.length) {
        return undefined;
    }
    let start = pos;
    while (start < text.length && /\s/.test(text[start])) {
        start++;
    }
    let end = start;
    if (text[start] === '<') {
        const broken = text[start + 1] === '<';
        const close = broken ? text.indexOf('>>', start + 2) : chordCloseFrom(text, start);
        const lineEnd = text.indexOf('\n', start);
        if (close < 0 || (lineEnd >= 0 && close > lineEnd)) {
            return undefined;
        }
        end = close + (broken ? 2 : 1);
        // What is glued after the chord and belongs to how long and how high it sounds.
        while (end < text.length && /[0-9.',]/.test(text[end])) {
            end++;
        }
    } else {
        while (end < text.length && !/[\s|@~()\[\]{}<>\\]/.test(text[end])) {
            end++;
        }
    }
    return end > start ? [start, end] : undefined;
}

/**
 * The ranges to light for the notes sounding at `positions`, each once, in the text's order.
 */
export function soundingRanges(text: string, positions: readonly number[]): [number, number][] {
    const seen = new Set<number>();
    const ranges: [number, number][] = [];
    for (const pos of positions) {
        const range = soundingRange(text, pos);
        if (range && !seen.has(range[0])) {
            seen.add(range[0]);
            ranges.push(range);
        }
    }
    return ranges.sort((a, b) => a[0] - b[0]);
}

/** A playback whose notes the editor lights: the file it plays and the version of its text
 * when its first report came. */
export interface SoundingSession {
    uri: string;
    version: number;
}

/**
 * What the editor of `uri` lights for one report of the preview's follow-along, and the
 * session to remember after it. The notes are those of the text Play read, so the session
 * holds that text's version: a report for a text edited since lights nothing — every offset
 * after the edit has moved — until the playback ends and the next one starts from the text
 * as it then is. A report for another file (the preview turned to it) starts a session of
 * its own. `text` is asked only when something is to be lit.
 */
export function soundingLight(
    session: SoundingSession | undefined, uri: string, version: number, text: () => string,
    positions: readonly number[], ended: boolean, enabled: boolean,
): { session: SoundingSession | undefined; ranges: [number, number][] } {
    if (ended) {
        return { session: undefined, ranges: [] };
    }
    // The playback is this text's from its first report, a rest included.
    const started = session && session.uri === uri ? session : { uri, version };
    if (positions.length === 0 || !enabled || started.version !== version) {
        return { session: started, ranges: [] };
    }
    return { session: started, ranges: soundingRanges(text(), positions) };
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
