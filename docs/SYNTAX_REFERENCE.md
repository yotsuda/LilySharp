# Lily# Syntax Reference

Complete reference for the `.lys` music notation language.

## Pitches

### Basic Pitch Names

| Pitch | Name |
|-------|------|
| `c` | C |
| `d` | D |
| `e` | E |
| `f` | F |
| `g` | G |
| `a` | A |
| `b` | B |

### Accidentals

| Suffix | Meaning | Example |
|--------|---------|---------|
| `is` | Sharp | `cis` = C# |
| `es` | Flat | `ees` = Eb |
| `isis` | Double sharp | `cisis` = C## |
| `eses` | Double flat | `deses` = Dbb |

Special forms: `ees` (Eb), `aes` (Ab), `bes` (Bb).

Annotations:

```
a4@courtesy    // Courtesy (cautionary) accidental — parenthesized, left of the note
c4@editorial   // Editorial (suggestion) accidental — small, above the note (musica ficta)
```

### Octave Marks

| Mark | Meaning |
|------|---------|
| `'` | One octave up |
| `''` | Two octaves up |
| `,` | One octave down |
| `,,` | Two octaves down |

Default starting octave is C4 (middle C). Each pitch takes the octave
closest to the previous note (an interval of a fourth or less); `'` and `,`
shift octaves on top of that.

> **Octave mode — relative (default) vs absolute.** By default Lily# resolves
> octaves *relative* to the previous note (the "nearest octave" rule above). It is
> compact, but a wide leap can leave the line an octave from where you meant it,
> and a repeated figure like `c c g g` can walk steadily downward. To write
> **fixed** octaves instead — bare `c` always means C4, and `'` / `,` are absolute
> per-note offsets — put **`octave absolute`** at the top of your file (return to
> the default with `octave relative`):
>
> ```
> octave absolute
> c' d' e' c'       // always C5 D5 E5 C5 — no drift, whatever the leaps
> ```

```
c d e f g a b c   // C4 D4 E4 F4 G4 A4 B4 C5 — bare c after b is already C5
c' c,             // C6 C5 — marks shift from the nearest octave
```

Each **phrase body** evaluates in a fresh frame — the default octave, pitch
and duration — regardless of where the phrase is referenced, so a phrase
always means the same notes at every call site. What flows out is the
phrase's **anchor** — its first note's bare letter, shifted with the
reference's own marks — exactly like a chord: a note written after the
reference is relative to that anchor, never to how the body ends.
**Section boundaries** also reset the frame.

## Durations

| Value | Name | Beats (in 4/4) |
|-------|------|-----------------|
| `1` | Whole | 4 |
| `2` | Half | 2 |
| `4` | Quarter | 1 |
| `8` | Eighth | 1/2 |
| `16` | Sixteenth | 1/4 |
| `32` | Thirty-second | 1/8 |
| `64` | Sixty-fourth | 1/16 |
| `128` | One-twenty-eighth | 1/32 |

### Dotted Notes

| Notation | Effect |
|----------|--------|
| `4.` | Dotted quarter (1.5 beats) |
| `2.` | Dotted half (3 beats) |
| `4..` | Double-dotted quarter (1.75 beats) |

### Default Duration

If no duration is specified, the previous note's duration is used:

```
c4 d e f   // All quarter notes
c8 d e f   // All eighth notes
```

### Bare Durations (repeat the previous event)

A duration standing **alone** repeats the previous note, chord or slash at the
new length — the same reading LilyPond gives an isolated duration:

```
bes8 8 8 8 8 8 8 8    // an eighth-note bass pump, written once
<c e g>4 4 2          // the chord again, quarter then half
c'4 r4 4              // rests are transparent: the last 4 is c' again
```

The bare number is a written duration, so it also sets the running default.
Rests and the empty chord `<>` are transparent to the run; an arpeggio breaks
it; a bare duration with nothing before it to repeat is an error (LYS0016).
The repeat keeps the original's absolute pitch (it is transparent to the
relative frame, like `q`) and takes only its own post-events.

A repeat that reaches back **across a barline** (`c4 d e f | 4 g f e`) is legal
but warns (LYS1031): a measure opening on a bare number is also what a dropped
pitch letter looks like (`4 g f e` meant as `a4 g f e`). Write the event itself
at the measure head when the repeat is meant; within-measure runs never warn.

## Notes, Rests, and Chords

### Notes

```
c4         // C quarter note
fis8       // F# eighth note
bes2.      // Bb dotted half note
```

### Slash Notes (rhythm notation)

`/` in note position is a pitchless note drawn as a **slash head on the middle
staff line** — comping rhythm. Duration carry, stems and beams behave as on an
ordinary note; playback is silent. Combine with a one-line staff
(`staff comp as lines 1` in the score — the line count is a property of the
rendering, so the same part can keep five lines elsewhere) for a rhythm chart:

```
/4 / / /              // four beat slashes
/8 8 /4 8 8 /4        // a comping figure (bare durations continue the run)
/4 4 g8 g /4 /        // ensemble kicks mix with pitched notes
```

`time 4/4`, `tuplet 3/2` and a chord entry's `c/g` keep their own `/` — only
the note position reads it as a slash.

### Rests

| Syntax | Meaning |
|--------|---------|
| `r4` | Quarter rest |
| `r2` | Half rest |
| `s4` | Spacer rest (invisible) |
| `R1` | Full-measure rest |
| `a4@rest` | Quarter rest placed where the note `a` would sit |

A rest normally finds its own height: on the middle line alone, up or down inside a
`voice { } { }` span, and out of the way of whatever the other voices are playing at
that moment. **Writing a pitch with `@rest` decides that height instead** — the rest
sits where that note would, and nothing moves it afterwards. Use it when two voices'
rests would otherwise land on top of each other, which is the one case the automatic
placement leaves alone:

```
octave absolute
part v { }
section Main {
  v {
    voice
    { g'8 g' g' r8 r2 | }
    { a,4@rest c r2 | }    // this rest sits at a — clear of the other voice's
    { c'4 c' f'2@rest | }  // and this one at f, on a staff line
    { r2 g | }
  }
}
form main { ~Main }
score main { staff ~v }
```

The pitch is only a height: it never sounds, never prints an accidental, and never
takes a ledger line of its own (a whole or half rest that lands off a staff line
carries a short one inside its own glyph, as it does anywhere else).

### Chords

Notes enclosed in angle brackets share a duration (written after the `>`):

```
<c e g>4       // C major triad, quarter note
<d fis a>2     // D major triad, half note
<c 3 5>4       // the same triad by scale DEGREES: root + 3rd + 5th of the key
<1 3 5>2       // degrees only — anchored on the key's tonic (C E G in C major)
<2 4 6>2       // the ii triad (D F A in C major); degrees follow the key
```

**Octaves — the anchor model.** One rule: *a mark moves only what it is attached to.*

- The chord's **anchor** is the first member's bare **letter** (or the key **tonic**
  when the chord is degrees-only), resolved nearest to the previous note. The note
  *after* the chord is relative to the anchor.
- Every member sits at-or-above the anchor, so the written order doesn't matter
  (`<c e g>` = `<c g e>`); only the first slot does (`<g c e>` anchors on g).
  Degrees are fully order-independent (`<2 4 6>` = `<6 2 4>`).
- A `'` / `,` **on a member** moves *that one note* only — the first member's
  included: `<c' e g>` = C5 E4 G4, and the next bare `c` is still C4.
- A `'` / `,` **after the `>`** (before the duration) moves the **whole chord**, anchor
  included — so it *propagates*: after `<c e g>'4` (C5 E5 G5) a bare `c` continues at C5.

```
<c e g>4       // C4 E4 G4
<c g,>4        // C4 G3 — a member ',' drops that one note
<c' e g>4      // C5 E4 G4 — the root's mark is local; next bare c = C4
<c e g>'4      // C5 E5 G5 — whole chord up; next bare c = C5
```

### Arpeggios (`<< … >>`)

An arpeggio is a *written-out* broken chord: the members play in **sequence** and
**equally subdivide** the group's total duration (members carry no durations of their
own — a bare number is always a scale degree). Octaves follow the chord anchor model:

```
<< c e g >>         // c, then e and g stacked above it (E4, G4) — an ascending arpeggio
<< c g >>           // g is a fifth ABOVE c, exactly like the chord <c g>
<< c g e >>         // same pitches as << c e g >>, only the play order differs
<< c 3 5 >>         // by degrees: c e g
<< 8 5 3 1 >>       // degrees-only anchors on the TONIC: C5 G4 E4 C4 — descending, no marks
<< c e g >>'        // the whole group an octave up; the next bare note follows it there
```

Members may be **chords** or **rests**:

```
<< <c e> g >>       // a chord member, then g — an arpeggio of stacked members
<< c r e g >>       // the rest is a gap (an equal share of the total)
```

Without a trailing duration the group takes the running duration and acts like one
note; a **duration after `>>`** sets the group's total. Either way the members split
it equally, becoming an automatic tuplet when needed:

```
<< c e g >>         // after c4: three notes in a quarter → a triplet of eighths
<< c e g >>2        // three in the time of a half → triplet quarters (3:2)
<< c d e f g >>4    // five in a quarter → a quintuplet
```

The group must fit within one measure (otherwise it crosses the barline and the measure
overflows its meter).

> **Note:** this reuses `<< … >>`, which in LilyPond means simultaneous voices. Lily#
> writes parallel voices as `voice { … } { … }` (§ Voices), so `<< … >>` is free to
> mean an arpeggio here. A `\\` inside still reports the removed-polyphony hint.

## Articulations

Articulations are attached to notes with the `@` prefix. Names are resolved from
text, not reserved keywords, so a word like `tr` or `accent` stays usable as an
ordinary identifier (say, a phrase name) elsewhere:

```
c4@staccato    // Staccato
d4@accent      // Accent
e4@tenuto      // Tenuto
f4@marcato     // Marcato
g4@fermata     // Fermata
a4@portato     // Portato (tenuto + staccato)
```

### Placement (`.up` / `.down`)

By default an articulation sits opposite the stem. Append `.up` or `.down` to force it
above or below the note:

```
c4@staccato.up     // staccato forced above
d4@accent.down     // accent forced below
```

## Ornaments

```
c4@trill         // Trill
d4@mordent       // Mordent
e4@prall         // Inverted mordent (pralltriller)
f4@turn          // Turn
g4@invertedturn  // Inverted turn
```

## Dynamics

Dynamics use `@` prefix (or `\` for LilyPond compatibility):

```
c4@ppp   // Pianississimo
c4@pp    // Pianissimo
c4@p     // Piano
c4@mp    // Mezzo piano
c4@mf    // Mezzo forte
c4@f     // Forte
c4@ff    // Fortissimo
c4@fff   // Fortississimo
```

Dynamics sit below the staff by default. Append `.up` / `.down` to force the side:

```
c4@f.up      // forte above the staff
d4@p.down    // piano below (the default)
```

### Hairpins (Crescendo/Decrescendo)

```
c4@p @cresc d e f |
g4@f @decresc a b c |
```

`.up` / `.down` cannot be applied to `@cresc` / `@decresc` / `@dim` — a hairpin is
always engraved below the staff, so a placement suffix there is rejected as an error
rather than silently ignored. (Placement works on dynamic *levels* like `@f.up`.)

## Ties and Slurs

### Ties

Connect notes of the same pitch with `~`:

```
c4~ | c4 d e f   // C tied across barline
```

### Slurs

Connect different pitches with `(` and `)`:

```
c4( d e f)        // Slur over four notes
c4( d) e( f)      // Two separate slurs
```

## Barlines

| Syntax | Type |
|--------|------|
| `\|` | Single barline |
| `\|\|` | Double barline |
| `\|.` | Final barline |
| `\|:` | Repeat start |
| `:\|` | Repeat end |

## Key Signature

```
key c major      // C major (no accidentals)
key g major      // G major (1 sharp)
key f major      // F major (1 flat)
key a minor      // A minor (no accidentals)
key d minor      // D minor (1 flat)
```

## Clef

```
clef treble      // Treble clef (G clef)
clef bass        // Bass clef (F clef)
clef alto        // Alto clef (C clef, line 3)
clef tenor       // Tenor clef (C clef, line 4)
```

## Time Signature

```
time 4/4         // Common time
time 3/4         // Waltz time
time 6/8         // Compound duple
time 2/2         // Cut time
```

## Tempo

```
tempo 120             // Quarter = 120 BPM
tempo "Allegro" 4 = 120  // With text marking
tempo 120 swing       // + swing/shuffle feel equation beside the mark
tempo 120 swing 16    // sixteenth-note swing (double-beamed)
```

Adding `swing` (or `shuffle`) after the tempo draws the swing equation — straight
notes = a beamed dotted + plain note under a triplet `3` — to the right of the
metronome mark, the way shuffle charts are headed. A trailing number picks the note
value that swings: `swing` (= `swing 8`) for eighths, `swing 16` for sixteenths
(double-beamed). The words are contextual, not reserved, so `swing` / `shuffle`
stay usable as your own names.

## Metadata

```
title "Sonata in C"
composer "W.A. Mozart"
```

## Text Fonts

The whole document in one face — that is the two generic families, bound together:

```
fonts {
  serif "Georgia"
  sans  "Georgia"
  embedded          // also subset-embed it in the PDF
}
```

⚠️ **`fonts` is plural, and takes a BLOCK.** There is no `font` keyword and no one-line
form; a bare value (`fonts "Georgia"`) is an error that quotes your face name back inside
the block to write instead. Completing `fonts` in the editor inserts the block pre-filled
with the faces already in use, so accepting it changes nothing until you edit a face.

Or a face per kind of text:

```
fonts {
  serif     "Georgia"                       // everything serif, unless overridden below
  sans      "Verdana"                       // chord symbols are the engine's one sans

  lyricText "Charis SIL" "Noto Serif CJK JP"  // a fallback chain, most preferred first
  title     "Cormorant"
  chordName serif                           // point a role at the OTHER bundled family
  marks     "Georgia"                       // a whole group at once
  tempo     "Playfair Display"              // ...and one member of it, overriding the group

  embedded
}
```

**The narrower spelling wins**, in either source order: `role` beats `group` beats
`serif`/`sans` beats the bundled face. So the `marks`/`tempo` pair above needs no
special case.

The keys, by group:

| Group | Roles it covers |
|---|---|
| `header` | `title` `composer` `instrument` |
| `lyrics` | `lyricText` `stanza` |
| `chords` | `chordName` `fretFrame` `figuredBass` |
| `marks` | `tempo` `mark` `pedal` `navigation` `text` `dynamics` `partCombine` |
| `numbers` | `barNumber` `fingering` `tuplet` `volta` `ottava` `bend` `tabTechnique` |
| `notation` | `clefOctave` `meter` `tabFret` |

⚠️ **`notation` is not reached by a `serif`/`sans` binding.** The
octave digit under a `treble_8` clef, a compound meter's `+`, and tab fret numbers are
notation that happens to be drawn as text — restyling them changes the notation rather
than the words — so they follow a face only when you name `notation` or the role itself.

**A named face is measured, not only drawn** (since 2026-08-18). The layout reserves space
with the same file the string is drawn in, so a title in a wide face gets a wide box.
Before this the reservation always used the bundled face, and on ordinary strings the drawn
width ran −2.05 to +3.61 staff spaces from the reserved one.

⚠️ **So a score that names a face lays out differently on a machine that does not have it**
— there the reservation falls back to the bundled face. LilyPond has the same exposure for
the same reason (its `font-name` goes to fontconfig), and it is why a missing face warns
rather than passing quietly. A score that names nothing is unaffected: the bundled faces
ship with the engine.

⚠️ **`embedded` does one thing**: it subsets the named faces into an exported PDF. It does
not change how anything is measured or drawn.

Weight and slant belong to the engraving and cannot be set here.

Unknown keys are an error (a binding that reaches nothing looks exactly like one that
works), a key bound twice in one block is a warning and the last wins, and `mono` is not a
key because no text in this engine is monospace.

A face this machine does not have is a **warning**, with or without `embedded` — whether a
font is installed is a property of the machine and not of the source, so a score that is
right on your box must not fail to compile on a runner that has no fonts.

**A named block is per-score.** `fonts NAME { … }` at the top level declares a reusable
block that binds nothing by itself; a score references it as `fonts NAME`, or overrides
part of it with `fonts NAME { lyricText "…" }`:

```
fonts house { serif "Georgia"  lyricText "Charis SIL" }

score main  { fonts house  staff melody }
score parts { fonts house { lyricText "Noto Serif CJK JP" }  staff melody }
```

The reference **replaces** the file's unnamed default for that score, and the override
block reads as if its entries were written at the end of the named block — the same key
written again wins, with no duplicate warning across the two blocks. ⚠️ **The
narrower-spelling rule keeps winning whichever block a binding came from**: the house
block's `lyricText` (a role) beats the score's `lyrics` (its group) — deliberately, so a
house style's role choices survive a score swapping the broad base. Override a role with
the same or a narrower key. An unknown reference name is an error naming the declared
blocks; a named block no score references is a warning; a second reference in one score
warns and the last wins.

## Paper

The page's dimensions — paper size, margins, indents, and the vertical spacing specs.
One block per file. Every default equals LilyPond's a4 default, so an absent block, an
empty one, and one that states the defaults all lay out identically:

```
paper {
  paperWidth 210mm
  paperHeight 297mm            // 0 = one content-driven page
  leftMargin 15mm  rightMargin 15mm
  topMargin 10mm  bottomMargin 10mm
  indent 15mm  shortIndent 0
  raggedRight                  // bare flag: lines keep their ideal width
  spacingIncrement 1.2         // horizontal note-spacing unit
  systemSystemSpacing { basicDistance 12  minimumDistance 8  padding 1  stretchability 60 }
  staffStaffSpacing   { basicDistance 9 }
}
```

**A bare number is staff spaces** — the unit everything else in this language is measured
in. A physical unit is a word **glued** to its number, one quantity: `210mm`, `29.7cm`,
`8.5in` (LilyPond spells the same thing `210\mm`). A spaced `210 mm` is an error naming
the glued spelling. The conversion is the one the engine's defaults were computed with
(1 staff space = 5 TeX points), rounded the same way — so writing a default out **is**
the default, byte for byte.

**A whole page by name**: `size b5` sets the width, the height **and** the four
margins, scaled the way LilyPond's `set-paper-size` scales them — each margin default by
the size's ratio to a4, rounded to whole millimetres, so `size a4` is the identity and
`size b5` gives 13mm sides and 8mm top/bottom. The name is **bare**, like every closed
vocabulary's values (`clef treble`, `tuning standard`); quote only a name that carries a
space (`size "ansi a"`) — the lyric syllable's rule. The names are LilyPond's paper
table — `a0`…`a10`, `b0`…`b10`, `c0`…`c10`, `letter`, `legal`, `tabloid`, `ledger`, and
the rest — plus **`jisb5`** (182 × 257 mm), which is Lily#-own: ISO `b5` (176 × 250) is
not the Japanese B5, and Japanese sheet music commonly uses JIS B5. `size` reads at its
position like every other key — write it first, then refine
(`size b5  topMargin 12mm`).

The spacing blocks, each taking `basicDistance` / `minimumDistance` / `padding` /
`stretchability` lines (`stretchability` is unitless):

| Key | The pair it spaces |
|---|---|
| `systemSystemSpacing` | two consecutive systems |
| `scoreSystemSpacing` | a score boundary, then the next system |
| `markupSystemSpacing` | a title/markup, then the next system |
| `scoreMarkupSpacing` | a system, then the next title/markup |
| `markupMarkupSpacing` | consecutive titles/markups |
| `topSystemSpacing` | the page top and the first system |
| `lastBottomSpacing` | the last element and the page bottom |
| `staffStaffSpacing` | two staves of a group |
| `staffGroupStaffSpacing` | a group's staff and the next group's |
| `defaultStaffStaffSpacing` | ungrouped staves |
| `nonStaffRelatedStaffSpacing` | a lyrics/chord row and its own staff |
| `nonStaffUnrelatedStaffSpacing` | a lyrics/chord row and an unrelated staff |
| `nonStaffNonStaffSpacing` | two lyrics/chord rows |

⚠️ **The staff-spacing family lives here, not in `override`**, although LilyPond keeps it
on grobs (`StaffGrouper.staff-staff-spacing`): these quantities are applied score-wide in
one pass, and `paper { }` is the spelling whose meaning is score-wide — an override would
parse a scope (`once`, staff tags) and then silently not apply it.

There is deliberately **no staff-size key** (the staff space is the unit itself; scaling
it is a different feature) and **no algorithm switch** (line/page-breaking strategy is
engine tuning, not a dimension of the picture).

Unknown keys are an error, a key set twice in one block is a warning and the last wins,
and a second unnamed `paper { }` block warns like every repeated global setting.

**A named block is per-score**, the same shape as a named fonts block: declare
`paper wide { paperWidth 250mm }` at the top level, reference it as `paper wide` inside
a score, or override part of it there (`paper wide { topMargin 12mm }`). The reference
replaces the file's unnamed default for that score; a spacing block's unwritten lines
keep the named block's values. One file can then carry a wide conductor page and
default part pages.

## Grace Notes

```
grace { d16 e } f4          // Grace notes before F
acciaccatura { a16 } b4     // Slashed grace (takes no time)
appoggiatura { c8 } d4      // Unslashed grace
```

**A grace body is parsed as a full music block, and engraved much more narrowly than that.**
The engraver reads a **bare note's pitch and its duration VALUE** out of the body and nothing
else. Everything else written inside is not drawn, and Lily# says so at what was written
(LYS4020):

```
grace { d16@staccato } c4     // the staccato is not drawn      (LYS4020)
grace { d16. } c4             // the dot is not drawn           (LYS4020)
grace { d16( e16) } c4        // the slur is not drawn          (LYS4020)
grace { d16 r16 } c4          // the rest is not drawn          (LYS4020)
grace { <d f>16 } c4          // no grace at all is drawn       (LYS4020)
```

**Two annotations ARE carried, and the line between them and the rest is whether they want a
column of their own on the page:**

* `grace { d16@mark("A") } c4` — the rehearsal mark. It is not the note's mark: its grob
  belongs to the bar (LilyPond consists `Mark_engraver` in the `Score` context), so a grace
  note never had to carry it.
* `grace { a,16\2 } b,8` — the string number. It is not drawn at all; it is what the tab's
  fret resolver reads, so a grace note on a `tab` staff takes the string you asked for.

A grace body with at least one bare note keeps its grace; only the parts of the body that are
not bare notes go missing.

LilyPond draws all of the spellings above, so each warning says "not drawn yet", not "do not
write this".

## Tuplets

```
tuplet 3/2 { c8 d e }       // Triplet: 3 eighth notes in time of 2
tuplet 5/4 { c16 d e f g }  // Quintuplet
```

Nested tuplets are supported:

```
tuplet 3/2 {
  c8 d tuplet 3/2 { e16 f g } |
}
```

## Cue Notes

A cue quotes another instrument in small type. It is a **region**, not a mark on a note —
there is no `@cue` — because that is what it is in LilyPond too: `cue { … }` becomes a
`CueVoice` context, whose size is a property of the context and not of any note in it.

```
c4 d cue { e4 f } g4 |      // The two notes inside are cue-sized
c4 d cue bass { e4 f } g4 | // Read in the quoted instrument's clef
```

Naming a clef writes it before the region and restores the staff's own clef after it, so
the following notes are unaffected. Any clef name works: `treble`, `bass`, `alto`, `tenor`,
`treble_8`.

A cue is a **voice of its own**, which decides what may cross its edge:

```
c4 cue { e4( f) } g4 |      // A slur closing inside the cue
c4( cue { e4 f } g4) |      // A slur passing OVER the cue - both ends outside it
```

A slur or a tie with one end inside the cue and the other outside is rejected (**LYS4012**).
LilyPond cannot engrave such a span at all — it drops it, in one direction without even a
warning — so close the span inside the region, or move the note it reaches for out of it.
The same applies between **two cue regions written side by side** — `cue { … } cue { … }` is
two voices, not one — so `c4 cue { e4( f } cue { g4) }` is rejected for the same reason, even
though both ends of that slur are cue notes.

Two other shapes are closed while the feature is young: a `cue` nested in a `cue`
(**LYS4013**) and a `voice { … } voice { … }` span inside one (**LYS4014**).

## Repeats

### Volta Repeats

Volta repeats are written symbolically with `|: … :|` repeat barlines and inline
volta endings `[1. …] [2. …]`. The play count defaults to 2 (or, when endings are
present, the highest volta number); give it explicitly with `|: … :|*N`.

```
{ |: c4 d e f | [1. g2 g | ] :| [2. a2 a | ] }
```

Endings accept ranges and lists: `[1-2. … ]`, `[1,3. … ]`. Without endings, a bare
`|: … :|` simply repeats the body (twice by default, or `|: … :|*N` times).

An ending needs a repeat to be an ending *of*. In a `form`, an ending that no repeat
opens — `form main { A [1. B] }` — draws no bracket and no number: it engraves as the
plain reference `B`, played once, and **LYS6008** warns that the `1.` prints nothing.
This is LilyPond's behaviour for the same shape. Note that it is the *tree* that
decides, not the reading order: in `|: A [1. D] :| [2. O]` the ending written after the
`:|` still belongs to that repeat, while in `|: A :| B [1. B]` it does not.

> Note: `repeat volta` / `alternative` are **not** Lily# constructs — the parser
> rejects them with a hint to use the symbolic form above. The `repeat` keyword
> survives only for `unfold` / `percent` / `tremolo` (see below).

#### One-sided repeat barlines

The two halves are **not** symmetric.

- A `:|` with no `|:` open **repeats from the beginning of the piece** — the
  ordinary reading of a one-sided end-repeat. It is not an error.
- A `|:` that no `:|` ever closes **is an error** (LYS4017): where the repeat ends
  is undefined.

The pair may be written **across layers**: a `|:` in a section's music can be
closed by a `:|` the `form` writes, because a section is not a piece of music on
its own — it becomes one when a `form` lays it out. So the pairing is judged on the
laid-out score, not on either layer alone:

```
part m { section A { |: c4 d e f | } section B { g4 a b c | } }
form main { A B :| }          // fine: the ':|' closes section A's '|:'
```

The one direction that will not work is the mirror: a `|:` written in a `form`
opens a repeat block, and that block must close in the `form`.

A repeat barline belongs to the **score**, not to one part: written in one part it
is drawn on every staff of the score.

> ⚠️ Two gaps, both narrow, both stated so nothing looks decided that is not: a
> one-sided `:|` written *inside section music* is drawn and reported correctly but
> is not yet played back in MIDI, and the LilyPond twin cannot express "repeat from
> the beginning" at all (`\bar ":|."` only draws the barline) — `lysc ly` warns
> when it emits one.

### Percent Repeats

Use the percent repeat syntax to repeat the previous measure:

```
c4 d e f |
repeat percent 2 { c4 d e f | }
```

## Beaming

Beams are automatic for eighth notes and shorter. Manual beam control:

```
c8[ d e f]    // Beam these four notes together
```

## Stem Direction

A stem points up or down automatically from the note's staff position. Force it with
`@stemUp` / `@stemDown`:

```
c''4@stemUp d''4@stemDown e''4   // first up, second down, third automatic
```

On a beamed note the beam's shared direction wins (a beam carries one direction for the
whole group).

## Parallel Voices (Multi-Voice)

```
voice { c'2 d } { e2 f }
```

```
voice sop { c'2 d } alt { e2 f }     // named — binds lyrics sop / lyrics alt
```

`voice` opens the span **once**; each `{ … }` after it is one simultaneous voice on the
same staff. Repeating the keyword (`voice { … } voice { … }`) is an error (LYS0019): it
would open a *second* span, and two one-voice spans play one after the other rather than
together. A single voice is transparent and warns (LYS4011) unless it is named — a name
is what a `lyrics NAME { … }` block binds to.

(The LilyPond `<< … \\ … >>` form is **not** Lily# — the parser rejects it with a hint.)

## Named Music (Phrases)

Named music is declared with the `phrase` keyword. (The earlier `name = { … }`
and `let name = …` forms have been removed — the parser rejects them with a hint
to use `phrase`.)

### Phrase Declaration

```
phrase theme {
  c4 d e f | g2 g |
}
```

### Phrase Reference

```
theme              // Insert the phrase's music here (bare name)
```

## Sections and Parts

### Part Declaration

Header attributes are written bare (no colon), the same as the top-level
`clef` / `key` / `time` / `tempo` commands:

```
part rightHand {
  clef treble
}

part leftHand {
  clef bass
}
```

### Section with Parts

```
section Main {
  rightHand { c4 d e f | }
  leftHand { c2 c | }
}
```

### Section-level key, meter, tempo, and pickup

A section may state its own `key`, `time`, `tempo`, or `partial` at the top of its body.
These apply to the **whole section** — they print on every part of it, and revert to the
score level at the next section (tempo persists):

```
section A {
  key g major
  time 3/4
  melody { g4 a b | }
}
```

In **part-major** layout, where each part holds its own inner sections, a section's key/
meter/tempo can be stated once in a standalone **header** — a `section` block with only
those settings — placed alongside the `part` blocks:

```
part melody { section A { c4 c g' g | } }
part bass   { section A { c2 e | } }
section A { key g major }       // applies to every part playing A
```

The layout converter turns the two forms into each other.

### Structure (Playback Order)

```
form main { Intro Main Main Coda }
```

A reused section prints the same section mark each time. Give an
occurrence its own display label with a string after the name; an empty
string suppresses the mark (like `~Name`):

```
form main { Intro Main Main "Main (reprise)" Coda }
```

Identifiers (sections, parts, phrases) may use any Unicode letters:

```
section イントロ { メロディ { 動機 } }
form main { イントロ イントロ "イントロ(再現)" }
```

### Navigation marks

The form may carry repeat-navigation marks between sections. The *signs*
`segno` and `coda` engrave at the start of the following section (the jump
target); the *text* directives `fine`, `to coda`, `dc`/`ds` (optionally
`dc al fine`, `ds al coda`, …) engrave at the end of the section just played:

```
form main {
  A segno
  B  to coda
  C  ds al coda
  coda  D
}
```

## Render Block

Controls output layout. Each `staff partName` names the part to draw (a bare
name, no braces); the clef comes from the part declaration, not the render block:

```
score main "out" {
  grandStaff {
    staff rightHand
    staff leftHand
  }
}
```

A single-staff score names one staff directly:

```
score main "out" {
  staff melody
}
```

### Staff groups — `grandStaff`, `staffGroup`, `choirStaff`

Three ways to engrave several staves as one group. All three take `staff` items and
nothing else, and they differ only in what is drawn down the left edge:

| | left edge | bar lines | reads as |
|---|---|---|---|
| `grandStaff` | brace | drawn **through** the gap | one instrument on two staves (piano, harp) |
| `staffGroup` | bracket | drawn **through** the gap | one family (the woodwinds) |
| `choirStaff` | bracket | **not** drawn through — each staff keeps its own | independent lines (voices) |

```
score choral "satb" {
  choirStaff { staff sop  staff alt  staff ten  staff bas }
}

score winds "winds" {
  staffGroup { staff flute  staff oboe  staff clarinet }
}
```

Each is the LilyPond context of the same name, so a `.ly` export of the three is
`\new GrandStaff` / `\new StaffGroup` / `\new ChoirStaff`.

⚠️ `staffGroup` reads in the other order on purpose, and is **not** a slip for
`groupStaff`. The other four `…Staff` items in a score body each *produce* a staff
(`condensedStaff` and `combinedStaff` put several parts on one) or are the established
name of one; a staff group produces a **group of staves**, and says so. LilyPond spells
its own contexts the same way for the same reason: of its seventeen staff contexts,
`StaffGroup` is the only one that is not a musical term.

### This score's own header, and parts that only play

A `title` / `composer` inside a score restates the file's metadata for **that score
alone** — a part extract can be headed with the part's name while the full score keeps
the work's title. A **bare part name** renders that part to MIDI only: played, never
engraved, which is how a click track or a cue part rides along without appearing on the
page.

```
score winds "winds" {
  staffGroup { staff flute  staff oboe }
  title "Woodwinds"    // this score only
  click                // played, never engraved
}
```

A staff's display name is a quoted string (`staff flute "Piccolo"`) — a bare word after
`staff NAME` is always another score item (`staff flute click` is flute's staff plus the
`click` MIDI-only part), so position never changes what a word means. And a score of
nothing but bare names has nothing to engrave, which is the empty-body error.

### This score's own page and faces

A score may also reference a **named** `fonts` / `paper` block (declared at the top
level — see those sections) and override part of it in place:

```
paper wide  { paperWidth 250mm }
fonts house { serif "Georgia"  lyricText "Charis SIL" }

score main  { paper wide  fonts house  staff melody }        // the conductor page
score parts { paper wide { topMargin 12mm }  staff melody }  // same paper, wider top
```

The reference replaces the file's unnamed default for that score alone; the override
block reads as if its entries were written at the end of the named block.

### Multiple forms (excerpts)

Declare several named forms and bind each `score` to one by name — for example a
full arrangement plus a practice excerpt that plays only the intro. The reserved
form `main` writes to the input file's name; any other form name becomes the
output file name (unless a `"basename"` overrides it). MIDI plays the `main` form
(or the first declared).

```
form main     { Intro Verse Outro }
form practice { Intro }

score main     "full" { staff melody }  // → full.svg
score practice        { staff melody }  // → practice.svg
```

## Override/Revert

Modify engraving properties. The vocabulary is four properties —
`NoteHead.transparent`, `Stem.transparent`, `NoteHead.color`, `Stem.color`. The syntax
accepts any `Grob.property`, but anything outside that list is refused (LYS1029, "not
supported in this version") rather than silently doing nothing; the list grows, and each
addition removes one error. All four take effect (`NoteColumn.force-hshift` left the
vocabulary 2026-08-23 — its implementation is disabled for the initial release, so it is
refused honestly until the per-voice implementation lands).

```
override NoteHead.color = red    // named colour, or a "#rrggbb" string
c4 d e f |
revert NoteHead.color

once override Stem.transparent = true
c4 d e f |       // 'once' applies to the next note only
```

## Lyrics

Lyrics bind to their **own melody at the definition** — `lyrics NAME sings PART`
— and the score only places them, by ORDER: a `lyrics NAME` row directly below
the staff engraving PART is that staff's verse (a run of rows stacks as
verses); anywhere else it draws **only the words, at the melody's rhythm**,
without engraving the melody — a part sheet carrying the chorus words. Several
tracks may sing the same part (Japanese and English words, a parody). A track
whose name matches the part (or one of its voices) is bound by the name alone;
a track with no binding is the even-spread lead-sheet row.

```
part melody
section Main {
  melody { c4 d e f | g2 g | }
  lyrics words sings melody {
    Hap- py birth- day |
    to you |
  }
}
form main { Main }
score main { staff melody  lyrics words }
```

Barlines in a lyrics block follow the music rule: every written `|` closes one
bar, the one that OPENS the run included — so `| きら | ひかる |` is one bar
longer than `きら | ひかる`, its first bar carrying no syllables. That leading
`|` is how a verse skips the rest bar the melody opens with.

## Music Marks

### Rehearsal Marks

```
c4@mark("A") d e f |
```

### Navigation Marks

A navigation mark is **bare** — it is a landmark in the music, not a note modifier, so it
takes no `@` (writing `c4@segno` is LYS1022). Place it at a barline boundary; mid-measure
it engraves but warns (LYS4003).

```
segno c4 d e f |
c4 d e f | to coda
c4 d e f | fine
c4 d e f | dc
c4 d e f | ds al fine
coda c4 d e f | ds al coda
```

The same words are how a `form` names the route: `form main { A segno B to coda C ds al
coda coda D }`.

### Text Spanners

A text spanner runs from its start to the `@!` that ends it, and **the end is required**:
a spanner nobody closes draws nothing at all — not its dashed line and not its word — and
says so (LYS4018). That is LilyPond's own answer (a `\startTextSpan` with no
`\stopTextSpan` is dropped, never shortened), and it is what makes the length on the page
always a length you wrote.

```
c4@rit d e f@!rit |                 // Ritardando over four notes
c4@accel d e f | g@!accel a b c |   // Accelerando over five
c4@rall d e f@!rall |               // Rallentando

c4@textSpan("poco rit.") d e f@!textSpan |   // any word you like
c4@textSpan d e f@!textSpan |                // no word: a bare dashed line
```

`@rit`, `@accel` and `@rall` are shorthand for `@textSpan("rit.")` and its siblings —
nothing else about them differs, so `@!rit` and `@!textSpan` are the same mark and either
closes either start. One spanner is open at a time in a voice, and a spanner does not
carry from one voice into another.

### Ottava Brackets

Like a text spanner, an ottava **must be closed** — an unclosed one draws no bracket at
all, and the notes under it are not transposed, and it says so (LYS4018). One
`@!ottava` closes whichever of the family was opened.

```
c4@ottava d e f@!ottava |            // 8va bracket
c4@ottava(bassa) d e f@!ottava |     // 8vb
c4@quindicesima d e f@!ottava |      // 15ma - the same terminator
```

⚠️ `@loco` is **retired**. It named a mark that printed nothing — writing it only moved
where the bracket stopped — and LilyPond has no `loco` command either (the word is in
its glossary and nowhere else). Write `@!ottava`.

### Pedal Markings

Each pedal is **one span**, opened by its name and closed by `@!` — the same rule as a text
spanner and an ottava. A pedal nobody releases draws nothing at all, and says so (LYS4018).

⚠️ A second `@sustain` while the pedal is down is **re-pedalling**, not a mistake: it
releases and re-engages, which is what "Ped. … Ped." means on the page.

```
c4@sustain d e f@!sustain |           // Sustain pedal
c4@sostenuto d@!sostenuto |           // Sostenuto pedal
c4@unaCorda d@!unaCorda |             // Una corda pedal
c4@unaCorda d@treCorde |              // the same release, written as the word it prints
```

### Trill Spanners

```
c4@startTrillSpan d e@stopTrillSpan f |
```

## Glissando

```
c4@glissando d |      // Glissando from C to D
```

## Arpeggio

```
<c e g>4@arpeggio     // Arpeggiate chord
```

## Figured Bass

```
c4@fig(6) d@fig(6 4) e@fig(5 3) |
```

## Chord Names

Written as they print — an UPPERCASE root, `#`/`b`, a bare quality (`C`, `Am`,
`G7`, `F#m`, `Bb7/D`; altered tensions spell `+`/`-`: `Gm7-5`):

```
c4@chord(C) d@chord(Dm) e@chord(Em) f@chord(F) |
```

In a `chords NAME { }` row the same symbols place themselves on the bar's beat
grid (no durations): one entry takes the bar, two in 4/4 are halves, and `.`
holds the previous chord one more beat:

```
chords prog { C | F G | C . . G7 | }
```

An entry may also be a **Roman degree of the key** at that bar, which is how a
progression is written once and follows the key:

```
chords prog { section A { Imaj7 | V7 | IIm7 | bVII | } }
```

In C that is `Cmaj7 G7 Dm7 B♭`; in E♭ the same source is `E♭maj7 B♭7 Fm7 D♭`. A degree is
an optional `b`/`#`, a numeral `I`–`VII`, the ordinary quality (`Imaj7`, `IIm7`, `V7`,
`VIIdim`, `Vaug`, `IIm7-5`) and an optional `/` bass written as a degree too (`V7/VII`).
Degrees and absolute names may not collide — a root is `A`–`G`, a numeral is `I` or `V` —
and both resolve to the same chord, so the written form and the displayed form stay
independent: a degree chart prints names by default and degrees under `as roman`.
⚠️ Use the ASCII `b`/`#`: the printed `♭ ♯ ° ø` are refused by the lexer, so write
`bVII` and `VIIdim`, not `♭VII` and `VII°`.

That fragment is the row's *contents*. Where it may sit depends on the file's layout:
inside the section whose bars it fills (`section A { chords prog { … } }`), or — in a
part-major file, where the parts carry their own sections — at the top level with the
sections named inside it (`chords prog { section A { … } }`). A **flat top-level track
in a part-major file is an error** (LYS2011 for chords, LYS4002 for lyrics): it has no
section to anchor to, so its bars would run from bar 0 across whatever the form plays,
and every section after the first would get nothing.

## Comments

```
// This is a line comment
/* This is a
   block comment */
```

## Reserved Words

The following words are keywords. They cannot be used as bare identifiers (variable /
part / section / phrase names) — **except** the four clef-name words `treble`, `bass`,
`alto`, `tenor`, which are accepted as part / section / phrase names (so a `bass` part can
be declared and referenced).

| Group | Words |
|-------|-------|
| Structure | `section` `form` `using` `tab` `ossia` `transpose` `octave` `instrument` `percussion` `drummap` |
| Score / layout | `score` `part` `staff` `grandStaff` `staffGroup` `choirStaff` `condensedStaff` `combinedStaff` `voice` `phrase` `repeat` `volta` `alternative` `break` `nobreak` `partial` `embedded` `fonts` `paper` |
| Metadata | `title` `composer` `tempo` `time` `key` `clef` |
| Modes | `major` `minor` `ionian` `dorian` `phrygian` `lydian` `mixolydian` `aeolian` `locrian` |
| Clef names | `treble` `bass` `alto` `tenor` `treble_8` `bass_8` `soprano` `mezzosoprano` `baritone` |
| Notation | `tuplet` `grace` `acciaccatura` `appoggiatura` `cue` `lyrics` `chords` `tuning` |
| Overrides | `override` `revert` `once` |
| Navigation (form block) | `segno` `fine` `coda` `dc` `ds` `al` `to` `tocoda` |
| Dynamics | `ppp` `pp` `p` `mp` `mf` `f` `ff` `fff` |

⚠️ The `fonts { }` keys (`serif` `header` `lyricText` `chordName` `barNumber` …) are **not**
reserved words — they are read inside that block only, against the role vocabulary, so
they stay free as part / section / phrase names. Several of them (`title`, `lyrics`,
`chords`, `tempo`, `instrument`, `tuplet`, `volta`) are reserved for other reasons and
appear above. The `paper { }` keys and units (`paperWidth`, `mm`, `basicDistance`, …)
are free the same way.

⚠️ Measured word by word against `Lexer.GetKeywordKind` on 2026-08-16, by asking whether
each can name a part. Five words this table listed are **not** reserved and name a part
fine — `include`, `let`, `use`, `chordnames`, `tabStaff` — and the sixteen added above are.
`structure` and `render` left the language when they became `form` and `score`.

Notes:

- Single letters `a`–`g` are pitch names; `r`/`R` are rests, `s` is a spacer rest.
- Articulation, ornament, dynamic-text and mark **names** (`staccato`, `tr`, `mordent`,
  `cresc`, `dim`, `segno`, …) are resolved from the `@name` text and are **not** reserved
  as identifiers — `tr`, `acc`, `ten`, `dim` etc. remain usable as your own names.
- **Keyword spelling is exact, including case.** `Lexer.GetKeywordKind` matches whole
  strings and the lexer has no case-folding path at all, so `grandstaff` is not a spelling
  of `grandStaff` — it is an ordinary identifier, and names a part fine (measured). Written
  where a keyword belongs it is refused exactly like any other unknown word.
  ⚠️ This line used to claim the opposite for `grandstaff` and `tabstaff`. It also outlived
  the measurement nine lines above, which had already found that `tabStaff` is not a keyword
  in the first place.
