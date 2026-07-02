# Visual regression review

## Why this exists

The SVG snapshot tests (`SvgSnapshotTests`, ~125 fixtures) are a byte-identical
gate: they **detect** every output change, but they cannot **judge** one. That
made them a one-way ratchet — refactors are protected, but any fix that
*intentionally* changes geometry (LilyPond-mimicry corrections, spacing/skyline
fixes, the M5–M8 / L1–L10 latent layout items) stalls, because there was no way
to see whether the new output is better.

This harness closes that gap: when snapshots mismatch, the test run itself
produces a reviewable visual report. The reviewer judges in a browser and
approves per fixture. Detection stays byte-exact; judgment becomes human.

## Workflow

```
# 1. Make a geometry-affecting change, then run the snapshot gate:
dotnet test --filter "FullyQualifiedName~SvgSnapshotTests"

# 2. Open the report every failing fixture was added to:
artifacts/visual-diff/report.html
#    Views per fixture: Side-by-side | Overlay (opacity slider) | Blink | Diff heatmap.
#    The index is sorted by diff magnitude; "changed region" localizes small diffs.

# 3. Approve what is genuinely better:
pwsh tools/Approve-Snapshots.ps1 -Name test/notes         # one fixture
pwsh tools/Approve-Snapshots.ps1 -Name 'test/multi-*'     # a family
pwsh tools/Approve-Snapshots.ps1                          # everything in the report

# 4. Re-run the gate. Approved fixtures are green; anything you did NOT
#    approve keeps failing — an unapproved diff is a regression by definition.
dotnet test --filter "FullyQualifiedName~SvgSnapshotTests"
```

`LILYSHARP_UPDATE_SNAPSHOTS=1` still works as the bulk approval path, but
prefer the script: it forces the approve set to be an explicit, reviewed list.

## How it renders

- Rasterization uses the repo's own `PngGenerator.ConvertSvgToPng` (Svg.Skia +
  the bundled Emmentaler OTF), at scale 2 (192 DPI) so sub-pixel staff-space
  shifts are visible.
- **Both sides are rendered by the same Skia in the same process**, so the
  pixel comparison is exact: any red pixel in the heatmap is a real output
  change, never raster noise. Area covered by only one side (page grew or
  shrank) counts as changed.
- The `.baseline.svg` / `.actual.svg` files are the exact byte strings the
  snapshot gate compared; the approve script promotes `actual` verbatim, so an
  approved fixture is guaranteed green on the next run. The raw SVGs render in
  a browser too, but only with Emmentaler installed locally — the PNGs are the
  authoritative rendering.
- `artifacts/` is transient (gitignored) and cleared at the start of each test
  process, so the report always reflects the latest run only.

## Judging guidance

- This project's layout ground truth is LilyPond (`lily/*.cc`, see
  `feedback: mimic LP source`). For a mimicry fix, judge the change against the
  LP behavior the fix cites, not against personal taste.
- Small diff% is not automatically safe (a stem attached to the wrong side can
  be 0.01%), and large diff% is not automatically wrong (a spacing fix
  legitimately reflows everything after it). Use Blink for the former, Overlay
  for the latter.
- If a fixture's change is plausible but you cannot decide from the corpus,
  add a minimal fixture that isolates the behavior (below) before approving.

## Programmatic snapshots (API-only features)

Features the `.lys` grammar cannot reach yet (e.g. `RemoveEmpty` hara-kiri)
are pinned through `ProgrammaticSnapshot.Assert(name, svg)` (see
`HaraKiriVisualTests`): the score is built through the model API, rendered,
and compared byte-identical against `Snapshots/programmatic__<name>.svg`.
Mismatches — and newly created baselines — land in the same
`artifacts/visual-diff/report.html`, and `Approve-Snapshots.ps1` approves them
by the same name (`-Name programmatic/hara-kiri`). The first such fixture
immediately caught a real defect (a hidden staff's clef and rests overprinted
the visible staff), which is exactly the point: build the fixture, LOOK at the
report, then commit the baseline.

## Adding coverage

1. Put a minimal `.lys` under `LilySharp.Tests/Fixtures/test/` (or
   `showcase/`).
2. Add it to `TestSamples()` / `ShowcaseSamples()` in `SvgSnapshotTests.cs`
   with a comment saying what it pins.
3. Run the filter once — the baseline is created and the test fails once
   asking for a re-run; check the rendered PNG in the report (or approve after
   eyeballing) and commit the new snapshot together with the fixture.
