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

// The editor-free half of "Split Sections to Match a Part". Run with `npm test`.

import { describe, it } from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { modalText, splitChoiceItems } from '../src/splitSectionsCore';

describe('the core is editor-free', () => {
    it('imports nothing from vscode', () => {
        const src = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'splitSectionsCore.ts'), 'utf8');
        assert.doesNotMatch(src, /from 'vscode'/);
    });
});

describe('splitChoiceItems', () => {
    it('lists each way to follow with the parts that share it, and sends back the first part', () => {
        const items = splitChoiceItems([
            { Part: 'vn1', Parts: 'vn1', Description: 'A 16 + B 17 + C 12 bars' },
            { Part: 'vn2', Parts: 'vn2, va, vc', Description: 'A 16 + B 29 bars' },
        ]);
        assert.deepEqual(items, [
            { label: 'vn1', description: 'A 16 + B 17 + C 12 bars', part: 'vn1' },
            { label: 'vn2, va, vc', description: 'A 16 + B 29 bars', part: 'vn2' },
        ]);
    });
});

describe('modalText', () => {
    it('puts the plan\'s first line in the message and the rest in the detail', () => {
        const plan = 'Follow vn1: A 16 + B 121 bars.\nSplit A in vn2, va and vc after bar 16 → A, B.\nForm main: A → A B.';
        assert.deepEqual(modalText(plan), {
            message: 'Lily#: Follow vn1: A 16 + B 121 bars.',
            detail: 'Split A in vn2, va and vc after bar 16 → A, B.\nForm main: A → A B.',
        });
    });

    it('reads a refusal the same way, whatever its line ends', () => {
        const error = "Nothing was changed: cb still holds section A 137 bars long, and splitting it to follow vn1 is refused:\r\n• cb, section A, after bar 45 (line 702): a slur (opened line 702) runs across the cut.";
        assert.deepEqual(modalText(error), {
            message: "Lily#: Nothing was changed: cb still holds section A 137 bars long, and splitting it to follow vn1 is refused:",
            detail: '• cb, section A, after bar 45 (line 702): a slur (opened line 702) runs across the cut.',
        });
    });

    it('leaves the detail empty for a one-line text', () => {
        assert.deepEqual(modalText('Nothing to split.'), { message: 'Lily#: Nothing to split.', detail: '' });
    });
});
