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
import { SungVoiceDto, VoiceProblems, engineFailureProblem, engineSaid, singerFailureCause, singerFailureText, voiceWho, wordsProblems } from '../src/singerCore';

// Owner, 2026-11-13 (kaze.lys): the engine failed on the alto - NEUTRINO's "Failed to load phrase
// data" - and the editor showed its words for eight seconds in the status bar, naming no line of
// the file and no cause. The failure is a warning on the voice's `singer` line in the Problems
// panel: which line, what happened in plain words, where to look, the engine's own words last.
// The wording is Core's Audio.SingerFailure (`lysc wav --singer` says the same sentences).

const TEXT = [
    'part alt "Alto" { instrument alto  lyrics aw }',
    '',
    '   singer female "neutrino/REINA"   on alt   // the alto',
    'section Verse3 {',
    "  alt { a'8 a' a' a'4 a'8 | fis'2. | }",
    '  lyrics aw { ど い た だ ろ~う | か | }',
    '}',
].join('\n');
const singerStart = TEXT.indexOf('singer');
const singerEnd = TEXT.indexOf('   // the alto');
const firstNote = TEXT.indexOf("a'8");
const strange = TEXT.indexOf("a'4");
const alto: SungVoiceDto = {
    Part: 'alt', Language: 'ja', Singer: 'neutrino/REINA', Gender: 'female', SingerStart: singerStart, SingerEnd: singerEnd,
    Notes: [{ T: 0, D: 1, P: 69, L: 'ど', S: firstNote }],
};

describe('what is said when the engine fails on a voice', () => {
    it('names the singer line, the cause in plain words, and the engine\'s own message last', () => {
        const problem = engineFailureProblem(alto, 'Failed to load phrase data', TEXT);
        assert.equal(problem.message,
            'alt (singer female "neutrino/REINA" on alt, line 3) is not sung: NEUTRINO could not make a phrase out of the notes it was given '
            + '(seen when a phrase begins on a held note with no syllable, or a syllable is not one it has a sound for). '
            + 'Its instrument plays instead. The engine said: Failed to load phrase data');
        assert.equal(TEXT.slice(problem.start, problem.end), 'singer female "neutrino/REINA"   on alt');
        assert.equal(problem.related, undefined);
    });

    it('says where to look first when the server found a syllable that is not kana', () => {
        const voice = { ...alto, Warnings: ['1 syllable(s) are not kana'], WarningAt: strange, WarningPlace: 'line 5 (section Verse3, bar 1)' };
        const problem = engineFailureProblem(voice, 'Error: Failed to load phrase data', TEXT);
        assert.match(problem.message, /Its instrument plays instead\. Look first at line 5 \(section Verse3, bar 1\)\. The engine said: Failed to load phrase data$/);
        assert.equal(TEXT.slice(problem.related!.start, problem.related!.end), "a'4");
    });

    it('shows a message it does not know as the engine said it', () => {
        assert.equal(singerFailureCause('the acoustic model wants energy'), undefined);
        assert.equal(singerFailureText('alt', ' the acoustic model wants energy\n'),
            'alt is not sung; its instrument plays instead. The engine said: the acoustic model wants energy');
        assert.equal(engineSaid(new Error('Error: NEUTRINO did not sing')), 'NEUTRINO did not sing');
    });

    it('knows the engine that did not start, took too long, or is not running', () => {
        assert.equal(singerFailureCause('neutrino_server.exe did not become ready within 60 s'), "the engine's program did not start");
        assert.equal(singerFailureCause('the engine gave no answer in 120 s'), 'the engine took too long and was given up');
        assert.equal(singerFailureCause('VOICEVOX did not answer at http://127.0.0.1:50021/: is it running?'), 'the engine is not running');
    });

    it('stands on the voice\'s first note when no line of the file asks for the voice', () => {
        const bare = { ...alto, SingerStart: null, SingerEnd: null };
        assert.equal(voiceWho(bare, TEXT), 'alt');
        const problem = engineFailureProblem(bare, 'boom', TEXT);
        assert.equal(TEXT.slice(problem.start, problem.end), "a'8");
        assert.equal(problem.message, 'alt is not sung; its instrument plays instead. The engine said: boom');
    });

    it('names a part\'s second voice by its key', () => {
        assert.equal(voiceWho({ ...alto, Key: 'alt#2' }, TEXT), 'alt#2 (singer female "neutrino/REINA" on alt, line 3)');
    });
});

describe('the words a voice will not sing', () => {
    it('are a warning on the note the first of them stands under', () => {
        const warning = '1 syllable(s) are not kana, which the Japanese voice needs: la — the first at line 5 (section Verse3, bar 1)';
        const [problem] = wordsProblems({ ...alto, Warnings: [warning], WarningAt: strange }, TEXT);
        assert.equal(problem.message, `alt: ${warning}`);
        assert.equal(TEXT.slice(problem.start, problem.end), "a'4");
        assert.deepEqual(wordsProblems({ ...alto, Warnings: [warning] }, TEXT), []);     // no place: the status bar's alone
        assert.deepEqual(wordsProblems(alto, TEXT), []);
    });
});

describe('the Problems panel\'s voices', () => {
    const failed = engineFailureProblem(alto, 'boom', TEXT);
    it('keeps a failure until the voice is sung', () => {
        const problems = new VoiceProblems();
        assert.equal(problems.set('a.lys', 'engine', 'alt', [failed]), true);
        assert.deepEqual(problems.of('a.lys'), [failed]);
        assert.deepEqual(problems.of('b.lys'), []);
        assert.equal(problems.set('a.lys', 'engine', 'alt', []), true);      // sung
        assert.deepEqual(problems.of('a.lys'), []);
        assert.equal(problems.set('a.lys', 'engine', 'alt', []), false);     // nothing to take away: nothing to redraw
    });

    it('keeps the kinds apart, and drops a voice the score sings no more', () => {
        const problems = new VoiceProblems();
        const words = { start: 1, end: 2, message: 'alt: not kana' };
        problems.set('a.lys', 'engine', 'alt', [failed]);
        problems.set('a.lys', 'words', 'alt', [words]);
        problems.set('a.lys', 'engine', 'sop', [failed]);
        problems.set('a.lys', 'words', 'alt', []);
        assert.equal(problems.of('a.lys').length, 2);
        assert.equal(problems.keep('a.lys', ['sop']), true);
        assert.deepEqual(problems.of('a.lys'), [failed]);
        assert.equal(problems.keep('a.lys', ['sop']), false);
        problems.forget('a.lys');
        assert.deepEqual(problems.of('a.lys'), []);
    });
});
