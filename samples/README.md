# Lily# Samples

Complete pieces written in Lily# (`.lys`) — public-domain music, plus two short originals. Render any of them with:

```bash
lysc svg samples/fur-elise.lys        # or: png / pdf / midi / xml
```

| Sample | Piece | What it shows off |
|--------|-------|-------------------|
| [`fur-elise.lys`](fur-elise.lys) | Beethoven — Für Elise (excerpt) | Grand staff, `octave absolute`, `phrase` reuse, `partial` pickup, *pp* |
| [`greensleeves.lys`](greensleeves.lys) | Traditional — Greensleeves | a `chords` row above the staff (one progression above the staff AND as a grid), verse lyrics with `~` melismas, 6/8 |
| [`amazing-grace.lys`](amazing-grace.lys) | Traditional — Amazing Grace | Lead sheet: melody + chords + lyrics, `partial` pickup, melismas, 3/4 |
| [`drunken-sailor.lys`](drunken-sailor.lys) | Traditional — Drunken Sailor | STAFF-LESS song sheet: chord grid + stacked verses (1./2.), and a lyrics-only text sheet from the same parts |
| [`canon-in-d.lys`](canon-in-d.lys) | Pachelbel — Canon in D | A 4-bar `phrase` ground cycled 13 times under progressively livelier variations — eighths, sixteenths, solid chords — spilling onto a second page (automatic pagination). |
| [`morning-light.lys`](morning-light.lys) | Original — Morning Light (the README's picture) | One source, four rows: chord symbols, a melody with its lyrics, and a bass part as notation AND as tablature. Written in relative octaves. |
| [`nocturne.lys`](nocturne.lys) | Original — Nocturne in D | Grand staff with pickup, slurs over notes and chords, a triplet, a grace note, hairpins, pedal changes (a second `@sustain` while down), a ritardando. |
| [`manual-beam-demo.lys`](manual-beam-demo.lys) | — (two bars) | Not a piece: manual beams `c8[ d e f g a]` against the automatic beaming of the bar below, and `form main { ~A }` to hide the section label. |

The traditional and classical pieces use `octave absolute` — every pitch is anchored to C4 (`c'` = C5,
`c,` = C3, with `part { octave N }` re-anchoring a bass part), so a wrong octave
never cascades into the following notes. It is the recommended mode when a tool
— or a human — writes notes it cannot immediately play back. The two originals are
written relative, the default and the shorter way to write by hand.

The test suite compiles every sample on every run, so they can never rot.
