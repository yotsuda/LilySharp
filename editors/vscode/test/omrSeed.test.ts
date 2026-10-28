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
import * as fs from 'node:fs';
import * as path from 'node:path';
import { SEED_FOLDER_SETTING, SeedBar, fixedCopyName, seedDirName, seedRecordOf, withBar } from '../src/omrSeedCore';

// Report Misread to OMR (owner's request 2026-10-29): the owner's alone, apart from Accept.
describe('Report Misread to OMR', () => {
    const root = path.join(__dirname, '..', '..');
    const fix = fs.readFileSync(path.join(root, 'src', 'omrAiFix.ts'), 'utf8');
    const bar: SeedBar = {
        lys: 'C:\\scores\\Largo (op. 1).lys', measure: 12, part: 'right', line: 40, bar: 'c4 d e f |',
        correct: 'c4 d e fis |', correctFrom: 'draft',
        crop: { page: 2, box: [10, 20, 110, 80] }, pageImage: 'C:\\scores\\largo-2.png', sideFile: 'C:\\scores\\Largo (op. 1).omr.json',
    };

    it('is turned on by a setting that is not contributed, so it never shows to anyone else', () => {
        const pkg = fs.readFileSync(path.join(root, 'package.json'), 'utf8');
        assert.ok(!pkg.includes(`lilysharp.${SEED_FOLDER_SETTING}`));
        assert.ok(!/omrReport|reportMisread|Report Misread/i.test(pkg));
        assert.match(fix, /const report = can && seedFolder\(\)/);
    });

    it('is its own button, never an Accept', () => {
        assert.match(fix, /case 'report':\s*if \(row && await reportMisread\(run, row\)\)/);
        const accept = fix.slice(fix.indexOf("case 'acceptRow':"), fix.indexOf("case 'report':"));
        assert.ok(!accept.includes('reportMisread'));
        assert.ok(!fix.slice(fix.indexOf('async function applyRows')).slice(0, 4000).includes('reportMisread'));
    });

    // Owner's request 2026-10-29: a report is made again because the first was wrong.
    it('replaces the row\'s earlier report once the new one is written', () => {
        const fn = fix.slice(fix.indexOf('async function reportMisread'), fix.indexOf('/** A row still in the panel'));
        assert.ok(fn.indexOf("'seed.json'), JSON.stringify(record") < fn.indexOf('fs.rmSync(old'));
        assert.match(fn, /old !== dir && path\.dirname\(old\) === path\.resolve\(root\) && fs\.existsSync\(path\.join\(old, 'seed\.json'\)\)/);
        assert.match(fix, /const REPORT_AGAIN = 'Reported ✓ — Replace Report…';/);
    });

    // Owner's decision 2026-10-29: the bar fixed in the panel is what is reported — the choice of
    // "the .lys's bar" was taken by mistake while the fix sat in the Draft, and dropped.
    it('reports the Draft as the right bar, with no choice of the .lys\'s', () => {
        assert.match(fix, /const correct = draft !== undefined && draft !== resolvedLine\(fileBar\) \? draft : null;/);
        assert.ok(!/showQuickPick/.test(fix.slice(fix.indexOf('async function reportMisread'), fix.indexOf('/** A row still in the panel'))));
        assert.ok(!fix.includes('The .lys is right'));
    });

    it('names its folder by when, the score and the bar', () => {
        assert.equal(seedDirName(new Date(2026, 9, 29, 15, 30, 12), bar), '20261029-153012-Largo_op_1-bar12-right');
        assert.equal(seedDirName(new Date(2026, 9, 29, 15, 30, 12), { lys: '/a/b.lys', line: 7, bar: 'c1 |', correct: 'c1 |', correctFrom: 'draft' }),
            '20261029-153012-b-line7');
    });

    // Owner's request 2026-10-29: the .lys may be fixed again or deleted after the report.
    it('takes everything at the report, the right bar with it, and never reads the .lys again', () => {
        const r = seedRecordOf(new Date(), bar, '', {});
        assert.equal(r.lysWas, bar.lys);
        assert.ok(!('lys' in r));
        assert.deepEqual([r.lysCopy, r.fixedCopy, r.correct, r.correctFrom], ['Largo (op. 1).lys', 'Largo (op. 1).fixed.lys', 'c4 d e fis |', 'draft']);
        assert.equal(withBar('a\r\n    c4 d e f |\r\nb', 2, 'c4 d e fis |'), 'a\r\n    c4 d e fis |\r\nb');
        assert.equal(fixedCopyName('/x/largo.lys'), 'largo.fixed.lys');
        // A bar not fixed in the Draft goes with what is wrong in words, asked for, not optional
        // (owner's report 2026-10-29: "Not fixed yet" gave no chance to say anything).
        const none = seedRecordOf(new Date(), { ...bar, correct: null, correctFrom: 'none' }, 'the natural is missing', {});
        assert.deepEqual([none.correct, none.correctFrom, none.fixedCopy, none.note], [null, 'none', null, 'the natural is missing']);
        assert.match(fix, /correctFrom: correct === null \? 'none' : 'draft'/);
        assert.match(fix, /correct === null \? \{ validateInput: /);
    });

    it('records the bar, where it is on the scan, and the copies made', () => {
        const r = seedRecordOf(new Date(Date.UTC(2026, 9, 29, 6, 30, 12)), bar, '  the natural is missing ', { image: true, crop: true, sideFile: false });
        assert.equal(r.reported, '2026-10-29T06:30:12.000Z');
        assert.equal(r.note, 'the natural is missing');
        assert.equal(r.lysCopy, 'Largo (op. 1).lys');
        assert.deepEqual([r.measure, r.part, r.line, r.bar], [12, 'right', 40, 'c4 d e f |']);
        assert.deepEqual(r.scan, {
            page: 2, box: [10, 20, 110, 80], image: 'C:\\scores\\largo-2.png', imageCopy: 'page-2.png', crop: 'bar.png',
            sideFile: 'C:\\scores\\Largo (op. 1).omr.json',
        });
        assert.equal(seedRecordOf(new Date(), { lys: 'x.lys', line: 1, bar: '', correct: null, correctFrom: 'none' }, 'x', {}).scan, null);
    });
});
