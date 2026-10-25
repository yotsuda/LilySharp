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

// An AI candidate can be heard before it is accepted (owner's request 2026-10-25): the candidate
// panel's Play asks the server for the notes of the text shown (lilysharp/playbackText) and the
// document's preview plays them, as its own Play would.
describe('an AI candidate plays before it is accepted', () => {
    const src = (f: string) => fs.readFileSync(path.join(__dirname, '..', '..', 'src', f), 'utf8');
    const transform = src('aiTransform.ts');
    const extension = src('extension.ts');

    it('has Play and Stop in the panel, Play sounding the pane shown', () => {
        assert.match(transform, /<button id="play"[^>]*>▶ Play<\/button>/);
        assert.match(transform, /<button id="stop">■ Stop<\/button>/);
        assert.match(transform, /const which = \(\) => paneAfter\.classList\.contains\('show'\) \? 'after' : 'before';/);
        // From the selected bar, if one is: its written stretch.
        assert.match(transform, /\{ action: 'play', which: which\(\), from: span\[0\], to: span\[1\] \}/);
    });

    it('asks for the notes of the candidate or the original, and plays them on the preview', () => {
        assert.match(transform, /sendRequest<[^>]*>\('lilysharp\/playbackText', \{\s*Text: which === 'after' \? play\.after : play\.before,/);
        assert.match(transform, /deps\.playOnPreview\?\.\(play\.uri, notes\)/);
        // A decision stops it.
        assert.match(transform, /if \(audible\) \{ deps\.stopOnPreview\?\.\(currentUri\(audible\.uri\)\); \}\s*resolve\?\.\(d\);/);
        // An untitled score saved under review: the preview it plays on went with it.
        assert.match(transform, /const play = \{ \.\.\.audible, uri: currentUri\(audible\.uri\) \};/);
    });

    it('lets the preview page play another text\'s notes from the top, as its Play does', () => {
        const start = extension.indexOf('return `<!DOCTYPE html>') + 'return '.length;
        const end = extension.indexOf('</html>`;', start) + '</html>`'.length;
        const html = new vm.Script(extension.slice(start, end).replace(/\$\{[^}]*\}/g, 'X')).runInNewContext({}) as string;
        assert.match(html, /case 'playNotes':\s*startPlayback\(message\.notes, 0\);/);
        assert.match(extension, /void panel\.webview\.postMessage\(\{ type: 'playNotes', notes \}\);/);
    });

    // The notes sounding light on the panel's score (owner's request 2026-10-25): the preview
    // follows the candidate's own offsets (R) and reports them; the panel lights that pane's notes.
    it('lights the notes sounding on the candidate panel, not on the preview', () => {
        assert.match(transform, /S: -1, R: n\.S \?\? -1 \}\)\);\s*playingPane = which;/);
        assert.match(extension, /const relay = notes\.some\(n => n\.R !== undefined\);/);
        assert.match(extension, /if \(relay\) \{\s*vscode\.postMessage\(\{ type: 'candidateSounding'/);
        // A stop clears the panel's light.
        assert.match(extension, /if \(relaying\) \{\s*relaying = false;\s*vscode\.postMessage\(\{ type: 'candidateSounding', sounding: \[\] \}\);/);
        assert.match(extension, /if \(message\.type === 'candidateSounding'\) \{\s*showCandidateSounding\(message\.sounding\);/);
        assert.match(transform, /if \(e\.data\.type === 'sounding'\) \{ lightSounding\(e\.data\.which, e\.data\.sounding \|\| \[\]\); return; \}/);
    });
});
