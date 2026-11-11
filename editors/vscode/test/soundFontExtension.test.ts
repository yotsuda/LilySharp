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

// Owner's decision 2026-11-11: a good sound from the start, in a package of its own — the
// MuseScore_General sound font, unchanged, with its authors' notices, under a name that says both
// whose package it is and what is in it.
describe('the Lily# Sound Font extension', () => {
    const root = path.join(__dirname, '..', '..', '..', 'vscode-soundfont');
    const read = (name: string) => fs.readFileSync(path.join(root, name), 'utf8');
    const pkg = JSON.parse(read('package.json')) as Record<string, unknown>;

    it('is a package of one file: no code, no contributions', () => {
        assert.equal(pkg.name, 'lilysharp-soundfont');
        assert.equal(pkg.publisher, 'yotsuda');
        assert.equal(pkg.displayName, 'Lily# Sound Font (MuseScore General)');
        assert.equal(pkg.license, 'MIT');
        assert.equal(pkg.main, undefined);
        assert.equal(pkg.contributes, undefined);
        assert.equal(pkg.activationEvents, undefined);
    });

    it('says whose work the font is, that it is unchanged, and that it is no official MuseScore package', () => {
        const readme = read('README.md');
        assert.match(String(pkg.description), /MuseScore_General sound font \(by S\. Christian Collins and others, MIT license\), unchanged\. Not an official MuseScore distribution\./);
        assert.match(readme, /\*\*Whose work it is\.\*\* Not ours\./);
        assert.match(readme, /exactly as its authors published it/);
        assert.match(readme, /\*\*This is not an official MuseScore distribution\.\*\*/);
        // The authors' own notices go with it, as the license asks.
        const license = read('MuseScore_General_License.md');
        assert.match(license, /MuseScore_General\.sf2 is shared under the MIT license/);
        assert.match(license, /The acknowledgements and copyright notices above must be included in any derivative work\./);
        for (const name of ['S. Christian Collins', 'Michael Cowgill', 'Frank Wen', 'Ethan Winer', 'Michael Schorsch']) {
            assert.ok(license.includes(name), name);
        }
        assert.ok(fs.existsSync(path.join(root, 'MuseScore_General_Readme.md')));
        assert.ok(fs.existsSync(path.join(root, 'MuseScore_General_Changelog.md')));
    });

    it('carries the file it names: one hash in the README and in the script that fetches it, and the font out of git', () => {
        const hash = /SHA-256 `([0-9a-f]{64})`/.exec(read('README.md'));
        assert.ok(hash);
        assert.ok(read('Get-SoundFont.ps1').includes(`$sha256 = '${hash[1]}'`));
        assert.match(read('.gitignore'), /^soundfont\/\*\.sf3$/m);
        // The script that builds the package is not in it.
        assert.match(read('.vscodeignore'), /^Get-SoundFont\.ps1$/m);
    });

    it('is what Lily# plays through by default when it is installed, ahead of the OS\'s bank', () => {
        const ext = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
        assert.match(ext, /const SOUND_FONT_EXTENSION = 'yotsuda\.lilysharp-soundfont';/);
        assert.equal(`${pkg.publisher}.${pkg.name}`, 'yotsuda.lilysharp-soundfont');
        assert.match(ext, /return packagedSoundFont\(\) \?\? osSoundFonts\(\)\[0\] \?\? null;/);
        assert.match(ext, /vscode\.extensions\.getExtension\(SOUND_FONT_EXTENSION\)/);
        assert.match(ext, /path\.join\(packaged\.extensionPath, 'soundfont'\)/);
        // A developer's own font under media/soundfont still comes first, and the setting before both.
        const chosen = ext.slice(ext.indexOf('function defaultSoundFont('), ext.indexOf('const SOUND_FONT_EXTENSION'));
        assert.ok(chosen.indexOf("'media', 'soundfont'") < chosen.indexOf('packagedSoundFont()'));
    });
});
