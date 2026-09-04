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

// What one smart keystroke costs to DECIDE, per key, with the caret at the end
// of the last bar of a long `.lys` — the worst place for a walk that reads
// from the block's start. Run with `npm run bench` (the repository's
// audit/lpreg/perf-*1k.lys books by default; pass other files as arguments).
//
// Only the core's decision is timed: VS Code's getText(), the snippet edit and
// the round trips are not in these numbers. The figures that led to walkStart
// (2026-09-04) were 8–25 ms per key here, against well under a millisecond
// for the keys that were already measure-local ('<', '>').

import * as fs from 'node:fs';
import * as path from 'node:path';
import { insertionPlan, planFor } from '../src/smartTypingCore';

const DEFAULT_BOOKS = ['perf-slurdot1k.lys', 'perf-fingbeam1k.lys', 'perf-scripts1k.lys']
    .map(f => path.join(__dirname, '..', '..', '..', '..', 'audit', 'lpreg', f));
const books = process.argv.length > 2 ? process.argv.slice(2) : DEFAULT_BOOKS;

/** The end of the last note core before the block's last barline. */
function caretAtLastNote(text: string): number {
    let p = text.lastIndexOf('|');
    while (p > 0 && !/[a-g0-9'.,]/.test(text[p - 1])) { p--; }
    return p;
}

function millis(fn: () => void, reps = 20): number {
    fn(); // warm up
    const t0 = process.hrtime.bigint();
    for (let i = 0; i < reps; i++) { fn(); }
    return Number(process.hrtime.bigint() - t0) / 1e6 / reps;
}

for (const book of books) {
    const text = fs.readFileSync(book, 'utf8');
    const offset = caretAtLastNote(text);
    const around = text.slice(Math.max(0, offset - 24), offset) + '‸' + text.slice(offset, offset + 6);
    console.log(`\n${path.basename(book)}: ${text.length} chars, caret ${offset} …${JSON.stringify(around)}`);
    const rows: [string, number][] = [];
    for (const key of ["'", ',', '.', '\\', '@', '4']) {
        rows.push([`planFor(${key})`, millis(() => planFor(key, text, offset))]);
    }
    for (const key of ['(', '()', ')', '[', ']', '~', '<', '>']) {
        const withKey = text.slice(0, offset) + key + text.slice(offset);
        rows.push([`insertionPlan(${key})`, millis(() => insertionPlan(key, withKey, offset))]);
    }
    for (const [name, ms] of rows) { console.log(`  ${name.padEnd(20)} ${ms.toFixed(2).padStart(7)} ms`); }
}
