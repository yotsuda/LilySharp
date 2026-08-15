# A/B perf: baseline (965cd92f) vs current, Release CLI, median of last 5 of 7 runs.
param()
$base = 'C:\MyProj\LilySharp-perfbase-965\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
$curr = 'C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
if (-not (Test-Path $base)) { throw 'baseline dll missing' }
if (-not (Test-Path $curr)) { throw 'current dll missing' }

$inputs = @(
  @{ Name='hairpin100 (100小節×hairpin+@f)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-hairpin.lys' },
  @{ Name='dots-poly200 (多声dotted 200 moment)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-dots.lys' },
  @{ Name='03-piano (通常プレビュー相当)'; File='C:\MyProj\LilySharp\LilySharp.Tests\Fixtures\showcase\03-piano.lys' }
)

function Median([double[]]$v) { $s = $v | Sort-Object; $s[[int]($s.Count/2)] }

foreach ($inp in $inputs) {
  foreach ($side in @(@{L='base';D=$base},@{L='curr';D=$curr})) {
    $times = @()
    for ($i = 0; $i -lt 7; $i++) {
      $out = "C:\MyProj\LilySharp\audit\lpreg\perf-out-$($side.L).svg"
      $t = Measure-Command { & dotnet $side.D svg $inp.File $out 2>&1 | Out-Null }
      $times += $t.TotalMilliseconds
    }
    $warm = $times[2..6]
    "{0}  {1}: median {2:F0} ms  (warm runs: {3})" -f $inp.Name, $side.L,
      (Median $warm), (($warm | ForEach-Object { '{0:F0}' -f $_ }) -join ' ')
  }
}
