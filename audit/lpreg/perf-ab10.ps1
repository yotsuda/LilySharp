# A/B perf round 10 (session 113: repeat-tie chord fan + SemiTiesOf shared column
# no.40, chord tremolo wiring no.41, rest voiced-position + spanning collision no.42).
# 999-bar inputs, interleaved runs, median-of-5 BOTH orders.
# Base = 8a1e92db (the commit before this session's first repair).
# Heavy sides of THESE changes:
#  - perf-chordsemi1k: 4 tie-less quarter chords per bar = ItemSkylineFactory now
#    walks every chord's members TWICE (once per semi-tie kind) per column build,
#    plus the collector's member loop scanning two names. NO semi-tie anywhere.
#    Output must be HASH-IDENTICAL to base.
#  - perf-restpoly1k: 2 voices, 4 printed rests per bar under a held whole =
#    CalculateRestNoteCollisions' spanning-interval scan + StemTipPositionOf via
#    StemCalculator + base offsets for every rest. Rest Y moved BY DESIGN -> no hash.
$base = 'C:\MyProj\LilySharp-perfbase-8a1e\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
$curr = 'C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
if (-not (Test-Path $base)) { throw 'baseline dll missing' }
if (-not (Test-Path $curr)) { throw 'current dll missing' }

$inputs = @(
  @{ Name='chordsemi1k (tie-less chords; hash must match)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-chordsemi1k.lys'; Hash=$true },
  @{ Name='restpoly1k (2-voice rests; moved by design)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-restpoly1k.lys'; Hash=$false }
)

function MedianOf($xs) { $s = $xs | Sort-Object; $s[[int](($s.Count - 1) / 2)] }

foreach ($inp in $inputs) {
  foreach ($order in 'base-first', 'curr-first') {
    $tb = @(); $tc = @()
    & dotnet $base svg $inp.File C:\MyProj\LilySharp\audit\lpreg\perf-wb.svg 2>&1 | Out-Null
    & dotnet $curr svg $inp.File C:\MyProj\LilySharp\audit\lpreg\perf-wc.svg 2>&1 | Out-Null
    for ($i = 0; $i -lt 5; $i++) {
      if ($order -eq 'base-first') {
        $tb += (Measure-Command { & dotnet $base svg $inp.File C:\MyProj\LilySharp\audit\lpreg\perf-b.svg 2>&1 | Out-Null }).TotalMilliseconds
        $tc += (Measure-Command { & dotnet $curr svg $inp.File C:\MyProj\LilySharp\audit\lpreg\perf-c.svg 2>&1 | Out-Null }).TotalMilliseconds
      } else {
        $tc += (Measure-Command { & dotnet $curr svg $inp.File C:\MyProj\LilySharp\audit\lpreg\perf-c.svg 2>&1 | Out-Null }).TotalMilliseconds
        $tb += (Measure-Command { & dotnet $base svg $inp.File C:\MyProj\LilySharp\audit\lpreg\perf-b.svg 2>&1 | Out-Null }).TotalMilliseconds
      }
    }
    $mb = MedianOf $tb; $mc = MedianOf $tc
    "{0} [{1}]: base med {2:F0} ms | curr med {3:F0} ms | delta {4:+0.0;-0.0}%  (base: {5} / curr: {6})" -f `
      $inp.Name, $order, $mb, $mc, (($mc - $mb) / $mb * 100),
      (($tb | ForEach-Object { '{0:F0}' -f $_ }) -join ' '), (($tc | ForEach-Object { '{0:F0}' -f $_ }) -join ' ')
  }
  if ($inp.Hash) {
    $hb = (Get-FileHash C:\MyProj\LilySharp\audit\lpreg\perf-b.svg).Hash
    $hc = (Get-FileHash C:\MyProj\LilySharp\audit\lpreg\perf-c.svg).Hash
    "  svg hash: base $($hb.Substring(0,12)) / curr $($hc.Substring(0,12)) match=$($hb -eq $hc)"
  }
}
