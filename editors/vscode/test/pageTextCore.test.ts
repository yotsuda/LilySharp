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

// The editor-free half of the page text's editing aids. Run with `npm test`.

import { describe, it } from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { listEnter, onListItem, surroundPlan, surrounds, textBodies, textBodyAt } from '../src/pageTextCore';

const F = '```';

/** The page texts of `lines`, and Enter at the '|' the one line marked with it carries. */
function enter(lines: string[]) {
    const line = lines.findIndex(l => l.includes('|'));
    const column = lines[line].indexOf('|');
    const text = lines.map(l => l.replace('|', ''));
    const body = textBodyAt(textBodies(text.length, i => text[i]), line);
    const plan = body ? listEnter(body, line, column, i => text[i]) : null;
    if (!plan) { return null; }
    const l = text[line];
    // What the document holds afterwards, the caret shown where the edit leaves it.
    const after = [...text.slice(0, line),
        ...(l.slice(0, plan.from) + plan.text + '|' + l.slice(plan.to)).split('\n'),
        ...text.slice(line + 1)];
    return after;
}

describe('the core is editor-free', () => {
    it('never imports vscode', () => {
        const source = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'pageTextCore.ts'), 'utf8');
        assert.doesNotMatch(source, /from 'vscode'/);
    });
});

describe('where a page text is', () => {
    const at = (lines: string[]) => textBodies(lines.length, i => lines[i]);

    it('is the lines between a fence and the next line of as many backticks', () => {
        const lines = ['title "x"', F + 'md', 'a', '', 'b', F, 'part p { }'];
        assert.deepEqual(at(lines), [{ open: 1, close: 5, indent: 0 }]);
        const bodies = at(lines);
        assert.equal(textBodyAt(bodies, 0), undefined);
        assert.equal(textBodyAt(bodies, 1), undefined, 'the opening line holds the words of Lily#');
        assert.ok(textBodyAt(bodies, 2));
        assert.ok(textBodyAt(bodies, 4));
        assert.equal(textBodyAt(bodies, 5), undefined, 'the closing line is not the body');
        assert.equal(textBodyAt(bodies, 6), undefined);
    });

    it('reads each of two texts, and the music between them is not one', () => {
        const lines = [F + 'md', 'a', F, 'section A { p { c4 } }', F + 'md end', 'b', F];
        const bodies = at(lines);
        assert.equal(bodies.length, 2);
        assert.equal(textBodyAt(bodies, 3), undefined);
        assert.ok(textBodyAt(bodies, 5));
    });

    it('closes a fence of four on four, a line of three inside it being the text', () => {
        const lines = [F + '`md', 'a', F + 'lilysharp', 'c4', F, 'b', F + '`', 'after'];
        assert.deepEqual(at(lines), [{ open: 0, close: 6, indent: 0 }]);
    });

    it('closes on MORE backticks too, and not on a line that says more than backticks', () => {
        assert.deepEqual(at([F + 'md', F + 'js', F + '`  ', 'x']), [{ open: 0, close: 2, indent: 0 }]);
    });

    it('runs a text that is not closed to the end of the file', () => {
        const lines = ['score {', '  ' + F + 'md', '  a', '  staff p', '}'];
        const bodies = at(lines);
        assert.deepEqual(bodies, [{ open: 1, close: 5, indent: 2 }]);
        assert.ok(textBodyAt(bodies, 4));
    });

    it('takes the indentation from the closing fence, as the compiler does', () => {
        assert.deepEqual(at(['    ' + F + 'md', '    a', '  ' + F]), [{ open: 0, close: 2, indent: 2 }]);
    });

    it('is not begun by backticks that do not begin their line, nor by fewer than three', () => {
        assert.deepEqual(at(['// see ' + F + 'md', '``md', 'title "' + F + '"']), []);
    });

    it('finds none in a score without one', () => {
        assert.deepEqual(at(['part p { clef treble }', 'section A { p { c4 d e f | } }']), []);
    });
});

describe('Enter in a list of a page text', () => {
    it('begins the next item with the same bullet', () => {
        assert.deepEqual(enter([F + 'md', '- first time|', F]), [F + 'md', '- first time', '- |', F]);
        assert.deepEqual(enter([F + 'md', '* a|', F]), [F + 'md', '* a', '* |', F]);
        assert.deepEqual(enter([F + 'md', '+ a|', F]), [F + 'md', '+ a', '+ |', F]);
    });

    it('counts a numbered list on, with the delimiter written', () => {
        assert.deepEqual(enter([F + 'md', '1. one|', F]), [F + 'md', '1. one', '2. |', F]);
        assert.deepEqual(enter([F + 'md', '9) nine|', F]), [F + 'md', '9) nine', '10) |', F]);
    });

    it('keeps the item\'s indentation, in a score and in a nested list', () => {
        assert.deepEqual(enter(['score {', '  ' + F + 'md', '  - a|', '  ' + F, '}']),
            ['score {', '  ' + F + 'md', '  - a', '  - |', '  ' + F, '}']);
        assert.deepEqual(enter([F + 'md', '- a', '  - b|', F]), [F + 'md', '- a', '  - b', '  - |', F]);
    });

    it('takes what stood right of the caret to the new item, and keeps the blanks left of it', () => {
        assert.deepEqual(enter([F + 'md', '- first| time', F]), [F + 'md', '- first', '- |time', F]);
        assert.deepEqual(enter([F + 'md', '- a line break  |', F]), [F + 'md', '- a line break  ', '- |', F]);
    });

    it('ends the list on an item with no words', () => {
        assert.deepEqual(enter([F + 'md', '- a', '- |', F]), [F + 'md', '- a', '|', F]);
        assert.deepEqual(enter([F + 'md', '1. a', '2. |', F]), [F + 'md', '1. a', '|', F]);
    });

    it('…leaving the text\'s own indentation in a score', () => {
        assert.deepEqual(enter(['score {', '  ' + F + 'md', '  - a', '  - |', '  ' + F, '}']),
            ['score {', '  ' + F + 'md', '  - a', '  |', '  ' + F, '}']);
    });

    it('makes an empty nested item an item of the list around it', () => {
        assert.deepEqual(enter([F + 'md', '- a', '  - b', '  - |', F]), [F + 'md', '- a', '  - b', '- |', F]);
        assert.deepEqual(enter([F + 'md', '3. a', '   - b', '   - |', F]), [F + 'md', '3. a', '   - b', '4. |', F]);
    });

    it('leaves Enter to VS Code everywhere else in the text', () => {
        assert.equal(enter([F + 'md', 'A paragraph.|', F]), null);
        assert.equal(enter([F + 'md', '- a', '  wrapped|', F]), null, 'an item\'s later line');
        assert.equal(enter([F + 'md', '|- a', F]), null, 'the caret before the marker');
        assert.equal(enter([F + 'md', '-| a', F]), null, 'the caret inside the marker');
        assert.equal(enter([F + 'md', '-word|', F]), null, 'no blank after the marker: a word');
        assert.equal(enter([F + 'md', '# Heading|', F]), null);
    });

    it('never answers outside a text\'s body', () => {
        assert.equal(enter(['- a|']), null, 'no text at all');
        assert.equal(enter([F + 'md', 'x', F, '- a|']), null, 'after the closing fence');
        assert.equal(enter(['- a|', F + 'md', 'x', F]), null, 'before the opening one');
        assert.equal(enter(['section A {', '  p { c4 d e f', '  1. e|', '}']), null, 'music');
    });

    it('answers the context key off the one line', () => {
        assert.ok(onListItem('- a', 3));
        assert.ok(onListItem('- a', 2));
        assert.ok(onListItem('  12. a', 6));
        assert.ok(!onListItem('- a', 1));
        assert.ok(!onListItem('a - b', 5));
        assert.ok(!onListItem('c4 d e f |', 4));
        assert.ok(!onListItem('', 0));
    });
});

describe('a selection surrounded in a page text', () => {
    it('is surrounded by an asterisk, an underscore and a code mark, and stays selected', () => {
        assert.deepEqual(surroundPlan('*', 'four voices'), { insert: 'four voices*', select: 11 });
        assert.deepEqual(surroundPlan('_', 'mf'), { insert: 'mf_', select: 2 });
        assert.deepEqual(surroundPlan('`', 'c4'), { insert: 'c4`', select: 2 });
    });

    it('is not by any other character, nor when nothing was selected', () => {
        for (const ch of ['~', '$', 'a', '4', ' ', '**', '']) {
            assert.ok(!surrounds(ch), ch);
            assert.equal(surroundPlan(ch, 'x'), null);
        }
        assert.equal(surroundPlan('*', ''), null);
    });
});
