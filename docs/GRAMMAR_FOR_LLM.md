# Lily# (`.lys`) — Language Spec for LLMs

Single-file, drop-in context for generating Lily# music notation. Lily# is an
explicit, unambiguous text notation that compiles to engraved SVG and MIDI. Each
construct below has exactly one canonical form and one minimal example. Prefer the
`@annotation` prefix everywhere; emit one statement/idea per line; end every measure
with `|`.

> This file is the compressed spec. Full human references: `docs/SYNTAX_REFERENCE.md`,
> `docs/GRAMMAR.md`. When they disagree with this file, those are authoritative.

## Document skeleton (top level, in this order)

```
title "Song"            // optional metadata
composer "Composer"     // optional
tempo 120               // optional, quarter = BPM ('tempo 120 swing' adds a shuffle-feel equation)
time 4/4                // optional (default 4/4)
key c major             // optional (default c major)

part rightHand { clef treble }  // declare each part; clef lives here
part leftHand  { clef bass }    // part names are identifiers, NOT reserved words
                                // (bass/treble/melody-as-keyword etc. are taken)

phrase motif { c4 d e f | }     // optional reusable music, referenced as $motif

section Main {                  // a section binds music to each part by name
  rightHand { $motif g2 g | }
  leftHand  { c2 c | g2 g | }
}

structure { Main }              // playback/print order of sections

score "out" {                   // one or more render blocks
  grandStaff {
    staff rightHand             // 'staff NAME' — bare name, no braces
    staff leftHand
  }
}
```

A minimal single-staff document:

```
part melody { clef treble }
section Main { melody { c4 d e f | g2 g | } }
structure { Main }
score "out" { staff melody }
```

## Pitches

- Names: `c d e f g a b`. Sharp `is`, flat `es`: `cis`=C#, `ees`=Eb, `cisis`=C##, `eses`=Dbb.
- Octave: `'` up one, `,` down one (repeatable: `''`, `,,`).
- Default octave is C4. Each bare pitch takes the octave nearest the previous note
  (an interval of a fourth or less); `'`/`,` shift from there.

```
c d e f g a b c    // C4 D4 E4 F4 G4 A4 B4 C5
c' c,              // C6 C5
```

## Durations

- Numbers: `1`=whole, `2`=half, `4`=quarter, `8`, `16`, `32`, `64`, `128`.
- Dots after the number: `4.`=dotted quarter, `4..`=double-dotted.
- Omitting the duration reuses the previous note's duration.

```
c4 d e f    // all quarters
c8 d e f    // all eighths
```

## Notes, rests, chords

```
fis8        // F# eighth
r4          // quarter rest
s4          // invisible spacer rest
R1          // full-measure rest
<c e g>4    // chord (shared duration), C major triad as a quarter
```

## Annotations (`@name` attached to a note or chord)

Attach with `@`. One note may take several: `c4@staccato@p`. Add `.up` / `.down` to force
an articulation above / below the note (default is automatic, opposite the stem):
`c4@staccato.up`, `d4@accent.down`.

- Stem direction: `@stemUp` / `@stemDown` force a note's stem (default is automatic).
  On a beamed note the beam's shared direction wins.
- Articulations: `@staccato @accent @tenuto @marcato @fermata @portato`
- Ornaments: `@trill @mordent @prall @turn @invertedturn`
- Dynamics: `@ppp @pp @p @mp @mf @f @ff @fff` (default below the staff; `.up` / `.down`
  forces the side, e.g. `@f.up`)
- Accidental style: `@courtesy` (cautionary, parenthesized), `@editorial` (musica ficta)
- Arpeggio: `<c e g>4@arpeggio`
- Glissando: `c4@glissando d` (line from this note to the next)
- Figured bass: `c4@fig.6` , `d4@fig.6.4`
- Chord names: `c4@chord.C` , `d4@chord.Dm`

```
c4@staccato d4@accent <e g>4@arpeggio |
```

## Ties, slurs, beams

```
c4~ | c4 d e f       // tie (same pitch across the barline) with ~
c4( d e f)           // slur (different pitches) with ( )
c8[ d e f]           // manual beam; beaming is automatic otherwise
```

## Hairpins (spanners over several notes)

Place the spanner mark on the starting note; it runs to the next dynamic.

```
c4@p@cresc d e f@f |        // crescendo p -> f
g4@f@decresc a b c@p |      // decrescendo
```

`.up` / `.down` is NOT allowed on `@cresc` / `@decresc` / `@dim` (a hairpin is always
below the staff — the parser rejects it). Placement applies only to dynamic levels: `@f.up`.

## Bar handling

- Barlines: `|` single, `||` double, `|.` final, `|:` repeat start, `:|` repeat end.
- Volta repeats are symbolic; endings are inline `[1. ... ]` `[2. ... ]`. Play count
  defaults to the highest ending number; set it with `*N`.

```
|: c4 d e f | [1. g2 g | ] :| [2. a2 a | ]
```

- Percent repeat (repeat the previous measure): `repeat percent 2 { c4 d e f | }`.
- NOT supported: `repeat volta` / `alternative` keywords (the parser rejects them — use
  the symbolic `|: ... :|` form above). `repeat` is only for `percent` / `unfold` / `tremolo`.

## Tuplets

```
tuplet 3/2 { c8 d e }                 // triplet: 3 in the time of 2
tuplet 3/2 { c8 d tuplet 3/2 { e16 f g } | }   // nesting allowed
```

## Grace notes

```
grace { d16 e } f4           // grace before F
acciaccatura { a16 } b4      // slashed grace
appoggiatura { c8 } d4       // unslashed grace
```

## Multi-voice (one staff)

```
voice { c'2 d } voice { e2 f }     // each voice { } is a simultaneous voice
```

## Lyrics (inside a section, aligned to that part's notes)

Syllables separated by spaces; `-` joins syllables of one word; `|` mirrors the music's barlines.

```
section Main {
  melody { c4 d e f | g2 g | }
  lyrics { Hap- py birth- day | to you | }
}
```

## Structure: reuse and navigation

```
structure { Intro Main Main "Main (reprise)" Coda }   // string = custom section label
```

Navigation marks sit between section names. Signs `segno` / `coda` engrave at the start
of the following section; text directives `fine`, `to coda`, `dc`/`ds` (and `dc al fine`,
`ds al coda`) engrave at the end of the section just played.

```
structure { A segno  B to coda  C ds al coda  coda D }
```

In-note marks: `c4@mark.A` (rehearsal mark), `@segno @coda @fine @dc @ds.al.fine`,
text spanners `@rit` / `@accel`, ottava `@ottava` ... `@loco`, trill spanner
`@startTrillSpan` ... `@stopTrillSpan`, pedals `@ped` ... `@ped.off`.

## Per-score structure

A `score` may carry its own `structure { ... }` to render a different arrangement
(e.g. a practice excerpt); it overrides the top-level structure for that score only.

```
structure { Intro Verse Outro }
score practice { structure { Intro } staff melody }
```

## Override / revert (engraving properties)

```
override Stem.length = 7        // property values are integers
c4 d e f |
revert Stem.length
once override Stem.length = 9    // 'once' applies to the next note only
c4 d e f |
```

## Rules and gotchas

- Each **phrase body** evaluates in a fresh frame (default octave/pitch/duration), so a
  phrase means the same notes at every `$call`. **Section boundaries also reset the frame.**
  After `$phrase`, the next note is relative to the phrase's last note.
- Part header attributes (`clef`/`key`/`time`/`tempo`) are written **bare, no `=`**, like
  the top-level commands. Override/revert use `=`.
- Identifiers (parts, phrases, sections) may use any Unicode letters: `phrase 動機 { ... }`.
- Comments: `// line` and `/* block */`.
- `@name` is the canonical annotation prefix. `\name` annotations are rejected (use `@`);
  backslash is reserved for tablature only (`\3` string numbers, `\tuning`). Lily# is NOT
  LilyPond — do not emit LilyPond-only constructs (`\repeat volta`, `\relative`, `\new
  Staff`, `\version`, `<< ... \\ ... >>`, etc.).

## Reserved words

These are keywords and cannot be used as bare identifiers, EXCEPT the four clef-name words
(`treble bass alto tenor`), which ARE allowed as part / section / phrase names (so a `bass`
part is fine). Keywords:

```text
section structure include tab ossia transpose octave instrument channel
score part staff grandStaff voice phrase repeat volta alternative let use break partial
title composer tempo time key clef
major minor dorian phrygian lydian mixolydian aeolian locrian
treble bass alto tenor treble_8
tuplet grace acciaccatura appoggiatura lyrics chordnames chords tabStaff tuning
override revert once
segno fine coda dc ds al to
ppp pp p mp mf f ff fff
```

Also special: single letters `a`-`g` are pitches; `r`/`R`/`s` are rests. Articulation,
ornament, dynamic-text and mark NAMES (`staccato`, `tr`, `mordent`, `cresc`, `dim`, …) are
NOT reserved — they are resolved from the `@name` text — so they remain free for your own
identifiers.
</content>
