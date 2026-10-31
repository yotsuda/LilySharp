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

### Chord diagrams: one key doing four jobs (proposed 2026-10-30, the owner's question; grammar decided 2026-10-31)

The owner: "`layout { chordDiagrams }` seems out of place in a layout block." The grammar was
settled before any code — §4 holds the owner's answers (2026-10-31, session 1035); the next step
is to build it, the places §5 lists.

**1. Today** (HANDOFF §2 K, built in sessions 664–668):

```
layout {
  chordDiagrams none | [TUNING] [capo N] [all] [{ Cm7 x35343  G  section Chorus { C x35553 } }]
  chordNames shape | sounding | both       // under a capo: the pressed name, the sounding one, both
  chordList true | false                   // the score's chords under the title
}
```

Code: `Semantics/LayoutPlan.cs` (`ChordDiagramsKey`; the plan's `ChordDiagrams`,
`ChordDiagramsAll`, `ChordDiagramTable` and the capo), `Semantics/LayoutPlanReader.cs` (the
`chordDiagrams` arm from about line 490, and the table reader), `Music/ChordShapes.cs` (`Drawn`)
and `Music/ChordShapeTable`. The capo is also read by the MIDI (`LayoutPlanReader`, about line 218),
by the sound of a `diagram(Eb x32010)` item, by the header's "Capo N" line, by the LilyPond twin and
by MusicXML.

**2. What stays: decided, not to reopen** (HANDOFF §2 K0, K6; the owner's decision A of 2026-10-31)
- The tuning a diagram is drawn for is the PART's fretted instrument first (`part gt { instrument
  guitar }`); the layout's tuning word reaches only a chord with no fretted part — a chords row, a
  piano's or a singer's chord — failing that, `guitar`. One source still makes a piano score
  (`none`) and a guitar or ukulele chart.
- No per-staff tuning override, no numbers in the source, and a diagram only where a shape is
  written (or with `all`).

**3. What is wrong**
- **The shape table is data inside a display block.** Which shape a chord takes belongs to the
  piece, not to one edition of it. Written in `layout`, a named `layout lp { }` has to copy the table,
  and two scores can show two shapes for one chord without anyone meaning it: "a key written again
  is written whole, table included" (`LayoutPlan`). The table is already keyed by tuning
  (`G(guitar 320003  ukulele 0232)`), so one file-level table can serve every edition.
- **The capo is tied to the diagrams.** `chordDiagrams none capo 3` is refused. So a chord sheet
  with "Capo 3" and pressed names but no diagrams cannot be written, and that is the commonest
  guitar chart.
- **The capo changes the sound** (a `diagram(Eb x32010)` item, the MIDI), yet it lives in a block
  whose rule is "a switch that picks a drawing" (the remarks on `LayoutPlan`). The shapes written
  IN THE MUSIC are pressed shapes, relative to the capo, so the capo is part of what those shapes
  mean.

**4. Decided grammar** (the owner's answers, 2026-10-31, session 1035 — "a well-ordered,
consistent grammar; no regard for backward compatibility")

```
capo 3                                     // top level, once: the fret every pressed shape in the
                                           // file is relative to (1..11); absent = no capo
chordShapes {                              // top level, once: the shapes (data)
  Cm7 x35343
  G(guitar 320003  ukulele 0232)           // a chords row's own spelling for two tunings
  section Chorus { C x35553 }
}
layout {
  chordDiagrams none | written | all       // WHICH chords draw a diagram: none; those with a
                                           // written shape (the default); every chord
  chordTuning guitar | ukulele | …         // the tuning a chord with NO fretted part draws on
                                           // (a chords row, a piano's or singer's chord); default guitar
  chordNames shape | sounding | both       // under a capo, as before
  chordList true | false                   // as before
}
```

- **Q1 — the block's name: `chordShapes { }`.** It says what it holds; `chords shapes { }` would
  use one word for music (a row) and for data.
- **Q2 — the capo: a top-level line `capo N`, file data** ((b′): (b) "beside the shapes" refined
  so that a chart with a capo and no table writes no empty block). Why not a part property (c):
  the `chordShapes` table's shapes are pressed shapes, so the capo must live at the table's level
  or the table's meaning is open; the commonest capo chart is a chords row, which belongs to no
  part; and a per-part capo can be added LATER as an override of the file's (as a part's
  `instrument` overrides the tuning) without unsettling this. Why not a layout key (a): the capo
  changes what `diagram(Eb x32010)` sounds, and a sound must not change with the edition.
  The "Capo N" header line prints only where a score prints diagrams (`chordDiagrams` not `none`)
  or pressed names (`chordNames shape | both`).
- **Q3 — one key, one question.** `chordDiagrams` answers only "which chords"; the tuning for a
  part-less chord is its own key `chordTuning`, so neither key mixes kinds of words (scope words
  against tuning names) and `none` leaves nothing stranded. Rejected: keeping `all` as a
  `chordDiagrams` word (one key, three kinds of word), and `chordDiagrams none | TUNING` plus
  `diagramsFor written | all` (two keys that interfere through `none`).

**5. Migration.** The old spellings become errors, each with a quick fix that writes the new one:
`layout { chordDiagrams guitar capo 3 all { … } }` becomes `capo 3`, `chordShapes { … }` and
`layout { chordTuning guitar  chordDiagrams all }`. For every book that converts, the page, the
LilyPond twin, MusicXML and the MIDI stay unchanged (the gate: render the five `.lys` that write
the old form before and after).

Files and places to touch:
- `LayoutPlan`, the reader, and two new top-level directives — `capo N` and `chordShapes { }`
  (parser, syntax facts, the file-level definitions `MeasureCollector.Definitions` reads). The
  table's semantic validators (`ChordDiagramValidator`, the shape table's) move with it; the capo's
  readers (`LayoutPlanReader` ~218 for the MIDI, `CapoAdvisor`, the header's "Capo N",
  `LilyPondExporter.Chords`/`Marks`, `MusicXmlExporter`) read the file's capo instead of the plan's.
- Completion, colouring (`lilysharp.tmLanguage.json`), hovers, and the step keys
  (`NoteStepper.Words.cs`: the capo and tuning words were added on 2026-10-30).
- GRAMMAR.md, GRAMMAR_FOR_LLM.md, SYNTAX_REFERENCE.md and the site.
- The samples and fixtures that write the old form (`grep -rn "chordDiagrams" --include=*.lys`).
- HANDOFF §2 K: rewrite K1, K2 and K5 in the new spellings.

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
