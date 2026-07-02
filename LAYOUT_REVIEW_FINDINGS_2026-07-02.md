# Layout-area review findings — 2026-07-02

Provenance: high-effort multi-agent review of the layout areas (beam
detection/grouping, slur/tie engravers, grob-override application, layout
engravers at large), 4 finders → 38 candidates → 27 independent verifiers →
29 verified / 9 refuted → 17 distinct defects, top 10 reported. Ground truth
for judgment: the LilyPond source at `C:\MyProj\lilypond-src` (cited per
finding). This file exists so the list cannot be lost the way the earlier
M5–M8/L1–L10 list was — update the checkboxes as items are fixed.

Predecessors rediscovered: "Raise" → VerticalSkyline.Raise (below cap,
latent dead code); "slur ordering" → the refuted start-before-end cluster
(5 candidates, all refuted — the original item was likely this false
positive); "empty BeamGroup" → not reproduced as a confirmed defect.
"greedy break-perm" (M5) was already fixed separately (`462b871`).

## Confirmed findings (all verdicts CONFIRMED)

- [x] **1. Secondary-voice beam indices crash the non-column path** (`def8f98`) —
  `ElementCoordinator.cs:425` (also :229, :240).
  `itemXPositions` is sized from `measureLayout.Items` (primary voice only);
  a secondary voice's beam group whose `ItemIndex` exceeds that length throws
  `ArgumentOutOfRangeException` (no guard like `SharedRenderer.cs:1028`).
  Repro: single staff, voice1 `c2`, voice2 `g8 g g g g g g g` → compile crash.

- [x] **2. Multi-page scores draw every page's overlays on every page** (`a0c8be2`) —
  `SharedRenderer.cs:96-122`.
  DrawTies/DrawSlurs/DrawDynamics/DrawLyrics/DrawHairpins… iterate the whole
  layout inside the per-page loop with no page filter, while system Y is
  page-local (PageLayouter restarts at MarginTop per page). Any 2+-page score
  overprints other pages' spanners as garbage.

- [x] **3. Cross-measure beam member renumbering breaks stem suppression** (`def8f98`) —
  `ElementCoordinator.cs:380`.
  `LayoutSingleSystemBeamPiece` renumbers `BeamMember.ItemIndex` to dense
  0..N while keeping real `MeasureIndex`; `BuildBeamedItemsSet`
  (`SharedRenderer.cs:185`) keys on (staff,voice,measure,ItemIndex). Repro:
  `r4 c8[ d8 | e8 f8] g4` → duplicate stems/flags on beamed notes, suppressed
  stems on unrelated notes. Same defect class as the fixed beam-reuse hole.

- [x] **4. Tie detection scans arbitrarily far forward** (`efee798`) —
  `TieDetector.cs:49` (also :101).
  A tie start finds "the next note with the same staff position" at any
  distance; LP (lily/tie-engraver.cc) ties only the immediately following
  note (mismatch ⇒ warning, no tie). Repro: `c'4~ d'4 e'4 c'4` → spurious
  long tie arc. The chord path (DetectChordTies) already stops at the
  immediate next item, so the two paths disagree.

- [x] **5. Override state leaks backward across voice/staff passes** (`47aaed2`) —
  `SharedRenderer.cs:938`.
  The single stateful GrobPropertyResolver is re-advanced from measure 0 per
  voice/staff pass without reset, so a persistent override activated late in
  the previous pass is already active in the next pass's early measures
  (LP: overrides take effect only from their timewise position,
  lily/context-property.cc). Repro: 2-voice staff, color override at m2 →
  voice 2's m0–m1 render colored.

- [x] **6. `\once` cleanup erases the underlying persistent override** (`47aaed2`) —
  `GrobProperty.cs:91`.
  Clearing a once-override does `active.Remove(propName)` instead of
  restoring the previous `\override` (LP pops the property stack,
  lily/context-property.cc execute_general_pushpop_property). Repro:
  red override + once blue → notes after the once revert to black, not red.

- [x] **7. `AdvanceTo` applies overrides only on exact position match** (`47aaed2`) —
  `GrobProperty.cs:100`.
  Should apply everything in (lastPos, currentPos]; consumers that skip item
  indices silently drop overrides. E.g. `CalculateVoiceOffsets` advances only
  to each collision column's minItemIndex → `NoteColumn.force-hshift` at any
  other index is ignored; positions after a measure's last item or at skipped
  TimeSignatureChangeItems never activate.

- [x] **8. Slur direction uses only the start note's stem** (`abd2f42`; snapshot test/multi-line-spanners re-approved via the visual harness) —
  `SlurDetector.cs:68`.
  LP rule (lily/slur.cc Slur::calc_direction): default DOWN, flip UP if ANY
  covered stem is DOWN. Repro: `g'4( c''4)` in treble — LP slurs UP, Lily#
  slurs DOWN into c'''s stem side.

- [x] **9. Tie-column monotonicity penalty inverted (device-Y vs Y-up)** (`abd2f42`; proven against the LP source line — the pinned fixture passes either way) —
  `TieFormattingProblem.cs:499`.
  `configEdgeY <= existingEdgeY` penalizes the CORRECT bottom-to-top stacking
  EmitChordTies emits (inverts lily/tie-formatting-problem.cc:868-873).
  Chord tie columns bias clumped/inverted. Same class as the Stage-4 Y-up
  migration defects.

- [x] **10. Tuplet bracket beam-coverage check ignores voice & gaps** (`abd2f42`) —
  `TupletBracketEngraver.cs:347`.
  `AreAllNotesBeamed`/`FindCoveringBeam` assume contiguous members
  (StartIndex+Members.Length-1) and never compare `BeamGroup.VoiceIndex` to
  the tuplet's voice (LP tuplet-bracket.cc:79-95 checks the bracket's OWN
  beam). Another voice's beam can hide a bracket; a beam with an interior
  rest under-counts and wrongly shows one.

## Distinct defects below the report cap (from the synthesis summary)

- [x] VerticalSkyline.Raise flattens slopes (`64960f7`) — slope now preserved
  (LP skyline.cc raise); pinned by a test although Raise still has no
  production caller.
- [x] Rest-shift resolution was last-writer-wins (`64960f7`) — greatest
  clearance now wins. Still primary-voice rests only (pre-existing scope).
- [x] Dead-code claims re-verified (`64960f7`): only
  `TieFormattingProblem.StaffLinePositions` was actually dead (removed).
  SlurScoringProblem's staff-line scorer is LIVE (:456/:477) and
  `Clone()` has a test caller — the wider claim was wrong, matching its
  refuted twin.
- [x] Three performance cleanups — two were already resolved by the main
  fixes (AdvanceTo linear scans → the replayable-timeline resolver
  `47aaed2`; per-page map rebuild → page-scoped by design in `a0c8be2`).
  Remaining micro-item (DrawBeams rebuilds staffByIndex per system)
  deliberately skipped: threading a cache through DrawSystem isn't worth
  the churn at this size.
- [x] StaffGrouper override finding (PLAUSIBLE) — re-examined and CLOSED AS
  UNREACHABLE: `ApplyOverrides` expects dotted sub-property names
  ("staff-staff-spacing.basic-distance"), but the grammar parses exactly
  `override Grob.property = value` (one dot), so these overrides are
  API/test-only today. The positional/`\once` semantics gap becomes real
  only if the dotted syntax is ever added to the parser — note it there.

## Refuted candidates (verifier-killed; re-derive before ever re-raising)

- Slur/beam "start-processed-before-end on the same item" cluster (5
  candidates + 1 beam variant) — all refuted; likely the source of the old
  "slur ordering" note.
- `GrobOverride` lacking staff identity ⇒ override applies to all staves —
  refuted (per-staff collection makes it a non-issue as claimed).
- Auto-knee gap widened by ±1 position vs LP's ±1 space (beam.cc:968-1056)
  — refuted on unit-semantics grounds.

Workflow journal (full per-agent evidence):
`.claude session wf_cb3ebfd8-362` — see the session's
`subagents/workflows/wf_cb3ebfd8-362/journal.jsonl` while it exists; this
file is the durable summary.
