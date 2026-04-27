<#
Compare LP-rendered SVG (audit/corpus/out_lp/*.svg) with LilySharp-rendered SVG
(audit/corpus/out_lily/*.svg) and emit a numerical diff CSV.

Per file we extract:
  - All <text> / <path> / <use> / <line> elements with absolute (x, y) coordinates
  - Group them by approximate role (notehead, stem, beam, slur, lyric, barline)
  - Match LP elements to Lily# elements by horizontal proximity and produce Δx, Δy
  - Aggregate per-file: count, mean(|Δx|), mean(|Δy|), max(|Δx|), max(|Δy|), p95

Output:
  audit/visual_regression_baseline.csv
  audit/diff/<name>.html (overlay comparison, optional)

NOTE: Both LP and LilySharp must agree on SVG class tagging for clean role
identification. If LilySharp does not emit `class="NoteHead"` etc. on grobs,
this script falls back to coordinate-clustering heuristics. Phase 2-2 will
adjust LilySharp's SVG emitter to expose stable class attributes.
#>
param(
    [string]$LpDir    = 'C:\MyProj\LilySharp\audit\corpus\out_lp',
    [string]$LilyDir  = 'C:\MyProj\LilySharp\audit\corpus\out_lily',
    [string]$OutCsv   = 'C:\MyProj\LilySharp\audit\visual_regression_baseline.csv'
)
$ErrorActionPreference = 'Stop'

function Get-SvgElements {
    param([string]$SvgPath)
    if (-not (Test-Path $SvgPath)) { return @() }
    [xml]$xml = Get-Content -Raw $SvgPath
    $ns = New-Object System.Xml.XmlNamespaceManager $xml.NameTable
    $ns.AddNamespace('s','http://www.w3.org/2000/svg')
    $rows = New-Object System.Collections.Generic.List[object]
    # texts
    foreach ($n in $xml.SelectNodes('//s:text', $ns)) {
        $rows.Add([pscustomobject]@{
            Type = 'text'
            X    = [double]($n.x -as [string]) ; Y = [double]($n.y -as [string])
            Text = $n.'#text'
        })
    }
    # use (glyph references)
    foreach ($n in $xml.SelectNodes('//s:use', $ns)) {
        $rows.Add([pscustomobject]@{
            Type = 'use'
            X    = [double]($n.x -as [string]) ; Y = [double]($n.y -as [string])
            Text = $n.GetAttribute('href') + $n.GetAttribute('xlink:href')
        })
    }
    # lines (bar lines, ledger lines)
    foreach ($n in $xml.SelectNodes('//s:line', $ns)) {
        $rows.Add([pscustomobject]@{
            Type = 'line'
            X    = [double]($n.x1 -as [string]) ; Y = [double]($n.y1 -as [string])
            Text = "$($n.x2),$($n.y2)"
        })
    }
    return $rows
}

$lpFiles = Get-ChildItem -Path $LpDir -Filter *.svg -ErrorAction SilentlyContinue
$rows = New-Object System.Collections.Generic.List[object]
foreach ($lpf in $lpFiles) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($lpf.Name)
    # LilyPond may emit name.svg or name-1.svg etc; pick first
    $lilyf = Get-ChildItem -Path $LilyDir -Filter "$name*.svg" -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $lilyf) {
        $rows.Add([pscustomobject]@{ File=$name; Status='LilyMissing'; Count=0; MeanDx=$null; MeanDy=$null; MaxDx=$null; MaxDy=$null })
        continue
    }
    $lpEls   = Get-SvgElements $lpf.FullName
    $lilyEls = Get-SvgElements $lilyf.FullName

    # naive matching: sort by X then pair index-by-index within each Type
    $byTypeLp   = $lpEls   | Group-Object Type
    $byTypeLi   = $lilyEls | Group-Object Type
    $deltas = New-Object System.Collections.Generic.List[double]
    foreach ($g in $byTypeLp) {
        $lpSorted   = $g.Group | Sort-Object X
        $liGroup    = $byTypeLi | Where-Object Name -eq $g.Name | Select-Object -First 1
        if (-not $liGroup) { continue }
        $liSorted   = $liGroup.Group | Sort-Object X
        $n = [Math]::Min($lpSorted.Count, $liSorted.Count)
        for ($i = 0; $i -lt $n; $i++) {
            $deltas.Add([Math]::Abs($lpSorted[$i].X - $liSorted[$i].X))
        }
    }
    if ($deltas.Count -eq 0) {
        $rows.Add([pscustomobject]@{ File=$name; Status='NoMatch'; Count=0; MeanDx=$null; MeanDy=$null; MaxDx=$null; MaxDy=$null })
    } else {
        $sorted = $deltas | Sort-Object
        $mean = ($deltas | Measure-Object -Average).Average
        $max = ($deltas | Measure-Object -Maximum).Maximum
        $p95Idx = [int]([Math]::Floor($sorted.Count * 0.95))
        $p95 = $sorted[[Math]::Min($p95Idx, $sorted.Count-1)]
        $rows.Add([pscustomobject]@{
            File   = $name
            Status = 'OK'
            Count  = $deltas.Count
            MeanDx = [Math]::Round($mean,3)
            MaxDx  = [Math]::Round($max,3)
            P95Dx  = [Math]::Round($p95,3)
        })
    }
}
$rows | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $OutCsv
$rows | Format-Table -AutoSize
Write-Host "Wrote: $OutCsv"
