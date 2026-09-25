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
    SEL_CLOSE, SEL_OPEN, isUnchanged, octaveOutliers, pitchToMidi, toCandidateEdit,
} from '../src/aiTransformCore';

const FILE = 'part m { clef treble }\npart h { clef treble }\n\nsection A {\n  m { c4 d e f | }\n}\n\nscore main {\n  staff m\n}\n';
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
        assert.ok(edit);
        assert.equal(edit.whole, false);
        assert.equal(edit.text, 'e4 f g a |');
        assert.equal(edit.snap.startOffset, FILE.indexOf(SEL));
    });

    it('that repeats the selection is unchanged', () => {
        const edit = toCandidateEdit(snapshotOf(FILE, SEL), SEL);
        assert.ok(edit && isUnchanged(edit));
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
        assert.ok(edit);
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
        assert.ok(edit);
        const spliced = FILE.slice(0, edit.snap.startOffset) + edit.text + FILE.slice(edit.snap.endOffset);
        assert.equal(spliced, changed);
    });

    it('gives a CRLF file its own line ends back', () => {
        const crlf = FILE.replace(/\n/g, '\r\n');
        const edit = toCandidateEdit(snapshotOf(crlf, SEL), `<file>\n${changed}</file>`);
        assert.ok(edit);
        const spliced = crlf.slice(0, edit.snap.startOffset) + edit.text + crlf.slice(edit.snap.endOffset);
        assert.equal(spliced, changed.replace(/\n/g, '\r\n'));
    });

    it('that is the file as it was is unchanged', () => {
        const edit = toCandidateEdit(snapshotOf(FILE, SEL), `<file>\n${FILE}</file>`);
        assert.ok(edit && isUnchanged(edit));
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
