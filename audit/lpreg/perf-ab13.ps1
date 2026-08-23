# A/B perf round 13 (session 115: script avoid-slur — outside_slur_callback port).
# 999-bar inputs, interleaved runs, median-of-5 BOTH orders.
# Base = 286cea23 (the commit before the avoid-slur port; ties already in).
# Heavy sides of THIS change:
#  - perf-slurscript1k: every bar one slur over 4 accented notes = every script
#    hits the measure-keyed slur dict, scans that measure's pieces and samples the
#    curve. Script Y moves BY DESIGN -> no hash.
#  - perf-scriptsym1k: accent+staccato, no slurs = slursAtMeasure stays null, the
#    default cost side. Output must HASH-match base.
$base = 'C:\MyProj\LilySharp-perfbase-f2ad\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
$curr = 'C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
if (-not (Test-Path $base)) { throw 'baseline dll missing' }
if (-not (Test-Path $curr)) { throw 'current dll missing' }

$inputs = @(
  @{ Name='slurscript1k (slurred accents; moved by design)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-slurscript1k.lys'; Hash=$false },
  @{ Name='scriptsym1k (accent+staccato, slur-less; hash must match)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-scriptsym1k.lys'; Hash=$true }
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
