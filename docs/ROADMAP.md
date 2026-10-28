# Lily# roadmap

Features proposed and not yet built, with what was decided about each. A feature leaves this
file when it ships (its CHANGELOG entry says the rest) or when it is decided against (it moves
to *Decided against* with the reason). What is being worked on now, and the next step, is
`docs/HANDOFF.md` §1 — not here.

---

## Proposed

### Page-turn check (proposed 2026-10-25)

Whether each page ends where the player can turn — on a rest, or a held note — and a mark where
it does not.

### Voice-leading check (proposed 2026-10-25)

Warnings for parallel fifths and octaves, crossed voices, and voices too far apart, for
four-part (SATB) writing — church choirs and choral arranging. Designed 2026-10-28
(`docs/voice-leading-design.md`): a general check pragma, `check voiceLeading s a t b`, turns it on;
the findings are information, not warnings. Stage 1 is built (one voice per part); the two
voices of one staff and the hidden fifths are stages 2 and 3 of the design.

### MIDI keyboard input (deferred by the owner 2026-10-25)

Step input from a MIDI keyboard. Deferred until there is a keyboard to try it with.

---

## Decided against

- **A range CodeLens on each part's line** (2026-10-25): the section CodeLens already stands
  between the lines, and the range is not needed while writing; the preview's Range panel
  answers it when asked.
- **An overflow (⋯) menu for the preview's header** (2026-10-25): the header's groups wrap as
  one onto the next row, and the practice controls live in their own drop-down.
