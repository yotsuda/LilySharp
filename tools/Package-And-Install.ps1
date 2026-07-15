# Repackages the Lily# VS Code extension and (re)installs it into VS Code.
#
# Use this after changing the extension's TypeScript (editors/vscode/src/**) to
# apply the change to the running editor. It is the automation of:
#     cd editors/vscode
#     npm run compile      # (vsce runs this via vscode:prepublish)
#     vsce package
#     code --install-extension <vsix> --force
#
# `vsce package` runs the extension's `vscode:prepublish` script itself
# (tsc --noEmit + a production esbuild), so a type error fails the build here and
# nothing is packaged. The current server/ folder is bundled into the VSIX as-is
# (this script does NOT rebuild the LSP server — use Deploy-Lsp.ps1 for that).
#
# Faster alternative for a client-only change: Deploy-Lsp.ps1 copies out/ +
# package.json straight into the installed extension (no reinstall) and also
# rebuilds the server. This script instead produces a real VSIX and does a clean
# --force reinstall, so the installed extension matches the repo exactly.
#
# Usage: pwsh tools/Package-And-Install.ps1 [-NoInstall] [-Clean]
#   -NoInstall  build the VSIX only, don't install it
#   -Clean      uninstall every installed lilysharp version before installing
# Afterwards: run "Developer: Reload Window" in VS Code (or restart it).

param(
    [switch]$NoInstall,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$extDir = Join-Path $repoRoot 'editors\vscode'

# Locate the VS Code CLI (a code.cmd shim on Windows) whether or not it's on PATH.
function Get-CodeCli {
    $cmd = Get-Command code -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($p in @(
            "$env:ProgramFiles\Microsoft VS Code\bin\code.cmd",
            "${env:ProgramFiles(x86)}\Microsoft VS Code\bin\code.cmd",
            "$env:LOCALAPPDATA\Programs\Microsoft VS Code\bin\code.cmd")) {
        if (Test-Path $p) { return $p }
    }
    return $null
}

Push-Location $extDir
try {
    $version = (Get-Content package.json -Raw | ConvertFrom-Json).version
    $vsix = Join-Path $extDir "lilysharp-$version.vsix"

    Write-Host "Packaging lilysharp $version ..." -ForegroundColor Cyan
    # vsce is not a repo dependency; npx fetches it on demand.
    npx --yes @vscode/vsce package --out $vsix
    if ($LASTEXITCODE -ne 0) { throw "vsce package failed ($LASTEXITCODE)" }
    Write-Host "Built $vsix" -ForegroundColor Green

    if ($NoInstall) {
        Write-Host '-NoInstall: skipping install.' -ForegroundColor Yellow
        return
    }

    $code = Get-CodeCli
    if (-not $code) {
        throw 'VS Code CLI (code) not found. Install manually: code --install-extension <vsix> --force'
    }

    if ($Clean) {
        Write-Host 'Uninstalling existing lilysharp extension...'
        & $code --uninstall-extension ytsuda.lilysharp 2>&1 | Out-Host
    }

    Write-Host 'Installing VSIX...' -ForegroundColor Cyan
    & $code --install-extension $vsix --force 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "install failed ($LASTEXITCODE)" }

    Write-Host "Installed lilysharp $version." -ForegroundColor Green
    Write-Host 'Now run "Developer: Reload Window" in VS Code (or restart it).'
    Write-Host 'If the change does not take effect (same version number), re-run with -Clean.' -ForegroundColor DarkGray
}
finally {
    Pop-Location
}
