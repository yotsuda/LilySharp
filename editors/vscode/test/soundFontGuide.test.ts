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

// The sound font guide (owner's request 2026-11-27): Choose Sound Font… ends with Find More Sound
// Fonts…, which opens the site's "Sound fonts" section — fonts to download, nearest to the default
// first, and how to set one up. The picker needs a VS Code, so its shape is read from the source.
test('Choose Sound Font… offers the site\'s guide, and the guide is there', () => {
    const root = join(__dirname, '..', '..');
    const src = readFileSync(join(root, 'src', 'soundFont.ts'), 'utf8');
    const guide = /export const SOUND_FONT_GUIDE = '([^']+)'/.exec(src)?.[1] ?? '';
    assert.equal(guide, 'https://yotsuda.github.io/LilySharp/editor.html#soundfonts');
    assert.match(src, /label: 'Find More Sound Fonts…'[^\n]*action: 'more'/);
    assert.match(src, /picked\.action === 'more'\) \{[^}]*openExternal\(vscode\.Uri\.parse\(SOUND_FONT_GUIDE\)\)/);
    // The section the address names, with the fonts it guides to and the steps.
    const site = readFileSync(join(root, '..', '..', 'site', 'editor-body.html'), 'utf8');
    const section = /<section id="soundfonts">([^]*?)<\/section>/.exec(site)?.[1] ?? '';
    for (const font of ['FluidR3Mono_GM', 'GeneralUser GS', 'Timbres of Heaven', 'MS Basic.sf3']) {
        assert.ok(section.includes(font), font);
    }
    assert.match(section, /<h3>Setting one up<\/h3>/);
    assert.match(section, /Find More Sound Fonts…/);
});
