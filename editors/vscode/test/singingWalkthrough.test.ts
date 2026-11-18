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
import { CANTORIA_OUTDATED_CONTEXT, FIRST_SONGS, cantoriaIsOutdated, firstSongFor,galleryQuery, newestInGallery, noLanguageGuide,noVocoderGuide, noVoiceGuide, unsingableGuide } from '../src/singerCore';
import { extensionSource } from './extensionSource';

// The way from nothing installed to a first voice (owner's request 2026-11-18, after a look at
// what a one-installer singing program asks of its user): the steps are this extension's own
// walkthrough, its buttons the commands that do each step. What it links is only an address that
// does not move - an engine's site by its domain, a project by its repository - and no page deep
// in a site (the owner, the same day).
describe('the singing walkthrough', () => {
    const root = path.join(__dirname, '..', '..');
    const manifest = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8'));
    const walkthrough = manifest.contributes.walkthroughs.find((w: { id: string }) => w.id === 'singing');
    const steps: { id: string; description: string; media: { markdown: string }; completionEvents: string[] }[] = walkthrough.steps;
    // The addresses that may be linked: a domain, or a repository's root.
    const STANDING = ['https://github.com/openvpi/DiffSinger', 'https://github.com/openvpi/vocoders', 'https://github.com/stakira/OpenUtau',
        'https://studio-neutrino.com/', 'https://voicevox.hiroshiba.jp/'];
    const movingLinks = (text: string) => [...text.matchAll(/https?:\/\/[^\s)\]]+/g)].map(m => m[0]).filter(u => !STANDING.includes(u));
    const commandsOf = (text: string) => [...text.matchAll(/\]\(command:([\w.]+)\)/g)].map(m => m[1]);

    it('goes from the extension to an engine to a first song', () => {
        assert.deepEqual(steps.map(s => s.id), ['cantoria', 'cantoria-upgrade', 'engine', 'check', 'first-song']);
    });

    // Owner's request 2026-11-18: with a Cantoria installed that is not the newest, the first step
    // says "Upgrade". A step's words cannot change, so there are two steps and a context shows one.
    it('says Upgrade for the first step while the Cantoria installed is not the newest', () => {
        const when = (id: string) => (steps.find(s => s.id === id) as { when?: string }).when;
        assert.equal(CANTORIA_OUTDATED_CONTEXT, 'lilysharp.cantoriaOutdated');
        assert.equal(when('cantoria'), '!lilysharp.cantoriaOutdated');
        assert.equal(when('cantoria-upgrade'), 'lilysharp.cantoriaOutdated');
        const upgrade = steps.find(s => s.id === 'cantoria-upgrade')!;
        assert.deepEqual(commandsOf(upgrade.description), ['lilysharp.upgradeCantoria']);
        assert.match(upgrade.description, /\[Upgrade Cantoria\]/);
        // VS Code keeps a checked step checked for good, and the next newer version must find this
        // one unchecked: its event is a context nothing sets. Upgraded, the context goes false and
        // the step shown is "Install" again, which an installed Cantoria has checked off.
        assert.deepEqual(upgrade.completionEvents, ['onContext:lilysharp.cantoriaUpgraded']);
        assert.doesNotMatch(extensionSource(), /cantoriaUpgraded\b/);
        // The gallery is asked each time the steps are opened, before they are.
        assert.match(extensionSource(), /'lilysharp\.singingGuide', true\);[^;]*?await checkCantoriaUpgrade\(\);\s+try \{/);
    });

    it('knows the newest Cantoria from the gallery\'s answer, and when the one installed is older', () => {
        assert.deepEqual(galleryQuery('yotsuda.cantoria'), { filters: [{ criteria: [{ filterType: 7, value: 'yotsuda.cantoria' }] }], flags: 17 });
        const pre = [{ key: 'Microsoft.VisualStudio.Code.PreRelease', value: 'true' }];
        const answer = { results: [{ extensions: [
            { publisher: { publisherName: 'someone' }, extensionName: 'cantoria', versions: [{ version: '9.0.0' }] },
            { publisher: { publisherName: 'Yotsuda' }, extensionName: 'cantoria', versions: [
                { version: '0.11.0', properties: pre }, { version: '0.4.0' }, { version: '0.10.1', properties: [] }, { version: '0.10.1' }, { version: 'next' }] }] }] };
        assert.equal(newestInGallery(answer, 'yotsuda.cantoria'), '0.10.1');     // by numbers (0.10 after 0.4), no pre-release, its own publisher's
        for (const none of [undefined, null, 'offline', {}, { results: [] }, { results: [{ extensions: [] }] }, { results: [{}] }]) { assert.equal(newestInGallery(none, 'yotsuda.cantoria'), undefined); }
        assert.equal(cantoriaIsOutdated('0.4.0', '0.10.1'), true);
        assert.equal(cantoriaIsOutdated('0.10.1', '0.10.1'), false);
        assert.equal(cantoriaIsOutdated('0.11.0', '0.10.1'), false);            // a build newer than the gallery's (the developer's)
        assert.equal(cantoriaIsOutdated(undefined, '0.10.1'), false);           // not installed: the step is Install
        assert.equal(cantoriaIsOutdated('0.4.0', undefined), false);            // the gallery did not answer
        assert.equal(cantoriaIsOutdated('0.4.0', '0.10.1', '0.10.1'), false);   // upgraded this session; the one running still says 0.4.0
        assert.equal(cantoriaIsOutdated('0.4.0', '0.10.2', '0.10.1'), true);    // …and a newer one came out since
    });

    it('is not opened by installing Lily#: it is hidden until it is asked for', () => {
        assert.equal(walkthrough.when, 'lilysharp.singingGuide');
        assert.match(extensionSource(), /executeCommand\('setContext', 'lilysharp\.singingGuide', true\);[^;]*?await checkCantoriaUpgrade\(\);\s+try \{\s+await vscode\.commands\.executeCommand\('workbench\.action\.openWalkthrough', `\$\{context\.extension\.id\}#singing`, false\);/);
    });

    it('has a button for every step, on a command that is there', () => {
        const own = new Set<string>(manifest.contributes.commands.map((c: { command: string }) => c.command));
        // Cantoria's own, as its package.json names them.
        const cantoria = new Set(['cantoria.findEngines', 'cantoria.chooseVoicesFolder', 'cantoria.chooseNeutrinoFolder', 'cantoria.chooseVoicevoxFolder', 'cantoria.checkSetup']);
        for (const step of steps) {
            const commands = commandsOf(step.description);
            assert.ok(commands.length > 0, step.id);
            for (const c of commands) {
                assert.ok(c.startsWith('lilysharp.') ? own.has(c) : cantoria.has(c), `${step.id}: ${c}`);
                if (c.startsWith('lilysharp.')) { assert.match(extensionSource(), new RegExp(`registerCommand\\('${c.replace('.', '\\.')}'`)); }
            }
        }
        assert.deepEqual(commandsOf(steps.find(s => s.id === 'engine')!.description), ['cantoria.findEngines', 'cantoria.chooseVoicesFolder', 'cantoria.chooseNeutrinoFolder', 'cantoria.chooseVoicevoxFolder']);
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

    it('carries its pages and its song, and links only an address that does not move', () => {
        for (const step of steps) {
            const page = fs.readFileSync(path.join(root, step.media.markdown), 'utf8');
            assert.deepEqual(movingLinks(page + step.description), [], step.id);
        }
        // …and the engines' page does link each engine's own site.
        const engines = fs.readFileSync(path.join(root, 'walkthrough', 'engines.md'), 'utf8');
        for (const address of ['https://github.com/openvpi/DiffSinger', 'https://studio-neutrino.com/', 'https://voicevox.hiroshiba.jp/']) { assert.ok(engines.includes(address), address); }
        const song = fs.readFileSync(path.join(root, 'walkthrough', 'first-song.lys'), 'utf8');
        assert.match(song, /^singer female on melody$/m);
        assert.match(song, /lyrics words \{/);
        // A song for each language Cantoria sings (owner's request 2026-11-18), each the opening
        // of a song in the public domain, its words saying their language where it is not Japanese.
        assert.deepEqual(FIRST_SONGS, { ja: 'first-song.lys', en: 'first-song-en.lys', zh: 'first-song-zh.lys', ko: 'first-song-ko.lys' });
        const words: Record<string, RegExp> = { ja: /lyrics words \{\s+う さ ぎ \|/, en: /lyrics words language english \{\s+Twin- kle,/, zh: /lyrics words language chinese \{\s+长 亭 外 \|/, ko: /lyrics words language korean \{\s+아 리 랑 \|/ };
        for (const [language, file] of Object.entries(FIRST_SONGS)) {
            const text = fs.readFileSync(path.join(root, 'walkthrough', file), 'utf8');
            assert.match(text, /^singer female on melody$/m, file);
            assert.match(text, words[language], file);
            assert.match(text, /public domain|traditional/, file);
        }
        assert.match(extensionSource(), /joinPath\(context\.extensionUri, 'walkthrough', await firstSongFile\(\)\)/);
    });

    it('opens the first song in a language the voices installed sing', () => {
        const ja = { language: 'ja' }, tiger = { language: 'ja', languages: ['ja', 'en', 'zh'] }, zh = { language: 'zh' }, ko = { languages: ['ko'] }, silent = {};
        assert.equal(firstSongFor([], 'en'), 'first-song.lys');                 // nothing installed yet: every engine of the steps sings Japanese
        assert.equal(firstSongFor([silent], 'en'), 'first-song.lys');           // a voice that does not say what it sings
        assert.equal(firstSongFor([ja], 'en'), 'first-song.lys');
        assert.equal(firstSongFor([tiger], 'en'), 'first-song-en.lys');         // the editor's language first, where a voice sings it
        assert.equal(firstSongFor([tiger], 'zh-CN'), 'first-song-zh.lys');
        assert.equal(firstSongFor([tiger], 'fr'), 'first-song.lys');            // …else Japanese before English, Chinese, Korean
        assert.equal(firstSongFor([zh], 'ja'), 'first-song-zh.lys');            // no Japanese voice: what there is
        assert.equal(firstSongFor([ko, zh], 'ja'), 'first-song-zh.lys');
        assert.equal(firstSongFor([ko], 'en-US'), 'first-song-ko.lys');
        assert.equal(firstSongFor([ja, ko], 'KO'), 'first-song-ko.lys');
        // Packaged: nothing in .vscodeignore leaves the folder out.
        assert.doesNotMatch(fs.readFileSync(path.join(root, '.vscodeignore'), 'utf8'), /^walkthrough/m);
    });

    it('is what every guide to a voice leads with, as a command and not an address', () => {
        const voices = [{ id: 'neutrino/X', engine: 'neutrino', name: 'X', language: 'ja' }];
        const guides = [noVoiceGuide(true), noLanguageGuide('ja', voices), noLanguageGuide('en', voices), noVocoderGuide('the voice bundles no vocoder'), unsingableGuide('no installed voice sings Latin (la)')];
        for (const g of guides) {
            assert.deepEqual(g.actions[0], { label: 'How to Install a Voice', command: 'lilysharp.setUpSingingVoice' });
            assert.ok(g.actions.every(a => !('url' in a)), g.message);
            assert.doesNotMatch(g.message, /https?:/);
            if ('documentation' in g) { assert.deepEqual(movingLinks((g as { documentation: string }).documentation), []); }
        }
    });
});
