# A/B perf round 6 (session 109: styled-head stem attachment + glissando bounds):
# 1000-bar inputs, interleaved runs, median-of-5 BOTH orders (this machine's
# batch-head boost trap: the first run of a batch is systematically fast, so
# read medians and a control book, never min alone).
# Base = a9c7b576 (pre-session-109). Heavy sides of THESE changes:
#  - plain1k: every stem's X now goes through the (style, half) switch instead of
#    a constant read (down side) — the whole-corpus overhead; output must be
#    HASH-IDENTICAL to base (no styled heads, no glissandi).
#  - styled1k: the styled lookup itself, per head, seeds + draw.
#  - glissnote1k: equal-work note glissandi (both sides draw them); current adds
#    the per-gliss head/accidental anchor reads.
#  - glisschord1k: chord glissando fans — base drew NOTHING here (the repaired
#    silent drop), so this is the new feature's own cost, not a regression read.
$base = 'C:\MyProj\LilySharp-perfbase-a9c7\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
$curr = 'C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
if (-not (Test-Path $base)) { throw 'baseline dll missing' }
if (-not (Test-Path $curr)) { throw 'current dll missing' }

$inputs = @(
  @{ Name='plain1k (control: bare eighths, hash must match)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-plain1k.lys'; Hash=$true },
  @{ Name='styled1k (x/triangle/diamond heads dense)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-styled1k.lys'; Hash=$false },
  @{ Name='glissnote1k (3 note glissandi per bar)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-glissnote1k.lys'; Hash=$false },
  @{ Name='glisschord1k (3 chord glissandi per bar, accidentals)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-glisschord1k.lys'; Hash=$false }
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
