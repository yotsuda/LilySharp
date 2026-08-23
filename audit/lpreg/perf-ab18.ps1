# A/B perf round 18 (session 119: trill to-barline + voiced-rest separation box
# + spring wish min replacement).
# Heavy sides this session created:
#   restpoly1k — two voices with rests (round 10's book): the voiced-rest BAKE
#                (ResolveVoiceStemDirections arm) + the skyline voiced arm.
#   plain1k    — dense single voice: the spring reorder + min replacement runs on
#                every inter-column pair; SVG hash must MATCH base (no voiced
#                rests, no knees, floors unreached).
# Base = 0d304af7 (before ALL of session 119).
# Interleaved, median-of-3, BOTH orders. ⚠️ No tests running alongside.
$base = 'C:\MyProj\LilySharp-perfbase-ae2a\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
$curr = 'C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
if (-not (Test-Path $base)) { throw 'baseline dll missing' }
if (-not (Test-Path $curr)) { throw 'current dll missing' }

$inputs = @(
  @{ Name='restpoly1k (2-voice rests; voiced-rest side)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-restpoly1k.lys'; Hash=$true },
  @{ Name='plain1k (dense control; hash must match)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-plain1k.lys'; Hash=$true }
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
