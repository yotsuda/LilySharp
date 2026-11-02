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

import { test } from 'node:test';
import * as assert from 'node:assert/strict';
import { STAFF_GROUPS, staffGroupWrap } from '../src/staffGroupWrapCore';

/** The document with the selection written «…», after wrapping it in `group`; null when not offered. */
function wrapped(marked: string, group = 'grandStaff', tab = '    '): string | null {
    const lo = marked.indexOf('«');
    const hi = marked.indexOf('»') - 1;
    const text = marked.replace('«', '').replace('»', '');
    const w = staffGroupWrap(text, lo, hi, group, tab);
    return w && text.slice(0, lo) + w.head + w.tail + text.slice(hi);
}

const WANTED = '  score {\n    grandStaff {\n      staff melody\n      staff melody\n    }\n  }\n';

test('the owner\'s example, however the two lines were selected', () => {
    assert.equal(wrapped('  score {\n    «staff melody\n    staff melody»\n  }\n'), WANTED);
    // From the start of the line, indentation and all.
    assert.equal(wrapped('  score {\n«    staff melody\n    staff melody»\n  }\n'), WANTED);
    // Whole lines, the last line's end with them, as Shift+Down selects.
    assert.equal(wrapped('  score {\n«    staff melody\n    staff melody\n»  }\n'), WANTED);
    // On past the next line's indentation.
    assert.equal(wrapped('  score {\n    «staff melody\n    staff melody\n  »}\n'), WANTED);
    // Selected from the first line's middle of its indentation.
    assert.equal(wrapped('  score {\n  «  staff melody\n    staff melody»\n  }\n'), WANTED);
});

test('the rows keep what they say and their own depth; CRLF is left to the editor', () => {
    assert.equal(wrapped('score {\n  «staff a with chords p\n    staff b\n\n  // low\n  lyrics w\n  staff c»\n}'),
        'score {\n  grandStaff {\n    staff a with chords p\n      staff b\n\n    // low\n    lyrics w\n    staff c\n  }\n}');
    assert.equal(wrapped('score {\r\n  «staff a\r\n  staff b»\r\n}'), 'score {\r\n  grandStaff {\n    staff a\n    staff b\n  }\r\n}');
});

test('the indent is the rows\' depth under the score, or the editor\'s where they show none', () => {
    assert.equal(wrapped('score {\n\t«staff a\n\tstaff b»\n}'), 'score {\n\tgrandStaff {\n\t\tstaff a\n\t\tstaff b\n\t}\n}');
    assert.equal(wrapped('score {\n«staff a\nstaff b»\n}', 'grandStaff', '   '), 'score {\ngrandStaff {\n   staff a\n   staff b\n}\n}');
});

test('on one line it stays on one line', () => {
    assert.equal(wrapped('score { «staff a  staff b» }'), 'score { grandStaff { staff a  staff b } }');
    assert.equal(wrapped('score full { «staff a staff b» }'), 'score full { grandStaff { staff a staff b } }');
});

test('every group that takes staff rows, in a named score too', () => {
    for (const group of STAFF_GROUPS) {
        assert.equal(wrapped('score winds {\n  «staff a\n  staff b»\n}', group), `score winds {\n  ${group} {\n    staff a\n    staff b\n  }\n}`);
    }
});

test('not offered for anything else', () => {
    assert.equal(wrapped('score {\n  «staff a»\n}'), null, 'one staff is no group');
    assert.equal(wrapped('score {\n  grandStaff {\n    «staff a\n    staff b»\n  }\n}'), null, 'already in a group');
    assert.equal(wrapped('score {\n  «grandStaff { staff a }\n  staff b\n  staff c»\n}'), null, 'a block among them');
    assert.equal(wrapped('score {\n  «staff a\n  tab b\n  staff c»\n}'), null, 'a row a group cannot hold');
    assert.equal(wrapped('section A {\n  «staff a\n  staff b»\n}'), null, 'not a score');
    assert.equal(wrapped('«staff a\nstaff b»'), null, 'the top level');
    assert.equal(wrapped('score {\n  sta«ff a\n  staff b»\n}'), null, 'half a line at the start');
    assert.equal(wrapped('score {\n  «staff a\n  staff» b\n}'), null, 'half a line at the end');
    assert.equal(wrapped('score {\n  «c4 d e f»\n}'), null);
});
