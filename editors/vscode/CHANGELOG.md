# Changelog

All notable changes to the Lily# VS Code extension are documented here.

## Unreleased

### Breaking changes

- **Strings follow C#'s grammar.** `"…"` decodes C#'s escapes (`\"`, `\\`, `\n`, `\uXXXX`…) and
  any other backslash is an error (LYS0036); `@"…"` is verbatim (`""` is a quote). Both forms
  are coloured, escapes included, and an escaped quote no longer ends the string's colour.
- **An unclosed last ending is a syntax error.** `:| [2. C D E` no longer holds C alone;
  write `[2. C -]` for the open (straight-ended) bracket, `[2. C]` for a hooked one. The
  `]` may still be left off right before a `:|`. New: `layout { voltaBracket all|line|N }`
  and an ending's own `[1. B C]@voltaBracket(2)` say how far the bracket reaches (a cut
  bracket ends straight) — completed and explained on hover, and `-]` is coloured.
- **Old form-ending spellings removed.** `|: A [1. D] [2. O] :|` and the bracketless
  `:| 2. O` get the ordinary syntax errors and no longer render as endings; write
  `|: A [1. D] :| [2. O]`. Diagnostics LYS1010 / LYS1011 are retired.
- **Figured bass is written `@figuredBass(…)`** (was `@fig(…)`); the figures inside are
  unchanged. `@fig(6)` now warns as an unknown annotation. Typing `@fig` still finds the new
  name in the completion list.
- **A chord diagram is written `@diagram(x32010)`** (was `@frame(…)`).
- **`fonts { }` keys renamed** to the words the source uses: `chord`, `diagram`, `finger`,
  `barNumbers`, `partCombineText`, `time`, `tab` (were `chordName`, `fretFrame`,
  `fingering`, `barNumber`, `partCombine`, `meter`, `tabFret`); `lyricText` is gone —
  `lyrics` binds the syllables.
- **`layout { marks … }` is `layout { markTempo … }`.**
- **`@ho` / `@po` are no longer offered** — they drew nothing. Typing `ho` or `po` in the
  completion list finds `hammerOn` / `pullOff`.
- **Names are case-sensitive** — annotations and `fonts` / `layout` / `paper` keys. A
  wrong-case spelling (`@hammeron`, `barnumbers`) is refused, and the diagnostic names the
  right one.
- **`@upBow`, `@downBow`, `@shortFermata`, `@longFermata`, `@reverseTurn`** (were all
  lowercase): a name of several words is camelCase.
- **Value words are lowercase only** — `@notehead(triangle)`, `@diagram(x32010)`,
  `size a4`, `210mm`; `@notehead(TRIANGLE)` or `size A4` is refused with the spelling.
- **`@chord(C 7)` is C followed by a shape, not C7** — the words after the chord symbol are
  its chord diagram's shapes; write `@chord(C7)`.

### Chord diagrams

- **A chord whose shape is written draws a diagram under its name** — `@chord(Cm7 x3x546)`
  on a note, `F(133211)` or `F(133211 2010)` (a shape per string count) or
  `F(guitar 133211 ukulele 2010)` in a `chords` row, `@chord(x32010)`. A name alone draws
  none.
- **On the part's instrument**: the score's `layout { chordDiagrams TUNING }` (`guitar`,
  `ukulele`, `mandolin`, `guitardropd`, … — the tab tuning words; `none` turns diagrams off),
  else the part's fretted instrument (for a row, the staff it stands above), else the guitar.
  Completion offers the key and its words, and the grammar colours them.
- **`chordDiagrams all` (or `chordDiagrams guitar all`) draws every chord** — each chord name
  its written shape, else the usual one; a chord with no shape on the tuning warns once. After
  a tuning word the completion offers `all`, and the grammar colours it. In such a score the
  hover of a name alone shows the diagram it draws — `guitar: 320003 (default) — shape 1 of N`
  — and `Ctrl+Shift+Up` on it writes the NEXT shape (the usual one is already drawn), while
  `Down` at the usual shape does nothing; the status bar says so.
- **A shape table in the layout lists the chords that draw**: `layout { chordDiagrams guitar {
  Cm7 x35343  G  section Chorus { C x35553 } } }` — each listed chord draws wherever it is
  named (the table's shape, or the usual one for a name alone; a `section NAME { … }` block's
  entries apply in that section; a shape written at the chord still wins). The hover of a
  listed name shows the shape it draws — `guitar: xx3211 (layout)` — and `Ctrl+Shift+Up` /
  `Down` count from it as in an `all` score. A bad symbol or shape in the table, a chord
  listed twice and a section nothing declares are warnings at the word.
  Inside the table the completion offers the chords the file names that the table does not
  list yet, then the key's, and `section` — then the file's section names after it.
- **A capo: `chordDiagrams guitar capo 3`** — the shapes, the names (`chordNames shape |
  sounding | both`, a new key) and "Capo 3" at the score's head follow the capo, and the
  hover, `Ctrl+Shift+Up`/`Down` and the shape check read the pressed chord. **The capo
  suggestion**: after `capo ` the completion lists the frets 0–7 ranked by how many of the
  file's chords would take a barre there (`capo 3: 0 barre chords of 3` first, `capo 0: 2
  barre chords of 3 (F 133211, Bb x13331)`), and hovering `capo` or its fret shows the same
  ranking. `capo`, `chordNames` and their words are completed and coloured.
- **The chord list: `layout { chordList true }`** — every chord the score uses, with its
  diagram, under the title in centred rows; completed and coloured.
- **`Ctrl+Shift+Up`/`Down` step on a ukulele part too**: past LilyPond's ukulele table the
  shapes come from Lily#'s order, which on the re-entrant ukulele drops the "root lowest" rule
  and opens on LilyPond's own shape for every chord tried; the hover counts them (`ukulele:
  0003 (written) — shape 1 of 39`).
- **Hover** a chord with no shape to see how to add one: `Ctrl+Shift+↑ adds a chord diagram
  (guitar: 320003)`. With a shape written, the hover shows the shape each tuning draws —
  `guitar: x3x546 (written)`, `ukulele: no diagram` — and where it stands in the editor's
  order. A written shape that cannot be used is a warning (LYS1038) that names the fix.
- **A written shape that disagrees with its chord symbol is a warning (LYS1039)** — a note
  outside the chord, or a missing 3rd, 7th, altered fifth or tension (the root and the perfect
  fifth may go; the bass is not checked) — naming the chord the shape does play:
  `'x02210' sounds A C E, which is Am, not C (A is not a tone of C) - write @chord(Am x02210) or
  another shape`. The usual shapes `Ctrl+Shift+Up` writes first all pass: nine of LilyPond's
  that would warn (guitar D♯m `xx4341`, Faug, Baug; ukulele Bsus2; mandolin C♯aug and C♯dim7)
  are replaced by the first shape of Lily#'s order.
- **Frets 10–15: a shape writes a two-digit fret with a `-` on each side** —
  `@chord(Cm xx-10-12-13-11)`, `Cm(8xx88-11)`, `@diagram(8-10-10-888)`; a `-` between every
  string (`x-x-10-12-13-11`, the common chord-chart notation) reads the same. Between dashes,
  exactly two digits are one fret, so frets 10, 9, 9 are `10-9-9`. A shape routes by its
  string count (`8xx88-11` is six); one without `-` is one character per string as before.
  `Ctrl+Shift+Up` / `Down` now walk into frets 10–15 too (Cm goes on past `8xx888` to
  `8xx88-11`), writing a `-` only around the two-digit frets and only when a fret needs it;
  the hover spells shapes the same way. A miswritten one (`x-x-16-…`, `x--3-…`, `10-99-…`,
  `X-3-…`) is a warning that names the fix.
- Chord-symbol completion inside `@chord(…)` works as before on the first word.
- **`chord(C x32013)1` writes a shape's notes** on the staff (and the tab, with string
  numbers), on the part's instrument. Hover it to see the notes it sounds —
  `guitar: x32013 — C3 E3 G3 C4 G4`; `Ctrl+Shift+Up` / `Down` on it steps its shape (and
  sounds it) as on an `@chord`, from `chord(C)` Up writes the usual shape, and Down stops
  there; inside `chord(` the completion offers the chord symbols; `chord` is coloured as a
  keyword. `chord(C)` with no shape warns (LYS1040).

### Editor

- **`Ctrl+Shift+Up` / `Ctrl+Shift+Down` step what the caret is on**: a note gains or loses
  one octave mark (`'` / `,`; a chord member alone, or the whole chord from its `>`; every
  note of a selection); on a chord — an `@chord` or a `chords` row entry — Up on a name alone
  writes its usual shape (LilyPond's predefined one, else Lily#'s first) and the diagram
  appears (`@chord(Cm7)` → `@chord(Cm7 x35343)`, `G` → `G(320003)`), further Ups walk Lily#'s
  shapes, and Down back at the usual shape removes it again. With several shapes the one for
  the chord's tuning steps. The status bar says `Cm7: shape 4 of 33 (…)`. On nothing
  steppable the keys do nothing on Windows and macOS, and are VS Code's *Add Cursor Above /
  Below* on Linux (their second binding there). One undo step for all cursors.
- **Stretch shapes are left out by default** — a shape whose fretted frets lie five apart
  is hard to play. `lilysharp.chordShapes.includeStretch` (default off) lets the step walk
  them too; a stretch shape already written steps to the shapes that sort around it. The
  hover says where a written shape stands, with both counts: `shape 1 of 33 (… with
  stretch)`. The usual shape of a chord LilyPond's table lacks is the first shape without a
  stretch, and a stretch shape only when it has no other.
- **Audition as you edit** (with a preview open): the note, chord or `@chord` shape the
  caret lands on, a note as you type it, and the result of a step sound through the preview's
  synth, with the pitches the compiler plays. `lilysharp.audition.enabled` (default on)
  switches it off; `Alt+P` / `Alt+M` are unaffected.
- **Lily#: Split Sections to Match a Part** (command palette, right-click menu, and a quick fix
  on the "not the same length everywhere" warning): when one part has cut a section into
  several and the others still write it whole, the others are cut at the same bars into the
  same sections — chord rows and lyrics tracks too — and the forms play the new sections where
  they played the old one. If the parts subdivide it differently, a picker asks which to
  follow; then one confirmation shows the plan, and **Apply** makes it one edit (one undo).
  The server checks the result first: every cut part sounds and counts exactly as before. The
  plan covers every part still holding the followed sections in one (from the A warning or the
  B warning alike) and is refused whole — no partial split is offered. A slur, phrasing slur,
  tie or hairpin across a cut is kept (it is carried into the new section) unless it is still
  open at that section's end; a beam, pedal or other span across a cut, or a cut mid-bar, is
  shown as the reason nothing changed. Files grouped by part.
- **A slur, phrasing slur, tie or hairpin may run into the next section** the form plays, and
  must end there; the Problems list says where one does not (LYS4023) — carried through a
  whole section, closing nothing carried in, carried into a section the part does not play
  (warnings), or — a slur, phrasing slur or hairpin — carried over a repeat sign, volta edge or
  jump (an error). Every form a score plays is checked, not only the first score's.
- **A tie may cross any repeat sign or ending**: it reaches the first note of every section
  played next (the body again, the next ending), and the preview draws the arc where that note
  is printed next and a hanging tie plus an automatic repeat tie where it is not; playback
  sustains the note on those passes. A target on another pitch is flagged (LYS4007).
- **A digit typed on a note always leaves a valid duration** (1 2 4 8 16 32 64 128): it
  extends the digits there only when that makes one (`c1|` + `6` → `c16`) and replaces them
  otherwise — `c1|` + `2` is now `c2`, no longer `c12` (a 128th is pasted, or typed with the
  aids off, or `8` typed after a `12`). A `3` or `6` that extends into nothing completes to
  32 or 64 (`c4|` + `6` → `c64`). `5`, `7`, `9` and `0` leave the note unchanged, with a
  status-bar hint, instead of writing `c45`.
- **Slash notes take the typing aids**: a digit replaces the duration (`/4|` + `8` → `/8`,
  was `/48`); dots, ties, slurs and beams land on them as on a note.
- **Several cursors**: an octave mark, digit, dot, `\` or `@` typed with several cursors is
  applied at each cursor on its own note, in one undo step (it used to be typed as pressed
  everywhere). A selection is still replaced by the key, as anywhere in VS Code.
- **`lilysharp.typingAids.enabled`** (default on) turns all the typing aids off: every key
  types exactly as pressed.

### Form endings

- **An ending may hold several sections**: `|: A [1. B C] :| [2. D]` — one bracket over B
  and C in the preview, both played on the first pass. Rename, go to definition and the
  undefined-section check see every section in an ending. **Split Sections** turns a section
  played as an ending into a two-section ending instead of refusing.
- **A repeat needs a body**: `|: [1. B] :| [2. C]` and `|: :|` are errors (LYS1041).

### Fixes

- **A tie into the next section sounds as one note in the preview's playback** in a book of
  more than one part (it was played twice).
- **An `@chord` on a rest or a spacer draws in the preview** (`r1@chord(C x32013)`,
  `s1@chord(G)` drew nothing): the name at that moment and its diagram, as on a note. The step,
  the hover and the audition work on it as on a note's. A bare `@chord` there warns — write
  the name.
- **The Problems list's "Did you mean …?" names only a close typo** — within a third of the
  name's length in edits (`@glisando` → `@glissando`, `@tenuot` → `@tenuto`); a one- or
  two-letter name only for a swapped pair (`@fs` → `@sf`). `@ho` no longer suggests `@sf`.
- **A stray dot after an annotation (`@finger.3`) gets the ordinary message**, without the
  hint for an older Lily# spelling.
- **Repeat ties and hanging ties draw in the preview on every staff and in every voice**
  (`@laissezVibrer`, `@repeatTie`, and the automatic repeat tie of a tie carried over a
  repeat were drawn only in the first voice of the first staff); a lower voice's curves down.
  A tab staff draws none, as in LilyPond.

## 0.9.0

### Breaking changes

- **In relative octaves, the note after a chord or an arpeggio reads the chord.** 0.8.0
  read it from the note before the chord, so editing a note two items back could move a
  later chord by an octave. The chord now hands on its anchor (its first member's bare
  letter, or the tonic for a degrees-only chord), as it did before 0.8.0. A note right after a
  chord or `<< >>` can land an octave away from where 0.8.0 put it, without a warning.

### AI

- **Copilot now knows the Lily# grammar.** The extension contributes the language spec as a
  Copilot instructions file for `.lys` files, so Copilot Chat, agent mode and inline chat
  (`Ctrl+I` from Copilot's own menus) answer in Lily# instead of guessing LilyPond. Lily#'s own
  *Transform Selection with AI* and ghost completion already sent it. Copilot's as-you-type
  inline suggestions still do not read instructions files — that is Copilot's rule, not
  something an extension can change.
- **Ghost Completion no longer does nothing when switched on.** It is an inline suggestion,
  and the extension's own default `"[lilysharp]": { "editor.inlineSuggest.enabled": false }`
  (there because Copilot's completion stalled every keystroke in a score) kept VS Code from
  ever asking for it. Switching `lilysharp.ai.ghostCompletion` on now offers, once, to turn
  inline suggestions on for `.lys` files only and to keep Copilot's own completions off there
  (`github.copilot.enable`), so the stall does not come back. Nothing is written without the
  click.
- **The AI features no longer take whichever model comes first.** That was `gpt-4o-mini` on a
  Copilot account — the smallest model on offer, writing a language it has never seen. With
  no choice made, Lily# now passes over the small models (mini, nano, lite, haiku, flash) and
  takes the one with the largest input; **Lily#: Select AI Model…** picks one for good
  (`lilysharp.ai.model`). Ghost Completion has its own model, `lilysharp.ai.ghostModel`, and
  its automatic choice is the other way round — a SMALL model: it answers as you type, and a
  large one (2–3 s a bar) was cancelled by VS Code every time before it could show.
- **Transform Selection tells the model what the compiler knows.** The model saw the selected
  text alone — no key, meter, part or neighbouring bars, and none of the file's problems. It
  now also gets the file with the selection marked (the whole file when it fits, else its head
  and a window around the selection) and the compiler's current errors and warnings, those in
  the selection first — so "fix this bar" knows what is wrong with it. A candidate that
  compiles but ADDS warnings (a bar that no longer fills its meter is a warning in Lily#) is
  sent back for repair like a broken one; if the repairs run out it is still shown, with the
  new warnings named above the score.
- **Transform Selection can change the file around the selection.** A harmony in a new part
  needs a `part` line, a block in the section and a score row — none of it inside the
  selection — so the model could only answer with `voice { }` or with the music unchanged.
  It may now return the whole file (`<file>…</file>`); the edit applied is the span where it
  differs, and the before/after score lights that span. A reply that changes nothing is sent
  back instead of being offered for acceptance.
- **Transform Selection checks the octaves.** An octave slip is valid Lily# and draws no
  diagnostic, so a harmony line an octave or two off compiled cleanly. The candidate's notes
  are now resolved by the compiler; any that land more than an octave outside the register of
  the notes they replace are named back to the model with the pitch they actually are. The
  model fixes them, or confirms the register by returning the same candidate; a candidate
  still questioned says so above the score.
- **Ghost Completion logs what happened** to each suggestion in the *Lily# Extension* output
  (asked, shown, refused as not compiling, cancelled) with the model and the time taken — its
  ghost text and Copilot's look the same in the editor. A suggestion that opens with a
  barline no longer leaves an empty bar.

### Editor

- **Convert Octaves to Absolute / Convert Octaves to Relative** (right-click menu and
  command palette) rewrite the whole file into the other octave mode, keeping every note at
  the pitch it sounds. The result is checked note by note before it replaces the text, and
  one undo restores the file.
- **Convert Layout (part-major ⇄ section-major) is now Regroup (by part ⇄ by section).**
  Same command, plainer names: a file grouped by part writes `part bass { section A { … } }`,
  one grouped by section writes `section A { bass { … } }`. Its command id is now
  `lilysharp.regroup` (was `lilysharp.convertLayout`): a keybinding set on the old id needs
  setting again.

## 0.8.0

### Breaking changes

The extension bundles the compiler, so these change what a `.lys` file means. Three are refused
with the spelling to write instead; **the first two change a book without any message**, so check
for them by eye. The repository's
[CHANGELOG](https://github.com/yotsuda/LilySharp/blob/master/CHANGELOG.md) carries the reasoning.

- **A clef no longer moves pitch** — a part's bare letters start from its `octave N`, else its
  `instrument` preset's octave, else 4, never its clef. A bass part that relied on `clef bass` to
  read `c` as C3 now writes `clef bass octave 3`. A score's `staff bass x` now draws the bass clef
  it names.
- **A slur or a tie holds its lyric syllable, as in LilyPond** — under `lyrics … sings`, the notes
  inside a slur (after its first) and a note a tie arrives at take no syllable: `c4( d e) f` with
  `la __ lu` puts `lu` on f. `__` is the extender line and takes no note; `_` still takes one.
  When a lyric line then runs out of notes, the warning says which slur or tie held them and what
  to write instead (`@phrasingSlur` for a phrasing-only slur; drop a `~` written for a tied note).
- **An instrument preset has one name** — fourteen second names are gone: `uke`,
  `acoustic-guitar`, `electric-guitar`, `bass-guitar`, `electric-bass`, `5-string-bass`,
  `6-string-bass`, `double-bass`, `french-horn`, `piano-treble`, `piano-bass`, `voice-soprano`,
  `voice-alto`, `voice-tenor`. Write `ukulele`, `guitar`, `bass`, `bass5`, `bass6`,
  `contrabass`, `horn`, `piano-right`, `piano-left`, `soprano`, `alto`, `tenor`, and name the
  sound with `midiInstrument`.
- **A section's label is hidden only at the form reference** — `section ~A { … }` is refused
  (LYS0033); write `form main { … ~A … }`. A `~` on a reference always hides.
- **`volta` and `alternative` are ordinary words** — a part, section or phrase may carry either
  name. LilyPond's `\alternative { … }` after a `repeat` is now an error (an undefined name),
  where it used to be accepted and dropped by everything but the `.mid`.

### Language

- **Phrasing slurs: `@phrasingSlur` … `@!phrasingSlur`** — LilyPond's `\(` … `\)`, drawn over
  and clear of the slurs inside, with `.up` / `.down`.
- **`eses` and `ases` are E double flat and A double flat**, as in LilyPond.

### Added

- **`midiInstrument "…"`** in a part header names the part's General MIDI sound (LilyPond's 128
  names, completed and checked); the `.mid` now gives every part its own track, channel and
  sound, and the preview plays the same timbre.
- **A string number that cannot fret its note is reported (LYS5003)**, where it used to be
  ignored in silence.

### Editor

- **The preview re-engraves only what a keystroke changed** and receives only the pages that
  changed; the Problems panel shares the preview's work instead of compiling the book again.
- **The language server starts in about half the time.**
- **A CodeLens over each section's first declaration** — `Section A · 2 bars · melody, chords 'harmony'
  · 2× in form main` (counted by kind when many write it: `11 parts, 1 chord row`), or
  `Section A · ⚠ 9 bars in flute, 8 bars in the other 11 · …` when they disagree, and `in no form`
  for a section nothing plays. A section is one span of time however many parts write it; a
  later declaration of it gets a line only when its length differs from what most of the
  others write (`⚠ Section A · 11 bars here (1 bar longer) · 10 bars in 10 parts`); in a
  section-major book that line stands over the odd block itself. A click lists everything that writes it.
- **Convert Layout no longer drops a cell written twice.** When a part, chord row or lyrics track
  writes the same section in two declarations, the other layout has room for only one of them;
  the command now leaves the file unchanged and names the cell instead of silently keeping the
  later one.
- **A section written at different lengths is one problem, not one per part** — grouped by bar
  count, placed on the odd one out, and with every part and track listed under it as a link. Its
  quick fix pads all the shorter ones at once.

### Engraving

Ties, laissez-vibrer and repeat ties, beams, tremolos, lyric extenders, accidental stacking,
ledger lines, clef octave digits, marks, `rit.`/`accel.` spanners, grace notes and the start of a
bar are now drawn and spaced by LilyPond's own arithmetic, each measured against LilyPond 2.26.0.
A tremolo on a beamed note is drawn at last, and `to coda` is drawn as the coda sign. The full
list is in the repository's CHANGELOG.

## 0.7.0

### Breaking changes

The extension bundles the compiler, so these change what a `.lys` file means. Each is refused
with the spelling to write instead (the first compiles and prints differently), and the
repository's [CHANGELOG](https://github.com/yotsuda/LilySharp/blob/master/CHANGELOG.md) carries
the reasoning.

- **Chord qualities print LilyPond's symbols by default** — `C°`, `C+`, `Cø`, `C°7` and a drawn
  triangle for a major seventh; `layout { chordQualities words }` brings back `Cdim`, `Caug`,
  `Cm7♭5`, `Cdim7`, `Cmaj7`.
- **`removeEmpty` moves from the part header to the score's staff item** —
  `staff lh as removeEmpty all`.
- **A font entry follows a generic family with `as`** — `fonts { chordName as serif }`.
- **`paper { topSystemPadding }` is retired** — `topSystemSpacing { padding N }` does the job.
- **A MIDI-only score row is a bare part name**; its `instrument` / `octave` options did nothing.
- **The drum name `hhs` is gone** — it drew and played a pedal hi-hat; write `hhp`, or `cyms`
  for a splash cymbal.
- **`@feather` takes `right` or `left` only** — `accel` / `rit` inside it are retired.
- **The tunings `standard` and `uke` are gone** — write `guitar` and `ukulele`.
- **A section's opening pickup is written in the section header only** —
  `section A { partial 4 … }`; a `partial` in a part's first bar is refused. Later in the
  music it still shortens the bar it stands in, written in every part.

### Language

- **`layout { }` gathers the score-wide display switches** — `marks`, `barNumbers`,
  `accidentals`, `sectionLabels`, `partCombineText`, `chordQualities`, `minorChords` — as a
  file default or a named block a score references. Completed and coloured inside the block.
- **A `fonts { }` entry carries a size and a style** — `mark "Charis SIL" step +1 bold`.
- **A `break` inside a bar splits the bar across two systems**, `paper { raggedBottom }` keeps
  every page at its natural spacing, `time none` is engraved, and `partial` may stand at a bar's
  start in a part's music mid-section (a section's opening pickup stays in its header).
- **LilyPond's whole drum and tuning tables** — the Latin percussion, and tunings such as
  `guitardropd`, `guitardadgad`, `guitar7`, `violin`, `mandolin`, `banjoopeng`; `mandolin` and
  `banjo` presets, and a bowed preset frets its tab on its own strings.
- **Staff groups nest**, at any depth, and hold `condensedStaff` / `combinedStaff` members.
- **A spaced dot inside `<< … >>` holds the member before it one more share** — `<< c . d >>4`
  is the swing figure (`tuplet 3/2 { c4 d8 }`), `<< c . . d >>4` is `c8. d16`. Written as its
  own token, never glued.
- **A `<< … >>` member carries scripts, fingering, dynamics, string numbers and slur marks**
  (`<< c@accent e\3 g( a) >>`); a string number on the group is every member's, and a tie or
  slur mark after `>>` hangs on the last member. String numbers on a group used to be dropped
  in silence.

### Added

- **A ```` ```lily# ```` fence in a Markdown file draws its score** in VS
  Code's built-in Markdown preview, the way a ```` ```mermaid ```` fence draws a diagram. A
  fence writes exactly one `score { }` and is laid out as a snippet — one page as tall as the
  music and as wide as its widest system. Opening a Markdown file costs nothing: the language
  server starts only when a score is opened or a fence needs drawing.
- **Right-click `.lys` files in the Explorer to export them** — a *Lily#: Export* submenu (PDF,
  SVG, PNG, LilyPond, MusicXML, MIDI, VOCALOID) writes every score of every selected file into
  one folder, named as `lysc --all` names them.
- **A quick fix pads a short section voice, chord row or lyrics cell with bar lines** on its
  LYS2007 warning; quick fixes now read every diagnostic the Problems panel shows.
- **The completion popup names every construct the grammar takes** — sixteen spellings no list
  offered, each compiled where it is offered — and reads the construct it is in: a chords or
  lyrics track's body, a silent section, a score header with options and a staff row at any
  length each get their own list. A scaffold that writes a part or form name takes one that
  exists (or, for `form`, one that is free), and a `fonts` entry offers `step` and `size` first.

### Engraving

- **A chord symbol is raised and sized where LilyPond sets it**, and under the default
  `symbols` a major seventh is LilyPond's drawn triangle.
- **A feathered beam fans** — `@feather(right)` / `@feather(left)` used to draw a plain beam.
- **A tab's strings are chosen by planning the whole phrase's fingering**, not one note at a
  time; a written `\N` always wins.
- **Braces, brackets, the system-start bar and the staff lines' ends stand where LilyPond puts
  them**, and a part-combine `a2` / `Solo` label is placed as LilyPond places it.
- **An arpeggio in a dotted total is spelled as compound metre spells it** — `<< c e >>4.`
  is a 2:3 duplet of eighths, `<< c e g a >>4.` a 4:3 quadruplet, instead of dotted
  members. See the repository's
  [CHANGELOG](https://github.com/yotsuda/LilySharp/blob/master/CHANGELOG.md) for the rule.

## 0.6.0

### Language

The extension bundles the compiler, so these change what a `.lys` file means. Each is
diagnosed rather than silent, and the repository's
[CHANGELOG](https://github.com/yotsuda/LilySharp/blob/master/CHANGELOG.md) carries the
reasoning behind each one.

- **`nobreak` is renamed `noBreak`** — the break family is LilyPond's spelling minus the
  backslash. The old lowercase word is no longer a keyword.
- **`@invertedturn` is renamed `@reverseturn`** — LilyPond's name for the ornament; the old
  one was MusicXML's. Completion and highlighting follow.
- **`tocoda` is gone; write `to coda`** — one spelling per navigation instruction. The
  run-together word is an ordinary name now.
- **A chord row has no `s`; a `.` at a bar's head is the silent slot** — `| . C |` is "no
  chord, then C", `| |` an empty bar, `r` prints N.C. An `s` in a `chords` block is reported
  with the spellings that replace it.
- **`pageBreak` / `noPageBreak`** — the page-break pair beside `break` / `noBreak`, in a
  section's music or in a `form`; `pageBreak` breaks the system too, as LilyPond's
  `\pageBreak` does. Both are offered by the completion where `break` is.
- **A score row's `sings` is that row's own melody** — `lyrics verse sings alt` under the alto
  staff is the alto's verse, whatever the definition said, so a chorale writes its words once
  and places them under every staff.

### Added

- **`lilysharp.preview.theme`** — the score preview's color scheme: follow VS Code's theme
  (the default, and what the preview always did), always light (the printed page), or
  always dark (the page inverted). A change reaches every open preview at once.
- **The key's chords are offered in a section's music, and accepting one writes its notes.**
  In C major the list carries `C`, `Cmaj7`, `Dm`, `Dm7` … and the degrees `I`, `IIm7`, `V7` …
  beside the pitch letters; choosing `C` inserts `<c e g>`, `IIm7` inserts `<d f a c>`, in
  the spelling of the key in force at the caret (`F#m` in D major is `<fis a cis>`). The
  `flatSpelling` setting applies to these notes as it does to the pitch rows. This row had
  been lost; it is back with a test that compiles every offered chord.
- **A form's completion carries the whole form vocabulary, in a writer's order.** Sections,
  silent sections, then the repeat block — `|:`, `:|`, `:|:`, `:|*N` — the endings
  `[1. ]` `[2. ]` `[3. ]` `[1-2. ]`, the navigation marks, the engraved barlines `||` `|.`
  `!`, `break` / `noBreak` and `_"…"`. The list used to sort by label, which buried the
  repeat bars under the section names, and lacked `:|:`, the count, the barlines and the
  breaks. A repeat bar typed as far as `|` is replaced by the item rather than appended to
  (`||:` no more). Every plain item is compiled in a form by a test.
- **A section-major section's body offers its header directives.** After the part cells,
  `partial`, `key`, `time`, `tempo` and `override` — the directives a `section A { … }` takes
  beside its cells, which the list did not carry (the part-major header already had them).
- **The music list no longer offers `partial`.** A pickup is a section directive
  (`section A { partial 4 … }`); written inside a part's music it is `LYS1024`, and the row
  had been teaching that. The test behind the list now runs the semantic validators too,
  not just the parser, which is how this one got through.
- **The music list no longer offers `|: :|`.** Repeat structure is form-only (`LYS1034`), so
  the two volta snippets that still stood in a section's completion taught a spelling the
  compiler refuses; they are gone, and a test now compiles every plain item the list offers.
- **The music list reads in the key's order.** On Ctrl+Space the pitches come in scale order
  from the tonic (`d e fis g a b cis` in D major), then the chord names root by root — triad,
  7th, sus4, sus2 — then the degrees in the same shape (`I Imaj7 Isus4 Isus2 IIm IIm7 …`).
- **Completing `pitch` re-opens the popup on `written` / `concert`** — at the top level, in a
  part header and on a score header alike, the same motion `octave`, `key` and `time` have.
  The top-level item used to insert a snippet choice; the part-header item inserted the bare
  word and stopped. The two words are read from the compiler.
- **Completing `repeat` re-opens the popup on `unfold` / `percent` / `tremolo`**, and picking a
  kind finishes the construct — count and braced body, caret inside. The item used to commit
  to `repeat unfold 2 { }` (`percent` in drum music) with the other kinds named only in its
  description.
- **A typed `[` closes on the FOLLOWING note, as `(` does.** `c8|` + `[` gives `c8[ d] e f`;
  widening the beam is dragging one `]` forward. It used to run to the last beamable note of
  the measure (`c8[ d e f]`). `]` mirrors it — the `[` goes after the beamable note before —
  and a beam already ending there is extended by one note, as a slur is.
- **Smart `(` and `[` see a tab's string number.** In `c\3 d` the typed mark found no note
  ahead — the walk ended the note at `c` and read `\3` as a wall — and did nothing. The
  `\N` is the note's own annotation, as the compiler reads it, so `c|\3 d` + `(` gives
  `c\3( d)` and `[` likewise.
- **A typed `\` opens the note's tab string number in its slot.** Pressed anywhere on a
  note it goes directly after the core, and the caret goes with it, ready for the digit —
  `|a4( d)` + `\` gives `a4\|( d)`. A digit typed on a note whose `\` is still waiting for
  it is the string number, not a duration. On a note that already has a `\N`, `\` inserts
  nothing and selects the N, so `\` + digit changes the string. Inside a chord the `\` is the
  member's the caret is on (`<c| e>4` + `\` gives `<c\| e>4`), as is a typed `@`; a rest
  takes it as typed.
- **After `\` the completion offers the tab string numbers `1`–`6` and nothing else.** It
  used to offer the LilyPond dynamic names (`ppp` … `cresc`, `dim`), every one of which the
  compiler refuses (`@p`, not `\p`); only a digit follows a backslash.
- **The smart keys write a note's marks in one order**, whatever order they were pressed
  in: string number, `@` annotations, `]`, `)`, `(`, `[`, `~` — from the note outward by
  how much music each mark spans, what ends on the note before what begins on it, brackets
  nested with the slur outside the beam, and the tie last, beside the note it joins. So
  `c8([ d e f])`, `d4)( e`, `a,4\4~` and `c4)~ c`, and `\4~` or `)~` can be searched for.
  The marks used to land wherever the earlier keystrokes had left the note's end. Text in
  another order is read as before and is not rewritten; a new mark on such a note goes
  after the last one that ranks at or below it. A typed `@` follows the same table: on a
  note it goes after the string number and the annotations already there, before the
  marks, with the caret after it and the name list opened there (`c4~|` + `@` gives
  `c4@|~`). A digit or an octave mark typed among a note's marks is typed on the note too:
  `c8\8(|[` + `4` gives `c4\8([`, the caret staying put.

### Fixed

- **The smart keys no longer slow the editor down in a long score.** Every mark a smart key
  places — `' , . \ @ ( ) [ ] ~` and the digits — is decided by reading the music around
  the caret, and that reading began at the start of the enclosing block on every keystroke,
  testing each character with a regex on the way: at the end of a 1000-bar block a key cost
  8–25 ms before anything was typed, and the one-order rule (above) had tripled the work per
  note. The reading now starts at the nearest barline before the caret — a barline cannot
  sit inside a note, a chord or an annotation, so the walk sees the same events — and the
  character tests are comparisons. The same keys cost 0.2–1 ms there; `npm run bench` in
  `editors/vscode` prints the figures for the repository's 1000-bar books. What each key
  writes is unchanged, and two new tests pin the one reader that has to look into the
  previous bar (`c4 | d` + `)` gives `c4( | d)`).
- **Moving the caret no longer costs the whole score.** Three things ran on every arrow
  key: the extension rebuilt the document's text to find the caret's token (now it reads
  the caret's line); the preview searched every drawn element for the note to light and
  cleared the last one with another search of the whole page (now both come from an index
  built once per render, and a held key paints only the position the caret has reached);
  and the language server, asked which names to highlight, walked the syntax tree five
  times before knowing whether the caret was on a name at all (the name lists are now
  built once per edit).
- **The preview redraws only the page an edit changed.** Every render used to replace the
  whole SVG, and on a five-page song that parse, layout and paint took about half a
  second in the editor's own window after each keystroke. The pages are now compared as
  text: a changed page is parsed again, a page whose drawing is the same but whose source
  offsets moved (everything after the edit) keeps its elements and has the offsets
  re-stamped, and untouched pages are left alone — about a fifth of the time, with the
  same picture as the full replacement (checked element for element in a browser).
  Within a changed page the same goes system by system: the preview's SVG now wraps each
  system, and each page's overlays (slurs, lyrics, dynamics, marks), in a group of its own,
  and only the group the edit touched is parsed again. Exported SVG is unchanged. Each
  system is also drawn in a frame of its own (its coordinates start at its top, and a
  transform puts it on the page), so a system that only moved down the page — every system
  below an edit that changed a height — keeps its elements and gets its transform set
  And the pages whose drawing changed are reconciled together, group by group and across
  the pages: every system is matched by content, kept where it is, moved by its transform,
  or carried to the page the breaker moved it to; only a system whose drawing changed (the
  edited one) and the pages' overlays are parsed (`reconciled N (groups kept K, moved M,
  carried C, parsed P)` on the update line).
- **The Lily# output channel now times each preview update**: the round trip to the
  language server on the `Got response` line, and a `PREVIEW update:` line from the page
  saying what it swapped (pages kept, re-stamped, updated by group, replaced) and how long
  the swap and the re-fit took. When an update feels slow, these two lines say which side
  to look at. The `Got response` line now also splits the round trip where the server's
  clock says it went — to server, queued, expand, lock, render, back, and how long the
  last diagnostics pass took — so a stall (the same one-system edit answered in 140 ms
  once and 3.8 s the next time, in one user log) is attributed to transport, the
  server's queue, an older render still running, or the editor's own event loop, and
  not read as engine time.
- **A bar inserted or deleted mid-score no longer re-prices every bar after it.** The
  per-bar spacing memo read the previous score bar by bar at the same index, so a bar added
  in the middle shifted every later one out of its slot and the whole tail was rebuilt (3
  of 112 bars reused on a 3-page bass book, against 110 for the same bar added at the end).
  The memo now looks where the tail went. The picture is unchanged; two tests pin the
  reuse counts for an insertion and a deletion.
- **Inline suggestions (ghost text) are off in Lily# files by default.** Measured with the
  extension host profiler on a real book (2026-09-04): with GitHub Copilot installed, every
  keystroke in a `.lys` file spent one to two seconds inside Copilot's inline-completion
  prompt building — the extension host, where every extension's messages pass, was
  blocked for that long, and the preview's request and answer sat behind it while the
  engine itself took under 100 ms. Lily# has completion of its own, so
  `"[lilysharp]": { "editor.inlineSuggest.enabled": false }` is now the extension's
  default; a user who wants ghost text in scores can turn it back on in their settings,
  which win over this default. The `Got response` line's `host lag max` is the number
  that shows whether something else in the extension host is in the way.
- **...and no longer lays every system after it out again.** The per-system memos
  (spacing, skylines, beams, ties, slurs, lyric bands) keyed each system on its first bar
  NUMBER, so a bar inserted or deleted mid-score put every later system under a number
  the memo had never seen and the whole tail was recomputed — the layout stage of such a
  keystroke cost three to four times that of the same bar added at the end. A system
  found under other numbers with the same music is now served with its numbers re-stamped.
  This pays where the systems after the edit are still the same bars — a book that pins
  its lines with `break`, as tab books do; under the automatic line breaker a bar inserted
  into a uniform book spills one bar into every later line, and those lines are new
  music. Separately, deleting a bar of BEAMED notes used to invalidate every later bar
  outright (the beam identity is numbered in score order), which had silently defeated
  every memo — spacing included — on such a keystroke; the key now folds the grouping,
  not the number. The picture is unchanged; the reuse counts are pinned for an insertion,
  a deletion and a chain of edits.
- **A chord track's bars are no longer checked as if they held durations.** A per-section
  `chords` track had each `s` / `r` slot priced as a quarter rest, so a 2/4 row spelled
  `s | C#m | …` reported every such bar as too short. A chord row divides its bars on the
  beat grid and keeps only its own grid diagnostics.

### Engraving

- **On a lead sheet, the volta bracket stands on the chord row and both ending labels stand
  on the bracket.** A chord row used to float the bracket a band too high, and a second
  ending's label could land under it, level with the symbols. The bracket now hangs off the
  staff and clears the row's symbols by LilyPond's padding; the labels clear the line exactly.
- **A `|:` that opens the piece is printed.** LilyPond's default drops the automatic repeat
  bar at the start of a piece; in Lily# a `|:` is always one the writer wrote, and lead
  sheets print it. The LilyPond twin carries `printInitialRepeatBar = ##t` so both pages agree.
- **A `|:` that opens a line stands where LilyPond's does, and the first note keeps its
  distance from it.** The bar is the last column of the clef/key/meter group and the first
  note stands 1.3 off its ink; it used to be spaced as if the bar were not there.
- **A rehearsal mark or section name mid-line is centred on its bar line**, as LilyPond
  centres it; the box used to hang off the bar to the right.
- **A hand-written slur from a grace note to its main note is drawn.** `grace { g16( } a8)`
  draws the bow `appoggiatura { g16 } a8` has always drawn — LilyPond's own pair of slur
  events. The `(` goes on the last grace note and the `)` on the main note; other placements
  are still reported as not engraved, and an unclosed `(` is reported unpaired.
- **The number of systems is chosen by the page's score, as LilyPond chooses it** — the line
  breaker's best count is only where the choice starts. On a 286-book bass corpus the system
  breaks matching LilyPond rose from 356 to 388 pairs. Known: two or three books now merge or
  split a line LilyPond does not (`Alone Again`, `Livin' It Up`); their bar widths are next.
- **A full-notation tab's stems, beams and flags are in its skyline**, so a tempo mark above
  the tab clears an up-beam in the first bar instead of printing through it.
- **The blank bars of a `repeat percent` over three or more bars print nothing on a tab**;
  the tab drew a whole rest in each.
- **A dotted chord on an `as numbers` tab draws no augmentation dot.**
- **What a `repeat percent` body writes prints once** — no slur, tie, script or dynamic
  under the percent signs.
- **A pedal bracket under a system keeps the next system away** — it joins the page's
  silhouette, so a bracket under one system no longer prints through the marks above the
  next.

## 0.5.0

### Language

The extension bundles the compiler, so these change what a `.lys` file means. Each is
diagnosed rather than silent, and the repository's
[CHANGELOG](https://github.com/yotsuda/LilySharp/blob/master/CHANGELOG.md) carries the
reasoning behind each one.

- **Repeat structure is written in a `form { … }` and nowhere else** (`LYS1034`). A repeat
  bar (`|:` `:|` `:|:`) or a volta ending (`[1. … ]`) written in music is an error.
  `repeat percent`, `repeat unfold` and `tremolo` stay in the music — they abbreviate notes
  rather than change the playing order.
- **Every span has to be closed, and `@!X` is how** — `@rit … @!rit`, `@ottava … @!ottava`,
  `@sustain … @!sustain`. An unclosed span draws nothing at all, which is LilyPond's own
  answer, and is an error (`LYS4018`).
- **`@loco`, `@sustainOn`, `@sustainOff`, `@sostenutoOn` and `@sostenutoOff` are retired.**
  The direction moved out of the name and into the `!`. `@treCorde` stays, because unlike
  the others it is a word the page actually prints.
- **A part setting (`clef`, `octave`, `instrument`, `transpose`) written beside a section's
  part cells is refused** (`LYS1035`, `LYS0030`) — it belonged to no cell, so nothing read
  it. **A `form` that plays a section declared only as a header is refused** (`LYS1036`).
- **A phrase reference's interval argument is removed.** `Melody'(3)` no longer plays the
  phrase a third up; the octave marks `Melody'` and `Melody,` are unchanged.
- **A percent repeat prints the sign its body's length earns**, and **a tab staff's style
  follows the score** — `tab NAME` with no `as` clause is `as numbers` beside a notation
  staff and `as full` when it stands alone.

### Added

- **A section reference carries octave marks** — `~B'`, `~B,`, `[1. B']` — the same
  spelling a phrase reference already had.
- **`section ~A { … }` declares that the section prints no rehearsal letter**, so a section
  cut solely to carry a repeat edge is silent without saying so at every reference.
- **A form can spell a third volta ending** (`[3. … ]`).

### Fixed

- **Clicking a tie or a slur in the preview jumps to the character that wrote it.** Ordinary
  bows carried no source address, so a caret on `~` used to light up the note in front of it.
  A `~` now cites its `~`, a slur its `(`, and a laissez-vibrer or repeat tie the annotation
  that draws it.

- **A note that opens an indented line is clickable, and a diagnostic on one names its
  column.** Both addresses included the whitespace in front of the note, so a click on the
  first note of an indented line resolved to nothing.

- **Clicking the clef or the key signature jumps to its source line from any staff line, not
  just the top one.** The prefix repeats at the head of every system, but only the first
  system's copy was clickable. Each repeat now carries the position of whatever put it in
  force there, so a line showing a change jumps to the change.

- **A rehearsal letter written inside an inline ending, a tuplet, a repeat or a cue is
  printed.** One chart wrote A, B, C and D and printed only A — the other three stood inside
  a `[2. … ]` and drew nothing, with no diagnostic. A letter that is written and still not
  printed now says where (`LYS4019`).

- **A `@rit` in a repeated section draws the same length both times.** From one written mark
  the first playing covered six bars and the second one, because the spanner was being ended
  by the next playing of itself. The hairpin had the same defect.

- **An `@rit` / `@accel` no longer prints on top of the chord row or the lyric row above its
  staff**, and one above a system's top staff reserves the room it is drawn in. The staff now
  reserves the room the spanner occupies, measured against LilyPond.

- **A lyrics row no longer prints inside the tab staff above it** — one quantity, how tall
  this staff is, was written in three places and two of them answered with the score's
  nominal four staff spaces.

- **A tab staff no longer repeats the markup the notation staff beside it already carries**,
  and the switch is `as numbers` vs `as full` rather than "is it a tab" — so a `@text` on a
  standalone tab appears, and an `@accent` on a numbers tab does not.

- **A TAB technique letter no longer prints on top of its own notehead.** `@tap` (T),
  `@hammeron` (H), `@pulloff` (P) and `@pluck`'s finger letter reserved a symmetric box
  around their anchor while the letter is drawn with its baseline there, so one landing
  below its note grew upward into the head. The room reserved is now the letter's own ink,
  which also gets `@pluck`'s descender right.

- **Grace notes carry what is written inside them.** A grace body is now walked like ordinary
  music, so a chord, a rest, a dot, a phrase reference and a tuplet's notes inside one are
  engraved, heard and exported instead of being dropped in silence. What is still dropped
  says so (`LYS4020`).

- **The chord completion inside `chords { }` lists the names first and the degrees after,**
  instead of interleaving them degree by degree. It now reads all seven names (`C Dm Em F G
  Am Bdim`, each with its 7th and suspensions) and then all seven degrees (`I IIm IIIm IV V
  VIm VIIdim`).

- **"Reveal in Explorer" after an export opens the file's own folder again when the path
  contains a space.** `explorer.exe` reads the raw command line rather than a parsed argv,
  and Node quotes any argument containing a space, so the `/select` switch ended up inside
  the quotes and Explorer fell back to Documents.

## 0.4.0

First Marketplace release — 0.3.0 was tagged and shipped as GitHub binaries only, so
this is the first version installable from the extension page. It is also the first
release with breaking changes to the language; the four below are all diagnosed, so a
0.3.0 file tells you what to change rather than failing silently.

### Breaking changes

- **A chord is written the way it prints.** `Am`, `G7`, `F#m`, `Bb7`, `Gm7-5`,
  `Cmaj7/E` — an uppercase root, optional `#`/`b`, and a bare quality. The lowercase
  `:` entry (`a:m`, `g2:7`) and its per-chord durations are gone: a bar's entries now
  divide it on the beat grid the beams already use, and `.` holds the previous chord
  one more beat. The case *is* the grammar, which is why `R` never collides with a
  rest and every altered tension spells `+`/`-` (`m7-5`, not `m7b5`, so `Bb5` stays
  B-flat's power chord). Diagnostic: `LYS1028`.
- **The `$` sigil is gone.** `$theme` no longer marks a phrase reference; a bare name
  is one. Drum vocabulary and `q` are refused as phrase *names* at the declaration
  (`LYS1030`) rather than being disambiguated by a sigil at every use.
- **A staff's display name must be quoted.** `staff flute "Flute"`. A trailing bare
  word is now always a part reference, so `staff flute click` plays the click track
  instead of silently relabelling the flute.
- **`NoteColumn.force-hshift` now errors** (`LYS1029`) instead of being accepted and
  ignored. In exchange, `NoteHead.color` and `Stem.color` are supported — a correctly
  coloured score used to ship with an error and a non-zero exit.

### New

- **`paper` blocks.** The page's dimensions come from the source. `size b5` (or
  `jisb5`, `letter`, …) sets width, height and all four margins, each margin scaled
  from a4 the way LilyPond's `set-paper-size` scales it.
- **Named `fonts` and `paper` blocks.** Declare `fonts house { … }` or
  `paper wide { … }` once at the top level, reference it per score, and override part
  of it in place — one file can carry a wide conductor page and default part pages.
- **MusicXML round-trip.** Import carries the source's page across as a `paper` block;
  export now emits a form's custom text.

### Engraving

- Lyrics: syllables align on their own voice's notehead, melisma spans reserve the
  range they occupy, a word crossing a barline is spaced as one, and a row standing
  below a multi-staff system clears the staff above it on every system.
- Lead sheets: every line opens with a bar, no bar runs through a word, stanza
  numbers anchor clear of the opening bar, and the grid row prints the meter.
- Pedal brackets and bar numbers hang from their own staff rather than the system.

### Performance

- Typing in a score with lyrics is markedly faster: the loose-line chain caches its
  prefix, verse skylines share one store with the measure layouts, and the lyric band
  joins the per-system memo.

### Packaging

- One VSIX per platform, each carrying its own .NET runtime and native rendering, so
  the extension needs nothing installed but VS Code.

## 0.3.0

First public release.

### Language support

- Semantic syntax highlighting for pitches, dynamics, and articulations
- Real-time diagnostics (errors and warnings as you type)
- Code completion for keywords, pitches, durations, dynamics, and `@`-annotations
- Hover documentation, signature help, and document highlight
- Document outline, go-to-definition (F12), find references (Shift+F12), and rename (F2)
- Code folding, document formatting, and quick-fix code actions

### Live score preview

- Rendered score preview that refreshes as you edit
- Click a note in the preview to jump to its source in the editor
- MIDI playback with note-by-note highlighting

### Engraving

- LilyPond-faithful layout: beaming, multi-articulation stacking, fingering,
  accel./rit. text spanners, dynamics and expressive text, volta brackets,
  and multi-staff scores

### Packaging

- Self-contained language server — each platform build bundles its own .NET
  runtime and native rendering, so nothing else needs to be installed
