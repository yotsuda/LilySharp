# Deploys the Lily# language server to the INSTALLED VS Code extension.
#
# `dotnet publish -o editors/vscode/server` alone only updates the repo copy;
# the editor launches its own copy under
# %USERPROFILE%\.vscode\extensions\yotsuda.lilysharp-<ver>\server.
#
# WHAT MAKES A DEPLOY CERTAIN IS THE CHECK AT THE END, NOT THE COPY.
# This script used to finish by printing the deployed ProductVersion and telling you
# to reload the window. That reports the copy it ATTEMPTED, not the copy that landed:
# a locked file, a half-written tree, or a deploy into a folder the editor no longer
# loads all end with the same green line. Every deploy now ends with a byte
# comparison of every file it placed, an assertion that the server the editor will
# launch is the one this run published, AND A LAUNCH OF IT. Bytes on disk are not a
# working server: this machine's Smart App Control refuses to load locally built
# unsigned assemblies, and a deploy that lands perfectly then leaves a dead preview
# is the same mystery in a new place (user report 2026-08-25). Any of the three
# failing throws.
#
# Usage: pwsh tools/Deploy-Lsp.ps1 [-Configuration Release] [-SkipCompile]
# Afterwards: run "Developer: Reload Window" in VS Code.

param(
    [string]$Configuration = 'Release',
    [switch]$SkipCompile
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$repoServer = Join-Path $repoRoot 'editors\vscode\server'
$repoExt = Join-Path $repoRoot 'editors\vscode'

# ---------------------------------------------------------------- helpers ----

# Every file under $Root, keyed by its path relative to $Root, with its hash.
# The comparison below is by CONTENT and not by timestamp: a copy that fails
# halfway leaves files whose timestamps are new and whose bytes are the old ones.
function Get-TreeHashes([string]$Root) {
    $map = @{}
    if (-not (Test-Path $Root)) { return $map }
    $prefix = (Resolve-Path $Root).Path.TrimEnd('\') + '\'
    foreach ($f in Get-ChildItem $Root -Recurse -File -Force) {
        $map[$f.FullName.Substring($prefix.Length)] = (Get-FileHash $f.FullName -Algorithm SHA256).Hash
    }
    return $map
}

# Names every way the two trees disagree. An EXTRA file on the destination is a real
# difference: a stale assembly the new publish no longer emits is still loadable, and
# a self-contained leftover is exactly what makes the client launch the apphost
# directly instead of running it through `dotnet`.
function Compare-Tree([hashtable]$Source, [hashtable]$Dest, [string]$Label) {
    $problems = @()
    foreach ($k in $Source.Keys) {
        if (-not $Dest.ContainsKey($k)) { $problems += "$Label/$k MISSING" }
        elseif ($Dest[$k] -ne $Source[$k]) { $problems += "$Label/$k DIFFERS" }
    }
    foreach ($k in $Dest.Keys) {
        if (-not $Source.ContainsKey($k)) { $problems += "$Label/$k EXTRA" }
    }
    return $problems
}

# The server processes THIS extension folder launched. Asked two ways on purpose:
# Win32_Process.CommandLine is null for a process this session cannot read, so the
# CommandLine filter alone can leave a live lock behind and the loop then spins its
# ten rounds against a process it never saw.
function Get-ServerProcesses([string]$ExtPath) {
    $found = @()
    $found += Get-Process -Name 'lilysharp-lsp' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($ExtPath, [StringComparison]::OrdinalIgnoreCase) }
    $leaf = Split-Path $ExtPath -Leaf
    $found += Get-CimInstance Win32_Process -Filter "Name='dotnet.exe' OR Name='lilysharp-lsp.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -and $_.CommandLine -like "*$leaf*" } |
        ForEach-Object { Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue }
    return @($found | Where-Object { $_ } | Sort-Object Id -Unique)
}

function Stop-ServerProcesses([string]$ExtPath) {
    $killed = @()
    foreach ($p in Get-ServerProcesses $ExtPath) {
        $killed += "$($p.ProcessName)($($p.Id))"
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    }
    return $killed
}

# Launches the deployed server and reports why it would not run, or $null when it
# does. Closing stdin makes an LSP see EOF and exit 0 immediately, so this costs
# milliseconds; a server the OS refuses dies with a FileLoadException instead.
# MEASURED both ways on 2026-08-25: an allowed build exits 0 after printing
# "LSP Server starting...", a blocked one exits -532462766 with 0x800711C7.
function Test-ServerLaunch([string]$Dll) {
    $psi = [Diagnostics.ProcessStartInfo]::new('dotnet', "`"$Dll`"")
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $p = [Diagnostics.Process]::Start($psi)
    $p.StandardInput.Close()
    $exited = $p.WaitForExit(15000)
    $err = $p.StandardError.ReadToEnd()
    if (-not $exited) { $p.Kill(); return $null }   # still listening: it loaded
    if ($p.ExitCode -eq 0) { return $null }
    return ($err -split "`n" | Where-Object { $_.Trim() } | Select-Object -First 1)
}

# ------------------------------------------------------- extension client ----

if (-not $SkipCompile) {
    # The compiler is a devDependency (esbuild), and editors/vscode/.gitignore keeps
    # node_modules/ out of the repo, so a checkout that has never run `npm ci` there
    # has no compiler at all. Without the bootstrap below `npm run compile` dies with
    # MODULE_NOT_FOUND: esbuild and the throw reported it as a COMPILE failure --
    # which sends the next reader looking for a type error in src/ that does not exist.
    Push-Location $repoExt
    try {
        if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
            throw 'npm was not found on PATH. Install Node.js (18+), or pass -SkipCompile to deploy the server alone.'
        }
        if (-not (Test-Path (Join-Path $repoExt 'node_modules'))) {
            Write-Host 'node_modules missing - installing extension dev dependencies...'
            if (Test-Path (Join-Path $repoExt 'package-lock.json')) { npm ci } else { npm install }
            if ($LASTEXITCODE -ne 0) { throw "npm install failed ($LASTEXITCODE)" }
        }
        Write-Host 'Compiling extension TypeScript...'
        npm run compile
        $compileExit = $LASTEXITCODE
    }
    finally { Pop-Location }
    if ($compileExit -ne 0) { throw "esbuild compile failed ($compileExit)" }
}

# --------------------------------------------------------------- publish -----

# Clean first: a prior SELF-CONTAINED publish leaves coreclr + the bundled runtime,
# which the client would detect and then launch the (framework-dependent) apphost
# DIRECTLY and fail. A clean framework-dependent publish keeps dev deploys fast and
# the client correctly runs it via `dotnet`. (Marketplace builds are self-contained
# via editors/vscode/publish-marketplace.ps1.)
function Publish-Server([string[]]$Extra) {
    if (Test-Path $repoServer) { Remove-Item $repoServer -Recurse -Force }
    # Out-Host, NOT the pipeline: a PowerShell function returns EVERY object written
    # to its output stream, so letting the publish log through makes the caller's
    # $builtVersion an array of build lines with the version at the end -- which the
    # identity check below then reports as a copy that did not land.
    # ReadyToRun: the server is precompiled, so the first renders after a launch do
    # not wait for the JIT. Every Reload Window relaunches the server cold, and the
    # editor's first previews were paying for it -- MEASURED (session 329, the
    # 3-page bench.lys, a bar inserted mid-score over real stdio): first svg after
    # didOpen 2.1-5.0 s with JIT against 1.3-1.5 s ReadyToRun, the first insertion
    # 0.7-1.0 s against 0.43-0.45 s, and the next ones 0.21-0.32 s against
    # 0.15-0.19 s. ReadyToRun needs a RID; --self-contained false keeps the
    # framework-dependent shape the client runs via `dotnet` (no coreclr beside it).
    dotnet publish (Join-Path $repoRoot 'LilySharp.Lsp') -c $Configuration -o $repoServer --nologo `
        -r win-x64 --self-contained false -p:PublishReadyToRun=true @Extra | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
    $dll = Join-Path $repoServer 'lilysharp-lsp.dll'
    if (-not (Test-Path $dll)) { throw "publish produced no lilysharp-lsp.dll in $repoServer" }
    return [System.Diagnostics.FileVersionInfo]::GetVersionInfo($dll).ProductVersion
}

Write-Host "Publishing LilySharp.Lsp ($Configuration)..."
$builtVersion = Publish-Server @()

# ---------------------------------------------------------- pick the target --

# ASK VS CODE WHICH EXTENSION IT LOADS rather than guessing from folder names.
# extensions.json is the editor's own registry, so it names the folder that will
# actually be launched -- including the case this machine is in, where an older
# publisher spelling (ytsuda.lilysharp-0.3.0) still has a folder on disk that the
# editor no longer loads. A deploy into that folder succeeds, verifies, and changes
# nothing the user can see, which is the worst kind of green.
$extRoots = @(
    $env:VSCODE_EXTENSIONS
    (Join-Path $env:USERPROFILE '.vscode\extensions')
    (Join-Path $env:USERPROFILE '.vscode-insiders\extensions')
) | Where-Object { $_ -and (Test-Path $_) }

$extDir = $null
foreach ($root in $extRoots) {
    $manifest = Join-Path $root 'extensions.json'
    if (-not (Test-Path $manifest)) { continue }
    $entry = @(Get-Content $manifest -Raw | ConvertFrom-Json) |
        Where-Object { $_.identifier.id -like '*.lilysharp' } |
        Sort-Object { $_.version } -Descending | Select-Object -First 1
    if (-not $entry) { continue }
    # location.path is a URI-ish '/c:/Users/...' form; make it a Windows path.
    $p = ($entry.location.path -replace '^/', '') -replace '/', '\'
    if (Test-Path $p) { $extDir = Get-Item $p; break }
}
if (-not $extDir) {
    # Fallback: the registry was unreadable, so scan. BOTH publisher spellings,
    # because the folder name is not the identifier.
    $extDir = $extRoots |
        ForEach-Object { Get-ChildItem $_ -Directory -Filter '*.lilysharp-*' -ErrorAction SilentlyContinue } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($extDir) { Write-Host "  extensions.json unreadable - guessed $($extDir.Name) by folder date" -ForegroundColor Yellow }
}
if (-not $extDir) {
    Write-Host 'No installed lilysharp extension found - repo publish only.' -ForegroundColor Yellow
    if ($extRoots) { Write-Host "  looked in: $($extRoots -join '; ')" -ForegroundColor DarkGray }
    else { Write-Host '  no VS Code extensions folder exists on this machine.' -ForegroundColor DarkGray }
    Write-Host '  install it once with: pwsh tools/Package-And-Install.ps1' -ForegroundColor DarkGray
    return
}
$dest = Join-Path $extDir.FullName 'server'
Write-Host "Deploying to $dest"

$wasRunning = Get-ServerProcesses $extDir.FullName
if ($wasRunning) {
    Write-Host "  a server is running from this extension: $(($wasRunning | ForEach-Object { "$($_.ProcessName)($($_.Id))" }) -join ', ')"
}

# -------------------------------------------------------------- the copy -----

# Leftovers from an earlier run whose swapped-out directory was still locked.
Get-ChildItem $extDir.FullName -Directory -Filter 'server.old-*' -ErrorAction SilentlyContinue |
    ForEach-Object { Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }

# STAGE, THEN SWAP DIRECTORIES. The old shape cleaned $dest and copied into it, so a
# copy that lost the race left the INSTALLED EXTENSION EMPTY and threw -- worse than
# not deploying, and a state the next reader has to recognise before re-running.
# Staging copies into a directory nothing has open, and the only racy steps left are
# two renames.
# NOTE ON THE OLD REMARK, which said "a rename-swap does NOT work: assemblies are
# loaded without share-delete". That is true OF THE FILES and it is why this renames
# the DIRECTORY: NTFS lets a directory be renamed while files inside it are open,
# because the lock is on the file. What can still hold it is a handle on the
# DIRECTORY itself -- a process whose working directory is inside it -- which is why
# the kill-and-retry loop is kept underneath rather than replaced.
function Move-ServerIntoPlace {
    $staging = Join-Path $extDir.FullName 'server.deploy-tmp'
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
    Copy-Item $repoServer $staging -Recurse -Force

    $ok = $false
    $rounds = 0
    $retiredPath = $null
    for ($round = 1; $round -le 10 -and -not $ok; $round++) {
        $rounds = $round
        # KILL FIRST, THEN HAMMER THE SWAP FOR A SHORT WINDOW. The handle drops the
        # instant the process dies and the language client needs a few milliseconds to
        # relaunch, so the rename has to be RETRIED CONTINUOUSLY rather than attempted
        # once per kill. Trying once per round is what made a deploy cost five crash
        # notifications: every attempt landed after the client had already restarted
        # the server, and the copy only went through when the client gave up (~5).
        $killed = Stop-ServerProcesses $extDir.FullName
        $deadline = (Get-Date).AddMilliseconds(800)
        do {
            try {
                # The first rename is the one that can be blocked; once $dest is out of
                # the way the second is a rename of a directory nothing has open, so the
                # retired path is remembered and not re-attempted.
                if (-not $retiredPath -and (Test-Path $dest)) {
                    $retired = 'server.old-{0:yyyyMMddHHmmssfff}' -f (Get-Date)
                    Rename-Item $dest $retired -ErrorAction Stop
                    $retiredPath = Join-Path $extDir.FullName $retired
                }
                Rename-Item $staging 'server' -ErrorAction Stop
                $ok = $true
            }
            catch { Start-Sleep -Milliseconds 25 }
        } while (-not $ok -and (Get-Date) -lt $deadline)
        if (-not $ok) {
            Write-Host "  round ${round}: still locked$(if ($killed) { " (killed $($killed -join ', '))" }), retrying..."
        }
    }
    if (-not $ok) {
        # Put the extension back the way it was before giving up.
        if ($retiredPath -and (Test-Path $retiredPath) -and -not (Test-Path $dest)) {
            Rename-Item $retiredPath 'server' -ErrorAction SilentlyContinue
        }
        Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
        throw "Could not swap $dest into place after $rounds rounds - close VS Code and re-run."
    }
    if ($retiredPath) { Remove-Item $retiredPath -Recurse -Force -ErrorAction SilentlyContinue }
    return $rounds
}

# EVERY FILE, BY CONTENT, against the tree the editor will launch -- not against the
# one the publish wrote. This is the step that turns "deployed" from a claim into a
# reading, and it is why this script exists in this shape.
function Assert-ServerTree {
    $problems = @(Compare-Tree (Get-TreeHashes $repoServer) (Get-TreeHashes $dest) 'server')
    if ($problems) {
        $problems | Select-Object -First 20 | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        if ($problems.Count -gt 20) { Write-Host "  ... and $($problems.Count - 20) more" -ForegroundColor Red }
        throw "Deploy verification failed: $($problems.Count) file(s) do not match."
    }
    # ...and the build identity, which is the check that catches a deploy that copied
    # an OLD publish perfectly.
    $deployed = [System.Diagnostics.FileVersionInfo]::GetVersionInfo(
        (Join-Path $dest 'lilysharp-lsp.dll')).ProductVersion
    if ($deployed -ne $builtVersion) {
        throw "Deployed $deployed but published $builtVersion - the copy did not land."
    }
    return $deployed
}

# TWO ATTEMPTS, AND THE SECOND ONE EXISTS FOR A MEASURED REASON. Smart App Control
# refuses to load locally built unsigned assemblies it has decided against, and the
# .NET build is DETERMINISTIC -- the same source produces the same bytes, so the same
# file is refused every time and re-running the deploy can never help. Publishing with
# Deterministic=false gives the assembly a fresh MVID and therefore a file the policy
# has no verdict on. MEASURED 2026-08-25: the refused build and a Deterministic=false
# build of the same source hash differently (55A62816.. against 297A56D9..) and the
# second one launches. It is a retry and not a cure: the durable answer is the machine
# owner's decision about Smart App Control, which the throw below names.
$rounds = Move-ServerIntoPlace
Write-Host 'Verifying...'
$deployedVersion = Assert-ServerTree
$launchError = Test-ServerLaunch (Join-Path $dest 'lilysharp-lsp.dll')

if ($launchError) {
    Write-Host "  the deployed server does not start:" -ForegroundColor Yellow
    Write-Host "    $launchError" -ForegroundColor Yellow
    if ($launchError -match '0x800711C7|control policy|Code Integrity|WLDP') {
        Write-Host '  re-publishing with a fresh assembly identity (Deterministic=false)...' -ForegroundColor Yellow
        $builtVersion = Publish-Server @('-p:Deterministic=false')
        $rounds = Move-ServerIntoPlace
        $deployedVersion = Assert-ServerTree
        $launchError = Test-ServerLaunch (Join-Path $dest 'lilysharp-lsp.dll')
    }
}
if ($launchError) {
    throw @"
The server was deployed and verified but the OS will not load it:
  $launchError
This machine has Smart App Control ON (HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy
VerifiedAndReputablePolicyState = 1), which blocks locally built unsigned assemblies.
The blocks are logged in Microsoft-Windows-CodeIntegrity/Operational (event 3077).
Turning Smart App Control off is the only durable fix and it CANNOT BE UNDONE without
reinstalling Windows, so it is the machine owner's call - see
memory/smart-app-control-blocks-lilysharp-dlls.md.
"@
}

# Grammar + client assets are not locked by the server process - plain copy.
# media/ carries the WEBVIEW's own copy of Emmentaler. A stale copy draws the right
# layout with the wrong glyphs: after the 2.26.0 font swap the installed preview kept
# the 2.24.4 woff2 and rendered noteheads as triangles, because 73 of the 115 PUA
# assignments moved between the two fonts (LilyPond looks glyphs up by feta NAME).
# The manifest too, so contributed settings / labels / commands deploy; VS Code
# re-reads package.json on the next window reload.
$assets = @('syntaxes', 'out', 'media')
foreach ($a in $assets) {
    $src = Join-Path $repoExt $a
    if (-not (Test-Path $src)) { continue }
    $dst = Join-Path $extDir.FullName $a
    if (-not (Test-Path $dst)) { New-Item -ItemType Directory -Path $dst | Out-Null }
    Copy-Item "$src\*" $dst -Recurse -Force
}
Copy-Item (Join-Path $repoExt 'package.json') $extDir.FullName -Force

$problems = @()
foreach ($a in $assets) {
    $src = Join-Path $repoExt $a
    if (-not (Test-Path $src)) { continue }
    # This direction only: the destination may legitimately hold an asset the packaged
    # VSIX carried and the repo no longer ships. Everything the repo HAS must be there
    # and identical.
    $problems += @(Compare-Tree (Get-TreeHashes $src) (Get-TreeHashes (Join-Path $extDir.FullName $a)) $a |
        Where-Object { $_ -notlike '*EXTRA' })
}
if ((Get-FileHash (Join-Path $repoExt 'package.json') -Algorithm SHA256).Hash -ne
    (Get-FileHash (Join-Path $extDir.FullName 'package.json') -Algorithm SHA256).Hash) {
    $problems += 'package.json DIFFERS'
}
if ($problems) {
    $problems | Select-Object -First 20 | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    throw "Deploy verification failed: $($problems.Count) asset file(s) do not match."
}

$head = git -C $repoRoot rev-parse HEAD 2>$null
if ($LASTEXITCODE -eq 0 -and $head) {
    $stamped = ($deployedVersion -split '\+')[-1]
    if ($stamped -and $head -notlike "$stamped*" -and $stamped -notlike "$head*") {
        Write-Host "  version stamp $stamped is not this HEAD ($($head.Substring(0, 8)))" -ForegroundColor Yellow
    }
    $dirty = @(git -C $repoRoot status --porcelain)
    if ($dirty) {
        Write-Host "  NOTE: the working tree has $($dirty.Count) uncommitted change(s), so the" -ForegroundColor Yellow
        Write-Host '        stamped commit names the build BASE, not its contents.' -ForegroundColor Yellow
    }
}


# --------------------------------------------------------------- finish ------

# A RUNNING SERVER STILL HAS THE OLD ASSEMBLY MAPPED. The files on disk are new, every
# check above passes, and the editor keeps answering from the image it loaded when it
# started -- which is the failure this script exists to prevent and the one easiest to
# mistake for "the fix did not work" (user report 2026-08-25: three layout fixes were
# reported as still broken because the preview held the previous image). Kill it so
# the client relaunches from what was just deployed.
$still = Stop-ServerProcesses $extDir.FullName
if ($still) { Write-Host "  stopped the old server ($($still -join ', ')) so the client reloads it" }

$fileCount = (Get-TreeHashes $dest).Count
Write-Host "Deployed $deployedVersion to $($extDir.Name)" -ForegroundColor Green
Write-Host "  $fileCount server files verified byte for byte (swap round $rounds)" -ForegroundColor Green
Write-Host 'Now run "Developer: Reload Window" in VS Code.'
