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
import { onAngleChord, onShapeItem } from '../src/shapeItemCore';

test('the caret on a chord(...) or diagram(...) item, its words or its duration', () => {
    const line = '  c4 diagram(133211)4 chord(C7(b9) x32310)2. e4';
    assert.equal(onShapeItem(line, line.indexOf('diagram')), true);
    assert.equal(onShapeItem(line, line.indexOf('1332')), true);
    assert.equal(onShapeItem(line, line.indexOf(')4') + 2), true);
    assert.equal(onShapeItem(line, line.indexOf('b9')), true);
    assert.equal(onShapeItem(line, line.indexOf('2.') + 2), true);
});

test('not on a note, and not on an @chord / @diagram mark', () => {
    const line = '  c4 e4@chord(C x32010) f@diagram(133211) g';
    assert.equal(onShapeItem(line, line.indexOf('c4')), false);
    assert.equal(onShapeItem(line, line.indexOf('x32010')), false);
    assert.equal(onShapeItem(line, line.indexOf('133211')), false);
    assert.equal(onShapeItem('part chord { }', 6), false);
});

test('the caret on a <...> chord, not on a << >> group', () => {
    const line = "  c4 <g b d' f'>2. << c e g >>4 e4";
    assert.equal(onAngleChord(line, line.indexOf('<g')), true);
    assert.equal(onAngleChord(line, line.indexOf("d'")), true);
    assert.equal(onAngleChord(line, line.indexOf('2.') + 2), true);
    assert.equal(onAngleChord(line, line.indexOf('c e g')), false);
    assert.equal(onAngleChord(line, line.indexOf('c4')), false);
    assert.equal(onAngleChord(line, line.indexOf('e4')), false);
});
