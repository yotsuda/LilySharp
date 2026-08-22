# Grob Override / Revert Scope — Design

## Goal

Make `override` / `revert` placeable at every context where it is meaningful, with the
scope determined by *where it is written* (a LilyPond-style context model), so an author
can colour/hide grobs per document, per part/staff, per section, or per voice without
surprises.

## Effective overrides

The renderer consumes these grob properties (corrected 2026-08-21 — the list below was
incomplete, and the correction that first replaced it was WRONG in the other direction):

- `NoteHead.color`, `Stem.color` — colour. Read by `SharedRenderer.Noteheads.ResolveColor`
  (`SharedRenderer.Noteheads.cs:919`, four call sites), which takes a named colour or a
  `#rgb` / `#rrggbb` hex code through `ColorParser`.
- `NoteHead.transparent`, `Stem.transparent` — hide / show.
- `NoteColumn.force-hshift` — manual collision resolution, **switched off**
  (`ElementCoordinator.cs:49`, `ForceHshiftEnabled = false`), resolver kept intact behind it.

> ✅ **Resolved 2026-08-23 (user-approved pair decision, GRAMMAR_AUDIT §4.2+§4.3).**
> `NoteHead.color` / `Stem.color` gained their `SupportedGrobOverrides` rows, and
> `NoteColumn.force-hshift` lost its row while its reader is disabled — the vocabulary
> now agrees with the engine in both directions (a row means IMPLEMENTED; the
> force-hshift row returns in the commit that lands the per-voice implementation).
> The paragraphs below record how the disagreement arose.

⚠️ **THE ENGINE CALLS ITS OWN WORKING FEATURE UNSUPPORTED** (recorded 2026-08-21, NOT
MEASURED — read off the source). `SupportedGrobOverrides` (`GrobProperty.cs:91-98`) holds
three pairs and **`color` is not among them**, so `OverrideVocabularyValidator.cs:77`
reports LYS1029 — an ERROR, exit 1 — for a spelling whose reader is live. The score still
draws in colour, because LYS1029 is one of the best-effort errors that writes its output
anyway (`CliBestEffortOutputTests.cs:146`), so the file engraves correctly WHILE being
told the property "is not supported in this version".

The two lists disagree because they were built from different questions. The validator's
own MEASURED note names what it tested — `Wibble.wobble`, `Stem.wibble`, `Stem.direction`,
`Stem.length`, `Beam.thickness`, `stem.direction` — and **`color` is not in that list**, so
"only the three pairs moved a single byte" was concluded without ever writing a colour.
`SupportedGrobOverrides` says in its own remarks that adding a property means adding its
reader AND its row in one commit: here the reader arrived without the row.

⇒ The fix is two rows in `SupportedGrobOverrides`, not two readers. It changes output for
files that error today (they stop erroring), so it is the user's call. CLAUDE.md's rule
that a quantity computed in two places is the address of the next defect is exactly what
produced this: the supported vocabulary is stated once as a whitelist and once as a set of
readers, and only one of them was updated.

Grob names are **PascalCase, LilyPond-style** (`NoteHead`, not `noteHead`/`Note`),
case-sensitive. `title` (the keyword) and a future `Title` grob are distinct namespaces.

## Scope = placement (a staff × time grid)

An override lives on a 2-D grid: *which staves*, and *which span of the timeline*.

| Where written | Staff scope | Time scope | Kind |
|---|---|---|---|
| Top level (global) | all staves | whole piece | default |
| `part {}` body (holds sections) | that part's staff | whole part | default |
| `section {}` directly, multi-part (holds part blocks) | all staves | that section | default |
| `section {}` directly, single-voice (holds notes) | that staff | from position | positional |
| part block in a section / section in a part | that staff | that section | positional |
| `voice {}` (parallel voice) | **that voice** | from position | positional |
| in a note stream | that staff/voice | from position | positional |

*Default* contexts set a starting state for their whole scope; *positional* contexts (note
streams) apply from the written point forward.

## `override` vs `revert` vs `once`

- **`override`** — allowed everywhere above (a default-setter in structural contexts, a
  positional change in note streams).
- **`revert`** — allowed **only in a note-stream (music) context**. It is inherently
  positional ("revert from here"); a structural context has no distinct position to revert
  at.
- **`once override` / `once revert`** — same rule as `revert`: **music contexts only**.

Rationale: global `tempo` / `time` / `key` are one-shot defaults with no "revert"; grob
defaults follow suit. To carve out a range you write `override … revert` inside music,
where positions are real.

**New validation** (diagnostic): a `revert` or `once` in a non-music context (global,
`part {}` body, multi-part `section {}` body) is an error — "revert/once must be inside a
music block".

## Section-boundary reset (self-containment)

At each section boundary the running grob-override state reverts to the **part-default
state** = (global overrides) + (this part's `part {}`-body overrides). This mirrors the
clef / key / time reset (clef is already implemented this way).

- A section-internal override does **not** leak into the next section.
- Global and part-body overrides **persist** across sections (they are the part default).

This is what makes part-major and section-major layouts agree: both reduce to the same
grid, and `PartSectionLayoutConverter` only has to preserve "boundary reset + part-default
persists".

## Staff-scoped resolver

Today there is one score-wide `GrobPropertyResolver`. Change:

- `GrobOverride` / `GrobRevert` gain a staff scope (`int? StaffIndex`; `null` = all staves).
- Collection tags each directive with its staff (global → `null`; part/section/voice → the
  staff/voice index being collected).
- Rendering builds a **per-staff** resolver from the overrides where `StaffIndex` is `null`
  or the current staff.

**Behaviour change**: an in-music override in one staff no longer bleeds into the others
(today it does — the resolver is score-wide).

## `{ }` music blocks

Plain grouping. They do **not** auto-scope an override. A range is delimited only by
`revert` and section boundaries. So `override red { c d e f }` colours the block because the
override runs forward, not because the braces scope it. `override red { c d revert … e f }`
and `{ override red c d revert … e f }` are equivalent (c d red, e f black).

## Non-goals / out of scope

- `<< \\ >>` — not a Lily# construct; parallel voices are written `voice {}`.
- Grobs beyond the four above (e.g. `Title.color`, `Stem.length`) — future; a new grob's
  override would be placeable in that grob's native context (e.g. `Title` → top level).

## Implementation plan

1. **Model** — add `int? StaffIndex` to `GrobOverride` / `GrobRevert`.
2. **Grammar** — accept `override` / `revert` / `once` in `part {}` and `section {}` bodies
   (already accepted in a note stream).
3. **Validator** — diagnostic for `revert` / `once` in a non-music context.
4. **Collector** —
   - Tag each directive with its staff scope (global = `null`).
   - Structural overrides positioned at their scope start; music ones at their point.
   - Section-boundary reset of the override *set* to part-default — analogous to
     `_sectionResetClef` but a set of grob properties (the trickiest piece: emit boundary
     reverts / re-overrides per staff).
5. **Layout / render** — build a per-staff resolver filtered by staff scope.
6. **Tests** — grid cases, section reset, staff isolation, validation errors, and
   part-major ⇄ section-major equivalence.

## Worked examples

- `override NoteHead.color = red` (global) → every staff red for the whole piece.
- `part melody { override NoteHead.color = red  section A {…} section B {…} }` → the melody
  staff is red throughout; other staves are unaffected.
- `section A { override NoteHead.color = red  melody {…} bass {…} }` → all staves red **only
  during A** (resets at A's end).
- `part melody { section A { override NoteHead.color = red  c4 d  revert NoteHead.color  e f } section B {…} }`
  → melody A: c d red, e f black; B black; **A2 red** (the section reset re-applies A's own
  in-section override on the reprise).
- `override NoteHead.color = red` then `revert NoteHead.color` at top level → **error**
  (revert is not allowed at global scope).
