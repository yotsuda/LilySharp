# LilySharp VS Code Extension Deployment

## Directory Structure

```
editors/vscode/
├── .vscode/          # Development settings (excluded from package)
│   ├── launch.json   # Debug configuration
│   ├── settings.json # Dev settings (no hardcoded paths!)
│   └── tasks.json    # Build tasks
├── media/fonts/      # Emmentaler font
├── out/              # Compiled JavaScript
├── server/           # LSP server (auto-generated, excluded from git)
├── src/
│   └── extension.ts  # Extension source
├── syntaxes/         # TextMate grammar
├── .gitignore
├── .vscodeignore     # Files excluded from .vsix package
├── DEPLOY.md         # This file
├── package.json
└── tsconfig.json
```

## Server Path Resolution

The extension resolves the LSP server in this order:

1. **User-configured path** (`lilysharp.serverPath` in VS Code settings)
2. **Bundled server** (`<extension>/server/lilysharp-lsp.exe`)
3. **PATH** (fallback to `lilysharp-lsp` command)

## Development Workflow

### Prerequisites

- Node.js and npm
- .NET SDK (for building LSP server)
- PowerShell (for build scripts)

### Build Commands

```bash
cd editors/vscode

# Compile TypeScript only
npm run compile

# Deploy LSP server to ./server/
npm run deploy-server

# Both (same as vscode:prepublish)
npm run vscode:prepublish
```

### Local Testing

After building, update the installed extension:

```powershell
cd C:\MyProj\LilySharp\editors\vscode

# Build everything
npm run vscode:prepublish

# Copy to installed extension
$ext = "$env:USERPROFILE\.vscode\extensions\yotsuda.lilysharp-0.4.0"
Copy-Item "server" $ext -Recurse -Force
Copy-Item "out\extension.js" "$ext\out\" -Force
Copy-Item "out\extension.js.map" "$ext\out\" -Force

# Restart VS Code (Ctrl+Shift+P -> "Developer: Reload Window")
```

### Quick Development Cycle

For rapid iteration on LSP changes only:

```powershell
# Build LSP (Debug) - faster than Release
dotnet build LilySharp.Lsp

# Reload VS Code
```

Note: This requires `lilysharp.serverPath` to point to Debug build.

## Publishing to Marketplace

The Marketplace listing is `yotsuda.lilysharp`. It is published as **platform-specific,
self-contained** VSIXs (one per VS Code target, each bundling its own .NET runtime),
so users need nothing but VS Code. `publish-marketplace.ps1` does all of this.

### Prerequisites (once per machine)

Whichever you pick, **sign in with the Microsoft account that OWNS the publisher** —
the one that shows the publisher at
https://marketplace.visualstudio.com/manage/publishers/yotsuda. A different account of
your own is the failure mode here, and it looks like a permissions error at upload
time.

⚠️ **MEASURED 2026-08-23: option A DOES NOT WORK for these publishers, and the reason is
not obvious.** `az login` succeeds as `ytsuda@gmail.com`, but a personal Microsoft
account signs Azure CLI in against an auto-created "Default Directory" tenant, and the
Entra user there is a DIFFERENT PRINCIPAL from the Microsoft account that owns the
publisher. vsce gets a valid token and the Marketplace then refuses it:

```
Access Denied: <entra-object-id> needs the following permission(s) on the resource
/yotsuda to perform this action: View user permissions on a resource
```

⚠️ The object id is elided on purpose and should not be pasted back in. A GUID's first
group is eight hex characters, which is exactly the shape of an abbreviated commit, so
`HistoryCitationTests.DeadCitationsDoNotGrow` counts it as a citation that resolves to
nothing and goes red. It caught this paragraph on CI.

The same identity is refused on `/ytsuda` too, which is what rules out "wrong publisher"
and leaves "wrong kind of identity". So **take option B** unless the publisher is one day
owned by a work/school (Entra) account — the `-AzureCredential` switch is kept for that
day, and for anyone forking this who publishes under an Entra account.

**Option A — Microsoft Entra ID (only for Entra-owned publishers; see the warning above):**

```powershell
winget install --id Microsoft.AzureCLI -e   # then open a NEW terminal: PATH is stale
az login                                    # add --allow-no-subscriptions if it
                                            # complains; a Marketplace publisher does
                                            # not imply an Azure subscription
npx @vscode/vsce verify-pat --azure-credential yotsuda
```

Then pass `-AzureCredential` to the publish script. There is no Azure DevOps
organization to create and nothing that expires.

**Option B — Personal Access Token (the working path for this publisher):**

0. Sign in everywhere as the SAME Microsoft account that owns the publisher —
   `ytsuda@gmail.com` here, the one that shows the publisher at
   /manage/publishers/yotsuda. This is the step option A gets wrong for you.
1. A PAT for the `yotsuda` publisher:
   - https://dev.azure.com/ -> User settings -> Personal access tokens -> New Token
   - Organization: **All accessible organizations**, Scopes: **Marketplace -> Manage**
   - ⚠️ `Marketplace` is hidden until you click **Show all scopes** at the bottom.
   - ⚠️ Signing in at `dev.azure.com` with no organization redirects to the Azure
     portal (portal.azure.com), which is a different product and has no PATs. A PAT
     lives under an organization, so create one first at https://aex.dev.azure.com/me
     — it is free and its name has nothing to do with the publisher id.
   - ⚠️ A PAT lasts a year at most, so this path breaks on a schedule nobody watches.
     Put the expiry date in the release checklist rather than discovering it at the
     next release.
2. Store it for vsce (kept in `~/.vsce`, never in the repo):
   ```bash
   cd editors/vscode
   npx @vscode/vsce login yotsuda
   ```
   (or export it as `VSCE_PAT` for the session instead)

### Publish Steps

```powershell
cd editors/vscode

# 1. Version must already match Directory.Build.props / CHANGELOG (the tag)

# 2. Dry run: build all platform VSIXs into ./dist and install one locally
./publish-marketplace.ps1
code --install-extension dist/lilysharp-win32-x64.vsix

# 3. Publish every target — add -AzureCredential if you took option A
./publish-marketplace.ps1 -Publish
```

The script runs `verify-pat` once before the first build: eight ~50 MB targets each
cross-publish a runtime before uploading, so an identity that cannot publish should
be caught in one request rather than eight builds.

`npm run package` / `npm run publish` build a single *universal* VSIX from whatever
is in `./server`; they are for local experiments, not for the Marketplace.

## Important Notes

### Avoid Hardcoded Paths

**Never commit hardcoded local paths** in:
- `.vscode/settings.json` - Use empty `serverPath` or remove it
- `.vscode/launch.json` - Use `${workspaceFolder}` variables
- `samples/.vscode/` - Should not exist

These cause issues when others clone the repository or when publishing.

### Files Excluded from Package

The `.vscodeignore` file excludes:
- `.vscode/**` - Development settings
- `src/**`, `*.ts` - TypeScript source
- `node_modules/**` - Dependencies (bundled separately)
- `*.vsix` - Previous packages

The `server/` directory IS included in the package.

### Checking for Stale Settings

If the extension uses an unexpected server path, check:

```powershell
# Global settings
Get-Content "$env:APPDATA\Code\User\settings.json" | Select-String "lilysharp"

# Workspace settings (in any .vscode folder you have open)
Get-ChildItem -Recurse -Filter "settings.json" | 
    ForEach-Object { Get-Content $_.FullName | Select-String "lilysharp" }
```

### Verifying Bundled Server

Check the Output panel ("Lily# Extension") for:
```
Using bundled server: <path>\server\lilysharp-lsp.exe
```

If you see "Using configured path:" instead, a settings file is overriding the bundled server.
