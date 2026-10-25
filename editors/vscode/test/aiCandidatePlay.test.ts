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
        assert.match(transform, /if \(audible\) \{ deps\.stopOnPreview\?\.\(audible\.uri\); \}\s*resolve\?\.\(d\);/);
    });

    it('lets the preview page play another text\'s notes from the top, as its Play does', () => {
        const start = extension.indexOf('return `<!DOCTYPE html>') + 'return '.length;
        const end = extension.indexOf('</html>`;', start) + '</html>`'.length;
        const html = new vm.Script(extension.slice(start, end).replace(/\$\{[^}]*\}/g, 'X')).runInNewContext({}) as string;
        assert.match(html, /case 'playNotes':\s*startPlayback\(message\.notes, 0\);/);
        assert.match(extension, /void panel\.webview\.postMessage\(\{ type: 'playNotes', notes \}\);/);
    });
});
