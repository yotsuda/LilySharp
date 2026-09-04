# Changelog

All notable changes to the Lily# VS Code extension are documented here.

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
  and only the group the edit touched is parsed again. Exported SVG is unchanged.
- **The Lily# output channel now times each preview update**: the round trip to the
  language server on the `Got response` line, and a `PREVIEW update:` line from the page
  saying what it swapped (pages kept, re-stamped, updated by group, replaced) and how long
  the swap and the re-fit took. When an update feels slow, these two lines say which side
  to look at.
- **A bar inserted or deleted mid-score no longer re-prices every bar after it.** The
  per-bar spacing memo read the previous score bar by bar at the same index, so a bar added
  in the middle shifted every later one out of its slot and the whole tail was rebuilt (3
  of 112 bars reused on a 3-page bass book, against 110 for the same bar added at the end).
  The memo now looks where the tail went. The picture is unchanged; two tests pin the
  reuse counts for an insertion and a deletion.
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
