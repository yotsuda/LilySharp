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
    SCAN_DEFAULT_THRESHOLD, anchorsOf, barOnLine, markFactsOf, pageLabel, parseSideFile, scanBarForPrinted, scanTitle,
    flaggedBars, hiddenTodoKeys, resolvedTodoKeys, shiftAnchors, shownAt, shownImage, sideFileOf, barStaff, todoStroke, todoTarget,
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
        // (a reader since LilySharp-Omr c22fe99 writes the staff too; the rh bar is an older one's)
        { measure: 4, part: 'lh', line: 33, page: 1, box: [1292, 1046, 2223, 1203], staff: [1292, 1085, 2223, 1164] },
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

// What the scan tints, as the preview tints a bar's staff: the reader's staff (LilySharp-Omr
// c22fe99), or from an older reader, which drew a part's bar two staff spaces past its outer lines
// (Sidecar.AddPage), the box's middle half.
describe('a bar\'s staff on the scan', () => {
    it('is the reader\'s staff where it wrote one', () => {
        assert.deepEqual(barStaff({ box: [472, 518, 1003, 684], staff: [472, 559, 1003, 643] }), [472, 559, 1003, 643]);
    });
    it('is the middle half of the box, its whole width, from an older reader', () => {
        // Largo p.1 bar 1 (the owner's scan): box 518..684, the lines at 559..643.
        const [x0, y0, x1, y1] = barStaff({ box: [472, 518, 1003, 684] });
        assert.deepEqual([x0, x1], [472, 1003]);
        assert.ok(Math.abs(y0 - 559.5) < 1 && Math.abs(y1 - 642.5) < 1, `${y0}..${y1}`);
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
        assert.deepEqual(side.measures.map(m => m.staff), [undefined, [1292, 1085, 2223, 1164]]);
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

describe('the scan view names the originals (LilySharp-Omr proposal 2026-10-12)', () => {
    const sidePath = path.join(WORK, 'score.omr.json');
    const side = (pages: object[]) => parseSideFile(JSON.stringify({ version: 1, pages, measures: [], todos: [] }), sidePath)!;
    const pdf = path.join(SCANS, 'score.pdf');

    it('reads a page\'s source and its page in it', () => {
        const s = side([
            { page: 1, image: 'score.p01.png', width: 10, height: 10, source: pdf, sourcePage: 1 },
            { page: 2, image: 'score.p03.png', width: 10, height: 10, source: 'score.pdf', sourcePage: 3 },
            { page: 3, image: path.join(SCANS, 'page1.jpg'), width: 10, height: 10 },
        ]);
        assert.equal(s.pages[0].source, pdf);
        assert.equal(s.pages[1].source, path.join(WORK, 'score.pdf'));   // resolved as image is
        assert.equal(s.pages[1].sourcePage, 3);
        assert.equal(s.pages[2].source, undefined);
        assert.equal(pageLabel(s.pages[1]), 'score.pdf p.3');
        assert.equal(pageLabel(s.pages[2]), 'page1.jpg');
    });

    it('titles the tab by the originals, each once', () => {
        const lys = path.join(WORK, 'score.lys');
        const pdfPage = (n: number) => ({ page: n, image: `score.p0${n}.png`, width: 10, height: 10, source: pdf, sourcePage: n });
        const jpg = (n: number) => ({ page: n, image: path.join(SCANS, `page${n}.jpg`), width: 10, height: 10 });
        assert.equal(scanTitle(side([pdfPage(1), pdfPage(2), pdfPage(3)]).pages, lys), 'Scan: score.pdf');
        assert.equal(scanTitle(side([jpg(1)]).pages, lys), 'Scan: page1.jpg');
        assert.equal(scanTitle(side([jpg(1), jpg(2)]).pages, lys), 'Scan: page1.jpg, page2.jpg');
        assert.equal(scanTitle(side([jpg(1), jpg(2), jpg(3)]).pages, lys), 'Scan: page1.jpg +2');
        assert.equal(scanTitle([], lys), 'Scan: score.lys');
    });
});

describe('how sure the reader is (LilySharp-Omr reply 2026-10-12: a confidence on every todo)', () => {
    const side = parseSideFile(JSON.stringify({
        version: 1, pages: [{ page: 1, image: 'p.png', width: 10, height: 10 }], measures: [],
        todos: [
            { key: 'b3', page: 1, box: [0, 0, 5, 5], confidence: 0.2, inSource: null },
            { key: 'b4', page: 1, box: [0, 0, 5, 5], confidence: 0.85, inSource: false },
            { key: 'o9', page: 1, box: [0, 0, 5, 5] },
        ],
    }), path.join(WORK, 'x.omr.json'))!;

    it('reads the confidence, and inSource: false as a mark the .lys does not carry', () => {
        assert.deepEqual(side.todos.map(t => [t.key, t.confidence, t.quiet]),
            [['b3', 0.2, undefined], ['b4', 0.85, true], ['o9', undefined, undefined]]);
    });

    it('draws the less sure heavier and darker, a quiet mark faint, a mark with no value as the least sure', () => {
        assert.deepEqual(todoStroke({ confidence: 0 }), { width: 2.5, opacity: 1 });
        assert.deepEqual(todoStroke({ confidence: 0.8 }), { width: 1.3, opacity: 0.48 });
        assert.deepEqual(todoStroke({}), todoStroke({ confidence: 0 }));
        assert.deepEqual(todoStroke({ confidence: 0.1, quiet: true }), { width: 1, opacity: 0.35 });
        assert.ok(todoStroke({ confidence: 0.2 }).width > todoStroke({ confidence: 0.6 }).width);
    });

    it('starts the slider at the reader\'s own threshold and shows what is below it', () => {
        assert.equal(SCAN_DEFAULT_THRESHOLD, 0.8);
        assert.deepEqual(side.todos.filter(t => shownAt(t, SCAN_DEFAULT_THRESHOLD)).map(t => t.key), ['b3', 'o9']);
        assert.deepEqual(side.todos.filter(t => shownAt(t, 0.1)).map(t => t.key), ['o9']);
    });

    it('tells the preview which marks the slider hides, by key, and none at the top', () => {
        assert.deepEqual(hiddenTodoKeys(side.todos, SCAN_DEFAULT_THRESHOLD), ['b4']);
        assert.deepEqual(hiddenTodoKeys(side.todos, 0.1), ['b3', 'b4']);
        assert.deepEqual(hiddenTodoKeys([...side.todos, { key: 'sure', confidence: 1 }], 1), []);
        assert.deepEqual(hiddenTodoKeys([{ key: 'b2', confidence: 0.5 }, { key: 'b2', confidence: 0.7 }], 0.3), ['b2']);
    });

    it('frames on the preview only the bars whose marks the slider shows', () => {
        const server = [
            { Key: 'b3', HostStart: 0, HostEnd: 1, Measure: 2 },
            { Key: 'b4', HostStart: 2, HostEnd: 3, Measure: 3 },
            { Key: 'b4', HostStart: 4, HostEnd: 5, Measure: 4 },
            { Key: null, HostStart: 6, HostEnd: 7, Measure: 3 },     // written by hand
            { Key: 'o9', HostStart: 8, HostEnd: 9, Measure: null },  // no score plays it
        ];
        assert.deepEqual(flaggedBars(server, []), [2, 3, 4]);
        assert.deepEqual(flaggedBars(server, hiddenTodoKeys(side.todos, SCAN_DEFAULT_THRESHOLD)), [2, 3]);
        assert.deepEqual(flaggedBars(server, ['b3', 'b4']), [3]);
    });

    it('calls a mark resolved once the .lys no longer carries it — never a quiet one', () => {
        const at = (...keys: (string | null)[]) => keys.map((Key, i) => ({ Key, HostStart: i, HostEnd: i + 1, Measure: 1 }));
        // b3 and o9 are in the .lys; b4 is quiet (never written there).
        assert.deepEqual(resolvedTodoKeys(side.todos, at('b3', 'o9')), []);
        assert.deepEqual(resolvedTodoKeys(side.todos, at('o9', null)), ['b3']);
        assert.deepEqual(resolvedTodoKeys(side.todos, []), ['b3', 'o9']);
    });
});

describe('the readings of a mark (B6)', () => {
    it('reads each keyed todo\'s confidence and candidates, boxed or not', () => {
        const facts = markFactsOf(JSON.stringify({
            version: 1,
            todos: [
                { key: 'o1203', confidence: 0.62, candidates: [{ label: 'F♯', text: "fis'8" }, { text: "f'8" }, { label: 'x' }] },
                { key: 'b9', page: null, box: null, candidates: [] },
                { candidates: [{ text: 'c4' }] },
            ],
        }));
        assert.deepEqual([...facts.keys()], ['o1203', 'b9']);
        assert.deepEqual(facts.get('o1203'), { confidence: 0.62, candidates: [{ label: 'F♯', text: "fis'8" }, { text: "f'8" }] });
        assert.deepEqual(facts.get('b9'), { candidates: [] });
    });

    it('is nothing for what is not a side file', () => {
        assert.equal(markFactsOf('{').size, 0);
        assert.equal(markFactsOf(JSON.stringify({ version: 9, todos: [{ key: 'a' }] })).size, 0);
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
        // The scan lights the staff: the reader's where it wrote one, else the box's middle half.
        assert.deepEqual(barOnLine(anchors, ...lineOf(text, 33))?.staff, [1292, 1085, 2223, 1164]);
        assert.deepEqual(barOnLine(anchors, ...lineOf(text, 32))?.staff, [1292, 935, 2223, 1005]);
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
