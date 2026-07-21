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

.EXAMPLE
    pwsh audit/lp-geometry/Measure-LilyPondGeometry.ps1
#>
[CmdletBinding()]
param(
    [string] $Probe = 'barline-spacing.ly',
    [string] $LilyPond = 'C:\bin\lilypond-2.24.4\bin\lilypond.exe'
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
    cmd /c "`"$LilyPond`" -dbackend=null -o `"$work\o`" `"$probePath`" > `"$out`" 2>&1 < NUL" | Out-Null

    $rows = Get-Content $out |
        Where-Object { $_ -match '^PROBE (\S+) (\S+) x=(\S+) ext=\((\S+) \. (\S+)\)' } |
        ForEach-Object {
            if ($_ -match '^PROBE (\S+) (\S+) x=(\S+) ext=\((\S+) \. (\S+)\)') {
                [pscustomobject]@{
                    Score = $Matches[1]
                    Kind  = $Matches[2]
                    Anchor = [double]$Matches[3]
                    InkL  = [double]$Matches[3] + [double]$Matches[4]
                    InkR  = [double]$Matches[3] + [double]$Matches[5]
                }
            }
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
    }
    Write-Host ""
    Write-Host "Paste the relevant figures into audit/lp-geometry/lp-geometry.json ('lilypond')." -ForegroundColor Yellow
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
