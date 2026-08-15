# A/B perf round 15 (session 117: slur stem plumbing — per-column stem_ in the
# encompass obstacles, grace columns join the slur's encompass, stem-attach X
# rule :738-760).
# Interleaved runs, median-of-3 BOTH orders.
# Base = 27dacde7 (the commit before the stem plumbing).
# Heavy sides of THIS change:
#  - perf-slur300: every slur now resolves per-column stems (NoteColumnLayout /
#    beam lookup) and runs the per-candidate stem-attach X rule. Slur curves may
#    move BY DESIGN (stem terms) -> no hash.
#  - perf-grace200: 800 grace slurs + covered grace columns rebuild
#    SpacingRules.GraceColumns / QuantGraceBeam per covered slur segment.
#  - perf-scriptsym1k: slur-less control. Output must HASH-match base.
$base = 'C:\MyProj\LilySharp-perfbase-27da\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
$curr = 'C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
if (-not (Test-Path $base)) { throw 'baseline dll missing' }
if (-not (Test-Path $curr)) { throw 'current dll missing' }

$inputs = @(
  @{ Name='slur300'; File='C:\MyProj\LilySharp\audit\lpreg\perf-slur300.lys'; Hash=$false },
  @{ Name='grace200'; File='C:\MyProj\LilySharp\audit\lpreg\perf-grace200.lys'; Hash=$false },
  @{ Name='scriptsym1k (hash must match)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-scriptsym1k.lys'; Hash=$true }
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
