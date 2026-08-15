# Deploys the Lily# language server to the INSTALLED VS Code extension.
#
# `dotnet publish -o editors/vscode/server` alone only updates the repo copy;
# the editor launches its own copy under
# %USERPROFILE%\.vscode\extensions\ytsuda.lilysharp-<ver>\server.
# The language client restarts a killed server within milliseconds and the
# fresh process re-locks the DLLs, so the copy runs in a kill->copy loop:
# after ~5 crashes the client gives up restarting and the copy goes through.
# (A rename-swap does NOT work: assemblies are loaded without share-delete.)
#
# Usage: pwsh tools/Deploy-Lsp.ps1 [-Configuration Release]
# Afterwards: run "Developer: Reload Window" in VS Code.

param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$repoServer = Join-Path $repoRoot 'editors\vscode\server'

# Compile the extension TypeScript so the out/ copy below carries the latest
# client changes (this script also deploys out/ and syntaxes/, not just the server).
#
# The compiler is a devDependency (esbuild), and editors/vscode/.gitignore keeps
# node_modules/ out of the repo, so a checkout that has never run `npm ci` there has
# no compiler at all. Without the bootstrap below `npm run compile` dies with
# MODULE_NOT_FOUND: esbuild and the throw reported it as a COMPILE failure -- which
# sends the next reader looking for a type error in src/ that does not exist. The
# .NET side never had this hole (dotnet restores on build); only the npm side did.
$repoExtSrc = Join-Path $repoRoot 'editors\vscode'
Push-Location $repoExtSrc
try {
    if (-not (Test-Path (Join-Path $repoExtSrc 'node_modules'))) {
        if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
            throw 'node_modules is missing and npm was not found on PATH. Install Node.js (18+), then re-run.'
        }
        Write-Host "node_modules missing - installing extension dev dependencies..."
        # ci for the pinned tree when the lockfile is there; install is the fallback.
        if (Test-Path (Join-Path $repoExtSrc 'package-lock.json')) { npm ci } else { npm install }
        if ($LASTEXITCODE -ne 0) { throw "npm install failed ($LASTEXITCODE)" }
    }

    Write-Host "Compiling extension TypeScript..."
    npm run compile
    $compileExit = $LASTEXITCODE
}
finally { Pop-Location }
if ($compileExit -ne 0) { throw "esbuild compile failed ($compileExit)" }

Write-Host "Publishing LilySharp.Lsp ($Configuration)..."
# Clean first: a prior SELF-CONTAINED publish leaves coreclr + the bundled runtime,
# which the client would detect and then launch the (framework-dependent) apphost
# DIRECTLY and fail. A clean framework-dependent publish keeps dev deploys fast and
# the client correctly runs it via `dotnet`. (Marketplace builds are self-contained
# via editors/vscode/publish-marketplace.ps1.)
if (Test-Path $repoServer) { Remove-Item $repoServer -Recurse -Force }
dotnet publish (Join-Path $repoRoot 'LilySharp.Lsp') -c $Configuration -o $repoServer --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

# Newest installed lilysharp extension (there may be several versions).
# The root is not always ~\.vscode\extensions: VSCODE_EXTENSIONS overrides it and
# Insiders keeps its own. It may also not exist AT ALL on a machine where VS Code
# has never installed an extension -- and Get-ChildItem on a missing path throws
# under $ErrorActionPreference='Stop', turning "nothing to deploy to" into a crash
# on the line AFTER the publish that did succeed.
$extRoots = @(
    $env:VSCODE_EXTENSIONS
    (Join-Path $env:USERPROFILE '.vscode\extensions')
    (Join-Path $env:USERPROFILE '.vscode-insiders\extensions')
) | Where-Object { $_ -and (Test-Path $_) }

$extDir = $extRoots |
    ForEach-Object { Get-ChildItem $_ -Directory -Filter 'ytsuda.lilysharp-*' -ErrorAction SilentlyContinue } |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $extDir) {
    Write-Host 'No installed lilysharp extension found - repo publish only.' -ForegroundColor Yellow
    if ($extRoots) { Write-Host "  looked in: $($extRoots -join '; ')" -ForegroundColor DarkGray }
    else { Write-Host '  no VS Code extensions folder exists on this machine.' -ForegroundColor DarkGray }
    # Deploy-Lsp copies INTO an installed extension; it cannot create one. The VSIX
    # route is the other script, and it is what a fresh machine needs first.
    Write-Host '  install it once with: pwsh tools/Package-And-Install.ps1' -ForegroundColor DarkGray
    return
}
$dest = Join-Path $extDir.FullName 'server'
Write-Host "Deploying to $dest"

$ok = $false
for ($round = 1; $round -le 10 -and -not $ok; $round++) {
    # Kill every server process launched from the installed extension.
    Get-CimInstance Win32_Process -Filter "Name='dotnet.exe' OR Name='lilysharp-lsp.exe'" |
        Where-Object { $_.CommandLine -like '*ytsuda.lilysharp*' } |
        ForEach-Object {
            Write-Host "  kill $($_.Name) $($_.ProcessId)"
            Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
        }
    Start-Sleep -Milliseconds 300
    try {
        # Clean the destination first: if the installed extension was a
        # SELF-CONTAINED build (marketplace VSIX), its coreclr + runtime would
        # linger and make the client launch this framework-dependent apphost
        # DIRECTLY (→ "No frameworks were found"). Removing them leaves a pure
        # FD server the client correctly runs via `dotnet`.
        if (Test-Path $dest) { Remove-Item "$dest\*" -Recurse -Force -ErrorAction Stop }
        Copy-Item "$repoServer\*" $dest -Recurse -Force -ErrorAction Stop
        $ok = $true
    } catch {
        Write-Host "  round ${round}: still locked, retrying..."
    }
}
if (-not $ok) { throw 'Copy kept failing - close VS Code and re-run.' }

# Grammar + client assets are not locked by the server process - plain copy.
$repoExt = Join-Path $repoRoot 'editors\vscode'
Copy-Item "$repoExt\syntaxes\*" (Join-Path $extDir.FullName 'syntaxes') -Recurse -Force -ErrorAction SilentlyContinue
if (Test-Path "$repoExt\out") {
    Copy-Item "$repoExt\out\*" (Join-Path $extDir.FullName 'out') -Recurse -Force -ErrorAction SilentlyContinue
}
# media/ carries the WEBVIEW's own copy of Emmentaler. It was missing here, and
# that is not cosmetic: the preview resolves the codepoints the server emits
# against THIS font, so a stale copy draws the right layout with the wrong
# glyphs. After the 2.26.0 font swap the installed preview kept the 2.24.4
# woff2 and rendered noteheads as triangles, because 73 of the 115 PUA
# assignments moved between the two fonts (LilyPond always looks glyphs up by
# feta NAME; the codepoints are not stable across versions).
Copy-Item "$repoExt\media\*" (Join-Path $extDir.FullName 'media') -Recurse -Force -ErrorAction SilentlyContinue

# The manifest too, so contributed settings / labels / commands (e.g. a new
# config enum or its enumItemLabels) deploy. VS Code re-reads package.json on the
# next window reload.
Copy-Item "$repoExt\package.json" $extDir.FullName -Force -ErrorAction SilentlyContinue

$ver = [System.Diagnostics.FileVersionInfo]::GetVersionInfo(
    (Join-Path $dest 'lilysharp-lsp.dll')).ProductVersion
Write-Host "Deployed $ver (round $round)" -ForegroundColor Green
Write-Host 'Now run "Developer: Reload Window" in VS Code.'
