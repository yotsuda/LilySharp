# Development & Bugfix Workflow

Practical procedures and hard-won tips for maintaining Lily#. Written for
AI-assisted development sessions (Claude et al.) as much as for humans —
every rule here was earned by an actual mistake or verified procedure.

## Golden rules

1. **Layout code mimics LilyPond — never invent.** Every layout fix must be
   modeled on the LilyPond source at `C:\MyProj\lilypond-src` (mainly
   `lily/*.cc`, `scm/define-grobs.scm`, `mf/` for fonts) and carry a
   `LILYPOND-REF:` comment naming the file/lines it follows. If you cannot
   find the LP counterpart, say so instead of guessing a formula.
2. **Run the FULL suite before every commit.** `dotnet test` (~2130 tests,
   15–40 s). Do not chain `dotnet test … ; git commit …` in one command —
   read the test result first, then commit. (Three commits in one session
   had to be amended because a red suite was piped straight into commit.)
3. **Push only on explicit owner GO.** Commit freely on `master`; `git push`,
   tags, releases and any public announcement wait for the owner.
   No unprompted branch creation either — work happens on `master`.
4. **`git add` explicit paths only.** Never `git add -A` / `git add .` —
   scratch files, dogfood renders and session notes live in the tree.
5. **Green tests ≠ correct output. Look at the picture.** Render a PNG and
   visually inspect it before declaring a layout change done (see below).

## The core loop

```powershell
dotnet build                                  # or let dotnet test build
dotnet test                                   # full gate, always before commit
dotnet test --filter "FullyQualifiedName~X"   # fast iteration on one area

# Render and LOOK at the result (scratch/dogfood/ is the dumping ground):
dotnet run --project LilySharp.Cli -c Debug --no-build -- check  file.lys
dotnet run --project LilySharp.Cli -c Debug --no-build -- png    file.lys out.png
dotnet run --project LilySharp.Cli -c Debug --no-build -- xml    file.lys out.xml
dotnet run --project LilySharp.Cli -c Debug --no-build -- midi   file.lys out.mid
```

- The CLI lives in `LilySharp.Cli` and must be built before `--no-build` runs.
- MusicXML changes: export and grep the elements you expect
  (`<measure-repeat>`, `<arpeggiate>`, …). Measure/note COUNTS catch whole
  classes of bugs (a flush bug once silently dropped every repeat after the
  first — visible instantly as "2 measures, 5 notes" instead of "4, 13").
- MIDI changes: parse the SMF bytes (status-byte scan per channel) rather
  than trusting playback; channel distribution and bend values are cheap to
  assert.

## Snapshot tests (visual regression)

Intentional visual changes fail `SvgSnapshotTests`. Procedure:

1. Render the affected fixture to PNG and **visually verify the change is
   the one you intended** — the diff report helps but is not the decision.
2. `pwsh tools/Approve-Snapshots.ps1 -Name test/xxx`
3. Re-run the suite; commit fixture + snapshot together with the code.

Fixtures live in `LilySharp.Tests/Fixtures/test/`, snapshots in
`LilySharp.Tests/Snapshots/`. See `docs/visual-regression.md`.

## LSP / VS Code extension

- Deploy to the INSTALLED extension with `pwsh tools/Deploy-Lsp.ps1`, then
  run **"Developer: Reload Window"** in VS Code. Publishing into the repo's
  `editors/vscode/server` alone does NOT reach the installed extension.
- If the server DLL is locked, kill the `pwsh`/`dotnet` console hosting it
  and redeploy. Verify the deployed version via the DLL's ProductVersion
  (stamped `0.x.y+<commit-sha>`).
- Release packaging is tag-driven (`.github/workflows/release.yml`): the tag
  `vX.Y.Z` stamps every artifact; the vsix job publishes the server into the
  extension before `vsce package`. See the workflow for the exact steps —
  it was dry-run locally end to end.

## Architecture in one paragraph

Pipeline: **Lexer → Parser** (green/red syntax tree) **→ MeasureCollector**
(syntax → measure/item model, `Svg/Collector/`) **→ Engravers**
(`Svg/Layout/*Engraver.cs` + `LayoutEngine.cs`, produce positioned layouts)
**→ SharedRenderer** (`Rendering/`, draws to an `IDrawingContext`) → SVG /
PNG / PDF. Exporters (`MusicXml/`, `Midi/`, VSQX) are separate tree walks
that deliberately mirror the collector's state machines (octave tracking,
pickup handling — keep them in sync when touching one). The LSP
(`LilySharp.Lsp`) reuses the core end to end. Deeper dives:
`docs/SVG_LAYOUT_ARCHITECTURE.md`, `docs/SKYLINE_ARCHITECTURE.md`,
`docs/COORDINATE_SYSTEM.md`, `docs/GRAMMAR.md`.

## Change-type checklists

These are the cross-cutting registration points that are easy to miss.
Drift canaries exist for some (the suite fails with an explicit message),
but not all.

### New syntax node
- Green class in `Syntax/InternalSyntax/GreenNodes.cs` — **slot arrays must
  preserve source token order** (span fidelity depends on it).
- Red class in `Syntax/SyntaxNodes.cs`; factory case in `SyntaxNode.cs`
  (the big kind→class switch); `SyntaxKind` entries; lexer keyword if any.
- A loose-parse pattern that works well: store the block body
  token-for-token in the green node and interpret it in a red-node property
  (see `DrummapDeclarationSyntax.Entries`).

### New articulation / annotation
Touch ALL of: `MusicEnums.cs` (enum) → `ArticulationRegistry.cs` (name) →
`AnnotationNameValidator.cs` (bare name and/or parenthesized forms) →
`ArticulationItem.GetGlyph` (glyph char, or a sentinel string like
`"tabtech:X"` / `"bendScoop"`) → `ArticulationEngraver` (placement; extents
via `GetGlyphBBox`/`GetSeedBBox` if the ink is bigger than the 0.5×0.5
fallback — undersized extents break skyline avoidance) → `SharedRenderer`
(custom drawing for sentinels) → `MusicXmlExporter` (articulations/technical
mapping). Grep an existing articulation (e.g. `SnapPizz`) to find every site.

### New Emmentaler glyph
Never guess a codepoint. Render a cmap contact strip (SkiaSharp script over
`LilySharp.Core/Fonts/emmentaler-20.otf`, label each U+E0xx) and READ it;
anchor against known-verified constants in `EmmentalerGlyphs.cs`. Some
LilyPond glyph names do not exist in Emmentaler at all (spiccato,
schleifer) — absence is a legitimate finding, document it and move on.

### New stateful field on `Staff`
Fold it into `MeasureContentKey.AddStaffIdentity` AND the allow-list in
`IncrementalReuseSoundnessTests.StaffIdentity_AccountsForEveryStatefulStaffField`.
The canary fails with instructions if you forget — read its message, it is
the spec.

### New slot on the `MidiNote` record
Use **named arguments at every construction site**. A positional insertion
once silently shoved `SourceOrdinal` into the new `QuarterBend` slot with no
compile error. `MidiExportShapeTests` pins this class of bug — extend it
when adding slots.

### Exporter parity
`MusicXmlExporter` and `MidiExporter` each re-implement relative-octave
resolution, default durations, pickup auto-close, tie pairing. A grammar
feature added to the collector usually needs the same handling in both
exporters — check all three before closing.

## Writing test .lys files (recurring authoring mistakes)

- **Part names must not collide with note/drum vocabulary**: a part named
  `g`, `p`, `b8`, `bd` breaks parsing in confusing ways. Use `m`, `pno`,
  `kit`, `vc`, …
- **Relative octave is the default** and resolves the FIRST note nearest C4
  (`g`/`a`/`b` land in octave 3). For pitch-exact tests, start with
  `octave absolute` and qualify (`c'` = C5 in absolute mode — note that
  MIDI pitch assertions must match THAT, e.g. `c'` → 72).
- `r1` is a plain beat-anchored rest; `R1` is the centered whole-measure
  rest. They are different items.
- Drum names: only the ~31 kit instruments are in the static registry
  (`DrumNameRegistry`); timbales etc. are NOT — an unknown name parses as a
  variable reference and warns about `$`.
- Bare identifiers in music are drum names; variables need `$`.

## Editing files in AI sessions (tooling gotchas)

- **Line endings are MIXED** (CRLF and LF within the repo, sometimes within
  a file). String-replace helpers must try both (`$old` and
  `$old -replace "\n","\r\n"`); when a replace "MISSes", switch to the
  editor tool or `[System.IO.File]::ReadAllText` + `IndexOf`/`Insert`
  instead of fighting quoting.
- PowerShell single-quoted here-strings corrupt content containing `':'`,
  `'{'` or `\uXXXX` sequences — one such corruption once truncated a source
  file mid-comment and broke the build. For code containing those, use the
  editor tool, not shell string surgery. PUA glyph literals (U+E0xx) DO
  survive `[System.IO.File]::WriteAllText` — verify by re-reading the
  codepoint, not by eyeballing console output (PUA renders as boxes).
- After writing a file via shell, the editor tool may refuse with
  "modified since read" — re-read the region, then edit.

## Debugging layout: the proven sequence

1. Reproduce in a minimal `.lys` in the scratch area; `check` first
   (diagnostics), then `png`.
2. **Read the PNG.** Identify WHAT is wrong (position, glyph, spacing)
   before touching code.
3. Find the LilyPond counterpart (`git grep` in `lilypond-src/lily`) and
   read how LP computes it. The fix is a faithful port, commented
   `LILYPOND-REF`.
4. Re-render, re-read the PNG, run the suite, approve snapshots if the
   change is intended, commit with explicit paths.

Known architecture facts that save re-derivation: the computation layer is
already Y-up (`docs/STAGE4_YUP_INVERSION.md`); fret frames and articulations
participate in the outside-staff skyline via their `Ink` boxes
(`OutsideStaffStacker`, `LayoutEngine`); chord names position against the
system up-skyline, so anything that seeds real ink gets avoided
automatically.

## Licensing (when adding assets or dependencies)

The project is GPL-3.0-or-later; every new source file gets the standard
15-line header. New fonts/assets: record origin + license next to the asset
(see `Fonts/Emmentaler-LICENSE.txt` — Emmentaler is GPL/OFL dual-licensed,
NOT plain OFL) and add an entry to `THIRD-PARTY-NOTICES.md`; the release
workflow bundles that file into the vsix and every CLI archive.
