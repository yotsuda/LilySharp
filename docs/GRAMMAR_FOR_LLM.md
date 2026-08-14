# Lily# (`.lys`) — Language Spec for LLMs

Single-file, drop-in context for generating Lily# music notation. Lily# is an
explicit, unambiguous text notation that compiles to engraved SVG and MIDI. Each
construct below has exactly one canonical form and one minimal example. Prefer the
`@annotation` prefix everywhere; emit one statement/idea per line; end every measure
with `|`.

> This file is the canonical compressed spec (every example is parse-verified). The
> parser is the ultimate authority; `docs/GRAMMAR.md` (formal EBNF) and
> `docs/SYNTAX_REFERENCE.md` are companion references kept in sync with this file.

## Document skeleton (top level, in this order)

```
title "Song"            // optional metadata
composer "Composer"     // optional
tempo 120               // optional; also: tempo "Allegro" 120, tempo "Andante" 4 = 96 (text + beat unit), tempo "Lively" 4. = 116 (dotted unit), tempo Comodo 4 = 84 (a bare word is the marking); 'tempo 120 swing' adds a shuffle-feel equation ('swing 16' = 16th swing)
time 4/4                // optional (default 4/4); 4/4 engraves as the C
                        // (common time) glyph and 2/2 as cut-C, like LilyPond
key c major             // optional (default c major); all church modes work:
                        // major minor ionian dorian phrygian lydian mixolydian aeolian locrian
                        // (key d dorian = no accidentals, key e dorian = 2 sharps)
partial 8               // optional: the pickup length, once for every part

part rightHand { clef treble }  // declare each part; clef lives here
part leftHand  { clef bass }    // part names are identifiers, NOT reserved words
                                // (bass/treble/melody-as-keyword etc. are taken)

phrase motif { c4 d e f | }     // optional reusable music, referenced as $motif

section Main {                  // a section binds music to each part by name
  rightHand { $motif g2 g | }
  leftHand  { c2 c | g2 g | }
}

form main { Main }              // playback/print order of sections

score main "out" {                   // one or more render blocks
  grandStaff {
    staff rightHand             // 'staff NAME' — bare name, no braces
    staff leftHand
  }
}
```

**Several parts on ONE staff** (a condensed score) — `condensedStaff { … }` takes BARE part
names, two or more, and gives each one a voice of the single staff it produces, in source
order (first part = voice 1, stems up). Members are bare names, not `staff` items, because
what goes in becomes a voice, not a staff. Being a score-level item, one source prints both
the condensed score and the separate parts:

```
score full  { condensedStaff { flute1 flute2 } }
score parts { staff flute1  staff flute2 }
```

This is plain condensation: unisons are not merged into one notehead and no "a2"/"Solo" is
printed.

**Two parts COMBINED** — `combinedStaff { partA partB }` takes exactly two bare part names
and merges them wherever they agree, the way an orchestral score condenses two players onto
one staff:

```
score full { combinedStaff { flute1 flute2 } }
```

- the same notes → **one** notehead, marked `a2`
- different notes, same rhythm, within a ninth → **one voice of chords**
- only one part sounding → one voice, marked `Solo` / `Solo II`, and the other part's rests
  are not engraved at all
- anything else → two voices, stems up and down

⚠️ The chord case is the usual outcome, not a corner: two parts a third or a sixth apart in
the same rhythm become chords in one voice. Use `condensedStaff` when the two lines must stay
visibly separate.

A minimal single-staff document:

```
part melody { clef treble }
section Main { melody { c4 d e f | g2 g | } }
form main { Main }
score main "out" { staff melody }
```

**Music always lives inside a part.** A file is a set of declarations; a note stream at the
top level is an error (LYS0020), as are a top-level `{ … }` block, `grace`/`tuplet` group,
`break`, or `$phrase` reference. This is what makes a top-level `clef`/`key`/`time`/`tempo`
unambiguous: with no music able to stand beside them they are always the FILE DEFAULTS, and
a directive written among the notes is always a mid-music change.

Below, a code block that is only music (no `part`/`section`/`score`) is showing a **section
body** — the four lines above are omitted so each example shows just what it teaches. To run
one, put it where `c4 d e f |` sits in the minimal document.

## Pitches

- Names: `c d e f g a b`. Sharp `is`, flat `es`: `cis`=C#, `ees`=Eb, `cisis`=C##, `eses`=Dbb.
- Octave: `'` up one, `,` down one (repeatable: `''`, `,,`).
- Default octave is C4. Each bare pitch takes the octave nearest the previous note
  (an interval of a fourth or less); `'`/`,` shift from there.
- `octave absolute` (top level, part header, or mid-music) switches to ABSOLUTE
  mode: bare `c` = C4 and `'`/`,` are absolute offsets from that anchor (`c'` = C5,
  `c,` = C3), independent per note - octave mistakes never cascade. RECOMMENDED
  when generating scores. `part X { octave 2 }` re-anchors the base (bass parts).
  Sections restore the file-level mode.

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
<c e g>4    // chord (shared duration after '>'), C major triad as a quarter
<c 3 5>4    // the same triad by scale degrees (root + 3rd + 5th of the key)
<1 3 5>2    // degrees only: anchored on the key TONIC (C E G in C major)
```

A duration is GLUED to what it lengthens — `c4`, `<c e g>4` — never spaced
(`c 4` is an error, LYS0016), and never on a chord/arpeggio member
(`<c e g2>` is an error, LYS0015). A SPACED number inside brackets is a
scale degree: `<c e g 2>`.

Chord octaves — the ANCHOR model (one rule: a mark moves only what it is attached to):
the anchor is the first member's bare LETTER (or the key tonic for a degrees-only
chord), resolved nearest to the previous note; members sit at-or-above it, so order is
free except the first slot (`<c e g>` = `<c g e>`; degrees are fully order-free). A
`'`/`,` on a member moves THAT note only — the first member's included: `<c' e g>` =
C5 E4 G4 and the next bare c is still C4. A `'`/`,` AFTER the `>` (before the duration)
moves the whole chord AND the anchor, so it propagates: `<c e g>'4 c` = C5 E5 G5, C5.

## Arpeggios `<< … >>` (written-out broken chords)

Members play in SEQUENCE and EQUALLY SUBDIVIDE the group's total (no per-member
durations — a bare number is always a scale degree). Octaves follow the chord anchor
model above; a degrees-only group anchors on the tonic. NOT LilyPond's `<< >>`
(parallel voices) — those are `voice { }` in Lily#; a `\\` inside is an error.

```
<< c e g >>      // c, then e/g stacked above (E4 G4); after c4 → a triplet of eighths
<< c 3 5 >>      // by degrees: c e g
<< 8 5 3 1 >>    // degrees-only anchors on the TONIC: C5 G4 E4 C4 — descending, no marks
<< <c e> g >>    // a chord member, then g
<< c r e >>      // a rest is a gap (an equal share); e still stacks above c
<< c e g >>2     // a duration after >> = the group's total: 3 in a half (triplet 3:2)
<< c e g >>'     // marks after >> shift the whole group and propagate to the next note
```

Must fit in one measure (else it overflows the meter).

## Annotations (`@name` attached to a note or chord)

Attach with `@`. One note may take several: `c4@staccato@p`. Two suffixes:
`.up` / `.down` forces an articulation/dynamic above / below the note (default is
automatic, opposite the stem): `c4@staccato.up`, `d4@accent.down`, `@f.up`.
An annotation that takes a VALUE puts it in parentheses (space- or comma-separated):
`@chord(Dm)`, `@fig(6 4)`, `@mark("A")`, `@finger(3)`.

- Stem direction: `@stemUp` / `@stemDown` force a note's stem (default is automatic).
  On a beamed note the beam's shared direction wins.
- Articulations: `@staccato @staccatissimo @accent @tenuto @marcato @fermata @portato`
- String technique: `@upbow @downbow @flageolet` - always above
- Ornaments: `@trill @mordent @prall @turn @invertedturn`
- Dynamics: `@ppp @pp @p @mp @mf @f @ff @fff` and the accent dynamics `@sfz @sf @fp @rfz @fz` (default below the staff; `.up` / `.down`
  forces the side, e.g. `@f.up`)
- Accidental style: `@courtesy` (cautionary, parenthesized), `@editorial` (musica ficta)
- Arpeggio: `<c e g>4@arpeggio`
- Glissando: `c4@glissando d` (line from this note to the next)
- Figured bass: `c4@fig(6)` , `d4@fig(6 4)`
- Chord names: `c4@chord(C)` , `d4@chord(Dm)`
- Fingering (per chord note): `<c@finger(1) e@finger(3)>4`
- Rehearsal mark: `c4@mark("A")`
- Half ties: `c4@laissezVibrer` (l.v. into silence), `c4@repeatTie` (resume from a repeat)
- Cue/effects: `@cue` (small cue note), `@cross`/`@dead` (x notehead), `@fall`/`@doit` (jazz bends), `@breath`/`@caesura`
- Feathered beams: `c16@feather(right) d e f` (accel), `@feather(left)` (rit)
- Free expressive text: `c4@text("dolce")` (plain italic below the note; `.up` forces
  above: `c4@text("pizz.").up`). Not a dynamic: hairpins run through it.

```
c4@staccato d4@accent <e g>4@arpeggio |
```

## Ties, slurs, beams

```
c4~ | c4 d e f       // tie (same pitch across the barline) with ~
c4( d e f)           // slur (different pitches) with ( )
<c e>4( <d f>)       // a slur may bind chords, not just single notes
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
  defaults to the highest ending number; set it with `*N`. The opening `[` is required
  (a bare `1. ...` ending is rejected); the closing `]` is optional — write it to draw
  the right cap (closed ending), omit it to leave the ending open. Section-level endings
  in a `form main { }` repeat use the same `[N. Section]` form.

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

## Repetition shorthand

`repeat unfold N { ... }` writes its body out N times - phrases welcome:

```
phrase ground { d2 a,2 | b,2 fis,2 | }
repeat unfold 8 { $ground }              // 32 bars from one line
```


## Grace notes

```
grace { d16 e } f4           // grace before F
acciaccatura { a16 } b4      // slashed grace
appoggiatura { c8 } d4       // unslashed grace
```

## Multi-voice (one staff)

```
voice { c'2 d } { e2 f }     // each voice { } is a simultaneous voice
```

## Lyrics (a named track, attached to a staff)

A `lyrics NAME { … }` track sits in a section next to the part it sings; the score
attaches it under a staff with `staff X with lyrics NAME`. Syllables are separated by
spaces; `-` joins syllables of one word; `|` mirrors the music's barlines. Barlines
follow the music rule: a lone leading `|` only anchors the start (`| きら | ひかる |`
== `きら | ひかる`), a bar with no syllables is the explicit `| |` pair (e.g. a
leading `| |` skips the melody's opening rest bar).

```
part melody
section Main {
  melody { c4 d e f | g2 g | }
  lyrics words { Hap- py birth- day | to you | }
}
form main { Main }
score main { staff melody with lyrics words }
```

## Lead sheet (chords and/or lyrics, no staff)

A NAMELESS `chords { … }` block inside a section aligns its symbols above the
co-written part's staff by timing; a NAMED part can be aligned the same way with
`staff melody with chords prog` in the score - and the SAME `prog` can also be a
lead-sheet row, written once. An independent `chords NAME { … }` and/or `lyrics NAME { … }` part, placed in a
`score` with `chords NAME` / `lyrics NAME` (instead of `staff NAME`), renders WITHOUT
a staff: just a grid of measure barlines, the chord symbols between them and the
lyrics below. A chord entry is `root[duration][:quality][/bass]` (`c`=C, `a:m`=Am,
`g:7`=G7, `c/g`=C over a G bass) and honours its duration; lyric syllables fill each
bar. Barlines in the source (`|` `|:` `:|` `||` `|.`) are drawn, and follow the same
bare-barline rule as music and lyrics: a lone leading `|` only anchors the start
(`| c1 | f1 |` == `c1 | f1 |`), an empty bar is the explicit `| |` pair.

```
section Main {
  chords prog  { c2 g:7 | a:m f | c1 :| }     // C G7 | Am F | C (repeat)
  lyrics words { Twin- kle | lit- tle | star | }
}
form main { Main }
score main "sheet" { chords prog lyrics words }     // chords + lyrics rows, no staff
```

## Structure: reuse and navigation

```
form main { Intro Main Main "Main (reprise)" Coda }   // string = custom section label
```

Navigation marks sit between section names. Signs `segno` / `coda` engrave at the start
of the following section; text directives `fine`, `to coda`, `dc`/`ds` (and `dc al fine`,
`ds al coda`) engrave at the end of the section just played.

```
form main { A segno  B to coda  C ds al coda  coda D }
```

In-note marks: `c4@mark("A")` (rehearsal mark), `@segno @coda @fine @dc @ds`,
text spanners `@rit` / `@accel`, ottava `@ottava` / `@ottava(bassa)` ... `@loco`,
trill spanner `@startTrillSpan` ... `@stopTrillSpan`, 15ma `@quindicesima` / `@quindicesima(bassa)`,
pedals `@sustainOn`/`@sustainOff`, `@sostenutoOn`/`@sostenutoOff`, `@unaCorda`/`@treCorde` — one word each,
LilyPond's own names, taking NO argument (`@ped`, `@ped(off)`, `@sost(off)`, `@una(corda)` do not exist).
An annotation's argument always goes in PARENTHESES — a dot after the name is the placement qualifier
instead (`@fermata.up`), so `@notehead.x` does not work either.
(`@ds al fine` etc. is the navigation form used inside `form main { }`.)

## Multiple forms (excerpts)

Declare several named forms and bind each `score` to one by name. The reserved
form `main` writes to the input file's name; any other form name becomes the
output file name (unless a `"basename"` overrides it).

```
form main { Intro Verse Outro }
form practice { Verse }
score main { staff melody }
score practice { staff melody }
```

## Override / revert (engraving properties)

```
override Stem.length = 7          // value fits the property: number, identifier (up/red), or "string"
override NoteColumn.force-hshift = 1.5   // fractional values are allowed, and negative ones (-0.5)
c4 d e f |
revert Stem.length
once override Stem.length = 9     // 'once' applies to the next note only
c4 d e f |
```

A decimal is a VALUE, not a duration: `c4.5` is an error (LYS0021), `c4.` is a dotted
quarter. Same in a tempo — `tempo 4. = 116` is dotted, `tempo 4.5 = 116` is LYS0022.

## Rules and gotchas

- Each **phrase body** evaluates in a fresh frame (default octave/pitch/duration), so a
  phrase means the same notes at every call. **Section boundaries also reset the frame.**
  A reference is ONE item to the relative chain (the chord rule): the next note is
  relative to the phrase's ANCHOR — its first note's bare letter, shifted with the
  reference's marks — never to how the body ends.
- A reference's trailing marks shift octaves (`Chorus'` / `Chorus,`); a GLUED `'(N)`
  is a DIATONIC interval — `Melody'(3)` plays the phrase a third up in the ambient key
  (the quality follows the scale), `Motif,(2)` a second down; `'(8)` == `'`. Spaced,
  ` (` still opens a slur. Great for sequences and parallel-third harmonies.
- Part header attributes (`clef`/`key`/`time`/`tempo`) are written **bare, no `=`**, like
  the top-level commands. Override/revert use `=`.
- `removeEmpty true|all` in a part header hides that part's staff in systems where it only
  rests (hara-kiri). `true` keeps the first system (LP `\RemoveEmptyStaves`), `all` hides it
  too (`\RemoveAllEmptyStaves`); any playing voice keeps the staff visible.
- Identifiers (parts, phrases, sections) may use any Unicode letters: `phrase 動機 { ... }`.
- Everything is **case-sensitive**: keywords, identifiers, and vocabulary values (clef /
  instrument-preset / tuning names, key modes) are written in their canonical (lowercase)
  case. `Treble` is a different, unknown symbol from `treble` and is an error — not a
  silent fallback.
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
section structure include tab ossia transpose octave instrument
score part staff grandStaff voice phrase repeat volta alternative break partial
title composer tempo time key clef
major minor ionian dorian phrygian lydian mixolydian aeolian locrian
treble bass alto tenor treble_8
tuplet grace acciaccatura appoggiatura lyrics chords tuning
override revert once with
segno fine coda dc ds al to
ppp pp p mp mf ff fff   (f is a PITCH; @f still works - dynamics resolve from text)
```

Also special: single letters `a`-`g` are pitches; `r`/`R`/`s` are rests. Articulation,
ornament, dynamic-text and mark NAMES (`staccato`, `tr`, `mordent`, `cresc`, `dim`, …) are
NOT reserved — they are resolved from the `@name` text — so they remain free for your own
identifiers.
</content>
