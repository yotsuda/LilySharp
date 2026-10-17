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
    & $Lysc svg $lys | Out-Null   # -> $here/$name.svg (lysc names its output for the book)
    Write-Host "ok  $name"
}
if ($failed) { throw "scores that do not compile:`n  $($failed -join "`n  ")" }

# ------------------------------------------------------------------ 2b. the lessons
# The learn pages show each lesson as it opens in the editor (build-manual.ps1). A lesson is a
# score with its tasks still to do — a short bar, a @todo — so its warnings are expected
# (LessonTests holds each one to what it teaches); an ERROR is refused.
$learnDir = Join-Path $here 'learn'
New-Item -ItemType Directory -Force -Path $learnDir | Out-Null
Get-ChildItem $learnDir -Filter *.svg | Remove-Item
foreach ($lesson in Get-ChildItem (Join-Path $here '../LilySharp.Lsp/Lessons/en') -Filter 'lesson-*.lys' | Sort-Object Name) {
    $out = & $Lysc check $lesson.FullName 2>&1 | Out-String
    if ($out -match ': error:') { $failed += "$($lesson.Name): $((($out -split "`r?`n") -match ': error:' | Select-Object -First 2) -join ' / ')"; continue }
    & $Lysc svg --todo $lesson.FullName -d $learnDir 2>&1 | Out-Null   # the tasks' red rings, as the editor shows them; its warnings are the tasks
    Write-Host "ok  $($lesson.BaseName)"
}
if ($failed) { throw "lessons that do not compile:`n  $($failed -join "`n  ")" }

# ------------------------------------------------------------------ 3. pages
& (Join-Path $here 'build-manual.ps1')
& (Join-Path $here 'build-preview.ps1')

# ------------------------------------------------------------------ 4. check the pages
Add-Type -AssemblyName System.Web
$problems = [System.Collections.Generic.List[string]]::new()
$published = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$tmp = Join-Path ([IO.Path]::GetTempPath()) 'lilysharp-site-check'
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
$pages = @('index.html', 'start.html', 'learn.html', 'grammar.html', 'ref-music.html', 'ref-structure.html',
    'ref-output.html', 'ref-lookup.html', 'chords.html', 'editor.html', 'commands.html', 'scan.html', 'about.html') +
    @(Get-ChildItem (Join-Path $here '../LilySharp.Lsp/Lessons/en') -Filter 'lesson-*.lys' | Sort-Object Name | ForEach-Object { "$($_.BaseName).html" })
foreach ($page in $pages) {
    $html = Get-Content (Join-Path $here $page) -Raw -Encoding UTF8
    function LineOf([int]$index) { ($html.Substring(0, $index) -split "`n").Count }

    # A demo clip still marked data-pending is hidden on the page and its file is not
    # expected yet: blank it out of the media scan (same length, so line numbers hold).
    $media = [regex]::Replace($html, '(?s)<figure class="demo-video" data-pending>.*?</figure>',
        { param($m) $m.Value -replace '[^\n]', ' ' })
    foreach ($m in [regex]::Matches($media, '<(?:img|source|video)[^>]+?\b(?:src|poster)="([^"]+)"')) {
        $src = $m.Groups[1].Value
        if ($src -match '^[a-z]+:') { continue }
        if (-not (Test-Path (Join-Path $here $src))) { $problems.Add("${page}:$(LineOf $m.Index): missing file $src") }
        else { $published.Add($src) | Out-Null }
    }
    # A link to another page of the site names one this build writes (the learn pages are
    # generated, and a lesson renamed would otherwise leave a link to nothing).
    foreach ($m in [regex]::Matches($html, '<a[^>]+href="([a-z0-9-]+\.html)(?:#[^"]*)?"')) {
        if ($pages -notcontains $m.Groups[1].Value) { $problems.Add("${page}:$(LineOf $m.Index): link to a page the site does not have: $($m.Groups[1].Value)") }
    }
    # ...and a link to a place on a page names an id that page has. The reference was split
    # into pages by its chapters (2026-10-12); a link rewritten to the wrong page, or a
    # chapter renamed, would land on the top of a page instead of the place.
    foreach ($m in [regex]::Matches($html, '<a[^>]+href="([a-z0-9-]+\.html)?#([^"]+)"')) {
        $target = $m.Groups[1].Value ? $m.Groups[1].Value : $page
        if ($pages -notcontains $target) { continue }
        $targetHtml = $target -eq $page ? $html : (Get-Content (Join-Path $here $target) -Raw -Encoding UTF8)
        if ($targetHtml -notmatch ('id="' + [regex]::Escape($m.Groups[2].Value) + '"')) {
            $problems.Add("${page}:$(LineOf $m.Index): link to #$($m.Groups[2].Value), which $target does not have")
        }
    }
    foreach ($m in [regex]::Matches($html, 'Lily# (\d+\.\d+\.\d+)')) {
        if ($m.Groups[1].Value -ne $ver) { $problems.Add("${page}:$(LineOf $m.Index): says Lily# $($m.Groups[1].Value), the repository is $ver") }
    }
    # The language's own todo is not a writer's leftover: the @todo mark, its section's
    # anchor (#todo), the CLI's --todo-as-error / --no-todo and the quick fix "Resolve this
    # TODO" are blanked out of
    # the leftover scan (same length, so line numbers hold).
    $prose = [regex]::Replace($html, '(?i)[@#-]todo\b|id="todo"|Resolve this TODO',
        { param($m) ' ' * $m.Value.Length })
    foreach ($pattern in '\{\{[A-Z_]+\}\}', '<!--EXAMPLE:', '(?i)local preview', '(?i)\bTODO\b') {
        foreach ($m in [regex]::Matches($prose, $pattern)) { $problems.Add("${page}:$(LineOf $m.Index): leftover '$($m.Value)'") }
    }
    # A <pre> that is a whole document (it has a form and a score) is compiled as shown.
    # `<pre data-fragment>` opts out: a listing that is not a .lys file as it stands (a
    # Markdown fence wrapped around one); so does `<pre data-lesson>`, a lesson with its tasks
    # still to do, checked for errors (only) in step 2b.
    $i = 0
    foreach ($m in [regex]::Matches($html, '(?s)<pre(?![^>]*(?:mermaid|data-fragment|data-lesson))[^>]*>(.*?)</pre>')) {
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
# Only the pages and the files they reference: a picture the pages stopped using does not
# linger on the published site (nor does videos/SHOTLIST.md, or a clip still pending).
$out = Join-Path $here '_site'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out | Out-Null
foreach ($f in @($pages) + @($published)) {
    $target = Join-Path $out $f
    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    Copy-Item (Join-Path $here $f) $target
}
# The favicon every page names (site-common.ps1) is the extension's icon: one picture, kept there.
Copy-Item (Join-Path $here '../editors/vscode/icon.png') (Join-Path $out 'favicon.png')
New-Item -ItemType File -Path (Join-Path $out '.nojekyll') | Out-Null   # serve as is, no Jekyll pass
Write-Host "Site built and checked: Lily# $ver -> _site/ ($($published.Count + $pages.Count) files)" -ForegroundColor Green
