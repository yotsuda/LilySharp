# Lily# roadmap

Features proposed and not yet built, with what was decided about each. A feature leaves this
file when it ships (its CHANGELOG entry says the rest) or when it is decided against (it moves
to *Decided against* with the reason). What is being worked on now, and the next step, is
`docs/HANDOFF.md` §1 — not here.

---

## Proposed

### Chord diagrams: one key doing four jobs (proposed 2026-10-30, the owner's question)

The owner: "`layout { chordDiagrams }` seems out of place in a layout block." The grammar is to be
settled before any code: the next session starts by putting §4's questions to the owner.

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
by the sound of a `chord(Eb x32010)` item, by the header's "Capo N" line, by the LilyPond twin and
by MusicXML.

**2. What stays: decided, not to reopen** (HANDOFF §2 K0, K6)
- The TUNING a diagram is drawn for is the SCORE's (`layout`). This lets one source make a piano
  score (`none`) and a guitar or ukulele score. Without it, the part's instrument decides; failing
  that, `guitar`.
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
- **The capo changes the sound** (a `chord(Eb x32010)` item, the MIDI), yet it lives in a block
  whose rule is "a switch that picks a drawing" (the remarks on `LayoutPlan`). The shapes written
  IN THE MUSIC are pressed shapes, relative to the capo, so the capo is part of what those shapes
  mean.

**4. Proposed grammar** (Q marks a question for the owner)

```
chordShapes {                              // top level, written once: the shapes (data)
  Cm7 x35343
  G(guitar 320003  ukulele 0232)           // a chords row's own spelling for two tunings
  section Chorus { C x35553 }
}
layout {
  chordDiagrams none | TUNING | all | TUNING all   // whether, and for what (the edition)
  capo N                                           // its own key, with or without diagrams
  chordNames shape | sounding | both
  chordList true | false
}
```

- **Q1: the block's name.** `chordShapes { }` says what it holds. `chords shapes { }` would sit
  beside the `chords NAME { }` rows, but a row is music and this is not.
- **Q2: where the capo lives.**
  - **(a) A `layout` key `capo N`.** It is the edition's, as the tuning is: the guitarist's chart
    has it and the piano score does not. It is the smallest change and removes the tie to the
    diagrams. But a score without the capo then reads `chord(Eb x32010)` as a shape that does not
    sound E♭.
  - **(b) Beside the shapes, at file level** (`chordShapes capo 3 { … }`). The shapes written in
    the music are relative to it, so it is data. An edition then chooses only the NAMES
    (`chordNames sounding` in the piano score) and whether "Capo 3" prints.
  - **(c) A part property.** K6 rejected a per-staff TUNING override; a capo is the player's, but a
    chords row belongs to no part.
  
  Recommended: **(b)**, with the "Capo N" line printed only where diagrams or pressed names print.
- **Q3: `all`.** It can stay a `chordDiagrams` word (`chordDiagrams guitar all`), or become a key
  of its own (`chordDiagrams guitar` plus `diagramsFor all | written`). Recommended: keep the word.

**5. Migration.** The old spellings become errors, each with a quick fix that writes the new one:
`layout { chordDiagrams guitar capo 3 { … } }` becomes `chordShapes … { … }` plus
`layout { chordDiagrams guitar  capo 3 }`, or the Q2 answer's form. For every book that converts,
the page, the LilyPond twin, MusicXML and the MIDI stay unchanged.

Files and places to touch:
- `LayoutPlan`, the reader, and a new top-level directive (parser, syntax facts). The table's
  semantic validators move with it.
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
