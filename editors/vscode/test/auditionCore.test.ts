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
import {
    AuditionMemory, Debouncer, EDIT_SELECTION_WINDOW_MS, CHORD_MS, NOTE_MS, STEP_DOWN_KEY, STEP_UP_KEY,
    auditionDurationMs, caretMoveMaySound, mayChangeATypedPitch, stepFallbackCommand,
} from '../src/auditionCore';

const DOC = 'file:///a.lys';

describe('the step keys (owner\'s decision 2026-09-28: Ctrl+Shift+Up / Down)', () => {
    it('are bound in package.json to Ctrl+Shift+Up / Down, in a writable .lys editor', () => {
        const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8'));
        const bindings = manifest.contributes.keybindings as { command: string; key: string; when: string }[];
        const up = bindings.filter(b => b.command === 'lilysharp.stepUp');
        const down = bindings.filter(b => b.command === 'lilysharp.stepDown');
        assert.deepEqual(up.map(b => b.key), [STEP_UP_KEY]);
        assert.deepEqual(down.map(b => b.key), [STEP_DOWN_KEY]);
        for (const b of [...up, ...down]) {
            assert.equal(b.when, 'editorTextFocus && !editorReadonly && editorLangId == lilysharp');
        }
        assert.ok(!bindings.some(b => /ctrl\+alt\+(up|down)/.test(b.key)), 'no Ctrl+Alt+Up/Down left');
    });

    it('fall back to Add Cursor Above / Below on Linux only (its second binding there)', () => {
        assert.equal(stepFallbackCommand(1, 'linux'), 'editor.action.insertCursorAbove');
        assert.equal(stepFallbackCommand(-1, 'linux'), 'editor.action.insertCursorBelow');
        for (const platform of ['win32', 'darwin']) {
            assert.equal(stepFallbackCommand(1, platform), undefined);
            assert.equal(stepFallbackCommand(-1, platform), undefined);
        }
    });
});

describe('the caret audition: a different note sounds, the same one does not', () => {
    it('sounds the first note the caret lands on, and not again while it stays on it', () => {
        const m = new AuditionMemory();
        assert.equal(m.caretLanded(DOC, { key: 10, pitches: [60] }), true);
        // Arrowing through the letters of the same note: one key.
        assert.equal(m.caretLanded(DOC, { key: 10, pitches: [60] }), false);
        assert.equal(m.caretLanded(DOC, { key: 14, pitches: [62] }), true);
        assert.equal(m.caretLanded(DOC, { key: 10, pitches: [60] }), true);
    });

    it('stays silent on nothing that sounds, and leaving a note for nothing does not re-arm it', () => {
        const m = new AuditionMemory();
        assert.equal(m.caretLanded(DOC, { key: 10, pitches: [60] }), true);
        assert.equal(m.caretLanded(DOC, { key: -1, pitches: [] }), false);
        assert.equal(m.caretLanded(DOC, { key: 10, pitches: [60] }), false);
    });

    it('keeps each document to itself', () => {
        const m = new AuditionMemory();
        assert.equal(m.caretLanded(DOC, { key: 10, pitches: [60] }), true);
        assert.equal(m.caretLanded('file:///b.lys', { key: 10, pitches: [60] }), true);
        m.forget(DOC);
        assert.equal(m.caretLanded(DOC, { key: 10, pitches: [60] }), true);
    });

    it('does not repeat what a step or a typed note just sounded', () => {
        const m = new AuditionMemory();
        m.record(DOC, { key: 10, pitches: [72] });
        assert.equal(m.caretLanded(DOC, { key: 10, pitches: [72] }), false);
        assert.equal(m.typed(DOC, { key: 20, pitches: [64] }), true);
        assert.equal(m.caretLanded(DOC, { key: 20, pitches: [64] }), false);
    });
});

describe('the typing audition: a changed sound plays once', () => {
    it('sounds a new note, then only when its pitch changes', () => {
        const m = new AuditionMemory();
        assert.equal(m.typed(DOC, { key: 30, pitches: [61] }), true);    // cis
        assert.equal(m.typed(DOC, { key: 30, pitches: [61] }), false);   // cis4: the digit changed nothing
        assert.equal(m.typed(DOC, { key: 30, pitches: [73] }), true);    // cis': an octave mark
        assert.equal(m.typed(DOC, { key: -1, pitches: [] }), false);     // typing a keyword
    });

    it('asks only about a small change that can carry a pitch', () => {
        assert.equal(mayChangeATypedPitch([{ text: 'c', rangeLength: 0 }]), true);
        assert.equal(mayChangeATypedPitch([{ text: 's', rangeLength: 0 }]), true);
        assert.equal(mayChangeATypedPitch([{ text: "'", rangeLength: 0 }]), true);
        // Smart typing relocates an octave mark by replacing the note's tail.
        assert.equal(mayChangeATypedPitch([{ text: "'4", rangeLength: 1 }]), true);
        assert.equal(mayChangeATypedPitch([{ text: '4', rangeLength: 0 }]), false);
        assert.equal(mayChangeATypedPitch([{ text: '.', rangeLength: 0 }]), false);
        assert.equal(mayChangeATypedPitch([{ text: ' ', rangeLength: 0 }]), false);
        assert.equal(mayChangeATypedPitch([{ text: '', rangeLength: 1 }]), false);
        assert.equal(mayChangeATypedPitch([{ text: "c'4 d'4 e'4 f'4", rangeLength: 0 }]), false);  // a paste
        assert.equal(mayChangeATypedPitch([{ text: 'c', rangeLength: 0 }, { text: 'c', rangeLength: 0 }]), false);
    });
});

describe('which selection changes may sound', () => {
    it('a caret move, not a selection being extended nor an edit moving the caret', () => {
        assert.equal(caretMoveMaySound({ allEmpty: true, msSinceEdit: 5000 }), true);
        assert.equal(caretMoveMaySound({ allEmpty: false, msSinceEdit: 5000 }), false);
        assert.equal(caretMoveMaySound({ allEmpty: true, msSinceEdit: 0 }), false);
        assert.equal(caretMoveMaySound({ allEmpty: true, msSinceEdit: EDIT_SELECTION_WINDOW_MS }), true);
    });

    it('rings a chord longer than a note', () => {
        assert.equal(auditionDurationMs(1), NOTE_MS);
        assert.equal(auditionDurationMs(5), CHORD_MS);
    });
});

describe('the debounce', () => {
    /** A hand-driven clock: timers run only when the test advances it. */
    function clock() {
        let now = 0;
        const timers = new Map<number, { at: number; fn: () => void }>();
        let next = 1;
        return {
            set: (fn: () => void, ms: number) => { timers.set(next, { at: now + ms, fn }); return next++; },
            clear: (h: unknown) => { timers.delete(h as number); },
            advance(ms: number) {
                now += ms;
                for (const [h, t] of [...timers]) {
                    if (t.at <= now) { timers.delete(h); t.fn(); }
                }
            },
        };
    }

    it('runs only the last of a burst, once the burst has rested', () => {
        const c = clock();
        const d = new Debouncer(80, c.set, c.clear);
        const ran: string[] = [];
        // An arrow key held down: a poke every 30 ms.
        for (const note of ['c', 'd', 'e', 'f']) {
            d.poke(() => ran.push(note));
            c.advance(30);
        }
        assert.deepEqual(ran, []);
        assert.equal(d.pending, true);
        c.advance(60);
        assert.deepEqual(ran, ['f']);
        assert.equal(d.pending, false);
    });

    it('drops a pending action when cancelled', () => {
        const c = clock();
        const d = new Debouncer(300, c.set, c.clear);
        const ran: string[] = [];
        d.poke(() => ran.push('x'));
        c.advance(100);
        d.cancel();
        c.advance(1000);
        assert.deepEqual(ran, []);
    });
});
