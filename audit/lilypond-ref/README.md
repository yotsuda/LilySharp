# LilyPond ground-truth comparison

`audit/scripts/Compare-WithLilypond.py` renders equivalent score pairs with
**Lily# and the real LilyPond binary**, extracts notehead X positions from a
chosen staff by pixel analysis, and compares the **normalized gap sequence**
(scale-invariant). It is the arbiter for layout reports:

- deviation from LilyPond → a bug to fix;
- match → the (possibly surprising-looking) spacing is correct engraving.

## Usage

```
py -3 audit/scripts/Compare-WithLilypond.py            # all cases
py -3 audit/scripts/Compare-WithLilypond.py --case even-quarters
py -3 audit/scripts/Compare-WithLilypond.py --keep     # keep PNGs for inspection
```

Requires pillow + numpy and a LilyPond binary (`LILYPOND_EXE`, default
`C:\bin\lilypond-2.24.4\bin\lilypond.exe`). Local audit tool — not wired
into CI (CI runners have no LilyPond).

## Adding a case

Create `cases/<name>/` with:

- `case.lys` — Lily# input. Remember Lily#'s phrase-fresh relative frame
  (a phrase body starts from C4); keep measured notes ON the staff so the
  detector's band finds them.
- `case.ly` — the equivalent LilyPond input (absolute pitches recommended).
- `case.json` — `{ "description", "staff" (0-based from top), "heads"
  (note count in the measured staff), "tolerance" }`.
  Optional `"knownDivergence": "<why>"` marks a tracked mismatch as XFAIL
  so it stays visible without failing the run; remove it when fixed.

## Detector notes / limitations (v1)

- Staff lines found as rows of long horizontal runs; footer text masked out.
- Filled noteheads only (vertical-run window 0.8–2.4 staff spaces; stems and
  barlines masked at ≥2.4). Don't measure staves of half/whole notes.
- The last `heads` blobs are compared, which drops clef/time-signature blobs.

## Current cases

| case | status | encodes |
|---|---|---|
| even-quarters | PASS | four plain quarters space evenly |
| triplet-vs-quarters | PASS (delta 0.013) | beats carrying triplet heads get more room; the beat under a half note gets plain quarter space |
| eighths-vs-quarters | XFAIL | down-stem 8th gaps ~8% narrower than LilyPond — suspected stem_dir_correction divergence (note-spacing.cc) |
