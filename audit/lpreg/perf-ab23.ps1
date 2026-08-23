# A/B perf round 23 (session 122: tab island week — tremolo wiring, split-tie
# parens, string-number distribution, unterminated trill).
# Heavy sides this week's changes created:
#   chordsemi1k — chord-heavy collect: the chord-level \N distribution walks
#                 chord.Articulations once per chord on EVERY collect (preview
#                 keystrokes included). No \N in the book → lazy list stays null,
#                 hash must MATCH base.
#   tab300      — dense tab: per-stem tremolo switch, tie-target branches,
#                 line-start firstSounding scan. Quarters + in-measure ties only
#                 (no halves = no 0.355→0.5 double stems, no split ties) →
#                 hash must MATCH base.
#   plain1k     — control: none of the changed code runs. Drift gauge + hash.
# Base = 1ef16476 worktree (session 122 start, before the first code change).
# Interleaved, median-of-3, BOTH orders. ⚠️ No tests running alongside.
$base = 'C:\MyProj\LilySharp-perfbase-b964\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
$curr = 'C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
if (-not (Test-Path $base)) { throw 'baseline dll missing' }
if (-not (Test-Path $curr)) { throw 'current dll missing' }

$inputs = @(
  @{ Name='chordsemi1k (chord collect; hash must match)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-chordsemi1k.lys'; Hash=$true },
  @{ Name='tab300 (dense tab; hash must match)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-tab300.lys'; Hash=$true },
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
