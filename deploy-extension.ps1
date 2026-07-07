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

# Step 1: Kill VS Code and LSP to release all file locks on the server DLL/EXE.
# VS Code is killed FIRST so it cannot respawn the LSP server while we work; then
# any leftover LSP process is killed. This is intentional — overwriting the bundled
# server requires the running exe/dll to be unlocked.
Write-Host "`n[1/7] Stopping VS Code and LSP server..." -ForegroundColor Green

function Stop-ByName($name) {
    $procs = Get-Process -Name $name -ErrorAction SilentlyContinue
    if ($procs) { $procs | Stop-Process -Force -ErrorAction SilentlyContinue }
}

Stop-ByName "Code"

# Wait for ALL Code processes (main + helpers) to actually exit before continuing.
for ($i = 0; $i -lt 30; $i++) {
    if (-not (Get-Process -Name "Code" -ErrorAction SilentlyContinue)) { break }
    Write-Host "  Waiting for VS Code to exit... ($($i+1))" -ForegroundColor DarkYellow
    Start-Sleep -Milliseconds 500
}

# Now kill any remaining LSP server (won't be respawned with Code down).
Stop-ByName "lilysharp-lsp"

# Wait for the server DLL lock to release.
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
Write-Host "`n[2/7] Updating versions..." -ForegroundColor Green
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

# Step 3: Build VSIX (runs: tsc, dotnet publish, vsce package).
# --skip-license + --allow-missing-repository keep vsce fully NON-INTERACTIVE so the
# deploy never stalls on a [y/N] prompt.
Write-Host "`n[3/7] Building VSIX package..." -ForegroundColor Green
Push-Location (Join-Path $projectRoot "editors/vscode")
# Publish the LSP SELF-CONTAINED for this machine (win-x64) so the bundled server
# runs without a system .NET install, then package a matching platform-specific
# VSIX. (Marketplace builds for every platform: editors/vscode/publish-marketplace.ps1.)
dotnet publish ../../LilySharp.Lsp -c Release -r win-x64 --self-contained true -o ./server
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "Server self-contained publish failed" }
npx @vscode/vsce package --target win32-x64 --allow-missing-repository --skip-license --pre-release
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "VSIX build failed" }
$vsix = Get-ChildItem *.vsix | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Pop-Location

# Step 4: Install extension (force overwrite so it always lands, even on a version
# clash). --force also suppresses any install confirmation.
Write-Host "`n[4/7] Installing extension..." -ForegroundColor Green
code --uninstall-extension ytsuda.lilysharp 2>$null
Start-Sleep -Seconds 1
code --install-extension (Join-Path $projectRoot "editors/vscode/$($vsix.Name)") --force
if ($LASTEXITCODE -ne 0) { throw "Extension install failed" }

# Step 5: Cleanup old VSIX files
Write-Host "`n[5/7] Cleanup..." -ForegroundColor Green
Get-ChildItem (Join-Path $projectRoot "editors/vscode") -Filter "*.vsix" |
    Sort-Object LastWriteTime -Descending | Select-Object -Skip 3 | Remove-Item -Force

# Step 6: Restart VS Code
Write-Host "`n[6/7] Restarting VS Code..." -ForegroundColor Green
Start-Process code

# Step 7: Commit the two version stamps so they never linger as a noisy
# working-tree diff that the next feature commit has to step around. Only the
# package.json version and the LSP Version const are committed (taken by explicit
# pathspec, so any other staged/unstaged work is left untouched). Skipped when
# not in a git repo or when the stamps are unchanged.
Write-Host "`n[7/7] Committing version bump..." -ForegroundColor Green
$PSNativeCommandUseErrorActionPreference = $false  # native non-zero is a signal here, not a failure
if ((git rev-parse --is-inside-work-tree 2>$null) -eq 'true') {
    git diff --quiet -- $packageJsonPath $lspServerPath
    if ($LASTEXITCODE -ne 0) {
        git commit -q -m "Bump dev build version ($newVersion)" -- $packageJsonPath $lspServerPath
        Write-Host "  Committed: Bump dev build version ($newVersion)" -ForegroundColor Yellow
    } else {
        Write-Host "  No version changes to commit" -ForegroundColor DarkGray
    }
} else {
    Write-Host "  Not a git repo; skipping version commit" -ForegroundColor DarkGray
}

Write-Host "`n=== Deployment Complete ===" -ForegroundColor Cyan
Write-Host "VSIX: $($vsix.Name)" -ForegroundColor Yellow
Write-Host "LSP:  $lspVersion" -ForegroundColor Yellow
