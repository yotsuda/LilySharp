# A/B perf round 5 (session 108 script support-chain + outline seed):
# 1000-bar inputs, interleaved runs, median-of-5 BOTH orders (session 107's
# batch-head boost trap: the first run of a batch is systematically fast, so
# read medians and a control book, never min alone).
# Base = 21586699 (pre-session-108). Heavy side of THIS change: above-staff
# priority-less scripts poking over the staff (seed walks the outline profile
# per script per pass) + a stacked pair per bar (engraver support chain).
$base = 'C:\MyProj\LilySharp-perfbase-2158\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
$curr = 'C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
if (-not (Test-Path $base)) { throw 'baseline dll missing' }
if (-not (Test-Path $curr)) { throw 'current dll missing' }

$inputs = @(
  @{ Name='scripts1k (accent+staccato dense, above staff)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-scripts1k.lys' },
  @{ Name='noscripts1k (control, same notes bare)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-noscripts1k.lys' }
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
}
# SVG identity check on the SCRIPT book (the change is expected to move stacked
# scripts, so identity is informational, not required):
$hb = (Get-FileHash C:\MyProj\LilySharp\audit\lpreg\perf-b.svg).Hash
$hc = (Get-FileHash C:\MyProj\LilySharp\audit\lpreg\perf-c.svg).Hash
"last book svg hash: base $($hb.Substring(0,12)) / curr $($hc.Substring(0,12)) match=$($hb -eq $hc)"
