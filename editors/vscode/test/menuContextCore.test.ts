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

import { describe, it } from 'node:test';
import * as assert from 'node:assert/strict';
import { caretMenuContext, fileMenuContext, selectionSpansSections } from '../src/menuContextCore';

// Owner's decision 2026-11-17: the right-click menu's top shows what acts on the thing under the caret.
describe('the menu context of the caret', () => {
    const line = '    D | D/F# | G | pageBreak G | break  // a | comment';
    const at = (s: string, k = 0) => caretMenuContext(line, line.indexOf(s) + k);
    it('is a bar line on one, just after one, and on a break', () => {
        assert.equal(at('| D/F#').onBarLine, true);
        assert.equal(at('| D/F#', 1).onBarLine, true);
        assert.equal(at('pageBreak', 4).onBarLine, true);
        assert.equal(at('break  //', 2).onBarLine, true);
    });
    it('is not inside a chord, nor in a comment', () => {
        assert.equal(at('D/F#', 2).onBarLine, false);
        assert.equal(at('a | comment', 2).onBarLine, false);
    });
    it('is a section head on `section A`, up to its name', () => {
        assert.equal(caretMenuContext('section A2 {', 3).onSectionHead, true);
        assert.equal(caretMenuContext('section A2 {', 9).onSectionHead, true);
        assert.equal(caretMenuContext('section A2 { bass { c1 | } }', 22).onSectionHead, false);
        assert.equal(caretMenuContext('  sectionA', 3).onSectionHead, false);
    });
});

describe('a selection across sections', () => {
    const text = 'section A {\n  a { c1 | }\n}\nsection B {\n  a { d1 | }\n}\n';
    it('reaches two heads or more', () => {
        assert.equal(selectionSpansSections(text, 0, text.indexOf('section B') + 3), true);
        assert.equal(selectionSpansSections(text, 2, text.indexOf('d1')), true);
        assert.equal(selectionSpansSections(text, text.indexOf('c1'), text.indexOf('d1')), false);
        assert.equal(selectionSpansSections(text, 5, 5), false);
    });
});

describe('what the file holds for Clean Up', () => {
    it('reads marks outside comments and strings', () => {
        assert.deepEqual(fileMenuContext('chords h { F@or(Bm) | }'), { hasOrMarks: true, hasTodos: false, hasStringNumbers: false });
        assert.deepEqual(fileMenuContext('// F@or(Bm) @todo\nbass { d,8\\3 | } c@todo("x")'), { hasOrMarks: false, hasTodos: true, hasStringNumbers: true });
        assert.deepEqual(fileMenuContext('text "@or(" c1 |'), { hasOrMarks: false, hasTodos: false, hasStringNumbers: false });
    });
});
