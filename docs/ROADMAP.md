# Lily# roadmap

Features proposed and not yet built, with what was decided about each. A feature leaves this
file when it ships (its CHANGELOG entry says the rest) or when it is decided against (it moves
to *Decided against* with the reason). What is being worked on now, and the next step, is
`docs/HANDOFF.md` §1 — not here.

---

## Proposed

### Score review for people (proposed 2026-10-30, the owner's design)

The composing checks, given a second entrance for a person's own piece: the AI tool
`lilysharp_reviewScore` returns a structured report (facts, strengths, and findings, each with its
kind, why, fix, fine-when and ask), which the AI explains to the person — above all to a beginner.
A command and `lysc review` do the same without an AI. The review adds key checks: the key heard
against the key written, notes outside the key, spelling by direction, and chords outside the
scale. The design and the order of work are in `docs/review-tool-design.md`.

### Page-turn check (proposed 2026-10-25)

Whether each page ends where the player can turn — on a rest, or a held note — and a mark where
it does not. It would be turned on as the voice-leading check is, by the check pragma:
`check pageTurns` (docs/voice-leading-design.md §7).

### MIDI keyboard input (deferred by the owner 2026-10-25)

Step input from a MIDI keyboard. Deferred until there is a keyboard to try it with.

---

## Decided against

- **Editing the score with the mouse in the preview** (2026-11-03): built in one long day and all taken
  out: dragging notes (by staff position, by octaves with Ctrl, by semitones with Alt), tempos,
  dynamics, articulations, chord shapes, clefs and key signatures, some of them redrawn as the mouse
  moved, a right-click menu on a note, and a padlock in the toolbar against a slip of the hand. The
  owner's reasons: typing was faster; it was inconsistent with the way other things are edited; the
  caret on a note and `Ctrl+Shift+Up` / `Down` is handier, and is where people should be led; a key
  signature's or a dynamic's change reflows the systems under the hand; and an operation this
  unimportant should not need a lock button. The preview is for reading and playing: a click goes
  to the source. The drags are at `59e4aff37` to `db0f2d9c9` should anyone ask again.- **A range CodeLens on each part's line** (2026-10-25): the section CodeLens already stands
  between the lines, and the range is not needed while writing; the preview's Range panel
  answers it when asked.
- **An overflow (⋯) menu for the preview's header** (2026-10-25): the header's groups wrap as
  one onto the next row, and the practice controls live in their own drop-down.
