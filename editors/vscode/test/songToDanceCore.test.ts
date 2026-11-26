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
import { DANCE_GUIDE_CONTEXT, SHOSA_ADD_DANCE, SHOSA_EXTENSION_ID, danceNextActions, danceNextMessage, gaveASong, hasSinger } from '../src/songToDanceCore';
import { extensionSource } from './extensionSource';

// The third step of the way from a song to a dance (Shosa's, 2026-11-26; docs/song-to-dance-design.md).
describe('from a song to a dance', () => {
    const band = 'title "Road"\npart gtr { instrument guitar }\nsection A { gtr { c1 | } }\n';
    const sung = band.replace('section', 'part vocal { instrument alto  lyrics words }\nsinger female sings on vocal\nsection');

    it('knows a score that has a singer line, and not one in a comment', () => {
        assert.equal(hasSinger(band), false);
        assert.equal(hasSinger(sung), true);
        assert.equal(hasSinger(band + '// singer female sings on vocal\n'), false);
        assert.equal(hasSinger('score {\n    singer "neutrino/SEVEN" sings on lead\n}\n'), true);
        // a word that only begins so is not the keyword
        assert.equal(hasSinger('singers = 2\n'), false);
    });

    it('says the next step when a change gives the score its song, and only then', () => {
        assert.equal(gaveASong(band, sung), true);
        assert.equal(gaveASong('', sung), true, 'a new piece with words');
        assert.equal(gaveASong(sung, sung + '\n'), false, 'a score that sang already');
        assert.equal(gaveASong(band, band + '\n'), false);
    });

    it('offers Add a Dance where Shosa is installed, and Shosa where it is not', () => {
        assert.deepEqual(danceNextActions(true), ['Add a Dance…', 'Show the Steps']);
        assert.deepEqual(danceNextActions(false), ['Install Shosa', 'Show the Steps']);
        assert.match(danceNextMessage('Road.lys', true), /^Lily#: Road\.lys has a song now\..*Add a Dance…/);
        assert.match(danceNextMessage('Road.lys', false), /Shosa, a free extension/);
    });

    it('is called on a proposal the musician accepts', () => {
        const source = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'aiTransform.ts'), 'utf8');
        assert.match(source, /deps\.log\('AI proposal \(chat\): accepted\.'\);[\s\S]{0,300}if \(gaveASong\(current, text\)\) \{\s+void offerDanceNext\(currentUri\(uri\)\);/);
        assert.match(extensionSource(), /registerSongToDance\(context\);/);
    });

    describe('the walkthrough', () => {
        const root = path.join(__dirname, '..', '..');
        const manifest = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8'));
        const walkthrough = manifest.contributes.walkthroughs.find((w: { id: string }) => w.id === 'songToDance');
        const steps: { id: string; description: string; media: { markdown: string }; completionEvents: string[] }[] = walkthrough.steps;
        const commandsOf = (text: string) => [...text.matchAll(/\]\(command:([\w.]+)\)/g)].map(m => m[1]);

        it('goes from the song to its voice, its dance and a video', () => {
            assert.deepEqual(steps.map(s => s.id), ['song', 'voice', 'dance', 'video']);
        });

        it('is hidden until it is asked for, as the singing one is', () => {
            assert.equal(walkthrough.when, DANCE_GUIDE_CONTEXT);
            const source = fs.readFileSync(path.join(root, 'src', 'songToDance.ts'), 'utf8');
            assert.match(source, /setContext', DANCE_GUIDE_CONTEXT, true\);\s+try \{\s+await vscode\.commands\.executeCommand\('workbench\.action\.openWalkthrough', `\$\{context\.extension\.id\}#songToDance`/);
        });

        it('links only commands that are there: its own, Cantoria\'s and Shosa\'s', () => {
            const own = new Set<string>(manifest.contributes.commands.map((c: { command: string }) => c.command));
            for (const step of steps) {
                const linked = commandsOf(step.description);
                assert.ok(linked.length > 0, `${step.id} has a button`);
                for (const command of linked) {
                    assert.ok(own.has(command) || command.startsWith('shosa.'), `${step.id}: ${command}`);
                }
                assert.ok(fs.existsSync(path.join(root, step.media.markdown)), step.media.markdown);
            }
            assert.equal(SHOSA_EXTENSION_ID, 'yotsuda.shosa');
            assert.equal(SHOSA_ADD_DANCE, 'shosa.addDance');
            const dance = steps.find(s => s.id === 'dance')!;
            assert.deepEqual(commandsOf(dance.description), ['lilysharp.danceWithShosa', 'lilysharp.installShosa']);
            assert.deepEqual(dance.completionEvents, ['onCommand:shosa.addDance']);
        });
    });
});
