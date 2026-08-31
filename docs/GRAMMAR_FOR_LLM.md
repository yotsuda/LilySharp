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
                        // (a pickup is 'partial', but it belongs to a SECTION — see below)
fonts {                  // optional; binds text faces. The two generic families together
  serif "Georgia"       // are "the whole document's text"; bind roles separately below
  sans  "Georgia"       // 'embedded' subsets every named face into the PDF
}
paper {                  // optional; page dimensions (defaults = LilyPond's a4)
  paperWidth 210mm      // bare numbers are staff spaces; units mm/cm/in GLUED (210mm)
  paperHeight 297mm     // see the paper section below for margins/indents/spacing
}

part rightHand { clef treble }  // declare each part; clef lives here
part leftHand  { clef bass }    // part names are identifiers, NOT reserved words
                                // (bass/treble/melody-as-keyword etc. are taken)

phrase motif { c4 d e f | }     // optional reusable music, referenced by bare name

section Main {                  // a section binds music to each part by name
  partial 8                     // optional pickup: shortens THIS section's opening bar
                                // for every part at once (top level rejects it)
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
printed.

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

**A score's own header, and parts that only play** — `title` / `composer` written inside a
score restate the file's metadata for that score alone, and a **bare part name** renders
that part to MIDI only (played, never engraved — a click track, a cue part):

```
score winds "winds" {
  staffGroup { staff flute  staff oboe }
  title "Woodwinds"    // this score only
  click                // played, never engraved
}
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

**Music always lives inside a part.** A file is a set of declarations; a note stream at the
top level is an error (LYS0020), as are a top-level `{ … }` block, `grace`/`tuplet` group,
`break`, or phrase reference. This is what makes a top-level `clef`/`key`/`time`/`tempo`
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
R1          // full-measure rest
a,4@rest    // quarter rest printed where the note a, would sit (a PITCHED rest)
<c e g>4    // chord (shared duration after '>'), C major triad as a quarter
<c 3 5>4    // the same triad by scale degrees (root + 3rd + 5th of the key)
<1 3 5>2    // degrees only: anchored on the key TONIC (C E G in C major)
```

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
combine with `staff comp as lines 1` in the score for a one-line rhythm staff. `time 4/4`,
`tuplet 3/2` and `c/g` keep their own `/`.

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
- Chord names: `c4@chord(C)` , `d4@chord(Dm)` , `e4@chord(Am7)` — the SYMBOL as it
  prints (`F#m`, `Bb7/D`, `Gm7-5`), the same format as a chords row. The retired
  lowercase `:` entry (`@chord(a:m)`) is not recognised (LYS1008 warns, no symbol is
  engraved). A bare `@chord` derives it from the notes.
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
- **A written `|` closes exactly one measure, and a measure with nothing in it is an
  empty one** — so `{ | | | | }` is four empty bars and `{ | c1 }` is an empty bar then
  `c1`. An empty bar is filled with a full-measure spacer (`| |` == `| s1 |`, on the page
  and in playback), so it is never diagnosed. Three barlines close nothing: `||`/`|.`/`:|`
  on an empty span DECORATE the bar behind them; `|:` OPENS the bar in front of it (so
  `{ |: c1 :| }` is one bar, while `{ | |: c1 :| }` is two); and a `|` landing where the
  meter just auto-filled a bar merely confirms it — which is why a trailing `c1 |` is one
  bar, not two.
- Volta repeats are symbolic; endings are inline `[1. ... ]` `[2. ... ]`. Play count
  defaults to the highest ending number; set it with `*N`. The opening `[` is required
  (a bare `1. ...` ending is rejected); the closing `]` is optional — write it to draw
  the right cap (closed ending), omit it to leave the ending open. Section-level endings
  in a `form main { }` repeat use the same `[N. Section]` form.
- An ending needs a repeat to be an ending OF. Write `form main { [1. A] }` and no bracket
  is drawn: it engraves as the plain reference `A`, and LYS6008 warns that the `1.` prints
  nothing. Put the ending inside the repeat — `form main { |: A [1. B] :| [2. C] }`.

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

A lyric track binds to its melody at the definition: `lyrics NAME sings PART`.
The score row may state (or repeat) the same binding: `lyrics NAME sings PART`
among the score items - one property of the track name, spelled at either site.
The score places its row by ORDER (score = a vertical stack of bands): a
`lyrics NAME` row directly below the staff engraving PART is that staff's verse
(a run of rows stacks as verses); anywhere else it shows only the words, at the
melody's rhythm, without engraving the melody. Multiple tracks may sing one part
(two languages = two names). A track named after the part or one of its voices
is bound by the name. An unbound row is the even-spread lead-sheet row; inside a
staff group a row must sing the staff directly above it (LYS6012).

A `lyrics NAME { … }` track sits in a section next to the part it sings; the score
places it with a `lyrics NAME` row under the staff. Syllables are separated by
spaces; `-` joins syllables of one word; `|` mirrors the music's barlines. Barlines
follow the music rule: every written `|` closes one bar, the one that OPENS the run
included, so `| きら | ひかる |` is one bar longer than `きら | ひかる` — that leading
`|` is how a verse skips the rest bar the melody opens with.

```
part melody
section Main {
  melody { c4 d e f | g2 g | }
  lyrics words sings melody { Hap- py birth- day | to you | }
}
form main { Main }
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
`r`/`R` print "N.C." in their slot, `s` prints nothing. Barlines in the source
(`|` `|:` `:|` `||` `|.`) are drawn, and follow the same bare-barline rule as music
and lyrics: every written `|` closes exactly one bar, the one that OPENS the run
included, so `| C | F |` is an empty bar and then two.

```
section Main {
  chords prog  { C G7 | Am F | C :| }     // two halves | two halves | whole (repeat)
  lyrics words { Twin- kle | lit- tle | star | }
}
form main { Main }
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
form main { A segno  B to coda  C ds al coda  coda D }
```

The same bare words are also written in a section's music, at a barline boundary
(`segno c4 d e f |`, `c4 d e f | ds al fine`) — they are landmarks, never note
modifiers, so `c4@segno` is an error (LYS1022) and mid-measure warns (LYS4003).

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
  and `transpose` is a part property (chromatic), so a motif quoted at another interval
  is written out.
- **`section ~A { … }` flips that section's label default.** A section prints a rehearsal
  letter by default and a reference's `~` hides it; declare it `section ~A` and it prints
  none by default, so there `~A` is the spelling that SHOWS. One meaning for the tilde at
  both sites — "the other one than the default" — and the rule is one equality:
  `shown = (declaration hides) == (reference has ~)`. Write it on a section cut only to
  carry a repeat edge. An empty label `""` still suppresses either way, and a label on a
  play that prints none is LYS0012.
- **A SECTION reference takes the same marks**: `form main { ~A ~B' }` opens B's play an
  octave up, `~B,` an octave down, `~B''` two. They belong to the PLAY, so one section can
  be quoted at two octaves (`~B ~B'`) while the declaration never moves, and the next
  reference is back at the part's anchor. Both spellings take them (`B'` and `~B'` — the
  tilde hides only the label), a volta ending takes them (`[1. B']`), and they work in
  `octave absolute` too. This is how a section cut only to carry a repeat says its music
  belongs an octave away: the boundary's frame reset stays, and the carry is written down.
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
## Text fonts (`fonts { … }`)

A face per kind of text. Keys are a generic family, a group, or a single role; the
NARROWER spelling wins in either source order.

```
fonts {
  serif     "Georgia"                         // everything serif unless overridden
  sans      "Verdana"                         // chord symbols are the one sans role
  lyricText "Charis SIL" "Noto Serif CJK JP"  // several names = a fallback chain
  title     "Cormorant"                       // one role
  marks     "Georgia"                         // a whole group
  tempo     "Playfair Display"                // ...beats the group above
  chordName serif                             // point a role at the other bundled family
  embedded                                    // subset the named faces into the PDF
}
```

Groups → roles: `header` → `title composer instrument` · `lyrics` → `lyricText stanza` ·
`chords` → `chordName fretFrame figuredBass` · `marks` → `tempo mark pedal navigation text
dynamics partCombine` · `numbers` → `barNumber fingering tuplet volta ottava bend
tabTechnique` · `notation` → `clefOctave meter tabFret`.

Rules worth knowing before emitting one:
- "The whole document" is `serif` and `sans` bound together — but NOT `notation`. The
  `treble_8` octave digit, a compound meter's `+` and tab fret numbers follow a face only
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
- No weight/slant/size here — those belong to the engraving.
- `mono` is not a key. Unknown keys are an error; a key bound twice is a warning (last wins).
- **Named blocks, per score**: `fonts NAME { … }` at the top level declares a reusable
  block (it binds nothing by itself); a score references it as `fonts NAME`, or overrides
  part of it with `fonts NAME { lyricText "…" }`. The reference REPLACES the file's
  unnamed default; the override block reads as if written at the end of the named block
  (same key → the override wins, no cross-block duplicate warning). ⚠️ Narrower still
  wins across blocks: a house block's `lyricText` (role) beats a score's `lyrics`
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
  spacingIncrement 1.2     // horizontal note-spacing unit (staff spaces)
  systemSystemSpacing { basicDistance 12  minimumDistance 8  padding 1  stretchability 60 }
  staffStaffSpacing   { basicDistance 9 }   // staves of a group
}
```

Rules worth knowing before emitting one:
- Scalar keys: `paperWidth paperHeight leftMargin rightMargin topMargin bottomMargin
  indent shortIndent topSystemPadding spacingIncrement`. Flag: `raggedRight`. Spacing
  blocks: `systemSystemSpacing scoreSystemSpacing markupSystemSpacing scoreMarkupSpacing
  markupMarkupSpacing topSystemSpacing lastBottomSpacing staffStaffSpacing
  staffGroupStaffSpacing defaultStaffStaffSpacing nonStaffRelatedStaffSpacing
  nonStaffUnrelatedStaffSpacing nonStaffNonStaffSpacing`, each taking `basicDistance /
  minimumDistance / padding / stretchability` lines.
- `size NAME` (bare) sets width, height and the four margins (LilyPond's
  set-paper-size scaling: margin defaults × the size's ratio to a4, rounded to whole
  mm — `size a4` is the identity). Names: LilyPond's paper table (`a0`..`a10`,
  `b0`..`b10`, `c0`..`c10`, `letter`, `legal`, `tabloid`, …) plus Lily#-own `jisb5`
  (182×257mm, the Japanese B5 — ISO `b5` is 176×250). Quote only a name that carries
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
section form using tab ossia transpose octave instrument percussion drummap
score part staff grandStaff staffGroup choirStaff condensedStaff combinedStaff
voice phrase repeat volta alternative break nobreak partial cue embedded fonts paper
title composer tempo time key clef
major minor ionian dorian phrygian lydian mixolydian aeolian locrian
treble bass alto tenor treble_8 bass_8 soprano mezzosoprano baritone
tuplet grace acciaccatura appoggiatura lyrics chords tuning
override revert once
segno fine coda dc ds al to tocoda
ppp pp p mp mf ff fff   (f is a PITCH; @f still works - dynamics resolve from text)
```

Also special: single letters `a`-`g` are pitches; `r`/`R`/`s` are rests. Articulation,
ornament, dynamic-text and mark NAMES (`staccato`, `tr`, `mordent`, `cresc`, `dim`, …) are
NOT reserved — they are resolved from the `@name` text — so they remain free for your own
identifiers.
</content>
