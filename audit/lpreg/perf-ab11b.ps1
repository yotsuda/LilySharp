# A/B round 11b: fingstack1k only, after the DigitRun memo (single-digit table) —
# re-measuring the +2.9/+5.0% both-orders-positive suspect from round 11.
$base = 'C:\MyProj\LilySharp-perfbase-b0b4\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
$curr = 'C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
if (-not (Test-Path $base)) { throw 'baseline dll missing' }
if (-not (Test-Path $curr)) { throw 'current dll missing' }
$file = 'C:\MyProj\LilySharp\audit\lpreg\perf-fingstack1k.lys'
function MedianOf($xs) { $s = $xs | Sort-Object; $s[[int](($s.Count - 1) / 2)] }
foreach ($order in 'base-first', 'curr-first') {
  $tb = @(); $tc = @()
  & dotnet $base svg $file C:\MyProj\LilySharp\audit\lpreg\perf-wb.svg 2>&1 | Out-Null
  & dotnet $curr svg $file C:\MyProj\LilySharp\audit\lpreg\perf-wc.svg 2>&1 | Out-Null
  for ($i = 0; $i -lt 5; $i++) {
    if ($order -eq 'base-first') {
      $tb += (Measure-Command { & dotnet $base svg $file C:\MyProj\LilySharp\audit\lpreg\perf-b.svg 2>&1 | Out-Null }).TotalMilliseconds
      $tc += (Measure-Command { & dotnet $curr svg $file C:\MyProj\LilySharp\audit\lpreg\perf-c.svg 2>&1 | Out-Null }).TotalMilliseconds
    } else {
      $tc += (Measure-Command { & dotnet $curr svg $file C:\MyProj\LilySharp\audit\lpreg\perf-c.svg 2>&1 | Out-Null }).TotalMilliseconds
      $tb += (Measure-Command { & dotnet $base svg $file C:\MyProj\LilySharp\audit\lpreg\perf-b.svg 2>&1 | Out-Null }).TotalMilliseconds
    }
  }
  $mb = MedianOf $tb; $mc = MedianOf $tc
  "fingstack1k [{0}]: base med {1:F0} ms | curr med {2:F0} ms | delta {3:+0.0;-0.0}%  (base: {4} / curr: {5})" -f `
    $order, $mb, $mc, (($mc - $mb) / $mb * 100),
    (($tb | ForEach-Object { '{0:F0}' -f $_ }) -join ' '), (($tc | ForEach-Object { '{0:F0}' -f $_ }) -join ' ')
}
