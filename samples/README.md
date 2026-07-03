# Lily# Samples

Complete public-domain pieces written in Lily# (`.lys`). Render any of them with:

```bash
lysc svg samples/fur-elise.lys        # or: png / pdf / midi / xml
```

| Sample | Piece | What it shows off |
|--------|-------|-------------------|
| [`fur-elise.lys`](fur-elise.lys) | Beethoven — Für Elise (excerpt) | Grand staff, `octave absolute`, `phrase`/`$ref` reuse, `partial` pickup, *pp* |
| [`greensleeves.lys`](greensleeves.lys) | Traditional — Greensleeves | `staff … with chords` (one progression above the staff AND as a grid), verse lyrics with `~` melismas, 6/8 |
| [`amazing-grace.lys`](amazing-grace.lys) | Traditional — Amazing Grace | Lead sheet: melody + chords + lyrics, `partial` pickup, melismas, 3/4 |
| [`drunken-sailor.lys`](drunken-sailor.lys) | Traditional — Drunken Sailor | STAFF-LESS song sheet: chord grid + stacked verses (1./2.), and a lyrics-only text sheet from the same parts |
| [`canon-in-d.lys`](canon-in-d.lys) | Pachelbel — Canon in D (excerpt) | A 4-bar `phrase` ground cycled 8×, `octave 3` bass re-anchor, `R1`, variation writing |

All samples use `octave absolute` — every pitch is anchored to C4 (`c'` = C5,
`c,` = C3, with `part { octave N }` re-anchoring a bass part), so a wrong octave
never cascades into the following notes. It is the recommended mode when a tool
— or a human — writes notes it cannot immediately play back.

The test suite compiles every sample on every run, so they can never rot.
