<#
Extract every LILYPOND-REF citation from LilySharp.Core/**/*.cs
and validate that the referenced file/line range exists in lilypond-src.

Output: audit/citation_drift.csv with columns:
  CsFile, CsLine, RawRef, RefFile, RefLineLo, RefLineHi, Status, Detail

Status values:
  OK            - file exists, line range within file
  RangeOOB      - file exists but line range exceeds file LOC
  FileMissing   - referenced .cc/.scm/.mf not found
  Unparsed      - couldn't parse line range from raw ref
#>
param(
    [string]$LilySharpRoot = 'C:\MyProj\LilySharp\LilySharp.Core',
    [string]$LilypondRoot  = 'C:\MyProj\lilypond-src',
    [string]$OutCsv        = 'C:\MyProj\LilySharp\audit\citation_drift.csv'
)

$ErrorActionPreference = 'Stop'

$pattern = [regex]'LILYPOND-REF\s*[:\-]?\s*((?:lily|scm|mf|flower)/[A-Za-z0-9_\-./]+\.(?:cc|hh|scm|mf|cpp))(?::([\d, +\-]+))?'

$files = Get-ChildItem -Path $LilySharpRoot -Recurse -Filter *.cs -File `
    | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }

$lpFileLineCache = @{}
function Get-LpFileLines {
    param([string]$RelPath)
    if ($lpFileLineCache.ContainsKey($RelPath)) { return $lpFileLineCache[$RelPath] }
    $abs = Join-Path $LilypondRoot $RelPath
    if (-not (Test-Path $abs)) {
        $lpFileLineCache[$RelPath] = -1
        return -1
    }
    $count = (Get-Content $abs -Raw).Split("`n").Count
    $lpFileLineCache[$RelPath] = $count
    return $count
}

$rows = New-Object System.Collections.Generic.List[object]

foreach ($f in $files) {
    $lineNo = 0
    foreach ($line in [System.IO.File]::ReadLines($f.FullName)) {
        $lineNo++
        $matches = $pattern.Matches($line)
        foreach ($m in $matches) {
            $rel = $m.Groups[1].Value -replace '\\', '/'
            $rangeRaw = $m.Groups[2].Value.Trim()
            $lo = $null; $hi = $null
            if ($rangeRaw) {
                # Handle: 123  or  123-456  or  123,456  or "123, 456-789"
                $cleaned = $rangeRaw -replace '\s', ''
                $first = ($cleaned -split '[,\-]')[0]
                if ([int]::TryParse($first, [ref]$lo)) {
                    $lastM = [regex]::Matches($cleaned, '\d+')
                    if ($lastM.Count -gt 1) {
                        [void][int]::TryParse($lastM[$lastM.Count - 1].Value, [ref]$hi)
                    } else {
                        $hi = $lo
                    }
                }
            }

            $totalLines = Get-LpFileLines -RelPath $rel
            $status = $null; $detail = $null
            if ($totalLines -lt 0) {
                $status = 'FileMissing'
                $detail = "$rel not found under $LilypondRoot"
            } elseif (-not $rangeRaw -or $null -eq $lo) {
                $status = 'NoRange'
                $detail = "rangeRaw='$rangeRaw'"
            } elseif ($lo -gt $totalLines -or ($hi -and $hi -gt $totalLines)) {
                $status = 'RangeOOB'
                $detail = "lo=$lo hi=$hi totalLines=$totalLines"
            } else {
                $status = 'OK'
                $detail = "lo=$lo hi=$hi totalLines=$totalLines"
            }

            $csRel = ($f.FullName.Substring($LilySharpRoot.Length + 1)).Replace('\','/')
            $rows.Add([pscustomobject]@{
                CsFile    = $csRel
                CsLine    = $lineNo
                RawRef    = $m.Value
                RefFile   = $rel
                RefLineLo = $lo
                RefLineHi = $hi
                Status    = $status
                Detail    = $detail
            })
        }
    }
}

$rows | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $OutCsv

Write-Host "Wrote $($rows.Count) citations to $OutCsv"
$grouped = $rows | Group-Object Status | Sort-Object Count -Descending
foreach ($g in $grouped) {
    Write-Host ("  {0,-12} {1,6}" -f $g.Name, $g.Count)
}
