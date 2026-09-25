# Builds a local preview page for the Lily# showcase.
# Run from this directory:  pwsh -File build-preview.ps1
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $here

function Esc([string]$s) { $s.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;') }
function LysOf([string]$n) { Esc ((Get-Content "$n.lys" -Raw -Encoding UTF8).TrimEnd()) }

# The version is read from the repository, never typed: a hand-written one went stale
# (0.5.0 on a 0.8.0 site). build-site.ps1 fails the build if any other version appears.
$props = Get-Content (Join-Path $here '../Directory.Build.props') -Raw -Encoding UTF8
$ver = [regex]::Match($props, '<Version>([^<]+)</Version>').Groups[1].Value
if (-not $ver) { throw 'No <Version> found in Directory.Build.props' }

# The architecture diagram is lifted out of README.md so the preview shows the real
# thing rather than a copy that can drift.
$readme = Get-Content (Join-Path $here '../README.md') -Raw -Encoding UTF8
$m = [regex]::Match($readme, '(?s)```mermaid\r?\n(.*?)```')
if (-not $m.Success) { throw 'No mermaid block found in README.md' }
$mermaid = Esc ($m.Groups[1].Value.TrimEnd())

$template = @'
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Lily# — sheet music from plain text</title>
<meta name="description" content="Lily#: publication-quality sheet music from plain text, engraved by a port of LilyPond's layout engine, with a VS Code extension that previews as you type.">
<style>
  /* No viewport units anywhere: every size is fixed or a share of its own
     container, so the layout does not depend on the browser's dimensions. */
  :root {
    --bg:#fbfaf8; --fg:#1a1a1a; --muted:#5f594f; --rule:#e4dfd6;
    --card:#fff; --accent:#8a5a2b; --code:#f4f1ea; --shadow:rgba(0,0,0,.09);
  }
  @media (prefers-color-scheme: dark) {
    :root:not([data-theme="light"]) {
      --bg:#141310; --fg:#ece8e0; --muted:#a8a094; --rule:#302c25;
      --card:#1e1c17; --accent:#d9a662; --code:#201e18; --shadow:rgba(0,0,0,.5);
    }
  }
  * { box-sizing:border-box; }
  html { -webkit-text-size-adjust:100%; }
  body {
    margin:0; background:var(--bg); color:var(--fg);
    font:16px/1.65 -apple-system,"Segoe UI",Roboto,"Helvetica Neue",sans-serif;
    overflow-wrap:break-word;
  }
  img { max-width:100%; height:auto; display:block; }
  /* Fills the window, but stops widening once lines get hard to track. The
     generous side padding is what keeps it breathing on a narrow window. */
  .wrap { width:100%; max-width:1440px; margin:0 auto; padding:0 56px; }
  @media (max-width:640px) { .wrap { padding:0 24px; } }
  .banner { background:var(--accent); color:#fff; text-align:center; padding:7px 14px; font-size:13px; }

  header { padding:80px 0 68px; }
  .brand { font-size:56px; line-height:1.05; margin:0 0 14px; letter-spacing:-.03em; font-weight:700; }
  .tagline { font-size:20px; color:var(--muted); margin:0 0 48px; max-width:34em; }

  /* auto-fit + minmax reflows on the CONTAINER's width, not the viewport's.
     min-width:0 stops a wide child (a long code line, a big SVG) from forcing
     its track wider than the container — the usual cause of a clipped page. */
  .cols2 { display:grid; grid-template-columns:repeat(auto-fit,minmax(320px,1fr)); gap:34px; align-items:center; }
  /* six pillars: 360px min lands them 3 + 3 on a wide screen, 2 + 2 + 2 then 1-up
     as it narrows — every step is a clean divisor of six */
  .cols4 { display:grid; grid-template-columns:repeat(auto-fit,minmax(360px,1fr)); gap:24px; }
  .gallery { display:grid; grid-template-columns:repeat(auto-fit,minmax(540px,1fr)); gap:48px; align-items:start; }
  .cols2>*, .cols4>*, .gallery>* { min-width:0; }

  pre { background:var(--code); border:1px solid var(--rule); border-radius:10px;
        padding:20px 22px; margin:0; overflow-x:auto;
        font:13px/1.7 "SFMono-Regular",Consolas,"Liberation Mono",monospace; }
  .paper { background:#fff; border:1px solid var(--rule); border-radius:10px;
           padding:16px; box-shadow:0 2px 14px var(--shadow); overflow-x:auto; }
  .arrow { text-align:center; color:var(--muted); font-size:13px; margin:10px 0 0; }

  .shot { border:1px solid var(--rule); border-radius:12px; overflow:hidden;
          box-shadow:0 4px 22px var(--shadow); background:var(--card); }
  .shot img, .shot video { display:block; width:100%; height:auto; }
  .placeholder { border:2px dashed var(--rule); box-shadow:none; background:var(--code);
                 aspect-ratio:16/10; min-height:300px; display:grid; place-items:center;
                 padding:32px; text-align:center; }
  .placeholder > div { max-width:52em; }
  .ph-title { font-size:19px; font-weight:700; margin:0 0 10px; color:var(--fg); }
  .ph-body  { margin:0 0 14px; color:var(--muted); font-size:15px; }
  .ph-hint  { margin:0; color:var(--muted); font-size:13.5px; }
  .caption  { text-align:center; color:var(--muted); font-size:14px; margin:14px 0 0; }

  ol.steps { counter-reset:s; list-style:none; margin:0; padding:0;
             display:grid; grid-template-columns:repeat(auto-fit,minmax(280px,1fr)); gap:22px; }
  ol.steps li { counter-increment:s; background:var(--card); border:1px solid var(--rule);
                border-radius:12px; padding:22px 24px; min-width:0; }
  ol.steps li::before { content:counter(s); display:block; font:12px/1 "SFMono-Regular",Consolas,monospace;
                        color:var(--accent); margin:0 0 10px; }
  ol.steps b { display:block; margin:0 0 5px; }
  ol.steps span { color:var(--muted); font-size:14.5px; }

  section { padding:76px 0; border-top:1px solid var(--rule); }
  .eyebrow { font:12px/1 "SFMono-Regular",Consolas,monospace; letter-spacing:.12em;
             text-transform:uppercase; color:var(--accent); margin:0 0 12px; }
  h2 { font-size:27px; margin:0 0 10px; letter-spacing:-.01em; }
  .lede { color:var(--muted); margin:0 0 42px; max-width:44em; }

  .pillar { background:var(--card); border:1px solid var(--rule); border-radius:12px; padding:26px 24px; }
  .pillar h3 { margin:0 0 8px; font-size:17px; }
  .pillar p { margin:0; color:var(--muted); font-size:14.5px; }
  .pillar p + p { margin-top:13px; }
  .pillar h3 + p { margin-top:0; }
  .pillar .n { font:12px/1 "SFMono-Regular",Consolas,monospace; color:var(--accent); display:block; margin:0 0 10px; }

  .card { margin:0; }
  .card h3 { font-size:19px; margin:0 0 4px; }
  .card .what { color:var(--muted); margin:0 0 12px; font-size:15px; }
  .tags { display:flex; flex-wrap:wrap; gap:6px; margin:0 0 13px; padding:0; list-style:none; }
  .tags li { font:12px/1 "SFMono-Regular",Consolas,monospace; color:var(--accent);
             border:1px solid var(--rule); border-radius:999px; padding:5px 10px; background:var(--card); }
  details { margin-top:12px; }
  summary { cursor:pointer; color:var(--accent); font-size:14px; }
  details[open] summary { margin-bottom:10px; }

  table { border-collapse:collapse; width:100%; font-size:14.5px; }
  th,td { text-align:left; padding:9px 12px; border-bottom:1px solid var(--rule); }
  th { font-size:12.5px; letter-spacing:.04em; text-transform:uppercase; color:var(--muted); font-weight:600; }
  td.num { font:14px/1 "SFMono-Regular",Consolas,monospace; white-space:nowrap; }
  .note { color:var(--muted); font-size:13px; margin:14px 0 0; }

  ul.plain { margin:0; padding-left:1.15em; }
  ul.plain li { margin:0 0 9px; }
  .box { background:var(--card); border:1px solid var(--rule); border-radius:12px; padding:20px; overflow-x:auto; }
  code { font:13px/1.4 "SFMono-Regular",Consolas,monospace; background:var(--code); padding:1px 5px; border-radius:4px; }
  a { color:var(--accent); text-underline-offset:3px; }
  a:hover { text-decoration-thickness:2px; }
  footer { padding:36px 0 64px; color:var(--muted); font-size:14px; border-top:1px solid var(--rule); }
</style>
</head>
<body>
<header><div class="wrap">
  <h1 class="brand">Lily#</h1>
  <p class="tagline">Publication-quality sheet music from plain text — engraved by a
  LilyPond port, edited in an IDE that keeps up with your keystrokes.
  <a href="grammar.html">Read the language manual &rarr;</a></p>
  {{HERO_SHOT}}
  <p class="caption">The source and the engraving, side by side. The preview follows your
  keystrokes — it does not wait for a save.</p>
</div></header>

<section><div class="wrap">
  <p class="eyebrow">Install</p>
  <h2>Install in two steps</h2>
  <p class="lede">Nothing else to install. Each platform's package brings its own .NET
  runtime and the Emmentaler and TeX Gyre fonts, so there is no toolchain to assemble.</p>
  <ol class="steps">
    <li><b><a href="https://code.visualstudio.com/">Install Visual Studio Code</a></b>
      <span>Version 1.90 or newer.</span></li>
    <li><b>Install the Lily# extension</b>
      <span>In VS Code, press <code>Ctrl+Shift+X</code> (<code>Cmd+Shift+X</code> on macOS),
      search for “Lily#” and click <b>Install</b> on the one by <i>yotsuda</i>. (Or install it
      from its <a href="https://marketplace.visualstudio.com/items?itemName=yotsuda.lilysharp">Marketplace
      page</a>.)</span></li>
    <li><b>Open any <code>.lys</code> file</b>
      <span>The score opens beside the source, with diagnostics and completion as you
      type. For batch work there is also a <code>lysc</code> command-line build.</span></li>
  </ol>
</div></section>

<section><div class="wrap">
  <p class="eyebrow">Why Lily#</p>
  <h2>Six things it is built to do</h2>
  <p class="lede">Most are shown further down, with the evidence.</p>
  <div class="cols4">
    <div class="pillar"><span class="n">01</span><h3>LilyPond's engraving</h3>
      <p>Beam quanting, slur and tie scoring, skylines, springs and page breaking are
      transliterated from LilyPond's own source — not approximated from its output.</p></div>
    <div class="pillar"><span class="n">02</span><h3>A grammar you can read</h3>
      <p>One canonical form per idea, no backslash constructs, and octaves that can be
      absolute so a mistake never cascades.</p></div>
    <div class="pillar"><span class="n">03</span><h3>Preview that keeps up</h3>
      <p>A keystroke reparses incrementally and reuses the systems it did not disturb, so
      the score redraws in milliseconds.</p></div>
    <div class="pillar"><span class="n">04</span><h3>AI that compiles first</h3>
      <p>Ask in words, and every candidate is compiled and repaired before you see it.
      A candidate that adds errors is never shown. It uses your GitHub Copilot models or
      your own API key.</p></div>
    <div class="pillar"><span class="n">05</span><h3>An editor that knows the grammar</h3>
      <p>Completion offers what can actually come next — after <code>lyrics&nbsp;NAME</code>
      it proposes <code>sings</code>, then the part names that exist in your file. Rename a
      part and every reference to it moves with it. Hover a chord and it names itself — symbol,
      degree and pitches. More than a dozen language-server features in all: diagnostics,
      hover, go to definition, find references, rename, document symbols, folding, formatting,
      code actions, CodeLens, signature help, semantic highlighting.</p></div>
    <div class="pillar"><span class="n">06</span><h3>One source, every score</h3>
      <p>A file can carry more than one <code>score</code> and more than one
      <code>form</code>. The full score, the separate parts, a staff-less chord grid and a
      practice excerpt all come out of the same notes — so they cannot drift apart. Write a
      progression once and print it both above the melody and as its own chart.</p></div>
  </div>
</div></section>

<section><div class="wrap">
  <p class="eyebrow">01 — Engraving</p>
  <h2>What comes out</h2>
  <p class="lede">Six pages, one engine. Each is a plain <code>.lys</code> file rendered
  with <code>lysc svg</code>; the source is under every one.</p>

  <div class="gallery">
  <div class="card">
    <h3>Choral octavo</h3>
    <p class="what">An die Freude — Beethoven, four voices with a verse under every staff.</p>
    <ul class="tags"><li>choirStaff</li><li>lyrics … sings</li><li>treble_8</li><li>four parts</li></ul>
    <div class="paper"><img src="ode-to-joy.svg" alt="SATB chorale"></div>
    <details><summary>Show the source</summary><pre>{{ODE_LYS}}</pre></details>
  </div>

  <div class="card">
    <h3>Guitar book</h3>
    <p class="what">The House of the Rising Sun — notation and tablature from one part.</p>
    <ul class="tags"><li>tab</li><li>tuning guitar</li><li>\2 string pin</li><li>chords row</li><li>@rit</li></ul>
    <div class="paper"><img src="rising-sun.svg" alt="Guitar notation with tablature"></div>
    <details><summary>Show the source</summary><pre>{{SUN_LYS}}</pre></details>
  </div>

  <div class="card">
    <h3>Lead sheet</h3>
    <p class="what">Blues in F — a written head over a one-line slash staff and a walking bass.</p>
    <ul class="tags"><li>as lines 1</li><li>slash notes</li><li>swing</li><li>chord symbols</li></ul>
    <div class="paper"><img src="blues-in-f.svg" alt="Twelve-bar blues lead sheet"></div>
    <details><summary>Show the source</summary><pre>{{BLUES_LYS}}</pre></details>
  </div>

  <div class="card">
    <h3>Jazz lead sheet</h3>
    <p class="what">Sketch in C — twelve bars of chord symbols and exactly one rhythmic
    kick. No melody, no filler.</p>
    <ul class="tags"><li>/ slash notes</li><li>as lines 1</li><li>F#m7-5</li><li>Bb7/D</li><li>tie over the barline</li></ul>
    <div class="paper"><img src="sketch-in-c.svg" alt="Jazz chord chart"></div>
    <p class="note"><b>You type the symbol, not an entry language.</b>
    <code>Cmaj7</code>, not LilyPond's <code>c:maj7</code>; <code>Bb7/D</code>, not
    <code>bes:7/d</code>. One rule to know: an altered tension takes <code>+</code> or
    <code>-</code> — <code>F#m7-5</code>, <code>B7-9</code>, <code>A7+5</code> — because
    <code>#</code> and <code>b</code> belong to the root, which is what keeps
    <code>Bb9</code> unambiguous.
    <b>Only the hits that matter get written.</b> A bar with nothing to say is just
    <code>|</code> and fills itself; the one kick is <code>s2.. /8~</code> — three and a half
    beats of silence, then a slash head on the "and" of 4, tied across the barline so the
    Cmaj7 lands an eighth early. <b>And the one-line staff is one word:</b>
    <code>staff comp as lines 1</code>. That is the entire chart.</p>
    <details><summary>Show the source</summary><pre>{{SKETCH_LYS}}</pre></details>
  </div>

  <div class="card">
    <h3>Repeats, and two voices on one staff</h3>
    <p class="what">Air in D — a short dance. The repeat and its two endings live in
    the form, and both voices share a single staff.</p>
    <ul class="tags"><li>voice { } { }</li><li>|: … :|</li><li>[1. …] [2. …]</li><li>@mordent</li><li>@turn</li><li>@trill</li></ul>
    <div class="paper"><img src="air-in-d.svg" alt="Two-voice dance with first and second endings"></div>
    <p class="note"><b>A repeat changes the playing order, so it is written where the order
    is.</b> The music itself holds no repeat barline —
    <code>form main { |: ~Body [1. ~First] :| [2. ~Second] }</code> is what draws the repeat
    dots and the numbered brackets. A <code>:|</code> written among the notes is refused
    outright. <b>Two voices, one staff:</b> <code>voice { … } { … }</code> opens the span
    once and each further brace is another voice — the stems sort themselves up and down.</p>
    <details><summary>Show the source</summary><pre>{{AIR_LYS}}</pre></details>
  </div>

  <div class="card">
    <h3>The engraver, working</h3>
    <p class="what">Petite Valse — a grand staff carrying much of the notation vocabulary at once.</p>
    <ul class="tags"><li>grandStaff</li><li>@sustain</li><li>hairpins</li><li>grace</li><li>tuplet</li><li>@trill</li><li>@arpeggio</li><li>@fermata</li></ul>
    <div class="paper"><img src="petite-valse.svg" alt="Piano waltz"></div>
    <details><summary>Show the source</summary><pre>{{VALSE_LYS}}</pre></details>
  </div>
  </div>
</div></section>

<section><div class="wrap">
  <p class="eyebrow">Lineage</p>
  <h2>What Lily# owes LilyPond</h2>
  <p class="lede">LilyPond has set the standard for computer-engraved music for decades.
  Lily# does not try to out-engrave it. It carries LilyPond's engraving decisions across —
  with attribution, and under the same licence.</p>

  <div class="cols2">
    <div class="pillar">
      <h3>The engine is a port, not an imitation</h3>
      <p>Beam quanting, slur and tie scoring, skylines, springs and page breaking are
      modified translations of LilyPond's own C++ and Scheme. Every ported file carries the
      copyright notice of the LilyPond file it came from, and
      <code>LILYPOND-ATTRIBUTION.md</code> lists all of them.</p>
      <p>The house rule is that layout code is transliterated from LilyPond's <i>source</i>
      rather than reverse-engineered from its pictures — and that nothing may be tuned merely
      to make the output match byte for byte. Elsewhere, most <code>LILYPOND-REF</code>
      comments are citations rather than ports: they mark where LilyPond decides something, so this
      code can be checked against it.</p>
    </div>
    <div class="pillar">
      <h3>It is an independent project</h3>
      <p><b>Lily# is not affiliated with, endorsed by, or a release of the LilyPond
      project.</b> The name is a nod, not a badge. If something here engraves badly, that is
      Lily#'s bug — please don't take it to the LilyPond maintainers.</p>
      <p>The language is deliberately not LilyPond's: <code>\relative</code>,
      <code>\new Staff</code>, <code>\version</code> and
      <code>&lt;&lt; … \\ … &gt;&gt;</code> are rejected outright, and a chord symbol is
      typed the way it prints. What the two share is engraving knowledge, not syntax.</p>
    </div>
  </div>

  <div class="box" style="margin-top:26px">
    <h3 style="margin:0 0 12px;font-size:17px">Licence</h3>
    <p style="margin:0 0 12px">Lily# is free software under the <b>GNU General Public
    License v3.0 or later</b>. It contains modified code from LilyPond, which is under that
    same licence; the modifications are Lily#'s own and are marked in the files that carry
    them.</p>
    <p class="note" style="margin:0">The fonts ship with it too. <b>Emmentaler</b>, the music
    face, comes from LilyPond (GPL-3.0-or-later / SIL OFL dual licensed, redistributed here
    under the GPL). <b>TeX Gyre Schola</b> and <b>TeX Gyre Heros</b> set every piece of text —
    and provide the metrics it is spaced by — under the GUST Font Licence (LPPL 1.3c). They
    are the same faces LilyPond sets text in.</p>
  </div>
</div></section>

<section><div class="wrap">
  <p class="eyebrow">02 — Grammar</p>
  <h2>Explicit, and only one way to say it</h2>
  <div class="cols2">
    <div>
      <ul class="plain">
        <li><b>No backslashes.</b> <code>\relative</code>, <code>\new Staff</code> and
        <code>&lt;&lt; … \\ … &gt;&gt;</code> are rejected outright, with a diagnostic that
        names the Lily# spelling instead of failing quietly.</li>
        <li><b>Music lives in a part.</b> A stray note at the top level is an error, which
        is what lets a bare <code>key</code> or <code>time</code> always mean the file default.</li>
        <li><b>Order carries meaning, not clauses.</b> A score is a stack of bands: a
        <code>lyrics</code> row under a staff is its verse; a <code>chords</code> row above it
        aligns over it.</li>
        <li><b>Octaves can be absolute.</b> <code>octave absolute</code> anchors a bare
        <code>c</code> to C4 (or to the part's <code>octave N</code>), so a wrong octave stays
        one wrong note instead of cascading.</li>
        <li><b>Repeats go where order is written.</b> <code>|:</code> and <code>:|</code> live in
        the <code>form</code>, because a repeat changes the playing order.</li>
        <li><b>A chord symbol is its own spelling.</b> You write <code>F#m7-5</code> and
        <code>Bb7/D</code> — the glyphs you would read off a chart — rather than encoding them
        as <code>fis:m7.5-</code> and <code>bes:7/d</code>.</li>
      </ul>
    </div>
    <div>
      <pre>{{GRAMMAR_SNIP}}</pre>
      <p class="note" style="margin-top:14px">Every construct, with the corners spelled out,
      is in the <a href="grammar.html">language manual</a>.</p>
    </div>
  </div>
</div></section>

<section><div class="wrap">
  <p class="eyebrow">03 — Speed</p>
  <h2>The preview redraws while you type</h2>
  <p class="lede">The compiler keeps a Roslyn-style red-green tree, so a keystroke reparses
  only the part you touched; the renderer then reuses every system whose width did not
  change. The parse is the cheap half — the time is in layout, and layout is what gets skipped.</p>
</div></section>

<section><div class="wrap">
  <p class="eyebrow">04 — AI</p>
  <h2>It compiles the answer before showing it to you</h2>
  <p class="lede">Select a few bars, press <code>Ctrl+I</code> (<code>Cmd+I</code> on macOS),
  and ask in words —
  <i>"harmonise this in thirds"</i>, <i>"transpose up a fourth"</i>,
  <i>"add a crescendo"</i>. What comes back is not a text diff to squint at: it is a
  rendered score, and it has already been compiled.</p>
  <div class="box"><pre class="mermaid">{{AI_FLOW}}</pre></div>
  <div class="cols2" style="margin-top:26px">
    <ul class="plain">
      <li><b>Broken candidates never reach you.</b> Each one is compiled in-process; its own
      diagnostics go back to the model for up to two repair rounds.</li>
      <li><b>You judge on the notation.</b> The candidate renders beside the original with an
      After/Before toggle.</li>
      <li><b>Nothing is touched until you accept.</b> One <code>WorkspaceEdit</code>, and one
      <code>Ctrl+Z</code> undoes it.</li>
    </ul>
    <ul class="plain">
      <li><b>The model always has the grammar.</b> <code>docs/GRAMMAR_FOR_LLM.md</code> is copied
      into the extension at build time, so the canon ships with it and cannot drift.</li>
      <li><b>Pick bars on the score.</b> Shift-click a range in the preview and transform it —
      the same loop, whether the selection began in text or on the page.</li>
      <li><b>Your model, no telemetry.</b> It runs on your GitHub Copilot models or your own
      API key, and outcomes go to an output channel on your machine.</li>
    </ul>
  </div>
</div></section>

<section><div class="wrap">
  <p class="eyebrow">Architecture</p>
  <h2>How it is put together</h2>
  <p class="lede">The same diagram as in <code>README.md</code>.</p>
  <div class="box"><pre class="mermaid">{{MERMAID}}</pre></div>
</div></section>

<footer><div class="wrap">
  <p style="margin:0 0 10px">Scores: Beethoven and the traditional song are public domain;
  Blues in F, Sketch in C, Air in D and Petite Valse were written for this showcase.
  Engraved by Lily# {{VER}}.</p>
  <p style="margin:0" class="note">Built {{BUILT}}.</p>
</div></footer>

<script src="https://cdnjs.cloudflare.com/ajax/libs/mermaid/10.9.1/mermaid.min.js"></script>
<script>
  if (window.mermaid) {
    var dark = matchMedia('(prefers-color-scheme: dark)').matches;
    mermaid.initialize({ startOnLoad:true, theme: dark ? 'dark' : 'neutral' });
  }
</script>
</body>
</html>
'@

$grammarSnip = Esc @'
// one canonical form per idea
time 6/8
key a minor
octave absolute            // a bare c is C4 here

part gt { clef treble_8 tuning guitar }

section Verse {
  gt     { a,8 e a c' a e | }
  chords prog { Am | }
}

form main { |: Verse :| }  // the repeat lives here

score main {
  chords prog              // a row above ...
  staff  gt                // ... the staff it belongs to
  tab    gt                // and its tablature
}
'@

$aiFlow = Esc @'
flowchart LR
    sel["bars you selected"] --> ask["your prompt"]
    ask --> model["language model"]
    model --> chk{"compiles?"}
    chk -- "no" --> repair["feed the diagnostics back<br/>up to 2 rounds"]
    repair --> model
    chk -- "yes" --> draw["render the candidate score"]
    draw --> you{"accept?"}
    you -- "iterate" --> model
    you -- "yes" --> apply["one WorkspaceEdit<br/>one Ctrl+Z undoes it"]
'@

# The hero is a clip of VS Code when there is one (`hero-vscode.mp4`, optionally with a
# `hero-vscode.webm` beside it), else a screenshot (`hero-vscode.png` or .jpg). The clip
# plays muted and looped, with the screenshot as its poster: what shows before it loads, and
# all a reader who turned motion off (prefers-reduced-motion) sees. Until either exists the
# page shows a placeholder saying what to capture.
$shotFile = @('hero-vscode.png','hero-vscode.jpg') | Where-Object { Test-Path $_ } | Select-Object -First 1
$clips = @('hero-vscode.webm', 'hero-vscode.mp4') | Where-Object { Test-Path $_ }
if ($clips -contains 'hero-vscode.mp4') {
    $poster = if ($shotFile) { ' poster="' + $shotFile + '"' } else { '' }
    $sources = ($clips | ForEach-Object {
        '<source src="' + $_ + '" type="video/' + [IO.Path]::GetExtension($_).TrimStart('.') + '">' }) -join ''
    $heroShot = '<div class="shot"><video class="hero-clip" autoplay muted loop playsinline' + $poster +
                ' aria-label="Typing a .lys file in VS Code while the score preview beside it redraws">' +
                $sources + '</video></div>' +
                '<script>if (matchMedia("(prefers-reduced-motion: reduce)").matches)' +
                ' document.querySelectorAll(".hero-clip").forEach(v => { v.removeAttribute("autoplay"); v.pause(); v.controls = true; });</script>'
    Write-Host "Hero clip: $($clips -join ', ')$(if ($shotFile) { " (poster $shotFile)" })"
} elseif ($shotFile) {
    $heroShot = '<div class="shot"><img src="' + $shotFile +
                '" alt="A .lys file open in VS Code with the live score preview beside it"></div>'
    Write-Host "Hero screenshot: $shotFile"
} else {
    $heroShot = @'
<div class="shot placeholder">
  <div>
    <p class="ph-title">VS Code screenshot goes here</p>
    <p class="ph-body">Save it in this folder as <code>hero-vscode.png</code> and re-run
    <code>build-preview.ps1</code>. The placeholder is replaced automatically — nothing
    else to edit.</p>
    <p class="ph-hint">Suggested shot: a <code>.lys</code> file open on the left with the
    live score preview docked on the right, window roughly 16:10. Light or dark theme both
    work. A visible completion popup, or a squiggle under a bad bar, makes the point
    better than a static editor.</p>
  </div>
</div>
'@
    Write-Host 'Hero screenshot: none yet — placeholder shown (save hero-vscode.png here)'
}

$html = $template.
    Replace('{{HERO_SHOT}}',    $heroShot).
    Replace('{{ODE_LYS}}',      (LysOf 'ode-to-joy')).
    Replace('{{SUN_LYS}}',      (LysOf 'rising-sun')).
    Replace('{{BLUES_LYS}}',    (LysOf 'blues-in-f')).
    Replace('{{SKETCH_LYS}}',   (LysOf 'sketch-in-c')).
    Replace('{{AIR_LYS}}',      (LysOf 'air-in-d')).
    Replace('{{VALSE_LYS}}',    (LysOf 'petite-valse')).
    Replace('{{GRAMMAR_SNIP}}', $grammarSnip).
    Replace('{{AI_FLOW}}',      $aiFlow).
    Replace('{{MERMAID}}',      $mermaid).
    Replace('{{VER}}',          $ver).
    Replace('{{BUILT}}',        (Get-Date -Format 'yyyy-MM-dd'))

Set-Content -Path (Join-Path $here 'index.html') -Value $html -Encoding UTF8
Write-Host "Wrote $(Join-Path $here 'index.html')"
