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

// The editor-free half of "Show Original Scan" (LilySharp-Omr proposal B3): the side file as
// the OMR reader writes it today (its Sidecar.Write), and the box a caret answers.

import { describe, it } from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import * as vm from 'node:vm';
import {
    anchorsOf, barOnLine, parseSideFile, scanBarForPrinted, shiftAnchors, shownImage, sideFileOf, todoTarget,
} from '../src/omrScanCore';

// Absolute on whichever OS runs the tests.
const SCANS = path.resolve('scans');
const WORK = path.resolve('work');

// What OmrProto writes (LilySharp-Omr src/OmrProto/Sidecar.cs), trimmed.
const WRITTEN = JSON.stringify({
    version: 1, omr: 'LilySharp-Omr OmrProto', lys: 'omr.lys',
    pages: [
        { page: 1, image: path.join(SCANS, 'p01.png'), width: 2480, height: 3508 },
        { page: 2, image: 'p02.tif', width: 2480, height: 3508 },
    ],
    measures: [
        { measure: 4, part: 'rh', line: 32, page: 1, box: [1292, 900, 2223, 1040] },
        { measure: 4, part: 'lh', line: 33, page: 1, box: [1292, 1046, 2223, 1203] },
        { measure: 5, part: 'rh', line: 34, page: null, box: null },
    ],
    todos: [
        { key: 'b4-lh', kind: 'bar', source: 'verify', measure: 4, part: 'lh', lines: [33], page: 1,
          box: [1292, 1046, 2223, 1203], memo: 'bar 4: voices 600+1880/2400 ticks', candidates: [] },
        { key: 'b9', kind: 'bar', measure: 9, part: null, page: null, box: null, memo: 'bar 9: …' },
    ],
});

describe('the core is editor-free', () => {
    it('imports nothing from vscode', () => {
        const src = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'omrScanCore.ts'), 'utf8');
        assert.doesNotMatch(src, /from 'vscode'/);
    });
});

describe('the scan page script', () => {
    // It lives in a template literal in omrScan.ts, so neither tsc nor esbuild parses it (as
    // the preview's — webviewScript.test.ts). It carries no substitution and no escape.
    it('parses', () => {
        const src = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'omrScan.ts'), 'utf8');
        const open = src.indexOf('<script nonce=');
        const body = src.slice(src.indexOf('>', open) + 1, src.indexOf('</script>', open));
        assert.doesNotMatch(body, /\$\{|\\/);
        assert.doesNotThrow(() => new vm.Script(body));
    });
});

describe('the side file', () => {
    const sidePath = path.join(WORK, 'omr.omr.json');
    const side = parseSideFile(WRITTEN, sidePath)!;

    it('is the .lys name with .omr.json', () => {
        assert.equal(sideFileOf('C:\\a\\Song.lys'), 'C:\\a\\Song.omr.json');
    });

    it('reads what the reader writes, dropping what cannot be drawn', () => {
        assert.equal(side.pages.length, 2);
        assert.equal(side.pages[0].image, path.join(SCANS, 'p01.png'));
        assert.equal(side.pages[1].image, path.join(WORK, 'p02.tif'));   // relative: the side file's folder
        assert.deepEqual(side.measures.map(m => `${m.measure}${m.part}`), ['4rh', '4lh']);
        assert.deepEqual(side.todos.map(t => t.key), ['b4-lh']);
        assert.equal(side.todos[0].memo, 'bar 4: voices 600+1880/2400 ticks');
    });

    it('is no file when it is not one this reads', () => {
        assert.equal(parseSideFile('not json', sidePath), undefined);
        assert.equal(parseSideFile(JSON.stringify({ version: 2, pages: [] }), sidePath), undefined);
        assert.ok(parseSideFile('\uFEFF' + WRITTEN, sidePath));   // a BOM is not a reason
    });

    it('shows a page from an image a webview draws, else from the annotated copy', () => {
        assert.equal(shownImage(side.pages[0]), path.join(SCANS, 'p01.png'));
        assert.equal(shownImage(side.pages[1]), undefined);
        assert.equal(shownImage({ ...side.pages[1], annotated: 'C:\\o\\x.omr-p02.png' }), 'C:\\o\\x.omr-p02.png');
    });
});

describe('the scan beside the score (B4)', () => {
    // The reader's Chopin Op.28/4: its bar 19 is written too long, so the page prints it as
    // 19 and 20, and the reader's bar 20 prints as 21. Two parts per bar.
    const bars = [
        { index: 0, printed: 18 }, { index: 1, printed: 18 },
        { index: 2, printed: 19 }, { index: 3, printed: 19 },
        { index: 4, printed: 21 }, { index: 5, printed: 21 },
        { index: 6, printed: null },
    ];

    it('shows the first bar that prints as the score\'s', () => {
        assert.equal(scanBarForPrinted(bars, 19), 2);
        assert.equal(scanBarForPrinted(bars, 21), 4);
    });

    it('shows the bar before for a bar only the page has', () => {
        assert.equal(scanBarForPrinted(bars, 20), 2);     // the second half of the reader's 19
        assert.equal(scanBarForPrinted(bars, 99), 4);
        assert.equal(scanBarForPrinted(bars, 1), undefined);
    });
});

describe('what the caret lights', () => {
    const side = parseSideFile(WRITTEN, path.join(WORK, 'omr.omr.json'))!;
    const marks = [{ Key: 'b4-lh', HostStart: 100, HostEnd: 130 }, { Key: null, HostStart: 200, HostEnd: 210 }];

    it('is the mark the caret is on, by its key', () => {
        assert.deepEqual(todoTarget(side, marks, 100), { kind: 'todo', page: 1, box: [1292, 1046, 2223, 1203], label: 'b4-lh' });
        assert.ok(todoTarget(side, marks, 130));
        assert.equal(todoTarget(side, marks, 131), undefined);
        assert.equal(todoTarget(side, marks, 205), undefined);   // a mark without a key has no box
    });

    // The .lys as written: 40 lines "l1".."l40"; the side file puts bar 4 on lines 32 (rh) and 33 (lh).
    const text = Array.from({ length: 40 }, (_, i) => `l${i + 1}`).join('\n') + '\n';
    const lineOf = (t: string, n: number) => {
        const start = t.indexOf(`l${n}\n`);
        return [start, start + `l${n}`.length] as const;
    };

    it('else the bar written on its line', () => {
        const anchors = anchorsOf(side, text);
        assert.equal(anchors.length, 2);                          // bar 5 has a line but no box: not drawn
        assert.deepEqual(barOnLine(anchors, ...lineOf(text, 33))?.box, [1292, 1046, 2223, 1203]);
        assert.deepEqual(barOnLine(anchors, ...lineOf(text, 32))?.label, '4');
        assert.equal(barOnLine(anchors, ...lineOf(text, 31)), undefined);
    });

    it('follows the bars through the edits', () => {
        const anchors = anchorsOf(side, text);
        // Two lines typed at the top: the bars are two lines down.
        let edited = 'a\nb\n' + text;
        shiftAnchors(anchors, [{ offset: 0, removed: 0, inserted: 4 }]);
        assert.equal(barOnLine(anchors, ...lineOf(edited, 33))?.box[1], 1046);
        // Typing at the head of the lh line keeps it the lh line.
        const [s33] = lineOf(edited, 33);
        edited = edited.slice(0, s33) + '  ' + edited.slice(s33);
        shiftAnchors(anchors, [{ offset: s33, removed: 0, inserted: 2 }]);
        const at = edited.indexOf('  l33');
        assert.equal(barOnLine(anchors, at, at + 5)?.box[1], 1046);
        // The rh line deleted: its line is gone, and the line that takes its place is lh.
        const [s32] = lineOf(edited, 32);
        shiftAnchors(anchors, [{ offset: s32, removed: 'l32\n'.length, inserted: 0 }]);
        edited = edited.slice(0, s32) + edited.slice(s32 + 4);
        const at2 = edited.indexOf('  l33');
        assert.equal(barOnLine(anchors, at2, at2 + 5)?.box[1], 1046);
    });
});
