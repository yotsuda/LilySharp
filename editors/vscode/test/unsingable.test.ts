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
import { CantoriaApi, CantoriaVoice, PlanAsk, SungVoiceDto, VOICE_INSTALL_GUIDE, languageRefusal, planAsked, unsingableGuide, unsingableIn, unsingableReason } from '../src/singerCore';
import { CantoriaExtensionBackend, PLAN_KEPT_MS, VoiceRenderer } from '../src/singer';

// Owner's decisions 2026-11-13: a line no installed voice can sing (a Latin alto, with no voice
// that sings Latin) is said to the writer - once a session for each reason, with the way to a
// voice - and its part's instrument plays. Who can sing is Cantoria's to know (its plan); an
// older Cantoria has no plan, and Lily# then tells what it can by itself.

const note = { T: 0, D: 1, P: 60, L: 'a' };
const melody: SungVoiceDto = { Part: 'melody', Language: 'ja', Notes: [note], Singer: 'neutrino/MERROW', Gender: 'female' };
const alto: SungVoiceDto = { Part: 'alto', Language: 'la', Notes: [note], Singer: 'female', Gender: 'female' };
const second: SungVoiceDto = { Part: 'melody', Key: 'melody#2', Language: 'en', Notes: [note], Singer: null, Gender: 'male' };

const LATIN = 'no installed voice sings Latin (la); installed voices sing ja, en, zh, ko';

/** A Cantoria extension's API as far as these tests go; `plan` and the rest only where given. */
function api(more: Partial<CantoriaApi>): CantoriaApi {
    return {
        version: '0.0.0', apiVersion: 1,
        status: async () => ({ ready: true, voice: 'neutrino/MERROW', problems: [] }),
        sing: async () => ({ wav: '', duration: 0 }),
        release: () => undefined,
        ...more,
    };
}

describe('what a score\'s voices ask the plan for', () => {
    it('is the voice a line names, else its gender alone, in the language of its words', () => {
        assert.deepEqual(planAsked([melody, alto, second]), [
            { part: 'melody', key: 'melody', voice: 'neutrino/MERROW', gender: 'female', language: 'ja' },
            { part: 'alto', key: 'alto', gender: 'female', language: 'la' },
            { part: 'melody', key: 'melody#2', gender: 'male', language: 'en' },
        ]);
    });
});

describe('the lines nobody installed can sing', () => {
    it('are the plan\'s entries with no voice, by key, each with its reason', () => {
        const answer = { ready: false, singers: [{ voice: 'neutrino/MERROW', reason: null }, { voice: null, reason: LATIN }, { voice: 'diffsinger/TIGER' }] };
        assert.deepEqual([...unsingableIn(answer, [melody, alto, second])], [['alto', LATIN]]);
    });

    it('are none when every line has a singer, and none from an answer that is not for these voices', () => {
        assert.equal(unsingableIn({ ready: true, singers: [{ voice: 'a/B' }] }, [melody]).size, 0);
        assert.equal(unsingableIn({ ready: false, singers: [{ voice: null, reason: LATIN }] }, [melody, alto]).size, 0);
        assert.equal(unsingableIn(undefined, [melody]).size, 0);
        assert.equal(unsingableIn({ ready: false } as never, [melody]).size, 0);
    });

    it('have a reason even when the plan gives none', () => {
        assert.deepEqual([...unsingableIn({ ready: false, singers: [{ voice: null }] }, [alto])], [['alto', 'no installed voice sings it']]);
    });
});

describe('the Cantoria extension asked who would sing', () => {
    it('is asked once for all the voices when it has a plan, and nothing is resolved or sung', async () => {
        const asked: PlanAsk[][] = [];
        const backend = new CantoriaExtensionBackend(api({
            plan: async input => { asked.push(input.asked); return { ready: false, singers: [{ voice: 'neutrino/MERROW' }, { voice: null, reason: LATIN }] }; },
            resolveSinger: async () => { throw new Error('not asked when there is a plan'); },
        }));
        assert.deepEqual([...await backend.unsingable([melody, alto])], [['alto', LATIN]]);
        assert.equal(asked.length, 1);
        assert.deepEqual(asked[0].map(a => a.key), ['melody', 'alto']);
    });

    it('an older one, with no plan: a voice that says what it sings and not these words\' language', async () => {
        const voices: CantoriaVoice[] = [
            { id: 'neutrino/MERROW', engine: 'neutrino', name: 'MERROW', languages: ['ja'], gender: 'female' },
            { id: 'diffsinger/QUIET', engine: 'diffsinger', name: 'QUIET' },
        ];
        const backend = new CantoriaExtensionBackend(api({
            voices: async () => voices,
            resolveSinger: async singer => ({ voice: singer === 'diffsinger/QUIET' ? singer : 'neutrino/MERROW' }),
        }));
        const says = await backend.unsingable([melody, alto, { ...alto, Part: 'bass', Singer: 'diffsinger/QUIET' }]);
        // (A voice that does not say what it sings is let try, as Cantoria lets it.)
        assert.deepEqual([...says], [['alto', 'neutrino/MERROW sings ja, and these words are la']]);
    });

    it('one older still, with no way to ask, says nothing: the voices go to the engine as before', async () => {
        assert.equal((await new CantoriaExtensionBackend(api({})).unsingable([alto])).size, 0);
    });

    it('is not asked the same question again within a run of edits, and is after it', async () => {
        let asked = 0;
        const renderer = new VoiceRenderer(new CantoriaExtensionBackend(api({
            plan: async () => { asked++; return { ready: false, singers: [{ voice: null, reason: LATIN }] }; },
        })));
        assert.deepEqual([...await renderer.unsingable([alto], 1000)], [['alto', LATIN]]);
        assert.deepEqual([...await renderer.unsingable([alto], 1000 + PLAN_KEPT_MS - 1)], [['alto', LATIN]]);
        assert.equal(asked, 1);
        await renderer.unsingable([{ ...alto, Language: 'ja' }], 2000);      // another question
        await renderer.unsingable([{ ...alto, Language: 'ja' }], 2000 + PLAN_KEPT_MS);
        assert.equal(asked, 3);
    });

    it('a question that fails says nothing either', async () => {
        const renderer = new VoiceRenderer(new CantoriaExtensionBackend(api({ plan: async () => { throw new Error('the engine is away'); } })));
        assert.equal((await renderer.unsingable([alto])).size, 0);
    });
});

describe('what is said of a line nobody can sing', () => {
    it('is the reason, what plays then, and the way to a voice', () => {
        const guide = unsingableGuide(LATIN);
        assert.equal(guide.message, `Lily#: a singer line is not sung: ${LATIN}. The part's instrument plays instead.`);
        assert.deepEqual(guide.actions, [{ label: 'How to Install a Voice', url: VOICE_INSTALL_GUIDE }]);
        // One message a reason: the session's set of things said is keyed by it.
        assert.equal(unsingableGuide(LATIN + '.').message, guide.message);
        assert.notEqual(unsingableGuide('no voice is installed').message, guide.message);
    });

    it('is told from a render gone wrong by what the engine threw', () => {
        assert.equal(unsingableReason(new Error('neutrino/MERROW sings ja, and these words are la')), 'neutrino/MERROW sings ja, and these words are la');
        assert.equal(unsingableReason('Error: no installed voice sings Latin (la); installed voices sing ja.'), 'no installed voice sings Latin (la); installed voices sing ja');
        assert.equal(unsingableReason(new Error('the engine gave no answer in 180 s')), undefined);
        assert.equal(unsingableReason('cancelled'), undefined);
    });

    it('a voice says what it does not sing only when it says what it sings', () => {
        const voices: CantoriaVoice[] = [{ id: 'a/B', engine: 'a', name: 'B', language: 'ja' }, { id: 'a/C', engine: 'a', name: 'C' }];
        assert.equal(languageRefusal('a/B', 'la', voices), 'a/B sings ja, and these words are la');
        assert.equal(languageRefusal('a/B', 'ja', voices), undefined);
        assert.equal(languageRefusal('a/C', 'la', voices), undefined);
        assert.equal(languageRefusal('a/NONE', 'la', voices), undefined);
    });
});

describe('the editor and a line nobody can sing', () => {
    const ext = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');

    it('says it once a session for each reason, and in the output each time', () => {
        assert.match(ext, /function tellUnsingable\(voice: string, reason: string\): void \{\s+outputChannel\.appendLine\([^\n]+\);\s+offerVoiceSetup\(unsingableGuide\(reason\)\);/);
        assert.match(ext, /function offerVoiceSetup\([^)]*\): void \{\s+if \(voiceSetupOffered\.has\(guide\.message\)\) \{ return; \}/);
    });

    it('never hands such a line to the engine: Play, Export and a proposal\'s Play', () => {
        // Play: shown as not sung on its line of the menu, with the reason; no voice, so the instrument stands in.
        assert.match(ext, /const unsingable = await engine\.unsingable\(active\);[\s\S]{0,600}tellUnsingable\(id, nobody\);\s+void panel\.webview\.postMessage\(\{ type: 'voiceState', part: id, speed, state: 'failed', detail: nobody \}\);\s+continue;/);
        // Export: skipped before the render, so its track is not silenced.
        assert.match(ext, /if \(nobody !== undefined\) \{ tellUnsingable\(key, nobody\); continue; \}\s+try \{\s+const \{ rendered \} = await engine\.render\(voice, speed\);/);
        assert.match(ext, /const unsingable = await engine\.unsingable\(candidates\);/);
    });

    it('says what a refusing engine threw the same way, not only in the status bar', () => {
        assert.match(ext, /function failVoice\([^)]*\): void \{[\s\S]{0,400}const unsingable = unsingableReason\(why\);\s+if \(unsingable\) \{ tellUnsingable\(part, unsingable\); \}/);
    });
});
