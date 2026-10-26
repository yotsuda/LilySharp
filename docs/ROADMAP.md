# Lily# roadmap

Features proposed and not yet built, with what was decided about each. A feature leaves this
file when it ships (its CHANGELOG entry says the rest) or when it is decided against (it moves
to *Decided against* with the reason). What is being worked on now, and the next step, is
`docs/HANDOFF.md` §1 — not here.

---

## Proposed

### Harmony analysis (proposed 2026-10-25)

Lily# already names a chord written in one part from its notes (`<c e g>@chord` → C, inversions
as slash chords — `ChordAutoNameTests`). Harmony analysis widens that to the sound of **all the
parts together**, over time. *Add Chord Track (auto-harmonize)* is another thing — it proposes
chords for a melody (`LilySharp.Core.Harmony.ChordHarmonizer`), where this reads the chords the
parts already sound; its chord fitting may still be worth sharing.

**What it shows**

- Beat by beat, the pitches sounding in every part are gathered; where the sound changes, a
  chord name is given (C, Am7, G7/B …).
- Beside it, the Roman numeral against the key signature (I, vi, V7, ii⁶ …).
- Short notes — passing and neighbour tones — weigh less by how long they sound and how strong
  their beat is, so they are kept out of the chord.

**Where it shows (proposed first version)**

- In the preview, faint, under the lowest staff, switched on and off by a header toggle like
  Range. The file is not changed.
- Hovering a chord says which notes it was read from; clicking it selects its bar.

**Next stage**

- *Write as a chords row*: the analysis added to the file as a `chords` row — a lead sheet from
  a piano score.

**The hard parts**

- **Ambiguous sets.** C6 and Am7 are the same pitches. The key and what comes before and after
  decide; when they cannot, both candidates are shown.
- **Modulation.** A written key change is followed. A passing tonicisation is shown at first as
  an applied chord (V/V) rather than detected as a new key.
- **One line alone.** Where a single melody sounds, no chord can be fixed: "—", or a marked guess.

**To decide before starting**: where it shows (the preview overlay above, or straight into the
file), and what it shows (chord names, Roman numerals, or both).

### Page-turn check (proposed 2026-10-25)

Whether each page ends where the player can turn — on a rest, or a held note — and a mark where
it does not.

### Voice-leading check (proposed 2026-10-25)

Warnings for parallel fifths and octaves, crossed voices, and voices too far apart, for
four-part (SATB) writing — church choirs and choral arranging.

### MIDI keyboard input (deferred by the owner 2026-10-25)

Step input from a MIDI keyboard. Deferred until there is a keyboard to try it with.

---

## Decided against

- **A range CodeLens on each part's line** (2026-10-25): the section CodeLens already stands
  between the lines, and the range is not needed while writing; the preview's Range panel
  answers it when asked.
- **An overflow (⋯) menu for the preview's header** (2026-10-25): the header's groups wrap as
  one onto the next row, and the practice controls live in their own drop-down.
