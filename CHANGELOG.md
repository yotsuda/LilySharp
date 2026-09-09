# Changelog

Notable changes to Lily# are recorded here, newest first. Release notes are taken
from this file: the topmost section is the version being tagged, and the release
workflow attaches that section to the GitHub Release verbatim.

## 0.7.0

### Language

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

- **`marks stacked | beside` arranges a section label and the tempo mark at the same bar.**
  `stacked` is the default and LilyPond's: the boxed label break-aligns to the key/clef
  column, the metronome mark to the meter column, and where their inks meet the label stacks
  over the tempo. `beside` is the chart's one line — the label's box at the line-start edge
  with the tempo to its right, the digits on the label's baseline ("[Chorus] ♩ = 132"); the
  pair is reserved and moved as one, so a chord symbol or a high note under either lifts
  both. A mid-line label stays centred on its bar and a mid-measure `tempo` keeps its note
  column either way. Written at the top level it is the file's default; `marks beside`
  inside a `score { }` body is that score's own, the two tiers `fonts` / `paper` take. The
  editor completes and colours the two words; the `.ly` twin has no spelling for `beside`
  and warns (LilyPond has no such pair). A book that writes neither is unchanged.

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
  — a bare word after a key is now the next key, so the old `chordName serif` opens an empty
  `serif` entry and is refused with the `as` form to write (no other book wrote it: 0 of the
  3493 `.lys` on disk hold a `fonts` block). `step`/`size` on a generic family is refused
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

- **A pickup can be declared in the music, mid-piece.** `partial` says "the bar it stands in
  is this long", and it is taken wherever a bar is: a section directive as before, and now a
  part's or voice's music at the bar's start — `… | partial 2. r2. | …` closes a three-beat bar
  and the meter resumes after it. It is per part, like a mid-music `time`: every part sharing
  the bar writes it, and a part that omits it keeps a full bar, which the cross-part check
  reports. (LilyPond's `\partial` moves one clock for all staves; Lily# keeps a bar length per
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

### Fixed

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

### Engraving

- **A line's first or last syllable overhangs its bar line, as LilyPond's does.** Lily# used
  to hold a lyric line's first syllable clear of the bar line before it (and its last clear of
  the bar after it) by half the word plus 0.4 staff spaces — a reservation LilyPond does not
  have: a syllable and a bar line never meet in LilyPond's spacing, only the next syllable
  binds. Measured on the twins: the bar that opens "Twin- kle twin- kle" after a rest bar stood
  19.67 staff spaces wide against LilyPond's 18.147 (+1.52, the reservation's deficit to the
  digit), the bar where a second verse begins +1.6. A lead sheet keeps its clearance: there the
  grid's bar lines run through the lyric band (user decision, 2026-08-20).
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

### Diagnostics

- **LYS1033 says which outputs are not cut.** The expansion budget is the picture's alone: it sits
  where the line breaker would fail anyway, so it is a limit of what can be drawn, not of the
  book. `lysc midi`, `lysc xml` and `lysc ly` write the whole expansion, and the warning now
  says so instead of leaving a reader to discover a million-note MIDI after "the picture is
  truncated".
- **LYS1005 names the glued custom text.** `form main { A _ "shown" }` is a reference to a section
  named `_` with a display label (a legal name, so the space cannot be forgiven); the custom
  text is the glued `_"shown"`. When nothing declares `_`, the error says to glue the quote.

### MIDI, MusicXML and the LilyPond twin

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
