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
import { CANTILENA_EXTENSION_ID, PREPARE_IDLE_MS, PrepareQueue, SungNoteDto, SungVoiceDto, cantilenaApiOf, cantilenaScore, parseServeLine, prepareModeOf, serveRequestLine, voiceKey } from '../src/singerCore';

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

// The manifest's side: the two settings, and no voice setting or picker of Lily#'s own (the
// voice is the Cantilena extension's) — as the MIDI output's (midiOutputSetting.test.ts).
// When an edited voice goes to the engine (docs/singing-design.md §5): after the text rests, one
// job a slot, the newest winning.
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
