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

// The Settings editor makes a setting's label from its key and allows no other (microsoft/vscode,
// settingsTreeModels.ts: settingKeyToDisplayFormat): it showed "Lilysharp › Playback: Midi Output".
// What comes before the key's last dot is dropped where it begins the id of the setting's section
// (trimCategoryForGroup), so each section has an id its keys begin with: the label is plain
// ("MIDI Output" under "Playback"), and the names are spelt right only in the sections' titles
// (the owner's request 2026-11-26; the same in Cantoria, Shosa and Bamen).
describe('the settings under their headings', () => {
    const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8'));
    const sections = manifest.contributes.configuration as { id: string; title: string; properties: object }[];

    it('gives each section an id its keys begin with, so that a label shows no category before it', () => {
        for (const section of sections) {
            for (const key of Object.keys(section.properties)) {
                assert.equal(key.slice(0, key.lastIndexOf('.')), section.id, `${key} under ${section.title}`);
            }
        }
    });

    it('keeps the key other extensions read', () => {
        const keys = sections.flatMap(s => Object.keys(s.properties));
        // Shosa and Bamen play their songs with this font.
        assert.ok(keys.includes('lilysharp.playback.soundFont'));
    });
});
