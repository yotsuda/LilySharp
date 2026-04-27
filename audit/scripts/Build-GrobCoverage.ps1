<#
Build a Grob × Property coverage matrix between LP and LilySharp.

Parses:
  - C:\MyProj\lilypond-src\scm\define-grobs.scm           (grob definitions)
  - C:\MyProj\lilypond-src\scm\define-grob-properties.scm (property metadata)

For each LP grob name and property name, count how many times it appears in
LilySharp.Core/**/*.cs (string-literal match, case-insensitive).

Output:
  audit/grob_coverage.csv
  audit/property_coverage.csv
#>
param(
    [string]$LpScmRoot       = 'C:\MyProj\lilypond-src\scm',
    [string]$LilySharpRoot   = 'C:\MyProj\LilySharp\LilySharp.Core',
    [string]$GrobOut         = 'C:\MyProj\LilySharp\audit\grob_coverage.csv',
    [string]$PropOut         = 'C:\MyProj\LilySharp\audit\property_coverage.csv'
)

$ErrorActionPreference = 'Stop'

# --- Parse grob list -------------------------------------------------------
$grobsScm = Join-Path $LpScmRoot 'define-grobs.scm'
$grobText = Get-Content -Raw $grobsScm
# pattern: line of form "    (GrobName" beginning at column 4-5 immediately after "  (".
# In define-grobs.scm grob entries look like:
#    (Accidental
#     . (
#        ...
$grobs = New-Object System.Collections.Generic.List[string]
foreach ($line in $grobText -split "`n") {
    if ($line -match '^\s{0,8}\(([A-Z][A-Za-z0-9_]+)\s*$') {
        $name = $matches[1]
        # skip "Tweak" headers and other non-grob nodes; grob list does not contain primitives
        if ($name -notmatch '^(Tweak|Layer|System|All|Element|Tweaks?)$') {
            $grobs.Add($name)
        }
    }
}
$grobs = $grobs | Sort-Object -Unique

Write-Host "Found $($grobs.Count) grob names in define-grobs.scm"

# --- Parse property list ---------------------------------------------------
$propScm = Join-Path $LpScmRoot 'define-grob-properties.scm'
$propText = Get-Content -Raw $propScm
# entries look like: (grob-property-description 'property-name predicate-name? doc-string)
# Use simple regex.
$props = New-Object System.Collections.Generic.List[string]
# Format: (property-name ,type? "description")
# Inside the all-user-grob-properties alist body.
$pat = [regex]"\(([a-zA-Z][a-zA-Z0-9-]+)\s+,[a-zA-Z][a-zA-Z0-9?:_-]*[\s\?]"
foreach ($m in $pat.Matches($propText)) {
    $props.Add($m.Groups[1].Value)
}
$props = $props | Sort-Object -Unique

Write-Host "Found $($props.Count) properties in define-grob-properties.scm"

# --- Index Lily# files -----------------------------------------------------
$csFiles = Get-ChildItem -Path $LilySharpRoot -Recurse -Filter *.cs -File `
    | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
Write-Host "Indexing $($csFiles.Count) C# files..."

$csCorpus = New-Object System.Text.StringBuilder
foreach ($f in $csFiles) {
    [void]$csCorpus.AppendLine([System.IO.File]::ReadAllText($f.FullName))
}
$blob = $csCorpus.ToString()

# --- Match grobs -----------------------------------------------------------
$grobRows = New-Object System.Collections.Generic.List[object]
foreach ($g in $grobs) {
    # match either exact PascalCase or whole-word case-insensitive
    $pat = [regex]"\b$([regex]::Escape($g))\b"
    $count = $pat.Matches($blob).Count
    $grobRows.Add([pscustomobject]@{
        Grob        = $g
        MatchCount  = $count
        Status      = if ($count -eq 0) { 'Absent' } elseif ($count -lt 3) { 'Mention' } else { 'Used' }
    })
}

# --- Match properties ------------------------------------------------------
$propRows = New-Object System.Collections.Generic.List[object]
foreach ($p in $props) {
    # Property names are kebab-case in LP (e.g. "outside-staff-priority")
    # In C# they may appear as: PascalCase, camelCase, or kebab-case in strings/comments
    $kebab = $p
    $pascal = ((($p -split '-') | ForEach-Object {
        if ($_) { $_.Substring(0,1).ToUpper() + $_.Substring(1) }
    }) -join '')
    $patterns = @(
        [regex]"\b$([regex]::Escape($kebab))\b",
        [regex]"\b$([regex]::Escape($pascal))\b"
    )
    $totalCount = 0
    foreach ($pat in $patterns) { $totalCount += $pat.Matches($blob).Count }
    $propRows.Add([pscustomobject]@{
        Property    = $p
        PascalCase  = $pascal
        MatchCount  = $totalCount
        Status      = if ($totalCount -eq 0) { 'Absent' } elseif ($totalCount -lt 3) { 'Mention' } else { 'Used' }
    })
}

$grobRows | Sort-Object Grob | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $GrobOut
$propRows | Sort-Object Property | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $PropOut

Write-Host ""
Write-Host "Grob coverage:"
$grobRows | Group-Object Status | Sort-Object Count -Descending | ForEach-Object { '  {0,-7} {1,4}' -f $_.Name, $_.Count }
Write-Host ""
Write-Host "Property coverage:"
$propRows | Group-Object Status | Sort-Object Count -Descending | ForEach-Object { '  {0,-7} {1,4}' -f $_.Name, $_.Count }
Write-Host ""
Write-Host "Wrote: $GrobOut"
Write-Host "Wrote: $PropOut"
