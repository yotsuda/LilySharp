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
import { noLanguageGuide, noVocoderGuide, noVoiceGuide, unsingableGuide } from '../src/singerCore';
import { extensionSource } from './extensionSource';

// The way from nothing installed to a first voice (owner's request 2026-11-18, after a look at
// what a one-installer singing program asks of its user): the steps are this extension's own
// walkthrough, its buttons the commands that do each step, and nothing in it is a link.
describe('the singing walkthrough', () => {
    const root = path.join(__dirname, '..', '..');
    const manifest = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8'));
    const walkthrough = manifest.contributes.walkthroughs.find((w: { id: string }) => w.id === 'singing');
    const steps: { id: string; description: string; media: { markdown: string }; completionEvents: string[] }[] = walkthrough.steps;
    const commandsOf = (text: string) => [...text.matchAll(/\]\(command:([\w.]+)\)/g)].map(m => m[1]);

    it('goes from the extension to an engine to a first song', () => {
        assert.deepEqual(steps.map(s => s.id), ['cantoria', 'engine', 'check', 'first-song']);
    });

    it('is not opened by installing Lily#: it is hidden until it is asked for', () => {
        assert.equal(walkthrough.when, 'lilysharp.singingGuide');
        assert.match(extensionSource(), /executeCommand\('setContext', 'lilysharp\.singingGuide', true\);\s+try \{\s+await vscode\.commands\.executeCommand\('workbench\.action\.openWalkthrough', `\$\{context\.extension\.id\}#singing`, false\);/);
    });

    it('has a button for every step, on a command that is there', () => {
        const own = new Set<string>(manifest.contributes.commands.map((c: { command: string }) => c.command));
        // Cantoria's own, as its package.json names them.
        const cantoria = new Set(['cantoria.chooseVoicesFolder', 'cantoria.chooseNeutrinoFolder', 'cantoria.chooseVoicevoxFolder', 'cantoria.checkSetup']);
        for (const step of steps) {
            const commands = commandsOf(step.description);
            assert.ok(commands.length > 0, step.id);
            for (const c of commands) {
                assert.ok(c.startsWith('lilysharp.') ? own.has(c) : cantoria.has(c), `${step.id}: ${c}`);
                if (c.startsWith('lilysharp.')) { assert.match(extensionSource(), new RegExp(`registerCommand\\('${c.replace('.', '\\.')}'`)); }
            }
        }
        assert.deepEqual(commandsOf(steps[1].description), ['cantoria.chooseVoicesFolder', 'cantoria.chooseNeutrinoFolder', 'cantoria.chooseVoicevoxFolder']);
    });

    it('checks a step off by what the step does', () => {
        const events = (id: string) => steps.find(s => s.id === id)!.completionEvents;
        assert.deepEqual(events('cantoria'), ['extensionInstalled:yotsuda.cantoria']);
        assert.deepEqual(events('engine'), ['onSettingChanged:cantoria.voicesFolder', 'onSettingChanged:cantoria.neutrinoFolder', 'onSettingChanged:cantoria.voicevoxFolder']);
        assert.deepEqual(events('check'), ['onCommand:cantoria.checkSetup']);
        assert.deepEqual(events('first-song'), ['onCommand:lilysharp.openFirstSong']);
        for (const step of steps) {
            for (const e of step.completionEvents) { assert.match(e, /^(onCommand|onSettingChanged|onContext|extensionInstalled|onView|onLink):/); }
        }
    });

    it('carries its pages and its song, and none of them leans on a link', () => {
        for (const step of steps) {
            const page = fs.readFileSync(path.join(root, step.media.markdown), 'utf8');
            assert.doesNotMatch(page + step.description, /https?:/, step.id);
        }
        const song = fs.readFileSync(path.join(root, 'walkthrough', 'first-song.lys'), 'utf8');
        assert.match(song, /^singer female on melody$/m);
        assert.match(song, /lyrics words \{/);
        // Packaged: nothing in .vscodeignore leaves the folder out.
        assert.doesNotMatch(fs.readFileSync(path.join(root, '.vscodeignore'), 'utf8'), /^walkthrough/m);
    });

    it('is what every guide to a voice leads with, as a command and not an address', () => {
        const voices = [{ id: 'neutrino/X', engine: 'neutrino', name: 'X', language: 'ja' }];
        const guides = [noVoiceGuide(true), noLanguageGuide('ja', voices), noLanguageGuide('en', voices), noVocoderGuide('the voice bundles no vocoder'), unsingableGuide('no installed voice sings Latin (la)')];
        for (const g of guides) {
            assert.deepEqual(g.actions[0], { label: 'How to Install a Voice', command: 'lilysharp.voiceInstallGuide' });
            assert.ok(g.actions.every(a => !('url' in a)), g.message);
            assert.doesNotMatch(g.message, /https?:/);
        }
    });
});
