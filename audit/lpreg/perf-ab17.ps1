# A/B perf round 17 (session 118: the tuplet-bracket LP port + the slur's
# tuplet-number extras + the flag-united slur edges).
# Heavy sides this session created:
#   slurtuplet300 — a slur COVERING a tuplet every bar: LayoutSlurs' gated
#                   Calculate + number extras + additional_ys, fully exercised.
#   tuplet300     — the same tuplets, NO slur: the bracket port's own cost in
#                   the annotation phase; LayoutSlurs' gate must keep it silent.
#   slurbeam300   — control WITHOUT tuplets (round 16's book): unbeamed-flag and
#                   tuplet code must not touch it; SVG hash must MATCH base.
# Base = 0c63deac (before ALL of session 118).
# Interleaved, median-of-3, BOTH orders. ⚠️ No tests running alongside (round 15's trap).
$base = 'C:\MyProj\LilySharp-perfbase-0c63\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
$curr = 'C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
if (-not (Test-Path $base)) { throw 'baseline dll missing' }
if (-not (Test-Path $curr)) { throw 'current dll missing' }

$inputs = @(
  @{ Name='slurtuplet300 (slur over tuplet each bar)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-slurtuplet300.lys'; Hash=$false },
  @{ Name='tuplet300 (tuplets, no slur)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-tuplet300.lys'; Hash=$false },
  @{ Name='slurbeam300 (no tuplets; hash must match)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-slurbeam300.lys'; Hash=$true }
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
    'hash: ' + $(if ($hb -eq $hc) { 'MATCH' } else { "MISMATCH base=$hb curr=$hc" })
  }
}
