# LilySharp VS Code Extension Deployment Script
# Builds a complete VSIX package and installs it locally.
# The package is identical to what would be published to VS Code Marketplace.
#
# Kills VS Code to release DLL locks, then restarts it after install.

param([switch]$Release)

$ErrorActionPreference = "Stop"
$projectRoot = $PSScriptRoot
$serverDir = Join-Path $projectRoot "editors/vscode/server"

Write-Host "=== LilySharp Extension Deployment ===" -ForegroundColor Cyan

# Step 1: Kill VS Code and LSP to release all file locks
Write-Host "`n[1/6] Stopping VS Code and LSP server..." -ForegroundColor Green
Get-Process -Name "lilysharp-lsp" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process -Name "Code" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# Wait for processes to exit and file locks to be released
$timeout = 10
for ($i = 0; $i -lt $timeout; $i++) {
    $locked = $false
    if (Test-Path (Join-Path $serverDir "lilysharp-lsp.dll")) {
        try {
            [IO.File]::Open((Join-Path $serverDir "lilysharp-lsp.dll"), 'Open', 'Read', 'None').Close()
        } catch {
            $locked = $true
        }
    }
    if (-not $locked) { break }
    Write-Host "  Waiting for file locks to release... ($($i+1)s)" -ForegroundColor DarkYellow
    Start-Sleep -Seconds 1
}

# Clean server directory
if (Test-Path $serverDir) {
    Remove-Item $serverDir -Recurse -Force
    Write-Host "  Cleaned server directory" -ForegroundColor DarkGray
}

# Step 2: Update versions
Write-Host "`n[2/6] Updating versions..." -ForegroundColor Green
$packageJsonPath = Join-Path $projectRoot "editors/vscode/package.json"
$packageJson = Get-Content $packageJsonPath -Raw | ConvertFrom-Json
$currentVersion = $packageJson.version

if ($currentVersion -match '^(\d+)\.(\d+)\.(\d+)-dev\.(\d+)$') {
    $devNum = [int]$Matches[4] + 1
    $newVersion = "$($Matches[1]).$($Matches[2]).$($Matches[3])-dev.$devNum"
} elseif ($currentVersion -match '^(\d+)\.(\d+)\.(\d+)$') {
    $newVersion = "$currentVersion-dev.1"
} else {
    throw "Invalid version format: $currentVersion"
}
$packageJson.version = $newVersion
$packageJson | ConvertTo-Json -Depth 10 | Set-Content $packageJsonPath -Encoding UTF8
Write-Host "Package: $currentVersion -> $newVersion" -ForegroundColor Yellow

# Update LSP version
$lspServerPath = Join-Path $projectRoot "LilySharp.Lsp/LilySharpLanguageServer.cs"
$lspContent = Get-Content $lspServerPath -Raw
$lspVersion = "0.1.1-$(Get-Date -Format 'yyyyMMdd-HHmm')"
$lspContent = $lspContent -replace 'public const string Version = "[^"]+";', "public const string Version = `"$lspVersion`";"
Set-Content $lspServerPath -Value $lspContent -Encoding UTF8
Write-Host "LSP: $lspVersion" -ForegroundColor Yellow

# Step 3: Build VSIX (runs: tsc, dotnet publish, vsce package)
Write-Host "`n[3/6] Building VSIX package..." -ForegroundColor Green
Push-Location (Join-Path $projectRoot "editors/vscode")
npx @vscode/vsce package --allow-missing-repository --pre-release
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "VSIX build failed" }
$vsix = Get-ChildItem *.vsix | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Pop-Location

# Step 4: Install extension
Write-Host "`n[4/6] Installing extension..." -ForegroundColor Green
code --uninstall-extension lilysharp.lilysharp 2>$null
Start-Sleep -Seconds 1
code --install-extension (Join-Path $projectRoot "editors/vscode/$($vsix.Name)")
if ($LASTEXITCODE -ne 0) { throw "Extension install failed" }

# Step 5: Cleanup old VSIX files
Write-Host "`n[5/6] Cleanup..." -ForegroundColor Green
Get-ChildItem (Join-Path $projectRoot "editors/vscode") -Filter "*.vsix" |
    Sort-Object LastWriteTime -Descending | Select-Object -Skip 3 | Remove-Item -Force

# Step 6: Restart VS Code
Write-Host "`n[6/6] Restarting VS Code..." -ForegroundColor Green
Start-Process code

Write-Host "`n=== Deployment Complete ===" -ForegroundColor Cyan
Write-Host "VSIX: $($vsix.Name)" -ForegroundColor Yellow
Write-Host "LSP:  $lspVersion" -ForegroundColor Yellow
