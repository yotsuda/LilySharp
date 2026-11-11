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

    // Owner's decisions 2026-11-11: the family's dark tile, a hue none of the three has (Lily# blue, Calamo
    // green, Cantoria amber), and Emmentaler's `sf` — sforzando, and the sound font's own initials.
    it('has the family\'s tile in a colour of its own', () => {
        assert.equal(pkg.icon, 'icon.png');
        assert.ok(fs.statSync(path.join(root, 'icon.png')).size > 1000);
        const icon = read('icon.svg');
        assert.match(icon, /<stop offset="0%" stop-color="#2d2d30"\/><stop offset="100%" stop-color="#1e1e1e"\/>/);
        assert.match(icon, /<path fill="#e86a9a" d="M/);
        const lily = fs.readFileSync(path.join(__dirname, '..', '..', 'icon.svg'), 'utf8');
        assert.match(lily, /<path fill="#5aa7e8"/);
        assert.ok(lily.includes('stop-color="#2d2d30"') && lily.includes('stop-color="#1e1e1e"'));
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
        // Owner's decision 2026-11-11: a developer's build reads no font of its own any more (it used to,
        // under media/soundfont, before the extension's) — a developer's machine takes the road a user's
        // does. The folder stays out of the package and out of git, lest one left over be carried.
        const chosen = ext.slice(ext.indexOf('function defaultSoundFont('), ext.indexOf('const SOUND_FONT_EXTENSION'));
        assert.doesNotMatch(chosen, /joinPath\(|readdirSync|'media'/);
        assert.doesNotMatch(ext, /this build's own, under media\/soundfont/);
        assert.match(fs.readFileSync(path.join(__dirname, '..', '..', '.vscodeignore'), 'utf8'), /^media\/soundfont\/\*\*$/m);
        assert.match(fs.readFileSync(path.join(__dirname, '..', '..', '.gitignore'), 'utf8'), /^media\/soundfont\/$/m);
    });

    // Owner's decision 2026-11-11: a pack, not a dependency. Lily# plays without the font, so the
    // font's package must never be what keeps Lily# from being installed, and it can be removed alone
    // (tried with dummy parents from VSIX files: RELEASING.md).
    it('is brought by Lily# as a pack member, not as a dependency', () => {
        const lily = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8')) as Record<string, unknown>;
        assert.deepEqual(lily.extensionPack, ['yotsuda.lilysharp-soundfont']);
        assert.equal(lily.extensionDependencies, undefined);
        assert.match(read('README.md'), /remove it on its own\s+and Lily# plays on without it; remove Lily# and it goes too\./);
    });
});
