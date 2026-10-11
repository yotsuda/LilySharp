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

// The manifest's side of `lilysharp.playback.soundFont` (docs/playback-design.md §4): the
// setting, the command that picks a file for it, and the link from the one to the other —
// the settings UI gives a string no browse button (owner's request 2026-10-11).
describe('the sound font setting', () => {
    const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8'));
    const setting = manifest.contributes.configuration.properties['lilysharp.playback.soundFont'];
    const commands = manifest.contributes.commands as { command: string; title: string }[];
    const palette = manifest.contributes.menus.commandPalette as { command: string; when?: string }[];

    it('is a machine-overridable string, empty by default (the bundled font)', () => {
        assert.equal(setting.type, 'string');
        assert.equal(setting.default, '');
        assert.equal(setting.scope, 'machine-overridable');
    });

    it('links to the picker command, which is in the palette without a language condition', () => {
        assert.match(setting.markdownDescription, /\(command:lilysharp\.chooseSoundFont\)/);
        const command = commands.find(c => c.command === 'lilysharp.chooseSoundFont');
        assert.ok(command, 'the command is contributed');
        assert.match(command!.title, /Sound Font/);
        const entry = palette.find(p => p.command === 'lilysharp.chooseSoundFont');
        assert.ok(entry, 'the command is in the palette');
        assert.equal(entry!.when, undefined);
    });
});
