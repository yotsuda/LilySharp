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
import * as vm from 'node:vm';

// The preview's Range panel (owner's request 2026-10-25): each part's lowest and highest note,
// asked of the server for the score shown, a click showing one on the score.
describe('the preview\'s Range panel', () => {
    const extension = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
    const start = extension.indexOf('return `<!DOCTYPE html>') + 'return '.length;
    const end = extension.indexOf('</html>`;', start) + '</html>`'.length;
    const html = new vm.Script(extension.slice(start, end).replace(/\$\{[^}]*\}/g, '0')).runInNewContext({}) as string;

    it('has a Range button and its panel, and the page script parses', () => {
        assert.match(html, /<button id="rangeBtn" class="toggle icon" type="button" aria-pressed="false" aria-label="Range"[^>]*><svg [^>]*>/);
        assert.match(html, /rangeBtn\.setAttribute\('aria-pressed', String\(!rangePanel\.hidden\)\);/);
        assert.match(html, /<div id="rangePanel" class="range-panel" hidden><\/div>/);
        for (const m of html.matchAll(/<script[^>]*>([\s\S]*?)<\/script>/g)) {
            assert.doesNotThrow(() => new vm.Script(m[1]));
        }
    });

    it('asks the server for the score shown, and again after every render while open', () => {
        assert.match(extension, /'lilysharp\/ranges', \{ textDocument: \{ uri \}, renderName: selectedRenders\.get\(uri\) \|\| null \}/);
        assert.match(html, /function refreshRanges\(\) \{\s*if \(!rangePanel\.hidden\) vscode\.postMessage\(\{ type: 'requestRanges' \}\);/);
        assert.match(html, /lightCaretBar\(\);\s*refreshRanges\(\);/);
        assert.match(html, /case 'rangesData':\s*rangeParts = message\.parts \|\| null;/);
    });

    it('shows a clicked end on the score and in the editor, the next place on the next click', () => {
        assert.match(html, /if \(rangeCur === key\) rangeAt\[key\] = \(\(rangeAt\[key\] \|\| 0\) \+ 1\) % n;/);
        assert.match(html, /const painted = highlightPositions\(\[pos\]\);/);
        assert.match(html, /vscode\.postMessage\(\{ type: 'jumpToPosition', position: pos \}\);\s*renderRanges\(\);/);
    });
});
