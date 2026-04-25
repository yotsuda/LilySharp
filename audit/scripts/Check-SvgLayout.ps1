# Check-SvgLayout.ps1
# Runs structural invariant checks on a Lily# SVG output.
# Detects layout bugs (collisions, overflows, malformed elements) without
# needing a reference rendering.
#
# Usage:
#   ./Check-SvgLayout.ps1 path/to/score.svg
#   ./Check-SvgLayout.ps1 path/to/score.svg -Json
#
# Exit codes:
#   0 = no violations
#   1 = at least one violation found
#   2 = script error (file not found, parse error)
#
# Coordinate system: SVG viewBox uses staff spaces (1 unit = 1 sp).
# Staff height = 4 sp. Tolerances expressed in sp directly.

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$SvgPath,
    [switch]$Json,
    [double]$NoteheadCollisionTol = 0.6,   # sp; horizontal distance below this = collision
    [double]$BarlinePiercingTol  = 0.4,    # sp; notehead within this of barline x = bug
    [double]$ViewBoxMargin       = 0.5     # sp; element this far outside viewBox = overflow
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $SvgPath)) {
    Write-Error "SVG file not found: $SvgPath"
    exit 2
}

[xml]$xml = Get-Content $SvgPath -Raw

# Extract viewBox
$svgRoot = $xml.svg
$viewBox = $svgRoot.viewBox -split '\s+' | ForEach-Object { [double]$_ }
if ($viewBox.Count -ne 4) {
    Write-Error "Cannot parse viewBox from SVG"
    exit 2
}
$vb = @{ MinX = $viewBox[0]; MinY = $viewBox[1]; Width = $viewBox[2]; Height = $viewBox[3] }
$vb.MaxX = $vb.MinX + $vb.Width
$vb.MaxY = $vb.MinY + $vb.Height

# Helper: get all elements as flat list with parsed coords
function Get-Element($node, $kind) {
    $r = @{ Kind = $kind; Class = $node.class; Element = $node.LocalName }
    switch ($node.LocalName) {
        'line' {
            $r.X1 = [double]$node.x1; $r.Y1 = [double]$node.y1
            $r.X2 = [double]$node.x2; $r.Y2 = [double]$node.y2
            $r.MinX = [Math]::Min($r.X1, $r.X2); $r.MaxX = [Math]::Max($r.X1, $r.X2)
            $r.MinY = [Math]::Min($r.Y1, $r.Y2); $r.MaxY = [Math]::Max($r.Y1, $r.Y2)
        }
        'rect' {
            $r.X = [double]$node.x; $r.Y = [double]$node.y
            $r.W = [double]$node.width; $r.H = [double]$node.height
            $r.MinX = $r.X; $r.MaxX = $r.X + $r.W
            $r.MinY = $r.Y; $r.MaxY = $r.Y + $r.H
        }
        'text' {
            $r.X = [double]$node.x; $r.Y = [double]$node.y
            $r.MinX = $r.X; $r.MaxX = $r.X
            $r.MinY = $r.Y; $r.MaxY = $r.Y
            $r.Text = $node.InnerText
            $anchor = if ($node.HasAttribute('text-anchor')) { $node.GetAttribute('text-anchor') } else { 'start' }
            if ($node.HasAttribute('font-size')) {
                $r.FontSize = [double]$node.GetAttribute('font-size')
                $r.MinY = $r.Y - $r.FontSize
                # Approximate width = font-size * len * 0.6 (proportional)
                $width = $r.FontSize * [Math]::Max(1, $r.Text.Length) * 0.6
                switch ($anchor) {
                    'middle' { $r.MinX = $r.X - $width / 2; $r.MaxX = $r.X + $width / 2 }
                    'end'    { $r.MinX = $r.X - $width;     $r.MaxX = $r.X }
                    default  { $r.MinX = $r.X;              $r.MaxX = $r.X + $width }
                }
            }
        }
        'path' {
            # Minimal bbox extraction — parse M/C commands. Also extract first/last
            # MoveTo as the spanner start/end (used for R7 malformed-bezier detection).
            $coords = [regex]::Matches($node.d, '-?\d+(?:\.\d+)?') | ForEach-Object { [double]$_.Value }
            if ($coords.Count -ge 2) {
                $xs = @(); $ys = @()
                for ($i = 0; $i -lt $coords.Count - 1; $i += 2) {
                    $xs += $coords[$i]; $ys += $coords[$i + 1]
                }
                $r.MinX = ($xs | Measure-Object -Minimum).Minimum
                $r.MaxX = ($xs | Measure-Object -Maximum).Maximum
                $r.MinY = ($ys | Measure-Object -Minimum).Minimum
                $r.MaxY = ($ys | Measure-Object -Maximum).Maximum
                # First M = start, last M (or end of last C) = end
                $r.PathStartX = $coords[0]; $r.PathStartY = $coords[1]
                # For typical tie/slur path "M sx,sy C c1x,c1y c2x,c2y ex,ey ..."
                # the end is at index 6,7 (after first C). Capture if available.
                if ($coords.Count -ge 8) {
                    $r.PathEndX = $coords[6]; $r.PathEndY = $coords[7]
                }
            }
        }
    }
    return $r
}

$all = @()
foreach ($node in $xml.SelectNodes('//*')) {
    if ($node.LocalName -in 'line', 'rect', 'text', 'path') {
        $kind = if ($node.HasAttribute('class')) { $node.GetAttribute('class') } else { '_unclassified' }
        $all += Get-Element $node $kind
    }
}

# Subset helpers
# SMuFL notehead range: U+E0A0..U+E0FF (E0A0-E0BF primary, E0C0-E0FF specialized)
# Anything outside that range with class="music" is a clef/timesig/dynamic/accidental/etc.
function Test-NoteheadCodepoint($txt) {
    if ([string]::IsNullOrEmpty($txt)) { return $false }
    $cp = [int][char]$txt[0]
    return ($cp -ge 0xE0A0 -and $cp -le 0xE0FF)
}
$noteheads = $all | Where-Object {
    $_.Kind -eq 'music' -and $_.Element -eq 'text' -and (Test-NoteheadCodepoint $_.Text)
}
$barlines  = $all | Where-Object { $_.Kind -eq 'barline' }
$staves    = $all | Where-Object { $_.Kind -eq 'staff' }
$ledgers   = $all | Where-Object { $_.Kind -eq 'ledger' }
$sectionLabels = $all | Where-Object { $_.Kind -in 'section-label-box', 'section-label-text' }

$issues = New-Object System.Collections.Generic.List[object]
function Add-Issue($severity, $rule, $description, $location) {
    $issues.Add([PSCustomObject]@{
        Severity    = $severity
        Rule        = $rule
        Description = $description
        Location    = $location
    })
}

# R1: viewbox_overflow
foreach ($e in $all) {
    if ($null -eq $e.MinX) { continue }
    $skip = $e.Kind -in 'title', 'tempo'  # these may extend outside intentionally
    if ($skip) { continue }
    if ($e.MinX -lt $vb.MinX - $ViewBoxMargin -or
        $e.MaxX -gt $vb.MaxX + $ViewBoxMargin -or
        $e.MinY -lt $vb.MinY - $ViewBoxMargin -or
        $e.MaxY -gt $vb.MaxY + $ViewBoxMargin) {
        Add-Issue 'error' 'R1_viewbox_overflow' `
            ("Element ({0} class={1}) extends outside viewBox" -f $e.Element, $e.Kind) `
            ("bbox=({0:F1},{1:F1})-({2:F1},{3:F1}) viewBox=({4:F1},{5:F1})-({6:F1},{7:F1})" -f
                $e.MinX, $e.MinY, $e.MaxX, $e.MaxY, $vb.MinX, $vb.MinY, $vb.MaxX, $vb.MaxY)
    }
}

# R2: notehead_collision (same y line, same x within tolerance)
$sortedNotes = $noteheads | Sort-Object { $_.Y }, { $_.X }
for ($i = 0; $i -lt $sortedNotes.Count; $i++) {
    for ($j = $i + 1; $j -lt $sortedNotes.Count; $j++) {
        $a = $sortedNotes[$i]; $b = $sortedNotes[$j]
        if ([Math]::Abs($a.Y - $b.Y) -gt 0.3) { break }  # different staff line; sorted, can break
        $dx = [Math]::Abs($a.X - $b.X)
        if ($dx -lt $NoteheadCollisionTol -and $dx -gt 0) {
            Add-Issue 'warning' 'R2_notehead_collision' `
                ("Two noteheads at same staff position within {0:F2}sp" -f $dx) `
                ("a=({0:F1},{1:F1}) b=({2:F1},{3:F1})" -f $a.X, $a.Y, $b.X, $b.Y)
        }
    }
}

# R3: notehead_pierced_by_barline (notehead x very close to barline x AND y within barline span)
foreach ($n in $noteheads) {
    foreach ($bar in $barlines) {
        $dx = [Math]::Abs($n.X - $bar.X1)
        if ($dx -lt $BarlinePiercingTol -and $n.Y -ge $bar.MinY -and $n.Y -le $bar.MaxY) {
            Add-Issue 'error' 'R3_notehead_on_barline' `
                ("Notehead overlaps barline (dx={0:F2}sp)" -f $dx) `
                ("note=({0:F1},{1:F1}) barline x={2:F1}" -f $n.X, $n.Y, $bar.X1)
        }
    }
}

# R4: staff_overlap (two staff line groups have overlapping y ranges)
# Cluster staff lines by y-proximity (lines on the same staff are within 4sp total)
$staffYs = $staves | ForEach-Object { $_.Y1 } | Sort-Object -Unique
$staffGroups = New-Object System.Collections.Generic.List[object]
$current = New-Object System.Collections.Generic.List[double]
foreach ($y in $staffYs) {
    if ($current.Count -eq 0 -or $y - $current[-1] -lt 1.5) {
        $current.Add($y)
    } else {
        $staffGroups.Add([PSCustomObject]@{ MinY = $current[0]; MaxY = $current[-1] })
        $current = New-Object System.Collections.Generic.List[double]
        $current.Add($y)
    }
}
if ($current.Count -gt 0) {
    $staffGroups.Add([PSCustomObject]@{ MinY = $current[0]; MaxY = $current[-1] })
}
for ($i = 0; $i -lt $staffGroups.Count - 1; $i++) {
    $a = $staffGroups[$i]; $b = $staffGroups[$i + 1]
    if ($a.MaxY -gt $b.MinY) {
        Add-Issue 'error' 'R4_staff_overlap' `
            "Two staff groups overlap vertically" `
            ("group A=({0:F1}..{1:F1}) group B=({2:F1}..{3:F1})" -f $a.MinY, $a.MaxY, $b.MinY, $b.MaxY)
    }
}

# R5: section_label_collision (section-label-box overlaps notehead)
$boxes = $sectionLabels | Where-Object { $_.Element -eq 'rect' }
foreach ($box in $boxes) {
    foreach ($n in $noteheads) {
        if ($n.X -ge $box.MinX -and $n.X -le $box.MaxX -and
            $n.Y -ge $box.MinY -and $n.Y -le $box.MaxY) {
            Add-Issue 'warning' 'R5_section_label_collision' `
                "Section label box overlaps a notehead" `
                ("box=({0:F1},{1:F1})-({2:F1},{3:F1}) note=({4:F1},{5:F1})" -f
                    $box.MinX, $box.MinY, $box.MaxX, $box.MaxY, $n.X, $n.Y)
        }
    }
}

# R7: malformed_bezier_path
# A tie/slur path's bbox should not be much wider than its endpoint span.
# When control points are placed wildly (e.g., outside the page), the bbox blows up.
# Excludes wavy-line spanners (trill, vibrato) where the path uses chained Q commands
# whose effective extent is larger than any single segment — they have a single short
# "tip" plus a long wavy continuation, which is intentional, not a bug.
$wavyLineClasses = @('trill-spanner-line', 'vibrato-line', 'glissando-wavy')
foreach ($p in $all | Where-Object { $_.Element -eq 'path' -and $null -ne $_.PathEndX -and $_.Kind -notin $wavyLineClasses }) {
    $span = [Math]::Abs($p.PathEndX - $p.PathStartX)
    $bboxW = $p.MaxX - $p.MinX
    # Require span >= 0.5sp so we don't flag tiny ties; bbox/span ratio < 3 is reasonable.
    if ($span -ge 0.5 -and $bboxW -gt $span * 3 -and $bboxW -ge 5) {
        Add-Issue 'error' 'R7_malformed_bezier' `
            ("Path bbox ({0:F1}sp wide) far exceeds endpoint span ({1:F1}sp); control points likely runaway" -f $bboxW, $span) `
            ("start=({0:F1},{1:F1}) end=({2:F1},{3:F1}) bbox=({4:F1},{5:F1})-({6:F1},{7:F1})" -f
                $p.PathStartX, $p.PathStartY, $p.PathEndX, $p.PathEndY,
                $p.MinX, $p.MinY, $p.MaxX, $p.MaxY)
    }
}

# R6: ledger_no_notehead_in_column
# A ledger line should have at least one notehead in its x-column (within ±0.5sp x).
# (Vertical position not checked: ledger lines extend the staff for high/low notes,
# so the notehead can be at, above, or below the ledger y depending on the chain.)
foreach ($l in $ledgers) {
    $hasNoteInColumn = $false
    foreach ($n in $noteheads) {
        if ($n.X -ge ($l.X1 - 0.5) -and $n.X -le ($l.X2 + 0.5)) {
            $hasNoteInColumn = $true; break
        }
    }
    if (-not $hasNoteInColumn) {
        Add-Issue 'warning' 'R6_orphan_ledger_line' `
            "Ledger line with no notehead in its x column" `
            ("ledger=({0:F1},{1:F1})-({2:F1},{3:F1})" -f $l.X1, $l.Y1, $l.X2, $l.Y2)
    }
}

# R8: uneven_same_glyph_spacing
# A run of >= 3 consecutive same-glyph noteheads on the same staff (clustered by y)
# should have roughly uniform x-gaps. Same-glyph implies same-or-similar duration class
# (e.g., all noteheadBlack), so spacing should follow time evenly.
# Flag if max(gap)/min(gap) > 1.5 within a run (excluding gaps > 6sp which suggest a
# barline / measure boundary).
$staffSpan = 1.5    # sp; vertical clustering tolerance for "same staff line group"
$runMaxGap = 6.0    # sp; max gap between consecutive notes in the same run
$ratioMaxThreshold = 1.5

# Group noteheads by glyph (Text codepoint), then build runs.
$noteByGlyph = @{}
foreach ($n in $noteheads) {
    if (-not $noteByGlyph.ContainsKey($n.Text)) { $noteByGlyph[$n.Text] = @() }
    $noteByGlyph[$n.Text] += $n
}
function Test-RunSpacing($run) {
    if ($run.Count -lt 3) { return }
    $gaps = @()
    for ($k = 1; $k -lt $run.Count; $k++) {
        $gaps += [Math]::Round($run[$k].X - $run[$k - 1].X, 2)
    }
    $minG = ($gaps | Measure-Object -Minimum).Minimum
    $maxG = ($gaps | Measure-Object -Maximum).Maximum
    if ($minG -le 0 -or ($maxG / $minG) -le $script:ratioMaxThreshold) { return }

    # Filter to reduce false positives from genuine multi-voice spacing:
    # In a 4-quarter bass run, if the melody has intermediate columns (e.g. eighth notes)
    # between some bass notes, those bass gaps legitimately widen — common pattern is
    # "smallest gap at start" (melody long-note before bass-1 → no intermediate column)
    # or "smallest gap in middle". Only "smallest gap at the LAST position" is a strong
    # signal of a real spring-solver bias (compressed-at-end).
    $minIdx = 0
    for ($k = 1; $k -lt $gaps.Count; $k++) { if ($gaps[$k] -lt $gaps[$minIdx]) { $minIdx = $k } }
    if ($minIdx -ne ($gaps.Count - 1)) { return }

    $coords = (($run | ForEach-Object { "({0:F1},{1:F1})" -f $_.X, $_.Y }) -join ' ')
    Add-Issue 'warning' 'R8_uneven_spacing' `
        ("Run of {0} same-glyph noteheads compressed at end: gaps {1} (ratio {2:F2})" -f
            $run.Count, ($gaps -join '/'), ($maxG / $minG)) `
        ("notes: $coords")
}

$script:ratioMaxThreshold = $ratioMaxThreshold
# For each glyph, first cluster notes by Y (same staff line band), then sort each
# cluster by X to detect runs. This avoids interleaving across staves.
foreach ($key in $noteByGlyph.Keys) {
    $notes = @($noteByGlyph[$key])
    if ($notes.Count -lt 3) { continue }
    # Y-cluster (greedy): sort by Y, group adjacent within $staffSpan
    $byY = $notes | Sort-Object { $_.Y }
    $yClusters = @()
    $currentCluster = @($byY[0])
    for ($i = 1; $i -lt $byY.Count; $i++) {
        if (($byY[$i].Y - $currentCluster[-1].Y) -le $staffSpan) {
            $currentCluster += $byY[$i]
        } else {
            $yClusters += , $currentCluster
            $currentCluster = @($byY[$i])
        }
    }
    $yClusters += , $currentCluster
    # Within each Y-cluster, sort by X and split into runs
    foreach ($cluster in $yClusters) {
        if ($cluster.Count -lt 3) { continue }
        $byX = $cluster | Sort-Object { $_.X }
        $current = @($byX[0])
        for ($j = 1; $j -lt $byX.Count; $j++) {
            $gap = $byX[$j].X - $current[-1].X
            if ($gap -le $runMaxGap) {
                $current += $byX[$j]
            } else {
                Test-RunSpacing $current
                $current = @($byX[$j])
            }
        }
        Test-RunSpacing $current
    }
}

# Output
$summary = [PSCustomObject]@{
    SvgPath  = $SvgPath
    Elements = $all.Count
    Errors   = ($issues | Where-Object Severity -eq 'error').Count
    Warnings = ($issues | Where-Object Severity -eq 'warning').Count
    Info     = ($issues | Where-Object Severity -eq 'info').Count
    Issues   = $issues
}

if ($Json) {
    $summary | ConvertTo-Json -Depth 6
} else {
    Write-Host "=== SVG Layout Check: $SvgPath ===" -ForegroundColor Cyan
    Write-Host ("Elements: {0}  Errors: {1}  Warnings: {2}  Info: {3}" -f
        $summary.Elements, $summary.Errors, $summary.Warnings, $summary.Info)
    if ($issues.Count -gt 0) {
        Write-Host ""
        $issues | Sort-Object @{e = { switch ($_.Severity) { 'error' { 0 } 'warning' { 1 } default { 2 } } } } |
            ForEach-Object {
                $color = switch ($_.Severity) { 'error' { 'Red' } 'warning' { 'Yellow' } default { 'DarkGray' } }
                Write-Host ("[{0}] {1}: {2}" -f $_.Severity.ToUpper(), $_.Rule, $_.Description) -ForegroundColor $color
                Write-Host ("    {0}" -f $_.Location) -ForegroundColor DarkGray
            }
    }
}

if ($summary.Errors -gt 0 -or $summary.Warnings -gt 0) { exit 1 } else { exit 0 }
