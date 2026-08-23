# A/B perf round 27 (session 126: combinedStaff / the part combiner).
# 1000-bar inputs, interleaved runs, median-of-5 BOTH orders.
# Base = f3adc99e (the commit before this session's first change).
#
# WHAT THIS SESSION ADDED TO A SCORE THAT HAS NO combinedStaff — the only thing worth
# measuring, since the combiner itself runs for nothing else:
#  - MeasureContentKey.AddStaffIdentity gained ONE IsDefaultOrEmpty test with an early
#    return. That method runs per STAFF per MEASURE on every key computation, so the heavy
#    side is many staves times many bars, not many notes.
#  - LayoutEngine.CalculateVoiceCollisions gained one extra pass over EnumerateStaves,
#    once per layout.
#  - RenderSpec.IsMultiStaff gained one Items.Any, per render.
# Nothing new runs per ITEM, which is why the note-dense books are here only as controls.
#
# Inputs, heavy side first:
#  - hairpingrand1k: a GRAND STAFF over 1000 bars = the most (staff x measure) pairs
#    available, which is exactly what AddStaffIdentity is charged per.
#  - plain1k:        one staff, 1000 bars of plain notes = the non-contact control. If this
#                    moves, the difference is the machine, not the change (HANDOFF 5.3).
# Output must be HASH-IDENTICAL on both: nothing this session did moves a glyph in a score
# that has no combinedStaff in it.
$base = 'C:\MyProj\LilySharp-perfbase-8753\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
$curr = 'C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
if (-not (Test-Path $base)) { throw 'baseline dll missing' }
if (-not (Test-Path $curr)) { throw 'current dll missing' }

$dir = 'C:\MyProj\LilySharp\audit\lpreg'
$inputs = @(
  @{ Name='hairpingrand1k (grand staff x 1000 bars = most staff-measure pairs)'; File="$dir\perf-hairpingrand1k.lys" },
  @{ Name='plain1k        (non-contact control)';                                File="$dir\perf-plain1k.lys" }
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
