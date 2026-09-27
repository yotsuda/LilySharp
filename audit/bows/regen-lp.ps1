param([string[]]$Books,
      [string]$LilyPond = 'C:\bin\lilypond-2.26.0\bin\lilypond.exe',
      [string]$Lysc)
# Re-takes the LilyPond side of the bow twin net (LilySharp.Tests/LpFidelity/BowTwinTests.cs):
# for every book in LilySharp.Tests/Fixtures/bows (or -Books), exports the Lily# twin with
# `lysc ly`, runs it through LilyPond with bowdump.ily included after \version, and writes the
# BOWBOOK / BOWSTAFF / HEAD / BOW lines next to the book as <stem>.lp.txt.
# Run it after a book is added or edited, or after a change to the twin exporter; then run
# BowTwinTests (LILYSHARP_UPDATE_BOWS=1 re-takes the residual table, see its remarks).
# ⚠️ LilyPond is started detached from the console's stdin (`< NUL`): it probes the console
# at start-up and otherwise waits on the prompt's pending read forever (RULES §5.5).
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not $Lysc) { $Lysc = Join-Path $repo 'LilySharp.Cli\bin\Debug\net10.0\lysc.exe' }
$dir = Join-Path $repo 'LilySharp.Tests\Fixtures\bows'
if (-not $Books) { $Books = @(Get-ChildItem $dir -Filter *.lys | ForEach-Object FullName) }
$inc = (Join-Path $PSScriptRoot 'bowdump.ily') -replace '\\', '/'
$tmp = Join-Path ([IO.Path]::GetTempPath()) ('lilysharp-bows-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $tmp | Out-Null
try {
    foreach ($book in $Books) {
        $stem = [IO.Path]::GetFileNameWithoutExtension($book)
        $work = Join-Path $tmp $stem
        New-Item -ItemType Directory -Force $work | Out-Null
        & $Lysc ly -d $work $book *> (Join-Path $work 'lysc.log')
        $ly = Join-Path $work "$stem.ly"
        if (-not (Test-Path -LiteralPath $ly)) { Write-Warning "no twin for $book"; continue }
        $src = [IO.File]::ReadAllText($ly)
        $m = [regex]::Match($src, '\\version\s+"[^"]*"')
        $patched = if ($m.Success) { $src.Insert($m.Index + $m.Length, "`n\include `"$inc`"`n") } else { "\include `"$inc`"`n" + $src }
        $dump = Join-Path $work "$stem.bowdump.ly"
        [IO.File]::WriteAllText($dump, $patched, [Text.UTF8Encoding]::new($false))
        Push-Location $work
        cmd /d /s /c "`"$LilyPond`" -dno-print-pages -o `"$stem.bowdump`" `"$dump`" < NUL > `"$dump.log`" 2>&1"
        Pop-Location
        $lines = @([IO.File]::ReadAllLines("$dump.log") | Where-Object { $_ -match '^(BOWBOOK|BOWSTAFF|BOW|HEAD)\b' })
        if (-not ($lines -contains 'BOWBOOK')) { Write-Warning "no BOWBOOK for $book (see $dump.log)"; continue }
        [IO.File]::WriteAllLines((Join-Path (Split-Path $book) "$stem.lp.txt"), [string[]]$lines)
        "$stem  $($lines.Count) lines"
    }
}
finally { Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue }
