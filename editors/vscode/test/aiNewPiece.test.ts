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
import { carrySelection, isProblem, toCandidateEdit } from '../src/aiTransformCore';

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
        assert.match(transform, /if \(compose\) \{[\s\S]{0,1500}?openTextDocument\(\{ language: 'lilysharp', content: `\$\{preface\}\$\{eol\}\$\{eol\}\$\{text\}` \}\);\s*await vscode\.window\.showTextDocument\(piece\);/);
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