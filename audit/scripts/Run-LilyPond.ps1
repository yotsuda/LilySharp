<#
Render every .ly under audit/corpus through LilyPond 2.24.4 binary,
producing one SVG per file at audit/corpus/out_lp/<name>.svg.

This script must be RUN BY THE USER (not Claude) because the Claude sandbox
blocks lilypond.exe execution. Once outputs exist Compare-Svg.ps1 can run.
#>
param(
    [string]$LpExe   = 'C:\bin\lilypond-2.24.4\bin\lilypond.exe',
    [string]$Corpus  = 'C:\MyProj\LilySharp\audit\corpus',
    [string]$OutDir  = 'C:\MyProj\LilySharp\audit\corpus\out_lp'
)
$ErrorActionPreference = 'Continue'
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }
$files = Get-ChildItem -Path $Corpus -Filter *.ly
Write-Host "Rendering $($files.Count) .ly files via $LpExe"
foreach ($f in $files) {
    $base = [System.IO.Path]::GetFileNameWithoutExtension($f.Name)
    $outBase = Join-Path $OutDir $base
    Write-Host "  $($f.Name) -> $outBase.svg"
    & $LpExe -dbackend=svg --output=$outBase -s $f.FullName 2>&1 | Out-Null
}
Write-Host "Done. SVGs in $OutDir"
