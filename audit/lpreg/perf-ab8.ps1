# A/B perf round 8 (session 111: percent/tab, main-notehead alignment, l.v.
# chords + spacing boxes, extender completize, broken-connector Y).
# 999-bar inputs, interleaved runs, median-of-5 BOTH orders.
# Base = 965f4b39 (the commit before this session's first repair).
# Heavy sides of THESE changes:
#  - chordsec1k: suspended-second chords x4 per bar = the collect walk's per-member
#    lv detection, ItemSkylineFactory.AddSemiTies switch, the right-extent lv loop
#    and RhythmicHeadExtent all run on EVERY chord with no l.v. present (the
#    default cost). No lyrics/@text/% -> output must be HASH-IDENTICAL.
#  - lyrhyph1k: melody + hyphenated lyrics over many systems = lyric centring
#    (main-extent), hyphen layout incl. crossing pairs. Crossing second-dash Y
#    moved BY DESIGN (no.37 repair), so no hash here.
$base = 'C:\MyProj\LilySharp-perfbase-965f\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
$curr = 'C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
if (-not (Test-Path $base)) { throw 'baseline dll missing' }
if (-not (Test-Path $curr)) { throw 'current dll missing' }

$inputs = @(
  @{ Name='chordsec1k (chords, default lv cost; hash must match)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-chordsec1k.lys'; Hash=$true },
  @{ Name='lyrhyph1k (lyrics+hyphens; crossing Y moved by design)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-lyrhyph1k.lys'; Hash=$false }
)

function MedianOf($xs) { $s = $xs | Sort-Object; $s[[int](($s.Count - 1) / 2)] }

foreach ($inp in $inputs) {
  foreach ($order in 'base-first', 'curr-first') {
    $tb = @(); $tc = @()
    & dotnet $base svg $inp.File -o C:\MyProj\LilySharp\audit\lpreg\perf-wb.svg 2>&1 | Out-Null
    & dotnet $curr svg $inp.File -o C:\MyProj\LilySharp\audit\lpreg\perf-wc.svg 2>&1 | Out-Null
    for ($i = 0; $i -lt 5; $i++) {
      if ($order -eq 'base-first') {
        $tb += (Measure-Command { & dotnet $base svg $inp.File -o C:\MyProj\LilySharp\audit\lpreg\perf-b.svg 2>&1 | Out-Null }).TotalMilliseconds
        $tc += (Measure-Command { & dotnet $curr svg $inp.File -o C:\MyProj\LilySharp\audit\lpreg\perf-c.svg 2>&1 | Out-Null }).TotalMilliseconds
      } else {
        $tc += (Measure-Command { & dotnet $curr svg $inp.File -o C:\MyProj\LilySharp\audit\lpreg\perf-c.svg 2>&1 | Out-Null }).TotalMilliseconds
        $tb += (Measure-Command { & dotnet $base svg $inp.File -o C:\MyProj\LilySharp\audit\lpreg\perf-b.svg 2>&1 | Out-Null }).TotalMilliseconds
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
