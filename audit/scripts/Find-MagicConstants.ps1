<#
Find numeric literals in layout files that lack a nearby LILYPOND-REF comment.

A "literal" is anything matching /\b\d+\.\d+\b/ (decimals) or  /\b\d{2,}\b/ (integers >= 10).
We exclude common non-constants:
  - Indices/loops (e.g. `[0]`, `i++`)
  - Sizes that are obviously buffer counts (StringBuilder(256))
  - Test/debug only

For each literal location we look ±5 lines for an existing LILYPOND-REF comment.

Output: audit/magic_constants.csv
  File, Line, Text, NearbyRef, Decision (Green/Yellow/Red)

Decision is heuristic; manual classification refines later.
#>
param(
    [string]$LilySharpRoot = 'C:\MyProj\LilySharp\LilySharp.Core',
    [string]$OutCsv        = 'C:\MyProj\LilySharp\audit\magic_constants.csv'
)

$targets = @(
    'Svg/Layout/SpacingRules.cs',
    'Svg/Layout/LyricEngraver.cs',
    'Svg/Layout/MultiStaffLayouter.cs',
    'Svg/Layout/PageBreaker.cs',
    'Svg/Layout/PageLayouter.cs',
    'Svg/Layout/SkylineBuilder.cs',
    'Svg/Layout/Skyline.cs',
    'Svg/Layout/HorizontalSkyline.cs',
    'Svg/Layout/VerticalSkyline.cs',
    'Svg/Layout/AccidentalPlacement.cs',
    'Svg/Layout/NoteCollision.cs',
    'Svg/Layout/BeamScoringProblem.cs',
    'Svg/Layout/BeamConfiguration.cs',
    'Svg/Layout/BeamQuantParameters.cs',
    'Svg/Layout/SlurScoringProblem.cs',
    'Svg/Layout/SlurScoreParameters.cs',
    'Svg/Layout/TieFormattingProblem.cs',
    'Svg/Layout/TieDetails.cs',
    'Svg/Layout/BreakAlignSpacing.cs',
    'Svg/Layout/ElementCoordinator.cs',
    'Svg/Layout/OutsideStaffStacker.cs',
    'Svg/Layout/HaraKiri.cs',
    'Svg/Layout/MeasureLayouter.cs',
    'Svg/Layout/LayoutEngine.cs',
    'Svg/Layout/ScoreLayout.cs',
    'Svg/Layout/SpringSolver.cs',
    'Svg/Layout/Spring.cs',
    'Svg/Layout/StaffSpacingParameters.cs',
    'Svg/Layout/NoteSpacingParameters.cs',
    'Svg/Layout/VerticalSpacingParameters.cs',
    'Svg/Layout/GraceSpacingParameters.cs',
    'Svg/Layout/KnuthPlassBreaker.cs',
    'Svg/Layout/ArticulationEngraver.cs',
    'Svg/Layout/HairpinEngraver.cs',
    'Svg/Layout/DynamicEngraver.cs',
    'Svg/Layout/TextSpannerEngraver.cs',
    'Svg/Layout/TupletBracketEngraver.cs',
    'Svg/Layout/OttavaBracketEngraver.cs',
    'Svg/Layout/PedalEngraver.cs',
    'Svg/Layout/GraceNoteEngraver.cs',
    'Svg/EngravingDefaults.cs',
    'Svg/PaperSettings.cs',
    'Svg/Layout/GlyphMetrics.cs',
    'Svg/EmmentalerGlyphs.cs'
)

# ⚠️ Four entries left this list on 2026-08-28: Svg/Layout/SystemLayouter.cs,
# Svg/Layout/OrnamentEngraver.cs, Svg/EngravingRules.cs and Svg/SpacingSettings.cs.
# All four were DELETED as dead code between 2026-06-23 and 2026-07-19 (66c6f6b3,
# cea7ae9d, 2734964f, de61ac23) — not renamed — so the audit lost no coverage and the
# script had been printing four warnings on every run. A name that disappears from
# here should be checked the same way: `git log --diff-filter=D` on the path.

$rows = New-Object System.Collections.Generic.List[object]
$context = 5

foreach ($rel in $targets) {
    $path = Join-Path $LilySharpRoot $rel
    if (-not (Test-Path $path)) {
        Write-Warning "Missing: $rel"
        continue
    }
    $lines = [System.IO.File]::ReadAllLines($path)
    # collect all LILYPOND-REF locations in file
    $allRefLines = @()
    for ($k = 0; $k -lt $lines.Count; $k++) {
        if ($lines[$k] -match 'LILYPOND-REF[^\r\n]*') { $allRefLines += @{Line=$k; Text=$matches[0]} }
    }
    $hasFileLevelRef = $allRefLines.Count -gt 0 -and $allRefLines[0].Line -lt 60
    $fileLevelRef = if ($hasFileLevelRef) { $allRefLines[0].Text.Trim() } else { '' }
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        # skip empty / pure comment lines
        if ($line -match '^\s*$') { continue }
        # candidate literal: decimal (3.6) or large int (>=10)
        $litMatches = [regex]::Matches($line, '(?<![\w.])(\d+\.\d+|\d{2,})(?![\w.])')
        if ($litMatches.Count -eq 0) { continue }
        # skip if the line is itself a comment about reference
        if ($line -match 'LILYPOND-REF') { continue }
        # skip pure using/namespace/array index syntax
        if ($line -match '^\s*//') { continue }   # line-comment (we already excluded LILYPOND-REF lines)
        # check if any nearby line has LILYPOND-REF
        $lo = [Math]::Max(0, $i - $context)
        $hi = [Math]::Min($lines.Count - 1, $i + $context)
        $nearbyRef = ''
        for ($j = $lo; $j -le $hi; $j++) {
            if ($lines[$j] -match 'LILYPOND-REF[^\r\n]*') {
                $nearbyRef = $matches[0].Trim()
                break
            }
        }
        # decision
        $decision = 'Red'
        if ($nearbyRef) { $decision = 'Green' }
        elseif ($hasFileLevelRef) { $decision = 'Yellow'; $nearbyRef = "(file-level) $fileLevelRef" }
        # heuristic upgrade: strings like 'approximation', 'simplified', 'rough' → Yellow
        $contextBlob = ($lines[$lo..$hi] -join ' ')
        if ($decision -eq 'Red' -and $contextBlob -match 'approximat|rough|simplif|heuristic|estimate|fallback|placeholder|stub|TODO|FIXME|HACK|NOT YET') {
            $decision = 'Yellow'
        }
        # heuristic: bracket/brace allocations or ToString format / IDs → not interesting
        if ($line -match 'StringBuilder\s*\(|new\s+\w+\s*\[\s*\d+\s*\]\s*;|0x[0-9a-fA-F]+|ToString\s*\(|Format\s*\(') {
            continue
        }
        # the literal text
        $litTexts = ($litMatches | ForEach-Object { $_.Value }) -join ', '
        $rows.Add([pscustomobject]@{
            File      = $rel
            Line      = $i + 1
            Literals  = $litTexts
            Text      = $line.Trim()
            NearbyRef = $nearbyRef
            Decision  = $decision
        })
    }
}

$rows | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $OutCsv
Write-Host "Wrote $($rows.Count) literal-bearing lines to $OutCsv"
$grouped = $rows | Group-Object Decision | Sort-Object Count -Descending
foreach ($g in $grouped) {
    Write-Host ("  {0,-7} {1,5}" -f $g.Name, $g.Count)
}
