# Handoff — `<< >>` Arpeggio Rework

Next-session brief: reimplement the `<< >>` arpeggio to the **confirmed spec** below. The
spec was finalized with the user across a design discussion; **do not re-litigate it**. This
doc also carries general Lily# development principles/procedures at the end — read those first
if you are new to the codebase.

---

## 1. What the user hit ("全然動いていない")

```
part melody { section A { << c 3 5 >> } }
```
errors: `Expected 'DoubleCloseAngle', found 'IntegerLiteral'` + `Invalid duration '3'`.

Root cause: `<< >>` members are parsed with `ParseMusicItem()`, which parses `c` as a note and
grabs the following `3` as the note's **duration** (invalid). The arpeggio doesn't support
**scale-degree** members (`3`, `5`), even though the chord form `<c 3 5>` does.

The user writes broken chords by **degree** (`<< c 3 5 >>` = root c + 3rd + 5th = c e g),
which is the natural way — and it doesn't parse. That is the whole bug.

Note: the **octave** behavior of `<< >>` is already CORRECT (verified via MIDI) and must be
preserved — see §2.

---

## 2. Confirmed spec for `<< >>` (LOCKED)

`<< members >>` is a **written-out broken chord**: the members play **in sequence** (separate
noteheads), not a stacked chord. (A stacked chord + arpeggio squiggle is the SEPARATE form
`<c e g>@arpeggio`.)

**Members** — any mix of:
- pitch: `c`, `e`, `g` (with octave marks `'` / `,`)
- **scale degree**: a bare number `3`, `5` (root-relative, like `<c 3 5>`)
- chord: `<e g>`
- rest: `r`

**No durations and no dots inside `<< >>`.** (A number is therefore always a degree, never a
duration — this resolves the ambiguity that broke `<< c 3 5 >>`.) The dotted-member idea was
explicitly **dropped** (a dot can't be both "1.5× the plain note" AND land on a clean beat, so
it makes rhythm unintuitive — see the design discussion; the user agreed to drop it).

**Duration of the whole group:**
- `<< … >>N` (trailing duration glued to `>>`) → the group's **total length = N**.
- No trailing → total = the **inherited/running duration** (the arpeggio acts like one note).
- Members **equally subdivide** the total ⇒ an **auto-tuplet** when needed. 3 notes in a quarter
  → triplet; 5 → quintuplet; 9 → nonuplet (the guitar fast-picking use case). This is the
  main change from today's behavior (today it scales the members' *natural* durations).

**Octaves (PRESERVE — already working):**
- The **root** = first pitched member, resolved in the **current octave mode** (absolute or
  relative) and the incoming frame. **Only the root is affected by the octave mode.**
- Every **later member stacks above the root**, mode-INDEPENDENTLY (nearest pitch above, like a
  `<c e g>` chord member). `'` / `,` shift from that stacked position (`<c e g'>` semantics:
  `'` = +1 octave). So `<< c' e' g' >>` = C5 root, e' = the E a **10th** above (E6), g' = the G
  a **12th** above (G6). Verified: MIDI `72 88 91` == the reference `c' e'' g''`.

**Degrees** resolve against the **root's step/octave + the current key** (see `ChordDegrees.Resolve`).
`<< c 3 5 >>` in C major = c, e, g. `<< d 3 5 >>` = d, f, a. A degree member is placed by the
same "stack above root" rule.

**Errors:** `<< a \\ b >>` (a `\\` inside) stays the removed-polyphony hint (parallel voices are
`voice { }`), NOT an arpeggio.

Grammar doc to update: `docs/GRAMMAR.md` §8.2 `Arpeggio` (line ~390) — the "keeps its own
duration" wording is now wrong (no per-member durations; equal subdivision).

---

## 3. Implementation plan

A **music-notation feature touches four places** — the parser and all THREE consumers, or the
forms silently diverge (SVG shows one thing, MIDI/MusicXML another):

1. **Parser** — `Parser.Music.cs` `ParseArpeggio()` (line ~376). Replace the `ParseMusicItem()`
   member loop with a chord-like loop that parses members **without durations**:
   - pitch → `ParsePitch(inChord: true)` (line 153; parses `'`/`,`, NO duration)
   - degree → `ParseScaleDegree()` (line 407)
   - chord → `ParseChord()` (line 308)
   - rest → a rest **without** a duration (add a helper or gate the existing rest parse)
   Keep the trailing `ParseOptionalDuration()` for `>>N`. This changes the member node types
   held by `ArpeggioGreen` (pitches/degrees, not full notes) — the collectors must follow.
   Mirror `ParseChord`'s loop (lines 330–358) for the pitch/degree/drum dispatch.

2. **SVG collector** — `MeasureCollector.MusicWalk.cs` `ProcessArpeggio` (line ~141) +
   `ComputeArpeggioTuplet` (line ~225) + `EmitArpeggioMember`/`EmitScaledItem`.
   - Change duration to **equal subdivision**: total (N or inherited) ÷ member count; emit an
     auto-tuplet bracket when the equal share isn't a plain note value.
   - Resolve **degree** members to pitches with `ChordDegrees.Resolve(rootStep, firstOctave,
     degree.Number, degree.Alteration, degree.OctaveOffset, writtenKeySharps)` then
     `ResolveAbsolutePitch(...)` — copy the pattern from `MeasureCollector.ItemFactory.cs`
     lines ~304–324 (the `<c 3 5>` chord path). `writtenKeySharps = _meta.KeySharps -
     _octave.TransposeKeySharps(0)` (degrees resolve in the WRITTEN key, transpose applied once).
   - **Preserve** the octave stacking (lines ~159–193): root uses `savedAbsolute` (file mode);
     members set `_octave.OctaveAbsolute = true`, `OctaveBase = anchorOctave + (pitchIndex >=
     rootStep ? 0 : 1)`; after the group, running reference = the root.

3. **MIDI** — `MidiExporter.cs` `ProcessArpeggio` (line ~959) + `ComputeArpeggioTupletRatio`
   (line ~1015). Same equal-subdivision + degree resolution. MIDI is the ground truth for pitch
   & timing (verify with it — see §Tips).

4. **MusicXML** — `MusicXmlExporter.cs` `ProcessArpeggio` (line ~1347) + `ArpeggioTupletRatio`
   (line ~1424). Same.

5. **Tests + snapshots** — add `<< c 3 5 >>` degree tests (parse + render), equal-subdivision
   tests (`<< c e g >>4` = triplet, `<< a b c d e >>4` = quintuplet), octave tests
   (`<< c' e' g' >>` = root+10th+12th, keep). **Equal subdivision changes existing arpeggio
   snapshots** — regenerate ONLY after confirming each diff is the intended equal-subdivision
   change (see §Tips). There is an existing `<< >>` fixture/test around the leading-rest root fix.

Suggested order: parser → SVG (visual confirmation) → MIDI → MusicXML → snapshots. Commit per
step. `git grep -n "ArpeggioSyntax\|ProcessArpeggio\|ComputeArpeggioTuplet"` to find every site.

---

# Lily# Development Guide (principles / procedures / tips)

## Architecture
Pipeline (SVG): **Parser** (green/red syntax trees) → **MeasureCollector** (`Svg/Collector/*`,
walks the tree into `Measure`/`MusicItem` model) → **LayoutEngine** (`Svg/Layout/*`) →
**SharedRenderer** (`Rendering/*`) → drawing contexts (SVG/PNG/PDF). **MIDI** and **MusicXML**
have their OWN tree-walkers (`Midi/MidiExporter.cs`, `MusicXml/MusicXmlExporter.cs`) — they do
NOT go through the collector. ⇒ **A notation feature must be implemented in all of: parser,
MeasureCollector, MidiExporter, MusicXmlExporter**, or the outputs diverge. `ScoreAssembler`
builds the `Score`/`MultiStaffScore` (one place; a field forgotten here silently vanishes — that
was a real bug: multi-staff dropped grob overrides).

Single-staff vs multi-staff: a bare/1-staff score uses `MeasureCollector.Collect`; a `score`
block with staves uses `CollectMultiStaff`. Both share `CollectMeasures` per voice.

## Build / deploy / run
- Tests: `dotnet test LilySharp.Tests -c Release --nologo` (filter with
  `--filter "FullyQualifiedName~Xxx"`). Runs ~1–2 min full.
- CLI: `dotnet run --project LilySharp.Cli -c Release -- <svg|png|midi|xml|check|layout> in.lys out`.
  Add `--no-build` to skip the rebuild once built. `png ... --crop` = tight PNG to eyeball.
- Deploy to the running VS Code extension (for the user to try): `pwsh tools/Deploy-Lsp.ps1`
  (rebuilds Core+LSP, kills the old lsp processes in a retry loop, copies `server/` + `out/` +
  `package.json` into the installed extension). Then tell the user to run **"Developer: Reload
  Window"**. A clean VSIX reinstall is `tools/Package-And-Install.ps1` (rarely needed).
- The user builds `PowerShell.MCP` etc. themselves — but for LilySharp YOU build/test/deploy.

## Verifying output
- **Read the PNG** (`png --crop`) to check notation visually — this is the fastest correctness
  signal for a rendering change.
- **MIDI is ground truth for pitch & duration.** Export `midi`, read note-on events: in pwsh,
  `for($i…){ if(($b[$i] -band 0xF0) -eq 0x90 -and $b[$i+2] -gt 0){ $notes += $b[$i+1] } }`.
  C4 = 60. Use this to prove octave/degree/duration behavior, not just "looks right".
- **Japanese/space paths break the CLI** (console encoding). Copy the `.lys` to
  `$env:TEMP\x.lys` (ASCII) first, then run. Same for reading a fixture with a non-ASCII path.
- `check` prints diagnostics only (errors + warnings), no render — good for validators.

## Testing & snapshots
- Snapshot tests: `SvgSnapshotTests` (`Fixtures/**/*.lys` ↔ `Snapshots/*.svg`, byte-exact) and
  `HaraKiriVisualTests` (programmatic). Regenerate with env `LILYSHARP_UPDATE_SNAPSHOTS=1` on a
  test run, or `pwsh tools/Approve-Snapshots.ps1 -Name <test/name>`. A failing snapshot prints a
  visual-diff report path (`artifacts/visual-diff/report.html`).
- **Never regenerate blindly.** Confirm each changed snapshot is the *intended* change (diff the
  non-font lines; the font `@font-face` base64 dominates a raw diff — filter it out). When a
  behavior change legitimately alters N snapshots, verify a representative sample, then regen.
- Add a focused unit/collector test for every fix. Collector tests: `new MeasureCollector()
  .Collect(tree[, voice])` or `.CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!)`.
  Semantic validators: `SemanticValidation.Run(tree)` returns diagnostics.

## Conventions (IMPORTANT)
- **Commit as yotsuda, autocrlf off**:
  `git -c core.autocrlf=false -c user.name=yotsuda -c user.email=ytsuda@gmail.com commit -m …`.
- **Push is HELD**: commit locally only; do NOT push/tag/release without an explicit GO
  (exception: the UiPathOrch repo, not relevant here). Don't create branches unprompted; work on
  master.
- **Shell**: the Bash tool is banned — use the pwsh MCP or ripple. Don't wrap in `pwsh -Command`.
- **Grob names are PascalCase, LilyPond-style** (`NoteHead`, `Stem`, `Beam`) — a distinct
  namespace from lowercase keywords (`title` keyword vs a future `Title` grob).
- **Mimic the LilyPond source** for layout/engraving correctness: `C:\MyProj\lilypond-src\lily\*.cc`
  (e.g. `note-collision.cc`, `clef-engraver.cc`). Add a `LILYPOND-REF:` comment when you port
  logic. Lily#-original constructs (degree chords, `voice{}`, `form`/`score`) are lowercase and
  our own.
- **Don't split large files** or rename things for tidiness; match surrounding style, comment
  density, and idiom.
- Diagnostics: add a code in `Syntax/Diagnostic.cs` (`LYSxxxx`; 0xxx lex/parse, 1xxx semantic,
  2xxx measure, 4xxx warnings, …) and a validator in `Semantics/` registered in
  `SemanticValidation.CreateAll()`. `_diagnostics.Error/Warning(span, code, message)`.

## Gotchas seen this codebase
- `Date.now()`/`Math.random()` are fine in C#; not relevant here.
- Source-generated regexes: production regexes use `[GeneratedRegex]` (partial class + `static
  partial Regex Foo()`), no runtime `new Regex`.
- Section self-containment: at each section boundary the collector reverts clef/key/time/octave
  AND (recently) grob-override state to the part default (`ProcessSection` in
  `MeasureCollector.Form.cs`). Keep new "running" state self-contained the same way.
- `<< >>` (arpeggio) ≠ `<< \\ >>` (removed polyphony → `voice{}`). `<c e g>@arpeggio` = rolled
  chord (a DIFFERENT feature from `<< >>`).
- Grob overrides now carry `StaffIndex`/`VoiceIndex` scope and resolve per staff/voice
  (`GrobPropertyResolver.ForStaffVoice`) — see `docs/grob-override-scope-design.md`. If arpeggio
  members emit notes, they inherit the walk's `_currentStaffIndex`/`_currentVoiceScope` — fine.

## Reference docs
- `docs/GRAMMAR.md` — the language grammar (update §8.2 Arpeggio).
- `docs/grob-override-scope-design.md` — the grob-override scope model (just implemented).
- The user communicates in Japanese; repo docs/comments are English.
