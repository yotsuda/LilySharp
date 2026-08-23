# A/B perf round 16 (session 117 self-audit: the slur work landed AFTER round
# 15 — rest-bound slurs, rest encompass columns — plus the heavy sides round 15
# never had: outer slurs COVERING grace runs (per-segment grace scan +
# QuantGraceBeam re-solve) and beamed slurred books (per-column stem lookup
# scans beamLayouts).
# Base = 490f3696 (before ALL of this session's slur work).
# Interleaved, median-of-3, BOTH orders.
$base = 'C:\MyProj\LilySharp-perfbase-27da\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
$curr = 'C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
if (-not (Test-Path $base)) { throw 'baseline dll missing' }
if (-not (Test-Path $curr)) { throw 'current dll missing' }

$inputs = @(
  @{ Name='slurgrace300 (outer slur over 2-note acciaccatura, 300 bars)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-slurgrace300.lys'; Hash=$false },
  @{ Name='slurbeam300 (beamed 8ths under slurs, 300 bars)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-slurbeam300.lys'; Hash=$false },
  @{ Name='slurrest300 (rest-bound + rest-covered slurs, 300 bars)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-slurrest300.lys'; Hash=$false },
  @{ Name='scriptsym1k (slur-less control; hash must match)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-scriptsym1k.lys'; Hash=$true }
)

function MedianOf($xs) { $s = $xs | Sort-Object; $s[[int](($s.Count - 1) / 2)] }

foreach ($inp in $inputs) {
  foreach ($order in 'base-first', 'curr-first') {
    $tb = @(); $tc = @()
    & dotnet $base svg $inp.File C:\MyProj\LilySharp\audit\lpreg\perf-wb.svg 2>&1 | Out-Null
    & dotnet $curr svg $inp.File C:\MyProj\LilySharp\audit\lpreg\perf-wc.svg 2>&1 | Out-Null
    for ($i = 0; $i -lt 3; $i++) {
      if ($order -eq 'base-first') {
        $tb += (Measure-Command { & dotnet $base svg $inp.File C:\MyProj\LilySharp\audit\lpreg\perf-b.svg 2>&1 | Out-Null }).TotalMilliseconds
        $tc += (Measure-Command { & dotnet $curr svg $inp.File C:\MyProj\LilySharp\audit\lpreg\perf-c.svg 2>&1 | Out-Null }).TotalMilliseconds
      } else {
        $tc += (Measure-Command { & dotnet $curr svg $inp.File C:\MyProj\LilySharp\audit\lpreg\perf-c.svg 2>&1 | Out-Null }).TotalMilliseconds
        $tb += (Measure-Command { & dotnet $base svg $inp.File C:\MyProj\LilySharp\audit\lpreg\perf-b.svg 2>&1 | Out-Null }).TotalMilliseconds
      }
    }
    $mb = MedianOf $tb; $mc = MedianOf $tc
    '{0} [{1}]: base {2:n0} ms / curr {3:n0} ms = {4:+0.0;-0.0}%' -f $inp.Name, $order, $mb, $mc, (($mc - $mb) / $mb * 100)
  }
  if ($inp.Hash) {
    $hb = (Get-FileHash C:\MyProj\LilySharp\audit\lpreg\perf-b.svg).Hash
    $hc = (Get-FileHash C:\MyProj\LilySharp\audit\lpreg\perf-c.svg).Hash
    '  hash: ' + ($(if ($hb -eq $hc) { 'MATCH' } else { 'DIFFER' }))
  }
}
