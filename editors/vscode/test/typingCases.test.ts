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
import * as fs from 'node:fs';
import * as path from 'node:path';
import { STAFF_GROUPS, staffGroupWrap } from '../src/staffGroupWrapCore';
import { show, typeKey, typeOverSelection } from './simulate';

// TYPING-TEST-CASES.md is what a person walks through by hand, so it has to be what the keys
// do: every row it marks T, S or W is replayed here. Written once from memory it would be
// wrong (docs/HANDOFF.md, session 1043: the teaching path said so of `[`).

const RELATIVE = path.join(__dirname, '..', '..', 'TYPING-TEST-CASES.md');
const ROW = /^\|\s*([TSW])(\d+)\s*\|\s*`(.+?)`\s*\|\s*`(.+?)`\s*\|\s*`(.+?)`\s*\|/;
const NONE = '（なし）';

/** A cell's code as the document means it: GFM's `\|` is a bar line, and `\n` a new line. */
const cell = (s: string) => s.replace(/\\\|/g, '|').replace(/\\n/g, '\n');

interface Row { id: string, kind: string, before: string, key: string, after: string }
const rows: Row[] = fs.readFileSync(RELATIVE, 'utf8').split(/\r?\n/).flatMap(line => {
    const m = ROW.exec(line);
    return m ? [{ id: m[1] + m[2], kind: m[1], before: cell(m[3]), key: m[4], after: cell(m[5]) }] : [];
});

test('the table is read: a row that no longer parses would pass in silence', () => {
    assert.ok(rows.filter(r => r.kind === 'T').length >= 40, `T rows: ${rows.filter(r => r.kind === 'T').length}`);
    assert.ok(rows.filter(r => r.kind === 'S').length >= 16, `S rows: ${rows.filter(r => r.kind === 'S').length}`);
    assert.ok(rows.filter(r => r.kind === 'W').length >= 15, `W rows: ${rows.filter(r => r.kind === 'W').length}`);
    assert.equal(new Set(rows.map(r => r.id)).size, rows.length, 'every number once');
    const table = fs.readFileSync(RELATIVE, 'utf8').split(/\r?\n/).filter(l => /^\|\s*[TSW]\d+/.test(l));
    assert.equal(table.length, rows.length, 'every row of a table is one the replay reads');
});

for (const r of rows) {
    test(`${r.id}: ${r.before} + ${r.key}`, () => {
        if (r.kind === 'T') { assert.equal(show(typeKey(r.before, r.key)), r.after); }
        if (r.kind === 'S') { assert.equal(show(typeOverSelection(r.before, r.key)), r.after); }
        if (r.kind === 'W') {
            assert.ok((STAFF_GROUPS as readonly string[]).includes(r.key), r.key);
            const lo = r.before.indexOf('«');
            const hi = r.before.indexOf('»') - 1;
            const text = r.before.replace('«', '').replace('»', '');
            const w = staffGroupWrap(text, lo, hi, r.key, '  ');
            assert.equal(w ? text.slice(0, lo) + w.head + w.tail + text.slice(hi) : NONE, r.after);
        }
    });
}
