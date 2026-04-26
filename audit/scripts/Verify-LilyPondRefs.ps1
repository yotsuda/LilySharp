# Verify-LilyPondRefs.ps1
# Audits LILYPOND-REF citations in the LilySharp source tree against the local
# LilyPond clone. Flags citations whose target file is missing or whose cited
# line range is out of bounds.
#
# Usage:
#   ./Verify-LilyPondRefs.ps1                    # text summary
#   ./Verify-LilyPondRefs.ps1 -Json              # machine-readable
#   ./Verify-LilyPondRefs.ps1 -ShowOk            # also list verified refs
#   ./Verify-LilyPondRefs.ps1 -LpRoot <path>     # override default path
#
# Categories:
#   OK            target file exists; line range (if any) within bounds
#   MISSING       target file does not exist under LpRoot
#   LINE-OOR      file exists but cited line/range exceeds file length
#   FREE-FORM     no parseable file path (e.g. just a function name)
#
# Exit codes:
#   0   all citations OK
#   1   one or more MISSING / LINE-OOR
#   2   script error

[CmdletBinding()]
param(
    [string]$LpRoot = 'C:\MyProj\lilypond-src',
    [string]$SourceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\LilySharp.Core')).Path,
    [switch]$Json,
    [switch]$ShowOk
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $LpRoot)) {
    Write-Error "LilyPond source clone not found at $LpRoot. Pass -LpRoot to override."
    exit 2
}
if (-not (Test-Path $SourceRoot)) {
    Write-Error "LilySharp source root not found at $SourceRoot."
    exit 2
}

# Match LILYPOND-REF citations. The remainder up to a sentence separator (em
# dash, period, semicolon, end-of-line) is the citation body; we then parse
# the leading "<path>[:<lines>]" out of it.
$refRegex = [regex]::new(
    '(?<![A-Za-z])LILYPOND-REF:\s*(?<body>[^\r\n]+)',
    'Compiled')
# Path looks like: <dir>/<file>.<ext> where dir starts with lily|scm|mf|ly|input
# and ext is one of .cc .hh .scm .mf .ly. Followed optionally by ":<linespec>".
$pathRegex = [regex]::new(
    '^(?<path>(?:lily|scm|mf|ly|input|flower|stepmake)/[^\s,:]+\.(?:cc|hh|scm|mf|ly))(?::(?<lines>[0-9\-,]+))?',
    'Compiled')

function Get-LineCount([string]$file) {
    [System.IO.File]::ReadAllLines($file).Length
}

function Parse-LineSpec([string]$lineSpec) {
    # Returns (start, end) tuple from "N", "N-M", "N,M,K" (returns highest), or $null
    if ([string]::IsNullOrEmpty($lineSpec)) { return @($null, $null) }
    $maxLine = 0
    $minLine = [int]::MaxValue
    foreach ($part in $lineSpec -split ',') {
        if ($part -match '^(\d+)-(\d+)$') {
            $a = [int]$Matches[1]; $b = [int]$Matches[2]
            if ($a -lt $minLine) { $minLine = $a }
            if ($b -gt $maxLine) { $maxLine = $b }
        } elseif ($part -match '^(\d+)$') {
            $n = [int]$part
            if ($n -lt $minLine) { $minLine = $n }
            if ($n -gt $maxLine) { $maxLine = $n }
        } else {
            return @($null, $null)  # unparseable line spec — treat as no-line
        }
    }
    if ($minLine -eq [int]::MaxValue) { return @($null, $null) }
    return @($minLine, $maxLine)
}

$results = New-Object System.Collections.Generic.List[object]
$lineCountCache = @{}

$csFiles = Get-ChildItem $SourceRoot -Recurse -Filter '*.cs' -File
foreach ($cs in $csFiles) {
    $content = [System.IO.File]::ReadAllText($cs.FullName)
    $matches = $refRegex.Matches($content)
    foreach ($m in $matches) {
        $body = $m.Groups['body'].Value.Trim()
        # Compute the .cs line number this citation appears at
        $lineIdx = ($content.Substring(0, $m.Index) -split "`n").Length
        $pathMatch = $pathRegex.Match($body)
        $entry = [PSCustomObject]@{
            CsFile     = (Resolve-Path $cs.FullName -Relative).TrimStart('.\\')
            CsLine     = $lineIdx
            Body       = $body
            LpPath     = $null
            LineSpec   = $null
            Status     = 'FREE-FORM'
            Detail     = ''
        }
        if ($pathMatch.Success) {
            $lpPath = $pathMatch.Groups['path'].Value
            $lineSpec = $pathMatch.Groups['lines'].Value
            $entry.LpPath = $lpPath
            $entry.LineSpec = $lineSpec
            $abs = Join-Path $LpRoot ($lpPath -replace '/', '\')
            if (-not (Test-Path $abs)) {
                $entry.Status = 'MISSING'
                $entry.Detail = "file not found: $abs"
            } else {
                $minMax = Parse-LineSpec $lineSpec
                if ($null -ne $minMax[0]) {
                    if (-not $lineCountCache.ContainsKey($abs)) {
                        $lineCountCache[$abs] = Get-LineCount $abs
                    }
                    $fileLen = $lineCountCache[$abs]
                    if ($minMax[1] -gt $fileLen) {
                        $entry.Status = 'LINE-OOR'
                        $entry.Detail = "cited line $($minMax[1]) > file length $fileLen"
                    } else {
                        $entry.Status = 'OK'
                        $entry.Detail = "lines $($minMax[0])-$($minMax[1]) within $fileLen"
                    }
                } else {
                    $entry.Status = 'OK'
                    $entry.Detail = "file exists, no line spec to verify"
                }
            }
        }
        $results.Add($entry)
    }
}

# Summarize
$grouped = $results | Group-Object Status | Sort-Object Name
$summary = [PSCustomObject]@{
    LpRoot       = $LpRoot
    SourceRoot   = $SourceRoot
    TotalRefs    = $results.Count
    Counts       = @{}
    Issues       = @($results | Where-Object { $_.Status -in 'MISSING', 'LINE-OOR' })
}
foreach ($g in $grouped) { $summary.Counts[$g.Name] = $g.Count }

if ($Json) {
    $output = $summary
    if ($ShowOk) { $output | Add-Member NoteProperty AllRefs $results }
    $output | ConvertTo-Json -Depth 6
} else {
    Write-Host "=== LilyPond Reference Audit ===" -ForegroundColor Cyan
    Write-Host "  LpRoot:     $LpRoot"
    Write-Host "  SourceRoot: $SourceRoot"
    Write-Host "  Total refs: $($summary.TotalRefs)"
    foreach ($g in $grouped) {
        $color = switch ($g.Name) {
            'OK'        { 'Green' }
            'FREE-FORM' { 'DarkGray' }
            default     { 'Red' }
        }
        Write-Host ("  {0,-12} {1,4}" -f $g.Name, $g.Count) -ForegroundColor $color
    }
    if ($summary.Issues.Count -gt 0) {
        Write-Host ""
        Write-Host "Issues:" -ForegroundColor Red
        foreach ($i in $summary.Issues) {
            Write-Host ("  [{0}] {1}:{2}" -f $i.Status, $i.CsFile, $i.CsLine) -ForegroundColor Red
            Write-Host ("      {0}" -f $i.Body) -ForegroundColor DarkGray
            Write-Host ("      → {0}" -f $i.Detail) -ForegroundColor DarkYellow
        }
    }
    if ($ShowOk) {
        Write-Host ""
        Write-Host "Verified:" -ForegroundColor Green
        foreach ($r in $results | Where-Object Status -eq 'OK') {
            Write-Host ("  {0}:{1} → {2}" -f $r.CsFile, $r.CsLine, $r.Body) -ForegroundColor DarkGray
        }
    }
}

if ($summary.Issues.Count -gt 0) { exit 1 } else { exit 0 }
