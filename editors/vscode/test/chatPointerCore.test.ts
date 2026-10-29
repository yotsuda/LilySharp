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
import { chatPointer, placeAt } from '../src/chatPointerCore';

// Owner's request 2026-10-29: the place a chat request is about — the file alone, or with bars,
// their lines, section, part and bars.
describe('the pointer put in the chat', () => {
    const text = [
        'title "Dusk"',                                   // 1
        'part vn1 "Violin I" { instrument violin }',      // 2
        'section A {',                                    // 3
        '  vn1 {',                                        // 4
        '    c\'\'4 d\'\' e\'\' f\'\' |',                 // 5
        '    g\'\'1 | // a "comment" with | in it',       // 6
        '    tuplet 3/2 { a\'\'8 b\'\' c\'\'\' } e\'\'2 |',// 7
        '  }',                                            // 8
        '  lyrics words sings vn1 { la la | }',          // 9
        '}',                                              // 10
        'section B {',                                    // 11
        '  vn1 { voice { c\'4 | d\'4 | } { e4 | f4 | } }',// 12
        '}',
    ].join('\n');
    const at = (line: number, col = 4) => text.split('\n').slice(0, line - 1).reduce((n, l) => n + l.length + 1, 0) + col;
    it('finds the section, the part and the bar as written', () => {
        assert.deepEqual(placeAt(text, at(5)), { section: 'A', part: 'vn1', bar: 1 });
        assert.deepEqual(placeAt(text, at(6)), { section: 'A', part: 'vn1', bar: 2 });
        // A bar line in a comment and the tuplet's braces count for nothing.
        assert.deepEqual(placeAt(text, at(7, 20)), { section: 'A', part: 'vn1', bar: 3 });
        // A lyrics track is not a part.
        assert.deepEqual(placeAt(text, at(9, 30)), { section: 'A', part: undefined, bar: undefined });
        assert.deepEqual(placeAt(text, at(2, 25)), { section: undefined, part: undefined, bar: undefined });
        assert.equal(placeAt(text, at(12, 20)).part, 'vn1');
    });
    it('is offered on the score and in the editor, and put in the chat unsent', () => {
        const root = path.join(__dirname, '..', '..');
        const pkg = fs.readFileSync(path.join(root, 'package.json'), 'utf8');
        assert.match(pkg, /"command": "lilysharp\.previewAskAiInChat",\s*"when": "webviewId == 'lilysharpPreview'"/);
        assert.match(pkg, /"command": "lilysharp\.askAiInChat",\s*"when": "resourceLangId == lilysharp"/);
        const ext = fs.readFileSync(path.join(root, 'src', 'extension.ts'), 'utf8');
        assert.match(ext, /executeCommand\('workbench\.action\.chat\.open', \{ query, isPartialQuery: true, mode: 'agent' \}\)/);
    });
    // Owner's requests 2026-10-29: the preview header's ✨ opens the chat, and so does Ctrl+I, in the
    // preview and in the editor.
    it('is what the preview header\'s ✨ and Ctrl+I do, the selected bars with it', () => {
        const ext = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
        assert.match(ext, /<button id="aiTransformBtn" class="ai icon" type="button" title="Ask AI in Chat \(Ctrl\+I\)">✨<\/button>/);
        assert.match(ext, /aiTransformBtn\.addEventListener\('click', askInChat\);/);
        assert.match(ext, /vscode\.postMessage\(\{ type: 'askAiInChat', startPos: aiRangeLo, endPos: aiRangeHi \}\);/);
        assert.match(ext, /message\.type === 'askAiInChat'\) \{[\s\S]{0,600}?await askAiInChat\(doc,/);
        assert.match(ext, /e\.preventDefault\(\); askInChat\(\);/);
        assert.ok(!/aiSubmitSelection|'aiWholeScore'/.test(ext));
        const pkg = fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8');
        assert.match(pkg, /"command": "lilysharp\.askAiInChat",\s*"key": "ctrl\+i",\s*"mac": "cmd\+i"/);
        // …and it is seen to be pressed: sunk while held, and a moment after a quick click.
        assert.match(ext, /\.toolbar button\.ai:active, \.toolbar button\.ai\.pressed \{\s*box-shadow: inset/);
        assert.match(ext, /aiTransformBtn\.classList\.add\('pressed'\);/);
    });
    it('is the file alone with no range, and its lines and bars with one', () => {
        assert.equal(chatPointer('Dusk.lys', text), 'Dusk.lys: ');
        assert.equal(chatPointer('Dusk.lys', text, { from: at(5), to: at(5, 10) }), 'Dusk.lys, line 5 (section A, part vn1, bar 1): ');
        assert.equal(chatPointer('Dusk.lys', text, { from: at(5), to: at(7, 20) }), 'Dusk.lys, lines 5–7 (section A, part vn1, bars 1–3): ');
        assert.equal(chatPointer('Dusk.lys', text, { from: at(1, 0), to: at(1, 3) }), 'Dusk.lys, line 1: ');
    });
});
