# Builds grammar.html — the Lily# language manual.
# Shares its stylesheet with the showcase page: the CSS is lifted out of
# build-preview.ps1 so there is only ever one copy of it.
# Run from this directory:  pwsh -File build-manual.ps1
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $here

# The version is read from the repository, never typed: a hand-written one went stale
# (0.5.0 on a 0.8.0 site). build-site.ps1 fails the build if any other version appears.
$props = Get-Content (Join-Path $here '../Directory.Build.props') -Raw -Encoding UTF8
$ver = [regex]::Match($props, '<Version>([^<]+)</Version>').Groups[1].Value
if (-not $ver) { throw 'No <Version> found in Directory.Build.props' }
$preview = Get-Content (Join-Path $here 'build-preview.ps1') -Raw -Encoding UTF8
$m = [regex]::Match($preview, '(?s)<style>\r?\n(.*?)</style>')
if (-not $m.Success) { throw 'No <style> block found in build-preview.ps1' }
$css = $m.Groups[1].Value.TrimEnd()

$extra = @'
  /* manual-only */
  .layout { display:grid; grid-template-columns:minmax(0,1fr); gap:40px; }
  @media (min-width:1000px) { .layout { grid-template-columns:250px minmax(0,1fr); } }
  nav.toc { align-self:start; }
  @media (min-width:1000px) { nav.toc { position:sticky; top:24px; } }
  nav.toc ol { list-style:none; margin:0; padding:0; font-size:14px; }
  nav.toc li { margin:0 0 2px; }
  nav.toc a { display:block; padding:4px 10px; border-radius:6px; text-decoration:none; color:var(--muted); }
  nav.toc a:hover { background:var(--code); color:var(--fg); }
  nav.toc .grp { margin:16px 0 6px; font:12px/1 "SFMono-Regular",Consolas,monospace;
                 letter-spacing:.1em; text-transform:uppercase; color:var(--accent); padding:0 10px; }
  article h2 { margin:0 0 12px; padding-top:8px; }
  article section { padding:34px 0; border-top:1px solid var(--rule); }
  article section:first-child { border-top:none; padding-top:0; }
  article h3 { font-size:18px; margin:26px 0 8px; }
  article p { max-width:64em; }
  article ul { max-width:64em; }
  article li { margin:0 0 7px; }
  .topbar { border-bottom:1px solid var(--rule); padding:14px 0; }
  .topbar .wrap { display:flex; flex-wrap:wrap; gap:18px; align-items:baseline; }
  .topbar a.home { font-weight:700; font-size:18px; text-decoration:none; }
  .topbar span { color:var(--muted); font-size:14px; }
  table.g { margin:14px 0 4px; }
  table.g td:first-child, table.g th:first-child { white-space:nowrap; }
  table.g code { white-space:nowrap; }
  figure.ex { margin:18px 0 22px; }
  figure.ex .paper { padding:12px; }
  figure.ex details { margin-top:10px; }
  .rough { border-left:3px solid var(--accent); padding:2px 0 2px 16px; margin:16px 0; }
  .rough p { margin:0 0 8px; }
  .rough p:last-child { margin:0; }
  .k { font:13px/1.4 "SFMono-Regular",Consolas,monospace; }
'@

# ---------------------------------------------------------------- body
$body = (Get-Content (Join-Path $here 'manual-body.html') -Raw -Encoding UTF8).Replace('{{VER}}', $ver)

# <!--EXAMPLE:name--> becomes the engraved example plus its COMPLETE source, folded
# away. The source is read from the file that was rendered, so the picture and the
# text on the page can never drift apart — build-examples.ps1 compiles every one.
$body = [regex]::Replace($body, '<!--EXAMPLE:([a-z0-9-]+)-->', {
    param($m)
    $name = $m.Groups[1].Value
    $lys = Join-Path $here "examples/$name.lys"
    $svg = "examples/$name.svg"
    if (-not (Test-Path $lys)) { throw "No example named '$name' (looked for $lys)" }
    $src = (Get-Content $lys -Raw -Encoding UTF8).TrimEnd().
        Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;')
    @"
<figure class="ex">
  <div class="paper"><img src="$svg" alt="engraved example"></div>
  <details><summary>Show the whole file</summary><pre>$src</pre></details>
</figure>
"@
})

$html = @"
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Lily# — the language</title>
<meta name="description" content="The Lily# language manual: every construct of the .lys music notation language, with engraved examples.">
<style>
$css
$extra
</style>
</head>
<body>
<div class="topbar"><div class="wrap">
  <a class="home" href="index.html">Lily#</a>
  <span>The language — a reference for <code>.lys</code></span>
</div></div>

<div class="wrap" style="padding-top:40px;padding-bottom:40px">
$body
</div>

<footer><div class="wrap">
  <p style="margin:0" class="note">Written against Lily# $ver, built $(Get-Date -Format 'yyyy-MM-dd').
  The parser is the authority: where this page and the compiler disagree, the compiler is
  right and this page is a bug.</p>
</div></footer>
</body>
</html>
"@

Set-Content -Path (Join-Path $here 'grammar.html') -Value $html -Encoding UTF8
Write-Host "Wrote $(Join-Path $here 'grammar.html')"
