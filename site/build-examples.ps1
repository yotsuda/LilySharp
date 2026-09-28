# Writes and renders the manual's inline examples.
# Each one is a COMPLETE .lys the reader can paste and run — the manual shows the
# engraved result and the whole source, never a fragment.
# Run:  pwsh -File build-examples.ps1   (build-site.ps1 runs it; any failure fails the build)
param([string]$Lysc = (Join-Path $PSScriptRoot ('../LilySharp.Cli/bin/Release/net10.0/' + ($IsWindows ? 'lysc.exe' : 'lysc'))))
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $here
$lysc = $Lysc
if (-not (Test-Path $lysc)) { throw "lysc not found at $lysc - build LilySharp.Cli (Release) first" }
$dir = Join-Path $here 'examples'
New-Item -ItemType Directory -Force -Path $dir | Out-Null

$examples = [ordered]@{

'octaves' = @'
// Relative is the DEFAULT: each bare pitch takes the octave nearest the note before
// it. `octave absolute` switches that off and anchors a bare c to C4 (or to the part's
// `octave N`), so a wrong octave never cascades — worth it when a generator is writing,
// not by hand.
octave absolute
time 4/4
part melody { clef treble }
section A { melody { c4 c' c'' c, | e g e' g, | } }
form main { ~A }
score main { staff melody }
'@

'durations' = @'
// A duration standing alone repeats the previous event at the new length, and `q`
// repeats the previous CHORD. Only `q` can be displaced: q' is an octave up.
time 4/4
part melody { clef treble }
section A {
  melody { c8 8 8 8 c4 4 | <c e g>4 q q' q | }
}
form main { ~A }
score main { staff melody }
'@

'events' = @'
// Rests, spacers, a full-measure rest, a pitched rest, chords by letter and by
// scale degree, and the pitchless slash note.
octave absolute
time 4/4
key c major
part melody { clef treble }
section A {
  melody {
    c4 r4 s4 g4 | R1 | c4 a,4@rest d4 r4 |
    <c e g>4 <c 3 5>4 <1 3 5>2 | /4 4 8 8 4 |
  }
}
form main { ~A }
score main { staff melody }
'@

'arpeggio' = @'
// `<< … >>` writes a broken chord out: the members play in sequence and divide the
// group's total. A bare number inside is a scale degree, never a duration.
octave absolute
time 4/4
key c major
part melody { clef treble }
section A {
  melody { << c e g >>4 << c 3 5 >>4 << 8 5 3 1 >>2 | << c e g >>2 << c r e >>2 | }
}
form main { ~A }
score main { staff melody }
'@

'connectors' = @'
// A tie joins the SAME pitch, a slur joins different ones, and square brackets
// beam by hand where the meter's own beaming is not what you want.
octave absolute
time 4/4
part melody { clef treble }
section A {
  melody { c4~ c4 d4( e4) | <c e>4( <d f>4) c8[ d e f] | }
}
form main { ~A }
score main { staff melody }
'@

'annotations' = @'
// Articulations, ornaments and dynamics attach with '@'. A hairpin goes on the
// note it starts from and runs to the next dynamic. '.up' forces the side.
octave absolute
time 4/4
part melody { clef treble }
section A {
  melody {
    c4@staccato d4@accent e4@tenuto f4@marcato |
    g4@trill a4@mordent b4@fermata c'4@staccato.up |
    c'4@p@cresc b4 a4 g4@f | f4@f@decresc e4 d4 c4@p |
  }
}
form main { ~A }
score main { staff melody }
'@

'spanners' = @'
// A spanner opens on one note and MUST be closed on another: one that is never
// closed draws nothing at all and says so.
octave absolute
time 4/4
part rh { clef treble }
part lh { clef bass octave 3 }
section A {
  rh {
    c'4@ottava d' e' f' | g'4 a' b' c''@!ottava |
    c'4@rit b a g@!rit | c'1 |
  }
  lh {
    c4@sustain e g e | c4@sustain e g e |   // a second @sustain while down re-pedals
    c4@sustain e g e | c1@!sustain |
  }
}
form main { ~A }
score main { grandStaff { staff rh  staff lh } }
'@

'groups' = @'
// Tuplets nest, grace notes come in three flavours, and `voice { } { }` opens a
// span of simultaneous voices on ONE staff.
octave absolute
time 4/4
part melody { clef treble }
section A {
  melody {
    tuplet 3/2 { c8 d e } tuplet 3/2 { f8 g a } c'2 |
    grace { d16 e } f4 acciaccatura { a16 } b4 appoggiatura { c'8 } d'4 r4 |
    voice { g'2 a'2 } { c'2 c'2 } |
  }
}
form main { ~A }
score main { staff melody }
'@

'barlines' = @'
// Every written '|' closes exactly one measure, so a bar with nothing in it is an
// empty bar. '||' and '|.' decorate the bar behind them; '!' is dashed.
octave absolute
time 4/4
part melody { clef treble }
section A {
  melody { c1 | | d1 || e1 ! f1 |. }
}
form main { ~A }
score main { staff melody }
'@

'repeats' = @'
// A repeat changes the playing ORDER, so it lives in the form, never in the music.
// The endings name sections; the bracket and the repeat dots are drawn from this.
octave absolute
time 3/4
key g major
part melody { clef treble }
section Body   { melody { d'4 g' fis' | g'2. | } }
section First  { melody { a'2. | } }
section Second { melody { g'2.@fermata | } }
form main { |: ~Body [1. ~First] :| [2. ~Second] }
score main { staff melody }
'@

'lyrics' = @'
// A lyric track sings a part. '-' joins syllables of one word, '~' holds the previous
// syllable over one more note, and '|' mirrors the music's barlines.
octave absolute
time 4/4
key f major
part melody { clef treble }
section A {
  melody { f4 g a bes | c'2 a4 f4 | g4 a bes a | f1 | }
  lyrics words sings melody {
    Sing- ing all the | day ~ long, |
    ev- ery word a | song |
  }
}
form main { ~A }
score main { staff melody  lyrics words }
'@

'sings' = @'
// `sings` is a property of the lyric TRACK, and it can be spelled at either site:
// on the definition, on the score row, or both. Spelling it on the ROW is what lets
// ONE set of words serve several staves — here the same track is placed twice.
octave absolute
time 4/4
key c major
part sop { clef treble }
part alt { clef treble }
section A {
  sop { c'4 c' b a | g1 | }
  alt { e4 e g f | e1 | }
  lyrics verse { Sing- ing a song | now | }
}
form main { ~A }
score main {
  staff sop "Soprano"
  lyrics verse sings sop
  staff alt "Alto"
  lyrics verse sings alt
}
'@

'instrument' = @'
// One word sets several things at once: each part's clef, the octave its bare letters
// anchor to, its tuning (so its frets), what it sounds, and its printed name. Neither
// part below says `clef`, `octave` or `tuning` — the preset already did, which is why the
// same letters land at C4 on the guitar and at C3 on the bass.
time 4/4
part gt { instrument guitar }
part bs { instrument bass "Bass" }
section A {
  gt { c4 e g e | }
  bs { c4 e g e | }
}
form main { ~A }
score main {
  staff gt  tab gt
  staff bs  tab bs
}
'@

'override' = @'
// The override vocabulary is four properties. `once` applies to the next note only,
// and `revert` puts the default back.
octave absolute
time 4/4
part melody { clef treble }
section A {
  melody {
    override NoteHead.color = red
    c4 d e f |
    revert NoteHead.color
    once override Stem.transparent = true
    g4 a b c' |
  }
}
form main { ~A }
score main { staff melody }
'@

# ---- chords.html: chords and chord diagrams, step by step

'chord-names' = @'
// Chord names alone. A `chords` row placed directly above a staff aligns over it;
// `@chord(NAME)` puts a name on one note. Neither draws a diagram: a name is only a name.
time 4/4
part melody { clef treble }
section A {
  melody { e'4 d c d | e e e2 | d4 d e d | c1@chord(C) | }
  chords prog { C | Am | Dm7 G7 | | }
}
form main { ~A }
score main { chords prog  staff melody }
'@

'chord-shapes' = @'
// A shape in parentheses after a row's chord draws its diagram under the name. One
// character per string from the low string: x mutes the string, 0 (or o) leaves it open,
// a digit is the fret. Frets 10-15 take a '-' on each side: 8xx88-11, xx-10-12-13-11.
octave absolute
time 4/4
part melody { clef treble }
section A {
  melody { e'2 c'2 | c'2 b2 | c''2 g'2 | }
  chords prog { C(x32010) Am(x02210) | F(133211) G7(320001) | Cm(8xx88-11) Cm(xx-10-12-13-11) | }
}
form main { ~A }
score main { chords prog  staff melody }
'@

'chord-on-note' = @'
// On a note, the shape follows the name inside @chord( ). A shape with no name names
// itself from its notes; @diagram draws a diagram with no name at all.
octave absolute
time 4/4
part melody { clef treble }
section A {
  melody { e'1@chord(C x32010) | c'1@chord(Am7 x02010) | a'1@chord(x02210) | b'1@diagram(x24442) | }
}
form main { ~A }
score main { staff melody }
'@

'chord-notes' = @'
// chord(NAME SHAPE) WRITES the shape's notes: each string's open pitch plus its fret,
// with its string number, so the tab shows the same shape. A bare @chord on it adds the
// name and the diagram. A chord name belongs to the beat, so a rest or a spacer takes one.
time 4/4
part gt { instrument guitar }
section A {
  gt {
    s1@chord(C x32010) | chord(C x32013)1 |
    chord(Am x02210)2@chord chord(F 133211)2@chord | r1@chord(G 320003) |
  }
}
form main { ~A }
score main { staff gt  tab gt }
'@

'chord-all' = @'
// `chordDiagrams all` draws a diagram for EVERY chord name: the shape written for the
// tuning, else its usual shape. The row stands above a ukulele staff, so the diagrams
// are the ukulele's - no layout word needed for that.
layout { chordDiagrams all }
octave absolute
time 4/4
part uke { instrument ukulele "Ukulele" }
section A {
  uke { g'4 e' c' e' | a'2 c'' | a'4 f' c' f' | g'1 | }
  chords prog { C | Am | F | G7(0212) | }
}
form main { ~A }
score main { chords prog  staff uke }
'@

}

$failed = @()
foreach ($name in $examples.Keys) {
    $path = Join-Path $dir "$name.lys"
    Set-Content -Path $path -Value $examples[$name] -Encoding UTF8
    $out = & $lysc check $path 2>&1 | Out-String
    if ($out -notmatch 'No errors found') {
        Write-Host "!! $name" -ForegroundColor Red
        ($out -split "`r?`n" | Where-Object { $_ -match '\S' }) | Select-Object -First 3 | ForEach-Object { "     $_" }
        $failed += $name
        continue
    }
    & $lysc svg $path | Out-Null   # -> $dir/$name.svg (lysc names its output for the book)
    Write-Host "ok  $name"
}
# A failed example would leave the manual with a broken picture and a source that does not
# compile: stop the build instead.
if ($failed) { throw "examples that do not compile: $($failed -join ', ')" }
