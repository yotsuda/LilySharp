# Generate-PredefinedFretboards.ps1
#
# Regenerates LilySharp.Core/Music/PredefinedFretboardsGenerated.cs — LilyPond's predefined
# fret diagrams (guitar, guitar ninths, ukulele, mandolin) as C# data.
#
# 1. Runs LilyPond on dump-predefined-fretboards.ly (beside this script), which EVALUATES the
#    three predefined files — many entries are Scheme (`chord-shape`, `offset-fret`), so the
#    .ly text is never parsed — and writes predefined-fretboards.txt: one line per entry of
#    default-fret-table, with the file:line that stored it.
# 2. Turns each entry's verbose placement list into frets (low string first, -1 muted),
#    fingers (0 none) and barres, and writes the C# file.
#
# Usage:   ./Generate-PredefinedFretboards.ps1 [-LilyPond <path to lilypond.exe>]
# Needs:   LilyPond 2.26.0 (the version the data was taken from; the header says so).
#
# The chord QUALITY is not decided here: the data keeps LilyPond's pitches (root spelling and
# semitones above it), and Music/PredefinedFretboards.cs matches them against Lily#'s
# ChordQualityRegistry at run time — so a quality registered later finds its shapes, and the
# entries no quality matches are listed by a test, not lost in a script.

[CmdletBinding()]
param(
    [string]$LilyPond = 'C:\bin\lilypond-2.26.0\bin\lilypond.exe'
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repo = (Resolve-Path (Join-Path $here '..\..')).Path
$out = Join-Path $repo 'LilySharp.Core\Music\PredefinedFretboardsGenerated.cs'

Push-Location $here
try {
    Remove-Item predefined-fretboards.txt -ErrorAction SilentlyContinue
    # LilyPond probes the console at start-up; stdin from NUL keeps it from waiting on it.
    cmd /d /s /c "`"$LilyPond`" -s dump-predefined-fretboards.ly < NUL > dump.log 2>&1"
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path predefined-fretboards.txt)) {
        Get-Content dump.log | Write-Host
        throw "LilyPond failed (exit $LASTEXITCODE)"
    }
    $lines = @(Get-Content predefined-fretboards.txt | Where-Object { $_ })
} finally {
    Pop-Location
}

$names = 'c', 'd', 'e', 'f', 'g', 'a', 'b'
$stepSemitone = 0, 2, 4, 5, 7, 9, 11
function Get-Semitone([string]$p) {
    $a = $p -split ','
    12 * [int]$a[2] + $stepSemitone[[int]$a[0]] + [int]$a[1]
}
function Get-LilyName([string]$p) {
    $a = $p -split ','
    $n = $names[[int]$a[0]]
    switch ([int]$a[1]) { -2 { $n += 'eses' } -1 { $n += 'es' } 1 { $n += 'is' } 2 { $n += 'isis' } }
    # LilyPond's own contractions of the default (Dutch) names.
    $n -replace '^ees', 'es' -replace '^aes', 'as'
}

# The tunings the tables are keyed by, string 1 first in semitones from c' → the Lily# name.
$tuningNames = @{
    '4 -1 -5 -10 -15 -20' = 'Guitar'
    '9 4 0 7'             = 'Ukulele'
    '16 9 2 -5'           = 'Mandolin'
}
$tableOrder = @{ Guitar = 0; Ukulele = 1; Mandolin = 2 }

$entries = foreach ($line in $lines) {
    $parts = $line -split ' \| ', 4
    $table = $tuningNames[$parts[0]]
    if (-not $table) { throw "unknown tuning '$($parts[0])'" }
    $n = ($parts[0] -split ' ').Count
    $pitches = $parts[1] -split ' '
    $root = Get-Semitone $pitches[0]
    $intervals = ($pitches | ForEach-Object { (Get-Semitone $_) - $root }) -join ' '
    $rootParts = $pitches[0] -split ','
    $file, $lineNo = $parts[2] -split ':'

    # The verbose placement list: strings are numbered 1 = the highest; Lily# lists frets from
    # the LOW string, so string s is index n - s.
    $frets = @($null) * $n
    $fingers = @(0) * $n
    $barres = @()
    foreach ($m in [regex]::Matches($parts[3], '\(([a-z-]+)((?: -?\d+)*)\)')) {
        $kind = $m.Groups[1].Value
        $args_ = @($m.Groups[2].Value.Trim() -split ' ' | Where-Object { $_ } | ForEach-Object { [int]$_ })
        switch ($kind) {
            'place-fret' {
                $frets[$n - $args_[0]] = $args_[1]
                if ($args_.Count -ge 3) { $fingers[$n - $args_[0]] = $args_[2] }
            }
            'open' { $frets[$n - $args_[0]] = 0 }
            'mute' { $frets[$n - $args_[0]] = -1 }
            'barre' { $barres += , $args_ }
            default { throw "unknown placement '$kind' in: $line" }
        }
    }
    # A string no placement names but a barre crosses sounds at the barre's fret.
    foreach ($b in $barres) {
        for ($s = [Math]::Min($b[0], $b[1]); $s -le [Math]::Max($b[0], $b[1]); $s++) {
            if ($null -eq $frets[$n - $s]) { $frets[$n - $s] = $b[2] }
        }
    }
    # A string no placement names at all LilyPond leaves bare (no dot, no o, no x). Lily#'s
    # frets have no "bare" value, so it is written muted, and listed. Measured on 2.26.0: two
    # entries, the mandolin's cis:aug and des:aug (predefined-mandolin-fretboards.ly:319/369),
    # whose terse string "6-4;3-1;o5-3;" is three items for four strings — a slip in
    # LilyPond's data that places gis c e, not an augmented C sharp.
    for ($i = 0; $i -lt $n; $i++) {
        if ($null -eq $frets[$i]) {
            Write-Warning "string $($n - $i) has no placement (written muted): $line"
            $frets[$i] = -1
        }
    }

    [pscustomobject]@{
        Table     = $table
        Order     = $tableOrder[$table]
        File      = $file
        Line      = [int]$lineNo
        RootName  = Get-LilyName $pitches[0]
        RootStep  = [int]$rootParts[0]
        RootAlter = [int]$rootParts[1]
        Intervals = $intervals
        Frets     = ($frets | ForEach-Object { if ($_ -lt 0) { 'x' } else { "$_" } }) -join ' '
        Fingers   = ($fingers | ForEach-Object { if ($_ -eq 0) { '-' } else { "$_" } }) -join ' '
        Barres    = ($barres | ForEach-Object { "$($_[0])-$($_[1])@$($_[2])" }) -join ' '
    }
}

$sorted = $entries | Sort-Object Order, File, Line
$counts = $sorted | Group-Object Table | ForEach-Object { "$($_.Name) $($_.Count)" }

$sb = [System.Text.StringBuilder]::new()
$licence = @'
// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

// <auto-generated>
// Generated by audit/fretboards/Generate-PredefinedFretboards.ps1, which runs
// audit/fretboards/dump-predefined-fretboards.ly on LilyPond 2.26.0 — LilyPond EVALUATES its
// predefined tables (chord-shape / offset-fret barre shapes included) and the script writes
// what default-fret-table holds. Do not edit by hand; re-run the script.
// </auto-generated>

namespace LilySharp.Core.Music;

public static partial class PredefinedFretboards
{
    /// <summary>
    /// Every entry of LilyPond's predefined fret-diagram tables, in file order: the table, the
    /// file line that stored it, the chord's root as LilyPond spells it (step 0 = C, alteration
    /// in semitones), the chord's semitones above the root, the frets LOW string first
    /// ("x" muted), the fingers LOW string first ("-" none) and the barres as
    /// FROM-TO@FRET in LilyPond's string numbers (1 = the highest).
    /// </summary>
    /// <remarks>
'@
[void]$sb.Append($licence.Replace("`r`n", "`n"))
[void]$sb.Append("`n")
[void]$sb.Append("    /// LILYPOND-REF: ly/predefined-guitar-fretboards.ly (and ly/predefined-guitar-ninth-fretboards.ly, which it includes)`n")
[void]$sb.Append("    /// LILYPOND-REF: ly/predefined-ukulele-fretboards.ly`n")
[void]$sb.Append("    /// LILYPOND-REF: ly/predefined-mandolin-fretboards.ly`n")
[void]$sb.Append("    /// Counts: $($counts -join ', ') (guitar = 136 + 17 ninths).`n")
[void]$sb.Append("    /// </remarks>`n")
[void]$sb.Append("    internal static readonly Entry[] Entries =`n    [`n")
$lastFile = $null
foreach ($e in $sorted) {
    if ($e.File -ne $lastFile) {
        [void]$sb.Append("        // ly/$($e.File)`n")
        $lastFile = $e.File
    }
    [void]$sb.Append("        new(Table.$($e.Table), $($e.Line), `"$($e.RootName)`", $($e.RootStep), $($e.RootAlter), `"$($e.Intervals)`", `"$($e.Frets)`", `"$($e.Fingers)`", `"$($e.Barres)`"),`n")
}
[void]$sb.Append("    ];`n}`n")

# UTF-8 without a BOM and LF line ends, as every source file of the repository.
[System.IO.File]::WriteAllText($out, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))
Write-Host "Wrote $out — $($counts -join ', ')"
