# Builds the whole site — examples, gallery pictures, the manual and the showcase page — and
# then CHECKS what it built. Any failure stops the build and lists every problem, so a page
# that would go out stale or broken never gets written as if it were fine.
# Run from anywhere:  pwsh -File build-site.ps1
#
# What the check refuses (each of these once reached a draft of this site):
#   - a .lys the site shows (an example, a gallery score, a complete example on a page) that
#     `lysc check` does not pass;
#   - an <img>, <video> poster or <source> whose file is missing;
#   - a "Lily# N.N.N" other than the repository's version (Directory.Build.props);
#   - a leftover template marker ({{…}}, <!--EXAMPLE:…-->) or local-preview note.
#
# What passes is copied to _site/ (the pages, the pictures they use, the hero shot or clip)
# — the folder .github/workflows/pages.yml publishes. Nothing else in site/ goes out.
param([string]$Lysc = (Join-Path $PSScriptRoot ('../LilySharp.Cli/bin/Release/net10.0/' + ($IsWindows ? 'lysc.exe' : 'lysc'))))
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
Set-Location $here
if (-not (Test-Path $Lysc)) { throw "lysc not found at $Lysc - build LilySharp.Cli (Release) first" }

$props = Get-Content (Join-Path $here '../Directory.Build.props') -Raw -Encoding UTF8
$ver = [regex]::Match($props, '<Version>([^<]+)</Version>').Groups[1].Value
if (-not $ver) { throw 'No <Version> found in Directory.Build.props' }

function Test-Lys([string]$path) {
    $out = & $Lysc check $path 2>&1 | Out-String
    if ($out -match 'No errors found') { return $null }
    # A warning is refused like an error — a site example should be clean — except the one
    # that is about the MACHINE, not the source: a `fonts` example naming a face the build
    # host lacks (Georgia on the Pages runner). The page shows the source, not that render.
    $lines = $out -split "`r?`n" | Where-Object { $_ -match ': (error|warning):' }
    $real = @($lines | Where-Object { $_ -notmatch 'is not installed on this system' })
    if ($lines -and -not $real -and $LASTEXITCODE -eq 0) { return $null }
    return (($out -split "`r?`n" | Where-Object { $_ -match '\S' }) | Select-Object -First 3) -join ' / '
}

# ------------------------------------------------------------------ 1. examples
& (Join-Path $here 'build-examples.ps1') -Lysc $Lysc

# ------------------------------------------------------------------ 2. gallery + figures
$pictures = 'ode-to-joy', 'rising-sun', 'blues-in-f', 'sketch-in-c', 'air-in-d', 'petite-valse', 'chord-axes'
$failed = @()
foreach ($name in $pictures) {
    $lys = Join-Path $here "$name.lys"
    $problem = Test-Lys $lys
    if ($problem) { $failed += "$name.lys: $problem"; continue }
    & $Lysc svg $lys (Join-Path $here "$name.svg") | Out-Null
    Write-Host "ok  $name"
}
if ($failed) { throw "scores that do not compile:`n  $($failed -join "`n  ")" }

# ------------------------------------------------------------------ 3. pages
& (Join-Path $here 'build-manual.ps1')
& (Join-Path $here 'build-preview.ps1')

# ------------------------------------------------------------------ 4. check the pages
Add-Type -AssemblyName System.Web
$problems = [System.Collections.Generic.List[string]]::new()
$published = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$tmp = Join-Path ([IO.Path]::GetTempPath()) 'lilysharp-site-check'
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
foreach ($page in 'index.html', 'grammar.html') {
    $html = Get-Content (Join-Path $here $page) -Raw -Encoding UTF8
    function LineOf([int]$index) { ($html.Substring(0, $index) -split "`n").Count }

    foreach ($m in [regex]::Matches($html, '<(?:img|source|video)[^>]+?\b(?:src|poster)="([^"]+)"')) {
        $src = $m.Groups[1].Value
        if ($src -match '^[a-z]+:') { continue }
        if (-not (Test-Path (Join-Path $here $src))) { $problems.Add("${page}:$(LineOf $m.Index): missing file $src") }
        else { $published.Add($src) | Out-Null }
    }
    foreach ($m in [regex]::Matches($html, 'Lily# (\d+\.\d+\.\d+)')) {
        if ($m.Groups[1].Value -ne $ver) { $problems.Add("${page}:$(LineOf $m.Index): says Lily# $($m.Groups[1].Value), the repository is $ver") }
    }
    foreach ($pattern in '\{\{[A-Z_]+\}\}', '<!--EXAMPLE:', '(?i)local preview', '(?i)\bTODO\b') {
        foreach ($m in [regex]::Matches($html, $pattern)) { $problems.Add("${page}:$(LineOf $m.Index): leftover '$($m.Value)'") }
    }
    # A <pre> that is a whole document (it has a form and a score) is compiled as shown.
    $i = 0
    foreach ($m in [regex]::Matches($html, '(?s)<pre(?![^>]*mermaid)[^>]*>(.*?)</pre>')) {
        $text = [System.Web.HttpUtility]::HtmlDecode(($m.Groups[1].Value -replace '<[^>]+>', ''))
        if ($text -notmatch '(?m)^\s*score\b' -or $text -notmatch '(?m)^\s*form\b') { continue }
        $i++
        $file = Join-Path $tmp "$($page -replace '\W', '-')-$i.lys"
        [IO.File]::WriteAllText($file, $text)
        $problem = Test-Lys $file
        if ($problem) { $problems.Add("${page}:$(LineOf $m.Index): the example does not compile: $problem") }
    }
    Write-Host "checked $page ($i complete example(s) compiled)"
}
if ($problems.Count) {
    throw "the site is not ready ($($problems.Count) problem(s)):`n  $($problems -join "`n  ")"
}

# ------------------------------------------------------------------ 5. assemble _site/
# Only the two pages and the files they reference: a picture the pages stopped using does
# not linger on the published site.
$out = Join-Path $here '_site'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out | Out-Null
foreach ($f in @('index.html', 'grammar.html') + @($published)) {
    $target = Join-Path $out $f
    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    Copy-Item (Join-Path $here $f) $target
}
New-Item -ItemType File -Path (Join-Path $out '.nojekyll') | Out-Null   # serve as is, no Jekyll pass
Write-Host "Site built and checked: Lily# $ver -> _site/ ($($published.Count + 2) files)" -ForegroundColor Green
