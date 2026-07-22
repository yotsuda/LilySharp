<#
.SYNOPSIS
    Runs an LP fidelity probe through real LilyPond and prints the geometry it produced.

.DESCRIPTION
    The numbers in lp-geometry.json are DATA measured from LilyPond, not values Lily#
    computed. This script is how they are (re)produced, so a reviewer can rerun a probe
    instead of taking a committed number on trust.

    It prints every dumped grob sorted by X, per score, with the anchor-to-anchor gaps the
    ledger records. Paste the relevant number into lp-geometry.json under "lilypond".

.PARAMETER Probe
    Probe file under probes/. Defaults to barline-spacing.ly.

.PARAMETER LilyPond
    Path to lilypond.exe.

.NOTES
    Guile deadlocks when LilyPond is launched with an inherited console, so the run is
    detached via `cmd /c "... < NUL"`. LilyPond exits with code 1 even on a clean run here
    (there is no output file with -dbackend=null); the dump on stdout is still complete.

    stdout and stderr are kept on SEPARATE files. Merged, LilyPond's diagnostics land in
    the middle of a dump line and the parser drops it — see the comment at the run itself.

.EXAMPLE
    pwsh audit/lp-geometry/Measure-LilyPondGeometry.ps1
#>
[CmdletBinding()]
param(
    [string] $Probe = 'barline-spacing.ly',
    [string] $LilyPond = 'C:\bin\lilypond-2.26.0\bin\lilypond.exe'
)

$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$probePath = Join-Path $here 'probes' $Probe
if (-not (Test-Path $probePath)) { throw "probe not found: $probePath" }
if (-not (Test-Path $LilyPond)) { throw "lilypond.exe not found: $LilyPond — pass -LilyPond" }

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("lp-geometry-" + [System.IO.Path]::GetRandomFileName())
New-Item -ItemType Directory -Path $work | Out-Null
try {
    $out = Join-Path $work 'out.txt'
    $err = Join-Path $work 'err.txt'
    # stderr goes to its OWN file. Merging it into stdout (`2>&1`) splices LilyPond's
    # diagnostics into the middle of a dump line — stderr is unbuffered and stdout is not.
    # Under -dbackend=null LilyPond always reports "Unbound variable: output-stencils", and
    # that once landed inside score MC's third note head, cutting `PROBE MC HEAD
    # x=17.102165 ext=(...)` in two. The parser dropped both halves and the probe still
    # looked complete, so `midmeasure.clef.clef-to-next-note` was being read to the FOURTH
    # head.
    cmd /c "`"$LilyPond`" -dbackend=null -o `"$work\o`" `"$probePath`" > `"$out`" 2> `"$err`" < NUL" | Out-Null

    $lines = @(Get-Content $out | Where-Object { $_ -match '^PROBE ' })

    # A PROBE line that does not parse is an ERROR, never a line to skip: a dump with a
    # hole in it silently re-indexes every "next glyph" quantity onto the wrong grob.
    $bad = @($lines | Where-Object { $_ -notmatch '^PROBE (\S+) (\S+) x=(\S+) ext=\((\S+) \. (\S+)\)$' })
    if ($bad) {
        throw ("LilyPond printed {0} unparsable PROBE line(s) — the dump is INCOMPLETE, " -f $bad.Count) +
              "so any number taken from it would be measuring the wrong grob:`n  " +
              ($bad -join "`n  ") + "`nLilyPond's stderr was:`n  " +
              ((Get-Content $err -ErrorAction SilentlyContinue) -join "`n  ")
    }

    # An EMPTY extent is LilyPond's `(+inf.0 . -inf.0)`, not a number: a grob with no ink,
    # e.g. the KeySignature left behind when a change to C major engraves its naturals as a
    # KeyCancellation instead. It has no position to measure, so it is dropped — but LOUDLY,
    # because "no ink" and "not dumped" must stay distinguishable.
    $empty = @()
    $rows = $lines | ForEach-Object {
        $null = $_ -match '^PROBE (\S+) (\S+) x=(\S+) ext=\((\S+) \. (\S+)\)$'
        if ($Matches[4] -eq '+inf.0') {
            $script:empty += "{0} {1}" -f $Matches[1], $Matches[2]
            return
        }
        [pscustomobject]@{
            Score = $Matches[1]
            Kind  = $Matches[2]
            Anchor = [double]$Matches[3]
            InkL  = [double]$Matches[3] + [double]$Matches[4]
            InkR  = [double]$Matches[3] + [double]$Matches[5]
        }
    }
    if ($empty) {
        Write-Host ("skipped {0} grob(s) with an empty extent: {1}" -f $empty.Count, ($empty -join ', ')) -ForegroundColor DarkYellow
    }

    if (-not $rows) { throw "no PROBE lines in $out — check the probe and the LilyPond version" }

    foreach ($score in ($rows.Score | Select-Object -Unique)) {
        $s = @($rows | Where-Object Score -eq $score | Sort-Object Anchor)
        Write-Host ""
        Write-Host "=== score $score ===" -ForegroundColor Cyan
        foreach ($r in $s) {
            "  {0,-5} anchor={1,12:F6}  ink=[{2,12:F6},{3,12:F6}]" -f $r.Kind, $r.Anchor, $r.InkL, $r.InkR
        }

        # The mid-line bar line is the first BAR that has music on both sides.
        $bars = @($s | Where-Object Kind -eq 'BAR')
        if ($bars.Count -ge 1) {
            $bar = $bars[0]
            $after = @($s | Where-Object { $_.Kind -ne 'BAR' -and $_.Anchor -gt $bar.InkR })
            $before = @($s | Where-Object { $_.Kind -ne 'BAR' -and $_.Anchor -lt $bar.InkL })
            Write-Host "  -- ledger quantities (anchor-to-anchor) --" -ForegroundColor DarkGray
            if ($after.Count -ge 1) {
                "     barline.next  (bar ink right -> next anchor)   = {0:F6}  [{1}]" -f ($after[0].Anchor - $bar.InkR), $after[0].Kind
            }
            for ($i = 1; $i -lt [Math]::Min($after.Count, 5); $i++) {
                "     barline.next  (+{0} glyphs)                     = {1:F6}  [{2}]" -f $i, ($after[$i].Anchor - $bar.InkR), $after[$i].Kind
            }
            if ($before.Count -ge 1) {
                $last = $before[-1]
                "     barline.prev  (last anchor -> bar ink left)     = {0:F6}  [{1}]" -f ($bar.InkL - $last.Anchor), $last.Kind
            }
        }

        # A MID-MEASURE change item sits between two note heads with no bar line involved,
        # so the bar-line block above never reports it. Print both of its gaps: the change's
        # own frame shows up as the two trading against each other, and a single gap hides it.
        $heads = @($s | Where-Object Kind -in 'HEAD', 'REST')
        foreach ($ch in @($s | Where-Object Kind -in 'CLEF', 'KEY', 'TIME')) {
            $prev = @($heads | Where-Object { $_.Anchor -lt $ch.Anchor })
            $next = @($heads | Where-Object { $_.Anchor -gt $ch.Anchor })
            if ($prev.Count -lt 1 -or $next.Count -lt 1) { continue }   # prefatory, not mid-measure
            # A change at a measure BOUNDARY is break-aligned into the boundary column and
            # priced by a different model (the barline.next.* block above). Test for a bar
            # line anywhere between the two heads, not just left of the change: a clef
            # change at a bar line is engraved BEFORE that bar line, so it has no bar to its
            # left yet is still break-aligned (probe scores B and D).
            if ($s | Where-Object { $_.Kind -eq 'BAR' -and $_.Anchor -gt $prev[-1].Anchor -and $_.Anchor -lt $next[0].Anchor }) { continue }
            "     midmeasure    (prev head -> {0,-4} anchor)          = {1:F6}" -f $ch.Kind, ($ch.Anchor - $prev[-1].Anchor)
            "     midmeasure    ({0,-4} anchor -> next head)          = {1:F6}" -f $ch.Kind, ($next[0].Anchor - $ch.Anchor)
        }
    }
    Write-Host ""
    Write-Host "Paste the relevant figures into audit/lp-geometry/lp-geometry.json ('lilypond')." -ForegroundColor Yellow
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
