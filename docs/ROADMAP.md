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

- **A range CodeLens on each part's line** (2026-10-25): the section CodeLens already stands
  between the lines, and the range is not needed while writing; the preview's Range panel
  answers it when asked.
- **An overflow (⋯) menu for the preview's header** (2026-10-25): the header's groups wrap as
  one onto the next row, and the practice controls live in their own drop-down.
