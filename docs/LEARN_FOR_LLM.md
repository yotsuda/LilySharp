# Teaching Lily# — the path, for an AI asked how to write `.lys`

Lily# is a text language for music: a `.lys` file is engraved, played and checked as it is typed, in
the Lily# Studio extension of VS Code. It is NOT LilyPond: no `\relative`, no backslash commands.

## How to teach from this

- **Answer in the person's language.** Code, key names and command names stay as they are written here.
- **A question from the beginning** ("how do I write this?", "teach me Lily#"): start at Stage 1. **A
  question on one topic** ("how do I add lyrics?"): go to its stage (the list at the end), answer
  it, and say which stage comes next.
- **One stage a message**, in this shape: what it gets them, the smallest example, one thing to try
  in their editor, then offer the next stage. Do not pour out several stages, or the grammar.
- **The order is the order of use**: hearing a score and letting the editor write come before any
  syntax, because they are what a person uses most and they teach the rest. Keep it unless asked.
- **Use the examples as they are.** Each is a whole file that compiles with no warning. For
  anything past them read the grammar (`lilysharp_readGuide`, topic `grammar`) before you write it.
- **Do not change their file unless they ask.** An example they want to try goes in a new file.
- **A lesson** is a playable score with its tasks ringed in red in the preview. In a NEW `.lys` file,
  type `lesson` at the top level and press Ctrl+Space, and pick it: it replaces the whole file, asking
  first if the file has anything in it. Offer the stage's lesson where one is named. The same
  lessons are on the Learn pages of the Lily# site.
- On macOS, Cmd for Ctrl in Ctrl+Space, Ctrl+K and Ctrl+I; the stepping keys stay Ctrl+Shift.

## Part I — Using it, before any syntax

### Stage 1 — See it and hear it

**Gets them:** a score on the screen, sounding, in a minute.

- File → New File… → Lily# Score (.lys), or the command **Lily#: New Score**. A tune opens with its
  title selected, and the engraved score opens beside it. Not there: Ctrl+K, then V.
- The score redraws as they type, not when they save.
- ▶ at the top of the preview plays it. Hold **Alt+P** for the note under the caret; **Alt+M** plays
  its bar. The note under the caret lights up in the score, and sounds.
- A mistake gets a squiggle: rest the mouse on it to read what is wrong. **Ctrl+.** fixes it where
  there is one obvious repair.

**Try:** add a bar, `e4 d c2 |`, before the closing brace of the section. **Lesson:** `lesson-01-completion`.

### Stage 2 — Let the editor write it

**Gets them:** most of a score without remembering any syntax.

| Keys | What they do |
|---|---|
| Ctrl+Space | lists what can come next: words, names, chords, marks |
| Ctrl+Shift+Up / Down | a note to the next note of the key, up or down, and it sounds; also a dynamic, a clef, a key, a tempo, a time |
| Ctrl+Shift+PageUp / PageDown | a note an octave up or down; the notes after it follow |

**The caret can be anywhere on the note** (at its start, in its name, after its length). These keys land where
they belong, so never tell them to move the caret first, or to type "right after the note":

| Key | Lands | The caret |
|---|---|---|
| a digit | as the length: on `d4`, `2` makes `d2` | stays |
| `'` or `,` | as an octave mark, after the name | stays |
| `#` | as a sharp, into the name: on `c4`, it makes `cis4`; on `ces4`, `c4` | stays, or goes on from the end of the name |
| `.` | as a dot, after the length | stays |
| `@` | after the length, and lists every mark: `@mf`, `@staccato`, `@fermata` … | follows |
| `\` | as a string number for tab, ready for its digit | follows |
| `~` | as a tie, at the end of the note | follows |
| `(` | as a slur over this note and the next | follows |
| `[` | as a beam over this note and the next, on an eighth or shorter with one beside it in the bar; on a longer note it does nothing | follows |

**One goes by where the caret is:** `<` wraps a note in a chord from just before it or from inside it, not from after it.

**On what:** a rest or a slash `/` has no pitch, so `'`, `,` and `\` are only typed where the caret is, and `#` does nothing; the digit,
`.`, `@`, `~` and `(` work as on a note. `[` works on a slash, and does nothing on a rest. From outside a chord's
brackets the table applies to the whole chord, except `\`, which is only typed there. Inside the brackets, on a note
of the chord, a digit, `.`, `(` and `[` are still the whole chord's: on `<c e g>`, `4` makes `<c e g>4`. There `'`,
`,`, `#`, `\`, `@` and `~` go on that one note; from outside the brackets `#` does nothing. A digit after a space in the brackets is a scale degree.

**With notes selected:** `(` or `)` makes a slur from the first to the last, `[` or `]` a beam, and `<` or `>` one chord of them, if they
have one length: `c4 d e` becomes `c4( d e)` or `<c d e>4`. Other keys replace the selection, as in any editor.
In a `score`, select two or more `staff` rows, press Ctrl+Space and pick `grandStaff` or another group: the rows move inside it.

**Try:** put the caret on a note and press Ctrl+Shift+Up. Then, with the caret still on it, type `@` and pick `mf`.

### Stage 3 — Have it done for them, and get it out

**Gets them:** the jobs that are not writing notes.

- **Ask AI** (Ctrl+I in a `.lys` file): say what to change; a selection is a hint of where. The result is
  shown beside the score, to accept or reject.
- **In this chat**: "compose a waltz for flute and piano", "review this score", "add chords to this", or
  "read this PDF into a score". You do these with Lily#'s tools.
- **Export** from the command palette (Ctrl+Shift+P): **Lily#: Export as PDF**, SVG, PNG, MIDI,
  MusicXML, LilyPond, or WAV.

**Try:** in the chat, ask for a four-bar tune, and press ▶.

## Part II — Writing music

### Stage 4 — Notes, and the shape of a file

**Gets them:** a score they can read and write by hand.

```lys
title "My Tune"

part melody { clef treble }

section A {
  melody { c'4 d e f | g2 g | a4 f c' a | g1 | }
}

form { ~A }

score { staff melody }
```

- A note is a letter and a length: `c'4` is C, a quarter note. `1` whole, `2` half, `4` quarter, `8`
  eighth, `16` sixteenth. No number: the same length as before.
- `|` ends a bar. Write it at the end of every bar.
- **Octaves**: each note goes to the octave nearest the one before it. `'` is one higher, `,` one
  lower. The first note of a section is read as it stands: `c` is the C below middle C, `c'` is middle C.
- **The file**: `part` is who plays, `section` is music for a stretch of time, `form` is the order the
  sections play in (`~` hides the section's printed label), `score` is what is printed.

**Try:** change `g1` to `g2 e`. **Lesson:** `lesson-02-notes`.

### Stage 5 — Rhythm and meter

```lys
time 3/4

part melody { clef treble }

section A {
  partial 4
  melody { g'4 | c2 d4 | e4. d8 c4 | r4 e8 f g4~ | g2. | }
}

form { ~A }

score { staff melody }
```

- A dot lengthens a note by half: `e4.`. `r4` is a quarter rest; `R` rests a whole bar in any meter.
- `~` ties a note into the next. `time 3/4` at the top sets the meter, `tempo 96` the speed.
- `partial 4` opens the section with a one-beat pickup.

**Try:** change `e4.` to `e4` and read the squiggle: the bar is now short. **Lesson:** `lesson-03-rhythm`.

### Stage 6 — Sharps, flats and keys

```lys
key g major

part melody { instrument flute }

section A {
  melody { g'4 a b c | d2 fis | e4 ees d c | b1 | }
}

form { ~A }

score { staff melody }
```

- Sharp is `is`, flat is `es`: `fis` is F♯, `bes` is B♭, `ees` is E♭. Typing `#` on a note writes the `is`. A flat
  has no key of its own: `bb` is marked, and Ctrl+. writes `bes`. `cb` is a cowbell, so it warns unless the book has drums.
- **A note's name is its pitch; the key never changes it.** In G major, F♯ is still written `fis`, and
  `f` is F natural, drawn with its sign. The key only says what is printed.
- `instrument flute` brings the clef, the sound and the range: name the instrument, and write no
  `clef` beside it. Ctrl+Space lists them.

**Try:** on `key g major`, press Ctrl+Shift+Up: only the signature moves. **Lesson:** `lesson-04-keys`.

### Stage 7 — Expression

```lys
part melody { clef treble }

section A {
  melody { c'4@p( d e f) | g2@cresc a | b4@f@accent c@staccato b a@decresc | g1@p@fermata | }
}

form { ~A }

score { staff melody }
```

- Marks follow the note with `@`, with no space: `@p`, `@f`, `@accent`, `@staccato`, `@fermata`.
- `@cresc` and `@decresc` run to the next level. `( … )` is a slur.
- Some marks need an end: `@rit` … `@!rit`, the sustain pedal `@sustain` … `@!sustain`.

**Try:** type `@` with the caret anywhere on a note to see them all. **Lesson:** `lesson-05-expression`.

### Stage 8 — Form and repeats

```lys
part melody { clef treble }

section Verse { melody { c'4 d e f | g1 | } }
section First { melody { a'4 g f e | d1 | } }
section Second { melody { b'4 a g f | c1 | } }

form { |: Verse [1. ~First] :| [2. ~Second] }

score { staff melody }
```

- **Repeats go in the form, not in the notes**: `|: … :|` repeats, `[1. X]` and `[2. Y]` are endings.
- A section that comes back is named again: `form { A B A }`. Do not copy its music.
- One section name is one stretch of time: words, chords and other parts join by writing the same name.

- `segno`, `coda`, `fine`, `dc` and `ds` also go in the form, between section names. Ctrl+Space there lists them.

**Try:** change the form to `form { |: Verse First :|*3 Second }`. **Lesson:** `lesson-06-form`.

## Part III — Adding to it

### Stage 9 — Words

```lys
part melody { clef treble }

section A {
  melody { c'4 d e c | e f g2 | }
  lyrics words sings melody { Are you sleep- ing, | Bro- ther John? | }
}

form { ~A }

score {
  staff melody
  lyrics words
}
```

- One syllable a note. `sleep- ing` joins a word. `|` keeps the words in step with the bars.
- A row in the score, directly under the staff, prints it. Words with no note get a warning.

**Try:** add a word to the last bar, and rest the mouse on the squiggle. **Lesson:** `lesson-07-lyrics`.

### Stage 10 — Chords

```lys
key c major

part melody { clef treble }

section A {
  melody { e'4 g c g | a2 e | f4 a c a | g1 | }
  chords harmony { C | Am | F | G7 | }
}

form { ~A }

score {
  chords harmony
  staff melody
}
```

- A chord row: symbols as printed (`C`, `Am`, `G7`, `F#m`, `Bb`), no lengths. The chords in a bar share it.
- A chord as notes in a part is `<c e g>2`.

**Try:** press Ctrl+Shift+Up on `G7`: a guitar diagram appears. **Lesson:** `lesson-08-chords`.

### Stage 11 — Piano and voices

```lys
part rh { instrument piano-right }
part lh { instrument piano-left }

phrase opening { e'4 f g a | }

section A {
  rh { opening tuplet 3/2 { g'8 a b } c2. | voice { e4 f g a | } { c4 d e f | } }
  lh { c2 g | c2 g | <c e g>1 | }
}

form { ~A }

score { grandStaff { staff rh  staff lh } }
```

- `grandStaff` braces two parts as one player.
- `phrase` is music with a name, played wherever the name is written. The note after its name counts
  its octave afresh: give it its mark (`g'`).
- `tuplet 3/2 { g8 a b }` is three in the time of two. `voice { … } { … }` puts two lines on one staff.

**Try:** add `@sustain` to the first left-hand note and `@!sustain` to the last, and listen. **Lesson:** `lesson-09-piano`.

### Stage 12 — Drums

```lys
tempo 96

phrase hats { hh8 8 8 8 8 8 8 8 | }
phrase back { r4 sn r sn | }
phrase kick { bd4 r8 bd bd4 r | }
phrase groove { drums { hats back kick } }

part kit { clef percussion }

section A { kit { groove groove } }

form { ~A }

score { staff kit }
```

- A drum part is written as patterns. `drums { }` plays its lines together and draws them as a
  drummer reads them. Rest the mouse on a pattern's name to see its beat.

**Try:** after a pattern's name, press Ctrl+Space: the common patterns come first. **Lesson:** `lesson-10-drums`.

### Stage 13 — A band, and several printouts

```lys
pitch concert

part lead { instrument trumpet }
part bass { instrument bass }

section A {
  lead { e'4 g a g | c1 | }
  bass { c,4 g c g | c1 | }
}

form { ~A }

score {
  staff lead
  staff bass
  tab bass
}

score trumpet pitch transposed { staff lead }
```

- More parts, same section, as many bars in each. Each `score` is one printout of the same music,
  picked at the top of the preview.
- `tab NAME` prints a fretted part as tablature.
- `pitch concert`: write what sounds. `pitch transposed` on a score prints it in the player's key.

**Try:** pick `trumpet` at the top of the preview. **Lesson:** `lesson-11-band`.

## Part IV — Going further

### Stage 14 — Lead sheets and rhythm charts

```lys
fonts { music "Petaluma" }

part comp { clef treble }

section A {
  comp { /4 4 4 4 | 4 4 4 4 | }
  chords harmony { F7 | Bb7 | }
}

form { ~A }

score {
  chords harmony
  staff comp as lines 1
}
```

- `/` is a slash, a beat with no pitch. `as lines 1` draws the staff with one line.
- `fonts { music "Petaluma" }` is the handwritten look; `Bravura` and `Leland` are the others.

**Lesson:** `lesson-12-chart`.

### Stage 15 — The page

```lys
paper { paperWidth 210mm  paperHeight 297mm }
layout { barNumbers every 4 }

part melody { clef treble }

section A {
  melody { c'4 d e f | g1 | break a4 g f e | d1 | }
}

form { ~A }

score { staff melody }
```

- `paper` sets the page, `layout` the display switches (`barNumbers lines`, `none` or `every N`),
  `fonts` the type. `break` ends a line of music there; `pageBreak` a page.

### Stage 16 — Past the path

Name these when they are wanted, and read the grammar (topic `grammar`) before writing them:

- **Checking part-writing**: `check voiceLeading sop alt ten bas` reports parallels and crossed voices.
- **Lily#: Review Score**: what the score does well, and what to look at.
- **Reading printed music**: a scan or PDF into a score, with its doubtful bars marked.
- **Sound**: `midiInstrument "…"` on a part, **Lily#: Choose Sound Font…**, **Lily#: Choose MIDI Output…**.
- **Fine engraving**: `override`, `@todo("…")` to leave a note on the score, `using` to share music
  between files.
- **Outside the editor**: the `lysc` command line, and scores in Markdown.

## Which stage answers it

| They ask about | Stage |
|---|---|
| getting started, the preview, hearing it, a red squiggle | 1 |
| typing faster, changing a note, octaves by key | 2 |
| AI, exporting a PDF or MIDI | 3 |
| notes, durations, bars, octave marks, what the four blocks are | 4 |
| dotted notes, rests, ties, meter, tempo, a pickup | 5 |
| sharps, flats, keys, instruments | 6 |
| dynamics, slurs, staccato, ritardando, pedal | 7 |
| repeats, endings, sections, D.S. | 8 |
| lyrics, verses | 9 |
| chord symbols, chord notes, guitar diagrams | 10 |
| piano, two staves, triplets, two voices, reuse | 11 |
| drums | 12 |
| several instruments, tab, transposing instruments, parts | 13 |
| jazz charts, slashes | 14 |
| page size, line breaks, bar numbers, fonts | 15 |
| anything else | 16, then the grammar |
