# Releasing Lily#

How a version of Lily# reaches its users. One pushed tag drives everything; the rest of
this file is what to do before pushing it and how to check afterwards that it worked.

This is the product-level procedure. The extension's packaging mechanics — platform
targets, authentication, the local publish script — live in
[`editors/vscode/DEPLOY.md`](../editors/vscode/DEPLOY.md) and are not repeated here.

> Every warning below was paid for. The measurements are from the 0.4.0 release
> (2026-08-23), the first one published to the Marketplace.

---

## What a release is

Pushing a tag `v<x.y.z>` runs [`.github/workflows/release.yml`](../.github/workflows/release.yml),
which does three things in order:

1. **`test`** — the full suite on ubuntu. Everything below is `needs: test`, so a red
   suite publishes nothing.
2. **`cli`** — `lysc` for `win-x64`, `linux-x64`, `osx-x64`, `osx-arm64`, attached to a
   GitHub Release whose body is CHANGELOG.md's topmost section.
3. **`vsix`** — eight platform-specific, self-contained VSIXs, attached to the same
   Release **and published to the VS Code Marketplace**.

The eight `vsix` legs run **one at a time** (`max-parallel: 1`). See
[Why the legs are serialised](#why-the-legs-are-serialised).

## Before you can release at all (once)

* A Marketplace publisher you own — `yotsuda`, confirmed at
  <https://marketplace.visualstudio.com/manage/publishers/yotsuda>.
* A `VSCE_PAT` repository secret: an Azure DevOps PAT with **Marketplace → Manage**,
  belonging to the Microsoft account that owns the publisher.
  `editors/vscode/DEPLOY.md` has the full walkthrough including the three places that
  trip people up.
* ⚠️ **A PAT expires — one year at most.** Nothing warns you. Put its expiry date
  somewhere you will look, or the next release fails at the last step with the version
  already burned.

---

## The procedure

### 1. Decide the number

Semantic-ish, and `0.x` still means the language may change. What forced 0.4.0 rather
than 0.3.1 was four breaking language changes; a release with none of those is a patch.

Whatever you choose, **it can never be reused** — see [Nothing here can be
undone](#nothing-here-can-be-undone).

### 2. Bump the version everywhere

Seven places. Missing one is not caught by the build.

⚠️ **`editors/vscode/package.json` is stored CRLF while every file beside it is LF.**
Editing it with a stream editor (`sed -i`, or anything that reads and rewrites the whole
file with `\n`) produces a **503-line diff for a one-line change** — which reads like a
review's worth of work and is none. Edit it in place, and check `git diff --numstat` is
`1 1` before committing.

| File | What |
|---|---|
| `Directory.Build.props` | `<Version>` — the single source for Core/Cli/Lsp assemblies |
| `editors/vscode/package.json` | `version` |
| `editors/vscode/package-lock.json` | **two** `version` fields |
| `editors/vscode/README.md` | the `**Version x.y.z**` banner |
| `editors/vscode/DEPLOY.md` | the installed-extension path example |
| `CHANGELOG.md` | a new topmost section — this becomes the GitHub Release body |
| `editors/vscode/CHANGELOG.md` | a new topmost section — this is the Marketplace listing's changelog tab |

⚠️ **The lockfile is not optional.** `release.yml` runs `npm ci`, which refuses to
install when `package.json` and `package-lock.json` disagree — and it disagrees about
more than the version. Before 0.4.0 the lockfile still recorded `@types/vscode` and
`engines.vscode` at `^1.85.0` where `package.json` said `^1.90.0`, which would have
failed the extension job on a dependency mismatch having nothing to do with the tag.
Resync with:

```bash
cd editors/vscode && npm install --package-lock-only
```

Then read the diff. It should be the version fields and nothing surprising.

### 3. Write the changelog honestly

The product CHANGELOG's topmost section is served verbatim as the GitHub Release body,
so it is read by people who did not follow the work. Lead with breaking changes and name
the diagnostic that catches each one, so a reader on the previous version can tell
whether their files still compile.

### 4. Verify locally

```powershell
dotnet build LilySharp.slnx --no-incremental -v q     # 0 errors, 0 Core warnings
dotnet test LilySharp.Tests\LilySharp.Tests.csproj -c Debug --no-build
dotnet build LilySharp.Cli -c Release -v q
.\LilySharp.Cli\bin\Release\net10.0\lysc.exe --version   # must print the new number
```

⚠️ **Run the suite even for a docs-only commit.** `HistoryCitationTests` reads `.md`,
`.yml`, `.cs`, `.json`, `.ps1` and `.csv`, so prose moves it. A commit that "cannot break
the build" can still fail the suite, and that is exactly how 0.4.0 went red on CI.

Optionally build all eight VSIXs without publishing, to see the packaging work before
the tag commits you:

```powershell
cd editors\vscode
.\publish-marketplace.ps1          # no -Publish: writes ./dist only
```

### 5. Push, and wait for CI to be green

Do not tag a commit whose CI you have not seen pass. The `test` job in `release.yml`
would catch it, but only after you have burned the tag.

### 6. Tag

```powershell
git tag -a v0.4.0 -m "Lily# 0.4.0 …"
git push origin v0.4.0
```

Annotated, matching `v0.3.0`. The workflow syncs the extension's version from the tag
name, so the tag and `package.json` must already agree.

### 7. Verify that it actually shipped

**This is the step that matters, and CI's colour is not the answer.**

Publishing is the last step of each `vsix` leg, and a leg can upload successfully and
still report failure. During 0.4.0, **five of eight targets were live on the Marketplace
while their jobs showed red**, and three were genuinely missing — including `win32-x64`,
most of the audience. The run's summary could not tell those apart.

Ask the Marketplace instead, target by target:

```powershell
$hdr = @{ 'Accept' = 'application/json;api-version=7.2-preview.1'; 'Content-Type' = 'application/json' }
$body = @{ filters = @(@{ criteria = @(@{ filterType = 7; value = 'yotsuda.lilysharp' });
                          pageNumber = 1; pageSize = 5; sortBy = 0; sortOrder = 0 });
           assetTypes = @(); flags = 2151 } | ConvertTo-Json -Depth 8
$r = Invoke-RestMethod -Uri 'https://marketplace.visualstudio.com/_apis/public/gallery/extensionquery' `
        -Method Post -Headers $hdr -Body $body
$have = @($r.results[0].extensions[0].versions | ForEach-Object { $_.targetPlatform })
$want = 'win32-x64','win32-arm64','linux-x64','linux-arm64','linux-armhf',
        'alpine-x64','darwin-x64','darwin-arm64'
"published: $($have.Count)/8"
$want | Where-Object { $_ -notin $have } | ForEach-Object { "MISSING: $_" }
```

⚠️ **Allow for indexing lag before concluding anything is missing.** The query index
trails publication by minutes, and a later publish re-indexes the whole extension: during
0.4.0 `win32-x64` appeared, vanished from the query while other targets were being
published, and came back — meanwhile VS Code told the maintainer *"not available for the
Windows 64 bit platform"*. `npx @vscode/vsce show yotsuda.lilysharp` reads a different
path and showed all eight throughout. If they disagree, wait and re-check rather than
republishing.

The strongest check is whether the package can actually be fetched:

```powershell
$v = $r.results[0].extensions[0].versions | Where-Object { $_.targetPlatform -eq 'win32-x64' }
$pkg = ($v.files | Where-Object { $_.assetType -eq 'Microsoft.VisualStudio.Services.VSIXPackage' }).source
(Invoke-WebRequest -Uri $pkg -Method Head).StatusCode    # 200
```

Also confirm the GitHub Release: twelve assets (8 VSIX + 4 CLI archives), neither draft
nor pre-release.

```powershell
gh release view v0.4.0 --json isDraft,isPrerelease,assets
```

### 8. If targets are missing

Publish the stragglers by hand, **one at a time**, from a machine logged in with the PAT:

```powershell
cd editors\vscode
dotnet publish ..\..\LilySharp.Lsp -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -o .\server
npx @vscode/vsce publish --target win32-x64
```

The RID → target mapping is in `publish-marketplace.ps1`. Sequential publishes succeed
where the parallel ones failed — that is what identified the cause during 0.4.0.

⚠️ Match `-p:PublishReadyToRun=true` so the hand-published targets start as fast as the
CI-built ones. If a cross-compile fails for a target, dropping it is sanctioned:
`release.yml` says R2R is a startup optimisation, not a correctness one.

---

## Why the legs are serialised

The first publication of an extension is not eight independent operations. Each leg asks
the Marketplace whether the extension exists before choosing create or update; run
concurrently, all eight are told "no", all eight try to **create** it, one wins and seven
die on `The extension already exists.` — an error that reads like a duplicate-version
mistake and is not one.

`max-parallel: 1` means each leg sees what the previous one created. `fail-fast: false`
goes with it: with fail-fast the survivors were cancelled mid-flight, so legs that had
already published and legs that never started both showed "cancelled".

A release now takes about twelve minutes instead of two. That is nothing against
finishing a release by hand while the version burns.

⚠️ **This is fixed but not yet proven.** 0.4.0 ran on the parallel version and was
completed manually; the serialised workflow has never been through a real tag. Watch the
first one, and verify with step 7 rather than the run's colour.

---

## Nothing here can be undone

* **The Marketplace never accepts a version number twice.** Not after an unpublish, not
  after a failure. A release that goes out wrong is fixed by releasing again with a
  higher number.
* **A published extension version cannot be edited** — not its VSIX, not its changelog
  tab. `DEPLOY.md` shipped inside 0.4.0 because `.vscodeignore` did not exclude it, and
  the fix necessarily lands in 0.5.0.
* **A pushed tag is public immediately.** `release.yml` fires on `v*` and starts
  publishing as soon as `test` passes.

So the order matters: the repository should be public and its CI green **before** the
extension is published, or the Marketplace listing's repository link and README badges
are broken for everyone who follows them.

---

## The listing's tags and categories, and why they are not a release-time edit

What the Marketplace page shows under the extension is **tags**, not categories, and only
some of them are yours. Measured from the packaged VSIX's `extension.vsixmanifest`
(2026-08-31, 0.5.0):

| Tag | Where it comes from |
|---|---|
| `music` `notation` `sheet music` `lilypond` `score` `engraving` | `keywords` in `package.json` |
| `keybindings` | derived from `contributes.keybindings` |
| `lilysharp` `Lily` | derived from `contributes.languages[].aliases` = `["Lily#", "lilysharp"]` |
| `__ext_lys` | derived from the `.lys` extension; the web page hides the `__ext_` ones |

* **`Lily` is `Lily#` with the `#` stripped by `vsce`.** It is not a keyword, so it cannot
  be removed by editing `keywords`. Removing it means dropping `"Lily#"` from the language
  aliases — and the first alias is the language's **display name inside VS Code** (the
  status bar, the Change Language Mode picker). That trades a visible name in the product
  for a cosmetic tag on the web page.
* **`Lily#` CAN be a tag**: a `keywords` entry keeps the `#` verbatim through packaging
  (measured). But the page would then show `Lily` *and* `Lily#`, which reads as a duplicate
  rather than a fix. How the web UI renders and links a tag containing `#` — a URL fragment
  delimiter — is **unverified**, and cannot be verified without publishing.
* **`Lily#` cannot be a category.** Categories are VS Code's fixed list (Programming
  Languages, Snippets, Linters, Themes, Debuggers, Formatters, Keymaps, SCM Providers,
  Other, Extension Packs, Language Packs, Data Science, Machine Learning, Visualization,
  Notebooks, Education, Testing…).

⚠️⚠️ **AND `vsce package` DOES NOT CHECK THE CATEGORY.** Packaging with
`"categories": ["Programming Languages", "Lily#"]` succeeded with **no warning at all** and
wrote `Lily#` straight into the manifest's `Categories`. The rejection is server-side, at
publish — which is *after* the tag is pushed and the version burned. So a category edit is
not a thing to try at tag time; it belongs in a release of its own, where the cost of
guessing wrong is a version number rather than this release.

## What is deliberately not automated

* **Deciding the version number.**
* **The changelog.** The workflow serves it; it does not write it.
* **Signing.** Releases are unsigned; see the Known limitations note in the extension
  changelog for the user-facing consequence.
* **`publish-marketplace.ps1`** stays as the manual path. It builds and publishes the
  same eight targets from a workstation, runs `verify-pat` before the first build, and is
  what you want when CI cannot run — as it could not while GitHub Actions was blocked on
  billing. (Actions is free for public repositories on standard runners, which is what
  unblocked it.)
