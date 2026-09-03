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

// A keystroke on a string: the editor smartTyping.ts talks to, replayed on a
// plain string so the core's plans can be tested without VS Code. The two
// things this stands in for are (1) what VS Code does to the document before
// the extension hears of a key — inserts the character, or the auto-closed
// pair, and moves the caret past it — and (2) how smartTyping.ts carries a
// plan out: one snippet replacement with the caret placed inside it
// (composeFix), or a plain edit that leaves VS Code's caret where it was.
//
// A document is written with the caret as ‸ — never '|', which is a barline.

import * as assert from 'node:assert/strict';
import {
    Edit, afterKeystrokeEdit, composeFix, deletionPlan, insertionPlan, isPlannedKey,
    planFor, typedKeyOutcome,
} from '../src/smartTypingCore';

export const CARET = '‸';

/** A document after a keystroke: its text, where the caret is, and how many
 * characters from the caret are selected (0 when none). */
export interface State { text: string, caret: number, select: number }

/** Reads `c4‸ d` as the text `c4 d` with the caret at 2. Exactly one marker. */
export function parse(marked: string): { text: string, caret: number } {
    const caret = marked.indexOf(CARET);
    if (caret < 0 || marked.indexOf(CARET, caret + 1) >= 0) {
        throw new Error(`exactly one ${CARET} expected in ${JSON.stringify(marked)}`);
    }
    return { text: marked.slice(0, caret) + marked.slice(caret + 1), caret };
}

/** Writes a state back in the marked form; a selection is shown as «…» after
 * the caret (`a4\‸«3»` — the caret at the 3, the 3 selected). */
export function show(s: State): string {
    const before = s.text.slice(0, s.caret);
    if (s.select > 0) {
        return before + CARET + '«' + s.text.slice(s.caret, s.caret + s.select) + '»'
            + s.text.slice(s.caret + s.select);
    }
    return before + CARET + s.text.slice(s.caret);
}

// The pairs language-configuration.json auto-closes, and the characters VS
// Code auto-closes them BEFORE (editor.autoCloseBefore's default): a '(' typed
// before a space or at the end of the line arrives as '()', one typed before a
// letter arrives alone.
const PAIRS: Record<string, string> = { '(': ')', '[': ']', '<': '>' };
const AUTO_CLOSE_BEFORE = ';:.,=}])> \n\t';

function autoCloses(text: string, offset: number, ch: string): boolean {
    if (!(ch in PAIRS)) { return false; }
    const next = text[offset];
    return next === undefined || AUTO_CLOSE_BEFORE.includes(next);
}

/** The document after smartTyping.ts's applyFixWithCaret: the composed span
 * replaced, the caret at the tabstop. */
function applyComposed(text: string, edits: Edit[], caret: number, select: number): State {
    const c = composeFix(text, edits, caret, select);
    return { text: text.slice(0, c.lo) + c.out + text.slice(c.hi), caret: c.lo + c.caretIn, select: c.selectLen };
}

/** The document after a plain editor.edit() of `edits` — every offset in the
 * text as it stood — with VS Code's caret carried along: text inserted at or
 * before it pushes it, text deleted before it pulls it back. */
function applyBuilder(text: string, edits: Edit[], caret: number): State {
    let out = text;
    let c = caret;
    for (const e of [...edits].sort((a, b) => b.at - a.at)) {
        const del = e.del ?? 0;
        const ins = e.ins ?? '';
        out = out.slice(0, e.at) + ins + out.slice(e.at + del);
        if (del > 0 && e.at < c) { c -= Math.min(del, c - e.at); }
        if (ins && e.at <= c) { c += ins.length; }
    }
    return { text: out, caret: c, select: 0 };
}

/** Types `key` at the caret of `marked` and returns the document as the
 * extension leaves it. `autoClose` overrides VS Code's auto-closing (the
 * default is what VS Code would do at that position).
 *
 * For the keys decided as a TypePlan — the octave marks, the digits, '.', '\'
 * and '@' — BOTH routes into the extension are replayed: the intercepted key
 * (planned on the text without the keystroke) and the change event (the
 * keystroke already in the document, taken back out by afterKeystrokeEdit).
 * They must agree — that is the property the plan's "decided on the text
 * WITHOUT the keystroke" framing exists for — and this asserts it on every call. */
export function typeKey(marked: string, key: string, opts: { autoClose?: boolean } = {}): State {
    const { text: before, caret: offset } = parse(marked);
    const autoClose = opts.autoClose ?? autoCloses(before, offset, key);
    const typed = autoClose ? key + PAIRS[key] : key;
    const withKey = before.slice(0, offset) + typed + before.slice(offset);
    const asTyped: State = { text: withKey, caret: offset + 1, select: 0 };

    if (isPlannedKey(key)) {
        const plan = planFor(key, before, offset);
        if (!plan) { return asTyped; }
        const outcome = typedKeyOutcome(before, offset, plan);
        const intercepted: State =
            outcome.kind === 'select' ? { text: before, caret: outcome.caret, select: outcome.select }
                : outcome.kind === 'absorbed' ? { text: before, caret: offset, select: 0 }
                    : applyComposed(before, outcome.edits, outcome.caret, outcome.select);
        const changeEvent = applyComposed(
            withKey, [afterKeystrokeEdit(withKey, offset, plan)], plan.caret, plan.select ?? 0);
        assert.deepEqual(changeEvent, intercepted,
            `the intercepted and change-event routes disagree for ${JSON.stringify(marked)} + ${key}`);
        return intercepted;
    }

    const plan = insertionPlan(typed, withKey, offset);
    if (!plan) { return asTyped; }
    return plan.caret !== undefined
        ? applyComposed(withKey, plan.edits, plan.caret, 0)
        : applyBuilder(withKey, plan.edits, asTyped.caret);
}

/** Presses Delete at the caret of `marked` (the character AFTER the caret goes)
 * and returns the document as the extension leaves it. */
export function pressDelete(marked: string): State {
    const { text: oldText, caret: offset } = parse(marked);
    return deleted(oldText, offset, offset);
}

/** Presses Backspace at the caret of `marked` (the character BEFORE it goes). */
export function pressBackspace(marked: string): State {
    const { text: oldText, caret } = parse(marked);
    return deleted(oldText, caret - 1, caret - 1);
}

function deleted(oldText: string, offset: number, caret: number): State {
    const ch = oldText[offset];
    const text = oldText.slice(0, offset) + oldText.slice(offset + 1);
    const plan = deletionPlan(ch, text, oldText, offset);
    if (!plan) { return { text, caret, select: 0 }; }
    return plan.caret !== undefined
        ? applyComposed(text, plan.edits, plan.caret, 0)
        : applyBuilder(text, plan.edits, caret);
}
