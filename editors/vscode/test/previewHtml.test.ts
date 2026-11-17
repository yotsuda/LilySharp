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
import * as vm from 'node:vm';
import { getPreviewHtml } from '../src/previewHtml';

// Session 1295 (review F3, stage 1): the page stands in a file of its own that knows no VS Code,
// so the text the webview is handed can be BUILT here — until then the tests could only read the
// template's source and cut functions out of it.
describe('the preview\'s page, built', () => {
    const html = getPreviewHtml('font://music', 'font://brace', 'csp://source', 'NONCE', 'dark', '/* text fonts */',
        { script: 'synth://script', processor: 'synth://processor', soundFont: 'synth://sf', stretch: 'synth://stretch' },
        'Some "MIDI" Port');
    const inline = [...html.matchAll(/<script nonce="NONCE">([\s\S]*?)<\/script>/g)].map(m => m[1]);

    it('is one document, every script of it under the nonce', () => {
        assert.ok(html.startsWith('<!DOCTYPE html>') && html.endsWith('</html>'));
        assert.equal((html.match(/<script\b/g) ?? []).length, 3);
        assert.equal((html.match(/<script nonce="NONCE"/g) ?? []).length, 3);
        assert.equal(inline.length, 2);
        assert.match(html, /script-src 'nonce-NONCE' csp:\/\/source /);
        assert.match(html, /<script nonce="NONCE" src="synth:\/\/script"><\/script>/);
    });
    it('carries what the host gave it', () => {
        assert.match(html, /const mode = 'dark';/);
        assert.match(html, /let themeMode = 'dark';/);
        assert.match(html, /processor: 'synth:\/\/processor', soundFont: 'synth:\/\/sf', stretch: 'synth:\/\/stretch', midiOutput: "Some \\"MIDI\\" Port" \};/);
        assert.match(html, /const HIGHLIGHT_THRESHOLD = 50;/);
        assert.ok(html.includes('/* text fonts */'));
        // Nothing of the template is left unfilled where the host's values go.
        assert.ok(!/\$\{(nonce|cspSource|theme|fontUri|braceFontUri|textFontCss|synth\.\w+|PLAIN_TONES_JS|FOLLOW_SCROLL_\w+)\}/.test(html));
    });
    it('has scripts a browser can read: each is JavaScript that compiles', () => {
        for (const code of inline) {
            assert.doesNotThrow(() => new vm.Script(code));
        }
        // …and the big one is the page's: it is where the page tells the host it is ready.
        assert.match(inline[1], /vscode\.postMessage\(\{ type: 'webviewReady' \}\);\s*$/);
    });
});
