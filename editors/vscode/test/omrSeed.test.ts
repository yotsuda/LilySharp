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
import { SEED_FOLDER_SETTING, SeedBar, seedDirName, seedRecordOf } from '../src/omrSeedCore';

// Report Misread to OMR (owner's request 2026-10-29): the owner's alone, apart from Accept.
describe('Report Misread to OMR', () => {
    const root = path.join(__dirname, '..', '..');
    const fix = fs.readFileSync(path.join(root, 'src', 'omrAiFix.ts'), 'utf8');
    const bar: SeedBar = {
        lys: 'C:\\scores\\Largo (op. 1).lys', measure: 12, part: 'right', line: 40, bar: 'c4 d e f |',
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

    it('names its folder by when, the score and the bar', () => {
        assert.equal(seedDirName(new Date(2026, 9, 29, 15, 30, 12), bar), '20261029-153012-Largo_op_1-bar12-right');
        assert.equal(seedDirName(new Date(2026, 9, 29, 15, 30, 12), { lys: '/a/b.lys', line: 7, bar: 'c1 |' }), '20261029-153012-b-line7');
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
        assert.equal(seedRecordOf(new Date(), { lys: 'x.lys', line: 1, bar: '' }, '', {}).scan, null);
    });
});
