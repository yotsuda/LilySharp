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
subtitle "Subtitle"     // optional: the line under the title (LilyPond's \header subtitle)
poet "Poet"             // optional: the left end of the composer's line (\header poet)
tempo 120               // optional; also: tempo "Allegro" 120, tempo "Andante" 4 = 96 (text + beat unit), tempo "Lively" 4. = 116 (dotted unit), tempo Comodo 4 = 84 (a bare word is the marking); 'tempo 120 swing' adds a shuffle-feel equation ('swing 16' = 16th swing; only 8 or 16; 'tempo swing' = the equation alone)
time 4/4                // optional (default 4/4); 4/4 engraves as the C
                        // (common time) glyph and 2/2 as cut-C, like LilyPond.
                        // 'time none' = senza misura (LilyPond \cadenzaOn): until the next
                        // 'time N/M' a measure ends only at a written '|' (drawn, breakable),
                        // no meter is drawn, no automatic beams from a span opened at a bar
                        // line (write c8[ d e f]; a beam already building when 'time none'
                        // arrives mid-bar runs on), no length checks, a 'partial' inside it
                        // does nothing (LYS2015), and the bar number does not advance across it
key c major             // optional (default c major); all church modes work:
                        // major minor ionian dorian phrygian lydian mixolydian aeolian locrian
                        // (key d dorian = no accidentals, key e dorian = 2 sharps)
                        // (a pickup is 'partial': the SECTION header for a section's opening
                        //  bar, or in the music at a LATER bar's start — see below; the top
                        //  level rejects it)
fonts {                  // optional; binds text faces. The two generic families together
  serif "Georgia"       // are "the whole document's text"; bind roles separately below
  sans  "Georgia"       // 'embedded' subsets every named face into the PDF
}
paper {                  // optional; page dimensions (defaults = LilyPond's a4)
  paperWidth 210mm      // bare numbers are staff spaces; units mm/cm/in GLUED (210mm)
  paperHeight 297mm     // see the paper section below for margins/indents/spacing
}
layout {                 // optional; the score-wide display switches (see the layout section
  markTempo stacked         // below): how a section label and the tempo at the same bar are
  barNumbers lines      // arranged, which bars carry a number, and which notes carry a
  accidentals default   // printed accidental. All three shown at their defaults.
}

part rightHand { clef treble }  // declare each part; clef lives here (it only DRAWS —
part leftHand  { clef bass octave 3 }  // the register is `octave N` or an instrument preset)
                                // a part name is any bare word except the structural
                                // keywords (voice staff section key time … — see below)

phrase motif { c4 d e f | }     // optional reusable music, referenced by bare name

section Main {                  // a section binds music to each part by name
  partial 8                     // optional pickup: shortens THIS section's opening bar
                                // for every part at once — the ONLY place for it (the top
                                // level, a part header and a part's first bar reject it).
                                // Later in a part's music, `| partial 2. r2. |` makes THAT
                                // bar 3 beats long — written in every part sharing the bar
  rightHand { motif g2 g | }
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
printed. One staff takes one key, meter and clef at a moment: a `time` both parts write is
drawn once, and a later part's `key` / `time` / `clef` that differs from the first part's at
the same moment is not applied and warns (LYS4024) — write the same change in every part.

**Several staves as ONE GROUP** — `grandStaff`, `staffGroup` and `choirStaff` all take
`staff` items and nothing else, and differ only in the left edge:

| | left edge | bar lines between staves | pick it for |
|---|---|---|---|
| `grandStaff` | brace | drawn through | one instrument on two staves (piano, harp) |
| `staffGroup` | bracket | drawn through | one family (the woodwinds) |
| `choirStaff` | bracket | **not** drawn through | independent lines (voices) |

```
score choral "satb" { choirStaff { staff sop  staff alt  staff ten  staff bas } }
score winds  "winds" { staffGroup { staff flute  staff oboe  staff clarinet } }
```

Each is the LilyPond context of the same name (`\new GrandStaff` / `\new StaffGroup` /
`\new ChoirStaff`). ⚠️ `staffGroup` is in that order on purpose and is **not** a typo for
`groupStaff`: the other `…Staff` items each produce a staff, while a staff group produces a
group of staves.

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

**A score's own header, and parts that only play** — `title` / `subtitle` / `composer` /
`poet` written inside a score restate the file's metadata for that score alone, and a **bare part name** renders
that part to MIDI only (played, never engraved — a click track, a cue part):

```
score winds "winds" {
  staffGroup { staff flute  staff oboe }
  title "Woodwinds"    // this score only
  click                // played, never engraved
}
```

**A score's own display switches** — `layout NAME` inside a score body references a named
top-level `layout NAME { … }` block (the `fonts` / `paper` shape), replacing the file's
unnamed `layout { … }` default for that score alone; an override block on the reference
restates part of it:

```
layout { markTempo beside }                              // the file default: chart layout
layout lp { markTempo stacked  barNumbers every 4 }      // a named block
score main  { staff melody }                         // chart layout
score parts { layout lp  staff melody }              // this score keeps LilyPond's stacking
score study { layout lp { barNumbers none }  staff melody }
```

A staff's display name is a quoted string (`staff flute "Piccolo"`) — a bare word after
`staff NAME` is always another score item (`staff flute click` is flute's staff plus the
`click` MIDI-only part), so position never changes what a word means.

A minimal single-staff document:

```
part melody { clef treble }
section Main { melody { c4 d e f | g2 g | } }
form main { Main }
score main "out" { staff melody }
```

⚠️ That document prints a boxed **"Main"** over its first bar: every section reference prints
its name as a section label by default. A piece with one section rarely wants that — write
`form main { ~Main }` (the `~` hides the label; see "Rules and gotchas"). A `@mark("A")` on a
note in that first bar is NOT printed while the label is (one mark a bar; LYS4021 says so) —
put the rehearsal letter on a later bar, or hide the label.

**Music always lives inside a part.** A file is a set of declarations; a note stream at the
top level is an error (LYS0020), as are a top-level `{ … }` block, `grace`/`tuplet` group,
`break`, or phrase reference. This is what makes a top-level `clef`/`key`/`time`/`tempo`
unambiguous: with no music able to stand beside them they are always the FILE DEFAULTS, and
a directive written among the notes is always a mid-music change.

A `time`, `key` or `clef` that changes nothing **draws nothing** — including one that opens a
section with what the section before it left (the boundary resets them, so a section that
continues in E♭ must say `key ees major` again, and that line is not engraved). To draw one
anyway, put `!` right after the keyword: `key! ees major`, `time! 3/4`, `clef! bass`. A `!`
after the value is the dashed barline (`key ees major !` = a key, then a dashed bar).

Below, a code block that is only music (no `part`/`section`/`score`) is showing a **section
body** — the four lines above are omitted so each example shows just what it teaches. To run
one, put it where `c4 d e f |` sits in the minimal document.

## Strings

Quoted text follows C#: `"say \"hi\""`, `"a\\b"`, `"\n"`, `"\u00e9"` (escapes `\" \' \\ \0 \a
\b \f \n \r \t \v \uXXXX \UXXXXXXXX \xH…`; any other backslash is error LYS0036), and the
verbatim `@"C:\music"` / `@"say ""hi"""` (backslash as written, `""` = one quote).

## Pitches

- Names: `c d e f g a b`. Sharp `is`, flat `es`: `cis`=C#, `ees`=Eb, `cisis`=C##, `deses`=Dbb. E/A flats also contract: `es`=`ees`, `as`=`aes`, `eses`=`eeses`, `ases`=`aeses`.
- Octave: `'` up one, `,` down one (repeatable: `''`, `,,`).
- Default octave is C4. Each bare pitch takes the octave nearest the previous note
  (an interval of a fourth or less); `'`/`,` shift from there.
- `octave absolute` (top level, part header, or mid-music) switches to ABSOLUTE
  mode: bare `c` = C4 and `'`/`,` are absolute offsets from that anchor (`c'` = C5,
  `c,` = C3), independent per note - octave mistakes never cascade. RECOMMENDED
  when generating scores. `part X { octave 2 }` re-anchors the base (bass parts).
  Sections restore the file-level mode.
- `pitch concert` (top level) says the letters are what SOUNDS: a part whose `instrument`
  transposes (clarinet, trumpet, horn, alto-sax, tenor-sax, …) is then PRINTED the way its
  player reads it, pitches and key signature together — an alto-sax `c'` prints as `a'` in
  A major when the piece is in C, and still plays C. The default, `pitch written`, prints
  the letters as they are and only playback moves. Octave-only instruments (bass, piccolo)
  keep their notation either way. A part header takes the same words for that part alone
  (`part sax { instrument alto-sax pitch written }` beside a top-level `pitch concert`: own
  wins, as with `transpose`), and without a transposing `instrument` the word changes
  nothing. `score full pitch concert { … }` prints THAT score at concert pitch (the
  conductor's score) whichever way the file is written.

```
c d e f g a b c    // C4 D4 E4 F4 G4 A4 B4 C5
c' c,              // C6 C5
```

## Durations

- Numbers: `1`=whole, `2`=half, `4`=quarter, `8`, `16`, `32`, `64`, `128`.
- Dots after the number: `4.`=dotted quarter, `4..`=double-dotted.
- Omitting the duration reuses the previous note's duration.
- A duration standing ALONE repeats the previous note/chord/slash at the new
  length (LilyPond's isolated-duration reading): `bes8 8 8 8` is a bass pump.
  It also sets the running default. Rests are transparent; with nothing before
  it to repeat it is an error (LYS0016). A repeat reaching back ACROSS a
  barline warns (LYS1031) — that shape is also a dropped pitch letter
  (`4 g f e` meant as `a4 g f e`) — so open a measure with the event itself.

```
c4 d e f    // all quarters
c8 d e f    // all eighths
bes8 8 8 8  // bes eighths, written once
<c e g>4 4  // the chord again
```

## Notes, rests, chords

```
fis8        // F# eighth
r4          // quarter rest
s4          // invisible spacer rest
| R         // full-measure rest: lasts ITS BAR in any meter (5/4, 7/8, a pickup); must OPEN the bar (LYS2016)
| R*3       // one 3-bar rest (a count above it); `R | R | R` stays three 1-bar rests
| R1 |      // a bar rest of a written duration (a whole note), as in LilyPond; a bare R moves no running duration
a,4@rest    // quarter rest printed where the note a, would sit (a PITCHED rest)
<c e g>4    // chord (shared duration after '>'), C major triad as a quarter
<c 3 5>4    // the same triad by scale degrees (root + 3rd + 5th of the key)
<1 3 5>2    // degrees only: anchored on the key TONIC (C E G in C major)
q           // THE PREVIOUS CHORD AGAIN, at the running duration
q2          // ...at its own duration; q@staccato takes its own post-events too
q'          // ...an octave up (q,, two down) — the marks ACCUMULATE, see below
```

`q` repeats the chord before it (notes and rests are transparent; only a written
`<…>` chord replaces it, and the run does not leak across a part/section/phrase
body). The original's own articulations are NOT copied — `q` carries only its own.
With no chord before it, `q` warns and occupies its time silently.

⚠️ **`q` takes octave marks and a bare duration cannot** — that is the difference
between them. `<c e g>4 4` and `<c e g>4 q4` engrave and play identically (measured:
same SVG, same MIDI), because a bare duration is a LENGTH — "the previous event
again, this long" — and a length has nothing to displace, so `4'` is not a spelling.
`q` is an EVENT — "this chord again" — so it takes a duration, post-events, and
octave marks. **The displacement accumulates along the chain**: `q' q` sounds the
chord up an octave twice, `q' q'` climbs by one then two, and a bare duration in the
run repeats the chord where the last `q` left it (LILYSHARP-OWN — LilyPond's `q`
takes no marks; user decision 2026-09-03).
⚠️ `lysc ly` cannot express a displaced `q`: LilyPond expands chord repetitions
*after* `\transpose` is applied, so the wrapper reaches an empty placeholder
(measured against 2.26.0). The twin writes a plain `q` and warns.

A rest normally places itself: the middle line, the voiced position inside a
`voice { } { }` span, and clear of the notes sounding with it. `@rest` on a NOTE
overrides that — the rest sits where that pitch would and nothing moves it again,
which is how two voices' colliding rests get pulled apart. The pitch never sounds and
prints no accidental. On anything but a note (`r4@rest`, `<c e>4@rest`) it is an error.

A duration is GLUED to what it lengthens — `c4`, `<c e g>4` — and never sits on
a chord/arpeggio member (`<c e g2>` is an error, LYS0015). A SPACED number
inside brackets is a scale degree (`<c e g 2>`); OUTSIDE brackets it is a bare
duration repeating the previous event (see Durations above).

`/` in note position is a SLASH NOTE — rhythm (comping) notation: a pitchless
note drawn as a slash head on the middle staff line, silent in playback, with
ordinary duration/stem/beam behaviour. `/4 4 8 8 4` is a comping figure;
combine with `staff comp as lines 1` in the score for a one-line rhythm staff. On a tab staff
a slash has no fret number: a `tab` that draws stems shows its stem and beam, one `as numbers`
shows nothing. `time 4/4`,
`tuplet 3/2` and `c/g` keep their own `/`.

Chord octaves — the ANCHOR model (one rule: a mark moves only what it is attached to):
the anchor is the first member's bare LETTER (or the key tonic for a degrees-only
chord), resolved nearest to the previous note; members sit at-or-above it, so order is
free except the first slot (`<c e g>` = `<c g e>`; degrees are fully order-free). A
`'`/`,` on a member moves THAT note only — the first member's included: `<c' e g>` =
C5 E4 G4 and the next bare c is still C4. A `'`/`,` AFTER the `>` (before the duration)
moves the whole chord AND the anchor, so it propagates: `<c e g>'4 c` = C5 E5 G5, C5.
The note AFTER a chord is relative to the chord's ANCHOR, exactly as after a single note
— a chord is ONE item of the relative chain: `g1 <c e g>1 <f a c>1` = G3, C4 E4 G4,
F4 A4 C5 (the f reads the chord's c, not the g), and `<c, e g> <c, e g>` repeats the
same C3 E4 G4 because the anchor is the bare c, not the lowered one. Degree chords
hand on the TONIC, not the first degree written.

A CHORD FROM A SHAPE — `chord(SYMBOL SHAPE)` + a chord's tail (duration, dots, ties, slurs,
beams, scripts, dynamics): writes the notes a fretted shape sounds (2026-09-28).

```
chord(C x32013)1            // C3 E3 G3 C4 G4 on a guitar — each string's open pitch + fret
chord(Cm7 x3x546)2@chord    // + name and diagram: a bare @chord takes the item's words
chord(C xx-10-12-13-12)4.~  // frets 10-15 in the dash form
```

- The words are `@chord(…)`'s (a symbol + a shape, or a shape alone); the shape is REQUIRED —
  `chord(C)` warns (LYS1040) and is a SPACER of its length.
- Tuning: the PART's fretted instrument (its tab tuning), else the guitar. The shape gives
  SOUNDING pitches; the staff writes them as the part writes any sounding pitch (a guitar
  part, treble_8, an octave up). ABSOLUTE: the frame, `octave absolute` and marks do not move
  it; marks after `)` are an error (LYS0035).
- The note AFTER it is read from its LOWEST sounding note (relative mode).
  A phrase whose body opens with it hands the same note on after its reference.
- Chord tones are spelled from the symbol (Cm7 → E♭ B♭); every note carries its string
  number (a tab shows the shape). It draws no name/diagram itself.
- `chord` is reserved in music: a phrase cannot be named it.

## Arpeggios `<< … >>` (written-out broken chords)

Members play in SEQUENCE and EQUALLY SUBDIVIDE the group's total (no per-member
durations — a bare number is always a scale degree). Octaves follow the chord anchor
model above — the next note reads the group's anchor, not its last member; a
degrees-only group anchors on the tonic. NOT LilyPond's `<< >>`
(parallel voices) — those are `voice { }` in Lily#; a `\\` inside is an error.

```
<< c e g >>      // c, then e/g stacked above (E4 G4); after c4 → a triplet of eighths (3:2)
<< c 3 5 >>      // by degrees: c e g
<< 8 5 3 1 >>    // degrees-only anchors on the TONIC: C5 G4 E4 C4 — descending, no marks
<< <c e> g >>    // a chord member, then g
<< c r e >>      // a rest is a gap (an equal share); e still stacks above c
<< c . d >>4     // a SPACED dot = one more share for the member before it: 2:1 → tuplet 3/2 { c4 d8 }
<< c . . d >>4   // 3:1 → c8. d16 (never glued: `c.` is a duration dot, `3.` a decimal)
<< c@accent e\3 g( a) >>  // a member carries scripts, string numbers, fingering, dynamics, slur marks
<< c e g >>4\2   // on the group: a dynamic, a chord name, a string number (every member's);
                 //   a '~' or '(' after >> hangs on the LAST member
<< c e g >>2     // a duration after >> = the group's total: 3 in a half (triplet 3:2 of quarters)
                 // (the convention's spelling: M against the power of two below it —
                 //  5 in a quarter → 16ths 5:4; 4 in a quarter → plain 16ths; 2 in a
                 //  dotted quarter → 8ths 2:3)
<< c e g >>'     // marks after >> shift the whole group and propagate to the next note
<< chord(C x32010) >>2   // a chord(…) member is SPREAD: its notes lowest first, one member each (5:4)
<< c chord(G 320003) e >>2   // mixed: c, G's six strings, then e (e stacks above the root c)
```

Must fit in one measure (else it overflows the meter).

## Annotations (`@name` attached to a note or chord)

Attach with `@`. One note may take several: `c4@staccato@p`. Two suffixes:
`.up` / `.down` forces an articulation/dynamic above / below the note (default is
automatic, opposite the stem): `c4@staccato.up`, `d4@accent.down`, `@f.up`.
An annotation that takes a VALUE puts it in parentheses (space- or comma-separated):
`@chord(Dm)`, `@figuredBass(6 4)`, `@mark("A")`, `@finger(3)`.
Names are case-sensitive, one spelling each: several words are camelCase even where
LilyPond is not (`@upBow`, `@shortFermata`, `@reverseTurn` — NOT `@upbow`), one word is
lowercase (`@staccato`, `@pralltriller`), as in `@hammerOn`, `@laissezVibrer`;
`@hammeron` is an unknown annotation (LYS1008 names the right spelling) and draws nothing.
Value words inside the parentheses are lowercase too (`@notehead(triangle)`,
`@diagram(x32010)`, `@figuredBass(6 s)`; `@notehead(TRIANGLE)` is unknown); free text
(`@text("Dolce")`) and chord symbols (`@chord(Dm)`) keep their case.

- Stem direction: `@stemUp` / `@stemDown` force a note's stem (default is automatic).
  On a beamed note the beam's shared direction wins.
- Articulations: `@staccato @staccatissimo @accent @tenuto @marcato @fermata @portato`
- String technique: `@upBow @downBow @flageolet` - always above
- Ornaments: `@trill @mordent @prall @turn @reverseTurn`
- Dynamics: `@ppp @pp @p @mp @mf @f @ff @fff` and the accent dynamics `@sfz @sf @fp @rfz @fz` (default below the staff; `.up` / `.down`
  forces the side, e.g. `@f.up`)
- Accidental style: `@courtesy` (cautionary, parenthesized), `@editorial` (musica ficta)
- Arpeggio: `<c e g>4@arpeggio`
- Glissando: `c4@glissando d` (line from this note to the next)
- Figured bass: `c4@figuredBass(6)` , `d4@figuredBass(6 4)`
- Chord diagram (guitar fret diagram): `c4@diagram(x32010)` — one character per string, low
  to high: a digit is the fret (`0` or `o` open), `x` muted — frets 10–15 with a `-` on each
  side (`@diagram(xx-10-12-13-11)`); 4–8 strings. Above the note
  whatever the stem; `@diagram(x32010).down` puts it below. MusicXML nests it as `<frame>`
  in the `<harmony>` of an `@chord` on the same note. Size: `fonts { diagram step +3 }`.
- Guitar bend: `c4@bend(half)` (1 semitone), `@bend(full)` (2), `@bend(N)` (N = 1–12
  semitones) — an arrow labelled in steps. Page only: the `.ly` twin drops it (warning),
  MusicXML does not carry it.
- Tab technique letters: `@hammerOn` (H), `@pullOff` (P), `@tap` (T) — small italic
  letters opposite the stem (`.up`/`.down` force), drawn on a `tab` staff too.
- Chord names: `c4@chord(C)` , `d4@chord(Dm)` , `e4@chord(Am7)` — the SYMBOL as it
  prints (`F#m`, `Bb7/D`, `Gm7-5`), the same format as a chords row. The retired
  lowercase `:` entry (`@chord(a:m)`) is not recognised (LYS1008 warns, no symbol is
  engraved). A bare `@chord` derives it from the notes. The symbol is the FIRST word only:
  `@chord(C 7)` is C with a one-character shape (warns) — write `@chord(C7)` for C7.
  It may sit on a rest or spacer too (`s1@chord(C)`, name and diagram as on a note); a bare
  `@chord` there has no notes to name and warns (LYS1020) — write the name.
- Chord diagrams (drawn under the name) appear ONLY where a chord WRITES its shape; a name
  alone (`@chord(G)`, a row's `G`) draws none — unless the score's layout says
  `chordDiagrams all` / `chordDiagrams guitar all`, where EVERY chord name (a bare `@chord`'s
  derived one too) draws its written shape, else the usual one — or LISTS the chord in its
  shape table: `layout { chordDiagrams guitar { Cm7 x35343  G  section Chorus { C x35553 } } }`
  (a listed chord draws wherever it is named — the table's shape, the usual one for a name
  alone; a `section NAME { … }` block's entries apply to the chords written in that section;
  a shape written at the chord still wins). A shape is one character per string, LOW string
  first (`x` muted, `o`/`0` open, a digit the fret): `@chord(Cm7 x3x546)`, `@chord(x32010)`
  (the name is derived from its notes); in a chords row glued to the symbol: `F(133211)`,
  `F(133211 2010)` (a guitar and a ukulele shape, routed by string count),
  `F(guitar 133211 ukulele 2010)` (bound by tuning name). Frets 10–15: put a `-` on each
  side of each two-digit fret — `@chord(Cm 8xx88-11)`, `Cm(xx-10-12-13-11)`,
  `Cm(8-10-10-888)`, `@diagram(x-15-13-12-13-x)`; a `-` between every string also reads
  (`x-x-10-12-13-11`). Between dashes, EXACTLY two digits are ONE fret: frets 10, 9, 9 are
  `10-9-9` (`10-99` = fret 99, an error); routed by string count (`8xx88-11` = 6); lower case
  only; use the one-character form when every fret is ≤ 9. The diagram's tuning: the score's
  `layout { chordDiagrams TUNING [all] [{ table }] }` (the tab tuning words; `none` = no diagrams at all), else
  the part's fretted instrument (an `@chord`'s part; for a row, the staff it stands directly
  above), else the guitar. Common guitar shapes (LilyPond's): C `x32010`, F `133211`, G
  `320003`, Cm7 `x35343`; ukulele C `0003`. A written shape that cannot be used warns LYS1038;
  the name still draws. A written shape that disagrees with its symbol (a non-chord tone, or a
  missing 3rd/7th/altered fifth/tension — the root and the perfect fifth may be left out; the
  bass is not checked) warns LYS1039 naming the fix. There is no voicing index and no `mute` word.
- Fingering (per chord note): `<c@finger(1) e@finger(3)>4`
- Rehearsal mark: `c4@mark("A")`
- Half ties: `c4@laissezVibrer` (l.v. into silence), `c4@repeatTie` (resume from a repeat)
- Effects: `@cross`/`@dead` (x notehead), `@fall`/`@doit` (jazz bends), `@breath`/`@caesura`
- Cue notes: `cue { … }` — a REGION, not an annotation, so there is no `@cue`:
  `c4 d cue { e4 f } g4 |` (it maps onto LilyPond's CueVoice context). Name the quoted
  instrument's clef to read the cue in it: `c4 d cue bass { e4 f } g4 |` — the staff's own
  clef returns after the region. A slur or tie may NOT cross the region's edge (LYS4012):
  a cue is a voice of its own, so close the span inside the cue or keep both ends outside.
  Two `cue` blocks side by side are two voices — a span may not run from one into the next
  either, even though both of its ends are cue notes.
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
r8[ c d e]           // a manual beam may open or close on a rest, and reaches it
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

- Barlines in MUSIC: `|` single, `||` double, `|.` final, `!` dashed.
  **The repeat barlines `|:` `:|` `:|:` are NOT music items — they go in the `form`**
  (LYS1034, user decision 2026-08-31). The line is "does it change the playing ORDER":
  a repeat does, so it belongs where the order is written.
- **A written `|` closes exactly one measure, and a measure with nothing in it is an
  empty one** — so `{ | | | | }` is four empty bars and `{ | c1 }` is an empty bar then
  `c1`. An empty bar is filled with a full-measure spacer (`| |` == `| s1 |`, on the page
  and in playback), so it is never diagnosed. Typed barlines close nothing: `||`/`|.`
  on an empty span DECORATE the bar behind them; and a `|` landing where the
  meter just auto-filled a bar merely confirms it — which is why a trailing `c1 |` is one
  bar, not two.
- Volta repeats are symbolic and live in the form: `form main { |: A [1. B] :| [2. C] }`.
  An ending NAMES one or more sections, played in order under one bracket:
  `|: A [1. B C] :| [2. D]` plays A B C, then A D. The body before the first ending must
  name a section (`|: [1. B] :| [2. C]` and `|: :|` are errors, LYS1041).
  With endings the numbers are the passes: `|: A [1-2. B] :| [3. C]` plays A B A B A C, and
  every pass from 1 to the highest number must be named by exactly one ending (LYS1043); a
  `:|*N` beside endings is an error (LYS1042). Without endings the body plays twice, or
  `|: A :|*N` N times. A bracket prints its passes as LilyPond does: `[1-2.` "1. 2.",
  `[1-3.` "1.–3.". The `]` ends the ending and hooks its bracket's right end
  down; `-]` ends it with a straight (open) right end: `|: A [1. B] :| [2. C D -]`. The
  `]` may be left off only right before a `:|` (`|: A [1. B C :| [2. D]` — the `:|` closes
  it, hooked); an unclosed LAST ending is an error. The first ending is the last thing
  before the `:|`, and each later ending follows its own `:|`: `:| [3. D]` again.
- How far the bracket reaches: `layout { voltaBracket all|line|N }` — `all` (default) every
  bar of the ending, `line` up to the end of the system it starts in, `N` its first N bars.
  One ending overrides it after its `]`: `[1. B C]@voltaBracket(2)`. A bracket cut short
  always ends straight, whatever `]` / `-]` says.
- An ending needs a repeat to be an ending OF. Write `form main { [1. A] }` and no bracket
  is drawn: it engraves as the plain reference `A`, and LYS6008 warns that the `1.` prints
  nothing. Put the ending inside the repeat — `form main { |: A [1. B] :| [2. C] }`.

```
part melody { clef treble }
section A { melody { c4 d e f | } }
section B { melody { g2 g | } }
section C { melody { a2 a | } }
form main { |: A [1. ~B] :| [2. ~C] }
score main { staff melody }
```

- A bar may be split by a SECTION boundary: end one section on a short bar and open the
  next (every section the form plays next) with exactly the rest of it — how a repeat sign
  or a volta ending lands mid-bar, `|: A [1. B] :| [2. C]` with A ending `g4 a |` and both
  endings opening `b4 c' |` in 4/4. The two halves are one bar to the bar check (no LYS2001,
  no LYS2006), provided every neighbour completes it exactly, and one bar to the numbering
  (the written bar line between them is still drawn). A later ending continues the body's
  bar the same way, but its number keeps counting from the ending before it, as in LilyPond.
- A slur, phrasing slur, tie or hairpin still open when a section ends is carried into the
  section the form plays NEXT and must end there (checked per form, per part, per play; LYS4023
  warns otherwise). A slur, phrasing slur or hairpin may NOT cross a repeat sign, a volta
  ending's edge or a jump mark — an error. A TIE may: it reaches the first note of every section
  PLAYED next (the body again, the next ending), which must repeat its pitch (LYS4007); the page
  adds the repeat tie itself. Beams and other spans never cross a section.
  The relative frame still resets at the boundary, so a tie's target writes its octave.
- Line breaks: `break` / `noBreak` force / forbid a system break after the bar they stand
  in; `pageBreak` / `noPageBreak` do the same for the page. **A `break` written INSIDE a bar
  with notes on both sides — `c4 d break e f |` — breaks the bar there**: the first half ends
  the system with no bar line, the second opens the next with no bar line and no bar number,
  and every part and row is cut at that beat. Where another part holds a note sounding across
  the break, or a beam / tuplet / percent repeat runs across it, the bar stays whole, the
  break falls to the next bar line and LYS1037 says why — tie the note (`c2~ break c2`) or
  move the break to a beat nothing crosses. `e2 break |` (a bar line right after) is the
  ordinary bar-line break.
- Percent repeat (repeat the previous measure): `repeat percent 2 { c4 d e f | }`.
- NOT Lily#: `repeat volta` / `alternative` are LilyPond's spellings (the parser refuses
  `repeat volta` and points at the symbolic `|: ... :|` form above; `alternative` is just a
  word). `repeat` is only for `percent` / `unfold` / `tremolo`.

## Tuplets

```
tuplet 3/2 { c8 d e }                 // triplet: 3 in the time of 2
tuplet 3/2 { c8 d tuplet 3/2 { e16 f g } | }   // nesting allowed
```

## Repetition shorthand

`repeat unfold N { ... }` writes its body out N times - phrases welcome:

```
phrase ground { d2 a,2 | b,2 fis,2 | }
repeat unfold 8 { ground }               // 32 bars from one line
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

## Lyrics (a named track that SINGS a part)

A lyric track binds to its DEFAULT melody at the definition: `lyrics NAME sings PART`.
A score row may carry its own binding: `lyrics NAME sings PART` among the score
items binds THAT ROW's placement, overriding the default - so one track can be
placed under several parts (a chorale's one verse under soprano, alto, tenor and
bass: `staff alt  lyrics verse sings alt`). A row without `sings` takes the default.
The score places its row by ORDER (score = a vertical stack of bands): a
`lyrics NAME` row directly below the staff engraving the part it sings is that
staff's verse (a run of rows stacks as verses); anywhere else it shows only the
words, at the melody's rhythm, without engraving the melody. Multiple tracks may
sing one part (two languages = two names). A track named after the part or one of
its voices is bound by the name. An unbound row is the even-spread lead-sheet row
(directly below a staff whose part has named voices it is still drawn, with warning
LYS6013: name the track after the voice, or write `sings PART`);
inside a staff group a row must sing the staff directly above it (LYS6012).

A `lyrics NAME { … }` track sits in a section next to the part it sings; the score
places it with a `lyrics NAME` row under the staff. Syllables are separated by
spaces; `-` joins syllables of one word; `|` mirrors the music's barlines. Barlines
follow the music rule: every written `|` closes one bar, the one that OPENS the run
included, so `| きら | ひかる |` is one bar longer than `きら | ひかる` — that leading
`|` is how a verse skips the rest bar the melody opens with.
**Melismas are LilyPond's**: a SLUR or a TIE holds its first syllable over every note it
covers by itself (`c4( d e) f` with `la lu` puts `lu` on f — do NOT add markers for the
slurred notes). `__` only draws the extender line and takes NO note; `_` takes ONE note
with no syllable (a melisma without a slur: `c4 d e f` with `la _ lu li`).

```
part melody
section Main {
  melody { c4 d e f | g2 g | }
  lyrics words sings melody { Hap- py birth- day | to you | }
}
form main { Main }
score main { staff melody  lyrics words }
```

**Verses** (different words per pass of a section) are verse headers inside the lyrics
block: `[1. up up up up |] [2. down down down down |]`. The number is the section's Nth
play and prints as the stanza number `1.`; `[1,3. …]` / `[1-2. …]` cover several plays;
`[~1. …]` keeps the words and hides the number. Under `|: B :|` (printed once) the verses
stack under B as verses 1 and 2. This is NOT a repeat ending — it never changes the order.

```
part melody
section A { melody { c4 d e f | } }
section B {
  melody { g4 a b c | }
  lyrics words sings melody { [1. up up up up |] [2. down down down down |] }
}
form main { A |: B :| }
score main { staff melody  lyrics words }
```

## Lead sheet (chords and/or lyrics, no staff)

A `chords NAME { … }` part's symbols align above a staff by timing when its row
stands directly above that staff in the score (`chords prog` then `staff melody`)
- and the SAME `prog` can also be a lead-sheet row, written once. (The nameless
`chords { }` auto-attach form was removed - LYS0032: name it and place it.) An independent `chords NAME { … }` and/or `lyrics NAME { … }` part, placed in a
`score` with `chords NAME` / `lyrics NAME` (instead of `staff NAME`), renders WITHOUT
a staff: just a grid of measure barlines, the chord symbols between them and the
lyrics below. A chord entry is the SYMBOL as it prints — `C`, `Am`, `G7`, `F#m`,
`Bb7`, `Gm7-5`, `C/G` — with NO durations: a bar's entries divide it on the meter's
beat grid (one entry = the bar, two in 4/4 = halves, four = beats), and `.` holds
the previous chord one more beat (`| C . . G7 |`; a `.` never crosses a barline).
`r`/`R` print "N.C." in their slot; a `.` at a bar's head is the bar's SILENT slot
(`| . C |` = no chord, then C), and there is no `s`. Barlines in the source
(`|` `||` `|.`) are drawn, and follow the same bare-barline rule as music
and lyrics: every written `|` closes exactly one bar, the one that OPENS the run
included, so `| C | F |` is an empty bar and then two. A `|:` in a chord row IS a repeat,
so it goes in the form like any other (LYS1034); the repeat barlines the form composes
still reach the row and draw as real repeat barlines.

```
section Main {
  chords prog  { C G7 | Am F | }          // two halves | two halves
  lyrics words { Twin- kle | lit- tle | }
}
section Loop {
  chords prog  { C | }
  lyrics words { star | }
}
form main { Main |: ~Loop :| }
score main "sheet" { chords prog lyrics words }     // chords + lyrics rows, no staff
```

## Structure: reuse and navigation

```
form main { Intro Main Main "Main (reprise)" Coda }   // string = custom section label
```

A trailing `'` / `,` on a reference shifts THAT play's octave (one per mark):

```
form main { Intro Main ~Main' Coda }
```

Navigation marks sit between section names. Signs `segno` / `coda` engrave at the start
of the following section; text directives `fine`, `to coda`, `dc`/`ds` (and `dc al fine`,
`ds al coda`) engrave at the end of the section just played.

```
form main { A segno  B to coda  C ds al coda  coda D }   // the MIDI plays A B C B D
```

The MIDI follows a FORM's jump texts as a player reads them (`dc` to the beginning, `ds` to
after the last `segno`; `al fine` ends at the first `fine` of the replay; `al coda` goes on
from the `coda` after the jump; a bare `dc`/`ds` goes on after the jump; repeats play once on
the replay). A `ds` with no `segno`, and a mark written in the music, are drawn only. A
jump text whose landmark is missing in the form (`ds` with no `segno` before it, `al fine`
with no `fine`, `al coda` with no `to coda` or no `coda` after it) warns (LYS4025) and names
the fallback the MIDI takes.

The words are **form-only** (LYS1034, user decision 2026-10-04 — the same line as the repeat
barlines): a navigation mark written in a section's music (`segno c4 d e f |`,
`c4 d e f | ds al fine`) is an error, because the route is read off the form alone and a mark
there was drawn and never followed. A landmark that falls inside a section is written by
cutting the section there (`~` hides the label). They are landmarks, never note modifiers, so
`c4@segno` is an error too (LYS1022).

In-note marks: `c4@mark("A")` (rehearsal mark),
text spanners `@textSpan("poco rit.")` ... `@!textSpan` (sugar: `@rit` / `@accel` / `@rall`,
closed by `@!rit` / `@!accel` / `@!rall` — **the end is REQUIRED**: a spanner nobody closes
draws nothing at all, its word included, and says so with LYS4018),
ottava `@ottava` / `@ottava(bassa)` / `@quindicesima` ... `@!ottava` (**the end is REQUIRED**;
one `@!ottava` closes any of them, and `@loco` is retired),
trill spanner `@startTrillSpan` ... `@stopTrillSpan`, 15ma `@quindicesima` / `@quindicesima(bassa)`,
pedals `@sustain` ... `@!sustain`, `@sostenuto` ... `@!sostenuto`, `@unaCorda` ... `@!unaCorda`
(`@treCorde` is the same release written as the word the Text style prints) — one word each,
LilyPond's own names, taking NO argument (`@ped`, `@ped(off)`, `@sost(off)`, `@una(corda)` do not exist).
A pedal CHANGE (release and re-press on the same note, LilyPond's `\sustainOff\sustainOn`) is
the start again while the pedal is down: `g,4@sustain` — the bracket draws its notch there, and
the one `@!sustain` at the end closes the span. `g,4@!sustain@sustain` means the same. How the span is
drawn is the PART's: `part lh { clef bass pedal text }` (`text` = "Ped. … *", `bracket` =
what an unset part draws, or `mixed` = text at the start, bracket for the hold).

```
d,4@sustain a, d a, | g,4@sustain d g d | a,1@!sustain |
```
Phrasing slur `@phrasingSlur` ... `@!phrasingSlur` (LilyPond's `\(` ... `\)`; **the end is REQUIRED**):
the long curve over a musical sentence, drawn over the ordinary slurs `( )` inside it. One is open
per voice at a time — they do not nest.
```
c'4@phrasingSlur d'( e') f' | g'( a') b' c''@!phrasingSlur |
```
An annotation's argument always goes in PARENTHESES — a dot after the name is the placement qualifier
instead (`@fermata.up`), so `@notehead.x` does not work either.
(The navigation marks above are the bare form — `ds al fine`, no `@` — in a form and in music alike.)

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

⚠️ The vocabulary is FOUR properties — `NoteHead.transparent`, `Stem.transparent`,
`NoteHead.color`, `Stem.color`. The syntax accepts any `Grob.property`, but anything
outside that list is refused (LYS1029, "not supported in this version") rather than
silently doing nothing. The list grows; each addition removes one error. All four take
effect (`NoteColumn.force-hshift` left the vocabulary 2026-08-23: its implementation is
disabled for the initial release, so writing it is an honest LYS1029 instead of a silent
no-op; it returns when the per-voice implementation lands).

```
override NoteHead.transparent = true     // value fits the property: number, identifier (true/up/red), or "string"
override NoteHead.color = red            // named colour, or a "#rrggbb" string
c4 d e f |
revert NoteHead.transparent
once override Stem.transparent = true    // 'once' applies to the next note only
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
- A reference's trailing marks shift octaves (`Chorus'` / `Chorus,`). There is no
  per-reference transposition: a glued `'(N)` diatonic interval was removed 2026-08-28,
  and `transpose` is a part property, so a motif quoted at another interval
  is written out. ⚠️ **`transpose` takes a PITCH, not a number of steps** —
  `part cl { clef treble transpose bes }`, and `transpose -2` is an error naming the
  pitch form. Do not confuse it with `transposition`, a *different* part property that
  takes an octave marker (`8va` / `8vb` / `15ma` / `15mb`) and nothing else. For a B♭
  or E♭ instrument you normally write NO `transpose` at all: name the `instrument` and
  either write what the player reads (the default) or write what sounds under a top-level
  `pitch concert` — the part is transposed for you (see Pitches).
- **A section's label is hidden at the FORM reference only**: `form main { A |: B [1. ~B1]
  :| [2. ~B2] }` plays B1 and B2 without a rehearsal letter. ⚠️ **Never write
  `section ~A { … }`** — a declaration takes no tilde (LYS0033). Write the `~` on every
  reference that should be silent (a section cut only to carry a repeat edge is usually
  referenced once). With no form, every section labels itself. An empty label `A ""` also
  suppresses, and a label written on a `~` play is LYS0012.
- **A SECTION reference takes the same marks**: `form main { ~A ~B' }` opens B's play an
  octave up, `~B,` an octave down, `~B''` two. They belong to the PLAY, so one section can
  be quoted at two octaves (`~B ~B'`) while the declaration never moves, and the next
  reference is back at the part's anchor. Both spellings take them (`B'` and `~B'` — the
  tilde hides only the label), a volta ending takes them (`[1. B']`), and they work in
  `octave absolute` too. This is how a section cut only to carry a repeat says its music
  belongs an octave away: the boundary's frame reset stays, and the carry is written down.
- Part header attributes (`clef`/`key`/`time`/`tempo`) are written **bare, no `=`**, like
  the top-level commands. Override/revert use `=`.
- `staff NAME as removeEmpty true|all` in a SCORE hides that staff in systems where it only
  rests (hara-kiri). `true` keeps the first system (LP `\RemoveEmptyStaves`), `all` hides it
  too (`\RemoveAllEmptyStaves`); any playing voice keeps the staff visible. It chains with
  the line count after one `as` (`staff m as lines 1 removeEmpty all`) and is NOT a part
  property: the full score hides the empty systems, the part sheet of the same part never does.
- Identifiers (parts, phrases, sections) may use any Unicode letters: `phrase 動機 { ... }`.
- Everything is **case-sensitive**: keywords, identifiers, and vocabulary values (clef /
  instrument-preset / tuning names, key modes) are written in their canonical (lowercase)
  case. `Treble` is a different, unknown symbol from `treble` and is an error — not a
  silent fallback.
## Text fonts (`fonts { … }`)

A face, a size and a style per kind of text. Keys are a generic family, a group, or a
single role; an entry is the key followed by its attributes in any order and ends at the
next key; the NARROWER spelling wins in either source order.

```
fonts {
  serif     "Georgia"                         // everything serif unless overridden
  sans      "Verdana"                         // chord symbols are the one sans role
  lyrics    "Charis SIL" "Noto Serif CJK JP"  // several names = a fallback chain
  title     "Cormorant" size 3.8 bold         // one role: face, absolute em, weight
  marks     "Georgia"                         // a whole group
  tempo     "Playfair Display" italic         // ...beats the group above; italic replaces bold
  mark      step +1                           // one LilyPond font-size step larger than default
  numbers   step -1                           // the whole group one step smaller
  chord     as sans                           // point a role at a bundled family (as FAMILY)
  embedded                                    // subset the named faces into the PDF
}
```

Attributes: quoted faces · `as serif|sans` · `step ±n` (relative, magstep 2^(n/6), −12..+12;
the twin writes it as `\override Grob.font-size`) · `size n` (absolute em in staff spaces,
0.5..20; NOT reproduced by the twin, prefer `step`) · `bold` `italic` `regular` (combine;
`regular` clears; a written style REPLACES the engraving's, e.g. `text bold` is bold, not
bold-italic). One entry takes `step` or `size`, not both.

Groups → roles: `header` → `title subtitle composer poet instrument` · `lyrics` → the
syllables (no key of their own) and `stanza` · `chords` → `chord diagram figuredBass` ·
`marks` → `tempo mark pedal navigation text dynamics partCombineText` · `numbers` →
`barNumbers finger tuplet volta ottava bend tabTechnique` · `notation` → `clefOctave time tab`.
A key is spelled as the source writes what it styles (`@chord` → `chord`, `time 6/8` →
`time`, a `tab` staff → `tab`); a key with no single source word names the family (`pedal`,
`navigation`, `dynamics`, `tabTechnique`) or the printed text (`stanza`, `volta`,
`clefOctave`). Syllables alone vs stanza numbers: `lyrics "X"  stanza "Y"` (narrower wins).

Rules worth knowing before emitting one:
- "The whole document" is `serif` and `sans` bound together — but NOT `notation`. The
  `treble_8` octave digit, a compound time signature's `+` and tab fret numbers follow a face only
  when `notation` or the role itself is named.
- ⚠️ **The keyword is `fonts`, plural, and it takes a BLOCK.** There is no `font` keyword
  (that word is free — a part may be named it) and no one-line form: a bare value,
  `fonts "Georgia"`, is an error naming the block to write instead.
- A named face is MEASURED as well as drawn (since 2026-08-18): the space reserved for a
  string comes from the same file it is drawn in. ⚠️ On a machine that does not have the
  face the reservation falls back to the bundled TeX Gyre face, so naming one makes the
  page machine-dependent — LilyPond's `font-name` has the same exposure. A missing face
  warns rather than passing quietly.
- `embedded` only subsets the named faces into an exported PDF; it changes nothing about
  measuring or drawing.
- ⚠️ **`chord serif` (a bare family word after a key) is refused** — write
  `chord as serif`. A bare word after a key is the next key.
- Size and style reach these roles: `title subtitle composer poet instrument stanza chord
  diagram tempo mark pedal navigation text dynamics partCombineText barNumbers tuplet volta
  ottava bend tabTechnique clefOctave tab time` (and the syllables, through `lyrics`);
  `finger` and `figuredBass` (Emmentaler digits) take a size and no style (a style on them
  warns, LYS8018). `tab step` moves the digit and what is measured from it, not the string
  spacing; `time` is only the compound numerator's `+` (the digits are Emmentaler glyphs).
  A generic family (`serif`/`sans`) takes faces only, so a notation role's size or style is
  always named out loud (`tab …` or `notation …`).
- `mono` is not a key. Unknown keys are an error; a key bound twice is a warning (last wins).
- **Named blocks, per score**: `fonts NAME { … }` at the top level declares a reusable
  block (it binds nothing by itself); a score references it as `fonts NAME`, or overrides
  part of it with `fonts NAME { lyrics "…" }`. The reference REPLACES the file's
  unnamed default; the override block reads as if written at the end of the named block
  (same key → the override wins, no cross-block duplicate warning). ⚠️ Narrower still
  wins across blocks: a house block's `stanza` (role) beats a score's `lyrics`
  (group) override — override a role with the same or a narrower key. An unknown
  reference name is an error; an unreferenced named block warns.

## Paper (`paper { … }`)

The page's dimensions — paper size, margins, indents, vertical spacing. One per file;
every default equals LilyPond's a4 default, so an absent block (or one that states the
defaults) changes nothing.

```
paper {
  size b5                  // a whole page by name: width, height AND scaled margins
                           // (bare; quote only a name with a space: size "ansi a")
  paperWidth 210mm         // bare numbers are staff spaces; a unit is GLUED (210mm, 29.7cm, 8.5in)
  paperHeight 297mm        // 0 = one content-driven page
  leftMargin 15mm  rightMargin 15mm  topMargin 10mm  bottomMargin 10mm
  indent 15mm  shortIndent 0
  raggedRight              // bare flag: do not justify lines
  raggedBottom             // bare flag: do not justify pages (default: only the last page is ragged)
  spacingIncrement 1.2     // horizontal note-spacing unit (staff spaces)
  systemSystemSpacing { basicDistance 12  minimumDistance 8  padding 1  stretchability 60 }
  staffStaffSpacing   { basicDistance 9 }   // staves of a group
}
```

Rules worth knowing before emitting one:
- Scalar keys: `paperWidth paperHeight leftMargin rightMargin topMargin bottomMargin
  indent shortIndent spacingIncrement`. Flags: `raggedRight raggedBottom`. Spacing
  blocks: `systemSystemSpacing scoreSystemSpacing markupSystemSpacing scoreMarkupSpacing
  markupMarkupSpacing topSystemSpacing lastBottomSpacing staffStaffSpacing
  staffGroupStaffSpacing defaultStaffStaffSpacing nonStaffRelatedStaffSpacing
  nonStaffUnrelatedStaffSpacing nonStaffNonStaffSpacing`, each taking `basicDistance /
  minimumDistance / padding / stretchability` lines.
- `size NAME` (bare) sets width, height and the four margins (LilyPond's
  set-paper-size scaling: margin defaults × the size's ratio to a4, rounded to whole
  mm — `size a4` is the identity). Names: LilyPond's paper table (`a0`..`a10`,
  `b0`..`b10`, `c0`..`c10`, `letter`, `legal`, `tabloid`, …) plus Lily#-own `jisb5`
  (182×257mm, the Japanese B5 — ISO `b5` is 176×250). Lowercase only (`size A4` is an
  error; units too: `210mm`, not `210MM`). Quote only a name that carries
  a space (`size "ansi a"`). It reads at its position: a later `topMargin` refines
  it, a later `size` overrides earlier keys. Prefer writing `size` first.
- ⚠️ A unit is glued to its number: `210 mm` (spaced) is an error naming the glued
  spelling. `stretchability` is unitless.
- ⚠️ The staff-spacing family lives HERE, not in `override` (applied score-wide in one
  pass). There is no staff-size key and no algorithm switch.
- Unknown keys are an error; a key set twice is a warning (last wins).
- **Named blocks, per score** — same shape as fonts: `paper wide { paperWidth 250mm }`
  at the top level, then `score main { paper wide staff melody }`, or
  `paper wide { topMargin 12mm }` inside the score to override part of it. The
  reference replaces the file's unnamed default; a spacing block's unwritten lines
  keep the named block's values.

## Layout (`layout { … }`)

The score-wide DISPLAY SWITCHES — closed vocabularies that pick one of a few drawings,
with no unit and no grob scope. The third block of the `fonts` / `paper` shape: one
unnamed block per file is the default, `layout NAME { … }` is a per-score declaration a
score references as `layout NAME` (or overrides in part with `layout NAME { … }`).

```
layout {
  markTempo beside             // section label + tempo at the same bar: stacked (default) | beside
  barNumbers every 4       // which bars carry a number: lines (default) | none | every N
  accidentals modern       // which notes carry one: default (d.) | modern | modernCautionary
                           //                        | forget | noReset
  sectionLabels plain      // section names: boxed (default) | plain | none
  partCombineText false    // a2 / Solo words: true (default) | false
  chordQualities words     // chord quality: symbols (default) | words
  minorChords lower        // a minor chord's root: upper (default) | lower
  chordDiagrams ukulele    // the tuning written chord shapes draw on: a tuning word | none (unset: the part's instrument, else guitar)
                           // `chordDiagrams ukulele all` / `chordDiagrams all`: every chord draws a diagram
                           // `chordDiagrams guitar { Cm7 x35343  G  section B { C x35553 } }`: the listed chords draw
                           // `chordDiagrams guitar capo 3`: a capo — pressed shapes, pressed names, "Capo 3" at the head
  chordNames both          // under a capo a name shows: shape (default, the pressed chord's) | sounding | both "E♭ (C)"
  chordList true           // the chords the score uses, each with its diagram, under the title: true | false (default)
  voltaBracket line        // how far an ending's bracket reaches: all (default) | line | N bars
}
```

- `markTempo stacked` is LilyPond's: the boxed label over the tempo, each on its own anchor.
  `markTempo beside` is the chart's one line, "[Chorus] ♩ = 132" — the label's box at the
  line-start edge, the tempo to its right on the label's baseline. Lily#-own; the `.ly`
  twin has no spelling for it and warns. A mid-line label and a mid-measure `tempo` are
  unmoved either way.
- `barNumbers lines` is LilyPond's default: the first bar of every line after the first.
  `none` prints no numbers (`\remove Bar_number_engraver`). `every N` prints every bar
  whose number is a multiple of N wherever it stands, and ONLY those — a line opening on
  bar 3 under `every 2` opens with no number, as LilyPond's `every-nth-bar-number-visible`
  prints it. N is a whole number of at least 1. The twin writes the same LilyPond words.
- `accidentals default` is the 18th-century style Lily# has always drawn: an alteration
  holds to the bar line, in its own octave. `modern` is Kurt Stone's — cancelled in other
  octaves and in the next measure too, and with no restore-natural. `modernCautionary`
  prints the accidentals `modern` ADDS in parentheses. `forget` remembers nothing (every
  note is read against the key signature). `noReset` never forgets. LilyPond's
  `\accidentalStyle` table, for the styles whose context is the staff; its voice / piano /
  choral families name a context a score-wide switch cannot, and are not offered. The twin
  writes `\accidentalStyle modern` at the head of each part's music.
- `sectionLabels boxed` is the frame Lily# has always drawn (Lily#-own: LilyPond's
  SectionLabel grob draws the bare string). `plain` drops the frame and engraves the name
  alone, which is LilyPond's own picture, and the twin writes `\mark \markup` with no
  `\box`. `none` engraves no section names at all — the part sheet's answer — and the twin
  writes no `\mark` either. The form still plays the section; MIDI and MusicXML are
  untouched.
- `partCombineText true` prints `a2` / `Solo` / `Solo II` on a `combinedStaff` (LilyPond's
  default); `false` is its `printPartCombineTexts = ##f`, which the twin writes. The merging
  itself is unchanged — this is the words, not the combining.
- `chordQualities symbols` is the default and LilyPond's own picture: the four qualities its
  exception table names print `C°`, `C+`, `Cø`, `C°7`, and a major seventh prints its drawn
  triangle. `words` spells them out instead — `Cdim`, `Caug`, `Cm7♭5`, `Cdim7`, `Cmaj7` — the
  lead-sheet convention. Every other quality is the same word either way.
- `minorChords upper` is LilyPond's default: `Am`, `Am7`. `lower` lowercases the root of a
  chord with a MINOR THIRD and drops its `m` — `a`, `a7` — which is LilyPond's
  `chordNameLowercaseMinor`, written by the twin. The slash BASS keeps its capital
  (`a7/C`), as LilyPond's does, and `Caug` (a major third) never lowercases. With both keys
  set Lily# spells these chords exactly as LilyPond prints them. `maj7` is NOT switchable:
  LilyPond draws it as a raised triangle, which Lily#'s one-line chord name has no home for.
- Neither chord key reaches MIDI or MusicXML — a `<harmony>` carries the chord as data.
- `chordDiagrams ukulele` (or `guitar`, `mandolin`, `guitardropd`, … — the words a tab's
  `tuning` takes) is the tuning the score's chord diagrams draw on. A diagram draws only where
  a chord writes its shape; this picks which written shape applies. Unset, a diagram takes the
  part's fretted instrument (an `@chord`'s part, the staff a row stands above), else the
  guitar. `none` draws no diagram at all, even where a shape is written — the same source
  makes a piano score and a guitar score. `all` after the tuning word (`chordDiagrams guitar
  all`), or alone (`chordDiagrams all`, the tuning as when unset), makes EVERY chord name draw:
  its written shape, else the usual one (LilyPond's predefined, else Lily#'s first); a chord
  with no shape on the tuning warns once per symbol and tuning. `none all`, `all guitar` (the
  tuning comes first) and a repeated word are errors. A shape TABLE in braces may follow the
  words — `chordDiagrams guitar { Cm7 x35343  G  section Chorus { C x35553 } }` — listing the
  chords that draw wherever they are named: each entry a chord symbol and the shape(s) a row
  writes after its symbol, a name alone the usual shape; a `section NAME { … }` block's entries
  apply to the chords written in that section, the rest to the whole score. Strongest first:
  the shape written at the chord, the section's entry, the song's, then (under `all`) the usual
  shape; an entry whose shapes fit no tuning of the score is not used there. `none` takes no
  table. A bad symbol or shape in the table and a chord listed twice (the last wins) are
  warnings, the rest stands; a section nothing declares warns. The shape rules are under
  Annotations (*Chord diagrams*). The twin writes a `FretBoards` context under a row's
  `ChordNames` when an entry draws (written or listed; under `all`, for every row); MusicXML
  nests an `@chord`'s `<frame>` in its `<harmony>`. `capo N` after the tuning word (before
  `all`; 1–11) puts a capo on fret N: the music still writes the SOUNDING chords (`Eb`), every
  shape is the shape pressed above the capo (`Eb(x32010)` is the C shape, checked against the
  pressed chord), the printed name is the pressed chord's (`C`), spelled in the key N
  semitones below the key at the bar, "Capo N" stands at the score's head, and a
  `chord(Eb x32010)` item sounds N semitones higher. The twin writes the pressed chords into
  `\chordmode` and `instrument = "Capo N"`; MusicXML's `<harmony>` stays sounding. After
  `capo ` the completion ranks the frets 0–7 by their barre chords (fewest first); hover `capo`
  for the same ranking; there is no `capo auto`.
- `chordNames shape` (the default) names the pressed chord under a capo, `sounding` the
  sounding chord, `both` both — `E♭m7 (Cm7)`. Without a capo all three print the same name.
  The twin cannot spell `both` and warns.
- `chordList true` puts every chord the score names at its head under the title, each once in
  order of first appearance with the diagram it draws (its usual shape when it draws none), in
  centred rows of even counts; `chordDiagrams none` lists the names alone. The twin writes the
  rows as `\markup` lines of `\center-column { "NAME" \fret-diagram-terse … }` before the score.
- `voltaBracket all` (default) draws an ending's bracket over every bar; `line` stops it at
  the end of the system it starts in; `N` covers the ending's first N bars (all of it when
  shorter). An ending overrides it with `[1. B C]@voltaBracket(2)`. A cut bracket ends
  straight. The twin writes `VoltaBracket.musical-length` / kills the later pieces; MusicXML
  stops the `<ending>` at the Nth bar as `discontinue` (`line` writes the whole ending there).
- ⚠️ **The keyword is `layout` and it takes a BLOCK.** A bare `markTempo beside` or
  `barNumbers none` at the top level is an error (the words are not directives); write
  `layout { markTempo beside }`. The keys and their words are not reserved (`part markTempo { }`
  compiles). `markTempo` is the layout key; the font GROUP for the same marks is `marks`
  (`fonts { marks "Georgia" }`).
- The line against `paper`: a quantity with a unit (a length, `raggedRight`) is the
  page's and stays in `paper`; a switch among drawings lives here. Neither is an
  `override` (which reads a `once` / section scope a whole-score switch would ignore).
- Keys are case-sensitive (a paper key's rule; a wrong-case key is refused with its
  right spelling); the value words are canonical case only. Unknown keys are an error; a key set twice warns (last wins); a
  brace inside the block is refused (no layout key opens a block).

- Comments: `// line` and `/* block */`.
- `@name` is the canonical annotation prefix. `\name` annotations are rejected (use `@`);
  backslash is reserved for tablature only (`\3` string numbers, `\tuning`). Lily# is NOT
  LilyPond — do not emit LilyPond-only constructs (`\repeat volta`, `\relative`, `\new
  Staff`, `\version`, `<< ... \\ ... >>`, etc.).
- **Write a note's marks in this order** (the parser accepts any order; this is the house
  style the editor writes, so files stay searchable): string number, `@` annotations, `]`,
  `)`, `(`, `[`, `~` — from the note outward by how much music each spans, what ends on
  the note before what begins on it, brackets nested (slur outside, beam inside), tie last.
  `a,4\4~`, `c8\3@accent([ d e f])`, `d4)( e`, `c4)~ c`.

## Reserved words

These are keywords. A PART or SECTION name may be any of them EXCEPT the structural ones
(`voice lyrics chords section part phrase form score tab staff ossia grandStaff staffGroup
choirStaff condensedStaff combinedStaff layout paper fonts grace acciaccatura appoggiatura
cue key time tempo partial override revert once using title subtitle composer poet segno
fine coda dc ds al to break noBreak pageBreak noPageBreak`) — so
`part p`, `part bass`, `part q` are fine (2026-10-03). A PHRASE name is referenced bare in
music, so it is an identifier, a clef word (`treble bass alto tenor`) or a dynamic word
(`ppp pp p mp mf ff fff`; `f` is the note F, so not `f`); any other keyword is LYS1030.
Keywords:

```text
section form using tab ossia transpose octave pitch instrument percussion drummap
score part staff grandStaff staffGroup choirStaff condensedStaff combinedStaff
voice phrase repeat break noBreak pageBreak noPageBreak partial cue embedded fonts paper layout
title subtitle composer poet tempo time key clef
major minor ionian dorian phrygian lydian mixolydian aeolian locrian
treble bass alto tenor treble_8 bass_8 soprano mezzosoprano baritone
tuplet grace acciaccatura appoggiatura lyrics chords tuning
override revert once
segno fine coda dc ds al to
ppp pp p mp mf ff fff   (f is a PITCH; @f still works - dynamics resolve from text)
```

Also special: single letters `a`-`g` are pitches; `r`/`R`/`s` are rests. Reserved IN
MUSIC only: `q`, `chord` (`chord(C x32010)`) and the drum names (`bd` `sn` `hh` …) — a
phrase cannot be named them; a part can. In a score, `tab X Y` is part X's tab plus the
MIDI-only part Y when the file declares a part named X, else the X tuning over part Y
(`tab bass click` beside `part bass`: the part wins, LYS1044 names `tab bass4 click` for
the tuning). Articulation,
ornament, dynamic-text and mark NAMES (`staccato`, `tr`, `mordent`, `cresc`, `dim`, …) are
NOT reserved — they are resolved from the `@name` text — so they remain free for your own
identifiers.
</content>
