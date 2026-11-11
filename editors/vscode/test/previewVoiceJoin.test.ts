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
import * as vm from 'node:vm';
import { nextVoiceEntry, voiceEntryEarly } from '../src/webview/playbackCore';

// Owner's request 2026-11-11: a Play starts while a voice is still being sung, its part on the
// instrument that stands in; when the voice lands, it comes in after the part's next rest and the
// instrument gives way there. The preview's page script (extension.ts, a template literal).
describe('a voice that lands while the preview plays', () => {
    const extension = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
    const cut = (head: string) => {
        const at = extension.indexOf(head);
        assert.ok(at >= 0, head);
        let depth = 0;
        for (let i = extension.indexOf('{', at + head.length - 1); i < extension.length; i++) {
            if (extension[i] === '{') depth++;
            else if (extension[i] === '}' && --depth === 0) return extension.slice(at, i + 1);
        }
        throw new Error(head);
    };

    function page(over: Record<string, unknown> = {}) {
        const sources: { when: number; offset: number; level: number }[] = [];
        const silenced: { T: number; N: number }[][] = [];
        const flashed: string[] = [];
        let handedOut = 0.25;
        const audioCtx = {
            currentTime: 101,   // one second into a Play whose list's offset sounded at 100
            // The voice's own gain: silent at its start, at the fader's level 30 ms on.
            createGain: () => {
                const g = { level: -1, from: [0, 0], to: [0, 0], connect: () => { /* to the output */ }, gain: {
                    setValueAtTime(v: number, t: number) { g.from = [v, t]; },
                    linearRampToValueAtTime(v: number, t: number) { g.to = [v, t]; g.level = v; },
                } };
                return g;
            },
            createBufferSource: () => {
                const s = { when: 0, offset: 0, level: 1, buffer: null as unknown, connect(out: { level?: number }) { this.level = out.level ?? -1; }, start(w: number, o: number) { this.when = w; this.offset = o; } };
                sources.push(s);
                return s;
            },
        };
        // melody: sung only (track 0); alto: sung and played (track 1); bass: played (track 2).
        const voices = new Map<string, unknown>([['melody', { buffer: {}, speed: 1, lead: 0.3 }]]);
        const context = vm.createContext({
            Math, Set, Map, audioCtx, voices,
            window: { LysSynth: { nextVoiceEntry, voiceEntryEarly } },
            rawParts: ['melody', 'alto', 'bass'], rawSung: ['melody', 'alto'], rawPlayed: ['alto', 'bass'],
            practice: { speed: 1, voicesOff: [], voicesOn: [], instrumentsOn: [], voiceLevels: {} },
            voicePlan: { pieces: null, rounds: 1 }, menuVoiceKeys: [], menuVoiceNames: { melody: 'Melody' },
            voiceSources: [], loopList: null, midiPlaying: false, countInFix: 0, playOffset: 0,
            playClock: () => audioCtx.currentTime - 100,
            playingNotes: [
                { T: 0, D: 1, N: 0 }, { T: 0, D: 8, N: 2 }, { T: 3, D: 1, N: 0 }, { T: 3, D: 1, N: 1 }, { T: 6, D: 1, N: 0 },
            ],
            sfPlayer: { get handedOutUntil() { return handedOut; }, silence: (list: { T: number; N: number }[]) => { silenced.push(list); } },
            voiceOutput: () => ({ level: 1 }),
            levelIn: (levels: Record<string, number>, name: string) => levels[name] ?? 1,
            flashInfo: (text: string) => { flashed.push(text); },
            ...over,
        });
        new vm.Script([
            cut('function voiceKeysOf(part)'), cut('function soundingVoices()'), cut('function sungTracks()'),
            cut('function menuVoicesOf(part)'), cut('function instrumentSounds(i, sung)'), cut('function joinVoice(key, entries, sungBefore)'),
        ].join('\n')).runInContext(context);
        const join = (entries: { at: number; room: number }[], sungBefore: number[] = []) => {
            context.entries = entries;
            context.before = new Set(sungBefore);
            new vm.Script("joinVoice('melody', entries, before)").runInContext(context);
        };
        return { context, audioCtx, sources, silenced, flashed, join, setHandedOut: (v: number) => { handedOut = v; } };
    }
    const entries = [{ at: 3, room: 2 }, { at: 6, room: 2 }];

    it('comes in at the next sung note after a rest, and the stand-in instrument gives way there', () => {
        const { sources, silenced, flashed, join } = page();
        join(entries);
        // The note at 3 s sounds at 103; the voice starts its lead's 0.3 s before, at that place in the WAV.
        assert.equal(sources.length, 1);
        assert.ok(Math.abs(sources[0].when - 102.7) < 1e-9 && Math.abs(sources[0].offset - 3) < 1e-9);
        // The melody's instrument alone is taken back, from the entry on: the alto is played as
        // well as sung, the bass is no singer's.
        assert.deepEqual(silenced, [[{ T: 3, D: 1, N: 0 }, { T: 6, D: 1, N: 0 }]]);
        assert.deepEqual(flashed, ['The voice of Melody comes in']);
        assert.equal(sources[0].level, 1);
    });

    it('does not wait for a rest: the next note it sings, a consonant\'s worth before the beat', () => {
        // The owner's song, 2026-11-11: eighth notes joined for bars. Every sung note is an entry.
        const { sources, silenced, join } = page();
        join([{ at: 0, room: 0 }, { at: 1, room: 0 }, { at: 1.5, room: 0 }, { at: 2, room: 0 }, { at: 3, room: 0 }]);
        // One second in: the note at 1.5 s is the first there is time for (0.4 s of notice).
        assert.ok(Math.abs(sources[0].when - (101.5 - 0.08)) < 1e-9 && Math.abs(sources[0].offset - (0.3 + 1.5 - 0.08)) < 1e-9);
        assert.deepEqual(silenced[0].map(n => n.T), [3, 6]);
    });

    it('takes the entry after, when the next is too near or already with the synth', () => {
        const near = page();
        near.audioCtx.currentTime = 102.8;      // 0.2 s before the note at 3 s
        near.join(entries);
        assert.ok(Math.abs(near.sources[0].when - 105.7) < 1e-9 && Math.abs(near.sources[0].offset - 6) < 1e-9);
        assert.deepEqual(near.silenced[0].map(n => n.T), [6]);
        const held = page();
        held.setHandedOut(3.2);                 // a hidden page looks a second and a half ahead
        held.join(entries);
        assert.deepEqual(held.silenced[0].map(n => n.T), [6]);
    });

    it('reads the score\'s seconds at the voice\'s speed, after a count-in, from where the Play began', () => {
        // Half speed: the note at 3 s of the score is at 6 s of the list; a count-in of 2 s moves it to 8 s.
        const slow = page({ countInFix: 2, practice: { speed: 0.5, voicesOff: [], voicesOn: [], instrumentsOn: [], voiceLevels: { melody: 0.5 } } });
        (slow.context.voices as Map<string, unknown>).set('melody', { buffer: {}, speed: 0.5, lead: 0.3 });
        slow.context.playingNotes = [{ T: 2, D: 2, N: 0 }, { T: 8, D: 2, N: 0 }];
        slow.join(entries);
        assert.ok(Math.abs(slow.sources[0].when - (100 + 8 - 0.3)) < 1e-9);
        assert.ok(Math.abs(slow.sources[0].offset - (0.3 + 6 - 0.3)) < 1e-9);
        assert.deepEqual(slow.silenced[0].map(n => n.T), [8]);
        // The menu's fader for the voice is the level its gain fades in to.
        assert.equal(slow.sources[0].level, 0.5);
        // A Play from 2 s in: the list's 2 sounded at 100, so its 3 sounds at 101.
        const mid = page({ playOffset: 2 });
        mid.audioCtx.currentTime = 100.2;
        mid.join(entries);
        assert.ok(Math.abs(mid.sources[0].when - 100.7) < 1e-9 && Math.abs(mid.sources[0].offset - 3) < 1e-9);
    });

    it('leaves the instrument playing where the voice cannot be brought in', () => {
        const cases: [string, Record<string, unknown>][] = [
            ['nothing plays', { playClock: null }],
            ['the score was edited since the Play began', { playingNotes: null }],
            ['a loop', { loopList: [] }],
            ['a stretch', { voicePlan: { pieces: [[0, 4]], rounds: 1 } }],
            ['the MIDI output', { midiPlaying: true }],
            ['the plain tones', { sfPlayer: null }],
            ['the voice switched off', { practice: { speed: 1, voicesOff: ['melody'], voicesOn: [], instrumentsOn: [], voiceLevels: {} } }],
            ['a voice of another speed', { practice: { speed: 0.8, voicesOff: [], voicesOn: [], instrumentsOn: [], voiceLevels: {} } }],
        ];
        for (const [name, over] of cases) {
            const p = page(over);
            p.join(entries);
            assert.equal(p.sources.length, 0, name);
            assert.equal(p.silenced.length, 0, name);
        }
        // No rest left.
        const late = page();
        late.audioCtx.currentTime = 107;
        late.join(entries);
        assert.equal(late.sources.length, 0);
        // The menu has the instrument on too: the voice comes in over it, and nothing is taken back.
        const both = page({ practice: { speed: 1, voicesOff: [], voicesOn: [], instrumentsOn: ['melody'], voiceLevels: {} } });
        both.join(entries);
        assert.equal(both.sources.length, 1);
        assert.equal(both.silenced.length, 0);
        // …as with the plain tones, where there is nothing to take back.
        const plainBoth = page({ sfPlayer: null, practice: { speed: 1, voicesOff: [], voicesOn: [], instrumentsOn: ['melody'], voiceLevels: {} } });
        plainBoth.join(entries);
        assert.equal(plainBoth.sources.length, 1);
    });

    it('is asked when a voice is decoded, with the entries the host read from its notes', () => {
        assert.match(extension, /const sungBefore = sungTracks\(\);\s*voices\.set\(message\.part, \{[^\n]+\}\);\s*setVoiceState\(message\.part, 'ready'\);\s*joinVoice\(message\.part, message\.entries, sungBefore\);/);
        assert.match(extension, /type: 'voice', part: id, key, speed, lead: VOICE_LEAD_S, bytes: new Uint8Array\(bytes\), entries: voiceEntries\(voice\.Notes\) \}\);/);
        assert.match(extension, /playOffset = offset;\s*playingNotes = notes;/);
        assert.match(extension, /playClock = null;\s*playingNotes = null;/);
        // A score edited while it plays: the Play is the old text's, and the new text's voice is not cut into it.
        assert.match(extension, /playbackNotes = null;\s*playingNotes = null;\s*collectPages\(\);/);
    });
});
