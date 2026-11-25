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

/** The second step of File > New File… → "Lily# Score (.lys)…", its one entry (owner's decision
 *  2026-11-26, widening 2026-11-18's "Score from File…": VS Code's New File… picker drops submenus —
 *  vscode newFile.contribution.ts keeps only MenuItemAction — so the choice is a second picker).
 *  Grouped by where a piece starts, not by file type: from nothing, or from music you have.
 *  Blank comes first, so Enter alone starts the template; it carries a button to edit the template.
 *  The entry itself is hidden from the palette, where each command is found by its own name.
 *  The scan and the recording show where their own commands show in the palette: on Windows (the
 *  command offers the reader's extension) or wherever their reader is found. */
export interface NewScoreStart {
    /** A separator's heading when `command` is absent. */
    label: string;
    command?: string;
    description?: string;
    detail?: string;
    /** The item that carries the Edit New Score Template button. */
    template?: true;
}
export function newScoreStarts(onWindows: boolean, omrFound: boolean, orecchianteFound: boolean): NewScoreStart[] {
    const starts: NewScoreStart[] = [
        { label: 'Start fresh' },
        { command: 'lilysharp.newScore', label: 'Blank', detail: 'The New Score template', template: true },
        { command: 'lilysharp.composeWithAiInChat', label: 'Compose with AI…', detail: 'Copilot\'s chat writes a new piece from what you describe' },
        { label: 'From music you have' },
    ];
    if (onWindows || orecchianteFound) {
        starts.push({ command: 'lilysharp.convertRecording', label: 'Recording (MP3, WAV, …)…', description: 'Preview',
            detail: 'A chord chart heard by Orecchiante: tempo, bar lines, chords and sections' });
    }
    if (onWindows || omrFound) {
        starts.push({ command: 'lilysharp.importImage', label: 'Scan (PDF, image)…', description: 'Preview',
            detail: 'Printed music read by Calamo' });
    }
    starts.push({ command: 'lilysharp.importMusicXml', label: 'MusicXML, MIDI or ABC…', detail: '.musicxml · .mxl · .xml · .mid · .midi · .abc' });
    return starts;
}

function stripComment(line: string): string {
    const at = line.indexOf('//');
    return at < 0 ? line : line.slice(0, at) + ' '.repeat(line.length - at);
}
