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

// "Ask AI in Chat" (owner's request 2026-10-29): the place the request is about, put in Copilot's
// chat input for the musician to finish — the file, and when bars are picked, their lines, the
// section, the part and the bars as written there. The editor-free half.

/** Words before a `{` that open a block inside a part's music, not a part. */
const MUSIC_BLOCKS = new Set(['voice', 'tuplet', 'grace', 'repeat', 'cue', 'tremolo', 'acciaccatura', 'appoggiatura',
    'afterGrace', 'chords', 'lyrics', 'drummap', 'fonts', 'paper', 'layout', 'override', 'once']);

/** Where an offset stands: the section and the part cell it is in, and the bar within the cell, 1-based, as written. */
export interface PlaceInScore {
    readonly section?: string;
    readonly part?: string;
    readonly bar?: number;
}

/**
 * The section, the part and the bar `offset` stands in: the section block around it, the part cell
 * in that section, and the bar lines written in the cell before it (a run of `|` is one bar line).
 * Comments and strings are skipped. A bar is counted as written: a phrase named in the cell plays
 * bars of its own that are not counted.
 */
export function placeAt(text: string, offset: number): PlaceInScore {
    type Block = { kind: 'section' | 'part' | 'other'; name?: string; bars: number };
    const stack: Block[] = [];
    let word = '';                 // the words since the last brace or bar, for the next `{`
    let i = 0;
    const end = Math.min(offset, text.length);
    while (i < end) {
        const c = text[i];
        if (c === '/' && text[i + 1] === '/') {          // a comment, to the line's end
            while (i < end && text[i] !== '\n') { i++; }
            continue;
        }
        if (c === '"') {                                 // a string
            i++;
            while (i < end && text[i] !== '"') { i += text[i] === '\\' ? 2 : 1; }
            i++;
            continue;
        }
        if (c === '{') {
            const m = /(?:\b(section|phrase)\s+(\w+)|\b([A-Za-z_]\w*))[^{}]*$/.exec(word.trim()) ?? null;
            const inSection = stack.length > 0 && stack[stack.length - 1].kind === 'section';
            if (m && m[1] === 'section') {
                stack.push({ kind: 'section', name: m[2], bars: 0 });
            } else {
                const head = /^\s*([A-Za-z_]\w*)/.exec(word.trim())?.[1];
                stack.push(inSection && head && !MUSIC_BLOCKS.has(head)
                    ? { kind: 'part', name: head, bars: 0 }
                    : { kind: 'other', bars: 0 });
            }
            word = '';
            i++;
            continue;
        }
        if (c === '}') {
            stack.pop();
            word = '';
            i++;
            continue;
        }
        if (c === '|') {
            const cell = [...stack].reverse().find(b => b.kind === 'part');
            if (cell) { cell.bars++; }
            while (i < end && text[i] === '|') { i++; }
            word = '';
            continue;
        }
        word += c;
        i++;
    }
    const section = stack.find(b => b.kind === 'section')?.name;
    const cell = [...stack].reverse().find(b => b.kind === 'part');
    return { section, part: cell?.name, bar: cell ? cell.bars + 1 : undefined };
}

/**
 * The text put in the chat's input: `Composed.lys: ` alone without a range (owner's request
 * 2026-10-29); with one, its lines, and where they stand — `Composed.lys, lines 42–43 (section A,
 * part vn1, bars 3–4): `.
 */
export function chatPointer(fileName: string, text: string, range?: { from: number; to: number }): string {
    if (!range) {
        return `${fileName}: `;
    }
    const lineOf = (at: number) => text.slice(0, at).split('\n').length;
    const [l1, l2] = [lineOf(range.from), lineOf(Math.max(range.from, range.to))];
    const a = placeAt(text, range.from), b = placeAt(text, Math.max(range.from, range.to));
    const lines = l1 === l2 ? `line ${l1}` : `lines ${l1}–${l2}`;
    const where: string[] = [];
    if (a.section) { where.push(`section ${a.section}`); }
    if (a.part) { where.push(`part ${a.part}${b.part && b.part !== a.part ? `–${b.part}` : ''}`); }
    if (a.bar !== undefined) {
        where.push(b.bar !== undefined && b.bar !== a.bar && b.part === a.part ? `bars ${a.bar}–${b.bar}` : `bar ${a.bar}`);
    }
    return `${fileName}, ${lines}${where.length ? ` (${where.join(', ')})` : ''}: `;
}
