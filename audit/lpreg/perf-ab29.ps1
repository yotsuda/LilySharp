# A/B perf round 29 (session 128: written-rest run boundaries + the combiner's boundary union).
# Interleaved runs, median-of-5 BOTH orders, SVG hash compared.
# Base = c1f504fd (the commit this session started from) in C:\MyProj\LilySharp-perfbase-ee1e.
#
# WHAT THIS SESSION ADDED, COUNTED FIRST (HANDOFF §5.3) — two changes, two reaches:
#  1. RestItem.OpensWrittenRun + MultiMeasureRestEngraver.StartsWrittenRest. The flag is one
#     bool on a record; the read is a short scan past leading break-aligned changes plus one
#     type test, and it runs ONLY from the run-extension loop, i.e. only over bars where every
#     staff already rests. ⚠️ The one cost a score with NO multi-measure rest still pays is
#     that HashContent is REFLECTIVE (only SourcePosition is excluded), so the new bool joins
#     the per-item content hash — and that hash is on the incremental/diagnostics path.
#     Also: the expansion loop clones the interior copy ONCE instead of per bar (it used to be
#     per bar in the first draft; the counting is what found it).
#  2. PartCombiner.UnionWrittenRestBoundaries. Runs ONCE per Combine, walking min(n1,n2) bars
#     with an O(items) scan each, allocating only where a boundary actually differs. Inside
#     Combine, so a score without a `combinedStaff` pays exactly nothing.
#
# Inputs, and what each is here to charge:
#  - plain1k:  1000 bars of plain notes. Neither change reaches it except through the item
#              hash. This is the machine-drift gauge (§5.3): if THIS moves, the number is the
#              machine and not the code.  -> hash MATCH
#  - mmr1k:    200 written R1*4 SEPARATED by note bars. Charges the run walk and the expansion
#              WITHOUT changing the grouping (no two written rests are adjacent), so it prices
#              change 1 with the output held still.  -> hash MATCH
#  - comb300:  300 bars of notes through the combiner. No rests at all, so the union pass walks
#              every bar and folds nothing: this is change 2's pure overhead.  -> hash MATCH
#  - mmradj:   250 ADJACENT written R1*4. -> hash DIFFERS BY DESIGN: one run of 1000 becomes
#              250 runs of 4. Prices the new grouping including the 250 extra MMR layouts.
#  - combmmr:  two combined parts of all-rests whose boundaries disagree (every 3 against every
#              2), so the fold fires on most bars. -> hash DIFFERS BY DESIGN.
#
# ⚠️ A DIFFER row is not a regression and a slower DIFFER row is not necessarily a cost of the
# code — the engine is drawing MORE symbols than before, because that is the fix. Read the
# MATCH rows for cost and the DIFFER rows for "what the new picture costs to draw".
$base = 'C:\MyProj\LilySharp-perfbase-ee1e\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
$curr = 'C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
if (-not (Test-Path $base)) { throw 'baseline dll missing' }
if (-not (Test-Path $curr)) { throw 'current dll missing' }

$dir = 'C:\MyProj\LilySharp\audit\lpreg'
$inputs = @(
  @{ Name='plain1k (drift gauge: only the item hash reaches it)'; File="$dir\perf-plain1k.lys";      Expect='MATCH'  },
  @{ Name='mmr1k   (200 separated R1*4 = change 1, output held still)'; File="$dir\perf-mmr1k.lys";  Expect='MATCH'  },
  @{ Name='comb300 (combiner, no rests = change 2 pure overhead)'; File="$dir\perf-comb300.lys";     Expect='MATCH'  },
  @{ Name='mmradj  (250 ADJACENT R1*4 = the new grouping)'; File="$dir\perf29-mmradj.lys";           Expect='DIFFER' },
  @{ Name='combmmr (disagreeing combined rests = the fold firing)'; File="$dir\perf29-combmmr.lys";  Expect='DIFFER' }
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
    $got = $(if ($hb -eq $hc) { 'MATCH' } else { 'DIFFER' })
    "{0} [{1}]: base {2:F0} ms | curr {3:F0} ms | delta {4:+0.0;-0.0}% | hash {5}{6}" -f `
      $inp.Name, $order, $mb, $mc, (($mc - $mb) / $mb * 100), $got,
      $(if ($got -ne $inp.Expect) { "  <-- EXPECTED $($inp.Expect)" } else { '' })
  }
}
