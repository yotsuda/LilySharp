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
                        // WHO SINGS WHICH SECTIONS, AND A HARMONY: after the part a singer line may say
                        //   singer "a/b" on lead sings words in Verse Bridge     — only in those sections
                        //   singer "c/d" on lead sings words up 3 in Chorus      — a third above, in the key, in the Chorus
                        //   singer male on lead sings words octave -1            — an octave below
                        // (order: the singer, `on PART`, `sings WORDS` only where the words must be named, then
                        //  up/down N, octave N, in SECTIONS in any order, each once, then `with` and the voice's settings).
                        // Several singer lines on one part sing together. Do not write a harmony part
                        // by hand when it is a third or a sixth along the key: write `up 3` / `down 6`.
                        // TO PRINT WHAT A SINGER SINGS, give the singer a name and name it in the score:
                        //   singer carol female on lead sings words up 3 in Chorus
                        //   score { staff lead  lyrics words  staff carol }                — carol's notes under the lead (the words are the lead's)
                        //   score carol { staff carol  lyrics carol }                      — carol's own sheet: her notes and her words
                        // (`staff SINGER` is the staff of what that singer sings; `lyrics SINGER` the words she sings.
                        //  A score prints what it says: either one, or both. Lines of one name are one singer; the
                        //  voice is written on one of them. Do not give a singer a part's name.)
                        // A PART WITH WORDS IN TWO LANGUAGES (`lyrics en  lyrics jp`): a singer line that names no
                        // words sings the ones in the file's language; `singer alice female japanese on lead`
                        // sings the Japanese ones. (The language is a bare word before the voice, as the gender is.)
                        // A TEXT FOR THE PAGE, written as Markdown in a fence at the top level: the
                        // lines between ```md and ``` are printed under the title (paragraphs, **bold**,
                        // *italic*, `# heading` and `## heading`, `- bullet` and `1. numbered` lists,
                        // nested lists; NOT drawn, and warned about (LYS4058): tables, code blocks, block
                        // quotes, rules, images, HTML); ```md end prints them under the last system. Nothing
                        // between the fences is Lily#. Inside score { } a fence replaces the file's for that score.
                        // The opening line may go on, in any order, each word once: `box` (a ruled frame
                        // around the text), `center` (each line centred), `xMargin 20mm` (white at both
                        // sides, inside the line width) or `xMargin 20mm 10mm` (left, right), `yMargin 2`
                        // (white above and below, in staff spaces unless a unit is glued on; default 1,
                        // `yMargin 0` is LilyPond's own spacing) or `yMargin 3 1` (above, below) — e.g.
                        // ```md end xMargin 30mm box. A length is 20mm, 2cm, 0.5in or bare staff spaces.
                        // Two fences for one place stack, each with its own shape. The whole of it,
                        // with examples: the section "A text for the page" below.
tempo 120               // optional; also: tempo "a tempo" (words alone: printed, the speed unchanged — for "a tempo", "Tempo I", "Meno mosso"), tempo "Allegro" 120, tempo "Andante" 4 = 96 (text + beat unit), tempo "Lively" 4. = 116 (dotted unit), tempo Comodo 4 = 84 (a bare word is the marking); 'tempo 120 swing' adds a shuffle-feel equation, and plays it from there on ('swing 16' = 16th swing; only 8 or 16; 'tempo swing' = the equation alone); a last word 'bare' PLAYS the count and does NOT PRINT it: tempo "Tempo I" 4 = 92 bare sounds at 92 and prints only the words — for a "Tempo I", "Più mosso", "a tempo" that must change the speed without a metronome mark (anywhere a tempo goes; it needs both words and a count, LYS0044 otherwise)
time 4/4                // optional (default 4/4); 4/4 engraves as the C
                        // An ADDITIVE numerator groups the beats: 'time 3+2+2/8' prints 3+2+2 over 8
                        // and beams the eighths 3+2+2 (a plain 'time 7/8' beams nothing, as LilyPond);
                        // 'time 7/8 (2+2+3)' prints 7/8 and beams 2+2+3 (the terms sum to the numerator)
                        // (common time) glyph and 2/2 as cut-C, like LilyPond.
                        // 'time none' = senza misura (LilyPond \cadenzaOn): until the next
                        // 'time N/M' a measure ends only at a written '|' (drawn, breakable),
                        // no meter is drawn, no automatic beams from a span opened at a bar
                        // line (write c8[ d e f]; a beam already building when 'time none'
                        // arrives mid-bar runs on), no length checks, a 'partial' inside it
                        // does nothing (LYS2015), and the bar number does not advance across it.
                        // A part SILENT under it writes an empty bar — '|' alone, which is as
                        // long as the other parts' bar and draws nothing — or leaves the section
                        // out, or writes rests that add up to the others' bar ('r1 r1 |'; a
                        // shorter one is LYS2004). A bare 'R' has no bar to last there (LYS2016):
                        // write its duration, 'R1 R1 |', adding up the same way
key c major             // optional (default c major); all church modes work:
                        // major minor ionian dorian phrygian lydian mixolydian aeolian locrian
                        // (key d dorian = no accidentals, key e dorian = 2 sharps)
                        // A note's NAME IS ITS PITCH: the key never changes it. In
                        // 'key c minor', 'b' is B natural (drawn with its natural sign) and
                        // B-flat is 'bes'; a minor key's raised leading tone is 'b', never 'ces'
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

part rightHand { instrument piano-right }  // declare each part: an instrument preset brings its
part leftHand  { instrument piano-left }   // clef, sound and range (see "Rules": name the
                                           // instrument, and no clef beside it)
                                // a part name is any bare word except the structural
                                // keywords (voice staff section key time … — see below)

phrase motif { c4 d e f | }     // optional reusable music, referenced by bare name

section Main {                  // a section binds music to each part by name
                                // SAME NAME = SAME TIME: every part, chord row and lyrics
                                // track under one name plays over the same bars — write them
                                // the same length (LYS2007 names each length when they differ)
  partial 8                     // optional pickup: shortens THIS section's opening bar
                                // for every part at once — the ONLY place for it (the top
                                // level, a part header and a part's first bar reject it).
                                // Only the piece's first section opens on one, or a section
                                // after a bar left that much short: after a whole bar it is a
                                // short bar mid-piece (LYS2022) — write the upbeat in the bar before.
                                // Later in a part's music, `| partial 2. r2. |` makes THAT
                                // bar 3 beats long — written in every part sharing the bar
  rightHand { g8 | motif g2 g | }
  leftHand  { r8 | c2 c | g2 g | }
}

form { Main }              // playback/print order of sections

score {                       // one or more render blocks; the unnamed one is the default
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

This is plain condensation: a unison keeps both stems (one head, a stem up and a stem down),
every part's rests are drawn (two bar rests in a bar both rest — `combinedStaff` merges them)
and no "a2"/"Solo" is printed. The parts are numbered as parts: the first has stems up and the
second down for the whole piece, and a `voice { } { }` inside one part is voiced up/down
again only for as long as it lasts. One staff takes one key, meter and clef at a moment: a `time` both parts write is
drawn once, and a later part's `key` / `time` / `clef` that differs from the first part's at
the same moment is not applied and warns (LYS4024) — write the same change in every part.
The staff's clef is the first part's unless one is written before the brace:
`condensedStaff bass { viola cello }` (also `combinedStaff bass { … }`). Every part is drawn
under that clef, at its own pitches. Dynamics the parts share at a moment are printed once;
where two parts differ (`@mf` in one over `@p` in the other, a crescendo against a diminuendo)
the upper part's level and hairpin stand ABOVE the staff and the lower part's below. A side
you write (`@p.up`) is kept.

**Several staves as ONE GROUP** — `grandStaff`, `staffGroup` and `choirStaff` take `staff`
items (and the `lyrics` rows under them); a `staffGroup` or `choirStaff` may also hold a group
one level inside it, `staffGroup { grandStaff { staff vn1  staff vn2 }  staff vc }` (a bracket
around a brace). A group of ONE staff is that staff, with no bracket. They differ only in the
left edge:

| | left edge | bar lines between staves | pick it for |
|---|---|---|---|
| `grandStaff` | brace | drawn through | one instrument on two staves (piano, harp) |
| `staffGroup` | bracket | drawn through | one family (the woodwinds) |
| `choirStaff` | bracket | **not** drawn through | independent lines (voices) |

```
score satb { choirStaff { staff sop  staff alt  staff ten  staff bas } }
score winds { staffGroup { staff flute  staff oboe  staff clarinet } }
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

**An alternative reading over a staff — `ossia`.** The alternative is a PART of its own
(any clef; no instrument needed) with music only in the bars that have one — every other bar
an empty `|`, so it is as many bars as the others. The score item `ossia NAME` is written
**right AFTER the staff it belongs to** and is drawn directly ABOVE that staff: a small staff
with no clef and no meter, present only over the bars where the part has notes.

```
part solo { instrument violin }
part alt  { clef treble }                       // the alternative reading
section A {
  solo { c''4 d e f | g4 a b c | c1 | }
  alt  { | g''8 a b c d2 | | }                  // bar 2 only: empty bars around it
}
form { ~A }
score { staff solo  ossia alt }                 // after its staff = drawn above it
```

- **The place is the staff BEFORE it** (the nearest `staff`, `tab` or staff group written
  before the item): `staff ob  staff alto  ossia orn` puts it over alto, between the two staves;
  `staff ob  ossia orn  staff alto` puts it over the oboe, at the top. A `lyrics` row between the
  staff and its ossia changes nothing (`staff alto  lyrics words  ossia orn`). Written before any
  staff it stands at the top of the system.
- Not inside a group's braces (LYS6011): for a staff in a `staffGroup` the ossia goes after the
  group and stands above the whole group.
- Silent bars are EMPTY bars (`|`). A bar of spacers, of `r` rests alone or a bar rest `R` is
  not drawn either (until 2026-11-13 an `R` drew a rest floating with no staff lines). Between
  two bars of notes the ossia is one piece, and a rest written there is drawn on its lines.
- The notes are read in the ossia part's own clef (`part alt { clef bass }`), or in the clef
  word the item writes (`ossia bass alt`, as `staff bass alt`). No clef is drawn either way.
- **An ossia is silent** in MIDI and audio (LILYSHARP-OWN, 2026-11-13): it is another reading
  of the bars under it, not a second player. To hear it, name the part as a bare row too
  (`staff solo  ossia alt  alt`) or give it a score of its own.

**A score's own header, and parts that only play** — `title` / `subtitle` / `composer` /
`poet` written inside a score restate the file's metadata for that score alone, and a **bare part name** renders
that part to MIDI only (played, never engraved — a click track, a cue part):

```
score winds {
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
score  { staff melody }                         // chart layout
score parts { layout lp  staff melody }              // this score keeps LilyPond's stacking
score study { layout lp { barNumbers none }  staff melody }
```

A staff's display name is a quoted string (`staff flute "Piccolo"`) — a bare word after
`staff NAME` is always another score item (`staff flute click` is flute's staff plus the
`click` MIDI-only part), so position never changes what a word means.

A part's own name and SHORT name: `part vn1 "Violin I" "Vn. I" { instrument violin }` — the long
name on the first system, the short one on every later system (LilyPond's shortInstrumentName).

A minimal single-staff document:

```
part melody { clef treble }
section Main { melody { c4 d e f | g2 g | } }
form { Main }
score { staff melody }
```

⚠️ That document prints a boxed **"Main"** over its first bar: every section reference prints
its name as a section label by default. A piece with one section rarely wants that — write
`form { ~Main }` (the `~` hides the label; see "Rules and gotchas"). A `@mark("A")` on a
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

**Who a mid-piece `key`, `time` and `tempo` reach, and for how long.** A `time` or a `tempo` is
every part's, wherever it is written. A `key` written IN THE MUSIC is that part's alone: for a
change every part makes, write it in each part at the same bar, or — where a section begins in
the new key — once in the section header (a key some parts of a section change and others do
not is warning LYS2023). All three go back to the file's at the next section: a section with no
`tempo` of its own plays at the file's top-level tempo, and the mark is printed where that
changes the tempo back (`form { A B A }` with `tempo 4 = 100` in B's header: the second A is at
the file's tempo, marked). With no top-level `tempo` there is none to go back to, and a tempo
holds until the next. So a tempo HELD over several sections is written in each one's header —
and a header tempo that states again the tempo already standing (the same words, count, unit,
swing and `bare` as the last tempo before it) **draws nothing** and changes nothing, like a
restated `key`: `section B { tempo "Più mosso" 4 = 108 … }  section B2 { tempo "Più mosso" 4 = 108 … }`
prints "Più mosso" once. `tempo! …` draws it anyway. Words alone (`tempo "a tempo"`) always print.

**A mark on a phrase call** goes on an empty chord before it, which takes no time:
`<>@mf theme`, `<>@p@text("dolce") theme` — the phrase's notes are the same at every call, so
`theme@mf` is an error that says this.

A key has at most seven sharps or flats: D♭ minor (eight flats, every A a B𝄫) is written
`key cis minor`, G♯ major `key aes major`, F♭ major `key e major` — the key that sounds the same
(LYS4034 warns otherwise). A hairpin goes the way its next level does: `@ff … @decresc … @fff`
warns (LYS4035) — a decrescendo leads to a softer level, a crescendo to a louder one.

A mid-music `time` is the **score's** meter, not just its part's: written at the start of a bar
in one part, it holds for every part to the end of the section, so the other parts need not
restate it — a part that writes nothing there is padded with bars of that meter. Two parts
writing different meters at the same bar warn (LYS2005; the first in the file stands).

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
- **Octaves are LilyPond's, in every part and for every instrument**: a bare `c` is C3 and
  `c'` is middle C (C4), `c''` C5, `c,` C2. An `instrument` never moves them.
- Relative (the default): each bare pitch takes the octave nearest the previous note (an
  interval of a fourth or less); `'`/`,` shift from there. The first note of a part, a section
  or a phrase is read as LilyPond's pitchless `\relative { … }` reads it — as if absolute:
  `c` = C3, `e'` = E4, `g''` = G5.
- `octave absolute` (top level, or in a part's music — `m { octave absolute c4 … }` — to the section's end;
  NOT inside `part X { }`, which takes only the base number) switches to ABSOLUTE
  mode, LilyPond's: every pitch says where it is — `c` = C3, `c'` = C4 (middle C), `c''` = C5,
  `c,` = C2 — independent per note, so octave mistakes never cascade. RECOMMENDED when
  generating scores. `part X { octave N }` moves a part's base (`octave 2`: a bare `c` is C2, for
  a bass line with fewer commas), in both modes; nothing else does. Sections restore the
  file-level mode.
- A part with an octave clef (`treble_8` — the guitar's, a tenor's) is written at the pitch it
  SOUNDS, as in LilyPond: the guitar's low E string is `e,` (E2), and the clef draws it where
  a treble staff has E3. A part that sounds an octave from its page WITHOUT the clef saying so
  (`instrument bass`, the double bass: plain bass clef) is written as the player READS it:
  the bass's low E string is `e,` (written E2), and it sounds E1 — unless the file is written
  at concert pitch (`pitch concert`, below): then every letter is what sounds, the bass's low E
  is `e,,`, and the page still shows it an octave up.
- `pitch` says which pitch the notes are in — what SOUNDS, or what the player of a
  transposing `instrument` (clarinet, trumpet, horn, alto-sax, …) READS — and its word says
  which notes: where notes are TYPED (the top level for the file, a part header for that part —
  own wins, as with `transpose`) it takes `written` (as the player reads them, the default;
  playback moves, a B♭ trumpet's `c` sounds B♭) or `concert`; where a page PRINTS (a score
  header, a staff's `as pitch`) it takes `transposed` (the player's key) or `concert`. Each
  place refuses the other's word with the fix. ⚠️ A score that writes none INHERITS: each part
  prints in its typed convention, so nothing moves. Under a top-level `pitch concert` the plain
  `score { … }` is the sounding (conductor's) score, and a player's part is
  `score trumpet pitch transposed { staff tp }` — an alto-sax `c'` prints there as `a'` in
  A major when the piece is in C, and still plays C. Octave-only instruments (bass, piccolo) are
  typed as they SOUND under `pitch concert` too (a bass's low E `e,,`, E1) and keep their printed
  octave on every page; without a transposing `instrument` the word changes nothing. A composed
  piece is best written at concert pitch: every part by the pitch it sounds.
- A chords track is SOUNDING pitch unless it says whose letters it is in: `chords tpc for tp
  { G6 | C7 }` is typed as the trumpet's notes are (under its `pitch`: the player's letters by
  default), and sounds `F6 Bb7`. With no `for` no `pitch` moves a track, and no staff it
  stands above does. A score ROW prints the reading of the part it is for — its own `for`, else
  the track's: `score trumpet pitch transposed { chords prog for tp  staff tp }` names concert
  `F6 Bb7` as `G6 C7`, spelled in the key the trumpet prints; the same row with no `for` prints
  `F6 Bb7`. `for` names a part (LYS7006); two blocks of a track naming different parts conflict
  (LYS7007) — a row's `for` never does.
- Numbers: `1`=whole, `2`=half, `4`=quarter, `8`, `16`, `32`, `64`, `128`.
- Dots after the number: `4.`=dotted quarter, `4..`=double-dotted.
- Omitting the duration reuses the previous note's duration.
- A duration standing ALONE repeats the previous note/chord/slash at the new
  length (LilyPond's isolated-duration reading): `bes8 8 8 8` is a bass pump.
  It also sets the running default. Rests are transparent; with nothing before
  it to repeat it is an error (LYS0016). A repeat reaching back ACROSS a
  barline warns (LYS1031) — that shape is also a dropped pitch letter
  (`4 g f e` meant as `a4 g f e`) — so open a measure with the event itself.
  A repeated slash (`/4 4 4 4 | 4 4 4 4 |`) never warns: it has no pitch to drop.

```
c4 d e f    // all quarters
c8 d e f    // all eighths
bes8 8 8 8  // bes eighths, written once
<c e g>4 4  // the chord again
```

**Bar lines are the bars.** Write `|` at the end of every bar. A bar with too much in it stays
one bar: what starts past its end is not drawn or played (LYS2002). Music that is two or more
whole bars with no `|` between them (within a quarter of a bar) is an ERROR (LYS2017) — it is
drawn and played in bars of the meter, every note kept, until the `|` is written; so is a
`time` written after music in a bar (write the `|` before it). A tuplet's notes add up to the
ratio's count of one value — `tuplet 3/2 { c8 d e }`, or `{ c4 d8 }` — so the group sounds a
note value; `tuplet 3/2 { g8 }` sounds 1/12 and is warned (LYS2020). `tuplet 3/2 bare { c8 d e }` draws the same notes with no number and no bracket. A `time` or `partial` is written before a tuplet, never in its body (LYS1065); a `voice { } { }` goes around tuplets, never inside one (LYS1066). A phrase's music is held to the
bar it plays in like any other. A repeat's turns and an `R1*N` still close their bars where
the beats fill.

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
<c 3 5>4 |  // the same triad by scale degrees (root + 3rd + 5th of the key)
<1 3 5>2    // degrees only: anchored on the key TONIC (C E G in C major)
q |         // THE PREVIOUS CHORD AGAIN, at the running duration
q2          // ...at its own duration; q@staccato takes its own post-events too
q' |        // ...an octave up (q,, two down) — the marks ACCUMULATE, see below
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

Chord octaves — the ANCHOR model (two rules: a chord is read as its FIRST note, and a
mark on a later member moves only that member): the anchor is where the first member
SOUNDS — its letter nearest to the previous note, then its own `'`/`,` (or the key tonic
for a degrees-only chord); the other members sit at-or-above it, so order is free except
the first slot (`<c e g>` = `<c g e>`; degrees are fully order-free). The first member's
mark moves the whole chord and propagates: `<c' e g>` = C4 E4 G4 and the next bare c is
C4 — the same as `<c e g>'`, a `'`/`,` AFTER the `>` (before the duration). A mark on a
later member moves THAT note from its stacked spot: `<c e g'>` = C3 E3 G4, `<c' e' g'>` =
C4 E5 G5 (a spread chord; the close chord an octave up is `<c' e g>`). The note AFTER a
chord is relative to the chord's ANCHOR, exactly as after a single note — a chord is ONE
item of the relative chain: `g1 <c e g>1 <f a c>1` = G3, C4 E4 G4, F4 A4 C5 (the f reads
the chord's c, not the g), and `<c, e g> <c, e g>` falls an octave, as `c, c,` does — write
the mark once. Degree chords hand on the TONIC, not the first degree written. (Until
2026-10-18 the anchor was the first member's BARE letter and its mark local — superseded.)

CHORDS IN ONE WORD (2026-10-31) — `chord` shows the NAME, `diagram` the PICTURE; with `@` it marks
the note it is on, without `@` it is an item that SOUNDS. Never two marks for one chord — but one:
`<f a c>1@chord@diagram`, the name and the fingering both read off the chord's notes.

| Written | Name | Diagram | Sounds |
|---|---|---|---|
| `c1@chord(F)` | ✓ | | |
| `c1@chord(F 133211)` | ✓ | ✓ | |
| `c1@chord(x32010)` | ✓ read off the shape (C) | ✓ | |
| `<f a c>1@chord` | ✓ read off the notes | | |
| `c1@diagram(133211)` | | ✓ | |
| `<f a c>1@diagram` | | ✓ the notes, fretted | |
| `<f a c>1@chord@diagram` | ✓ read off the notes | ✓ the notes, fretted | |
| `chord(F)4` | ✓ | | ✓ F A C, close |
| `chord(F 133211)4` | ✓ | ✓ | ✓ the shape's strings |
| `diagram(133211)4` / `diagram(F 133211)4` | | ✓ | ✓ the shape's strings |

```
chord(C x32013)1 |            // C3 E3 G3 C4 G4 on a guitar, the name C and its diagram
diagram(C x32013)1 |          // the same notes and diagram, no name (C is data: checked, stepped)
chord(G)2 chord(D)2 |         // no shape: G B D, D F# A, close, on every part (a guitar: G3 B3 D4)
diagram(C xx-10-12-13-12)4.~  // frets 10-15 in the dash form
```

- Item words are `@chord(…)`'s: a symbol + a shape, a shape alone, or a symbol alone. With no
  shape, `chord(F)` plays the chord's notes close (F A C), root by middle C as the staff draws
  it (in the instrument's range, holdable on a fretted part), no string numbers, and no diagram
  even in an `all` score — on every part, a guitar's too; `diagram(F)` plays and draws the usual
  shape (pressed above a capo): the part's fretted instrument's, else the guitar's. A shape
  written badly, or only for another instrument, warns (LYS1040) and is a spacer of its length.
- Tuning: the PART's fretted instrument (its tab tuning), else the guitar. The shape gives
  SOUNDING pitches, and a guitar part (treble_8) is written as it sounds. ABSOLUTE: the frame, `octave absolute` and marks do not move
  it; marks after `)` are an error (LYS0035).
- The note AFTER it is read from its LOWEST sounding note (relative mode).
  A phrase whose body opens with it hands the same note on after its reference.
- Chord tones are spelled from the symbol (Cm7 → E♭ B♭); every note carries its string
  number (a tab shows the shape). The diagram is drawn unless `chordDiagrams none`, or an
  `@chord`/`@diagram` on the item draws its own.
- Strum after strum (`chord(F 133211)4 chord(F 133211)4 …`), the name and diagram stand where
  the chord CHANGES and again at each system's head — write every strum the same; never thin
  them out yourself. The same holds for `<f a c>4@chord@diagram` strum after strum (the diagram
  thins; the `@chord` name prints every time).
- To learn the name of a shape: `chord(x32010)4` prints it. `@diagram(x32010)@chord` warns
  (LYS1056): write `@chord(C x32010)`.
- `chord` and `diagram` are reserved in music: a phrase cannot be named either.

## Arpeggios `<< … >>` (written-out broken chords)

Members play in SEQUENCE and EQUALLY SUBDIVIDE the group's total (no per-member
durations — a bare number is always a scale degree). Octaves follow the chord anchor
model above — the next note reads the group's anchor, not its last member; a
degrees-only group anchors on the tonic. Under `octave absolute` every member is where
it is written, exactly as in a `<…>` chord (`<< fis' a' d'' >>` = `<fis' a' d''>`'s notes). NOT LilyPond's `<< >>`
(parallel voices) — those are `voice { }` in Lily#; a `\\` inside is an error.

```
<< c e g >>      // c, then e/g stacked above (C3 E3 G3); after c4 → a triplet of eighths (3:2)
<< c 3 5 >>      // by degrees: c e g
<< 8 5 3 1 >>    // degrees-only anchors on the TONIC: C4 G3 E3 C3 — descending, no marks
<< <c e> g >> |  // a chord member, then g
<< c r e >>      // a rest is a gap (an equal share); e still stacks above c
<< c . d >>4     // a SPACED dot = one more share for the member before it: 2:1 → tuplet 3/2 { c4 d8 }
<< c . . d >>4   // 3:1 → c8. d16 (never glued: `c.` is a duration dot, `3.` a decimal)
<< c@accent e\3 g( a) >> |  // a member carries scripts, string numbers, fingering, dynamics, slur marks
<< c e g >>4\2   // on the group: a dynamic, a chord name, a diagram, a @todo (rings the FIRST
                 //   member), a string number (every member's); a '~' or '(' after >> hangs on the LAST member
<< c e g >>2 |   // a duration after >> = the group's total: 3 in a half (triplet 3:2 of quarters)
                 // (the convention's spelling: M against the power of two below it —
                 //  5 in a quarter → 16ths 5:4; 4 in a quarter → plain 16ths; 2 in a
                 //  dotted quarter → 8ths 2:3)
<< c e g >>'     // marks after >> shift the whole group and propagate to the next note
<< diagram(C x32010) >>2 | // a diagram(…) member is SPREAD: its notes lowest first, one member each (5:4)
<< c diagram(G 320003) e >>2   // mixed: c, G's six strings, then e (e stacks above the root c)
```

Must fit in one measure (else it overflows the meter).

## Annotations (`@name` glued to an item with a place in time)

Attach with `@`, glued to a note, rest, chord or `<< >>` group (a chords row's symbol takes
`@todo` only). Nothing else takes one — not a lyric syllable, `key`, `time`, `tempo`, `clef`
or a form's section (the one exception: a volta bracket's `]@voltaBracket(N)`).
One note may take several: `c4@staccato@p`. Two suffixes:
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
- Voice column: `@shiftOff` keeps a note of a third or later voice in its group's first
  column, so it stands under the voice above instead of a column aside.
- Articulations: `@staccato @staccatissimo @accent @tenuto @marcato @fermata @portato`
- **A mark over a run of notes**: `c8@staccato d e f@!staccato` — the end `@!NAME` goes on the
  LAST note of the run, and every note and chord from the opening one to that one carries the
  mark (drawn and played on each, exactly as `c8@staccato d@staccato e@staccato f@staccato`).
  For `@staccato @staccatissimo @tenuto @accent @marcato`. Without an `@!NAME` after it a mark is
  one note's. Rests carry nothing. A run opens and closes in the same music — one part in one
  section, one phrase, one branch of `voice { } { }` — and the end closes the nearest `@NAME`
  before it there; an `@!staccato` with no `@staccato` before it is an error (LYS4018). Not
  covered: grace notes, and the notes of a phrase called inside the run (write the run inside
  the phrase). Use it for a passage marked throughout instead of a mark on every note.
- String technique: `@upBow @downBow @flageolet` (`@harmonic` on a note or a whole chord is the
  same mark, the small circle) `@stopped` (+)
- Artificial harmonic (strings): `@harmonic` on a MEMBER of a chord — `<c' f'@harmonic>4`, the
  stopped note and the touched note above it. That member is drawn with the open diamond head
  (at every duration, with no dot of its own and no circle); the other member is an ordinary
  note. It SOUNDS one pitch for the three standard cases: the diamond a perfect fourth above the
  stopped note sounds two octaves above the stopped note, a perfect fifth above an octave and a
  fifth, a major third above two octaves and a third; any other interval sounds the two notes as
  written. (`@notehead(diamond)` is another head — filled from the quarter — and changes no sound.)
- A guitarist's pick strokes are the same two signs: a down-stroke is `@downBow`, an up-stroke
  `@upBow` (there is no `@downStroke` / `@upStroke`)
  `@snapPizz` (Bartók pizzicato) - always above
- Right-hand finger (plucked strings): `c4@pluck(p)` — `p i m a`
- Ornaments: `@trill @mordent @prall @turn @reverseTurn`
- **An accidental on an ornament**: `c''2@trill(sharp)`, `@trill(flat)`, `@trill(natural)` — the
  small accidental drawn above the sign (the trill's upper note is sharp / flat / natural). The
  same argument on `@turn`, `@reverseTurn`, `@mordent`, `@prall`, `@pralltriller`. Only these
  three words, glued: `@trill(sharp)`. (A slur starting on a trilled note is still
  `c4@trill( d e)`.)
- Dynamics: `@ppp @pp @p @mp @mf @f @ff @fff` and the accent dynamics `@sfz @sf @fp @sfp @rfz @fz` (default below the staff, above it on a `staff` with a lyrics row directly under it — not on a `condensedStaff` / `combinedStaff` or a tab, where you write `.up`; `.up` / `.down`
  forces the side, e.g. `@f.up`). Subito: `@p.subito` prints an italic "sub." before the p and plays as `@p`;
  a side goes after it (`@ff.subito.up`). Editorial (an editor's suggestion): `@p(editorial)` prints the
  level in parentheses, "(p)", and plays as `@p` — any level (`@mf(editorial)`, `@sfz(editorial)`), a side
  after it (`@f(editorial).up`); not on `@cresc` / `@decresc` / `@dim` / `@niente` (LYS4076), not with
  `.subito`. Write exactly `(editorial)`, glued: any other `(` after a level opens a slur
- Niente: `@niente` — `c4@p@decresc d e f | g1@niente` fades to nothing (circled tip), `c4@niente@cresc …` grows
  from nothing; never at a hairpin's loud end (LYS4029)
- Accidental style: `@courtesy` (cautionary, parenthesized), `@editorial` (musica ficta)
- Arpeggio: `<c e g>4@arpeggio` (wavy line), `<c e g>4@arpeggio(bracket)` (a bracket: not rolled)
- **Arrowed arpeggio**: `<c e g>4@arpeggio(up)` — the wavy line with an arrow head at its TOP (roll
  upward); `<c e g>4@arpeggio(down)` — the arrow head at its BOTTOM (roll downward). On a staff
  and on a tab. Also across a grand staff: `@arpeggio(cross up)`, `@arpeggio(cross down)`. A
  bracket takes no direction (`@arpeggio(bracket up)` is reported and ignored, LYS1008)
- **Arpeggio across both staves** of a `grandStaff`: `<c' e' g'>2@arpeggio(cross)` on the UPPER
  staff's chord (or single note) — one line runs down through the chord or note that begins at
  the same moment on the staff directly below. The lower staff writes NOTHING (no `@arpeggio`
  there). `@arpeggio(cross bracket)` is one bracket across both. If the part is not in a
  `grandStaff` with a staff below, or the staff below has a rest / a held note / another rhythm
  at that moment, it is drawn on its own chord and warns (LYS4064)
- Glissando: `c4@glissando d` (line from this note to the next). `c4@slide d` is the same line
  under a guitarist's name for it — a straight line from this note to the next, short between
  neighbouring notes — and nothing more: no bend, no change to the sound; MusicXML writes it as
  `<slide>` where `@glissando` writes `<glissando>`
- Vibrato is two marks by the part it is written in. On a FRETTED instrument's part (a guitar, a
  bass, a ukulele, a banjo, a mandolin — a part whose `instrument` or `tuning` gives it strings
  with frets) `a'2@vibrato` draws the wavy line above the note, from the note to the next one, on
  the staff and on the tab; the sound is unchanged, and numbers there are an error (LYS4066).
  On every other part it is a SINGER's mark: `a'2@vibrato` (also `@vibrato(depth)`,
  `@vibrato(depth, rate)`) is performed by a singing voice and draws nothing. On a part nobody
  sings (no `singer` line, no words set to it) and that is not fretted it warns (LYS1008) and
  is ignored; for the word on the page write `@text("vib.")`
- Figured bass: `c4@figuredBass(6)` , `d4@figuredBass(6 4)` — the figures of one bass note, top
  to bottom, with spaces between them. A change of figure over ONE held bass — the 4–3 of a
  cadence — is a COMMA between the groups: `a2@figuredBass(4, 3)`. The comma divides the note's
  length into equal parts, one group each (4 on the first quarter, 3 on the second;
  `a2.@figuredBass(6 4, 5 3, 7)` is three groups a quarter apart). So a comma is never a way
  to stack: `6 4` is 6 over 4, `6, 4` is 6 then 4. ONE annotation a note: a second
  `@figuredBass` on the same note is an error (LYS1067; write the comma form, not
  `a2@figuredBass(4)@figuredBass(3)`), the comma form on a grace note is an error (LYS1068), and
  an empty part (`4,`) makes the group unknown. The whole vocabulary:
  - a digit `0`–`9` is a figure (`6`, `6 4`, `7 5 3`; `0` is an empty row);
  - `s`, `f`, `n` AFTER a digit give it a sharp, a flat, a natural: `6 s` (6♯), `4 f` (4♭),
    `7 n` (7♮) — also written closed up, `6s`, `4f`;
  - `#` BEFORE a digit is that digit's sharp (`#6`, the same as `6 s`; it prints after the
    digit); `#` with no digit after it is a figure of its own, the sharp alone — the raised
    third — at any row: `#`, `7 #`, `5 #`, `7 5 #`. So `6 #` is 6 over a sharp, and `6 # 4` is
    6 over ♯4;
  - `_` is a figure held from the note before (an extension line): `_`, `6 _`.

  Nothing else is read, and a group with anything else in it is ignored whole with a warning
  (LYS1008) that lists the above: no `b`, `+`, `-`, `/`, `!`, `♯`, `♭`, no words (`sharp`,
  `flat`), and no `s` / `f` / `n` without a digit before it (`b7`, `6+`, `6 b`, `n`, `f` are all
  refused; a flat or a natural alone is not written yet). Figures in a row under one long note
  are the comma form above, in equal parts; unequal parts are tied notes with a figure each
- Chord diagram (guitar fret diagram): `c4@diagram(x32010)` — one character per string, low
  to high: a digit is the fret (`0` or `o` open), `x` muted — frets 10–15 with a `-` on each
  side (`@diagram(xx-10-12-13-11)`); 4–8 strings. Above the note
  whatever the stem; `@diagram(x32010).down` puts it below. Size: `fonts { diagram step +3 }`.
  A bare `@diagram` on a `<…>` chord draws the chord's own notes as the part's strings fret them
  (`<g b d' f'>1@diagram` = xx5431); a chord no hand can hold draws none (LYS1055 warns). On a
  single note it draws where that note is held (`c'1@diagram` = xxxx1x).
  On a fretted part, `@diagram(SHAPE)` on a `<…>` chord must sound the chord's notes, or LYS1057
  warns: after editing the notes, rewrite the shape (or write a bare `@diagram`).
- Guitar bend: `c4@bend(half)` (1 semitone), `@bend(full)` (2), `@bend(N)` (N = 1–12
  semitones) — an arrow labelled in steps.
- Bend release: `@release`. On the SAME note as the bend, `a'4@bend(full)@release`, the string
  is bent up and let back within the note — the arrow up with its amount, then an arrow down.
  On the note AFTER a bent note, `a'4@bend(full) a'4@release`, it is let back to that note — an
  arrow down from the bend's height to the note. "After" is the next note of the same voice; a
  rest or another note between them breaks the pair. A `@release` with no `@bend(…)` on its own
  note or on the note before it is an error (LYS4067). Silent in MIDI, as the bend is.
- Natural harmonic at a fret (fretted parts): `e,4@harmonic(12)`. Write the OPEN STRING the
  harmonic is played on — its own note, which names the string — and the fret the string is
  touched at: 12, 7, 19, 5, 24, 4, 9, 16 or 3 (an integer; nothing else). The staff shows what
  it SOUNDS, with the harmonic head (a diamond, open for a half note and longer): 12 an octave
  above the open string, 7 and 19 an octave and a fifth, 5 and 24 two octaves, 4, 9 and 16 two
  octaves and a major third, 3 two octaves and a fifth. The tab shows the fret in angle
  brackets on that string, `⟨12⟩`, and MIDI plays the sounding pitch. On a guitar:
  `e,4@harmonic(12) a,4@harmonic(7) d2@harmonic(5)` sounds E3 E4 D5. A relative-octave note
  after it is read from the note written (the open string). Errors: a fret not in the list
  (LYS4070); a part that is not a fretted instrument's (LYS4071); a chord or a chord member
  instead of a single note (LYS4072 — write several strings' harmonics in voices); a note that
  is not an open string of the part's tuning (LYS4073 — with a file `capo N` the open strings
  are the capo's, written as they sound: `fis,4@harmonic(12)` under `capo 2`). Two strings open
  at one pitch: add the string, `d'4\1@harmonic(12)`. Only a number glued inside the
  parentheses is a fret: `e'4@harmonic(d' c')` is still the circle and a slur.
- Guitar lines over a run of notes, each written on the first note and closed on the LAST note
  it covers (both included), as `@textSpan("…") … @!textSpan` is:
  - palm mute `e,8@palmMute e, e, e,@!palmMute` — "P.M.", a dashed line and a hook at its end;
    played short (each note half its length).
  - let ring `e,8@letRing b, e g@!letRing` — "let ring", the same line; each note sounds on to
    the end of the run's last note (or until its own pitch is struck again).
  - barre `b,8@barre(7) fis b d'@!barre` — "CVII" (C and the fret, 1–24, as a Roman numeral);
    `@barre(7 half)` prints "½CVII". A `@barre(N)` with no `@!barre` after it is ONE note's: the
    label alone, no line (so is one that another `@barre` follows before any end). A barre with
    no fret, a fret outside 1–24 or another word is an error (LYS4065). No change to the sound.
  They stand above the staff, and above a tab that stands alone (`tab g` with no `staff g`; a tab
  beside its staff repeats none of them). The three are independent lines: a palm mute may run
  under a barre and beside a `@rit`, each closed by its own name. A palm mute or a let ring
  nobody closes is an error (LYS4018), as an unclosed `@rit` is.
- Tab technique letters: `@hammerOn` (H), `@pullOff` (P), `@tap` (T) — small italic
  letters opposite the stem (`.up`/`.down` force), drawn on a `tab` staff too.
- Chord names: `c4@chord(C)` , `d4@chord(Dm)` , `e4@chord(Am7)` — the SYMBOL as it
  prints (`F#m`, `Bb7/D`, `Gm7-5`), the same format as a chords row, in every printed
  spelling: `CΔ7`/`CM7`/`CMaj7` (maj7), `Cø7`/`C07` (m7-5), `C°7`/`Co7` (dim7), `C+7`,
  `C-7`/`Cmi7` (m7), `C7no3`, `B♭7` (`♭`/`♯` for `b`/`#`), and a parenthesized alteration
  list with spaces or commas inside — `C7(b9, #11)`, `Cm7(b5)`, `C(add9)`, `Cm(maj7)`,
  `C7(9)` (= C9), `C(9)` (= Cadd9). `C-5` is refused (write `C(b5)`), and so is a `b`/`#`
  glued after a letter inside the quality (`C7b9`: write `C7(b9)` or `C7-9`). The page prints
  the canonical name whatever the spelling. The retired
  lowercase `:` entry (`@chord(a:m)`) is not recognised (LYS1008 warns, no symbol is
  engraved). A bare `@chord` derives it from the notes. The symbol is the FIRST word only:
  `@chord(C 7)` is C with a one-character shape (warns) — write `@chord(C7)` for C7; the
  alteration list is part of that word (`@chord(C7(b9, #11) x32310)`).
  It may sit on a rest or spacer too (`s1@chord(C)`, name and diagram as on a note); a bare
  `@chord` there has no notes to name and warns (LYS1020) — write the name.
- Chord diagrams (drawn under the name) appear ONLY where a chord WRITES its shape; a name
  alone (`@chord(G)`, a row's `G`) draws none — unless the score's layout says
  `chordDiagrams all`, where EVERY chord name (a bare `@chord`'s derived one too) draws its
  written shape, else the usual one — or the file's top-level shape table LISTS the chord:
  `chordShapes { Cm7 x35343  G  section Chorus { C x35553 } }` (a listed chord draws wherever
  it is named — the table's shape, the usual one for a name alone; a `section NAME { … }`
  block's entries apply to the chords written in that section; a shape written at the chord
  still wins). A shape is one character per string, LOW string
  first (`x` muted, `o`/`0` open, a digit the fret): `@chord(Cm7 x3x546)` (`@chord(x32010)` names
  it from the shape, `@diagram(x32010)` draws it with no name, `chord(x32010)4` plays it and
  names it); in a chords row
  glued to the symbol: `F(133211)`,
  `F(133211 2010)` (a guitar and a ukulele shape, routed by string count),
  `F(guitar 133211 ukulele 2010)` (bound by tuning name). Frets 10–15: put a `-` on each
  side of each two-digit fret — `@chord(Cm 8xx88-11)`, `Cm(xx-10-12-13-11)`,
  `Cm(8-10-10-888)`, `@diagram(x-15-13-12-13-x)`; a `-` between every string also reads
  (`x-x-10-12-13-11`). Between dashes, EXACTLY two digits are ONE fret: frets 10, 9, 9 are
  `10-9-9` (`10-99` = fret 99, an error); routed by string count (`8xx88-11` = 6); lower case
  only; use the one-character form when every fret is ≤ 9. The diagram's tuning: the part's
  fretted instrument (an `@chord`'s or item's part; for a row, the staff it stands directly
  above), else the score's `layout { chordTuning TUNING }` (the tab tuning words), else the
  guitar; `layout { chordDiagrams none }` = no diagrams at all, whatever the parts. A capo is
  the top-level line `capo N` (1–11): every shape is then the shape pressed above it, the names
  the pressed chords' (`layout { chordNames shape | sounding | both }`), and "Capo N" stands at
  the head. Common guitar shapes (LilyPond's): C `x32010`, F `133211`, G
  `320003`, Cm7 `x35343`; ukulele C `0003`. A written shape that cannot be used warns LYS1038;
  the name still draws. A written shape that disagrees with its symbol (a non-chord tone, or a
  missing 3rd/7th/altered fifth/tension — the root and the perfect fifth may be left out; the
  bass is not checked) warns LYS1039 naming the fix. There is no voicing index and no `mute` word.
- Fingering: on a note, `d''4.@finger(5)`; per chord note, `<c@finger(1) e@finger(3)>4`
- Rehearsal mark: `c4@mark("A")` — written in ONE part it is the whole score's: every score of the piece prints it, a part sheet (`score vn2 { staff vn2 }`) of another part too. So is a `tempo` written inside one part's music, and on a part sheet a `@rit` / `@accel` / `@rall` / `@textSpan` of a part the sheet does not show (unless the sheet's own part writes one in that bar). Do not copy them into every part
- Half ties: `c4@laissezVibrer` (l.v. into silence), `c4@repeatTie` (resume from a repeat)
- Effects: `@dead` (x notehead; `@notehead(cross)` is the same head — `@cross` is NOT: it moves the
  note to the other staff of a grand staff; on a part drawn with no staff beside it = warning
  LYS4031 and the note stays). In a chord, `<c e@dead>4` mutes that member and
  `<c e>4@dead` every member (since 2026-11-13; before, both drew nothing). `@notehead(cross)`
  is the head alone — `<c e@notehead(cross)>4`, `<c e>4@notehead(cross)`. Other heads the
  same way: `c2@notehead(diamond)`, `<c f@notehead(diamond)>2` (a stopped note and the touched
  fourth over it). Also `@parenthesize` (head in parentheses, a ghost note:
  `sn16@parenthesize`; `<c e g>2@parenthesize` = one pair round the chord), `@fall`/`@doit` (jazz bends), `@breath`/`@caesura`
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
- Left to check: `fis'8@todo`, `@todo("F# or F natural?")`, `@todo(o12)`,
  `@todo(o12 "memo")` on a note, rest or chord — a mark that draws and plays nothing; the
  compiler reports it (LYS4026) and the preview draws the head red. A quoted argument is the
  memo, a bare one the key (a letter, then letters, digits, `_`, `-`) a tool links the mark by.
  **When you rewrite music that carries `@todo`, keep every mark you did not resolve, on the
  same note; drop a mark only where your change answers it.** Never invent a key.
  A chords-row symbol or no-chord `r` takes the same mark, GLUED after the symbol (and after its
  shape group): `| C Am7@todo("Am7 or C?") |`, `F(133211)@todo(o12 "barre?")`, `r@todo` — the ONLY
  annotation a chords row accepts (any other is LYS1028).

```
c4@staccato d4@accent <e g>4@arpeggio |
```

## Ties, slurs, beams

```
c4~ | c4 d e f |     // tie (same pitch across the barline) with ~
<c e g>2~ <c e g> |  // a tie after a chord ties every note the next item repeats
<c e g~>2 <c e g> |  // a tie on a member ties that note alone; the others sound again
c4( d e f) |         // slur (different pitches) with ( )
<c e>2( <d f>) |     // a slur may bind chords, not just single notes
c8( d e( f) g) r4. | // WRONG: one slur at a time (the inner '(' is ignored); an outer phrase is @phrasingSlur
c4@slur(dashed)( d e f) | // a DASHED slur: @slur(dashed) on the note where the slur begins (also @slur(dotted))
c2@tie(dashed)~ c2 | // a dashed tie: @tie(dashed) on the note with the ~ (also @tie(dotted)); after a chord, all its ties
c8[ d e f]           // manual beam; beaming is automatic otherwise
r8[ c d e] |         // a manual beam may open or close on a rest, and reaches it
```

`@slur(dashed)` / `@tie(dashed)` say how the ONE slur / tie beginning on their note is drawn
(an editor's suggestion, a slur for one verse only); they sound as the solid ones. The word
is `dashed` or `dotted` — or `solid`, the plain curve, there so the editor's step keys can show all three — and nothing else (LYS4074). On a note where no slur (no tie) begins —
a rest, a note inside a chord's brackets, the slur's LAST note — it is an error (LYS4075).
A phrasing slur takes the same word on its opening mark: `c4@phrasingSlur(dashed) d e f@!phrasingSlur`
(the closing `@!phrasingSlur` is unchanged).

A `[ … ]` only JOINS notes: `c8[]` groups nothing — the note is beamed like `c8` — and warns
(LYS4016). A SUNG part's beams are a part property: `part s { clef treble beaming syllabic }`
beams the traditional vocal way — an automatic beam joins only the notes sung on ONE syllable,
and every note that starts a syllable keeps its own flag. What holds a syllable is read off
the NOTES (as LilyPond reads a melisma): the notes of a slur after its first, and a note tied
into. So `c8 d e( f) g16( a b c')` is flag, flag, a beamed pair, a beamed four. The lyrics are
not asked: a melisma written only in the words (`_`) needs a slur — or a manual `[ ]`, which
still joins whatever it brackets. `syllabic` is the only word; leave the property out for the
ordinary beams of the meter.

## Hairpins (spanners over several notes)

Place the spanner mark on the starting note; it runs to the next dynamic — or to an explicit
stop, `@!cresc` / `@!decresc` / `@!dim` (LilyPond's `\!`), where no dynamic follows.

```
c4@p@cresc d e f@f |        // crescendo p -> f
g4@f@decresc a b c@p |      // decrescendo
c4@cresc d e f@!cresc | g1 | // a crescendo that ends on f, no dynamic after it
```

A hairpin stands on its staff's own side like a level: below, and above a staff with a lyrics
row directly under it. `@cresc.up` / `@decresc.down` sends it to the other side, and the marks
after it on the same line (until the hairpin has ended) that write no side follow, as in LilyPond.

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
- Volta repeats are symbolic and live in the form: `form { |: A [1. B] :| [2. C] }`.
  An ending NAMES one or more sections, played in order under one bracket:
  `|: A [1. B C] :| [2. D]` plays A B C, then A D. The body before the first ending must
  name a section (`|: [1. B] :| [2. C]` and `|: :|` are errors, LYS1041).
  With endings the numbers are the passes: `|: A [1-2. B] :| [3. C]` plays A B A B A C, and
  every pass from 1 to the highest number must be named by exactly one ending (LYS1043); a
  `:|*N` beside endings is an error (LYS1042). Without endings the body plays twice, or
  `|: A :|*N` N times — more than twice prints "N×" over the closing repeat bar. A bracket prints its passes as LilyPond does: `[1-2.` "1. 2.",
  `[1-3.` "1.–3.". The `]` ends the ending and hooks its bracket's right end
  down; `-]` ends it with a straight (open) right end: `|: A [1. B] :| [2. C D -]`. The
  `]` may be left off only right before a `:|` (`|: A [1. B C :| [2. D]` — the `:|` closes
  it, hooked); an unclosed LAST ending is an error. The first ending is the last thing
  before the `:|`, and each later ending follows its own `:|`: `:| [3. D]` again.
- How far the bracket reaches: `layout { voltaBracket all|line|N }` — `all` (default) every
  bar of the ending, `line` up to the end of the system it starts in, `N` its first N bars.
  One ending overrides it after its `]`: `[1. B C]@voltaBracket(2)`. A bracket cut short
  always ends straight, whatever `]` / `-]` says.
- An ending needs a repeat to be an ending OF. Write `form { [1. A] }` and no bracket
  is drawn: it engraves as the plain reference `A`, and LYS6008 warns that the `1.` prints
  nothing. Put the ending inside the repeat — `form { |: A [1. B] :| [2. C] }`.

```
part melody { clef treble }
section A { melody { c4 d e f | } }
section B { melody { g2 g | } }
section C { melody { a2 a | } }
form { |: A [1. ~B] :| [2. ~C] }
score { staff melody }
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
  adds the repeat tie itself. A beam never crosses a section. A sustain pedal still down
  (`@sustain` with no `@!sustain` yet) is carried into the next section, as a slur is: do not
  lift and press it again at a section's edge unless the music does.
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
- Percent repeat: write the bars once, then ONE mark per repetition, each alone between bar
  lines — `%` repeats the bar before it, `%N` the N bars before it:
  `c4 d e f | % | % | % |` (the bar four times), `a2 b | c'2 d' | %2 | %2 |` (two bars three
  times). The bars repeated must be whole bars; a mark of another length cannot follow a run
  of marks. `repeat percent` / `repeat unfold` are gone (LYS1047; the editor's quick fix rewrites them).
- Tremolo. On ONE note, `:N` after the duration strikes the stem (`c4:16` — one stroke per
  beam of N: `:8` one, `:16` two, `:32` three). Between TWO notes, `repeat tremolo N { a16 b }`
  alternates them, and lasts N times the body: `repeat tremolo 4 { c16 e }` = 4 × an eighth =
  a half note's time, drawn as two half notes joined by the tremolo beams.
- NOT Lily#: `repeat volta` / `alternative` are LilyPond's spellings (the parser refuses
  `repeat volta` and points at the symbolic `|: ... :|` form above; `alternative` is just a
  word). `repeat` is only for `tremolo`.

## Tuplets

```
tuplet 3/2 { c8 d e }                 // triplet: 3 in the time of 2
tuplet 3/2 { c8 d tuplet 3/2 { e16 f g } | }   // nesting allowed
```

A tuplet lies inside ONE bar. A group that runs over the bar line is two tuplets, tied:
`c4 tuplet 3/2 { d4 e8~ } | tuplet 3/2 { e8 f4 } g4 |` in 2/4 (a `|` in the middle of the braces
closes no bar: LYS2017 says a bar line is missing).

## Repetition shorthand

A phrase written N times is its music N times - the shorthand for music that is played again
and drawn again:

```
phrase ground { d2 a,2 | b,2 fis,2 | }
ground ground ground ground              // 8 bars from one line
```


## Grace notes

```
grace { d16 e } f4           // grace before F
acciaccatura { a16 } b4      // slashed grace
appoggiatura { c8 } d4 |     // unslashed grace
c2 afterGrace { d16 e } f2 | // AFTER the note before it: the run ends the c (LilyPond \afterGrace)
```

## Multi-voice (one staff)

```
voice { c'2 d } { e2 f }                 // ONE `voice`, then each simultaneous line in braces
```

```
voice sop { c'2 d } alt { e2 f }         // named lines — `lyrics words { … }` sings one
```

`voice` opens the span once; the first line's stems go up, the second's down. Writing the
keyword again (`voice { … } voice { … }`) is an error (LYS0019): that would be two spans one
after the other, not two lines together. The LilyPond `<< … \\ … >>` form is not Lily#.

## Instrument ranges

A part with an `instrument` plays only inside that instrument's range, and every note outside
it is warned (LYS1052: "this note sounds C2, below the range of the Guitar"). In such a part keep
EVERY note inside the range below — move a note an octave, or give the line to another part,
rather than write it out of range; a guitar part's accompaniment, a bass line written for the
guitar, a melody lifted from the piano, all stay inside. "write" is the range as written in
absolute octaves (LilyPond's — `c' ` is middle C in every part; a part's `octave N` moves it; a
transposing instrument writes what its player reads, an octave clef's part what it sounds).
Read the "write" column, not the pitch names: `instrument bass` (the 4-string electric or
double bass, lowest string E) sounds an octave under what it writes, so its lowest note is
written `e,` (E2 on the page, sounding E1) and a written `c,` sounds C1 — out of range; a bass
line that wants to go lower turns up an octave there.

A range is how far an instrument CAN go, not where its part lies: keep each part where its
player usually plays. A piano's left hand lies mostly between C2 and C4, round C3 — a left hand
that lives under C2 is a rumble, not a bass line — and its right hand mostly between C4 and C6.
Both hands write LilyPond's octaves: C3 is `c`, middle C `c'`, C5 `c''`.

<!-- BEGIN instrument ranges: generated from InstrumentDefaults.SoundingRange by InstrumentRangePromptTests (LILYSHARP_UPDATE_DOCS=1) -->
| instrument | sounds | write (absolute octaves) |
|---|---|---|
| `violin` | G3–A7 | `g` – `a''''` |
| `viola` | C3–E6 | `c` – `e'''` |
| `cello` | C2–C6 | `c,` – `c'''` |
| `bass` | E1–G4 | `e,` – `g''` |
| `contrabass` | E1–G4 | `e,` – `g''` |
| `contrabass5` | B0–G4 | `b,,` – `g''` |
| `piano-right` | A0–C8 | `a,,,` – `c'''''` |
| `piano-left` | A0–C8 | `a,,,` – `c'''''` |
| `guitar` | E2–E6 | `e,` – `e'''` |
| `bass5` | B0–G4 | `b,,` – `g''` |
| `bass6` | B0–C5 | `b,,` – `c'''` |
| `ukulele` | C4–C6 | `c'` – `c'''` |
| `mandolin` | G3–B6 | `g` – `b'''` |
| `banjo` | D3–D6 | `d` – `d'''` |
| `flute` | C4–D7 | `c'` – `d''''` |
| `piccolo` | D5–C8 | `d'` – `c''''` |
| `oboe` | B♭3–A6 | `bes` – `a'''` |
| `clarinet` | D3–B♭6 | `e` – `c''''` |
| `clarinet-a` | C♯3–A6 | `e` – `c''''` |
| `bassoon` | B♭1–E5 | `bes,,` – `e''` |
| `soprano-sax` | A♭3–E6 | `bes` – `fis'''` |
| `alto-sax` | C♯3–A♭5 | `bes` – `f'''` |
| `tenor-sax` | A♭2–E5 | `bes` – `fis'''` |
| `baritone-sax` | C2–A♭4 | `a` – `f'''` |
| `trumpet` | E3–B♭5 | `fis` – `c'''` |
| `trumpet-c` | F♯3–C6 | `fis` – `c'''` |
| `horn` | B1–F5 | `fis,` – `c'''` |
| `trombone` | E2–F5 | `e,` – `f''` |
| `tuba` | D1–F4 | `d,,` – `f'` |
| `timpani` | D2–C4 | `d,` – `c'` |
| `organ-right` | C2–C7 | `c,` – `c''''` |
| `organ-left` | C2–C7 | `c,` – `c''''` |
| `organ-pedal` | C2–G4 | `c,` – `g'` |
| `soprano` | C4–C6 | `c'` – `c'''` |
| `alto` | F3–F5 | `f` – `f''` |
| `tenor` | C3–C5 | `c` – `c''` |
| `baritone` | G2–G4 | `g,` – `g'` |
| `voice-bass` | E2–E4 | `e,` – `e'` |
<!-- END instrument ranges -->

**Chords on a bowed string** (`violin`, `viola`, `cello`, `contrabass`). The bow sounds two
neighbouring strings at once, so only a DOUBLE STOP within the hand's reach is a chord there.
Three or four notes struck at one moment warn — LYS1055, "this chord (G3 D4 B4) cannot be bowed
at once … keep two of its notes (a double stop), or break the chord, the low notes first" — and
so does a pair that is not on neighbouring strings. A chord that is not bowed at once does
not warn (since 2026-11-13): one marked `@arpeggio` (rolled) or `@snapPizz`, and every chord
after a `@text("pizz.")` until the `@text("arco")` that ends it. A bowed triple or quadruple
stop is otherwise written the way it is played, BROKEN, the low strings first:

```
octave absolute
part vn { instrument violin }
section A {
  vn {
    grace { <g d'>8 } <b' g''>2  acciaccatura { g16 d' } <b' g''>2 |   // two and two; one at a time
    <g d'>8 <b' g''>4. << g d' b' g'' >>2 |                            // in time; an arpeggio group
  }
}
form { ~A }
score { staff vn }
```

All four spellings are clean. Keep the stacked `<g d' b'>4` only where the warning is accepted.

## Tablature (`tab`)

A tab is a SCORE item: the part is written as ordinary notes, at the pitch it sounds on its
staff, and `tab NAME` draws the same music as fret numbers. Nothing in the music changes.

```
octave absolute
part gtr { instrument guitar }
section A {
  gtr {
    e,4 a, d g | b4 e' <e, b, e gis b e'>2 |          // open strings; a six-string chord
    c'4\3 c'\2 e'4@dead <c' e'@notehead(cross)>4 |    // \N = on string N; a dead note
    a'8@hammerOn( b') b'@pullOff( a') a'4@tap a'@bend(full) |
    a4@glissando d' a'@slide b' |
  }
}
form { ~A }
score { staff gtr  tab gtr }
```

- **Asking for it**: `tab NAME` in the score, usually right under `staff NAME`. Beside a
  notation staff of the same part it is fret numbers only (the staff carries the rhythm);
  alone (`score { tab gtr }`) it draws stems, flags, rests and the meter itself. `tab gtr as
  numbers` / `tab gtr as full` says so by hand.
- **Tuning**: the part's `instrument` (`guitar`, `bass`, `bass5`, `bass6`, `ukulele`,
  `mandolin`, `banjo` = open G; a part with no instrument is fretted as a guitar), or the part's
  `tuning WORD`, or — for one score — the word BEFORE the part: `tab guitarDropD gtr`. Never
  `tab gtr tuning guitarDropD` (an error that says so). The words: `guitar guitar7 guitarDropD
  guitarDropC guitarOpenG guitarOpenD guitarDadgad guitarLute guitarAsus4 bass bass4 bassDropD
  bass5 bass6 violin viola cello doubleBass mandolin banjoOpenG banjoC banjoModal banjoOpenD
  banjoOpenDm banjoDoubleC banjoDoubleD ukulele ukuleleD tenorUkulele baritoneUkulele` — exactly
  these (`dropD` is an unknown tuning; the error lists them).
- **A tuning of your own — write the strings out**, quoted, where the word stands:
  `part gtr { instrument guitar  tuning "d, a, d g a d'" }` (DADGAD), or for one tab
  `tab "d, a, d g a d'" gtr`. The list is the open strings from the HIGHEST STRING NUMBER
  (normally the lowest string) to string 1, spaced, each an absolute pitch name exactly as a
  note is written under `octave absolute` (`c` = the C below middle C; `'` up an octave, `,`
  down; `fis`, `bes`), at the pitch the string SOUNDS. 4 to 8 strings. Re-entrant tunings are
  written as strung: `tuning "g' c' e' a'"` is a ukulele. A BASS part writes its notes an
  octave above their sound, so its strings are an octave below its notes:
  `part b { instrument bass  tuning "d,, a,, d, g," }` (drop D by hand). A list that is not 4–8
  pitch names is an error (LYS4068). Everything follows the list: the frets, `\N`, LYS5002 /
  LYS5003, the part's range, its chord shapes and diagrams (one character per string of the
  list). Not for `layout { chordTuning … }`, which takes the words only.
- **A dropped tuning is the part's** (a word or a list): `part gtr { instrument guitar  tuning guitarDropD }` frets
  on D A D G B E and moves the part's RANGE with it — the low `d,` is in range (LYS1052 measures
  from the tuning's lowest string to its highest string's top).
- **Strings and frets are chosen for you**: low positions first, the hand kept where it is, a
  chord's notes on a string each; frets 0–24. A TIE stays on its string (its second note prints
  no number), and a `@glissando` / `@slide` keeps both notes on one string where one string
  plays both. A slur does not hold a string.
- **`\N` after a note asks for string N** (1 = the highest): `c'4\3`. Write it only where the
  string matters. In a chord, on the members — `<e'\1 b\2>` — because `<e' g'>\1` numbers both
  (two notes on one string, LYS1063); one pitch twice needs two strings, `<e'\1 e'\2>`
  (LYS1064 otherwise). A string that cannot play the note — no such string (`\7` on a guitar),
  or the note out of that string's frets — warns and is chosen again (LYS5003).
- **A note the strings do not have** (below the lowest string, above fret 24) warns LYS5002
  and is left off the tab — in a chord too; the staff still shows it. Usually an octave slip. A
  tab in ANOTHER instrument's tuning says the octave it is played in: `tab bass5 melody as 8vb`
  (`8vb 15mb 8va 15ma`; LYS5004 proposes it).
- **Capo**: the file's top-level `capo N` (1–11). Notes are still written as they SOUND; the
  tab counts its frets FROM the capo (0 = the capo, as on the chord diagrams), so under `capo 2`
  a sounding `fis,` is fret 0 of the sixth string. A note below the capo on every string — an
  open low `e,` under `capo 2` — cannot be played: LYS5002 names it and it is left off the tab.
  One capo for every fretted part (`part g { capo 2 }` is refused); a BASS's tab ignores it and
  counts from the nut.
- **One tab's own capo**: `tab gtr capo 2` in the score (after the part and any `as …`, before
  `hidden` / `sounds`) — THAT tab counts its frets from fret 2, whatever the file's `capo` is;
  `capo 0` = no capo on this tab of a capoed file; 0–11. Two guitars, one capoed:
  `score { staff g1  tab g1 capo 2   staff g2  tab g2 }`. It changes the tab's numbers ONLY:
  the notes are written as they sound, the sound is the same, and the chord diagrams, the
  pressed chord names and "Capo N" at the head stay the FILE's `capo N` (so do not give a tab
  another capo than the file's under a part that writes `chord(…)` shapes or diagrams — it
  warns, LYS4069). It is not printed: say it in the part's name
  (`instrument guitar "Guitar 1 (capo 2)"`). `staff gtr capo 2` is an ERROR (LYS4069): a
  staff shows notes as they sound and reads no capo.
- **What a tab shows of the marks**: slurs, ties (the held note's number is dropped — a held
  chord's too; a `q` or a bare duration repeats a chord on the strings it was written on),
  no `@finger` when the part's staff stands beside the tab (the staff carries it),
  `@hammerOn` / `@pullOff` / `@tap` (the letters H P T — a slur is still written for the curve:
  `a'8@hammerOn( b')`; the two notes under that slur are put on one string), `@bend(half|full|N)` (the arrow) and its `@release`, `@vibrato` (the wavy
  line), `@glissando` / `@slide` (the line
  between two fret numbers), `@arpeggio`, a harmonic's fret in angle brackets (`e,4@harmonic(12)`
  is `⟨12⟩` on its string; the touched member of `<c' f'@harmonic>4` is its fret in brackets),
  and `@dead` (a × in place of the number — a note's,
  one chord member's `<c e@dead>4`, or every member's `<c e>4@dead`). `@notehead(cross)` /
  `@notehead(diamond)` change the STAFF's head only: the tab keeps the number (as LilyPond's
  `\override NoteHead.style` does), so for a muted string write `@dead`. A slash `/`
  has no number.
- **Chord shapes are not tab**: `chord(C x32010)`, `diagram(…)`, `@diagram`, the top-level
  `chordShapes { A7(b9) x02323  G }` table (a name may carry its parenthesised alteration) and
  `capo` draw fret DIAGRAMS over a staff — see *Annotations* and *Layout*. A `chord(…)` /
  `diagram(…)` item's notes carry their strings, so a tab under it shows the shape.
- `@vibrato` on a fretted instrument's part (a part whose `instrument` or `tuning` gives it
  strings with frets) is the WAVY LINE above the note, on the staff and on the tab alike — see
  *Annotations*. (On a sung part it is the singer's vibrato and draws nothing; only on a part
  that is neither does it warn, LYS1008.) The word "vib." is `@text("vib.")` anywhere.

## Drums (`clef percussion`)

```
part kit { clef percussion }          // a drum part: drum names, no key signature, MIDI channel 10
section Groove {
  kit {
    voice { hh8 hh hh hh hh hh hho hhc | }   // hands up
          { bd4 r8 bd sn4 bd | }             // feet (and snare) down
  }
}
drummap { hh: position 6 notehead x }  // TOP LEVEL, optional: re-table a drum
```

- Names are LilyPond's drum pitches, short or long: `bd`/`bassdrum` `sn`/`snare` `ss`
  (side stick) `hh` `hho` (open, draws ○) `hhc` (closed, draws +) `hhp` (pedal) `tomh tommh
  tomml toml tomfh tomfl` (toms, high → low) `cymc` (crash) `cymr` (ride) `rb` (ride bell)
  `cyms` (splash) `cb` (cowbell) `tamb`, plus the Latin set (`boh bol cgh cgl timh timl agh
  agl cab mar wbh wbl tri …`). `tt` (tamtam) is not one.
- Durations, dots, bare durations, rests, `<bd hh>4` chords, `repeat`, `@` marks work as on
  notes. A drum name is read as a drum note in ANY part, so a phrase cannot be named after
  one (LYS1030); keep pitches out of a drum part. `cb` in a part with no drums warns
  (LYS1062), C flat being `ces`.
- `drums { LINE LINE … }` — a drum chart: each LINE is a phrase name or `{ … }`, optionally
  led by `up`/`down`; the lines play together (`drums { hats back kick }`). Drawn in two
  voices: hands merged on stems up, feet (`bd bda hhp`) stems down. Lines hold drum notes,
  drum chords, rests, `tuplet`, percent marks. Notes straight in the chart =
  LYS0040; three hands / three feet / hh with hho at once = warning LYS4030. A level written in two
  lines at one moment prints once when it is the same (`@ff` and `@ff`); two different levels
  (`@f` and `@p`) = warning LYS4033 — write a drum level in one line. Prefer it to
  `voice { } { }` for a kit: write each pattern once as a one-bar phrase. Flam:
  `acciaccatura { sn16 } sn4`. The editor offers stock one-bar patterns (`hats8 hats16 hats4
  hatsOpen shuffle back halfTime ghost flam fourFloor kick8 kickSync fillToms fillTriplet`) and
  writes their `phrase` declarations into the file; outside the editor, declare them yourself.
- **Bar lines around a chart and before `%`.** A chart of one-bar phrases is one bar: write a
  bar line after EACH chart, `drums { hats back kick } | drums { hats back fill } | % |`. A `%`
  repeats what stands between the two bar lines before it, so two charts (or two phrases,
  `riff riff | % |`) with no bar line between them are two bars before one mark — an error for
  phrases (LYS1049), a section one bar too long for charts (LYS2007). A one-bar phrase then a
  mark is fine: `riff | % | % |`. An INLINE line may end its bar or not, as a phrase may:
  `drums { { cymr4 cymr cymr cymr } { r4 hhp r hhp } } | % |` and
  `drums { { cymr4 cymr cymr cymr | } { r4 hhp r hhp | } } | % |` are the same three bars —
  the bar line after a chart confirms the bar its lines closed (a second one, `… } | |`, is an
  empty bar). Not so a `voice { … | } { … | } |` span: its bar lines are the staff's own, and
  the one after it opens an empty bar.
- `drummap { NAME: position N notehead W midi K mark M … }`: position −9…9 (0 = middle
  line), notehead `x cross diamond triangle slash xcircle default`, midi 0…127, mark `open
  stopped staccato tenuto`. It only changes existing drums; a bad entry warns LYS0024.

## Lyrics (a named track of words)

**THE STANDARD WAY to say whose words they are: on the part.** `part melody { instrument soprano
lyrics words }` — the part's header names its track (no braces; one statement a track). Write
this whenever a part has words: it is the one place read off the part itself, the singer line
then needs no name (`singer female on melody`), and a row or a singer line that names
other words for the part is warned about (LYS4055). The same in both writings of a file:

```
// written by section
part melody { instrument soprano  lyrics words }
section Main {
  melody { c4 d e f | g2 g | }
  lyrics words { Hap- py birth- day | to you | }
}
singer female on melody
form { Main }
score { staff melody  lyrics words }
```

```
// written by part: the same statement, with the header's properties
part melody {
  instrument soprano
  lyrics words
  section Main { c4 d e f | g2 g | }
}
lyrics words {
  section Main { Hap- py birth- day | to you | }
}
singer female on melody
form { Main }
score { staff melody  lyrics words }
```

A score's row under a staff and a singer line's `on PART` say the same relation for ONE score
or ONE voice, and a file that writes only those still works; the part's statement is what makes
the file say it once.

A lyrics track holds its words alone. A score row PRINTS them, and a top-level
`singer` line SINGS them (below). The score places its row by ORDER (score = a
vertical stack of bands): a `lyrics NAME` row directly below a staff is that
staff's verse (a run of rows stacks as verses, and a chorale writes `staff alt
lyrics verse` once per voice). `lyrics NAME on PART` follows another part's or a
named voice's melody instead: under another staff it shows only the words, at
that melody's rhythm, without engraving the melody. Directly below a staff of
several parts `on` is required (LYS4051). Multiple tracks may follow one part (two
languages = two names). A track may not have the name of a part or voice
(LYS7008). A row with no staff above and no `on` is the even-spread lead-sheet row.
A track that follows a named voice takes that voice's rhythm whatever the track is
called - the way to put a second verse or language under one voice (`lyrics en on
alt`). Inside a staff group a row with `on` may stand anywhere, on its own band; a row
without `on` needs a plain staff directly above it (LYS6012), and the same words on the
same part twice in one group are warned about (LYS6015).

Two track names need no `lyrics { }` block, being made from the notes: `singer female
sings hum on sop` hums, and `singer female on alt sings solfege` sings the note names
(movable do, in the file's `language`). The score row `lyrics solfege` prints the names
under the notes; `lyrics hum` is an error (LYS4050), there being nothing to print.

A part may name its words in its header: `part sop { instrument soprano  lyrics words }`,
no braces, one statement a track. `singer female on sop` then sings them with no name
(several named: write the name, LYS4054). A track the part does not list, on a `singer` line or
on a row for that part, is warned about (LYS4055). The words themselves stay outside the part
(`lyrics NAME { … }` in a part is LYS4049), and `on` belongs to a score row, never to a definition.

A `lyrics NAME { … }` track sits in a section next to its part; the score
places it with a `lyrics NAME` row under the staff. Syllables are separated by
spaces; `-` joins syllables of one word; `|` mirrors the music's barlines. Barlines
follow the music rule: every written `|` closes one bar, the one that OPENS the run
included, so `| きら | ひかる |` is one bar longer than `きら | ひかる` — that leading
`|` is how a verse skips the rest bar the melody opens with.
**Melismas are LilyPond's**: a SLUR or a TIE holds its first syllable over every note it
covers by itself (`c4( d e) f` with `la lu` puts `lu` on f — do NOT add markers for the
slurred notes). `__` only draws the extender line and takes NO note; `_` takes ONE note
with no syllable (a melisma without a slur: `c4 d e f` with `la _ lu li`) and draws the
extender line under it; a standalone `~` takes one note the same way with NO line.

**Two syllables under ONE note (an elision)** — a `~` GLUED between them, no spaces:
`Glo- ri~a in ex- cel- sis` puts "ri" and "a" on one note and prints them joined by an
undertie, ri‿a (`e~il`, `de~i`, `きょ~う` the same; more than two: `wa~o~a`). It is one syllable
to the count — one note — and may still take its hyphen (`ri~a-`). MusicXML writes an
`<elision>`, the LilyPond twin the same undertie. A sung voice is handed the pair as one
syllable; the VOCALOID export writes both syllables' phonemes on the note. With spaces (`ri ~ a`) the `~`
is the melisma above, a note of its own.

**A reading for a kanji word** is glued to it in square brackets: `漢[かん]` prints 漢 and
sings かん (the preview's voice and `lysc wav --singer`); `大[おお]空[ぞら]` prints 大空 and sings
おおぞら. Write the reading in kana. The brackets must touch the word — `漢 [かん]` is an error
(LYS4036) — and a kanji without a reading prints but is not sung (the voice says so once).
Words are `japanese` unless a line `language english` says so for the file, or a track does:
`lyrics en language english { … }` (the names: japanese english chinese korean latin — Latin is for the page and the check: no voice Cantoria drives sings it yet, so a `singer` line on Latin words is not sung and its part's instrument plays; `lysc check --singer cantoria FILE` says which singer lines have a voice on this machine).
**Who sings is a top-level line of its own**: `singer "neutrino/MERROW" on melody sings words`. A `lyrics` row only prints; only a `singer` line sings, whether the page shows the words or not. The voice is `"engine/name"`, `male` or `female` (each machine's default voice of that gender), a gender before a name (`singer male "neutrino/SOMA" sings words`), or nothing (`singer sings words`: the part's own default voice). The order is fixed. **How the voice sings** may follow, on the same line: `with NAME VALUE NAME VALUE …` — `singer "diffsinger/Nishiren/2P" on melody sings words with breathiness 3 gender -0.2`. The names are the voice's own engine's (the editor lists what the line's voice takes, with each one's range); a value is a number, a word or a string; no commas. A name the voice does not take is not sung, and said. Leave `with` out and the voice sings as it does by itself — write it only for what you want changed. **Inside the music** the same names go on a note: `c4@sing(breathiness 6)` holds from that note on, as a dynamic does, until the next `@sing` for that name; `@sing(breathiness default)` gives it back to the singer line's value; `g2@sing(breathiness to 9)` arrives at 9 with that note, gliding from where the value before was written. Several settings in one: `@sing(breathiness 6 tension -2)`. Leave out `on PART` when the file has one part or every score prints the words under one part; leave out the name for unnamed lyrics. Each line is one voice, and several lines with the same words on one part sing it together; a part takes one set of words (LYS4044). A `singer` line inside a score replaces, in that score, the top level's lines of the same named singer (with no name: every line for its part), so one score can sing Japanese words and another English ones. A staff with no `sounds` is its singer's voice if it has one and its instrument if not; `staff m sounds both` (or `voice`, `instrument`) says so in that score, and `staff m hidden` sounds without being drawn. A part with no `staff` in the score does not sound.

```
part melody { lyrics words }
section Main {
  melody { c4 d e f | g2 g | }
  lyrics words { Hap- py birth- day | to you | }
}
form { Main }
score { staff melody  lyrics words }
```

**Verses** — the words for each time a section is sung — are `verse N { … }` blocks inside the
lyrics block: `verse 1 { up up up up | } verse 2 { down down down down | }`. Three rules and
nothing else: ⑴ one `lyrics` track is ONE line of words under the staff; more lines are more
tracks, placed in score order. ⑵ words written with no `verse` are sung every time the section
comes round. ⑶ `verse N { … }` is the words for the Nth time — where they go follows from the
form: under `|: B :|` (printed once) the verses stack under B in order, each with its number
`1.` `2.`; a section the form names again written out (`Verse Verse`) prints each verse at its
own printing, on the first line, with its number before it (`2.` before the second). `verse 1,3
{ }` / `verse 1-2 { }` cover several times; `verse ~1 { }` keeps the words and hides the number.
This is NOT a repeat ending — it never changes the order. (`[1. … ]` was this construct's old
spelling; `[` means nothing in lyrics now.)

```
part melody
section A { melody { c4 d e f | } }
section B {
  melody { g4 a b c | }
  lyrics words { verse 1 { up up up up | } verse 2 { down down down down | } }
}
form { A |: B :| }
score { staff melody  lyrics words }
```

## Lead sheet (chords and/or lyrics, no staff)

A `chords NAME { … }` part's symbols align above a staff by timing when its row
stands directly above that staff in the score (`chords prog` then `staff melody`)
- and the SAME `prog` can also be a lead-sheet row, written once. A nameless `chords { … }`
is the file's one chord track, named bare in the score (`chords`). An independent `chords NAME { … }` and/or `lyrics NAME { … }` part, placed in a
`score` with `chords NAME` / `lyrics NAME` (instead of `staff NAME`), renders WITHOUT
a staff: just a grid of measure barlines, the chord symbols between them and the
lyrics below. A chord entry is the SYMBOL as it prints — `C`, `Am`, `G7`, `F#m`,
`Bb7`, `Gm7-5`, `C/G`, and the chart's other spellings (`CΔ7`, `Cø7`, `C°7`, `C-7`, `CM7`,
`B♭7`, `C7(b9, #11)`, `Cm7(b5)`, `C7(9)`; see *Chord names* above) — with NO durations: a bar's entries divide it on the meter's
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
form { Main |: ~Loop :| }
score sheet { chords prog lyrics words }     // chords + lyrics rows, no staff
```

**The strum's rhythm — `strokes`.** A placed row HOLDS each chord over its slot in the MIDI (a
keyboard's reading). A guitar's strum is a `strokes` part: a rhythm and no pitch — VALUES alone
(`4 8 8 4 4`, each a slash head; rests, ties, `%` for the bar before) — which the row `plays`:

```
key d major
part singer { instrument alto }
strokes rhythm { section A { 4 8 8 4 4 | % | % | % | } }   // a note or chord in it is LYS2021
chords harmony { section A { D | A | Bm | G | } }
section A {
  singer { d'4 fis'8 a' d''2 | a'4 cis'' e'' cis'' | b'4 d'' e'' d'' | g'4 b'8 d'' b'2 | }
  lyrics words { ゆ う や け | み ち を き | と あ る い | ね よ る の | }
}
form { A }
score { chords harmony plays rhythm  staff singer  lyrics words }
```

`plays` is how the row SOUNDS — the MIDI strikes the chord in force at every stroke — and draws
nothing: the page is the lead sheet it was. To PRINT the strum, place the part: `strokes rhythm`
in the score draws it as a ONE-LINE staff of slashes (no clef, key or name), and a chords row
written above that item stands over it (`score { staff singer  lyrics words  chords harmony
plays rhythm  strokes rhythm }`). `lyrics strum { … }` puts words under the strokes,
one syllable a stroke — the way to place a verse's words when its tune is not written out.

**Roman numerals.** A chord entry may also be a DEGREE of the key in force at its bar —
`I` `IIm7` `V7` `IVmaj7` `VIIm7-5` `bVII` (`b`/`#` or `♭`/`♯`; the quality in any of its
spellings, `IIø7`, `V7(b9)`). After a `/`, a
NUMERAL is a secondary function's target, read in the major key on it (`V7/V` = D7 in C,
`IIm7/IV` = Gm7, `VIIdim7/V` = F#dim7), and a DIGIT 1-7 (with `b`/`#`) is the BASS as the
key's scale degree (`I/3` = C/E, `V7/7` = G7/B, `IV/b7` = F/Bb; `V7/V/#4` = D7/F#, bass
last). Either spelling is printed as names or as degrees by the SCORE: `chords prog` prints names,
`chords prog as roman` prints degrees. To show both, place the track twice (two rows):

```
key c major
part bass { instrument bass }
section Main {
  chords prog { C | Am | F | G7 | }       // prints C Am F G7 …
  bass { c1 | a | f | g | }
}
form { ~Main }
score {
  chords prog
  chords prog as roman                    // … and under it I VIm IV V7
  staff bass
}
```

The degrees follow the key: a `key` in the music re-bases every degree after it, and so
does the revert to the file's key at a section boundary. Write chord functions for teaching
this way rather than as lyrics.

## Structure: reuse and navigation

```
form { Intro Main Main "Main (reprise)" Coda }   // string = custom section label
```

A trailing `'` / `,` on a reference shifts THAT play's octave (one per mark):

```
form { Intro Main ~Main' Coda }
```

Navigation marks sit between section names. Signs `segno` / `coda` engrave at the start
of the following section; text directives `fine`, `to coda`, `dc`/`ds` (and `dc al fine`,
`ds al coda`) engrave at the end of the section just played.

```
form { A segno  B to coda  C ds al coda  coda D }   // the MIDI plays A B C B D
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
the guitar lines `@palmMute` ... `@!palmMute`, `@letRing` ... `@!letRing`, `@barre(7)` ... `@!barre`
(see the guitar marks above),
ottava `@8va` / `@8vb` / `@15ma` / `@15mb` ... `@!8va` (the label the page prints; `@ottava` / `@quindicesima` are retired)
(**the end is REQUIRED**, and **it repeats the name the span was opened with** — `@8va` ... `@!15ma` is
an error; `@loco` is retired),
trill spanner `@trillSpan` ... `@!trillSpan` (an ottava's and a trill's end goes on the LAST note
the line covers — `c4@8va d e f@!8va` moves f too),
pedals `@sustain` ... `@!sustain`, `@sostenuto` ... `@!sostenuto`, `@unaCorda` ... `@!unaCorda`
(`@treCorde` is the same release written as the word the Text style prints) — one word each,
LilyPond's own names, taking NO argument (`@ped`, `@ped(off)`, `@sost(off)`, `@una(corda)` do not exist).
A pedal CHANGE (release and re-press on the same note, LilyPond's `\sustainOff\sustainOn`) is
the start again while the pedal is down: `g,4@sustain` — the bracket draws its notch there, and
the one `@!sustain` at the end closes the span. `g,4@!sustain@sustain` means the same. How the span is
drawn is the PART's: `part lh { clef bass pedal text }` (`text` = "Ped. … *", `bracket` =
what an unset part draws — the same plain bracket for all three pedals, as LilyPond's bracket style — or `mixed` = text at the start, bracket for the hold; `text` and `mixed` print "Sost. Ped." and "una corda" / "tre corde").

```
d,4@sustain a, d a, | g,4@sustain d g d | a,1@!sustain |
```
Phrasing slur `@phrasingSlur` ... `@!phrasingSlur` (LilyPond's `\(` ... `\)`; **the end is REQUIRED**):
the long curve over a musical sentence, drawn over the ordinary slurs `( )` inside it. One is open
per voice at a time — they do not nest.
```
c'4@phrasingSlur d'( e') f' | g'( a') b' c''@!phrasingSlur |
c'4@phrasingSlur(dashed) d'( e') f' | g'( a') b' c''@!phrasingSlur |  // DASHED: the word on the OPENING mark only
c'4@phrasingSlur(dotted) d' e' f'@!phrasingSlur |                     // dotted; a side goes after it: @phrasingSlur(dashed).up
```
The word is `dashed`, `dotted` or `solid` (anything else: LYS4074, and the curve is drawn solid); the slurs
inside stay solid. Only a word glued to the `(` is read — `c'4@phrasingSlur( d' e')` is, as always,
a phrasing slur AND a slur starting on that note.
An annotation's argument always goes in PARENTHESES — a dot after the name is the placement qualifier
instead (`@fermata.up`), so `@notehead.x` does not work either.
(The navigation marks above are the bare form — `ds al fine`, no `@` — in a form and in music alike.)

## Multiple forms (excerpts)

The unnamed `form { }` is the file's default; a score plays another with a `form NAME`
item. A score's name is its own, a bare word (`score practice`, never `score "practice"`):
the unnamed score writes to the input file's name and is `(Default)` in the preview,
`score NAME` writes `<input>-NAME`.

```
form { Intro Verse Outro }
form practice { Verse }
score { staff melody }
score practice { form practice staff melody }
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

## Common requests — which construct answers them

What an everyday request asks for, in the construct Lily# has for it. Each example is a whole
file; the lines a request adds are marked `// added`. Octaves are relative unless the file
says `octave absolute`, and ⚠️ **every section starts its chain afresh**: its first note is
read as if absolute (`c` = C3, `c'` = middle C, unless the part writes `octave N`) — write the
first note of each section's music with the marks that put it where it belongs.

**"Add chords" / "harmonize with chords"** — a CHORD TRACK, not `@chord(…)` on the notes: a
`chords NAME { … }` block in each section beside the part (symbols as they print, no
durations; a bar's entries split it on the beat grid), and a `chords NAME` row directly above
that part's staff, which aligns the symbols with it. If the file already has a chord track,
write into it. `@chord(…)` on a note is only for a name or a fretboard diagram asked of that
note.

```
key c major
part melody { clef treble }
section A {
  melody { c4 d e f | g2 g | a4 f c' a | g1 | }
  chords harmony { C | C G7 | F | C | }    // added
}
form { ~A }
score {
  chords harmony                           // added, directly above its staff
  staff melody
}
```

**"Add lyrics"** — a `lyrics NAME { … }` track in each section beside the part,
one syllable a note (`-` joins a word's syllables; a slur holds one syllable over its notes),
and a `lyrics NAME on PART` row directly below the part's staff. See *Lyrics*.

**"Add a second verse" / "the words for the second time"** — `verse N { … }` blocks in the SAME
track, not a second track and not a repeat ending: `verse 1 { … } verse 2 { … }` in the section's
lyrics, and the form sings the section twice (`|: A :|` stacks the verses under it; `A A` prints
each at its own time). Never `[1. … ]` in lyrics (it is not a verse) and never the music written out again.

```
part melody { clef treble }
section A {
  melody { c4 d e f | g2 g | }
  lyrics words {
    verse 1 { Twin- kle twin- kle | lit- tle | }   // added: the verse blocks
    verse 2 { How I won- der | what you | }        // added
  }
}
form { |: ~A :| }                                  // added: the repeat that sings it twice
score { staff melody  lyrics words }
```

**"Make the chords strum" / "a guitar rhythm for the chords"** — a `strokes` part (values alone,
no pitch) that the chord row `plays`; it changes the MIDI and draws nothing. Only when the strum
is asked for on the page, a `strokes NAME` item in the score (a one-line staff of slashes).

```
part melody { clef treble }
strokes rhythm { section A { 4 8 8 4 4 | % | } }       // added: the strum, one bar then "the same"
chords harmony { section A { C | G7 | } }
section A { melody { e4 d c d | e2 e | } }
form { ~A }
score { chords harmony plays rhythm  staff melody }    // added: plays — the row sounds it
```

**"Add a bass line" / "add a part for …"** — three places: the `part` declaration, its music
in EVERY section (as many bars as the other parts there), and a `staff` row in each score
that should show it. A piano accompaniment is two parts in a `grandStaff { … }`.

```
part melody { clef treble }
part bass { instrument bass }              // added: the bass clef, its sounding an octave down, and its sound come with it
section A {
  melody { c4 d e f | g2 g | }
  bass { c2 g | c2 e | }                   // added
}
section B {
  melody { a'4 f c' a | g1 | }             // a' = A4: the section starts its chain afresh
  bass { f,2 a | c1 | }                    // added
}
form { ~A ~B }
score {
  staff melody
  staff bass                               // added
}
```

**"Add a second voice" / "harmony on the same staff"** — `voice { … } { … }` inside the
part's music, every line covering the same bars. A line for ANOTHER instrument is a new part
(above), not a voice.

```
part melody { clef treble }
section A {
  melody {
    voice { e'4 f g a | g1 | }             // the melody, stems up
          { c4 d e f | e1 | }              // added: the lower line, stems down
  }
}
form { ~A }
score { staff melody }
```

**"Add tab" / "guitar tab"** — the part names a fretted `instrument` (`guitar`, `bass`,
`ukulele`, …) and the score gets a `tab NAME` row under its staff. The tab chooses strings and
frets itself: write a string number (`\4`) only where a particular string is asked for. Two notes
of a chord written on one string warn (LYS1063): `<e'\1 b\2>`, not `<e' g'>\1`, which numbers both.
One pitch twice in a chord warns too (LYS1064), unless on two strings: `<e'\1 e\2>`.
A `@glissando` / `@slide` keeps its two notes on one string where one string plays both (a
written `\N` still decides); a slur does not. A string the tuning does not have (`\7` on a
guitar) warns and is chosen again (LYS5003). The tab's tuning is the part's (`part g { tuning
guitarDropD }`, which also moves the part's range: its low D is in range) or the word BEFORE the
part in the score — `tab guitarDropD g`, never `tab g tuning guitarDropD`. Under the file's
`capo N` the frets count from the capo (see `capo`). Everything a tab does is in *Tablature*.

```
part guitar { instrument guitar }          // added: instrument guitar
section A {
  guitar { e4 g a b | d2 b | a4\4 b c d | e1 | }
}
form { ~A }
score {
  staff guitar
  tab guitar                               // added
}
```

**"Repeat this" / "first and second endings"** — the FORM, not copied music: `|: … :|`
around the sections that repeat, `[1. …]` / `[2. …]` for the endings, each ending its own
section.

```
part melody { clef treble }
section A { melody { c4 d e f | g2 g | } }
section E1 { melody { a'4 g f e | d1 | } }
section E2 { melody { b'4 a g f | c1 | } }
form { |: A [1. E1] :| [2. E2] }
score { staff melody }
```

**"Transpose up/down …"** — rewrite the notes, the `key` lines and the chord symbols. The part
property `transpose` is NOT this: it says how a transposing instrument (B♭ clarinet, …) is
printed. The first note of each section may need new octave marks.

**"Make it triplets"** — `tuplet 3/2 { c8 d e }` (three in the time of two), keeping each bar's
length. **"Add dynamics / articulations"** — marks glued to the note: `c4@p`, `c4@cresc … g@f`,
`c4@staccato`, `c4@accent`. **"Change the meter / tempo"** — at the top (`time 3/4`,
`tempo 96`), or at the start of a bar, in a section header or in the music, from there on.

**"Compose a piece"** — a new piece is a whole file. What makes it music for its players (form,
harmony, theme, parts, the kinds of music, the setting of words) is the composing guide, read
with the `lilysharp_readGuide` tool, topic `composing` (`docs/COMPOSE_FOR_LLM.md`).

## Rules and gotchas

- **A part of a known instrument names it with `instrument` and writes no `clef`
  beside it.** The preset brings its clef — the cello its bass clef, the viola the alto clef, the
  guitar its octave-down treble clef — and its sound and its range (see *Instrument ranges*); it
  does NOT move the octaves, which are LilyPond's in every part. So `part vc "Cello" { instrument
  cello }`, not `{ instrument cello clef bass }`, and a piano is `instrument piano-right` /
  `instrument piano-left`. Write `clef` only for a part no preset fits (a melody for no
  instrument in particular, drums' `clef percussion`). Leave the parts already in the file as they are unless asked to change them.
- **Clef words.** A part header's `clef` takes eleven: `treble` `bass` `alto` `tenor`
  `treble_8` (an octave lower: guitar, tenor voice) `treble^8` (an octave higher) `bass_8`
  `soprano` `mezzosoprano` `baritone` (the C clef on the first, second and FIFTH line)
  `percussion`. A `clef` change IN THE MUSIC, and the clef word of a score's `staff bass NAME`,
  take only `treble` `bass` `alto` `tenor` `treble_8`. ⚠️ `clef baritone` is the old C clef on
  the top line, not what a baritone reads: a baritone singer is `instrument baritone` (a
  `treble_8` clef, as a tenor's) or `clef bass`. No other name is a clef (`varbaritone`,
  `subbass`, `french`, `tab` are unknown — a tab is a score item).
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
  `pitch concert` and print the player's part with `score NAME pitch transposed` (see Pitches).
- **A section's label is hidden at the FORM reference only**: `form { A |: B [1. ~B1]
  :| [2. ~B2] }` plays B1 and B2 without a rehearsal letter. ⚠️ **Never write
  `section ~A { … }`** — a declaration takes no tilde (LYS0033). Write the `~` on every
  reference that should be silent (a section cut only to carry a repeat edge is usually
  referenced once). With no form, every section labels itself. An empty label `A ""` also
  suppresses, and a label written on a `~` play is LYS0012.
- **A label every play prints is declared once, at the top level**: `section A2 "A'" { }`
  (the body may be empty in a file grouped by part) makes every `form { A2 }` print A'; a
  reference's own `A2 "A''"` still wins and `~A2` still hides it. ⚠️ **Never write the label
  inside a part** — `part p1 { section A2 "A'" { … } }` is LYS0039.
- **A SECTION reference takes the same marks**: `form { ~A ~B' }` opens B's play an
  octave up, `~B,` an octave down, `~B''` two. They belong to the PLAY, so one section can
  be quoted at two octaves (`~B ~B'`) while the declaration never moves, and the next
  reference is back at the part's anchor. Both spellings take them (`B'` and `~B'` — the
  tilde hides only the label), a volta ending takes them (`[1. B']`), and they work in
  `octave absolute` too. This is how a section cut only to carry a repeat says its music
  belongs an octave away: the boundary's frame reset stays, and the carry is written down.
- Part header attributes are written **bare, no `=`**, like the top-level commands:
  `clef` `key` `octave` `instrument` `transpose` `transposition` `tuning` `pitch` `pedal`
  `beaming syllabic` (a sung part: see Ties, slurs, beams)
  and `midiInstrument "acoustic bass"` (a quoted General MIDI sound — what playback sounds
  like, overriding the preset's). ⚠️ `time` and `tempo` are NOT part attributes: every
  part shares them, so `part m { time 3/4 }` is an error — write them at the top level or
  in a section header. Override/revert use `=`.
- `staff NAME as removeEmpty true|all` in a SCORE hides that staff in systems where it only
  rests (hara-kiri). `true` keeps the first system (LP `\RemoveEmptyStaves`), `all` hides it
  too (`\RemoveAllEmptyStaves`); any playing voice keeps the staff visible. It chains with
  the line count after one `as` (`staff m as lines 1 removeEmpty all`) and is NOT a part
  property: the full score hides the empty systems, the part sheet of the same part never does.
- `staff NAME as pitch transposed|concert` prints THAT staff's transposing part for its player or
  as it sounds, over the score's `pitch` (see Pitches): `score { staff tp as pitch transposed
  staff bs }` under a top-level `pitch concert` is a sounding score with the trumpet in its
  player's key. Not on an `ossia` (it prints as its staff).
- Identifiers (parts, phrases, sections) may use any Unicode letters: `phrase 動機 { ... }`.
- Everything is **case-sensitive**: keywords, identifiers, and vocabulary values (clef /
  instrument-preset / tuning names, key modes) are written in their canonical (lowercase)
  case. `Treble` is a different, unknown symbol from `treble` and is an error — not a
  silent fallback.
## A text for the page (the ```` ```md ```` fence)

A performance note, a dedication, the words of the song, practice instructions: Markdown
between a line that begins ```` ```md ```` and a line that begins ```` ``` ````. Nothing between
the two lines is Lily# (`//`, `{`, `"` mean nothing there).

````
title "Morning Song"
composer "A. Composer"

```md box
# Performance note
Keep the tempo **slow**; the left hand never rises above *piano*.

- first time *mf*
- second time *p*
```

```md center
*for my choir*
```

```md end xMargin 30mm
## Words
Morning comes, the window opens.
```

part m { clef treble }
section A { m { c'1 | } }
form { A }
score { staff m }
````

**Where a fence may stand** — two places only:

| Written | Printed |
|---|---|
| at the top level, anywhere among the declarations: ```` ```md ```` | under the title (and the poet / composer line), above the first system |
| at the top level: ```` ```md end ```` | under the last system of the last page (on a page of its own after it when that page has no room) |
| directly inside `score NAME { … }` | the same two places, in THAT score only: it replaces the file's text for the place (an empty fence = no text there in this score) |

A text prints in every score with nothing written in `score { }`. A fence inside a part, a
section's music or a phrase is an error; a fence directly in a top-level `section` is read
and NOT printed yet (warned, LYS4058).

**The opening line's words** — after `md` (or `markdown`), in any order, each once:

| Word | Meaning |
|---|---|
| `end` | the place: under the last system instead of under the title |
| `box` | a thin ruled frame around the text |
| `center` | each line centred (headings too; a list keeps its shape at the left) |
| `xMargin 20mm` / `xMargin 20mm 10mm` | white at both sides / at the left and the right, inside the line width: the text is set narrower |
| `yMargin 2` / `yMargin 3 1` | white above and below / above, below. Default 1 staff space each; `yMargin 0` is LilyPond's own tight spacing |

A length is `20mm`, `2cm`, `0.5in` (the unit glued on) or a bare number of staff spaces. Any
other word, a word twice, a negative length, an `xMargin` that leaves under 10 staff spaces of
text, a `yMargin` over 50: LYS4057.

**Two fences for one place** print one under the other in written order, and EACH HAS ITS OWN
SHAPE — the boxed note and the centred dedication above. Between two blocks stands the upper
one's white below and the lower one's white above (`yMargin`). Two fences that write no shape
word are simply one text, the second its next paragraph.

**What is drawn**: paragraphs (a blank line between them), `**bold**`, `*italic*`, a line ended
by hand (two spaces or `\` at the line's end), `# Heading` (bold, the subtitle's size) and
`## Heading` (bold, one size smaller; deeper levels the same), `- bullet` and `1. numbered`
lists, nested by indenting. Lines break by themselves at the text's width — between words, and
between characters in Japanese, Chinese and Korean. **Read and NOT drawn, each warned where it
is written (LYS4058)**: tables, code blocks, block quotes, rules (`---`), images, HTML. A code
span's and a link's words are printed as plain words (the link's address is not).

There is no copyright or footer line: write it as an `md end` text.

**In a Markdown file**, a Lily# example that holds a page text is fenced with FOUR backticks,
because its own three would end a three-backtick block:

`````
````lys
title "Morning Song"
```md
Keep it slow.
```
part m { clef treble }
section A { m { c'1 | } }
form { A }
score { staff m }
````
`````

(Inside a `.lys` file the same rule holds one level down: a fence opened with four backticks
closes on four, so its text may write three.)

Not in MusicXML or MIDI. The LilyPond export writes the text as a `\markuplist`.

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

Attributes: quoted faces · `as serif|sans` · `step ±n` (relative, magstep 2^(n/6), −12..+12)
· `size n` (absolute em in staff spaces, 0.5..20; prefer `step`) · `bold` `italic` `regular` (combine;
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

## Music font (`fonts { music "…" }`)

The glyphs — noteheads, clefs, accidentals, rests, dynamics — come from the `music` entry
of the same `fonts { }` block:

```
fonts {
  music "Bravura"                 // Emmentaler (LilyPond's own) when not written
}
score sketch { fonts { music "Petaluma" }  staff melody }   // one score in another font
```

- Bundled: **Emmentaler** (the default), **Bravura**, **Petaluma** (handwritten, jazz),
  **Leland** — the last three are SMuFL fonts. The name is compared without regard to case.
- Several names are a per-glyph fallback chain, most preferred first:
  `music "Petaluma" "Bravura"`. A glyph no named font has is drawn in Emmentaler, with a
  warning once per glyph (Leland lacks the figured-bass digits, for one).
- ⚠️ `music` takes **quoted names only**. `step`, `size`, `as` and a style on it are errors:
  a glyph's size is its grob's (`layout { NoteHead.scale 1.2 }`).
- Another SMuFL font is found by NAME — a score never names a file — in the folders of the
  VS Code setting `lilysharp.fontPaths`, then `LILYSHARP_FONT_PATH`, the SMuFL metadata
  location and the system font folders. A name found nowhere warns (LYS8019) and the next
  name, or Emmentaler, is used.
- Under a music font, chord symbols use the font's own ♭ ♯ △ ° ø + glyphs, and under
  Petaluma their letters are set in Petaluma Script — unless `chord` (or `chords`, or `sans`)
  names a face, or `chord as serif` / `as sans` says which family to follow.

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
  breaksOnly               // bare flag: break lines/pages only at the written break / pageBreak
  spacingIncrement 1.2     // horizontal note-spacing unit (staff spaces)
  staffSpace 1.5mm         // THE STAFF'S SIZE: the distance between two staff lines (default 1.757299mm,
                           // a 20pt staff). Smaller = smaller staves, more music on the page
  systemsPerPage 4         // exactly 4 systems on every page (or minSystemsPerPage / maxSystemsPerPage)
  measuresPerSystem 4      // exactly 4 bars on every system
  systemSystemSpacing { basicDistance 12  minimumDistance 8  padding 1  stretchability 60 }
  staffStaffSpacing   { basicDistance 9 }   // staves of a group
}
```

Rules worth knowing before emitting one:
- Scalar keys: `paperWidth paperHeight leftMargin rightMargin topMargin bottomMargin
  indent shortIndent spacingIncrement shortestDurationSpace staffSpace systemsPerPage
  minSystemsPerPage maxSystemsPerPage measuresPerSystem`. Flags: `raggedRight raggedBottom breaksOnly`. Spacing
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
  pass). There is no algorithm switch.
- **Smaller staves / how much goes on a page** — three keys:
  - `staffSpace 1.5mm` is the staff's size (LilyPond's `set-global-staff-size`): the distance
    between two staff lines. The default is `1.757299mm` (a 20pt staff); `1.493704mm` is a 17pt
    staff, about `1.2mm`–`1.4mm` suits a full score of 12–20 staves on A4. It NEEDS its unit
    (`mm`, `cm`, `in`). The paper, the margins and every length written with a unit keep their
    size; the staves, notes and text get smaller on it, so more bars fit a line and more
    systems a page.
  - `systemsPerPage 4` puts exactly 4 systems on every page (the lines are re-broken to fill
    them); `minSystemsPerPage 3` / `maxSystemsPerPage 5` are a floor and a cap instead. More
    than a page can hold are pressed together, with a warning.
  - `measuresPerSystem 4` puts exactly 4 bars on every system (a lead sheet's layout).
  ```
  paper { size a4  staffSpace 1.3mm }            // a big score: smaller staves
  ```
  A page that warns "page N is over-full … its systems were pressed together" wants a smaller
  `staffSpace` (or fewer staves: `removeEmpty`), not spacing overrides.
- Unknown keys are an error; a key set twice is a warning (last wins).
- **Named blocks, per score** — same shape as fonts: `paper wide { paperWidth 250mm }`
  at the top level, then `score { paper wide staff melody }`, or
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
  sectionLabelAlign left   // a mid-line section name on its bar line: center (default) | left
  partCombineText false    // a2 / Solo words: true (default) | false
  chordQualities words     // chord quality: symbols (default) | words
  minorChords lower        // a minor chord's root: upper (default) | lower
  ottavaLine wave          // an ottava's line: dashed with a hook (default) | wave with no hook, as in old prints
  restStyle z              // the quarter rest: default | classical (a mirrored eighth rest) | z (an old French print's)
  chordDiagrams all        // which chords draw a diagram: written (default: those with a written or listed shape) | none | all
  chordTuning ukulele      // the tuning for chords no fretted part owns: a tuning word (a fretted part: its own; unset: guitar)
                           // the shape table and the capo are TOP-LEVEL lines, not layout keys:
                           //   chordShapes { Cm7 x35343  G  section B { C x35553 } }   the listed chords draw, wherever named
                           //   capo 3                                                  pressed shapes, pressed names, "Capo 3" at the head
  chordNames both          // under a capo a name shows: shape (default, the pressed chord's) | sounding | both "E♭ (C)"
  chordList true           // the chords the score uses, each with its diagram, under the title: true | false (default)
  voltaBracket line        // how far an ending's bracket reaches: all (default) | line | N bars
  compressRests true       // R1 | R1 | R1 written apart engrave as one rest with its count (a part sheet): true | false (default)
}
```

- `markTempo stacked` is LilyPond's: the boxed label over the tempo, each on its own anchor.
  `markTempo beside` is the chart's one line, "[Chorus] ♩ = 132" — the label's box at the
  line-start edge, the tempo to its right on the label's baseline. A mid-line label and a mid-measure `tempo` are
  unmoved either way.
- `barNumbers lines` is LilyPond's default: the first bar of every line after the first.
  `none` prints no numbers (`\remove Bar_number_engraver`). `every N` prints every bar
  whose number is a multiple of N wherever it stands, and ONLY those — a line opening on
  bar 3 under `every 2` opens with no number, as LilyPond's `every-nth-bar-number-visible`
  prints it. N is a whole number of at least 1.
- `accidentals default` is the 18th-century style Lily# has always drawn: an alteration
  holds to the bar line, in its own octave. `modern` is Kurt Stone's — cancelled in other
  octaves and in the next measure too, and with no restore-natural. `modernCautionary`
  prints the accidentals `modern` ADDS in parentheses. `forget` remembers nothing (every
  note is read against the key signature). `noReset` never forgets. LilyPond's
  `\accidentalStyle` table, for the styles whose context is the staff; its voice / piano /
  choral families name a context a score-wide switch cannot, and are not offered.
- `sectionLabels boxed` is the frame Lily# has always drawn (Lily#-own: LilyPond's
  SectionLabel grob draws the bare string). `plain` drops the frame and engraves the name
  alone, which is LilyPond's own picture. `none` engraves no section names at all — the part
  sheet's answer. The form still plays the section.
- `sectionLabelAlign center` centres a mid-line section name on its bar line, half of it over
  the end of the bar before (LilyPond's RehearsalMark, as the twin writes it). `left` puts its
  left edge on the bar line, clear of the bar before — LilyPond's own SectionLabel — so a mark
  over the last note there (a bend's "full", a fermata) stands beside it, not under it. A name
  that opens a line stands alike either way.
- `partCombineText true` prints `a2` / `Solo` / `Solo II` on a `combinedStaff` (LilyPond's
  default); `false` is its `printPartCombineTexts = ##f`. The merging
  itself is unchanged — this is the words, not the combining.
- `chordQualities symbols` is the default and LilyPond's own picture: the four qualities its
  exception table names print `C°`, `C+`, `Cø`, `C°7`, and a major seventh prints its drawn
  triangle. `words` spells them out instead — `Cdim`, `Caug`, `Cm7♭5`, `Cdim7`, `Cmaj7` — the
  lead-sheet convention. Every other quality is the same word either way.
- `minorChords upper` is LilyPond's default: `Am`, `Am7`. `lower` lowercases the root of a
  chord with a MINOR THIRD and drops its `m` — `a`, `a7` — which is LilyPond's
  `chordNameLowercaseMinor`. The slash BASS keeps its capital
  (`a7/C`), as LilyPond's does, and `Caug` (a major third) never lowercases. With both keys
  set Lily# spells these chords exactly as LilyPond prints them. `maj7` is NOT switchable:
  LilyPond draws it as a raised triangle, which Lily#'s one-line chord name has no home for.
- `restStyle` is LilyPond's `Rest.style`, the whole score's: `classical` draws the quarter rest
  as a mirrored eighth rest, `z` as a z with a knob at each end (old French prints). Only the
  quarter rest's shape changes — not its value, not the other rests, not the MIDI or MusicXML;
  the spacing follows the glyph's own width, as LilyPond's does. There is no per-note form.
- `chordDiagrams written` (the default) draws a diagram where a chord has a shape — written at
  it, or listed in the file's `chordShapes { }`; `all` makes EVERY chord name draw: its written
  or listed shape, else the usual one (LilyPond's predefined, else Lily#'s first; a chord with
  no shape on the tuning warns once per symbol and tuning); `none` draws no diagram at all,
  even where a shape is written — the same source makes a piano score and a guitar score. One
  word; the old `chordDiagrams guitar capo 3 all { … }` (a tuning, a capo, a table on this key)
  is an error that names the new spelling, and the editor rewrites it.
- `chordTuning ukulele` (or `guitar`, `mandolin`, `guitarDropD`, … — the words a tab's
  `tuning` takes) is the tuning the chord diagrams no fretted part owns draw on (a piano's, a
  voice's, a lead-sheet row's). A fretted part's chords always take its own instrument (an
  `@chord`'s or item's part, the staff a row stands above); unset, the rest take the guitar.
- The shape TABLE and the CAPO are the piece's data and stand at the TOP LEVEL, once each,
  not in `layout { }`: `chordShapes { Cm7 x35343  G  section Chorus { C x35553 } }` lists the
  chords that draw wherever they are named — each entry a chord symbol and the shape(s) a row
  writes after its symbol, a name alone the usual shape; a `section NAME { … }` block's entries
  apply to the chords written in that section, the rest to the whole piece. Strongest first:
  the shape written at the chord, the section's entry, the song's, then (under `all`) the usual
  shape; an entry whose shapes fit no tuning of the score is not used there. A bad symbol or
  shape in the table and a chord listed twice (the last wins) are warnings, the rest stands; a
  section nothing declares warns. The shape rules are under Annotations (*Chord diagrams*).
  `capo N` (1–11) puts a capo on fret N: the music still writes the SOUNDING chords (`Eb`),
  every shape is the shape pressed above the capo (`Eb(x32010)` is the C shape, checked against
  the pressed chord), the printed name is the pressed chord's (`C`), spelled in the key N
  semitones below the key at the bar, "Capo N" stands at the head of a score that prints
  diagrams or pressed names, and a `chord(Eb x32010)` / `diagram(Eb x32010)` item sounds N
  semitones higher in every score. There is no `capo auto`. NOTES under a capo are written,
  like the chords, at the pitch they SOUND (the staff and the MIDI are what is written); a
  `tab` counts its frets FROM the capo — 0 is the capo, as on the diagrams — so with `capo 2`
  a sounding `fis,` is fret 0 of the sixth string and `diagram(D x32010)` shows x32010 on the
  tab too. A note below the capo on every string (an open low `e,` under `capo 2`) cannot be
  played: it warns by name (LYS5002) and is left off the tab. The capo is the FILE's, one for
  every fretted part — `part g { capo 2 }` is not a property (a bass's tab alone ignores the
  capo and counts from the nut).
- `chordNames shape` (the default) names the pressed chord under a capo, `sounding` the
  sounding chord, `both` both — `E♭m7 (Cm7)`. Without a capo all three print the same name.
- `chordList true` puts every chord the score names at its head under the title, each once in
  order of first appearance with the diagram it draws (its usual shape when it draws none), in
  centred rows of even counts; `chordDiagrams none` lists the names alone.
- `voltaBracket all` (default) draws an ending's bracket over every bar; `line` stops it at
  the end of the system it starts in; `N` covers the ending's first N bars (all of it when
  shorter). An ending overrides it with `[1. B C]@voltaBracket(2)`. A cut bracket ends
  straight.
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
(`voice lyrics chords strokes section part phrase form score tab staff ossia grandStaff staffGroup
choirStaff condensedStaff combinedStaff layout paper fonts grace acciaccatura appoggiatura afterGrace
cue key time tempo partial override revert once using title subtitle composer poet segno
fine coda dc ds al to break noBreak pageBreak noPageBreak`, and `as` — a score's `staff X as …` word) — so
`part p`, `part bass`, `part q` are fine (2026-10-03). A PHRASE name is referenced bare in
music, so it is an identifier, a clef word (`treble bass alto tenor`) or a dynamic word
(`ppp pp p mp mf ff fff`; `f` is the note F, so not `f`); any other keyword is LYS1030.
Keywords:

```text
section form using tab ossia transpose shift octave pitch instrument percussion drummap
score part strokes staff grandStaff staffGroup choirStaff condensedStaff combinedStaff
voice phrase repeat break noBreak pageBreak noPageBreak partial cue embedded fonts paper layout
title subtitle composer poet tempo time key clef
major minor ionian dorian phrygian lydian mixolydian aeolian locrian
treble bass alto tenor treble_8 bass_8 soprano mezzosoprano baritone
tuplet grace acciaccatura appoggiatura afterGrace lyrics chords tuning
override revert once
segno fine coda dc ds al to
ppp pp p mp mf ff fff   (f is a PITCH; @f still works - dynamics resolve from text)
```

Also special: single letters `a`-`g` are pitches; `r`/`R`/`s` are rests. Reserved IN
MUSIC only: `q`, `chord` and `diagram` (`chord(C x32010)`, `diagram(C x32010)`) and the drum names (`bd` `sn` `hh` …) — a
phrase cannot be named them; a part can. In a score, `tab X Y` is part X's tab plus the
MIDI-only part Y when the file declares a part named X, else the X tuning over part Y
(`tab bass click` beside `part bass`: the part wins, LYS1044 names `tab bass4 click` for
the tuning). A tab in another instrument's tuning says the octave it is PLAYED in after its
`as`, beside the style, each once: `tab bass5 gt as 8vb numbers` frets a guitar line an octave
lower on a five-string bass (`8vb` `15mb` `8va` `15ma`); only the tab moves, never what the part
sounds. Articulation,
ornament, dynamic-text and mark NAMES (`staccato`, `tr`, `mordent`, `cresc`, `dim`, …) are
NOT reserved — they are resolved from the `@name` text — so they remain free for your own
identifiers.
</content>
