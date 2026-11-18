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
 * The editor's menu context (owner's decision 2026-11-17, the menus' reorganisation): the top of the
 * right-click menu shows what acts on the thing under the caret, and the whole-file actions show
 * where the file has something for them. These read the line and the text — cheap and synchronous,
 * as a context key must be; the server still decides, and refuses with the reason, when one is run.
 */

/** Where the caret stands, for the menu: on (or just after) a bar line or a line / page break —
 *  where a section can be cut (Split Section Here); on a section's head (`section A {`) — where
 *  it can be joined with the next (Join Sections). */
export function caretMenuContext(line: string, col: number): { onBarLine: boolean; onSectionHead: boolean } {
    const code = stripComment(line);
    let i = col;
    while (i > 0 && (code[i - 1] === ' ' || code[i - 1] === '\t')) { i--; }
    const onBar = code[col] === '|' || (i > 0 && code[i - 1] === '|');
    let w0 = col, w1 = col;
    while (w0 > 0 && /[A-Za-z]/.test(code[w0 - 1])) { w0--; }
    while (w1 < code.length && /[A-Za-z]/.test(code[w1])) { w1++; }
    const word = code.slice(w0, w1);
    const onBreak = word === 'break' || word === 'pageBreak';
    const head = /^\s*section\s+~?[A-Za-z_][A-Za-z0-9_\-]*/.exec(code);
    const onSectionHead = !!head && col <= head[0].length;
    return { onBarLine: (onBar || onBreak) && !onSectionHead, onSectionHead };
}

/** Whether a selection [start, end) of `text` reaches across two or more section heads — joined by Join Sections. */
export function selectionSpansSections(text: string, start: number, end: number): boolean {
    if (end <= start) { return false; }
    // The heads the selection touches: those whose line it starts on, or whose line's start it reaches
    // (a selection ending in `sec` of the next head reaches it).
    const lineStart = text.lastIndexOf('\n', start - 1) + 1;
    let heads = 0;
    for (const m of text.matchAll(/^[ \t]*section\s+~?[A-Za-z_]/gm)) {
        const at = m.index ?? 0;
        if (at >= lineStart && at < end) { heads++; }
    }
    return heads >= 2;
}

/** What the file holds for the whole-file "Clean Up" actions: `@or` marks, `@todo` marks, string numbers. */
export function fileMenuContext(text: string): { hasOrMarks: boolean; hasTodos: boolean; hasStringNumbers: boolean } {
    const code = text.replace(/\/\/[^\n]*/g, '').replace(/"(?:[^"\\\n]|\\.)*"/g, '""');
    return {
        hasOrMarks: /@or\(/.test(code),
        hasTodos: /@todo\b/.test(code),
        // a string number: a backslash and a digit glued to a note (d,8\3)
        hasStringNumbers: /\\[1-9]/.test(code),
    };
}

/** The second step of "Score from File (MusicXML, MIDI, Scan, Recording)…" (owner's decision
 *  2026-11-18: File > New File… lists one entry, as VS Code's New File… picker drops submenus —
 *  vscode newFile.contribution.ts keeps only MenuItemAction). The entry itself is hidden from the
 *  palette, where the three commands are found by their own names (owner's decision 2026-11-18).
 *  Each source shows where its own command shows in the palette: the scan and the recording on
 *  Windows (the command offers the reader's extension) or wherever their reader is found. */
export interface NewScoreSource { command: string; label: string; detail: string }
export function newScoreSources(onWindows: boolean, omrFound: boolean, dettatoFound: boolean): NewScoreSource[] {
    const sources: NewScoreSource[] = [
        { command: 'lilysharp.importMusicXml', label: 'MusicXML or MIDI…', detail: '.musicxml · .mxl · .xml · .mid · .midi' },
    ];
    if (onWindows || omrFound) {
        sources.push({ command: 'lilysharp.importImage', label: 'Image or PDF (Scan)… (Preview)', detail: 'printed music read by Calamo' });
    }
    if (onWindows || dettatoFound) {
        sources.push({ command: 'lilysharp.convertRecording', label: 'Recording… (Preview)', detail: 'a chord chart heard by Dettato' });
    }
    return sources;
}

function stripComment(line: string): string {
    const at = line.indexOf('//');
    return at < 0 ? line : line.slice(0, at) + ' '.repeat(line.length - at);
}
