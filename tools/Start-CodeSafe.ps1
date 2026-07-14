# Safely (re)launches VS Code for Lily# development.
#
# WHY: VS Code keeps its UI state in a SQLite database (state.vscdb) opened in
# rollback-journal mode, where a write takes an exclusive lock on the whole file.
# When the main process dies (e.g. mid-crash) it can leave a helper process still
# holding a connection AND an orphaned "-journal" behind. The next launch then
# collides during journal recovery -> "SQLITE_BUSY: database is locked" -> the main
# process throws an uncaught exception and crashes again. Because each crash re-arms
# the same leftover state, a plain restart re-enters the loop.
#
# This breaks the loop by making the next start genuinely clean:
#   1. Stop EVERY Code process (leftover helpers are what hold the stray lock) and
#      any orphaned lilysharp-lsp.
#   2. Wait until they are really gone and the DB is unlocked.
#   3. Launch a single fresh instance -> SQLite recovers the journal with no rival
#      connection, so it succeeds instead of hitting SQLITE_BUSY.
#
# The Lily# extension itself is not the cause (it registers no webview serializer
# and writes no state.vscdb) -- this is VS Code infrastructure hygiene.
#
# Usage:
#   pwsh tools/Start-CodeSafe.ps1                 # confirm-then-clean-restart on the repo
#   pwsh tools/Start-CodeSafe.ps1 -Force          # no confirmation
#   pwsh tools/Start-CodeSafe.ps1 -ClearJournal   # also remove orphaned -journal (backed up)
#   pwsh tools/Start-CodeSafe.ps1 -ResetState     # nuclear: rebuild state.vscdb
#   pwsh tools/Start-CodeSafe.ps1 -Path other.lys # open something else
#   pwsh tools/Start-CodeSafe.ps1 -NoLaunch       # clean up only, don't start Code

[CmdletBinding()]
param(
    [string]$Path = (Split-Path $PSScriptRoot -Parent),  # default: the Lily# repo root
    [switch]$Force,          # kill a running VS Code without asking
    [switch]$ClearJournal,   # remove orphaned *-journal files (backed up first)
    [switch]$ResetState,     # back up and drop state.vscdb (VS Code rebuilds it)
    [switch]$NoLaunch        # do the cleanup but don't start Code
)

$ErrorActionPreference = 'Stop'

function Step($m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Ok($m)   { Write-Host "    $m" -ForegroundColor Green }
function Note($m) { Write-Host "    $m" -ForegroundColor Yellow }

# The global UI-state DB (the one that goes SQLITE_BUSY) and the shared-storage DB.
$dbPaths = @(
    (Join-Path $env:APPDATA   'Code\User\globalStorage\state.vscdb'),
    (Join-Path $env:USERPROFILE '.vscode-shared\sharedStorage\state.vscdb')
)

# True when nothing else holds the file (an exclusive open succeeds).
function Test-Unlocked([string]$db) {
    if (-not (Test-Path -LiteralPath $db)) { return $true }
    try { $fs = [IO.File]::Open($db, 'Open', 'ReadWrite', 'None'); $fs.Close(); return $true }
    catch { return $false }
}

# --- 1. Stop every Code process (and stray language servers) ---
$code = @(Get-Process Code -ErrorAction SilentlyContinue)
if ($code.Count -gt 0) {
    Step "VS Code is running ($($code.Count) processes)."
    if (-not $Force) {
        $ans = Read-Host "    Close it for a clean restart? UNSAVED CHANGES WILL BE LOST [y/N]"
        if ($ans -notmatch '^(y|yes)$') { Note 'Aborted - nothing was changed.'; return }
    }
    $code | Stop-Process -Force -ErrorAction SilentlyContinue
    Ok "Stopped VS Code."
} else {
    Step "No VS Code running."
}
@(Get-Process lilysharp-lsp -ErrorAction SilentlyContinue) | Stop-Process -Force -ErrorAction SilentlyContinue

# --- 2. Wait for the OS to release the DB handles ---
$deadline = (Get-Date).AddSeconds(10)
do { Start-Sleep -Milliseconds 250 }
while ((Get-Process Code -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline)

if (Get-Process Code -ErrorAction SilentlyContinue) {
    Note 'Some Code processes survived the kill; the lock may not be fully cleared.'
} else {
    Ok 'All Code processes are gone.'
}

# --- 3. Report journal state (and optionally remove it) ---
Step 'Checking state databases...'
foreach ($db in $dbPaths) {
    $journal = "$db-journal"
    if (Test-Path -LiteralPath $journal) {
        if ($ClearJournal) {
            if (Test-Unlocked $db) {
                $bak = "$journal.orphan-$(Get-Date -Format yyyyMMdd-HHmmss)"
                Move-Item -LiteralPath $journal -Destination $bak -Force
                Ok "Removed orphaned $(Split-Path $journal -Leaf) (backup: $(Split-Path $bak -Leaf))."
            } else {
                Note "$db still locked - left its journal in place. Close all Code windows and retry."
            }
        } else {
            Note "Orphaned $(Split-Path $journal -Leaf) present. A clean single start recovers it automatically; pass -ClearJournal to remove it if the crash persists."
        }
    }
}

# --- 4. Optional hard reset of BOTH state DBs ---
# Recurring SQLITE_BUSY here is Settings Sync's "UI State" cycle writing state.vscdb
# at the same time as the main process, on the SAME file because VS Code fell back to
# using globalStorage as the application store. Rebuilding both DBs can clear that
# fallback so the two writers land on separate files again. If crashes persist, turn
# Settings Sync OFF (Command Palette -> "Settings Sync: Turn Off") to remove the rival
# writer entirely -- that is the guaranteed fix.
if ($ResetState) {
    foreach ($db in $dbPaths) {
        if ((Test-Path -LiteralPath $db) -and (Test-Unlocked $db)) {
            $bak = "$db.reset-$(Get-Date -Format yyyyMMdd-HHmmss)"
            Move-Item -LiteralPath $db -Destination $bak -Force
            Ok "Reset $(Split-Path (Split-Path $db -Parent) -Leaf)\state.vscdb (backup: $(Split-Path $bak -Leaf)). VS Code rebuilds it; settings and extensions are untouched."
        } elseif (Test-Path -LiteralPath $db) {
            Note "$db still locked - not reset."
        }
    }
}

# --- 5. Launch a single fresh instance ---
if ($NoLaunch) { Ok 'Cleanup complete (-NoLaunch); VS Code not started.'; return }

$launcher = (Get-Command code -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1).Source
if (-not $launcher) {
    $launcher = @(
        (Join-Path $env:ProgramFiles 'Microsoft VS Code\Code.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Microsoft VS Code\Code.exe')
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $launcher) { throw "Could not find 'code' on PATH or Code.exe. Pass -Path and start VS Code manually." }

Step "Launching VS Code on: $Path"
Start-Process -FilePath $launcher -ArgumentList $Path
Ok 'Started. Open the preview once the window has settled.'
