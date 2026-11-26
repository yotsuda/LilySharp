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
    ORECCHIANTE_INPUTS, chartForConversion, orecchianteArgs, orecchianteFailure, judgeOrecchiante, locateOrecchiante, parseOrecchianteVersion,
    besideChart, replaceQuestion, canFetchLyrics, lyricsArgs, lyricsIdArgs, lyricsListArgs, lyricsOutcome, lyricsPathFor, lyricsQuestion,
    lyricsSearchStart, lyricsSongItem, parseLyricsList,
} from '../src/orecchianteCore';
import { newScoreStarts } from '../src/menuContextCore';

describe('Orecchiante, the recording reader', () => {
    it('is found by the variable first, then by its extension when its reader is on disk', () => {
        const ext = { path: 'C:\\x\\bin\\orecchiante.exe', version: '0.2.1', protocol: 1, inputs: ['WAV', 'mp3'] };
        assert.deepEqual(locateOrecchiante({ LILYSHARP_ORECCHIANTE_PATH: ' D:\\dev\\orecchiante.exe ' }, ext, () => true),
            { path: 'D:\\dev\\orecchiante.exe', source: 'environment', inputs: ORECCHIANTE_INPUTS });
        assert.deepEqual(locateOrecchiante({}, ext, () => true),
            { path: 'C:\\x\\bin\\orecchiante.exe', source: 'extension', version: '0.2.1', protocol: 1, inputs: ['wav', 'mp3'] });
        assert.equal(locateOrecchiante({}, ext, () => false), undefined);
        assert.equal(locateOrecchiante({}, undefined), undefined);     // not installed, or not Windows x64
        assert.equal(locateOrecchiante({}, { version: '1' }), undefined);
    });

    it('reads its --version --json', () => {
        assert.deepEqual(parseOrecchianteVersion('{"version":"0.2.1","protocol":1,"commands":["read"],"inputs":["WAV","mp3"]}\n'),
            { version: '0.2.1', protocol: 1, inputs: ['wav', 'mp3'], commands: ['read'] });
        assert.deepEqual(parseOrecchianteVersion('{"version":"0.2.0","protocol":1}'), { version: '0.2.0', protocol: 1, inputs: undefined, commands: undefined });
        assert.equal(parseOrecchianteVersion('0.2.0'), undefined);
        assert.equal(parseOrecchianteVersion('{"version":2}'), undefined);
    });

    it('fetches the words only with a reader that can list them to choose from, into <name>.lrc beside the recording', () => {
        assert.equal(canFetchLyrics({ version: '0.18.0', protocol: 1, commands: ['read', 'lyrics', 'lyrics-choose'] }), true);
        assert.equal(canFetchLyrics({ version: '0.17.0', protocol: 1, commands: ['read', 'lyrics'] }), false);
        assert.equal(canFetchLyrics({ version: '0.2.0', protocol: 1 }), false);
        assert.equal(lyricsPathFor('C:\\m\\今宵の月のように.mp3'), 'C:\\m\\今宵の月のように.lrc');
        assert.equal(lyricsPathFor('C:\\m.d\\song'), 'C:\\m.d\\song.lrc');
        assert.deepEqual(lyricsArgs('C:\\m\\a.mp3'), ['lyrics', 'C:\\m\\a.mp3']);
        assert.deepEqual(lyricsListArgs('C:\\m\\a.mp3'), ['lyrics', 'C:\\m\\a.mp3', '--list']);
        assert.deepEqual(lyricsListArgs('C:\\m\\a.mp3', '  '), ['lyrics', 'C:\\m\\a.mp3', '--list']);
        assert.deepEqual(lyricsListArgs('C:\\m\\a.mp3', ' american band grand funk '), ['lyrics', 'C:\\m\\a.mp3', '--list', '--query', 'american band grand funk']);
        assert.deepEqual(lyricsIdArgs('C:\\m\\a.mp3', 17618460), ['lyrics', 'C:\\m\\a.mp3', '--id', '17618460', '--force']);
        assert.match(lyricsQuestion('a.mp3'), /title, artist and length .* lrclib\.net/);
        assert.match(lyricsQuestion('a.mp3'), /only when they are Japanese or English\.$/);   // Orecchiante's request 27
    });

    it('reads what the fetch came to by its exit code, with the entry found or the reason', () => {
        const found = '今宵の月のように by エレファントカシマシ (明日に向かって走れ), 4:42\n'
            + '  今宵の月のように by エレファントカシマシ (明日に向かって走れ-月夜の歌-), 282 s, lines with their times (LRCLIB 10699791)\n';
        assert.deepEqual(lyricsOutcome(0, found), {
            kind: 'found', entry: '今宵の月のように by エレファントカシマシ (明日に向かって走れ-月夜の歌-), 282 s, lines with their times (LRCLIB 10699791)' });
        assert.deepEqual(lyricsOutcome(1, 'x, 3:00\norecchiante: LRCLIB has no lyrics for x within 2 s of 3:00 (--artist may find them)\n'),
            { kind: 'notFound', reason: 'LRCLIB has no lyrics for x within 2 s of 3:00 (--artist may find them)' });
        assert.deepEqual(lyricsOutcome(3, 'orecchiante: lrclib.net did not answer'), { kind: 'unreached', reason: 'lrclib.net did not answer' });
        assert.deepEqual(lyricsOutcome(2, ''), { kind: 'failed', reason: '' });
    });

    // Orecchiante's request 28: the songs LRCLIB has, to choose from, and a search by other words.
    it('reads the list of songs and shows each with its length against the recording', () => {
        const list = parseLyricsList('{"title": "wab", "artist": null, "query": null, "seconds": 209, "songs": ['
            + '{"id": 17618460, "title": "We\'re An American Band", "artist": "Grand Funk", "album": "We\'re An American Band PT-SHM", "seconds": 208.1, "timed": true, "words": true},'
            + '{"id": 5, "title": "Live", "artist": null, "album": null, "seconds": 240, "timed": false, "words": true},'
            + '{"id": 6, "title": "Instrumental", "seconds": 209, "timed": false, "words": false},'
            + '{"title": "no id"}]}\n');
        assert.ok(list);
        assert.deepEqual([list.title, list.artist, list.query, list.seconds], ['wab', undefined, undefined, 209]);
        assert.deepEqual(list.songs.map(s => [s.id, s.words]), [[17618460, true], [5, true], [6, false]]);
        assert.deepEqual(lyricsSongItem(list.songs[0], 209), {
            label: 'We\'re An American Band — Grand Funk',
            description: 'We\'re An American Band PT-SHM · 3:28 (1 s shorter than the recording) · timed' });
        assert.deepEqual(lyricsSongItem(list.songs[1], 209), { label: 'Live', description: '4:00 (31 s longer than the recording) · words without times' });
        assert.equal(lyricsSongItem(list.songs[2], 209).description, '3:29 (as long as the recording) · words without times');
        assert.equal(lyricsSongItem(list.songs[2]).description, '3:29 · words without times');
        assert.equal(parseLyricsList('not json'), undefined);
        assert.equal(parseLyricsList('{"title": "x"}'), undefined);
    });

    it('starts a search from the last one, else from the title and artist looked for', () => {
        assert.equal(lyricsSearchStart({ title: 'wab', songs: [] }), 'wab');
        assert.equal(lyricsSearchStart({ title: '今宵の月のように', artist: 'エレファントカシマシ', songs: [] }), '今宵の月のように エレファントカシマシ');
        assert.equal(lyricsSearchStart({ title: 'wab', query: 'grand funk', songs: [] }), 'grand funk');
        assert.equal(lyricsSearchStart(undefined), '');
    });

    it('is judged as Calamo is: a newer protocol asks to update Lily#, an older one or an old version offers its update', () => {
        // (0.11.0 is the floor: the reader under its new name, Orecchiante — its request 20, 2026-11-20)
        assert.deepEqual(judgeOrecchiante({ version: '0.11.0', protocol: 1 }), { kind: 'ok' });
        assert.deepEqual(judgeOrecchiante({ version: '0.12.3', protocol: 1 }), { kind: 'ok' });
        const beforeTheName = judgeOrecchiante({ version: '0.10.0', protocol: 1 });
        assert.deepEqual([beforeTheName.kind, (beforeTheName as { goesOn: boolean }).goesOn], ['offerUpdate', true]);
        const newer = judgeOrecchiante({ version: '0.5.0', protocol: 2 });
        assert.equal(newer.kind, 'updateLilySharp');
        assert.match((newer as { text: string }).text, /Update Lily#\.$/);
        const older = judgeOrecchiante({ version: '0.1.0', protocol: 0 });
        assert.deepEqual([older.kind, (older as { goesOn: boolean }).goesOn], ['offerUpdate', false]);
        const old = judgeOrecchiante({ version: '0.1.9', protocol: 1 });
        assert.deepEqual([old.kind, (old as { goesOn: boolean }).goesOn], ['offerUpdate', true]);
        assert.match((old as { text: string }).text, /goes on with this one otherwise/);
    });

    it('is called with the recording and the chart, --force only when the person agreed to replace it', () => {
        assert.deepEqual(orecchianteArgs('C:\\a b\\曲.m4a', 'C:\\a b\\曲.lys', false), ['C:\\a b\\曲.m4a', '-o', 'C:\\a b\\曲.lys']);
        assert.deepEqual(orecchianteArgs('x.wav', 'x.lys', true), ['x.wav', '-o', 'x.lys', '--force']);
        assert.equal(chartForConversion('C:\\songs\\take 1.mp3'), 'C:\\songs\\take 1.lys');
        assert.equal(chartForConversion('/a.b/rec'), '/a.b/rec.lys');
    });

    it('says why it did not write the chart in Calamo\'s words: the recording cannot be read, or the reader failed', () => {
        assert.deepEqual(orecchianteFailure(1, 'listening to x.mp3\nthe beats\norecchiante: x.mp3 is shorter than 5 seconds\n', 'x.mp3'),
            { text: 'Lily#: x.mp3 cannot be read: x.mp3 is shorter than 5 seconds', showOutput: false });
        assert.deepEqual(orecchianteFailure(2, '', 'x.mp3'), { text: 'Lily#: x.mp3 cannot be read.', showOutput: false });
        // The reader names the recording itself: said once (Orecchiante's note of 2026-11-16).
        assert.deepEqual(orecchianteFailure(1, 'orecchiante: x.wav was not read: shorter than 5 seconds', 'x.wav'),
            { text: 'Lily#: x.wav cannot be read: shorter than 5 seconds', showOutput: false });
        assert.deepEqual(orecchianteFailure(3, 'orecchiante: the models are missing', 'x.mp3'),
            { text: 'Lily#: the Orecchiante reader failed (exit code 3): the models are missing', showOutput: true });
    });

    it('asks before writing over a chart already there, offering to write the reading beside it', () => {
        assert.equal(replaceQuestion('take.lys', 0),
            'take.lys is already there. Replace it with Orecchiante\'s reading, or write the reading beside it to compare?');
        assert.equal(replaceQuestion('take.lys', 1),
            'take.lys is already there. Replace it, and the 1 bar you placed by hand in the recording, with Orecchiante\'s reading, or write the reading beside it to compare?');
        assert.match(replaceQuestion('take.lys', 12), /the 12 bars you placed by hand/);
    });

    it('writes the reading beside a chart already there under the first free "(Orecchiante N)" name', () => {
        const taken = new Set<string>();
        const exists = (p: string) => taken.has(p);
        assert.equal(besideChart('C:\\s\\song.lys', exists), 'C:\\s\\song (Orecchiante).lys');
        taken.add('C:\\s\\song (Orecchiante).lys');
        assert.equal(besideChart('C:\\s\\song.lys', exists), 'C:\\s\\song (Orecchiante 2).lys');
        // A file beside a chart of that name counts too: the reading must not take another's recording file.
        taken.add('C:\\s\\song (Orecchiante 2).orecchiante.json');
        assert.equal(besideChart('C:\\s\\song.lys', exists), 'C:\\s\\song (Orecchiante 3).lys');
    });

    it('takes the recordings the Explorer offers it for — the same list as package.json\'s right-click', () => {
        const pkg = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8'));
        const item = pkg.contributes.menus['explorer/context'].find((m: { command: string }) => m.command === 'lilysharp.convertRecording');
        assert.ok(item, 'the right-click item');
        for (const ext of ORECCHIANTE_INPUTS) {
            assert.match(item.when, new RegExp(`\\b${ext}\\b`), ext);
        }
        // Shown without the reader too: the command offers it (owner's decision 2026-11-15) — the
        // same condition as Calamo's "Convert Image/PDF to Lily# (.lys)…", in the right-click and
        // in the palette.
        const omr = pkg.contributes.menus['explorer/context'].find((m: { command: string }) => m.command === 'lilysharp.importImage');
        const head = (when: string) => when.slice(0, when.indexOf('&&')).trim();
        assert.equal(head(item.when).replace('orecchianteAvailable', 'X'), head(omr.when).replace('omrAvailable', 'X'));
        const palette = pkg.contributes.menus.commandPalette;
        const mine = palette.find((m: { command: string }) => m.command === 'lilysharp.convertRecording');
        const theirs = palette.find((m: { command: string }) => m.command === 'lilysharp.importImage');
        assert.equal(mine.when.replace('orecchianteAvailable', 'X'), theirs.when.replace('omrAvailable', 'X'));
    });

    it('keeps the New File… picker out of the palette, where each command is found by name (owner\'s decisions 2026-11-18, 2026-11-26)', () => {
        const pkg = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8'));
        const palette = pkg.contributes.menus.commandPalette;
        assert.equal(palette.find((m: { command: string }) => m.command === 'lilysharp.newScoreMenu')?.when, 'false');
        for (const c of ['lilysharp.newScore', 'lilysharp.composeWithAiInChat', 'lilysharp.importMusicXml', 'lilysharp.importImage', 'lilysharp.convertRecording']) {
            assert.notEqual(palette.find((m: { command: string }) => m.command === c)?.when, 'false', c);
        }
    });

    it('lists one Lily# entry in File > New File…, its picker grouped by where a piece starts (owner\'s decision 2026-11-26)', () => {
        const pkg = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8'));
        const newFile = pkg.contributes.menus['file/newFile'].map((m: { command: string }) => m.command);
        assert.deepEqual(newFile, ['lilysharp.newScoreMenu']);
        const shown = (w: boolean, o: boolean, d: boolean) => newScoreStarts(w, o, d).map(s => s.command ?? `— ${s.label}`);
        const fresh = ['— Start fresh', 'lilysharp.newScore', 'lilysharp.composeWithAiInChat', '— From music you have'];
        assert.deepEqual(shown(true, false, false), [...fresh, 'lilysharp.convertRecording', 'lilysharp.importImage', 'lilysharp.importMusicXml']);
        assert.deepEqual(shown(false, false, false), [...fresh, 'lilysharp.importMusicXml']);
        assert.deepEqual(shown(false, true, false), [...fresh, 'lilysharp.importImage', 'lilysharp.importMusicXml']);
        assert.deepEqual(shown(false, false, true), [...fresh, 'lilysharp.convertRecording', 'lilysharp.importMusicXml']);
        // Blank first (Enter alone starts it) and the one that edits the template.
        const items = newScoreStarts(true, false, false).filter(s => s.command);
        assert.equal(items[0].command, 'lilysharp.newScore');
        assert.deepEqual(items.filter(s => s.template).map(s => s.command), ['lilysharp.newScore']);
        // The readers still in preview say so, as their palette titles do.
        for (const s of items.filter(s => s.command === 'lilysharp.importImage' || s.command === 'lilysharp.convertRecording')) {
            assert.equal(s.description, 'Preview', s.label);
        }
        const titles = pkg.contributes.commands
            .filter((c: { command: string }) => c.command === 'lilysharp.importImage' || c.command === 'lilysharp.convertRecording')
            .map((c: { title: string }) => c.title);
        for (const t of titles) assert.match(t, /… \(Preview\)$/, t);
        const entry = pkg.contributes.commands.find((c: { command: string }) => c.command === 'lilysharp.newScoreMenu');
        assert.equal(entry.shortTitle, 'Lily# Score (.lys)…');
        const ext = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
        assert.match(ext, /onDidTriggerItemButton\(\(\) => run\('lilysharp\.editNewScoreTemplate'\)\)/);
    });
});
