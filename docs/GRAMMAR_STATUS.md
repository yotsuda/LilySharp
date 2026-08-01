# Lily# Grammar — Doc Map, Coverage & Known Gaps

This is the **index** for the grammar documentation plus a high-level coverage
summary. It does not re-list syntax — see the references below for that.

## Which grammar doc to read

| Doc | Role | When to read |
|-----|------|--------------|
| [`GRAMMAR_FOR_LLM.md`](GRAMMAR_FOR_LLM.md) | **Canonical spec** — compressed, every example parse-verified | Authoring `.lys`; dropping into an LLM's context |
| [`GRAMMAR.md`](GRAMMAR.md) | **Formal EBNF** of the same language | You want the precise grammar productions |
| [`SYNTAX_REFERENCE.md`](SYNTAX_REFERENCE.md) | Human reference (tables, longer examples) | A worked, browsable reference |
| this file | Doc index + coverage + known gaps | "Is X implemented?" / "what's missing?" |

The parser is the ultimate authority; the three spec docs above are kept in sync with it.

## Coverage at a glance (syntax in `GRAMMAR_FOR_LLM.md`)

All ✅ implemented:

- **Core music** — notes / rests / spacers / full-measure rests, chords, relative
  AND absolute octave modes (`octave absolute`), accidentals, dotted & tremolo
  durations.
- **Connectors** — ties, slurs (over notes *and* chords), automatic & manual beams.
- **Groupings** — tuplets (nested), grace / acciaccatura / appoggiatura, multi-voice
  (`voice { } { }`).
- **Annotations** (`@name`, with `.up`/`.down`) — articulations (incl. staccatissimo,
  up/down-bow, harmonic), ornaments, dynamics (incl. the sfz family), free text
  `@text("…")`, hairpins, `@stemUp/@stemDown`, `@courtesy`/`@editorial`, arpeggio,
  glissando, figured bass, inline chord names, half ties, cue/cross/dead, feathered
  beams.
- **Structure** — parts, phrases (`$ref`), sections, named `form`s bound by `score <Name>`,
  repeats `|: :|` (`:|*N`), volta endings, navigation marks & spanners (segno/coda/fine/
  D.S./D.C., rit/accel, ottava, trill spanner, pedals), `break`.
- **Render targets** — staff, grandStaff, tab, ossia, per-staff attachments
  (`staff X with chords NAME` above / `staff X with lyrics NAME` below, repeatable to
  stack verses), and **staff-less lead sheets** (`chords name` / `lyrics name` rows drawn
  as a barline grid); `tempo … swing`; `override`/`revert`.
- **Output** — SVG / PDF / PNG engraving, MIDI, MusicXML (partial — see gaps).

## Known gaps (not implemented)

- **Cross-staff beam layout** — a beam spanning two staves of a grand staff. (Multi-staff
  rendering otherwise works; the upstream layout does not yet emit cross-staff beams.)
- **MusicXML export: lyrics and tuplet numbers** — parsed but not emitted; the rest of
  MusicXML (notes, ties, slurs, grace, dynamics, articulations, ornaments, multi-part)
  is exported.
- **Multi-file projects** (`include` across files) and a **LilyPond → Lily# converter**.
- The long tail of specialist notation (early music, microtonal/maqam, fretboard
  diagrams, chord grids, clusters, ambitus, …) is intentionally out of scope.
