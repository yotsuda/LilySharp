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
import { asksForOneStaff, carrySelection, cleanTitle, isPlaceholderTitle, isProblem, partCount, titleOf, toCandidateEdit, withTitle } from '../src/aiTransformCore';

// Owner's report 2026-10-26: "discard the score and compose a new string quartet" failed three
// times — the model answered with <edit> blocks quoting the old score, and copied none of it
// exactly. A new piece is the whole new file; and a failure says why.
describe('a new piece in place of the score', () => {
    const transform = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'aiTransform.ts'), 'utf8');

    it('is offered as a third reply form, the whole new file in <file>', () => {
        assert.match(transform, /Reply in ONE of three forms:/);
        assert.match(transform, /'3\. The request discards the score, or replaces nearly all of it/);
        assert.match(transform, /reply with the whole new file between <file> and <\/file>/);
    });

    it('is suggested when an edit quotes text the file does not have', () => {
        const file = 'title "Old"\npart p { clef treble }\nsection A { p { c4 d e f | } }\n';
        const reply = '<edit>\n<old>\nsection A { p { g4 a b c | } }\n</old>\n<new>\nsection A { p { e4 | } }\n</new>\n</edit>';
        const edit = toCandidateEdit({ origFullText: file, startOffset: 0, endOffset: 0, origSelectedText: '' }, reply);
        assert.ok(edit && isProblem(edit));
        assert.match((edit as { problem: string }).problem, /reply with the whole new file between <file> and <\/file> instead/);
    });

    it('a failure names the last try\'s reason, and every turned-down try is logged', () => {
        assert.match(transform, /deps\.log\(`AI transform: attempt \$\{attempt \+ 1\}\/\$\{MAX_REPAIR_ATTEMPTS \+ 1\} turned down — \$\{reason\}`\)/);
        assert.match(transform, /could not produce a valid transform in \$\{MAX_REPAIR_ATTEMPTS \+ 1\} tries\. Nothing was changed\.`\s*\+ \(why \? ` The last try: \$\{why\}` : ''\), showLog\)/);
    });

    it('a new piece is checked as music once, and what is found goes back for a revision', () => {
        assert.match(transform, /const newPiece = compose \|\| \/<file>\/\.test\(raw\);/);
        assert.match(transform, /if \(newPiece && !musicAsked && attempt < MAX_REPAIR_ATTEMPTS\) \{\s*const music = await compositionFindings\(client, reconstructed\);/);
        assert.match(transform, /sendRequest<\{ Findings: string\[\] \}>\('lilysharp\/compositionCheck', \{ Text: text \}\)/);
        // The octave check measures against the old notes: not for a new piece.
        assert.match(transform, /if \(edit\.text !== octaveAsked && !newPiece\) \{/);
        // A revision that does not come leaves the piece as first written, with what was found.
        assert.match(transform, /if \(candidate === null && flat\) \{\s*candidate = flat\.edit;/);
    });

    it('Compose with AI is a command of its own, writing into a new score', () => {
        assert.match(transform, /registerCommand\('lilysharp\.composeWithAi', \(\) => runCompose\(deps\)\)/);
        // No document while the AI works (owner's request 2026-10-27): the start is in memory, and
        // the piece opens in an editor of its own when it comes — no review panel, nothing to compare.
        assert.match(transform, /uri: vscode\.Uri\.parse\('untitled:Composed\.lys'\),/);
        assert.doesNotMatch(transform.slice(transform.indexOf('async function runCompose'), transform.indexOf('async function runAiTransform')), /openTextDocument/);
        assert.match(transform, /if \(compose\) \{[\s\S]{0,3000}?openTextDocument\(\{ language: 'lilysharp', content: `\$\{preface\}\$\{eol\}\$\{eol\}\$\{text\}` \}\);\s*await vscode\.window\.showTextDocument\(piece\);/);
        // The request and the AI's note head the piece as comments (owner's request 2026-10-27).
        assert.match(transform, /line\(`Asked: \$\{composeAsked \?\? instruction\}`\),\s*\.\.\.\(note \? \[line\(`The AI: \$\{note\}`\)\] : \[\]\),/);
        // What the notification says is the musician's request, not the instruction to the AI.
        assert.match(transform, /const said = composeAsked \?\? instruction;/);
        const pkg = fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8');
        assert.match(pkg, /"command": "lilysharp\.composeWithAi",\s*"category": "Lily#",\s*"icon": "\$\(sparkle\)",\s*"title": "Compose with AI…"/);
    });
});

// Owner's report 2026-10-27: "add guitar chords" asked again of a composed piece failed three
// times — the AI's edits quoted the piece it had written, and were looked for in the file before
// it. Asked again, the AI answers against the file as its candidate left it.
// Owner's request 2026-10-27: a composed piece opens in relative octaves, converted by the
// server, not by the AI.
describe('a composed piece opens in relative octaves', () => {
    const transform = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'aiTransform.ts'), 'utf8');
    it('converts the text before it opens, and keeps it as it came if it cannot', () => {
        const at = (s: string) => transform.indexOf(s);
        assert.ok(at("'lilysharp/convertOctaves', { Text: text, Absolute: false }") > 0);
        assert.ok(at("'lilysharp/convertOctaves', { Text: text, Absolute: false }") < at("const piece = await vscode.workspace.openTextDocument({ language: 'lilysharp'"));
        assert.match(transform, /if \(relative\.Success && relative\.NewText\) \{\s*text = relative\.NewText;/);
        assert.match(transform, /the piece stays in absolute octaves/);
    });
});

// Owner's report 2026-10-27: asked for guitar diagrams, the AI wrote under the guitar's range,
// and the warnings filled the candidate's head.
describe('the instruments\' ranges, and a head that holds a long list', () => {
    const transform = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'aiTransform.ts'), 'utf8');
    const grammar = fs.readFileSync(path.join(__dirname, '..', '..', '..', '..', 'docs', 'GRAMMAR_FOR_LLM.md'), 'utf8');
    it('tells the AI each instrument\'s range, the guitar\'s among them, in the grammar every request carries', () => {
        assert.match(grammar, /## Instrument ranges[\s\S]*keep\s+EVERY note inside the range/);
        assert.match(grammar, /\| `guitar` \| E2–E6 \| `e,` – `e'''` \|/);
    });
    it('holds the candidate\'s head to a few lines, scrolled for the rest', () => {
        assert.match(transform, /\.header \.texts \{ flex:1; min-width:0; max-height:7\.5em; overflow-y:auto;/);
        assert.match(transform, /<div class="header">\s*<div class="texts">/);
    });
});

// Owner's report 2026-10-27: Accept said "the document was closed" — VS Code had let go of a
// document no editor showed while the AI thought.
describe('an Accept on a file no editor shows', () => {
    const transform = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'aiTransform.ts'), 'utf8');
    // The case of the report: an unsaved composed piece, Ctrl+I on it, Accept — the name
    // "Untitled-1" had been saved once before, and its old move pointed at a file not open.
    it('takes an open document by its own name before any move, and forgets a name given out again', () => {
        assert.match(transform, /function currentUri\(uri: vscode\.Uri\): vscode\.Uri \{\s*if \(vscode\.workspace\.textDocuments\.some\(d => d\.uri\.toString\(\) === uri\.toString\(\)\)\) \{\s*return uri;/);
        assert.match(transform, /onDidOpenTextDocument\(d => \{\s*if \(d\.uri\.scheme === 'untitled'\) \{ movedUris\.delete\(d\.uri\.toString\(\)\); \}/);
    });
    it('opens the file again rather than saying it was closed; an untitled score alone cannot be', () => {
        assert.match(transform, /if \(!doc && uri\.scheme !== 'untitled'\) \{\s*try \{\s*doc = await vscode\.workspace\.openTextDocument\(uri\);/);
    });
});

describe('a revision is of the candidate', () => {
    const transform = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'aiTransform.ts'), 'utf8');

    it('reads the reply against the candidate, and writes it as one change to the file', () => {
        assert.match(transform, /const parsed = toCandidateEdit\(base, raw\);/);
        assert.match(transform, /base = revisionBase\(work, candidate\);/);
        assert.match(transform, /\{ \.\.\.wholeFileEdit\(work, spliceCandidate\(parsed\.snap, parsed\.text\)\), reply: parsed\.reply \}/);
        assert.match(transform, /The file now reads as your candidate left it — copy each <old> /);
        // An ask that brings nothing goes back to the candidate, and so does the text read against.
        assert.match(transform, /prepared!\.messages\.length = askedFrom;\s*base = baseAsked;/);
    });

    it('carries the selection into the candidate\'s text', () => {
        // "abcdefghij": "cd" (2..4) became "CCCDDD" (2..8); the selection "fg" (5..7) moves by 4.
        const spans = [{ beforeLo: 2, beforeHi: 4, afterLo: 2, afterHi: 8 }];
        assert.deepEqual(carrySelection(spans, 5, 7), { start: 9, end: 11 });
        // A selection before the change stays; one ending where it starts does not take it in.
        assert.deepEqual(carrySelection(spans, 0, 2), { start: 0, end: 2 });
        // One inside the change widens to the whole of it.
        assert.deepEqual(carrySelection(spans, 3, 6), { start: 2, end: 10 });
        // An insertion at the selection's end is taken in.
        assert.deepEqual(carrySelection([{ beforeLo: 4, beforeHi: 4, afterLo: 4, afterHi: 7 }], 1, 4), { start: 1, end: 7 });
    });
});

// Owner's request 2026-10-27: a piece the AI composes always has a title that fits it.
describe('a composed piece has a title', () => {
    const transform = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'aiTransform.ts'), 'utf8');

    it('is asked for, never seeded with a placeholder, and asked for alone when it does not come', () => {
        assert.match(transform, /Give the piece a title of its own that '\s*\+ 'fits its music and its mood/);
        assert.doesNotMatch(transform, /const start = 'title "New Piece"/);
        assert.match(transform, /if \(!had \|\| isPlaceholderTitle\(had\)\) \{\s*const title = await titleFor\(chat, text, composeAsked \?\? instruction\);/);
    });

    it('reads, cleans and writes a title', () => {
        assert.equal(titleOf('// note\ntitle "黄昏のワルツ"\ntime 3/4\n'), '黄昏のワルツ');
        assert.equal(titleOf('time 3/4\n'), null);
        assert.ok(isPlaceholderTitle('New Piece'));
        assert.equal(cleanTitle('Title: "Autumn Elegy"\n'), 'Autumn Elegy');
        assert.equal(cleanTitle('「黄昏のワルツ」'), '黄昏のワルツ');
        assert.equal(cleanTitle('Untitled'), null);
        assert.equal(withTitle('title "New Piece"\ntime 3/4\n', 'Autumn Elegy'), 'title "Autumn Elegy"\ntime 3/4\n');
        // No title line: after the leading comments.
        assert.equal(withTitle('// a\ntime 3/4\n', 'Autumn Elegy'), '// a\ntitle "Autumn Elegy"\ntime 3/4\n');
    });
});

// Owner's rule 2026-10-27: one staff only when it is asked for — a lead sheet, a melody alone, a
// solo; anything else (a jazz piece too: a combo) has its accompaniment.
describe('one staff only when asked for', () => {
    const transform = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'aiTransform.ts'), 'utf8');

    it('knows the requests that ask for one staff', () => {
        for (const yes of ['ジャズのリードシートを書いて', 'a jazz lead sheet in F', 'この歌詞にメロディだけ付けて', 'フルート独奏の曲', 'an unaccompanied cello piece', 'a guitar solo']) {
            assert.ok(asksForOneStaff(yes), yes);
        }
        for (const no of ['ジャズの曲を書いて', 'a dramatic string quartet', 'エレキギターとドラムの曲']) {
            assert.ok(!asksForOneStaff(no), no);
        }
    });

    it('counts the parts, and sends a composed piece of one back unless one staff was asked for', () => {
        assert.equal(partCount('part lead { clef treble }\nsection A { lead { c1 | } }\n'), 1);
        assert.equal(partCount('part rh { clef treble }\npart lh { clef bass }\n'), 2);
        assert.match(transform, /if \(compose && partCount\(reconstructed\) < 2 && !asksForOneStaff\(composeAsked \?\? instruction\)\) \{\s*music\.unshift\(/);
    });
});