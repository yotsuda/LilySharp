# A/B perf round 22 (session 121: staff-gap on a touching point).
# Heavy sides this change created:
#   restpoly1k — 2-voice book: the skyline seed now looks up a collision shift per
#                item (memoised CWT shared with ApplyCrossVoiceColumnSpacing), and
#                shifted voices' seed boxes move. Output MAY legitimately move where
#                a shifted voice's ink binds a staff gap — single staff here, so the
#                inter-SYSTEM silhouette is the consumer.
#   plain1k    — single voice: the shift path never fires; only the <= in the
#                distance walks. Hash must MATCH base.
# Base = ce9e8774 worktree (HEAD before session 121's fix).
# Interleaved, median-of-3, BOTH orders. ⚠️ No tests running alongside.
$base = 'C:\MyProj\LilySharp-perfbase-ca46\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
$curr = 'C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
if (-not (Test-Path $base)) { throw 'baseline dll missing' }
if (-not (Test-Path $curr)) { throw 'current dll missing' }

$inputs = @(
  @{ Name='restpoly1k (2 voices; shift lookup + moved seed side)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-restpoly1k.lys'; Hash=$true },
  @{ Name='plain1k (single voice; hash must match)'; File='C:\MyProj\LilySharp\audit\lpreg\perf-plain1k.lys'; Hash=$true }
)

function MedianOf($xs) { $s = $xs | Sort-Object; $s[[int](($s.Count - 1) / 2)] }

foreach ($inp in $inputs) {
  foreach ($order in 'base-first', 'curr-first') {
    $tb = @(); $tc = @()
    & dotnet $base svg $inp.File C:\MyProj\LilySharp\audit\lpreg\perf-wb.svg 2>&1 | Out-Null
    & dotnet $curr svg $inp.File C:\MyProj\LilySharp\audit\lpreg\perf-wc.svg 2>&1 | Out-Null
    for ($i = 0; $i -lt 3; $i++) {
      if ($order -eq 'base-first') {
        $tb += (Measure-Command { & dotnet $base svg $inp.File C:\MyProj\LilySharp\audit\lpreg\perf-b.svg 2>&1 | Out-Null }).TotalMilliseconds
        $tc += (Measure-Command { & dotnet $curr svg $inp.File C:\MyProj\LilySharp\audit\lpreg\perf-c.svg 2>&1 | Out-Null }).TotalMilliseconds
      } else {
        $tc += (Measure-Command { & dotnet $curr svg $inp.File C:\MyProj\LilySharp\audit\lpreg\perf-c.svg 2>&1 | Out-Null }).TotalMilliseconds
        $tb += (Measure-Command { & dotnet $base svg $inp.File C:\MyProj\LilySharp\audit\lpreg\perf-b.svg 2>&1 | Out-Null }).TotalMilliseconds
      }
    }
    $mb = MedianOf $tb; $mc = MedianOf $tc
    '{0} [{1}]: base {2:n0} ms / curr {3:n0} ms = {4:+0.0;-0.0}%' -f $inp.Name, $order, $mb, $mc, (($mc - $mb) / $mb * 100)
  }
  if ($inp.Hash) {
    $hb = (Get-FileHash C:\MyProj\LilySharp\audit\lpreg\perf-b.svg).Hash
    $hc = (Get-FileHash C:\MyProj\LilySharp\audit\lpreg\perf-c.svg).Hash
    'hash: ' + $(if ($hb -eq $hc) { 'MATCH' } else { "MISMATCH base=$hb curr=$hc" })
  }
}
