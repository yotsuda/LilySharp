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
import * as fs from 'node:fs';
import * as path from 'node:path';

// The preview's header is in extension.ts's page, which needs a VS Code to run: its shape is read
// from the source (owner's request 2026-11-02: the metronome button as the Range button is).
const source = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
const button = (id: string) => new RegExp(`<button id="${id}"[^]*?</button>`).exec(source)?.[0] ?? '';

test('the metronome button is an icon button as the Range button is, with no arrow', () => {
    assert.match(button('rangeBtn'), /class="toggle icon"/);
    assert.match(button('practiceBtn'), /class="toggle icon practice"/);
    assert.doesNotMatch(button('practiceBtn'), /caret/);
    assert.doesNotMatch(button('practiceBtn'), /▼|▾/);
    assert.match(button('practiceBtn'), /<svg viewBox="0 0 16 16" fill="currentColor"/);
    assert.doesNotMatch(source, /svg\.caret/);
});

test('it keeps the tempo it plays at, and looks pressed while its menu is open', () => {
    assert.match(button('practiceBtn'), /<span id="practiceSpeed"><\/span>/);
    assert.match(source, /button\.practice\[aria-expanded="true"\] \{\s*background: #d6e6f8/);
    assert.match(source, /#practiceSpeed:empty \{ display: none; \}/);
});
