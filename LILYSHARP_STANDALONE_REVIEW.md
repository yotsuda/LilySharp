# Lily# Standalone Code Review (Phase 1)

> Scope: Lily# internal correctness & design, **without** line-by-line LilyPond-source
> comparison (that is Phase 2). Goal: establish a trustworthy baseline before judging
> "faithful to LilyPond."
> Date: 2026-06-09. Branch: `feature/svg-shared-renderer`.

## Verdict

**The layout core is the real thing.** Beam quanting (least-squares init + quant
generation + lazy scoring, tracking `beam-quanting.cc`), Knuth–Plass line breaking, and
the spring-rod horizontal spacer (a Gourlay / `simple-spacer` port) are a faithful,
substantial LilyPond port with dense `LILYPOND-REF:` anchors. This is high-quality work.

**The dominant structural debt is parallelism: several independent pipelines interpret
the source, they have diverged, and some are dead while still pinned green by tests.**
You cannot meaningfully ask "is this faithful to LilyPond?" until the canonical path is
declared and the dead twins are quarantined — otherwise fixes land in code nobody runs,
and live bugs hide behind passing tests for dead code.

Confidence markers below: **[verified]** = confirmed directly via code/grep in this
review; **[reported]** = surfaced by subagent review, not yet independently confirmed.

---

## 1. The parallel-pipeline problem (headline)

There are **3–4 independent walks of the source**, plus dead twins inside them.

| Pipeline | Path | Status |
|---|---|---|
| **A. Semantic analysis (Roslyn-style)** | `SemanticCompiler` → `SymbolCollector` → `Binder` → `BoundScore` → `ScoreBuilder` | **Orphaned** — no consumer outside `Semantics/` and tests. Not used for any output. **[verified]** |
| **B. Rendering (LIVE)** | `MeasureCollector` → `LayoutEngine` → `SharedRenderer` | The only live path; SVG/PDF/PNG generators all use it. Re-walks the syntax tree independently of A. **[verified]** |
| **C. Export** | `MidiExporter` / `MusicXmlExporter` walk the `SyntaxTree` directly | Independent of A and B; re-implements pitch/duration/relative a third time. **[verified]** |
| **D. LSP diagnostics** | `MeasureValidator` / `DurationValidator` / `SymbolReferenceValidator` | Used by the LSP (`LilySharpLanguageServer.cs:213-231`), **not** by `SemanticCompiler`. **[verified]** |

Dead twins nested within these:

- **`SvgRenderer` (5028 lines) is dead in production.** `SvgGenerator.cs:145` calls
  `SharedRenderer.RenderTo`; `new SvgRenderer` appears only in tests/benchmarks (10
  sites). **[verified]** Its header comment ("migration in progress / feature-incomplete")
  is stale. **However, `SvgRenderer` still implements features `SharedRenderer` lacks**
  (see §3), so it is a *reference to retire after parity*, not deletable today.
- **`SlurEngraver` / `TieEngraver` are dead twins** of `SlurScoringProblem` /
  `TieFormattingProblem` (the versions `ElementCoordinator` actually uses). Tests exercise
  the dead pair. **[reported]**

**Implication:** declare the canonical path, quarantine the rest, repoint tests. Until
then "comprehensive fidelity" is unmeasurable.

---

## 2. Canonical path decision (Phase 1a)

**Canonical = Pipeline B** (`MeasureCollector → LayoutEngine → SharedRenderer`). This
matches the repo's own in-flight "Phase 3" migration (commits: *"switch SvgGenerator to
SharedRenderer + SvgDocumentContext"*). Consequences:

- **`SvgRenderer`** → legacy reference. Retire **after** its remaining features are ported
  to `SharedRenderer` (§3). Do not delete yet.
- **`SlurEngraver` / `TieEngraver`** → reference twins; production is the `*ScoringProblem`
  pair. Mark as such; repoint or delete their tests in favor of the live path.
- **Semantic layer A (`SemanticCompiler`/`BoundTree`)** → *built ahead, not yet wired.*
  This is the stated "Roslyn-style" design goal, so it is aspirational infrastructure, not
  garbage — but today it is orphaned. Either wire B's collector onto it, or clearly mark it
  "not yet integrated." **Do not delete without an explicit product decision.**

> Phase 1a does this **non-destructively**: fix the misleading `SharedRenderer` header,
> add status banners to the dead/orphaned classes, keep this inventory. Actual deletion is
> a separate, approval-gated step.

---

## 3. SharedRenderer ↔ SvgRenderer feature-parity gap (live path)

`SvgRenderer` renders these; `SharedRenderer` does **not** yet (the migration backlog):

1. **Barline types** — double / final / repeat-start / repeat-end / repeat-both + repeat
   dots. `SharedRenderer.DrawBarlines` (`:617-625`) draws one thin rect at *every* measure
   end regardless of type. **[verified]** — visible regression on any score with repeats /
   final barline.
2. **Cross-staff / system SpanBars** connecting staff groups. **[reported]**
3. **Multiple voices per staff** — `SharedRenderer` uses `staff.PrimaryVoice` only
   (`:224,353`); polyphony `<< {} \\ {} >>` loses all but voice 1. **[verified]**
4. **Tablature staves** (string/fret). **[reported]**
5. **Tempo marking from layout** (`DrawTempoMarking`). **[reported]**
6. **Grace slurs** (grace noteheads drawn, connecting slur not). **[reported]**
7. **Cross-staff / kneed beams** — `DrawBeams` uses a single `staffMiddleY` (`:631`).
   **[verified]**
8. **Ossia barlines.** **[reported]**
9. **Real serif text metrics** — `SvgRenderer` measures per-glyph advances;
   `SharedRenderer` uses `Text.Length * factor` estimates (rehearsal box `:1394`, ottava
   `:935`) → box/centering drift. **[reported]**

Shared (not a regression): beams are flat thick lines in both; LilyPond uses sloped quad
stencils (`SharedRenderer.cs:705` admits this).

---

## 4. High-impact correctness bugs (cross-cluster)

### Live render path (B) — user-visible
- **Barline type ignored** (`SharedRenderer.cs:617-625`). **[verified]** High.
- **Only PrimaryVoice rendered** (`:224,353`). **[verified]** High.
- **Key-signature accidental placement per clef is a "rough approximation"** int hack
  (`:318-325`, author's own comment). **[verified]** Medium.
- **Multi-page SVG overlap** — page 2+ gets no Y offset / no new `<svg>`
  (`SvgDocumentContext.cs:58-69`); **PNG reserves height `h*4` from page 0** so page 5+ is
  clipped (`PngDocumentContext.cs:81`). **[reported]** Medium.
- **`PdfDocumentContext.ToBytes()` throws** (requires `_disposed==true` then `Save`s a
  disposed doc; only `GetBytes()` works). **[reported]** Medium (latent, public).

### LSP diagnostics (D) — editor experience
- **No tuplet scaling in `MeasureValidator`/`DurationValidator`** (no tuplet/times/scale
  references; only time-sig parsing). **[verified]** → false "measure incomplete"
  diagnostics on any tuplet. **No pickup/anacrusis concept** → every pickup bar warns.
  Medium.

### Export path (C) — output correctness
- **MusicXML relative octave wrong** — uses `_currentOctave + OctaveOffset` only, ignoring
  the interval/nearest-octave rule that MIDI and the collector implement; XML & MIDI
  octaves disagree for non-adjacent intervals. **[reported]** High.
- **Ties broken** — MIDI re-articulates (no `TieSyntax` case); MusicXML sets `TieStart` but
  `TieStop` is dead → malformed ties. **[reported]** High.
- **Tuplets absent from MusicXML** (no `<time-modification>`, written durations). **[reported]** Medium.
- **MIDI grace adds time instead of stealing** → measure overflow. **[reported]** Medium.

### Shared primitives — wide blast radius
- **`Fraction` arithmetic overflow-unsafe** — int multiply with no widening/checked;
  `Math.Abs(int.MinValue)` throws. Used across B, C, D. **No `Fraction` unit tests at
  all.** **[reported]** High (risk), needs confirmation.
- **`Binder._defaultDuration` leaks across voices** — reset once (`Binder.cs:49`), not per
  voice in `BindWithStructure`. **[verified: single reset site]** Affects orphaned path A.

### Layout core (B) — fidelity
- **`StemCalculator.CalculateBeamedStemInfo` is dead AND unit-inconsistent;
  `BeamScoringProblem` uses constant stem lengths (3.5 / 2.5) for every member.**
  Largest beam-fidelity gap vs LilyPond's per-stem `Stem_info`. **[reported]** High (fidelity).
- **`HorizontalSkyline.Merge` doesn't merge envelopes** (just concatenates); `Distance`
  samples only 3 Y points → collision under-reporting. **[reported]** Medium.
- `BeamScorer.OriginalDistance` scorer never runs; `ScoreForbiddenQuants` constants
  unverified; concaveness slope is per-index not per-x. **[reported]** Medium.

---

## 5. Test-suite credibility

Passing tests currently over-state confidence:
- Most feature tests (`TablatureTests`, `GrandStaffRenderTests`, `IntegrationTests`,
  `GrobOverrideTests`, `ManualBeamTests`) construct **`SvgRenderer` (dead path)**. Only 3
  files hit the live `SharedRenderer` (`SharedRendererBeamTests/PdfTests/PngTests`).
- The dead `SlurEngraver`/`TieEngraver` are tested; the live `*ScoringProblem` less so.
- `MultiVoiceRenderingTests` asserts collector/layout state only — never rendered output —
  so `SharedRenderer`'s missing multi-voice rendering is invisible to CI.
- **Zero** `Fraction` / `DurationCalculator` unit tests. `ArticulationEngraver` quantize
  logic, `VoltaBracketEngraver`, `CustomTextEngraver` untested.

**Action:** repoint feature tests at the live path; add `Fraction` tests; add rendered-output
assertions for barline types and multi-voice (both currently silent failures).

---

## 6. Per-cluster notes (brief)

- **Parser/Syntax:** Roslyn-shaped red/green split is present but **no green-node interning,
  no incremental reparse** (`SyntaxTree.WithChanges` fully re-parses), O(n²)
  `GetChildPosition`, and **synthetic/missing tokens break full-fidelity round-trip**. The
  "incremental compilation" goal is unmet. `Advance()` guard `_position < Count-1` is a
  latent position-drift risk.
- **Semantics:** clean 3-phase shape but orphaned (§1). No tuplet model in duration math;
  `_defaultDuration` voice leak; duplicated measure-duration logic; two diagnostic systems.
- **Layout core:** the strong part. Main gaps are unconsumed LP algorithms (stem-info,
  forbidden-quant/collision scorers) and the non-merging skyline.
- **Engravers:** `TupletBracketEngraver` coordinate-frame bug (`:345,357`, drops the `+2.0`
  staff-middle offset) **[reported]**; hairpin same-measure end ambiguity; pedal end-X uses
  measure start; many hand-tuned magic Y constants with no shared outside-staff stacker.
- **Collectors/Export:** triple reimplementation of pitch/duration/relative is the root
  smell; exporters drop tuplets, mid-piece tempo/key/clef changes, multi-voice, voltas.

---

## 7. Recommended order of work

1. **(1a — this doc) Declare canonical path B; quarantine dead twins (non-destructive).**
2. Fix live-path visible bugs: **barline types → multiple voices → key-sig clef placement.**
3. Fix LSP tuplet/pickup false diagnostics (shared tuplet-aware duration).
4. Make `Fraction` overflow-safe + unit tests.
5. Repoint tests from dead `SvgRenderer`/`SlurEngraver`/`TieEngraver` to the live path.
6. Then Phase 2 (LilyPond comparison), ranked by fidelity impact:
   1. Beam quanting & stem length (`beam-quanting.cc`, `stem.cc`).
   2. Horizontal spacing (`spacing-*.cc`, `skyline.cc`).
   3. Relative octave resolution (unify B/C; fix XML).
   4. Tie/slur Bézier bow (`tie.cc`, `slur.cc`, `bezier-bow.cc`).
   5. Barline / SpanBar (`bar-line.cc`, `span-bar.cc`).
