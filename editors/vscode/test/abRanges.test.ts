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
import { AB_SLOTS, abRangeBand, abRangeLabel, normalizeAbRange, normalizeAbRanges } from '../src/abRangesCore';

// The A-B repeat's eight kept ranges (docs/ab-repeat-design.md §1, step 5).
describe('the A-B repeat\'s kept ranges', () => {
    it('are always eight strips, the empty ones null', () => {
        assert.equal(AB_SLOTS, 8);
        assert.deepEqual(normalizeAbRanges(undefined), new Array(8).fill(null));
        const r = normalizeAbRanges([{ from: 12, to: 15, fromBar: 13, toBar: 16 }, null, 'x']);
        assert.equal(r.length, 8);
        assert.deepEqual(r[0], { from: 12, to: 15, fromBar: 13, toBar: 16 });
        assert.equal(r[1], null);
        assert.equal(r[2], null);
        // More than eight sent: the first eight.
        assert.equal(normalizeAbRanges(new Array(12).fill({ from: 0, to: 0 })).length, 8);
    });

    it('keep only what is a range: whole bars from 0, B not before A, a trimmed name', () => {
        assert.equal(normalizeAbRange({ from: 4, to: 3 }), null);
        assert.equal(normalizeAbRange({ from: -1, to: 3 }), null);
        assert.equal(normalizeAbRange({ from: 1.5, to: 3 }), null);
        assert.equal(normalizeAbRange({ from: '1', to: 3 }), null);
        assert.deepEqual(normalizeAbRange({ from: 2, to: 2, name: '  Bridge  ', extra: 1 }), { from: 2, to: 2, name: 'Bridge' });
        assert.deepEqual(normalizeAbRange({ from: 2, to: 5, name: '   ' }), { from: 2, to: 5 });
        assert.equal(normalizeAbRange({ from: 0, to: 1, name: 'x'.repeat(200) })!.name!.length, 80);
    });

    it('say their name, else the bar numbers printed now, else those kept', () => {
        const r = { from: 12, to: 15, fromBar: 13, toBar: 16 };
        assert.equal(abRangeLabel(r), '13–16');
        assert.equal(abRangeLabel({ ...r, name: 'Solo' }), 'Solo');
        // The page prints other numbers now (a pickup added): those.
        assert.equal(abRangeLabel(r, m => m), '12–15');
        // A bar the page no longer has: the number kept.
        assert.equal(abRangeLabel(r, () => undefined), '13–16');
        // One bar, no numbers kept: the measure index from 1.
        assert.equal(abRangeLabel({ from: 4, to: 4 }), '5');
    });

    it('colour their part of the whole score', () => {
        assert.deepEqual(abRangeBand({ from: 0, to: 9 }, 40), { left: 0, width: 25 });
        assert.deepEqual(abRangeBand({ from: 30, to: 39 }, 40), { left: 75, width: 25 });
        // The score grew shorter: up to its end; past it, nothing.
        assert.deepEqual(abRangeBand({ from: 36, to: 50 }, 40), { left: 90, width: 10 });
        assert.equal(abRangeBand({ from: 40, to: 45 }, 40), null);
        assert.equal(abRangeBand({ from: 0, to: 1 }, 0), null);
    });

    it('are drawn by the page from the script bundle, and kept by the extension per file', () => {
        const src = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
        const synth = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'webview', 'previewSynth.ts'), 'utf8');
        for (const name of ['AB_SLOTS', 'normalizeAbRanges', 'abRangeLabel', 'abRangeBand']) {
            assert.match(synth, new RegExp('^\\s+' + name + ',$', 'm'), name + ' is in LysSynth');
        }
        assert.match(src, /id="abStrips"/);
        assert.match(src, /postMessage\(\{ type: 'abRanges', chart: uri, ranges: abRangesFor\(uri\) \}\)/);
        assert.match(src, /message\.type === 'abRanges'[\s\S]{0,120}keepAbRanges\(message\.chart, message\.ranges\)/);
    });
});
