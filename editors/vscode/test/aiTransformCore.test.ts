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
    pickModel, pitchToMidi, splitNote, startTimeAt, toCandidateEdit,
} from '../src/aiTransformCore';

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