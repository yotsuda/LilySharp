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

// The DECIDING half of smart typing: everything a keystroke does to a `.lys`
// document, worked out on a plain string and an offset — what to insert or
// remove where, and where the caret ends up. Nothing here touches an editor,
// so the whole of it runs, and is tested, under plain node
// (`npm test` → test/smartTypingCore.test.ts). smartTyping.ts is the other
// half: it hears the keystrokes from VS Code, asks here, and carries the
// answer out.
//
// ⚠️ This file must not import 'vscode'. The test checks that it does not —
// that is the one property everything below depends on.
//
// The rules, numbered as the tests cite them:
//
// Smart angle-bracket typing — keeps a chord's/arpeggio's two ends in sync as
// the user edits ONE of them:
//
// 1. Typing '<' directly before a note wraps that one note (`c4` → `<c>4`,
//    durations live after the '>') and puts the caret after the note so typing
//    continues the chord. A '<' typed in whitespace keeps the plain `<>` pair.
// 2. '<'  → '<<' : the measure's matching '>'  becomes '>>'.
// 3. '<<' → '<'  : the measure's matching '>>' becomes '>'.
// 4. '>'  → '>>' : the measure's matching '<'  becomes '<<'.
// 5. '>>' → '>'  : the measure's matching '<<' becomes '<'.
// 6. When the measure AHEAD already holds an unresolved '>', a typed '<' stays
//    bare — the note wrap is suppressed and an auto-closed `<>` pair has its
//    '>' removed — so the '<' pairs with the existing close.
// 7. Typing a lone '>' with NO unresolved '<' before it in the measure
//    auto-opens: the pitch directly before is wrapped (`c` + '>' → `<c>`),
//    else an empty `<>` forms. With an unresolved '<' the '>' simply closes
//    it, and nothing is added.
//
// Smart slur typing, on the same principle — a typed '(' reaches for what the
// slur will actually cover instead of closing on itself:
//
// 8. Typing '(' after a note puts the ')' after the FOLLOWING note event
//    (`c4( d e` → `c4( d) e`), so the shortest real slur is one keystroke and
//    widening it means dragging one ')' forward. Which note the slur STARTS on
//    is read off the caret: a '(' glued to the note AHEAD of it points at that
//    note and moves just past it (`c |d e` → `c d( e)`), while one glued to the
//    note behind — or spaced from both — stays put (`c| d e` → `c( d) e`). The
//    pair always covers two notes; the caret picks which two.
// 9. When an unresolved ')' already lies ahead, the '(' stays bare — it pairs
//    with that existing end (an auto-closed `()` has its ')' removed), which is
//    how a slur's range is WIDENED: retype the '(' earlier.
// 10. With no note event ahead — end of the block, or anything the scan cannot
//    positively identify as a note — the plain `()` pair is left as typed.
// 11. Typing ')' mirrors it: the '(' goes after the note BEFORE the one the ')'
//    closes on (`c4 d` + ')' → `c4( d)`), so the shortest automatic slur spans
//    two notes whichever end is typed first.
// 12. An unresolved '(' before the typed ')' means it simply closes that, and
//    nothing is added — which is also what keeps `@finger(3)` intact.
// 13. When a slur already ends on that preceding note, the typed ')' EXTENDS it
//    by one note (`c4( d) e` + ')' → `c4( d e)`) instead of opening a second
//    one. A slur that ended further back is left alone and a new slur starts,
//    so both readings stay reachable: ')' extends, '(' begins.
//
// 19. A tie is the same mark-after-its-note shape with no second note to find,
//    so '~' typed anywhere ON a note moves to that note's end (`|c2` + '~' →
//    `c2~`). Which note it ties TO is whatever follows, and whether the pitch
//    repeats is the compiler's business (LYS4007), not the editor's.
//
// 20. A manual beam opens the same way and closes the way a slur does — on the
//    FOLLOWING note, not on a run: '[' typed on a note puts the ']' after the
//    next note in the SAME MEASURE that can still be beamed (`c8|` + '[' →
//    `c8[ d] e f`), so the shortest real beam is one keystroke and widening it
//    means dragging one ']' forward — exactly rule 8. (Until 2026-09-03 it ran to
//    the LAST beamable note of the measure, `c8[ d e f]`; owner request.) A
//    beam cannot cross a barline and cannot hold a quarter, a longer note or a
//    rest, so the measure bounds the search, the running duration is carried
//    along (the `d e f` of `c8 d e f` count as eighths even though only the
//    first says so), and a rest is SPANNED (`c8[ r8 d]`) but never closed on.
//    A beam already starting on that next note is extended backwards instead
//    (`c8| d[ e]` + '[' → `c8[ d e]`), rule 8's extension in the other direction.
// 21. ']' mirrors it backwards, as ')' does '(' (rule 11): the '[' goes after
//    the beamable note BEFORE the one the ']' closes on (`c8 d` + ']' →
//    `c8[ d]`), and a beam already ending on that note is EXTENDED by one
//    (`c8[ d] e` + ']' → `c8[ d e]`, rule 13) instead of opening a second one.
// 22. A note that cannot carry a beam, or one with nothing beamable beside it,
//    is left as typed — as is a '[' that is not on a note, which is what an
//    inline volta's `[1.` is.
//
// Smart octave marks, on the same reading of the caret:
//
// 14. "'" or ',' typed at either END of a note (`|c4`, `c4|`) moves into the
//    slot the pitch actually takes it in — between the pitch letters and the
//    duration (`c'4`), or after a chord's '>'. Typed where it already belongs
//    it is simply left alone.
// 15. Typed against the OPPOSITE mark it CANCELS one of them ("'" undoes a ',',
//    ',' undoes a "'"), so an overshot octave is walked back with the same key
//    that caused it.
//
// Smart durations, read the same way:
//
// 16. A digit typed anywhere on a note moves into the duration slot, which sits
//    AFTER the octave marks (`|c` and `c,|` alike → `c,4`).
// 17. With the caret DIRECTLY AFTER the digits the keystroke lands in the slot by
//    itself and simply EXTENDS them: `c1|` + '6' → `c16`, and `c1|.` + '2' →
//    `c12.`, which is the one place a 128th can still be spelled out. The test is
//    a PREFIX test against 1/2/4/8/16/32/64/128.
// 18. ANYWHERE ELSE on the note it turns on whether the digit IS a duration.
//    1, 2, 4 and 8 are, so they start a FRESH one: `c1.|` + '2' is `c2.`, a
//    dotted half, and not the `c12.` that reading it as a 128th in the making
//    would give. 3 and 6 are not — nothing sounds for six — so they can only be
//    building one, and they EXTEND: `c1.|` + '6' is `c16.`.
// 18a. A digit that cannot stand alone and has nothing to extend is COMPLETED to
//    the one duration that begins with it — '3' writes `32` and '6' writes `64`,
//    so `c1.|` + '3' is `c32.` and `c2.|` + '6' is `c64.`. Nothing is guessed:
//    those are the only durations beginning with those digits, and the half-typed
//    `c3` sounds as nothing, so there was never a reason to leave it on the page.
//    ⚠️ Only a digit that is the WHOLE of what gets written is completed. A run
//    being BUILT UP is left alone (`c1|` + '2' stays `c12`, rule 17): finishing
//    it to `c128` would turn the '8' the typist goes on to press into `c8`.
//    DECIDED (user, 2026-08-11): a 128th is rare enough that a caret past the
//    digits is better evidence of a fresh duration than of one being built up,
//    but only where the digit could have meant a fresh duration at all. So the
//    ambiguous keystrokes (1/2/4/8) restart and the unambiguous ones (3/6) do
//    not, and `c1|.` keeps both readings reachable. Retyping the SAME digit
//    changes nothing at all, and a digit that starts no duration (5, 7, 9, 0) is
//    left exactly as typed.
//
// 23. An augmentation dot is the LAST thing on a note's core, after the duration
//    and after the dots already there, so one typed anywhere else on the note
//    moves to the end of that run: `c|'8.` + '.' → `c'8..`. A REST takes dots on
//    the same terms (`r4.`), unlike octave marks.
//
// Smart string numbers (tab), on the same reading of the caret — owner request,
// 2026-09-03:
//
// 24. A '\' typed anywhere ON a note opens that note's string number in its slot
//    — directly after the core, before any `@` annotation or slur/tie/beam mark
//    (`|c4( d)` + '\' → `c4\|( d)`), which is where every `\N` in the language's
//    own tests is spelled; the parser reads the post-events unordered, so the
//    slot is a convention, not a constraint. ⚠️ Unlike every other relocated
//    mark THE CARET FOLLOWS, to just after the '\': a backslash is never the
//    end of what is being typed — the string number comes next, and it has to
//    land after the '\' (owner decision, 2026-09-03: `|a4` + '\' → `a4\|`). A
//    rest plays no string, so on one the '\' is typed as pressed; inside a
//    chord it is the member's (rule 29).
// 24a. When the note ALREADY carries a `\N` — in the slot, or after a mark, as
//    `g')\2` is legal — the keystroke inserts nothing and SELECTS the N, so the
//    next digit typed replaces it: the string is changed with '\' + digit, no
//    backspacing. A '\' still waiting for its digit absorbs the keystroke.
// 25. A digit typed on a note whose '\' is still waiting for its digit is that
//    string number, not a duration: `c|4\` + '3' → `c4\3`, the caret staying
//    put. Rule 24 leaves the caret after the '\' so the common case never
//    comes here; this is for a caret that was moved away and back onto the
//    core, where rule 16 would otherwise take the digit and write `c43\`. 0 is
//    no string number and is typed as pressed.
//
// 26. EVERY mark this module places lands in its note's CANONICAL SLOT, so one
//    spelling reaches the page whatever order the keys were pressed in
//    (owner decision, 2026-09-03 — see POST_EVENT_RANK for the reasoning):
//
//        core  \N  @…  ]  )  (  [  ~
//
//    `c8([ d e f])`, `d4)( e`, `a,4\4~`, `c4)~ c`. A note whose marks are in
//    another order is not rewritten; the new mark goes after the last one that
//    ranks at or below it. The parser reads any order.
// 27. '@' is placed by the same table (owner request, 2026-09-03: "its position
//    was indeterminate"): typed anywhere on a note it lands after the string
//    number and the annotations already there, before the marks, with the
//    caret after it and the name completion re-opened there. It is not
//    intercepted — '@' has no layout-safe key — so it travels through the
//    change-event route, with that route's one-frame flicker.
// 28. A digit or an octave mark typed among a note's marks is still typed ON
//    the note (`c8\8(|[` + '4' → `c4\8([`, the caret staying put; owner
//    request 2026-09-03) — the run is the note's. Inside an annotation's
//    argument list it is the argument and is typed as pressed. The dot keeps
//    to the core: after `@text("x")` a '.' is the `.up` placement.
// 29. Inside a chord, '\' and '@' belong to the MEMBER the caret is on
//    (`<c| e>4` + '\' → `<c\| e>4`; `<c\3 e>` + '@' on the c → `<c\3@| e>`),
//    which is where the parser reads a member's string number and annotations
//    (`<c\3 e\2>`, `<c@finger(1) e>`). At the chord's ends and on a degree
//    member they are typed as pressed.
//
// Rules 14–23 and 25 move TEXT and never the CARET: the mark or digit travels to the
// slot it belongs in and the cursor keeps the position it was pressed at (see
// stayPut), because a correction to what was typed is not a request to go
// somewhere. Octave marks stay reachable from wherever the caret rests and stack
// there (`c4|` + "'" + "'" → `c''4|`), the slot being re-read off the note each
// time rather than remembered from the caret. Durations read the caret once, and
// only to break a tie: right after the digits it always builds on them (rule 17),
// and elsewhere the digit itself decides (rule 18).
//
// A caret INSIDE a chord is pointing at a member (`<c, e g>`, `<c 3 5>` — the
// spaced digits are scale degrees), and one past the note's core is in its
// annotations (`@fig(6,4)`); both are left as typed.
//
// The search for both the following note and the unresolved ')' is bounded by
// the innermost `{ … }` block, so an auto-placed ')' never leaves the part (or
// voice) the '(' was typed in, let alone its section. Unlike chords, a slur DOES
// cross barlines, so measure bounds are not used. A '(' glued to a name is an
// annotation's argument list (`@finger(3)`), never a slur, and is left alone — as is
// one inside a string or a comment.
//
// Deleting a lone '<' or '>' deliberately does NOT delete its partner: an
// orphaned end is how a chord's RANGE is changed (delete the '>', retype it
// after more notes — rules 6/7 then pair the retyped end with the orphan
// instead of adding brackets). Only the '<<'⇄'<' / '>>'⇄'>' promotions watch
// deletions.
//
// "Matching" honors nested chord members (`<< c <e g> … >>`) by depth counting
// and never crosses a measure boundary ('|', '{', '}'). An end that is already
// consistent is left alone, as is anything that would make a triple bracket.

// The note token a typed '<' wraps: a pitch letter with its glued accidental
// letters (cis, des, bd …) and octave marks. The duration and dots are NOT part
// of a chord member — they belong AFTER the closing '>' — so they stay outside:
// typing '<' before `c4` yields `<c>4`.
const NOTE_TOKEN = /^[a-g][a-z]*[',]*/;

// A complete note EVENT as written: a pitch (letter + accidental spelling +
// octave marks) or a rest, plus the duration and dots glued to it. Anchored, and
// every caller checks that a word character does NOT follow, so `clef`, `bass`
// and `rightHand` can never pose as notes.
const NOTE_EVENT = /^(?:[a-g](?:isis|eses|is|es)?[',]*|[rsR])(?:[0-9]+\.*)?/;

// Characters that sit BETWEEN note events — barlines and repeat marks, beam and
// volta brackets, ties, other slur marks, and the bare numbers of a volta or a
// `:|*N` repeat count. A slur spans all of them.
const BETWEEN_EVENTS = /[|:.*[\]~()\-0-9]/;

// The pitch letters an octave mark attaches to: the note name with its glued
// accidental spelling. The marks go after these and BEFORE the duration.
const PITCH_LETTERS = /^[a-g](?:isis|eses|is|es)?/;

// Every DurationBase there is (GRAMMAR.md §Duration). Tested as a PREFIX for
// what a keystroke could still become — '3' is not a duration but it is the
// first keystroke of '32', and '12' is the first two of '128' — and as a MEMBER
// for what one already is. Dots and a ':' tremolo are separate slots.
const DURATIONS = ['1', '2', '4', '8', '16', '32', '64', '128'];
const isDurationPrefix = (digits: string) => DURATIONS.some(d => d.startsWith(digits));
// ⚠️ The two tests disagree on exactly 3 and 6, and that gap is what tells a
// FRESH duration from one being BUILT UP: nothing sounds for six, so a typed '6'
// can only ever be reaching for 16 or 64.
const isDuration = (digits: string) => DURATIONS.includes(digits);

// A mid-music command, which changes context between two notes without being one
// — a slur spans it, so the scan for the following note skips the whole command
// (its operands included, or `key g major` would offer 'g' as the note).
const MID_MUSIC_COMMAND =
    /^(?:clef\s+[A-Za-z_][A-Za-z0-9_]*|key\s+[a-g](?:isis|eses|is|es)?\s+[A-Za-z]+|time\s+[0-9]+\s*\/\s*[0-9]+|partial\s+[0-9]+\.*|octave\s+[A-Za-z0-9]+|break)(?![A-Za-z0-9_])/;

// The insertions this module reacts to — the pairs VS Code auto-closes included.
// Checked BEFORE the document text is read, so an ordinary letter, a space or a
// paste never pays for a full getText().
const SMART_INSERTS = new Set(['<', '<>', '>', '(', '()', ')', "'", ',', '.', '~', '[', '[]', ']', '\\', '@']);

// The durations that carry a flag, and so can be beamed. A note that spells no
// duration inherits the running one — `c8 d e f` beams all four and only the
// first says '8' — so a beam run can only be read with the measure's running
// value carried along.
const BEAMABLE = new Set([8, 16, 32, 64, 128]);
export const isSmartInsert = (t: string) => SMART_INSERTS.has(t) || /^[0-9]$/.test(t);

/** The keys whose keystroke is decided as a TypePlan on the text WITHOUT it —
 * the ones planFor answers for. The rest (the brackets, the tie) are decided on
 * the text WITH the keystroke, by insertionPlan. */
export const isPlannedKey = (t: string) => t.length === 1 && ("',.\\@".includes(t) || /^[0-9]$/.test(t));

/** One replacement in the document as it stands: `del` characters at `at` give
 * way to `ins`. Either half may be absent. */
export interface Edit { at: number, del?: number, ins?: string }

/** What a keystroke decided on the text WITH it does: the edits, in the current
 * text's offsets, and where the caret ends up in the FINISHED text — or no
 * caret at all, when the keystroke's own caret (wherever VS Code left it) is
 * the right one and only the text changes. */
export interface FixPlan { edits: Edit[], caret?: number, what: string }

/** What a typed octave mark or duration digit does to the text, decided ENTIRELY
 * on the text as it stands WITHOUT that keystroke.
 *
 * That framing is what lets one decision serve both routes into this module. The
 * `type` interception has the pre-keystroke text in hand already; the
 * onDidChangeTextDocument fallback reconstructs it. Neither can be decided on the
 * text WITH the keystroke in it — a mark parked mid-note splits the very token
 * that has to be read (`c|2` + "'" reads as `c`, `'`, `2` and hides the note
 * `c2`) — so both want the same view, and now compute the same answer from it.
 *
 * `at`/`del`/`ins` replace one span; `caret` is the finished text's offset for
 * the cursor. A plan whose `ins` equals what it replaces is a NO-OP: the
 * keystroke is absorbed and nothing is edited at all — unless it carries a
 * `select`, the number of characters from `caret` to leave SELECTED, which is
 * how a keystroke that changes no text still offers something to retype
 * (rule 24a). */
export interface TypePlan { at: number, del: number, ins: string, caret: number, what: string, select?: number }

/** The characters whose keys are intercepted, mapped to what they plan — and
 * '@', which is not intercepted but is decided the same way (rule 27). */
export function planFor(ch: string, before: string, offset: number): TypePlan | null {
    if (ch === "'" || ch === ',') { return octaveMarkPlan(before, offset, ch); }
    if (ch === '.') { return dotPlan(before, offset); }
    if (ch === '\\') { return stringNumberPlan(before, offset); }
    if (ch === '@') { return annotationPlan(before, offset); }
    if (/^[0-9]$/.test(ch)) { return durationPlan(before, offset, ch); }
    return null;
}

/** What an insertion of `typed` at `offset` does, the document ALREADY holding
 * it — the change-event route's dispatch, for the keys that are decided on the
 * text with the keystroke in it: the chord brackets, the slur and beam marks
 * and the tie, each alone or as the pair VS Code auto-closed it into. */
export function insertionPlan(typed: string, text: string, offset: number): FixPlan | null {
    switch (typed) {
        case '<': return chordOpenPlan(text, offset);
        case '<>': return chordAutoClosePlan(text, offset);
        case '>': return chordClosePlan(text, offset);
        case '(': return slurOpenPlan(text, offset, false);
        case '()': return slurOpenPlan(text, offset, true);
        case ')': return slurClosePlan(text, offset);
        case '~': return tiePlan(text, offset);
        case '[': return beamOpenPlan(text, offset, false);
        case '[]': return beamOpenPlan(text, offset, true);
        case ']': return beamClosePlan(text, offset);
        default: return null;
    }
}

/** What a one-character deletion at `offset` does — `deleted` is the character
 * that went, `text` the document without it and `oldText` the document as it
 * was. Only the chord brackets watch deletions (rules 3 and 5). */
export function deletionPlan(deleted: string, text: string, oldText: string, offset: number): FixPlan | null {
    if (deleted === '<') { return chordDeleteOpenPlan(text, oldText, offset); }
    if (deleted === '>') { return chordDeleteClosePlan(text, oldText, offset); }
    return null;
}

/** The one span [lo, hi) of `text` that carries out `edits`, rebuilt as `out`,
 * with the caret (and a selection of `selectLen` characters from it) located
 * INSIDE that span — the arithmetic behind the single snippet insertion that
 * applies a fix and places the caret in one operation.
 *
 * `edits` and `caret` are offsets into the document as it stands NOW, except
 * that `caret` is where the caret should end up in the FINISHED text. Every
 * position below `lo` is untouched by definition, so the two agree there and
 * the tabstop can be placed by simple subtraction. */
export interface ComposedFix { lo: number, hi: number, out: string, caretIn: number, selectLen: number }

export function composeFix(text: string, edits: Edit[], caret: number, selectLen = 0): ComposedFix {
    let lo = Infinity;
    let hi = -Infinity;
    for (const e of edits) {
        lo = Math.min(lo, e.at);
        hi = Math.max(hi, e.at + (e.del ?? 0));
    }
    // The span [lo, hi) rebuilt with every edit applied — one replacement, so
    // the edits can never half-land or overlap. Where a delete and an insert
    // share an offset — which is every keystroke replaced in place — the INSERT
    // goes first: taking the deletion first would step the cursor past the text
    // the insert then copies again, emitting it twice.
    const ordered = [...edits].sort((a, b) =>
        a.at - b.at || (a.ins ? 0 : 1) - (b.ins ? 0 : 1));
    let out = '';
    let i = lo;
    for (const e of ordered) {
        out += text.slice(i, e.at);
        if (e.ins) { out += e.ins; }
        i = Math.max(i, e.at + (e.del ?? 0));
    }
    out += text.slice(i, hi);

    // A tabstop can only be placed INSIDE the replaced span, and the caret is
    // often outside it — an inserted '(' three notes back leaves a one-character
    // span and a caret at the far end. Grow the span until it reaches, copying
    // the text on the way; unchanged text costs nothing to replace with itself.
    let caretIn = caret - lo;
    while (caretIn + selectLen > out.length && hi < text.length) { out += text[hi]; hi++; }
    while (caretIn < 0 && lo > 0) { lo--; out = text[lo] + out; caretIn++; }
    caretIn = Math.max(0, Math.min(out.length, caretIn));
    selectLen = Math.min(selectLen, out.length - caretIn);
    return { lo, hi, out, caretIn, selectLen };
}

/** The single replacement that carries out `plan` — decided on the text WITHOUT
 * the keystroke — on a document that ALREADY holds it at `offset`: the fallback
 * route, used when the key was not intercepted. The typed character is removed
 * and the plan's span rewritten in ONE replacement, so the two can overlap
 * freely (a digit typed inside the digit run is exactly that case). */
export function afterKeystrokeEdit(text: string, offset: number, plan: TypePlan): Edit {
    const lo = Math.min(plan.at, offset);
    const hi = Math.max(plan.at + plan.del, offset);
    const before = text.slice(0, offset) + text.slice(offset + 1);
    const replacement =
        before.slice(lo, plan.at) + plan.ins + before.slice(plan.at + plan.del, hi);
    return { at: lo, del: hi + 1 - lo, ins: replacement };
}

/** What carrying out `plan` on a document that does NOT hold the keystroke
 * comes to — the intercepted route. Nothing was inserted at the cursor, so
 * there is no keystroke to take back out; a plan that changes no text is
 * either a selection (rule 24a) or absorbed outright. */
export type TypedKeyOutcome =
    | { kind: 'select', caret: number, select: number }
    | { kind: 'absorbed' }
    | { kind: 'fix', edits: Edit[], caret: number, select: number };

export function typedKeyOutcome(before: string, offset: number, plan: TypePlan): TypedKeyOutcome {
    if (plan.ins === before.slice(plan.at, plan.at + plan.del)) {
        if (plan.select) { return { kind: 'select', caret: plan.caret, select: plan.select }; }
        if (plan.caret === offset) { return { kind: 'absorbed' }; }
    }
    return {
        kind: 'fix',
        edits: [{ at: plan.at, del: plan.del, ins: plan.ins }],
        caret: plan.caret, select: plan.select ?? 0,
    };
}

/** Where the caret belongs when a keystroke was RELOCATED rather than accepted
 * where it was pressed: exactly where the typist left it.
 *
 * The marks that travel — an octave mark into its slot, a digit into the
 * duration — are corrections to the TEXT, not requests to move the cursor, and
 * moving it makes the editor feel like it is typing back. So the caret keeps the
 * position it had BEFORE the keystroke (`at`, in the text without it), adjusted
 * only for characters the rewrite added or removed IN FRONT of it: text sliding
 * under a cursor is not the cursor moving.
 *
 * `spanAt` is where the rewritten run starts, `oldLen`/`newLen` its length before
 * and after — all in the pre-keystroke text, which is what every caller computes
 * in. A caret at or before the run's start does not move; one past its end shifts
 * by the length change; one INSIDE it is clamped to the rewritten run, which is
 * as close to "unmoved" as a caret in text that no longer exists can be.
 *
 * ⚠️ A keystroke that ALREADY lands in its slot never reaches here — those return
 * early and are typed the ordinary way, carrying the caret past the character as
 * typing does. `c1|` + '6' → `c16|` is that case; `c1.|` + '2' → `c2.|` is this
 * one, where the digit travelled and the caret did not. */
export function stayPut(at: number, spanAt: number, oldLen: number, newLen: number): number {
    if (at <= spanAt) { return at; }
    if (at >= spanAt + oldLen) { return at + newLen - oldLen; }
    return Math.min(at, spanAt + newLen);
}

/** The measure bounds around `at`: (start, end) offsets between '|'/'{'/'}'. */
function measureBounds(text: string, at: number): [number, number] {
    let start = 0;
    for (let i = at - 1; i >= 0; i--) {
        const c = text[i];
        if (c === '|' || c === '{' || c === '}') { start = i + 1; break; }
    }
    let end = text.length;
    for (let i = at; i < text.length; i++) {
        const c = text[i];
        if (c === '|' || c === '{' || c === '}') { end = i; break; }
    }
    return [start, end];
}

/** True when the measure span [from, end) holds a '>' that no '<' inside the
 * span opens — an unresolved close a just-typed '<' will pair with, so no
 * automatic '>' should be added for it. */
function hasUnresolvedClose(text: string, from: number, end: number): boolean {
    let depth = 0;
    for (let i = from; i < end; i++) {
        const c = text[i];
        if (c === '<') { depth++; }
        else if (c === '>') {
            if (depth === 0) { return true; }
            depth--;
        }
    }
    return false;
}

/** The offset of the matching close ('>') for an opener whose body starts at
 * `from`, skipping nested '<…>' members by depth; -1 when the measure has none. */
function findClose(text: string, from: number, end: number): number {
    let depth = 0;
    for (let i = from; i < end; i++) {
        const c = text[i];
        if (c === '<') { depth++; }
        else if (c === '>') {
            if (depth > 0) { depth--; }
            else { return i; }
        }
    }
    return -1;
}

/** The offset of the matching opener ('<') for a close whose body ends at
 * `from` (exclusive), skipping nested members; -1 when the measure has none. */
function findOpen(text: string, from: number, start: number): number {
    let depth = 0;
    for (let i = from - 1; i >= start; i--) {
        const c = text[i];
        if (c === '>') { depth++; }
        else if (c === '<') {
            if (depth > 0) { depth--; }
            else { return i; }
        }
    }
    return -1;
}

/** The innermost `{ … }` block around `at`, as (start, end) offsets. A slur
 * closes inside the part/voice block it opened in — which is nested in the
 * section, so the bound is at least as tight as "the same section" everywhere. */
function blockBounds(text: string, at: number): [number, number] {
    let start = 0;
    for (let i = at - 1, depth = 0; i >= 0; i--) {
        const c = text[i];
        if (c === '}') { depth++; }
        else if (c === '{') {
            if (depth === 0) { start = i + 1; break; }
            depth--;
        }
    }
    let end = text.length;
    for (let i = at, depth = 0; i < text.length; i++) {
        const c = text[i];
        if (c === '{') { depth++; }
        else if (c === '}') {
            if (depth === 0) { end = i; break; }
            depth--;
        }
    }
    return [start, end];
}

/** The offset of the first ')' in [from, end) that no '(' inside the span opens
 * — the end of the slur that is already open at `from`, and the one a just-typed
 * '(' would pair with. -1 when there is none. An annotation's `(args)` is
 * balanced, so it neither hides nor fakes one. */
function unresolvedSlurClose(text: string, from: number, end: number): number {
    let depth = 0;
    for (let i = from; i < end; i++) {
        const c = text[i];
        if (c === '(') { depth++; }
        else if (c === ')') {
            if (depth === 0) { return i; }
            depth--;
        }
    }
    return -1;
}

const hasUnresolvedSlurClose = (text: string, from: number, end: number) =>
    unresolvedSlurClose(text, from, end) >= 0;

/** Past the `@annotation`s glued to the note ending at `i` — name, any
 * `(args)`, and a `.up` / `.down` placement suffix — and past a tab's `\N`
 * string number, which the parser reads in the same post-event list as the
 * `@`s and in any order with them — so a ')' lands after the whole note item
 * rather than between the note and its own markings.
 *
 * ⚠️ The string number was not read here until 2026-09-03 (owner report): in
 * `c\3 d` the walk ended the note at `c`, met `\3` as a BARRIER, and every mark
 * typed on that note — '(' , '[' and their closes — found "no note ahead" and
 * did nothing. The lexer's rule is the one used: a backslash directly followed
 * by a digit 1–9 (Lexer.cs, StringNumber). */
function skipAnnotations(text: string, i: number, end: number): number {
    while (text[i] === '@' || (text[i] === '\\' && /[1-9]/.test(text[i + 1] ?? ''))) {
        i = text[i] === '\\' ? i + 2 : skipAnnotation(text, i, end);
    }
    return i;
}

/** Past ONE `@annotation` starting at `i`: its name, any `(args)` and a
 * `.up` / `.down` placement suffix. */
function skipAnnotation(text: string, i: number, end: number): number {
    let k = i + 1;
    while (k < end && /[A-Za-z0-9_-]/.test(text[k])) { k++; }
    if (text[k] === '(') {
        let depth = 1;
        for (k++; k < end && depth > 0; k++) {
            if (text[k] === '(') { depth++; }
            else if (text[k] === ')') { depth--; }
        }
    }
    const placement = /^\.(?:up|down)/.exec(text.slice(k, end));
    return placement ? k + placement[0].length : k;
}

/** The tab string number glued to the note whose core ends at `from` — the `\`
 * of its `\N` — read across everything the parser lets stand between the note
 * and it: `@` annotations and the slur, tie and beam marks (`g')\2` is legal,
 * the post-events being unordered). `digit` says whether the N is there yet; a
 * bare `\` is one still waiting for it (rules 24a, 25). null when the note has
 * none. Whitespace or anything else ends the note, so the scan is short. */
function stringNumberAt(text: string, from: number): { at: number, digit: boolean } | null {
    const item = postEventRun(text, from).find(e => e.kind === 'string');
    return item ? { at: item.at, digit: item.end - item.at === 2 } : null;
}

/** THE CANONICAL ORDER OF A NOTE'S POST-EVENTS — what every smart key writes, so
 * that one shape reaches the page whatever order the marks were typed in and
 * `\3~`, `)~`, `([` can be searched for (owner decision, 2026-09-03).
 *
 * From the note outward, by how much music the mark spans: the string number
 * and the `@` annotations belong to this note alone; a beam stays inside the
 * bar; a slur spans a phrase; and the tie stands between the two notes it
 * joins, so it goes last, next to its partner. What ENDS on the note is written
 * before what BEGINS on it (`d4)(`), and the brackets nest — slur outside, beam
 * inside — so `c8([ d e f])` reads like parenthesised code:
 *
 *     core  \N  @…  ]  )  (  [  ~
 *
 * The parser reads the post-events unordered (LILYPOND-REF: lily/parser.yy
 * post_events), so this is a writing convention and not a constraint: text in
 * another order is left as it is, and a new mark simply goes after the last
 * item that ranks at or below it — which nudges `c4(~` + '[' to `c4([~`. */
export type PostEventKind = 'string' | 'annotation' | ']' | ')' | '(' | '[' | '~';
export const POST_EVENT_RANK: Record<PostEventKind, number> =
    { string: 0, annotation: 1, ']': 2, ')': 3, '(': 4, '[': 5, '~': 6 };

/** The post-events glued to the note whose core ends at `from`, in source
 * order, each as [at, end). A `\` still waiting for its digit is a 'string'
 * item of length 1. Whitespace or anything else ends the note. */
export function postEventRun(text: string, from: number): { kind: PostEventKind, at: number, end: number }[] {
    const run: { kind: PostEventKind, at: number, end: number }[] = [];
    for (let i = from; i < text.length;) {
        const c = text[i];
        if (c === '\\') {
            const end = /[1-9]/.test(text[i + 1] ?? '') ? i + 2 : i + 1;
            run.push({ kind: 'string', at: i, end });
            i = end;
        } else if (c === '@') {
            const end = skipAnnotation(text, i, text.length);
            run.push({ kind: 'annotation', at: i, end });
            i = end;
        } else if (c === '(' || c === ')' || c === '~' || c === '[' || c === ']') {
            run.push({ kind: c, at: i, end: i + 1 });
            i++;
        } else { break; }
    }
    return run;
}

/** Where a new post-event of `kind` goes on the note whose core ends at
 * `coreEnd`: after the last mark already there that ranks at or below it, so
 * the run stays in the canonical order. */
export function postEventSlot(text: string, coreEnd: number, kind: PostEventKind): number {
    let slot = coreEnd;
    for (const item of postEventRun(text, coreEnd)) {
        if (POST_EVENT_RANK[item.kind] <= POST_EVENT_RANK[kind]) { slot = item.end; }
    }
    return slot;
}

/** The mark of `kind` already on the note whose core ends at `coreEnd`, or
 * undefined — the question "does this note already open a slur?" asked of the
 * run rather than of the one character after the note, where it used to be
 * asked and where a `~` or a `\3` in front of the '(' hid it. */
function postEventOn(text: string, coreEnd: number, kind: PostEventKind) {
    return postEventRun(text, coreEnd).find(e => e.kind === kind);
}

/** Where the core of the note event [start, end) ends — the slot the
 * post-events hang off. Falls back to the event's end for anything noteSlots
 * cannot read. */
function coreEndOf(text: string, event: { start: number, end: number }): number {
    return noteSlots(text, event.start, event.end)?.coreEnd ?? event.end;
}

/** The pitch MEMBER of a chord the caret at `offset` is on — inside the `<…>`,
 * on the letters, the octave marks or the member's own glued `@`s and `\N` — as
 * the offset where that member's core (letters + octave marks) ends; null when
 * the caret is not inside a chord, or is on a degree (`<c 3 5>`), a space, the
 * brackets, or inside a member annotation's argument list. A chord's members
 * carry their own string number and annotations (the parser's chord_body
 * post-events: `<c\3 e\2>`, `<c@finger(1) e>`), so a '\' or '@' typed on one
 * belongs to that member and not to the chord (rule 29). */
function chordMemberAtCaret(text: string, offset: number): number | null {
    const [blockStart, blockEnd] = blockBounds(text, offset);
    for (const event of musicEvents(text, blockStart, blockEnd)) {
        if (!event.note || postEventEnd(text, event) < offset) { continue; }
        if (event.start > offset || text[event.start] !== '<') { return null; }
        const arpeggio = text[event.start + 1] === '<';
        const bodyStart = event.start + (arpeggio ? 2 : 1);
        let close = bodyStart;
        for (let depth = 0; close < event.end; close++) {
            if (text[close] === '<') { depth++; }
            else if (text[close] === '>') {
                if (depth === 0) { break; }
                depth--;
            }
        }
        for (let k = bodyStart; k < close;) {
            if (/\s/.test(text[k])) { k++; continue; }
            const m = PITCH_LETTERS.exec(text.slice(k, close));
            if (!m) {
                while (k < close && !/\s/.test(text[k])) { k++; } // a degree, a nested chord
                continue;
            }
            let core = k + m[0].length;
            while (core < close && (text[core] === "'" || text[core] === ',')) { core++; }
            const run = postEventRun(text, core);
            const end = run.length > 0 ? run[run.length - 1].end : core;
            if (offset >= k && offset <= end) {
                return insideAnnotationArgs(text, core, offset) ? null : core;
            }
            k = Math.max(end, k + 1);
        }
        return null;
    }
    return null;
}

/** Where the note event's glued post-events end — the far end of what a caret
 * can be "on" for that note. musicEvents ends an event after its `@`s and `\N`
 * only; the slur, tie and beam marks are read here. */
function postEventEnd(text: string, event: { start: number, end: number }): number {
    const run = postEventRun(text, coreEndOf(text, event));
    return run.length > 0 ? run[run.length - 1].end : event.end;
}

/** One step of a music walk: a note EVENT with its offsets, or a BARRIER —
 * anything the scan cannot positively identify as a note (a keyword, a phrase
 * reference, a nested block, an unclosed chord). Trivia a slur spans — barlines,
 * beams, ties, other slur marks, comments, mid-music commands — is not reported. */
type MusicEvent = { note: true, start: number, end: number } | { note: false };

/** Past a nested `{ … }` (a tuplet's, grace's or repeat's body), which the walk
 * treats as one opaque item rather than descending into it. */
function skipBlock(text: string, i: number, end: number): number {
    for (let depth = 0; i < end; i++) {
        if (text[i] === '{') { depth++; }
        else if (text[i] === '}') {
            depth--;
            if (depth === 0) { return i + 1; }
        }
    }
    return end;
}

/** Walks the music in [from, end), yielding each note event — a pitch/rest or a
 * chord/arpeggio with its duration, trailing octave marks and annotations — and
 * a barrier for everything else. Both slur ends read this one walk, so they
 * agree on what a "note" is and on what they refuse to reach across. */
function* musicEvents(text: string, from: number, end: number): Generator<MusicEvent> {
    let i = from;
    while (i < end) {
        const c = text[i];
        if (/\s/.test(c) || BETWEEN_EVENTS.test(c)) { i++; continue; }
        if (c === '/' && text[i + 1] === '/') {
            while (i < end && text[i] !== '\n') { i++; }
            continue;
        }
        if (c === '/' && text[i + 1] === '*') {
            for (i += 2; i < end && !(text[i] === '*' && text[i + 1] === '/'); i++) { /* skip */ }
            i += 2;
            continue;
        }
        if (c === '<') {
            // A chord `<c e g>4` or an arpeggio `<< c e g >>2`; members may nest.
            const arpeggio = text[i + 1] === '<';
            let j = i + (arpeggio ? 2 : 1);
            for (let depth = 0; j < end; j++) {
                if (text[j] === '<') { depth++; }
                else if (text[j] === '>') {
                    if (depth === 0) { break; }
                    depth--;
                }
            }
            if (j >= end) { yield { note: false }; return; } // unclosed
            j++;                                                        // past '>'
            if (arpeggio && text[j] === '>') { j++; }
            while (j < end && /[',]/.test(text[j])) { j++; }             // octave marks
            while (j < end && /[0-9.]/.test(text[j])) { j++; }           // duration + dots
            const chordEnd = skipAnnotations(text, j, end);
            yield { note: true, start: i, end: chordEnd };
            i = chordEnd;
            continue;
        }
        const rest = text.slice(i, end);
        const command = MID_MUSIC_COMMAND.exec(rest);
        if (command) { i += command[0].length; continue; }
        const m = NOTE_EVENT.exec(rest);
        const after = m ? i + m[0].length : -1;
        // A longer word merely STARTS like a note: `clef`, `bass`, `rightHand`.
        if (m && !(after < end && /[A-Za-z0-9'_,]/.test(text[after]))) {
            const noteEnd = skipAnnotations(text, after, end);
            yield { note: true, start: i, end: noteEnd };
            i = noteEnd;
            continue;
        }
        yield { note: false };
        if (c === '{') { i = skipBlock(text, i, end); }
        else {
            const word = /^[A-Za-z_][A-Za-z0-9_]*/.exec(rest);
            i += word ? word[0].length : 1;
        }
    }
}

/** The first note event in [from, end), or null when a barrier or the block end
 * comes first — so an automatic slur mark is only ever placed on something the
 * walk positively identified. */
function firstNoteEvent(text: string, from: number, end: number)
    : { start: number, end: number } | null {
    for (const event of musicEvents(text, from, end)) {
        return event.note ? { start: event.start, end: event.end } : null;
    }
    return null;
}

/** The note event BEFORE the one a ')' typed at `at` closes on — the note that
 * ')' wants its '(' on. Mirrors firstNoteEvent, so the shortest automatic slur
 * spans two notes whichever end is typed first. A barrier resets the pair,
 * keeping the '(' on this side of anything unrecognized. null = none. */
function precedingNote(text: string, start: number, at: number)
    : { start: number, end: number } | null {
    let beforeLast: { start: number, end: number } | null = null;
    let last: { start: number, end: number } | null = null;
    for (const event of musicEvents(text, start, at)) {
        if (!event.note) { beforeLast = null; last = null; continue; }
        beforeLast = last;
        last = { start: event.start, end: event.end };
    }
    return beforeLast;
}

/** True when [start, at) holds a '(' that no ')' inside the span closes — the
 * slur the typed ')' is there to close. An annotation's still-open `(args)`
 * counts too, which is exactly right: that ')' closes the arguments. */
function hasUnresolvedSlurOpen(text: string, start: number, at: number): boolean {
    return unresolvedSlurOpen(text, start, at) >= 0;
}

/** The offset of the '(' in [start, at) that no ')' inside the span closes, or
 * -1 — the one hasUnresolvedSlurOpen reports on. */
function unresolvedSlurOpen(text: string, start: number, at: number): number {
    let depth = 0;
    for (let i = at - 1; i >= start; i--) {
        const c = text[i];
        if (c === ')') { depth++; }
        else if (c === '(') {
            if (depth === 0) { return i; }
            depth--;
        }
    }
    return -1;
}

/** True when `offset` sits inside a string literal or a comment, where a '(' is
 * plain text. Strings are single-line; block comments are not. */
function inStringOrComment(text: string, offset: number): boolean {
    const blockOpen = text.lastIndexOf('/*', offset);
    if (blockOpen >= 0 && text.lastIndexOf('*/', offset) < blockOpen) { return true; }
    let inString = false;
    for (let i = text.lastIndexOf('\n', offset - 1) + 1; i < offset; i++) {
        if (text[i] === '"') { inString = !inString; }
        else if (!inString && text[i] === '/' && text[i + 1] === '/') { return true; }
    }
    return inString;
}

/** True when a '(' typed at `offset` opens a SLUR. A '(' GLUED to a name is an
 * annotation's argument list (`@finger(3)`); everything else is a slur — the same
 * adjacency rule the parser uses.
 *
 * ⚠️ A GLUED '(' AFTER A PHRASE REFERENCE IS A SLUR NOW. It used to be that reference's
 * diatonic interval argument (`Melody'(3)`), so this returned false for a name that was
 * not a note event in full — which suppressed the auto-close after `Melody'(`. The
 * spelling was removed 2026-08-28 and the parser reads that '(' as a slur, so the editor
 * has to as well or it silently stops closing a slur the compiler is expecting. */
function isSlurOpen(text: string, offset: number): boolean {
    const prev = text[offset - 1];
    if (prev === undefined || /\s/.test(prev) || prev === '|' || prev === '>') { return true; }
    let j = offset - 1;
    while (j >= 0 && /[A-Za-z0-9'.,_-]/.test(text[j])) { j--; }
    if (text[j] === '@') { return false; }  // @annotation(args)
    return true;                            // a note (`c4(`), a chord's `>4(`, a phrase ref
}

/** The note a typed '(' attaches to: the one the caret is ON, wherever in it the
 * caret sits — at its end, in its middle or at its start are all the same note.
 * 'member' when the caret is inside a chord's brackets, where it is pointing at
 * a member and no slur mark can go; null when it is between events. */
function slurAnchorAt(text: string, offset: number)
    : { start: number, end: number } | 'member' | null {
    const [blockStart, blockEnd] = blockBounds(text, offset);
    for (const event of musicEvents(text, blockStart, blockEnd)) {
        // The note reaches to the end of its glued post-events — a caret after
        // the '[' of `a8[|` is still on the a (owner report 2026-09-03: '(' typed
        // there stayed put, because the walk's event ends before the marks).
        if (!event.note || postEventEnd(text, event) < offset) { continue; }
        if (event.start > offset) { return null; } // between events
        const slots = noteSlots(text, event.start, event.end);
        if (slots && text[event.start] === '<'
            && offset > event.start && offset < slots.marksEnd) { return 'member'; }
        // Past the core the caret is among the note's annotations, and an
        // unclosed '(' there that is GLUED TO A NAME is an argument list being
        // typed (`@fig(6| 4)`) — the parens are the annotation's, not the
        // music's. An unclosed slur '(' (`c4(|`) is the note's own mark, and
        // the caret after it is still on the note (2026-09-03: it used to read
        // as 'member' too, which left a '@' typed there where it was).
        if (slots && insideAnnotationArgs(text, slots.coreEnd, offset)) { return 'member'; }
        return { start: event.start, end: event.end };
    }
    return null;
}

/** True when a caret past the note's core (which ends at `coreEnd`) sits inside
 * an annotation's still-open argument list (`@fig(6| 4)`): the unclosed '(' is
 * glued to a NAME — back over the name's characters to an '@', the same reading
 * isSlurOpen uses. An unclosed slur '(' (`c4(|`) is the note's own mark and the
 * caret after it is still on the note. ⚠️ Testing just the one character before
 * the '(' read the `8` of `c8\8(` as a name and left a '@' typed after that '('
 * where it was (owner report, 2026-09-03). */
function insideAnnotationArgs(text: string, coreEnd: number, offset: number): boolean {
    if (offset <= coreEnd) { return false; }
    const open = unresolvedSlurOpen(text, coreEnd, offset);
    if (open < 0) { return false; }
    let j = open - 1;
    while (j >= 0 && /[A-Za-z0-9_-]/.test(text[j])) { j--; }
    return text[j] === '@';
}

/** '(' typed in music: the slur's ')' belongs after the note the slur COVERS,
 * not at the caret. Places it after the following note event, drops it entirely
 * when an unresolved ')' already lies ahead, and leaves the plain `()` pair
 * alone when there is no note left in the block. `autoClosed` = VS Code already
 * inserted the ')' at the caret (it does so before whitespace and EOL). */
export function slurOpenPlan(text: string, offset: number, autoClosed: boolean): FixPlan | null {
    if (inStringOrComment(text, offset) || !isSlurOpen(text, offset)) { return null; }
    // Decided on the text WITHOUT the keystroke, like the octave marks and the
    // durations: a paren parked mid-note splits the very token that has to be
    // read (`c|2` + '(' reads as `c`, `(`, `2` and hides the note `c2`).
    const typedLen = autoClosed ? 2 : 1;
    const before = text.slice(0, offset) + text.slice(offset + typedLen);
    const [, end] = blockBounds(before, offset);

    // WHICH note the slur starts on is read off the caret: it is the note the
    // caret is ON — at its end, in its middle, or at its start, all the same
    // note — and the '(' is written after that note, because that is where the
    // parser reads a slur start from. So `c| d e` and `c|2 d` and `c |d e` all
    // start the slur on the note the caret was in, and the paren travels to that
    // note's end by itself.
    const anchor = slurAnchorAt(before, offset);
    if (anchor === 'member') { return null; } // pointing inside a chord
    // The '(' goes into the note's canonical slot (POST_EVENT_RANK): after what
    // ends there, before a '[' and the tie.
    let openAt = anchor ? postEventSlot(before, coreEndOf(before, anchor), '(') : offset;
    let closeOn = firstNoteEvent(before, anchor ? anchor.end : offset, end);
    if (!closeOn) {
        // Nothing after the anchor to cover. When the caret was pointing AHEAD
        // at that note, the nearest legal two-note slur is the one anchored on
        // the note BEFORE the caret, so the paren stays where it was typed.
        if (!anchor || offset !== anchor.start) { return null; }
        openAt = offset;
        closeOn = firstNoteEvent(before, offset, end);
        if (!closeOn) { return null; } // nothing to slur to — keep the pair as typed
    }
    const closeCore = coreEndOf(before, closeOn);

    // An unresolved ')' ahead: the '(' pairs with THAT, so no close is added —
    // it still moves onto the note the caret pointed at.
    const paired = hasUnresolvedSlurClose(before, openAt, end);
    // A slur already STARTING where this one would close is extended backwards
    // instead: its open gives way to this one (`e| c4( d)` → `e( c4 d)`).
    const existingOpen = paired ? undefined : postEventOn(before, closeCore, '(');
    const extend = existingOpen !== undefined
        && hasUnresolvedSlurClose(before, existingOpen.at + 1, end);

    // Offsets computed on `before` are placed in the document as it stands, which
    // still holds the keystroke. The caret goes right after the '(' — in the same
    // edit, so it is never seen at the position the key was pressed.
    const insertAt = (p: number) => (p <= offset ? p : p + typedLen);
    const charAt = (p: number) => (p < offset ? p : p + typedLen);
    const edits: Edit[] = [
        { at: offset, del: typedLen },
        { at: insertAt(openAt), ins: '(' },
    ];
    if (extend) { edits.push({ at: charAt(existingOpen!.at), del: 1 }); }
    else if (!paired) { edits.push({ at: insertAt(postEventSlot(before, closeCore, ')')), ins: ')' }); }
    return {
        edits, caret: openAt + 1,
        what: `( typed -> ${openAt === offset ? '' : 'moved to the end of its note, '}`
            + (extend ? 'extended the slur starting there'
                : paired ? 'paired with the unresolved ) ahead'
                    : ') placed after the following note'),
    };
}

/** The note events of the measure around `at`, each with the duration it
 * actually sounds — the running value carried from the measure's start, because
 * only the first note of `c8 d e f` spells the eighth out. Bounded by the
 * MEASURE and not by the block, which is what separates a beam from a slur: a
 * beam cannot cross a barline. A barrier ends what can be read. */
type MeasureEvent = { start: number, end: number, core: number, duration: number, rest: boolean };

function measureEvents(text: string, at: number): MeasureEvent[] {
    const [mStart, mEnd] = measureBounds(text, at);
    const events: MeasureEvent[] = [];
    let running = 0;
    for (const event of musicEvents(text, mStart, mEnd)) {
        if (!event.note) {
            if (events.length > 0) { break; }
            continue;
        }
        const slots = noteSlots(text, event.start, event.end);
        if (!slots) { break; }
        const digits = text.slice(slots.marksEnd, slots.digitsEnd);
        running = digits ? parseInt(digits, 10) : running;
        events.push({
            start: event.start, end: event.end, core: slots.coreEnd,
            duration: running, rest: slots.octave === null,
        });
    }
    return events;
}

/** The beamable note NEXT to `events[i]` in `step` direction — the one a beam
 * typed on `events[i]` pairs with — as its index, or -1 when the note at `i`
 * cannot carry a beam or nothing beamable stands beside it. A rest is spanned —
 * `c8[ r8 d8]` is a beam over a rest — but is never the partner, so the walk
 * steps over beamable rests to the first NOTE. A duration that carries no flag
 * (a quarter, a longer note) is a wall, rest or not: the beam cannot reach past
 * it. Until 2026-09-03 this walked the whole run and answered its far end. */
function beamNeighbour(events: MeasureEvent[], i: number, step: 1 | -1): number {
    if (i < 0 || !events[i] || events[i].rest || !BEAMABLE.has(events[i].duration)) { return -1; }
    for (let k = i + step; k >= 0 && k < events.length; k += step) {
        if (!BEAMABLE.has(events[k].duration)) { return -1; }
        if (!events[k].rest) { return k; }
    }
    return -1;
}

/** True when [from, end) holds a ']' that no '[' inside the span opens — an
 * existing beam end a just-typed '[' pairs with. */
function hasUnresolvedBeamClose(text: string, from: number, end: number): boolean {
    let depth = 0;
    for (let i = from; i < end; i++) {
        if (text[i] === '[') { depth++; }
        else if (text[i] === ']') {
            if (depth === 0) { return true; }
            depth--;
        }
    }
    return false;
}

/** True when [start, at) holds a '[' that no ']' inside the span closes — the
 * beam a just-typed ']' is there to close. */
function hasUnresolvedBeamOpen(text: string, start: number, at: number): boolean {
    let depth = 0;
    for (let i = at - 1; i >= start; i--) {
        if (text[i] === ']') { depth++; }
        else if (text[i] === '[') {
            if (depth === 0) { return true; }
            depth--;
        }
    }
    return false;
}

/** '[' typed in music: a manual beam opens after the note it starts on, exactly
 * as a slur and a tie do, and closes on the FOLLOWING beamable note of the same
 * measure (`c8|` + '[' → `c8[ d] e f`) — the slur's shape (slurOpenPlan),
 * bounded by the barline. A note that cannot carry a beam, or one with nothing
 * beamable beside it, is left as typed — as is a '[' that is not on a note at
 * all, which is what an inline volta's `[1.` is. */
export function beamOpenPlan(text: string, offset: number, autoClosed: boolean): FixPlan | null {
    if (inStringOrComment(text, offset)) { return null; }
    const typedLen = autoClosed ? 2 : 1;
    const before = text.slice(0, offset) + text.slice(offset + typedLen);
    const anchor = slurAnchorAt(before, offset);
    if (!anchor || anchor === 'member') { return null; }
    const events = measureEvents(before, anchor.start);
    const closeIdx = beamNeighbour(events, events.findIndex(e => e.start === anchor.start), 1);
    if (closeIdx < 0) { return null; } // not beamable, or nothing to group with
    const closeOn = events[closeIdx];
    // Both ends go into their notes' canonical slots (POST_EVENT_RANK).
    const openAt = postEventSlot(before, coreEndOf(before, anchor), '[');
    const closeAt = postEventSlot(before, closeOn.core, ']');

    // An unresolved ']' ahead: the '[' pairs with THAT, so no close is added.
    const [, mEnd] = measureBounds(before, anchor.end);
    const paired = hasUnresolvedBeamClose(before, openAt, mEnd);
    // A beam already STARTING on the note this one would close on is extended
    // backwards instead: its open gives way to this one (`c8| d[ e]` → `c8[ d e]`).
    const existingOpen = paired ? undefined : postEventOn(before, closeOn.core, '[');
    const extend = existingOpen !== undefined
        && hasUnresolvedBeamClose(before, existingOpen.at + 1, mEnd);

    const insertAt = (p: number) => (p <= offset ? p : p + typedLen);
    const charAt = (p: number) => (p < offset ? p : p + typedLen);
    const edits: Edit[] = [
        { at: offset, del: typedLen },
        { at: insertAt(openAt), ins: '[' },
    ];
    if (extend) { edits.push({ at: charAt(existingOpen!.at), del: 1 }); }
    else if (!paired) { edits.push({ at: insertAt(closeAt), ins: ']' }); }
    return {
        edits, caret: openAt + 1,
        what: `[ typed -> beam ${extend ? 'extended the beam starting on the next note'
            : paired ? 'opened against the ] ahead' : '] placed after the following note'}`,
    };
}

/** ']' typed in music: the end of a beam is written after its note like every
 * other mark here, so one typed inside a note moves to that note's end — and it
 * reaches BACK for the beamable note BEFORE the one it closes on, the mirror of
 * what '[' does forwards and the shape ')' has (slurClosePlan). A beam that
 * already ends on that preceding note is EXTENDED by one instead of a second one
 * opening beside it. An unresolved '[' already in the measure means the ']'
 * simply closes that, and only moves. */
export function beamClosePlan(text: string, offset: number): FixPlan | null {
    if (inStringOrComment(text, offset)) { return null; }
    const before = text.slice(0, offset) + text.slice(offset + 1);
    const anchor = slurAnchorAt(before, offset);
    if (!anchor || anchor === 'member') { return null; }

    const [mStart] = measureBounds(before, anchor.start);
    const events = measureEvents(before, anchor.start);
    // The ']' goes into its note's canonical slot (POST_EVENT_RANK): after the
    // string number and the annotations, before a ')' and anything that opens.
    const closeAt = postEventSlot(before, coreEndOf(before, anchor), ']');
    const insertAt = (p: number) => (p <= offset ? p : p + 1);
    const charAt = (p: number) => (p < offset ? p : p + 1);
    const edits: Edit[] = [];
    if (closeAt !== offset) {
        edits.push({ at: offset, del: 1 }, { at: insertAt(closeAt), ins: ']' });
    }
    let caret = closeAt + 1;
    let what: string;

    if (hasUnresolvedBeamOpen(before, mStart, anchor.start)) {
        // Already open — this ']' is its close, and only moves onto its note.
        if (edits.length === 0) { return null; } // nothing to do at all
        what = 'moved into its slot on the note';
    } else {
        const openIdx = beamNeighbour(events, events.findIndex(e => e.start === anchor.start), -1);
        const openOn = openIdx >= 0 ? events[openIdx] : undefined;
        const old = openOn ? postEventOn(before, openOn.core, ']') : undefined;
        if (openOn && old && hasUnresolvedBeamOpen(before, mStart, old.at)) {
            // A beam already ends on that note: EXTEND it by one rather than
            // opening a second one beside it (`c8[ d] e` + ']' → `c8[ d e]`).
            edits.push({ at: charAt(old.at), del: 1 });
            caret -= 1;
            what = 'extended the beam ending on the previous note';
        } else if (openOn) {
            edits.push({ at: insertAt(postEventSlot(before, openOn.core, '[')), ins: '[' });
            // Past the ']' — which the '[' inserted before it has pushed along by one.
            caret += 1;
            what = '[ placed after the preceding note';
        } else if (edits.length === 0) {
            return null; // not beamable, or nothing to open on — leave the ']' as typed
        } else {
            what = 'moved into its slot on the note';
        }
    }
    return { edits, caret, what: `] typed -> ${what}` };
}

/** '~' typed in music: a tie is written after the note it starts from, exactly
 * as a slur's '(' is (`c4~ | c4`), so one typed anywhere ON a note moves to that
 * note's end. Unlike a slur it is a single mark — it needs no second note here,
 * because the note it ties TO is whatever follows, and the compiler is the one
 * that checks the pitch repeats (LYS4007). */
export function tiePlan(text: string, offset: number): FixPlan | null {
    if (inStringOrComment(text, offset)) { return null; }
    const before = text.slice(0, offset) + text.slice(offset + 1);
    const anchor = slurAnchorAt(before, offset);
    if (!anchor || anchor === 'member') { return null; } // between events, or a chord member
    // The tie is the LAST post-event (POST_EVENT_RANK): it stands between the
    // two notes it joins, after everything that ends or begins on this one.
    const slot = postEventSlot(before, coreEndOf(before, anchor), '~');
    if (slot === offset) { return null; } // already where it belongs
    return {
        edits: [
            { at: offset, del: 1 },
            { at: slot <= offset ? slot : slot + 1, ins: '~' },
        ],
        caret: slot + 1,
        what: '~ typed -> moved to the end of its note',
    };
}

/** ')' typed in music: the slur's '(' belongs after the note BEFORE the one the
 * ')' closes on (`c4 d` + ')' → `c4( d)`), the mirror of a typed '(' reaching
 * forward. An unresolved '(' before it — a slur the user already opened, or an
 * annotation's argument list — means the ')' just closes that, so nothing is
 * added; nor is anything added with no preceding note to open on. */
export function slurClosePlan(text: string, offset: number): FixPlan | null {
    if (inStringOrComment(text, offset)) { return null; }
    const before = text.slice(0, offset) + text.slice(offset + 1);
    const [blockStart, blockEnd] = blockBounds(before, offset);

    // WHERE it belongs: in its slot on the note the caret is on (POST_EVENT_RANK
    // — after a ']', before anything that opens and before the tie).
    const anchor = slurAnchorAt(before, offset);
    if (anchor === 'member') { return null; }
    const closeAt = anchor ? postEventSlot(before, coreEndOf(before, anchor), ')') : offset;

    const insertAt = (p: number) => (p <= offset ? p : p + 1);
    const charAt = (p: number) => (p < offset ? p : p + 1);
    const edits: Edit[] = [];
    if (closeAt !== offset) {
        edits.push({ at: offset, del: 1 }, { at: insertAt(closeAt), ins: ')' });
    }
    let caret = closeAt + 1;
    let what: string;

    if (hasUnresolvedSlurOpen(before, blockStart, closeAt)) {
        // INSIDE an open slur, so this ')' is that slur's end now and the end it
        // used to have — necessarily ahead — gives way: `(c d| e)` becomes
        // `(c d) e`. Slurs nest, and both searches count depth, so the pair that
        // moves is the INNERMOST one the caret sits in.
        const oldClose = unresolvedSlurClose(before, closeAt, blockEnd);
        if (oldClose < 0 && edits.length === 0) { return null; } // nothing to do
        if (oldClose >= 0) { edits.push({ at: charAt(oldClose), del: 1 }); }
        what = oldClose >= 0 ? 'moved the slur end here' : 'closed the open slur';
    } else {
        // Not inside one: the ')' needs a slur to belong to, so it reaches back
        // for the note BEFORE the one it closes on.
        const openOn = precedingNote(before, blockStart, anchor ? anchor.end : offset);
        const openCore = openOn ? coreEndOf(before, openOn) : -1;
        const old = openOn ? postEventOn(before, openCore, ')') : undefined;
        if (openOn && old && hasUnresolvedSlurOpen(before, blockStart, old.at)) {
            // A slur already ends on that note: EXTEND it by one rather than
            // opening a second one beside it.
            edits.push({ at: charAt(old.at), del: 1 });
            caret -= 1;
            what = 'extended the slur ending on the previous note';
        } else if (openOn) {
            edits.push({ at: insertAt(postEventSlot(before, openCore, '(')), ins: '(' });
            caret += 1;
            what = '( placed after the preceding note';
        } else if (edits.length === 0) {
            return null; // nothing to open on — leave the ')' as typed
        } else {
            what = 'moved to the end of its note';
        }
    }
    return { edits, caret, what: `) typed -> ${what}` };
}

/** The written slots of the note event at [start, end): where its octave marks
 * go (null for a rest, which takes none), where the marks already there END —
 * which is also where the duration starts — where its digits end, and where its
 * CORE does (pitch, marks, duration and dots, but NOT its annotations). null
 * when the event is not something these slots apply to. */
interface NoteSlots { octave: number | null, marksEnd: number, digitsEnd: number, coreEnd: number }

function noteSlots(text: string, start: number, end: number): NoteSlots | null {
    let octave: number | null = null;
    let marksEnd: number;
    if (text[start] === '<') {
        const arpeggio = text[start + 1] === '<';
        let j = start + (arpeggio ? 2 : 1);
        for (let depth = 0; j < end; j++) {
            if (text[j] === '<') { depth++; }
            else if (text[j] === '>') {
                if (depth === 0) { break; }
                depth--;
            }
        }
        if (j >= end) { return null; }
        j++;
        if (arpeggio && text[j] === '>') { j++; }
        octave = marksEnd = j;
    } else {
        const m = PITCH_LETTERS.exec(text.slice(start, end));
        if (m) { octave = marksEnd = start + m[0].length; }
        else if ('rsR'.includes(text[start])) { marksEnd = start + 1; } // a rest
        else { return null; }
    }
    while (marksEnd < end && (text[marksEnd] === "'" || text[marksEnd] === ',')) { marksEnd++; }
    let digitsEnd = marksEnd;
    while (digitsEnd < end && /[0-9]/.test(text[digitsEnd])) { digitsEnd++; }
    let coreEnd = digitsEnd;
    while (coreEnd < end && text[coreEnd] === '.') { coreEnd++; }
    return { octave, marksEnd, digitsEnd, coreEnd };
}

/** The note event the caret at `offset` is ON, with its slots — or null when the
 * caret is between events, on a barrier, or past a note's core (inside its
 * annotations, where a ',' or a digit means something else entirely). Anywhere
 * inside a plain note counts; a chord counts only at its ENDS, because its
 * interior belongs to the members (`<c, e g>`, `<c 3 5>`) and what is typed
 * there is already in the right place. */
function noteAtCaret(text: string, offset: number, throughMarks = false)
    : { start: number, slots: NoteSlots } | null {
    const [blockStart, blockEnd] = blockBounds(text, offset);
    for (const event of musicEvents(text, blockStart, blockEnd)) {
        if (!event.note || postEventEnd(text, event) < offset) { continue; }
        if (event.start > offset) { return null; } // the caret is between events
        const slots = noteSlots(text, event.start, event.end);
        if (!slots) { return null; }
        // `throughMarks`: the caret is on the note ANYWHERE in its glued
        // post-events too (`c8\8(|[` + '4' → `c4\8([`, owner request 2026-09-03)
        // — except inside an annotation's argument list, where a digit is the
        // argument. The dot does not ask for this: a '.' typed after
        // `@text("x")` is the `.up` placement, not an augmentation dot.
        const far = throughMarks && !insideAnnotationArgs(text, slots.coreEnd, offset)
            ? postEventEnd(text, event) : slots.coreEnd;
        const onIt = text[event.start] === '<'
            ? offset === event.start
                || (offset >= slots.marksEnd && offset <= far)
            : offset >= event.start && offset <= far;
        return onIt ? { start: event.start, slots } : null;
    }
    return null;
}

/** "'" or ',' typed in music. An octave mark belongs between the pitch and the
 * duration, so one typed at either END of a note (`|c4`, `c4|`) moves into that
 * slot rather than splitting the token. Typed against the OPPOSITE mark it
 * CANCELS one instead — "'" undoes a ',' and vice versa — so an overshot octave
 * is walked back with the key that caused it, no selecting or backspacing. */
export function octaveMarkPlan(before: string, offset: number, mark: string): TypePlan | null {
    if (inStringOrComment(before, offset)) { return null; }
    const found = noteAtCaret(before, offset, true);
    if (!found || found.slots.octave === null) { return null; } // a rest takes none
    const { octave, marksEnd } = found.slots;

    const opposite = mark === "'" ? ',' : "'";
    const cancel = before.lastIndexOf(opposite, marksEnd - 1);
    if (cancel >= octave) {
        // The caret STAYS where it was pressed (stayPut): the cancelled mark is
        // one character leaving the note, so a caret after it slides back with
        // the text and one before it does not move at all.
        return {
            at: cancel, del: 1, ins: '',
            caret: stayPut(offset, cancel, 1, 0),
            what: `cancelled one ${opposite}`,
        };
    }
    if (marksEnd === offset) { return null; } // already in the slot — type it normally
    // The caret STAYS where it was pressed (stayPut). The mark travelling into
    // the slot is not a cursor movement the typist asked for, and each further
    // mark stacks correctly anyway — the slot is re-read from the note every
    // time, not remembered from where the caret was left.
    return {
        at: marksEnd, del: 0, ins: mark,
        caret: stayPut(offset, marksEnd, 0, 1),
        what: 'moved into the octave slot',
    };
}

/** A digit typed in music: a duration belongs AFTER the octave marks (`c,4`),
 * so one typed anywhere else on the note moves there — `|c` and `c,|` alike.
 * Digits already in the slot are EXTENDED while the result is still a duration
 * in the making (`c1` + '6' → `c16`, from any caret position on the note) and
 * REPLACED when it cannot become one (`c4` + '2' → `c2`, since neither `c42`
 * nor `c24` exists). A digit that starts no duration at all — 5, 7, 9, 0 — is
 * left exactly as typed. */
export function durationPlan(before: string, offset: number, digit: string): TypePlan | null {
    if (inStringOrComment(before, offset)) { return null; }
    const found = noteAtCaret(before, offset, true);
    if (!found) { return null; }
    const { marksEnd, digitsEnd } = found.slots;

    // A '\' on the note still waiting for its digit takes this one (rule 25): it
    // is the string number rule 24 opened, and handing the digit to the duration
    // would write `c43\`. The caret stays put here as well. 0 is no string number
    // (the lexer reads `\1`–`\9`) and goes to the duration path, which leaves it
    // as typed too. A caret DIRECTLY after that '\' — where rule 24 leaves it — is
    // ordinary typing: the digit lands there by itself and carries the caret.
    const stringNumber = found.slots.octave === null ? null
        : stringNumberAt(before, found.slots.coreEnd);
    if (stringNumber && !stringNumber.digit && digit !== '0') {
        const slot = stringNumber.at + 1;
        if (slot === offset) { return null; }
        return {
            at: slot, del: 0, ins: digit,
            caret: stayPut(offset, slot, 0, 1),
            what: `string number ${digit} (rule 25)`,
        };
    }

    const digits = before.slice(marksEnd, digitsEnd);

    const atRunEnd = offset === digitsEnd;
    let written: string;
    if (atRunEnd && isDurationPrefix(digits + digit)) {
        // The caret is where the digits are being typed, so the keystroke simply
        // joins them: `c1|` + '6' → `c16`, `c1|.` + '2' → `c12.`.
        written = digits + digit;
    } else if (!isDurationPrefix(digit)) {
        return null;                                  // 5, 7, 9, 0 — no duration
    } else if (!isDuration(digit) && isDurationPrefix(digits + digit)) {
        // A '6' is no duration — nothing sounds for six — so it can only be
        // building one, and it extends what is already there: `c1.|` + '6'.
        written = digits + digit;
    } else {
        written = digit;                              // a fresh duration
    }

    // A digit that cannot stand alone is FINISHED here rather than left for the
    // typist to complete: only 32 begins with a 3 and only 64 with a 6, so there
    // is nothing to guess and `c3` — which sounds as nothing — never has to be
    // written down. ⚠️ Only when the digit is the WHOLE of what gets written. A
    // run being built up must not be finished for the typist: completing the
    // `c12` of a 128th would turn the '8' they go on to press into `c8`.
    const finishes = DURATIONS.filter(d => d.startsWith(written));
    let action: string;
    if (written === digit && !isDuration(written) && finishes.length === 1) {
        written = finishes[0];
        action = 'completed';
    } else if (written !== digit) { action = 'extended'; }
    else if (written === digits) { action = 'restarted'; }
    else { action = 'replaced'; }

    // Ordinary typing already produces this, so leave it to do so — that path
    // relocates nothing and cannot flicker.
    if (atRunEnd && written === digits + digit) { return null; }

    // At the run end the digits are being typed and the caret carries on past
    // what was written, exactly where ordinary typing would have left it.
    // Everywhere else it STAYS where it was pressed (stayPut), the digit run
    // having grown or shrunk under it — `c1.|` + '2' gives `c2.|`.
    // `written === digits` is the same duration retyped (`c1.` + '1'): the plan
    // is then a no-op, and the keystroke is simply absorbed.
    return {
        at: marksEnd, del: digitsEnd - marksEnd, ins: written,
        caret: atRunEnd ? marksEnd + written.length
            : stayPut(offset, marksEnd, digitsEnd - marksEnd, written.length),
        what: `duration ${action} (${written})`,
    };
}

/** '.' typed in music: an augmentation dot is the LAST thing on a note's core —
 * after the duration and after any dots already there — so one typed anywhere
 * else on the note moves to the end of that run (`c|'8.` + '.' → `c'8..`), the
 * same slot rule the octave marks and the duration follow.
 *
 * A dot is the one slot a REST takes on the same terms as a note (`r4.`), so
 * unlike an octave mark this does not ask whether the event has a pitch. Past the
 * core the caret is in the note's annotations, where a '.' is an argument's
 * decimal point or the `.up` of a placement suffix — noteAtCaret already refuses
 * those, and they are typed exactly as pressed. */
export function dotPlan(before: string, offset: number): TypePlan | null {
    if (inStringOrComment(before, offset)) { return null; }
    const found = noteAtCaret(before, offset);
    if (!found) { return null; }
    const { coreEnd } = found.slots;
    if (coreEnd === offset) { return null; } // already in the slot — type it normally
    return {
        at: coreEnd, del: 0, ins: '.',
        caret: stayPut(offset, coreEnd, 0, 1),
        what: 'moved into the dot slot',
    };
}

/** '\' typed in music: a tab string number belongs to the note it plays, in the
 * slot directly after the note's core (`c4\3`, `g'\2`) — so one typed anywhere
 * ON the note opens it there, and the caret goes with it, to just after the
 * '\', because the digit is what gets typed next (rule 24; `|a4` + '\' →
 * `a4\|`). A note that already carries a `\N` gets nothing inserted: its N is SELECTED
 * instead, so the digit typed next replaces it (rule 24a). A `\` still waiting
 * for its digit absorbs the keystroke.
 *
 * The caret counts as on the note ANYWHERE in the event — its core, its
 * annotations, its own `\N` — because a string number is about the note, not a
 * slot the caret has to be in. A rest plays no string and takes the '\' as
 * pressed; inside a chord the '\' is the MEMBER's the caret is on (rule 29,
 * chordMemberAtCaret), and at the chord's ends it is typed as pressed. */
export function stringNumberPlan(before: string, offset: number): TypePlan | null {
    if (inStringOrComment(before, offset)) { return null; }
    const [blockStart, blockEnd] = blockBounds(before, offset);
    for (const event of musicEvents(before, blockStart, blockEnd)) {
        if (!event.note || postEventEnd(before, event) < offset) { continue; }
        if (event.start > offset) { return null; } // the caret is between events
        let core: number;
        if (before[event.start] === '<') {
            // A chord: the string number is the MEMBER's (`<c\3 e\2>`), so the
            // caret has to be on one; at the chord's ends it is typed as pressed.
            const member = chordMemberAtCaret(before, offset);
            if (member === null) { return null; }
            core = member;
        } else {
            const slots = noteSlots(before, event.start, event.end);
            if (!slots || slots.octave === null) { return null; } // a rest plays no string
            core = slots.coreEnd;
        }

        const existing = stringNumberAt(before, core);
        if (existing && existing.digit) {
            // Already numbered: offer the digit for retyping. No text changes —
            // the plan replaces the digit with itself — and the caret becomes a
            // one-character selection over it.
            const at = existing.at + 1;
            return {
                at, del: 1, ins: before[at], caret: at, select: 1,
                what: 'selected the string number to retype',
            };
        }
        if (existing) {
            // A `\` with no digit yet: the string number is already opened.
            return {
                at: existing.at + 1, del: 0, ins: '', caret: offset,
                what: 'string number already opened, waiting for its digit',
            };
        }
        const slot = postEventSlot(before, core, 'string'); // = the core's end
        if (slot === offset) { return null; } // already in the slot — type it normally
        // The caret FOLLOWS the '\' (not stayPut): the string number is typed
        // next and belongs right after it.
        return {
            at: slot, del: 0, ins: '\\',
            caret: slot + 1,
            what: 'moved into the string-number slot, caret after it',
        };
    }
    return null;
}

/** '@' typed in music: an annotation belongs to the note it decorates, in the
 * slot after the string number and the annotations already there and before
 * every slur, tie and beam mark (POST_EVENT_RANK, rule 27) — so one typed
 * anywhere ON a note goes there, the caret following it because the name is
 * typed next (`c4~|` + '@' → `c4@|~`). Between events, in a chord's brackets or
 * inside an annotation's arguments the '@' is left where it was typed. */
export function annotationPlan(before: string, offset: number): TypePlan | null {
    if (inStringOrComment(before, offset)) { return null; }
    const anchor = slurAnchorAt(before, offset);
    if (!anchor) { return null; }
    // 'member' is a caret inside a chord's brackets or inside an annotation's
    // arguments: on a chord MEMBER the '@' is that member's (`<c@finger(1) e>`,
    // rule 29); in an argument list it is typed as pressed.
    const core = anchor === 'member' ? chordMemberAtCaret(before, offset) : coreEndOf(before, anchor);
    if (core === null) { return null; }
    const slot = postEventSlot(before, core, 'annotation');
    if (slot === offset) { return null; } // already in the slot
    return {
        at: slot, del: 0, ins: '@', caret: slot + 1,
        what: 'moved into the annotation slot, caret after it',
    };
}

/** '<' typed: wrap the following note, or — against an existing '<' — promote
 * the measure's matching '>' to '>>'. */
export function chordOpenPlan(text: string, offset: number): FixPlan | null {
    const after = offset + 1;

    if (text[after] === '<' || text[offset - 1] === '<') {
        // A stray third '<' — leave it alone.
        if (text[offset - 1] === '<' && text[offset - 2] === '<') { return null; }
        if (text[after] === '<' && text[after + 1] === '<') { return null; }
        // Scan from past the pair, so the pair's own chars don't count as nesting.
        const bodyStart = text[after] === '<' ? after + 1 : after;
        const [, end] = measureBounds(text, bodyStart);
        const close = findClose(text, bodyStart, end);
        if (close < 0 || text[close + 1] === '>') { return null; } // unclosed, or already '>>'
        return { edits: [{ at: close + 1, ins: '>' }], what: '< doubled -> promoted the matching > to >>' };
    }

    // An unresolved '>' later in the measure means the user is re-opening an
    // existing chord — the bare '<' pairs with it, so add nothing.
    const [, mEnd] = measureBounds(text, after);
    if (hasUnresolvedClose(text, after, mEnd)) { return null; }

    // '<' typed directly before a note: wrap that ONE note.
    const m = NOTE_TOKEN.exec(text.slice(after));
    if (!m) { return null; }
    const noteEnd = after + m[0].length;
    // A '>' already there means the note IS wrapped (a re-typed '<' in front
    // of `c>`); inserting another would double-close it.
    if (text[noteEnd] === '>') { return null; }
    // Caret between the note and the '>': typing ' e g' continues the chord.
    return { edits: [{ at: noteEnd, ins: '>' }], caret: noteEnd, what: `wrapped note -> <${m[0]}>` };
}

/** VS Code auto-closed a typed '<' into '<>'. When the measure ahead already
 * holds an unresolved '>', the user is re-opening an existing chord — keep just
 * the '<' and drop the auto-inserted '>' so the old close pairs with it. */
export function chordAutoClosePlan(text: string, offset: number): FixPlan | null {
    const closeAt = offset + 1; // the auto-inserted '>'
    const [, end] = measureBounds(text, closeAt + 1);
    if (!hasUnresolvedClose(text, closeAt + 1, end)) { return null; }
    return { edits: [{ at: closeAt, del: 1 }], what: 'unresolved > ahead -> dropped the auto-closed >' };
}

/** One '<' of a '<<' deleted: demote the measure's matching '>>' to '>'. A
 * LONE '<' deleted is deliberately left alone — the orphaned '>' is how the
 * chord's range gets re-drawn (see the rules above). */
export function chordDeleteOpenPlan(text: string, oldText: string, offset: number): FixPlan | null {
    const wasPair = oldText[offset + 1] === '<' || oldText[offset - 1] === '<';
    if (!wasPair) { return null; }
    // The OLD text must have held exactly '<<' here (not '<<<').
    if (oldText[offset - 2] === '<'
        || (oldText[offset - 1] === '<' && oldText[offset + 1] === '<')
        || oldText[offset + 2] === '<') { return null; }
    // The remaining '<' sits at `offset` (the deletion closed the gap) or one
    // before it when the SECOND '<' of the pair was the deleted one.
    const remaining = text[offset] === '<' ? offset : offset - 1;
    if (text[remaining] !== '<') { return null; }
    const [, end] = measureBounds(text, remaining + 1);
    const close = findClose(text, remaining + 1, end);
    if (close < 0 || text[close + 1] !== '>') { return null; } // no close, or already single
    return { edits: [{ at: close, del: 1 }], what: '<< reduced -> demoted the matching >> to >' };
}

/** '>' typed. Against an existing '>' it promotes the measure's matching '<'
 * to '<<'. A lone '>' closing an unresolved '<' is left alone; a lone '>' with
 * nothing to close auto-opens — wrapping the pitch directly before it, or
 * forming an empty `<>`. */
export function chordClosePlan(text: string, offset: number): FixPlan | null {
    const after = offset + 1;
    if (text[after] === '>' || text[offset - 1] === '>') {
        // A stray third '>' — leave it alone.
        if (text[offset - 1] === '>' && text[offset - 2] === '>') { return null; }
        if (text[after] === '>' && text[after + 1] === '>') { return null; }
        // Scan from before the pair, so the pair's own chars don't count as nesting.
        const pairStart = text[offset - 1] === '>' ? offset - 1 : offset;
        const [pStart] = measureBounds(text, pairStart);
        const open = findOpen(text, pairStart, pStart);
        if (open < 0 || text[open - 1] === '<') { return null; } // unopened, or already '<<'
        return { edits: [{ at: open, ins: '<' }], what: '> doubled -> promoted the matching < to <<' };
    }
    // Lone '>': with an unresolved '<' before it, it IS that chord's close.
    const [start] = measureBounds(text, offset);
    if (findOpen(text, offset, start) >= 0) { return null; }
    // Otherwise auto-open: wrap the pitch token ending right at the '>' —
    // `c` + '>' -> `<c>` — or, with no note there, form an empty `<>`. The
    // boundary check keeps a word's tail (`time`) from posing as a pitch.
    const m = /(?:^|[^a-z])([a-g][a-z]*[',]*)$/.exec(text.slice(start, offset));
    const openAt = m ? offset - m[1].length : offset;
    return {
        edits: [{ at: openAt, ins: '<' }],
        what: m ? `> typed -> wrapped <${m[1]}>` : '> typed -> auto-opened <>',
    };
}

/** One '>' of a '>>' deleted: demote the measure's matching '<<' to '<'. A
 * LONE '>' deleted is deliberately left alone — the orphaned '<' is how the
 * chord's range gets re-drawn (see the rules above). */
export function chordDeleteClosePlan(text: string, oldText: string, offset: number): FixPlan | null {
    const wasPair = oldText[offset + 1] === '>' || oldText[offset - 1] === '>';
    if (!wasPair) { return null; }
    // The OLD text must have held exactly '>>' here (not '>>>').
    if (oldText[offset - 2] === '>'
        || (oldText[offset - 1] === '>' && oldText[offset + 1] === '>')
        || oldText[offset + 2] === '>') { return null; }
    const remaining = text[offset] === '>' ? offset : offset - 1;
    if (text[remaining] !== '>') { return null; }
    const [start] = measureBounds(text, remaining);
    const open = findOpen(text, remaining, start);
    if (open < 0 || text[open - 1] !== '<') { return null; } // no opener, or already single
    return { edits: [{ at: open - 1, del: 1 }], what: '>> reduced -> demoted the matching << to <' };
}
