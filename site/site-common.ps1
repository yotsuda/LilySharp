# What every page of the site shares, dot-sourced by build-manual.ps1 (the reading pages) and
# build-preview.ps1 (index.html) so the two cannot drift: the links along the top, the
# <head> lines beyond a page's title and description (the favicon, and the Open Graph /
# Twitter card tags a link shared in a chat or a post is drawn from), and the architecture
# diagram lifted out of README.md.
#
# favicon.png is the extension's own icon, copied in by build-site.ps1 when it assembles
# _site/ — one picture, kept in editors/vscode/icon.png.

$SiteUrl = 'https://yotsuda.github.io/LilySharp/'

# The links along the top of EVERY page, the home page included (owner's decision 2026-10-12:
# Home · Get started · Learn · Reference · Editor · About). A page that is not one of these
# marks the one it belongs under (a lesson under Learn, a reference page under Reference).
$SiteNav = @(
    @{ Out = 'index.html';   Nav = 'Home' },
    @{ Out = 'start.html';   Nav = 'Get started' },
    @{ Out = 'learn.html';   Nav = 'Learn' },
    @{ Out = 'grammar.html'; Nav = 'Reference' },
    @{ Out = 'editor.html';  Nav = 'Editor' },
    @{ Out = 'about.html';   Nav = 'About' }
)

function Get-SiteNav([string]$current) {
    ($SiteNav | ForEach-Object {
        if ($_.Out -eq $current) { "<b aria-current=`"page`">$($_.Nav)</b>" } else { "<a href=`"$($_.Out)`">$($_.Nav)</a>" }
    }) -join ' '
}

# How to install, step by step, for a reader who has never used VS Code (owner's request
# 2026-10-12: "Lily#'s users are not all developers who use VS Code"). ONE copy, written into
# the home page (build-preview.ps1) and Get started (start-body.html's {{INSTALL_STEPS}}).
$InstallSteps = @'
<ol class="steps">
    <li><b>Get Visual Studio Code — free</b>
      <span>Lily# runs inside <a href="https://code.visualstudio.com/">Visual Studio Code</a>, a
      free editor from Microsoft (“VS Code”). You don't need to know programming to use it: here
      it is simply the window you write music in. On its website, click the download button for
      your computer — Windows, Mac or Linux — open the file that downloads, and accept the
      installer's suggestions.</span></li>
    <li><b>Add Lily# to it</b>
      <span>Start VS Code. On its left edge, click the <b>Extensions</b> icon — four small squares,
      one coming loose — or press <kbd>Ctrl+Shift+X</kbd> (<kbd>Cmd+Shift+X</kbd> on a Mac). Type
      <b>Lily#</b> in the search box at the top, and click <b>Install</b> next to <i>Lily#</i>
      by <i>yotsuda</i>. Everything it needs comes with it, the music fonts included; there is
      nothing else to download.</span></li>
    <li><b>Start a score</b>
      <span>In the menu, choose <b>File → New File…</b> and pick <b>Lily# Score (.lys)</b>. A
      short tune appears on the left, and its printed score on the right. Change a note on the
      left and the score changes as you type. Save it with <b>File → Save</b>, under a name that
      ends in <code>.lys</code> (<code>my-song.lys</code>).</span></li>
    <li><b>Hear it</b>
      <span>Click the score once, then press <b>▶</b> at its top: it plays, and each note lights
      as it sounds. (A computer lets a page make sound only after you have clicked in it — that
      first click is what it waits for.)</span></li>
    <li><b>Learn the rest as you go</b>
      <span>On an empty line at the end of the file, type <code>lesson</code> and press
      <kbd>Ctrl+Space</kbd>: a list of twelve short lessons appears. Pick
      <code>lesson-01-completion</code> to begin — it takes the place of the tune (VS Code asks
      first) and shows you one thing at a time. The same lessons are on the
      <a href="learn.html">Learn</a> pages.</span></li>
  </ol>
'@

# The architecture diagram is lifted out of README.md so a page shows the real thing rather
# than a copy that can drift; HTML-escaped, for a <pre class="mermaid">.
function Get-ReadmeMermaid([string]$siteDir) {
    $readme = Get-Content (Join-Path $siteDir '../README.md') -Raw -Encoding UTF8
    $m = [regex]::Match($readme, '(?s)```mermaid\r?\n(.*?)```')
    if (-not $m.Success) { throw 'No mermaid block found in README.md' }
    $m.Groups[1].Value.TrimEnd().Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;')
}

# The script that draws a page's <pre class="mermaid"> diagrams, in the reader's light or dark.
$MermaidScript = @'
<script src="https://cdnjs.cloudflare.com/ajax/libs/mermaid/10.9.1/mermaid.min.js"></script>
<script>
  if (window.mermaid) {
    var dark = matchMedia('(prefers-color-scheme: dark)').matches;
    mermaid.initialize({ startOnLoad:true, theme: dark ? 'dark' : 'neutral' });
  }
</script>
'@

function Get-HeadMeta([string]$title, [string]$desc, [string]$page) {
    $t = [System.Net.WebUtility]::HtmlEncode($title)
    $d = [System.Net.WebUtility]::HtmlEncode($desc)
    $url = if ($page -eq 'index.html') { $SiteUrl } else { $SiteUrl + $page }
    @"
<link rel="icon" type="image/png" href="favicon.png">
<meta property="og:type" content="website">
<meta property="og:site_name" content="Lily#">
<meta property="og:title" content="$t">
<meta property="og:description" content="$d">
<meta property="og:url" content="$url">
<meta property="og:image" content="${SiteUrl}hero-vscode.png">
<meta name="twitter:card" content="summary_large_image">
"@
}
