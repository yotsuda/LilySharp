# LilySharp VS Code Extension Deployment Script
# Usage: .\deploy-extension.ps1 [-Release]
#
# Development: 0.1.1-dev.1 → 0.1.1-dev.2 → ...
# Release:     0.1.1 → 0.1.2 → ...

param(
    [switch]$Release
)

$ErrorActionPreference = "Stop"
$config = if ($Release) { "Release" } else { "Debug" }

Write-Host "=== LilySharp Extension Deployment ===" -ForegroundColor Cyan
Write-Host "Configuration: $config" -ForegroundColor Yellow

# Step 0: Kill LSP process to release file locks
Write-Host "`n[0/7] Stopping LSP server..." -ForegroundColor Green
$lspProcesses = Get-Process -Name "lilysharp-lsp" -ErrorAction SilentlyContinue
if ($lspProcesses) {
    $lspProcesses | Stop-Process -Force
    Start-Sleep -Seconds 1
    Write-Host "LSP server stopped" -ForegroundColor Yellow
} else {
    Write-Host "LSP server not running" -ForegroundColor Gray
}

# Step 1: Update version
Write-Host "`n[1/7] Updating version..." -ForegroundColor Green
$packageJsonPath = "editors/vscode/package.json"
$packageJson = Get-Content $packageJsonPath -Raw | ConvertFrom-Json
$currentVersion = $packageJson.version

if ($Release) {
    # Release: strip prerelease tag
    if ($currentVersion -match '^(\d+)\.(\d+)\.(\d+)(-dev\.\d+)?$') {
        $newVersion = "$($Matches[1]).$($Matches[2]).$($Matches[3])"
    } else {
        throw "Invalid version format: $currentVersion"
    }
} else {
    # Development: increment dev number
    if ($currentVersion -match '^(\d+)\.(\d+)\.(\d+)-dev\.(\d+)$') {
        $devNum = [int]$Matches[4] + 1
        $newVersion = "$($Matches[1]).$($Matches[2]).$($Matches[3])-dev.$devNum"
    } elseif ($currentVersion -match '^(\d+)\.(\d+)\.(\d+)$') {
        $newVersion = "$currentVersion-dev.1"
    } else {
        throw "Invalid version format: $currentVersion"
    }
}

$packageJson.version = $newVersion
$packageJson | ConvertTo-Json -Depth 10 | Set-Content $packageJsonPath -Encoding UTF8
Write-Host "Version: $currentVersion -> $newVersion" -ForegroundColor Yellow

# Step 2: Build LSP
Write-Host "`n[2/7] Building LSP server ($config)..." -ForegroundColor Green
dotnet build LilySharp.Lsp -c $config
if ($LASTEXITCODE -ne 0) { throw "LSP build failed" }

# Step 3: Compile TypeScript
Write-Host "`n[3/7] Compiling TypeScript..." -ForegroundColor Green
Push-Location editors/vscode
npm run compile
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "TypeScript compile failed" }

# Step 4: Build VSIX
Write-Host "`n[4/7] Building VSIX package..." -ForegroundColor Green
npx @vscode/vsce package --allow-missing-repository --pre-release
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "VSIX build failed" }
$vsix = Get-ChildItem *.vsix | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Pop-Location

# Step 5: Close VS Code (optional - user should do this)
Write-Host "`n[5/7] Checking VS Code..." -ForegroundColor Green
$vscodeProcesses = Get-Process -Name "Code" -ErrorAction SilentlyContinue
if ($vscodeProcesses) {
    Write-Host "VS Code is running. Please close it before installing." -ForegroundColor Yellow
}

# Step 6: Uninstall old extension
Write-Host "`n[6/7] Uninstalling old extension..." -ForegroundColor Green
code --uninstall-extension lilysharp.lilysharp 2>$null
Start-Sleep -Seconds 1

# Step 7: Install new extension
Write-Host "`n[7/7] Installing new extension..." -ForegroundColor Green
$vsixPath = Join-Path "editors/vscode" $vsix.Name
code --install-extension $vsixPath
if ($LASTEXITCODE -ne 0) { throw "Extension install failed" }

Write-Host "`n=== Deployment Complete ===" -ForegroundColor Cyan
Write-Host "Version: $newVersion" -ForegroundColor Yellow
Write-Host "Installed: $($vsix.Name)" -ForegroundColor Yellow
Write-Host "`nIMPORTANT: Restart VS Code completely to apply changes!" -ForegroundColor Red
