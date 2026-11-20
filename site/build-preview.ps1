# Builds a local preview page for the Lily# showcase.
# Run from this directory:  pwsh -File build-preview.ps1
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $here

. (Join-Path $here 'site-common.ps1')
function Esc([string]$s) { $s.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;') }
function LysOf([string]$n) { Esc ((Get-Content "$n.lys" -Raw -Encoding UTF8).TrimEnd()) }

# The version is read from the repository, never typed: a hand-written one went stale
# (0.5.0 on a 0.8.0 site). build-site.ps1 fails the build if any other version appears.
$props = Get-Content (Join-Path $here '../Directory.Build.props') -Raw -Encoding UTF8
$ver = [regex]::Match($props, '<Version>([^<]+)</Version>').Groups[1].Value
if (-not $ver) { throw 'No <Version> found in Directory.Build.props' }

$template = @'
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Lily# — sheet music from plain text</title>
<meta name="description" content="Lily#: publication-quality sheet music from plain text, engraved by a port of LilyPond's layout engine, with Lily# Studio, a VS Code extension that previews as you type.">
{{HEAD_META}}
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
  .tagline { font-size:20px; color:var(--muted); margin:0 0 18px; max-width:34em; }
  /* the three ways in, one per line: start, learn, or bring a scan in (the reader's page) */
  ul.entries { list-style:none; margin:0 0 48px; padding:0; font-size:18px; }
  ul.entries li { margin:0 0 6px; }
  ul.entries .entry-note { color:var(--muted); font-size:14px; margin-left:6px; white-space:nowrap; }

  /* auto-fit + minmax reflows on the CONTAINER's width, not the viewport's.
     min-width:0 stops a wide child (a long code line, a big SVG) from forcing
     its track wider than the container — the usual cause of a clipped page. */
  .cols2 { display:grid; grid-template-columns:repeat(auto-fit,minmax(min(320px,100%),1fr)); gap:34px; align-items:center; }
  /* six pillars: 360px min lands them 3 + 3 on a wide screen, 2 + 2 + 2 then 1-up
     as it narrows — every step is a clean divisor of six */
  .cols4 { display:grid; grid-template-columns:repeat(auto-fit,minmax(min(360px,100%),1fr)); gap:24px; }
  .gallery { display:grid; grid-template-columns:repeat(auto-fit,minmax(min(540px,100%),1fr)); gap:48px; align-items:start; }
  .cols2>*, .cols4>*, .gallery>* { min-width:0; }
  /* min(…, 100%): on a phone narrower than a track's minimum the track shrinks to the
     container instead of pushing the page sideways (375 px scrolled before). */

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
  /* A table wider than a phone scrolls inside its own box, never the page. */
  @media (max-width:700px) { table { display:block; overflow-x:auto; } }
  .note { color:var(--muted); font-size:13px; margin:14px 0 0; }

  ul.plain { margin:0; padding-left:1.15em; }
  ul.plain li { margin:0 0 9px; }
  .box { background:var(--card); border:1px solid var(--rule); border-radius:12px; padding:20px; overflow-x:auto; }
  code { font:13px/1.4 "SFMono-Regular",Consolas,monospace; background:var(--code); padding:1px 5px; border-radius:4px; }
  a { color:var(--accent); text-underline-offset:3px; }
  a:hover { text-decoration-thickness:2px; }
  footer { padding:36px 0 64px; color:var(--muted); font-size:14px; border-top:1px solid var(--rule); }

  /* A demo clip. A figure.demo-video carries the data-pending attribute until its file is in
     videos/: a pending one is not shown at all (and build-site.ps1 does not look for its
     file), so the published pages never carry an empty box. Drop the clip into videos/ and
     delete data-pending to publish it. */
  figure.demo-video { margin:18px 0 22px; }
  figure.demo-video[data-pending] { display:none; }
  figure.demo-video video { display:block; width:100%; max-width:960px; height:auto;
                            border:1px solid var(--rule); border-radius:10px; box-shadow:0 2px 14px var(--shadow); }
  figure.demo-video figcaption { color:var(--muted); font-size:14px; margin:8px 0 0; }
  /* A still picture (a scan beside what the reader wrote of it), framed like a clip. */
  figure.shot { margin:18px 0 22px; }
  figure.shot img { display:block; width:100%; max-width:960px; height:auto;
                    border:1px solid var(--rule); border-radius:10px; box-shadow:0 2px 14px var(--shadow); }
  figure.shot figcaption { color:var(--muted); font-size:14px; margin:8px 0 0; }
  kbd { font:12.5px/1.3 "SFMono-Regular",Consolas,monospace; background:var(--code);
        border:1px solid var(--rule); border-bottom-width:2px; border-radius:4px; padding:1px 5px; white-space:nowrap; }
  /* the links along the top of every page (site-common.ps1) */
  .topbar { border-bottom:1px solid var(--rule); padding:14px 0; }
  .topbar .wrap { display:flex; flex-wrap:wrap; gap:18px; align-items:baseline; }
  .topbar a.home { font-weight:700; font-size:18px; text-decoration:none; }
  .topbar span { color:var(--muted); font-size:14px; }
  .topbar nav.pages { margin-left:auto; display:flex; flex-wrap:wrap; gap:6px 18px; font-size:14px; }
  .topbar nav.pages b { font-weight:600; }
  @media (max-width:700px) { .topbar nav.pages { margin-left:0; } }
</style>
</head>
<body>
<div class="topbar"><div class="wrap">
  <a class="home" href="index.html">Lily#</a>
  <nav class="pages">{{NAV}}</nav>
</div></div>
<header><div class="wrap">
  <h1 class="brand">Lily#</h1>
  <p class="tagline">Write music as plain text. See it engraved as you type, hear it played
  and sung, and ask AI to compose or change it — with every note still yours to edit.</p>
  <ul class="entries">
    <li><a href="start.html">Get started &rarr;</a></li>
    <li><a href="learn.html">Learn it lesson by lesson &rarr;</a></li>
    <li><a href="scan.html">Bring in printed music from a scan or PDF &rarr;</a>
      <span class="entry-note">Windows · beta</span></li>
    <li><a href="recording.html">Turn a recording into a chord chart &rarr;</a>
      <span class="entry-note">Windows · preview</span></li>
    <li><a href="sing.html">Have the words of a score sung &rarr;</a></li>
    <li><a href="songs.html">Hear music composed by AI &rarr;</a></li>
  </ul>
  {{HERO_SHOT}}
  <p class="caption">The source and the engraving, side by side. The preview follows your
  keystrokes — it does not wait for a save.</p>
</div></header>

<section id="how"><div class="wrap">
  <p class="eyebrow">In one minute</p>
  <h2>You write music as text. Lily# does the rest.</h2>
  <p class="lede">No dragging notes onto a staff. You type the notes, the chords and the words in
  a plain text file, and the page beside it is always the engraved score of what you typed.</p>
  <ol class="steps">
    <li><b>Write</b><span>Notes are their letters: <code>c4 d e f</code> is four quarter notes.
    A bar ends with <code>|</code>. Words, chords and drums are written the same way, in their
    own lines.</span></li>
    <li><b>See</b><span>The score redraws as you type, in milliseconds, with nothing to save or
    run. Click a note on the page and the editor goes to where it is written.</span></li>
    <li><b>Hear</b><span>Press ▶ and the score plays, every instrument. Loop the bars you
    select, slow them down, turn parts off — and have the words
    <a href="#sing">sung</a>.</span></li>
    <li><b>Ask</b><span>Tell an AI model what you want — <i>“add a bass line”</i> — and judge
    its answer as a score, not as a diff. Or let it <a href="#composed">write a whole
    piece</a>.</span></li>
    <li><b>Share</b><span>The same file goes out as a PDF, as MIDI or audio, or as a
    <a href="#share">video</a> of the page with each note lit as it sounds.</span></li>
  </ol>
  <p class="note" style="margin-top:16px">Lily# is free and open source, and runs inside Visual
  Studio Code. <a href="#install">Install it</a> in a few minutes, or read on to see what it
  does.</p>
</div></section>

<section id="lyrics"><div class="wrap">
  <p class="eyebrow">01 — Words</p>
  <h2>Lyrics are a line of text, bar by bar</h2>
  <p class="lede">You do not attach a syllable to each note in a dialog box. You write the words
  of each bar between the same <code>|</code> that end the music's bars, and Lily# sets each
  syllable under its note.</p>
  <div class="cols2" style="align-items:start">
    <div>
      <pre>melody {
  g4 a b d   | c4 b a r    | b4 c d g   | fis4 e d r |
  e4 fis g e | d4 b g r    | a4 b c a   | b4 a g2    |
}
lyrics words {
  Write a line of   | words for me, | set them on the | mel- o- dy;    |
  press the key and | lend an ear:  | what you wrote is | what you hear. |
}</pre>
      <p class="note">The melody and its words, as they are written in the file. The picture
      is what Lily# engraves from them, with a piano part added.</p>
    </div>
    <ul class="plain">
      <li><b>A space</b> moves to the next note. <b><code>-</code></b> joins the syllables of
      one word (<code>mel- o- dy</code>), and <b><code>~</code></b> holds a syllable over one
      more note.</li>
      <li><b>The bar lines are shared.</b> A bar with one syllable too many is underlined
      where it happens, so a slip stays inside its bar instead of pushing every later word out
      of place.</li>
      <li><b>More verses, more languages</b> are more lines of the same kind.
      <a href="lesson-07-lyrics.html">The lesson on lyrics &rarr;</a></li>
    </ul>
  </div>
  <div class="paper" style="margin-top:28px"><img src="examples/sing-first.svg" style="width:100%" alt="Eight bars of a melody with the words 'Write a line of words for me, set them on the melody' under the notes, over a piano part"></div>
</div></section>

<section id="sing"><div class="wrap">
  <p class="eyebrow">02 — Voice</p>
  <h2>Then press ▶, and the words are sung</h2>
  <p class="lede">One more line — <code>singer female on melody</code> — and a synthetic voice
  sings the lyrics on their notes, in time with the other instruments.</p>
  <div class="cols2" style="align-items:start">
    <figure class="demo-video" style="margin:0">
      <video src="videos/sing-first.mp4" controls playsinline preload="metadata"></video>
      <figcaption>A melody with words and a piano, sung. This is the video Lily# itself
      exports: nothing was recorded.</figcaption>
    </figure>
    <ul class="plain">
      <li><b>It follows your edits.</b> Change a word or a note and only the phrase you touched
      is sung again; the rest is kept.</li>
      <li><b>Free voices, on your own computer.</b> Singing comes from a separate free
      extension, Cantoria, which drives engines you install yourself —
      <b>DiffSinger</b>, <b>NEUTRINO</b> and <b>VOICEVOX</b>. Nothing is sent anywhere.</li>
      <li><b>Several singers, several languages.</b> Each part can have its own voice, and a
      voice sings Japanese — or English, Chinese or Korean where the voice knows them.</li>
      <li><b>Each voice has its maker's terms.</b> The engines and voices are their authors'
      work, downloaded from them, and their conditions of use are theirs.
      <a href="sing.html">How to set up singing &rarr;</a></li>
    </ul>
  </div>
</div></section>

<section id="composed"><div class="wrap">
  <p class="eyebrow">03 — Composed by AI</p>
  <h2>AI writes the score, not a sound file</h2>
  <p class="lede">Because a Lily# score is text, a language model can write one from the first
  bar to the last. What you get back is not a recording you can only replay: it is sheet music
  you can read, play, correct note by note, and print.</p>
  <div class="cols2" style="align-items:start">
    <figure class="demo-video" style="margin:0">
      <video src="videos/songs/Zunda-to-Metan-no-Jikoshokai.mp4" controls playsinline preload="metadata" style="max-width:520px"></video>
      <figcaption>A song for two voices and a band, composed by an AI model in GitHub Copilot's
      chat and sung by two synthetic voices. The whole file is on the
      <a href="songs.html#zundamon-and-metan">songs page</a>.</figcaption>
    </figure>
    <ul class="plain">
      <li><b>Lily# checks what the model wrote.</b> A bar that is too long, a word with no
      note, a name that does not exist: each is reported with its place, and the model fixes
      it before you hear it.</li>
      <li><b>It reviews the piece as music, too.</b> A part that never changes its figure,
      chords too low to sound clear, a melody that is all one rhythm — the model is told what
      to look at, and writes again.</li>
      <li><b>The result stays yours.</b> Open the file, move a note, change a word, ask for
      another verse. Nothing is locked inside a generator.</li>
      <li><b>Seven pieces, with their files:</b> songs in Japanese, Korean, English and
      Chinese, a piano piece, a bossa nova with its harmony explained, and a string quartet.
      <a href="songs.html">Music composed by AI &rarr;</a></li>
    </ul>
  </div>
</div></section>
<section id="ask"><div class="wrap">
  <p class="eyebrow">04 — Ask AI</p>
  <h2>Ask for a change, and judge it as a score</h2>
  <p class="lede">Press <code>Ctrl+I</code> (<code>Cmd+I</code> on macOS) or ✨ in the
  preview, and ask in words —
  <i>"add a bass line"</i>, <i>"harmonise this in thirds"</i>, <i>"transpose up a fourth"</i>.
  What comes back is not a text diff to squint at: it is a rendered score, what changed in
  green, and it has already been compiled.</p>
  <div class="box"><pre class="mermaid">{{AI_FLOW}}</pre></div>
  <div class="cols2" style="margin-top:26px">
    <ul class="plain">
      <li><b>Broken candidates never reach you.</b> Each one is compiled in-process; its own
      diagnostics go back to the model for up to two repair rounds.</li>
      <li><b>You judge on the notation.</b> The candidate renders beside the original with an
      After/Before toggle, the notes the AI changed in green and its own word on what it did;
      ▶ plays it before you decide.</li>
      <li><b>Nothing is touched until you accept.</b> One <code>WorkspaceEdit</code>, and one
      <code>Ctrl+Z</code> undoes it.</li>
    </ul>
    <ul class="plain">
      <li><b>The model always has the grammar.</b> <code>docs/GRAMMAR_FOR_LLM.md</code> is copied
      into Lily# Studio at build time, so the canon ships with it and cannot drift. It reads
      the whole file, and answers with edits.</li>
      <li><b>Point at the bars.</b> Select them in the text or on the score (Shift-click), or
      right-click a note or a bar: the place is a hint of where to look, and the whole score
      stays open to change — a new part brings its score row with it.</li>
      <li><b>Your model, no telemetry.</b> It runs on the language models VS Code offers you —
      GitHub Copilot's, or another provider's — and outcomes go to an output channel on your
      machine. <a href="editor.html#ai">More on the AI features &rarr;</a></li>
    </ul>
  </div>
</div></section>

<section id="editor"><div class="wrap">
  <p class="eyebrow">05 — The editor</p>
  <h2>An editor that knows what a note is</h2>
  <p class="lede">Lily# Studio is not a text editor with colours. It knows the key you are in,
  the chord under the caret and the parts your file has — so one pair of keys does the musical
  thing, wherever you are.</p>
  <div class="cols2" style="align-items:start">
    <div>
      <table>
        <tr><th>The caret is on</th><th><kbd>Ctrl+Shift+Up</kbd> / <kbd>Down</kbd></th></tr>
        <tr><td>a note</td><td>moves it to the next note <b>of the key</b>: in F major,
        <code>a</code> → <code>bes</code> → <code>c</code></td></tr>
        <tr><td>a chord</td><td><b>inverts</b> it: the lowest note goes up an octave</td></tr>
        <tr><td>a chord name</td><td>walks the <b>shapes a guitarist can hold</b>, and draws
        the diagram</td></tr>
        <tr><td>a tempo, a key, a dynamic, a clef</td><td>changes it one step, with no
        typing</td></tr>
      </table>
      <p class="note">With <kbd>PageUp</kbd> / <kbd>PageDown</kbd> instead, a note moves an
      octave and a chord name walks the chords of the key:
      <code>Cmaj7</code> → <code>Dm7</code> → <code>Em7</code> → <code>Fmaj7</code>. The note you
      land on sounds. <a href="editor.html#step">Stepping &rarr;</a></p>
    </div>
    <ul class="plain">
      <li><b>It completes what can come next.</b> After <code>singer female on</code> it offers
      the part names that exist in your file, not every word it knows.</li>
      <li><b>Hover a chord and it names itself</b> — symbol, degree and pitches.</li>
      <li><b>Rename a part</b> and every reference to it moves with it.</li>
      <li><b>Mistakes are underlined where they are,</b> in plain words: a bar one beat too
      long says which bar and by how much.</li>
      <li><b>Practice with it.</b> Loop the bars you select, slower or faster, with a
      metronome and only the parts you choose.
      <a href="editor.html#practice">Practice playback &rarr;</a></li>
      <li><a href="editor.html">Everything Lily# Studio does &rarr;</a></li>
    </ul>
  </div>
</div></section>

<section id="scores"><div class="wrap">
  <p class="eyebrow">06 — One source</p>
  <h2>Write the music once, print it every way you need</h2>
  <p class="lede">The notes are written once. A <code>score</code> block only says what to print
  of them, so another layout is a few more lines — and a note you correct is corrected in all
  of them.</p>
  <div class="cols2" style="align-items:start">
    <pre>score {                 // the full score
  staff vox
  lyrics words
  staff keys
  staff bass
  staff kit
}

score vocal {           // the singer's part alone
  staff vox
  lyrics words
}</pre>
    <ul class="plain">
      <li><b>A conductor's score and each player's part,</b> a lead sheet, a chord chart with
      no staff, a practice excerpt: all are scores of one file, each written out under its own
      name.</li>
      <li><b>Notation and tablature</b> of one guitar part, side by side — and the same
      progression as a guitar book, a ukulele book and a piano score.
      <a href="chords.html">Chords and chord diagrams &rarr;</a></li>
      <li><b>One song in four languages:</b> the same tune with Korean, English, Japanese and
      Chinese words, each a score with its own title and its own singer.
      <a href="songs.html#spring-breeze-bicycle">See it &rarr;</a></li>
    </ul>
  </div>
</div></section>

<section id="share"><div class="wrap">
  <p class="eyebrow">07 — Share</p>
  <h2>A video anyone can follow, and every format a musician asks for</h2>
  <p class="lede">The videos on this page were not filmed. <b>Export as MP4</b> writes the
  engraved pages with their sound, each note lit while it plays and the page turning by
  itself — so someone with no software at all can read along.</p>
  <div class="cols4">
    <div class="pillar"><h3>To read</h3>
      <p>PDF for print, SVG and PNG for a page or a slide.</p></div>
    <div class="pillar"><h3>To hear</h3>
      <p>WAV and MP3 as the preview plays it, MIDI for a sequencer, MP4 with the score on
      screen.</p></div>
    <div class="pillar"><h3>To take elsewhere</h3>
      <p>MusicXML for other notation programs, LilyPond source, and a VOCALOID sequence for
      the vocal line. Printed music comes <i>in</i> too, from a scan or a PDF
      (<a href="scan.html">Windows, beta</a>).</p></div>
  </div>
  <p class="note" style="margin-top:16px">Every export is a command in the editor, and a command
  line tool for batches. <a href="commands.html#export">Export &rarr;</a></p>
</div></section>

<section id="engraving"><div class="wrap">
  <p class="eyebrow">08 — Engraving</p>
  <h2>The page looks like published music</h2>
  <p class="lede">Lily# engraves with LilyPond's own rules — how beams slant, how slurs curve, how
  notes are spaced and pages broken are transliterated from LilyPond's source, not approximated
  from its output. Six pages, one engine. Each is a plain <code>.lys</code> file rendered
  with <code>lysc svg</code>; the source is under every one.</p>

  <div class="gallery">
  <div class="card">
    <h3>Choral octavo</h3>
    <p class="what">An die Freude — Beethoven, four voices with a verse under every staff.</p>
    <ul class="tags"><li>choirStaff</li><li>lyrics rows</li><li>treble_8</li><li>four parts</li></ul>
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
    <code>bes:7/d</code>, and a chart's own spellings (<code>CΔ7</code>, <code>Bø7</code>) read
    too. One rule to know: an altered tension takes <code>+</code> or
    <code>-</code> — <code>F#m7-5</code>, <code>B7-9</code>, <code>A7+5</code> — or goes in
    parentheses (<code>C7(b9, #11)</code>), because
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
    <code>form { |: ~Body [1. ~First] :| [2. ~Second] }</code> is what draws the repeat
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

<section id="grammar"><div class="wrap">
  <p class="eyebrow">09 — Grammar</p>
  <h2>Explicit, and only one way to say it</h2>
  <div class="cols2">
    <div>
      <ul class="plain">
        <li><b>No backslashes.</b> <code>\relative</code>, <code>\new Staff</code> and
        <code>&lt;&lt; … \\ … &gt;&gt;</code> are rejected outright, with a diagnostic that
        names the Lily# spelling instead of failing quietly.</li>
        <li><b>Octaves can be absolute.</b> <code>octave absolute</code> anchors a bare
        <code>c</code> to C4 (or to the part's <code>octave N</code>), so a wrong octave stays
        one wrong note instead of cascading.</li>
        <li><b>Repeats go where order is written.</b> <code>|:</code> and <code>:|</code> live in
        the <code>form</code>, because a repeat changes the playing order.</li>
      </ul>
    </div>
    <div>
      <pre>{{GRAMMAR_SNIP}}</pre>
      <p class="note" style="margin-top:14px">The three ideas the whole language follows are in
      <a href="grammar.html#read">How to read this</a>; every construct, with the corners
      spelled out, is in the <a href="grammar.html">reference</a>.</p>
    </div>
  </div>
</div></section>

<section id="install"><div class="wrap">
  <p class="eyebrow">Install</p>
  <h2>Install it, and write your first score</h2>
  <p class="lede">Two free downloads and a few minutes. Nothing else to set up — no programming,
  no command line.</p>
  {{INSTALL_STEPS}}
  <p class="note" style="margin-top:16px">Stuck? <a href="start.html">Get started</a> goes
  through it again with a first score, step by step.</p>
</div></section>

<footer><div class="wrap">
  <p style="margin:0 0 10px">Scores: Beethoven and the traditional song are public domain;
  Blues in F, Sketch in C, Air in D and Petite Valse were written for this showcase.
  Engraved by Lily# {{VER}}.</p>
  <p style="margin:0 0 10px">Lily# is not affiliated with, endorsed by, or a release of the
  LilyPond project — <a href="about.html">what it owes LilyPond, and its licence</a>.</p>
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

part gt { clef treble_8 tuning guitar }

section Verse {
  gt     { a8 e' a c a e | }   // each note: the octave nearest the last
  chords prog { Am | }
}

form { |: Verse :| }  // the repeat lives here

score {
  chords prog              // a row above ...
  staff  gt                // ... the staff it belongs to
  tab    gt                // and its tablature
}
'@

$aiFlow = Esc @'
flowchart LR
    sel["the score, and where you point"] --> ask["your request"]
    ask --> model["language model"]
    model --> chk{"compiles?"}
    chk -- "no" --> repair["feed the diagnostics back<br/>up to 2 rounds"]
    repair --> model
    chk -- "yes" --> draw["render the candidate score<br/>what changed in green"]
    draw --> you{"accept?"}
    you -- "ask again" --> model
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
    Replace('{{HEAD_META}}',    (Get-HeadMeta 'Lily# — sheet music from plain text' "Publication-quality sheet music from plain text, engraved by a port of LilyPond's layout engine, with Lily# Studio, a VS Code extension that previews as you type." 'index.html').TrimEnd()).
    Replace('{{HERO_SHOT}}',    $heroShot).
    Replace('{{ODE_LYS}}',      (LysOf 'ode-to-joy')).
    Replace('{{SUN_LYS}}',      (LysOf 'rising-sun')).
    Replace('{{BLUES_LYS}}',    (LysOf 'blues-in-f')).
    Replace('{{SKETCH_LYS}}',   (LysOf 'sketch-in-c')).
    Replace('{{AIR_LYS}}',      (LysOf 'air-in-d')).
    Replace('{{VALSE_LYS}}',    (LysOf 'petite-valse')).
    Replace('{{GRAMMAR_SNIP}}', $grammarSnip).
    Replace('{{AI_FLOW}}',      $aiFlow).
    Replace('{{NAV}}',          (Get-SiteNav 'index.html')).
    Replace('{{INSTALL_STEPS}}', $InstallSteps).
    Replace('{{VER}}',          $ver).
    Replace('{{BUILT}}',        (Get-Date -Format 'yyyy-MM-dd'))

Set-Content -Path (Join-Path $here 'index.html') -Value $html -Encoding UTF8
Write-Host "Wrote $(Join-Path $here 'index.html')"
