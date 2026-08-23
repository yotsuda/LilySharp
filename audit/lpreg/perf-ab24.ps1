# A/B perf round 24 (session 123: tuplet family + volta week — avoid-scripts
# port, direction tiebreak, member-X invisible brackets, OuterEdge stem/head
# split, all-rest offset pass, volta chains + pointwise profile).
# Heavy sides this week's changes created:
#   tupscr300 — 300 measures × (drawn tuplet + beamed tuplet + 3 scripts):
#               the LayoutEngine Script sieve walks every script per layout,
#               CalculateSlope's avoid-scripts loop walks every script per
#               drawn tuplet, the suppressed branch walks beam members per
#               beamed tuplet. Notes sit BELOW the middle (stems up, accents
#               below) so no script point can win an UP bracket.
#               ⚠️ hash does NOT match base, and the difference is the DESIGNED
#               session-123 change: the 300 beamed-tuplet numbers move 0.08
#               (y 9.87→9.79, X unchanged) because a SLOPED beam's invisible
#               bracket is now evaluated at the tuplet's own stem Xs (the
#               follow-beam letter), not the beam ends — verified line-by-line
#               (exactly 300 diff lines, all number <text> Y). The flat-beam
#               invariance claim held only for flat beams; ascending c-d-e is
#               sloped.
#   plain1k   — control: none of the changed code runs (no tuplets, no
#               scripts, no voltas). Drift gauge + hash.
# Base = 0618241e worktree (session 123 start, before the first code change).
# Interleaved, median-of-3, BOTH orders. ⚠️ No tests running alongside.
$base = 'C:\MyProj\LilySharp-perfbase-31a0\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
$curr = 'C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
if (-not (Test-Path $base)) { throw 'baseline dll missing' }
if (-not (Test-Path $curr)) { throw 'current dll missing' }

$inputs = @(
  @{ Name='tupscr300 (tuplets+scripts; hash must match)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-tupscr300.lys'; Hash=$true },
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
    'hash: ' + $(if ($hb -eq $hc) { 'MATCH' } else { "MISMATCH base=$hb curr=$hc" })
  }
}
