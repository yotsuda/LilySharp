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
a4@courtesy     % Courtesy (cautionary) accidental — parenthesized, left of the note
c4@editorial    % Editorial (suggestion) accidental — small, above the note (musica ficta)
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

```
c d e f g a b c    % C4 D4 E4 F4 G4 A4 B4 C5 — bare c after b is already C5
c' c,              % C6 C5 — marks shift from the nearest octave
```

Each **phrase body** evaluates in a fresh frame — the default octave, pitch
and duration — regardless of where the `$phrase` is referenced, so a phrase
always means the same notes at every call site. State flows out normally:
a note written after `$phrase` is relative to the phrase's last note.
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
c4 d e f    % All quarter notes
c8 d e f    % All eighth notes
```

## Notes, Rests, and Chords

### Notes

```
c4          % C quarter note
fis8        % F# eighth note
bes2.       % Bb dotted half note
```

### Rests

| Syntax | Meaning |
|--------|---------|
| `r4` | Quarter rest |
| `r2` | Half rest |
| `s4` | Spacer rest (invisible) |
| `R1` | Full-measure rest |

### Chords

Notes enclosed in angle brackets share a duration:

```
<c e g>4        % C major triad, quarter note
<d fis a>2      % D major triad, half note
<c e g b>1      % C major 7th, whole note
```

## Articulations

Articulations are attached to notes with the `@` prefix. Names are resolved from
text (not reserved keywords), so common abbreviations also work and names like
`tr`/`acc`/`ten`/`dim` remain usable as ordinary identifiers elsewhere:

```
c4@staccato     % Staccato   (abbrev: @stac)
d4@accent       % Accent     (abbrev: @acc)
e4@tenuto       % Tenuto      (abbrev: @ten)
f4@marcato      % Marcato     (abbrev: @marc)
g4@fermata      % Fermata     (abbrev: @ferm)
a4@portato      % Portato (tenuto + staccato)
```

## Ornaments

```
c4@trill          % Trill (abbrev: @tr)
d4@mordent        % Mordent
e4@prall          % Inverted mordent (pralltriller)
f4@turn           % Turn
g4@invertedturn   % Inverted turn
```

## Dynamics

Dynamics use `@` prefix (or `\` for LilyPond compatibility):

```
c4@ppp    % Pianississimo
c4@pp     % Pianissimo
c4@p      % Piano
c4@mp     % Mezzo piano
c4@mf     % Mezzo forte
c4@f      % Forte
c4@ff     % Fortissimo
c4@fff    % Fortississimo
```

### Hairpins (Crescendo/Decrescendo)

```
c4@p @cresc d e f |
g4@f @decresc a b c |
```

## Ties and Slurs

### Ties

Connect notes of the same pitch with `~`:

```
c4~ | c4 d e f    % C tied across barline
```

### Slurs

Connect different pitches with `(` and `)`:

```
c4( d e f)         % Slur over four notes
c4( d) e( f)       % Two separate slurs
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
key c major       % C major (no accidentals)
key g major       % G major (1 sharp)
key f major       % F major (1 flat)
key a minor       % A minor (no accidentals)
key d minor       % D minor (1 flat)
```

## Clef

```
clef treble       % Treble clef (G clef)
clef bass         % Bass clef (F clef)
clef alto         % Alto clef (C clef, line 3)
clef tenor        % Tenor clef (C clef, line 4)
```

## Time Signature

```
time 4/4          % Common time
time 3/4          % Waltz time
time 6/8          % Compound duple
time 2/2          % Cut time
```

## Tempo

```
tempo 120              % Quarter = 120 BPM
tempo "Allegro" 4 = 120   % With text marking
```

## Metadata

```
title "Sonata in C"
composer "W.A. Mozart"
```

## Grace Notes

```
grace { d16 e } f4           % Grace notes before F
acciaccatura { a16 } b4      % Slashed grace (takes no time)
appoggiatura { c8 } d4       % Unslashed grace
```

## Tuplets

```
tuplet 3/2 { c8 d e }        % Triplet: 3 eighth notes in time of 2
tuplet 5/4 { c16 d e f g }   % Quintuplet
```

Nested tuplets are supported:

```
tuplet 3/2 {
  c8 d tuplet 3/2 { e16 f g } |
}
```

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

### Percent Repeats

Use the percent repeat syntax to repeat the previous measure:

```
c4 d e f |
repeat percent 2 { c4 d e f | }
```

## Beaming

Beams are automatic for eighth notes and shorter. Manual beam control:

```
c8[ d e f]     % Beam these four notes together
```

## Parallel Voices (Multi-Voice)

```
<< { c'2 d } \\ { e2 f } >>
```

The `\\` separator creates a second voice on the same staff.

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
$theme              % Insert the phrase's music here
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

### Structure (Playback Order)

```
structure { Intro Main Main Coda }
```

A reused section prints the same section mark each time. Give an
occurrence its own display label with a string after the name; an empty
string suppresses the mark (like `~Name`):

```
structure { Intro Main Main "Main (reprise)" Coda }
```

Identifiers (sections, parts, phrases) may use any Unicode letters:

```
section イントロ { メロディ { $動機 } }
structure { イントロ イントロ "イントロ(再現)" }
```

## Render Block

Controls output layout. Each `staff { partName }` names the part to draw; the
clef comes from the part declaration, not the render block:

```
score "out" {
  grandStaff {
    staff { rightHand }
    staff { leftHand }
  }
}
```

A single-staff score names one staff directly:

```
score "out" {
  staff { melody }
}
```

### Per-Score Structure

A score may carry its own `structure { ... }` to render a different
arrangement of the same sections — for example a practice excerpt that
plays only the intro. It overrides the file's top-level structure for
that score only; scores without one keep using the top-level structure
(and MIDI always uses the top-level form).

```
structure { Intro Verse Outro }   % the default form

score full {
  staff melody
}

score 練習 {
  structure { Intro }             % this score renders only the intro
  staff melody
}
```

## Override/Revert

Modify engraving properties:

```
override Stem.length = 7
c4 d e f |
revert Stem.length

once override NoteHead.color = "red"
c4 d e f |        % Only first note is red
```

## Lyrics

```
section Main {
  melody { c4 d e f | g2 g | }
  lyrics {
    Hap- py birth- day |
    to you |
  }
}
```

## Music Marks

### Rehearsal Marks

```
c4@mark.A d e f |
```

### Navigation Marks

```
c4@segno d e f |
c4@coda d e f |
c4@fine
c4@dc
c4@ds.al.fine
```

### Text Spanners

```
c4@rit d e f |         % Ritardando
c4@accel d e f |       % Accelerando
```

### Ottava Brackets

```
c4@ottava d e f |      % 8va bracket
c4@loco d e f |        % End ottava
```

### Pedal Markings

```
c4@ped d e f@ped.off |        % Sustain pedal
c4@sost.ped d@sostenuto |     % Sostenuto pedal
c4@una.corda d@tre.corde |    % Una corda pedal
```

### Trill Spanners

```
c4@startTrillSpan d e@stopTrillSpan f |
```

## Glissando

```
c4@glissando d |       % Glissando from C to D
c4@gliss d |           % Short alias for @glissando
```

## Arpeggio

```
<c e g>4@arpeggio      % Arpeggiate chord
```

## Figured Bass

```
c4@fig.6 d@fig.6.4 e@fig.5.3 |
```

## Chord Names

```
c4@chord.C d@chord.Dm e@chord.Em f@chord.F |
```

## Comments

```
// This is a line comment
/* This is a
   block comment */
```
