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

# Builds platform-specific, SELF-CONTAINED VSIXs for the Marketplace — one per
# target, each bundling that platform's .NET runtime so users need NO system .NET.
#
#   ./publish-marketplace.ps1                     # package .vsix files into ./dist (default)
#   ./publish-marketplace.ps1 -Publish            # upload each target, PAT auth
#   ./publish-marketplace.ps1 -Publish -AzureCredential   # ...Microsoft Entra ID auth
#
# Requires: dotnet SDK and npm deps installed. -Publish additionally needs ONE of:
#   * a vsce PAT for the publisher — VSCE_PAT env var, or `npx @vscode/vsce login <pub>`.
#     The PAT comes from an Azure DevOps organization and expires (one year at most),
#     so a release stops working on a schedule nobody is watching.
#   * -AzureCredential, which takes a Microsoft Entra ID token instead: `az login`
#     once (Azure CLI, or any other credential in @azure/identity's chain) and there
#     is no Azure DevOps organization to create and no token to rotate.
# Either way the identity must OWN the publisher named in package.json; signing in
# with a different Microsoft account is the failure this script now checks for up
# front rather than discovering on the first upload.
param([switch]$Publish, [switch]$AzureCredential)

$ErrorActionPreference = 'Stop'
$vscodeDir = $PSScriptRoot
Push-Location $vscodeDir
try {
    # VS Code target -> .NET RID. Only platforms whose SkiaSharp natives we ship.
    $targets = [ordered]@{
        'win32-x64'    = 'win-x64'
        'win32-arm64'  = 'win-arm64'
        'linux-x64'    = 'linux-x64'
        'linux-arm64'  = 'linux-arm64'
        'linux-armhf'  = 'linux-arm'
        'alpine-x64'   = 'linux-musl-x64'
        'darwin-x64'   = 'osx-x64'
        'darwin-arm64' = 'osx-arm64'
    }

    # ONE AUTH CHECK BEFORE EIGHT ~50 MB UPLOADS. Without it the first thing a wrong
    # identity meets is `vsce publish` for win32-x64 — after that target's runtime has
    # been cross-published and packed — and the seven that follow each pay the same
    # build before failing the same way. verify-pat asks the Marketplace whether this
    # identity may publish for this publisher and costs one request.
    # ⚠️ THE PUBLISHER IS READ FROM package.json, not spelled again here: it is the
    # value vsce itself will use, so the check cannot drift from the upload.
    if ($Publish) {
        $publisher = (Get-Content package.json -Raw | ConvertFrom-Json).publisher
        Write-Host "Verifying publish rights for '$publisher'..." -ForegroundColor Cyan
        if ($AzureCredential) { npx @vscode/vsce verify-pat --azure-credential $publisher }
        else                  { npx @vscode/vsce verify-pat $publisher }
        if ($LASTEXITCODE -ne 0) {
            throw "verify-pat failed for publisher '$publisher' — nothing was published. " +
                  "Check that the signed-in identity owns that publisher at " +
                  "https://marketplace.visualstudio.com/manage/publishers/$publisher"
        }
    }

    Write-Host "Compiling extension TypeScript..." -ForegroundColor Cyan
    npm run compile
    if ($LASTEXITCODE -ne 0) { throw "TypeScript compile failed" }
    New-Item -ItemType Directory -Force -Path dist | Out-Null

    # These VSIXs are conveyed to the public, so they carry the licenses of
    # everything they bundle: the GPL itself, Lily#'s third-party notices, the
    # Apache-2.0 text (Six Labors), the upstream notices for the Skia/HarfBuzz
    # natives, and the ported-file list with its LilyPond copyright lines.
    # release.yml does the same for the tag-driven builds.
    Write-Host "Bundling licenses and notices..." -ForegroundColor Cyan
    foreach ($f in @('LICENSE', 'THIRD-PARTY-NOTICES.md', 'THIRD-PARTY-NOTICES-SkiaSharp.txt',
                     'LICENSE-Apache-2.0.txt', 'LILYPOND-ATTRIBUTION.md')) {
        Copy-Item (Join-Path $vscodeDir "../../$f") (Join-Path $vscodeDir $f) -Force
    }

    foreach ($target in $targets.Keys) {
        $rid = $targets[$target]
        Write-Host "`n=== $target  ($rid) ===" -ForegroundColor Cyan

        if (Test-Path server) { Remove-Item server -Recurse -Force }
        # Self-contained publish for this RID: bundles the .NET runtime + only this
        # platform's SkiaSharp native. dotnet cross-publishes from any host.
        dotnet publish ../../LilySharp.Lsp -c Release -r $rid --self-contained true -o ./server
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $rid" }

        if ($Publish) {
            # Spelled as two whole calls rather than a spliced argument array: the
            # command already begins with a literal `@vscode/vsce`, and a second `@`
            # in argument position is PowerShell's splatting sigil.
            if ($AzureCredential) { npx @vscode/vsce publish --target $target --azure-credential }
            else                  { npx @vscode/vsce publish --target $target }
        } else {
            npx @vscode/vsce package --target $target -o "dist/lilysharp-$target.vsix"
        }
        if ($LASTEXITCODE -ne 0) { throw "vsce failed for $target" }
    }

    Write-Host "`nDone. $($targets.Count) target(s) processed." -ForegroundColor Green
    if (-not $Publish) { Get-ChildItem dist\*.vsix | ForEach-Object { '{0}  {1:N1} MB' -f $_.Name, ($_.Length/1MB) } }
}
finally {
    Pop-Location
}
