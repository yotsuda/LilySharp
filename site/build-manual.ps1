# Builds the site's reading pages, each from its *-body.html:
#   start.html    <- start-body.html    Get started: install, a first score, where next
#   grammar.html, ref-music.html, ref-structure.html, ref-output.html, ref-lookup.html
#                 <- manual-body.html   the language reference, split by its contents' groups
#   chords.html   <- chords-body.html   chords and chord diagrams, step by step (under Reference)
#   editor.html   <- editor-body.html   the VS Code extension and its AI features
#   commands.html <- commands-body.html every editor command, one by one (under Editor)
#   about.html    <- about-body.html    lineage, licence, speed, architecture
# and the learn pages from the lessons themselves.
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
. (Join-Path $here 'site-common.ps1')

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
  nav.toc summary { cursor:pointer; font-weight:600; padding:8px 10px; border:1px solid var(--rule);
                    border-radius:8px; }
  @media (min-width:1000px) { nav.toc summary { display:none; } }
  nav.toc .grp { margin:16px 0 6px; font:12px/1 "SFMono-Regular",Consolas,monospace;
                 letter-spacing:.1em; text-transform:uppercase; color:var(--accent); padding:0 10px; }
  article h2 { margin:0 0 12px; padding-top:8px; }
  article section { padding:34px 0; border-top:1px solid var(--rule); }
  article section:first-child { border-top:none; padding-top:0; }
  article h3 { font-size:18px; margin:26px 0 8px; }
  article p { max-width:64em; }
  article ul { max-width:64em; }
  article li { margin:0 0 7px; }
  .ba { font:13px/1.5 "SFMono-Regular",Consolas,monospace; white-space:nowrap; }
  table.g { margin:14px 0 4px; }
  table.g td:first-child, table.g th:first-child { white-space:nowrap; }
  table.g code { white-space:nowrap; }
  @media (max-width:700px) { table.g code, table.g td:first-child { white-space:normal; } }
  figure.ex { margin:18px 0 22px; }
  figure.ex .paper { padding:12px; }
  figure.ex details { margin-top:10px; }
  .rough { border-left:3px solid var(--accent); padding:2px 0 2px 16px; margin:16px 0; }
  .rough p { margin:0 0 8px; }
  .rough p:last-child { margin:0; }
  .k { font:13px/1.4 "SFMono-Regular",Consolas,monospace; }
  /* the reference's front page */
  section.refcard { border-top:1px solid var(--rule); padding:18px 0 4px; }
  section.refcard h2 { font-size:21px; margin:0 0 6px; }
  section.front { padding:0 0 10px; }
  /* learn pages */
  article.lesson { max-width:64em; }
  article.lesson h1 { font-size:32px; margin:0 0 14px; letter-spacing:-.01em; }
  article.lesson h2 { font-size:21px; margin:30px 0 10px; }
  ul.lsteps { list-style:none; padding:0; }
  ul.lsteps li, ol.llist li { margin:0 0 10px; }
  ol.llist { padding-left:1.4em; }
  nav.lnav { display:flex; justify-content:space-between; gap:12px; margin:36px 0 0;
             padding-top:16px; border-top:1px solid var(--rule); }
'@

# ---------------------------------------------------------------- pages
# Every page carries the same links along the top, so a reader can get from any page to any
# other; the page being read is marked rather than linked.
$byParser = 'The parser is the authority: where this page and the compiler disagree, the compiler is right and this page is a bug.'
$byExtension = 'The extension is the authority: where this page and the extension disagree, the extension is right and this page is a bug.'

# The pages written from a *-body.html of their own. Section: the link along the top they
# stand under (site-common.ps1's $SiteNav).
$pages = @(
    @{ Out = 'start.html'; Body = 'start-body.html'; Section = 'start.html'
       Title = 'Lily# — get started'
       Desc = 'Install the Lily# extension for VS Code, write a first score and hear it — in a few minutes.'
       Sub = 'Get started — install, a first score, and where next'
       Authority = $byExtension },
    @{ Out = 'chords.html'; Body = 'chords-body.html'; Section = 'grammar.html'
       Title = 'Lily# — chords and chord diagrams'
       Desc = 'Chord names, guitar chord diagrams and chords written from a shape in Lily#, step by step, with engraved examples.'
       Sub = 'Reference — chords and chord diagrams, step by step'
       Authority = $byParser },
    @{ Out = 'editor.html'; Body = 'editor-body.html'; Section = 'editor.html'
       Title = 'Lily# — the editor and AI'
       Desc = 'The Lily# VS Code extension: live preview, typing aids, stepping, audition, commands, settings and the AI features.'
       Sub = 'The VS Code extension, and what it does with AI'
       Authority = $byExtension },
    @{ Out = 'commands.html'; Body = 'commands-body.html'; Section = 'editor.html'
       Title = 'Lily# — editor commands'
       Desc = 'Every Lily# command in VS Code — the right-click menus and the command palette: what each does, with examples.'
       Sub = 'Editor commands — what each one does'
       Authority = $byExtension },
    @{ Out = 'about.html'; Body = 'about-body.html'; Section = 'about.html'
       Title = 'Lily# — about'
       Desc = 'What Lily# owes LilyPond, its licence and fonts, how it is put together, and why the preview keeps up.'
       Sub = 'About — lineage, licence, architecture'
       Authority = 'The repository is the authority: README.md, LILYPOND-ATTRIBUTION.md and the source say the same, at more length.'
       Scripts = $MermaidScript }
)

# The language reference: ONE source, manual-body.html, written as four pages by its
# contents' groups (learning-set-design §7 段 4, owner's decision 2026-10-12). grammar.html,
# the URL README.md and every older link name, is the reference's front page: "How to read
# this", the four pages' chapters, and a redirect for a link to a chapter that moved.
$refPages = [ordered]@{
    'ref-music.html'     = @{ Title = 'Lily# reference — writing music'
                              Sub = 'Reference — writing music: notes, rhythm, marks, keys and meters'
                              Desc = 'The Lily# language: pitches, durations, notes and chords, ties and slurs, annotations, tuplets and voices, bar lines, keys, meters, tempo, clefs and dynamics.'
                              Lead = 'Notes, rhythm, marks, keys and meters — what goes inside the braces.' }
    'ref-structure.html' = @{ Title = 'Lily# reference — structure'
                              Sub = 'Reference — structure: parts, phrases, sections, lyrics, chords and forms'
                              Desc = 'The Lily# language: parts, phrases, sections, lyrics, chord rows, forms and repeats, and using other files.'
                              Lead = 'How a piece is put together: who plays, in which sections, in what order.' }
    'ref-output.html'    = @{ Title = 'Lily# reference — output'
                              Sub = 'Reference — output: scores, tablature, paper, fonts and layout'
                              Desc = 'The Lily# language: scores and staves, tablature, paper and fonts, layout switches, and overrides.'
                              Lead = 'What is printed: scores and their staves, the page, the faces, the switches.' }
    'ref-lookup.html'    = @{ Title = 'Lily# reference — look-up'
                              Sub = 'Reference — look-up: reserved words, diagnostics, LilyPond'
                              Desc = 'Lily# look-up tables: reserved words, diagnostics, coming from LilyPond, and the rough edges.'
                              Lead = 'Reserved words, what the diagnostics say, and the way from LilyPond.' }
}
# Which page each group of the manual's contents goes to; "How to read this" heads the front page.
$refGroupPage = @{
    'Orientation' = 'ref-music.html'; 'Writing music' = 'ref-music.html'; 'Key, meter, tempo' = 'ref-music.html'
    'Structure' = 'ref-structure.html'; 'Output' = 'ref-output.html'; 'Reference' = 'ref-lookup.html'
}
$refFront = @('read')

$manual = (Get-Content (Join-Path $here 'manual-body.html') -Raw -Encoding UTF8).Replace('{{VER}}', $ver)
$tocHtml = [regex]::Match($manual, '(?s)<nav class="toc">(.*?)</nav>').Groups[1].Value
$refGroups = foreach ($g in [regex]::Matches($tocHtml, '(?s)<p class="grp">(.*?)</p>\s*<ol>(.*?)</ol>')) {
    $name = $g.Groups[1].Value
    if (-not $refGroupPage.ContainsKey($name)) { continue }   # "Other pages": the links along the top say it now
    [pscustomobject]@{ Name = $name; Page = $refGroupPage[$name]
        Ids = @([regex]::Matches($g.Groups[2].Value, 'href="#([a-z0-9-]+)"') | ForEach-Object { $_.Groups[1].Value }) }
}
$refSections = @{}
foreach ($m in [regex]::Matches($manual, '(?s)<section id="([a-z0-9-]+)">.*?</section>')) { $refSections[$m.Groups[1].Value] = $m.Value }

# Every id in the reference — a chapter's, and a sub-section's (#drums, #todo) — and the page it
# is on: links written as grammar.html#ID are pointed there when a page is written.
$RefPageOf = @{}
foreach ($grp in $refGroups) {
    foreach ($id in $grp.Ids) {
        if (-not $refSections.ContainsKey($id)) { throw "manual-body.html: the contents name #$id, but no <section id=`"$id`"> is there" }
        $page = $refFront -contains $id ? 'grammar.html' : $grp.Page
        foreach ($m in [regex]::Matches($refSections[$id], 'id="([a-z0-9-]+)"')) { $RefPageOf[$m.Groups[1].Value] = $page }
    }
}
foreach ($id in $refSections.Keys) {
    if (-not $RefPageOf.ContainsKey($id)) { throw "manual-body.html: <section id=`"$id`"> is in no group of the contents, so no reference page would carry it" }
}

# A link to grammar.html#ID goes to the page ID is on; on a reference page, a bare #ID on another one.
function Update-RefLinks([string]$html, [string]$out) {
    $html = [regex]::Replace($html, 'href="grammar\.html#([a-z0-9-]+)"', {
        param($m)
        $id = $m.Groups[1].Value
        $RefPageOf.ContainsKey($id) ? "href=`"$($RefPageOf[$id])#$id`"" : $m.Value
    })
    if ($out -like 'ref-*.html' -or $out -eq 'grammar.html') {
        $html = [regex]::Replace($html, 'href="#([a-z0-9-]+)"', {
            param($m)
            $id = $m.Groups[1].Value
            $RefPageOf.ContainsKey($id) -and $RefPageOf[$id] -ne $out ? "href=`"$($RefPageOf[$id])#$id`"" : $m.Value
        })
    }
    $html
}

# What an engraved example shows, for its alt text: the first sentence of the comment the
# example opens with (every example says what it shows there), else its title. Until
# 2026-10-12 every picture was "engraved example", which tells a screen reader nothing.
function Get-AltText([string]$lys) {
    $comment = (($lys -split "`r?`n") | ForEach-Object { $_.Trim() } |
        Where-Object { $_ -match '^//' } | Select-Object -First 4 | ForEach-Object { $_ -replace '^//\s*', '' }) -join ' '
    if (-not $comment) {
        $title = [regex]::Match($lys, '(?m)^\s*title\s+"([^"]*)"').Groups[1].Value
        $comment = if ($title) { $title } else { 'An engraved example' }
    }
    $sentence = [regex]::Match($comment, '^.*?[.!?](?=\s|$)').Value
    if (-not $sentence) { $sentence = $comment }
    # Code quotes go (`drums { }`, '.up'); an apostrophe inside a word (group's) stays.
    $sentence = $sentence -replace '`', '' -replace "(?<=^|\s)'([^']+)'", '$1'
    'Engraved: ' + [System.Net.WebUtility]::HtmlEncode($sentence)
}

# Section numbers are counted, not typed: a body's sections are numbered in the order they
# stand, so a section moved or added renumbers the rest (they were typed 01..25 by hand,
# and moving a chapter broke them — learning-set-design §6.4).
function Add-SectionNumbers([string]$body) {
    $count = @{ N = 0 }
    [regex]::Replace($body, '(<section id="[^"]+">)(\r?\n)', {
        param($m)
        $count.N++
        "$($m.Groups[1].Value)$($m.Groups[2].Value)  <p class=`"eyebrow`">$('{0:00}' -f $count.N)</p>$($m.Groups[2].Value)"
    })
}

# The contents list stands before the text on a phone (one column), and the manual's runs
# to several screens. It is a <details>: open on a wide screen, where it is the sidebar,
# and closed on a narrow one (the script in the page), where a tap opens it.
function Add-FoldingToc([string]$body) {
    $body -replace '<nav class="toc">', '<nav class="toc"><details open><summary>Contents</summary>' `
          -replace '(?s)(<nav class="toc"><details open>.*?)</nav>', '$1</details></nav>'
}

# <!--EXAMPLE:name--> becomes the engraved example plus its COMPLETE source, folded
# away. The source is read from the file that was rendered, so the picture and the
# text on the page can never drift apart — build-examples.ps1 compiles every one.
function Expand-Examples([string]$body) {
    [regex]::Replace($body, '<!--EXAMPLE:([a-z0-9-]+)-->', {
        param($m)
        $name = $m.Groups[1].Value
        $lys = Join-Path $here "examples/$name.lys"
        $svg = "examples/$name.svg"
        if (-not (Test-Path $lys)) {
            # A gallery picture (build-site.ps1's $pictures, drawn from site/NAME.lys) is shown
            # the same way, source and all, so no engraving on a page is left without its file.
            $lys = Join-Path $here "$name.lys"
            $svg = "$name.svg"
        }
        if (-not (Test-Path $lys)) { throw "No example named '$name' (looked for examples/$name.lys and $name.lys)" }
        $raw = (Get-Content $lys -Raw -Encoding UTF8).TrimEnd()
        $src = $raw.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;')
        $alt = Get-AltText $raw
        @"
<figure class="ex">
  <div class="paper"><img src="$svg" alt="$alt"></div>
  <details><summary>Show the whole file</summary><pre>$src</pre></details>
</figure>
"@
    })
}

function Write-Page([string]$out, [string]$section, [string]$title, [string]$desc, [string]$sub, [string]$body,
    [string]$authority, [string]$scripts = '') {
    $nav = Get-SiteNav $section
    $body = Update-RefLinks $body $out
    $html = @"
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>$title</title>
<meta name="description" content="$desc">
$(Get-HeadMeta $title $desc $out)
<style>
$css
$extra
</style>
</head>
<body>
<div class="topbar"><div class="wrap">
  <a class="home" href="index.html">Lily#</a>
  <span>$sub</span>
  <nav class="pages">$nav</nav>
</div></div>

<div class="wrap" style="padding-top:40px;padding-bottom:40px">
$body
</div>

<footer><div class="wrap">
  <p style="margin:0" class="note">Written against Lily# $ver, built $(Get-Date -Format 'yyyy-MM-dd').
  $authority</p>
</div></footer>
<script>{ const narrow = matchMedia('(max-width:999px)'), fold = () => document.querySelectorAll('nav.toc details').forEach(d => d.open = !narrow.matches); fold(); narrow.addEventListener('change', fold); }</script>
$scripts
</body>
</html>
"@
    Set-Content -Path (Join-Path $here $out) -Value $html -Encoding UTF8
    Write-Host "Wrote $(Join-Path $here $out)"
}

foreach ($page in $pages) {
    $body = Expand-Examples ((Get-Content (Join-Path $here $page.Body) -Raw -Encoding UTF8).Replace('{{VER}}', $ver).
        Replace('{{MERMAID}}', (Get-ReadmeMermaid $here)).Replace('{{INSTALL_STEPS}}', $InstallSteps))
    $body = Add-FoldingToc (Add-SectionNumbers $body)
    Write-Page $page.Out $page.Section $page.Title $page.Desc $page.Sub $body $page.Authority ($page.Scripts ?? '')
}

# ---------------------------------------------------------------- the reference
# The other reference pages, in the side contents of each: where the reader goes next.
function Get-RefSiblings([string]$out) {
    $items = foreach ($p in $refPages.Keys) {
        $name = ($refPages[$p].Sub -replace '^Reference — ', '' -replace ':.*$', '')
        $name = $name.Substring(0, 1).ToUpperInvariant() + $name.Substring(1)
        $p -eq $out ? "    <li><a href=`"$p`" aria-current=`"page`"><b>$name</b></a></li>" : "    <li><a href=`"$p`">$name</a></li>"
    }
    "  <p class=`"grp`">Reference pages</p>`n  <ol>`n    <li><a href=`"grammar.html`">How to read this</a></li>`n$($items -join "`n")`n    <li><a href=`"chords.html`">Chords and diagrams</a></li>`n  </ol>"
}

foreach ($p in $refPages.Keys) {
    $groups = @($refGroups | Where-Object { $_.Page -eq $p })
    $toc = ($groups | ForEach-Object {
        $g = $_
        $items = ($g.Ids | Where-Object { $refFront -notcontains $_ } | ForEach-Object {
            $h = [regex]::Match($refSections[$_], '(?s)<h2>(.*?)</h2>').Groups[1].Value
            "    <li><a href=`"#$_`">$h</a></li>"
        }) -join "`n"
        $items ? "  <p class=`"grp`">$($g.Name)</p>`n  <ol>`n$items`n  </ol>" : ''
    }) -join "`n"
    $sections = ($groups | ForEach-Object { $_.Ids } | Where-Object { $refFront -notcontains $_ } |
        ForEach-Object { $refSections[$_] }) -join "`n`n"
    $body = @"
<div class="layout">

<nav class="toc">
$toc
$(Get-RefSiblings $p)
</nav>

<article>

$sections

</article>
</div>
"@
    $body = Add-FoldingToc (Add-SectionNumbers (Expand-Examples $body))
    Write-Page $p 'grammar.html' $refPages[$p].Title $refPages[$p].Desc $refPages[$p].Sub $body $byParser
}

# The front page: how to read the reference, every chapter by page, and — for a link that
# still names grammar.html#CHAPTER from before the split (README, an old bookmark, a post) —
# a jump to the page the chapter is on now.
$frontCards = ($refPages.Keys | ForEach-Object {
    $p = $_
    $chapters = ($refGroups | Where-Object { $_.Page -eq $p } | ForEach-Object { $_.Ids } | Where-Object { $refFront -notcontains $_ } |
        ForEach-Object { "<a href=`"${p}#$_`">$([regex]::Match($refSections[$_], '(?s)<h2>(.*?)</h2>').Groups[1].Value)</a>" }) -join ' · '
    $name = ($refPages[$p].Sub -replace '^Reference — ', '' -replace ':.*$', '')
    "<section class=`"refcard`">`n  <h2><a href=`"$p`">$($name.Substring(0, 1).ToUpperInvariant() + $name.Substring(1))</a></h2>`n  <p>$($refPages[$p].Lead)</p>`n  <p class=`"note`">$chapters</p>`n</section>"
}) -join "`n"
$redirects = ($RefPageOf.GetEnumerator() | Where-Object { $_.Value -ne 'grammar.html' } | Sort-Object Key |
    ForEach-Object { "`"#$($_.Key)`":`"$($_.Value)`"" }) -join ','
$frontBody = @"
<article class="lesson">
<p class="eyebrow">Reference</p>
<h1>The Lily# language</h1>
<p>A reference to the <code>.lys</code> language as Lily# $ver accepts it, in four pages. Every
complete example on them is compiled when the site is built. New to it? Start with
<a href="start.html">Get started</a> and the <a href="learn.html">lessons</a>.</p>
$(($refFront | ForEach-Object { $refSections[$_] -replace '<section id=', '<section class="front" id=' }) -join "`n")
$frontCards
<section class="refcard">
  <h2><a href="chords.html">Chords and chord diagrams</a></h2>
  <p>Chord names, guitar and ukulele diagrams, and chords written from a shape — step by step.</p>
</section>
</article>
<script>{ const moved = {$redirects}; const hash = decodeURIComponent(location.hash); if (moved[hash]) location.replace(moved[hash] + hash); }</script>
"@
Write-Page 'grammar.html' 'grammar.html' 'Lily# — the language reference' `
    'The Lily# language reference: every construct of the .lys music notation language, in four pages, with engraved examples.' `
    'Reference — the <code>.lys</code> language' (Expand-Examples $frontBody) $byParser

# ---------------------------------------------------------------- learn pages
# One page per lesson, GENERATED from the lesson files the editor offers (owner's decision
# 2026-10-12): the lesson's own words, the score as it opens, the whole file and where the
# manual says more. A lesson changed or translated changes its page, and the two cannot
# disagree. build-site.ps1 draws learn/LABEL.svg (and LABEL-NAME.svg for each named score).
$lessonDir = Join-Path $here '../LilySharp.Lsp/Lessons/en'
$lessons = @(Get-ChildItem $lessonDir -Filter 'lesson-*.lys' | Sort-Object Name)
if ($lessons.Count -eq 0) { throw "No lessons found in $lessonDir" }
# The count in words, from the lessons found — a lesson added leaves no stale "eleven" behind.
$countWord = @('zero','one','two','three','four','five','six','seven','eight','nine','ten','eleven','twelve','thirteen','fourteen','fifteen','sixteen','seventeen','eighteen','nineteen','twenty')[[Math]::Min($lessons.Count, 20)]
$countWordTitle = $countWord.Substring(0, 1).ToUpperInvariant() + $countWord.Substring(1)

# Where the manual says more, per lesson: page#section. The link text is the section's own
# heading, read from its body file, so a renamed or removed section fails the build here.
$moreOf = @{
    'lesson-01-completion' = 'editor.html#language', 'grammar.html#glance', 'grammar.html#parts'
    'lesson-02-notes'      = 'grammar.html#pitch', 'grammar.html#duration', 'editor.html#step'
    'lesson-03-rhythm'     = 'grammar.html#duration', 'grammar.html#meter', 'grammar.html#connect'
    'lesson-04-keys'       = 'grammar.html#key', 'grammar.html#pitch', 'grammar.html#scores'
    'lesson-05-expression' = 'grammar.html#dynamics', 'grammar.html#anno', 'grammar.html#connect'
    'lesson-06-form'       = 'grammar.html#forms', 'grammar.html#sections'
    'lesson-07-lyrics'     = 'grammar.html#lyrics'
    'lesson-08-chords'     = 'grammar.html#chordrows', 'chords.html#names', 'chords.html#shapes'
    'lesson-09-piano'      = 'grammar.html#parts', 'grammar.html#phrases', 'grammar.html#groups'
    'lesson-10-drums'      = 'grammar.html#drums', 'grammar.html#phrases'
    'lesson-11-band'       = 'grammar.html#transposing', 'grammar.html#tab', 'grammar.html#scores'
    'lesson-12-chart'      = 'grammar.html#events', 'grammar.html#scores', 'grammar.html#paper', 'grammar.html#chordrows'
}
$headings = @{}
foreach ($page in @(@{ Out = 'grammar.html'; Body = 'manual-body.html' }) + $pages) {
    $src = Get-Content (Join-Path $here $page.Body) -Raw -Encoding UTF8
    foreach ($m in [regex]::Matches($src, '(?s)<section id="([a-z0-9-]+)">.*?<h2>(.*?)</h2>')) {
        $headings["$($page.Out)#$($m.Groups[1].Value)"] = ($m.Groups[2].Value -replace '<[^>]+>', '')
    }
    foreach ($m in [regex]::Matches($src, '(?s)<h3 id="([a-z0-9-]+)">(.*?)</h3>')) {   # a sub-section (#drums)
        $headings["$($page.Out)#$($m.Groups[1].Value)"] = ($m.Groups[2].Value -replace '<[^>]+>', '')
    }
}

function ConvertTo-LessonHtml([string]$text) {
    $t = $text.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;')
    $t = [regex]::Replace($t, '`([^`]+)`', '<code>$1</code>')
    # Keys: Ctrl+Space, Ctrl+Shift+Up, Ctrl+K … — outside the code spans already made.
    $parts = [regex]::Split($t, '(<code>.*?</code>)')
    ($parts | ForEach-Object {
        if ($_ -like '<code>*') { $_ } else { [regex]::Replace($_, '\b(?:Ctrl|Shift|Alt|Cmd)\+(?:[A-Za-z0-9]+|\.)(?:\+[A-Za-z0-9]+)*', '<kbd>$0</kbd>') }
    }) -join ''
}

# The comments of a lesson as blocks: STEP n, TRY, NEXT, or prose; where it stands (head of
# the file, among the music, or after it). A block runs until a blank comment line or the next
# block's opening word; a line indented under `//` is a table row and keeps its line.
function Get-LessonBlocks([string[]]$lines) {
    $lastCode = 0
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '\S' -and $lines[$i].TrimStart() -notlike '//*') { $lastCode = $i }
    }
    $blocks = [System.Collections.Generic.List[object]]::new()
    $cur = $null
    $where = 'head'
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $trim = $lines[$i].TrimStart()
        if ($trim -notlike '//*') {
            $cur = $null
            if ($lines[$i] -match '\S') { $where = 'music' }
            continue
        }
        if ($i -gt $lastCode) { $where = 'tail' }
        $text = $trim.Substring(2)
        if ($text.StartsWith(' ')) { $text = $text.Substring(1) }
        if ($i -eq 0 -or $text -match '^Preview:') { continue }          # the title line; the old preview hint
        if ($text -notmatch '\S') { $cur = $null; continue }
        $m = [regex]::Match($text, '^(STEP \d+|TRY|NEXT)\b\s*[—:-]?\s*(.*)$')
        if ($m.Success) {
            $kind = $m.Groups[1].Value -replace ' \d+', ''
            $cur = [pscustomobject]@{ Kind = $kind; Label = $m.Groups[1].Value; Where = $where; Lines = [System.Collections.Generic.List[string]]::new() }
            $blocks.Add($cur)
            if ($m.Groups[2].Value) { $cur.Lines.Add($m.Groups[2].Value) }
            continue
        }
        if (-not $cur) {
            $cur = [pscustomobject]@{ Kind = 'prose'; Label = ''; Where = $where; Lines = [System.Collections.Generic.List[string]]::new() }
            $blocks.Add($cur)
        }
        $cur.Lines.Add($text)
    }
    return $blocks
}

function Format-Block($block) {
    $out = ''
    $afterRow = $false
    foreach ($line in $block.Lines) {
        if ($line -match '^  \S') {
            # A table row under `//` (two or more spaces in): its own line, its spacing kept.
            $out += "<br><span class=`"k`" style=`"white-space:pre-wrap`">$(ConvertTo-LessonHtml $line.TrimEnd())</span>"
            $afterRow = $true
        } else {
            $out += ($afterRow ? '<br>' : ($out ? ' ' : '')) + (ConvertTo-LessonHtml $line.Trim())
            $afterRow = $false
        }
    }
    # "TRY: the red circles…" reads as a sentence once the label is a heading.
    if ($out -match '^[a-z]') { $out = $out.Substring(0, 1).ToUpperInvariant() + $out.Substring(1) }
    return $out
}

$learnItems = @()
for ($n = 0; $n -lt $lessons.Count; $n++) {
    $file = $lessons[$n]
    $label = $file.BaseName
    $lines = Get-Content $file.FullName -Encoding UTF8
    $head = [regex]::Match($lines[0], '^//\s*Lesson (\d+) of (\d+)\s*[—-]\s*(.+)$')
    if (-not $head.Success) { throw "$($file.Name): the first line is not '// Lesson N of M — Title'" }
    $num = $head.Groups[1].Value; $of = $head.Groups[2].Value; $title = $head.Groups[3].Value.Trim()
    $blocks = Get-LessonBlocks $lines
    # The head reads in its own order — its prose and any STEP it holds (lesson 1's STEP 0 comes
    # before its "Then:"); the steps among the music follow the picture.
    $headBlocks = @($blocks | Where-Object { $_.Where -eq 'head' -and $_.Kind -in 'prose', 'STEP' })
    $intro = @($headBlocks | Where-Object { $_.Kind -eq 'prose' })
    $steps = @($blocks | Where-Object { $_.Kind -eq 'STEP' -and $_.Where -ne 'head' })
    $tries = @($blocks | Where-Object { $_.Kind -eq 'TRY' })
    $closing = @($blocks | Where-Object { $_.Kind -eq 'prose' -and $_.Where -eq 'tail' })

    $svgs = @(Get-ChildItem (Join-Path $here 'learn') -Filter "$label*.svg" -ErrorAction SilentlyContinue |
        Where-Object { $_.BaseName -eq $label -or $_.BaseName -like "$label-*" } | Sort-Object { $_.BaseName -ne $label }, Name)
    if ($svgs.Count -eq 0) { $svgs = @([pscustomobject]@{ Name = "$label.svg"; BaseName = $label }) }
    $figures = ($svgs | ForEach-Object {
        $scoreName = $_.BaseName -eq $label ? '' : $_.BaseName.Substring($label.Length + 1)
        $cap = $scoreName ? "The score <code>$scoreName</code>." : 'The lesson as it opens: the red rings mark where a step happens.'
        "<figure class=`"ex`">`n  <div class=`"paper`"><img src=`"learn/$($_.Name)`" alt=`"$title — engraved`" loading=`"lazy`"></div>`n  <p class=`"caption`">$cap</p>`n</figure>"
    }) -join "`n"

    $src = ((Get-Content $file.FullName -Raw -Encoding UTF8).TrimEnd()).Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;')
    $more = ($moreOf[$label] | ForEach-Object {
        if (-not $headings.ContainsKey($_)) { throw "${label}: no section '$_' to link to (the learn page's 'In the manual' list in build-manual.ps1)" }
        "<li><a href=`"$_`">$($headings[$_])</a></li>"
    }) -join "`n  "
    if (-not $moreOf.ContainsKey($label)) { throw "${label}: no 'In the manual' links in build-manual.ps1 (`$moreOf)" }

    $prev = $n -gt 0 ? "<a href=`"$($lessons[$n - 1].BaseName).html`">&larr; Lesson $n</a>" : '<span></span>'
    $next = $n -lt $lessons.Count - 1 ? "<a href=`"$($lessons[$n + 1].BaseName).html`">Lesson $($n + 2) &rarr;</a>" : '<span></span>'

    $body = @"
<article class="lesson">
<p class="eyebrow">Lesson $num of $of</p>
<h1>$title</h1>
<p class="note">In VS Code, in an empty <code>.lys</code> file: type <code>lesson</code>, press <kbd>Ctrl+Space</kbd> and pick <code>$label</code>.</p>
$(($headBlocks | ForEach-Object { $_.Kind -eq 'STEP' ? "<p><b>$($_.Label)</b> — $(Format-Block $_)</p>" : "<p>$(Format-Block $_)</p>" }) -join "`n")
$figures
$($steps.Count ? "<h2>Steps</h2>`n<ul class=`"lsteps`">`n$(($steps | ForEach-Object { "  <li><b>$($_.Label)</b> — $(Format-Block $_)</li>" }) -join "`n")`n</ul>" : '')
$($tries.Count ? "<h2>Try</h2>`n$(($tries | ForEach-Object { "<p>$(Format-Block $_)</p>" }) -join "`n")" : '')
$(($closing | ForEach-Object { "<p>$(Format-Block $_)</p>" }) -join "`n")
<h2>The whole lesson</h2>
<pre data-lesson>$src</pre>
<h2>In the manual</h2>
<ul>
  $more
</ul>
<nav class="lnav">$prev <a href="learn.html">All lessons</a> $next</nav>
</article>
"@
    Write-Page "$label.html" 'learn.html' "Lily# — Lesson ${num}: $title" `
        "Lily# lesson ${num} of ${of}: $title — a short score that teaches one step, opened in VS Code with one word." `
        "Learn — $countWord short lessons, in the editor" $body `
        'The lesson file is the authority: this page is drawn from it, and the editor offers the same file.'
    # The list's one line: the intro's first sentence ("Then: " is lesson 1's link to its STEP 0).
    $first = $intro.Count ? ((Format-Block $intro[0]) -replace '^Then: ', '' -replace '<br>.*$', '' -replace '(?<=[.!?])\s.*$', '') : ''
    $learnItems += "<li><a href=`"$label.html`"><b>Lesson ${num} — $title</b></a><br><span class=`"note`">$first</span></li>"
}

$learnBody = @"
<article class="lesson">
<p class="eyebrow">Learn</p>
<h1>$countWordTitle short lessons</h1>
<p>Each lesson is a complete, playable score that teaches one step. They live in the editor:
in VS Code, open an empty <code>.lys</code> file, type <code>lesson</code> and press
<kbd>Ctrl+Space</kbd> — the list shows all $($lessons.Count), and picking one puts it in the
file. The preview opens beside it and redraws as you follow the steps. These pages show each
lesson as it opens, what it asks you to do, and where the manual says more.</p>
<p class="note">Not installed yet? <a href="start.html#install">Install the extension</a> — one
search in VS Code's Extensions view, nothing else to install.</p>
<!-- VIDEO learn-lesson: SHOTLIST 12. An empty .lys: type lesson, Ctrl+Space, pick lesson-02-notes.
     The lesson replaces the file and the preview opens with red rings; do what the first ring
     says ("caret before d, type 2"), delete its @todo, and the ring goes. ~20 s, no sound. -->
<figure class="demo-video" data-pending>
  <video src="videos/learn-lesson.mp4" muted loop playsinline autoplay preload="none"></video>
  <figcaption>A lesson from the completion list: the red rings mark the steps.</figcaption>
</figure>
<ol class="llist">
$($learnItems -join "`n")
</ol>
</article>
"@
Write-Page 'learn.html' 'learn.html' "Lily# — learn in $countWord lessons" `
    "Learn Lily#, the plain-text music notation language, in $countWord short lessons that open in VS Code." `
    "Learn — $countWord short lessons, in the editor" $learnBody `
    'The lesson files are the authority: these pages are drawn from them, and the editor offers the same files.'
