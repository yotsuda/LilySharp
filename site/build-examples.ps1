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
// it. `octave absolute` switches that off and anchors a bare c to C3, as LilyPond's
// `\fixed c` does — `c'` is middle C (or to the part's `octave N`) — so a wrong octave
// never cascades: worth it when a generator is writing, not by hand.
octave absolute
time 4/4
part melody { clef treble }
section A { melody { c'4 c'' c''' c | e' g' e'' g | } }
form { ~A }
score { staff melody }
'@

'durations' = @'
// A duration standing alone repeats the previous event at the new length, and `q`
// repeats the previous CHORD. Only `q` can be displaced: q' is an octave up.
time 4/4
part melody { clef treble }
section A {
  melody { c'8 8 8 8 c4 4 | <c e g>4 q q' q | }
}
form { ~A }
score { staff melody }
'@

'events' = @'
// Rests, spacers, a full-measure rest, a pitched rest, chords by letter and by
// scale degree, and the pitchless slash note.
time 4/4
key c major
part melody { clef treble }
section A {
  melody {
    c'4 r4 s4 g'4 | R1 | c,4 a4@rest d4 r4 |
    <c e g>4 <c 3 5>4 <1 3 5>2 | /4 4 8 8 4 |
  }
}
form { ~A }
score { staff melody }
'@

'arpeggio' = @'
// `<< … >>` writes a broken chord out: the members play in sequence and divide the
// group's total. A bare number inside is a scale degree, never a duration.
time 4/4
key c major
part melody { clef treble }
section A {
  melody { << c' e g >>4 << c 3 5 >>4 << 8 5 3 1 >>2 | << c e g >>2 << c r e >>2 | }
}
form { ~A }
score { staff melody }
'@

'connectors' = @'
// A tie joins the SAME pitch, a slur joins different ones, and square brackets
// beam by hand where the meter's own beaming is not what you want.
time 4/4
part melody { clef treble }
section A {
  melody { c'4~ c4 d4( e4) | <c e>4( <d f>4) c8[ d e f] | }
}
form { ~A }
score { staff melody }
'@

'tie-repeats' = @'
// A tie may cross a repeat sign or an ending. I's C ties into A (the next section printed
// and played); A's E ties into ending 1 as an ordinary arc and into ending 2, which is not
// printed next, as an automatic repeat tie.
part vn {
  section I { c'''1~ || }
  section A { c'''1 | e1~ || }
  section B { e'''1 | }
}
form { I |: A [1. B] :| [2. B] }
score { staff vn }
'@

'annotations' = @'
// Articulations, ornaments and dynamics attach with '@'. A hairpin goes on the
// note it starts from and runs to the next dynamic. '.up' forces the side.
time 4/4
part melody { clef treble }
section A {
  melody {
    c'4@staccato d4@accent e4@tenuto f4@marcato |
    g4@trill a4@mordent b4@fermata c4@staccato.up |
    c4@p@cresc b4 a4 g4@f | f4@f@decresc e4 d4 c4@p |
  }
}
form { ~A }
score { staff melody }
'@

'spanners' = @'
// A spanner opens on one note and MUST be closed on another: one that is never
// closed draws nothing at all and says so.
time 4/4
part rh { clef treble }
part lh { clef bass }
section A {
  rh {
    c''4@8va d e f | g4 a b c@!8va |
    c,4@rit b a g@!rit | c1 |
  }
  lh {
    c4@sustain e g e | c4@sustain e g e |   // a second @sustain while down re-pedals
    c4@sustain e g e | c1@!sustain |
  }
}
form { ~A }
score { grandStaff { staff rh  staff lh } }
'@

'groups' = @'
// Tuplets nest, grace notes come in three flavours, and `voice { } { }` opens a
// span of simultaneous voices on ONE staff.
time 4/4
part melody { clef treble }
section A {
  melody {
    tuplet 3/2 { c'8 d e } tuplet 3/2 { f8 g a } c2 |
    grace { d,16 e } f4 acciaccatura { a16 } b4 appoggiatura { c8 } d4 r4 |
    voice { g2 a2 } { c2 c2 } |
  }
}
form { ~A }
score { staff melody }
'@

'barlines' = @'
// Every written '|' closes exactly one measure, so a bar with nothing in it is an
// empty bar. '||' and '|.' decorate the bar behind them; '!' is dashed.
time 4/4
part melody { clef treble }
section A {
  melody { c'1 | | d1 || e1 ! f1 |. }
}
form { ~A }
score { staff melody }
'@

'repeats' = @'
// A repeat changes the playing ORDER, so it lives in the form, never in the music.
// The endings name sections; the bracket and the repeat dots are drawn from this.
time 3/4
key g major
part melody { clef treble }
section Body   { melody { d''4 g fis | g2. | } }
section First  { melody { a''2. | } }
section Second { melody { g''2.@fermata | } }
form { |: ~Body [1. ~First] :| [2. ~Second] }
score { staff melody }
'@

'lyrics' = @'
// A lyric track sings a part. '-' joins syllables of one word, '~' holds the previous
// syllable over one more note, and '|' mirrors the music's barlines.
time 4/4
key f major
part melody { clef treble }
section A {
  melody { f'4 g a bes | c2 a4 f4 | g4 a bes a | f1 | }
  lyrics words sings melody {
    Sing- ing all the | day ~ long, |
    ev- ery word a | song |
  }
}
form { ~A }
score { staff melody  lyrics words }
'@

'sings' = @'
// `sings` is a property of the lyric TRACK, and it can be spelled at either site:
// on the definition, on the score row, or both. Spelling it on the ROW is what lets
// ONE set of words serve several staves — here the same track is placed twice.
time 4/4
key c major
part sop { clef treble }
part alt { clef treble }
section A {
  sop { c''4 c b a | g1 | }
  alt { e'4 e g f | e1 | }
  lyrics verse { Sing- ing a song | now | }
}
form { ~A }
score {
  staff sop "Soprano"
  lyrics verse sings sop
  staff alt "Alto"
  lyrics verse sings alt
}
'@

'instrument' = @'
// One word sets several things at once: each part's clef, its tuning (so its frets), what
// it sounds, and its printed name. Neither part below says `clef` or `tuning` — the preset
// already did. It never moves the octave: a bare c is C3 in every part, as in LilyPond. The
// guitar's treble_8 clef draws that C3 where a guitarist reads it, and the bass, read on its
// own staff, sounds an octave below the page.
time 4/4
part gt { instrument guitar }
part bs { instrument bass "Bass" }
section A {
  gt { c4 e g e | }
  bs { c4 e g e | }
}
form { ~A }
score {
  staff gt  tab gt
  staff bs  tab bs
}
'@

'override' = @'
// The override vocabulary is four properties. `once` applies to the next note only,
// and `revert` puts the default back.
time 4/4
part melody { clef treble }
section A {
  melody {
    override NoteHead.color = red
    c'4 d e f |
    revert NoteHead.color
    once override Stem.transparent = true
    g4 a b c |
  }
}
form { ~A }
score { staff melody }
'@

# ---- chords.html: chords and chord diagrams, step by step

'chord-names' = @'
// Chord names alone. A `chords` row placed directly above a staff aligns over it;
// `@chord(NAME)` puts a name on one note. Neither draws a diagram: a name is only a name.
time 4/4
part melody { clef treble }
section A {
  melody { e''4 d c d | e e e2 | d4 d e d | c1@chord(C) | }
  chords prog { C | Am | Dm7 G7 | | }
}
form { ~A }
score { chords prog  staff melody }
'@

'chord-shapes' = @'
// A shape in parentheses after a row's chord draws its diagram under the name. One
// character per string from the low string: x mutes the string, 0 (or o) leaves it open,
// a digit is the fret. Frets 10-15 take a '-' on each side: 8xx88-11, xx-10-12-13-11.
time 4/4
part melody { clef treble }
section A {
  melody { e''2 c2 | c2 b2 | c'2 g2 | }
  chords prog { C(x32010) Am(x02210) | F(133211) G7(320001) | Cm(8xx88-11) Cm(xx-10-12-13-11) | }
}
form { ~A }
score { chords prog  staff melody }
'@

'chord-on-note' = @'
// On a note, the shape follows the name inside @chord( ): the name over its diagram.
// @diagram draws a diagram with no name at all; .down puts it under the staff.
time 4/4
part melody { clef treble }
section A {
  melody { e''1@chord(C x32010) | c1@chord(Am7 x02010) | a'1@diagram(x02210) | b1@diagram(x24442).down | }
}
form { ~A }
score { staff melody }
'@

'chord-notes' = @'
// chord(NAME SHAPE) WRITES the shape's notes: each string's open pitch plus its fret,
// with its string number, so the tab shows the same shape, and prints the name over its
// diagram; diagram(NAME SHAPE) the same notes under the diagram alone. A chord name
// belongs to the beat, so a rest or a spacer takes one.
time 4/4
part gt { instrument guitar }
section A {
  gt {
    s1@chord(C x32010) | diagram(C x32013)1 |
    chord(Am x02210)2 chord(F 133211)2 | r1@chord(G 320003) |
  }
}
form { ~A }
score { staff gt  tab gt }
'@

'chord-all' = @'
// `chordDiagrams all` draws a diagram for EVERY chord name: the shape written for the
// tuning, else its usual shape. The row stands above a ukulele staff, so the diagrams
// are the ukulele's - no layout word needed for that.
layout { chordDiagrams all }
time 4/4
part uke { instrument ukulele "Ukulele" }
section A {
  uke { g''4 e c e | a2 c | a4 f c f | g1 | }
  chords prog { C | Am | F | G7(0212) | }
}
form { ~A }
score { chords prog  staff uke }
'@

'chord-table' = @'
// A shape table lists the chords that draw, wherever they are named. C draws its usual
// shape and F the listed xx3211 - except in the Chorus, where F is the barre and G draws too.
layout { chordDiagrams guitar { C  F xx3211  section Chorus { F 133211  G } } }
time 4/4
part melody { clef treble }
section Verse  { melody { e''2 c2 | f2 d2 | }  chords prog { C | F G | } }
section Chorus { melody { e''2 c2 | f2 d2 | }  chords prog { C | F G | } }
form { ~Verse ~Chorus }
score { chords prog  staff melody }
'@

'chord-capo' = @'
// A capo on the third fret. The music writes the SOUNDING chords (Eb, Ab, Bb7); the page
// prints what the player presses (C, F, G7), draws the pressed shapes, and says "Capo 3"
// at the head. `chordNames both` prints both: the sounding name, then the pressed one.
layout { chordDiagrams guitar capo 3 all  chordNames both }
title "Capo"
time 4/4
key ees major
part melody { clef treble }
section A {
  melody { g''2 ees2 | aes2 f2 | bes2 d,2 | ees1 | }
  chords prog { Eb | Ab | Bb7 | Eb | }
}
form { ~A }
score { chords prog  staff melody }
'@

'chord-list' = @'
// `chordList true` puts every chord the score names at its head, once each in order of
// first appearance, over the diagram it draws there - the songbook's "chords used" row.
layout { chordDiagrams guitar  chordList true }
title "Chord list"
time 4/4
part melody { clef treble }
section A {
  melody { e''2 c2 | f2 d2 | g2 b,2 | c1 | }
  chords prog { C | F(xx3211) | G | C | }
}
form { ~A }
score { chords prog  staff melody }
'@

# ---- chord-input.html: writing chords as notes

'chord-entry' = @'
// A chord is notes in angle brackets, one duration after the >. Degrees of the key write
// the same chords; q repeats the last chord, and a bare duration repeats it at a new length.
time 4/4
key c major
part piano { clef treble }
section A {
  piano { <c' e g>2 <1 3 5>2 | <2 4 6>4 q q2 | <f a c>4 4 <g b d f>2 | <c e g c'>1 | }
}
form { ~A }
score { staff piano }
'@

'chord-named' = @'
// A bare @chord names a chord from its own notes - a seventh, an inversion, the bass.
time 4/4
part piano { clef treble }
section A {
  piano { <c' e g>2@chord <a c e g>2@chord | <f a c e>2@chord <g b d f>2@chord | <e g c>1@chord | }
}
form { ~A }
score { staff piano }
'@

'chord-broken' = @'
// @arpeggio rolls a chord; << >> writes a broken chord out, its notes sharing the time
// (a bare number inside is a degree of the key). A member may be a chord itself.
time 4/4
part piano { clef treble }
section A {
  piano { <c' e g c'>2@arpeggio <c e g c'>2 | << c e g c' >>2 << c 3 5 8 >>2 | << <c e> g <c e> g >>1 | }
}
form { ~A }
score { staff piano }
'@

'chord-items' = @'
// chord(...) plays a chord and prints its name; with no shape it plays the chord's notes
// close (G B D), and strum after strum the name and diagram stand only where the chord changes.
// chord(x32010) prints the name the shape spells; diagram(...) plays it under the picture alone.
time 4/4
part gt { instrument guitar }
section A {
  gt {
    chord(G)4 chord(G)4 chord(G)4 chord(G)4 | chord(C x32010)4 chord(C x32010)4 chord(D)2 |
    chord(x02210)2 diagram(x24442)2 | chord(G)1 |
  }
}
form { ~A }
score { staff gt }
'@

'chord-items-piano' = @'
// On a piano, chord(...) with no shape plays the chord close, by middle C, and prints its name.
time 4/4
part piano { clef treble }
section A {
  piano { chord(C)2 chord(Am)2 | chord(F)2 chord(G7)2 | chord(C)1 | }
}
form { ~A }
score { staff piano }
'@

'chord-fretted' = @'
// On a guitar, a bare @diagram draws a <...> chord's own notes as they are fretted.
// diagram(NAME SHAPE) writes a shape's notes and draws it; in << >> its strings are
// played in turn, lowest first.
octave absolute
time 4/4
part gt { instrument guitar }
section A {
  gt { <g b d' f'>1@diagram | diagram(C x32010)2 chord(Am x02210)2 | << diagram(G 320003) >>1 | }
}
form { ~A }
score { staff gt  tab gt }
'@

}

# The pictures in chords.html's table of spellings (#rule): one bar of a guitar part for each
# row, on a page as narrow as the bar, so it sits in a table cell.
$cells = [ordered]@{
    'cell-note-chord'          = "c'1@chord(F)"
    'cell-note-chord-shape'    = "c'1@chord(F 133211)"
    'cell-note-chord-of-shape' = "c'1@chord(x32010)"
    'cell-note-diagram'        = "c'1@diagram(133211)"
    'cell-notes-chord'         = "<f a c'>1@chord"
    'cell-notes-diagram'       = "<f a c'>1@diagram"
    'cell-item-chord'          = 'chord(F)1'
    'cell-item-chord-shape'    = 'chord(F 133211)1'
    'cell-item-chord-of-shape' = 'chord(x32010)1'
    'cell-item-diagram'        = 'diagram(133211)1'
    'cell-repeat-notes-q'      = "<f a c'>4@diagram q2 q4"
    'cell-repeat-item-q'       = 'chord(F 133211)4 q2 q4'
    'cell-repeat-item-length'  = 'chord(F 133211)4 4 2'
    'cell-repeat-item-strum'   = 'chord(F 133211)4 chord(F 133211)2 chord(F 133211)4'
}
foreach ($cell in $cells.GetEnumerator()) {
    $examples[$cell.Key] = @"
// $($cell.Value) - one bar for the table of chord spellings (chords.html).
octave absolute
time 4/4
paper { paperWidth 70mm  paperHeight 0  leftMargin 2mm  rightMargin 2mm  topMargin 2mm  bottomMargin 2mm  indent 0 }
part gt { instrument guitar }
section A { gt { $($cell.Value) | } }
form { ~A }
score { staff gt }
"@
}

# A table cell's picture is cut to its staff: the page's right edge where the staff ends, as
# far past it as the staff starts from the left edge (a single line keeps its ideal width, so
# each bar ends elsewhere). Every cell is then drawn at one scale, 7.5 px a staff space.
function Set-CellWidth([string]$svg) {
    $t = Get-Content $svg -Raw
    $shift = [double]([regex]::Match($t, '<g transform="translate\(([-\d.]+),').Groups[1].Value)
    $staff = [regex]::Match($t, '<line x1="([-\d.]+)" y1="[-\d.]+" x2="([-\d.]+)" y2="[-\d.]+" stroke="#000000" stroke-width="0\.100"/>')
    $left = $shift + [double]$staff.Groups[1].Value
    $width = $shift + [double]$staff.Groups[2].Value + $left
    $height = [double]([regex]::Match($t, 'viewBox="0 0 [-\d.]+ ([-\d.]+)"').Groups[1].Value)
    $inv = [Globalization.CultureInfo]::InvariantCulture
    $w = $width.ToString('0.##', $inv)
    $t = [regex]::Replace($t, '<svg ([^>]*?)width="[-\d.]+" height="[-\d.]+" viewBox="0 0 [-\d.]+ ([-\d.]+)"',
        { param($m) "<svg $($m.Groups[1].Value)width=`"$(($width * 7.5).ToString('0.#', $inv))`" height=`"$(($height * 7.5).ToString('0.#', $inv))`" viewBox=`"0 0 $w $($m.Groups[2].Value)`"" }, 'None', [TimeSpan]::FromSeconds(5))
    $t = [regex]::Replace($t, '<rect x="0" y="0" width="[-\d.]+"', "<rect x=`"0`" y=`"0`" width=`"$w`"")
    Set-Content -Path $svg -Value $t -Encoding UTF8 -NoNewline
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
    if ($cells.Contains($name)) { Set-CellWidth (Join-Path $dir "$name.svg") }
    Write-Host "ok  $name"
}
# A failed example would leave the manual with a broken picture and a source that does not
# compile: stop the build instead.
if ($failed) { throw "examples that do not compile: $($failed -join ', ')" }
