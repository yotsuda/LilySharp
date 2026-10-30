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

// The editor-free half of the AI transform: reading a reply into its edit, and the octave
// check. Run with `npm test`.

import { describe, it } from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import {
    SEL_CLOSE, SEL_OPEN, carryOffset, documentBudget, documentContext, estimateTokens, isProblem, isUnchanged, marksContext, octaveOutliers,
    pickModel, isAutoModel, pitchToMidi, splitNote, planOf, withoutPlan, startTimeAt, toCandidateEdit, changedWords, diffMarkup, wholeTextMarkup, rangeFixOf,
    aiFailure, isConnectionError, spokenDuration, servedModelsIn, cleanCandidate, warningTodoLines, withLyricsRows, promptFits, compositionHint, hintTonic, drawnKeyNote, copilotModelOf, withModelNamed, withLeadingComments, isEmptyScore, guidePages, headerLanguageNote, diagLines,
} from '../src/aiTransformCore';

// Owner's request 2026-10-30: the header to name the model that composed the piece.
describe('the model a chat\'s piece names in its header', () => {
    const log = [
        '{"type":"session.start","data":{"selectedModel":"auto"}}',
        '{"type":"session.usage_record","data":{"usage":{"model":"gpt-6-luna","modelDisplayName":"GPT-6 Luna"}}}',
        '{"type":"tool.execution_start","data":{"toolName":"readGuide","model":"other-model"}}',
        '{"type":"tool.execution_start","data":{"toolName":"proposeScore","model":"gpt-6-luna"}}',
        'not json',
    ].join('\n');
    it('is the one that called proposeScore, by its display name, else its id', () => {
        assert.equal(copilotModelOf(log), 'GPT-6 Luna');
        assert.equal(copilotModelOf('{"type":"tool.execution_start","data":{"toolName":"lilysharp_proposeScore","model":"m-1"}}'), 'm-1');
        assert.equal(copilotModelOf('{"type":"session.start","data":{}}'), undefined);
    });
    it('goes into the "Composed in Copilot\'s chat" line, once', () => {
        const text = '// Composed in Copilot\'s chat, 2026-10-30\n// Asked: rock\n';
        const named = withModelNamed(text, 'GPT-6 Luna');
        assert.equal(named, '// Composed in Copilot\'s chat (GPT-6 Luna), 2026-10-30\n// Asked: rock\n');
        assert.equal(withModelNamed(named, 'Claude Opus 5.5'), '// Composed in Copilot\'s chat (Claude Opus 5.5), 2026-10-30\n// Asked: rock\n');
        assert.equal(withModelNamed('title "x"\n', 'M'), 'title "x"\n');
    });
});

// Owner's report 2026-10-29: one range warning came back 28 times, a line a note.
describe('compiler diagnostics for the AI', () => {
    it('says the same message on the same line once, with how many times, and its fix', () => {
        const low = 'this note sounds D2, below the range of the Guitar';
        const d = (Line: number, Message: string, fix = '') => ({ Line, Message, fix });
        const lines = diagLines([d(16, low), d(16, low), d(16, low, ' — write `d8`'), d(22, low), d(30, 'another')], x => x.fix);
        assert.equal(lines, `  - line 17 (3 times): ${low} — write \`d8\`\n  - line 23: ${low}\n  - line 31: another`);
    });
});

// Owner's request 2026-10-29: asked in Japanese, the chat described its piece in English.
describe('a new piece\'s header in the request\'s language', () => {
    const header = (asked: string, said: string) => `// Composed in Copilot's chat, 2026-10-29\n// Asked: ${asked}\n// The AI: ${said}\n\ntitle "x"\n`;
    it('is said when the AI line is in another script than the request', () => {
        assert.match(headerLanguageNote(header('結婚式で流す、優しいピアノ曲を書いて', 'A gentle piano piece in F major.')) ?? '',
            /not in the language of the request/);
        assert.ok(headerLanguageNote(header('Write a gentle piano piece', '結婚式のための優しい曲です。')));
    });
    it('is not said when they agree, or a line is missing', () => {
        assert.equal(headerLanguageNote(header('結婚式で流す曲', 'F majorの穏やかなピアノ曲です。')), undefined);
        assert.equal(headerLanguageNote(header('Write a waltz', 'A waltz in D — "Valse à deux".')), undefined);
        assert.equal(headerLanguageNote('title "x"\n'), undefined);
    });
    it('goes back with the new piece\'s check, once', () => {
        const src = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'aiTransform.ts'), 'utf8');
        assert.match(src, /const language = headerLanguageNote\(text\);\s*\/\/[^\n]*\n\s*const music = \[\.\.\.report\.findings, language, drawnKeyNote\(text, drawnTonic\)\]/);
    });
});

// Owner's report 2026-10-29: the composing guide, 19 KB in one answer, went to a temp file that
// Copilot's agent read back with PowerShell — "Run in terminal?" in the middle of composing.
describe('a guide in parts', () => {
    const docs = path.join(__dirname, '..', '..', '..', '..', 'docs');
    const squash = (s: string) => s.replace(/\s+/g, ' ').trim();
    for (const name of ['COMPOSE_FOR_LLM.md', 'GRAMMAR_FOR_LLM.md']) {
        it(`cuts ${name} into parts read whole, losing nothing`, () => {
            const text = fs.readFileSync(path.join(docs, name), 'utf8');
            const pages = guidePages(text, 7000, 4500);
            assert.ok(pages.length > 1);
            assert.ok(Buffer.byteLength(pages[0], 'utf8') <= 4500);
            for (const p of pages) {
                assert.ok(Buffer.byteLength(p, 'utf8') <= 7000, `a part of ${Buffer.byteLength(p, 'utf8')} bytes`);
            }
            assert.equal(squash(pages.join('\n\n')), squash(text));
        });
    }
    it('keeps a paragraph whole where it fits, and one that does not fit goes line by line', () => {
        assert.deepEqual(guidePages('aaaa\n\nbbbb\n\ncccc', 10), ['aaaa\n\nbbbb', 'cccc']);
        assert.deepEqual(guidePages('x\n\n123456\n789012\n345', 8), ['x', '123456', '789012', '345']);
    });
});

// Owner's request 2026-10-29: a piece proposed in Copilot's chat dropped the file's opening
// comments; a new piece is checked as music once before it is shown.
describe('a proposal from Copilot\'s chat', () => {
    const start = '// Composed in Copilot\'s chat, 2026-10-29\r\n// Asked: a quartet\r\n\r\noctave absolute\r\npitch concert\r\n';
    it('keeps the opening comments the proposal left out', () => {
        assert.equal(withLeadingComments(start, 'octave absolute\npitch concert\ntitle "A"\n'),
            '// Composed in Copilot\'s chat, 2026-10-29\n// Asked: a quartet\n\noctave absolute\npitch concert\ntitle "A"\n');
        const kept = '// Composed in Copilot\'s chat, 2026-10-29\n// Asked: a quartet\noctave absolute\n';
        assert.equal(withLeadingComments(start, kept), kept);
        assert.equal(withLeadingComments('title "A"\n', 'title "B"\n'), 'title "B"\n');
    });
    it('knows a file with no music yet', () => {
        assert.ok(isEmptyScore(start));
        assert.ok(isEmptyScore(''));
        assert.ok(!isEmptyScore(start + 'title "A"\n'));
    });
    it('checks a new piece as music once, its findings sent back unseen', () => {
        const src = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'aiTransform.ts'), 'utf8');
        assert.match(src, /let text = withLeadingComments\(current, input\.text\);/);
        assert.match(src, /if \(isEmptyScore\(current\) && !musicCheckedFor\.has\(uri\.toString\(\)\)\) \{\s*const report = await compositionReport\(client, text\);[\s\S]*?const music = \[\.\.\.report\.findings, language, drawnKeyNote\(text, drawnTonic\)\]\.filter\(\(m\): m is string => m !== undefined\);\s*drawnTonic = undefined;[^\n]*\s*drawnHint = undefined;[^\n]*\s*ranges = report\.ranges;\s*musicCheckedFor\.add\(uri\.toString\(\)\);/);
        // Each part's range goes with it, a fact to check against what was meant (owner's request
        // 2026-10-29: a beginner's piece slipped an octave), on the bounce, the write and an Accept.
        // Its octaves said as written (owner's report 2026-10-30: left absolute, told relative).
        assert.match(src, /const octaves = ABSOLUTE_LINE\.test\(musicians\)\s*\? 'It is written in absolute octaves, as you proposed it/);
        // One draw a piece, the guide read however often (owner's trial 2026-10-30: F, then E♭).
        assert.match(src, /const hint = drawnHint \?\?= compositionHint\(\);\s*drawnTonic = hintTonic\(hint\);/);
        assert.match(src, /'is checked again only for what it brings in, and what the check still finds in it is told to the musician\.'\s*\+ rangesSaid\(ranges\);/);
        // What the revision brought in goes back once more, the proposal after it written (owner's
        // decision 2026-10-30: a revision's bass slipped two octaves and was written in untold).
        assert.match(src, /firstFindings\.set\(uri\.toString\(\), new Set\(report\.findings\.map\(findingKey\)\)\);/);
        assert.match(src, /const brought = kept\.filter\(m => !first\.has\(findingKey\(m\)\)\);\s*if \(brought\.length > 0 && !recheckedFor\.has\(uri\.toString\(\)\)\) \{\s*recheckedFor\.add\(uri\.toString\(\)\);/);
        assert.match(src, /'The proposal after this one is written as it is, and what the check still finds in it is told to the musician\.'\s*\+ rangesSaid\(ranges\);/);
        // The revision checked again, written, and what still holds told to the musician (owner's
        // decision 2026-10-30: a rock piece sent back as too short came again as short, unmentioned).
        assert.match(src, /if \(ranges === undefined\) \{\s*const report = await compositionReport\(client, text\);\s*ranges = report\.ranges;\s*kept = report\.findings;/);
        assert.match(src, /\+ \(kept\.length > 0\s*\? '\\nLily#\\'s check of the new piece still finds this in it, as written — tell the musician each point/);
        assert.match(src, /keep it, and say why in the proposal\\'s note/);
        assert.match(src, /an octave mark slips easily/);
        // …saying first that it counts notes and leaves what does not fit the music (owner's decision 2026-10-29).
        assert.match(src, /'Lily#\\'s check of a new piece found this\. It counts notes and does not know '\s*\+ 'the kind of music you mean: take what helps the piece, and leave what does not fit it —\\n'/);
        // Its compiler warnings go back with it, the same once, before it is written (owner's report
        // 2026-10-29: a piece written with a warning was proposed again at once, over a panel).
        assert.match(src, /if \(music\.length > 0 \|\| added\.length > 0\) \{/);
        assert.match(src, /\(added\.length > 0 \? `The Lily# compiler warns:\\n\$\{formatDiags\(added, text\)\}\\nFix what is not meant\. ` : ''\)/);
        assert.ok(src.indexOf('const added = addedWarnings(') < src.indexOf('const report = await compositionReport(client, text);'));
        // Written, it is done: not proposed again unasked.
        assert.match(src, /'do not propose it again unless the musician asks for a change in the chat\.'/);
    });
});

// Owner's request 2026-10-29: six "dramatic string quartets" were all D minor, 4/4, A–B–A, with the
// same opening — a starting point drawn at random, the request's own word first.
describe('a starting point drawn at random', () => {
    it('names a key, a meter, a form and an opening, each drawn from its list', () => {
        assert.equal(compositionHint(() => 0), 'A starting point for this piece, drawn at random so that not every piece begins alike — write the piece '
            + 'so wherever the request itself says nothing of it: whatever the request says (a key, a meter, a form, a length, '
            + 'a style) comes first, and a band\'s music — rock, pop — keeps its 4/4 whatever meter is drawn; it is yours '
            + 'alone, unseen by the musician — do not speak of it: '
            + 'the key on C, major or minor as the music asks; 4/4; A–B–A; opening with the bass alone for a bar or two.');
        assert.match(compositionHint(() => 0.9999), /: the key on F, major or minor as the music asks; 3\/4; one section that grows from start to end; opening with a short introduction on the accompaniment's figure\.$/);
    });
    // Owner's report 2026-10-29: two "bright waltzes" drew minor keys, set them aside, and both fell
    // back on G major — the tonic alone is drawn, its mode left to the music; all twelve come.
    it('draws a tonic alone, every one of the twelve, the mode left to the music', () => {
        const tonics = new Set(Array.from({ length: 2000 }, () => /the key on ([A-G][♯♭]?), major or minor as the music asks( \(its minor written as [A-G]♯ minor\))?;/.exec(compositionHint())![1]));
        assert.equal(tonics.size, 12);
        assert.ok(!/major;|minor;/.test(compositionHint().replace('major or minor as the music asks;', '')));
    });
    // Owner's request 2026-10-29: rock and pop are nearly always in 4/4; a three only now and then.
    it('draws 4/4 most of the time and 3/4 seldom', () => {
        const meters = Array.from({ length: 2000 }, () => /; (\d+\/\d+); /.exec(compositionHint())![1]);
        const share = (m: string) => meters.filter(x => x === m).length / meters.length;
        assert.ok(share('4/4') > 0.5, `4/4 ${share('4/4')}`);
        assert.ok(share('3/4') < 0.2, `3/4 ${share('3/4')}`);
    });
    // Owner's decision 2026-10-30: gentle piano pieces left the drawn A and B for G and F — the key is
    // checked once against the draw, unless the request named one; enharmonic keys are the same.
    it('holds a new piece to the tonic drawn, said to the AI alone', () => {
        assert.equal(hintTonic(compositionHint(() => 0)), 'C');
        assert.equal(hintTonic(compositionHint(() => 0.9999)), 'F');
        const piece = (key: string, asked = '優しいピアノ曲を書いて') => `// Asked: ${asked}\noctave absolute\nkey ${key}\n`;
        assert.equal(drawnKeyNote(piece('g major'), 'A'),
            'The key: write the piece on A (A major or A minor, as the music asks), the starting point drawn for it — the request names no key.');
        assert.equal(drawnKeyNote(piece('a minor'), 'A'), undefined);
        assert.equal(drawnKeyNote(piece('fis major'), 'F♯'), undefined);
        assert.equal(drawnKeyNote(piece('ges major'), 'F♯'), undefined);
        assert.equal(drawnKeyNote(piece('bes major'), 'B♭'), undefined);
        assert.equal(drawnKeyNote(piece('es major'), 'E♭'), undefined);
        assert.ok(drawnKeyNote(piece('e minor'), 'E♭'));
        assert.equal(drawnKeyNote(piece('g major', 'ト長調でピアノ曲を'), 'A'), undefined);
        assert.equal(drawnKeyNote(piece('g major', 'a waltz in G major'), 'A'), undefined);
        assert.equal(drawnKeyNote(piece('g major'), undefined), undefined);
        // D♭'s and A♭'s minors are written C♯ and G♯ (owner's review 2026-10-30: D♭ minor, every A a B𝄫).
        assert.equal(drawnKeyNote(piece('e minor'), 'D♭'),
            'The key: write the piece on D♭ (D♭ major or C♯ minor, as the music asks), the starting point drawn for it — the request names no key.');
        assert.equal(drawnKeyNote(piece('cis minor'), 'D♭'), undefined);
        assert.equal(drawnKeyNote(piece('gis minor'), 'A♭'), undefined);
        const flat = Array.from({ length: 400 }, () => compositionHint()).find(h => hintTonic(h) === 'D♭')!;
        assert.match(flat, /the key on D♭, major or minor as the music asks \(its minor written as C♯ minor\);/);
        assert.equal(drawnKeyNote('// Asked: x\ntitle "A"\n', 'A'), undefined);
    });
    it('differs from piece to piece', () => {
        const seen = new Set(Array.from({ length: 20 }, () => compositionHint()));
        assert.ok(seen.size > 1);
    });
    it('goes with every Compose with AI request', () => {
        const src = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'aiTransform.ts'), 'utf8');
        assert.match(src, /in the language of the request\.\\n'\s*\/\/[^\n]*\n\s*\/\/[^\n]*\n\s*\+ hint,/);
        assert.match(src, /const hint = compositionHint\(\);\s*deps\.log\(`AI transform: \$\{hint\}`\);/);
    });
});

// Owner's request 2026-10-29: the instructions are some 30 000 tokens, and a model on this machine
// reads 12 000 — it is told so, not sent them.
describe('instructions too large for a model', () => {
    it('fit a model with room for the file and the answer, not one without', () => {
        assert.ok(promptFits(111_409, 30_000));
        assert.ok(!promptFits(12_078, 30_000));
        assert.ok(!promptFits(45_000, 30_000));   // 40 500 usable, 42 000 needed
        assert.ok(promptFits(undefined, 30_000));
        assert.ok(promptFits(0, 30_000));
    });
    it('is checked before Ask and Compose send anything', () => {
        const src = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'aiTransform.ts'), 'utf8');
        assert.match(src, /if \(!tellsTooSmall\(chat, systemTokens, 'compose with'\)\) \{\s*return;/);
        assert.match(src, /if \(!tellsTooSmall\(chat, systemTokens, 'Ask'\)\) \{\s*return;/);
    });
});

// Owner's report 2026-10-29: a composed song wrote `lyrics words sings lead` in every section and no
// `lyrics words` row in its score, so the words were drawn nowhere.
describe('a lyrics track no score shows', () => {
    const song = 'part lead { instrument soprano }\r\nsection A {\r\n  lead { c\'\'4 d\'\' e\'\' f\'\' | }\r\n  lyrics words sings lead { a b c d | }\r\n}\r\n'
        + 'form { A }\r\n\r\nscore {\r\n  staff lead\r\n  staff gtr\r\n}\r\n';
    it('is given a row under the staff it sings', () => {
        const r = withLyricsRows(song);
        assert.deepEqual(r.added, ['words']);
        assert.match(r.text, /score \{\r\n  staff lead\r\n  lyrics words\r\n  staff gtr\r\n\}/);
    });
    it('leaves a score that shows it, or does not show the part, as it is', () => {
        const shown = song.replace('  staff lead\r\n', '  staff lead\r\n  lyrics words\r\n');
        assert.deepEqual(withLyricsRows(shown), { text: shown, added: [] });
        const other = song.replace('  staff lead\r\n', '');
        assert.deepEqual(withLyricsRows(other), { text: other, added: [] });
        assert.deepEqual(withLyricsRows('score { staff a }'), { text: 'score { staff a }', added: [] });
    });
    it('goes in before each candidate is checked, so its syllables are counted against the notes', () => {
        const src = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'aiTransform.ts'), 'utf8');
        const placedAt = src.indexOf('const placed = withLyricsRows(spliceCandidate(edit.snap, edit.text));');
        assert.ok(placedAt > 0 && placedAt < src.indexOf("step('validating the candidate')"));
    });
    it('is added to each score that shows the part', () => {
        const two = song.replace(/\n\r\nscore \{/, '\n\r\nscore full {') + 'score voice {\n  staff lead\n}\n';
        const r = withLyricsRows(two);
        assert.equal((r.text.match(/lyrics words\r?\n/g) ?? []).length, 2);
        assert.match(r.text, /score voice \{\n  staff lead\n  lyrics words\n\}/);
    });
});
import { withTodoMark } from '../src/omrAiFixCore';

// Owner's request 2026-10-29: a newer composed version that renders is shown though it warns, each
// warning marked with a @todo on the line it is about.
describe('a composed piece\'s warnings marked @todo', () => {
    const text = 'section A {\r\n  lead {\r\n    a\'8 fis\'8 e\'8 d\'8 b\'8 a\'8 d\'2 |\r\n  }\r\n}';
    it('marks the item the warning points at, with the warning\'s words', () => {
        const lines = warningTodoLines(text, [{ Line: 2, Char: 4, Message: 'Measure duration 5/4 exceeds time signature 4/4: the music past the bar line is not drawn or played' }], withTodoMark);
        assert.deepEqual(lines, [{ line: 2, marked: '    a\'8@todo("Measure duration 5/4 exceeds time signature 4/4") fis\'8 e\'8 d\'8 b\'8 a\'8 d\'2 |\r' }]);
    });
    it('puts two warnings of one line in one mark, and leaves a line with nothing to mark', () => {
        const two = warningTodoLines(text, [{ Line: 2, Char: 30, Message: 'one: x' }, { Line: 2, Char: 4, Message: 'two: y' }], withTodoMark);
        assert.equal(two.length, 1);
        assert.match(two[0].marked, /@todo\("one \/ two"\)/);
        assert.deepEqual(warningTodoLines(text, [{ Line: 4, Char: 0, Message: 'brace' }], withTodoMark), []);
    });
    it('shows the newer of the two, as long as it renders, and tries each line\'s mark on its own', () => {
        const src = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'aiTransform.ts'), 'utf8');
        assert.match(src, /const newerWarned = compose && warned !== null && \(flat === null \|\| warned\.attempt > flat\.attempt\);/);
        assert.match(src, /if \(candidate === null && flat && !newerWarned\) \{/);
        assert.match(src, /for \(const m of warningTodoLines\(text, warned\.warnings, withTodoMark\)\) \{[\s\S]*?if \(!check\.HasErrors\) \{/);
    });
});

// Owner's trial 2026-10-29: a piece fenced as "```Lily#" kept the '#' as its first line.
describe('a candidate in a code fence', () => {
    it('takes the fence off whatever its language tag', () => {
        assert.equal(cleanCandidate('```Lily#\ntitle "A"\n```'), 'title "A"');
        assert.equal(cleanCandidate('```lilysharp\ntitle "A"\n```'), 'title "A"');
        assert.equal(cleanCandidate('```\ntitle "A"\n```'), 'title "A"');
        assert.equal(cleanCandidate('```lys#\ntitle "A"'), 'title "A"');
        assert.equal(cleanCandidate('title "A"'), 'title "A"');
    });
});

// Owner's request 2026-10-29: a piece composed through Copilot's Auto said "copilot/auto" on its
// first line; Copilot's own log names the model that answered.
describe('the models Copilot\'s log says answered', () => {
    const log = [
        '2026-10-29 09:18:43.285 [info] ccreq:ad40da6e.copilotmd | markdown',
        '2026-10-29 09:18:50.000 [info] ccreq:11111111.copilotmd | success | claude-fable-5.1 | 900ms | [copilotLanguageModelWrapper]',
        '2026-10-29 09:19:38.601 [info] ccreq:92b4ed0a.copilotmd | success | gpt-6-luna | 55133ms | [copilotLanguageModelWrapper]',
        '2026-10-29 09:20:10.000 [info] ccreq:22222222.copilotmd | success | gpt-6-luna | 30000ms | [copilotLanguageModelWrapper]',
        '2026-10-29 09:21:00.000 [info] ccreq:33333333.copilotmd | success | claude-sonnet-5.5 | 800ms | [panel/editAgent]',
        '2026-10-29 09:22:00.000 [info] ccreq:44444444.copilotmd | success | claude-fable-5.1 | 4000ms | [copilotLanguageModelWrapper]',
    ].join('\r\n');
    it('takes the extension\'s requests since the time asked, in order, a model once in a row', () => {
        assert.deepEqual(servedModelsIn(log, new Date(2026, 9, 29, 9, 18, 44)), ['claude-fable-5.1', 'gpt-6-luna', 'claude-fable-5.1']);
        assert.deepEqual(servedModelsIn(log, new Date(2026, 9, 29, 9, 19, 0)), ['gpt-6-luna', 'claude-fable-5.1']);
        assert.deepEqual(servedModelsIn(log, new Date(2026, 9, 29, 10, 0, 0)), []);
        assert.deepEqual(servedModelsIn('', new Date(0)), []);
    });
    it('writes the model that answered on the composed piece\'s first line', () => {
        const src = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'aiTransform.ts'), 'utf8');
        assert.match(src, /Composed with AI \(\$\{chat\.answeredBy\?\.\(\) \?\? chat\.label\}\)/);
    });
});

// Owner's report 2026-10-29: "compose with AI failed: Please check your firewall rules and network
// connection then try again. Error Code: net::ERR_HTTP2_PROTOCOL_ERROR." — two minutes into the
// piece the connection broke, and the message said nothing of that.
describe('a failed AI request', () => {
    const broke = 'Please check your firewall rules and network connection then try again. Error Code: net::ERR_HTTP2_PROTOCOL_ERROR.';
    it('tells a broken connection, when it broke, and offers to try again', () => {
        assert.ok(isConnectionError(broke));
        const f = aiFailure('compose', broke, 144155);
        assert.ok(f.retry);
        assert.equal(f.text, 'Lily#: the connection to the AI broke off after 2 min 24 s, before the piece was written. '
            + 'Try again — or ask for a shorter piece, which takes the AI less time to write.');
        assert.equal(aiFailure('ask', 'read ECONNRESET', 24000).text, 'Lily#: the connection to the AI broke off after 24 s. Try again.');
    });
    // Owner's report 2026-10-29: "compose with AI failed: Response contained no choices."
    it('tells a request the AI ended with no answer, and offers to try again', () => {
        const f = aiFailure('compose', 'Response contained no choices.', 164071);
        assert.ok(f.retry);
        assert.equal(f.text, 'Lily#: the AI ended the request after 2 min 44 s without writing anything back — nothing in the request '
            + 'caused it. Try again — or ask for a shorter piece, which takes the AI less time to write.');
        assert.equal(aiFailure('ask', 'Response contained no choices.', 9000).text, 'Lily#: the AI ended the request after 9 s without an answer. Try again.');
    });
    it('says any other error as it came, with no Try Again', () => {
        const f = aiFailure('compose', 'Model gpt-5.6-luna is only available to VS Code core.', 100);
        assert.ok(!f.retry);
        assert.equal(f.text, 'Lily#: compose with AI failed: Model gpt-5.6-luna is only available to VS Code core.');
    });
    it('reads a length of time as a person does', () => {
        assert.equal(spokenDuration(0), '0 s');
        assert.equal(spokenDuration(59400), '59 s');
        assert.equal(spokenDuration(120000), '2 min');
        assert.equal(spokenDuration(144155), '2 min 24 s');
    });
});

// Owner's report 2026-10-27: "this note sounds C1, below the range of the Bass" came back through
// every repair — the repair now names the note as written and the note to write.
describe('the repair of a note out of its instrument\'s range', () => {
    const below = { Code: 'LYS1052', Message: 'this note sounds C1, below the range of the Bass (E1-G4, as it sounds)' };
    const above = { Code: 'LYS1052', Message: 'this note sounds C7, above the range of the Guitar (E2-E6, as it sounds)' };
    it('names the note as written, and the octave to write it in', () => {
        const text = 'bass { c,8 g,8 c,4 } ';
        assert.equal(rangeFixOf({ ...below, Offset: text.indexOf('c,8') }, text), ' — you wrote `c,8`: write it an octave higher, `c8`');
        assert.equal(rangeFixOf({ ...below, Offset: 0 }, 'fis,,4. d'), ' — you wrote `fis,,4.`: write it an octave higher, `fis,4.`');
        assert.equal(rangeFixOf({ ...below, Offset: 0 }, 'e4~ e'), " — you wrote `e4`: write it an octave higher, `e'4`");
        assert.equal(rangeFixOf({ ...above, Offset: 0 }, "c'''8)"), " — you wrote `c'''8`: write it an octave lower, `c''8`");
        assert.equal(rangeFixOf({ ...above, Offset: 0 }, 'c8'), ' — you wrote `c8`: write it an octave lower, `c,8`');
    });
    it('says nothing of another report, or of text that is no note', () => {
        assert.equal(rangeFixOf({ Code: 'LYS2001', Offset: 0, Message: 'short' }, 'c4'), '');
        assert.equal(rangeFixOf({ ...below, Offset: 0 }, '<c e>4'), '');
    });
});

// Only the notes the AI really changed are lit (owner's report 2026-10-25: a rewritten stretch lit
// every note in it, the ones copied through too).
describe('what the candidate panel lights as changed', () => {
    const lit = (text: string, spans: { lo: number; hi: number }[], at: (w: string) => number) => {
        const w = text.match(/\S+/g)!;
        return w.filter(x => spans.some(s => at(x) >= s.lo && at(x) < s.hi));
    };
    it('lights the changed words of a span, not the ones both sides share', () => {
        const before = 'melody = { c4 d e f | g a b c }';
        const after = 'melody = { c4 d es f | g a bes c }';
        const spans = changedWords(before, after, [{ beforeLo: 10, beforeHi: before.length - 1, afterLo: 10, afterHi: after.length - 1 }]);
        const afterSpans = spans.map(s => ({ lo: s.afterLo, hi: s.afterHi }));
        const beforeSpans = spans.map(s => ({ lo: s.beforeLo, hi: s.beforeHi }));
        assert.deepEqual(lit(after, afterSpans, x => after.indexOf(` ${x} `) + 1), ['es', 'bes']);
        assert.deepEqual(lit(before, beforeSpans, x => before.indexOf(` ${x} `) + 1), ['e', 'b']);
    });
    it('reaches back over the spaces before a word, where a drawn note\'s data-pos stands', () => {
        const before = 'x = {\n  c4 d\n}';
        const after = 'x = {\n  c4 e\n}';
        const [s] = changedWords(before, after, [{ beforeLo: 5, beforeHi: 12, afterLo: 5, afterHi: 12 }]);
        assert.deepEqual([s.afterLo, s.afterHi], [after.indexOf(' e'), after.indexOf('e') + 1]);
    });
    it('marks the replacement text: dropped words struck, written ones green', () => {
        const before = 'm = { c4 d e <f a> }';
        const after = 'm = { c4 d es <f a> g }';
        const spans = changedWords(before, after, [{ beforeLo: 4, beforeHi: before.length, afterLo: 4, afterHi: after.length }]);
        assert.equal(diffMarkup(before, after, 4, after.length, spans),
            '{ c4 d <del>e</del> <ins>es</ins> &lt;f a&gt; <ins>g</ins> }');
    });
    it('shows the whole text, not the replacement alone, its changes marked where they are', () => {
        // (owner's request 2026-10-26) The text around the selection is there, unmarked.
        const before = 'title "x"\npart m { clef treble }\nsection A { m { c4 d e f | } }\nform { A }\n';
        const after = before.replace('c4 d e f', 'c4 d es f');
        const lo = before.indexOf('{ c4'), hi = before.indexOf(' }\nform');
        const spans = changedWords(before, after, [{ beforeLo: lo, beforeHi: hi, afterLo: lo, afterHi: hi + 1 }]);
        assert.equal(wholeTextMarkup(before, after, spans),
            'title &quot;x&quot;\npart m { clef treble }\nsection A { m { c4 d <del>e</del> <ins>es</ins> f | } }\nform { A }\n');
    });
    it('lights nothing on the side an insertion leaves empty', () => {
        const before = '{ c d }';
        const after = '{ c g a d }';
        const spans = changedWords(before, after, [{ beforeLo: 0, beforeHi: before.length, afterLo: 0, afterHi: after.length }]);
        assert.equal(spans.length, 1);
        assert.equal(spans[0].beforeLo, spans[0].beforeHi);
        assert.equal(after.slice(spans[0].afterLo, spans[0].afterHi), ' g a');
    });
});

describe('the automatic model', () => {
    // What the owner's VS Code offered on 2026-10-23: Copilot's chat models, and copilotcli — the
    // Copilot CLI agent's door, no family, the largest input limit, an empty answer to a plain request.
    const models = [
        { id: 'copilotcli', vendor: 'copilotcli', family: '', maxInputTokens: 1_000_000 },
        { id: 'gpt-4o-mini', vendor: 'copilot', family: 'gpt-4o-mini', maxInputTokens: 128_000 },
        { id: 'claude-fable-5.1', vendor: 'copilot', family: 'claude-fable-5.1', maxInputTokens: 200_000 },
        { id: 'claude-haiku-5.5', vendor: 'copilot', family: 'claude-haiku-5.5', maxInputTokens: 200_000 },
        { id: 'local-big', vendor: 'ollama', family: 'llama', maxInputTokens: 400_000 },
    ];

    it('never picks a door with no family, and prefers Copilot\'s chat models', () => {
        assert.equal(pickModel(models, false)?.id, 'claude-fable-5.1');
        assert.equal(pickModel(models, true)?.id, 'claude-haiku-5.5');
    });

    it('passes over copilotcli with a family too (VS Code 1.142 names it "auto")', () => {
        const auto = { id: 'auto', vendor: 'copilotcli', family: 'auto', maxInputTokens: 1_000_000 };
        assert.equal(pickModel([auto, models[4]], false)?.id, 'local-big');
        assert.equal(pickModel([auto], false)?.id, 'auto');                 // all there is: the caller says why it fails
    });

    // Owner's report 2026-10-27: Copilot's "Auto" (id auto, family claude-fable-5.1, the largest
    // input limit) was the automatic model, and the model that answered was Copilot's choice.
    it('passes over Copilot\'s Auto, whose family names a model it may not send to', () => {
        const auto = { id: 'auto', vendor: 'copilot', family: 'claude-fable-5.1', maxInputTokens: 935_793 };
        assert.equal(pickModel([auto, ...models], false)?.id, 'claude-fable-5.1');
        assert.equal(pickModel([auto], false)?.id, 'auto');                 // all there is
        assert.ok(isAutoModel(auto) && !isAutoModel(models[2]));
    });

    it('passes over a model VS Code keeps for itself, small as its name is (owner\'s report 2026-10-25)', () => {
        const core = { id: 'copilot-dictation-cleanup-nano', vendor: 'copilot', family: 'copilot-dictation-cleanup-nano', maxInputTokens: 500_000 };
        assert.equal(pickModel([core, ...models], true)?.id, 'claude-haiku-5.5');
        assert.equal(pickModel([core, ...models], false)?.id, 'claude-fable-5.1');
    });

    it('takes another vendor\'s chat model when Copilot offers none, and anything rather than nothing', () => {
        assert.equal(pickModel(models.filter(m => m.vendor !== 'copilot'), false)?.id, 'local-big');
        assert.equal(pickModel([models[0]], false)?.id, 'copilotcli');
        assert.equal(pickModel([], false), undefined);
    });
});

const FILE = 'part m { clef treble }\npart h { clef treble }\n\nsection A {\n  m { c4 d e f | }\n}\n\nscore {\n  staff m\n}\n';
const SEL = 'c4 d e f |';

function snapshotOf(text: string, selected: string) {
    const start = text.indexOf(selected);
    return { origFullText: text, startOffset: start, endOffset: start + selected.length, origSelectedText: selected };
}

describe('the core is editor-free', () => {
    it('never imports vscode', () => {
        const source = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'aiTransformCore.ts'), 'utf8');
        assert.doesNotMatch(source, /from 'vscode'/);
    });
});

describe('a selection-only reply', () => {
    it('replaces the selection, fences and markers removed', () => {
        const edit = toCandidateEdit(snapshotOf(FILE, SEL), '```lilysharp\n' + SEL_OPEN + 'e4 f g a |' + SEL_CLOSE + '\n```');
        assert.ok(edit && !isProblem(edit));
        assert.equal(edit.whole, false);
        assert.equal(edit.text, 'e4 f g a |');
        assert.equal(edit.snap.startOffset, FILE.indexOf(SEL));
    });

    it('that repeats the selection is unchanged', () => {
        const edit = toCandidateEdit(snapshotOf(FILE, SEL), SEL);
        assert.ok(edit && !isProblem(edit) && isUnchanged(edit));
    });

    it('that is empty is nothing', () => {
        assert.equal(toCandidateEdit(snapshotOf(FILE, SEL), '```\n```'), null);
    });
});

describe('a whole-file reply', () => {
    // The owner's case: a harmony in a NEW part needs three edits outside the selection.
    const changed = FILE
        .replace('  m { c4 d e f | }\n', '  m { c4 d e f | }\n  h { e4 f g a | }\n')
        .replace('  staff m\n', '  staff m\n  staff h\n');

    it('is reduced to the span where it differs', () => {
        const edit = toCandidateEdit(snapshotOf(FILE, SEL), `<file>\n${changed}</file>`);
        assert.ok(edit && !isProblem(edit));
        assert.equal(edit.whole, true);
        const spliced = FILE.slice(0, edit.snap.startOffset) + edit.text + FILE.slice(edit.snap.endOffset);
        assert.equal(spliced, changed);
        assert.equal(edit.snap.origSelectedText, FILE.slice(edit.snap.startOffset, edit.snap.endOffset));
        // Neither the head nor the tail of the file is inside the span.
        assert.ok(edit.snap.startOffset > FILE.indexOf('section'));
        assert.ok(edit.snap.endOffset <= FILE.length - '}\n'.length);
    });

    it('keeps the file\'s final newline when the model drops it', () => {
        const edit = toCandidateEdit(snapshotOf(FILE, SEL), `<file>\n${changed.trimEnd()}\n</file>`);
        assert.ok(edit && !isProblem(edit));
        const spliced = FILE.slice(0, edit.snap.startOffset) + edit.text + FILE.slice(edit.snap.endOffset);
        assert.equal(spliced, changed);
    });

    it('gives a CRLF file its own line ends back', () => {
        const crlf = FILE.replace(/\n/g, '\r\n');
        const edit = toCandidateEdit(snapshotOf(crlf, SEL), `<file>\n${changed}</file>`);
        assert.ok(edit && !isProblem(edit));
        const spliced = crlf.slice(0, edit.snap.startOffset) + edit.text + crlf.slice(edit.snap.endOffset);
        assert.equal(spliced, changed.replace(/\n/g, '\r\n'));
    });

    it('that is the file as it was is unchanged', () => {
        const edit = toCandidateEdit(snapshotOf(FILE, SEL), `<file>\n${FILE}</file>`);
        assert.ok(edit && !isProblem(edit) && isUnchanged(edit));
    });
});

describe('the note a reply carries', () => {
    it('is the musician\'s, and the edit is read without it', () => {
        const reply = '<note>3度上に重ねました。</note>\ne4 f g a |';
        assert.deepEqual(splitNote(reply), { note: '3度上に重ねました。', rest: '\ne4 f g a |' });
        const edit = toCandidateEdit(snapshotOf(FILE, SEL), reply);
        assert.ok(edit && !isProblem(edit));
        assert.equal(edit.text, 'e4 f g a |');
        assert.equal(edit.reply, reply);   // the history keeps it whole
    });

    it('is absent from a reply without one, and a note alone is no candidate', () => {
        assert.deepEqual(splitNote('e4 |'), { note: null, rest: 'e4 |' });
        assert.equal(toCandidateEdit(snapshotOf(FILE, SEL), '<note>sorry</note>'), null);
    });

    // A composer's working notes (owner's request 2026-10-27): read by no one, not even when they
    // quote a <note> or the music, and not lost when the reply is cut off inside them.
    it('is read past a composer\'s <plan>, which is neither the note nor the edit', () => {
        const reply = '<plan>Key E minor; A (8) B (8) A\' (8).\n<note>not this</note> e4 f g a |</plan>\n<note>力強い曲にしました。</note>\ne4 f g a |';
        assert.equal(planOf(reply), 'Key E minor; A (8) B (8) A\' (8).\n<note>not this</note> e4 f g a |');
        assert.deepEqual(splitNote(reply), { note: '力強い曲にしました。', rest: '\n\ne4 f g a |' });
        const edit = toCandidateEdit(snapshotOf(FILE, SEL), reply);
        assert.ok(edit && !isProblem(edit));
        assert.equal(edit.text, 'e4 f g a |');
        assert.equal(withoutPlan('<plan>cut off here'), '');
        assert.equal(planOf('e4 |'), null);
    });
});

describe('a reply of edits', () => {
    // The owner's case again, as the edits it takes: the new part's block and its score row.
    const reply = '<edit>\n<old>\n  m { c4 d e f | }\n</old>\n<new>\n  m { c4 d e f | }\n  h { e4 f g a | }\n</new>\n</edit>\n'
        + '<edit><old>  staff m</old><new>  staff m\n  staff h</new></edit>';
    const changed = FILE
        .replace('  m { c4 d e f | }\n', '  m { c4 d e f | }\n  h { e4 f g a | }\n')
        .replace('  staff m\n', '  staff m\n  staff h\n');
    const spliced = (text: string, e: { snap: { startOffset: number; endOffset: number }; text: string }) =>
        text.slice(0, e.snap.startOffset) + e.text + text.slice(e.snap.endOffset);

    it('applies each edit where its old text stands, and lights each place alone', () => {
        const edit = toCandidateEdit(snapshotOf(FILE, SEL), reply);
        assert.ok(edit && !isProblem(edit));
        assert.equal(edit.whole, true);
        assert.equal(spliced(FILE, edit), changed);
        assert.equal(edit.spans.length, 2);
        const [a, b] = edit.spans;
        assert.equal(FILE.slice(a.beforeLo, a.beforeHi), '  m { c4 d e f | }');
        assert.equal(changed.slice(b.afterLo, b.afterHi), '  staff m\n  staff h');
        // The music between the two edits is not among the places changed.
        assert.ok(a.afterHi < changed.indexOf('score'));
    });

    it('takes them in any order', () => {
        const parts = [...reply.matchAll(/<edit>[\s\S]*?<\/edit>/g)].map(m => m[0]);
        const edit = toCandidateEdit(snapshotOf(FILE, SEL), parts[1] + '\n' + parts[0]);
        assert.ok(edit && !isProblem(edit));
        assert.equal(spliced(FILE, edit), changed);
    });

    it('works on a CRLF file, and takes an old text off only by the spaces at its ends', () => {
        const crlf = FILE.replace(/\n/g, '\r\n');
        const edit = toCandidateEdit(snapshotOf(crlf, SEL), reply.replace('<old>  staff m</old>', '<old> staff m </old>'));
        assert.ok(edit && !isProblem(edit));
        assert.equal(spliced(crlf, edit), changed.replace(/\n/g, '\r\n'));
    });

    it('says which edit is not in the file, or in it twice, or overlaps another', () => {
        const missing = toCandidateEdit(snapshotOf(FILE, SEL), '<edit><old>staff q</old><new>x</new></edit>');
        assert.ok(isProblem(missing) && /Edit 1: .* not in the file/.test(missing.problem));
        const twice = toCandidateEdit(snapshotOf(FILE, SEL), '<edit><old>clef treble</old><new>clef bass</new></edit>');
        assert.ok(isProblem(twice) && /occurs 2 times/.test(twice.problem));
        const overlap = toCandidateEdit(snapshotOf(FILE, SEL),
            '<edit><old>c4 d e</old><new>x</new></edit><edit><old>e f |</old><new>y</new></edit>');
        assert.ok(isProblem(overlap) && /Edits 1 and 2 overlap/.test(overlap.problem));
        const empty = toCandidateEdit(snapshotOf(FILE, SEL), '<edit><old>\n</old><new>x</new></edit>');
        assert.ok(isProblem(empty) && /empty <old>/.test(empty.problem));
    });

    it('that change nothing are unchanged', () => {
        const edit = toCandidateEdit(snapshotOf(FILE, SEL), '<edit><old>staff m</old><new>staff m</new></edit>');
        assert.ok(edit && !isProblem(edit) && isUnchanged(edit));
    });
});

describe('a selection carried into absolute octaves', () => {
    // `c4 d e f | g a b c | c' b, a g |` → `c4 d e f | g a b c' | c'' b a g |`, with the line on top.
    const src = 'part m { }\nsection A {\n  m { c4 d e f | g a b c | c\' b, a g | }\n}\n';
    const dst = 'octave absolute\npart m { }\nsection A {\n  m { c4 d e f | g a b c\' | c\'\' b a g | }\n}\n';
    const at = (s: string, i: number) => src.indexOf(s) + i;
    const changes = [
        { Start: 0, End: 0, Text: 'octave absolute\n' },
        { Start: at('c |', 1), End: at('c |', 1), Text: '\'' },
        { Start: at('c\' b', 1), End: at('c\' b', 2), Text: '\'\'' },
        { Start: at('b, a', 1), End: at('b, a', 2), Text: '' },
    ];

    it('the changes rebuild the converted text', () => {
        let out = '';
        let from = 0;
        for (const c of changes) { out += src.slice(from, c.Start) + c.Text; from = c.End; }
        assert.equal(out + src.slice(from), dst);
    });

    it('keeps a selection on the same notes', () => {
        const sel = 'g a b c | c\' b, a g |';
        const lo = carryOffset(changes, src.indexOf(sel));
        const hi = carryOffset(changes, src.indexOf(sel) + sel.length);
        assert.equal(dst.slice(lo, hi), 'g a b c\' | c\'\' b a g |');
    });

    it('puts an offset at the file\'s head after the inserted line', () => {
        assert.equal(carryOffset(changes, 0), 'octave absolute\n'.length);
    });
});

describe('the file the model is shown', () => {
    const snap = snapshotOf(FILE, SEL);

    it('is the whole file, the selection marked, when it fits the model', () => {
        const shown = documentContext(snap, documentBudget(128_000, 25_000), false);
        assert.equal(shown.complete, true);
        assert.equal(shown.text, FILE.replace(SEL, SEL_OPEN + SEL + SEL_CLOSE));
    });

    it('is unmarked with nothing selected', () => {
        const all = { origFullText: FILE, startOffset: 0, endOffset: FILE.length, origSelectedText: FILE };
        assert.equal(documentContext(all, 10_000, true).text, FILE);
    });

    it('is the head and the bars around the selection when it does not', () => {
        const long = 'part m { clef treble }\n' + 'section A {\n' + '  m { c4 d e f | }\n'.repeat(3000)
            + '  m { g4 a b c | }\n' + '  m { c4 d e f | }\n'.repeat(3000) + '}\n';
        const s = snapshotOf(long, 'g4 a b c |');
        const shown = documentContext(s, 8_000, false);
        assert.equal(shown.complete, false);
        assert.ok(shown.text.startsWith('part m { clef treble }'));
        assert.ok(shown.text.includes(SEL_OPEN + 'g4 a b c |' + SEL_CLOSE));
        assert.ok(shown.text.includes('… (lines omitted) …') && shown.text.endsWith('… (rest of the file omitted) …'));
        assert.ok(estimateTokens(shown.text) <= 8_000);
    });

    it('takes the model\'s input, less the grammar and a reserve; a small or silent model gets the old share', () => {
        assert.equal(documentBudget(128_000, 25_000), 115_200 - 25_000 - 12_000);
        assert.equal(documentBudget(undefined, 25_000), 4_000);
        assert.equal(documentBudget(16_000, 25_000), 4_000);
    });

    it('counts kana as a token each', () => {
        assert.equal(estimateTokens('abcdef'), 2);
        assert.equal(estimateTokens('さくら'), 3);
    });
});

describe('the octave check', () => {
    it('reads the compiler\'s spellings as MIDI', () => {
        assert.equal(pitchToMidi('C4'), 60);
        assert.equal(pitchToMidi('F#3'), 54);
        assert.equal(pitchToMidi('Bb5'), 82);
        assert.equal(pitchToMidi('Ex4'), 66);
        assert.equal(pitchToMidi('Dbb2'), 36);
        assert.equal(pitchToMidi('r'), null);
    });

    const melody = ['G4', 'A4', 'B4', 'C5'].map((r, i) => ({ Offset: i, Written: r.toLowerCase(), Resolved: r }));
    const at = (resolved: string[]) => resolved.map((r, i) => ({ Offset: 100 + i, Written: r, Resolved: r }));

    it('passes a harmony a third above', () => {
        assert.deepEqual(octaveOutliers(melody, at(['B4', 'C5', 'D5', 'E5'])), []);
    });

    it('passes a line an octave below', () => {
        assert.deepEqual(octaveOutliers(melody, at(['G3', 'A3', 'B3', 'C4'])), []);
    });

    it('flags a harmony that landed two octaves off', () => {
        // The melody spans G4..C5, so anything below G3 or above C6 is questioned.
        const out = octaveOutliers(melody, at(['B2', 'C3', 'D3', 'G3']));
        assert.deepEqual(out.map(p => p.Resolved), ['B2', 'C3', 'D3']);
    });

    it('says nothing when there was no register to measure by', () => {
        assert.deepEqual(octaveOutliers([], at(['C1'])), []);
    });
});

describe('the marks left to check (B6)', () => {
    it('says nothing when the selection carries none', () => {
        assert.equal(marksContext([], new Map()), null);
    });

    it('lists each mark with its memo, and an OMR reader\'s confidence and readings', () => {
        const text = marksContext([
            { line: 11, written: "fis'8@todo(o1203 \"F# or F?\")", key: 'o1203', memo: 'F# or F?' },
            { line: 13, written: 'c4@todo', key: null, memo: null },
        ], new Map([['o1203', { confidence: 0.62, candidates: [{ label: 'F#', text: "fis'8" }, { text: "f'8" }] }]]))!;
        assert.match(text, /line 12: fis'8@todo\(o1203 "F# or F\?"\) — memo: F# or F\?; reader's confidence 0\.62; readings: fis'8 \(F#\), f'8/);
        assert.match(text, /line 14: c4@todo$/m);
        assert.match(text, /keep the @todo of every mark you leave/);
    });
});

describe('startTimeAt — playing an AI candidate from a clicked note', () => {
    const notes = [
        { S: 10, T: 0 }, { S: 14, T: 0.5 }, { S: 18, T: 1 },
        { S: 10, T: 2 }, { S: 14, T: 2.5 },   // the phrase played again
        { S: -1, T: 3 },                       // a note with no source
    ];
    it('starts at the clicked note, at its first play', () => {
        assert.equal(startTimeAt(notes, 14), 0.5);
    });
    it('starts at the nearest note before a click on something else', () => {
        assert.equal(startTimeAt(notes, 16), 0.5);
    });
    it('starts a bar at its first sounding note, a rest before it skipped', () => {
        const bar = [{ S: 10, T: 0 }, { S: 22, T: 1.5 }, { S: 26, T: 2 }, { S: 22, T: 9.5 }];
        assert.equal(startTimeAt(bar, 20, 30), 1.5);      // the bar [20, 30): its rest at 20 has no note
        assert.equal(startTimeAt(bar, 30, 40), 2);        // nothing written in it: as from 30 alone (the note before)
    });

    it('starts at the top before any note', () => {
        assert.equal(startTimeAt(notes, 3), 0);
    });
});