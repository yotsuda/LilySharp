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
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

// The preview's synth runs in the page (an AudioContext and a worklet), so its shape is read from
// the source, as playbackCore.test.ts reads it.
//
// Owner's report 2026-11-27: with MuseScore 4's MS Basic.sf3 set as the sound font, the preview fell
// silent. The page, reloaded for the new font's folder, began with the font its HTML was written
// with; the extension's message for the new font came while the worklet started; the old request was
// counted after the wait, came second, won, and was refused (HTTP 401) — nothing was loaded.
test('the latest sound font asked wins, counted when it is asked, not when the worklet is ready', () => {
    const src = readFileSync(join(__dirname, '..', '..', 'src', 'webview', 'previewSynth.ts'), 'utf8');
    const body = /async setSoundFont\(url: string\): Promise<void> \{([^]*?)\n {4}\}/.exec(src)?.[1] ?? '';
    const asked = body.indexOf('++this.bankSerial'), waited = body.indexOf('await this.init'), fetched = body.indexOf('fetch(url)');
    assert.ok(asked >= 0 && waited > asked, 'the request is counted before the wait for the worklet');
    assert.ok(fetched > waited, 'the fetch comes after the wait');
    // (a request overtaken while the worklet started gives way before it fetches)
    assert.match(body.slice(waited, fetched), /serial !== this\.bankSerial\) \{ return; \}/);
    // The page's own font is asked at once, so a later one overtakes it — not after the worklet.
    assert.match(src, /this\.init = this\.start\(processorUrl\);\s*\/\/[^\n]*\n\s*void this\.setSoundFont\(soundFontUrl\);/);
    assert.doesNotMatch(src, /this\.init\.then\(\(\) => this\.setSoundFont/);
});

// The same day: after the setting changed, the preview stayed on "Loading preview..." — VS Code
// reloads a page whose options change (the new font's folder), and nothing drew the reloaded one.
test('a preview page that loads again is drawn again and told the sound font; a first load is not', () => {
    const src = readFileSync(join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
    const ready = /if \(message\.type === 'webviewReady'\) \{([^]*?)\n {12}\}/.exec(src)?.[1] ?? '';
    // (read before pageLoaded marks the page as loaded)
    assert.ok(ready.indexOf('pageHasLoaded') >= 0 && ready.indexOf('pageHasLoaded') < ready.indexOf('.pageLoaded()'));
    const reload = /if \(reloaded\) \{([^]*?)\n {16}\}/.exec(ready)?.[1] ?? '';
    assert.match(reload, /type: 'setSoundFont'/);
    assert.match(reload, /updatePreviewContent\(doc, panel, context\)/);
    // Shosa's request 7: the voices too — forgotten as sent, then asked for again.
    assert.ok(reload.indexOf('pageReloaded(panel)') >= 0 && reload.indexOf('pageReloaded(panel)') < reload.indexOf('prepareVoices(panel, uri'));
    const singing = readFileSync(join(__dirname, '..', '..', 'src', 'singing.ts'), 'utf8');
    const reloaded = /export function pageReloaded\(panel: vscode\.WebviewPanel\): void \{([^]*?)\n\}/.exec(singing)?.[1] ?? '';
    assert.match(reloaded, /postedVoices\.delete\(panel\)/);
    assert.match(reloaded, /voiceTurned\.add\(panel\)/);
});
