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

// The DECIDING half of wrapping selected staves in a group from the completion list
// (owner's request 2026-11-02): `staff a` and `staff b` selected in a score's body,
// grandStaff picked, and they stand inside `grandStaff { … }`. Nothing here touches an
// editor (staffGroupWrap.ts is the other half), so it runs and is tested under plain node.

/** The groups that take `staff` items (GRAMMAR, "Several staves as ONE GROUP"). The
 * bare-name groups, condensedStaff and combinedStaff, would have to rewrite the rows. */
export const STAFF_GROUPS = ['grandStaff', 'staffGroup', 'choirStaff'] as const;

/** What replaces the selection: `head`, then the caret, then `tail`. Literal text, its
 * indentation already written. */
export interface StaffGroupWrap { head: string, tail: string }

const IS_ROW = /^(?:staff|lyrics)\b[^{}]*$/;
const IS_COMMENT = /^\/\//;

/** The text before the unmatched '{' around `offset`, from the start of its line; null at
 * the top level. The body of a score holds no strings with braces worth reading past. */
function headOfBlock(text: string, offset: number): { head: string, lineStart: number } | null {
    for (let i = offset - 1, depth = 0; i >= 0; i--) {
        const c = text[i];
        if (c === '}') { depth++; }
        else if (c === '{') {
            if (depth > 0) { depth--; continue; }
            const lineStart = text.lastIndexOf('\n', i - 1) + 1;
            return { head: text.slice(lineStart, i), lineStart };
        }
    }
    return null;
}

const leadingSpace = (s: string) => /^[ \t]*/.exec(s)![0];

/** The selection [lo, hi) wrapped in `group`, or null unless it is a score body's own rows,
 * two staves at least, and whole lines when it runs over several. `tab` is one indent
 * where the file does not show it: the rows' own depth under the `score {` line says it. */
export function staffGroupWrap(text: string, lo: number, hi: number, group: string, tab: string): StaffGroupWrap | null {
    const selected = text.slice(lo, hi);
    if ((selected.match(/\bstaff\b/g) ?? []).length < 2) { return null; }
    const block = headOfBlock(text, lo);
    if (!block || !/^[ \t]*score\b/.test(block.head)) { return null; }

    const content = selected.replace(/\s+$/, '');
    const tail = selected.slice(content.length);
    const lines = content.replace(/\r/g, '').split('\n');
    if (lines.some(l => { const t = l.trim(); return t !== '' && !IS_COMMENT.test(t) && !IS_ROW.test(t); })) { return null; }

    // On one line: `score { staff a  staff b }` is written on one line, and stays so.
    if (lines.length === 1) {
        return { head: `${group} { ${content.trim()} }`, tail };
    }

    // Over several: whole lines, or the group's own lines would land among other text.
    const lineStart = text.lastIndexOf('\n', lo - 1) + 1;
    const before = text.slice(lineStart, lo);
    let lineEnd = text.indexOf('\n', lo + content.length);
    if (lineEnd < 0) { lineEnd = text.length; }
    if (before.trim() !== '' || text.slice(lo + content.length, lineEnd).trim() !== '') { return null; }

    const indent = before + leadingSpace(lines[0]);
    const outer = leadingSpace(block.head);
    const unit = indent.length > outer.length && indent.startsWith(outer) ? indent.slice(outer.length) : tab;
    const rows = [before + lines[0], ...lines.slice(1)].map(l => {
        if (l.trim() === '') { return ''; }
        return indent + unit + (l.startsWith(indent) ? l.slice(indent.length) : l.trimStart());
    });
    return {
        head: `${indent.slice(before.length)}${group} {\n${rows.join('\n')}\n${indent}}`,
        tail,
    };
}
