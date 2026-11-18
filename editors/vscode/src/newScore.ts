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

// What a score starts from: the New Score command's template (the built-in one, or the writer's
// own beside it), the title the command selects in it, and the command behind the `template-…`
// completions, which puts a whole worked example in the file's place. It stood in extension.ts
// until session 1298; it reads nothing of the extension's own state.

import * as vscode from 'vscode';

// Content for the "Lily#: New Score" command (File ▸ New File… ▸ Lily# Score).
//
// The SMALLEST complete piece: one part, one section, one score — a blank page a
// writer overwrites, not a worked example to delete. It used to be the whole of
// Twinkle, Twinkle with lyrics, verses, a |: :| repeat and six paragraphs of
// teaching comments, which is a fine thing to READ and a poor thing to start from:
// the first act on a new file was deleting 40 lines.
//
// The worked examples did not go away — they are the `template-…` completions
// (Ctrl+Space at the top level), which replace the file rather than joining it.
// The first comment line below names Ctrl+Space, the key that lists them and everything
// else, and how to start lesson 1 with it; the second the step keys and what each pair
// does (owner's requests 2026-10-16 — a writer who finds typing hard changes a note, the
// tempo, the key or the clef with them; it replaced the preview's Ctrl+K V — and
// 2026-10-26: the very first stage introduces Ctrl+Space and the step keys, as lesson 1's
// head does). They are the only lines here that are not score.
//
// ⚠️ Two spaces, no tabs: 545 of the 548 tracked .lys files that indent at all use
// two spaces and not one uses a tab (measured 2026-08-23). The formatter cannot
// settle this — it takes the indent from the client's formatting options.
export const NEW_SCORE_TEMPLATE = `// New to Lily#? Ctrl+Space lists what you can write. On a new line type "lesson", Ctrl+Space, pick lesson-01.
// On a note: Ctrl+Shift+Up/Down = the next note of the key, Ctrl+Shift+PageUp/PageDown = an octave (on a tempo, key or clef too).
title "Untitled"

tempo 100
time 4/4
key c major

part melody {
  clef treble
  section A { c'4 d e f | g2 g | }
}

form { A }

score {
  staff melody
}
`;

/** Where the writer's own New Score template is kept (Edit New Score Template). */
export function newScoreTemplateUri(context: vscode.ExtensionContext): vscode.Uri {
    return vscode.Uri.joinPath(context.globalStorageUri, 'new-score-template.lys');
}

export async function uriExists(uri: vscode.Uri): Promise<boolean> {
    try {
        await vscode.workspace.fs.stat(uri);
        return true;
    } catch {
        return false;
    }
}

/** What New Score starts from: the writer's template when there is one with anything in it,
 *  else the built-in one. */
export async function newScoreTemplate(context: vscode.ExtensionContext): Promise<string> {
    try {
        const own = Buffer.from(await vscode.workspace.fs.readFile(newScoreTemplateUri(context))).toString('utf8');
        if (own.trim().length > 0) {
            return own;
        }
    } catch {
        // none yet: the built-in one
    }
    return NEW_SCORE_TEMPLATE;
}

/**
 * The text INSIDE the quotes of the document's `title "…"` line, or null if it has none.
 *
 * Read out of the document rather than counted off the template constant: the offset of a
 * hard-coded line and column is a second spelling of the template's shape, and the two
 * would drift the first time a line moved above it. A title with nothing between its quotes
 * returns null — selecting a zero-width range would look exactly like nothing happened.
 */
export function newScoreTitleRange(doc: vscode.TextDocument): vscode.Range | null {
    const match = /^title "([^"]*)"/m.exec(doc.getText());
    if (!match || match[1].length === 0) {
        return null;
    }
    const at = match.index + match[0].indexOf('"') + 1;
    return new vscode.Range(doc.positionAt(at), doc.positionAt(at + match[1].length));
}

/**
 * Put a score template into the editor, as the WHOLE file.
 *
 * The `template-…` completion items name this command (extension.ts registers it).
 * Accepting one of those items does not edit the document — it re-types the word the
 * writer was completing, unchanged — so everything the writer had is still there when
 * the question below is asked, and answering "no" leaves it that way.
 *
 * `range` is that re-typed word (startLine, startChar, endLine, endChar), and it is
 * excluded from the "is there anything to lose here?" test: a brand-new file where the
 * writer typed `temp` to find the item is empty, and asking permission to clear an empty
 * file is a dialog with no question in it. The server omits the four numbers when it has
 * no caret position (its unit tests call it that way), which just makes the test read
 * the whole document.
 */
export async function applyScoreTemplate(label: string, template: string, range: number[]) {
    const editor = vscode.window.activeTextEditor;
    if (!editor || editor.document.languageId !== 'lilysharp') {
        vscode.window.showErrorMessage('Lily#: open a .lys file to apply a score template.');
        return;
    }

    const doc = editor.document;
    const fullRange = new vscode.Range(doc.positionAt(0), doc.positionAt(doc.getText().length));

    let existing = doc.getText();
    if (range.length === 4) {
        const typed = new vscode.Range(range[0], range[1], range[2], range[3]);
        existing = doc.getText(new vscode.Range(fullRange.start, typed.start))
                 + doc.getText(new vscode.Range(typed.end, fullRange.end));
    }

    if (existing.trim().length > 0) {
        const replace = 'Replace';
        const choice = await vscode.window.showWarningMessage(
            `Lily#: replace the whole file with the "${label}" template?`,
            {
                modal: true,
                detail: 'A score template is a complete piece, so it takes the place of what '
                      + 'is here rather than joining it. Everything currently in this file is '
                      + 'discarded (Ctrl+Z brings it back).'
            },
            replace);
        if (choice !== replace) {
            return;
        }
    }

    const edit = new vscode.WorkspaceEdit();
    edit.replace(doc.uri, fullRange, template);
    const ok = await vscode.workspace.applyEdit(edit);
    if (!ok) {
        vscode.window.showErrorMessage('Lily#: the score template could not be applied.');
        return;
    }
    // The caret is left wherever the replaced text put it — park it at the top of the
    // template, which is where a writer starts reading (and editing) a fresh score.
    const start = new vscode.Position(0, 0);
    editor.selection = new vscode.Selection(start, start);
    editor.revealRange(new vscode.Range(start, start));
}
