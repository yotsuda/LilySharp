# Changelog

All notable changes to the Lily# VS Code extension are documented here.

## Unreleased

### Ctrl+Shift+Up / Down switch `octave relative` and `octave absolute`

- **On an `octave absolute` / `octave relative`** — the file's, a part's (`part lh { octave absolute }`)
  or one inside music — `Ctrl+Shift+Up` writes `absolute` and `Ctrl+Shift+Down` `relative`
  (`Ctrl+Shift+PageUp` / `PageDown` do the same); a second press stays. Only the word changes,
  so the notes after it are read the other way; the status bar names *Convert Octaves to
  Absolute / Relative*, the command that rewrites the notes to keep their pitches.

## 0.13.0

### Ctrl+Shift+Up / Down step a note to the next note of the key; the octave moves to PageUp / PageDown

- **`Ctrl+Shift+Up` / `Ctrl+Shift+Down` now move a note to the next note of the key in force**,
  spelled as the signature spells it: `key c major` walks the white keys (`c` → `d` → `e`),
  `key f major` steps `a` → `bes` → `c`, `key g major` `e` → `fis` → `g`. A minor key walks its
  signature and its leading tone (`key a minor`: `g` → `gis` → `a`), a church mode its own, `key custom …` the notes it writes; a
  note off the key steps to the nearest note of it that way (`key c major`: `cis` Up → `d`,
  Down → `c`). The note keeps its pitch: where the new letter would land in another octave the
  octave marks are corrected (`b'` → `c''`; in relative octaves `f b` → `f c'`). A chord member
  moves alone; on the chord's `>` or its duration the chord inverts — Up raises its lowest member an octave (`<c' e' g'>` → `<c'' e' g'>`), Down lowers its highest, the others keeping their pitches (PageUp / PageDown there still move the whole chord, `<c' e' g'>'`); a selection moves every note in it. The
  flats are written the file's way (`ees` / `aes`, or `es` / `as` where the file writes those).
  On an `@chord` or a chords-row entry the keys step the shape as before. **Breaking:** the
  octave step moved to **`Ctrl+Shift+PageUp` / `Ctrl+Shift+PageDown`** (same command ids,
  `lilysharp.stepUp` / `lilysharp.stepDown`; the new pair's ids are `lilysharp.stepSemitoneUp` /
  `lilysharp.stepSemitoneDown`, named when the step was a semitone). Off a note, those keys keep
  VS Code's own *Move Editor Left / Right in Group*.

### Ctrl+Shift+Up / Down step a dynamic, an articulation, a time, a part's octave and a transpose

- **On a dynamic** (the caret after its `@`) either pair steps it a level, `pp` → `p` → `mp` →
  `mf` → `f` → `ff` (`ppppp` to `fffff`). On `@cresc` / `@decresc` / `@dim`, Up writes `@cresc`
  and Down `@decresc` — the same keys, nothing new to remember.
- **On an articulation** (the caret after its `@`) either pair steps it along its group — Up
  towards the up-bow, the hammer-on, the longer, the stronger, the one above — and stops at
  either end: `@downBow` → `@upBow`, `@pullOff` → `@hammerOn`, `@staccatissimo` → `@staccato` →
  `@portato` → `@tenuto`, `@accent` → `@marcato`, `@shortFermata` → `@fermata` → `@longFermata`,
  `@mordent` → `@prall`, `@reverseTurn` → `@turn`, `@trill` → `@pralltriller`, `@heel` → `@toe`,
  `@scoop` → `@plop`, `@fall` → `@doit`, `@breath` → `@caesura`, `@stemDown` → `@stemUp`; Down
  goes back. The status bar lists the group from the top down (`upBow / downBow`), the way the
  keys go. A `.up` / `.down` stays. A mark in no group (`@tap`, `@harmonic`), a dynamic on no ladder (`@sfz`) and a
  bare `@chord` on a `<…>` chord step the note, member or chord they are written on instead —
  `<c' e' g'>@chord` inverts the chord, as from its `>` (owner's request 2026-10-17).
- **On a `time`** `Ctrl+Shift+Up` / `Down` step the beats by 1 (`3/4` → `4/4`), and
  `Ctrl+Shift+PageUp` / `PageDown` walk the common meters by the length of the bar,
  `3/8` `2/4` `6/8` `3/4` `2/2` `4/4` `9/8` `12/8` (`6/8` ⇄ `3/4` is one press).
- **On a part's `octave N`** either pair steps N by 1, 0 to 8; the part's bare notes move with it.
- **On a `transpose`** `Ctrl+Shift+Up` / `Down` move the target a semitone, spelled so the
  transposed key has the fewest sharps or flats (from the file's first `key`), and the status bar
  says how far; `Ctrl+Shift+PageUp` / `PageDown` add or take an octave mark. `transpose` still
  takes a pitch, not a number of semitones — a number would leave the spelling open.
- **On the file's `shift`** either pair steps it a semitone (`shift +1` → `shift +2`, −11 to
  +11) and the status bar names the key the piece lands in (`shift +1: key c major -> des
  major`). A top-level `shift N` is new in Lily# itself: it moves the whole piece — every
  part, its own `transpose` included, the key signatures and the chord rows — the karaoke
  move; see the compiler's changelog. The `+1` the completion leaves selected steps too: a
  selection with no note in it that lies within one steppable thing (a tempo's number, a
  dynamic, the shift) now steps that thing instead of doing nothing.

- **A new score's first lines point at the step keys**: File ▸ New File… ▸ Lily# Score now says
  to put the caret on a note, the tempo, the key or the clef and press `Ctrl+Shift+Up` / `Down`
  or `Ctrl+Shift+PageUp` / `PageDown`. The preview's `Ctrl+K V` hint is gone from it.

- **Lesson 1 practises the step keys, and the other lessons drop their `Ctrl+K V` line**: step 4,
  after writing `@mf`, has you press `Ctrl+Shift+Up` on it (`f`) and `Ctrl+Shift+Down` twice
  (`mp`). The preview opens by itself, so lessons 2-12 no longer start with `// Preview: Ctrl+K V`;
  lesson 1 still says how to bring it back.

- **Fixed: a digit typed in an additive `time` went to a duration**: in `time 2+|/2`, typing `3`
  wrote `time 2+/32` — the `/2` was read as a slash note. An additive meter, whole (`3+2/8`) or
  half typed, is now a meter to the typing aids, and the digit is typed as pressed.

- **A click on a mark in the preview — a fermata, a dynamic, an `@chord` — puts the caret after
  its `@`**, so `Ctrl+Shift+Up` then steps the mark (`@fermata` → `@longFermata`) and not the
  note it is written on: the click used to land just before the `@`, which is the note's own
  place, and the step moved its pitch. A note, a bar and a title land where they did.

- **Convert Image/PDF to Lily# (.lys)… asks for the Scan Import extension before the file
  dialog** when the reader is not installed, instead of after you had chosen the files — and the
  Explorer's right-click on an image or a PDF offers it the same way on Windows, where it used
  to show only once the reader was installed.
- **A Scan Import older than this Lily# is written for is offered its update** when a scan is
  read (Lily#'s own floor, 0.3.0 today; keeping the extension current is VS Code's auto-update,
  which Lily# does not repeat), and the read goes on with the old one if you decline. A reader
  speaking an older protocol gets the same *Update Scan Import* button instead of a bare error.
  The reader is the extension's, or the one `LILYSHARP_OMR_PATH` names: the third place Lily#
  used to look in — a copy under its own storage, from the download plan the extension replaced,
  never written since — is gone, so no copy there can stay old outside the Marketplace's updates.

### Ctrl+Shift+Up / Down step a clef

- **On a `clef`, `Ctrl+Shift+Up` steps to the clef for higher notes and `Ctrl+Shift+Down` to the
  clef for lower ones** — `clef bass` → `clef tenor` → `clef alto` → `clef treble`, stopping at
  either end — a part header's `clef` (`part lh { clef bass }`) or a `clef` inside music. Any
  other clef steps from where it sorts (`clef treble_8`: Up → `alto`, Down → `tenor`).
  `Ctrl+Shift+PageUp` / `Ctrl+Shift+PageDown` move `treble` and `bass` an octave
  (`treble_8` ⇄ `treble` ⇄ `treble^8`, `bass_8` ⇄ `bass`; inside music only `treble` ⇄
  `treble_8`). Only the clef changes, not the notes after it.

### Ctrl+Shift+Up / Down walk a key round the circle of fifths

- **On a `key`, `Ctrl+Shift+Up` adds a sharp and `Ctrl+Shift+Down` a flat** — the key a fifth up
  or down: `key c major` → `key g major` → `key d major`; Down → `key f major`. The mode stays
  (`a minor` → `e minor`, `d dorian` → `a dorian`), the walk stops at seven sharps or flats, and
  flats are written the file's way (`ees` / `es`). Only the signature changes, not the notes after
  it. `key custom …` steps nothing.
- **On a `key`, `Ctrl+Shift+PageUp` switches to the parallel key and `Ctrl+Shift+PageDown` to the
  relative key** — `key c major` ⇄ `key c minor` (the same tonic), `key c major` ⇄ `key a minor`
  (the same signature). Major and minor only; a church mode says so.

### The step keys change a tempo: by 1, or by 10

- **On a `tempo`, `Ctrl+Shift+Up` / `Ctrl+Shift+Down` change the metronome number by 1, and
  `Ctrl+Shift+PageUp` / `Ctrl+Shift+PageDown` by 10** — the caret anywhere from `tempo` to its
  last value (`tempo "Allegro" 4 = 120` → `130`). Only the beats per minute step, never the beat
  unit or a `swing 16`; the number stops at 1. A tempo with no number (`tempo "Allegro"`,
  `tempo swing`) steps nothing, and the status bar says so.

### Guess Section Key reads the chord names too

- **Lily#: Guess Section Key… now reads a section's chord rows as well as its notes** — a
  `chords { }` row written in the section, or a chord track's `section` — each chord weighted by
  how long it sounds in the bar, and the last chord taken as the section's ending. A section of
  chords alone is guessed too. On the sections in our test books that write both a key and a chord
  row, the written key came first in 48 of 64, where the notes alone gave 38.

### A space after the note that fills a bar writes the bar line

- **Type `c d e r` and a space, and the `|` is written for you** (`c d e r | `) — when that note
  brings the bar to exactly the meter, at the end of what you are writing. Durations carry from
  the bars before as the compiler carries them, and the meter is the `time` in force. Where the
  bar cannot be counted for sure (a tuplet, grace or voice block, a pickup, a multi-bar rest) it
  writes nothing. Typing `|` and a space yourself right after is absorbed, one Ctrl+Z takes the
  bar line back, and `lilysharp.typingAids.enabled` turns it off with the other typing aids.
  Bare durations count (`c8 8 8 8 8 8 8 8`). In `voice { } { }` it moves on instead: the first
  voice's first bar full writes `} { ` (the next voice), and a later voice as long as the first
  writes `} ` (the span closes). No bar line is written inside a tuplet, a grace or a repeat.
  A closed voice span counts in the bar it stands in, as long as its first voice
  (`voice { c d } { d e } c2` + space writes the `|`).

### The preview keeps the caret's bar lit

- **The bar the editor's caret is in stays lit on the preview**, as the scan view lights it:
  each staff's bar from its first note to the bar line closing it (every printed copy), and it
  follows the caret and the edits.

### The preview frames what the scan boxes, part for part

- **The red dashed frame the preview draws for an OMR reader's mark is round the same thing as
  the reader's box on the scan**: the bar of the one part the mark is on; for a bar whose music
  does not fill its time, the bars of the parts that are off (two, where the reader boxed two);
  for a mark on the bar's stems or tuplets, every part (owner's request 2026-10-17: "なるべく
  ピンポイントに", and the same region on both). Until now the preview framed the whole system's
  bar, every part, whatever the mark. The frame follows the bars' lines in the `.lys` through
  the edits, as the hover between the two views does, so a section played twice frames both
  copies; a `@todo` the scan does not know (written by hand) frames the staff's bar it is written
  in.
- **And the scan frames such a mark part by part too**: a mark over two parts' bars is two
  frames on the scan, one per part-bar, where the reader drew one box round both (owner's
  report 2026-10-17: "破線の大きさが揃わない"). The frames share the mark's key, so the slider
  and a resolve take them together, and the slider's count stays a count of marks.
- **The preview's frame is as tall as the scan's**: two staff spaces above the staff's top line
  and two below its bottom one, the box the reader draws on the scan, instead of the staff's
  lines exactly (owner's request 2026-10-17: the roomier frame is the easier to see).

### The scan and the preview are cut alike at the top, and both show their title at the top

- **With *Scroll with the preview* on, the two panes are cut off alike at their top edge**
  (owner's request 2026-10-17): the bar at the top of the one you scroll is put at the top of
  the other with the same share of it out of sight — half a bar hidden here is half a bar hidden
  there — instead of a little below the edge whatever the cut.
- **Scrolling either view all the way up takes the other all the way up too** (same day). Each
  used to put the other's first bar at its top edge, which pushed the title — the score's on the
  preview, the page's head on the scan — out of sight.

### A mark on the scan is a dashed red frame, and the bar under it is still a bar

- **The red frame round a mark on Show Original Scan is dashed, as the preview's frame round a
  flagged bar is** (the same red, the same dashes; owner's request 2026-10-17). It is only a
  frame now: the mouse over it tints the bar under it blue, as anywhere else on the bar, and a
  click goes to that bar. Until now the frame was solid, filled red under the mouse, and its
  click went to the `@todo` itself; the memo and the reader's confidence still show as its
  tooltip, and the editor's caret on the `@todo` still lights its frame.

### Ctrl+wheel zooms the scan

- **Show Original Scan zooms with Ctrl+wheel**, as the score preview does (25% to 400% in steps
  of 10%; owner's request 2026-10-17). 100% is a page as wide as the pane; the point under the
  mouse stays under it, the zoom shows for a moment at the bottom right, and each scan keeps its
  own zoom while it is open. The bars and marks on the pages scale with them.
- **The scan's right-click menu is *Fit Width* and *Reset Zoom (1:1)***, as the preview's, in
  place of the webview's own Cut, Copy and Paste, which had nothing to act on (same day). Fit
  Width is 100%, a page as wide as the pane; 1:1 shows the page at the size it was read at, one
  pixel of the scan to one on the screen (within 400%). Both are in the Command Palette too
  while the scan is the active panel.

### The scan view scrolls when you drag a page

- **Show Original Scan scrolls when you drag a page** with the left mouse button, as the score
  preview does (an open hand over the pages, a closed one while they move; owner's request
  2026-10-17). A press becomes a drag only once the mouse has moved a few pixels, so a click on a
  bar or a red mark still goes to the music, and the click that ends a drag does nothing. The
  head line, its checkbox and slider, the scroll bars and touch work as before.

### The scan view's head stays put

- **The line at the top of Show Original Scan — what was read, *Scroll with the preview*, the
  *Marks below* slider — stays in view while the pages scroll under it** (owner's request
  2026-10-17). The scan's own scrolling (the bar it reports to the preview, the bar the preview
  asks for, the caret's bar coming into view) counts the top of the view from under that line.

### The scan view and the preview light each other's bar under the mouse

- **With Show Original Scan beside the preview, the bar under the mouse lights on both**: hover a
  bar of the preview and the scan lights that bar of that part; hover a bar of the scan and the
  preview lights it (each printed copy, where a section is played again).

### A line that opens on a `%4` keeps its four bars alike

- **A multi-bar percent (`%3` and up) that opens a justified line stands evenly**: its first bar,
  after the clef and the key, used to stretch with the key's width as well, so with five or six
  sharps it came out nearly twice as long as the others and the sign stood off the middle.

### A click on a preview opens its closed score again

- **With the `.lys` closed and its preview still open, a click on the preview opens the `.lys`
  again** and puts the caret on what was clicked (a note, a bar). It opens in the group that
  still has a tab of it, else the leftmost group without a preview, else a new group left of the
  preview. An untitled score that was closed is gone and is not reopened.

### Click a bar in the preview to go to it

- **A click on a bar in the score preview puts the editor's caret at the start of that bar**: its
  first note, rest or chord, in the staff you clicked. A click beside a staff (a lyric line, a
  chord name) goes to the nearest staff of that bar, and the bar under the mouse is tinted. A
  click on a note still goes to the note, a drag still scrolls, and during playback a click on a
  bar plays from there.
- **The scan view (Show Original Scan) and the preview light a bar alike**: the staff alone, in
  the same blue, with no frame — on the scan the bar the caret is in and the bar under the mouse,
  where the scan used to tint the box two spaces past the staff and frame the caret's bar.

### A clef change in mid-line writes the key's sharps and flats out

- After a clef change in the middle of a line, a note its key alters carries the key's ?/?
  (once a bar and note name, until the line ends) — in the preview as on the page.

### The percent repeat: `| % |` after the bars

- **`c4 d e f | % | % | % |`** — a percent repeat is the bars once and a mark a repetition after
  them (`%2` for two bars). The mark is coloured, its hover says how many bars it repeats and how
  many times they sound, and a mark that repeats nothing is underlined with the reason.
- **`repeat percent` / `repeat unfold` are errors with a quick fix** that rewrites the item as
  marks — or writes the music out, for an unfold or a beat. `repeat` now offers `tremolo` alone.

### A clef change on a condensed staff moves every part

- **A `clef` written by one part of a `condensedStaff` changes the staff for all of them**, and
  now the other parts' notes follow it too: they used to stay where the old clef put them.

### A quick fix for LilyPond's script shorthands

- **`c4-.` → `c4@staccato`** in one step: the error on a LilyPond shorthand (`-.` `->` `--` `-^`
  `-_` `-!`) now offers to write the annotation it names, glued to the note.

### Join a part's rests: `layout { compressRests true }`

- **`compressRests true`** engraves whole-bar rests written one after another (`R1 | R1 | R1`,
  typically one per section a part sits out) as one rest with its count, as `R1*3` would be. A
  run stops at a section label, a mark or tempo, a volta, a special bar line, a written break, a
  key/time/clef change, or a dynamic or fermata on the later rest. Completion, colouring and the
  layout snippet know the key.

### A clef for a condensed or combined staff

- **`condensedStaff bass { vla vc }`** (and `combinedStaff bass { … }`) draws the shared staff in
  the clef written before the brace, as `staff bass x` does for one part; without one the staff
  keeps its first part's clef. The twin writes the same `\clef`, and completion still offers the
  part names inside the braces.

### A shared staff places every part under its clef

- **`condensedStaff { vla vc }` and `combinedStaff { … }` drew a later part's notes where its own
  clef would put them**: the cello's C3 landed on the alto staff's B3 space instead of under the
  staff. Every part now reads its pitches under the staff's one clef (the first part's), as
  LilyPond does.

### Short instrument names

- **`part vn1 "Violin I" "Vn. I"`**: a second string on the part is its short name, printed at the
  staff's left from the second system on (LilyPond's `shortInstrumentName`, which the twin writes;
  MusicXML's `part-abbreviation`). Like LilyPond with its default short indent, the short name
  hangs into the left margin. `staff ~vn1` silences both names. A later system no longer prints
  the long name when a paper's `shortIndent` is set.
### Subito dynamics

- **`@p.subito`** prints the dynamic with an italic *sub.* before it and plays it as the plain
  dynamic. The level stands where the plain one would; the word hangs to its left, as LilyPond's
  `make-dynamic-script` draws it (which the twin writes). A side goes after it: `@ff.subito.up`.
  MusicXML writes the words and the dynamic in one direction. The editor offers `.subito` after a
  dynamic.
### One octave rule for both modes (breaking)

- **In `octave absolute`, an `instrument` preset now sets the part's base octave, as it always did
  in relative mode**: `instrument bass` makes a bare `c` C3, `flute` C5. Until now absolute mode
  ignored the preset (bare `c` was C4 whatever the instrument) while `octave N` moved it — so the
  same part sat an octave apart depending on the mode. **Existing files written in absolute mode
  with a preset whose octave is not 4 (viola, cello, bass/contrabass/bass5/bass6, piano-left,
  bassoon, trombone, voice-bass: 3; tuba: 2; flute, piccolo: 5) move by
  that octave**; add `octave 4` to the part to keep their notes where they were.
### Clearer errors for five common slips

- **LilyPond's script shorthands** (`c4-.` `->` `--` `-^` `-_` `-!`) are now one error each that
  names the Lily# annotation (`@staccato`, `@accent`, …), instead of two or three unrelated ones —
  and `a-!` no longer closes the bar with a dashed barline.
- **A second `(` while a slur is open is ignored and named**, and the `)` after it closes the first
  slur — as LilyPond draws it ("already have slur"). Lily# used to draw the inner slur and drop the
  outer one. A grace run's own slur inside a slurred passage is unaffected. A slur mark written
  after a barline (`g1 |)`) is now named where it stands.
- **`octave absolute` in a `part` declaration** says where the mode goes (the top of the file, or
  a part's music in a section), instead of "'absolute' is not an octave number".
- **An unknown instrument preset suggests the one meant**: `voice-alto` → `alto` (only the bass
  voice is `voice-bass`, since `bass` is the bass guitar), `violn` → `violin`.
- **Repeated slashes across a barline** (`/4 4 4 4 | 4 4 4 4 |`) no longer warn LYS1031: a slash
  has no pitch letter to have dropped.
### D.S. / D.C. / Fine clear the notes under the staff

- **A jump instruction under the staff ("D.S. al Coda", "D.C.", "Fine") now drops below the bottom
  staff's own ink** — a low note, its ledger lines, an accent under it, a dynamic — keeping LilyPond's
  outside-staff padding, where it used to hang at one fixed depth and could print through them.
### Reopening the scan

- **A `.lys` read from a scan has a button in the editor's title bar that opens the scan
  again** (*Show Original Scan*, until now only in the Command Palette — closing the scan's tab
  left no visible way back). The scan is found through the `x.omr.json` the reader wrote beside
  `x.lys`, so keep the two together when moving or renaming the file.
### A resolved `@todo` leaves the scan view

- **Deleting a `@todo` from the `.lys` takes its red frame off the scanned page** (shortly after the
  edit), and an undo brings it back. Marks the reader kept out of the `.lys` (the faint dashed
  ones) are not affected.
### The scan view's slider filters the preview too

- **Moving *Marks below* in the scan view hides and shows the same `@todo` marks in the score
  preview** — their rings, memos and leaders, and the red dashed frame of a bar whose marks are
  all hidden; the notes they ring stay. Closing the scan view shows them all again.
### A `@todo` ring round a moved rest

- **A rest marked `@todo` is ringed where it is drawn.** A rest moved up or down by its voice
  (`voice { r4.@todo … } { … }`), by another voice's notes, or by its written pitch
  (`g''4@rest`) used to be ringed at the middle line, beside the rest.
### Drag the preview to scroll

- **The score preview scrolls when you drag the sheet** with the left mouse button (an open hand
  over the sheet, a closed one while it moves). A click on a note still jumps to the editor —
  a press becomes a drag only once the mouse has moved a few pixels, and the click that ends a
  drag does nothing. Shift-click still selects, and touch, the scroll bars and the toolbar work
  as before.
### Grace notes after a note: `afterGrace`

- **`c2 afterGrace { d16 e }` writes grace notes AFTER the note before them** — LilyPond's
  `\afterGrace c2 { d16 e }`, which the twin writes. The run stands at 3/4 of the main note, as
  LilyPond places it: mid-bar it reads as the next note's grace, at the end of a bar it sits
  before the bar line. A tie over it reaches the next note (`g1~ afterGrace { a16 b } | g4`), it
  works inside a tuplet, and the MIDI sounds the run at the end of the main note. MusicXML marks
  the run `steal-time-previous="25"`. Written with no note before it, it is not engraved and
  Lily# says so (LYS4020). `afterGrace` cannot name a part.
### A part cannot be named `as`

- **`part as` is refused at the declaration**, naming the reserved words. In a score `as` opens a
  staff's drawing words (`staff m as lines 1`), and a bare `staff as …` is the unnamed part drawn
  so — a part named `as` could never be placed, and the score used to say "a bare 'staff' renders
  the unnamed part" about a file that named one.

### Ending a hairpin with no dynamic

- **`@!cresc`, `@!decresc` and `@!dim` end a hairpin on their note** — LilyPond's `\!`, which
  the twin writes. The wedge reaches that note's right edge, or the bar line when the note opens
  its bar, as LilyPond draws it. Until now a hairpin could only end at the next dynamic or
  hairpin, and `@!cresc` was refused.

### Additive meters beam by their terms

- **`time 3+2+2/8` beams its eighths 3 + 2 + 2** (and `2+2+3/8` 2 + 2 + 3): an additive
  numerator is the beat structure, as LilyPond reads the twin's `\time #'((3 2 2) . 8)`. Until
  now the page drew the additive meter and left every eighth unbeamed. A plain `time 7/8` still
  beams nothing — LilyPond's table has no grouping for it either.
- **`time 7/8 (2+2+3)`: a beat grouping after the meter.** The signature prints 7/8 and the
  eighths beam 2 + 2 + 3 — LilyPond's `\time 2,2,3 7/8`, which the twin writes. The terms add
  up to the numerator (LYS0041 otherwise). A `time` that only regroups the meter in force prints
  no signature again and regroups the beams from there; a section that writes no `time` returns
  to the file's grouping.

### Chord names on a transposing part

- **A chords row over a staff printed for a transposing player names the chords that player
  reads.** The row is sounding pitch (it belongs to no instrument, and the MIDI plays it so);
  over a B♭ trumpet's part (`score trumpet pitch transposed { chords prog  staff tp }`, or a
  `staff tp as pitch transposed` in a sounding score) concert `F6 B♭7 B°7` prints `G6 C7 C♯°7`,
  spelled in the key the staff prints — the capo's pressed-name road. Until now the trumpet read
  G major under concert names. A row over a sounding staff, or over none, is unchanged.
  The LilyPond twin writes the row under the same `\transpose`; MusicXML writes a transposing
  part's harmonies in its written pitch, as it writes its notes (a reader adds the part's
  `<transpose>` to both).

### `@todo` is information, not a warning

- **A `@todo` mark (LYS4026) is now reported as information**, not as a warning: a mark left to
  check is not a problem in the music. It still lists in the Problems view, and F8 / Shift+F8
  still reach it — after the real warnings, since VS Code orders them by severity. In the editor
  it underlines in blue. `lysc check` prints it as `info:`; `--todo-as-error` and `--no-todo` are
  unchanged. A malformed `@todo(…)` (LYS4027) and a key written twice (LYS4028) stay warnings.
- **The OMR reader's marks it is unsure of are warnings.** Where an `x.omr.json` beside the score
  gives a mark a `confidence` below 0.6 (or none), the mark is a warning; the rest stay
  information. F8 then walks the doubtful readings — a wrong note, a bar's length — before the
  ties and slurs the reader was surer of.
- **The scan view draws a mark by how sure the reader is**: an outline only, darker and a little
  heavier the less sure (1 to 2.5 px, so it does not cover the heads under it); a slider at the top (*Marks below 0.80*, with how many show) hides the surer
  ones, and starts where the reader's own threshold left the `.lys`. Marks the reader kept out of
  the `.lys` (`inSource: false`) draw as faint dashes. The import's message counts them
  ("+40 quiet").

### Scan import (Beta)

- **The OMR reader comes as its own extension, *Lily# Scan Import (Beta)*** (`yotsuda.lilysharp-omr`,
  Windows x64). Lily# finds the reader it carries — after `LILYSHARP_OMR_PATH`, before the global
  storage — and *Convert Image/PDF to Lily# (.lys)… (Beta)* offers to install it when no reader is found. The
  command is in the palette on Windows, before any `.lys` is opened; the Explorer's right-click
  item appears once Lily# is running and a reader is installed, and goes when it is removed, with no
  reload.
- **Import checks what it read right away**: a reader that has `omr verify` engraves the score and
  compares it with the scan while reading (`omr read --verify`), so the first look already carries
  the comparison's marks. `.bmp` scans are offered too.
- The first import says once that the reader is a Beta and where its marks are.
- **The scan view's tab names the original** — `Scan: score.pdf`, `Scan: page1.jpg, page2.jpg`, `Scan: page1.jpg +2` — instead of the `.lys`; hovering a page shows which file and page it is (`score.pdf p.3`).

## 0.12.0

### Lessons

- **Twelve lessons inside the editor.** At the top level of a `.lys` file, Ctrl+Space offers
  `lesson-01-completion`; typing `lesson` lists all twelve. Each one replaces the file (it asks
  first) with a short, playable score that teaches one step: the completion list and the
  preview, notes and octaves, rhythm, keys, expression, form and repeats, lyrics, chords,
  piano, drums (patterns picked from the completion list), a band, and a jazz chart (slashes
  on a one-line staff under chord symbols, in Petaluma). Lessons are kept per language and
  follow the editor's display language where a translation exists (English for now). The New
  Score file points to them, and the same lessons are on the site's Learn pages.

### Language

- **Drum charts: `drums { hats  back  kick }`.** A kit part written as the patterns a drummer
  thinks in, each line a phrase or a block, all played together; the preview draws the hands'
  hits stems up and the feet's stems down. In a drum part, Ctrl+Space also offers fourteen
  stock one-bar patterns (hi-hats, a ride shuffle, backbeat, half time, ghost notes, a flam,
  four on the floor, fills…), each showing its drum grid; picking one writes its phrase.
- **A head in parentheses: `@parenthesize`** — a drummer's ghost note, or any note — offered by
  the `@` completion; on a chord one pair encloses it, on a member that head alone.
- **One staff can print in its player's key**: `staff tp as pitch transposed` (or `concert`).
- **A chord symbol takes `@todo`.** In a `chords` row the mark is glued after the symbol (and
  after its shape group): `Am7@todo("Am7 or C?")`, `F(133211)@todo(o12 "barre?")`, and `r@todo`
  on a no-chord slot. A chord chart has no notes, so a doubt about a chord had nowhere to go.
  It is reported like a note's mark (LYS4026, Resolve this TODO), completed after `G@`, and the
  preview rings the symbol and sets the memo, with room reserved for it. It is the only mark a
  chords row takes; any other is LYS1028.

### Breaking changes

- **A score without `pitch` prints the parts as they are written**, and a page says
  `pitch transposed` where it said `written` (`written` stays where notes are typed). The
  product changelog has the details.
- **An `instrument` preset prints a real name ("Alto Saxophone"), and only beside another
  instrument**: a one-staff score loses the preset's word as its label. Write `part X "Name"` to
  keep one.
- **A block's name is optional, and a score has one name, a bare word.** `form { … }` is the
  file's default form, `score { … }` its default score (the picker shows it as `(Default)` and
  draws it first), `part { … }` its one part, `chords { … }` and `lyrics { … }` its unnamed
  tracks, named bare in a score (`staff`, `tab`, `chords`, `lyrics`). `score "tab"` and
  `score tab "both"` are LYS0038, with a quick fix that writes the word; `score tab` exports to
  `<file>-tab`, the unnamed score to `<file>`. A score picks its form inside (`form NAME` or
  `form { … }`); `score main` and `form main` are plain names no more. The `form` and `score`
  snippets write the unnamed block first, and `form |` inside a score lists the file's forms.
- **A book with sections and no `score` block is an error** (LYS6014), with a quick fix that
  appends the score — every chord track as a row, a staff per part, every lyrics track. It used
  to draw the first part alone.
- **`staff bass click` beside a part named `bass` is that part's staff, with `click` played to
  MIDI only**, as `tab bass click` reads; LYS1046 names the spellings when a word is both a part
  and a clef.

### Music fonts (SMuFL)

- **`fonts { music "…" }` names the music font** — Bravura, Petaluma and Leland are bundled
  beside Emmentaler, and several names are a per-glyph fallback chain. The key completes with
  its quotes and the bundled names, the grammar colours it, and the preview draws the named
  font (it declares Bravura, Petaluma, Leland and Petaluma Script beside the text faces).
- **`lilysharp.fontPaths`** (machine-overridable) lists the folders this machine keeps SMuFL
  fonts in — a font's metadata and program directly in a folder or in a subfolder of its name
  — searched before the standard SMuFL locations; `LILYSHARP_FONT_PATH` is read too. A score
  names its font only, never a path. Changing the folders redraws the open previews.
- **Under Petaluma the text is Petaluma Script**: the header, chord symbols, rehearsal marks and
  section labels, tempo and navigation words, `@text`, dynamic and pedal words and instrument
  names, unless the score names their face; the lyrics keep the engraving face.

### Playback

- **The preview plays a sound font.** The Play button, Alt+P / Alt+M and the auditions sound
  through a SoundFont synthesizer (spessasynth_lib, in an audio worklet) instead of the old
  oscillator tones: each part plays the General MIDI instrument its `midiInstrument` or preset
  names, as the exported MIDI does, with drums on the drum channel and a quarter tone as a
  pitch bend. By default it plays the General MIDI bank your OS ships for its own synth
  (Windows' `gm.dls`, macOS's `gs_instruments.dls`) — no font is bundled; where the OS has
  none (Linux), the oscillator tones play. `lilysharp.playback.soundFont` names an `.sf2`,
  `.sf3` or `.dls` of your own for fuller instruments; it takes effect in open previews.
  **Lily#: Choose Sound Font…** (also a link in the setting's description) picks the default
  or a file of yours through a file dialog, and remembers the last five fonts used, listed for
  a pick (a font whose file is not there now is left out; 🗑 takes one off the list). The
  list is kept on this computer, not in the settings. The site's editor page names three free General
  MIDI fonts (MuseScore_General, FluidR3Mono_GM, GeneralUser GS) and how to set one up.
- **A dense score no longer breaks up.** The old player built every note's oscillator the
  moment Play was pressed and summed the voices with no ceiling, so four voices already
  clipped and a long piece dropped out. The synth is now fed a quarter of a second ahead of
  the clock (longer while the preview is hidden), the output goes through a limiter, Stop
  silences what was queued, and the follow-along highlight walks the onsets once instead of
  scanning the piece every tick.

### Editor

- **The preview opens by itself and follows the file in front.** Opening a `.lys` (or New
  Score) opens the preview beside it (`lilysharp.preview.openOnOpen`); switching to another
  `.lys` turns the same preview to it, each keeping its zoom and scroll. The lock in the
  preview's title bar keeps it on one file.
- **A phrase says what it holds**: hovering its name shows its length, how often the file
  plays it and its source — a drum phrase as a grid, a row per drum.
- **A tied note sounds as its part plays it**: landing on or stepping the second note of a tie
  struck the default piano at the written pitch; it now takes the part's instrument and
  transposition.
- **Guess Section Key…** (editor context menu and the command palette). It reads the notes of
  the section at the caret — or of every section a selection touches, or the one a `form`
  reference names — across all its parts, and lists the likely keys best first, only those scoring within 0.3 of the best and at most five (duration-weighted
  pitch classes against the Krumhansl–Kessler profiles, plus a nudge for the key whose tonic the
  section ends on; the tonic is spelled as the written accidentals spell it, `fis` or `ges`).
  The key picked is written as the one the section starts in: a key already stated there is
  rewritten, otherwise it goes into the section's header (a file grouped by part with no
  top-level `section` gets `section A { key … }`). Escape changes nothing.
  After a key is picked, the notes spelled against it are offered for respelling (below).
- **Respell Notes to Key…** (editor context menu and the command palette). Notes written before
  the key was known come out spelled the wrong way round — `fis` where G♭ major wants `ges`, or
  `fis` and `ges` both in one section. The command lists the respellings that put the section
  at the caret (or every section a selection touches) in the key it starts in — the guessed
  key when it states none — as `fis → ges · 5 notes`, all checked; the checked ones are
  applied. A note in the key's scale takes the scale's spelling; a note outside it is left
  alone when the section writes it one way, and unified to the spelling written most when it
  writes it several (a tie goes to the key's way: the raised sixth and seventh in minor, sharps
  in a sharp key, flats in a flat key). Notes after a key change inside the section are left
  alone. Every note keeps its pitch: octave marks are recomputed for relative music and for
  `b` ⇄ `ces`, and the rewrite is checked note by note or refused.
- **`midiInstrument` completes with its quotes and its names.** Picking the property writes
  `midiInstrument ""` with the caret between the quotes and lists LilyPond's 128 General MIDI
  names there, in GM order with their family; a name typed part-way (`"electric gui`) is
  replaced whole, spaces and parentheses included, and before any quote the names bring
  their own.
- **A `lily#` fence may hold a whole book**: the preview draws the book's default score instead
  of refusing a fence with more than one score. A fence with no score is still refused.
- **A section's label is declared once** — `section A2 "A'" { }` at the top level — and the
  forms stop repeating it (LYS1045 when two declarations disagree, LYS0039 inside a part).

### Fixes

- **`@mp|` (any dynamic level) offers `.up` / `.down` on Ctrl+Space**, as `@staccato|` did:
  the placement completion knew the articulations only. A hairpin (`@cresc`) still offers none,
  since it is always below the staff.
- **A `@todo`'s warning underlines what it says, not the whole mark.** `@todo("check this")`
  underlines `check this` (a key when there is no memo, the word `todo` when there is
  neither): a note to yourself is not a mistake. "Resolve this TODO" (Ctrl+.) still answers
  anywhere on the mark and deletes all of it.
- **The preview checks the source offsets of a page it updated in place.** After reusing the
  unchanged systems of a changed page, it reads every offset back against the page it was
  sent; a page that disagrees is replaced whole, and the output channel says
  `STALE OFFSETS on page N`. A stale offset made the caret or a selection light the wrong
  note until the preview was reopened.
- **A `@todo` memo in a score with a numeric meter (3/4, 6/8, …) no longer stops the
  preview.** Placing the memo measured every glyph on the page by its design box, which the
  time-signature digits do not have; the picture failed to render at all (4/4, drawn as the
  C glyph, was not affected).
- **Petaluma Script is kerned in the preview.** The bundled WOFF2s are now the OTFs wrapped
  unchanged; the old conversion carried no GPOS or kern table, so a section label overran its
  box and every chord symbol was a little wide.
- **SixLabors.ImageSharp is not in the VSIX any more** — a PdfSharpCore dependency Lily# never
  used, carrying five advisories.
- **A member of `<…>` sounds its whole chord** when stepped (Ctrl+Shift+↑/↓) or landed on — one
  sound, with the member moved — where it struck the member alone; arrowing across the members
  strikes it once.
- **The preview says when its sound is blocked.** A step or an audition struck before the
  preview has been clicked or typed in once (the browser's autoplay rule) now puts *click the
  preview once to enable its sound* in the status bar, and the preview keeps one synth for its
  life and resumes it from that first click; it used to stay silent with no word.
- **A `@todo` is shown as red ink over the page.** The preview rings the marked head, rest or
  chord with a ring drawn as a hand draws one (one ring round a whole chord) instead of filling
  it red, and a memo stands as a line of small red text, underlined, in the margin above the
  system — `.down` below — set off to the side with more room, with a leader from the ring
  to the underline, all in one pen. The preview's layout reserves each memo's place on the
  system's silhouette, so a system moves down only where a memo needs more room than the
  page has there (a memo that fits between the systems or in the margin moves nothing);
  within that room the memo is set where nothing else is drawn, crossing to
  the note's other side where that is clear and climbing past a chord symbol, a dynamic, a
  ledgered note or an earlier memo otherwise, and a system's memos are arranged together so
  that no leader runs through another memo's words where any arrangement avoids it.
  `lilysharp.preview.highlightTodos` off hides them, and an export draws none unless asked
  (`lysc svg|png|pdf --todo`) — without them it is the page the book would have without its
  memos.
- **`@todo` in the preview, fixed where it was wrong**: a memo stands in its own staff's margin
  (and in the gap between two staves when it is wider than a bar), not over the whole system; a
  tab note's ring goes round its fret number; a `@todo` in a `drums { }` line or on a slash
  note is drawn; the underline sits alike under Latin and Japanese words.
- **Regroup keeps the comments** it used to drop.
- **Drums in the preview**: a drum chord draws once, a drum in a chord takes its own marks, a
  flam is drawn and played, a drum note's own script and drum notes inside `tuplet { }` are
  drawn, and a bare duration repeating a drum keeps its mark.
- **A fallback music font's glyphs show** (Leland's slash heads were missing), and so do a
  grace's or a cue's design of the music font.
- **Two new warnings**: a chord shape no score can draw (LYS1038), and a tie on the last note
  of a `repeat percent` body (LYS4007).
- **`todo` completes into its quotes**: `todo("")` with the caret between the quotes, like
  `text` and `mark`; it used to write the bare word.
- **`.down` completes after a memo**: after `@todo("…")`, `@text("…")` or `@diagram(…)`, a `.`
  (or completion right after the `)`) offers `.up` and `.down`, the side that moves the mark
  first; the `.` opens a list nowhere else.

## 0.11.0

### Breaking changes

- **Strings follow C#'s grammar.** `"…"` decodes C#'s escapes (`\"`, `\\`, `\n`, `\uXXXX`…) and
  any other backslash is an error (LYS0036); `@"…"` is verbatim (`""` is a quote). Both forms
  are coloured, escapes included, and an escaped quote no longer ends the string's colour.
- **A navigation mark is written in the `form` and nowhere else** (LYS1034). `segno`, `coda`,
  `fine`, `to coda`, `dc`, `ds` and their `al` forms in a section's music are errors, and the
  music completion no longer offers them; write them between the section names in the `form`.

### Marks left to check (`@todo`) and scanned originals

- **`@todo` marks a note, rest or chord to come back to** (`c4@todo`, `c4@todo("F♯ or F♮?")`).
  Each is listed in Problems (LYS4026), the preview draws its head red
  (`lilysharp.preview.highlightTodos` turns that off), and the quick fixes *Resolve this TODO* /
  *Resolve all N TODOs* delete them. When an OMR reader wrote the file, the readings its
  `x.omr.json` offers for a mark are quick fixes too.
- **Lily#: Show Original Scan.** On a `.lys` an OMR reader wrote, the scanned pages open beside
  the score with the marks the reader was unsure of boxed in red. A click on a mark goes to its
  `@todo`, a click on a bar to the line it is written on, and the caret lights its mark or bar —
  through any edit since. With *Scroll with the preview* on, the scan and the preview scroll
  together bar by bar.
- **The AI transform sees the marks it is asked about.** For each `@todo` in the selection the
  prompt carries its memo and, from an OMR reader, how sure the reader was and the readings it
  offers; a mark it resolves loses its `@todo`.
- **Check on save against the scan** (`lilysharp.omr.verifyOnSave`, off by default). Saving a
  `.lys` an OMR reader wrote has the reader engrave it again and compare it with the scan; the
  scan view and the quick fixes pick up what it finds. Needs a reader with `omr verify`.

### Chord diagrams

- **Completion and colours inside a chord-shape table.** Inside the table the completion offers
  the chords the file names that the table does not list yet, then the key's, and `section` —
  then the file's section names after it. The table is coloured — `section NAME`, the tuning
  words and the shapes — and its closing brace no longer ends the layout block's colours (the
  keys after a table used to go plain).
- **A `chord(…)` item reads each score's capo.** Its hover lists each score's capo when the
  scores playing the part differ, and names the notes it sounds under the capo (it used to read
  the first score's capo, and under any capo named the notes as if there were none). The item's
  shape check reads each score's capo too.

### Fixes

- **The preview's Export button writes the selected score's own form** to MIDI, MusicXML
  and LilyPond source. With the picker on `score practice` whose `form practice` starts at
  section B, the .mid began with section A (the file's primary form); it now begins at B,
  as `lysc midi --score practice` does.
- **The preview's Export button and the Explorer's batch export write what `lysc` writes.** The
  button's PDF or PNG of a score whose name no longer matched drew the file's scoreless picture,
  and its SVG could leave the font out; a MIDI or LilyPond export's warnings now reach the editor.
- **The preview plays the score it shows.** With a score of one part picked, Play and the
  audition keys sounded every part of the file; they now sound that score's parts and form.
- **An edit in a section the form plays again reaches every play in the preview.** Stepping a
  `chord(…)`'s shape (Ctrl+Shift+↑) or typing a note in a section played six times redrew only
  the first play.
- **The preview follows a section header written below the part.** A key, `partial`, `time` or
  `tempo` typed into such a header left the preview on the old header until the music was
  touched.
- **A half-typed `key` no longer stops the preview,** and a click on a line's key signature goes
  where the key was set after any edit.
- **The outline and breadcrumbs work in a book with a custom key** (`key custom fis cis` made
  them fail for the whole book).

## 0.10.0

### Breaking changes

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
