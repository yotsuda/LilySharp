# A/B perf round 14 (session 116: slur-scoring rebuild — real curve generation,
# LP attachment grid, dot extra-encompass).
# Interleaved runs, median-of-5 BOTH orders.
# Base = 6e80a05c (the commit before the slur-scoring rebuild).
# Heavy sides of THIS change:
#  - perf-slur300: every bar two slurred quarter pairs = every slur runs the
#    full grid (≈100 candidates × real bezier generation + cubic-solve scoring).
#    Slur curves move BY DESIGN -> no hash. 300 bars, not 999: the slur book
#    scales superlinearly in BOTH builds (a shared pre-existing regime), so a
#    999-bar run takes minutes per side and measures the same ratio.
#  - perf-scriptsym1k: accent+staccato, no slurs = LayoutSlurs early-returns,
#    the default cost side. Output must HASH-match base.
# ⚠️ NOT used as heavy side: dotted books (perf-slurdot1k). Their cost is a
# PRE-EXISTING dot-path quadratic (base cc19cccc Debug 127 s vs curr 134 s on
# a 200-bar all-dotted SLUR-LESS book) — measured separately, ticketed in the
# handoff, not this change's bill.
$base = 'C:\MyProj\LilySharp-perfbase-6e80\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
$curr = 'C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
if (-not (Test-Path $base)) { throw 'baseline dll missing' }
if (-not (Test-Path $curr)) { throw 'current dll missing' }

$inputs = @(
  @{ Name='slur300 (slurred quarters; moved by design)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-slur300.lys'; Hash=$false },
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
