# Round 29b: how wide is this machine's band, really?
#
# Round 29's drift gauge (plain1k) reversed sign between orders (-2.4% then +23.1%) and the
# BASE BINARY ITSELF moved 10653 -> 11902 ms between the two orders. A band that wide makes
# every MATCH row in round 29 uninformative, so measure the band directly instead of reading
# through it: run the SAME base dll against ITSELF, ten times, and report the spread.
# Whatever this says is the floor on what any A/B claim in round 29 is allowed to mean.
$base = 'C:\MyProj\LilySharp-perfbase-ee1e\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
$dir  = 'C:\MyProj\LilySharp\audit\lpreg'
$file = "$dir\perf-plain1k.lys"

& dotnet $base svg $file "$dir\perf-w.svg" 2>&1 | Out-Null   # warm

$t = @()
for ($i = 0; $i -lt 10; $i++) {
  $t += (Measure-Command { & dotnet $base svg $file "$dir\perf-x.svg" 2>&1 | Out-Null }).TotalMilliseconds
}
$s = $t | Sort-Object
$min = $s[0]; $max = $s[-1]; $med = $s[[int](($s.Count - 1) / 2)]
"samples : {0}" -f (($t | ForEach-Object { '{0:F0}' -f $_ }) -join ', ')
"min {0:F0} | median {1:F0} | max {2:F0} ms" -f $min, $med, $max
"spread  : {0:F1}% of median (max-min)" -f (($max - $min) / $med * 100)
"=> no A/B delta smaller than this can be read as a code effect."
