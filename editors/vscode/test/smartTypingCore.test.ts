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

// The smart typing rules, one keystroke at a time. Each case is a document
// with the caret as ‸, a key, and the document the extension leaves behind;
// the numbers are the rules at the top of src/smartTypingCore.ts, and the
// examples are that list's own. Run with `npm test` (node's test runner —
// nothing here needs VS Code, which is the point of smartTypingCore.ts).

import { describe, it } from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { composeFix, multiCursorPlan, stayPut } from '../src/smartTypingCore';
import { parseMulti, pressBackspace, pressDelete, show, showMulti, typeKey, typeKeyAtCarets } from './simulate';

/** Asserts that typing `key` at the caret of `before` leaves `after`. */
function typed(before: string, key: string, after: string, opts?: { autoClose?: boolean }) {
    assert.equal(show(typeKey(before, key, opts)), after, `${JSON.stringify(before)} + ${key}`);
}

describe('the core is editor-free', () => {
    it('never imports vscode — the property that lets it run here at all', () => {
        const source = fs.readFileSync(
            path.join(__dirname, '..', '..', 'src', 'smartTypingCore.ts'), 'utf8');
        assert.doesNotMatch(source, /from\s+'vscode'/);
        assert.doesNotMatch(source, /require\(\s*'vscode'\s*\)/);
    });
});

describe('chord brackets (rules 1–7)', () => {
    it('1. < before a note wraps that one note, caret inside', () => {
        typed('‸c4 d', '<', '<c‸>4 d');
        typed('‸cis,4 d', '<', '<cis,‸>4 d');
    });
    it('1. < in whitespace keeps the plain auto-closed pair', () => {
        typed('c4 ‸ d', '<', 'c4 <‸> d');
    });
    it('2. < doubled promotes the matching > to >>', () => {
        typed('‸<c e>4', '<', '<‸<c e>>4');
    });
    it('3. one < of a << deleted demotes the matching >>', () => {
        assert.equal(show(pressDelete('‸<<c e>>4')), '‸<c e>4');
        assert.equal(show(pressBackspace('<<‸c e>>4')), '<‸c e>4');
    });
    it('4. > doubled promotes the matching < to <<', () => {
        typed('<c e>‸4', '>', '<<c e>>‸4');
    });
    it('5. one > of a >> deleted demotes the matching <<', () => {
        assert.equal(show(pressDelete('<<c e>‸>4')), '<c e>‸4');
        assert.equal(show(pressBackspace('<<c e>>‸4')), '<c e>‸4');
    });
    it('6. an unresolved > ahead keeps a typed < bare', () => {
        typed('c4 ‸ e g>4', '<', 'c4 <‸ e g>4');   // the auto-closed > is dropped
        typed('‸e g>4', '<', '<‸e g>4');           // the wrap is suppressed
    });
    it('7. a lone > with nothing to close auto-opens', () => {
        typed('c‸ d', '>', '<c>‸ d');
        typed('c4 ‸d', '>', 'c4 <>‸d');
        typed('<c e‸', '>', '<c e>‸');            // closes the open <, adds nothing
    });
    it('deleting a lone bracket leaves its partner — that is how a range is redrawn', () => {
        assert.equal(show(pressDelete('‸<c e>4')), '‸c e>4');
        assert.equal(show(pressDelete('<c e‸>4')), '<c e‸4');
    });
});

describe('slurs (rules 8–13)', () => {
    it('8. ( after a note closes after the following note', () => {
        typed('c4‸ d e', '(', 'c4(‸ d) e');
        typed('c‸ d e', '(', 'c(‸ d) e');
        typed('c‸4 d', '(', 'c4(‸ d)');            // typed mid-note: the note is read whole
    });
    it('8. ( glued to the note ahead starts the slur on that note', () => {
        typed('c ‸d e', '(', 'c d(‸ e)');
    });
    it('8. ( on the note before an open slur extends it backwards', () => {
        typed('b‸ c4( d) e', '(', 'b(‸ c4 d) e');
    });
    it('9. an unresolved ) ahead keeps the ( bare', () => {
        typed('c4 d‸ e) f', '(', 'c4 d(‸ e) f');
    });
    it('10. no note ahead leaves the pair as typed', () => {
        typed('c4‸', '(', 'c4(‸)');
    });
    it('11. ) after a note opens after the note before', () => {
        typed('c4 d‸', ')', 'c4( d)‸');
        typed('c4 ‸d', ')', 'c4( d)‸');           // typed at the note's start: same slur
    });
    it('11. the note before may lie in the bar before — a slur crosses barlines', () => {
        typed('c4 | d‸', ')', 'c4( | d)‸');
        typed('c4 | time 3/4 | d‸', ')', 'c4( | time 3/4 | d)‸'); // an event-less bar between
        typed('c4 Melody | d‸', ')', 'c4 Melody | d)‸');           // a barrier there still resets
    });
    it('a barline inside a comment or a string is no place to start reading from', () => {
        typed('c4 // bar |\n d‸', ')', 'c4( // bar |\n d)‸');
        typed('c4@text("|") d‸', ')', 'c4@text("|")( d)‸');
    });
    it('12. an unresolved ( before the ) is simply closed — annotations included', () => {
        typed('c4( d‸', ')', 'c4( d)‸');
        typed('c4@finger(3‸', ')', 'c4@finger(3)‸');
    });
    it('13. ) on the note after a slur\'s end extends that slur', () => {
        typed('c4( d) e‸', ')', 'c4( d e)‸');
    });
    it(') inside an open slur moves its end here', () => {
        typed('c4( d‸ e) f', ')', 'c4( d)‸ e f');
    });
    it('a slur spans a mid-music command, never lands on its operand', () => {
        typed('c4‸ key g major d', '(', 'c4(‸ key g major d)');
    });
    it('a chord is one note to a slur; inside its brackets nothing is placed', () => {
        typed('<c e>4‸ d', '(', '<c e>4(‸ d)');
        typed('<c ‸e>4 d', '(', '<c (‸e>4 d');
    });
    it('a ( in a comment or a string is plain text', () => {
        typed('// c4‸ d', '(', '// c4(‸) d');
        typed('c4 "a‸ b" d', '(', 'c4 "a(‸) b" d');
    });
});

describe('ties (rule 19)', () => {
    it('~ anywhere on a note moves to its end', () => {
        typed('‸c2 c', '~', 'c2~‸ c');
        typed('c‸2 c', '~', 'c2~‸ c');
        typed('c2‸ c', '~', 'c2~‸ c');
    });
});

describe('beams (rules 20–22)', () => {
    it('20. [ on a beamable note closes on the next one', () => {
        typed('c8‸ d e f', '[', 'c8[‸ d] e f');
    });
    it('20. a beam already starting on the next note is extended backwards', () => {
        typed('c8‸ d[ e]', '[', 'c8[‸ d e]');
    });
    it('20. a rest is spanned but never closed on; a quarter is a wall', () => {
        typed('c8‸ r8 d', '[', 'c8[‸ r8 d]');
        typed('c8‸ d4 e8', '[', 'c8[‸] d4 e8');
    });
    it('20. a beam never crosses a barline', () => {
        typed('c8‸ | d8', '[', 'c8[‸] | d8');
    });
    it('21. ] after a note opens after the beamable note before', () => {
        typed('c8 d‸', ']', 'c8[ d]‸');
    });
    it('21. ] on the note after a beam\'s end extends that beam', () => {
        typed('c8[ d] e‸', ']', 'c8[ d e]‸');
    });
    it('22. a note that cannot carry a beam, or a [ off a note, is left as typed', () => {
        typed('c4‸ d', '[', 'c4[‸] d');
        typed('c4 ‸ d', '[', 'c4 [‸] d');
    });
    it('the running duration is carried: the d e f of c8 d e f are eighths', () => {
        typed('c8 d‸ e f', '[', 'c8 d[‸ e] f');
    });
});

describe('octave marks (rules 14–15)', () => {
    it('14. a mark at either end of a note moves into its slot, caret unmoved', () => {
        typed('‸c4', "'", '‸c\'4');
        typed('c4‸', "'", 'c\'4‸');
        typed('c4‸', ',', 'c,4‸');
    });
    it('14. a mark already in its slot is typed as pressed', () => {
        typed('c‸4', "'", 'c\'‸4');
    });
    it('14. marks stack from wherever the caret rests', () => {
        typed('c\'4‸', "'", 'c\'\'4‸');
    });
    it('15. the opposite mark cancels one', () => {
        typed('c\'‸4', ',', 'c‸4');
        typed('c,4‸', "'", 'c4‸');
    });
    it('a rest takes no octave mark', () => {
        typed('r4‸', "'", 'r4\'‸');
    });
});

describe('durations (rules 16–18a, 25, 28)', () => {
    it('16. a digit anywhere on a note goes after the octave marks, caret unmoved', () => {
        typed('‸c', '4', '‸c4');
        typed('‸c,', '4', '‸c,4');
        typed('c‸4', '2', 'c‸2');
    });
    it('16. a digit already in its slot is typed as pressed', () => {
        typed('c,‸', '4', 'c,4‸');
    });
    it('17. right after the digits the keystroke extends them only into a valid duration', () => {
        typed('c1‸', '6', 'c16‸');
        typed('c1‸', '2', 'c2‸');                  // 12 is no duration: replaced, not kept for 128
        typed('c1‸.', '2', 'c2‸.');
        typed('c4‸', '8', 'c8‸');
        typed('c16‸', '1', 'c1‸');
        typed('c12‸', '8', 'c128‸');               // a 12 already on the page is finished
    });
    it('18. elsewhere on the note, 1/2/4/8 start afresh and 3/6 extend', () => {
        typed('c1.‸', '2', 'c2.‸');
        typed('c1.‸', '6', 'c16.‸');
        typed('c‸1', '6', 'c‸16');
        typed('c12.‸', '8', 'c8.‸');
    });
    it('18a. a 3 or 6 that extends into nothing is completed, wherever the caret is', () => {
        typed('c1.‸', '3', 'c32.‸');
        typed('c2.‸', '6', 'c64.‸');
        typed('c4‸', '6', 'c64‸');
        typed('c1‸', '3', 'c32‸');
        typed('c‸', '3', 'c32‸');
    });
    it('18a. retyping the same duration changes nothing', () => {
        typed('c1.‸', '1', 'c1.‸');
        typed('c4‸', '4', 'c4‸');
        typed('c32‸', '3', 'c32‸');
    });
    it('18b. a digit that starts no duration leaves the note unchanged', () => {
        typed('c4‸', '5', 'c4‸');
        typed('c‸4', '7', 'c‸4');
        typed('c‸4', '0', 'c‸4');
        typed('r2‸', '9', 'r2‸');
        typed('c‸4\\', '0', 'c‸4\\');              // not a string number either
    });
    it('18b. off a note those digits are ordinary typing', () => {
        typed('time ‸', '5', 'time 5‸');
        typed('c4@finger(‸)', '5', 'c4@finger(5‸)');
        typed('R1*‸', '5', 'R1*5‸');
    });
    it('25. a digit on a note whose \\ is waiting takes the string number', () => {
        typed('c‸4\\', '3', 'c‸4\\3');
        typed('c4\\‸', '3', 'c4\\3‸');            // right after the \: ordinary typing
    });
    it('28. a digit among the note\'s marks is still typed on the note', () => {
        typed('c8\\8(‸[', '4', 'c4\\8(‸[');
    });
    it('28. inside an annotation\'s arguments a digit is the argument', () => {
        typed('c4@figuredBass(6‸)', '4', 'c4@figuredBass(64‸)');
    });
    it('a digit in a header line is typed as pressed', () => {
        typed('time 4/‸', '4', 'time 4/4‸');
    });
});

// THE DIGIT TABLE: every event that takes a duration, and what each digit
// leaves on it, the caret right after the digits (rule 17), elsewhere on it
// (rule 18) and at its start (rule 16). Every result is a valid duration or the
// note untouched (rule 18b).
describe('the digit table (rules 16–18b) over notes, rests, chords and slash notes', () => {
    const cases: [string, string, string][] = [
        // right after the digits
        ['c1‸', '6', 'c16‸'], ['c1‸', '2', 'c2‸'], ['c1‸', '4', 'c4‸'], ['c1‸', '8', 'c8‸'],
        ['c1‸', '3', 'c32‸'], ['c1‸', '1', 'c1‸'], ['c1‸', '5', 'c1‸'],
        ['c2‸', '1', 'c1‸'], ['c2‸', '6', 'c64‸'], ['c4‸', '5', 'c4‸'], ['c4‸', '6', 'c64‸'],
        ['c6‸', '4', 'c64‸'], ['c3‸', '2', 'c32‸'], ['c8‸', '6', 'c64‸'], ['c16‸', '8', 'c8‸'],
        ['c64‸', '2', 'c2‸'], ['c‸', '8', 'c8‸'], ['c‸', '6', 'c64‸'], ['c,‸', '9', 'c,‸'],
        // elsewhere on the note
        ['c‸4', '8', 'c‸8'], ['‸c4.', '2', '‸c2.'], ['c\'‸4', '6', 'c\'‸64'], ['c‸1', '6', 'c‸16'],
        // rests
        ['r4‸', '8', 'r8‸'], ['r1‸', '2', 'r2‸'], ['R1‸', '6', 'R16‸'], ['s4‸', '0', 's4‸'],
        // chords, at their ends
        ['<c e>4‸', '8', '<c e>8‸'], ['<c e>1‸', '2', '<c e>2‸'], ['‸<c e>4', '7', '‸<c e>4'],
        ['<c e>4‸', '6', '<c e>64‸'],
        // slash notes (rule 3 of the review: `/4|` + '8' used to give `/48`)
        ['/4‸', '8', '/8‸'], ['/1‸', '6', '/16‸'], ['/1‸', '2', '/2‸'], ['/4‸', '5', '/4‸'],
        ['/‸', '4', '/4‸'], ['‸/4.', '2', '‸/2.'], ['c4 /4‸ d', '6', 'c4 /64‸ d'],
        ['/4~‸', '8', '/8~‸'],
    ];
    for (const [before, key, after] of cases) {
        it(`${before} + ${key} → ${after}`, () => typed(before, key, after));
    }
});

describe('slash notes', () => {
    it('take dots, ties and beams like a note', () => {
        typed('‸/4', '.', '‸/4.');
        typed('/‸2 /', '~', '/2~‸ /');
        typed('/8‸ /', '[', '/8[‸ /]');
    });
    it('take no octave mark and no string number — those are typed as pressed', () => {
        typed('/4‸', "'", '/4\'‸');
        typed('/4‸', '\\', '/4\\‸');
    });
    it('only a slash that stands alone is one: tuplet ratios and chord slashes are not', () => {
        typed('tuplet 3/‸', '8', 'tuplet 3/8‸');
        typed('tuplet 3/2‸', '4', 'tuplet 3/24‸');
        typed('c/g‸', '8', 'c/g8‸');
        typed('time 3/4‸', '8', 'time 3/48‸');
    });
});

describe('several carets (rule 30)', () => {
    const multi = (before: string, key: string, after: string) =>
        assert.equal(showMulti(typeKeyAtCarets(before, key)), after, `${JSON.stringify(before)} + ${key}`);
    it('each caret gets its own aid, on its own note', () => {
        multi('c4‸ d4‸ e4‸', '8', 'c8‸ d8‸ e8‸');
        multi('c1‸ d2‸ /4‸', '6', 'c16‸ d64‸ /64‸');
        multi('c4‸ d1‸ /4‸', '6', 'c64‸ d16‸ /64‸');   // the site's example
        multi('c1‸ d4‸', '2', 'c2‸ d2‸');
        multi('‸c4 ‸d4', "'", '‸c\'4 ‸d\'4');
        multi('c4‸ d8.‸', '.', 'c4.‸ d8..‸');
    });
    it('a caret with no aid gets the key as pressed, the others their aid', () => {
        multi('c4‸ time ‸', '8', 'c8‸ time 8‸');
        multi('c4‸ d4‸', '5', 'c4‸ d4‸');
        multi('c4‸ time ‸', '5', 'c4‸ time 5‸');
    });
    it('carets shift with the rewrites before them', () => {
        multi('c‸ d‸ e‸', '3', 'c32‸ d32‸ e32‸');
        multi('c32‸ d‸', '8', 'c8‸ d8‸');
    });
    it('two carets on one note have no single answer: the key is typed as pressed', () => {
        multi('‸c‸4', '8', '8‸c8‸4');
    });
    it('the result is one set of edits — one undo step', () => {
        const { text, carets } = parseMulti('c4‸ d4‸');
        const plan = multiCursorPlan('8', text, carets)!;
        assert.equal(plan.intercepted.length, 2);
        assert.equal(plan.afterKeystroke.length, 2);
    });
});

describe('the typingAids.enabled setting (rule 31)', () => {
    const root = path.join(__dirname, '..', '..');
    const pkg = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8'));
    it('is declared, on by default', () => {
        const setting = pkg.contributes.configuration.properties['lilysharp.typingAids.enabled'];
        assert.equal(setting?.type, 'boolean');
        assert.equal(setting?.default, true);
    });
    it('holds off every intercepted key', () => {
        const bindings = pkg.contributes.keybindings.filter((b: { command: string }) => b.command === 'lilysharp.smartType');
        assert.equal(bindings.length, 14);
        for (const b of bindings) { assert.match(b.when, /config\.lilysharp\.typingAids\.enabled/); }
    });
    it('holds off the change-event route and the command', () => {
        const source = fs.readFileSync(path.join(root, 'src', 'smartTyping.ts'), 'utf8');
        assert.match(source, /if \(applyingFix \|\| !aidsEnabled\) \{ return; \}/);
        assert.match(source, /\|\| !aidsEnabled\s*\n/);
        assert.match(source, /affectsConfiguration\('lilysharp\.typingAids\.enabled'\)/);
    });
});

describe('dots (rule 23)', () => {
    it('a dot anywhere on a note goes to the end of its core, caret unmoved', () => {
        typed('c‸\'8.', '.', 'c‸\'8..');
        typed('‸r4', '.', '‸r4.');
    });
    it('a dot already in its slot is typed as pressed', () => {
        typed('r4‸', '.', 'r4.‸');
    });
    it('after an annotation a dot is the .up placement, not an augmentation', () => {
        typed('c4@text("x")‸', '.', 'c4@text("x").‸');
    });
});

describe('string numbers (rules 24–24a, 29)', () => {
    it('24. \\ anywhere on a note opens its slot, the caret following', () => {
        typed('‸a4', '\\', 'a4\\‸');
        typed('a‸4', '\\', 'a4\\‸');
    });
    it('24. the slot is directly after the core, before the marks', () => {
        typed('‸c4( d)', '\\', 'c4\\‸( d)');
    });
    it('24a. a note already numbered gets its digit selected, nothing inserted', () => {
        typed('a4\\3‸', '\\', 'a4\\‸«3»');
        typed('g\')\\2‸', '\\', 'g\')\\‸«2»');   // the \N may follow a mark
    });
    it('24a. a \\ still waiting for its digit absorbs the keystroke', () => {
        typed('a4\\‸', '\\', 'a4\\‸');
    });
    it('a rest plays no string', () => {
        typed('r4‸', '\\', 'r4\\‸');
    });
    it('29. inside a chord the \\ is the member\'s under the caret', () => {
        typed('<‸c e>4', '\\', '<c\\‸ e>4');
        typed('<c‸ e>4', '\\', '<c\\‸ e>4');
    });
    it('29. at the chord\'s ends it is typed as pressed', () => {
        typed('‸<c e>4', '\\', '\\‸<c e>4');
    });
});

describe('annotations (rule 27, 29)', () => {
    it('27. @ on a note lands after the string number, before the marks', () => {
        typed('c4~‸', '@', 'c4@‸~');
        typed('c8\\8(‸', '@', 'c8\\8@‸(');
    });
    it('27. between events the @ stays where it was typed', () => {
        typed('c4 ‸ d', '@', 'c4 @‸ d');
    });
    it('29. inside a chord the @ is the member\'s under the caret', () => {
        typed('<c‸\\3 e>', '@', '<c\\3@‸ e>');
        typed('<c\\3‸ e>', '@', '<c\\3@‸ e>');
    });
});

describe('the canonical order of a note\'s marks (rule 26)', () => {
    it('a [ on a note that already opens a slur goes inside the (, before the ~', () => {
        typed('c8(~‸ d', '[', 'c8([‸~ d]');
    });
    it('a ( on a note whose marks include a [ goes before the [', () => {
        typed('a8[‸ b', '(', 'a8(‸[ b)');
    });
    it('what ends on a note is written before what begins on it', () => {
        typed('c4( d)‸ e f', '(', 'c4( d)(‸ e) f');
    });
});

describe('composeFix', () => {
    it('rebuilds one span with every edit applied, the insert before the delete at a shared offset', () => {
        const c = composeFix('c4 d', [{ at: 2, del: 1 }, { at: 2, ins: '(' }], 3);
        assert.deepEqual(c, { lo: 2, hi: 3, out: '(', caretIn: 1, selectLen: 0 });
    });
    it('grows the span to reach a caret outside it, copying the text on the way', () => {
        const c = composeFix('c4 d)', [{ at: 2, ins: '(' }], 6);
        assert.deepEqual(c, { lo: 2, hi: 5, out: '( d)', caretIn: 4, selectLen: 0 });
        const back = composeFix('c4', [{ at: 1, ins: "'" }], 0);
        assert.deepEqual(back, { lo: 0, hi: 1, out: "c'", caretIn: 0, selectLen: 0 });
    });
});

describe('stayPut', () => {
    it('a caret before the rewritten run does not move', () => {
        assert.equal(stayPut(1, 3, 1, 2), 1);
        assert.equal(stayPut(3, 3, 1, 2), 3);
    });
    it('a caret past the run shifts by the length change', () => {
        assert.equal(stayPut(6, 3, 1, 2), 7);
        assert.equal(stayPut(6, 3, 2, 0), 4);
    });
    it('a caret inside the run is clamped to the rewritten run', () => {
        assert.equal(stayPut(4, 3, 2, 1), 4);
        assert.equal(stayPut(5, 3, 3, 1), 4);
    });
});
