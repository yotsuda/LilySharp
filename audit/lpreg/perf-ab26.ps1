# A/B perf round 26 (session 125: LYS0020 top-level-music rejection, empty-chord slur +
# duration, condensedStaff).
# 1000-bar inputs, interleaved runs, median-of-5 BOTH orders.
# Base = dd6de6f6 (the commit before this session's first change).
#
# Heavy sides of THESE changes:
#  - chordsemi1k / chordsec1k: CHORDS every bar. MeasureDurations.ItemDuration now asks
#    ChordSyntax.IsEmpty of every chord (it asked nothing before), and the music walk asks
#    it too. That is the one NEW per-chord cost this session, so a chord-dense book is the
#    side to watch.
#  - plain1k: plain notes, no chords and no `<>` anywhere. This is where the parser's new
#    IsTopLevelMusicStart (once per top-level item) and the walk's new pending-slur flag
#    check (once per item) show up with nothing to hide behind.
# Output must be HASH-IDENTICAL on every input: none of this session's changes moves a
# glyph in a file that was already valid.
$base = 'C:\MyProj\LilySharp-perfbase-dd6de6f6\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
$curr = 'C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
if (-not (Test-Path $base)) { throw 'baseline dll missing' }
if (-not (Test-Path $curr)) { throw 'current dll missing' }

$dir = 'C:\MyProj\LilySharp\audit\lpreg'
$inputs = @(
  @{ Name='chordsemi1k (chords every bar = the new per-chord IsEmpty)'; File="$dir\perf-chordsemi1k.lys" },
  @{ Name='chordsec1k  (chord sections)';                               File="$dir\perf-chordsec1k.lys" },
  @{ Name='plain1k     (non-contact control)';                          File="$dir\perf-plain1k.lys" }
)

function MedianOf($xs) { $s = $xs | Sort-Object; $s[[int](($s.Count - 1) / 2)] }

foreach ($inp in $inputs) {
  if (-not (Test-Path $inp.File)) { "SKIP (missing): $($inp.Name)"; continue }
  foreach ($order in 'base-first', 'curr-first') {
    $tb = @(); $tc = @()
    & dotnet $base svg $inp.File "$dir\perf-wb.svg" 2>&1 | Out-Null   # warm
    & dotnet $curr svg $inp.File "$dir\perf-wc.svg" 2>&1 | Out-Null
    for ($i = 0; $i -lt 5; $i++) {
      if ($order -eq 'base-first') {
        $tb += (Measure-Command { & dotnet $base svg $inp.File "$dir\perf-b.svg" 2>&1 | Out-Null }).TotalMilliseconds
        $tc += (Measure-Command { & dotnet $curr svg $inp.File "$dir\perf-c.svg" 2>&1 | Out-Null }).TotalMilliseconds
      } else {
        $tc += (Measure-Command { & dotnet $curr svg $inp.File "$dir\perf-c.svg" 2>&1 | Out-Null }).TotalMilliseconds
        $tb += (Measure-Command { & dotnet $base svg $inp.File "$dir\perf-b.svg" 2>&1 | Out-Null }).TotalMilliseconds
      }
    }
    $mb = MedianOf $tb; $mc = MedianOf $tc
    $hb = (Get-FileHash "$dir\perf-b.svg").Hash
    $hc = (Get-FileHash "$dir\perf-c.svg").Hash
    "{0} [{1}]: base {2:F0} ms | curr {3:F0} ms | delta {4:+0.0;-0.0}% | hash {5}" -f `
      $inp.Name, $order, $mb, $mc, (($mc - $mb) / $mb * 100), $(if ($hb -eq $hc) { 'MATCH' } else { 'DIFFER' })
  }
}
