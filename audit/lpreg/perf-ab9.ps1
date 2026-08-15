# A/B perf round 9 (session 112: hyphen print rewrite no.38, melisma left-align +
# span reservation + extender held-end no.39, audit tags only thereafter).
# 999-bar inputs, interleaved runs, median-of-5 BOTH orders.
# Base = 502660d2 (the commit before this session's first repair).
# Heavy sides of THESE changes:
#  - perf-lyrmel1k: hyphen + __ ~ extender EVERY bar = AppendDashes per hyphen,
#    collector marker rewrites, melisma left-align edges, HeldEndInkRight's
#    systems scan per extender (the only O(bars) probe per item this session).
#    Hyphen Y/thickness and extender end moved BY DESIGN -> no hash.
#  - perf-lyrplain1k: plain centred syllables = the common-case cost of the
#    (Left, Centre) edges refactor and the span reservation rewrite with NO
#    melisma anywhere. Output must be HASH-IDENTICAL to base.
$base = 'C:\MyProj\LilySharp-perfbase-5026\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
$curr = 'C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
if (-not (Test-Path $base)) { throw 'baseline dll missing' }
if (-not (Test-Path $curr)) { throw 'current dll missing' }

$inputs = @(
  @{ Name='lyrmel1k (hyphen+melisma extender; moved by design)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-lyrmel1k.lys'; Hash=$false },
  @{ Name='lyrplain1k (plain lyrics; hash must match)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-lyrplain1k.lys'; Hash=$true }
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
