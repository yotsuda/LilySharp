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
import {
    DETTATO_INPUTS, chartForConversion, dettatoArgs, dettatoFailure, judgeDettato, locateDettato, parseDettatoVersion,
    besideChart, replaceQuestion,
} from '../src/dettatoCore';
import { newScoreSources } from '../src/menuContextCore';

describe('Dettato, the recording reader', () => {
    it('is found by the variable first, then by its extension when its reader is on disk', () => {
        const ext = { path: 'C:\\x\\bin\\dettato.exe', version: '0.2.1', protocol: 1, inputs: ['WAV', 'mp3'] };
        assert.deepEqual(locateDettato({ LILYSHARP_DETTATO_PATH: ' D:\\dev\\dettato.exe ' }, ext, () => true),
            { path: 'D:\\dev\\dettato.exe', source: 'environment', inputs: DETTATO_INPUTS });
        assert.deepEqual(locateDettato({}, ext, () => true),
            { path: 'C:\\x\\bin\\dettato.exe', source: 'extension', version: '0.2.1', protocol: 1, inputs: ['wav', 'mp3'] });
        assert.equal(locateDettato({}, ext, () => false), undefined);
        assert.equal(locateDettato({}, undefined), undefined);     // not installed, or not Windows x64
        assert.equal(locateDettato({}, { version: '1' }), undefined);
    });

    it('reads its --version --json', () => {
        assert.deepEqual(parseDettatoVersion('{"version":"0.2.1","protocol":1,"commands":["read"],"inputs":["WAV","mp3"]}\n'),
            { version: '0.2.1', protocol: 1, inputs: ['wav', 'mp3'] });
        assert.deepEqual(parseDettatoVersion('{"version":"0.2.0","protocol":1}'), { version: '0.2.0', protocol: 1, inputs: undefined });
        assert.equal(parseDettatoVersion('0.2.0'), undefined);
        assert.equal(parseDettatoVersion('{"version":2}'), undefined);
    });

    it('is judged as Calamo is: a newer protocol asks to update Lily#, an older one or an old version offers its update', () => {
        assert.deepEqual(judgeDettato({ version: '0.2.0', protocol: 1 }), { kind: 'ok' });
        assert.deepEqual(judgeDettato({ version: '0.10.0', protocol: 1 }), { kind: 'ok' });
        const newer = judgeDettato({ version: '0.5.0', protocol: 2 });
        assert.equal(newer.kind, 'updateLilySharp');
        assert.match((newer as { text: string }).text, /Update Lily#\.$/);
        const older = judgeDettato({ version: '0.1.0', protocol: 0 });
        assert.deepEqual([older.kind, (older as { goesOn: boolean }).goesOn], ['offerUpdate', false]);
        const old = judgeDettato({ version: '0.1.9', protocol: 1 });
        assert.deepEqual([old.kind, (old as { goesOn: boolean }).goesOn], ['offerUpdate', true]);
        assert.match((old as { text: string }).text, /goes on with this one otherwise/);
    });

    it('is called with the recording and the chart, --force only when the person agreed to replace it', () => {
        assert.deepEqual(dettatoArgs('C:\\a b\\曲.m4a', 'C:\\a b\\曲.lys', false), ['C:\\a b\\曲.m4a', '-o', 'C:\\a b\\曲.lys']);
        assert.deepEqual(dettatoArgs('x.wav', 'x.lys', true), ['x.wav', '-o', 'x.lys', '--force']);
        assert.equal(chartForConversion('C:\\songs\\take 1.mp3'), 'C:\\songs\\take 1.lys');
        assert.equal(chartForConversion('/a.b/rec'), '/a.b/rec.lys');
    });

    it('says why it did not write the chart in Calamo\'s words: the recording cannot be read, or the reader failed', () => {
        assert.deepEqual(dettatoFailure(1, 'listening to x.mp3\nthe beats\ndettato: x.mp3 is shorter than 5 seconds\n', 'x.mp3'),
            { text: 'Lily#: x.mp3 cannot be read: x.mp3 is shorter than 5 seconds', showOutput: false });
        assert.deepEqual(dettatoFailure(2, '', 'x.mp3'), { text: 'Lily#: x.mp3 cannot be read.', showOutput: false });
        // The reader names the recording itself: said once (Dettato's note of 2026-11-16).
        assert.deepEqual(dettatoFailure(1, 'dettato: x.wav was not read: shorter than 5 seconds', 'x.wav'),
            { text: 'Lily#: x.wav cannot be read: shorter than 5 seconds', showOutput: false });
        assert.deepEqual(dettatoFailure(3, 'dettato: the models are missing', 'x.mp3'),
            { text: 'Lily#: the Dettato reader failed (exit code 3): the models are missing', showOutput: true });
    });

    it('asks before writing over a chart already there, offering to write the reading beside it', () => {
        assert.equal(replaceQuestion('take.lys', 0),
            'take.lys is already there. Replace it with Dettato\'s reading, or write the reading beside it to compare?');
        assert.equal(replaceQuestion('take.lys', 1),
            'take.lys is already there. Replace it, and the 1 bar you placed by hand in the recording, with Dettato\'s reading, or write the reading beside it to compare?');
        assert.match(replaceQuestion('take.lys', 12), /the 12 bars you placed by hand/);
    });

    it('writes the reading beside a chart already there under the first free "(Dettato N)" name', () => {
        const taken = new Set<string>();
        const exists = (p: string) => taken.has(p);
        assert.equal(besideChart('C:\\s\\song.lys', exists), 'C:\\s\\song (Dettato).lys');
        taken.add('C:\\s\\song (Dettato).lys');
        assert.equal(besideChart('C:\\s\\song.lys', exists), 'C:\\s\\song (Dettato 2).lys');
        // A file beside a chart of that name counts too: the reading must not take another's recording file.
        taken.add('C:\\s\\song (Dettato 2).dettato.json');
        assert.equal(besideChart('C:\\s\\song.lys', exists), 'C:\\s\\song (Dettato 3).lys');
    });

    it('takes the recordings the Explorer offers it for — the same list as package.json\'s right-click', () => {
        const pkg = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8'));
        const item = pkg.contributes.menus['explorer/context'].find((m: { command: string }) => m.command === 'lilysharp.convertRecording');
        assert.ok(item, 'the right-click item');
        for (const ext of DETTATO_INPUTS) {
            assert.match(item.when, new RegExp(`\\b${ext}\\b`), ext);
        }
        // Shown without the reader too: the command offers it (owner's decision 2026-11-15) — the
        // same condition as Calamo's "Convert Image/PDF to Lily# (.lys)…", in the right-click and
        // in the palette.
        const omr = pkg.contributes.menus['explorer/context'].find((m: { command: string }) => m.command === 'lilysharp.importImage');
        const head = (when: string) => when.slice(0, when.indexOf('&&')).trim();
        assert.equal(head(item.when).replace('dettatoAvailable', 'X'), head(omr.when).replace('omrAvailable', 'X'));
        const palette = pkg.contributes.menus.commandPalette;
        const mine = palette.find((m: { command: string }) => m.command === 'lilysharp.convertRecording');
        const theirs = palette.find((m: { command: string }) => m.command === 'lilysharp.importImage');
        assert.equal(mine.when.replace('dettatoAvailable', 'X'), theirs.when.replace('omrAvailable', 'X'));
    });

    it('keeps Score from File… out of the palette, where the three commands are found by name (owner\'s decision 2026-11-18)', () => {
        const pkg = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8'));
        const palette = pkg.contributes.menus.commandPalette;
        assert.equal(palette.find((m: { command: string }) => m.command === 'lilysharp.newScoreFromFile')?.when, 'false');
        for (const c of ['lilysharp.importMusicXml', 'lilysharp.importImage', 'lilysharp.convertRecording']) {
            assert.notEqual(palette.find((m: { command: string }) => m.command === c)?.when, 'false', c);
        }
    });

    it('lists one entry in File > New File… for the three readers, whose picker follows the palette (owner\'s decision 2026-11-18)', () => {
        const pkg = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8'));
        const newFile = pkg.contributes.menus['file/newFile'].map((m: { command: string }) => m.command);
        assert.ok(newFile.includes('lilysharp.newScoreFromFile'));
        for (const c of ['lilysharp.importMusicXml', 'lilysharp.importImage', 'lilysharp.convertRecording']) {
            assert.ok(!newFile.includes(c), `${c} is in the picker, not the list`);
        }
        const all = (w: boolean, o: boolean, d: boolean) => newScoreSources(w, o, d).map(s => s.command);
        assert.deepEqual(all(true, false, false), ['lilysharp.importMusicXml', 'lilysharp.importImage', 'lilysharp.convertRecording']);
        assert.deepEqual(all(false, false, false), ['lilysharp.importMusicXml']);
        assert.deepEqual(all(false, true, false), ['lilysharp.importMusicXml', 'lilysharp.importImage']);
        assert.deepEqual(all(false, false, true), ['lilysharp.importMusicXml', 'lilysharp.convertRecording']);
        const titles = pkg.contributes.commands
            .filter((c: { command: string }) => c.command === 'lilysharp.importImage' || c.command === 'lilysharp.convertRecording')
            .map((c: { title: string }) => c.title);
        for (const t of titles) assert.match(t, /… \(Preview\)$/, t);
        // Named in New File… as its neighbours are, by their short titles: "Lily# Score …".
        const entries = pkg.contributes.commands
            .filter((c: { command: string }) => newFile.includes(c.command))
            .map((c: { command: string; shortTitle?: string }) => c.shortTitle ?? c.command);
        for (const s of entries) assert.match(s, /^Lily# Score\b/, s);
    });
});
