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
import { CANTORIA_EXTENSION_ID, PREPARE_IDLE_MS, PrepareQueue, StageClock, SungNoteDto, SungVoiceDto, VoiceSampleQueue, cantoriaApiOf, cantoriaScore, fitsGender, learnRenderRate, parseServeLine, prepareModeOf, serveCancelLine, serveRequestLine, afterSingerWord, voiceMenuLabels, voiceMenuKey, voiceMenuTitle, noLanguageGuide, noVoiceGuide, printedWordsNote, singerChoices, singerLanguageColumn, singerValueAt, singerWrittenAt, singsLanguage, stepVoice, sungKey, sungSeconds, voiceKey, voiceListText, voiceRenderEstimateMs, voiceSampleFileName, voiceSampleKey, sampleOctave, parseVoiceSampleKey, voiceSampleScore, writesSinger } from '../src/singerCore';
import { VoiceBarPart, voiceBar, voiceBarShown, voiceProgress, voiceRound, voiceRowProgress, voiceStillSinging, voiceToldFraction } from '../src/webview/playbackCore';

// The singing voice's pure half (docs/singing-design.md §5): the server's voice becomes the
// engine's score, the key names the render, the engine's lines are read, and the Cantoria
// extension's exports are recognised.
describe('the engine\'s score of a sung voice', () => {
    // The owner's 雪やこんこ, its first bar: ゆ, the vowel held on the d, き, や with a breath.
    const voice: SungVoiceDto = {
        Part: 'melody', Language: 'ja', Warnings: [],
        Notes: [
            { T: 0, D: 0.375, P: 60, L: 'ゆ', M: false, B: false, S: 10 },
            { T: 0.375, D: 0.125, P: 62, L: null, M: true, B: false, S: 14 },
            { T: 0.5, D: 0.375, P: 60, L: 'き', M: false, B: false, S: 18 },
            { T: 0.875, D: 0.125, P: 62, L: 'や', M: false, B: true, S: 22 },
        ],
    };

    it('is seconds, pitch and the syllable; a melisma has no lyric; a breath is kept; the whole moved by the lead', () => {
        // VOICE_LEAD_S (0.3 s) before the score's 0: the room for the first syllable's consonant,
        // which the engine places before the beat (owner's report 2026-11-08: さ at 0 sang as た).
        const score = cantoriaScore(voice, 1);
        assert.equal(score.sampleRate, 48000);
        assert.equal(score.language, 'ja');
        assert.deepEqual(score.notes, [
            { start: 0.3, duration: 0.375, pitch: 60, lyric: 'ゆ' },
            { start: 0.675, duration: 0.125, pitch: 62, melisma: true },
            { start: 0.8, duration: 0.375, pitch: 60, lyric: 'き' },
            { start: 1.175, duration: 0.125, pitch: 62, lyric: 'や', breath: true },
        ]);
    });

    it('is slower at a practice speed below one, the way the notes are', () => {
        const score = cantoriaScore(voice, 0.5);
        assert.deepEqual(score.notes.map(n => [n.start, n.duration]), [[0.3, 0.75], [1.05, 0.25], [1.3, 0.75], [2.05, 0.25]]);
        assert.deepEqual(cantoriaScore(voice, 0).notes[1].start, 0.675, 'a speed of 0 is read as 1');
    });

    it('is keyed by the voice and the score, so the speed and the voice each sing anew', () => {
        const a = voiceKey(cantoriaScore(voice, 1), 'MERROW');
        assert.match(a, /^[0-9a-f]{40}$/);
        assert.equal(voiceKey(cantoriaScore(voice, 1), 'MERROW'), a);
        assert.notEqual(voiceKey(cantoriaScore(voice, 0.8), 'MERROW'), a);
        assert.notEqual(voiceKey(cantoriaScore(voice, 1), 'OTHER'), a);
    });
});

describe('the engine\'s wire, and the extension\'s API', () => {
    it('reads the engine\'s answers and leaves its chatter alone', () => {
        assert.deepEqual(parseServeLine('{"ready":true,"backend":"neutrino","voices":["MERROW"]}'), { ready: true, backend: 'neutrino', voices: ['MERROW'] });
        assert.deepEqual(parseServeLine('{"id":"k1","wav":"C:\\\\x.wav","duration":8,"sampleRate":48000,"seconds":0.4}'),
            { id: 'k1', wav: 'C:\\x.wav', duration: 8, sampleRate: 48000, seconds: 0.4 });
        assert.deepEqual(parseServeLine('{"id":"k2","error":"note 0 at 0 s: no lyric"}'), { id: 'k2', error: 'note 0 at 0 s: no lyric' });
        assert.equal(parseServeLine('cantoria: serving neutrino'), null);
        assert.equal(parseServeLine(''), null);
        assert.equal(parseServeLine('{not json'), null);
    });

    it('writes one request a line, the WAV named by the key, the voice the engine\'s own', () => {
        const line = serveRequestLine('abc', { sampleRate: 48000, language: 'ja', notes: [] }, 'C:\\tmp\\abc.wav');
        assert.ok(line.endsWith('\n'));
        assert.deepEqual(JSON.parse(line), { id: 'abc', score: { sampleRate: 48000, language: 'ja', notes: [] }, output: 'C:\\tmp\\abc.wav' });
    });

    it('takes the Cantoria extension\'s exports when they are the first API, and nothing else', () => {
        assert.equal(CANTORIA_EXTENSION_ID, 'yotsuda.cantoria');
        const api = { version: '0.1.0', apiVersion: 1, status: async () => ({ ready: true, voice: 'MERROW', problems: [] }), sing: async () => ({ wav: 'x', duration: 1 }), release: () => undefined };
        assert.equal(cantoriaApiOf(api), api);
        assert.equal(cantoriaApiOf({ ...api, apiVersion: 2 }), undefined);
        assert.equal(cantoriaApiOf({ path: 'calamo.exe' }), undefined);
        assert.equal(cantoriaApiOf(undefined), undefined);
    });
});

// Between a `singer`'s quotes (owner's request 2026-11-09): only the names right for the language.
describe('the singer choices', () => {
    const voices = [
        { id: 'neutrino/MERROW', engine: 'neutrino', name: 'MERROW', label: 'Merrow', language: 'ja' },
        { id: 'diffsinger/TIGER', engine: 'diffsinger', name: 'TIGER', label: 'TIGER', language: 'ja' },
        { id: 'diffsinger/ALICE', engine: 'diffsinger', name: 'ALICE', label: 'Alice', language: 'en' },
        { id: 'diffsinger/NOTSAID', engine: 'diffsinger', name: 'NOTSAID', label: 'x' },
    ];

    it('knows a score that names who sings', () => {
        assert.equal(writesSinger('singer "a/b" sings w'), true);
        assert.equal(writesSinger('singer sings w on m'), true);
        assert.equal(writesSinger('lyrics v { la | }'), false, 'words alone');
        assert.equal(writesSinger('lyrics singer { la | }'), false, 'a track called singer');
        assert.equal(writesSinger('// singer "a/b"'), false, 'a comment');
    });

    it('offers Cantoria only where a voice was asked for, once', () => {
        const ext = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
        assert.match(ext, /if \(cantoriaOffered \|\| vscode\.extensions\.getExtension\(CANTORIA_EXTENSION_ID\)\) \{ return; \}/);
        assert.match(ext, /'workbench\.extensions\.installExtension', CANTORIA_EXTENSION_ID/);
        assert.match(ext, /if \(reason === 'play' && writesSinger\(/);
        assert.equal(ext.match(/offerCantoria\(\);/g)?.length, 4, 'the completion, the hearing, the step keys, the play');
    });

    it('opens the list on the space after singer', () => {
        assert.equal(afterSingerWord('singer '), true);
        assert.equal(afterSingerWord('  singer male '), true);
        assert.equal(afterSingerWord('singer'), false, 'the word is still being typed');
        assert.equal(afterSingerWord('lyrics verse '), false);
        assert.equal(afterSingerWord('misinger '), false);
        assert.equal(afterSingerWord('// singer '), false);
        assert.equal(afterSingerWord('singer "a/b",'), false, 'one voice a line');
        assert.equal(afterSingerWord('singer "a/b" '), false);
    });

    it('lists the voices for the chat, narrowed as asked', () => {
        const all = [
            { id: 'neutrino/A', engine: 'neutrino', name: 'A', label: 'Ann', language: 'ja', gender: 'female' },
            { id: 'neutrino/B', engine: 'neutrino', name: 'B', label: 'B', language: 'ja', gender: 'male' },
            { id: 'diffsinger/T', engine: 'diffsinger', name: 'T', label: 'Tiger', language: 'ja', languages: ['ja', 'en'] },
            { id: 'voicevox/Z', engine: 'voicevox', name: 'Z', label: 'Zed', language: 'ja', licence: 'Credit it' },
        ];
        const names = (text: string) => [...text.matchAll(/^([a-z]+\/\w+):/gm)].map(m => m[1]);
        assert.deepEqual(names(voiceListText(all, {})), ['diffsinger/T', 'neutrino/A', 'neutrino/B', 'voicevox/Z']);
        assert.deepEqual(names(voiceListText(all, { provider: 'Neutrino' })), ['neutrino/A', 'neutrino/B']);
        assert.deepEqual(names(voiceListText(all, { gender: 'male' })), ['diffsinger/T', 'neutrino/B', 'voicevox/Z'], 'a voice that does not state one stays');
        assert.deepEqual(names(voiceListText(all, { provider: 'neutrino', gender: 'female' })), ['neutrino/A']);
        assert.deepEqual(names(voiceListText(all, { language: 'english' })), ['diffsinger/T']);
        assert.deepEqual(names(voiceListText(all, { language: 'en' })), ['diffsinger/T']);
        const one = voiceListText(all, { provider: 'diffsinger' });
        assert.match(one, /^1 voice \(diffsinger\):\ndiffsinger\/T: Tiger · gender not stated · sings ja, en$/m);
        assert.match(one, /Two lines with the same words on one part sing it together/);
        assert.match(voiceListText(all, { provider: 'voicevox' }), /Credit it/);
        assert.match(voiceListText(all, { provider: 'nope' }), /There are: diffsinger, neutrino, voicevox\./);
        assert.match(voiceListText(all, { gender: 'robot' }), /male or female/);
        assert.match(voiceListText(all, { provider: 'neutrino', language: 'en' }), /No voice matches \(neutrino, en\)\. 4 voices/);
        assert.match(voiceListText([], {}), /No singing voice/);
    });

    it('asks the language from inside the name, wherever the caret is on the singer', () => {
        const line = 'lyrics v singer "a/b" {';
        assert.equal(singerLanguageColumn(line, line.indexOf('{') - 1), 17, 'just past the closing quote');
        assert.equal(singerLanguageColumn(line, 10), 17, 'on the word');
        assert.equal(singerLanguageColumn('singer male x', 2), 11, 'a bare gender');
        assert.equal(singerLanguageColumn('lyrics v {', 3), 3, 'off a singer');
    });

    it('finds the name the caret stands on', () => {
        const line = 'singer "a/b" sings w';
        assert.deepEqual(singerValueAt(line, 9), { start: 8, end: 11, value: 'a/b' });
        assert.deepEqual(singerValueAt(line, 1), { start: 8, end: 11, value: 'a/b' }, 'on the word');
        assert.equal(singerValueAt(line, 15), undefined, 'past the name');
        assert.equal(singerWrittenAt(line, 0, 6), 'a/b');
        assert.equal(singerWrittenAt('singer male "a/b"', 0, 6), 'a/b');
        assert.equal(singerWrittenAt('singer male', 0, 6), 'male');
    });

    it('counts every language a voice sings', () => {
        const both = [{ id: 'd/T', engine: 'd', name: 'T', language: 'ja', languages: ['ja', 'en'] }];
        assert.deepEqual(singerChoices(both, 'en').map(c => c.label), ['d/T']);
        assert.deepEqual(singerChoices(both, 'ko'), []);
    });

    it('offers the voices that sing the language', () => {
        assert.deepEqual(singerChoices(voices, 'ja').map(c => c.label), ['diffsinger/TIGER', 'neutrino/MERROW'], 'by name');
        assert.deepEqual(singerChoices(voices, 'en').map(c => c.label), ['diffsinger/ALICE']);
        assert.deepEqual(singerChoices(voices, 'ko'), [], 'no voice sings it: nothing to offer');

    it('offers every voice with no language given', () => {
        assert.ok(singerChoices(voices, undefined).length > singerChoices(voices, 'ja').length);
    });
    });

    it('names the voice on the engine\'s wire when one is asked for', () => {
        assert.equal(JSON.parse(serveRequestLine('k', { sampleRate: 1, language: 'ja', notes: [] }, 'x.wav', 'diffsinger/TIGER')).voice, 'diffsinger/TIGER');
    });
});

// The manifest's side: the two settings, and no voice setting or picker of Lily#'s own (the
// voice is the Cantoria extension's) — as the MIDI output's (midiOutputSetting.test.ts).
// When an edited voice goes to the engine (docs/singing-design.md §5): after the text rests, one
// job a slot, the newest winning.
describe('choosing a voice with the step keys', () => {
    const v = (engine: string, name: string) => ({ id: `${engine}/${name}`, engine });
    const voices = [v('neutrino', 'A'), v('neutrino', 'B'), v('neutrino', 'C'), v('diffsinger', 'X'), v('diffsinger', 'Y')];

    it('finds the name from the word to the closing quote, and nowhere else', () => {
        const line = '  lyrics w singer "neutrino/A" sings m';
        const start = line.indexOf('neutrino');
        const whole = { start, end: start + 'neutrino/A'.length, value: 'neutrino/A' };
        assert.deepEqual(singerValueAt(line, line.indexOf('singer')), whole);
        assert.deepEqual(singerValueAt(line, start + 3), whole);
        assert.deepEqual(singerValueAt(line, whole.end + 1), whole);
        assert.equal(singerValueAt(line, 3), undefined);
        assert.equal(singerValueAt(line, whole.end + 5), undefined);
        assert.equal(singerValueAt('title "singer"', 8), undefined);
        assert.equal(singerValueAt('a-singer "x"', 4), undefined);
    });

    it('reads what the singer under a caret or a selection writes', () => {
        const line = '  lyrics w singer "neutrino/A" sings m';
        const start = line.indexOf('singer');
        assert.equal(singerWrittenAt(line, start, start), 'neutrino/A');
        assert.equal(singerWrittenAt(line, start, start + 6), 'neutrino/A');           // the word selected
        assert.equal(singerWrittenAt(line, line.indexOf('neutrino') + 2, line.indexOf('neutrino') + 2), 'neutrino/A');
        assert.equal(singerWrittenAt(line, 3, 3), undefined);
        assert.equal(singerWrittenAt(line, line.indexOf('sings'), line.indexOf('sings')), undefined);
        assert.equal(singerWrittenAt(line, 3, start + 2), undefined);                  // reaching outside it
    });

    it('takes the gender when no name is written, and nothing in a comment or a string', () => {
        assert.equal(singerWrittenAt('singer male "n/A"', 0, 0), 'n/A');
        assert.equal(singerWrittenAt('singer male', 3, 3), 'male');
        assert.equal(singerWrittenAt('singer female ""', 3, 3), 'female');
        assert.equal(singerWrittenAt('singer ""', 3, 3), '');
        assert.equal(singerWrittenAt('singer manly', 3, 3), undefined);
        assert.equal(singerWrittenAt('// singer "n/A"', 6, 6), undefined);
        assert.equal(singerWrittenAt('title "the singer male"', 13, 13), undefined);
    });

    it('goes up the list with Up and down it with Down, and stops at both ends', () => {
        assert.equal(stepVoice(voices, 'neutrino/B', 1, false), 'neutrino/A');
        assert.equal(stepVoice(voices, 'neutrino/A', -1, false), 'neutrino/B');
        assert.equal(stepVoice(voices, 'diffsinger/Y', 1, false), 'diffsinger/X');
        assert.equal(stepVoice(voices, 'neutrino/A', 1, false), undefined);
        assert.equal(stepVoice(voices, 'diffsinger/Y', -1, false), undefined);
    });

    it('walks from one engine into the next', () => {
        assert.equal(stepVoice(voices, 'neutrino/C', -1, false), 'diffsinger/X');
        assert.equal(stepVoice(voices, 'diffsinger/X', 1, false), 'neutrino/C');
    });

    it('enters the first engine\'s list from a role or an empty name, at the end the key leaves', () => {
        assert.equal(stepVoice(voices, '', -1, false), 'neutrino/A');
        assert.equal(stepVoice(voices, 'soprano', 1, false), 'diffsinger/Y');
    });

    it('crosses to the first voice of the next or previous engine, and does not go round', () => {
        assert.equal(stepVoice(voices, 'neutrino/B', -1, true), 'diffsinger/X');
        assert.equal(stepVoice(voices, 'diffsinger/X', 1, true), 'neutrino/A');
        assert.equal(stepVoice(voices, 'diffsinger/Y', 1, true), 'neutrino/A');
        assert.equal(stepVoice(voices, 'neutrino/C', -1, true), 'diffsinger/X');
        assert.equal(stepVoice(voices, 'diffsinger/X', -1, true), undefined);
        assert.equal(stepVoice(voices, 'neutrino/A', 1, true), undefined);
    });

    it('reads a singer with a gender before the name as one', () => {
        const line = 'singer male "neutrino/A"';
        const start = line.indexOf('neutrino');
        const whole = { start, end: start + 'neutrino/A'.length, value: 'neutrino/A' };
        assert.deepEqual(singerValueAt(line, line.indexOf('male')), whole);
        assert.deepEqual(singerValueAt(line, start), whole);
        assert.equal(singerValueAt('singer man "x"', 8), undefined);
    });

    it('leaves out only a voice that says it is the other gender, in the list and for the keys', () => {
        assert.equal(fitsGender({ gender: 'male' }, 'male'), true);
        assert.equal(fitsGender({ gender: 'female' }, 'male'), false);
        assert.equal(fitsGender({}, 'male'), true);
        assert.equal(fitsGender({ gender: 'female' }, null), true);
    });

    it('lists the voices of the gender written and those that name none', () => {
        const all = [
            { id: 'n/A', engine: 'n', name: 'A', language: 'ja', gender: 'male' },
            { id: 'n/B', engine: 'n', name: 'B', language: 'ja', gender: 'female' },
            { id: 'n/C', engine: 'n', name: 'C', language: 'ja' },
        ];
        const labels = (gender?: string | null) => singerChoices(all, 'ja', gender).map(c => c.label);
        assert.deepEqual(labels(), ['n/A', 'n/B', 'n/C']);
        assert.deepEqual(labels('male'), ['n/A', 'n/C']);
        assert.deepEqual(labels('female'), ['n/B', 'n/C']);
    });

    it('has nowhere to go with one engine or one voice', () => {
        assert.equal(stepVoice(voices.slice(0, 3), 'neutrino/A', 1, true), undefined);
        assert.equal(stepVoice([v('neutrino', 'A')], 'neutrino/A', 1, false), undefined);
        assert.equal(stepVoice([], 'x', 1, false), undefined);
    });

    it('has a phrase for Japanese, and none it cannot write', () => {
        assert.equal(voiceSampleScore('ja')?.language, 'ja');
        assert.equal(voiceSampleScore('ko'), undefined);
        assert.equal(voiceSampleKey('d/T', 'high', 'ja'), 'd/T~high');
        assert.equal(voiceSampleKey('d/T', 'low', 'en'), 'd/T@en~low');
        assert.equal(voiceSampleKey('d/T', 'low'), 'd/T~low');
        assert.deepEqual(parseVoiceSampleKey('d/T@en~low'), { id: 'd/T', language: 'en', octave: 'low' });
        assert.deepEqual(parseVoiceSampleKey('d/T~high'), { id: 'd/T', language: undefined, octave: 'high' });
        assert.equal(voiceSampleFileName('neutrino/MERROW~low'), 'neutrino_MERROW_low-5.wav');
        assert.notEqual(voiceSampleFileName('d/T~low'), voiceSampleFileName('d/T~high'), 'a voice is kept in each octave it is heard in');
        assert.notEqual(voiceSampleFileName('voicevox/ずんだもん-ノーマル~low'), voiceSampleFileName('voicevox/四国めたん-ノーマル~low'));
        assert.deepEqual(voiceSampleScore('en')?.notes.map(n => [n.lyric, !!n.hyphen]), [['hel', true], ['lo', false], ['how', false], ['are', false], ['you', false]]);
        assert.deepEqual(voiceSampleScore('en', 'low')?.notes.map(n => n.pitch), [48, 50, 52, 53, 55]);
        assert.deepEqual(voiceSampleScore('en')?.notes.map(n => [n.start, n.pitch]), voiceSampleScore('ja')?.notes.map(n => [n.start, n.pitch]), 'the melody of the Japanese phrase');
        assert.equal(voiceSampleFileName('d/T@en~low'), 'd_T_en_low-5.wav', 'a new phrase, sung again');
    });

    it('sings the phrase an octave apart, low unless asked', () => {
        const pitches = (octave?: 'high' | 'low') => voiceSampleScore('ja', octave)?.notes.map(n => n.pitch);
        assert.deepEqual(pitches('low'), pitches('high')?.map(p => p - 12));
        assert.deepEqual(pitches(), pitches('low'));
    });

    it('hears a voice in the octave its singer line says, else its own, else low', () => {
        assert.equal(sampleOctave(undefined, 'female'), 'high', 'a voice that says no gender, on a female line');
        assert.equal(sampleOctave(undefined, 'male'), 'low', 'and on a male line');
        assert.equal(sampleOctave('male', 'female'), 'high', 'the line wins over the voice');
        assert.equal(sampleOctave('female', 'male'), 'low');
        assert.equal(sampleOctave('female', null), 'high', 'a line that says neither: the voice');
        assert.equal(sampleOctave('male', undefined), 'low');
        assert.equal(sampleOctave(undefined, null), 'low', 'neither says');
    });

    it('ends the phrase as it is said, with wa', () => {
        assert.equal(voiceSampleScore('ja')?.notes.map(n => n.lyric).join(''), 'こんにちわ');
    });

    it('times each stage from the one before it', () => {
        let t = 100;
        const clock = new StageClock(100, () => t);
        t = 103;
        clock.mark('key queue');
        t = 110;
        clock.mark('server');
        assert.equal(clock.toString(), 'key queue 3 ms, server 7 ms');
        assert.equal(clock.startedAt, 100);
    });
});

describe('the bar under Play', () => {
    it('stays through a repeated preparing while the engine sings, and goes with the answer', () => {
        assert.equal(voiceStillSinging(undefined, 'preparing', true), true);
        assert.equal(voiceStillSinging(true, 'preparing', undefined), true);        // a Play pressed meanwhile
        assert.equal(voiceStillSinging(undefined, 'preparing', undefined), false);
        assert.equal(voiceStillSinging(false, 'preparing', undefined), false);
        assert.equal(voiceStillSinging(true, 'ready', undefined), false);
        assert.equal(voiceStillSinging(true, 'failed', undefined), false);
        assert.equal(voiceStillSinging(true, 'waiting', undefined), false);
    });

    it('shows only while the engine is singing a voice', () => {
        assert.equal(voiceBarShown([{ state: 'preparing', singing: true }]), true);
        assert.equal(voiceBarShown([{ state: 'ready' }, { state: 'preparing', singing: true }]), true);
        assert.equal(voiceBarShown([{ state: 'preparing' }]), false);       // named, not yet at the engine; or the placeholder
        assert.equal(voiceBarShown([{ state: 'waiting' }, { state: 'ready' }, { state: 'failed' }]), false);
        assert.equal(voiceBarShown([{ state: 'ready', singing: true }]), false);
        assert.equal(voiceBarShown([]), false);
    });

    it('draws a voice\'s own bar only while the engine is singing it', () => {
        const parts = [
            { part: 'm', state: 'ready' as const, estimateMs: 1000 },
            { part: 'm#2', state: 'preparing' as const, singing: true, estimateMs: 1000 },
            { part: 'alt', state: 'preparing' as const, singing: true, estimateMs: 1000 },
            { part: 'ten', state: 'preparing' as const, estimateMs: 1000 },
        ];
        assert.equal(voiceRowProgress(parts, 'm#2', 500), 0.5, 'the one at the engine, against its estimate');
        assert.equal(voiceRowProgress(parts, 'alt', 500), 0, 'its turn is to come');
        assert.equal(voiceRowProgress(parts, 'm', 500), null, 'done');
        assert.equal(voiceRowProgress(parts, 'ten', 500), null, 'not at the engine yet');
        assert.equal(voiceRowProgress(parts, 'nobody', 500), null);
        assert.equal(voiceRowProgress(parts, 'm#2', 60000), 0.95, 'only the engine\'s answer finishes it');
    });

    it('counts the voices ready whole and the one being sung against its estimate', () => {
        const parts = [
            { state: 'ready' as const, estimateMs: 2000 },
            { state: 'preparing' as const, estimateMs: 1000 },
            { state: 'preparing' as const, estimateMs: 1000 },
        ];
        assert.equal(voiceProgress(parts, 0), 0.5);
        assert.equal(voiceProgress(parts, 500), 0.625);
    });

    it('does not finish on an estimate alone, only on the engine\'s answer', () => {
        const one = [{ state: 'preparing' as const, estimateMs: 1000 }];
        assert.equal(voiceProgress(one, 60000), 0.95);
        assert.equal(voiceProgress([{ state: 'ready' as const, estimateMs: 1000 }], 0), 1);
    });

    it('leaves a failed voice out, and a voice of unknown length has a weight', () => {
        assert.equal(voiceProgress([{ state: 'failed' as const, estimateMs: 1000 }], 500), 0);
        assert.equal(voiceProgress([{ state: 'failed' as const, estimateMs: 1000 }, { state: 'ready' as const }], 0), 1);
        assert.equal(voiceProgress([{ state: 'preparing' as const }], 1500), 0.5);
        assert.equal(voiceProgress([], 100), 0);
    });

    it('estimates from what is sung and learns the engine\'s speed', () => {
        assert.equal(sungSeconds({ Notes: [{ T: 0, D: 1, P: 60 }, { T: 3, D: 2, P: 60 }] }, 2), 2.5);
        assert.equal(voiceRenderEstimateMs(10, 100), 2500);
        assert.equal(learnRenderRate(100, 11500, 100), 100);
        assert.equal(learnRenderRate(100, 21500, 100), 150);
        assert.equal(learnRenderRate(100, 21500, 0), 100);
    });
});

describe('the voice sample queue', () => {
    function queue(have: string[] = []) {
        const sung: string[] = [];
        const gates: (() => void)[] = [];
        const q = new VoiceSampleQueue(id => have.includes(id), id => new Promise<void>((resolve, reject) => {
            sung.push(id);
            gates.push(() => { if (id === 'bad') { reject(new Error('no')); } else { have.push(id); resolve(); } });
        }));
        const open = async () => { gates.shift()?.(); await new Promise(r => setImmediate(r)); };
        return { q, sung, open };
    }

    it('sings one at a time, and an asked voice before the warmed ones', async () => {
        const { q, sung, open } = queue();
        q.warm(['a', 'b', 'c']);
        assert.deepEqual(sung, ['a']);
        const asked = q.ask('c');
        await open();
        assert.deepEqual(sung, ['a', 'c']);
        await open();
        assert.equal(await asked, true);
        assert.deepEqual(sung, ['a', 'c', 'b']);
        await open();
    });

    it('answers at once for a sample it has, and sings nothing for it', async () => {
        const { q, sung } = queue(['a']);
        assert.equal(await q.ask('a'), true);
        q.warm(['a']);
        assert.deepEqual(sung, []);
    });

    it('joins the voice being sung', async () => {
        const { q, sung, open } = queue();
        const first = q.ask('a');
        const second = q.ask('a');
        await open();
        assert.deepEqual(await Promise.all([first, second]), [true, true]);
        assert.deepEqual(sung, ['a']);
    });

    it('does not warm a failed voice again, but an ask tries it', async () => {
        const { q, sung, open } = queue();
        const first = q.ask('bad');
        await open();
        assert.equal(await first, false);
        q.warm(['bad']);
        assert.deepEqual(sung, ['bad']);
        const again = q.ask('bad');
        assert.deepEqual(sung, ['bad', 'bad']);
        await open();
        assert.equal(await again, false);
    });
});

describe('the prepare queue', () => {
    // Fake timers: `tick(ms)` fires what is due.
    const timers = () => {
        let now = 0, id = 0;
        const due = new Map<number, { at: number; fn: () => void }>();
        return {
            api: { setTimeout: (fn: () => void, ms: number) => { due.set(++id, { at: now + ms, fn }); return id; }, clearTimeout: (h: unknown) => { due.delete(h as number); } },
            tick(ms: number) { now += ms; for (const [k, d] of [...due]) { if (d.at <= now) { due.delete(k); d.fn(); } } },
        };
    };
    const flush = () => new Promise<void>(r => setTimeout(r, 0));

    it('runs the newest job once the text has rested, however many times it was scheduled', async () => {
        const t = timers();
        const ran: string[] = [];
        const q = new PrepareQueue<string>(async j => { ran.push(j); }, t.api);
        q.schedule('p1/melody', 'a', 1500);
        t.tick(1000);
        q.schedule('p1/melody', 'b', 1500);     // typing again: the clock restarts
        t.tick(1000);
        assert.deepEqual(ran, []);
        t.tick(600);
        await flush();
        assert.deepEqual(ran, ['b']);
    });

    it('tells of a newer job while one runs, and the newer follows as soon as it has ended', async () => {
        const t = timers();
        const ran: string[] = [];
        const told: [string, string][] = [];
        let release: () => void = () => undefined;
        const q = new PrepareQueue<string>(j => { ran.push(j); return new Promise<void>(r => { release = r; }); }, t.api, (running, newer) => told.push([running, newer]));
        q.schedule('s', 'a', 1500);
        assert.deepEqual(told, [], 'nothing runs yet');
        t.tick(1500);
        assert.equal(q.busy('s'), true);
        q.schedule('s', 'b', 1500);
        assert.deepEqual(told, [['a', 'b']], 'at the new text, not when its rest is over');
        q.schedule('s', 'c', 0);                // Play
        assert.deepEqual(told, [['a', 'b'], ['a', 'c']]);
        release();
        await flush(); await flush();
        assert.deepEqual(ran, ['a', 'c']);
        release();
        await flush(); await flush();
        assert.equal(q.busy('s'), false);
        q.schedule('s', 'd', 1500);
        assert.equal(told.length, 2, 'nothing runs any more to let go of');
    });

    it('a slot lets go of its own running job, not another\'s', () => {
        const t = timers();
        const told: string[] = [];
        const q = new PrepareQueue<string>(() => new Promise<void>(() => undefined), t.api, running => told.push(running));
        q.schedule('p/sop', 'a', 0);
        q.schedule('p/alt', 'x', 0);
        assert.deepEqual(told, []);
        q.schedule('p/alt', 'y', 0);
        assert.deepEqual(told, ['x']);
    });

    it('the wire says which request to let go of', () => {
        assert.deepEqual(JSON.parse(serveCancelLine('k.1')), { command: 'cancel', id: 'k.1' });
    });

    it('runs at once with no delay, lets one job run at a time, and runs only the newest waiting one after it', async () => {
        const t = timers();
        const ran: string[] = [];
        let release: () => void = () => undefined;
        const q = new PrepareQueue<string>(j => { ran.push(j); return new Promise<void>(r => { release = r; }); }, t.api);
        q.schedule('s', 'a', 0);
        assert.deepEqual(ran, ['a']);
        q.schedule('s', 'b', 0);
        q.schedule('s', 'c', 0);
        assert.deepEqual(ran, ['a'], 'b and c wait while a runs');
        assert.equal(q.hasWaiting('s'), true);
        release();
        await flush(); await flush();
        assert.deepEqual(ran, ['a', 'c'], 'b was never sung');
        assert.equal(q.hasWaiting('s'), false);
        release();
        await flush();
    });

    it('forgets what waits for a prefix when cancelled; other slots keep theirs', async () => {
        const t = timers();
        const ran: string[] = [];
        const q = new PrepareQueue<string>(async j => { ran.push(j); }, t.api);
        q.schedule('p1/melody', 'a', 1500);
        q.schedule('p2/melody', 'b', 1500);
        q.cancel('p1/');
        t.tick(1500);
        await flush();
        assert.deepEqual(ran, ['b']);
    });

    it('forgets one slot exactly when a part is sung no more; a part whose name begins the same keeps its job', async () => {
        const t = timers();
        const ran: string[] = [];
        const q = new PrepareQueue<string>(async j => { ran.push(j); }, t.api);
        q.schedule('p1/melody', 'a', 1500);
        q.schedule('p1/melody2', 'b', 1500);
        q.cancelSlot('p1/melody');
        assert.equal(q.hasWaiting('p1/melody'), false);
        t.tick(1500);
        await flush();
        assert.deepEqual(ran, ['b']);
    });

    it('reads the prepare mode, afterEdit unless onPlay is said', () => {
        assert.equal(prepareModeOf('onPlay'), 'onPlay');
        assert.equal(prepareModeOf('afterEdit'), 'afterEdit');
        assert.equal(prepareModeOf(undefined), 'afterEdit');
        assert.equal(prepareModeOf('typo'), 'afterEdit');
        assert.equal(PREPARE_IDLE_MS, 1500);
    });
});

describe('the singer settings', () => {
    const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8'));
    const settings = Object.assign({}, ...manifest.contributes.configuration.map((c: { properties: object }) => c.properties));
    const commands = manifest.contributes.commands as { command: string; title: string }[];

    it('name a program only for developers, empty by default (the Cantoria extension), and nothing else', () => {
        assert.equal(settings['lilysharp.playback.singer'].type, 'string');
        assert.equal(settings['lilysharp.playback.singer'].default, '');
        assert.equal(settings['lilysharp.playback.singer'].scope, 'machine-overridable');
        assert.match(settings['lilysharp.playback.singer'].markdownDescription, /yotsuda\.cantoria/);
        // The voice doubles the part's instrument (owner's decision 2026-11-08): no mute setting, no voice setting.
        assert.equal(settings['lilysharp.playback.singerReplacesPart'], undefined);
        assert.equal(settings['lilysharp.playback.singerVoice'], undefined);
        // When an edited voice is prepared (owner's decision 2026-11-08): after the text rests, or at Play.
        assert.deepEqual(settings['lilysharp.playback.singerPrepare'].enum, ['afterEdit', 'onPlay']);
        assert.equal(settings['lilysharp.playback.singerPrepare'].default, 'afterEdit');
        assert.equal(commands.find(c => c.command === 'lilysharp.chooseSingingVoice'), undefined);
    });
});

describe('printedWordsNote', () => {
    it('says which part and how to have it sung', () => {
        const note = printedWordsNote('melody');
        assert.match(note, /melody/);
        assert.match(note, /singer/);
        assert.match(note, /singer sings WORDS on melody/);
    });
});

describe('noVoiceGuide', () => {
    it('with Cantoria, names the engines and offers its folders', () => {
        const guide = noVoiceGuide(true);
        assert.match(guide.label, /No singing voice is installed/);
        for (const engine of ['NEUTRINO', 'DiffSinger', 'VOICEVOX']) { assert.match(guide.documentation, new RegExp(engine)); assert.match(guide.message, new RegExp(engine)); }
        assert.deepEqual(guide.actions.flatMap(a => a.command ? [a.command] : []), ['cantoria.chooseNeutrinoFolder', 'cantoria.chooseVoicesFolder', 'cantoria.chooseVoicevoxFolder']);
    });
    it('without Cantoria, says the extension is the first step', () => {
        const guide = noVoiceGuide(false);
        assert.match(guide.label, /Cantoria is not installed/);
        assert.match(guide.detail, /Install Cantoria/);
    });
});

describe('noLanguageGuide', () => {
    const japanese = [{ id: 'neutrino/X', engine: 'neutrino', name: 'X', language: 'ja' }];
    const commands = (guide: ReturnType<typeof noLanguageGuide>) => guide.actions.flatMap(a => a.command ? [a.command] : []);
    it('says what the installed voices sing', () => {
        assert.match(noLanguageGuide('en', japanese).label, /No installed voice sings English/);
        assert.match(noLanguageGuide('en', japanese).documentation, /The installed voices sing Japanese\./);
    });
    it('for English, names the one engine that can and offers only its folder', () => {
        const guide = noLanguageGuide('en', japanese);
        assert.match(guide.message, /DiffSinger voice with a dsdict-en\.yaml/);
        assert.deepEqual(commands(guide), ['cantoria.chooseVoicesFolder']);
    });
    it('for Japanese, offers every engine', () => {
        const english = [{ id: 'diffsinger/Y', engine: 'diffsinger', name: 'Y', languages: ['en'] }];
        assert.equal(commands(noLanguageGuide('ja', english)).length, 3);
    });
    it('for Chinese and Korean, names that dictionary and offers the DiffSinger folder', () => {
        for (const language of ['zh', 'ko']) {
            const guide = noLanguageGuide(language, japanese);
            assert.match(guide.message, new RegExp(`dsdict-${language}\\.yaml`));
            assert.deepEqual(commands(guide), ['cantoria.chooseVoicesFolder']);
        }
    });
    it('for a language no engine sings, offers nothing and says so', () => {
        const guide = noLanguageGuide('fr', japanese);
        assert.match(guide.message, /do not sing it yet/);
        assert.deepEqual(guide.actions, []);
    });
    it('says when the voices do not say what they sing', () => {
        assert.match(noLanguageGuide('en', [{ id: 'a/b', engine: 'a', name: 'b' }]).documentation, /do not say what they sing/);
    });
});

describe('voiceMenuKey', () => {
    const v = (Singer: string | null, more: object = {}) => ({ Part: 't', Singer, Track: 'w', ...more });
    it('is the same for the same lines', () => assert.equal(voiceMenuKey([v('diffsinger/TIGER')]), voiceMenuKey([v('diffsinger/TIGER')])));
    it('changes with who sings', () => assert.notEqual(voiceMenuKey([v('diffsinger/TIGER')]), voiceMenuKey([v('voicevox/満別花丸-元気')])));
    it('changes with a line added', () => assert.notEqual(voiceMenuKey([v('a/b')]), voiceMenuKey([v('a/b'), v('a/c', { Part: 'b' })])));
    it('changes with what it sings', () => assert.notEqual(voiceMenuKey([v('a/b')]), voiceMenuKey([v('a/b', { Track: 'x' })])));
    it('changes with whether the score sings it', () => assert.notEqual(voiceMenuKey([v('a/b')]), voiceMenuKey([v('a/b', { InScore: false })])));
    it('a voice of one engine or another by the same name is the same line', () => assert.equal(voiceMenuKey([v('a/b')]), voiceMenuKey([v('c/b')])));
});

describe('voiceMenuLabels', () => {
    const v = (Part: string, Singer: string | null) => ({ Part, Singer });
    it('names the voice, without its engine', () => assert.deepEqual(voiceMenuLabels([v('melody', 'neutrino/MERROW')]), { melody: 'MERROW' }));
    it('gives each singer line of a part its own line, by its key', () => assert.deepEqual(
        voiceMenuLabels([{ Part: 'm', Key: 'm', Singer: 'neutrino/MERROW' }, { Part: 'm', Key: 'm#2', Singer: 'diffsinger/TIGER' }]), { m: 'MERROW', 'm#2': 'TIGER' }));
    it('takes the part as the key where the server gave none', () => assert.equal(sungKey({ Part: 'm' }), 'm'));
    it('tells two voices apart by their keys', () => assert.equal(sungKey({ Part: 'm', Key: 'm#2' }), 'm#2'));
    it('says the part beside a voice that sings several', () => assert.deepEqual(
        voiceMenuLabels([v('sop', 'neutrino/MERROW'), v('alt', 'neutrino/MERROW'), v('ten', 'male')]),
        { sop: 'MERROW (sop)', alt: 'MERROW (alt)', ten: 'male' }));
    it('is the part for its default voice', () => assert.deepEqual(voiceMenuLabels([v('melody', null)]), { melody: 'melody' }));
    it('says on hover what is sung', () => {
        assert.equal(voiceMenuTitle('MERROW', 'verse', 'melody'), 'MERROW sings verse on melody');
        assert.equal(voiceMenuTitle('MERROW', 'lyrics', 'melody'), 'MERROW sings melody');
    });
});

// Owner's request 2026-11-11: a voice taken from a top-level `singer`'s list goes on to `sings`
// and the list of the file's lyrics, without a key between.
describe('a voice taken at the top level goes on to sings', () => {
    const { atTopLevel, singerGoesOnToSings } = require('../src/singerCore') as {
        atTopLevel: (before: string) => boolean;
        singerGoesOnToSings: (before: string, line: string, after: string) => boolean;
    };
    it('knows the top level by its braces, comments and quoted text aside', () => {
        assert.equal(atTopLevel(''), true);
        assert.equal(atTopLevel('part lead { instrument soprano }\n'), true);
        assert.equal(atTopLevel('section A {\n  lead { c4 d |\n'), false);
        assert.equal(atTopLevel('section A {\n  lead { c4 d | }\n}\n'), true);
        assert.equal(atTopLevel('// a brace in a comment {\ntitle "a { in a title"\n'), true);
        assert.equal(atTopLevel('score {\n  staff lead\n'), false);
    });
    it('goes on from a singer line of the top level that says no sings yet', () => {
        const top = 'part lead { instrument soprano }\n';
        assert.equal(singerGoesOnToSings(top, 'singer ', ''), true);
        assert.equal(singerGoesOnToSings(top, 'singer female "', ''), true);
        assert.equal(singerGoesOnToSings(top, 'singer "neu"', ''), true, 'the caret in quotes with nothing after the closing one');
        assert.equal(singerGoesOnToSings(top, 'singer "neu"   // who', '   // who'), true, 'a comment after is not text');
        // The line is whole already: the name alone is changed, the caret in it as before.
        assert.equal(singerGoesOnToSings(top, 'singer "neu" sings words on lead', ' sings words on lead'), false);
        assert.equal(singerGoesOnToSings(top, 'singer "neu" on lead', ' on lead'), false, 'something follows the name');
        // In a score or a part the line is that block's.
        assert.equal(singerGoesOnToSings('score {\n  staff lead\n', '  singer "neu"', ''), false);
        assert.equal(singerGoesOnToSings(top, 'lyrics words singer ', ''), false, 'not a singer line');
        assert.equal(singerGoesOnToSings(top, 'singers ', ''), false);
    });
    it('writes the name closed and ` sings ` after it, asks the list again and hears the voice where the name stands', () => {
        const ext = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
        // The closing quote is part of what the item replaces, so ` sings ` lands past it.
        assert.match(ext, /close >= 0 \? close \+ \(goesOn \? 1 : 0\) : position\.character\)/);
        assert.match(ext, /new vscode\.SnippetString\(\)\.appendText\(`\$\{slot\.Quoted \? '' : '"'\}\$\{c\.label\}" sings `\)\.appendTabstop\(0\);/);
        assert.match(ext, /command: 'lilysharp\.singerTaken', title: '[^']+', arguments: \[c\.label, nameColumn\]/);
        assert.match(ext, /async function singerTaken\(label: unknown, nameColumn: unknown\): Promise<void> \{\s*void vscode\.commands\.executeCommand\('editor\.action\.triggerSuggest'\);\s*await hearSinger\(label, nameColumn\);/);
        assert.match(ext, /registerCommand\('lilysharp\.singerTaken', singerTaken\)/);
        // Off the top level, and on a line that has its `sings`, the item is what it was.
        assert.match(ext, /\} else \{\s*if \(!slot\.Quoted\) \{ item\.insertText = new vscode\.SnippetString\(`"\$\{c\.label\}\$0"`\); \}\s*item\.command = \{ command: 'lilysharp\.hearSinger'/);
        // What the list after `sings ` holds is the server's: the file's lyrics.
        const server = fs.readFileSync(path.join(__dirname, '..', '..', '..', '..', 'LilySharp.Lsp', 'LilySharpLanguageServer.Completion.cs'), 'utf8');
        assert.match(server, /CompletionContext\.AfterSingerSings => new CompletionList \{ Items = \[\.\. SingerWordsGoOnToOn\(\[\.\. GetDeclaredNameCompletions\(doc\.Text, "lyrics"/);
    });
});

// Owner's request 2026-11-11: the bar that shows a voice being prepared was counted on the whole
// score - and on the voices already there. It is counted on what has to be sung anew, so it begins
// empty and is full when the voice is ready.
describe('the voice bar counts what is sung anew', () => {
    // A voice the engine spoke of at `at`: `sung` of `toSing` seconds done, the first word at 1000.
    const told = (toSing: number, sung: number, at = 1000, msPerSecond = 500) => ({ toSing, sung, at, firstAt: 1000, msPerSecond });

    it('counts a voice by the engine\'s own count: empty before the first phrase, full after the last', () => {
        assert.equal(voiceToldFraction(told(6, 0), 1000), 0);
        assert.equal(voiceToldFraction(told(6, 6, 9000), 9000), 1);
        assert.ok(Math.abs(voiceToldFraction(told(6, 2, 2000), 2000) - 1 / 3) < 1e-9);
        // Nothing to sing anew (every phrase kept): full at once.
        assert.equal(voiceToldFraction(told(0, 0), 1000), 1);
    });

    it('creeps on between two phrases at the engine\'s pace, never past what is left', () => {
        // Before the first phrase is in, at the host's pace: 500 ms a second of music.
        assert.ok(Math.abs(voiceToldFraction(told(6, 0), 2000) - 2 / 6) < 1e-9);
        // Once a phrase is in, at the pace measured: 2 s of music in 4000 ms, so 1 s more 2000 ms on.
        assert.ok(Math.abs(voiceToldFraction(told(6, 2, 5000), 7000) - 3 / 6) < 1e-9);
        // A long wait does not fill it: the engine's word does.
        const late = voiceToldFraction(told(6, 2, 5000), 500000);
        assert.ok(late < 1 && late > 0.9);
        assert.ok(voiceToldFraction(told(6, 0, 1000, 0), 999999) === 0, 'no pace known: it waits for the first phrase');
    });

    it('leaves out the voices that were ready before the bar came up', () => {
        const parts = [{ part: 'sop', state: 'ready' as const }, { part: 'alt', state: 'preparing' as const }, { part: 'ten', state: 'waiting' as const }];
        // The bar comes up: only what is being sung.
        assert.deepEqual(voiceRound([], parts, false), ['alt']);
        // While it shows: what becomes ready stays in (done), what begins joins, what the score sings no more goes.
        const later = [{ part: 'sop', state: 'ready' as const }, { part: 'alt', state: 'ready' as const }, { part: 'ten', state: 'preparing' as const }];
        assert.deepEqual(voiceRound(['alt'], later, true), ['alt', 'ten']);
        assert.deepEqual(voiceRound(['alt', 'gone'], later, true), ['alt', 'ten']);
    });

    it('begins at 0 and ends at 1 over the round, each voice weighed by its own work', () => {
        const bar = (parts: VoiceBarPart[], now: number) => voiceBar(parts, now, 0);
        // One voice with one phrase of ten to sing again: the whole bar is that phrase.
        const one: VoiceBarPart[] = [{ part: 'alt', state: 'preparing', singing: true, estimateMs: 60000, told: told(2, 0) }];
        assert.equal(bar(one, 1000), 0);
        one[0].told = told(2, 2, 3000);
        assert.equal(bar(one, 3000), 1);
        // Two voices: 2 s and 6 s to sing at one pace - the first done is a quarter of the bar.
        const two: VoiceBarPart[] = [
            { part: 'alt', state: 'ready', told: told(2, 2, 2000) },
            { part: 'ten', state: 'preparing', singing: true, told: told(6, 0, 2000) },
        ];
        assert.ok(Math.abs(bar(two, 2000) - 0.25) < 1e-9);
        // A voice that failed is not waited for.
        assert.equal(bar([{ part: 'alt', state: 'failed' }, { part: 'ten', state: 'ready' }], 0), 1);
        assert.equal(bar([], 0), 0);
    });

    it('falls back on the estimate for an engine that says nothing', () => {
        // An older Cantoria: the voice being sung by the time gone against its estimate, as before.
        const quiet: VoiceBarPart[] = [{ part: 'alt', state: 'preparing', singing: true, estimateMs: 4000 }];
        assert.equal(voiceBar(quiet, 0, 1000), 0.25);
        assert.equal(voiceBar(quiet, 0, 0), 0);
        assert.equal(voiceBar(quiet, 0, 999999), 0.95);
        // Two such: the engine sings one at a time, the second waits at 0.
        assert.equal(voiceBar([...quiet, { part: 'ten', state: 'preparing', singing: true, estimateMs: 4000 }], 0, 2000), 0.25);
    });

    it('draws each voice\'s own line by the same count', () => {
        const parts: VoiceBarPart[] = [
            { part: 'alt', state: 'preparing', singing: true, told: told(6, 3, 4000) },
            { part: 'ten', state: 'preparing', singing: true, estimateMs: 4000 },
            { part: 'sop', state: 'ready' },
        ];
        assert.equal(voiceRowProgress(parts, 'alt', 0, 4000), 0.5);
        assert.equal(voiceRowProgress(parts, 'ten', 1000, 4000), 0.25, 'the first voice the engine has not spoken of');
        assert.equal(voiceRowProgress(parts, 'sop', 0, 4000), null);
    });

    it('is told by the engine through the host, the count held by the render\'s key', () => {
        const ext = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
        // The host: every word of the engine goes to the page with the job's key and the pace learnt.
        assert.match(ext, /void panel\.webview\.postMessage\(\{ type: 'voiceProgress', part: id, key: job\.key, toSing: p\.toSing, sung: p\.sung, msPerSecond: voiceRenderRate \}\);/);
        assert.match(ext, /await engine\.render\(voice, speed, job\.letGo\.signal, onProgress\)\);/);
        // The pace is learnt on the seconds sung anew, and not at all from next to nothing.
        assert.match(ext, /const sungSecondsNow = told \? told\.toSing : v\.duration;\s*if \(!told \|\| told\.toSing >= 1\) \{/);
        // The page: the count survives a repeated "preparing" of the same render, and goes with another's.
        assert.match(ext, /const told = state === 'preparing' && before\.told && key && before\.told\.key === key \? before\.told : undefined;/);
        assert.match(ext, /case 'voiceProgress':\s*setVoiceTold\(message\);/);
        // …and the bar: the round's voices only, never drawn back.
        assert.match(ext, /voiceBarRound = window\.LysSynth\.voiceRound\(voiceBarRound, parts, true\);\s*voiceBarDrawn = Math\.max\(voiceBarDrawn, window\.LysSynth\.voiceBar\(parts\.filter\(p => voiceBarRound\.includes\(p\.part\)\), now, now - voiceBarFrom\)\);/);
        assert.match(ext, /voiceBarRound = window\.LysSynth\.voiceRound\(\[\], voiceBarParts\(\), false\);\s*voiceBarDrawn = 0;/);
        // The backends pass the engine's word on: the extension's API, and the program's lines.
        const singer = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'singer.ts'), 'utf8');
        assert.match(singer, /await this\.api\.sing\(score, \{ voice, signal, onProgress \}\);/);
        assert.match(singer, /if \(answer\.progress\) \{\s*this\.pending\.get\(answer\.progress\.id\)\?\.onProgress\?\.\(\{ toSing: answer\.progress\.toSing, sung: answer\.progress\.sung \}\);\s*return;/);
        const { parseServeLine } = require('../src/singerCore') as { parseServeLine: (line: string) => { id?: string; progress?: { id: string; toSing: number; sung: number } } | null };
        const line = parseServeLine('{"progress":{"id":"k.1","toSing":6,"sung":2}}');
        assert.deepEqual(line, { progress: { id: 'k.1', toSing: 6, sung: 2 } });
    });
});

// Owner's request 2026-11-12, a trial: `@vibrato` on a sung note, sung by a DiffSinger voice.
describe('a vibrato written on a note goes to the engine', () => {
    it('as its swing in cents and its swings a second, whatever the practice speed', () => {
        const voice: SungVoiceDto = { Part: 'lead', Language: 'ja', Notes: [
            { T: 0, D: 1, P: 60, L: 'ら', Vb: 40 },
            { T: 1, D: 1, P: 62, L: 'ら', Vb: 70, Vr: 6 },
            { T: 2, D: 1, P: 64, L: 'ら' },
        ] };
        const notes = cantoriaScore(voice, 1).notes;
        assert.deepEqual(notes.map(n => [n.vibrato, n.vibratoRate]), [[40, undefined], [70, 6], [undefined, undefined]]);
        // At half speed the notes are twice as long; the swings a second are the singer's own.
        assert.deepEqual(cantoriaScore(voice, 0.5).notes.map(n => [n.vibrato, n.vibratoRate]), [[40, undefined], [70, 6], [undefined, undefined]]);
        // It is in the voice's key: a note given a vibrato is sung anew, and one left plain is not.
        const plain = { ...voice, Notes: voice.Notes.map(n => ({ ...n, Vb: undefined, Vr: undefined })) };
        assert.notEqual(JSON.stringify(cantoriaScore(voice, 1)), JSON.stringify(cantoriaScore(plain, 1)));
        assert.ok(!JSON.stringify(cantoriaScore(plain, 1)).includes('vibrato'));
    });
});

// Owner's decisions 2026-11-12: how a voice sings is written on its singer line after `with`, a
// name and a value each, by the names the voice's own engine takes; the editor lists them.
describe('a singer line\'s settings', () => {
    const { singerSettingSlot, singParameterDetail } = require('../src/singerCore') as {
        singerSettingSlot: (before: string) => { kind: string; written?: string[]; of?: string } | undefined;
        singParameterDetail: (p: { name: string; scope: string; what: string; min?: number; max?: number; default?: number; unit?: string }) => string;
    };
    const line = 'singer female "diffsinger/Nishiren/2P" sings words on lead ';

    it('go to the engine with the score, in one order whatever the line\'s', () => {
        const voice: SungVoiceDto = { Part: 'lead', Language: 'ja', Notes: [{ T: 0, D: 1, P: 60, L: 'ら' }], Sing: { tension: -2, breathiness: 3, support: 'KIRITAN' } };
        const score = cantoriaScore(voice, 1);
        assert.deepEqual(score.sing, { breathiness: 3, support: 'KIRITAN', tension: -2 });
        assert.deepEqual(Object.keys(score.sing!), ['breathiness', 'support', 'tension']);
        // The same settings written the other way round are the same voice (its key is the score's text).
        const other = cantoriaScore({ ...voice, Sing: { support: 'KIRITAN', breathiness: 3, tension: -2 } }, 1);
        assert.equal(JSON.stringify(other), JSON.stringify(score));
        // A line that sets nothing says nothing: the voices sung before it could are still found.
        assert.ok(!('sing' in cantoriaScore({ ...voice, Sing: null }, 1)));
        assert.ok(!('sing' in cantoriaScore({ ...voice, Sing: {} }, 1)));
        assert.notEqual(JSON.stringify(cantoriaScore({ ...voice, Sing: null }, 1)), JSON.stringify(score));
    });

    it('are listed where a name is expected, and `with` where the line has none', () => {
        // After the words or the part and a space: the word itself.
        assert.deepEqual(singerSettingSlot(line), { kind: 'with' });
        assert.deepEqual(singerSettingSlot('singer "a/b" sings words '), { kind: 'with' });
        // Not where the line still needs its words or its part, nor while a word is being typed.
        assert.equal(singerSettingSlot('singer "a/b" sings '), undefined);
        assert.equal(singerSettingSlot('singer "a/b" sings words on '), undefined);
        assert.equal(singerSettingSlot('singer "a/b" sings words on lead'), undefined);
        assert.equal(singerSettingSlot('singer "a/b" '), undefined);
        // After `with`, and after each value: a name, what is written already left out of the list.
        assert.deepEqual(singerSettingSlot(line + 'with '), { kind: 'name', written: [] });
        assert.deepEqual(singerSettingSlot(line + 'with bre'), { kind: 'name', written: [] });
        assert.deepEqual(singerSettingSlot(line + 'with breathiness 3 '), { kind: 'name', written: ['breathiness'] });
        assert.deepEqual(singerSettingSlot(line + 'with breathiness 3 gender -0.2 te'), { kind: 'name', written: ['breathiness', 'gender'] });
        // After a name: its value, which is the writer's to type.
        assert.deepEqual(singerSettingSlot(line + 'with breathiness '), { kind: 'value', of: 'breathiness' });
        assert.deepEqual(singerSettingSlot(line + 'with breathiness 3 gender -0'), { kind: 'value', of: 'gender' });
        // A voice whose name holds the word, a track or a part called with, a comment, another line.
        assert.deepEqual(singerSettingSlot('singer "x/with y" sings words on lead '), { kind: 'with' });
        assert.equal(singerSettingSlot('singer "a/b" sings with on '), undefined);
        assert.equal(singerSettingSlot(line + 'with breathiness 3 // '), undefined);
        assert.equal(singerSettingSlot('lyrics words with '), undefined);
    });

    it('say what each does, its range, and where it can change', () => {
        assert.equal(singParameterDetail({ name: 'breathiness', scope: 'curve', what: 'How much breath is in the voice', min: -12, max: 12, default: 0, unit: 'dB' }),
            'How much breath is in the voice — -12 to 12 dB, 0 as it is');
        assert.equal(singParameterDetail({ name: 'transpose', scope: 'phrase', what: 'Sung as if the key were shifted', min: -12, max: 12 }),
            'Sung as if the key were shifted — -12 to 12 — one value a phrase: it changes where a phrase begins');
        assert.equal(singParameterDetail({ name: 'support', scope: 'voice', what: 'Another voice\'s model as support' }),
            'Another voice\'s model as support — one value for the voice');
        // A setting that takes a name says so, with the first of its names.
        const named = singParameterDetail({ name: 'support', scope: 'voice', what: 'Support', word: true, values: ['A', 'B', 'C', 'D', 'E', 'F', 'G'] } as never);
        assert.equal(named, 'Support — a name: A, B, C, D, E, F, … — one value for the voice');
        const ext = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
        assert.match(ext, /const names = taken\.find\(p => p\.name === slot\.of\)\?\.values \?\? \[\];/);
    });

    it('are the voice\'s own to list, and what it does not take is said as a warning', () => {
        const ext = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
        assert.match(ext, /registerCompletionItemProvider\(\{ language: 'lilysharp' \}, singerSettingCompletion, ' '\)/);
        assert.match(ext, /taken = await backend\.parameters\(await backend\.voiceName\(named\?\.\[2\] \|\| named\?\.\[1\] \|\| null\)\);/);
        assert.match(ext, /return taken\.filter\(p => !slot\.written\.includes\(p\.name\)\)\.map\(/);
        assert.match(ext, /for \(const note of v\.notes \?\? \[\]\) \{ tellSingerSetting\(note\); \}/);
        assert.match(ext, /void vscode\.window\.showWarningMessage\(`Lily#: \$\{text\}`\);/);
        const singer = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'singer.ts'), 'utf8');
        assert.match(singer, /return this\.api\.parameters \? await this\.api\.parameters\(voice\) : \[\];/);
    });
});

// Owner's decisions 2026-11-12: inside the music a setting is written on a note with `@sing(…)`,
// held from the note as a dynamic is, given back with `default`, arrived at with `to`.
describe('a note\'s @sing', () => {
    const { singMarkSlot, singersWritten } = require('../src/singerCore') as {
        singMarkSlot: (before: string) => { kind: string; written?: string[]; of?: string } | undefined;
        singersWritten: (text: string) => string[];
    };

    it('goes to the engine as what is held from the note and what is arrived at', () => {
        const voice: SungVoiceDto = { Part: 'lead', Language: 'ja', Notes: [
            { T: 0, D: 1, P: 60, L: 'ら', Sg: { tension: -2, breathiness: 6.5 } },
            { T: 1, D: 1, P: 62, L: 'ら', St: { breathiness: 9 }, Sg: { 'mix.Sweet': 0.3 } },
            { T: 2, D: 1, P: 64, L: 'ら', Sg: { breathiness: 'default' } },
            { T: 3, D: 1, P: 65, L: 'ら' },
        ] };
        const notes = cantoriaScore(voice, 1).notes;
        assert.deepEqual(notes[0].sing, { breathiness: 6.5, tension: -2 });
        assert.deepEqual(Object.keys(notes[0].sing!), ['breathiness', 'tension']);
        assert.deepEqual([notes[1].sing, notes[1].singTo], [{ 'mix.Sweet': 0.3 }, { breathiness: 9 }]);
        assert.deepEqual(notes[2].sing, { breathiness: 'default' });
        assert.ok(!('sing' in notes[3]) && !('singTo' in notes[3]));
        // A note that writes nothing is the note it was: the voices sung before are still found.
        const plain = { ...voice, Notes: voice.Notes.map(n => ({ T: n.T, D: n.D, P: n.P, L: n.L })) };
        assert.ok(!JSON.stringify(cantoriaScore(plain, 1)).includes('sing'));
    });

    it('lists a name where one is expected, and default and to where a value is', () => {
        assert.deepEqual(singMarkSlot('  c4@sing('), { kind: 'name', written: [] });
        assert.deepEqual(singMarkSlot('  c4@sing(bre'), { kind: 'name', written: [] });
        assert.deepEqual(singMarkSlot('  c4@sing(breathiness '), { kind: 'value', of: 'breathiness' });
        assert.deepEqual(singMarkSlot('  c4@sing(breathiness 6'), { kind: 'value', of: 'breathiness' });
        assert.deepEqual(singMarkSlot('  c4@sing(breathiness 6 '), { kind: 'name', written: ['breathiness'] });
        // `to` belongs to the value: after it the value is still to come.
        assert.deepEqual(singMarkSlot('  c4@sing(breathiness to '), { kind: 'value', of: 'breathiness' });
        assert.deepEqual(singMarkSlot('  c4@sing(breathiness to 9 ten'), { kind: 'name', written: ['breathiness'] });
        assert.deepEqual(singMarkSlot('  c4@sing(breathiness default tension -2 '), { kind: 'name', written: ['breathiness', 'tension'] });
        // Closed, another annotation's parenthesis, a comment: not one.
        assert.equal(singMarkSlot('  c4@sing(breathiness 6) d'), undefined);
        assert.equal(singMarkSlot('  c4@finger('), undefined);
        assert.equal(singMarkSlot('  // c4@sing('), undefined);
    });

    it('lists what the voices of the file\'s singer lines take', () => {
        const file = 'part lead { }\nsinger female "diffsinger/Nishiren/2P" sings words on lead\n// singer "x/y" sings z\nsinger male sings other on bass\nsinger "diffsinger/Nishiren/2P" sings third\nsinger sings fourth\n';
        assert.deepEqual(singersWritten(file), ['diffsinger/Nishiren/2P', 'male', '']);
        assert.deepEqual(singersWritten('part lead { }\n'), []);
        const ext = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
        assert.match(ext, /registerCompletionItemProvider\(\{ language: 'lilysharp' \}, singMarkCompletion, '\(', ' '\)/);
        // Inside the music what can change there: all but the settings that are one value for the voice.
        assert.match(ext, /if \(p\.scope !== 'voice' && !taken\.has\(p\.name\)\) \{ taken\.set\(p\.name, p\); \}/);
    });
});

// Owner's decisions 2026-11-12: a setting the voice does not take is the score's mistake, and is
// shown where it is written.
describe('a voice\'s settings are checked where they are written', () => {
    const { singProblems } = require('../src/singerCore') as {
        singProblems: (settings: object[], takenBy: Map<string, object[]>) => { start: number; end: number; message: string }[];
    };
    const diff = [
        { name: 'breathiness', scope: 'curve', what: '', min: -12, max: 12, default: 0, unit: 'dB' },
        { name: 'gender', scope: 'curve', what: '', min: -1, max: 1, default: 0 },
    ];
    const neutrino = [
        { name: 'transpose', scope: 'phrase', what: '', min: -12, max: 12, default: 0 },
        { name: 'support', scope: 'voice', what: '', word: true, values: ['KIRITAN', 'SOMA'] },
    ];
    const at = (name: string, over: object = {}) => ({ Name: name, Start: 10, End: 10 + name.length, ValueStart: 30, ValueEnd: 33, ...over });

    it('on a singer line, against that line\'s own voice', () => {
        const takenBy = new Map<string, object[]>([['diffsinger/X', diff], ['neutrino/M', neutrino]]);
        // Taken, in range: nothing to say.
        assert.deepEqual(singProblems([at('breathiness', { Number: 3, Singer: 'diffsinger/X' }), at('support', { Word: 'kiritan', Singer: 'neutrino/M' })], takenBy), []);
        // Another voice's setting is not this one's.
        const other = singProblems([at('transpose', { Number: 2, Singer: 'diffsinger/X' })], takenBy);
        assert.deepEqual([other[0].start, other[0].end], [10, 19]);
        assert.equal(other[0].message, '"diffsinger/X" does not take the setting "transpose" (it takes breathiness, gender): it is not sung');
        // Out of range, on the value; a word for a number; a number for a name; a name that is none of them.
        const wide = singProblems([at('breathiness', { Number: 99, Singer: 'diffsinger/X' })], takenBy);
        assert.deepEqual([wide[0].start, wide[0].end], [30, 33]);
        assert.equal(wide[0].message, '99 is outside what "breathiness" takes, -12 to 12 dB: it is sung as 12');
        assert.match(singProblems([at('gender', { Word: 'low', Singer: 'diffsinger/X' })], takenBy)[0].message, /"gender" takes a number, -1 to 1, not "low"/);
        assert.match(singProblems([at('support', { Number: 2, Singer: 'neutrino/M' })], takenBy)[0].message, /"support" takes a name, not a number/);
        assert.match(singProblems([at('support', { Word: 'NOBODY', Singer: 'neutrino/M' })], takenBy)[0].message, /"NOBODY" is none of the names "support" takes: KIRITAN, SOMA/);
        // A voice with no setting at all, and the default voice.
        assert.match(singProblems([at('breathiness', { Number: 3, Singer: 'voicevox/Z' })], new Map([['voicevox/Z', []]]))[0].message, /"voicevox\/Z" does not take the setting "breathiness" \(it takes no setting\)/);
        assert.match(singProblems([at('zzz', { Number: 3 })], new Map([['', diff]]))[0].message, /^the default voice does not take the setting "zzz"/);
    });

    it('on a note, against any voice of the file, and against where a setting can change', () => {
        const takenBy = new Map<string, object[]>([['diffsinger/X', diff], ['neutrino/M', neutrino]]);
        assert.deepEqual(singProblems([at('breathiness', { Number: 3, OnNote: true }), at('transpose', { Number: 2, OnNote: true }), at('gender', { Word: 'default', OnNote: true })], takenBy), []);
        assert.match(singProblems([at('zzz', { Number: 1, OnNote: true })], takenBy)[0].message, /^no voice of this file takes the setting "zzz" \(they take breathiness, gender, transpose, support\)/);
        assert.match(singProblems([at('zzz', { Number: 1, OnNote: true })], new Map([['diffsinger/X', diff]]))[0].message, /^this file's voice does not take the setting "zzz" \(it takes breathiness, gender\)/);
        // One value for the voice belongs on the singer line; one value a phrase cannot glide.
        assert.match(singProblems([at('support', { Word: 'SOMA', OnNote: true })], takenBy)[0].message, /"support" is one value for the voice: write it on the singer line/);
        const glide = singProblems([at('transpose', { Number: 2, Arrives: true, OnNote: true })], takenBy);
        assert.match(glide[0].message, /"transpose" is one value a phrase and cannot glide/);
        assert.deepEqual([glide[0].start, glide[0].end], [30, 33]);
        assert.deepEqual(singProblems([at('breathiness', { Number: 3, Arrives: true, OnNote: true })], takenBy), []);
    });

    it('is asked a moment after the text rests, of a Cantoria that says what a voice takes', () => {
        const ext = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
        assert.match(ext, /client\.sendRequest\('lilysharp\/singSettings', \{ textDocument: \{ uri: document\.uri\.toString\(\) \} \}\);/);
        // A document that sets nothing asks no engine; an older Cantoria's silence is not "takes nothing".
        assert.match(ext, /if \(!written\.Settings \|\| written\.Settings\.length === 0\) \{ singDiagnostics\.delete\(document\.uri\); return; \}\s*const engine = await renderer\(\);/);
        assert.match(ext, /engine\.backend\.listsParameters \? engine\.backend : undefined;/);
        assert.match(ext, /vscode\.workspace\.onDidChangeTextDocument\(e => scheduleSingProblems\(e\.document\)\)/);
        const singer = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'singer.ts'), 'utf8');
        assert.match(singer, /get listsParameters\(\): boolean \{ return typeof this\.api\.parameters === 'function'; \}/);
    });
});

// Owner's request 2026-11-12: what a setting does (transpose…) is heard as a note or a voice is
// heard - the voice says its phrase as the line has it, when a value is stepped and when the
// caret comes to the line's settings.
describe('a singer line is heard as it says the voice sings', () => {
    const core = require('../src/singerCore') as {
        singerLineOf: (line: string) => { voice: string; askColumn: number; sing: Record<string, number | string>; withAt: number } | undefined;
        voiceSampleKey: (id: string, octave: string, language?: string, sing?: Record<string, number | string>) => string;
        parseVoiceSampleKey: (key: string) => { id: string; language?: string; octave: string; sing?: Record<string, number | string> };
        voiceSampleFileName: (key: string) => string;
    };

    it('reads the line: its voice, its settings, and where they begin', () => {
        const line = 'singer female "neutrino/MERROW" sings words on lead with transpose 3 support KIRITAN gender -0.2 note "a b"';
        const read = core.singerLineOf(line)!;
        assert.equal(read.voice, 'neutrino/MERROW');
        assert.deepEqual(read.sing, { transpose: 3, support: 'KIRITAN', gender: -0.2, note: 'a b' });
        assert.equal(line.slice(read.withAt, read.withAt + 4), 'with');
        assert.equal(line.slice(read.askColumn, read.askColumn + 8), 'neutrino');
        // A gender alone, no voice at all, no settings; a track or a part called with.
        assert.deepEqual(core.singerLineOf('singer male sings words with transpose 2'), { voice: 'male', askColumn: 11, sing: { transpose: 2 }, withAt: 24 });
        assert.equal(core.singerLineOf('singer sings words on lead')!.voice, '');
        assert.equal(core.singerLineOf('singer "a/b" sings words on lead')!.withAt, -1);
        assert.deepEqual(core.singerLineOf('singer "a/b" sings with on with')!.sing, {});
        assert.deepEqual(core.singerLineOf('singer "a/b" sings with on with with x 1')!.sing, { x: 1 });
        // A name that holds the word; a name written twice keeps its last; a comment; another line.
        assert.deepEqual(core.singerLineOf('singer "x/with y" sings words with a 1 a 2')!.sing, { a: 2 });
        assert.deepEqual(core.singerLineOf('singer "a/b" sings words // with a 1')!.sing, {});
        assert.equal(core.singerLineOf('lyrics words { la }'), undefined);
        assert.equal(core.singerLineOf('singer "a/b"'), undefined);
    });

    it('keeps a sample by the voice AND the settings it was sung with', () => {
        const plain = core.voiceSampleKey('neutrino/MERROW', 'high');
        const set = core.voiceSampleKey('neutrino/MERROW', 'high', undefined, { transpose: 3, support: 'KIRITAN' });
        assert.equal(plain, 'neutrino/MERROW~high');
        assert.equal(set, 'neutrino/MERROW~high!support=KIRITAN;transpose=3');
        // The same settings in another order are the same sample; another value another.
        assert.equal(core.voiceSampleKey('neutrino/MERROW', 'high', undefined, { support: 'KIRITAN', transpose: 3 }), set);
        assert.notEqual(core.voiceSampleKey('neutrino/MERROW', 'high', undefined, { transpose: 4, support: 'KIRITAN' }), set);
        assert.equal(core.voiceSampleKey('neutrino/MERROW', 'high', undefined, {}), plain);
        assert.deepEqual(core.parseVoiceSampleKey(set), { id: 'neutrino/MERROW', language: undefined, octave: 'high', sing: { support: 'KIRITAN', transpose: 3 } });
        assert.deepEqual(core.parseVoiceSampleKey(core.voiceSampleKey('diffsinger/X/Y', 'low', 'en', { gender: -0.2 })),
            { id: 'diffsinger/X/Y', language: 'en', octave: 'low', sing: { gender: -0.2 } });
        assert.equal(core.parseVoiceSampleKey(plain).sing, undefined);
        // Two values are two files.
        assert.notEqual(core.voiceSampleFileName(set), core.voiceSampleFileName(core.voiceSampleKey('neutrino/MERROW', 'high', undefined, { transpose: -3, support: 'KIRITAN' })));
    });

    it('sings only the last of the values a step runs through', async () => {
        const sung: string[] = [];
        let release: (() => void) | undefined;
        const queue = new VoiceSampleQueue(() => false, id => new Promise<void>(resolve => { sung.push(id); release = resolve; }));
        const same = (key: string) => (other: string) => other.includes('!') && other.slice(0, other.indexOf('!')) === key.slice(0, key.indexOf('!'));
        const first = queue.ask('v~high!transpose=1', same('v~high!transpose=1'));      // being sung
        const second = queue.ask('v~high!transpose=2', same('v~high!transpose=2'));     // waiting
        const other = queue.ask('w~high');                                               // another voice's: not of these
        const third = queue.ask('v~high!transpose=3', same('v~high!transpose=3'));      // takes the second's place
        assert.equal(await second, false);
        release!();
        assert.equal(await first, true);
        await new Promise<void>(resolve => setImmediate(resolve));
        release!();
        assert.equal(await third, true);
        await new Promise<void>(resolve => setImmediate(resolve));
        release!();
        assert.equal(await other, true);
        assert.deepEqual(sung, ['v~high!transpose=1', 'v~high!transpose=3', 'w~high']);
    });

    it('is asked when a value is stepped and when the caret comes to the settings', () => {
        const ext = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
        const step = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'stepAudition.ts'), 'utf8');
        // The phrase is sung with the line's settings, and kept by them.
        assert.match(ext, /if \(sing && Object\.keys\(sing\)\.length > 0\) \{ score\.sing = sing; \}/);
        assert.match(ext, /const key = voiceSampleKey\(id, sampleOctave\(voice\?\.gender, lineGender\), language, sing\);/);
        assert.match(ext, /line && Object\.keys\(line\.sing\)\.length > 0 \? line\.sing : undefined\);/);
        // With the caret in the settings the words' language is asked at the voice's name.
        assert.match(ext, /typeof nameColumn === 'number' \? nameColumn : inSettings \? line\.askColumn : caret\.character\)\);/);
        // Samples sung with settings are in a folder of their own, which keeps the newest few.
        assert.match(ext, /\? path\.join\(voiceSamplesDir \?\? '', 'tried', voiceSampleFileName\(id\)\)/);
        assert.match(ext, /if \(key\.includes\('!'\)\) \{ trimTriedSamples\(\); \}/);
        // A step on a setting, and the caret coming to the settings - not over a Play.
        assert.match(step, /if \(line && line\.withAt >= 0 && caret\.character >= line\.withAt && deps\.getPreview\(uri\) && !deps\.isPlaying\(uri\)\) \{[\s\S]{0,160}void deps\.hearSinger\(line\.voice\);/);
        assert.match(step, /if \(line && line\.withAt >= 0 && one\.start\.character >= line\.withAt\) \{\s*written = line\.voice;/);
    });
});