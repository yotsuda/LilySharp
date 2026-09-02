# Changelog

Notable changes to Lily# are recorded here, newest first. Release notes are taken
from this file: the topmost section is the version being tagged, and the release
workflow attaches that section to the GitHub Release verbatim.

## 0.5.0

The language settles two things it had left loose — where a book's playing order is
written, and how a span ends — and the engraver starts walking the inside of a grace body.
The rest is defects found by engraving real scores.

### Language

- **A score row's `sings` is that row's own melody.** `lyrics verse sings alt` among the
  score items places the `verse` track at the alto's rhythm — under the alto staff it is the
  alto's verse — whatever the definition said. The definition's `sings` is now the track's
  DEFAULT, taken by a row that writes none; a second target on a definition block is still
  `LYS7005`, but rows never conflict. It is how a chorale writes its words once and puts
  them under the soprano, alto, tenor and bass: `staff alt  lyrics verse sings alt`. Until
  now the row spelling was the same single track property, so the second staff's row was
  refused (`LYS7005`) and, inside the group, refused again (`LYS6012`).

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
