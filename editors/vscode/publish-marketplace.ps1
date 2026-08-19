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
#   ./publish-marketplace.ps1            # package .vsix files into ./dist (default)
#   ./publish-marketplace.ps1 -Publish   # upload each target to the Marketplace
#
# Requires: dotnet SDK, npm deps installed, and (for -Publish) a vsce PAT for the
# `ytsuda` publisher (VSCE_PAT env var or `npx @vscode/vsce login ytsuda`).
param([switch]$Publish)

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
            npx @vscode/vsce publish --target $target
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
