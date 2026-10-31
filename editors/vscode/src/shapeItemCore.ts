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

/**
 * Whether the caret at `character` of `line` stands on a `chord(…)` or `diagram(…)` ITEM —
 * the word, its parentheses (a symbol's own `(…)` nested inside), and the duration after
 * them — never on an `@chord(…)` / `@diagram(…)` mark. What shows "Convert to chord notes" in
 * the editor's context menu (`lilysharp.onShapeItem`); the server decides the rest.
 */
export function onShapeItem(line: string, character: number): boolean {
    const word = /(?<![@\w])(chord|diagram)\s*\(/g;
    let m: RegExpExecArray | null;
    while ((m = word.exec(line)) !== null) {
        let i = m.index + m[0].length;
        let depth = 0;
        for (; i < line.length; i++) {
            if (line[i] === '(') depth++;
            else if (line[i] === ')' && depth-- === 0) break;
        }
        if (i >= line.length) return character >= m.index;   // words run on: still on the item
        let end = i + 1;
        while (end < line.length && /[0-9.]/.test(line[end])) end++;
        if (character >= m.index && character <= end) return true;
        word.lastIndex = end;
    }
    return false;
}

/**
 * Whether the caret at `character` of `line` stands on a `<…>` chord — its brackets, its notes
 * and the duration after it — never inside a `<< >>` group's double brackets. What shows
 * "Add Guitar Diagram" in the editor's context menu (`lilysharp.onAngleChord`).
 */
export function onAngleChord(line: string, character: number): boolean {
    for (let i = 0; i < line.length; i++) {
        if (line[i] !== '<') continue;
        if (line[i + 1] === '<' || line[i - 1] === '<') { i++; continue; }
        const close = line.indexOf('>', i + 1);
        if (close < 0) return character >= i;
        if (line[close + 1] === '>') { i = close + 1; continue; }
        let end = close + 1;
        while (end < line.length && /[0-9.]/.test(line[end])) end++;
        if (character >= i && character <= end) return true;
        i = end - 1;
    }
    return false;
}

/**
 * Whether two document URIs name the same file. A URI the language server hands back in a
 * command's arguments (.NET's `Uri.ToString()`, `file:///c:/song.lys`) is spelled otherwise than
 * VS Code's own (`file:///c%3A/song.lys`), so the two are compared decoded, the drive letter in
 * either case (owner's report 2026-10-31: "Add Guitar Diagram" from the light bulb found no
 * document and refused).
 */
export function sameDocumentUri(a: string, b: string): boolean {
    const norm = (u: string) => {
        let s = u;
        try { s = decodeURIComponent(u); } catch { /* keep it as written */ }
        return s.replace(/^file:\/\/\/([A-Za-z]):/, (_m, d: string) => `file:///${d.toLowerCase()}:`);
    };
    return norm(a) === norm(b);
}
