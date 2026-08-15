# A/B perf round 25 (session 124: rest mover chaining, rest dot column, voiced
# span scoping + stamp reads, trill direction, tab all-voices).
# Heavy side: restdot300 — 300 measures x 2 voices, dotted rests + dotted notes
# sharing moments (stamp-read collision pass, RestDotOffsetsOf column solve,
# chained CalculateRestShifts, skyline rest dot seed).
# ⚠️ restdot300 hash will NOT match base BY DESIGN (sessions 124-1..3 moved rest
# and dot Y toward LP). plain1k = untouched control (hash must match).
# tab300 = tab path control, single voice (CreateTab overload; hash must match).
# Base = 3fce5fea worktree (session 124 start). Median-of-3, both orders.
$base = 'C:\MyProj\LilySharp-perfbase-3fce\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
$curr = 'C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
if (-not (Test-Path $base)) { throw 'baseline dll missing' }
if (-not (Test-Path $curr)) { throw 'current dll missing' }

$inputs = @(
  @{ Name='restdot300 (heavy; hash differs BY DESIGN)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-restdot300.lys'; Hash=$true },
  @{ Name='tab300 (tab control; hash must match)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-tab300.lys'; Hash=$true },
  @{ Name='plain1k (control; hash must match)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-plain1k.lys'; Hash=$true }
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
    'hash: ' + $(if ($hb -eq $hc) { 'MATCH' } else { 'MISMATCH' })
  }
}
