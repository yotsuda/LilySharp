# Lily# Tutorial

Learn the basics of writing music notation with Lily#.

## Your First Score

Create a file `hello.lys`:

```
part melody

section A {
  melody { c4 c g' g | a a g2 | f4 f e e | d d c2 | }
}

form main { ~A }

score main { staff melody }
```

A file declares its **parts**, puts their music in **sections**, says in which order the
sections play (the **form**), and says what to print (the **score**). The notes live
inside the part's `{ … }`.

Compile it:

```bash
lysc svg hello.lys
```

This creates `hello.svg` with a single staff showing the "Twinkle Twinkle Little Star" melody.

**Octaves are relative.** Each note takes the octave nearest the note before it, and `'`
(up) or `,` (down) shift from there. That is why the first `g` above is written `g'`: the
nearest G to C4 is the G *below* it, and the mark lifts it to the G above. The next `g`
needs no mark — it is already nearest to the one before.

## Adding Metadata

Title, composer, tempo, meter and key go at the top of the file:

```
title "Twinkle Twinkle Little Star"
composer "Traditional"
tempo 100
time 4/4
key c major

part melody

section A {
  melody { c4 c g' g | a a g2 | f4 f e e | d d c2 | }
}

form main { ~A }

score main { staff melody }
```

From here on, the examples show only the notes. They go inside a part's `{ … }`, where
`c4 c g' g | …` stands in the score above.

## Working with Accidentals

Sharps use `is`, flats use `es`:

```
// G major scale with F#
g'4 a b c | d e fis g |
```

```
// Chromatic passage
c4 cis d dis | e f fis g |
```

## Chords

Enclose pitches in angle brackets:

```
<c e g>2 <d f a> |     // C major, D minor
<e g b>2 <f a c>  |    // E minor, F major
```

## Dynamics and Articulations

```
c4@p d e f |              // Piano (soft)
g4@cresc a b c |          // Crescendo
d4@f@staccato e f g |     // Forte with staccato
```

## Ties and Slurs

Ties connect the same pitch:

```
c2 c~ | c4 d e f |    // C held across the barline
```

Slurs phrase different pitches:

```
c4( d e f | g2) r2 |   // Slur from C to G
```

## Grand Staff (Piano)

```
title "Simple Piano"
tempo 120
time 4/4

part rightHand { clef treble }
part leftHand { clef bass octave 3 }   // a clef only draws; octave 3 puts bare c at C3

section Main {
  rightHand { e4 d c d | e e e2 | }
  leftHand  { c2 g | c g | }
}

score main {
  grandStaff {
    staff rightHand
    staff leftHand
  }
}

form main { Main }
```

## One Section Name, One Span of Time

A section's name names a **span of time** in the piece, not a block of text. Every
place that writes a section with the same name — a part, a chord row, a lyrics track —
is writing the same bars, and they sound together. The examples so far list the parts
inside each section; you can equally list the sections inside each part, and the result
is the same:

```
part melody {
  section A { c4 d e f | g1 | }
  section B { a4 g f e | d1 | }
}

chords harmony {
  section A { C | G | }   // the same two bars as the melody's A
  section B { F | G | }
}

form main { A B }

score main { chords harmony  staff melody }
```

The form plays `A` then `B`, and during `A` the melody's `c d e f | g` and the chords
`C | G` sound at once. The easy mistake is to give the accompaniment a name of its own:

```
part melody {
  section A { c4 d e f | g1 | }
}

chords harmony {
  section AChords { C | G | }   // ✗ a different name is a different time
}

form main { A AChords }

score main { chords harmony  staff melody }
```

This is valid, but `AChords` is a separate section that comes *after* `A`: two bars of
melody with no chords, then two bars of chords over a silent staff. The rule:

> **Same section name = same time. Different name = different time.**

Because everything sharing a name shares its bars, they should all be the same length.
When they are not, the compiler warns (LYS2007), naming the length each part, chord row
and lyrics track writes, and how many bars the section is laid out at. In VS Code, the line above every
`section` shows its length and who writes it; click it to list them all.

## Reusing Phrases

Define reusable musical phrases:

```
phrase theme {
  c4 d e f | g2 g |
}

phrase variation {
  c8 c d d e e f f | g4 f e d |
}

section Main {
  melody { theme variation theme }
}
```

## Repeats

Repeats use the symbolic `|: … :|` barlines — **written in the `form`, not in the
music**. A repeat changes the order the music plays in, and the form is where a piece's
order lives, so the bars that repeat go in a `section` of their own and the form repeats
the section. Add volta endings with `[1. Section] [2. Section]` for first/second-time
bars; the repeat count defaults to 2 (or the number of endings), or state it as
`|: … :|*N`.

```
part melody { clef treble }

section Body {
  melody { c4 d e f | g4 a b c | }
}
section First  { melody { d'2 d | } }   // First time
section Second { melody { c'2 c | } }   // Second time

form main { |: Body [1. ~First] :| [2. ~Second] }

score main { staff melody }
```

A plain `form main { |: Body :| }` (no endings) just repeats its body. A third and later
ending is written the same way: `:| [3. Third]`.

## Grace Notes

```
acciaccatura { d16 } c4 e g e |   // Quick grace note
appoggiatura { e8 } d4 f a f |    // Longer grace note
```

## Tuplets

```
// Triplets: 3 notes in time of 2
tuplet 3/2 { c8 d e } f4 g a |
```

```
// Quintuplets: 5 in time of 4
tuplet 5/4 { c16 d e f g } a4 b c |
```

## Lyrics

A `lyrics NAME { … }` track sits in the section beside the part it sings; the
score places it as a `lyrics NAME` row directly under the staff. The `sings`
word is the binding: the words belong to that melody wherever the score places
them — write the row under another part's staff (or alone) and you get ONLY the
words, at the melody's rhythm, without the melody's staff (chorus words on a
horn part). A row can also name its own melody — `lyrics words sings alto` —
so one verse serves every staff of a chorale.

```
part melody
section Verse {
  melody { c4 d e f | g2 g | }
  lyrics words sings melody {
    Hap- py birth- day |
    to you |
  }
}
form main { Verse }
score main { staff melody  lyrics words }
```

## Output Formats

```bash
lysc svg score.lys       # Vector graphics (scalable)
lysc pdf score.lys       # Print-ready PDF
lysc png score.lys       # Raster image (192 DPI default)
lysc midi score.lys      # Audio playback
lysc xml score.lys       # MusicXML for other notation software
lysc check score.lys     # Validate syntax only
```

## Next Steps

- See [SYNTAX_REFERENCE.md](SYNTAX_REFERENCE.md) for the complete syntax reference
- See [CLI_REFERENCE.md](CLI_REFERENCE.md) for all command-line options
- Explore the `samples/` directory for more examples
