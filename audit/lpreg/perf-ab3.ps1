# A/B perf round 3 (session 106 dynamics/hairpin work): 1000-bar inputs,
# interleaved runs, min-of-5. Base = b6d6dfb4 (pre-session-106).
$base = 'C:\MyProj\LilySharp-perfbase-b6d6\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
$curr = 'C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
if (-not (Test-Path $base)) { throw 'baseline dll missing' }
if (-not (Test-Path $curr)) { throw 'current dll missing' }

$inputs = @(
  @{ Name='hairpin1k  (1000小節 hairpin+@f)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-hairpin1k.lys' },
  @{ Name='dots-poly1k (多声dotted 2000 moment)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-dots1k.lys' }
)

foreach ($inp in $inputs) {
  $tb = @(); $tc = @()
  & dotnet $base svg $inp.File C:\MyProj\LilySharp\audit\lpreg\perf-wb.svg 2>&1 | Out-Null
  & dotnet $curr svg $inp.File C:\MyProj\LilySharp\audit\lpreg\perf-wc.svg 2>&1 | Out-Null
  for ($i = 0; $i -lt 5; $i++) {
    $tb += (Measure-Command { & dotnet $base svg $inp.File C:\MyProj\LilySharp\audit\lpreg\perf-b.svg 2>&1 | Out-Null }).TotalMilliseconds
    $tc += (Measure-Command { & dotnet $curr svg $inp.File C:\MyProj\LilySharp\audit\lpreg\perf-c.svg 2>&1 | Out-Null }).TotalMilliseconds
  }
  $mb = ($tb | Measure-Object -Minimum).Minimum; $mc = ($tc | Measure-Object -Minimum).Minimum
  "{0}:  base min {1:F0} ms | curr min {2:F0} ms | delta {3:+0.0;-0.0}%  (base: {4} / curr: {5})" -f `
    $inp.Name, $mb, $mc, (($mc-$mb)/$mb*100),
    (($tb | ForEach-Object { '{0:F0}' -f $_ }) -join ' '), (($tc | ForEach-Object { '{0:F0}' -f $_ }) -join ' ')
}
