# Lily# Grammar — Doc Map, Coverage & Known Gaps

This is the **index** for the grammar documentation plus a high-level coverage
summary. It does not re-list syntax — see the references below for that.

## Which grammar doc to read

| Doc | Role | When to read |
|-----|------|--------------|
| [`GRAMMAR_FOR_LLM.md`](GRAMMAR_FOR_LLM.md) | **Canonical spec** — compressed, every example parse-verified | Authoring `.lys`; dropping into an LLM's context |
| [`GRAMMAR.md`](GRAMMAR.md) | **Formal EBNF** of the same language | You want the precise grammar productions |
| [`SYNTAX_REFERENCE.md`](SYNTAX_REFERENCE.md) | Human reference (tables, longer examples) | A worked, browsable reference |
| [`GRAMMAR_AUDIT.md`](GRAMMAR_AUDIT.md) | **Open grammar defects** — ambiguities, gaps, and vocabulary that disagrees with the engine | Changing the language, or picking up what is unfinished |
| this file | Doc index + coverage + known gaps | "Is X implemented?" / "what's missing?" |

The parser is the ultimate authority; the three spec docs (`GRAMMAR_FOR_LLM.md`, `GRAMMAR.md`, `SYNTAX_REFERENCE.md`) are kept in sync with it. `GRAMMAR_AUDIT.md` is the opposite kind of document: it records where they and the engine disagree.

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
- **Structure** — parts, phrases (bare-name refs), sections, named `form`s bound by `score <Name>`,
  repeats `|: :|` (`:|*N`), volta endings, navigation marks & spanners (segno/coda/fine/
  D.S./D.C., rit/accel, ottava, trill spanner, pedals), `break`.
- **Render targets** — staff, grandStaff, tab, ossia, rows placed by ORDER (score =
  a vertical stack of bands: `chords NAME` directly above a staff aligns the symbols
  over it, a bound `lyrics NAME` row directly below is that staff's verse, a run of
  rows stacks as verses), and **staff-less lead sheets** (`chords name` / `lyrics name` rows drawn
  as a barline grid); `tempo … swing`; `override`/`revert` (**four properties** —
  `NoteHead.transparent`, `Stem.transparent`, `NoteHead.color`, `Stem.color`; anything
  else is refused by LYS1029 rather than silently ignored, and the list grows. All four
  take effect — the vocabulary and the engine agree in both directions since 2026-08-23).
- **Output** — SVG / PDF / PNG engraving, MIDI, MusicXML (partial — see gaps).

## Known gaps (not implemented)

- **`NoteColumn.force-hshift`** — refused by LYS1029 while its implementation is disabled
  (`ElementCoordinator.cs`, `ForceHshiftEnabled = false`: the current code cannot do the
  per-voice, magnitude-honoring shift the property is for; the resolver is kept intact).
  The vocabulary row and this line resolve together when the proper implementation lands.
  (✅ The former entry here — `NoteHead.color`/`Stem.color` implemented but refused —
  was fixed 2026-08-23: the rows were added, so colour is simply supported now.)

- **Cross-staff beam layout** — a beam spanning two staves of a grand staff. (Multi-staff
  rendering otherwise works; the upstream layout does not yet emit cross-staff beams.)
- **MusicXML export: lyrics and tuplet numbers** — parsed but not emitted; the rest of
  MusicXML (notes, ties, slurs, grace, dynamics, articulations, ornaments, multi-part)
  is exported.
- A **LilyPond → Lily# converter**.
  (⚠️ **Multi-file projects used to be listed here and that was wrong**: `using "other.lys"`
  is implemented — `Parser/UsingExpander.cs`, depth-first, de-duplicated by full path,
  cycles stop, an unreadable file is inert. Verified 2026-08-15 by declaring a part in one
  file and rendering it from another.)
- The long tail of specialist notation (early music, microtonal/maqam, fretboard
  diagrams, chord grids, clusters, ambitus, …) is intentionally out of scope.
