# A/B perf round 28 (session 127: part-combine silence / condensed rest voicing / R1*N bars).
# Interleaved runs, median-of-5 BOTH orders, SVG hash compared.
# Base = 2222418d (the commit this session started from).
#
# WHAT THIS SESSION ADDED, COUNTED FIRST (HANDOFF §5.3) — three changes, three reaches:
#  1. PartCombiner: slots carry an onset, Materialise sorts each measure's entries and fills
#     gaps with a spacer. Runs ONLY inside PartCombiner.Combine, i.e. only for a score with a
#     `combinedStaff`. Per render, per measure, per slot — never per item.
#  2. RenderSpec.ToStaffGroups: one ResolveVoiceStemDirections pass, INSIDE
#     `case CondensedStaffSpec`. Zero for anything else.
#  3. MeasureModel.Split: one type test per music item, plus N-1 list appends per written
#     `R…*N`. ⚠️ THIS ONE IS ON THE DIAGNOSTICS PATH — SemanticValidation runs MeasureValidator,
#     and the editor asks for diagnostics on every keystroke. It is the only change of the
#     three that a plain score pays anything for at all.
#
# Inputs, and what each is here to charge:
#  - perf-mmr1k:  1000 bars = 200 x (4 notes + a written R1*4). The heavy side of change 3:
#                 800 items paying the type test AND 200 rests paying the expansion.
#  - perf-comb300: the heavy side of change 1 — 300 bars that actually go through the combiner.
#  - perf-plain1k: 1000 bars of plain notes. Change 3 charges it one type test per item and
#                  nothing else, so it doubles as the machine-drift gauge (§5.3): if THIS
#                  moves, the number is the machine.
# Every output must be HASH-IDENTICAL: none of the three changes moves a glyph in these books.
$base = 'C:\MyProj\LilySharp-perfbase-2222\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
$curr = 'C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
if (-not (Test-Path $base)) { throw 'baseline dll missing' }
if (-not (Test-Path $curr)) { throw 'current dll missing' }

$dir = 'C:\MyProj\LilySharp\audit\lpreg'
$inputs = @(
  @{ Name='mmr1k   (200 written R1*4 + 800 notes = the changed walk''s heavy side)'; File="$dir\perf-mmr1k.lys" },
  @{ Name='comb300 (combinedStaff = the only score the combiner runs for)';          File="$dir\perf-comb300.lys" },
  @{ Name='plain1k (one type test per item = drift gauge)';                          File="$dir\perf-plain1k.lys" }
)

function MedianOf($xs) { $s = $xs | Sort-Object; $s[[int](($s.Count - 1) / 2)] }

foreach ($inp in $inputs) {
  if (-not (Test-Path $inp.File)) { "SKIP (missing): $($inp.Name)"; continue }
  foreach ($order in 'base-first', 'curr-first') {
    $tb = @(); $tc = @()
    & dotnet $base svg $inp.File "$dir\perf-wb.svg" 2>&1 | Out-Null   # warm
    & dotnet $curr svg $inp.File "$dir\perf-wc.svg" 2>&1 | Out-Null
    for ($i = 0; $i -lt 5; $i++) {
      if ($order -eq 'base-first') {
        $tb += (Measure-Command { & dotnet $base svg $inp.File "$dir\perf-b.svg" 2>&1 | Out-Null }).TotalMilliseconds
        $tc += (Measure-Command { & dotnet $curr svg $inp.File "$dir\perf-c.svg" 2>&1 | Out-Null }).TotalMilliseconds
      } else {
        $tc += (Measure-Command { & dotnet $curr svg $inp.File "$dir\perf-c.svg" 2>&1 | Out-Null }).TotalMilliseconds
        $tb += (Measure-Command { & dotnet $base svg $inp.File "$dir\perf-b.svg" 2>&1 | Out-Null }).TotalMilliseconds
      }
    }
    $mb = MedianOf $tb; $mc = MedianOf $tc
    $hb = (Get-FileHash "$dir\perf-b.svg").Hash
    $hc = (Get-FileHash "$dir\perf-c.svg").Hash
    "{0} [{1}]: base {2:F0} ms | curr {3:F0} ms | delta {4:+0.0;-0.0}% | hash {5}" -f `
      $inp.Name, $order, $mb, $mc, (($mc - $mb) / $mb * 100), $(if ($hb -eq $hc) { 'MATCH' } else { 'DIFFER' })
  }
}
