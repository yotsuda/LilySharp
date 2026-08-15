# A/B round 10b: chordsemi1k re-run after the AddSemiTies tie-less early-out.
$base = 'C:\MyProj\LilySharp-perfbase-8a1e\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
$curr = 'C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
$file = 'C:\MyProj\LilySharp\audit\lpreg\perf-chordsemi1k.lys'
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
  "chordsemi1k-b [{0}]: base med {1:F0} ms | curr med {2:F0} ms | delta {3:+0.0;-0.0}%  (base: {4} / curr: {5})" -f `
    $order, $mb, $mc, (($mc - $mb) / $mb * 100),
    (($tb | ForEach-Object { '{0:F0}' -f $_ }) -join ' '), (($tc | ForEach-Object { '{0:F0}' -f $_ }) -join ' ')
}
$hb = (Get-FileHash C:\MyProj\LilySharp\audit\lpreg\perf-b.svg).Hash
$hc = (Get-FileHash C:\MyProj\LilySharp\audit\lpreg\perf-c.svg).Hash
"  svg hash: base $($hb.Substring(0,12)) / curr $($hc.Substring(0,12)) match=$($hb -eq $hc)"
