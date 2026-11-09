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
import { CANTILENA_EXTENSION_ID, PREPARE_IDLE_MS, PrepareQueue, StageClock, SungNoteDto, SungVoiceDto, VoiceSampleQueue, cantilenaApiOf, cantilenaScore, fitsGender, learnRenderRate, parseServeLine, prepareModeOf, serveRequestLine, afterSingerWord, singerChoices, singerValueAt, singerWrittenAt, singsLanguage, stepVoice, sungSeconds, voiceKey, voiceRenderEstimateMs, voiceSampleFileName, voiceSampleKey, voiceSampleScore } from '../src/singerCore';
import { voiceBarShown, voiceProgress, voiceStillSinging } from '../src/webview/playbackCore';

// The singing voice's pure half (docs/singing-design.md §5): the server's voice becomes the
// engine's score, the key names the render, the engine's lines are read, and the Cantilena
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
        const score = cantilenaScore(voice, 1);
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
        const score = cantilenaScore(voice, 0.5);
        assert.deepEqual(score.notes.map(n => [n.start, n.duration]), [[0.3, 0.75], [1.05, 0.25], [1.3, 0.75], [2.05, 0.25]]);
        assert.deepEqual(cantilenaScore(voice, 0).notes[1].start, 0.675, 'a speed of 0 is read as 1');
    });

    it('is keyed by the voice and the score, so the speed and the voice each sing anew', () => {
        const a = voiceKey(cantilenaScore(voice, 1), 'MERROW');
        assert.match(a, /^[0-9a-f]{40}$/);
        assert.equal(voiceKey(cantilenaScore(voice, 1), 'MERROW'), a);
        assert.notEqual(voiceKey(cantilenaScore(voice, 0.8), 'MERROW'), a);
        assert.notEqual(voiceKey(cantilenaScore(voice, 1), 'OTHER'), a);
    });
});

describe('the engine\'s wire, and the extension\'s API', () => {
    it('reads the engine\'s answers and leaves its chatter alone', () => {
        assert.deepEqual(parseServeLine('{"ready":true,"backend":"neutrino","voices":["MERROW"]}'), { ready: true, backend: 'neutrino', voices: ['MERROW'] });
        assert.deepEqual(parseServeLine('{"id":"k1","wav":"C:\\\\x.wav","duration":8,"sampleRate":48000,"seconds":0.4}'),
            { id: 'k1', wav: 'C:\\x.wav', duration: 8, sampleRate: 48000, seconds: 0.4 });
        assert.deepEqual(parseServeLine('{"id":"k2","error":"note 0 at 0 s: no lyric"}'), { id: 'k2', error: 'note 0 at 0 s: no lyric' });
        assert.equal(parseServeLine('cantilena: serving neutrino'), null);
        assert.equal(parseServeLine(''), null);
        assert.equal(parseServeLine('{not json'), null);
    });

    it('writes one request a line, the WAV named by the key, the voice the engine\'s own', () => {
        const line = serveRequestLine('abc', { sampleRate: 48000, language: 'ja', notes: [] }, 'C:\\tmp\\abc.wav');
        assert.ok(line.endsWith('\n'));
        assert.deepEqual(JSON.parse(line), { id: 'abc', score: { sampleRate: 48000, language: 'ja', notes: [] }, output: 'C:\\tmp\\abc.wav' });
    });

    it('takes the Cantilena extension\'s exports when they are the first API, and nothing else', () => {
        assert.equal(CANTILENA_EXTENSION_ID, 'yotsuda.cantilena');
        const api = { version: '0.1.0', apiVersion: 1, status: async () => ({ ready: true, voice: 'MERROW', problems: [] }), sing: async () => ({ wav: 'x', duration: 1 }), release: () => undefined };
        assert.equal(cantilenaApiOf(api), api);
        assert.equal(cantilenaApiOf({ ...api, apiVersion: 2 }), undefined);
        assert.equal(cantilenaApiOf({ path: 'omr.exe' }), undefined);
        assert.equal(cantilenaApiOf(undefined), undefined);
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

    it('opens the list on the space after singer', () => {
        assert.equal(afterSingerWord('lyrics verse sings melody singer '), true);
        assert.equal(afterSingerWord('  singer male '), true);
        assert.equal(afterSingerWord('lyrics verse sings melody singer'), false, 'the word is still being typed');
        assert.equal(afterSingerWord('lyrics verse sings melody '), false);
        assert.equal(afterSingerWord('misinger '), false);
        assert.equal(afterSingerWord('// singer '), false);
        assert.equal(afterSingerWord('singer "a/b",'), true, 'the next voice');
        assert.equal(afterSingerWord('singer "a/b", '), true);
        assert.equal(afterSingerWord('singer "a/b","c/d",'), true);
        assert.equal(afterSingerWord('singer "a/b" '), false);
    });

    it('finds the one name the caret stands on, and hears them together', () => {
        const line = 'singer "a/b", "c/d"';
        assert.deepEqual(singerValueAt(line, 9), { start: 8, end: 11, value: 'a/b' });
        assert.deepEqual(singerValueAt(line, 17), { start: 15, end: 18, value: 'c/d' });
        assert.deepEqual(singerValueAt(line, 1), { start: 8, end: 11, value: 'a/b' }, 'on the word');
        assert.deepEqual(singerValueAt(line, 12), { start: 8, end: 11, value: 'a/b' }, 'on the comma');
        assert.equal(singerWrittenAt(line, 0, 6), 'a/b+c/d');
        assert.equal(singerWrittenAt('singer male "a/b","c/d"', 0, 6), 'a/b+c/d');
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
    });

    it('names the voice on the engine\'s wire when one is asked for', () => {
        assert.equal(JSON.parse(serveRequestLine('k', { sampleRate: 1, language: 'ja', notes: [] }, 'x.wav', 'diffsinger/TIGER')).voice, 'diffsinger/TIGER');
    });
});

// The manifest's side: the two settings, and no voice setting or picker of Lily#'s own (the
// voice is the Cantilena extension's) — as the MIDI output's (midiOutputSetting.test.ts).
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
        assert.equal(voiceSampleKey('d/T', 'ja'), 'd/T', 'the samples already sung keep their files');
        assert.equal(voiceSampleKey('d/T', 'en'), 'd/T@en');
        assert.equal(voiceSampleFileName('neutrino/MERROW'), 'neutrino_MERROW-2.wav', 'the samples already sung keep their files');
        assert.notEqual(voiceSampleFileName('voicevox/ずんだもん-ノーマル'), voiceSampleFileName('voicevox/四国めたん-ノーマル'));
        assert.equal(voiceSampleKey('d/T'), 'd/T');
        assert.deepEqual(voiceSampleScore('en')?.notes.map(n => [n.lyric, !!n.hyphen]), [['hel', true], ['lo', false]]);
        assert.deepEqual(voiceSampleScore('en', 'male')?.notes.map(n => n.pitch), [48, 52]);
    });

    it('sings the phrase an octave lower for a male voice only', () => {
        const pitches = (gender?: string) => voiceSampleScore('ja', gender)?.notes.map(n => n.pitch);
        assert.deepEqual(pitches('male'), pitches()?.map(p => p - 12));
        assert.deepEqual(pitches('female'), pitches());
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

    it('name a program only for developers, empty by default (the Cantilena extension), and nothing else', () => {
        assert.equal(settings['lilysharp.playback.singer'].type, 'string');
        assert.equal(settings['lilysharp.playback.singer'].default, '');
        assert.equal(settings['lilysharp.playback.singer'].scope, 'machine-overridable');
        assert.match(settings['lilysharp.playback.singer'].markdownDescription, /yotsuda\.cantilena/);
        // The voice doubles the part's instrument (owner's decision 2026-11-08): no mute setting, no voice setting.
        assert.equal(settings['lilysharp.playback.singerReplacesPart'], undefined);
        assert.equal(settings['lilysharp.playback.singerVoice'], undefined);
        // When an edited voice is prepared (owner's decision 2026-11-08): after the text rests, or at Play.
        assert.deepEqual(settings['lilysharp.playback.singerPrepare'].enum, ['afterEdit', 'onPlay']);
        assert.equal(settings['lilysharp.playback.singerPrepare'].default, 'afterEdit');
        assert.equal(commands.find(c => c.command === 'lilysharp.chooseSingingVoice'), undefined);
    });
});
