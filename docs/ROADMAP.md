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

- **More mouse editing in the preview: a right-click menu on a note** (2026-11-03): rest, chord
  member out, marks. Built and taken back the same day (`425fc8cb5`, reverted). The owner found
  typing faster, and does not want the mouse to become the way Lily# is written: people are to be
  led to the keyboard. What `Ctrl+Shift+Up` can step may be dragged; beyond that, no new mouse
  editing, and no more menus.
- **Dragging anything but a note or a tempo in the preview** (2026-11-03): dynamics, articulations, chord
  symbols, diagrams and clefs were all draggable, the dynamics redrawn as the mouse moved to see it and
  judge. The owner took them out: not intuitive, at odds with how other symbols are edited, and
  `Ctrl+Shift+Up` does the same while leading people to the editor. A note's height is the staff's
  own direction, and a tempo's drag was liked.
- **Dragging a key signature in the preview** (2026-11-03): tried, with the signature redrawn as the
  mouse moved. A key changes the signature's width, so the systems reflow and the staves move under
  the hand; the owner took it out. `Ctrl+Shift+Up` steps a key. The time signature was never offered.
- **A range CodeLens on each part's line** (2026-10-25): the section CodeLens already stands
  between the lines, and the range is not needed while writing; the preview's Range panel
  answers it when asked.
- **An overflow (⋯) menu for the preview's header** (2026-10-25): the header's groups wrap as
  one onto the next row, and the practice controls live in their own drop-down.
