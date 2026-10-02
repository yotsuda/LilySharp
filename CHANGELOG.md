# Changelog

Notable changes to Lily# are recorded here, newest first. Release notes are taken
from this file: the topmost section is the version being tagged, and the release
workflow attaches that section to the GitHub Release verbatim.

## Unreleased

### Breaking changes

- **An ending's numbers are its passes.** `|: A [1. B] :|*3 [2. C]` is now an error
  (LYS1042): the count said three passes where the numbers said two, and the page printed
  "1." "2." while it played A B A C A C — a C followed by a return no bar line shows (LilyPond
  reads the same shape as "1. 2." "3.", A B A B A C). Write each pass as a number:
  `|: A [1-2. B] :| [3. C]`. A pass no ending names, or two name (`[1. B] :| [3. C]`,
  `[1-2. B] :| [2. C]`), is an error too (LYS1043). A repeat without endings keeps
  `|: A :|*N`. A bracket now prints its passes as LilyPond does — `[1-2.` prints "1. 2.",
  `[1-3.` "1.–3.", `[1,3.` "1. 3." — where it printed the written "1-2.". A last ending that
  plays a pass before the last (`|: A [1,3. B] :| [2. C]`, `[2-3. C]`) now ends in the repeat
  bar its return needs, on the page and in MusicXML, as LilyPond draws it; only the MIDI
  returned there before.
- **Strings follow C#'s grammar.** A regular `"…"` decodes C#'s escapes — `\"`, `\\`, `\n`,
  `\t`, `\uXXXX` and the rest — and any other backslash is an error (LYS0036); the new
  verbatim `@"…"` takes a backslash as written, with `""` for a quote. Until now no escape
  was decoded anywhere: `@text("say \"hi\"")` printed `say \"hi\`, and the LilyPond twin
  doubled the backslashes. A backslash written for its own sake (`"\^{~}3"`) is now LYS0036
  — write `"\\^{~}3"` or `@"\^{~}3"`. The MusicXML import writes values back in the same
  escapes, and the editor colours both forms and marks an invalid escape.
- **An ending's range, end shape and length are three separate settings.** The `]` ends an
  ending; it may be left off only right before a `:|` (`|: A [1. B C :| [2. D]` — the `:|`
  closes it, and its bracket now hooks). An **unclosed last ending is now a syntax error**
  ("Expected 'CloseBracket'"): `:| [2. C D E` used to hold C alone and draw an open
  bracket, with D E playing after the repeat. Write `[2. C -]` for that open look — `-]` ends
  an ending with a straight right end, `]` hooks it down — and move D E out of the brackets.
  The length is new: `layout { voltaBracket all|line|N }` (`all`, the default, covers the
  whole ending; `line` stops at the end of the system the bracket starts in; `N` covers its
  first N bars), and one ending overrides it with `[1. B C]@voltaBracket(2)`. A bracket cut
  short always ends straight. The `.ly` twin now draws what the page draws — a hooked last
  ending re-sets LilyPond's hook, `-]` zeroes it, N sets `VoltaBracket.musical-length`, `line`
  drops the later pieces — and MusicXML writes `<ending type="stop">` for a hook and
  `"discontinue"` for a straight end or at the cut (a closed last ending was always
  `"discontinue"` before). Completion and hover know the key, the annotation and `-]`.
- **The old form-ending spellings are gone.** `|: A [1. D] [2. O] :|` (the repeat bar after
  both endings) and the bracketless `:| 2. O` / `|: A | 1. D :| 2. O` were refused (LYS1010,
  LYS1011) but still built endings, so they kept rendering. They now get the ordinary
  errors — the first ending must be followed by its `:|` ("Expected 'RepeatEndBar'"), and a
  bare `2.` is a stray form item (LYS0030) — and nothing is engraved as an ending. Write
  `|: A [1. D] :| [2. O]`. LYS1010 and LYS1011 are retired.
- **Figured bass is written `@figuredBass(…)`.** The abbreviation `@fig(…)` told a reader
  nothing; the new name is the term itself and matches the `fonts { figuredBass … }` role.
  The figures are written as before (`c4@figuredBass(6 4)`). `@fig(6)` is now an unknown
  annotation — it warns and draws nothing.
- **A chord diagram is written `@diagram(x32010)`.** It was `@frame(…)`, MusicXML's element
  name, which no player uses; guitarists call it a chord diagram. The position string and
  `.down` are unchanged. `@frame(…)` is now an unknown annotation.
- **`fonts { }` keys are spelled as the source writes what they style.** `chordName` →
  `chord`, `fretFrame` → `diagram`, `fingering` → `finger`, `barNumber` → `barNumbers`,
  `partCombine` → `partCombineText`, `meter` → `time`, `tabFret` → `tab`. `lyricText` is
  gone: `lyrics` binds the syllables (with the stanza numbers), and `lyrics "X"  stanza "Y"`
  still sets the two apart. An old key is refused as unknown (LYS8004), and the message
  lists the keys.
- **`layout { marks stacked|beside }` is `layout { markTempo stacked|beside }`.** It
  arranges a boxed label and the metronome mark at the same bar; `marks` read like the
  `fonts` group of the same name. The old key is refused (LYS9101).
- **`@ho` and `@po` warn as unknown annotations.** They were retired before 0.3.0, but the
  checker still accepted them, so `c4@ho` compiled without a word and drew no H — nor did
  the `.ly` twin or MusicXML carry it. Write `@hammerOn` / `@pullOff`.
- **Names are case-sensitive.** Every annotation name and every `fonts` / `layout` /
  `paper` key (and the fonts words `as step size bold italic regular serif sans`) has one
  spelling: `@hammeron`, `@Staccato`, `@FiguredBass(6)` and `barnumbers` used to be read,
  and are now refused as unknown — nothing drawn or exported — with the right spelling in
  the message (`Names are case-sensitive: write '@hammerOn'.`).
- **A name of several words is camelCase, even where LilyPond's is not.** `@upbow`,
  `@downbow`, `@shortfermata`, `@longfermata` and `@reverseturn` are now `@upBow`,
  `@downBow`, `@shortFermata`, `@longFermata` and `@reverseTurn`, like `@laissezVibrer` and
  `@hammerOn` beside them. The old spellings are told the new one. The `.ly` twin still
  writes LilyPond's `\upbow`.
- **Value words are case-sensitive too.** The words inside an annotation's parentheses
  (`@notehead(triangle)`, `@bend(full)`, `@pluck(p)`, `@feather(right)`,
  `@arpeggio(bracket)`, the `x` / `o` of `@diagram(x32010)`, the `s` / `f` / `n` of
  `@figuredBass(6 s)`), paper size names (`size a4`) and units (`210mm`) are lowercase
  only; `@notehead(TRIANGLE)`, `size A4` and `210MM` are refused with the spelling to
  write. Free text (`@text("Dolce")`) and chord symbols (`@chord(Dm)`) keep their case.
- **`@chord`'s argument is read as words: `@chord(C 7)` is C followed by a shape, not C7.**
  The words after the symbol are its chord diagram's shapes (below), so a space no longer joins
  the symbol back together — write `@chord(C7)`. `@chord(C 7)` names C and warns (LYS1038) that
  `7` is a shape no tuning has strings for; `@chord(C m7)` warns that `m7` is neither a shape
  nor a tuning. No book in the repository or the test corpora wrote a spaced `@chord(…)`.

### Chord diagrams

- **A chord whose shape is written draws a chord diagram under its name**: `c4@chord(Cm7 x3x546)`
  on a note, `F(133211)` in a `chords` row (over a staff, or on a lead sheet with no staff),
  `@chord(x32010)` (the name derived from its notes) — between the name and the staff, side by
  side, with the row's spacing and the bars widened for them. A name alone (`@chord(G)`, a row's
  `G`) draws none. A shape is one character per string from the low string (`x` muted, `o` or
  `0` open, a digit the fret); it goes to the tuning with as many strings, so one chord can
  carry several: `F(133211 2010)`; a tuning word binds a shape by name when two tunings have
  the same string count: `F(guitar 133211 guitardropd 333211)`. `@diagram(…)` is unchanged and
  always draws.
- **Frets 10–15: a shape writes a two-digit fret with a `-` on each side** — `8xx88-11`,
  `xx-10-12-13-11`, `8-10-10-888`: `@chord(Cm xx-10-12-13-11)`, `@chord(8-10-10-888)` (named
  from its frets), `Cm(8xx88-11)`, `@diagram(x-15-13-12-13-x)`. A shape holding `-` is read in
  segments (the parts between the dashes): exactly two digits are ONE fret, 10–15; any other
  segment is one character per string — so the chord-chart spelling with a `-` between every
  string reads the same (`x-x-10-12-13-11`, `F(1-3-3-2-1-1 2-0-1-0)`). Frets 10, 9, 9 are
  `10-9-9`: a `99` segment is fret 99 and `09` no fret, and both warn naming the split. A shape
  routes by its string count (`8xx88-11` is six); one without `-` is one character per string,
  as before. Lower case only; a leading, trailing or doubled `-`, a fret above 15 and an
  upper-case shape warn (LYS1038 / the case fix), naming the fix. The page, the twin (terse
  markup and `FretBoards` tables) and MusicXML carry the two-digit frets. The editor's step now
  walks shapes at frets 10–15 too (it used to skip them) and writes a `-` only around the
  two-digit frets (and between two lone single digits: `10-9-9`), and only when a fret needs it.
- **The diagram's tuning** is the score's `layout { chordDiagrams TUNING }` — a tuning word,
  the ones a tab's `tuning` takes (`guitar`, `ukulele`, `mandolin`, `guitardropd`, …) — else
  the instrument of the part when it is fretted (the part an `@chord`'s note is in; for a row,
  the staff it stands directly above), else the guitar. `chordDiagrams none` draws no diagram
  at all, so one source makes a piano score and a guitar score, or a guitar score and a
  ukulele score.
- **`chordDiagrams all` draws a diagram for EVERY chord.** The scope word `all`, after the
  tuning (`chordDiagrams guitar all`, `chordDiagrams ukulele all`) or alone (`chordDiagrams all`:
  the tuning as when unset), makes every chord name in the score draw one — the `chords` rows'
  entries and every `@chord`, a bare `@chord` by the name it derives: the shape written for the
  tuning, else the usual shape (below). A chord with no shape at all on the tuning (C13 on the
  ukulele) draws none and warns once per chord and tuning (write its shape). `none all`, `all
  guitar` (the tuning comes first) and a word written twice are errors. Without `all`, only
  written shapes draw, as before. The twin writes a `FretBoards` entry for every chord of such a
  score (one-shape tables, so LilyPond draws exactly the page's shape) and the `@chord` markup
  for every name; MusicXML a `<frame>` for every drawn diagram.
- **A shape table in the layout lists the chords that draw.** After the `chordDiagrams` words,
  a table in braces — `layout { chordDiagrams guitar { Cm7 x35343  G  section Chorus { C x35553
  } } }` — names the chords that draw a diagram wherever they are named (a `chords` row, an
  `@chord`, a bare `@chord` by the name it derives) and the shape each draws, written as a row
  writes shapes after its symbol (`F 133211 2010`, `F guitar 133211 ukulele 2010`); a name
  alone draws the usual shape. A `section NAME { … }` block's entries apply to the chords
  written in that section (an `@chord`'s note, a row's bar, a by-part row's inner section), the
  rest to the whole score. Strongest first: the shape written at the chord, the section's entry,
  the song's, then — under `all` — the usual shape. The table follows `all` (`chordDiagrams all
  { F xx3211 }`) or stands alone (`chordDiagrams { C }`); `none` takes none. A symbol that is no
  chord, a bad shape, a shape before any symbol and a chord listed twice (the last wins) are
  warnings and the rest of the table stands; a section nothing declares warns; a table shape
  that disagrees with its chord is LYS1039; a chord listed alone with no shape on the tuning
  warns as in an `all` score. The twin's `FretBoards` context now appears under a row when some
  entry DRAWS (written or listed) and is left out when none does — a row with a written shape
  whose symbol is a degree or does not parse used to get a context of silent slots; MusicXML
  nests the listed shape's `<frame>`. Inside the table the editor's completion offers the chords the
  file names that the table does not list yet, then the key's, and `section` (then the file's
  section names); until now it offered the notes of a music block there.
- **A capo: `chordDiagrams guitar capo 3`.** The music still writes the sounding chords (`Eb`,
  `@chord(Eb)`), and everything a player reads follows the capo: every shape is the shape
  PRESSED above it (`Eb(x32010)` is the C shape, and LYS1039 checks it against the pressed
  chord; the usual shape is the pressed chord's; the table's shapes are pressed shapes); the
  printed name is the pressed chord's — `C` for `Eb`, `G` for `Bb` — spelled in the key that
  many semitones below the key at the bar (in E major at capo 3 a sounding `G#m` prints `Fm`);
  "Capo 3" stands at the score's head on the header's instrument line; a `chord(Eb x32010)`
  item sounds three semitones higher. `capo 0`, a fret above 11, `none capo 3` and `all capo 3`
  (the capo comes first) are errors. New key **`chordNames shape | sounding | both`**: what a
  name shows under the capo — the pressed chord's (default), the sounding chord's, or both,
  `E♭m7 (Cm7)`, each name with its own raised quality. The `.ly` twin writes the pressed chords
  into `\chordmode` and `instrument = "Capo 3"` in its `\header` (under `sounding` the sounding
  chords; under `both` each name is set by a small `chordNameFunction` that names the sounding
  chord and then, in brackets, the pressed one, so LilyPond prints `E♭m7 (Cm7)` too);
  MusicXML's `<harmony>` stays the sounding chord, its `<frame>` the pressed shape; the MIDI
  plays the sounding music.
- **The chord list: `layout { chordList true }`.** Every chord the score names — its `chords`
  rows and every `@chord` — once, in order of first appearance, at the head of the score under
  the title, each as the name the score prints over the diagram it draws there (a chord that
  draws none in the score shows its usual shape; `chordDiagrams none` lists the names alone;
  under a capo the pressed names and shapes). The cells stand in the fewest rows that fit the
  line with as nearly equal counts as those rows allow (16 chords where 12 fit a row make 8 +
  8), each row centred on the page. The `.ly` twin writes the rows as `\markup` lines of
  `\center-column { "NAME" \fret-diagram-terse … }` before the score.
- **The ukulele's shapes beyond LilyPond's table.** Lily#'s order now lists shapes on the
  re-entrant tunings too (the ukulele's high G, a banjo's drone) — the same rules less "the
  lowest note is the root", which the lowest string cannot promise there. Measured: with that,
  the order opens on LilyPond's own ukulele shape for every chord tried (C `0003` of 39, Am
  `2000` of 38, F `2010` of 23, G7 `0212` of 19). So a chord the ukulele table lacks now has a
  usual shape (Cmaj9 `4203`), `Ctrl+Shift+Up`/`Down` steps on a ukulele part, and the hover
  counts the order there; a chord no rule can voice on four strings (C13) still has none.
- **Warnings (LYS1038)** about written shapes: a shape of the wrong length, a word that is
  neither a shape nor a tuning, two unnamed shapes of one length, a tuning given two shapes; a
  symbol-less `@chord` whose shape is miswritten (`@chord(x3a010)`) now gets that warning rather
  than "unknown annotation". A name with no shape is not warned about (save in a
  `chordDiagrams … all` score, above).
- **A written shape is checked against its symbol (LYS1039).** `@chord(C x02210)` warns
  *'x02210' sounds A C E, which is Am, not C (A is not a tone of C) - write @chord(Am x02210) or
  another shape*; `@chord(C7 x3201x)` *lacks B♭ (the 7th) - fret B♭ or write another shape* —
  one warning per shape listing every problem, in `@chord` and in rows, on the tuning each score
  routes the shape to (a shape no score uses is not checked). Two rules: only chord tones (for
  X/Y, plus Y), and every tone but the root and the perfect fifth (for X/Y, a Y that is
  neither). The bass is not checked — an inversion is an ordinary shape. A symbol-less
  `@chord(x32010)`, `@diagram` and a Roman degree in a row are not checked.
- **Nine of LilyPond's predefined shapes are left out** of Lily#'s tables because they would
  warn — seven sound a note outside their chord (guitar D♯m/E♭m `xx4341`, Faug `xx1443`, Baug
  `x3200x`; ukulele Bsus2 `5122`; mandolin C♯aug/D♭aug `x630`), two lack the diminished fifth
  (mandolin C♯dim7/D♭dim7 `3210`, only B♭ and E). Those chords take the first shape of Lily#'s
  order instead (the ukulele's Bsus2 has no usual shape), on the page and in the `.ly` twin.
  Every other predefined shape passes the check.
- **The editor writes the shapes** (see the VS Code extension's changelog): `Ctrl+Shift+Up` on
  a chord adds its usual shape — LilyPond's predefined one (the guitar's 136 and 17 ninth
  chords, the ukulele's 306, the mandolin's 204, less the nine above: C `x32010`, F `133211`, Cm7 `x35343`; ukulele
  C `0003`), else the first of Lily#'s order (at least three strings, chord tones only, the
  root lowest, a span of four frets, four fingers — *Chord Diagrams* in the syntax reference).
- **A diagram reaching past the 4th fret is shifted as LilyPond shifts it.** `x35343` is drawn
  from the 3rd fret with a `3fr` label; the grid used to stay at the nut and drop its 5th-fret
  dots. A shape spanning five frets draws five rows.
- **The twin and MusicXML carry them**: under a `chords` row with a written shape the `.ly`
  twin writes a `FretBoards` context over the same chord music (each written shape as a
  one-shape table, a silent slot for every chord without one, `stringTunings` for a tuning
  other than the guitar's); an `@chord`'s diagram is the note's `\fret-diagram-terse`, under
  the name.
  MusicXML nests an `@chord`'s `<frame>` — with `<first-fret>` when shifted — in its
  `<harmony>` (MusicXML does not export `chords` rows yet). The MIDI is unchanged.
- **`chord(SYMBOL SHAPE)` writes a shape's notes.** `chord(C x32013)1` is a chord of each
  string's open pitch plus its fret — C3 E3 G3 C4 G4 on a guitar — on the tuning of the part
  that plays it (its fretted instrument, else the guitar), every note with its string number so
  a `tab` shows the shape. The pitches are absolute (octave marks after the `)` are an error,
  LYS0035) and written the way the part writes a sounding pitch (a guitar part an octave up);
  the note after it reads its lowest note. It takes a chord's tail (duration, dots, ties,
  slurs, beams, scripts, dynamics, tuplets, grace). Chord tones are spelled from the symbol
  (Cm7's E♭ and B♭). It draws no name itself; a bare `@chord` on it names the item and draws
  its shape. The shape is required: `chord(C)` warns (LYS1040) and keeps its time as a spacer.
  The MIDI plays it, MusicXML writes each note's `<string>`, the `.ly` twin writes the chord
  out with `\5`…`\1`. `chord` is now reserved in music — a phrase cannot be named it (none in
  the repo's or the Lab corpora's 1,206 books was). A phrase whose body opens with the item
  hands its lowest note on after the reference, as the item does (the reference's marks do not
  move it); it used to anchor on the body's next note, so editing the phrase moved the music
  after it.
- **`chord(…)` in `<< >>` is spread.** `<< chord(C x32010) >>2` plays the shape's notes one
  after another, lowest first, dividing the half into five (5:4) — a written-out broken chord
  of a guitar shape — and mixed with other members (`<< c chord(G 320003) e >>`) its notes
  take their places in the sequence. Each note is absolute with its string number; first in
  the group, the shape is the root (the next note reads from its lowest note). A share dot
  after it holds its last note, a `(` after it starts the bow on its first. The page, MIDI,
  MusicXML and the LilyPond twin (`\tuplet 5/4 { c8\5 e8\4 … }`) agree. It used to be a syntax
  error. The chord-track harmonizer also counts a shape's (and a degree chord's) notes now —
  a bar of them used to read as a rest.

### Editor

- **Split Sections to Match a Part.** When one part of a file grouped by part has cut a
  section into several (`vn1: section A` of 16 bars + `section B` of 121) while the others
  still write the passage in one `section A` of 137 — the "not the same length everywhere"
  warning (LYS2007) — the new editor command, also that warning's quick fix, cuts the other
  parts' `A` (and the section's chord rows and lyrics tracks) at the same bars into the same
  sections and makes every form play `A B` where it played `A`, inside repeats too. When two
  parts subdivide the section differently it asks which to follow. It carries on to every
  section still split differently — a part holding several of the followed part's sections in
  one (a double bass with all of `A`…`H` in `A`) is cut too, from whichever warning it was
  started — and refuses the whole plan if any part of it is refused, so no split that leaves
  a section long in one part is offered; anything still not the same length is named. At each cut the new
  section's first note gets the octave marks and the note value the section boundary would
  otherwise reset, and the meter, key and clef in force are restated. The rewrite is compiled
  and checked before it is offered — every part it cuts sounds exactly as before (MIDI, part
  by part) and writes as many bars, the warning is gone, no error is new — and one
  confirmation shows the plan (*Follow vn1: A 16 + B 121 bars. Split A in vn2, va, vc and cb
  after bar 16 → A, B. Form main: A → A B.*). A slur, phrasing slur, tie or hairpin across a
  cut is kept — carried into the new section, which every form plays next — unless it is still
  open at that section's end. A manual beam, a pedal or other span across a cut, a cut that
  falls mid-bar, or the section played as a repeat ending is reported with where, and nothing
  changes. A file grouped by section is not supported yet (regroup it by part first).
- **Typing aids: a digit typed on a note, rest, chord or slash note always leaves a valid
  duration** (1 2 4 8 16 32 64 128). It extends the digits there only when that makes a
  duration (`c1|` + `6` → `c16`) and replaces them otherwise: `c1|` + `2` is now `c2`, no
  longer `c12` (a 128th is pasted, typed with the aids off, or finished by typing `8` after a
  `12`). A `3` or `6` that extends into nothing completes to 32 or 64 (`c4|` + `6` → `c64`).
  `5`, `7`, `9` and `0` leave the note unchanged, with a status-bar hint, where they used to
  write `c45`. Slash notes take the aids too (`/4|` + `8` → `/8`, was `/48`). With several
  cursors, an octave mark, digit, dot, `\` or `@` is applied at each cursor on its own note,
  in one undo step. A new setting, `lilysharp.typingAids.enabled` (default on), turns every
  typing aid off.

### Form endings

- **An ending may hold several sections.** `form main { |: A [1. B C] :| [2. D] }` plays
  A B C, then A D: the sections play in order under one bracket, which spans all of them.
  Each is written as in the form body (`[1. ~B C']`), ranges and lists stay (`[1-2. B C]`),
  and the MIDI, MusicXML (one `<ending>` from the first section to the last) and the
  LilyPond twin (one `\alternative` branch) follow. A slur may run from one section of an
  ending into the next. Without its `]` an ending runs up to the `:|` after it (see the
  breaking change on unclosed endings above). Split Sections now splits a section
  played as an ending (`[1. A]` → `[1. A B]`) instead of refusing. An undeclared section
  named in an ending is now reported as undefined (it was dropped in silence).
- **An ending's list runs past two numbers.** `|: A [1,3,5. B] :| [2,4. C]` plays B on passes
  1, 3 and 5 and C on 2 and 4; the bracket prints "1. 3. 5.", MusicXML writes
  `number="1,3,5"` and the LilyPond twin `\volta 1,3,5`. The same holds for an ending in the
  music. A list stopped at two numbers, and `[1,3,5.` was a string of syntax errors.
- **A repeat needs a body.** `|: [1. B] :| [2. C]` (nothing before the first ending),
  `|: :|` and an empty run after `:|:` are errors (**LYS1041**) — they compiled in silence.

### Spans across a section boundary

- **A slur, phrasing slur, tie or hairpin may run from one section into the next.** One still
  open when a section ends is carried into the section the form plays next and must end there
  (`section C { … f( || } section D { g4) … }`). It is checked per form, per part and per
  play, in every form a score plays — so `form main { C D C E }` carries C's slur into D the
  first time and into E the second — and a span that breaks the rule draws nothing (a hairpin
  is cut at its own section's end) and is reported, **LYS4023**: carried in and not ended in
  that next section, a close at a section's start with nothing carried in, or carried into a
  section the part plays nothing in (warnings); a slur, phrasing slur or hairpin carried over a
  repeat sign, into or out of a volta ending, or over a jump mark (an error). Until now such a
  span was paired silently in printed order, over whole sections and repeats, and a reordered
  form drew it between the wrong notes. The running state a section starts from (octaves, note
  value, meter, key, clef, overrides) still resets at the boundary.
- **A tie may cross any repeat sign or ending.** It is carried to the first note of every
  section PLAYED after its own — the order the MIDI plays: the body again at each pass, that
  pass's ending, what follows the block — and each such note must repeat the tied pitch
  (LYS4007 otherwise). Where the section played next is also printed next the tie is an arc, as
  before; where it is not (back to `|:`, into a later ending) the tied note gets a hanging tie
  and the target an automatic repeat tie — drawn once, and not doubled where `@repeatTie` is
  written. The MIDI sustains the note on the passes the tie is carried on, MusicXML writes the
  stop on every note it reaches, and the LilyPond twin writes `\repeatTie`.
- **Other scores' forms are checked.** An unpaired slur, phrasing slur or tie in a form only a
  second score plays is now reported too, naming the form (`(in form 'other')`); only the first
  score's form was checked before.
- **A restatement draws nothing; `key!`, `time!`, `clef!` draw it anyway.** A `time`, `key` or
  `clef` that changes nothing (the key with its tonic) is no longer engraved, and the LilyPond
  twin omits it too. At a section's start it is compared with what the section before it left
  in force, so neither the restatement nor the boundary's reset is drawn — until now that
  printed a second, identical signature. A `!` right after the keyword (`key! ees major`,
  `time! 3/4`, `clef! bass`) draws it whether or not it changes anything; the twin writes a
  forced clef with `\set Staff.forceClef = ##t`, and the MusicXML export states a forced one's
  `<attributes>` again. The VS Code grammar colours the `!` with its keyword. A `!` after the
  value is still the dashed barline. A different value is drawn as before. The MusicXML import now cuts a section at every
  rehearsal mark a slur or hairpin does not run through — a mark where the meter or key had to
  be restated was left inside the section before it.
- **The MusicXML import keeps a restated key, time or clef, and cuts a section there.** A
  `<key>`, `<time>` or `<clef>` that changes nothing was dropped, so `key!` exported to
  MusicXML came back without its `!`. It now comes back as `key!`, `time!` or `clef!` and opens
  a section — except on a bar that opens a system (`<print new-system="yes">`), where it is the
  courtesy one a line's head prints. A piece with no rehearsal mark is also cut at every change
  of key, time or clef; where there are marks, the sections are the marks'.
- **`R` with no duration is a full-measure rest in any meter.** It lasts the bar it opens —
  five quarters in 5/4, the pickup's length in a pickup — so a 5/4 or 5/8 bar rest can be
  written at last (`R1` is four quarters, and no single note value is five). `R*3` is one
  three-bar rest; `R | R | R` stays three. It leaves the running duration alone, must open its
  bar (LYS2016, also under `time none`), and `R1`, `R2.*4` keep their meaning. The LilyPond
  twin writes `R1`, `R2.` or `R4*5`; MusicXML writes the bar's length (no `<type>` where no
  note value spells it); the MusicXML import writes every whole-measure rest as `R` / `R*N`.
  An `R` with no duration used to take the running duration.

### Fixes

- **A dynamic stays in the part it is written in, in the MIDI.** `@p` on one part's note
  quietened whichever part the exporter played next — the other part of the same section,
  or the first part of the next section — because one running velocity was shared by all
  the parts. Each part now carries its own, as LilyPond's Dynamic_performer does per voice:
  a part's `@p` stands into its later sections and no further; a part never marked plays
  at the default; a chord row accompanies at 70% of the default rather than of whatever the
  previous part left. Sixteen of the 998 books on hand sound differently, by velocity alone.
- **The LilyPond twin writes figured bass, note-head styles and a chord member's own marks.**
  `@figuredBass` becomes a FiguredBass context under the staff (a `\figuremode` stream read
  off the page, `<6 4>`, `6+`, `6-`, `6!`; a held `_` is written blank and said so);
  `@notehead(x|diamond|triangle|slash|xcircle)` becomes `\once \override NoteHead.style`; a
  chord member's string number and script stay on that member (`<a,\2 d>`, `<c-. e-.>`). All
  were "dropped (out of scope)", and a `@chord` or `@figuredBass` in a phrase was reported
  dropped while the twin printed it. The repository's twin warnings go from 8,910 to 801 (800
  are two benchmark books with no score).
- **`@text` stands beyond the dynamics, as in LilyPond.** A text is placed after the dynamics
  and hairpins of its side (LilyPond's TextScript priority 450 against their 250), so
  `c'4@text("dolce") d'@p` seats the p by the staff and the text under it; the p used to be
  pushed below the text. Above the staff the same.
- **The MIDI rests through every bar of `R1*N`.** It sounded one bar of `R1*3` where the page
  draws three, so everything after it came in two bars early.

- **A slur written in a grace body is drawn.** `grace { d'16( e'16) } c'4`, a slur from an
  earlier grace note to the main note (`grace { f16( g16 } a8)`) and one from a main note into
  the body (`c'4( grace { e'16) } d'4`) were reported as not drawn (LYS4020) and left off the
  page; they are now ordinary slurs at the grace notes' own place and size, bending down when
  they start in the body, as LilyPond draws them. In the upper voice of a `voice { } { }`
  the voice's side wins, for these and for an acciaccatura's or appoggiatura's slur, which
  was always drawn below.
- **The slur of an acciaccatura or appoggiatura is shaped as LilyPond shapes it.** The bow
  from a grace note to its main note (and a hand-written `grace { g16( } a8)`) was drawn
  from fixed clearances; it is now laid out as an ordinary slur. It reaches under the main
  note's head rather than stopping at its stem, clears what lies between, and ends on the
  head when the main note is beamed with the notes before it.
- **A rest in a grace body stands clear of the note before it, and a slur clears the rest.**
  `c'4 grace { r16 d'16 } e'4` drew the grace rest on the middle line, where it ran into the
  held note above it, and a slur over the run passed through the rest. As in LilyPond, the
  rest now moves up off the note that still sounds at the grace's moment (two spaces over
  an `e`, five over an `e'`), and the slur arches over it. A slur over a single flagged
  grace note (`a4( grace { b16 } c'4)`) now clears its stem as drawn; it sat half a space
  low.
- **A slash note stays off the tab, in MusicXML and in the warnings.** Beside a `tab`, the
  MusicXML wrote each `/4` onto the TAB staff as a note with a display pitch, which readers
  fretted — MuseScore drew a "7" per slash where the page's tab is empty. The TAB staff now
  keeps the slash's time as a gap (`<forward>`), on a TAB-only staff too. The tab leaves a
  slash out as before, but no longer warns LYS5002 ("likely an octave too low") once per
  slash: a slash has no pitch. And every note on a two-staff part now names its `<voice>` —
  the first staff's is 1 — where it was left to the reader beside the second staff's 5.
- **A slash note on a tab staff is rhythm.** A tab that draws stems (`tab gt`) drew nothing
  for a slash but ran the beam over its place, so a group opening with slashes began in
  mid-air. A slash now draws its stem and beam there, with no fret number, its stem starting
  at the middle string as the slash stands on a staff's middle line. On the
  MusicXML's TAB staff, where a stem cannot stand without a note, a group with slashes is
  beamed over its fretted notes alone (one left alone is a flag).
- **MusicXML import reads octave lines and pedals, and marks each staff's own notes.**
  `<octave-shift>` and `<pedal>` were not read, so every `@ottava`, `@quindicesima` and
  `@sustain` was lost on import; they come back as the marks and their `@!` ends (a pedal
  stop ends the pedal that is down). A stop written after a bar's last note — where most
  programs write it — closes on the next note, the first one outside the line. In a part on
  two staves a dynamic, a text or a line now marks a note of its own staff: the left hand's
  `@f` used to come back on the right hand's note at the same beat.
- **Staff labels survive a MusicXML round trip.** The import now writes a part's printed
  `<part-name>` back as the staff's label (`staff pianoRH "Piano"`, on the first staff of a
  split grand staff); it used to drop every label. The export marks the name of a staff the
  page labels nothing `print-object="no"` — it writes the part's id there, which a reader
  printed and the import would now bring back.
- **Staff groups survive a MusicXML round trip.** `staffGroup` and `choirStaff` wrote no
  `<part-group>`, so a reader drew their staves unbracketed; every group is now written — a
  `staffGroup` a bracket with its bar lines through, a `choirStaff` one without, a
  `grandStaff` kept as two parts a brace — nested as written. The import reads them back
  (brace → `grandStaff`, bracket → `staffGroup`, or `choirStaff` when the bar lines stop at
  each staff), nested; it read none and every group came back as unrelated staves. A part
  whose name is a Lily# word — "Soprano", "S", "A" — is imported under an index name
  (`part1`): `part soprano` and `part s` do not parse, and the imported book failed to.
- **MusicXML carries the page's beams.** `lysc xml` wrote no `<beam>`, so a reader beamed by
  its own rule — MuseScore ran nine eighths of a 7/4 bar under one beam where the page beams
  by the beat. Each note now carries the page's beam levels (begin / continue / end and
  hooks).
- **A repeat around `voice { } { }` draws its voices.** `repeat percent 3 { voice { … } { … } | }`
  drew its first bar empty before the % signs, and `repeat unfold` lost both voices on
  every pass: the music gather took nothing from a voice block standing inside a repeat.
  A repeat written inside each voice was not affected.
- **A MusicXML grand staff is one part.** `grandStaff { staff rh  staff lh }` was two
  unrelated parts; it is now one part on two staves (`<staves>2`, a numbered clef each, the
  lower hand's notes on staff 2, its slurs, hairpins and octave lines numbered apart from the
  upper hand's), as a piano part is written. Importing it splits it back
  into two parts as before. Only a plain two-staff brace of two parts is merged; other
  groups stay separate parts. When the two staves are labelled apart
  (`staff rh "Right"  staff lh "Left"`), they stay two parts under a brace `<part-group>`,
  so both labels survive.
- **MusicXML names parts and sections as the page does.** A part's `<part-name>` is the
  label the page gives its staff (`part vo "Vocal"`, `staff rh "Right"`) rather than its id,
  and each section label the page draws is a `<rehearsal>` at the section's first bar
  (framed, or unframed under `layout { sectionLabels plain }`). The open and closed hi-hat
  (`hho`, `hhc`) and the other drums that carry a mark of their own write it too
  (`<open/>`, `<stopped/>`).
- **MusicXML says what the page draws, and no more, at the head of a part.** A drum part
  (`clef percussion`) carries no `<key>` — the page draws none, and MuseScore put the
  piece's sharps on the drum staff. A piece that states no tempo opens with a bare
  `<sound tempo="120">` for playback instead of a ♩ = 120 `<metronome>` the page never
  printed. A 4/4 is written `symbol="common"` and a 2/2 `symbol="cut"`, the C the page draws
  for them.
- **MusicXML carries a top-level lyrics track.** `lyrics words sings vo { … }`, placed by
  the score's `lyrics words`, reached the page but not the file — only lyrics written inside
  a section or part block did. Its syllables are now `<lyric>`s on the notes the page sets
  them under, with their hyphens (`begin` / `middle` / `end`) and extenders. A track the
  score does not place stays out, as it does on the page.
- **MusicXML carries the tab staff.** `lysc xml` left a score's `tab` out entirely. A part
  shown as `staff gt  tab gt` is now one part on two staves — notation, and a TAB staff
  (`<clef>` TAB, `<staff-details>` with the lines and open strings) holding the same notes
  with each one's `<string>` and `<fret>`; a part shown as `tab gt` alone is a TAB staff.
  The strings are the ones the page prints (a written `\N`, and the page's own fingering);
  a grace note is fretted as the page frets it, its written `\N` included; a note below
  the fretboard, which the page hides, has no fret. Importing such a file reads
  the TAB staff as the copy it is, not as a second staff of music.
- **MusicXML `repeat percent` is a complete measure repeat.** The `<measure-repeat>` start
  now sits on the first repetition and names its length (1 for `%`, 2 for `%%`), and the
  stop sits on the first bar after the run — also when that bar restates a clef, key or
  meter, or opens the next section, where it used to be dropped. A two-bar body is now a
  `%%` in the file too, where it was only written out.
- **Text on a multi-measure rest stands over it, as in LilyPond.** `R1*4@text("tacet")` (and
  `R1@text(…)`) is LilyPond's multi-measure-rest text: above the staff by default, centred on
  the rest, clear of its bar count. It printed below the staff at the bar's left edge, like
  text on a note; `.down` still puts it below, centred. The bar count itself stands 0.05 higher,
  0.4 over the staff line's ink as LilyPond puts it, where it stood 0.4 over the line's centre.
- **An ending written with its printed points is one clear error.** `[1.3. B]` (for
  `[1,3. B]`) stopped `lysc check` with "The input string '' was not in a correct format." and no
  diagnostic at all; it is now LYS0037, naming `[1,3.` and `[1-3.`, in the form and in the
  music alike. An ending whose number is missing (`[. B]`, as while typing one) and a repeat
  whose one list skips a pass (`|: A [1,3. B] :|`) no longer crash the check or the preview
  either — the first is the parser's one error, the second the usual LYS1043.
- **A click on a line's key signature goes where the key was set, after any edit.** When a key
  change typed into one section made a later section go back to the book's key, the preview's
  later lines kept pointing a click at the old declaration until the whole book was redrawn.
- **The editor's outline works in a book with a custom key.** `key custom fis cis` made the
  document outline (and the breadcrumbs that read it) fail for the whole book.
- **A rest in a grace group no longer breaks the page.** `c'4 grace { r16 d'16 } e'4@staccato`
  (a script on the note after a grace group with a rest in it) and `c'4( grace { r16 d'16 }
  e'4)` (a slur over one) are valid, and the page threw on both.
- **A duration that is no note value is reported, not a crash.** `c'3` (or digits read as
  durations while a line is typed) is LYS1004; several of them in a bar made every output —
  the check included — throw an arithmetic overflow instead of showing the error.
- **A `key` with nothing after it yet no longer breaks the preview.** The half-typed `key`
  (on the way to `key f major`) threw out of the page's collector, so the preview stopped
  at that keystroke; it is now just the parser's error.
- **Bars of multi-measure rest are as wide as LilyPond makes them.** A rest opening a line
  was spaced as if a bar line stood before it rather than the clef and time signature, so its
  bar came out narrower than LilyPond's (0.8 of a staff space for a treble staff in 4/4); a rest
  after a double or repeat bar line was spaced wider by the difference between that bar line
  and a single one. Inside the bar, a dynamic or chord written on the rest stands where
  LilyPond puts it (about 0.03 further right).
- **Ornaments and a few articulations sit where LilyPond puts them.** A script was set off its
  note by one fixed box — and every ornament by the same stand-in box — so a mordent stood
  about a sixth of a staff space too low, and a turn or trill a few hundredths. A script now
  clears the note head over the head's width and the stem across its whole width by its own
  outline, as LilyPond does.
- **Tab fret numbers are a little smaller.** The default is now 2.8 (it was 3.0): at 3.0 a
  number stood heavier than the note heads above it and all but touched the neighbouring
  strings. It is still larger than LilyPond's. `fonts { tab size 3 }` brings the old size back
  for one book.
- **A note just after a bar line no longer keeps room for an accidental that misses the bar
  line.** A sharp on a low F (below the staff) or a high one (above it) stands under or over
  the bar line, and LilyPond sets the note where it would set it with no accidental; Lily# put
  the accidental's whole width in front, about 1 staff space more. The bar line and the note
  are now kept apart as LilyPond keeps them, shape against shape.
- **Articulations on a tab staff sit where LilyPond puts them.** A turn, fermata, staccato or
  accent on a tab was set a fixed distance from the fret number, the stem or the staff edge,
  up to a staff space from LilyPond's place. It is now placed the way LilyPond places any
  script — clear of the stem that points its way and of the fret number, with its padding
  scaled by the tab's wider line spacing, and a staccato rounded into a space between strings.
- **A sloped tuplet bracket's ends and its gap for the number follow the slope.** The ends
  reached past the notes level and the line broke a fixed width around the number; both now
  run along the bracket as LilyPond draws it, and the gap is the number's width plus one staff
  space.
- **A tuplet bracket on a tab staff starts and ends at its stems.** The bracket's bounds were
  read with the notation staff's notehead geometry, so on a tab — where the stem stands at the
  fret digit's centre — each hook stood half a digit or more off the notes it bounds
  (bohemian-rhapsody.lys, score "tab2", bar 25). The bounds are now the tab stems' edges (or a
  digit's ink where the stem points away), the hooks reach 0.2 × the tab's line spacing past
  them and stand 0.7 × it tall, as LilyPond scales them by the TabStaff's `staff-space`, and
  the bracket takes the side the strings' stems point to. Its height is read off the tab too:
  the line stands LilyPond's padding (1.1) past the tab stems' tips and the fret digits,
  where it used to run through the stems of a down-stemmed triplet (bar 42 of the same score).
  A bracket around another tuplet now clears the inner one's bracket and number; a script
  under the bracket (a turn) is cleared; a covering beam on the bracket's side is followed;
  when the stems split evenly the side is LilyPond's (the head reaching deeper past the
  staff); and a beamed note counts its beam's direction. A tab triplet whose beam hides its
  bracket puts its number one padding off the beam, where it used to sit on it.
- **A whole-bar `R1` on a tab staff is one whole rest, hanging from the upper central
  string.** The tab drew the bar's own rest under the whole-bar symbol as well, and the symbol
  itself stood half a string high — its middle and staff positions were read in notation
  spaces on strings 1.5 apart, through the notation staff's line table — so a 4/4 bar showed
  what looked like a whole rest beside a half rest (bohemian-rhapsody.lys, score "tab", bars
  8–12). The symbol now takes the tab's own line spacing and string positions, as LilyPond's
  `church_rest` scales by the TabStaff's `staff-space`; the count of a longer run stands above
  the tab's top string, and its H-bar scales the same way.
- **An edit in a section the form plays again reaches every play in the preview.** Stepping a
  `chord(…)`'s shape (Ctrl+Shift+↑) in a section played six times redrew the first play's
  diagram and kept the other five; typing a note there did the same. The incremental render
  adopted its previous walk of the replayed section as if the edit lay past it — a replayed
  measure cites the form's position, its notes the section's — and now walks those plays
  again. A full render (`lysc`, the exports) was never affected.
- **The preview plays the score it shows.** With `score main "p2" { staff p2 }` picked, Play
  (and the audition keys) sounded every part of the file, p1 included; the request now names the
  picked score and the MIDI sounds that score's parts and form. The same rule reaches every
  `.mid`: a score's file (`song-p2.mid`, the preview's MIDI export, `lysc midi`) carries the
  parts that score engraves plus its bare MIDI-only rows (`click`), the way a `chords` row it
  does not place is already silent; a file with no `score` block still sounds every part. The
  timeline is untouched — a section the picked score's parts sit out still takes its bars — and
  the words of a part that does not sound leave with its notes. The capo a `chord(…)` sounds
  under is that score's too (`layout … chordDiagrams guitar capo N`); it was the first score's
  whichever score was written. The shape check (LYS1039) reads each score's capo the same way:
  `chord(Eb x32010)` in a book with a capo-3 score and an open one warns, since the open score
  sounds C — it used to read the first score's capo alone.
- **MusicXML says where the capo is.** Under `chordDiagrams … capo N` the `<frame>`s were
  the pressed shapes but the document never said so; every part that carries a frame now
  opens with `<staff-details><capo>N</capo></staff-details>`.
- **MusicXML keeps every part's bars aligned across the sections.** A part with no block
  for a played section now gets that section's bars as silence, under the section's meter
  and pickup, as the page and the `.ly` twin already did — its next section no longer
  follows its previous one directly — and a `chords` row over such a part keeps its
  symbols on those bars. A second part writing a section that states its own `time` now
  states it too, and no part repeats its clef at a section it did not change it in.
- **A note's `@text("…")` and `@mark("…")` reach MusicXML.** Both were dropped on export; the
  text is now a `<words>` direction at the note (below, or above with `.up`), the rehearsal
  mark a `<rehearsal>`. A text on a multi-measure rest stands on the run's first bar.
- **A chord row clears the raised "7" and the ♭ of the chord names under it.** The room a
  staff keeps above itself for its own `@chord` names was a flat cap height over the highest
  note anywhere in the system, so a `chords` row's finger numbers printed into a Cm⁷ below,
  and a high note in another bar pushed the row away for nothing. The room is now the names'
  own ink, measured where they stand.
- **A voice's side reaches its ties, slurs and half-ties under a written stem direction, and
  in a combined staff.** Inside `voice { } { }` a note with `@stemUp` / `@stemDown` lost the
  voice's direction for everything but the stem, so its `@laissezVibrer` / `@repeatTie`, tie
  and slur curved by pitch; on a `combinedStaff` the "one" and "two" voices never had it. They
  now follow `\voiceOne` / `\voiceTwo` as in LilyPond (the stem alone obeys the annotation).
- **The `.ly` twin's `voltaBracket N` counts the ending's own bars.** LilyPond's
  `VoltaBracket.musical-length` was written as N bars of the meter the ending opens in, so a
  `time` change inside the ending (or a bar shorter than its meter) ended the twin's bracket
  on a different bar from the page's. It is now the length of the first N bars themselves,
  summed across an ending of several sections.
- **The endings of a run a form-level `:|:` opens are endings to every reader.** In
  `form main { A :|: B [1. C] :| [2. D] }` the MIDI, MusicXML and the `.ly` twin already
  played and wrote C and D as the run's endings; the page played them so too but drew no
  bracket, and the checker warned (LYS6008) that no repeat opened them. The page now draws
  the brackets (a staffless score too, which also drew none of its form-level repeat bars),
  and the warning is kept for an ending nothing opened (`A :|: B :| A [1. B]`).
- **A staffless score draws the bracket of a `~` ending.** `|: A [1. ~D] :| [2. ~O]` in a
  score of chord or lyric rows only drew no ending: the tilde hides the section's label, not
  the bracket, as it has on a staff since 0.6.
- **A form ending's range or list — `[1-2. B]`, `[1,3. B]` — plays on the passes it names.**
  The MIDI played the first written ending on pass 1, the second on pass 2, and played the
  body as many times as there were endings, so `form main { |: A [1-2. B] :| [3. C] }` sounded
  A B A C (the same music written inline sounded A B A B A C). The tie carried along the played
  order and the split-bar exemption at a section boundary read the form the same way; all three
  now read the numbers, as the MusicXML `<ending number="1,2">` already did. The `.ly` twin
  writes such endings with their `\volta 1,2 { … }`, and Split Sections counts a `[1,3. B]`
  play twice. A pass past every number replays the last ending, as it always has inline
  (`|: A [1. B] :|*3 [2. C]` plays C on passes 2 and 3).
- **A tuplet — and everything else tied to a bar — in a lower voice of a later bar lands
  in its own bar.** In
  `voice { … } { tuplet 3/2 { … } … }` written anywhere but the first bar, the tuplet in voice
  2 (or 3, …) drew no bracket, and a `3` bracket appeared under the same voice's notes in an
  earlier bar — the bracket was counted from the start of the `voice` block instead of the
  start of the piece. The same miscount put everything else written in those voices that
  belongs to a bar in an earlier bar: a `<< >>` group's automatic bracket, dynamic, chord name
  and articulations; an `override` / `once override` / `revert` (so a colour set in bar 2's
  lower voice coloured bar 1's too); a `tempo` change; a `segno` / `coda` / `fine` and the
  other navigation marks; an inline volta `[1. … ]`; and the signs of `repeat percent`. The
  MIDI, the MusicXML `<time-modification>`/`<tuplet>` and the `.ly` twin's `\tuplet` were
  already right.
- **The MusicXML no longer adds an empty bar after `voice { … } { … } |`.** The bar line
  after a `voice` block was read as a second, empty bar, so each such bar was followed by a
  whole-bar rest (a two-bar piece exported four measures). A `||`, `:|` or `|.` written there
  now marks the block's last bar; `voice { … } { … } | |` still writes one empty bar.

- **A tie into the next section sounds and exports as one note in a book of several parts.**
  The MIDI kept one tie memory for the whole score, and every other part's block of the section
  overwrote it, so `c~ ||` into the next section's `c` played twice; the MusicXML forgot the
  open tie at every section boundary and wrote a `<tie type="start">` with no stop. The page
  drew the tie all along.

- **A section label, mark or volta bracket stands clear of a repeat tie or hanging tie.** A
  half-tie (`@repeatTie`, `@laissezVibrer`, and the automatic repeat tie a tie carried over a
  repeat draws) was in no vertical skyline, so the label at a second ending or at a section
  opened by `@repeatTie` was drawn on top of the tie. It is now inside-staff ink like any tie,
  as in LilyPond, and what stands above the staff is placed over it.

- **Repeat ties and hanging ties draw on every staff and in every voice.** `@laissezVibrer`,
  `@repeatTie` and the automatic repeat tie of a tie carried over a repeat were drawn only in
  the first voice of the first staff: in a second part, a piano's lower staff or a lower voice
  the room was kept but no tie was drawn. A lower voice's half-tie now curves down and an upper
  voice's up, like its ordinary ties (a written `.up`/`.down` still decides). A tab staff draws
  none, as in LilyPond (a tab used to draw one when it was the score's first staff). The
  MusicXML now writes them too, in every part and voice: `@laissezVibrer` as
  `<tied type="let-ring"/>` (on every note of a chord that carries it), `@repeatTie` as a tie
  stop; neither was exported before.

- **An `@chord` on a rest or a spacer draws.** `r1@chord(C x32013)`, `s1@chord(G)` drew
  nothing — neither the name nor the diagram, and said nothing — while `r1@diagram(…)` drew. A
  chord symbol belongs to the beat, not to a note: on a rest, a spacer or a multi-measure rest
  it now draws exactly as on a note (the name at that moment, the diagram under it by the usual
  rules), in the `.ly` twin's chord names and as a MusicXML `<harmony>` before the rest. A bare
  `@chord` there has no notes to name: it warns (LYS1020), naming the fix — write the name.

- **"Did you mean …?" is offered only for a close typo.** An unknown annotation is compared
  with the known names allowing a third of its length in edits (at most two; a swap of two
  adjacent letters is one), and a one- or two-letter name gets a suggestion only for a swapped
  pair (`@fs` → `@sf`). `c4@ho` said *did you mean '@sf'?*; it now says only that `@ho` is
  unknown. `@glisando`, `@acent` and `@tenuot` are still pointed at `@glissando`, `@accent`
  and `@tenuto`.
- **The stray-dot error no longer names an old annotation spelling.** `c4@finger.3` gets the
  ordinary *This '.' belongs to nothing* (LYS0023) and `@finger`'s own *takes its argument in
  parentheses*; the dot message used to add *write @finger(3) and @chord(c), not @finger.3
  and @chord.c*, a hint for an older Lily# spelling.

## 0.9.0

### Breaking changes

- **`lysc` names its outputs for the book and writes every score.** `lysc svg song.lys`
  writes `song.svg` for the main score and `song-<alias>.svg` for each other one
  (`score main "tab"` → `song-tab.svg`), and so do `pdf`, `png`, `midi`, `xml` and `ly`.
  It used to write only the first score, and `pdf`/`png` could not write the others at all
  — 92 of 332 bass books declare two to four scores. `-d <folder>` chooses where they go
  (default: the input's folder, made if missing); `--score <name>` still writes one, under
  the same fixed name. **`-o/--output`, the output argument and `--all` are gone**, so a
  file can no longer be given a name that says nothing about its book; an old script that
  uses them is told what to write instead. In a `--batch` list the TAB column now names a
  folder, and `-d` applies to the whole batch. `import` writes `<input>.lys` the same way.
  Two scores that would get one name (two unlabelled `score main`) are refused rather than
  written over each other.
- **In relative octaves, the note after a chord or an arpeggio reads the chord.** 0.8.0 let
  a chord pass through the chain, so the note after it was relative to the note *before*
  the chord: in `g1 | <c e g>1 | <f a c>1`, rewriting the `g1` as `f1` moved the second
  chord an octave while the first stayed put. The chord now hands on its anchor — the first
  member's bare letter (a mark on that member moves only that note), or the tonic for a
  degrees-only chord — shifted by any mark after the `>`, as it did before 0.8.0. A note right after a chord or `<< >>` can therefore land an octave away from where
  0.8.0 put it; nothing warns, so check such spots by ear or by the page. MIDI, MusicXML and
  the LilyPond twin follow the same rule.

### Engraving

- **A numbers-only tab is spaced by its hidden stems, as in LilyPond.** LilyPond's default
  tab draws no stems, but they are still there, zero-length, and they still take part in
  the optical spacing corrections from a half note up: a quarter moving to a higher string
  gets a quarter staff space more room, one moving lower a quarter less, and the gaps into
  and out of a bar line shift by up to 0.05. Lily# gave those stems nothing, which left
  every staff + tab bar end 0.02 short of LilyPond. Of 332 bass books 194 move by a few
  hundredths; none breaks its lines differently.

- **The first note after a bar line gets LilyPond's room when the staves differ.** LilyPond
  gives a down stem just after a bar line a little extra room, and it works this out once
  for each staff, measuring every staff's first notes against that staff's bar line before
  averaging the results. Lily# measured each staff's own notes only, so a staff + tab bar
  that opened on a down stem stood up to 0.014 too wide, and on a system opening with a
  repeat sign a down stem on one staff earned only half its room. Of 332 bass books 142
  move by hundredths; none breaks its lines differently.

- **A tab chord is spaced by the fret on the stem's far side, as in LilyPond.** A chord
  pairing a two-digit fret with a one-digit one took the wider fret's room after it; LilyPond
  reads the fret opposite the stem, so such a chord could stand up to half a staff space too
  far from the next note. No book in the repository or corpora changes.

- **Fret diagrams are LilyPond's size, stand side by side, and can be resized.** A chord
  diagram used to be drawn at about half LilyPond's size and was hard to read. It now has
  one staff space between strings and between frets, as in LilyPond. `fonts { fretFrame
  step ±n }` scales the whole diagram: `step +6` doubles it. Neighbouring diagrams no
  longer overprint each other. They sit side by side, and a bar too narrow for them gets
  wider. The `.ly` export writes the same size and spacing.

- **A fret diagram stands over its chord.** `@frame(…)` took the side opposite the stem
  like a staccato, so a low chord's diagram hung under the staff; it is above now, and
  `@frame(…).down` puts it below.

- **A note takes one dynamic.** In `c4@f@sfz` only the `f` is printed now, and the `sfz`
  warns (LYS4022) — LilyPond keeps the first of two as well. The page used to stack both.
  No book in the repository or corpora writes two.

- **An ottava that starts or stops inside a bar covers exactly the notes it spans.** An
  `@!ottava` on the last note of a bar used to leave the whole bar at written pitch and
  end the bracket at the bar before; an `@ottava` in the middle of a bar moved the notes
  before it too. Now, as in LilyPond, the octavation begins and ends at the note, and a
  bracket that stops inside a bar ends just after the last note it covers. 3 books in the
  repository change, among them the website's spanner example.

- **A beam runs across a clef or key change, and clears it.** As in LilyPond, eighths on
  both sides of a mid-measure `clef` or `key` are beamed together, and the beam is placed
  clear of the new clef or signature; Lily# used to break the beam in two at the change.
  2 books in the repository change (LilyPond's own loose-column regression input, whose
  beam now matches LilyPond's to the hundredth).

- **A key change in a score of drum or tab staves alone takes no room.** Neither staff
  prints a key signature, as in LilyPond, but the change still pushed the next bar's notes
  up to eight staff spaces right. Beside a pitched staff it keeps the room that staff's
  signature needs. No book in the repository or corpora changes.

- **The room a fermata or ornament takes in the note spacing is the room LilyPond gives
  it.** A lower voice's fermata was reserved on voice 1's note with the same number, above
  the staff, and a beamed note's fermata was reserved beside its own stem rather than above
  the beam's reach — each could push the next note 0.47 staff spaces right where LilyPond
  does not, and a lower voice's grace note 0.37 left. No book in the repository or corpora
  changes.

- **In a `voice { } { }` span every articulation follows its voice.** Voice 1's marks go
  above and voice 2's below, whatever the mark's own habit — as LilyPond's `\voiceOne` /
  `\voiceTwo` set them; an explicit `.up` / `.down` still wins. Voice 1's staccato and
  accent used to sit between the voices, voice 2's fermata, trill and bow marks stood over
  voice 1, and a drum kit's closed hi-hat `+` in the lower voice landed on the cymbal
  above. Of 94 books with a voice span, 1 changes (one snapshot).

- **A drum staff prints no key signature.** LilyPond's DrumStaff has no key engraver, as a
  tab staff has none; Lily# excluded only the tab staff, so a keyed score printed its
  signature — and its key changes — on the drum staff of every system, and a drum-only
  score reserved room for one.

- **A pedal bracket that crosses a line break is drawn on both lines.** The whole bracket
  was drawn on the line it starts on, with the release read from the next line's
  coordinates: a stub two staff spaces long closed by a hook, and nothing on the next
  line. Now, as in LilyPond, it runs hook-less to the end of the line and continues from
  the start of the next one, and a pedal change on the first note of a line is notched
  there. Of the 25 books with pedals in the repository and corpora, 13 change (the
  nocturne sample among them); no snapshot does.

- **A pedal written on a member of a `<< >>` group lands on that member.** Every member's
  mark was anchored at the start of its bar, so a pedal change on the third group of a
  12/8 bar landed on beat one and the bracket lost whole bars.

- **A slur may open right after `@ottava`.** `e8@ottava( d c b)` read the slur's whole run,
  its `@!ottava` included, as `@ottava`'s argument, and the notes vanished from the bar.
  For the names whose argument is optional (`@ottava`, `@quindicesima`, `@arpeggio`, a bare
  `@chord`) a `(` followed by a space now opens the slur; the argument is still written
  against the parenthesis (`@ottava(bassa)`).

- **The swing equation is LilyPond's.** `tempo 122 swing` drew two beamed eighths "=" a
  beamed dotted eighth and eighth under a "3" — a figure that is not a triplet — at a
  small size of its own. It now draws what LilyPond's `\rhythm { 8[ 8] } = \rhythm
  { \tuplet 3/2 { 4 8 } }` draws: a quarter and a flagged eighth under a tuplet bracket,
  at `\rhythm`'s size, measured from LilyPond to 0.01 staff space (`swing 16`:
  `16[ 16] = \tuplet 3/2 { 8 16 }`). The bracket rises above the metronome mark, so the
  mark is spaced a little higher, as LilyPond's is. **`tempo swing` with no number now
  prints the equation alone** (it printed nothing), and after a marking with no number
  (`tempo "Medium" shuffle`) it follows the marking. A swung value other than 8 or 16
  (`swing 4`) is reported, LYS0034. The LilyPond twin writes the same `\rhythm` markup.

- **Section labels and rehearsal marks are set in the regular weight, not bold**, and a
  label with a descender ("Bridge") keeps half the gap under it and has as much frame
  above its capitals as below its baseline, so its letters sit centred in the frame
  with the descender hanging into the lower half. Regular is also what the LilyPond twin draws; the section-label
  page ledger point moves to within 0.0001 of LilyPond. A score's `fonts { mark … }` style
  still wins. 236 snapshots change (the labels only; no book changes its systems).

- **A section label's frame stands the same distance off the text on all four sides.** The
  frame's height wrapped the text's ink but its width wrapped the advance, so the side
  margins also carried the letters' side bearings and read wider than the top and bottom
  ones. Both now wrap the ink, and the text is centred by its ink. Rehearsal-mark boxes
  follow the same rule. A deliberate departure from LilyPond, whose box takes a text's
  width from the advance. 236 snapshots change (the frames, and what is spaced against
  them); no book changes its number of systems.

- **A chords row no longer drops "D.S. al Coda" under the system.** A jump text (and any
  below-staff mark) under a book with chord names hung the chord row's depth — about 5.4
  staff spaces — further below the bottom staff than LilyPond places it. The system's
  bottom and the lyric lines are now measured from the same staff the mark is.

- **`to coda` is drawn "To 𝄌" again, clear of the next section's label.** The departure had
  been the bare coda sign, as LilyPond's `\codaMark` draws it, which read as the coda itself.
  It now stands 1.0 staff space left of the label's box, whatever the label's width; it used
  to stand a fixed 4.0 left of the label's centre, so a wide label like "Bridge" covered it.
  The sign no longer overlaps the "o" of "To": it stands 0.3 staff space past the word
  (not a whole word space), its centre on the middle of the word's ink, and at the same
  size as the coda sign at the arrival (it was drawn at 0.8 of it). The `.ly` export writes the words again.

- **A beamed grace group on a lower staff stays on its heads.** Every system is spaced against
  its own staves, so a lower staff stands at a different depth below the system top from one
  system to the next; the grace's beam and its stems were drawn at ONE score-wide depth while
  the heads followed their system, so on the other systems the beam floated above or below the
  heads, further the more that system's spacing differed. The grace now reads its own system's
  staff. Of 942 books, 1 changes (its grace beams only).

- **The first system is indented as LilyPond indents it — 15 mm — whether or not a staff
  names an instrument.** Lily# used to indent only a score whose staves carried names and
  set every other first system flush left, so every nameless book's first line differed from
  LilyPond's. A `paper { indent … }` is honoured as written (0 included), and `size` scales
  the indent with the side margins, as LilyPond's `set-paper-size` does. Every nameless book
  moves its first system right by 8.54 staff spaces (213 snapshots; no snapshot changes its
  number of systems).

- **A natural clears a down stem.** LilyPond places accidentals against the note heads AND
  their stems; a down stem reaches below its head, where a natural's lower-right stroke meets
  it, so a natural on a stem-down note stands 0.0117 staff space further left. Flats, sharps
  and the doubles are unaffected. Among the LilyPond twins, *Universe* now breaks into the
  same 33 systems.

- **Line breaking prices a line's edges.** A line that opens on a meter or key change
  engraves it in its prefix, and the line before it prints the courtesy signatures after its
  last bar line; a score's first line is indented for its instrument names. The line breaker
  now reserves all three, as the layout does, so it no longer packs a line the layout must
  squeeze harder than it priced.

- **A tab staff's notes are spaced as LilyPond spaces them.** LilyPond takes one spacing
  wish per voice and averages them, a TabVoice's included, and a tab voice's wish reads its
  fret digit where a staff's reads a notehead — with no stem correction when the tab prints
  numbers only. Lily# priced the tab voice as a copy of the staff's, so a staff+tab system
  was spaced as the staff alone: 0.29 staff space wide on the first sixteenth of a dotted
  pair, 0.23 before the bar line. The tab's wish now reads LilyPond's own digit (the size
  the digits are drawn at is unchanged, and still keeps them apart), and on a measured bass
  line every gap between notes matches LilyPond to four places. Every book with a tab moves
  a little — 276 of 942 in the sweep, 37 snapshots; against the LilyPond twins of the tab
  corpus, 419 of 457 scores now break into systems the same way (413 before), and the
  three tab-slur geometry points recorded as caused by the digit size are exact.

- **A dotted eighth that starts off the beat is not beamed to the eighth after the beat.**
  LilyPond asks whether an automatic beam ends at a new note twice — first with the shortest
  note the beam held before it, then with the new note counted — and the first question can
  close it: in 4/4 a lone dotted eighth has no beaming exception, so the beat ends the beam,
  and `r16 g8. g8` keeps both flags. Lily# asked only the second question and joined them
  over the beat. 9 of 942 books in the sweep change; none of the snapshots does.

- **An accidental makes room only where its own ink reaches.** Spacing pads each note
  column's outline by a small margin above and below, as LilyPond does, but LilyPond adds the
  accidentals afterwards from their bare shapes; Lily# padded them with the rest, so a natural
  held its neighbour away even when it stood clear of that neighbour's head. A dotted-eighth
  bass line in Universe now compresses to within 0.012 of LilyPond's width where it was 0.14
  wide. 42 of 942 books in the sweep move slightly; 2 snapshots.

### MIDI, MusicXML and the LilyPond twin

- **An instrument named with a display label keeps its preset in the MIDI.** For
  `instrument bass5 "bass"` the MIDI read the value as one unknown preset, `bass5"bass"`, so
  it played the part without the bass's octave-down sound, and in relative mode from middle
  C's anchor instead of the preset's: an octave away from the page. It now plays like
  `instrument bass5`. One of the 332 bass books writes an instrument this way.

- **An octave-clef staff stands where the page does in the twin.** Under `treble_8` Lily#
  draws `g` where treble draws it and it sounds an octave down; the twin handed LilyPond
  the written pitch, which LilyPond's `treble_8` then drew an octave higher. The staff now
  gets the sounding pitch, as the tab already did. 22 books use an octave clef.

- **Fret diagrams reach the twin** as `\fret-diagram-terse` markups, over the chord (a
  chord member's diagram belongs to the chord there — LilyPond takes no text script on one
  note head).

- **The LilyPond twin reads `tab bass as full` as the tab of part `bass`.** It took the
  first plain name after `tab` — and `bass` reads as a clef word — so it wrote a tab staff
  of a part called `full`, in guitar tuning, with the road-map marks piled on one moment.
  2 books in the repository and corpora wrote their tab that way.

- **A chord row's bars in the twin follow the music's meter.** A chord row under a `time
  7/8` bar was written as a whole 4/4 bar, and LilyPond's bar check failed there.

- **A dynamic's `.up` / `.down` reaches the twin** as LilyPond's `^` / `_`.

- **A pedal change is written as the pedal again.** A second `@sustain` while the pedal is
  down already engraved as a pedal change; it is now the documented spelling (`g4@sustain`),
  with `@!sustain@sustain` kept as the same thing written out. The LilyPond twin writes it
  as `\sustainOff\sustainOn` — a bare second `\sustainOn` drew no notch in LilyPond's
  bracket — and likewise for `@sostenuto` and `@unaCorda`. The nocturne sample uses it.

- **No false warning after a phrase reference in `octave absolute`.** The export warned that
  "a note follows the phrase reference … check that stretch by hand", which is a relative-
  mode difference; in absolute mode the reference is inlined and the stretch is exact.

- **A keyed book with a drum part exports a twin LilyPond accepts.** The drum part's
  `\drummode` block carried `\key f \major`, where LilyPond reads the tonic as a drum name
  and refuses the file. A drum part now writes no key (its staff has none on the page
  either).

- **A part named after a LilyPond keyword or command gets another variable name.** A part
  called `drums` became `drums = \drummode { … }` and `{ \drums }` — `\drums` is a LilyPond
  keyword, and the file was refused. Such a name now steps aside the way a duplicate does
  (`drumsVarTwo`), and so does one that would shadow a command the twin itself writes
  (`bar`, `mark`, `break`, …). No book on disk has either shape.

- **The twin of a named score engraves that score's staves.** `lysc ly --score NAME` and
  `lysc ly --all` wrote every score's twin with the file's FIRST `score` — its staves, its
  `fonts` and `layout` plans, its instrument names — and only the form followed the name. Two
  scores on one form therefore came out the same: a tab book's `score main "tab" { tab … }`
  twin had no TabStaff, and a chords-only `score main "grid"` carried the melody and the
  lyrics of the first. Each twin now reads its own score, and so does the preview's
  export-all. The single export from the preview still writes the primary form; `.mid` and
  `.xml`, which take the form's music and no staves, are unchanged. Of 942 books, 86 write a
  different twin for a second or later score; no first score's twin changes.

- **A `chords { }` row sounds in the MIDI.** A row the score places (`chords NAME`, or
  `staff … with chords NAME`) plays on a track of its own, `NAME (chords)`, after every part's
  track (so no part changes channel) and at 70% of the velocity in force; a row no score places
  stays silent. Each symbol sounds over exactly the span the page prints it over — `.` holds
  it, `r` (N.C.) is silence, every written symbol strikes again — and a section's pickup
  shortens the row's first bar as it does the parts'. A symbol voices no octave, so Lily# uses
  one voicing everywhere: every tone from G3 up to G4, a slash bass an octave below
  (`G7/B` = B2 G3 B3 D4 F4; a quality Lily# does not know sounds its root). A chords-only
  section now takes its bars. **A chord row no longer delays the parts:** the rows' bar lines
  used to count as empty bars, so a section with a chord row could run long — *Greensleeves*
  played 13,200 ticks of silence between its verse and chorus. Of 942 books, 24 gain a chord
  track; the parts' notes change in 2 (those two delays), and nothing else moves.

- **A section's pickup shortens every part's first bar in the MIDI.** The first part played
  spent the `partial`, so every other part opened with a full bar and could run the section
  long: *partial.lys* (`partial 2`, a melody and an empty second part) played its second
  section 960 ticks late. Of 942 books, that one changes.

### Editor

- **Convert a file between absolute and relative octaves without moving a note.** The editor
  commands **Convert Octaves to Absolute** / **Convert Octaves to Relative** and
  `lysc octave --absolute|--relative` rewrite the `octave` directives and every note's
  `'` / `,` marks, and nothing else. The compiler itself decides what each mark must become —
  phrases, section references, chords, arpeggios, scale degrees and instrument anchors are
  all read the way the page reads them — and the result is compiled and compared note by
  note before it is written. A file with a note no score plays is left alone, since nothing
  says what octave that note means. All 419 books of the corpora and samples that compile
  with every note played convert both ways, and their MIDI is byte-identical before and after.

- **A file is "grouped by part" or "grouped by section".** These used to be "part-major"
  and "section-major" layouts, a term borrowed from matrix storage that says little to a
  musician (and "layout" is also a keyword). A file grouped by part writes
  `part bass { section A { … } }`; one grouped by section writes `section A { bass { … } }`.
  The editor command Convert Layout is now **Regroup (by part ⇄ by section)**, and the
  diagnostics and docs use the new names. Nothing about the files changes.

- **Regroup indents the closing brace of a cell that spans lines.** It was written at column
  0, so `section B { … }` inside a part seemed to close the part itself. It now closes under
  the line that opened it.

- **Punctuation in lyrics is text.** A `;` or `?` written against a syllable (`gent- ly;`,
  `are you?`) was reported as a stray character — an error — though the syllable kept it
  and the page printed it. Inside a `lyrics { }` body it is now simply part of the word.

- **A lyric track may sing a part named `bass` (or `treble`, `alto`, `tenor`).**
  `lyrics words sings bass { … }` was a parse error, though those four words are legal part
  names everywhere else and the score row `lyrics words sings bass` already accepted it.

- **A pedal's warnings speak of the pedal.** An unreleased `@sustain` was told to write
  `@!rit`, and a stray `@!sustain` said "no text spanner is open".

- **Each piece of a tempo mark clicks to its own token in the preview.** Clicking the note
  or "= 120" lands on `tempo |120` (the note on a written unit, `tempo |4 = 120`), clicking
  the swing equation on `tempo 120 |swing`, and the marking on its text; a caret lights
  only its own piece (the `16` of `swing 16` lights the equation). The whole mark used to
  carry one offset, so the swing jumped to the number and a caret on the number lit the
  swing as well.

- **After a tempo's number, completion offers the feel words.** `tempo 100 ` (or Ctrl+Space
  with the caret still on the 100) offers `swing`, `shuffle` and their sixteenth forms; it
  offered nothing before. Inside music (`c4 tempo 96 `) they lead the list and the notes
  stay in it, since a note may follow there instead. After the feel word
  (`tempo 120 swing `, `tempo swing `, or with the caret still on the word) completion
  offers `8` and `16`. `tempo ` itself now also
  offers `swing` and `shuffle` alone — the equation with no metronome mark.

- **The preview draws text in the faces the layout measured.** The preview loaded only the
  music font, so on a machine without TeX Gyre installed every title, lyric, tempo, chord
  name and label fell back to the browser's own serif and sans — narrower than what the
  layout had spaced them for (a section label's frame read too wide around its text). The
  preview and the AI-candidate view now load TeX Gyre Schola and Heros from the fonts the
  extension already ships with its language server.

- **A finding is reported once.** A validator that reads the collected score sees a section
  once per time the form plays it, and reported the same warning once per play — a lyric
  overflow in a chorus the form plays twice appeared twice in the Problems panel and in
  `lysc check`. Identical diagnostics (same place, code and message) are now reported once.

- **Hovering a chord shows its chord symbol, its degree and its pitches.** A chord, a `<< >>`
  arpeggio or a `q` — or any note inside one — hovers as the symbol a bare `@chord` on it would
  print, read by the same code, so the hover and the page agree; then its Roman-numeral degree
  in the key in force (what `as roman` prints); then the pitches it sounds, a chord's lowest
  first and an arpeggio's in the order they play. In C major `<d f a>` is `Dm (IIm) D4 F4 A4`,
  and `<f d a>` is `Dm/F (IIm/IV) F4 A4 D5`, which shows that the first member written is the
  bass. A chord whose notes name no chord still lists its pitches. A `chords { }` entry hovers
  the same way, with the pitches the MIDI plays for it (below) — `G7/B` is
  `G7/B (V7/VII) B2 G3 B3 D4 F4`, the slash bass first.

- **The completion popup in the music is narrow again.** VS Code widens it to its widest
  row's detail, and a handful of long explanations (`<< >>`, `cue`, `partial`, the page
  breaks, the diatonic chord rows) stretched it over the preview beside the editor — over
  the bars being typed. Each row's detail is now a short label (a chord row shows the notes
  it inserts); the explanation moved to the row's documentation, shown in the details panel.

## 0.8.0

Every part gets its own MIDI track and sound, phrasing slurs arrive, and a long run of engraving
work brings ties, beams, lyrics, marks and spacing onto LilyPond's own arithmetic, each change
measured against LilyPond 2.26.0. The editor's preview now re-engraves only what a keystroke
changed. Five things a 0.7.0 book could write now read or print differently or are refused;
they come first. Three are refused with a message; **two change a book without one**, so check
for them by eye: **a clef no longer moves pitch** (a bass part that relied on `clef bass` to read
bare letters an octave down now needs `octave 3`), and **a slur or tie holds its lyric syllable**
(a lyrics line that gave the notes inside a slur syllables of their own now shifts).

### Breaking changes

- **`volta` and `alternative` are ordinary words.** Neither is reserved any more, so a part,
  section or phrase may carry either name. LilyPond's `\repeat volta` is still refused where it
  is written — LYS0006 points at the form's `|: A [1. B] :| [2. C]` — by the word, not by a
  keyword. LilyPond's `\alternative { … }` after a `repeat unfold` / `percent` / `tremolo`,
  which the parser used to accept and every reader but the `.mid` dropped in silence, is now an
  ordinary error (an undefined name).

- **An instrument preset has one name.** Fourteen presets were second names for another —
  identical in clef, octave, tuning, transposition and, measured byte for byte, in the `.mid` —
  and are gone: `uke` (write `ukulele`), `acoustic-guitar` and `electric-guitar` (`guitar`),
  `bass-guitar` and `electric-bass` (`bass`), `5-string-bass` and `6-string-bass` (`bass5`,
  `bass6`), `double-bass` (`contrabass`), `french-horn` (`horn`), `piano-treble` and `piano-bass`
  (`piano-right`, `piano-left`), and `voice-soprano`, `voice-alto`, `voice-tenor` (`soprano`,
  `alto`, `tenor`; `voice-bass` stays, since `bass` is the bass guitar). Each is refused as an
  unknown preset ("Unknown instrument preset 'electric-guitar'"). The sound a name used to
  suggest — an electric guitar, a steel-string acoustic — is now written with `midiInstrument`
  (below).

- **A clef no longer moves pitch.** The octave a part's bare letters start from is its
  `octave N`, else its `instrument` preset's octave, else 4 — never its clef. Until now
  `part x { clef bass }` read a bare `c` as C3, and a mid-music `clef bass` or `cue bass { }`
  re-anchored the notes after it at octave 3 while keeping the previous letter, so the result
  depended on the note before the clef. A clef anywhere — part header, mid-music, cue, or a
  score's `staff bass x` — now only changes how the staff is drawn, as LilyPond's `\clef` never
  moves `\relative`, and the LilyPond twin writes the source's own octave marks. To keep a bass
  part's register, write `clef bass octave 3` (or use an `instrument` preset). A score's
  `staff bass x` also draws the bass clef it names; it was silently ignored before.

- **A section's label is hidden only at the form reference.** `section ~A { … }` is an error
  (LYS0033) that names the spelling to use instead: write the `~` on the reference,
  `form main { |: B [1. ~A] :| [2. C] }`. Until now the declaration's tilde flipped the
  section's label default, so a `~A` reference to it SHOWED the label; in part-major layout
  every part declared its own copy of the section, and one of them could flip it alone. A `~`
  on a reference now always hides, and a form line says on its own which plays are labelled.

- **A slur or a tie holds its lyric syllable, as in LilyPond.** Under `lyrics … sings`, the
  notes inside a slur (after its first) and a note a tie arrives at take no syllable of their
  own: the syllable on the first note is sung over them, left-aligned, with its extender to
  the last one. `c4( d e) f` with `la __ lu` now puts `lu` on f; it used to go on e. `__` is
  the extender line only and takes no note (it used to take one); `_` still takes one. A book
  that spelled a slur's notes out with markers (`la __ ~ ~ lu`) now writes them the
  LilyPond way (`la __ lu`).

### Added

- **A slur can start or end on one note of a chord.** Write the mark after the pitch inside
  the brackets — `<c e( g>4 <d f) a>` — and the bow joins those two note heads instead of the
  whole chords, as LilyPond's `<c e( g>` does: it leaves the head's inner edge, stays close to
  it and does not climb over the chord. A mark after `>` still belongs to the chord. The `.ly`
  twin writes the mark on the member and MusicXML puts the slur on that note. A mark with no
  pitch before it inside the brackets is reported.

- **`eses` and `ases` are E double flat and A double flat**, as in LilyPond, alongside
  `eeses` / `aeses` — the double-flat forms of the `es` / `as` contractions already accepted.
  They used to be read as undefined names; `key ases major` works too.

- **The `.mid` gives every part its own track, channel and General MIDI sound.** Until now
  every part of every book went into one track on channel 1 with no program change, so any MIDI
  player sounded the whole score as a piano. A part's sound comes from its `instrument` preset
  — `violin` plays "violin", `guitar` "acoustic guitar (nylon)", `bass` "electric bass
  (finger)", `horn` "french horn", a voice "choir aahs" — and `midiInstrument "…"` in the part
  header names any of LilyPond's 128 General MIDI sounds by LilyPond's own name:
  `part gtr { instrument guitar  midiInstrument "electric guitar (clean)" }`. A part with
  neither plays "acoustic grand", LilyPond's default. Drums stay on channel 10. Past fifteen
  pitched parts a part shares the channel of an earlier part with the same sound, and the export
  warns when there is none. An unknown name is refused with the list. The LilyPond twin writes
  the sound as the staff's `midiInstrument`, the MusicXML as the part's `<midi-instrument>`, and
  the editor's preview plays its timbre from the same sound (which also stops it reading
  `piano-bass` as a bass guitar).

- **A string number that cannot fret its note is now reported (LYS5003).** `c'4\1` on a guitar
  — the first string cannot play c' — was ignored and the string chosen again in silence; it
  still is (LilyPond does the same), but Lily# now says so, once per note or chord member, as
  LilyPond does ("Requested string for pitch requires negative fret"). A `\N` that stopped
  fitting after a transposition or an octave change is the usual cause.

- **Phrasing slurs: `@phrasingSlur` … `@!phrasingSlur`** — LilyPond's `\(` … `\)`, the long
  curve over a musical sentence. It is drawn over the slurs inside it and clears them as
  LilyPond does (its taller shape and its avoidance of the enclosed slurs measured against
  LilyPond 2.26 to the SVG's two decimals). Like every span it must be closed (LYS4018);
  `.up` / `.down` fixes its side. It survives a `combinedStaff`. The LilyPond twin writes `\(`
  `\)`, and MusicXML a `<slur>` numbered apart from the ordinary slurs — and on import any
  `<slur>` numbered other than 1 is read back as a phrasing slur, since a voice cannot hold two
  ordinary slurs at once.

### Editor

- **A CodeLens over a section's first declaration shows the section as a whole.** A section is
  one span of time however many parts, chord rows and lyrics tracks write it; its first
  declaration now carries a line saying so and naming who writes it —
  `Section A · 2 bars · melody, chords 'harmony' · 2× in form main`, counted by kind when there
  are many (`8 bars · 11 parts, 1 chord row`), each length with who writes it when they disagree
  (`⚠ 9 bars in flute, 8 bars in the other 11`), and `in no form` for a section nothing plays. A
  later declaration gets a line only when it is the odd one out — measured against the length
  most of the section's parts and chord rows write, so ten parts at 10 bars and one at 11 mark
  the one (`⚠ Section A · 11 bars here (1 bar longer) · 10 bars in 10 parts`), not the ten; on
  a tie, the shorter. A part-major book is not lined with copies of the same line. In a
  section-major book the odd one's line stands over its own block (`chords prog { … }`) rather
  than over the section's name. A click lists everything that writes it in the references peek. The
  counts are the bar checker's own, so the lens and LYS2007 agree.
- **Convert Layout no longer drops a cell written twice.** The other layout has room for one
  text per part and section, so a part (or chord row, or lyrics track) that writes the same
  section in two declarations — or two declarations of a section stating different directives
  (`key g major` in one, `key d major` in the other) — came out with the later text only and the earlier gone without a word. The command
  now leaves the file unchanged and says which cell is written twice. A section spread over
  several declarations whose cells do not collide still merges into one.
- **The tutorial says what a section name means.** A new "One Section Name, One Span of Time"
  section shows parts and chord rows each listing their sections, and the mistake of giving
  the accompaniment a name of its own (`section AChords`), which plays it *after* `A` instead
  of with it.
- **The preview receives only the pages a keystroke changed.** The language server used to
  answer every preview request with the whole SVG document — on a 1000-bar book 3.6–12 MB of
  JSON per keystroke, serialized, parsed, keyed, cloned into the preview page and split there
  again, of which one page (200–700 KB) had changed. The answer is now page-wise: the client says
  which picture its preview holds, and the server replies with the pages that changed, tells it
  which pages are the same and which only moved their source offsets (with the edit window to
  move them by), and the preview keeps or shifts those in place. A single-page score now gets the
  same page wrapper as a multi-page one in the preview, so it takes the page-wise path too where
  it used to replace the whole picture on every keystroke. Exported SVG files are unchanged.

- **The Problems panel no longer collects the book a second time.** A keystroke starts two
  computations over the same text — the preview's incremental compile and, behind it, the
  validation pass that fills the panel — and the pass used to open with a full collect of the
  whole book of its own, on top of the one the preview had just finished. It now borrows the
  preview's collect when the preview has rendered the current text (and the default score, which
  is the one the panel's collector-backed warnings are asked of), and collects for itself only
  when there is nothing to borrow. Measured on a 1000-bar book, the pass went from 139 ms to
  98 ms; what it says does not change. The check for a shadowed plain lyric verse (LYS4004)
  then stopped collecting the book a third time for itself — on a book with no lyrics at all
  it was 46 of the validators' 110 ms — and reads the same collect; it now also sees a
  shadowed verse under any staff the score draws, not only the first part's. The cross-part
  bar check then stopped splitting every bar of a section that has only one voice into a
  count it never compared, and stopped gathering the book's phrase table a second time — on
  a one-part 1000-bar book that was 58 of the bar validator's 69 ms, and 12 of its 15 ms on
  the plain one; what it reports does not change (759 books, identical).
- **A keystroke's collect no longer gathers the bars it adopts.** The preview's incremental
  collect resumes at a recorded bar boundary and adopts everything before it, but to find that
  boundary it first gathered the whole part's flat list of music sites — a walk of the entire
  block on every keystroke, 9,000 sites on a plain 1000-bar book and 33,000 on the fingered one,
  even when every bar was adopted. Each recorded boundary now remembers where its site stands
  in the block (its slot path), a resumed collect enters the walk right there, and the sites
  before it are never gathered. Measured on an unchanged-tree keystroke (Debug, min of 5): the
  collect went from 17 to 14 ms on the plain book and from 35 to 22 ms on the fingered one, the
  gather's whole cost; the whole keystroke from 24 to 21 and 56 to 40 ms. The two-voice book,
  whose block is one `<< \\ >>` span, is unchanged. What the collect produces does not change:
  a resume from every recorded boundary of every net book still equals the full collect, and
  the seeked walk is the full walk's remainder site by site.
- **A keystroke no longer lays out the first system's beams twice.** The page's first Y is
  read off the first system's silhouette before the per-system loop, and that read used to
  build the silhouette directly — quanting the edge staff's beams on every keystroke — while
  the loop's own copy of the same system came from the per-system memo. The up-front read now
  goes through the same memo entry. Measured on an edit at the last bar (Release, tiered
  compilation off, min of 6): 3.1 ms and 3.0 MB of a 21.7 ms keystroke on the plain 1000-bar
  book, 3.7 ms and 4.4 MB on the fingered one. The picture does not change: the memo hands
  back the very silhouette the loop reads. The system-count loop that follows — LilyPond's
  "try more systems than the ideal", which prices some seventy line counts on a 200-system
  book every keystroke — then stopped stacking each candidate's lines twice (once for the
  page-count bound, once for the page DP) and constructing a fresh page accumulator per line
  inside the DP: 13.0 MB → 7.7 MB per keystroke on the same books, the loop's time 6.1 → 5.7
  ms. Every count is priced by the same numbers as before.
- **A keystroke solves the note collisions of the bars it changed, not the whole staff twice.**
  On a staff with two or more voices the collision pass — which voice a second pushes aside,
  which head a unison merges away, where a dot column goes — ran over every bar of the staff
  twice per keystroke: once for the spacing floor, in a memo keyed on a voice list that every
  edit replaces, and once more for the picture, with no memo at all. It is now solved a bar at
  a time on first ask; the spacing side asks only for the bars it re-springs, and the picture's
  tables are filed from the per-system memo, which serves the unchanged systems (re-stamped when
  a bar was inserted before them). Counted on an edit at the last bar of the two-voice 1000-bar
  book: 2,000 bars and 6,000 columns solved per keystroke before, 3 bars and 8 columns after
  (15 MB less per keystroke). Nothing moves: a bar's answer reads that bar of every voice and
  nothing else, and every book of the incremental net still equals its full compile.
- **A keystroke detects the beams of the bar it changed, not of the whole staff.** The layout
  detects each staff's beam groups a second time after the collect (on the bars with their stem
  directions baked in), and its memos for that answer were keyed on the staff and voice objects
  an edit replaces — so every keystroke walked every bar again: on the plain 1000-bar book, 1,000
  bars and 2,000 groups per keystroke, at any edit position, while the collect's own detection of
  the same bars was already replaying all 1,000 from its per-bar memo. The layout now has a per-bar
  memo of the same kind, kept with the rest of the session's geometry, and replays the bars a
  previous keystroke detected with their groups pointed at the live notes; only the edited bar is
  detected. Counted on the plain and the fingered 1000-bar books (Release, tiered compilation off,
  min of 6): 1,000 bars walked per keystroke before, 0 after (999 replayed, 1 detected in a real
  session); the detection step 2.8 → 0.3 ms and 2.8 → 0.8 MB, 3.4 → 0.3 ms and 4.1 → 0.8 MB; the
  preliminary annotation pass it sits in 4.4 → 1.7 ms and 5.9 → 2.7 ms. A staff with two or more
  voices is unchanged (its detection is the per-voice fan the memo does not serve). The picture
  does not change: a replayed bar's groups equal a live detection's on every field the quanter and
  the seeds read, the notes they point at are the live ones, and every book of the incremental net
  — including two chained edits and a bar inserted before the beams — still equals its full compile.
- **A book with no fingering and no script no longer pays for the pass that places them.** The
  annotation pass runs twice per keystroke, and each run opened by folding a map of every beamed
  note in the score, two more per-bar maps and a probe of every system's fingering memo — before
  discovering there was nothing to place. Counted on a plain 1000-bar book (Release, tiered
  compilation off, min of 6): 8,000 beam members folded and 200 memo units probed per pass for an
  empty answer, 0.44 ms and 1.4 MB a pass, 0.87 ms and 2.9 MB a keystroke. The pass now asks first
  whether any note carries a digit, which is one read per note and no allocation. On a book that
  DOES carry digits, the beam map a rebuilt system needs is now built from that system's own beams
  rather than from the whole score's: with the memo serving 199 systems of 200, the whole-score fold
  was being paid for the one system that changed — 0.23 ms and 0.8 MB a pass on the fingered
  1000-bar book, 0.50 ms and 1.7 MB a keystroke with the rest. Nothing moves: a system's own beams
  are the ones its memo key is already built from, and every book of the incremental net — including
  a beamed, fingered one edited twice — still equals its full compile.
- **The installed extension's language server starts in about half the time.** The server is now
  precompiled to native (ReadyToRun) in the published build, which the Marketplace VSIXs never were
  — the development deploy and the downloadable release archives have been precompiled for some
  time, so this closes a gap where the copy users install was the slow one. The server pays a JIT warm-up once per process and the process starts
  on every activation. Measured on a grand-staff sample, min of five cold starts, driving the real
  server over its own protocol in the shape the Marketplace ships: 152 ms to initialize, 283 more to
  the first diagnostics and 521 for the first picture, 958 ms in all — while the SAME render takes
  2.7 ms once the server is warm. That factor of roughly two hundred is warm-up, not the score. With
  the change: 531 ms. Warm performance is unchanged, as expected. The VSIX grows by 13 MB; a blanket
  precompile would have cost 41 MB and saved no further time, because 28 MB of it is one imaging
  dependency the startup path never touches, so that one is excluded. No environment variable
  substitutes for this: the two that make the JIT work harder up front measured 59% and 81% WORSE,
  so the cost is the amount of JIT work rather than the quality of the code it first produces.
- **One measure map for the whole annotation pass, not nine.** The map from a bar to the system it
  fell on is a pure function of the laid-out systems, and almost everyone in the pass built a private
  copy: the pass's own staff-Y resolver, its pedal lookup, both outside-staff stackers, the walk that
  places fingerings and scripts, and the script engraver — and the above-staff stacker built it twice
  inside one call, its own remark calling that "the same map the core builds (cheap)". Counted per
  keystroke (Release, tiered compilation off; the pass runs twice per keystroke): 14 whole-score
  builds on a plain 1000-bar book, 18 on the fingered one, 16 on the two-voice one — every one a walk
  of all 1000 bars, at any edit position. The pass now builds one of each kind at the top and hands
  them down, each house keeping its own build for the callers that arrive without one: 14 → 8,
  18 → 8, 16 → 10, and the pass itself builds two where it built five to nine. No time is claimed —
  the difference is below this machine's measurement floor. Nothing moves: both spellings of the map
  keep the last system's entry for a repeated bar index, which is the property the fingering walk's
  unit plan is derived from; the 249 SVG snapshots are unmoved; and poisoning the shared maps reddens
  205 and 148 tests, so neither is unobserved.
- **The annotation pass builds its measure map once, not four times.** Three of the engravers that
  close the pass — half-ties, multi-measure rests, ledger lines — each opened by building their own
  dictionary of every measure in the score, and the half-tie one built two (the second is the first
  plus the system, over the same keys). The pass runs twice per keystroke, so a 1000-bar book paid
  eight of those builds per keystroke; two of the three walkers draw nothing at all on the books
  measured and paid anyway. The pass now builds one map and hands it over: eight builds become two,
  about 710 KB less per keystroke on each of the three perf books. No time is claimed — the
  difference is below this machine's measurement floor. Nothing moves: the map is the same walk over
  the same systems, and poisoning the half-tie engraver reddens 17 tests, so the fixtures do reach it.
- **A keystroke's collect walks the book five times less.** Even when the preview's
  incremental collect adopted every bar of the previous keystroke, it still walked the whole
  tree five more times for facts that do not depend on the edit: twice per part for the
  file's `transpose` default and `pitch` convention, and three times for the section bar
  counts the page pads by. Those are now read in the one definitions walk every collect
  makes anyway. Measured on an unchanged-tree keystroke (Debug, min of 5): the collect went
  from 25 to 18 ms on a plain 1000-bar book, from 72 to 33 ms on the fingered one and from
  21 to 13 ms on the two-voice one; the whole keystroke from 33 to 25, 99 to 51 and 29 to
  18 ms. The page does not change — the walk answers exactly as the whole-tree readers do
  on every book of the net, which a new test holds.
- **The Problems panel walks the book once, not twenty-eight times.** Every validator that
  looked for one kind of node — a section, a key, a cue, a grand staff, a part cell — began
  with its own walk of the whole tree to find it, and twenty-eight of them did, on every
  settled keystroke. The tree is now walked once per text and the walk kept, and a validator
  asking for a kind of node is answered from that one walk. Measured on the validation pass
  of a freshly parsed 1000-bar book (min of 5): 51 → 11 ms on the plain one, 292 → 107 ms
  on the fingered one, 42 → 9 ms on the two-voice one. What the panel says does not change
  (760 books, every diagnostic identical and in the same order). The undefined-name check
  then stopped scanning every node of the book for its handful of declarations and
  references, and asks that same kept walk for just the kinds it reads: 107 → 91 ms on
  the fingered book, and again nothing it says changes.
- **Checking the annotations no longer rebuilds every one of them to answer "is this known?".**
  The check that catches a mistyped `@glisando` visits each annotation and asks whether anything
  consumes it — and on a book of 24,000 fingerings that question was doing three things nobody
  read the result of. It walked all 234,030 nodes of the book to reach the 24,000 annotations;
  it built each annotation's internal dotted name, a string only the branches that report
  something need; and the reading of the argument that answers the question built a growing
  list, a text builder and a second copy of text it already had. All three are now paid only
  where they are used: the check asks the kept walk for the two kinds it has a case for, the
  dotted name is read inside the branches that name it (the three `@chord` tests ask the note's
  shape first, which is a reference test rather than a string to build), and an argument of one
  token is that token's own text. Counted per settled keystroke on the fingered 1000-bar book
  (Release, tiered compilation off, min of 5): 234,030 nodes visited → 24,000, 24,000 dotted
  names built → 0, and the whole check 18.8 MB → 6.9 MB and 6.8 → 3.1 ms. Every reader of an
  annotation's arguments — the collector and the exporters as well as this check — goes through
  the same reading, so all of them allocate less; a full collect of that book built 48,000 of
  them. What the panel says does not change (760 books, every diagnostic identical and in the
  same order), and none of the three is unobserved: dropping either kind from the list reddens
  33 tests, and poisoning the three arms of the argument reading reddens 175, 20 and 9.
- **The Problems panel's validators walk the book as the array it already is.** A dozen of the
  checks scan the whole flat list of the book's nodes for the handful they are about — 234,030
  nodes apiece on a fingered 1000-bar book, some 2.6 million node visits per settled keystroke.
  On a book's root that list is already an array, kept since the walk-once change above, but it
  was handed back as a general sequence, so every element cost an interface call. It is now
  handed back as a value that a `foreach` can walk directly, which needed no change at any of
  the places that ask for it. Measured back to back on the fingered book (Release, tiered
  compilation off, min of 5): the pass on a freshly parsed book 44.4 → 39.9 ms, the sum of its
  validators 21.0 → 17.3 ms, and each scan also stops allocating an enumerator. The check whose
  walk the previous entry had already moved onto the kind buckets does not change, which is the
  control. The nodes, their order and every diagnostic are the same (760 books, identical).
- **The bar checker stops reading the whole book to find four kinds of thing.** It used to
  look for its work by walking the entire book, stepping around the containers whose bodies
  are not bars of their own — a tuplet, a grace, a cue, a repeat, a voice span, a chord cell.
  On a fingered 1000-bar book that walk entered 65,009 nodes, and every slot of each, to
  arrive at TWO. It now asks the kept walk for the four kinds its checks act on and asks
  whether a candidate stands inside one of those containers, which is a question about its
  parents. Measured back to back (Release, tiered compilation off, min of 5): the check 3.39
  → 2.33 ms per settled keystroke on that book, its walk 2.12 → 1.06, the rest being the
  checking itself. Every reason for stepping around a container is kept where it was written
  — each one is a bug that was reported once — and a new test holds the old walk beside the
  new one over every book in the net, asking that they act on the same nodes in the same
  order. What the panel says does not change (760 books, identical).

### Engraving

- **On a full-notation tab, an eighth pair and a sixteenth group over one string line up.** An
  eighth group's stems now take a sixteenth group's length, so a bar of `8 16 16` and `8 8`
  over the same string beams at one height, the way the same rhythm over low notes does on a
  notation staff. LilyPond's `\tabFullNotation` gives the two groups their own lengths, and
  what lines them up on a notation staff — every beamed stem is extended to the middle line —
  never happens on a tab, whose digits all reach past the middle already; so its eighth pair
  stood a step below the sixteenth group beside it. Nothing else moves: each stem is measured
  from its own digit, so a beam over another string stands at that string's height and a run
  across the strings still slopes. A deliberate difference from LilyPond, recorded against its
  tab beam measurements.

- **A chord's accidentals stack in LilyPond's order.** Accidentals on different letters are
  placed the way LilyPond places them: the highest nearest the notes, then the lowest, then the
  next highest, so a lower one tucks under the one above it — `<c'' ees'' ges'' bes''>` now
  spans 1.67 staff spaces, as in LilyPond, instead of 1.93 with every flat a full column left
  of the one before. Octaves of one letter still share one column. A natural is no longer put
  nearest the notes ahead of a higher accidental on another letter. The chord moves left to
  match, and a chord-name row above it now clears the flats at LilyPond's height.

- **A resized dynamic keeps its place on its line.** With `fonts { dynamics step … }` a
  dynamic's baseline now hangs below the line of dynamics and hairpins by LilyPond's 0.6 scaled
  with the letters (0.424 at step −3, 0.756 at +2, measured), not by 0.6 at every size.

- **A bass figure takes the room its digits take.** The box a figure offers the spacing — the
  row stacking, its drop under the staff, the gap to the next staff and the next system — is now
  the drawn figure, from the note rightward by its own width (LilyPond's measured 0.921869 for a
  digit), instead of a 0.8-wide box centred on the note (1.6 wide between systems), which sat
  left of the ink and stopped short of it.

- **A rolled chord's wiggle is the column's leftmost ink.** An arpeggio (and a non-arpeggiated
  chord's bracket) now stands `padding` clear of whichever of the chord's own ink reaches
  furthest left — a head reversed to the far side of a down stem, or the leftmost accidental —
  and the column's leftward reach runs to the wiggle's own left edge, so the bar line, the line
  start and the keep-inside-line rod all keep room for it. A wiggle opening a bar used to print
  through the bar line, and one beside a sharp used to print over it.

- **A grace note's ink is inside-staff ink.** A grace's head, stem, flag and accidental now join
  the staff's vertical profile at the fonts each grob states, where the run is drawn, so the
  things placed above a staff (a rehearsal mark, a section label, a text) clear them and two
  staves are spaced off them. A section label over a grace used to print through its flag.

- **A chord symbol attached to a note stands where LilyPond's spacing puts it.** It stood a
  flat 0.6 above the staff's top line plus whatever rose under it; now its own ink clears the
  staff's skyline by 0.5, the padding LilyPond's ChordNames context declares — so a round "C"
  and a flat-footed "F" no longer share a baseline by accident, and a symbol over a high note
  rides that note's head by the same margin. The room a lower staff reserves for its chord row
  reads the same padding, 0.1 tighter than before.
- **Stanza numbers end together, one space left of the leftmost first syllable.** Every
  verse's "1.", "2." used to start a flat four spaces before the first measure; now, as
  LilyPond's stanza-number alignment does, they all end 1.0 left of whichever verse's first
  syllable starts furthest left.
- **A fall or doit (`@fall`, `@doit`) is LilyPond's BendAfter.** It was a short invented
  curve: eight straight segments 1.25 long and 1.7 deep, 0.13 thick, leaving the head by 0.15.
  Now it is one stroked curve, 0.2 thick, that leaves the head's ink (or its dot's, when the dot
  sits on the head's own row) by 0.5, ends 0.5 short of the next note, rest or bar line but
  reaches at least 0.5, and drops or rises 2 staff spaces — `\bendAfter #-4` / `#+4`, which is
  what the LilyPond twin has always written.
- **A PDF's vertically centred text sits where the SVG's does.** Instrument names and tuplet
  numbers were placed from a guessed cap height; the PDF now reads the face's own ascender and
  descender, as the PNG does.
- **The lyric extender (`__`) is LilyPond's.** It sat 0.7 below the syllable's baseline, 0.1
  thick, left the syllable by 0.2 and ran only to the last held note. Now it sits on the
  baseline, 0.08 thick, leaves the syllable by that thickness, reaches at least 1.5 past the
  syllable (capped at the line's end) and at least to the melisma's last note, stops 0.08 short
  of the next syllable, and disappears only when shorter than 0.12.
- **An accidental shortens its note's ledger lines as LilyPond's font says, and a chord's
  shared ledger is the union of its heads'.** Every accidental shortened the three positions
  around its head, to a fixed distance from the head. Now only the lines within the glyph's
  own range are shortened (a sharp 0.8 below to 1 above, a flat 0 to 0.8, a natural 1.8
  below to 1 above, a courtesy's parenthesis 1 either way), to midway between the drawn
  accidental's right edge and the head — so a D♭ above the staff shortens nothing below it,
  a C♯ its own line but not the one beneath — and each head of a chord asks for its own
  lines, a line two heads share keeping the longer of the two.
- **The octavation digit of `treble_8`, `bass_8` and `treble^8` is LilyPond's ClefModifier.**
  The "8" was drawn at 3.2 staff spaces of em — 2.3 times LilyPond's — at fixed offsets from
  the clef. It is now italic text at the paper's size stepped by −4 (em 1.39), its centre on the
  clef's own alignment point (a fifth of the half-width left of centre under a G clef, three
  tenths under an F clef, a tenth right above a G clef), and its near edge on the clef's ink or
  0.7 outside the staff, whichever is further; a mid-music change clef and a cue clef damp it
  as LilyPond does.
- **A dead note (`@dead`) is drawn with the font's cross head.** The staff drew two strokes of
  its own across the head's box and the tab a bold "×" of the fret face; LilyPond's `\deadNote`
  is the cross note-head style and nothing else, so the page now draws `noteheads.s2cross` (the
  half and whole crosses for those values) at the head size on the staff, and the same glyph at
  the tab head's size in place of the fret number. The MusicXML gains `<notehead>x</notehead>`
  on the note.
- **The dashed bar line (`!`) is drawn as LilyPond draws it: one dash centred on every staff
  line.** The dashes ran from the top of the bar in a fixed 0.67-on / 0.33-off rhythm, so only
  the first straddled a line and the last ended wherever the height left it. Now each dash is
  0.6 of a staff space about its line and the outer two are cut at the staff's edge — on a tab
  staff at the strings' own spacing, between the staves of a group at the layout's.
- **`to coda` is drawn as the coda sign, not the words "To Coda".** The departure and the
  arrival are one mark in LilyPond (`\codaMark` at both), so the page now draws the sign at
  both ends of the jump: centred on the barline `to coda` stands at, at the music size, and
  stacked closest to the staff like the arrival. The LilyPond twin writes `\codaMark`, the
  MusicXML a `<coda/>` with the `tocoda` jump attribute.
- **A navigation mark in a section's music stands at the barline it is written at, whichever
  side of the `|` it is on.** `c4 d e f | fine` drew Fine one measure late (at the end of the
  NEXT bar) and a `| ds al coda` after the last bar was dropped without a word; both now stand
  at the bar they are written at, as `c4 d e f fine |` always did — a text (`fine`, `dc`, `ds`,
  `to coda`) to the bar's left, a sign (`segno`, `coda`) to its right, the way LilyPond aligns
  `JumpScript` and `SegnoMark`. Mid-measure placement still engraves and warns (LYS4003).
- **One mark at a bar: a `@mark` written at the bar a section label opens is not printed, and
  says so (LYS4021).** `section Solo { c4@mark("Solo") … }` under `form main { … Solo … }` drew
  two boxes stacked over the bar — the section's label and the rehearsal mark — which LilyPond
  never draws: its `Mark_engraver` keeps the first mark of a moment and discards the second with
  a warning, and the label is the first. The page now keeps the label alone and the mark warns at
  its `@mark`. Drop the mark, or hide the label with `~Solo` in the form or
  `layout { sectionLabels none }`. Eight of the reader's books, one page count.
- **A note that ends one slur and starts the next draws both.** In `c4( d)( e)`, the page paired
  the `(` on `d` with the `)` right beside it, so it drew one bow from `c` to `e` and a zero-length
  one on `d`, while the tab's hammer-ons already read `c`–`d` and `d`–`e`. A note carrying both
  marks now closes before it opens, whatever order they are written in, as LilyPond does, and a
  script on that note rides the slur that starts there (an accent 2.81 above the middle line, as
  in LilyPond, instead of 2.67). The same note with no slur open, `c4()`, is no longer a one-note
  slur: like LilyPond, the editor warns of a `)` that closes nothing and a `(` left open.
- **A bar whose only rest or note shares it with a skip ending inside the bar is spaced as
  LilyPond spaces it.** `voice { r1 } { s2 }`, and a combined part's `voice { r1 } { s2 } { s4 }`,
  took the 1.0 staff space of extra room LilyPond gives a note or rest that fills its bar alone,
  so the bar came out one staff space wider than LilyPond's. LilyPond places a column where the
  skip ends, and that column means the rest no longer fills the bar; the page now does the same
  (bar line to rest 1.09 instead of 2.09).
- **Every `R` is drawn as a multi-measure rest, whichever voice wrote it and whatever else
  sounds in its bar.** An `R` in a voice other than the first (`voice { s1 } { R1 }`, or a
  combined staff whose second part rests against the first part's skip) was drawn as an
  ordinary whole rest at the start of the bar and gave the bar none of a multi-measure rest's
  width (7.69 or 6.69 where LilyPond has 7.89); an `R` sharing its bar with another staff's or
  voice's notes was likewise drawn at the start of the bar. Now every `R` is a rest centred in
  its bar at its voice's height — voice one's one staff space higher than a lone `R1`, voice
  two's three lower, and `voice { R1 } { R1 }` draws both — and a bar every staff rests is
  spaced like a bare `R1` whichever voice holds the rest. A condensed staff whose parts rest for
  different lengths splits its rests where either part starts one, as LilyPond does. In a
  multi-bar rest the breve moves with the voice and the semibreve sits two positions above it.
  When two voices of a staff rest the same bars, the bar count is printed once, not once per
  voice.
- **A multi-measure rest centres in the room its bar actually leaves.** A key or time change
  printed after the bar line that opens the rest's bar, and a clef change printed before the bar
  line that closes it, now stay outside the space the rest centres in, as LilyPond keeps them —
  also when the change stands on another staff. The rest used to centre between the bar lines
  alone and sat too far left after a new signature (by half its width: 2.6 staff spaces after
  five sharps) and too far right before a clef.
- **A key change takes the room it draws.** A change whose cancellation naturals precede the
  new signature reserved only the glyphs' widths, without the kerning between the naturals or
  the gap LilyPond leaves between the naturals and the new signature, so the note after it was
  crowded in: up to 1.85 staff spaces short (four flats to five sharps: 0.92). The naturals and
  the new signature are now spaced as LilyPond spaces them, and the page reserves exactly what
  it draws. A key change also reads the clef in effect at its own moment — a clef change
  earlier in the line, or at the same bar written after the `key`, now places the accidentals
  (and so the naturals' kerning) for that clef rather than the one the line opened with.
- **A note with its stem down after a key or time change at the start of a bar is no longer
  pushed right.** The small extra gap a bar line leaves before a down stem was also given after
  a new key or time signature, where LilyPond gives none, so that note sat 0.1 to 0.2 staff
  spaces too far right and the bar came out that much wider. Bars opening on a key or time
  change now match LilyPond's; the gap after a plain bar line is unchanged.
- **The start of a bar is spaced from every staff, not only the one that needs the most room.**
  When a key or time change (or anything else) opens a bar on one staff only, LilyPond takes
  the gap each staff wants between the bar line and the first note and averages them, so the
  staves without the change pull the first note back in. Lily# gave the whole bar the widest
  staff's gap, so such a bar came out wider than LilyPond's — about half a staff space after a
  five-sharp key change on one staff of two. A bar whose staves all open alike is unchanged.
- **A bar that opens with a grace note is spaced as LilyPond spaces it.** The gap from the bar
  line to the grace note was a fixed 0.8 staff space, and a main note with its stem down added
  the small extra gap a bar line leaves before a down stem — although the stem that follows the
  bar line there is the grace note's, which always points up. The grace note now sits 0.68 staff
  space after the bar line as in LilyPond, and the main note and the rest of the bar follow
  0.12 closer.
- **Laissez-vibrer and repeat ties (`@laissezVibrer`, `@repeatTie`) are shaped and placed as LilyPond's.**
  They stood a fixed 0.4 staff space off the head's centre with a bow of their own; they now go
  through the same tie arithmetic as ordinary ties (the head they hang from, the dots, the staff
  lines), and their position, width and curvature agree with LilyPond's to the drawn digit. On a
  tab staff they are drawn as before.
- **A tremolo on a beamed note is drawn, and the beam makes room for it.** `a8:32[ a8:32]`
  drew no slashes at all, and the beam sat where it would without them. The slashes now hang
  inside the beam at LilyPond's slope and spacing, and the stems lengthen so they fit — a flat
  pair's beam rises from 2.81 to 4.0 staff spaces, as in LilyPond. `:64` on an eighth now draws
  three slashes, not two.
- **`rit.`, `accel.` and `rall.` are set at LilyPond's size, and their dashed line starts after the
  word.** The word was 10% small, and the dashes began at an estimated width that fell short of
  longer words, so the line ran through the end of "accel.". A chord or lyric row above the staff
  now also clears the word at the height it is actually drawn.
- **A form text (`~A _"meno mosso"`) under a chord row stays on its staff.** On a system that a
  chord row leads, the text was stacked above the row and drawn on top of the chord symbol; it now
  stands over its staff, and the chord row rises to clear it, as in LilyPond.
- **A grace note inside a cue is cue-sized.** The two reductions add (LilyPond's font size −4 plus
  −3); a grace in `cue { }` was drawn at an ordinary grace's size.
- **A lyrics row whose staff below is removed (`as removeEmpty`) no longer leaves that staff's room.**
  On a system where the staff under a lyrics row was hidden, the page still reserved the space of
  its clef and lines under the words, so the next system stood several staff spaces too low.
- **Smaller engraving repairs.** A short tie under the staff avoids a ledger line's position, and a
  tie over dotted notes clears the dots by the bow it actually draws. A kneed beam over colliding
  notes finds LilyPond's position. On a `combinedStaff`, a beam no longer joins notes across a
  change of the voice that engraves them.

### Diagnostics

- **A section written at different lengths is one warning, not one per part.** One extra bar
  in one part of a section that eleven parts and a chord row write used to raise eleven
  warnings — one on each part that was right, and none on the one that was not. It is now a
  single LYS2007 per section — "Section 'A' is not the same length everywhere it is written:
  9 bar(s) in part 'flute'; 8 bar(s) in part 'oboe', … and chords 'harmony'" — that says what
  the page does with each kind of shortfall and does not claim either length is correct. It
  stands on the odd one out — the part or chord row whose length the fewest share — and lists
  every part and track as a related location: a link in the editor's Problems panel, a `note:`
  line under the warning in `lysc check`. The quick fix pads every shorter one in one action.
- **A lyric line that runs out of notes says when a slur or a tie is why.** Since a slur or tie
  now holds its syllable (above), a slur written only as a legato mark over a lyric line swallows
  the syllables of the notes inside it, and a lyric `~` written for a tied note takes the next
  note instead. The "has no note to align with" warning now counts the notes of that bar a slur
  or a tie held and says what to do: write a phrasing-only slur as `@phrasingSlur` …
  `@!phrasingSlur`, which holds no syllable, and drop a `~` or `_` written for a tied note.
- **A section's pickup is checked against its own first bar.** `section A { partial 2 }` over a
  first bar that fills the whole meter drew no warning — a full first bar was taken for some
  later section's — and now reports "Pickup measure duration 1 exceeds the declared partial
  1/2". A section without a `partial` is no longer measured against another section's.
- **A phrase reference counts its beats in the bar check.** With `phrase riff { c4 d }`, the bar
  `riff e f |` was reported short, because the checker counted the reference as taking no time;
  it now plays the phrase in place as the page does.
- **`@!mf`, `@!p` and every other word glued to `@!` or `@` read as a mark name.** Some were
  refused as "Expected Identifier" before the name was even looked up.
- **The "music at the top level" error (LYS0020) survives an edit in the editor.** Deleting the
  first stray line could make the error vanish while later ones remained.
- **A phrase that refers to itself inside a `cue { }` is reported as a cycle, not a crash.**

### MIDI, MusicXML and the LilyPond twin

- **The `.mid` carries the lyrics, on the notes they are sung on.** Each syllable of a
  `lyrics { … }` block is a lyric meta event at the onset of the note it belongs to — a rest is
  not sung, a tie continuation holds its note, a hyphenated word is its syllables, a lyric
  bar line moves to the next bar — where the file used to carry no lyric events at all.

- **A tempo is played and exported in the beat it names.** `tempo 2 = 60` is sixty minims a
  minute: the `.mid` now plays 120 crotchets a minute, and the MusicXML writes
  `<beat-unit>half</beat-unit>` with `<sound tempo="120">`; `tempo 4. = 40` writes the dotted
  unit and plays at 60. Both used to take the figure as crotchets whatever the unit said.

- **A section's header reaches the MusicXML.** `section A { partial 8 }`, and a header's `key`,
  `time` and `tempo`, apply to every part's first bar of the section as they do on the page —
  the pickup is an implicit measure 0. A standalone header declaration no longer opens an
  empty `<part>` of its own, which the schema forbade and the importer could not read.

- **A `voice { } { }` span reads bare note values from where it opened, in every output.** A
  second voice used to restart at a crotchet (`c8 voice { d e } { f g }` drew f g as crotchets
  against d e's quavers), and the music after the span took a different length in each output.
  Every branch, and the music after the span, now read the value in force where the span
  opened — the rule the octave frame already followed. The twin writes the value out where
  LilyPond's own carry would differ.

- **Smaller repairs.** A `~` written after a rest (`c4 r4 ~ c4`) no longer sustains the note
  before the rest through it in the `.mid`; a grace note that closes one part's cell no longer
  shortens the next part's first note; `time 3+2/8` reaches the twin as `\time #'((3 2) . 8)`,
  which LilyPond 2.26.0 accepts (it rejects `\time 3+2/8`); the twin writes the crotchet a phrase
  body opens at, and the value a tuplet, cue or repeat body last wrote carries out of it as it
  does on the page.

- **A `voice { } { }` span in a combined part reaches the twin as one voice.** Inside a
  `combinedStaff` the page reads a span's blocks as one voice's simultaneous music — the
  combiner chooses between the silences they hold — but `lysc ly` wrote them as separate voices
  (`<< { R1 } \\ { s1 } >>`), which LilyPond voicifies inside `\partCombine`, drawing its rests
  apart and warning "too many colliding rests". The twin now writes `<< { R1 } { s1 } >>`. A
  span on a plain or condensed staff keeps `\\`, the page's voice-one / voice-two reading, and a
  part played both by a combined staff and by another staff keeps `\\` with a warning.

## 0.7.0

A chord symbol is spelled and raised the way LilyPond draws it, a `layout { }` block gathers
the score-wide display switches, staff groups nest, and the drum and tuning tables are
LilyPond's whole ones. Nine things a 0.6.0 book could write now print differently or are
refused; they come first, each with what the compiler says.

### Breaking changes

- **A chord's quality prints LilyPond's symbols by default.** A book compiles unchanged but
  prints `C°`, `C+`, `Cø`, `C°7` and a drawn triangle for a major seventh where 0.6.0 printed
  `Cdim`, `Caug`, `Cm7♭5`, `Cdim7` and `Cmaj7`. `layout { chordQualities words }` restores the
  lead-sheet spelling (see Language below).
- **`removeEmpty` leaves the part header for the score's staff item.** `part lh { clef bass
  removeEmpty all }` is refused ("Unknown part property 'removeEmpty'"); write `staff lh as
  removeEmpty all` in the score instead, so a full score can hide a staff its part sheet keeps.
- **A `fonts { }` entry follows a generic family with `as`.** `fonts { chordName serif }` is
  refused ("'serif' is a generic family and takes quoted face names"); write
  `chordName as serif`. A bare word after a key is now the next key, which is what lets an
  entry carry a size and a style.
- **`paper { topSystemPadding }` is retired.** Nothing read it and LilyPond has no such
  variable; a book that writes it is refused as an unknown paper key. The padding under the
  header is `topSystemSpacing { padding N }`.
- **A MIDI-only score row is a part name and nothing else.** `score main { staff rh  lh
  instrument violin }` is refused ("'instrument' is not something a score can hold"). The row's
  `instrument` / `octave` options were never read — the MIDI takes both from the part — so no
  output changes.
- **The drum name `hhs` is gone.** It claimed a splash hi-hat, which LilyPond does not have,
  and drew and played a pedal hi-hat. It now reads as an undefined phrase; write `hhp` for the
  pedal hi-hat or `cyms` for LilyPond's splash cymbal.
- **`@feather` takes `right` or `left`, nothing else.** The tempo words `accel` and `rit` were a
  second spelling of the same two directions, and the same words name the `@accel` / `@rit`
  text spanners. `@feather(accel)` is now ignored with a warning ("Unknown annotation
  '@feather(accel)'"); write `@feather(right)` for accelerando and `@feather(left)` for
  ritardando.
- **The tunings `standard` and `uke` are gone.** They were Lily#'s own second names for
  `guitar` and `ukulele`; every tuning word is now LilyPond's. `tuning standard` is refused
  ("Unknown tuning 'standard'", with the list of names); write `tuning guitar` or
  `tuning ukulele`.
- **A section's opening pickup is written in the section header, and only there.**
  `section A { partial 4  rh { … } lh { … } }` shortens the opening bar for every part at once,
  and beside part-major cells a standalone `section A { partial 4 }` does the same. A `partial`
  written in a part's music within the section's first bar is refused ("A pickup at the start of
  section A belongs to the section header"). Later in a section, `partial` in the music still
  shortens the bar it stands in, written in every part that shares that bar.

### Language

- **`paper { raggedBottom }` keeps every page's systems at their natural spacing.** LilyPond's
  `ragged-bottom`, as a bare flag beside `raggedRight`. Without it only the last page is ragged
  (LilyPond's `ragged-last-bottom` default, which Lily# shares), so a `pageBreak` that leaves one
  system on a first page justifies that system to the page bottom — the title half a page above
  the staff, in both engines (measured on 2.26.0: the staff at 91.12 of a 169.01-space page in
  each). The editor completes and colours the word; the `.ly` twin does not write it, as it does
  not write `raggedRight`.

- **A `break` inside a bar breaks the bar across two systems.** `c4 d break e f |` ends the
  first system after `d` with no bar line and opens the next with `e`, with no bar line and no
  bar number — LilyPond's `\break` at that moment (measured on 2.26.0: the second system's bar
  lines and its first note land where LilyPond's do). Every part, chord row, lyrics row and
  empty `| |` gap of the score is cut at the same beat, `pageBreak` does the same for the page,
  a repeated section breaks at every play, and bar numbers keep counting bars. A `break` right
  before a bar line (`e2 break |`) is the bar-line break it always was. Where some part holds a
  note sounding across the break, or a beam, tuplet or percent repeat runs across it, or the
  bar is unmetered, the bar stays whole, the break falls to the next bar line and LYS1037 names
  the part and the reason (LilyPond splits under a sounding note and draws the rest of the bar
  empty; Lily# declares that divergence rather than draw it). Until now every mid-bar `break`
  silently fell to the next bar line.

- **A bar split by a section boundary is one bar to the bar check.** When a section ends on a
  short bar and every section the form plays after it opens with exactly the rest of that bar
  — a repeat sign or a volta bracket standing mid-bar, `|: A [1. B] :| [2. C]` with A ending on
  the half bar and both endings opening with the other half — neither half warns: no LYS2001 on
  the last bar, no LYS2006 pickup nudge on the first. The exemption is read off the form's play
  order per part and holds only when every neighbour completes the bar exactly; one that does
  not brings both warnings back. The page counts the two halves as one bar too — the bar
  number does not advance across the boundary and a system opening with the second half
  carries no number, as LilyPond's currentBarNumber does not advance at a mid-bar repeat sign
  — while the author's bar line between them stays drawn. A second or later ending is read
  against the repeat's body, not the ending before it, so it is exempt the same way; its
  number, though, keeps counting, as LilyPond's does (bar numbers continue through
  alternatives; only the position in the bar is restored at each one).

- **`layout { }` gathers the score-wide display switches.** The third block of the `fonts`
  / `paper` shape: an unnamed `layout { … }` at the top level is the file's default, a
  named `layout chart { … }` is a per-score declaration a score references as `layout
  chart` (or overrides in part with `layout chart { barNumbers none }`), and the reference
  replaces the default for that score alone. What belongs here and not in `paper`: a switch
  among a few drawings, with no unit and no grob scope — a length or a justification flag
  stays in `paper`. Seven keys, each a closed vocabulary, none reserved:
  - **`marks stacked | beside`** arranges a section label and the tempo mark at the same
    bar. `stacked` is the default and LilyPond's: the boxed label break-aligns to the
    key/clef column, the metronome mark to the meter column, and where their inks meet the
    label stacks over the tempo. `beside` is the chart's one line — the label's box at the
    line-start edge with the tempo to its right, the digits on the label's baseline
    ("[Chorus] ♩ = 132"); the pair is reserved and moved as one, so a chord symbol or a high
    note under either lifts both. A mid-line label stays centred on its bar and a
    mid-measure `tempo` keeps its note column either way. The `.ly` twin has no spelling
    for `beside` and warns (LilyPond has no such pair).
  - **`barNumbers lines | none | every N`** says which bars carry a printed number, in
    LilyPond's vocabulary. `lines` is the default and LilyPond's: the first bar of every
    line after the first. `none` prints no numbers. `every N` prints every bar whose number
    is a multiple of N wherever it stands in the line, and only those — under `every 2` a
    line opening on bar 3 opens with no number, as LilyPond's `every-nth-bar-number-visible`
    prints it (the visibility function answers before the line-start rule). A pickup is bar
    0, so the multiples are of the displayed number. The `.ly` twin writes the same LilyPond
    words into its `\layout` block (`\remove Bar_number_engraver`; `barNumberVisibility`
    with `BarNumber.break-visibility = #end-of-line-invisible`), so the two pages number
    the same bars.
  - **`accidentals default | modern | modernCautionary | forget | noReset`** says which
    notes carry a printed accidental — LilyPond's `\accidentalStyle` table, transcribed for
    the styles whose context is the staff. `default` is the 18th-century style Lily# has
    always drawn (an alteration holds to the bar line, in its own octave). `modern` is Kurt
    Stone's: cancelled in other octaves and in the next measure too, and with no
    restore-natural. `modernCautionary` prints the accidentals `modern` adds in parentheses.
    `forget` remembers nothing, so every note is read against the key signature alone;
    `noReset` never forgets. LilyPond's voice / piano / choral families name a context a
    score-wide switch cannot name, and its neo-modern, teaching and dodecaphonic families
    need rules of another kind: those are absent, and a style the table does not hold is
    refused at the word. The `.ly` twin writes `\accidentalStyle modern` at the head of each
    part's music. Under a style that remembers past the bar line (`modern`,
    `modernCautionary`, `noReset`) the preview recompiles an edited section whole, so it
    answers a keystroke a little more slowly.
  - **`sectionLabels boxed | plain | none`** says how a `form` section's name is drawn.
    `boxed` is the default and the frame Lily# has always drawn — a Lily#-own picture, since
    LilyPond's `SectionLabel` draws the bare string. `plain` drops the frame and engraves the
    name alone, which *is* LilyPond's own picture, and the twin writes `\mark \markup` with
    no `\box`. `none` engraves no section names at all, which is what a part sheet wants, and
    the `.ly` twin then writes no `\mark` either, so the two pictures stay one picture. It is
    a display switch: the form still plays the section, and MIDI and MusicXML are untouched.
  - **`partCombineText true | false`** says whether a `combinedStaff` prints `a2` / `Solo` /
    `Solo II`. `true` is the default and LilyPond's; `false` is its `printPartCombineTexts =
    ##f`, which the twin writes. With the words off no text item is made at all, so nothing
    is drawn and nothing is reserved — the combining itself is unchanged.
  - **`chordQualities symbols | words`** says how a chord's quality is spelled after the root.
    `symbols` is the default and LilyPond's: the four qualities LilyPond's own exception table
    names print `C°`, `C+`, `Cø`, `C°7`, and a major seventh prints LilyPond's drawn triangle
    (see the next entry). `words` prints `Cdim`, `Caug`, `Cm7♭5`, `Cdim7` and `Cmaj7`, the
    lead-sheet spelling every book printed before this version. Every other quality is the
    same either way, because LilyPond spells the rest with digits too. It is a different
    question from `chords NAME as names | roman`, which says which *quantity* a row shows and
    stays on the row; the two compose, and a Roman degree row is unchanged by this key. With
    the default, the chord row stands where LilyPond 2.26.0 puts it, to nine digits.
  - **`minorChords upper | lower`** says whether a chord with a MINOR THIRD prints an
    uppercase root with its `m` (`upper`, the default and LilyPond's) or a lowercase root
    with the `m` dropped (`lower` — LilyPond's `chordNameLowercaseMinor`, which the twin
    writes on the `ChordNames` context). The test is the third, so `Cdim` lowercases too
    and a `sus` chord never does; the slash bass keeps its capital, as LilyPond's
    `chordNoteNamer` does. Measured on LilyPond 2.26.0: with both keys set Lily# spells
    `c°` `C+` `cø` `c°7` `a7/C` — character for character what LilyPond prints for the same
    chords. Neither key reaches MIDI or MusicXML, where a chord is data.

  The editor completes the block pre-filled with the defaults, the keys, and each key's
  words, and colours them inside the block; an unknown key is an error and a bad word is
  refused on the word. A book that writes no `layout` block is unchanged.
- **A chord symbol is raised where LilyPond raises it, and a major seventh can be its
  triangle.** Everything between the root and the slash bass — the digits, the `sus` / `add`
  words, an altered tension's ♭/♯ — is now set in a raised, reduced run, which is LilyPond's
  `super-markup`: three font-size steps down (1.8500 against the root's 2.6165) lifted by
  `magstep` of the symbol's own step (1.1892). The root, the minor `m`, the `+` and `°` of
  the symbol vocabulary and the slash bass stay on the baseline, as LilyPond leaves them, so
  a plain triad and a bare `Cm` are unchanged. Under `layout { chordQualities symbols }` (the
  default) a major seventh is LilyPond's `majorSevenSymbol`: a drawn triangle, 1.0703 wide and
  0.9204 tall at stroke 0.1, traced as three round-capped segments the way `ly:round-polygon`
  traces it — never a character, since no text face carries one. Every number was read off
  LilyPond 2.26.0's own output rather than derived. The symbol is narrower (a digit at 71%) and
  taller (the lift), so a chord row reserves a different band and a tight line can break
  elsewhere. Lily#'s `Dmaj7` ink is now `(0.000000 . 2.497137)` where LilyPond's is
  `(0.0 . 2.5008)` — the `j` descender Lily# used to hang below the baseline is gone, because
  LilyPond never had one there.

- **A `fonts { }` entry carries a size and a style, not only a face.** After a key, in any
  order: quoted faces, `as serif|sans` (follow a generic family), `step ±n` (LilyPond
  `font-size` steps relative to the role's default — six steps double), `size n` (an absolute
  em in staff spaces), and `bold` / `italic` / `regular` (combine; `regular` clears; a
  written style replaces the engraving's, so `text bold` is bold and upright). `mark "Charis
  SIL" step +1 bold` is one entry. The size and the style resolve like the face — the role's
  entry, then its group's, then the engraving — for the roles whose drawing and reserved
  space both read the plan: `title composer instrument lyricText stanza chordName fretFrame
  tempo mark pedal navigation text dynamics partCombine barNumber tuplet volta ottava bend
  tabTechnique clefOctave tabFret meter`; `fingering` and `figuredBass` are Emmentaler digit
  runs and take a size (the glyph's own font-size steps — design, em and box together) but no
  style (LYS8018); `tabFret step` moves the fret digit and what is measured from it (its
  column, the string-line bite, the stem's near end, a tie's clearance), not the string
  spacing; `meter` is the compound numerator's `+` alone and widens the signature's column
  with it. A chord symbol's accidental and a metronome mark's note step with their text.
  The twin writes a `step` as `\override Grob.font-size = #n` (and a style as `font-series`
  / `font-shape`) in the `\Score` context, the header roles as `\markup \fontsize`; a `size`
  has no LilyPond spelling and is warned about. **The redirect is spelled `chordName as sans`**
  — a bare word after a key is now the next key, so `chordName serif` opens an empty `serif`
  entry and is refused. `step`/`size` on a generic family is refused
  (LYS8015), a value out of range (`step` ±12, `size` 0.5..20) is LYS8016, both sizes in one
  entry is LYS8017. The editor completes the attributes after a key and inside an open entry,
  and colours them in the block.

- **A spaced dot inside `<< … >>` holds the member before it one more share.** `<< c . d >>4`
  is two shares against one in the quarter — the swing figure, spelled as the convention
  spells it, `tuplet 3/2 { c4 d8 }` — and `<< c . . d >>4` is `c8. d16`. The dot is a share,
  not the 1.5× of a duration dot: inside a group there is no duration for a dot to belong
  to, so it is written as its own token, and glued (`c.`, `3.`) or leading it is reported
  (LYS0023). The tuplet is spelled from the total number of shares; a member no single note
  can spell is written as tied notes. A `~` inside the group is reported with this spelling
  to use instead.
- **A `<< … >>` member carries what a note carries.** A script, a fingering, a dynamic, a
  string number and a slur mark are written on the member (`<< c@accent e\3 g( a) >>`) and
  reach the page, the MIDI, the MusicXML and the twin. On the group, a string number is every
  member's (`>>4\2`), and a tie or slur mark written after `>>` hangs on the last member. Other
  marks on the group still warn (LYS4008) — write them on the member.

- **`time none` is engraved.** Senza misura was read by the parser and the validator and
  ignored by the page, which kept filling 4/4 bars under it. Now, from `time none` to the next
  `time N/M`, a measure ends only at a written `|` (still drawn, still a place the line may
  break), no time signature is drawn for it, no automatic beam is made (write them), and the
  bar number does not advance across the span — the bar after the unmetered ones carries the
  same number, as LilyPond's `\cadenzaOn` (`Timing.timing = ##f`) numbers it. Written at the
  top, in a section header, or in the music, per part like any `time`. The twin writes
  `\cadenzaOn`, a `|` inside it as `\bar "|"` (a bare `|` is only a bar check there), and
  `\cadenzaOff` before the returning `\time`; `lysc layout` reports the meter as `none`; the
  MIDI conductor track writes no meter event for it and keeps the last one, as LilyPond's
  performer does.

- **A pickup can be declared mid-section, in the music.** `partial` says "the bar it stands in
  is this long". A section's opening pickup stays in the section header, for every part at
  once (see Breaking changes); after the section's first bar, a part's or voice's music takes
  it at the bar's start — `… | partial 2. r2. | …` closes a three-beat bar and the meter
  resumes after it. It is per part, like a mid-music `time`: every part sharing the bar writes
  it, and a part that omits it keeps a full bar, which the cross-part check reports. (LilyPond's `\partial` moves one clock for all staves; Lily# keeps a bar length per
  voice.) The top level of a structured file and a part header hold no bar and still refuse it
  (LYS1024). The completion offers `partial` in music again.

- **Hara-kiri is the score's, not the part's: `staff m as removeEmpty true|all`.** `removeEmpty`
  leaves the part header — where it made a part hide its empty systems in every score it was
  placed in — and joins `lines` as a selector of the staff item, chained after one `as`
  (`staff m as lines 1 removeEmpty all`, either order). The full score hides the empty systems
  and the part sheet of the same part never does, which one part-global value could not spell;
  LilyPond's `\RemoveEmptyStaves` is likewise a context mod written in `\layout` or a staff's
  `\with`, never on the music. A part header now refuses the word as an unknown property, like
  `lines` since 0.3.0; `pedal` stays on the part. Ossia takes the selector (and is hara-kiri
  regardless); a tab item takes neither. The editor offers `as removeEmpty` after a staff name
  and its values after it.

- **LilyPond's whole drum and tuning tables.** The drum vocabulary grows from 30 names to
  LilyPond's 63 — the Latin and accessory percussion: `hibongo` (`boh`), `hiconga` (`cgh`),
  `hitimbale` (`timh`), `claves`, `maracas` (`mar`), `cabasa` (`cab`), `guiro`, `triangle`,
  `hiwoodblock`, `sidestick` (`ss`), `splashcymbal` (`cyms`) and the rest — each at the staff
  position, notehead and GM key of the LilyPond table that places it. `shortguiro`, `longguiro`
  and `guiro` share a line and a head, and LilyPond tells them apart by a staccato or tenuto
  mark, which the page draws. The tunings grow from 7 words to 32, every
  `\makeDefaultStringTuning` of LilyPond's named as its symbol without `-tuning`:
  `guitardropd`, `guitardadgad`, `guitar7`, `bassdropd`, `violin`, `viola`, `cello`,
  `mandolin`, `banjoopeng`, `tenorukulele` and more. A string number goes up to `\7` for the
  seven-string guitar. Both tables are pinned to LilyPond's, row by row and string by string.
- **A bowed or plucked preset frets its tab on its own strings.** `instrument violin`, `viola`
  and `cello` shown as a `tab` fell back to the guitar's six strings; they take LilyPond's
  violin, viola and cello tunings. `mandolin` (treble clef at sounding pitch, the violin's
  strings) and `banjo` (the guitar's octave-down treble clef, LilyPond's open-G banjo tuning)
  are new presets.
- **Staff groups nest.** A `grandStaff`, `staffGroup` or `choirStaff` holds `condensedStaff
  { … }` and `combinedStaff { … }` members beside its staves — `staffGroup { condensedStaff
  { fl1 fl2 } staff ob }` is the woodwind bracket — and any group may stand inside any other,
  at any depth, drawn as written: the piano's brace inside the orchestra's bracket. As in
  LilyPond 2.26.0, measured: each delimiter clears its parent's ink (a bracket by 0.8, a brace
  by 0.3), the space between two staves follows the innermost group they share, and a span bar
  crosses a gap when a `grandStaff` or `staffGroup` holds both staves, never a `choirStaff`.

### Editor

- **The completion popup names every construct the grammar takes.** An audit of the popup
  against the grammar found sixteen spellings a book could write that no list offered; they are
  offered now, each compiled where it is offered. In music: `cue { }`, `q` (repeat the chord),
  the document's phrase names as references, and the navigation marks (`segno`, `to coda`,
  `ds al fine`, …) the form list already had. In a section-major section: the `lyrics NAME sings
  PART { }` and `chords NAME { }` cells. At the top level: `transpose`, `using "file.lys"` and
  `drummap { }`. On a score header, before its brace: the quoted basename, `transpose` and
  `pitch`. In a score body: the declared parts as bare MIDI-only items, the five clefs before a
  `staff` / `ossia` part, the tunings before a `tab` part. In a lyrics body (a section's
  `lyrics` cell, a track's inner section) the verse headers `[1. ]` `[2. ]` `[1-2. ]` `[~1. ]` —
  and nothing else: that body fell through to the music list and proposed pitches at every
  syllable. After `time`: `none`. After `tempo`: the `shuffle` feel beside `swing`. Inside
  `@feather( )`: `right` / `left`; inside `@bend( )`: a semitone count; `@arpeggio(bracket)`.
  Two lists that were hand-written copies of the compiler's now read it: the nine key modes
  (`SyntaxFacts.KeyModeVocabulary`, which the parser and its "Unknown mode" message read too)
  and the override targets (`SupportedGrobOverrides`, the list LYS1029 enforces); the
  navigation marks and the tempo feel words are published the same way.

- **The tab row completes its style, and the last hand-written completion tables read the
  compiler.** After `tab NAME` (and `tab TUNING NAME`) the popup offers `as numbers` / `as full`
  before the next render item, as `staff NAME` offers `as lines` and `chords NAME` offers `as
  roman`; after `tab bass` — a tuning word that is a legal part name too — the parts and the
  selector both. The printed ottava spellings `8va` `8vb` `15ma` `15mb`, which the compiler has
  always read, are rows in the `@` list beside `ottava` / `quindicesima` (before, "8va" only
  found the `ottava` row). Three lists that were copies now read one vocabulary each: the score
  body's fifteen render items (`SyntaxFacts.ScoreItemKeywordVocabulary`, held to the parser by
  the same measurement that checks the grammar's ScoreItem), a section header's five directives
  (`SyntaxFacts.SectionSettingVocabulary`, which the parser's stray-item message now spells
  from), and the paper block's `size` key; the signature each `key` tonic row describes is asked
  of `KeySpelling` rather than typed. GRAMMAR.md gains the `UsingDecl` production it had called
  "reserved for multi-file support" and lists the section-scoped `override` under SectionItem.

- **The paper block's flags are completed from the reader's own table.** `raggedRight` was the
  one paper spelling the completion listed by hand instead of reading from the vocabulary, so a
  flag added to the reader (`raggedBottom`) would not have reached the popup; both now come from
  the same list the reader validates against, each with its one-line help.

- **A quick fix pads a short section voice with bar lines.** On an LYS2007 squiggle — a
  `section A` that writes fewer bars in this part or chord row than in another voice of the
  section — the lightbulb offers "Add N bar line(s) to section A (| |)": bare `|` appended after
  the voice's last item, one per missing bar, plus one to close a bar the voice left open
  (`{ g2 g }`). The edit is re-validated before it is offered, so it is only shown when it
  makes the warning go. Quick fixes now read the same diagnostics the Problems panel shows
  (until now only parse errors reached the lightbulb) and match the caret anywhere inside a
  squiggle, not only at its first character.
- **A lyrics cell shorter than its section is an LYS2007 too, with the same quick fix.**
  `lyrics words { section A { la la | } }` under a two-bar melody A — or `lyrics words sings
  vocal { la la | }` beside a two-bar part block — warns on the cell's name ("spans 1 bar(s) in
  lyrics 'words' but 2 in part 'melody' — the track sings nothing over the remaining bar(s)"),
  and the lightbulb offers "Add 1 bar line to section A (|)" / "… to lyrics words". The other
  direction stays silent: a lyrics cell longer than its music is a stacked verse (verse 2 over
  the same bars), never a claim about the section's length, and lyrics cells alone (no part,
  no chord row) have nothing to be short of. Until now a lyrics track was left out of the
  comparison entirely.
- **A ```` ```lily# ```` fence in a Markdown file renders as the score in VS Code's built-in
  Markdown preview**, the way a ```` ```mermaid ```` fence renders as a diagram; the language's
  name is the one fence word. A fence draws one picture and says which: it writes exactly one
  `score { }`, as a file does, and is refused with the reason when it writes none or two (a
  first cut let a fence imply its score from its parts; that quietly dropped everything a
  score names — lyrics, chord rows, tab — so it went). The fence is drawn by the language server and inlined as SVG; while it
  renders, the source stays in view, and a syntax error is shown with its line above the
  source. The picture takes the column's width and follows the dark theme. In the editor the
  fence is coloured as Lily#. Opening a Markdown file no longer costs anything: the language
  server starts only when a score is opened or a fence needs drawing. A fence is laid out as a
  snippet, LilyPond's `ly:one-page-breaking`: one page as tall as the music, no automatic page
  break, a `pageBreak` a plain line break, the systems at their natural spacing — and the page
  as wide as its widest system (or its title, when that is wider), so a two-bar example is a
  two-bar picture, not a sheet of A4 with two bars in its corner. A `paper { }` inside the
  fence overlays that.
- **Right-click one or more `.lys` files in the Explorer to export them all at once.** The
  context menu gains a *Lily#: Export* submenu — PDF, SVG, PNG, LilyPond, MusicXML, MIDI,
  VOCALOID — that asks for a folder and writes every score of every selected file there, named
  as `lysc --all` names them (the `main` score takes the file's name, every other score appends
  its own: `song.pdf`, `song-sub.pdf`; a `.vsqx` holds one arrangement, so it takes the first
  score and the log says which were left out). A file open in an editor is exported as it
  stands, unsaved edits included, as the preview shows it. Two selected files that would share a
  name are pointed out before anything is written; an existing file is overwritten, as the CLI
  does. The same commands sit in the Command Palette for the score being edited. Until now the
  preview's Export button was the only way out, one file and one score at a time.
- **`lilysharp/export` takes a file by path and `all`.** The request the preview's button
  sends now also accepts `path` (a `.lys` that is not open, read with its `using` includes),
  `all` with `outputDirectory` (every score, the CLI's names), and answers with `outputPaths`
  and `warnings`; the one-score call is unchanged.
- **Completion reads the construct it is in, whatever its header spells.** A chords track's own
  body offers `section` and the section names, not chord names (a chord written beside a
  track's sections is dropped); a lyrics track completes the same with `sings PART` in its
  header as without it; a silent `section ~B { }` completes like `section B { }`; a score with
  any header option (`score main "out" transpose d pitch concert {`) offers its render items
  where it offered the music list; and the staff row is read by its grammar, so `staff treble
  melody |`, `staff ~m |`, `staff m "Violin I" |` and `staff m as lines 1 |` each offer what
  can follow. Every legal spelling of every score row is pinned by a test.
- **A scaffold names what must exist, and leaves free what it creates.** `lyrics … sings`
  writes the only declared part, opens the part list when there are several, and writes no
  clause when there is none — it used to type the keyword `part` there. `score` takes the
  book's only form (it typed `main` into a book whose form is `verse`, LYS1018), and `form`
  takes `main` while it is free, else the first free `mainN` (it typed a duplicate, LYS1017).
- **A `fonts` entry offers `step` and `size` first**, then the styles, `as` and a quoted face,
  and offers a second face after the first, which is how a fallback chain is written.

### Fixed

- **A slur on cue notes reads the cue stems and accidentals.** A cue slur started from the
  cue head since last week, but the stem it attaches to still stood where a full-size head's
  stem would, and the accidentals it avoids were full size: `cue { e4( a4 d'4 c4) }` started
  its slur 0.49 space right of LilyPond's, and a slur over a cue sharp or flat stood up to
  0.87 space taller. Both now read the cue font, as LilyPond's CueVoice does (measured on
  2.26.0).

- **Cue beams and cue spacing are LilyPond's.** A beamed cue group was drawn with a full-size
  beam: 0.48 space thick instead of 0.35, and 1.06 space too high because its stems took the
  full-size length. Cue notes were also spaced as full-size notes wherever a minimum decided
  the gap: beamed cue sixteenths stood 1.804 apart where LilyPond puts 1.315, a cue sharp or
  flat widened its gap by up to 0.84, and a cue note after a bar line stood 0.41 late when it
  carried an accidental. The beam now takes CueVoice's thickness, length and cue-sized stem
  attachment, and every part of a cue note's spacing box is read at the cue size. On the
  LilyPond books compared, every cue beam, cue gap and cue slur now matches. One general
  closing gap moved with it. A note before a bar line whose own ink is narrower than the
  spacing increment kept the increment as its minimum; it now takes its own, as LilyPond
  does. No full-size book moved.

- **A beat slash is spaced as LilyPond spaces it.** A `repeat percent` whose body is shorter
  than a bar draws a slash for each repetition and, until now, left too much room after it —
  a whole notehead's width of space before the next note, and as much again before the bar
  line, so a bar of `repeat percent 2 { c16 d e f } repeat percent 2 { g8. c16 }` came out 1.7
  spaces wider than LilyPond's. LilyPond treats the slash's column as a column with a grob but
  no note head: the spring out of it is the plain duration space less the notehead increment,
  and the bar line stands off the slash group's own ink. Both are now read that way (measured on
  2.26.0: slash to next note 3.600000, dotted slash to bar line 4.057645, exact on both sides —
  `audit/lp-geometry` `percent.beat-slash.*`). On a tab staff the sign is one-and-a-half-sized
  and the bar line stands off THAT group, on a staff-plus-tab system as on a tab alone
  (5.936467, exact).

- **A `score { }` pasted into the file now appears in the preview's score picker at once.**
  The preview host skips sending the webview a picture identical to the one it already
  shows, and the key it compared was the picture and the error banner alone. A second
  score pasted below the one being drawn changes nothing in the picture, so the message
  that carries the picker's list was skipped and the new score did not appear until some
  later edit happened to move the picture. The picker's list and the drawn score's name are
  part of the key now (an unchanged picture still posts, cheaply: the webview compares the
  page markup and keeps every page).

- **A note typed into an empty bar no longer vanishes from the preview.** With `g2 g | | | | d1 | c |`
  open in the editor, typing an `e` into one of the empty bars drew that bar still empty — the
  bar count unchanged, one note gone — and, depending on the edit history, a note elsewhere
  in the section could disappear or reappear with each keystroke. The preview's collect resume reuses the previous keystroke's measures past the
  edit; a measure whose closing bar line stood exactly at the insertion point was read as
  untouched, although text inserted there lands before that bar line, inside the measure, so
  the old empty bar was adopted over the one just typed. The measure ending at the edit is
  now walked live. `lysc` and every export were unaffected (they compile from scratch); only
  the live preview reused a stale bar. The net that guards this reuse now also types a note
  at both edges of a bar line in every fixture, which found one more stale reuse: after an
  edit above the form line, a `segno` / `coda` / `to coda` / `fine` / `_"text"` written on the
  form line kept its pre-edit source position in the preview (a click on the sign jumped to
  the wrong column); the form-line mark is now a header read the resume verifies.

- **The preview's reuse of the previous keystroke was audited edge by edge, and four more
  stale reuses were closed.** A sweep of 36 edit shapes over every fixture and sample (a note
  or bar line at either edge of every body, a duplicated or deleted note, an annotation added
  or removed, edits in phrase bodies, the form line and the file ends) found 88 keystrokes in
  258 books where the preview differed from a full compile; after the fixes it finds none.
  The shapes: a note typed after a body's last item (before the `}`) or before its first,
  which the previous keystroke's measures were reused across; a bar line or note typed at the
  start of a body written as phrase references, where the reuse resumed one node late; an
  empty bar typed into a chord row of a two-part piece, after which the chord names of every
  section were placed at both their old and their new bar (16 names for 10); and a bar line
  typed at section level, outside the part blocks of a `partial` piece, which the reuse did
  not see as a change to the section. `lysc` and the exports were never affected.

- **A part written as phrases, a multi-measure rest or a repeat is measured at its played
  length when the page decides how long a section is.** The page counted one bar per written
  token — `R1*4` was one bar, `repeat unfold 13 { … }` its body once, a phrase reference
  nothing — so a section whose longest voice was written that way (`lh { p_bass p_bass p_bass }`
  beside a shorter `rh`) never padded the short part, and every part after it drifted a bar or
  more out of alignment while LYS2007 claimed the part was "padded with rests to align". The
  count now reads all three off the source, with the same edge rule as the engraver (a `|`
  right after a phrase or a repeat confirms its close; a `|` opening a phrase's body is an empty
  bar), and agrees with the validator's count on every fixture and sample.
- **The MusicXML carried no string number and no fingering, on any note.** `c4\3`,
  `<e dis'>4\5\4` and `c4@finger(1)` reached the page and the twin but left the note's
  `<technical>` empty; they are written now as `<string>` and `<fingering>`. A chord's outside
  list pairs with its members as the page pairs them (a member's own `\N` wins, the last outside
  one repeats), and a string number on a `<< … >>` group is every member's that names none.
  The importer reads them back (`lysc import`): a `<string>` is the note's `\N`, a numeric
  `<fingering>` its `@finger(N)`, each inside the brackets when the note is a chord member.
- **A string number on a `<< … >>` member or group was dropped in silence.** `<< c\3 e\2 g >>`
  printed no strings and raised no diagnostic; it is applied now.
- **A slur mark after `>>` was dropped from the page and then blamed on the `)`.** `<< c e g >>4( d)`
  drew no bow and warned that the `)` had no `(` (LYS4010); the MusicXML had the slur all along.
- **`paper { spacingIncrement }` did nothing.** It was parsed and read by no spacing rule, so
  every page spaced its notes on the built-in 1.2. It reaches the springs now, carried with the
  shortest duration as LilyPond's `Spacing_options` carries them: a ragged line of quarters,
  eighths and sixteenths is 74.95 / 86.89 / 98.83 staff spaces long at 1.2 / 1.5 / 1.8 in both
  engines, where Lily# drew 74.95 for all three.
- **A unisono after a part-two solo lost its "a2".** On a `combinedStaff` the label looked for
  its note in part one, which LilyPond does not engrave there, found nothing, and the next
  label overwrote it. It hangs on part two's engraved head now, and such a passage prints
  LilyPond's Solo II / a2 / Solo / a2.

### Engraving

- **A system no longer settles onto the raised part of the chord symbols below it.** A chord
  symbol's quality is drawn raised and reduced (LilyPond's `\super`), and the raised run — not
  the root's capital — is the top of the symbol's ink. Ten measuring passes still priced the
  symbol as one unraised run, among them the one that reserves the room a chord row leading a
  later system stands in, so the page reserved a box 0.59 staff spaces shorter than the symbol
  it drew and the system above came down by that much; the same stale box also made every
  symbol 1.71 too wide to the horizontal springs, so bars with chord names were spaced a little
  loose. Measured against LilyPond 2.26.0 (probe `chord-superscript-row.ly`): with a raised
  digit in the row LilyPond holds two systems 10.775757854 apart against 10.182224744 without,
  a difference of 0.593533110 which is the raised run's own ink, and Lily# now reads the pair
  to face bits. Three snapshots move.

- **Tab fret digits keep LilyPond's clearance between columns, not a readability gap.** The
  room a tab staff reserves between one column's fret digits and the next's was 0.6 staff
  spaces of clear air, a Lily# choice made so single- and two-digit frets read at one density.
  It is now LilyPond's own rod: each digit's box widened by the default `extra-spacing-width`
  (0.1 a side) and the spacing spanner's padding (0.1) — 0.3 between the inks. The digits
  themselves are still Lily#'s enlargement of LilyPond's tiny fret numbers. Measured on a
  16th-note bass line (2.26.0): with 0.6 every 16th-to-16th spring's reservation reached the
  spring's ideal and the line could not be compressed at all, so a four-bar system LilyPond
  squeezes to 73% was split in two; with LilyPond's clearance the four bars sit on one system as
  LilyPond's do, and a whole bass-tab book lays out in LilyPond's number of systems. Every tab book
  with adjacent digit columns draws a little tighter (284 of 925 in the sweep, 13 snapshots).
  To compare a book with its `.ly` twin, write `fonts { tabFret size 2 }` first so both pages
  carry near-equal digits.

- **A section name's box is the size of LilyPond's rehearsal mark.** The label's em followed
  LilyPond's `\sectionLabel` grob (`font-size` 1.5, 2.616 staff spaces) while its position
  already followed the rehearsal mark's — and the `.ly` twin spells a `form` section name
  `\mark \markup \box`, a rehearsal mark, at `font-size` 2 (2.772). The box now takes that em
  and the frame padding that goes with it, so an `A` boxes 2.744 tall as the twin's does
  instead of 2.60. Measured on a titled page against the pinned twin: the first staff's top
  line stood 0.14 above LilyPond's and now stands on it (the ledger's
  `page.section-label.first-staff-refpoint` goes −0.140 → +0.003, and the `form`-spelled and
  `@mark`-spelled twins of one book read one number at last). Every book with a section name
  moves by the box's growth — 234 snapshots, and a sweep of the 599 tracked books plus 323
  bass-tab books moves 460 — with no system or page count changing; four bass-tab books whose
  first page was full to the last space carry one system over to the next page. LilyPond's
  own `\sectionLabel` size stays a declared Lily#-own deviation, like its left-edge position.
- **A dotted column reserves its dots where it draws them.** Lily# has drawn an augmentation
  dot at the head's ink right plus one dot width (0.45), pushed right by a flag standing on
  its row — but the column's spacing box, the keep-inside-line reach and
  the tie outline still put the reserved dot 0.3 after the head, with no push: two spellings
  of one quantity, 0.15 apart on every dotted note and 0.76 apart on a flagged one whose dot
  is lifted into the flag's band. In one voice a dotted note's own spacing wish outranks the
  rod through its dot at every natural density, which is why no book showed it; a column
  pair two voices share is held by that rod alone, and there the dotted-quarter-against-
  quarter bar of `test/dot-force-down` stood 16.55 against LilyPond's 16.702. The three
  reservation houses now read `DotColumn.Reserved` — the renderer's own rule, fed from the
  spacing side's stem and flag — and the constant is retired. Measured on 2.26.0
  (audit/lp-geometry/probes/dot-column-spacing.ly): four ledger points, opened at
  −0.150000 / 0 / 0 / −0.913200 and closed at zero by this port.
- **A half-tie's spacing box is its stencil's, half a line thickness past the curve.** A
  laissez-vibrer or repeat tie reserved the bare curve span; LilyPond's grob extent is the
  stencil `Lookup::bezier_sandwich` builds, the curve's box widened by half the tie's line
  thickness (0.8 × 0.1 / 2 = 0.04) on every edge, and the grob declares
  `extra-spacing-height (-0.5 . 0.5)` on top. The rod from an l.v. tie to the next column's
  arpeggio is 4.244 in LilyPond and read 4.204 here — the regression book
  `laissez-vibrer-arpeggio` was 0.08 a bar short. Ledger point `semi-tie.lv-to-arpeggio-gap`,
  opened at −0.040000 and closed at zero. Measured on 2.26.0
  (audit/lp-geometry/probes/semi-tie-spacing.ly).
- **A spacing wish's minimum runs head to head; only the rod carries the dots.** The port
  above turned up its own second defect: two dotted cluster chords in one voice (bar 2 of
  the regression book `dots`) came out 0.15 WIDER once the reserved dot stood where the drawn
  one does. LilyPond keeps two separation items on a column — the note column's, whose
  elements are the heads, the stem and the flag, and the paper column's, which every item
  joins — and Note_spacing's minimum reads the first, the column rod the second. Lily# built
  one skyline for both, so a dot that LilyPond lets a neighbour's reversed head tuck under
  (the rod through it: 3.843) had the wish floor the ideal above it (4.043). The wish's
  skylines now hold the note column's elements only; dots and half-ties reach a neighbour
  through the rod alone. Ledger point `dots.wish.cluster-pair`, opened at +0.200 and closed at
  zero. Measured on 2.26.0 (probe book DCW).
- **A whole-note second is a full collision, and a shifted head refines the closing spring
  too.** LilyPond's `check_meshing_chords` treats a distant half-collide of wholes (or longer)
  as a full collide — the 0.5 shift, the UP voice moving one whole head (1.962) — where Lily#
  tested "whole or longer" against its own note value with LilyPond's duration-log threshold
  and took the 0.4 distant-half shift (1.5696) instead; the same slip spelt LilyPond's "never
  merge quarter and half" as whole-against-half, so half and quarter heads could merge under
  `merge-differently-headed` (the regression book says only 8th or shorter merge with an open
  head). And the wish's head-width refinement of the last column into the bar line now reads
  the shifted head as the column pairs do: two voices of half-note seconds (`e2 f` over
  `d2 e`) close their bar at LilyPond's 11.086 (was 10.40), and the ledger book of whole-note
  seconds under a tuplet at 23.44 against 23.443. Measured on 2.26.0. Eight of 924 books move (seven tracked, two snapshots re-based).
- **A column pair two voices share is held open by its rod alone, a skip is no spacing wish,
  and a beam a voice turns round carries its pure stem on the stem's side.** Three findings on
  one bar — `test/beam-over-stem` bar 2, `b8 b s2.` under `s16 d''4 s8. s2`, measured on
  LilyPond 2.26.0 with its NoteSpacing wishes and column skylines dumped — which stood 21.26
  wide against LilyPond's 20.928. LilyPond files a spacing wish per VOICE, from the rhythmic
  grobs that voice engraved, so the pair from voice one's `b8` into voice two's `d''4` a
  sixteenth later is spanned by no wish: it takes the bare duration ideal (1.2) with minimum 0
  and is held open only by the column rod, 1.6042. Lily# treated two voices of one staff as one
  wish (the skyline minimum plus merge_springs' 0.3 headroom: 1.8042), and let voice two's skip
  count as a wish endpoint besides (the head-width refinement: 1.3042 where LilyPond's next gap
  is the bare 1.2). And voice one's beam — down by its pitches, turned up by the voice — kept the
  down beam's pure tip under its up stems, so the stem's spacing band lay below the head and
  its correction into the bar line read 0.1562 for LilyPond's 0.1296, the +0.026 every bar of
  that book carried. The bar reads 20.93 / 20.63 now against 20.928 / 20.628. The dotted-half
  cross-voice book (`test/dot-cross-voice-spacing`) is priced by the same rod: 3.18 against
  LilyPond's 3.33, the 0.15 being the head-to-dot gap (Lily#'s 0.3 against LilyPond's one dot
  width, 0.45 — recorded at `EngravingDefaults.DotGap`, not moved); the Dots grob's own
  `extra-spacing-width` (0 . 0.2) enters its spacing box. And the head-width refinement of a
  wish reads the head where the column FRAME has it — collision shift included — averaged over
  the pair's wishes, as LilyPond's `left_head_end` is the head's extent in the column (a bar of
  `a1` under `b1` a second apart opens 7.042 in LilyPond, the mean of one shifted and one
  unshifted whole head; shift-blind it opened 6.058). Seventy of 924 books move: 29 by the
  wish, skip and beam-tip readings alone, 41 when the dot's box reaches its 0.2, the rest by the
  shifted-head refinement; seventeen snapshots re-base.
- **A note column's spacing skyline is padded as LilyPond pads it, and a head outside the
  staff reaches to its first ledger line.** LilyPond thickens every note column's horizontal
  skyline by 0.15 staff spaces when it builds it (`NoteColumn.skyline-vertical-padding`,
  intrinsic — on top of the 0.08 the distance adds), and a note head beyond the staff carries
  its spacing box to the first ledger line (`NoteHead.extra-spacing-height =
  include-ledger-line-height`). Lily# built the skyline from the bare boxes, so a part that
  missed its neighbour by less than 0.3 in Y — an up-stem flag over a head a step lower, on
  ledger lines — priced nothing: eight flagged eighths below the staff (`time none g8 a b c d
  c b a`) stood 20.24 wide against LilyPond's 21.629 and stand 21.45 now; the same figure
  inside the staff, beamed ledger runs and flagged eighths in metered bars were exact before
  and stay exact. Sixty-five of 923 books move (14 tracked, four snapshots re-based).
- **A flag's spacing box is the glyph's, hung from the stem's real end.** LilyPond places the
  flag half a blot diameter (0.04) inside the stem's end and reserves the whole glyph box
  (3.115 tall for an eighth); Lily# hung a 2.985-tall box from the head plus the ideal 3.5,
  so a lengthened stem's flag was reserved a whole space too low and every flag stopped 0.105
  short at the foot. That foot is what meets the next lower head: the cadenza bar above goes
  from 21.45 to LilyPond's 21.629, and a metered `c'4. b8 a4. g8` bar from 14.97 to 15.037.
  Thirty-nine more books move (2 tracked); the slur-edge extent reads the same band.
- **A beam already building when `time none` arrives mid-bar runs on, as LilyPond's does.**
  Lily# stopped every automatic beam in a bar that turned unmetered. LilyPond's `\cadenzaOn`
  only freezes the measure position, and its auto-beam check reads that frozen reading at every
  stem: a span opened at a bar line (or at a beat the meter ends beams on) still makes no beam,
  but `c'8 d time none e8 f g a b c d e f4 g |` is one beam over the ten eighths in LilyPond
  2.26.0 and was ten flags in Lily#; the frozen reading also carries across the cadenza's
  written `|`, so the eighths of the next unmetered bar beam the same way. Measured on the
  twins; nothing changes for a `time none` written at a bar line.
- **A line's first or last syllable overhangs its bar line, as LilyPond's does.** Lily# used
  to hold a lyric line's first syllable clear of the bar line before it (and its last clear of
  the bar after it) by half the word plus 0.4 staff spaces — a reservation LilyPond does not
  have: a syllable and a bar line never meet in LilyPond's spacing, only the next syllable
  binds. Measured on the twins: the bar that opens "Twin- kle twin- kle" after a rest bar stood
  19.67 staff spaces wide against LilyPond's 18.147 (+1.52, the reservation's deficit to the
  digit), the bar where a second verse begins +1.6. A lead sheet keeps its clearance: there the
  grid's bar lines run through the lyric band (user decision, 2026-08-20).
- **Ink at either end of a line is kept inside the line the way LilyPond keeps it.** The rod
  that keeps a column's ink inside the line runs, in LilyPond, between the line's two end
  columns: the start column at the line's left edge, with the clef, key and meter hanging to
  its right, and the end column at the end bar line's RIGHT edge, the bar line hanging to its
  left. Lily# measured from the prefix's right edge and to the bar line's left edge. At the
  start, a wide first syllable pushed the first note: "Twas" under the first note of
  test/lyrics-verses moved it from LilyPond's 8.585 to 8.97 (the syllable may run under the
  meter, as LilyPond lets it), and test/lyrics' whole first line stood 0.3 to the right. At
  the end, a wide last syllable or chord symbol left one bar-line ink (0.19) too much: the
  last bar of test/lyrics-verses, a whole note under "saved", was 8.44 wide against
  LilyPond's 8.251. Inert wherever the springs already clear the ink — every column but a
  line's first and last, and those only when something on them is wider than its spring. A
  lead sheet keeps its own line-start edge (the grid's opening bar plus its gap, user
  decision 2026-08-20).
- **A lyric line broken across two systems reserves nothing at the break, as LilyPond's
  does.** Where a word or phrase ran on past a system's end, Lily# held the last syllable
  clear of the end bar line by 0.4 staff spaces, and held the next system's first note clear
  of its first syllable by the same 0.4 — two pre-port quantities kept "until measured".
  Measured on LilyPond 2.26.0: a line ending on "bright-" (hyphenated onward) and one ending
  on "bright" (a word's end) give the same last bar, 18.686 staff spaces, the syllable's ink
  ending on the bar line's right edge; and a next system opening under "lyrically" or
  "Lyrically" keeps its first note at the plain 5.8 from the clef, the syllable running back
  under the clef. Lily# drew that bar 19.28 wide and that note at 7.72 / 8.15. Only the rod
  that keeps ink inside the line holds a system's first and last syllable now. Moves every
  book whose lyric line breaks under a syllable wider than its note's spring.
- **A narrow syllable after a wide one no longer pushes its note.** The rod between two
  syllables is LilyPond's arithmetic on their reaches, and a syllable narrower than the note
  head's alignment extent — "I" centred on a quarter — reaches a negative distance left of its
  column, which shortens the rod. Lily# clamped that reach at 0, so "How I won- der" was 0.16
  staff spaces wider than LilyPond's on every such bar.
- **A chord or lyrics row's bar is as long as the music's bar.** The row grids its slots on
  the score meter, so a pickup bar under a row carried a whole meter of row spacer and was
  spaced for it — amazing-grace's one-beat pickup stood 6.34 staff spaces wider with its chords
  row than without. A bar under a mid-piece meter change the row never saw was the same. The
  row's slots now scale to the music's bar, share for share, and the page is the same with the
  row as without it.
- **A lyric row no longer votes on the spacing basis.** The shortest duration a piece is
  spaced on is the most common per-bar shortest of its notes; LilyPond turns lyric syllables
  away from that vote, and Lily# now does too. An independent `lyrics` row of eight words over
  a bar of quarters used to loosen the whole piece to the eighth, and a row that `sings` a
  melody used to vote a whole at each of the melody's rest bars, which could tighten a piece
  of eighths to the quarter's basis. A chord row's cells keep voting (a chord symbol is a
  rhythmic grob in LilyPond).
- **An arpeggio in a dotted total is spelled as compound metre spells it.** `<< c e >>4.`
  is two eighths under 2:3 (a duplet), `<< c e g >>4.` three plain eighths, `<< c e g a >>4.`
  four eighths under 4:3 — where the members used to be dotted (three dotted eighths under
  3:2, four dotted sixteenths), a spelling no engraver writes. A plain total is unchanged:
  three in a quarter are eighths under 3:2, five sixteenths under 5:4, as before. The MIDI
  is unchanged (the shares were always equal); the MusicXML carries the note type and
  time-modification.
- **A system-start brace, bracket and bar stand where LilyPond puts them, and a staff line's ink
  stops short of its span.** LilyPond places the `SystemStartBar` left of the staff start
  (padding −0.1) and each delimiter against that bar — a brace by 0.3, a bracket's stroke by
  0.8 — where Lily# measured from the indent: the brace stood 0.06 to the right, the bracket
  0.285 and the bar 0.02 to the left. Every staff line, tab string line and ossia line now
  begins and ends half its thickness inside the staff's span, as `staff-symbol.cc` draws it
  (Lily# drew to the edges, which showed as a 0.05 overhang after a courtesy signature). All
  measured on LilyPond 2.26.0; almost every snapshot moves by these amounts and nothing else.
- **Two ledgered notes are held apart by LilyPond's ledger-line rod.** Consecutive columns whose
  heads carry ledger lines on the same side stand 2 × head width × 0.25 plus the heads' extents
  apart (1.9563 staff spaces for black heads), a rod LilyPond's `Ledger_line_spanner` raises and
  Lily# did not: a run of 32nds on `a''` is 1.9563 per step as in LilyPond (1.8042 before), and
  `samples/canon-in-d`'s ragged length went from 17.66 staff spaces short of LilyPond's to 1.72.
  The rod reads its column outlines padded 0.08, LilyPond's `PaperColumn` value, not the note
  column's 0.15, which had pushed apart two voices whose notes never meet.
- **A part-combine `a2` / `Solo` label is placed as LilyPond places it.** It is an outside-staff
  item stacked after the text scripts and before the marks, so a chord row or a section label
  above clears it — it stood at a flat 1.5 over the system and could be drawn through the
  `Intro` label — and its baseline is LilyPond's `aligned_side`: the label's extent box kept 0.5
  over the heads and stems of its own voice (a beamed stem at its drawn length) or over the
  staff. Five ledger points agree with LilyPond to the last digit of the reading.
- **A feathered beam fans.** `@feather(right)` and `@feather(left)` were read and carried to the
  renderer, which drew a plain beam; the secondary beams now meet the primary at one end and
  open to a full beam spacing at the other, by LilyPond's feather factor (`beam.cc`).
- **A tab's strings are chosen by planning the fingering of the whole voice.** The chooser
  weighed one note at a time and could not see a shift a phrase needs later. It now chooses,
  for all the notes at once, the string, fret and hand position that cost least: a shift is
  cheaper the more time the hand has (a rest, a leap, an open string), and stretches, string
  skips (the octave shape excepted), slurs across strings and leaving low position are charged.
  The weights were fitted to fingerings agreed passage by passage in real books and held
  against every tab fixture, where a bar that fits in the first position stays there. A written
  `\N` is never overridden. This is Lily#'s own model, deliberately not LilyPond's; a tab with no
  `\N` may show different fret numbers than 0.6.0 did.

### Diagnostics

- **LYS1033 says which outputs are not cut.** The expansion budget is the picture's alone: it sits
  where the line breaker would fail anyway, so it is a limit of what can be drawn, not of the
  book. `lysc midi`, `lysc xml` and `lysc ly` write the whole expansion, and the warning now
  says so instead of leaving a reader to discover a million-note MIDI after "the picture is
  truncated".
- **LYS1005 names the glued custom text.** `form main { A _ "shown" }` is a reference to a section
  named `_` with a display label (a legal name, so the space cannot be forgiven); the custom
  text is the glued `_"shown"`. When nothing declares `_`, the error says to glue the quote.
- **A chord track's section counts toward the section's length, and LYS2007 reads it.**
  `chords prog { section A { Dm7 | G7 } }` over a melody whose `section A` writes one bar used
  to pass `lysc check` silently, and the page put G7 on B's first bar beside B's own chord.
  A section spans as many bars as its longest voice, and a named chord row is one of its
  voices: the melody's A is now warned as the short one (LYS2007, anchored on its section
  name, naming the track as the longer voice) and padded to two bars, so G7 stands in A's
  second bar and B begins after it. The other direction warns too — a row that writes fewer
  bars than the part says the remaining bars carry no chord. Both spellings are read: the
  part-major track (`chords prog { section A { … } }`) and a named chords block inside a
  section-major section, where one part beside a chord row used to end the pass before the
  count was compared. Lyrics tracks are left out on purpose: a lyrics section longer than its
  music is a stacked verse.
- **LYS2015: a `partial` inside `time none` does nothing, and says so.** The clock stands still
  in an unmetered span, so there is no bar length for a pickup to shorten; the page silently
  ignored it. It is a warning now, and the LilyPond twin leaves the `\partial` out (LilyPond's
  would move its frozen measure position and the bar after the cadenza would then fail its bar
  check).

### MIDI, MusicXML and the LilyPond twin

- **`lysc ly --pin-fonts` writes a `\paper` block that pins the twin's text faces.** LilyPond
  2.26 replaces `fonts.serif` and `fonts.sans` with the generic names `serif` and `sans` under
  its svg backend only, and fontconfig then picks whatever the machine prefers, so a twin
  measured through `lilypond -dbackend=svg` reads machine-dependent text widths (a chord `Am`
  4.34 against the canonical 3.93; a title baseline 0.21 off) while its pdf does not. The flag
  writes the two `property-defaults.fonts.serif / .sans` lines the LP-fidelity probes carry,
  right after `\version`. Off by default: the twin is a control on LilyPond's own paper, and
  the corpus's twins do not move for a measuring convenience.
- **A `:|:` inside a form's `|: … :|` splits it into two repeats for the MIDI.** `|: B :|: C :|`
  is `|: B :| |: C :|` on the page, in the twin and in the MusicXML; the MIDI played the whole
  body per pass (B C B C). It now plays each run as its own repeat (B B C C), the block's
  `:|*N` on every run and an ending with the run it follows.

- **A form-level `:|:` is two bars to the MIDI and the MusicXML too.** `form main { A :|: B :| }`
  draws `:|` after A, `|:` before B and `:|` after B, and the LilyPond twin writes the two
  repeats; the MIDI sounded A B A B and the MusicXML wrote one backward repeat on the last bar,
  both skipping the divider and rewinding at the closing `:|`. The form reader now reads the
  divider as the one-sided `:|` it starts with (repeat from the beginning) followed by a block
  the next form-level `:|` closes — its `:|*N` the count, its trailing endings the block's — so
  the MIDI sounds A A B B and the MusicXML carries all three repeat bars. A second `:|:` closes
  that block and opens the next, as it does inside a written `|: … :|`.

- **The LilyPond twin plays a form-level `:|`.** A `:|` written in the form outside any
  `|: … :|` block repeats the piece from its beginning, and the `:|` half of a form-level `:|:`
  is the same bar. The twin used to write LilyPond's `\bar ":|."` — a glyph that repeats
  nothing — and warn; for `A :|: B :|` it drew no bar after A at all and repeated only B. It now
  writes the stretch before the bar as a `\repeat volta N { … }` body (`:|*N` is N), so
  `A :|: B :|` is `\repeat volta 2 { A } \repeat volta 2 { B }` and the page's three repeat
  bars are all in the twin; a chord row is split at the same bars. A book that opens with such
  a body writes `printInitialRepeatBar = ##f`, since no `|:` was written at the start and the
  page draws none. Two rewinds (`A :| B :|`) nest, and the twin warns that LilyPond then replays
  the inner repeat on the outer pass where Lily# replays the written stretch once.

- **A section voice shorter than its section-mates is padded in every export, as it is on the
  page.** A section spans as many bars as its longest voice — a part or a named chord row — and
  the page has always padded a shorter part's staff with silent bars; the LilyPond twin, the
  MusicXML and (where only a chord row made the section longer) the MIDI did not. A one-bar
  melody A beside a two-bar bass A put melody's B under bass's A in the twin and gave the
  MusicXML one part a measure fewer than the other; beside a two-bar chord row A the MIDI
  played B a bar early. All four readers now take the count from one place: the twin writes
  `s1 |` per missing bar (a silent `\chordmode` bar for a short row), the MusicXML a whole-bar
  rest measure, the MIDI the bar's silence — with one extra bar line when the voice's last bar
  was left open, since that one only closes it.
- **An empty `| |` bar reaches the MusicXML and the LilyPond twin as the bar of silence it
  is.** The page and the MIDI have filled it with a full-measure spacer since 0.6.0; the
  twin copied the bare bar lines — bar checks to LilyPond, which take no time — so
  `c'1 | | e'1` was two bars in the twin and three on the page, and an empty pickup
  `partial 4 | c'4 …` failed LilyPond's bar check and pulled the `c'4` into the pickup; the
  MusicXML wrote no measure for the gap and pulled the note the same way. The twin now
  writes the spacer the author would have typed (`s1 |`, `s2. |` in 3/4, `s4 |` under
  `partial 4`), the MusicXML a whole-bar rest of the same length, both under the page's own
  rule: a bare `|` (or a `|:` that does not open the scope) with no music since the last bar
  line is an empty bar; `||`, `:|` and `|.` on an empty span decorate and open none. A form's
  `|:` (`form main { A |: B :| C }`) opens the repeated section's scope and pairs with nothing:
  the first cut of this rule read it as an empty bar and wrote `s1` before every
  `\repeat volta`, so LilyPond drew one more bar than the page.
- **`lysc ly` returns from a cadenza opened mid-bar with `\partial`.** `c'8 d time none … |
  time 4/4 c1 |` closes the cadenza's bar at the written `|` and starts the next bar fresh;
  LilyPond's measure position froze at the `time none` and `\cadenzaOff` does not reset it, so
  the twin's returning `\time 4/4` was a mid-measure meter change, the `c1 |` failed its bar
  check and an automatic bar line landed inside the whole note. The twin now writes
  `\cadenzaOff \time 4/4 \partial 1` (`\partial 2.` for a 3/4 return) — measurePosition 0, no
  warning, the page's bars. A span opened at a bar line needs nothing and gets nothing.
- **An empty bar inside a pickup sounds as long as the pickup.** `partial 4 | c4 d e f |` — an
  empty pickup written as a bare bar — draws one beat of space on the page and now sounds one
  beat in the MIDI, at the piece's opening, after a mid-piece `partial`, and from a section
  header's `partial`. The exporter had no `partial` arm at all, so the gap was priced at the
  whole meter and the first note sounded a full bar late (tick 1920 against 480 for the `s4`
  spelling). Written-out pickups (`partial 4 g4 |`) were never affected.
- **`lysc ly` left-aligns a melisma syllable as the page does.** A syllable held over a melisma
  (`saved~`, `star __`) stands with its left edge on the note on the page (LilyPond's
  `lyricMelismaAlignment`); the twin's `\lyricmode` line, carrying durations instead of
  `\lyricsto`, gave LilyPond no melisma to align by and the word was centred. The syllable now
  carries `\once \override LyricText.self-alignment-X = #LEFT`, so the two engravers put the
  word in the same place. (`~` stays Lily#'s own melisma source; LilyPond reads it from the
  music's slurs, ties or `\melisma` — decided 2026-09-08.)
- **`lysc ly` writes a `chords` row as a `ChordNames` context.** The track goes out as a
  `\chordmode` variable — `C Am | F Gm7-5 |` is `c2 a2:m | f2 g2:m7.5- |` — following the
  form's repeats and endings exactly as the music does, and the row stands above the staff
  it is written over. Every Lily# spelling is rewritten into an entry LilyPond accepts
  (`F#m7-5/C#` → `fis:m7.5-/cis`, `Cmmaj7` → `c:m7+`, `C7sus4` → `c:sus4.7`, a roman degree
  resolved in its key), and LilyPond then prints its own name for the chord, which is what
  the twin is for. A `.` extends the entry (at a bar's head it is the silent slot), `r` is LilyPond's
  `N.C.`, an empty bar (a bare `|`, a pickup's included) is silent and a pickup bar is as
  short as the section's `partial`. A quality Lily# does not know goes out as its root alone, with a warning; a row shown `as
  roman` is named, not numbered, on the twin (warned once). The twin used to drop every chord
  row with "the twin has no chord row".
- **`lysc ly` writes a part's inline `@chord` marks as a `ChordNames` context over its
  staff.** The symbols are taken from the page's own placement — at the note's moment, a bare
  `@chord` named from the notes it sits on, a pickup bar as short as the page's — with silence
  between them, so LilyPond prints exactly the symbols the page prints. They used to be
  dropped with "@chord dropped (out of scope)". A numbers-only tab gets none, as on the page.
- **`lysc ly` writes the lyrics.** Every line the page places — a verse attached under a staff,
  a row that `sings` a part, an independent row spread over its bars, each stacked verse — is a
  `Lyrics` context over a `\lyricmode` line whose syllables carry the durations the page
  aligned them to (`Mu4 -- sic4 fills4 the4 |`, a melisma one longer syllable), standing
  below its staff or at the row's place. The twin used to carry no lyrics at all ("lyrics
  row … is not exported", and an attached verse went without a word). Not carried: the stanza
  number before verse 2+, and an extender's exact end (LilyPond runs `__` to the next
  syllable; the page stops it at the last held note).
- **`lysc ly` writes an arpeggio as the tuplet it is.** `<< r c cis >>4` becomes
  `\tuplet 3/2 { r8 c cis }`, `<< c e g a >>` after a quarter `c16 e g a`, with the octave
  marks recomputed for LilyPond's `\relative` — Lily# stacks the members on the root and the
  next note follows the root, LilyPond reads them one after another — and the duration after
  the group written out where the two carries differ. The twin used to drop the whole group
  with "Arpeggio not exported", a bar short by the group's duration, so no book with a
  written-out broken chord could be put against LilyPond.
- **The twin reopens every section at a quarter.** A section whose first note writes no
  duration is a quarter on the page (the section is a reusable unit; decided 2026-09-04), but
  the twin let LilyPond carry the previous section's last duration across the boundary — a
  section opening `aes aes'` after a whole read as two wholes and failed its bar check. The
  twin now writes the quarter out at such a boundary; a section following a quarter is
  written as before.
- **The note after a dotted one carries the dot in the twin too.** `c4. d` has been a dotted
  quarter followed by a dotted quarter on the page since 0.4.0; the twin still wrote `d4`,
  five eighths where the page has six. It writes `d4.` now.
- **`lysc ly` writes a condensed or combined staff.** A top-level `condensedStaff` or
  `combinedStaff` was left out of the twin with no warning; it is written as `\new Staff {
  \clef … << \a \\ \b >> }` and `\new Staff { \clef … \partCombine \a \b }`, with the first
  part's clef as on the page, and inside a group it is no longer reported as not exported. A
  `grandStaff` inside a `staffGroup` is written as a nested context.
- **Parts named apart only by a digit keep their own music in the twin.** `part fl1` and `part
  fl2` both became `\fl`, so LilyPond kept the last definition and every staff played it; the
  digits are spelled as words now (`\flOne`, `\flTwo`), and a name that still collides takes a
  suffix. Between this and the entry above, the twins of 35 of the 599 tracked books change, and
  all 35 compile in LilyPond 2.26.0.
- **`lysc ly` writes a feathered beam** as `\once \override Beam.grow-direction = #RIGHT` (or
  `#LEFT`) on the note that opens it, and LilyPond draws the same fan. `\featherDurations` is not
  written: it changes the played durations, which `@feather` does not. The MusicXML carries no
  feather, because that exporter writes no `<beam>` elements at all.

## 0.6.0

Three spellings settle to LilyPond's, the page chooses how many systems a score gets, and
a tab staff's stems and beams join the skyline. The rest is defects found by engraving real
scores against LilyPond's picture of the same book.

### Breaking changes

- **`nobreak` is renamed `noBreak`.** The break family is now LilyPond's own spelling minus
  the backslash — `break`, `noBreak`, `pageBreak`, `noPageBreak` — the rule every other
  command already follows (`grandStaff`, `tempo`). The lowercase `nobreak` was the one word
  that had been folded to lowercase, and it is not accepted any more: it reads as an
  ordinary name, so a book that still writes it reports an undefined phrase at that word.
- **`@invertedturn` is renamed `@reverseturn`.** Ornament names are LilyPond's, and this
  one was MusicXML's (`inverted-turn`) — the only articulation spelled by another
  vocabulary. The MusicXML importer writes the new name; the old one is an unknown
  annotation (`LYS1008`).
- **`tocoda` is gone; `to coda` is the one spelling.** The run-together word was a second
  way to write the same instruction, and one spelling per instruction is the rule that
  retired `$`. `tocoda` reads as an ordinary name now — in a form, an undefined section.
- **A chord row has no `s`, and a `.` at a bar's head is the bar's silent slot.** The spacer
  said nothing the row could not already say: an empty bar is `| |`, a slot with no chord
  is `.`, and `r` prints N.C. So `| . C |` is "no chord, then C on the second beat" — it
  used to be refused (LYS2010, retired) — and `s` in a `chords` block is reported (LYS1028)
  with the two spellings that replace it.

### Language

- **Transposing instruments, both ways.** An `instrument` preset now carries its chromatic
  transposition — `clarinet`, `clarinet-a`, `trumpet`, `trumpet-c`, `horn`, `soprano-sax`,
  `alto-sax`, `tenor-sax`, `baritone-sax` — so a part written the way its player reads it
  plays at concert pitch in the `.mid` (an alto saxophone's written `c'` sounds E♭). And a
  top-level **`pitch concert`** says the letters are what SOUNDS: every such part is then
  printed the way its player reads it, pitches and key signature together — the alto
  saxophone's `c'` prints as `a'`, in A major when the piece is in C, and still plays C.
  The default, `pitch written`, is what every book meant before. A part header takes the
  same words for that part alone — `part sax { instrument alto-sax pitch written }` beside a
  top-level `pitch concert` copies the saxophone from its transposed part-sheet and the rest
  from the concert-pitch score — and `score full pitch concert { … }` prints one score at
  concert pitch, the conductor's score of a book written either way. Without a transposing
  `instrument` the word changes nothing. Octave-only instruments (bass, piccolo, a `transposition 8vb`) keep their notation
  under both, as a printed concert score keeps them. The MusicXML carries the same
  `<transpose>` whichever way the file is written; the LilyPond twin wraps the part in the
  `\transpose` the shift amounts to.
- **A part-major book's `transpose` reaches the `.mid`.** A `transpose` on a part whose
  sections are written inside the `part { }` block moved the page and not the playback; the
  section-major spelling of the same book already played the transposed pitch.
- **`pageBreak` and `noPageBreak`** — the page-break pair beside `break` / `noBreak`, written
  where those are written: in a section's music after a bar, or in a `form` between sections.
  `pageBreak` forces a page break and, with it, the system break (LilyPond's `\pageBreak`
  carries both permissions); `noPageBreak` forbids a page break and leaves the line alone.
  The LilyPond twin writes `\pageBreak` and `\noPageBreak`.
- **A score row's `sings` is that row's own melody.** `lyrics verse sings alt` among the
  score items places the `verse` track at the alto's rhythm — under the alto staff it is the
  alto's verse — whatever the definition said. The definition's `sings` is now the track's
  DEFAULT, taken by a row that writes none; a second target on a definition block is still
  `LYS7005`, but rows never conflict. It is how a chorale writes its words once and puts
  them under the soprano, alto, tenor and bass: `staff alt  lyrics verse sings alt`. Until
  now the row spelling was the same single track property, so the second staff's row was
  refused (`LYS7005`) and, inside the group, refused again (`LYS6012`).

### New

- **`--batch <list>`: one command, many files, one process.** Most of what a `lysc` run
  costs is starting up — a one-bar file takes 1352 ms through `lysc svg` where a real
  three-page book takes 2390 ms, so about a second of every run is fixed cost paid before
  any music is engraved. `--batch` reads a list of files and pays it once: measured on the
  same machine with outputs verified byte-identical to the one-process-per-file runs, 120
  small books go from 995 to 45 ms each (22x) and 40 books of a real bass-tab corpus from
  1149 to 231 ms each (5x — the same ~950 ms saved per book, a smaller ratio only because a
  big book spends more of its time actually engraving). The flag is read before the command
  is chosen, so every command has it, including the ones that write nothing (`check`,
  `layout`). One file per line, `#` comments and blank lines skipped, and a TAB naming that
  file's output; `-` reads the list from a pipe. A file that fails does not stop the rest —
  the exit code reports that one did. It cannot be combined with `-o`, since one path
  cannot name many files.
- **`-j, --parallel <n>`: engrave several files of a batch at once.** Off by default; `-j 0`
  uses one worker per processor, and each file's report still arrives as one block rather
  than interleaved. ⚠️ It buys much less than the core count suggests, and the measurement
  ships with it rather than being left for the reader to discover: on 16 processors, `ly`
  goes 2.2x faster and `svg` only 1.3x — for five times the CPU — because rendering
  serialises on a process-wide lock (HarfBuzz shaping is not thread-safe). Several `--batch`
  processes scale far better than one `--batch -j N`, since separate processes have separate
  locks: a 597-book two-sided corpus comparison takes 11.1 minutes as one process per book
  and 2.6 minutes as ten `--batch` processes. `pdf` refuses `-j` outright — PdfSharpCore
  allows one font resolver per process and each document re-points it at its own faces, so
  concurrent PDF export would embed another document's fonts.
- **A preview whose editor tab is closed still answers.** Closing the last editor of a file
  disposes its document, and every path that looked the previewed document up among the OPEN
  ones then did nothing at all: picking another score moved the picker and left the picture on
  the first score, with no banner, no dimming and no line in the log — indistinguishable, on
  screen, from a picker that does not work. The preview now reopens its source silently (no
  editor is shown) and renders, and the same goes for the refresh that follows a language
  server restart. The one source that cannot come back — an unsaved buffer, whose text went
  with its editor — says so rather than drawing an empty score.
- **The preview's score picker selects the score it names.** A score's output name — the
  stem `svg --all` writes and the word `--score` takes — was computed in three places, and
  only the renderer's copy dropped an extension: a score written `score main "Take 1.0"`
  was therefore offered in the picker under a word (`Take 1.0`) that the renderer matched
  against nothing, so choosing it silently drew the FIRST score and the preview looked
  stuck on the main score. The rule now has one home, which the renderer, the picker and
  the duplicate check all read. The picker's LABEL is still what the writer wrote; its
  value is the output name. And the answer now says which score was drawn, so a selection
  that cannot be honoured — a block renamed or deleted since it was picked — moves the
  picker to the score on screen instead of naming one that is not the picture.
- **The score preview's color scheme is a setting** — `lilysharp.preview.theme`: follow
  VS Code's theme (the default, and what the preview always did), always light (the printed
  page), or always dark (the page inverted). A change reaches every open preview at once.
- **`lysc layout` reports the pages.** A line `pages: 3  |  systems per page: 8, 7, 3`
  follows the system count, so how the systems fell onto pages — the page breaker's one
  decision a system list cannot show — can be read without rendering. The report used to
  say the engine had no printed-page concept, which stopped being true when the page chain
  was ported.

### Diagnostics

- **A chord track's bars are no longer checked as if they held durations.** A `chords`
  track written per section (`chords prog { section A { s | C#m | … } }`) was walked by the
  bar check like a part, and a slot's `s` or `r` was priced as a quarter rest, so every bar
  of a 2/4 row that held one was reported as "1/4 is less than 2/4" (LYS2001 / LYS2006). A
  chord row is measure-relative — its entries carry no duration and divide the bar on the
  beat grid — so its bars can be neither short nor long, and the row keeps only its own
  grid diagnostics (LYS2009 / LYS2010).
- **Two scores whose names differ only after a dot are reported as the collision they are.**
  The duplicate-output check compared the written basenames, so `score main "Take 1.0"` and
  `score main "Take 1.1"` read as two names — while both write `Take 1.svg` and both offer
  the same word to the preview's picker. It reads the output name now (LYS6001 as before).
- **A `time` written after a repeat body no longer reaches back into it.** A percent or
  volta body closes its own rendered bars, so the written bar around it may read
  `repeat percent 9 { r1 } time 1/4 r4 |` — the meter change belongs to the `r4`. The bar
  check adopted the bar's last meter before looking inside the repeat and reported the
  body's `r1` as "1 exceeds 1/4" (LYS2002) while the page drew the book right; the meter
  is now adopted in the order it is written, so the two spellings — with and without a bar
  line between `}` and `time` — are both clean, as they render alike.

### Engraving

- **A rest inside a tuplet is one of the things the bracket clears.** LilyPond's bracket is
  placed one padding beyond every column of the tuplet, rests included — a rest's own ink, or
  its invisible stem where a beam runs over it; only the *slope* is taken from the outer
  notes. Lily# cleared the notes and chords and the staff edge, which is the same answer for
  a rest on the middle line (its ink never reaches past the staff) and the wrong one for a
  rest written at a pitch or pushed out of the staff by another voice. Measured on a probe
  built for it: with the middle rest of `tuplet 3/2 { c'4 c4@rest c' }` written at C4,
  LilyPond's bracket drops from 4.1 to 5.35 below the middle line — exactly the rest's ink
  bottom plus the padding — and does so with the rest at the tuplet's edge just as in its
  middle; Lily#'s bracket stayed at 4.1, 1.250000 shallow on both books, and now reads
  LilyPond's to +0.000021 (the constant every staff-gap book of this family carries). A rest
  raised to the bracket's far side moves nothing, in either engine. None of 920 swept books
  writes a rest that reaches past its tuplet's notes, so none moves.
- **`c4@rest` inside a `tuplet { }` draws a rest.** The tuplet body is walked by an arm of
  its own, and that arm did not know the pitched-rest spelling: it built a note — head, stem,
  a note-on in the `.mid` and a `<pitch>` in the MusicXML — where the same words outside the
  tuplet drew a rest. The probe above found it: the first reading of the deep-rest book came
  back 0.545 (a notehead's half-height) off the prediction instead of 1.25. The tuplet arm now
  turns the spelling aside exactly as the main walk does, with slur and beam bounds, scripts
  and dynamics riding it as they ride `r4`; a pitched rest outside a tuplet now also takes a
  beam bound (`c8@rest[`) and a dynamic, which its plain sibling already did.
- **The beam's face is read in one frame — the stems' — by everything that must clear a
  beam.** A quanted beam's two heights are LilyPond's `positions`: the line at the beam's two
  outer *stems*. Lily# kept them but interpolated between the two outer note *columns*, one
  stem attachment to the left of where the numbers were true — 1.2392 staff spaces for an up
  stem. A reader that asked for a member's own tip by its column got the right answer by
  accident, since the shift cancelled; a reader that asked at a real x — a slur attaching to a
  beamed stem, a tuplet's bounding rest, a rest the beam runs over — read the line one
  attachment further along its slope. The tuplet engraver had then corrected its note tips for
  a shift they had never suffered, so a bracket that follows a sloped beam sat too deep by the
  slope times an attachment, its slope exact. The layout now carries the outer stems' x and
  every reader hands in a drawn stem's x. Measured against LilyPond: the two follow-beam
  ledger points close from +0.007811 and +0.006569 to +0.000021; a bracket following an
  up-stem sloped beam reads LilyPond's `positions` (2.279442 . 2.191727) to six digits; a
  slur's attachment on a beamed, rising eighth is now within the drawn precision of
  LilyPond's 2.094 above the middle line where it had been 0.14 high. Forty-eight of 920
  swept books move — tuplet numbers and brackets over sloped beams by up to 0.3, slur ends
  on beamed stems by up to 0.15, and the pages that reflow beneath them.
- **A tuplet bracket bounded by a rest opens at the rest's ink, not at a stem the rest
  does not have.** LilyPond's bracket spans from bound to bound, and a bound is the column's
  stem only when that stem is visible and points the bracket's way; a rest's is invisible,
  so a rest-bound bracket starts at the rest's own edge. Lily# gave every bound a stem in the
  bracket's direction, which put a rest-bound bracket's left end one up-stem attachment
  (1.17 staff spaces) to the right of LilyPond's — and since that end is the origin the
  bracket's slope is laid out from, a sloped bracket over a bounding rest also sat low: on
  `e8[ tuplet 3/2 { r8 d c ] } c8` the bracket's slope was LilyPond's to six digits but both
  ends were 0.040574 too deep. Both ends now read LilyPond's `positions`
  (1.989688 . 1.569942) exactly. The same rule covers a stemless whole and a stem pointing
  against the bracket, whose column edges are the attachment edges Lily# already read, so
  those do not move.
- **A beam's room in the staff skyline is the drawn beam, not the note columns under it.**
  The band a beam reserves against the neighbouring staff began at its first note's column
  and ended at its last — one stem attachment (1.24 staff spaces on an up-stem beam) left of
  the drawn beam at both ends. LilyPond reserves the beam's stencil, from half a stem thickness
  outside the first stem to the same outside the last. So a grob of the other staff that
  reached into the gap just left of a beam's first stem met a beam that was not there:
  measured on a probe built for it, a down stem standing at the first column's edge under an
  up-stem beam pushed the staves 1.000000 further apart than LilyPond does (the stem meets
  LilyPond's staff lines, not the beam); the band now at the drawn beam reads LilyPond's
  7.050000 exactly, and the probe's two controls are unmoved. Ninety-three of 920 swept
  books move by 0.01–0.12 in system or page position — the same mechanism in small doses.
- **A beam bracketed onto a tuplet's bounding rest is the tuplet's own beam, and hides the
  bracket.** `tuplet 3/2 { r8[ c c] }` drew a bracket over its beam: the beam's bounds were
  read from its note members, so a beam that opens on the rest looked one column shorter
  than the tuplet. LilyPond's beam is bound to the rest's column, the bounds are equal, and
  the bracket is not printed — only the number, centred on the bracket's X span from the
  rest's ink at the bracket's height (measured: LilyPond's number centre 3.0042 from the
  rest's left, 1.5 above the middle line; Lily#'s the same). No swept book writes this
  shape yet.
- **A tuplet bracket that rides a beam is placed the way LilyPond places one, and never off
  another staff's beam.** Three faults, all in the same arm. LilyPond has two ways of placing a
  bracket and they are separate: one for a bracket that follows its own beam — take the outer
  two *columns'* stem tips, slope between them, done — and one for everything else, where the
  staff joins in, the slope is checked against the notes' own contour and then damped. Lily#
  ran the second arm's checks over the first arm's answer as well, so a bracket over a beam
  that rises while its notes descend was flattened; it read its two points at the outer *note*
  columns, missing a bounding rest's invisible stem and reading each note's tip half a
  stem-attach off along the slope; and it would accept a beam belonging to a **different
  staff** of the same measure, so a grand staff's right-hand triplet followed the left hand's
  beam. Measured against LilyPond: on a triplet whose beam crosses its leading rest the
  bracket's rise is now 1.043497 where LilyPond's is 1.043497 (it was flat); on the two-staff
  fixture the bracket is now flat at 3.4 below the middle line, LilyPond's own answer, where it
  had been at 1.5. Eighteen of 920 swept books move, all of them books with a tuplet bracket
  over a beam.
- **A manual beam may open and close on a rest, and it reaches that rest.** `r8[ c d e]` and
  `c8[ d e r]` were refused — the bracket on the rest was dropped on the floor, the `]` then
  paired with nothing (`LYS4016`), and the grouping the file asked for was discarded in favour
  of automatic beaming. LilyPond has no such rule: it gives every note column a stem, a rest's
  included, and beams it like any other, so a rest inside or at the edge of a beam is one of
  that beam's stems — an invisible one, standing on the rest's own ink centre, with the beam
  reaching half a stem thickness past it. Lily# now reads the bracket on a rest, makes that
  rest the beam's bound, and draws the beam to it, at LilyPond's own arithmetic: measured on
  `r8[ c e g]`, the beam spans exactly LilyPond's x and its line sits at LilyPond's
  `positions` to the drawn precision. The invisible stem still stays out of the stem scoring,
  as LilyPond keeps it out (`Stem::is_normal_stem`), so a beam bounded by a rest quants like
  the same beam with a note in the rest's place. Automatic beams still end at every rest —
  only a written bracket spans one. Sweeping 920 books moves none of them: no file could
  write this before.
- **A tuplet whose first or last note is a rest no longer draws its bracket through that
  rest.** A tuplet bracket may follow the beam under it instead of the staff, and which one it
  does decides everything: following the beam, the bracket rides one padding off the stems and
  slopes with them; not following it, the staff joins the encompass points and the bracket
  comes out flat, one padding clear of the staff. Lily# picked the beam whenever one covered
  every *note* of the tuplet, treating rests as transparent. LilyPond looks only at the
  tuplet's OUTER two columns and needs a stem on each of them — and a rest column has no stem
  — so `tuplet 3/4 { r16 c a, }`, whose two notes are beamed, has no parallel beam there at
  all. Lily# followed the beam, drew a bracket sloped 1.38 to 2.15 below the middle line, and
  ran it straight through the 16th rest, whose ink reaches 2.05 down. It now reads LilyPond's
  rule and draws the flat bracket LilyPond draws, 3.40 below the middle line. Sweeping the
  tracked corpus and 323 bass-tab books — 920 in all — moves 11, every one a book with a rest
  at a tuplet's edge, and moves nothing on any of them but the bracket and its number.
- **A boxed label — a rehearsal mark, a section label — is the size and height LilyPond draws
  it.** Three spellings were off at once and they could only be corrected together. The label's
  em was `FontSize × 0.6` = 2.4 where LilyPond's is `text-font-size` 11pt = 2.2 staff spaces
  magnified by `font-size 2`, 2.771822 — 13.4% small. The frame was drawn around the letter's
  *ink* rather than around its em box, so the box's height followed the letter and the error
  changed sign from one letter to the next (`A` too tall, `x` far too tall, `Q` too short) —
  which is why no constant could fix it and why fitting the box to `A` alone moved every other
  letter further away. And the frame's bottom stood 1.1 over the top line where LilyPond puts
  it at 0.85 (`padding` 0.8 plus the staff line's half-thickness), a quarter space of air on
  every marked system. Measured on a two-staff bass-plus-tab book, the marked system's height
  was 0.305957 over LilyPond's and is now 0.002761; ten ledger points improve and none regress.
  ⚠️ The label is also 0.65 WIDER than it was, which is the correct width — and a wider box at
  a system's head can meet ink hanging below the system above it, so a book with a very low
  note under a labelled system may space its systems differently.
- **A numbers-only tab prints no `@chord`.** A tab written (or defaulting to) `as numbers` is
  the line that carries the fret digits *because* the notation staff above it carries
  everything else — the meter, the rests, the stems, the scripts — and the chord name over a
  note is more of that same list: printed on both lines it is the same annotation twice, once
  over the staff and once over the tab of the same part. It now prints on the staff only, and
  the room reserved for it under the tab goes with it (a band under a line nothing is drawn on
  is the empty gap that reads as a mistake). A tab the writer asked to be complete —
  `tab X as full`, and a lone tab, which is full by default — keeps its own chord names, as it
  keeps its own scripts and dynamics. A chords TRACK placed on a tab (`tab X with chords P`, or
  a `chords` row folded into it) is a line the writer put there, and stays.
- **A chord written on a tab note stands over the tab, not through the staff above it.** An
  `@chord` is placed 0.6 staff-spaces above its staff's top line, plus whatever that staff's
  own ink protrudes — and the protrusion is read from a skyline built about the staff's
  reference point, reflected once into "above the top line". The reflection subtracted the
  score's nominal half-staff (2.0) from a TAB staff, which spans 7.5 (LilyPond gives a
  TabStaff 1.5 per string whatever the string count), so 1.75 of it was left undone: the
  symbol floated 1.75 too high, crossing the bottom line of the staff above, while the room
  reserved for it under that staff stood empty. A tab's chord now takes the same 0.65 over its
  own top line that a notation staff's does. Sweeping the tracked corpus and 323 bass-tab
  books — 920 in all — moves exactly the one book that puts an `@chord` on a tab staff.
- **A full-notation tab stem is the length LilyPond draws it.** A tab under `\tabFullNotation`
  draws stems, and Lily# gave every one a flat three string-spaces from the fret digit — so a
  low bass note's up-stem reached far above the staff and, more consequentially, every
  full-notation tab system stood about 1.4 staff spaces taller than LilyPond's, enough to cost
  a page: a bass tab that fills two pages in LilyPond spilled onto a third. The stem now
  follows LilyPond's own rule run in the tab's frame — its tip is the ordinary stem end by
  duration and string, and it is drawn from the far edge of the whited-out digit rather than a
  fixed gap past it, so the visible line is shorter and its tip lands where LilyPond's does.
  Its direction, and a beam's, is LilyPond's default-direction rule read over the fret digits'
  strings (the farther string decides, a chord by its extremes, not the average), and a tab
  beam's stems shorten when forced against their natural direction, as a notation beam's do.
- **A tab staff's silhouette is its own clef's.** The layout's skyline for a tab staff
  carried a TREBLE clef's outline — 3.55 staff spaces under the middle string and 3.8 over
  it — where the staff prints the TAB clef, which sits inside the strings. A six-string tab
  hid it; a five-string bass tab reserved 0.55 of phantom ink below its lowest string and
  0.8 above its highest on every system, so a full page of bass systems compressed a little
  more than LilyPond's and a one-page book was that much taller. The skyline now holds the
  TAB clef's own box. Six-string tabs are unchanged.
- **The title sits where LilyPond puts it, and the first system under it.** The title's
  baseline used to be drawn AT the top margin — its ascenders in the margin, the composer a
  title-height below in italics — and only the ink under that baseline kept the first system
  down, so a titled book's first staff stood 5.6 staff spaces higher than LilyPond's and
  its first page held one system fewer. The title is now LilyPond's book-title column: its
  top 4 staff spaces below the margin (top-markup-spacing), the composer 3.5 below the
  title's baseline (the column's baseline-skip), upright, and the first system spaced from
  the column's bottom by markup-system-spacing — a spring of the page like any other, so a
  full page compresses or stretches the gap with the rest. Books without a title or a
  composer are unchanged.
- **The page breaker prices a multi-staff system at the height LilyPond does.** It used to
  measure a system as it was drawn — its staff pairs at their basic distance, and its last
  staff's reference point a nominal half staff above the bottom line whatever the staff — so
  a staff-plus-tab system was priced two staff spaces taller than LilyPond prices it, and
  eight such systems that LilyPond squeezes onto one page went 7 + 1 (the user's bass book
  paged 7 systems where LilyPond pages 8). The breaker now reads each system at its
  alignment minimum, with the tab's real reference point, exactly as LilyPond's page
  breaking estimates do; the placement of the chosen systems is unchanged. Books whose
  systems have one staff page as before.
- **A chord symbol written on a note keeps its neighbours off.** An inline `@chord` used to
  reserve no width at all — only a `chords` track's symbols did — so two whole-note chords
  with wide names (`F♯sus4` then `Emaj7/D♯`) printed 0.78 staff spaces apart and read as one
  word. The symbol now carries its note's onset and prices its width on that column exactly
  as a track's symbol does (LilyPond has one grob for both spellings): the bar line clears
  the name and the next symbol stands a clear four spaces on. A bar the name did not
  outgrow is unchanged.
- **A chord row and a chord written on a note print on one line.** A score carrying both an
  independent `chords` row and inline `@chord` symbols on the staff under it engraved them on
  two lines — the row in its own band, every `@chord` about three staff spaces lower, just over
  the staff — so a reader following one chord line had to follow two. An `@chord` now prints at
  the row's own baseline, and keeps its own line below the row only where the two symbols'
  boxes share an X, which is exactly the case one line could not hold: a row chord on beat one
  and an `@chord` on beat three share the line, two on the same column do not. Where they do
  share a column it is the ROW's symbol that lifts clear — the `@chord` names what the notes
  under it actually spell, so it keeps the line beside them, and the reading stays on one row
  with the odd nominal chord stacked over it. The room goes with the symbols: a staff whose
  chord line the row has taken stops reserving the band above itself, so the page is that much
  shorter instead of carrying a blank line under the names. Several `chords` rows stacked in
  one `score` keep their own separate lines, and an `@chord` joins the nearest one above its
  staff. A `staff … with chords` track is untouched — that is a line the writer asked for by
  placing it — and so is a score that uses only one of the two spellings.
- **An empty bar is as wide as LilyPond's.** A bar holding nothing but a skip (`s1`, or
  the `| |` placeholder), and a bar a percent repeat covers, used to be spaced as if a
  whole note stood in it — 6.39 staff spaces whatever the piece — where LilyPond drops the
  skip's column altogether and spaces the two bar lines as one pair, linear in the bar's
  length over the piece's shortest note: 5.51 in a piece of quarters, 8.07 in eighths,
  15.75 in sixteenths. Both formulas are LilyPond's own (`standard_breakable_column_spacing`),
  the second for a bar beside a bar line that cannot break — the inside of a two-bar `%%`
  pair or of a slash body of three bars or more, where LilyPond forbids the break while the
  repeat's sign is still sounding, and Lily# now forbids it there too. A skip also stops
  voting for the piece's common shortest note, as LilyPond's skips never did, so a book
  that is mostly percent repeats keeps the spacing of its written music. Measured against
  LilyPond 2.26.0 to the digit on six probes. The `%%` sign itself is in the bar's width
  too: LilyPond break-aligns it on the bar line it straddles, so its ink is part of that
  column and each bar of the pair is wider by half the sign — 7.57 and 7.38 in a piece of
  quarters, LilyPond's figures — and the sign is now centred on the bar line's left edge as
  LilyPond's is. And a skip inside a bar has no column of its own either: `c4 s2.` spaces
  the note to the bar line as four quarters of the note's own spring (12.75, LilyPond's
  figure, where 9.03 was drawn), a bar opening with a skip reaches its first note by the
  bar-to-column duration space, and a note sounding through another voice's skip is
  unchanged. Two voices sharing a staff over skips (`beam-over-stem`) now sit within a
  third of a space of LilyPond's bar widths where they were five spaces narrow.
- **On a lead sheet, the volta bracket stands on the chord row and both ending labels stand
  on the bracket.** A chord row leading the system used to float the bracket a whole band
  too high — its floor was measured from the row's top edge rather than the staff — and the
  row's symbols were kept out of the bracket's and the labels' way, so a second ending's
  label could land under the bracket, level with the symbols (reported on `Lambada
  Complicada`). The bracket now hangs off the staff it spans and clears the row's symbols by
  LilyPond's outside-staff padding, as its `Volta_engraver` does at the score level, and the
  labels clear the bracket's drawn line exactly (a 0.02 of air over the line went with it).
  Text spanners and dynamics, which belong to the staff, still sit under the row as before.
- **A `|:` that opens the piece is printed.** LilyPond's default drops the automatic repeat
  bar at the very start of a piece, and 0.6.0's earlier builds copied that; but in Lily# a
  `|:` is always one the writer wrote, and lead sheets print it — LilyPond itself keeps the
  door open with `printInitialRepeatBar`. So the sign goes where it was written, the LilyPond
  twin now carries `\set Score.printInitialRepeatBar = ##t` so both pages agree, and a `|:`
  one bar in is unchanged.
- **A `|:` that opens a line stands where LilyPond's does, and the first note keeps its
  distance from it.** At a line start the repeat bar is the last column of the clef/key/meter
  group — 1.0 after the meter, 0.7 after a bare clef, 1.1 after a key signature — and the
  first note stands 1.3 off the bar's ink, LilyPond's own `first-note` distance for a bar
  line. The bar used to be nudged past the prefix by a number nobody had measured and the
  first note spaced as if the bar were not there, which put the note 0.3 too close to it
  (reported on `Lambada Complicada`'s section C). Measured against LilyPond to the digit on
  both quantities; the span bar of a grand staff and the tab staff follow the same column.
- **A rehearsal mark or section name mid-line is centred on its bar line.** LilyPond
  break-aligns the mark on the bar and centres it on the bar's anchor, the middle of the
  strokes with the repeat dots left out; Lily# stood the box's left edge on the bar, a whole
  half-box too far right (reported on `Lambada Complicada`'s endings). Five LilyPond
  measurements now referee it: a plain bar, `|:`, `:|`, `:|:`, and the two ending labels of
  an alternative. A mark that opens a line is unchanged.
- **A hand-written slur from a grace note to its main note is drawn.** `grace { g16( } a8)`
  now draws the bow `appoggiatura { g16 } a8` has always drawn — in LilyPond the two are the
  same pair of slur events (`ly/grace-init.ly`), and the corpus writes the pair by hand seven
  times. The `(` must stand on the LAST grace note and the `)` on the main note; a `(` on an
  earlier grace note, or on a grace rest, is still reported as not engraved (LYS4020), and a
  `(` the main note does not close is reported unpaired (LYS4010) and draws nothing, as
  LilyPond's "unterminated slur" does.
- **A section name's box stands where LilyPond's rehearsal mark stands.** At a line start
  the box used to sit on the system's left edge, over the clef, and a tempo mark beside it
  slid right whenever a key signature widened the prefix. The box now takes the rehearsal
  mark's anchor — the key signature's right edge, the clef's when there is no key, or the
  drawn `|:` of a section that opens a line with a repeat — with the tempo stacked under
  it, which is the picture LilyPond draws for the `\mark \markup \box` the twin writes.
  LilyPond's own `\sectionLabel` grob keeps the left edge; that placement is not gone for
  good, it is the `marks beside` display option still to come.
- **The number of systems is chosen by the page's score, as LilyPond chooses it.** The line
  breaker's best breaking was the breaking engraved; LilyPond's `Optimal_page_breaking::solve`
  only starts there, then tries fewer and more systems and engraves the count whose lines
  AND pages score best — without the term that made the line breaker split the line after
  a very underfull forced-break line into two half-full ones. On a real-world corpus of 286
  bass books the system breaks matching LilyPond's rose from 356 to 388 pairs, and the books
  matching on every score from 170 to 183.
- **The system-count loop prices each candidate line by its own line-start ink.** The
  loop's page estimate gave every candidate line ONE begin bucket — the widest line start
  any placed system showed, which is the first system's by construction, since the tempo
  and the opening mark stand over its prefix. LilyPond's `begin_line_heights` is per break
  rank: a line starting at a given column is priced for what stands at THAT column. On
  `Le Freak` (staff + tab) every candidate line was priced 7.30 above the body where the
  placed continuation lines are 2.31, so the estimate fitted six systems to a page where
  the placement fits eight; the ideal count then needed a page more than the count below
  it, and the loop's "one page fewer and stretched" exit fired one count too early —
  LilyPond's 23-line breaking, whose line sum Lily# had already priced cheapest, was
  never tried and the book was set in 24. A candidate line now takes the begin bucket of
  the placed system that started at its first bar, and the bare continuation prefix
  (clef, key, bar number) where none did. On the 286-book corpus the T7 pairs matching
  LilyPond rise from 148 to 149 (`Le Freak`, all three scores) and all pairs from 440 to
  443; no pair is lost.
- **A candidate line is priced by solving its springs, as LilyPond spaces every line it
  considers.** The line breaker estimated a compressed line's force from per-measure sums —
  the linear part of LilyPond's `compress_line`, exact only until the first spring reaches
  its minimum — and so priced a line whose every bar ends on a flagged eighth (each of which
  blocks early) far too cheaply: the head of `Alone Again` scored 1.22 as one 8-bar system
  where LilyPond scores it 1.65, and was engraved 8 | 4 where LilyPond engraves 4 | 4 | 4.
  Each candidate line is now solved with the same spring solver the engraved system is,
  blocking springs walked one by one; the reproduction scores 1.58 and breaks as LilyPond
  does. Lines carrying a multi-measure-rest rod or a lyric rod keep the estimate. ⚠️ On the
  286-book corpus this moves the system breaks of 32 books: four scores now match LilyPond
  that did not (`Livin' It Up`, `Lovely Day`, `Together Forever`) and ten no longer do, eight
  of them tab scores — lines the estimate accepted but the solver refuses, because a spring
  with no compress strength (a line-start spring, a tab fret-digit floor) cannot give what
  the estimate assumed. Those lines were set past the margin before; the springs behind
  them are the next measurement.
- **The gap on either side of a bar line takes LilyPond's column rods.** A note before a
  bar line was priced by its head alone, so an up-stem flag reached through the bar line
  when a line was compressed (LilyPond's rod for a flagged eighth before a bar line is
  2.3674 staff spaces against the head-only 1.6042); the bar line → note gap lacked the
  0.1 rod padding; a drawn rest was boxed as a notehead (an eighth rest is 1.0 wide, not
  1.3042); a pair of unequal heads was measured between the heads' centres rather than
  from the left head's column origin; and an unbeamed eighth or shorter took the optical
  stem correction that LilyPond skips after a flag. With all five the reproduction's bar
  compresses to 9.0432 and sets 15.8432, LilyPond's to four digits. The visible change is
  small: a flagged note before a bar line stands a little further from it, and a rest
  before a note a little closer.
- **A full-notation tab's stems, beams and flags are in its skyline.** A tempo mark or any
  other outside-staff item above a tab staff cleared only the fret digits, and printed
  through an up-beam in the first bar; now it clears the drawn stems, beams and flags the
  way it clears them above a notation staff (LilyPond's `\tabFullNotation` reverts the
  Stem, Beam and Flag stencils, so they are in the axis group's skyline there too).
- **The blank bars of a `repeat percent` over three or more bars print nothing on a tab.**
  The notation staff already left them empty; the tab drew a whole rest in each.
- **A dotted chord on an `as numbers` tab draws no augmentation dot.** A dotted single note
  already drew none there; the chord arm had no gate, so `<c e g>4.` printed its dots beside
  the fret digits on a numbers tab.
- **What a `repeat percent` body writes prints once.** The slur, tie, script and dynamic of
  the body drew again under every percent sign — on a notation staff and a tab alike —
  because the covered iterations re-walked the body with its markers and post-events.
  LilyPond never plays those iterations (its iterator reports one percent event in their
  place), so the collector now drops the bows and note-riding annotations while it re-walks
  a covered iteration; the notes stay for playback and spacing, hidden as before.
- **A pedal bracket under a system now keeps the next system away.** The bracket was solved
  against its own staff and seeded into that staff's profile — the one the lyric floor and the
  staff-to-staff springs read — but not into the silhouette the page spaces systems by, so a
  sustain bracket under the last staff of one system was drawn through the trill and the
  fermata above the first staff of the next. The bracket's stencil now joins the page's
  silhouette the way LilyPond's `build_system_skyline` merges every element, raised by its
  staff's translation, and the pair of systems opens by exactly LilyPond's amount
  (ledger `page.pedal-bracket.gap-first`, 13.345 staff spaces on the probe, exact). A page's
  bottom edge counts the bracket too, so a book ending in a pedal is cropped a little deeper.

### MIDI, MusicXML and the LilyPond twin

- **A shifted chord in an absolute-octave book exports a twin LilyPond can read.** `lysc ly`
  wrote a member's own octave mark and then the chord's after it, so `<b cis' fis>,` became
  `<b, cis', fis,>` — and `cis',` is a syntax error in LilyPond, which takes `'` or `,` on a
  pitch but never both. Each member now carries one net figure: `<b, cis fis,>`.
- **The twin declares two more things the page does not draw.** A `\N` string number
  steers the tab's string choice and is drawn nowhere on Lily#'s notation staff, while
  LilyPond's Staff prints a circled digit for every one; the twin's Staff now omits
  StringNumber when the part carries one, as the hand-written corpus books do. And a tab
  beside a notation staff of the same part is fret digits only on the page, but the
  exporter read only the explicit `as` word and asked LilyPond for `\tabFullNotation` on
  every paired tab — stems, beams and rests the page never draws. The twin now takes the
  page's own reading: a lone tab stays full, a paired one is bare, an explicit clause wins.

## 0.5.0

The language settles two things it had left loose — where a book's playing order is
written, and how a span ends — and the engraver starts walking the inside of a grace body.
The rest is defects found by engraving real scores.

### Language

- **Repeat structure is written in a `form { … }` and nowhere else** (`LYS1034`). A repeat
  bar (`|:` `:|` `:|:`) or a volta ending (`[1. … ]`) written in music is an error. The line
  is whether it changes the playing ORDER: `repeat percent`, `repeat unfold` and `tremolo`
  stay in the music, because they abbreviate notes. The two spellings really did mean
  different things — in music a `|:` expanded only the part it was written in, while the
  page had always treated it as score-level, so the page and the MIDI could disagree. Now
  the disagreeing spelling cannot be written. A lyric verse header `[1. … ]` is untouched.

- **A span must be closed, and `@!X` is how.** `@textSpan("poco rit.")` is the primitive;
  `@rit` / `@accel` / `@rall` are start-only sugar for it, closed by `@!rit`, `@!accel`,
  `@!rall` or `@!textSpan`. An unclosed span draws nothing at all — not its dashed line and
  not its word — and is an error (`LYS4018`). That is LilyPond's own answer: its
  `Text_spanner_engraver` ends an unterminated span by discarding it, so there is no default
  length anywhere to fall back on. Lily#'s one-measure fallback is retired, together with
  the search that let the next `rit.` end the previous one. Pairing is per (staff, voice),
  so a terminator in another voice reaches nothing.

- **The ottava and the pedal are spans too.** `@ottava … @!ottava` — one terminator for
  `@ottava(bassa)` and `@quindicesima` as well — and `@sustain … @!sustain`,
  `@sostenuto … @!sostenuto`, `@unaCorda … @!unaCorda`. **`@loco`, `@sustainOn`,
  `@sustainOff`, `@sostenutoOn` and `@sostenutoOff` are retired:** the direction moved out
  of the name and into the `!`. This is LilyPond's model said once rather than a departure
  from it — `sustainOn` is `#(make-span-event 'SustainEvent START)`, one span event with a
  direction argument, and the On/Off suffix was only how the surface command spelled it.
  `@treCorde` stays as sugar for `@!unaCorda`, because it is a word the page actually
  prints; `@loco` went because it named a mark nothing printed, and LilyPond has no `\loco`
  either. Refusing to draw an unclosed ottava or pedal is this language's answer and not a
  port — LilyPond draws both to the end of the music, in silence.

- **A part setting written beside a section's part cells is refused.** `clef` and `octave`
  there are `LYS1035`; `instrument` and `transpose` already reached `LYS0030`, whose message
  now names where a one-part setting goes. All four belong to no cell in that position, so
  nothing read them: `clef` did nothing at all, and `octave` was worse than nothing — the
  resolved pitches did not move while the LilyPond twin's wrapper for the whole part flipped
  from `\relative c'` to `\fixed c'`. The position is the rule, not the keyword:
  `part m { section A { clef bass … } }` and `section A { clef bass c'4 … }` are both
  correct, because only a section holding cells has nowhere to put a loose one.

- **A `form` that plays a section declared only as a header is refused** (`LYS1036`). With
  `section A { key g major }` as A's only declaration, the page armed the header's key and
  carried it into the next section's bar — a header-only section engraves no bar, so the
  boundary that would restore the score key never fired — while the LilyPond twin wrote no
  key at all. It is `LYS1005`'s sibling, and it asks whether ANY declaration of the name
  carries music, so a section belonging to a part this score does not draw stays legal. An
  empty `section A { }` is deliberately not a header.

- **A phrase reference's interval argument is removed.** `Melody'(3)` no longer plays the
  phrase a third up; the octave marks `Melody'` and `Melody,` are unchanged. The glued form
  made `'` mean "an octave" or "upwards" depending on whether `(N)` followed, with a 1-based
  degree on top of that, and a single space turned the whole thing into a reference followed
  by a slur. Transposition is a part property and chromatic, so a motif quoted a third
  higher is written out.

- **A percent repeat prints the sign its body's LENGTH earns**, exactly as LilyPond decides
  it. A one-measure body is unchanged. A two-measure body prints ONE double sign on the bar
  line between the pair, and both measures under it print no music. Anything else — shorter
  than a measure, longer than one, or a run of three or more — prints ONE repeat slash where
  the repetition starts and leaves the rest of its measures blank. Equal durations give a
  plain slash, unequal ones the double. LilyPond has two length tests and one else, not four
  cases; the belief that it reserved the slash for sub-measure patterns came from the grob
  descriptions rather than the iterator, and `LYS2014`, which admitted the invented picture,
  is retired with it.

- **A tab staff's style follows the score, and a numbers tab draws no tie.** `tab NAME` with
  no `as` clause is `as numbers` when the same part is also on a notation staff — that staff
  already carries the meter, the rests, the dots, the stems and the ties — and `as full`
  when the tab stands alone. An explicit clause always wins; a condensed, combined or grand
  staff counts as a notation staff and an ossia does not. Separately, `as numbers` no longer
  draws ties: LilyPond's tab context hides the tie and says a held note is held by dropping
  the tied-to fret number. `as full` keeps its ties, and slurs are unaffected in both, which
  is also LilyPond's answer.

- **`staff <clef word>` alone names a part.** `staff bass` renders a part named `bass`, and
  says so (`LYS1007`) when none is declared. The reference scan read that lone word as a
  clef with the name left off and collected nothing, so a score whose staff named no part
  engraved a page of empty bars while `lysc check` answered "No errors found". Four words
  could reach it: `treble`, `bass`, `alto`, `tenor`.

### New

- **A section reference carries octave marks** — `~B'`, `~B,`, `[1. B']` — shifting the
  relative frame that play opens in, one octave per mark. It is the spelling and the meaning
  a phrase reference already had. The shift belongs to the occurrence: `~B ~B'` is one
  section played at two octaves, and the reference after it is back at the part's anchor.

- **`section ~A { … }` declares that the section prints no rehearsal letter.** The tilde
  keeps one meaning at both sites — "the other one than the default". A plain section
  carries a letter and a reference's `~` hides it; a `~` section carries none and there the
  reference's `~` shows it. The whole rule is `shown = (declaration hides) == (reference has
  ~)`. A section cut solely to carry a repeat edge should not be labelled, and that is a
  property of the section, so it is written once on the declaration.

- **A form can spell a third volta ending** — `:| [3. C]` — which the music spelling could
  and the form could not.

### Diagnostics

- **A node's address is where its own text starts.** Every source-pointing feature — the
  SVG's `data-pos` that click-to-source resolves, the `(line, column)` `lysc check` prints —
  took the node's position INCLUDING the whitespace in front of it, so the first note of
  every indented line carried the offset of the newline and the indent: a click there lit
  nothing, and an overfull measure whose first note stood at column 5 reported column 1.
  Only a composite node was affected — a note, a chord, a repeat — because leading trivia is
  overridden by tokens alone.

- **A drawn tie or slur cites the character that wrote it**, so clicking a bow in the
  preview jumps to its `~` or its `(`. Ordinary bows carried no source offset at all, so a
  caret on `~` lit the nearest preceding address, which is the note. The third bow family —
  a laissez-vibrer or repeat tie, drawn by an annotation rather than by a symbol of its own —
  cites that annotation.

- **A rehearsal letter that is written and not printed says where** (`LYS4019`). A `@mark`
  inside a container that owns its own walk — an inline ending, a tuplet, a repeat, a cue —
  was dropped by the collector in silence. The drop is fixed (below); this is so that the
  next way to lose one cannot be silent either. A `@mark` inside `repeat unfold N` prints
  once.

- **A grace body says what it drops** (`LYS4020`). The body is parsed by the ordinary
  music-block parser, so it accepts everything a music block accepts, while the collector
  read a column's pitches and duration and nothing else. Most of that list has since left it
  (below); what remains is reported at what was written, as a warning — "not drawn yet"
  rather than "do not write this".

- **A part engraved on two staves no longer complains twice.** A score putting one part on
  both a standard staff and a tab staff collects it once per staff, and the per-voice sanity
  scans — the tie target, the unclosed slur, the unopened manual beam, the slur or tie
  crossing a cue boundary — appended to lists living on the whole collect. The scans now run
  once per voice, however many staves engrave it.

- **An unclosed `form` repeat is one error at the `|:`.** `form main { ~Body |: A }` used to
  report the form's own `}`, then `score`, `{`, `staff` and `}` as five things a form cannot
  hold, and only then the missing `:|` — the item loop ran to end of file, so the score block
  was consumed as stray form items and four of the five errors were about good text. The
  loop now stops at the form's own closing brace and reports the missing half as `LYS4017`.

- **An overfull bar that runs into a `repeat` is reported where it was WRITTEN.** A bar left
  open in front of a repeat is part of the repeat body's first bar, so the warning landed
  inside the body — in music the writer had not made a mistake in.

- **An inline volta ending's own bars are counted.** The measure validator held a whole
  `[1. … ]` ending as one opaque zero-duration item, which nobody else does, so
  `[1. c'1 c'1 c'1 | ]` in 4/4 was silent and the note value did not thread through the
  ending into the music after it.

- **The tree keeps post-events in the order they were typed**, so every node stands where it
  says it stands, and the two orders of a post-event run agree on their diagnostics too.

### Engraving

- **A grace body is engraved, heard and exported.** The body is now walked by the ordinary
  walker rather than read for a bare note's pitch and duration, and everything that walk
  reaches comes with it: a CHORD is one column with N heads, through the same chord and
  accidental rules a full-size chord uses read out of the grace's own fonts; a REST is a
  column with no head, and the beam covers the leading run of heads; a DOT is drawn and
  clears the flag only where the flag is; a PHRASE reference expands; and a TUPLET's notes
  are engraved, heard and exported, with only its bracket and number still dropped. The
  page itself does not move. What the change exposed on the way were five real defects it
  then fixed — among them a grace in the second voice displacing the FIRST voice's
  noteheads, and a grace cutting a beamed run it should have been spanned by.

- **A rehearsal letter is built where its note is**, so the containers stop swallowing it: a
  chart writing A, B, C and D printed only A, because the other three stood inside a second
  inline ending and drew nothing at all.

- **A `rit.` cannot be ended by the next playing of itself**, so a repeated section draws it
  the same both times. The spanner was ended at "the next `rit.`/`accel.` on the same staff"
  over the marks of the PLAYED piece, so a section the form repeats contributed one instance
  that closed the other. From one written mark, the first playing covered six bars and the
  second one. The hairpin carried the same defect and is fixed with it.

- **Both ends of a text spanner are the writer's.** The left bound was a constant, so
  `c4 d e@rit f | g@!rit` drew from `c` rather than from `e` — the terminator was honoured
  and the start was not — and the bound padding is now spent where LilyPond spends it.

- **A lyrics row clears the bottom string of the tab staff above it.** The cause was one
  quantity — how tall is THIS staff — written in three places, two of which answered with
  the score's nominal four staff spaces however many lines the staff actually has.

- **A tab staff prints none of the markup the notation staff beside it already carries**,
  and the switch is `as numbers` vs `as full` rather than "is it a tab" — so a `@text` on a
  tab with no notation staff beside it now appears, and an `@accent` on a numbers tab does
  not.

- **A `rit.` above a system's top staff reserves the room it is drawn in**, so it no longer
  collides with a lyric on the system above.

- **A chord row over a `rit.` clears it on every system, not only the first.** The row is
  spaced against the staff below it, and the staff's own `rit.`/`accel.` text is
  outside-staff ink standing between the two — which the first system's placement knew about
  and no later system's did, because a row above a later system is placed by the loose-line
  chain and that chain measured the staff's INSIDE silhouette.

- **A dynamic on a lower voice is engraved at its own note.** The label's column was resolved
  against the STAFF's stream of items, so the voice's item index named whatever the first
  voice happened to have at that ordinal. An item index only means something inside the
  stream it was recorded against, and the sibling script side had always resolved the
  voice's own measures — so one note could carry two marks that disagreed about where it was.

- **The percent-repeat sign is LilyPond's own shape:** the slash is the parallelogram
  `Lookup::repeat_slash` builds, cut horizontally at its ends, rather than a stroked line cut
  square to the slope — which made the ink 0.51 too tall and 0.51 too narrow on a tab staff,
  where everything is 1.5-sized. Its two dots now hang off the edges of the whole slash group
  with LilyPond's negative kern instead of a constant offset.

- **A repeat barline's dots are the size LilyPond draws them, and sit where its search puts
  them.** LilyPond has no radius there at all — it stamps the `dots.dot` glyph, whose half
  extent is 0.225 — and the drawn circle was 0.2. The horizontal room a repeat barline
  reserves is computed from the same number, so it had been reserving for a dot LilyPond does
  not draw. Their vertical place comes from LilyPond's search for the first space wide enough
  to hold a dot and a staff line, folding the staff about its centre; Lily# used 0.5, which
  is that search's answer for five lines and for three and wrong for the rest — a one-line
  rhythm staff wants 0.45, and 0.5 put its dots nearly on the single line.

- **What a part writes on a `combinedStaff` is drawn on that part's own notes.** The combiner
  rewrites both streams — moving notes between the two staves it draws, merging a shared
  moment into one column, leaving unengraved whatever the other part is covering — while
  everything a part hangs off a note was still addressed by where it stood in the part. So on
  the second part all of it landed on the FIRST part's notes. A passage the combiner engraves
  with nobody now takes its markings with it, which is what LilyPond does.

- **The last block on a page is solved into the paper**, not into the height the page was
  cropped to, and the crop is then sized from where the block is drawn. A spring lands on its
  minimum exactly when the room it is given is short, and the room this chain was given was
  not a room at all.

- **A row leading the next system is spaced against that system's staff as LilyPond publishes
  it** — outside-staff ink and all. The chain closed on the next system's first spaceable
  staff by reading its INSIDE-staff silhouette plus one hand-merged special case, where
  LilyPond spaces a loose line against the axis group's skyline.

### MIDI, MusicXML and the LilyPond twin

- **A form's `:|*3` plays three times.** The MIDI exporter read neither the written play
  count nor the form walk's — it was `max(2, endings)` — so a form repeat sounded twice while
  the same body written inline sounded three times.

- **A section boundary restores the score METER in every reader.** A section stating no
  `time` of its own opens at the score meter, so a mid-section change cannot leak into the
  next section. The page obeyed that and the measure validator agreed; three of the five
  readers did not, and each exporter had to be told separately.

- **A standalone section header is a header wherever it is written.** The same book with its
  header moved across the part produced two different LilyPond twins — one of them the
  directive twice and not one note — while the page, `--pitches` and `check` all agreed
  either way. The two spellings differed by line order alone.

- **A plain repeat imported from MusicXML comes back as sections plus a form.** The importer
  left everything but first/second endings as one flat section whose measures were joined by
  bar lines, which writes `|:` and `:|` into the music — a book Lily# now refuses.

- **A phrase named in a grace body is heard and exported**, not only engraved.

## 0.4.0

The first release installable from the VS Code Marketplace (`yotsuda.lilysharp`);
0.3.0 shipped as tagged GitHub binaries only. It is also the first release that
changes the language, and every change below is diagnosed rather than silent.

### Breaking changes

- **Chord entry is the printed symbol** — `Am`, `G7`, `F#m`, `Bb7`, `Gm7-5`,
  `Cmaj7/E`: an uppercase root, optional `#`/`b`, a bare quality. The LilyPond-shaped
  lowercase `:` entry (`a:m`, `g2:7`) and its per-chord durations are replaced by
  measure-relative placement — a bar's entries divide it on the beat grid the beams
  already own, and `.` holds the previous chord one more beat. The case is the
  grammar: uppercase letters are roots, so `R` the rest never collides and `b` after
  a root is a flat, which is why altered tensions spell `+`/`-` (`Bb5` remains
  B-flat's power chord). `LYS1028` names the old spelling.
- **The `$` sigil is removed.** A bare name is a phrase reference. The ambiguity it
  hid is closed at the declaration instead: drum vocabulary and `q` are refused as
  phrase names (`LYS1030`), dynamics were already reserved, and the clef words stay
  reachable because `clef bass` owns its keyword.
- **A staff's display name is a quoted string** — `staff flute "Flute"`. A trailing
  bare word now always reads as a part reference, so `staff flute click` no longer
  eats `click` as a label and silently stops the click track. The corpus wrote
  neither form, so the measured migration cost was zero of 572 books.
- **`NoteColumn.force-hshift` is refused** (`LYS1029`) rather than accepted and
  ignored — the exact no-op four documents claimed the language did not have. Its
  row and the implementation flag flip back together. In the same pass
  `NoteHead.color` and `Stem.color` became supported: their reader had been live all
  along, so a correctly coloured score shipped with an error and exit 1.

### New

- **`paper` blocks.** A page's dimensions come from the source, and `size <name>`
  sets width, height and the four margins from the paper table, each margin scaled
  from a4 exactly the way `scm/paper.scm`'s `set-paper-size` scales it — horizontal
  by the width ratio, vertical by the height ratio, rounded to whole millimetres.
  `size a4` is pinned as the identity.
- **Named `fonts` and `paper` blocks.** `fonts NAME { }` / `paper NAME { }` at the
  top level declare reusable blocks that bind nothing by themselves; a score
  references one or overrides part of it in place, the override reading as if its
  entries were appended to the named block.
- **MusicXML**: import carries the stated page across as a `paper` block, in
  millimetres through the scaling bridge, writing only the keys the source wrote;
  export emits a form's custom text.

### Engraving fidelity

- Lyrics saw the largest body of work: syllables align on their own voice's
  notehead, a melisma span became the range rod it always was, every reservation
  lands on the column its syllable is drawn on, a word crossing a barline is one
  rod, and an independent lyrics row below a multi-staff system now reads that
  staff's own profile — it had no floor at all, and was engraved through the notes
  on every system but the last.
- Lead sheets: every line opens with a bar, no bar runs through a word, stanza
  numbers anchor at the line start clear of the opening bar, and the grid row prints
  the meter.
- Pedal brackets and text-style pedal words hang from their own staff on their own
  spanner; bar numbers hang on their staff rather than the system's top band.
- Spacing: a glyph is priced at the value it is drawn at rather than its scaled
  duration, and several wishes average the way LilyPond averages them.

### Performance

- Keystroke latency in scores with lyrics: the loose-line chain caches its prefix and
  closes live, verse skylines ride the measure layouts' identity so both passes share
  one store, and the lyric band joins the per-system memo.

### Verification

- The LilyPond-fidelity ledger is now **573 recorded geometric quantities** against
  LilyPond 2.26.0 (was 529), with **222 SVG snapshots** and **5816 tests**, green on
  Windows and Linux.

### Packaging

- The VS Code extension ships as one self-contained VSIX per platform, each bundling
  that platform's .NET runtime and native rendering, so users install nothing but the
  extension.

## 0.3.0

First tagged release. Earlier version numbers (0.1.x, 0.2.x) were internal
assembly versions that were never tagged or distributed, so this entry describes
the product rather than a delta.

### Highlights

- **The Lily# notation language** — parts, sections, forms and scores; phrases
  (named music); pitches, durations, tuplets, grace notes, slurs (including over
  chords), ties, articulations and dynamics; lyrics; chord rows and staff-less
  lead sheets; volta repeats with inline endings; parallel voices; mid-piece key,
  time and clef changes; rhythm (comping) notation — `/` slash notes on the
  middle line, bare durations that repeat the previous note or chord
  (`bes8 8 8 8`), and one-line rhythm staves via `staff … as lines 1`; lyric tracks bind
  to their own melody (`lyrics ja sings vocal`) and can print as words-only
  rows at that melody's rhythm — chorus words on an instrumental part. A
  score is a vertical stack of bands: a bound `lyrics` row directly below
  its staff is that staff's verse, a `chords` row directly above a staff
  aligns the symbols over it. The
  complete grammar is in
  [`docs/GRAMMAR.md`](docs/GRAMMAR.md), with a tutorial in
  [`docs/TUTORIAL.md`](docs/TUTORIAL.md). Lily# is deliberately **not**
  LilyPond's language: backslash constructs are rejected.
- **LilyPond-fidelity engraving.** Beam quanting, slur and tie scoring, skylines,
  spring spacing and page breaking are ported from LilyPond (GNU GPL; every
  ported file is listed in
  [`LILYPOND-ATTRIBUTION.md`](LILYPOND-ATTRIBUTION.md)), and the output is
  continuously measured against LilyPond 2.26.0 through a ledger of 529 recorded
  geometric quantities and 220 SVG snapshots.
- **Outputs**: SVG, PDF, PNG, MIDI and MusicXML from one source file.
- **`lysc` CLI** — `check`, `layout`, `svg`, `pdf`, `png`, `midi`, `xml`; see
  [`docs/CLI_REFERENCE.md`](docs/CLI_REFERENCE.md).
- **VS Code extension with live preview**, backed by a full LSP server:
  diagnostics as you type, completion (keywords, pitches, dynamics, font faces),
  hover, document symbols, go-to-definition, references, rename, formatting,
  semantic highlighting, folding, code actions and signature help. Preview
  updates run through an incremental compiler (Roslyn-style red-green syntax
  tree, single-pass parsing, per-system layout and render memos), so a keystroke
  re-renders far less than the whole score.
- **Bundled fonts** — Emmentaler for music glyphs, TeX Gyre Schola / TeX Gyre
  Heros for text, with all text measured from the bundled files: the engraved
  page does not depend on which fonts a machine has installed.
- **Cross-platform** — .NET 10; binaries are published for win-x64, linux-x64,
  osx-x64 and osx-arm64. The full test suite (5,600+ tests) runs green on both
  CI legs, Windows and Linux; macOS binaries are built but not CI-tested.

### Known limitations

- Cross-staff beam layout is not implemented.
- MusicXML export does not yet emit lyrics or tuplet numbers.
- Release binaries are not code-signed, so Windows SmartScreen or Smart App
  Control may warn about or block `lysc.exe`. Unblocking the file, or running
  `dotnet lysc.dll` on a machine with the .NET 10 runtime, avoids it.
