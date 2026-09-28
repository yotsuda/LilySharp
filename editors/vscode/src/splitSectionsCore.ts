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

// The editor-free half of "Lily#: Split Sections to Match a Part" (extension.ts holds the half
// that talks to VS Code): what the part picker lists, and how the server's plan and refusal
// read in a modal dialog — its first line as the message, the rest as the detail. The server
// (lilysharp/splitSections) builds, checks and words both. Nothing here imports vscode, so
// `npm test` runs it.

/** One way to follow, as the server groups it: the parts that subdivide the section alike. */
export interface SplitSectionsChoice {
    /** The part to name as the reference (the first of `Parts`). */
    Part: string;
    /** Every part that subdivides the section this way, comma-separated. */
    Parts: string;
    /** `A 16 + B 121 bars`. */
    Description: string;
}

/** A QuickPick row for one choice; `part` is what goes back to the server. */
export interface SplitChoiceItem {
    label: string;
    description: string;
    part: string;
}

/** The part picker's rows, in the server's order (the order the parts are written). */
export function splitChoiceItems(choices: readonly SplitSectionsChoice[]): SplitChoiceItem[] {
    return choices.map(c => ({ label: c.Parts, description: c.Description, part: c.Part }));
}

/** A modal dialog's two texts: the message (bold, one line) and the detail under it. */
export interface ModalText {
    message: string;
    detail: string;
}

/**
 * Splits a server text into the modal's message (its first line, prefixed) and detail (the
 * rest). The plan's first line is "Follow vn1: A 16 + B 121 bars." — what the author is
 * agreeing to — and the lines after it say what is cut, what the forms become and what was
 * checked; a refusal's first line says nothing was changed and which part still holds a section long, and its bullets say why — the plan is applied whole or not at all.
 */
export function modalText(text: string, prefix = 'Lily#: '): ModalText {
    const lines = text.replace(/\r\n/g, '\n').split('\n');
    return { message: prefix + lines[0], detail: lines.slice(1).join('\n') };
}
