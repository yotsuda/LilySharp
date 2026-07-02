<#
.SYNOPSIS
Selectively promote reviewed "actual" outputs from artifacts/visual-diff/ to
the SVG snapshot baselines (LilySharp.Tests/Snapshots/).

.DESCRIPTION
The visual regression flow is:
  1. dotnet test --filter "FullyQualifiedName~SvgSnapshotTests"
  2. Open artifacts/visual-diff/report.html and judge each change.
  3. Approve the good ones:   pwsh tools/Approve-Snapshots.ps1 -Name test/notes
     (wildcards work:         -Name 'test/multi-staff-*')
     Approve everything:      pwsh tools/Approve-Snapshots.ps1
  4. Re-run the tests — approved fixtures go green; unapproved ones keep
     failing until the code (or the baseline) is fixed.

This is the surgical alternative to LILYSHARP_UPDATE_SNAPSHOTS=1, which
re-blesses ALL changed snapshots at once.

.PARAMETER Name
Fixture name(s) as shown in the report / test output, e.g. "test/notes" or
"showcase/03-piano". Wildcards allowed. Default: every changed fixture.
#>
param(
    [string[]]$Name = @('*')
)

$repo = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $repo 'artifacts/visual-diff'
$snapshots = Join-Path $repo 'LilySharp.Tests/Snapshots'

if (-not (Test-Path $artifacts)) {
    Write-Error "No visual-diff artifacts at $artifacts — run the snapshot tests first: dotnet test --filter `"FullyQualifiedName~SvgSnapshotTests`""
    exit 1
}

$approved = 0
foreach ($pattern in $Name) {
    # "test/notes" (report name) -> "test__notes.actual.svg" (artifact name)
    $filePattern = ($pattern -replace '[/\\]', '__') + '.actual.svg'
    foreach ($f in Get-ChildItem $artifacts -Filter $filePattern) {
        $target = Join-Path $snapshots ($f.Name -replace '\.actual\.svg$', '.svg')
        Copy-Item $f.FullName $target -Force
        Write-Host "approved: $($f.BaseName -replace '\.actual$','') -> $target"
        $approved++
    }
}

if ($approved -eq 0) {
    $changed = (Get-ChildItem $artifacts -Filter '*.actual.svg' |
        ForEach-Object { ($_.BaseName -replace '\.actual$','') -replace '__','/' }) -join ', '
    Write-Warning "Nothing matched '$($Name -join ', ')'. Changed fixtures: $changed"
    exit 1
}

Write-Host "$approved snapshot(s) updated. Verify: dotnet test --filter `"FullyQualifiedName~SvgSnapshotTests`""
