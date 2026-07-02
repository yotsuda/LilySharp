# Lily# Samples

Complete public-domain pieces written in Lily# (`.lys`). Render any of them with:

```bash
lysc svg samples/fur-elise.lys        # or: png / pdf / midi / xml
```

| Sample | Piece | What it shows off |
|--------|-------|-------------------|
| [`fur-elise.lys`](fur-elise.lys) | Beethoven — Für Elise (excerpt) | Grand staff, `octave absolute`, `phrase`/`$ref` reuse, `partial` pickup, *pp* |
| [`greensleeves.lys`](greensleeves.lys) | Traditional — Greensleeves | Melody + chord symbols (nameless `chords { }`) + verse lyrics with `~` melismas, 6/8 |
| [`canon-in-d.lys`](canon-in-d.lys) | Pachelbel — Canon in D (excerpt) | A 4-bar `phrase` ground cycled 8×, `octave 3` bass re-anchor, `R1`, variation writing |

All samples use `octave absolute` — every pitch is anchored to C4 (`c'` = C5,
`c,` = C3, with `part { octave N }` re-anchoring a bass part), so a wrong octave
never cascades into the following notes. It is the recommended mode when a tool
— or a human — writes notes it cannot immediately play back.

The test suite compiles every sample on every run, so they can never rot.
