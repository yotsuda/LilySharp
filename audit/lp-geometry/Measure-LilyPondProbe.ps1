<#
.SYNOPSIS
    Runs ONE probe file through real LilyPond and prints the PROBE lines it emitted, raw.

.DESCRIPTION
    The cheap runner. Measure-LilyPondGeometry.ps1 and Measure-LilyPondPageGeometry.ps1 each
    know the shape of one dump and do arithmetic on it; this one knows nothing and prints what
    LilyPond said. That is the point: a probe written to answer ONE question can format its own
    output, and asking it should not cost what re-measuring the whole corpus costs.

    WHY IT EXISTS (HANDOFF 5.0, 2026-07-28). page-vertical.ly is 62 books and takes over twenty
    minutes to run — past the MCP tool timeout, so it needs detaching and polling — while a
    dedicated four-book probe runs in about eighty seconds, i.e. inside a single call. The
    ledger holds `probe` per ENTRY and is already spread over six files, and \book blocks are
    independent, so a new pair belongs in a probe of its own: measured there it stays cheap
    forever, and measured in page-vertical.ly it makes everyone who touches it afterwards pay
    the twenty minutes. This script is what makes the cheap side a first-class thing to do
    rather than a command typed by hand into a console.

.PARAMETER Probe
    Probe file under probes/ (or a full path).

.PARAMETER Prefix
    Line prefix to extract. Defaults to PROBE, which covers PROBE, PROBEV and PROBEB alike.

.PARAMETER LilyPond
    Path to lilypond.exe.

.PARAMETER KeepWork
    Keep the temporary output directory (and say where it is) instead of deleting it.

.NOTES
    Guile deadlocks when LilyPond is launched with an inherited console, so the run is detached
    via `cmd /c "... < NUL"`, and stdout/stderr are kept on SEPARATE files — merged, an
    unbuffered diagnostic lands in the middle of a dump line. Both hazards are documented at
    length in Measure-LilyPondGeometry.ps1; they apply here unchanged.

    -dbackend=svg rather than null, for the same reason the page script uses it: Y-offsets are
    filled in by Page::page_stencil, which only runs when the pages are actually realized
    (lily/paper-book.cc:775-788). A probe that only reads grobs does not need it and is not
    slowed down measurably by it.

.EXAMPLE
    pwsh audit/lp-geometry/Measure-LilyPondProbe.ps1 -Probe skyline-binding.ly
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Probe,
    [string] $Prefix = 'PROBE',
    [string] $LilyPond = 'C:\bin\lilypond-2.26.0\bin\lilypond.exe',
    [switch] $KeepWork
)

$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$probePath = if (Test-Path $Probe) { (Resolve-Path $Probe).Path } else { Join-Path $here 'probes' $Probe }
if (-not (Test-Path $probePath)) { throw "probe not found: $probePath" }
if (-not (Test-Path $LilyPond)) { throw "lilypond.exe not found: $LilyPond — pass -LilyPond" }

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("lp-probe-" + [System.IO.Path]::GetRandomFileName())
New-Item -ItemType Directory -Path $work | Out-Null
try {
    $out = Join-Path $work 'out.txt'
    $err = Join-Path $work 'err.txt'
    cmd /c "`"$LilyPond`" -dbackend=svg -o `"$work\o`" `"$probePath`" > `"$out`" 2> `"$err`" < NUL" | Out-Null

    $lines = @(Get-Content $out -ErrorAction SilentlyContinue | Where-Object { $_ -match "^$Prefix" })
    if (-not $lines) {
        # No lines is ALWAYS an error, never an empty answer: a probe that failed to compile
        # and a probe that measured nothing look identical from here, and the second one has
        # never happened.
        throw "no $Prefix lines in $out — LilyPond's stderr was:`n  " +
              ((Get-Content $err -ErrorAction SilentlyContinue) -join "`n  ")
    }
    $lines
    Write-Host ""
    Write-Host ("{0} {1} line(s). Paste the figures into audit/lp-geometry/lp-geometry.json ('lilypond')." -f $lines.Count, $Prefix) -ForegroundColor Yellow
}
finally {
    if ($KeepWork) { Write-Host "work kept at $work" -ForegroundColor DarkGray }
    else { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }
}
