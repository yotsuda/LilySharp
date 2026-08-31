# Changelog

Notable changes to Lily# are recorded here, newest first. Release notes are taken
from this file: the topmost section is the version being tagged, and the release
workflow attaches that section to the GitHub Release verbatim.

## 0.5.0

Defects a reader of real scores found, and the language spellings that turned out to be
hiding them. The largest change is that a book's playing order is now written in one
place — a `form { … }` — and the second largest is that every span the language can open
now has to be closed. Both are diagnosed rather than silent, and both will stop existing
books compiling; read the breaking changes before upgrading.

### Breaking changes

- **Repeat structure is written in a `form { … }` and nowhere else.** A repeat bar line
  (`|:`, `:|`, `:|:`) or a volta ending (`[1. … ]`) written in MUSIC is now an error,
  `LYS1034`. The line drawn is "does it change the playing ORDER": a repeat does, and a
  book's order lives where its form is, so `repeat percent`, `repeat unfold` and
  `tremolo` stay in music — they abbreviate notes and the order is unchanged. The
  spelling still parses, which is what lets the diagnostic point at the character; what
  is forbidden is a place, not a token. **This is the change most likely to stop a book
  of yours compiling** — 115 of the author's 326 books and 11 of the repository's tracked
  books do — and it lands as a hard error with no grace period and no rewriter. The move
  is not purely syntactic, and that is a consequence rather than an open question: a `|:`
  means something different in the two places. Measured on a two-part book with the
  repeat written in the upper part only, in music it expands THAT PART alone (8 notes
  over 4) and in a form it expands the whole score (8 and 8). The page has always read it
  as score-level, so before this rule the page and the MIDI disagreed with each other;
  after it, the disagreeing spelling cannot be written. Two spellings it deliberately
  does not catch, because they are different node types rather than an exclusion list: a
  LYRIC verse header `[1. … ]` (the words for the Nth pass, not a repeat) and a lyric
  row's own bar lines.

- **A span must be closed, and `@!X` is how — the terminator the language never had.**
  `@textSpan("poco rit.")` is the primitive and `@rit` / `@accel` / `@rall` are
  START-ONLY sugar for it; each is ended by `@!rit`, `@!accel`, `@!rall` or
  `@!textSpan`. A span nobody closes now draws NOTHING — not its dashed line and not its
  word — where Lily# used to cover one measure and say nothing about the length being the
  engine's guess rather than the writer's instruction. That is LilyPond's own answer:
  `Text_spanner_engraver`'s `finalize` warns and calls `suicide()`, so the word goes with
  the line, and there is no default length anywhere in LilyPond. Three inventions are
  retired with the fallback: the one-measure default, the search that let "the next
  `rit.`" end the previous one, and the guard against a mark ending its own second
  playing. Pairing is per (staff, VOICE), which is the context LilyPond keeps the
  engraver in, so a terminator written in another voice reaches nothing. Of the whole
  corpus — 1582 books on disk, the author's library included — 28 move, and all 28 are
  books that write a text spanner; one of the author's now draws no `rit.` until its
  `@!rit` is written.

- **The ottava and the pedal are spans too, and their direction moved out of the name
  into the `!`.** `@ottava … @!ottava` (and `@ottava(bassa)`, `@quindicesima` — one
  terminator for the whole family); `@sustain … @!sustain`, `@sostenuto …
  @!sostenuto`, `@unaCorda … @!unaCorda`. **`@loco`, `@sustainOn`, `@sustainOff`,
  `@sostenutoOn` and `@sostenutoOff` are retired.** This is LilyPond's own model said
  once rather than a departure from it: `ly/spanners-init.ly` spells six pedal commands
  and every one of them is the same line with a direction argument
  (`sustainOn = #(make-span-event 'SustainEvent START)`) — one span event, and the
  On/Off suffix was only how the surface command spelt START and STOP. `@loco` went
  because it named a mark NOTHING PRINTED and LilyPond has no `\loco` either (the whole
  2.26.0 tree holds the word once, in a C++ comment). `@treCorde` is kept as sugar for
  `@!unaCorda` by the same criterion applied the other way: measured in the Text style,
  the una corda release really does print "tre corde", while "Off" is never printed in
  any style. `@ped` was weighed and refused — LilyPond has no `\ped`, all three of these
  are pedals so `ped` would name a category while its siblings name mechanisms, and the
  default Bracket style prints no pedal word at all.
  ⚠️ Refusing to draw an unclosed ottava or pedal is the LANGUAGE's answer and not a
  port, and it is declared as such: LilyPond's `Ottava_spanner_engraver::finalize`
  neither warns nor suicides and `Piano_pedal_engraver::finalize` typesets to the end of
  the music — both draw, in silence (measured on 2.26.0: an unclosed `\ottava #1` over
  four bars draws 49.98 of dashed bracket and emits no warning).

- **An unterminated span is refused, not warned about.** `LYS4018` is one code with two
  severities now: `Unterminated` is an ERROR in both families, while a `@!` that closes
  nothing and a second start inside an open span stay warnings. The hole was the
  severity, not the rule — GRAMMAR.md had said an end is REQUIRED since the terminator
  landed, and nothing is drawn either way, so a book with a dropped `@!rit` passed
  `lysc check` and shipped with its `rit.` silently absent. Auto-terminating at the bar
  line was asked for and refused: a `rit.` normally spans the last two to four bars of a
  phrase, so ending it at its own bar would draw a plausible and WRONG length, in
  silence, in the common case — and it would take away the report that names the fix.
  LilyPond requires `\stopTextSpan` too. Of 1977 books swept, SVG, MIDI, MusicXML and
  LilyPond output move on none; `check` differs on 24, every one a past session's scratch
  probe, and every diff is the single word `warning:` becoming `error:`.

- **A part setting written beside a section's part cells is refused.** `clef` and
  `octave` there are `LYS1035`; `instrument` and `transpose` already reached `LYS0030`,
  whose message now names where a one-part setting goes. This adds symmetry rather than a
  rule: all four can be written in that position and only two of them spoke, because
  `clef` and `octave` are real music items elsewhere, so the parser's music arm took them
  and they became bare music belonging to no part. `clef` there did nothing at all;
  `octave` was worse than nothing — the resolved pitches did not move while the LilyPond
  twin's wrapper for the WHOLE part flipped from `\relative c'` to `\fixed c'`, which is
  a disagreement between readers rather than a no-op. The POSITION is the whole rule:
  `part m { section A { clef bass … } }` and `section A { clef bass c'4 … }` both engrave
  the clef correctly, and only a section that holds CELLS has nowhere to put a loose one.
  Reach is zero — eleven of 1954 books on disk change their `check` output and all eleven
  are scratch probes; no page, MIDI or MusicXML moves anywhere.

- **A `form` that plays a section declared only as a header is refused** (`LYS1036`).
  `section A { key g major }` as A's ONLY declaration, played by `form main { ~A ~B }`,
  had the two readers disagreeing: the page ARMED the header's key and carried it into
  B's bar (a header-only section engraves no bar, so the section boundary that would
  restore the score key never fires) while the LilyPond twin wrote no key at all. Neither
  reader is wrong so much as the spelling is. It is `LYS1005`'s sibling — a form playing a
  name no section declares is already `Undefined section` — and the question is asked of
  the NAME, not of one part, because `part fl { section A { … } }` beside a score that
  draws only `part m` is a correct book. An EMPTY `section A { }` is deliberately not a
  header: there is no directive for it to be only.

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

### New

- **A section reference carries octave marks.** `~B'`, `~B,` and `[1. B']` shift the
  relative frame THAT PLAY opens in, one octave per mark — the same spelling and the same
  meaning a phrase reference already carried. A section boundary reopens the frame at the
  part's anchor and that reset stays; this is the notation that lets a book say otherwise.
  The shift belongs to the occurrence, so `~B ~B'` is one section played at two octaves,
  the declaration never moves, and the reference after it is back at the anchor. All four
  readers — the page, the pitch resolver, MIDI and the LilyPond twin — were told
  separately.

- **`section ~A { … }` declares that the section prints no rehearsal letter.** The tilde
  keeps ONE meaning at both sites, "the other one than the default": a section carries a
  letter by default and a form reference's `~` hides it, while a section declared with `~`
  carries none and there the reference's `~` SHOWS it. The whole rule is one equality —
  `shown = (declaration hides) == (reference has ~)`. It exists because a section cut
  solely to carry a repeat edge should not be labelled, and "this is structure" is a
  property of the SECTION, so it is written once on the declaration instead of at every
  reference; the author's books hold 2309 bare references against 260 tilde ones. This
  was the last precondition for making `|:` form-only.

- **A form can spell a third volta ending.** `|: X | [1. A] :| [2. B] :| [3. C]` was
  writable in music and not in a form: the form's repeat block took ONE ending after its
  `:|` and stopped, so a third fell out of the block, warned `LYS6008` ("no repeat opens
  this ending") and engraved as a plain section reference. That was survivable only while
  the music spelling existed. 13 of the author's 326 books and one tracked book write a
  third or later ending.

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

- **A rehearsal letter that is written and not printed says where** (`LYS4019`). This
  family was silent for 200 sessions: a `@mark("A")` written inside a container that owns
  its own walk — an inline ending, a tuplet, a repeat, a cue — was dropped by the
  collector and nothing said so. 45 of one reader's books were missing 120 letters before
  anyone looked. The drop itself is fixed (see below); this is the decision that the
  family should answer the question the way an unpaired span does — **if it is not drawn,
  say where** — so that the next way to lose one cannot be silent either. A `@mark` inside
  `repeat unfold N` prints once.

- **A grace body says what it drops** (`LYS4020`). A grace body is parsed by the ordinary
  music-block parser, so it accepts everything a music block accepts, while the collector
  read a column's pitches and duration value and nothing else — dots, slur, beam and tie
  markers, `@staccato`, `@text`, `@f`, `@finger`, `@trill`, `@sustain`, `@rit` and
  `@cresc` were each dropped in silence, and a body made only of a chord, a rest or a
  tuplet drew NO grace at all. LilyPond 2.26.0 draws every one of them. Most of that list
  has since left it (below); what remains is reported at what was written, as a warning,
  because the report is "not drawn yet" rather than "do not write this". Nothing in the
  reader's 326 books and nothing in the 581 tracked books writes anything a grace body
  still drops.

- **An unclosed `form` repeat is one error at the `|:`, not five wrong ones and the rest
  of the file.** `form main { ~Body |: A }` used to report the form's own `}`, then
  `score`, `{`, `staff` and `}` as five things "a form cannot hold", and only then say
  `Expected 'RepeatEndBar', found 'EndOfFile'` — the item loop ran to end of file looking
  for a `:|` that was never coming, so the score block was consumed as stray form items
  and declared garbage, and four of the five errors were about perfectly good text. The
  loop stops at the form's own closing brace and the missing half is reported as
  `LYS4017`. Reported by a reader on a book whose author had moved a repeat's OPEN into
  the form and left its `:|` in the section's music.

- **An inline volta ending's own bars are counted.** The measure validator held a whole
  `[1. … ]` ending as ONE opaque zero-duration item, which nobody else does — the
  collector walks the ending's music in place, bar lines and all, and only overlays a
  bracket across the bars it occupies. So `[1. c'1 c'1 c'1 | ]` in 4/4 was silent, and
  the note value did not thread through the ending into the music after it.

- **Clicking a tie or a slur in the preview jumps to the character that wrote it.** No
  ordinary tie or slur carried a source offset at all: of the 56 bow-shaped `<path>`
  elements in the tracked snapshots exactly 6 had a `data-pos`, and all 6 were grace slurs
  that happen to fall inside their note's scope, so a caret on `~` lit the nearest
  PRECEDING address — the note. A `~` now cites its `~`, a slur cites its `(`, and the
  third bow family (a laissez-vibrer or repeat tie, which is drawn by an annotation rather
  than by a symbol of its own) cites the annotation that draws it.

- **The tree keeps post-events in the order they were typed, so every node stands where it
  says it stands.** A note's trailing post-events were read with LilyPond's order-free
  semantics but BUILT in the wrong order, so a node's recorded position could disagree
  with where its text is. The two orders of a post-event run now agree on their
  diagnostics as well.

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

- **A lyrics row clears the bottom string of the tab staff above it.** A reader reported
  lyrics printing INSIDE a tab staff, on the middle system of three while the other two
  looked right. The cause is one quantity — how tall is THIS staff — written in three
  places, two of which answered with the score's nominal four staff spaces however many
  lines the staff actually has.

- **A `rit.` cannot be ended by the next playing of itself, so a repeated section draws it
  the same both times.** A reader asked whether the length of their `@rit` was right: in
  their book the first covered six bars and the second covered one, from ONE written mark.
  The spanner was ended at "the next `rit.`/`accel.` on the same staff" over the marks of
  the PLAYED piece, so a section the form repeats contributed one instance that closed the
  other. The hairpin carried the same shape and is fixed with it — that one took a probe
  to see, because Lily#'s `@cresc` draws a WEDGE rather than the word, so grepping the SVG
  for "cresc." found nothing while the hairpin was there all along.

- **Both ends of a text spanner are the writer's.** The left bound was passed as a
  constant, so `c4 d e@rit f | g@!rit` drew from `c` rather than from `e` — the terminator
  was honoured and the start was not — and the bound padding is now spent where LilyPond
  spends it.

- **A tab staff prints none of the markup the notation staff beside it already carries,
  and the switch is `as numbers` vs `as full` rather than "is it a tab".** Reported by a
  reader on their own book, as "the staff, the chord names and the rit overlap": a tab
  drawn beside a notation staff of the same part was repeating markup the notation staff
  already carried. The same switch answers two more of their requests — a `@text` on a
  tab that has no notation staff beside it now appears, and an `@accent` on an
  `as numbers` tab does not.

- **A `rit.` above a system's top staff reserves the room it is drawn in.** The same
  reader's follow-up on the same book: "the render improved but is still incomplete — near
  the A2 section mark the rit and the lyric overlap; it looks like the gap between the 2nd
  and 3rd systems needs to be a little wider." That diagnosis was exactly right.

- **A rehearsal letter is built where its note is, so the containers stop swallowing it.**
  Reported on a chart whose letters A, B, C and D are all written and only A was printed:
  A stood in the body, and B, C and D all stood inside a second inline ending, where none
  of the three drew anything — no box, no letter, and no diagnostic. See `LYS4019` above
  for the report that now covers the next way to lose one.

- **A row leading the next system is spaced against that system's staff as LilyPond
  publishes it — outside-staff ink and all.** A leading row's chain closed on the next
  system's first spaceable staff by reading that staff's INSIDE-staff silhouette plus one
  hand-merged special case; LilyPond spaces a loose line against the axis group's skyline,
  which a placed outside-staff grob is part of.

- **The last block on a page is solved into the paper, not into the height the page was
  cropped to, and the crop is then sized from where the block is drawn.** A spring lands
  on its minimum exactly when the room it is given is short, and the room this chain was
  given was not a room at all. With the block solved into the paper its syllables sat up
  to (ideal − floor) below the height that had been computed for them; the page's bottom
  white shrank by exactly that much, which could never clip but was not what LilyPond
  publishes either.

- **A grace body is engraved, heard and exported.** The body is now walked by the
  ordinary walker rather than read for a bare note's pitch and duration, and everything
  that walk reaches comes with it: a CHORD is one column with N heads (through the same
  chord and accidental rules a full-size chord uses, read out of the grace's own fonts), a
  REST is a column with no head and the beam covers the leading run of heads, a DOT is
  drawn and clears the flag only where the flag is, a PHRASE reference expands, and a
  TUPLET's notes are engraved, heard and exported — only its bracket and its number are
  still dropped. The whole-tree sweep that closed the walk shows all 2007 books on disk
  producing byte-identical SVG, MIDI, MusicXML, LilyPond and `check` output, so the page
  does not move; what it exposed on the way were five real defects it then fixed, among
  them a grace in the second voice displacing the FIRST voice's noteheads and a grace
  cutting a beamed run it should have been spanned by.

### MIDI, MusicXML and the LilyPond twin

- **A form's `:|*3` plays three times.** The MIDI exporter read neither the written play
  count nor the form walk's — it was `max(2, endings)` — so `form main { |: ~X :|*3 }`
  sounded twice while the same body written inline sounded three times (16 note-ons
  against 24). With repeat structure legal only in a form, the form is now the only place
  an explicit count can be written, so this is the only behaviour that quantity has.

- **A section boundary restores the score METER in every reader.** A section that states
  no `time` of its own opens at the SCORE meter, so a mid-section change cannot leak into
  the next section. The page obeyed that rule and the measure validator agreed with it;
  three of the five readers did not, and each of the three exporters had to be told
  separately.

- **A standalone section header is a header wherever it is written.** The same book with
  its header moved across the part produced two different LilyPond twins — before the
  part, `\key g \major c'4 c c c |`; after it, `\key g \major \key g \major` and not one
  note. The page engraved the four notes either way, `--pitches` resolved them either way,
  and `check` said "No errors found": the two spellings differed by LINE ORDER alone. The
  exporter was asking its own question about what declares a section instead of the shared
  one.

- **A plain repeat imported from MusicXML comes back as sections plus a form.** The
  importer factored first and second endings into named sections and a form and left
  everything else as one flat section whose measures were joined by bar lines — which
  writes `|:`, `:|` and `:|:` into the music, and is therefore a book Lily# now refuses
  (`LYS1034`). An imported score compiles.

- **A phrase named in a grace body is heard and exported**, not only engraved: the one
  statement had four readers and only two of them were listening.

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
