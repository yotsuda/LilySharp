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

## Notes, Rests, and Chords

### Notes

```
c4         // C quarter note
fis8       // F# eighth note
bes2.      // Bb dotted half note
```

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

## Grace Notes

```
grace { d16 e } f4          // Grace notes before F
acciaccatura { a16 } b4     // Slashed grace (takes no time)
appoggiatura { c8 } d4      // Unslashed grace
```

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
$theme             // Insert the phrase's music here
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
section イントロ { メロディ { $動機 } }
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

Modify engraving properties. The vocabulary is three properties —
`NoteHead.transparent`, `Stem.transparent`, `NoteColumn.force-hshift`. The syntax accepts
any `Grob.property`, but anything outside that list is refused (LYS1029, "not supported in
this version") rather than silently doing nothing; the list grows, and each addition
removes one error.

```
override NoteHead.transparent = true
c4 d e f |
revert NoteHead.transparent

once override Stem.transparent = true
c4 d e f |       // 'once' applies to the next note only
```

## Lyrics

```
part melody
section Main {
  melody { c4 d e f | g2 g | }
  lyrics words {
    Hap- py birth- day |
    to you |
  }
}
form main { Main }
score main { staff melody with lyrics words }
```

Barlines in a lyrics block follow the music rule: a lone leading `|` only
anchors the start — `| きら | ひかる |` equals `きら | ひかる` — and a bar with
no syllables is written as an explicit `| |` pair (a leading `| |` skips the
melody's opening rest bar).

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

```
c4@rit d e f |        // Ritardando
c4@accel d e f |      // Accelerando
```

### Ottava Brackets

```
c4@ottava d e f |     // 8va bracket
c4@loco d e f |       // End ottava
```

### Pedal Markings

```
c4@sustainOn d e f@sustainOff |       // Sustain pedal
c4@sostenutoOn d@sostenutoOff |       // Sostenuto pedal
c4@unaCorda d@treCorde |              // Una corda pedal (tre corde IS the release)
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

```
c4@chord(c) d@chord(d:m) e@chord(e:m) f@chord(f) |
```

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
| Structure | `section` `form` `include` `tab` `ossia` `transpose` `octave` `instrument` |
| Score / layout | `score` `part` `staff` `grandStaff` `voice` `phrase` `repeat` `volta` `alternative` `let` `use` `break` `partial` |
| Metadata | `title` `composer` `tempo` `time` `key` `clef` |
| Modes | `major` `minor` `ionian` `dorian` `phrygian` `lydian` `mixolydian` `aeolian` `locrian` |
| Clef names | `treble` `bass` `alto` `tenor` `treble_8` |
| Notation | `tuplet` `grace` `acciaccatura` `appoggiatura` `lyrics` `chordnames` `chords` `tabStaff` `tuning` |
| Overrides | `override` `revert` `once` |
| Navigation (form block) | `segno` `fine` `coda` `dc` `ds` `al` `to` |
| Dynamics | `ppp` `pp` `p` `mp` `mf` `f` `ff` `fff` |

Notes:

- Single letters `a`–`g` are pitch names; `r`/`R` are rests, `s` is a spacer rest.
- Articulation, ornament, dynamic-text and mark **names** (`staccato`, `tr`, `mordent`,
  `cresc`, `dim`, `segno`, …) are resolved from the `@name` text and are **not** reserved
  as identifiers — `tr`, `acc`, `ten`, `dim` etc. remain usable as your own names.
- `grandStaff` and `tabStaff` also accept the all-lowercase spellings `grandstaff` /
  `tabstaff`.
