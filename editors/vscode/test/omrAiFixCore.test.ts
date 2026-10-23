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

// The editor-free half of "Fix OMR marks with AI…" (docs/omr-ai-fix-design.md M1): the bars to
// fix and their order, the prompt, the reading of the answer, the gates, the crop, the apply.

import { describe, it } from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import {
    Diag, FixableTodo, Rgba, acceptableByAll, anchorOf, averageOf, barBoxFor, batchesOf, checkedByDefault, cropRect, cropRgba,
    decodeImage, diagnosticsOn, encodePng, estimateOf, fixTargets, gateOf, headOf, labelOf, neighboursOf, parseProposal,
    repairPromptOf, scanCropPng, spliceBar, stripTodoMarks, systemPromptOf, targetsInLines, taskPromptOf, withSample,
} from '../src/omrAiFixCore';
import * as jpeg from 'jpeg-js';
import { markFactsOf, parseSideFile } from '../src/omrScanCore';

// A score as an OMR reader writes one: a part's bar on a line of its own, a @todo on the first
// event of a doubtful bar (LilySharp-Omr Sidecar.Annotate), two parts.
const LYS = [
    '// Read by OmrProto from page images 1-2.',
    'title "scan"',
    'octave absolute',
    'time 4/4',
    'key bes major',
    '',
    'part p1 { clef treble }',
    'part p2 { clef bass }',
    '',
    'section Piece {',
    '  p1 {',
    '    r2 d8 f4 a8 |',                                                                    // line 12 (0-based 11): bar 1
    '    break tuplet 3/2 { g8@todo(b2-p1 "bar 2: 1760/1920 ticks") f8~ } f2 r4 |',         // line 13: bar 2
    '    c\'4 bes8 a8~ a8 bes8 f8 f8~ |',                                                   // line 14: bar 3
    '  }',
    '  p2 {',
    '    bes,1 |',                                                                           // line 17: bar 1
    '    f,2 f,4@todo(b2-p2 "bar 2: 1440/1920 ticks") |',                                    // line 18: bar 2
    '    bes,4@todo("check the slur") r4 bes,2 |',                                           // line 19: bar 3, by hand
    '  }',
    '}',
    '',
    'score { staff p1 staff p2 }',
    '',
].join('\n');

const offsetOf = (s: string) => { const i = LYS.indexOf(s); assert.ok(i >= 0, s); return i; };

// The marks as lilysharp/todos lists them (HostStart = the event the mark is on).
const TODOS: FixableTodo[] = [
    { Key: 'b2-p1', Memo: 'bar 2: 1760/1920 ticks', HostStart: offsetOf('g8@todo(b2-p1'), HostEnd: offsetOf('g8@todo(b2-p1') + 40, Measure: 2, Part: 'p1' },
    { Key: 'b2-p2', Memo: 'bar 2: 1440/1920 ticks', HostStart: offsetOf('f,4@todo(b2-p2'), HostEnd: offsetOf('f,4@todo(b2-p2') + 40, Measure: 2, Part: 'p2' },
    { Key: null, Memo: 'check the slur', HostStart: offsetOf('bes,4@todo("check'), HostEnd: offsetOf('bes,4@todo("check') + 30, Measure: 3, Part: 'p2' },
];

const SIDE_PATH = path.resolve('scans', 'scan.omr.json');
const SIDE_TEXT = JSON.stringify({
    version: 1, omr: 'LilySharp-Omr OmrProto', lys: 'scan.lys',
    pages: [{ page: 1, image: 'p01.png', width: 2400, height: 3500 }],
    measures: [
        { measure: 1, part: 'p1', line: 12, page: 1, box: [300, 1800, 700, 1900] },
        { measure: 2, part: 'p1', line: 13, page: 1, box: [700, 1800, 1100, 1900] },
        { measure: 3, part: 'p1', line: 14, page: 1, box: [1100, 1800, 1500, 1900] },
        { measure: 1, part: 'p2', line: 17, page: 1, box: [300, 2000, 700, 2100] },
        { measure: 2, part: 'p2', line: 18, page: 1, box: [700, 2000, 1100, 2100] },
        { measure: 3, part: 'p2', line: 19, page: 1, box: [1100, 2000, 1500, 2100] },
    ],
    todos: [
        { key: 'b2-p1', kind: 'bar', source: 'length', measure: 2, part: 'p1', lines: [13], page: 1,
          box: [700, 1800, 1100, 1900], memo: 'bar 2: 1760/1920 ticks', confidence: 0.3, candidates: [] },
        { key: 'b2-p2', kind: 'bar', source: 'length', measure: 2, part: 'p2', lines: [18], page: 1,
          box: [700, 2000, 1100, 2100], memo: 'bar 2: 1440/1920 ticks', confidence: 0.9,
          candidates: [{ label: 'dotted', text: 'f,2 f,4.' }] },
    ],
});
const SIDE = parseSideFile(SIDE_TEXT, SIDE_PATH)!;
const FACTS = markFactsOf(SIDE_TEXT);

describe('the core is editor-free', () => {
    it('imports nothing from vscode', () => {
        const src = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'omrAiFixCore.ts'), 'utf8');
        assert.doesNotMatch(src, /from 'vscode'/);
    });
});

describe('the bars to fix', () => {
    const targets = fixTargets(LYS, TODOS, SIDE, FACTS);

    it('are one per marked line, the least sure first — a mark with no confidence before them all', () => {
        assert.deepEqual(targets.map(t => [t.line + 1, t.confidence]), [[19, undefined], [13, 0.3], [18, 0.9]]);
    });

    it('span the line without its indentation, marks included', () => {
        const bar2 = targets.find(t => t.line === 12)!;
        assert.equal(bar2.written, 'break tuplet 3/2 { g8@todo(b2-p1 "bar 2: 1760/1920 ticks") f8~ } f2 r4 |');
        assert.equal(LYS.slice(bar2.start, bar2.end), bar2.written);
        assert.equal(LYS[bar2.start - 1], ' ');
    });

    it('carry the memo, the reader\'s readings, the bar and the crop', () => {
        const p2 = targets.find(t => t.line === 17)!;
        assert.deepEqual(p2.memos, ['bar 2: 1440/1920 ticks']);
        assert.deepEqual(p2.candidates, [{ label: 'dotted', text: 'f,2 f,4.' }]);
        assert.equal(labelOf(p2), 'bar 2 (p2)');
        assert.deepEqual(p2.crop, { page: 1, box: [700, 2000, 1100, 2100] });
    });

    it('place a mark written by hand by its line in the side file\'s bars', () => {
        const hand = targets.find(t => t.line === 18)!;
        assert.deepEqual(hand.keys, []);
        assert.deepEqual(hand.crop, { page: 1, box: [1100, 2000, 1500, 2100] });
        assert.equal(labelOf(hand), 'bar 3 (p2)');
    });

    it('crop a note mark to its bar, not to the heads it boxes', () => {
        const sideText = JSON.stringify({
            version: 1, pages: [{ page: 1, image: 'p01.png', width: 2400, height: 3500 }],
            measures: [{ measure: 2, part: 'p1', line: 13, page: 1, box: [700, 1800, 1100, 1900] }],
            todos: [
                { key: 'o7', kind: 'note', measure: 2, part: 'p1', lines: [13], page: 1, box: [900, 1840, 930, 1860], memo: 'F# or F?', confidence: 0.4 },
                { key: 'o8', kind: 'note', measure: 9, part: 'p1', lines: [40], page: 1, box: [100, 100, 130, 120], memo: 'alone' },
            ],
        });
        const side = parseSideFile(sideText, SIDE_PATH)!;
        const [t] = fixTargets(LYS, [{ Key: 'o7', Memo: 'F# or F?', HostStart: offsetOf('g8@todo(b2-p1'), HostEnd: 0, Measure: 2, Part: 'p1' }], side);
        assert.deepEqual(t.crop, { page: 1, box: [700, 1800, 1100, 1900] });
        // …and the heads when the side file knows no bar for the line.
        const [alone] = fixTargets(LYS, [{ Key: 'o8', Memo: 'alone', HostStart: offsetOf('bes,4@todo("check'), HostEnd: 0, Measure: 3, Part: 'p2' }], side);
        assert.deepEqual(alone.crop, { page: 1, box: [100, 100, 130, 120] });
    });

    it('need no side file: the memo and the line are enough', () => {
        const bare = fixTargets(LYS, TODOS);
        assert.equal(bare.length, 3);
        assert.equal(bare.every(t => t.crop === undefined && t.confidence === undefined), true);
    });

    it('are checked by default below the slider, as the scan view shows them', () => {
        assert.deepEqual(targets.map(t => checkedByDefault(t, 0.8)), [true, true, false]);
        assert.deepEqual(targets.map(t => checkedByDefault(t, 1)), [true, true, true]);
    });

    it('are narrowed to a selection by line', () => {
        assert.deepEqual(targetsInLines(targets, 16, 18).map(t => t.line), [18, 17]);
    });

    it('name a line no score plays by its number', () => {
        const [t] = fixTargets('  c4@todo("x") d e f |', [{ Key: null, Memo: 'x', HostStart: 2, HostEnd: 14, Measure: null }]);
        assert.equal(labelOf(t), 'line 1');
    });
});

describe('what the model is shown', () => {
    const targets = fixTargets(LYS, TODOS, SIDE, FACTS);
    const bar2 = targets.find(t => t.line === 12)!;

    it('is the declarations up to the first section', () => {
        const head = headOf(LYS);
        assert.match(head, /^\/\/ Read by OmrProto/);
        assert.match(head, /part p2 \{ clef bass \}$/);
        assert.doesNotMatch(head, /section/);
    });

    it('is the bar before and after in the same part and the other parts\' bar, from the side file', () => {
        const n = neighboursOf(LYS, bar2, SIDE);
        assert.equal(n.before, 'r2 d8 f4 a8 |');
        assert.equal(n.after, 'c\'4 bes8 a8~ a8 bes8 f8 f8~ |');
        assert.deepEqual(n.others, [{ part: 'p2', text: 'f,2 f,4@todo(b2-p2 "bar 2: 1440/1920 ticks") |' }]);
    });

    it('falls back to the lines beside the bar without a side file, and only when they are bars', () => {
        const [first] = fixTargets(LYS, TODOS.slice(0, 1));
        assert.deepEqual(neighboursOf(LYS, first), { before: 'r2 d8 f4 a8 |', after: 'c\'4 bes8 a8~ a8 bes8 f8 f8~ |', others: [] });
        const hand = fixTargets(LYS, TODOS.slice(2))[0];
        assert.equal(neighboursOf(LYS, hand).after, undefined);   // the line after is `}`
    });

    it('lists the compiler\'s reports on the line only', () => {
        const diags: Diag[] = [
            { Line: 12, Offset: bar2.start + 5, Length: 3, Severity: 'warning', Message: 'Bar 2 is short', Code: 'LYS2001' },
            { Line: 17, Offset: 900, Length: 3, Severity: 'warning', Message: 'elsewhere', Code: 'LYS2001' },
            { Line: 12, Offset: bar2.start + 5, Length: 3, Severity: 'info', Message: 'a mark', Code: 'LYS4026' },
        ];
        assert.deepEqual(diagnosticsOn(diags, bar2), ['warning LYS2001: Bar 2 is short']);
    });

    it('asks for one JSON object and forbids @todo and LilyPond', () => {
        const system = systemPromptOf('GRAMMAR');
        assert.match(system, /<lilysharp-grammar>\nGRAMMAR\n<\/lilysharp-grammar>/);
        assert.match(system, /"bar": null/);
        assert.match(system, /Never write @todo/);
        const task = taskPromptOf({ head: headOf(LYS), target: bar2, neighbours: neighboursOf(LYS, bar2, SIDE), diagnostics: [] });
        assert.match(task, /<bar>\nbreak tuplet 3\/2/);
        assert.match(task, /bar 2: 1760\/1920 ticks/);
        assert.match(task, /480 a quarter note/);
        assert.match(task, /confidence that the line needs no change: 0\.30/);
        assert.doesNotMatch(task, /picture/);
        assert.match(repairPromptOf(['LYS2002: too long']), /LYS2002: too long/);
    });

    it('says what the picture is when the message carries the scan (M2)', () => {
        const task = taskPromptOf({ head: '', target: bar2, neighbours: { others: [] }, diagnostics: [], image: true });
        assert.match(task, /^The picture attached to this message is the scanned page/);
        assert.match(task, /Read the notes, rests, dots, ties, beams and\ntuplets from the picture/);
    });
});

describe('the scan of a bar, for the model', () => {
    // A 40×30 page: white, with a black 10×6 block at (20, 10).
    const page: Rgba = (() => {
        const data = new Uint8Array(40 * 30 * 4).fill(255);
        for (let y = 10; y < 16; y++) {
            for (let x = 20; x < 30; x++) { const i = (y * 40 + x) * 4; data[i] = data[i + 1] = data[i + 2] = 0; }
        }
        return { width: 40, height: 30, data };
    })();
    const px = (img: Rgba, x: number, y: number) => img.data[(y * img.width + x) * 4];

    it('crops the rectangle as it is', () => {
        const c = cropRgba(page, { x: 18, y: 8, width: 14, height: 10 });
        assert.deepEqual([c.width, c.height], [14, 10]);
        assert.equal(px(c, 0, 0), 255);      // (18, 8): white
        assert.equal(px(c, 2, 2), 0);        // (20, 10): the block's corner
        assert.equal(px(c, 11, 7), 0);       // (29, 15): its far corner
        assert.equal(px(c, 12, 8), 255);     // (30, 16): past it
    });

    it('keeps the crop on the page and samples a big one down by a whole factor', () => {
        const c = cropRgba(page, { x: -5, y: -5, width: 100, height: 100 });
        assert.deepEqual([c.width, c.height], [40, 30]);
        const small = cropRgba(page, { x: 0, y: 0, width: 40, height: 30 }, 16);
        assert.deepEqual([small.width, small.height], [13, 10]);   // step 3
        assert.equal(px(small, 7, 4), 0);                           // (21, 12): in the block
        assert.equal(px(small, 0, 0), 255);
    });

    it('reads a PNG and a JPEG back, and writes a PNG the reader can read', () => {
        const png = encodePng(page);
        assert.deepEqual([png[0], png[1], png[2], png[3]], [0x89, 0x50, 0x4E, 0x47]);
        const back = decodeImage(png)!;
        assert.deepEqual([back.width, back.height, px(back, 25, 12), px(back, 0, 0)], [40, 30, 0, 255]);
        const jpg = jpeg.encode({ width: page.width, height: page.height, data: Buffer.from(page.data) }, 95).data;
        const fromJpeg = decodeImage(new Uint8Array(jpg))!;
        assert.deepEqual([fromJpeg.width, fromJpeg.height], [40, 30]);
        assert.ok(px(fromJpeg, 25, 12) < 40 && px(fromJpeg, 2, 2) > 215);   // lossy, but black is black
        assert.equal(decodeImage(new Uint8Array([1, 2, 3, 4, 5, 6, 7, 8, 9])), undefined);
    });

    it('crops a page around a box with the margin, as a PNG', () => {
        const out = scanCropPng(page, [20, 10, 30, 16]);
        // cropRect: pad = 6 * 0.3 = 1.8 → x 18.2..31.8, y 8.2..17.8 → floor/ceil → 18..32 × 8..18
        assert.deepEqual([out.width, out.height], [14, 10]);
        const back = decodeImage(out.png)!;
        assert.equal(px(back, 2, 2), 0);
    });
});

describe('reading the answer', () => {
    const written = 'break tuplet 3/2 { g8@todo(b2-p1 "bar 2: 1760/1920 ticks") f8~ } f2 r4 |';

    it('takes the JSON object, fenced or not', () => {
        const p = parseProposal('```json\n{"bar": "break tuplet 3/2 { g8 f8~ } f2 r4. |", "reason": "a dot is visible", "confidence": 0.8}\n```', written);
        assert.deepEqual(p, { bar: 'break tuplet 3/2 { g8 f8~ } f2 r4. |', reason: 'a dot is visible', confidence: 0.8 });
    });

    it('takes the object out of words around it', () => {
        const p = parseProposal('Here you go: {"bar":"f,2 f,4. |","reason":"dotted","confidence":0.7} — done.', 'f,2 f,4 |');
        assert.equal(p?.bar, 'f,2 f,4. |');
    });

    it('reads null, a missing bar, or an empty one as no proposal', () => {
        assert.deepEqual(parseProposal('{"bar": null, "reason": "the page is blurred", "confidence": 0}', written),
            { bar: null, reason: 'the page is blurred', confidence: 0 });
        assert.equal(parseProposal('{"reason": "?"}', written)?.bar, null);
        assert.equal(parseProposal('{"bar": "   "}', written)?.bar, null);
    });

    it('is nothing when there is no object', () => {
        assert.equal(parseProposal('I cannot help with that.', written), null);
        assert.equal(parseProposal('{"bar": ', written), null);
    });

    it('drops a @todo the model copied, folds the text onto one line and keeps the bar line', () => {
        const p = parseProposal('{"bar": "break tuplet 3/2 { g8@todo(b2-p1 \\"bar 2\\") f8~ }\\n  f2 r4.", "reason": "", "confidence": 1}', written);
        assert.equal(p?.bar, 'break tuplet 3/2 { g8 f8~ } f2 r4. |');
        assert.equal(stripTodoMarks('c4@todo("a (b)").down d4@todo e4@todo(k1)'), 'c4 d4 e4');
    });

    it('calls the written line again, marks aside, unchanged', () => {
        const p = parseProposal('{"bar": "break tuplet 3/2 { g8 f8~ } f2 r4 |", "reason": "looks right", "confidence": 0.9}', written);
        assert.equal(p?.bar, null);
        assert.match(p!.reason, /^unchanged/);
    });

    it('clamps the confidence and tolerates its absence', () => {
        assert.equal(parseProposal('{"bar": "c4 d e f |", "confidence": 7}', 'c4 d e |')?.confidence, 1);
        assert.equal(parseProposal('{"bar": "c4 d e f |"}', 'c4 d e |')?.confidence, null);
    });

    it('splices the proposal in place of the line', () => {
        const [t] = fixTargets(LYS, TODOS.slice(0, 1));
        const text = spliceBar(LYS, t, 'break tuplet 3/2 { g8 f8~ } f2 r4. |');
        assert.match(text, /\n    break tuplet 3\/2 \{ g8 f8~ \} f2 r4\. \|\n    c'4 bes8/);
        assert.equal(text.length, LYS.length - written.length + 'break tuplet 3/2 { g8 f8~ } f2 r4. |'.length);
    });
});

describe('the gates', () => {
    const span = { start: 100, length: 30 };
    const d = (Offset: number, Severity: string, Code: string | null, Message = Code ?? 'x'): Diag =>
        ({ Line: 0, Offset, Length: 2, Severity, Message, Code });

    it('pass a proposal that adds no error and fills its bar', () => {
        const before = [d(105, 'warning', 'LYS2001', 'short'), d(900, 'error', 'LYS0001', 'old')];
        const after = [d(900, 'error', 'LYS0001', 'old')];
        assert.deepEqual(gateOf(before, after, span), { compile: true, length: true, problems: [] });
    });

    it('fail compile on an error in the span, and name it', () => {
        const after = [d(110, 'error', 'LYS0002', 'Unknown pitch'), d(900, 'error', 'LYS0001', 'old')];
        const g = gateOf([d(900, 'error', 'LYS0001', 'old')], after, span);
        assert.equal(g.compile, false);
        assert.equal(g.length, true);
        assert.deepEqual(g.problems, ['LYS0002: Unknown pitch']);
    });

    it('fail compile when the file has more errors in all, even outside the span', () => {
        const g = gateOf([], [d(500, 'error', null, 'a bar line is missing')], span);
        assert.equal(g.compile, false);
        assert.deepEqual(g.problems, ['a bar line is missing']);
    });

    it('fail length on a short or long bar in the span, not elsewhere', () => {
        const g = gateOf([], [d(120, 'warning', 'LYS2002', 'long'), d(400, 'warning', 'LYS2001', 'other bar')], span);
        assert.deepEqual([g.compile, g.length, g.problems], [true, false, ['LYS2002: long']]);
        assert.equal(gateOf([], [d(400, 'warning', 'LYS2001', 'other bar')], span).length, true);
    });

    it('let Accept all take a row only when both pass and the model is sure enough', () => {
        const ok = { compile: true, length: true, problems: [] };
        assert.equal(acceptableByAll(ok, 0.8), true);
        assert.equal(acceptableByAll(ok, null), true);
        assert.equal(acceptableByAll(ok, 0.4), false);
        assert.equal(acceptableByAll({ ...ok, length: false }, 1), false);
    });
});

describe('the crop, the estimate, the batches, the apply', () => {
    it('crops the page around the box by 30% of its height, kept on the page', () => {
        assert.deepEqual(cropRect([700, 1800, 1100, 1900], { width: 2400, height: 3500 }), { x: 670, y: 1770, width: 460, height: 160 });
        assert.deepEqual(cropRect([0, 0, 100, 100], { width: 120, height: 120 }), { x: 0, y: 0, width: 120, height: 120 });
    });

    it('estimates from the measured average, else 3 s a bar and says so', () => {
        assert.equal(estimateOf(12, undefined), '12 bars · about 36 s (2–4 s a bar)');
        assert.equal(estimateOf(1, 2.4), '1 bar · about 2 s');
        assert.equal(estimateOf(40, 2.5), '40 bars · about 2 min');
    });

    it('keeps the last 20 timings and averages them', () => {
        let samples: number[] = [];
        for (let i = 1; i <= 25; i++) { samples = withSample(samples, i); }
        assert.equal(samples.length, 20);
        assert.equal(samples[0], 6);
        assert.equal(averageOf(samples), 15.5);
        assert.equal(averageOf([]), undefined);
    });

    it('cuts the targets into batches of the setting', () => {
        assert.deepEqual(batchesOf([1, 2, 3, 4, 5], 2), [[1, 2], [3, 4], [5]]);
        assert.deepEqual(batchesOf([1, 2], 0), [[1], [2]]);
        assert.deepEqual(batchesOf([], 25), []);
    });

    it('applies at the recorded place while the text is there, follows a moved line, and refuses an ambiguous one', () => {
        const [t] = fixTargets(LYS, TODOS.slice(0, 1));
        assert.equal(anchorOf(LYS, t), t.start);
        const moved = '// a line added\n' + LYS;
        assert.equal(anchorOf(moved, t), t.start + '// a line added\n'.length);
        assert.equal(anchorOf(LYS + '    ' + t.written + '\n', t), t.start);          // still there: the copy below does not matter
        assert.equal(anchorOf(moved + '    ' + t.written + '\n', t), -1);             // moved, and twice: which?
        assert.equal(anchorOf(LYS.replace(t.written, 'r1 |'), t), -1);
    });

    it('finds the preview bar whose written stretch meets the line', () => {
        const boxes = [{ from: 0, to: 20 }, { from: 21, to: 50 }, { from: 51, to: 80 }];
        assert.equal(barBoxFor(boxes, 25, 50), 1);
        assert.equal(barBoxFor(boxes, 50, 60), 1);
        assert.equal(barBoxFor(boxes, 90, 99), -1);
    });
});
