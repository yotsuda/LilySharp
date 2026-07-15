# Grob Override / Revert Scope — Design

## Goal

Make `override` / `revert` placeable at every context where it is meaningful, with the
scope determined by *where it is written* (a LilyPond-style context model), so an author
can colour/hide grobs per document, per part/staff, per section, or per voice without
surprises.

## Effective overrides

The renderer consumes only four grob properties; this design covers exactly these (other
grobs parse and store but are no-ops today — a separate gap):

- `NoteHead.color`, `Stem.color` — colour
- `NoteHead.transparent` — hide / show
- `NoteColumn.force-hshift` — manual collision shift

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
