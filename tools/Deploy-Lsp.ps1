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
Write-Host "Compiling extension TypeScript..."
Push-Location (Join-Path $repoRoot 'editors\vscode')
npm run compile
$compileExit = $LASTEXITCODE
Pop-Location
if ($compileExit -ne 0) { throw "tsc compile failed ($compileExit)" }

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
$extDir = Get-ChildItem "$env:USERPROFILE\.vscode\extensions" -Directory -Filter 'ytsuda.lilysharp-*' |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $extDir) {
    Write-Host 'No installed lilysharp extension found - repo publish only.' -ForegroundColor Yellow
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

$ver = [System.Diagnostics.FileVersionInfo]::GetVersionInfo(
    (Join-Path $dest 'lilysharp-lsp.dll')).ProductVersion
Write-Host "Deployed $ver (round $round)" -ForegroundColor Green
Write-Host 'Now run "Developer: Reload Window" in VS Code.'
