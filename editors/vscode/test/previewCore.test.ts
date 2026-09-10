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
import { svgPostKey } from '../src/previewCore';

describe('the svg post key', () => {
    const svg = '<svg/>';
    const main = { Name: 'main', Type: 'score', Filename: '' };
    const sub = { Name: 'S', Type: 'score', Filename: 'S' };

    it('is the same for the same picture, banner, list and drawn score', () => {
        assert.equal(svgPostKey(svg, null, [main], ''), svgPostKey(svg, undefined, [main], ''));
    });

    it('changes when a score is added to the list although the picture did not change', () => {
        // The 2026-09-11 report: a `score { }` pasted below the drawn one left the picture
        // identical, and the skipped post left the picker without the new score.
        assert.notEqual(svgPostKey(svg, null, [main], ''), svgPostKey(svg, null, [main, sub], ''));
    });

    it('changes when the drawn score changes, when the banner changes, and when the picture changes', () => {
        assert.notEqual(svgPostKey(svg, null, [main, sub], ''), svgPostKey(svg, null, [main, sub], 'S'));
        assert.notEqual(svgPostKey(svg, null, [main], ''), svgPostKey(svg, 'Line 1: oops', [main], ''));
        assert.notEqual(svgPostKey(svg, null, [main], ''), svgPostKey('<svg><g/></svg>', null, [main], ''));
    });

    it('treats a missing list as empty', () => {
        assert.equal(svgPostKey(svg, null, null, ''), svgPostKey(svg, null, [], ''));
    });
});
