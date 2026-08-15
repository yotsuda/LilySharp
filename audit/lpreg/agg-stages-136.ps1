# TEMPORARY — session 136, HANDOFF §1 ▶ ⒟⁵. Groups the stage log by ROUND, classifies each
# round by the regime its own log line reports (skip=/reuse=), and prints the per-stage FLOOR
# (min across rounds of the same regime) — HANDOFF §5.3: with a >50% band the minimum is the
# robust statistic, and the regime must never be averaged across.
param([string]$Path = 'C:\MyProj\LilySharp\audit\lpreg\stages-136.txt')

$rounds = @{}          # round -> ordered list of (stage, ms)
$regime = @{}          # round -> "reuse" | "gate-held" | "gate-moved"
foreach ($line in Get-Content $Path) {
    $f = $line -split "`t"
    if ($f[0] -eq 'NOTE') {
        if ($f[2] -match 'skip=(\w+) reuse=(\w+)') {
            $regime[$f[1]] = if ($Matches[2] -eq 'True') { 'reuse' }
                             elseif ($Matches[1] -eq 'True') { 'gate-held' } else { 'gate-moved' }
        }
        continue
    }
    if ($f[0] -ne 'STAGE') { continue }
    if (-not $rounds.ContainsKey($f[1])) { $rounds[$f[1]] = @{} }
    # a stage name can repeat within a round only if Layout ran twice; it does not here.
    $rounds[$f[1]][$f[2]] = [double]$f[3]
}

# The unlabelled '-' rows are the warm-up (full compile + one unchanged re-render); they carry
# two regimes under one tag, so they are reported separately and never merged with the rounds.
$order = 'compile.collect','compile.contentkey','compile.springs','compile.gate',
         'layout.prologue','layout.break','layout.firstsystem','layout.persystem',
         'layout.extents','layout.prelimannotation','layout.pages','layout.loosechain',
         'layout.spanners','layout.annotationctx','layout.annotationpass',
         'layout.voicecollisions','layout.finalize','compile.layout','compile.render'

foreach ($r in 'gate-held','gate-moved','reuse') {
    $keys = $rounds.Keys | Where-Object { $regime[$_] -eq $r -and $_ -notlike '*warm*' -and $_ -ne '-' }
    if (-not $keys) { continue }
    "=== regime $r  (n=$(@($keys).Count) rounds: $($keys -join ', '))"
    foreach ($s in $order) {
        $vals = @($keys | ForEach-Object { $rounds[$_][$s] } | Where-Object { $null -ne $_ })
        if (-not $vals) { continue }
        $mn = ($vals | Measure-Object -Minimum).Minimum
        $mx = ($vals | Measure-Object -Maximum).Maximum
        '{0,-26} floor {1,8:F1}   max {2,8:F1}' -f $s, $mn, $mx
    }
    ''
}
