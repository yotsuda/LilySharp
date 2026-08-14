# Lily# - Music notation compiler
# Copyright (C) 2025-2026 Yoshifumi Tsuda
#
# This program is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.
#
# This program is distributed in the hope that it will be useful,
# but WITHOUT ANY WARRANTY; without even the implied warranty of
# MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
# GNU General Public License for more details.
#
# You should have received a copy of the GNU General Public License
# along with this program.  If not, see <https://www.gnu.org/licenses/>.

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
Write-Host "`n[1/6] Stopping VS Code and LSP server..." -ForegroundColor Green

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

# Step 2: Bump the extension version (LOCAL dev-install convenience ONLY).
# A monotonically-newer 0.3.0-dev.N makes VS Code treat each deploy as a fresh
# build. This bump is EPHEMERAL: the finally block below restores package.json to
# its exact original bytes, so it never lingers as a working-tree diff and can
# never be swept into a commit. Releases do NOT use this path at all - release.yml
# derives the version straight from the pushed 'v*' tag.
Write-Host "`n[2/6] Bumping version (ephemeral, reverted after build)..." -ForegroundColor Green
$packageJsonPath = Join-Path $projectRoot "editors/vscode/package.json"
# Snapshot the exact bytes up front so the revert restores encoding/BOM/newlines
# verbatim, regardless of how Set-Content below rewrites the file.
$originalPackageJsonBytes = [System.IO.File]::ReadAllBytes($packageJsonPath)

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

try {
    $packageJson.version = $newVersion
    $packageJson | ConvertTo-Json -Depth 10 | Set-Content $packageJsonPath -Encoding UTF8
    Write-Host "Version: $currentVersion -> $newVersion" -ForegroundColor Yellow
    # The LSP no longer carries a hand-edited version constant - it reads its
    # AssemblyInformationalVersion at runtime, so the bundled server is stamped via
    # -p:Version at publish time (Step 3), exactly as release.yml does.

    # Step 3: Build VSIX (runs: tsc, dotnet publish, vsce package).
    # --skip-license + --allow-missing-repository keep vsce fully NON-INTERACTIVE so the
    # deploy never stalls on a [y/N] prompt.
    Write-Host "`n[3/6] Building VSIX package..." -ForegroundColor Green
    Push-Location (Join-Path $projectRoot "editors/vscode")
    # Publish the LSP SELF-CONTAINED for this machine (win-x64) so the bundled server
    # runs without a system .NET install, then package a matching platform-specific
    # VSIX. (Marketplace builds for every platform: editors/vscode/publish-marketplace.ps1.)
    dotnet publish ../../LilySharp.Lsp -c Release -r win-x64 --self-contained true -p:Version=$newVersion -o ./server
    if ($LASTEXITCODE -ne 0) { Pop-Location; throw "Server self-contained publish failed" }
    npx @vscode/vsce package --target win32-x64 --allow-missing-repository --skip-license --pre-release
    if ($LASTEXITCODE -ne 0) { Pop-Location; throw "VSIX build failed" }
    $vsix = Get-ChildItem *.vsix | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    Pop-Location

    # Step 4: Install extension (force overwrite so it always lands, even on a version
    # clash). --force also suppresses any install confirmation.
    Write-Host "`n[4/6] Installing extension..." -ForegroundColor Green
    code --uninstall-extension ytsuda.lilysharp 2>$null
    Start-Sleep -Seconds 1
    code --install-extension (Join-Path $projectRoot "editors/vscode/$($vsix.Name)") --force
    if ($LASTEXITCODE -ne 0) { throw "Extension install failed" }

    # Step 5: Cleanup old VSIX files
    Write-Host "`n[5/6] Cleanup..." -ForegroundColor Green
    Get-ChildItem (Join-Path $projectRoot "editors/vscode") -Filter "*.vsix" |
        Sort-Object LastWriteTime -Descending | Select-Object -Skip 3 | Remove-Item -Force

    # Step 6: Restart VS Code
    Write-Host "`n[6/6] Restarting VS Code..." -ForegroundColor Green
    Start-Process code
}
finally {
    # Revert the ephemeral bump so the working tree ends exactly as it started -
    # zero diff, nothing to commit or discard, and no way for the bump to be swept
    # into an unrelated commit. Runs even if a step above threw.
    [System.IO.File]::WriteAllBytes($packageJsonPath, $originalPackageJsonBytes)
    Write-Host "Reverted bump ($newVersion -> $currentVersion); working tree clean." -ForegroundColor DarkGray
}

Write-Host "`n=== Deployment Complete ===" -ForegroundColor Cyan
Write-Host "VSIX: $($vsix.Name)" -ForegroundColor Yellow
Write-Host "Version: $newVersion (built into the VSIX; NOT committed)" -ForegroundColor Yellow
