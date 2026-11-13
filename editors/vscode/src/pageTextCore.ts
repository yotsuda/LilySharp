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

// A page text in a .lys file (docs/markdown-text-design.md): the lines between a line that
// begins with three or more backticks and the next line that is as many backticks and
// nothing else. To the compiler the whole of it is one token and nothing in it is Lily#;
// in the editor its body is Markdown (the grammar embeds VS Code's own, and package.json
// maps its scope to the language `markdown`).
//
// What the embedded language brings by itself, read in VS Code's source (2026-11-13):
// Toggle Comment writes Markdown's `<!-- -->` (lineCommentCommand.ts and
// blockCommentCommand.ts ask the language AT THE POSITION), bracket matching is Markdown's
// (bracketPairsImpl.ts), and so are the word pattern and the Enter rules (enterAction.ts) —
// of which Markdown has none. What it does NOT bring: the pairs a typed character closes
// or surrounds a selection with are the DOCUMENT's language's (cursorCommon.ts builds them
// from model.getLanguageId()), so `*`, `_` and a backtick over a selection would replace
// it, as in Lily# music; and VS Code's text editor continues no Markdown list on Enter at
// all (markdown-basics has no onEnterRules; `markdown.editor.smartEnter` belongs to its
// separate custom editor). Those two are written here (owner's decision 2026-11-13), as
// Lily#'s own — nothing below is taken from VS Code.
//
// This file never sees an editor: pageText.ts carries its answers out, and the tests run
// it under node. Its backticks are character codes and quoted strings, never a template
// literal's.

/** Where a line's first non-blank character stands, and how many backticks begin there. */
function backticksAt(line: string): { indent: number, ticks: number } {
    let i = 0;
    while (i < line.length && (line[i] === ' ' || line[i] === '\t')) { i++; }
    let j = i;
    while (j < line.length && line.charCodeAt(j) === 96) { j++; }
    return { indent: i, ticks: j - i };
}

/** A text's lines: `open` its opening line, `close` its closing one — the document's line
 * count where it is not closed, as the lexer runs such a text to the end of the file —
 * and `indent`, how many blanks the compiler takes off each line (the closing fence's own;
 * the opening one's until it is written). The BODY is the lines strictly between. */
export interface TextBody { open: number, close: number, indent: number }

/**
 * Every page text of a document, by the lexer's rule read a line at a time
 * (Lexer.ScanFence): three or more backticks open one, and it closes at the first later
 * line that begins with at least as many and holds nothing else.
 *
 * ⚠️ Line by line is an approximation of the lexer in one direction only: backticks that
 * begin a line INSIDE a block comment or a lyrics block are read here as a fence and are
 * not one. The cost of being wrong there is that Lily#'s typing aids rest until the next
 * such line; nothing is rewritten on this function's word alone (listEnter and surround
 * ask the line and the selection as well).
 */
export function textBodies(lineCount: number, lineAt: (line: number) => string): TextBody[] {
    const found: TextBody[] = [];
    for (let i = 0; i < lineCount; i++) {
        const open = backticksAt(lineAt(i));
        if (open.ticks < 3) { continue; }
        let close = lineCount;
        let indent = open.indent;
        for (let j = i + 1; j < lineCount; j++) {
            const line = lineAt(j);
            const at = backticksAt(line);
            if (at.ticks >= open.ticks && line.slice(at.indent + at.ticks).trim() === '') {
                close = j;
                indent = at.indent;
                break;
            }
        }
        found.push({ open: i, close, indent });
        i = close;
    }
    return found;
}

/** The text whose BODY holds `line` — not its opening line, where the words are Lily#'s
 * (`end`, `box`, `xMargin 20mm`, `yMargin 2`, completed by the server), nor its closing one. */
export function textBodyAt(bodies: readonly TextBody[], line: number): TextBody | undefined {
    for (const b of bodies) {
        if (line <= b.open) { return undefined; }
        if (line < b.close) { return b; }
    }
    return undefined;
}

/** A list item's line: its indentation, its marker (`-`, `*`, `+`, or a number with its
 * `.` or `)`), the blanks after it and where its words begin. A marker with no blank after
 * it is not one (CommonMark: `-word` is a word). */
interface ListItem { indent: string, bullet: string, number: number | undefined, delimiter: string, gap: string, content: number }

function listItem(line: string): ListItem | undefined {
    const m = /^([ \t]*)(?:([-*+])|(\d{1,9})([.)]))([ \t]+)/.exec(line);
    if (!m) { return undefined; }
    return {
        indent: m[1], bullet: m[2] ?? '', number: m[3] === undefined ? undefined : Number(m[3]),
        delimiter: m[4] ?? '', gap: m[5], content: m[0].length,
    };
}

/** The marker of the item AFTER `item` in its list: the same bullet, the next number. */
function nextMarker(item: ListItem): string {
    return item.number === undefined ? item.bullet : String(item.number + 1) + item.delimiter;
}

/** Whether Enter at `column` of this line continues or ends a list: the line is a list
 * item's and the caret is in or after its words. Read off the one line, for the context
 * key the Enter binding waits on. */
export function onListItem(line: string, column: number): boolean {
    const item = listItem(line);
    return item !== undefined && column >= item.content;
}

/** What Enter writes: `text` in place of columns `from`…`to` of the caret's line, the caret
 * left at its end. `text` holds a line feed where a line is begun. */
export interface EnterPlan { from: number, to: number, text: string, what: string }

/**
 * Enter in a list of a page text (owner's decision 2026-11-13).
 *
 * - On an item with words, the caret in or after them: the next line begins with the
 *   item's indentation and the next marker — the same bullet, the next number with the
 *   same `.` or `)` — and takes what stood right of the caret. Blanks LEFT of the caret
 *   stay: two at a line's end are Markdown's line break.
 * - On an item with no words: the list ends there. The marker goes; a nested item becomes
 *   an item of the list around it (the marker after the nearest item above that is
 *   indented less), an outermost one an empty line at the text's own indentation.
 * - Anywhere else — a paragraph, an item's wrapped line, the caret inside the marker —
 *   null: Enter is VS Code's.
 *
 * The numbers of the items below are left as written: the compiler counts on from the
 * FIRST item's number whatever the later ones say (CommonMark's rule; section 5.2).
 *
 * @param lineAt the document's lines; read at `line` and, for an empty item, upward to
 *               the text's opening line at most.
 */
export function listEnter(body: TextBody, line: number, column: number,
    lineAt: (line: number) => string): EnterPlan | null {
    if (line <= body.open || line >= body.close) { return null; }
    const text = lineAt(line);
    const item = listItem(text);
    if (!item || column < item.content || column > text.length) { return null; }

    if (text.slice(item.content).trim() === '') {
        for (let above = line - 1; above > body.open; above--) {
            const outer = listItem(lineAt(above));
            if (outer && outer.indent.length < item.indent.length) {
                return {
                    from: 0, to: text.length, text: outer.indent + nextMarker(outer) + outer.gap,
                    what: 'Enter on an empty nested item -> an item of the list around it',
                };
            }
        }
        return {
            from: 0, to: text.length, text: item.indent.slice(0, body.indent),
            what: 'Enter on an empty item -> the list ends',
        };
    }

    let to = column;
    while (to < text.length && (text[to] === ' ' || text[to] === '\t')) { to++; }
    return {
        from: column, to, text: '\n' + item.indent + nextMarker(item) + item.gap,
        what: 'Enter in a list item -> the next item',
    };
}

/** The characters that surround a selection in a page text: Markdown's emphasis, strong
 * emphasis (typed twice) and code. The other pairs — brackets and the quote — VS Code
 * already surrounds with in a .lys file. */
export function surrounds(typed: string): boolean {
    return typed === '*' || typed === '_' || typed === '`';
}

/** What a surround writes after the character that has just replaced the selection: the
 * selection back, and the character again. `select` is how much of it is left selected —
 * the words, as VS Code leaves them after its own surround, so that a second `*` makes
 * `**strong**`. */
export function surroundPlan(typed: string, selection: string): { insert: string, select: number } | null {
    if (!surrounds(typed) || selection.length === 0) { return null; }
    return { insert: selection + typed, select: selection.length };
}
