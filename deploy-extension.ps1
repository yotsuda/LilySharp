# LilySharp VS Code Extension Deployment Script
# Usage: .\deploy-extension.ps1 [-Release] [-SkipVSCodeSettings]
#
# Development: 0.1.1-dev.1 → 0.1.1-dev.2 → ...
# Release:     0.1.1 → 0.1.2 → ...

param(
    [switch]$Release,
    [switch]$SkipVSCodeSettings
)

$ErrorActionPreference = "Stop"
$config = if ($Release) { "Release" } else { "Debug" }
$projectRoot = $PSScriptRoot

Write-Host "=== LilySharp Extension Deployment ===" -ForegroundColor Cyan
Write-Host "Configuration: $config" -ForegroundColor Yellow

# Detect target framework from csproj
$csprojPath = Join-Path $projectRoot "LilySharp.Lsp/LilySharp.Lsp.csproj"
$csprojContent = Get-Content $csprojPath -Raw
if ($csprojContent -match '<TargetFramework>([^<]+)</TargetFramework>') {
    $targetFramework = $Matches[1]
    Write-Host "Target Framework: $targetFramework" -ForegroundColor Yellow
} else {
    throw "Could not detect target framework from csproj"
}

# Step 0: Kill LSP process to release file locks
Write-Host "`n[0/8] Stopping LSP server..." -ForegroundColor Green
$lspProcesses = Get-CimInstance Win32_Process -Filter "Name = 'lilysharp-lsp.exe'"
if ($lspProcesses) {
    $lspProcesses | ForEach-Object { Stop-Process -Id # LilySharp VS Code Extension Deployment Script
# Usage: .\deploy-extension.ps1 [-Release] [-SkipVSCodeSettings]
#
# Development: 0.1.1-dev.1 → 0.1.1-dev.2 → ...
# Release:     0.1.1 → 0.1.2 → ...

param(
    [switch]$Release,
    [switch]$SkipVSCodeSettings
)

$ErrorActionPreference = "Stop"
$config = if ($Release) { "Release" } else { "Debug" }
$projectRoot = $PSScriptRoot

Write-Host "=== LilySharp Extension Deployment ===" -ForegroundColor Cyan
Write-Host "Configuration: $config" -ForegroundColor Yellow

# Detect target framework from csproj
$csprojPath = Join-Path $projectRoot "LilySharp.Lsp/LilySharp.Lsp.csproj"
$csprojContent = Get-Content $csprojPath -Raw
if ($csprojContent -match '<TargetFramework>([^<]+)</TargetFramework>') {
    $targetFramework = $Matches[1]
    Write-Host "Target Framework: $targetFramework" -ForegroundColor Yellow
} else {
    throw "Could not detect target framework from csproj"
}

# Step 0: Kill LSP process to release file locks
Write-Host "`n[0/8] Stopping LSP server..." -ForegroundColor Green
$oldEAP = $ErrorActionPreference
$ErrorActionPreference = "SilentlyContinue"
$lspProcesses = Get-Process -Name "lilysharp-lsp" 2>$null
$ErrorActionPreference = $oldEAP
if ($lspProcesses) {
    $lspProcesses | Stop-Process -Force
    Start-Sleep -Seconds 1
    Write-Host "LSP server stopped" -ForegroundColor Yellow
} else {
    Write-Host "LSP server not running" -ForegroundColor Gray
}

# Step 1: Update version
Write-Host "`n[1/8] Updating version..." -ForegroundColor Green
$packageJsonPath = Join-Path $projectRoot "editors/vscode/package.json"
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

# Step 2: Clean old VSIX files (keep only latest 2)
Write-Host "`n[2/8] Cleaning old VSIX files..." -ForegroundColor Green
$vsixDir = Join-Path $projectRoot "editors/vscode"
$oldVsix = Get-ChildItem $vsixDir -Filter "*.vsix" | Sort-Object LastWriteTime -Descending | Select-Object -Skip 2
if ($oldVsix) {
    $oldVsix | ForEach-Object {
        Remove-Item $_.FullName -Force
        Write-Host "Removed: $($_.Name)" -ForegroundColor Gray
    }
} else {
    Write-Host "No old VSIX files to clean" -ForegroundColor Gray
}

# Step 3: Build LSP
Write-Host "`n[3/8] Building LSP server ($config)..." -ForegroundColor Green
Push-Location $projectRoot
dotnet build LilySharp.Lsp -c $config
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "LSP build failed" }
Pop-Location

# Step 4: Compile TypeScript
Write-Host "`n[4/8] Compiling TypeScript..." -ForegroundColor Green
Push-Location (Join-Path $projectRoot "editors/vscode")
npm run compile
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "TypeScript compile failed" }

# Step 5: Build VSIX
Write-Host "`n[5/8] Building VSIX package..." -ForegroundColor Green
npx @vscode/vsce package --allow-missing-repository --pre-release
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "VSIX build failed" }
$vsix = Get-ChildItem *.vsix | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Pop-Location

# Step 6: Update VS Code settings
Write-Host "`n[6/8] Updating VS Code settings..." -ForegroundColor Green
if (-not $SkipVSCodeSettings) {
    $settingsPath = "$env:APPDATA\Code\User\settings.json"
    $lspPath = Join-Path $projectRoot "LilySharp.Lsp/bin/$config/$targetFramework/lilysharp-lsp.exe"
    
    if (Test-Path $settingsPath) {
        $settingsContent = Get-Content $settingsPath -Raw
        $settings = $settingsContent | ConvertFrom-Json
        
        $currentServerPath = $settings.'lilysharp.serverPath'
        if ($currentServerPath -ne $lspPath) {
            $settings | Add-Member -NotePropertyName 'lilysharp.serverPath' -NotePropertyValue $lspPath -Force
            $settings | ConvertTo-Json -Depth 10 | Set-Content $settingsPath -Encoding UTF8
            Write-Host "Updated serverPath: $lspPath" -ForegroundColor Yellow
        } else {
            Write-Host "serverPath already correct" -ForegroundColor Gray
        }
    } else {
        Write-Host "VS Code settings.json not found, skipping" -ForegroundColor Yellow
    }
} else {
    Write-Host "Skipped (--SkipVSCodeSettings)" -ForegroundColor Gray
}

# Step 7: Check VS Code
Write-Host "`n[7/8] Checking VS Code..." -ForegroundColor Green
$vscodeProcesses = Get-CimInstance Win32_Process -Filter "Name = 'Code.exe'"
if ($vscodeProcesses) {
    Write-Host "VS Code is running. Please close it before installing." -ForegroundColor Yellow
}

# Step 8: Uninstall old and install new extension
Write-Host "`n[8/8] Installing extension..." -ForegroundColor Green
code --uninstall-extension lilysharp.lilysharp 2>$null
Start-Sleep -Seconds 1

$vsixPath = Join-Path $projectRoot "editors/vscode/$($vsix.Name)"
code --install-extension $vsixPath
if ($LASTEXITCODE -ne 0) { throw "Extension install failed" }

Write-Host "`n=== Deployment Complete ===" -ForegroundColor Cyan
Write-Host "Version: $newVersion" -ForegroundColor Yellow
Write-Host "Installed: $($vsix.Name)" -ForegroundColor Yellow
Write-Host "LSP Server: LilySharp.Lsp/bin/$config/$targetFramework/lilysharp-lsp.exe" -ForegroundColor Yellow
Write-Host "`nIMPORTANT: Restart VS Code completely to apply changes!" -ForegroundColor Red


.ProcessId -Force }
    Start-Sleep -Seconds 1
    Write-Host "LSP server stopped" -ForegroundColor Yellow
} else {
    Write-Host "LSP server not running" -ForegroundColor Gray
}

# Step 1: Update version
Write-Host "`n[1/8] Updating version..." -ForegroundColor Green
$packageJsonPath = Join-Path $projectRoot "editors/vscode/package.json"
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

# Step 2: Clean old VSIX files (keep only latest 2)
Write-Host "`n[2/8] Cleaning old VSIX files..." -ForegroundColor Green
$vsixDir = Join-Path $projectRoot "editors/vscode"
$oldVsix = Get-ChildItem $vsixDir -Filter "*.vsix" | Sort-Object LastWriteTime -Descending | Select-Object -Skip 2
if ($oldVsix) {
    $oldVsix | ForEach-Object {
        Remove-Item $_.FullName -Force
        Write-Host "Removed: $($_.Name)" -ForegroundColor Gray
    }
} else {
    Write-Host "No old VSIX files to clean" -ForegroundColor Gray
}

# Step 3: Build LSP
Write-Host "`n[3/8] Building LSP server ($config)..." -ForegroundColor Green
Push-Location $projectRoot
dotnet build LilySharp.Lsp -c $config
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "LSP build failed" }
Pop-Location

# Step 4: Compile TypeScript
Write-Host "`n[4/8] Compiling TypeScript..." -ForegroundColor Green
Push-Location (Join-Path $projectRoot "editors/vscode")
npm run compile
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "TypeScript compile failed" }

# Step 5: Build VSIX
Write-Host "`n[5/8] Building VSIX package..." -ForegroundColor Green
npx @vscode/vsce package --allow-missing-repository --pre-release
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "VSIX build failed" }
$vsix = Get-ChildItem *.vsix | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Pop-Location

# Step 6: Update VS Code settings
Write-Host "`n[6/8] Updating VS Code settings..." -ForegroundColor Green
if (-not $SkipVSCodeSettings) {
    $settingsPath = "$env:APPDATA\Code\User\settings.json"
    $lspPath = Join-Path $projectRoot "LilySharp.Lsp/bin/$config/$targetFramework/lilysharp-lsp.exe"
    
    if (Test-Path $settingsPath) {
        $settingsContent = Get-Content $settingsPath -Raw
        $settings = $settingsContent | ConvertFrom-Json
        
        $currentServerPath = $settings.'lilysharp.serverPath'
        if ($currentServerPath -ne $lspPath) {
            $settings | Add-Member -NotePropertyName 'lilysharp.serverPath' -NotePropertyValue $lspPath -Force
            $settings | ConvertTo-Json -Depth 10 | Set-Content $settingsPath -Encoding UTF8
            Write-Host "Updated serverPath: $lspPath" -ForegroundColor Yellow
        } else {
            Write-Host "serverPath already correct" -ForegroundColor Gray
        }
    } else {
        Write-Host "VS Code settings.json not found, skipping" -ForegroundColor Yellow
    }
} else {
    Write-Host "Skipped (--SkipVSCodeSettings)" -ForegroundColor Gray
}

# Step 7: Check VS Code
Write-Host "`n[7/8] Checking VS Code..." -ForegroundColor Green
$vscodeProcesses = Get-CimInstance Win32_Process -Filter "Name = 'Code.exe'"
if ($vscodeProcesses) {
    Write-Host "VS Code is running. Please close it before installing." -ForegroundColor Yellow
}

# Step 8: Uninstall old and install new extension
Write-Host "`n[8/8] Installing extension..." -ForegroundColor Green
code --uninstall-extension lilysharp.lilysharp 2>$null
Start-Sleep -Seconds 1

$vsixPath = Join-Path $projectRoot "editors/vscode/$($vsix.Name)"
code --install-extension $vsixPath
if ($LASTEXITCODE -ne 0) { throw "Extension install failed" }

Write-Host "`n=== Deployment Complete ===" -ForegroundColor Cyan
Write-Host "Version: $newVersion" -ForegroundColor Yellow
Write-Host "Installed: $($vsix.Name)" -ForegroundColor Yellow
Write-Host "LSP Server: LilySharp.Lsp/bin/$config/$targetFramework/lilysharp-lsp.exe" -ForegroundColor Yellow
Write-Host "`nIMPORTANT: Restart VS Code completely to apply changes!" -ForegroundColor Red



