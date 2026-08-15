# A/B perf round 30 (session 129: the voice-context boundary of the left-head refinement).
# Interleaved runs, median-of-5 BOTH orders, SVG hash compared.
# Base = a70a0fd8 (the commit this session started from) in C:\MyProj\LilySharp-perfbase-a70a.
#
# WHAT THIS SESSION ADDED, COUNTED FIRST (HANDOFF §5.3) — three reaches, only one of which
# touches a score with no combiner:
#  1. MusicItem.VoiceContext, one enum on the BASE record. Nothing reads it outside spacing,
#     but HashContent is REFLECTIVE (exclusion list), so the new field joins the per-item
#     content hash of EVERY item type — and that hash is on the incremental/diagnostics path.
#     This is the only cost a plain score pays, and it is the same shape as round 29's bool.
#  2. PartCombiner.InContext: one shallow record clone per ROUTED item. Inside PlaceItems, so
#     a score without a `combinedStaff` pays exactly nothing. ⚠️ It is a SECOND clone on the
#     items that WithVoiceDirection already copied; folding the two would need one `with` per
#     record arm, which is three more spellings of the routing — not taken, disclosed here.
#  3. SpacingRules.ContextMask replacing the old cue-only loop in CrossesVoiceBoundary. Same
#     two passes over the same two item lists as before (once per column pair), plus one type
#     test per item; no allocation in either version.
#
# Inputs, and what each is here to charge:
#  - plain1k:  1000 bars of plain notes, no combiner. Only reach 1 touches it. Drift gauge.
#              -> hash MATCH
#  - comb300:  300 bars of notes through the combiner, no rests. Charges reaches 2 and 3 on
#              the one score shape that has them. ⚠️ Its hash is NOT predicted: whether an
#              all-notes combined score has any pair that no context spans is exactly what
#              this run is here to find out. A DIFFER here means the fix reaches ordinary
#              combined scores and every such pair needs an LP number before it is believed.
#  - combmmr:  two combined all-rest parts whose boundaries disagree (round 29's book). Rests
#              route through shared/solo/null, so this is the shape the boundary fires on.
$base = 'C:\MyProj\LilySharp-perfbase-a70a\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
$curr = 'C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
if (-not (Test-Path $base)) { throw 'baseline dll missing' }
if (-not (Test-Path $curr)) { throw 'current dll missing' }

$dir = 'C:\MyProj\LilySharp\audit\lpreg'
$inputs = @(
  @{ Name='plain1k (drift gauge: only the item hash reaches it)'; File="$dir\perf-plain1k.lys";  Expect='MATCH'   },
  @{ Name='comb300 (combiner, all notes = reaches 2+3)';          File="$dir\perf-comb300.lys";  Expect='UNKNOWN' },
  @{ Name='combmmr (combined rests = the shape the boundary fires on)'; File="$dir\perf29-combmmr.lys"; Expect='UNKNOWN' }
)

function MedianOf($xs) { $s = $xs | Sort-Object; $s[[int](($s.Count - 1) / 2)] }
function MinOf($xs) { ($xs | Measure-Object -Minimum).Minimum }

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
    $hb = (Get-FileHash "$dir\perf-b.svg").Hash
    $hc = (Get-FileHash "$dir\perf-c.svg").Hash
    # Round 29 measured this machine's spread at 40% on plain1k, so the FLOOR is reported
    # beside the median: noise only ever adds time, which makes the minimum the robust read.
    $medB = MedianOf $tb; $medC = MedianOf $tc
    $minB = MinOf $tb;    $minC = MinOf $tc
    $dMed = ($medC - $medB) / $medB * 100
    $dMin = ($minC - $minB) / $minB * 100
    $hash = if ($hb -eq $hc) { 'MATCH' } else { 'DIFFER' }
    '{0} [{1}]: base med {2:F0} / floor {3:F0} ms | curr med {4:F0} / floor {5:F0} ms | med {6:+0.0;-0.0}% floor {7:+0.0;-0.0}% | hash {8}' -f `
      $inp.Name, $order, $medB, $minB, $medC, $minC, $dMed, $dMin, $hash
  }
}
