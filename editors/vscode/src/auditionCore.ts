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

// The editor-free half of the step keys and the automatic audition (stepAudition.ts
// holds the half that talks to VS Code): WHEN something should sound. What sounds
// is the server's answer (lilysharp/step, lilysharp/auditionAt) — the pitches come
// from the compiler, never from here. Nothing here imports vscode, so `npm test`
// runs it.
//
// Owner's decision, 2026-09-28: the caret landing on a DIFFERENT note sounds it
// (debounced, never while a selection is being extended), a note typed from the
// keyboard sounds once its token has settled, a step sounds what it stepped to.

/** A caret move sounds only after the caret has rested this long: an arrow key held
 * down repeats every ~30 ms, and a note per repeat would machine-gun. */
export const CARET_DEBOUNCE_MS = 80;

/** A typed note sounds once no keystroke has come for this long, so `cis'` typed at
 * an ordinary pace sounds once, as cis', not as c, then cis, then cis'. A slower
 * typist hears the intermediate notes — the price of not waiting longer on everyone. */
export const TYPING_SETTLE_MS = 300;

/** A selection change this soon after a document change is the edit's own caret
 * movement (typing, a step, an undo), not the user moving the caret. */
export const EDIT_SELECTION_WINDOW_MS = 60;

/** How long an audition rings: a single note, and a chord or a voicing (whose
 * strings deserve the time to be heard together). */
export const NOTE_MS = 450;
export const CHORD_MS = 900;

/** The ring length for an answer of `pitchCount` pitches. */
export function auditionDurationMs(pitchCount: number): number {
    return pitchCount > 1 ? CHORD_MS : NOTE_MS;
}

/** What the server says the caret (or a typed note) is on: its key (the start
 * offset of the note, member, chord or @chord; -1 for nothing that sounds) and the
 * pitches it sounds. */
export interface AuditionAnswer {
    readonly key: number;
    readonly pitches: readonly number[];
}

/**
 * Remembers what sounded last in each document and decides whether an answer is
 * news. Two different questions, one memory:
 *
 * - a caret move sounds when it lands on a different THING than last time (the key)
 *   — arrowing through the letters of `cis'` is one note, and leaving a note and
 *   coming back to it with nothing sounded in between is not news either;
 * - a typed keystroke sounds when the thing's SOUND changed (key and pitches) — a
 *   duration digit typed after `cis'` leaves the pitch alone and stays silent, an
 *   octave mark changes it and sounds.
 *
 * Every audition that does sound (a step's included) is recorded, so the caret
 * resting on the note that was just stepped or typed does not sound it again.
 */
export class AuditionMemory {
    private readonly last = new Map<string, { key: number; signature: string }>();

    /** A caret landed and the server answered: sound it? Records it when so. */
    caretLanded(doc: string, answer: AuditionAnswer): boolean {
        if (answer.key < 0 || answer.pitches.length === 0) { return false; }
        const prev = this.last.get(doc);
        if (prev && prev.key === answer.key) { return false; }
        this.record(doc, answer);
        return true;
    }

    /** A keystroke settled and the server answered for the caret: sound it? */
    typed(doc: string, answer: AuditionAnswer): boolean {
        if (answer.key < 0 || answer.pitches.length === 0) { return false; }
        const prev = this.last.get(doc);
        if (prev && prev.signature === signatureOf(answer)) { return false; }
        this.record(doc, answer);
        return true;
    }

    /** Something sounded this answer (a step): remember it as the last one. */
    record(doc: string, answer: AuditionAnswer): void {
        this.last.set(doc, { key: answer.key, signature: signatureOf(answer) });
    }

    /** The document closed. */
    forget(doc: string): void {
        this.last.delete(doc);
    }
}

function signatureOf(answer: AuditionAnswer): string {
    return `${answer.key}:${answer.pitches.join(',')}`;
}

/** The facts of one selection change that decide whether it is a caret move worth
 * asking about. */
export interface SelectionFacts {
    /** Every selection is a bare caret (none is being extended). */
    readonly allEmpty: boolean;
    /** Milliseconds since this document last changed. */
    readonly msSinceEdit: number;
}

/** A selection change that may sound: carets only, not an edit's own caret move. */
export function caretMoveMaySound(f: SelectionFacts): boolean {
    return f.allEmpty && f.msSinceEdit >= EDIT_SELECTION_WINDOW_MS;
}

/** One content change as VS Code reports it (the fields read here). */
export interface ChangeLike {
    readonly text: string;
    readonly rangeLength: number;
}

/**
 * A document change that may have changed the pitch of the note it was typed into:
 * one small change whose new text holds a letter (a pitch name, an accidental) or
 * an octave mark. The shape is loose on purpose — smart typing relocates an octave
 * mark by REPLACING a span (`c4` + `'` → `c'4`), so the change is not always a bare
 * insertion — and AuditionMemory.typed() is what keeps an unchanged pitch silent.
 * A digit, a dot, a space or a bracket alone never asks; a paste (a long text) or a
 * multi-cursor edit never asks.
 */
export function mayChangeATypedPitch(changes: readonly ChangeLike[]): boolean {
    if (changes.length !== 1) { return false; }
    const c = changes[0];
    if (c.text.length === 0 || c.text.length > 8 || c.rangeLength > 8) { return false; }
    return /[A-Za-z',]/.test(c.text);
}

/**
 * The trailing-edge debounce both automatic auditions use: each `poke` restarts the
 * wait, and only the last one's action runs. `cancel` drops a pending one (an edit
 * arrived while a caret move was waiting). The timer functions are injectable so
 * the tests can drive the clock.
 */
export class Debouncer {
    private handle: unknown;

    constructor(
        private readonly delayMs: number,
        private readonly setTimer: (fn: () => void, ms: number) => unknown = setTimeout,
        private readonly clearTimer: (h: unknown) => void = h => clearTimeout(h as ReturnType<typeof setTimeout>),
    ) { }

    poke(action: () => void): void {
        this.cancel();
        this.handle = this.setTimer(() => { this.handle = undefined; action(); }, this.delayMs);
    }

    cancel(): void {
        if (this.handle !== undefined) {
            this.clearTimer(this.handle);
            this.handle = undefined;
        }
    }

    get pending(): boolean {
        return this.handle !== undefined;
    }
}
