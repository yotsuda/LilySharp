# Changelog

Notable changes to Lily# are recorded here, newest first. Release notes are taken
from this file: the topmost section is the version being tagged, and the release
workflow attaches that section to the GitHub Release verbatim.

## 0.5.0

Unreleased. Defects a reader of real scores found, and the two language spellings that
turned out to be hiding them.

### Breaking changes

- **A phrase reference's interval argument is removed.** `Melody'(3)` no longer plays the
  phrase a third up; the octave marks `Melody'` and `Melody,` stay exactly as they were.
  The glued form asked a reader to hold two non-obvious rules to read one token — `'`
  stopped meaning "an octave" and started meaning "upwards" as soon as `(N)` was attached,
  and the degree was 1-based on top of that (`'(8)` == `'`) — while a single space turned
  the whole thing into a reference followed by a slur. No book in the repository wrote it.
  There is no per-reference transposition any more: `transpose` is a part property and
  chromatic, so a motif quoted a third higher is now written out.
- **A percent repeat prints the sign its body's LENGTH earns, exactly as LilyPond decides
  it.** A one-measure body is unchanged. A TWO-measure body prints ONE double sign on the
  bar line between the pair, where it used to print a single `%` in each measure — so
  `repeat percent 4 { r1 | r1 }` is three double signs rather than six single ones, and
  both measures under each sign print no music. **Anything else** — a body shorter than a
  measure, longer than one, or a whole run of three or more — prints ONE repeat slash where
  the repetition starts and leaves the rest of the repetition's measures blank, where Lily#
  used to stamp a percent in every repeated measure. `repeat percent 2 { c1 | c1 | c1 }` is
  three written measures, one slash, and two empty bars. The slash count comes from the
  body's written durations: equal durations give a plain slash, unequal ones give the
  double slash. LilyPond has two length tests and one else, not four cases; the belief that
  it reserved the slash for sub-measure patterns came from the grob descriptions rather
  than from the iterator, and the warning that admitted the invented picture (`LYS2014`) is
  retired with it. A reader reported the warning as wrong and was right.
- **A tab staff's style now follows the score, and a numbers tab draws no tie.** `tab NAME`
  with no `as` clause is `as numbers` when the same part is also on a notation staff — that
  staff already carries the meter, the rests, the dots, the stems and the ties, so the tab
  needs fret digits only — and `as full` when the tab stands alone and has to carry them
  itself. An explicit clause always wins, and a condensed, combined or grand staff counts as
  a notation staff while an ossia does not. Separately, `as numbers` no longer draws ties:
  LilyPond's tab context sets `Tie.stencil` to `##f` and says a held note is held by dropping
  the tied-to fret number, which Lily# already did — it was drawing the bow as well, on both
  styles, and a reader reported it. `as full` keeps its ties. Slurs are unaffected in both,
  which is also LilyPond's answer: the same block that hides the tie keeps the slur.

- **`staff <clef word>` alone names a part** — `staff bass` renders a part named
  `bass`, and says so (`LYS1007`) when none is declared. The reference scan read that
  lone word as a clef with the name left off and collected nothing, so a score whose
  staff named no part at all engraved a page of empty bars while `lysc check` answered
  "No errors found"; `staff bass as lines 1` reported the selector's `as` instead of
  the name. Four words could reach this — `treble`, `bass`, `alto`, `tenor`. The
  corpus's one occurrence names a part that exists, so `lysc check` is byte-identical
  across all 573 books.

### Diagnostics

- **A note that opens an indented line is clickable, and a diagnostic on one names its
  column.** The address every source-pointing feature hands out — the SVG's `data-pos` that
  click-to-source resolves, the `(line, column)` `lysc check` prints — came from the node's
  position INCLUDING the whitespace in front of it. Same-line spacing belongs to the
  previous token, so only a line break showed it: the first note of every indented line
  carried the offset of the newline and the indent, the editor resolved a click there and
  lit nothing up, and an overfull measure whose first note stood at column 5 reported column
  1. `GreenNode.LeadingTrivia` is virtual and only a token overrides it, so a composite node
  — a note, a chord, a repeat — answered "no leading trivia" whatever its first token
  carried. 597 of the 1519 books on disk change, and every one of them differs ONLY in
  `data-pos`: no glyph and no page moves. 466 diagnostic lines move, and every one of them
  differs only in its address — no message, no count and no exit code changes.

- **A part engraved on two staves no longer complains twice.** A score that puts one part
  on both a standard staff and a tab staff (`score { staff bass  tab bass }`) collects that
  part once per staff, and the per-voice sanity scans — the tie target, the unclosed slur,
  the unopened manual beam, and the slur or tie that crosses a cue boundary — appended to
  lists that live on the whole collect. So one slip in the source printed one complaint per
  staff, at the same position, naming the same character (an error, in the cue case, rather
  than a warning). The scans now run once per voice, however many staves engrave it. 260 of the
  899 books put a part on two staves; four of them also carry one of these slips, and those
  four printed five lines twice over. Each is now printed once. No warning disappeared, no
  exit code changed, and no page moved — these scans have no say in what is drawn.

- An overfull bar that runs into a `repeat` is reported where it was WRITTEN. A bar left
  open in front of a repeat is part of the repeat body's first bar, and the warning used
  to land inside the body — so `r1 r1 r1` with the bar lines left out drew its complaint
  on the next line, in music the writer had not made a mistake in. The span now reaches
  back over the enclosing music and still covers the body, because the bar really is made
  of both. Across 898 books no warning appeared or disappeared and no exit code changed;
  30 of them (all outside the repository) point somewhere earlier.

### Engraving fidelity

- **A chord row over a `rit.` clears it on every system, not only the first.** The row is
  spaced against the staff below it, and the staff's own `rit.`/`accel.` text is
  outside-staff ink standing between the two — which the first system's placement knew
  about and no later system's did, because a row above a later system is placed by the
  loose-line chain and that chain measured the staff's INSIDE silhouette. So one book
  printed its chord symbols clear of the word on system 1 and straight through it on
  system 3. LilyPond spaces a loose line against the axis group's skyline, which a placed
  outside-staff grob is part of. One book of 1519 on disk moves, which is the book that was
  reported; nothing else does, and `lysc check` is byte-identical.

- **A dynamic on a lower voice is engraved at its own note.** The label's column was
  resolved against the STAFF's stream of items, so the voice's item index named whatever
  the first voice happened to have at that ordinal — with `voice { c''8 d'' e'' f'' … } { c2 e2@f }`
  the `f` was drawn under the upper voice's `d''`, three notes early. An item index only
  means anything inside the stream it was recorded against, and the sibling script side
  (`@staccato` on the same note) had always resolved the voice's own measures, so one note
  could carry two marks that disagreed about where it was. Both `voice { } { }` and
  `condensedStaff` reach it. The three existing two-voice dynamics books all give their
  voices the same rhythm, which makes index and timing agree, so no book in the corpus
  could see it: `lysc check` is byte-identical and no stored page moves.

- The percent-repeat sign is LilyPond's own shape: the slash is the parallelogram
  `Lookup::repeat_slash` builds, cut horizontally at its ends, rather than a stroked line
  cut square to the slope — which made the ink 0.51 too tall and 0.51 too narrow on a tab
  staff, where everything is 1.5-sized and a user noticed the sign looking too heavy. Its
  two dots now hang off the edges of the whole slash group with LilyPond's negative kern
  instead of a constant offset (0.81 and 1.11 staff spaces from the centre, matching the
  measurement, against 0.5 and 0.625), and each is the font's own 0.225 rather than a 0.25
  stand-in. The perpendicular thickness was already right; the outline was not.
- **A repeat barline's dots are the size LilyPond draws them.** LilyPond has no radius
  there at all — it stamps the `dots.dot` glyph, the same one an augmentation dot uses —
  and that glyph's half extent is 0.225, which Lily#'s own font extraction has said all
  along. The drawn circle was 0.2: a fifth of a staff space short across, on every repeat
  sign in every score. Closing it also widens the horizontal room a repeat barline
  reserves, because that reservation is computed from the same number, so bar lines and
  everything after them shift by 0.05 per repeat — the reservation had been reserving for
  a dot LilyPond does not draw. Eleven snapshots re-based; 132 of 685 books move, all of
  them by that shift and the radius.
- **And they sit where LilyPond's search puts them, which depends on how many staff lines
  there are.** LilyPond looks for the first space wide enough to hold a dot and a staff
  line, folding the staff about its centre; Lily# used one number, 0.5, which is that
  search's answer for five lines and for three and wrong for the rest. Measured on 2.26.0,
  one staff per line count: 1 line → 0.45, 2 → 0.95, 3 → 0.5, 4 → 1.0, 5 → 0.5, and a band
  with no staff lines at all (a lead-sheet row) → 0.45. A one-line rhythm staff
  (`staff comp as lines 1`) is the case that gets written, and 0.5 put its dots nearly on
  the single line the search exists to keep them off.
- **What a part writes on a `combinedStaff` is drawn on that part's own notes.** Combining
  two parts onto one staff does not put their voices on it: the combiner rewrites both
  streams, moving notes between the two it draws, merging a shared moment into one column,
  and leaving unengraved whatever the other part is covering. Everything a part hangs off a
  note — a dynamic, a piece of text, an articulation, a fingering, a chord frame, a bend, a
  trill, a tuplet bracket — was still addressed by where it stood in the part, so on the
  second part all of it landed on the FIRST part's notes: a `@f` under a note nobody wrote
  it on, a triplet number engraved above the staff over the other part's beam. It is now
  addressed where the combiner actually put the note, and a passage the combiner engraves
  with nobody takes its markings with it, which is what LilyPond does. Three books in the
  repository move, all three onto the measurement LilyPond gave them: a bar where one part
  writes `R1` and the other `r1` prints one rest and ONE label, not two.

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
