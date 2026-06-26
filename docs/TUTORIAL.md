# Lily# Tutorial

Learn the basics of writing music notation with Lily#.

## Your First Score

Create a file `hello.lys`:

```
c4 d e f | g2 g | a4 a a a | g2. r4 |
```

Compile it:

```bash
lysc svg hello.lys
```

This creates `hello.svg` with a single staff showing "Twinkle Twinkle Little Star" melody.

## Adding Metadata

```
title "Twinkle Twinkle Little Star"
composer "Traditional"
tempo 100
time 4/4
key c major

c4 c g g | a a g2 |
f4 f e e | d d c2 |
```

## Working with Accidentals

Sharps use `is`, flats use `es`:

```
key g major

// G major scale with F#
g4 a b c' | d' e' fis' g' |

// Chromatic passage
c4 cis d dis | e f fis g |
```

## Chords

Enclose pitches in angle brackets:

```
<c e g>2 <d f a> |     // C major, D minor
<e g b>2 <f a c'>  |    // E minor, F major
```

## Dynamics and Articulations

```
c4@p d e f |              // Piano (soft)
g4@cresc a b c' |         // Crescendo
d'4@f@staccato e' f' g' | // Forte with staccato
```

## Ties and Slurs

Ties connect the same pitch:

```
c2~ | c4 d e f |    // C held across the barline
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

part rightHand { clef: treble }
part leftHand { clef: bass }

section Main {
  rightHand { e'4 d' c' d' | e' e' e'2 | }
  leftHand  { c2 g, | c g, | }
}

score {
  grandStaff {
    staff treble { rightHand }
    staff bass { leftHand }
  }
}

structure { Main }
```

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
  melody { $theme $variation $theme }
}
```

## Repeats

Volta repeats use the symbolic `|: … :|` barlines. Add inline volta endings with
`[1. …] [2. …]` for first/second-time bars; the repeat count defaults to 2 (or the
highest volta number), or state it as `|: … :|*N`.

```
{
  |: c4 d e f |
     g4 a b c' |
  [1. d'2 d' | ] :|   // First time
  [2. c'2 c' | ]      // Second time
}
```

A plain `|: … :|` (no endings) just repeats its body.

## Grace Notes

```
acciaccatura { d16 } c4 e g |   // Quick grace note
appoggiatura { e8 } d4 f a |    // Longer grace note
```

## Tuplets

```
// Triplets: 3 notes in time of 2
tuplet 3/2 { c8 d e } f4 g a |

// Quintuplets: 5 in time of 4
tuplet 5/4 { c16 d e f g } a4 b c' |
```

## Lyrics

```
section Verse {
  melody { c4 d e f | g2 g | }
  lyrics {
    Hap- py birth- day |
    to you |
  }
}
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
