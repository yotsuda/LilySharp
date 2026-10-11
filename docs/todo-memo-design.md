# The preview's `@todo` memos — where they go and why

The editor's red ink over the finished page: a ring round the marked head, rest or chord,
and for a memo a line of small red text, underlined, with a leader from the ring to the
underline. LILYSHARP-OWN throughout — LilyPond has no marks to place. The owner's decisions
of 2026-10-10 and 2026-10-11 fixed the look (sessions 891–898, `HANDOFF-ARCHIVE.md`); this
page records the placing, which sessions 896–899 built up and session 899 settled.

## 1. What moves, and where

- **An export moves nothing and draws nothing.** PDF, PNG and SVG are the page the book would
  have without its memos (`TodoCalloutTests.AMark_MovesNothingOnThePage`).
- **The preview's layout reserves each memo's place** (`LayoutOptions.TodoBands`, set by the
  interactive render options — `SvgGenerator.PaperBaseOf`, and the same base in
  `IncrementalCompiler` so the session's picture is the full render's). `TodoMemoBand.Boxes`
  puts each memo's box where the drawer would set it — off its note, on the side whose ink
  under the box is lower, on the system's paging silhouette at that X, stacked over the memos
  estimated before it — and the boxes join that silhouette as X-aware mark boxes
  (`AugmentSkylinesForPaging`, after the marks, chord names, dynamics and lyrics are in it)
  and the system's scalar extents (`LayoutEngine.Prelim.cs`), which the page's top spring and
  the breaker read. So a memo that fits in the room the page has anyway — between the
  lyrics of the system above and a bar with nothing over it, in the margin — moves nothing;
  the inter-system floor grows only where the system above really reaches the box, and a
  page's first system moves only where a memo stands higher than its own topmost ink. (A
  full-width band over every marked system, session 899's first spelling, widened
  `Untitled-2.lys`'s systems for nothing — owner's report 2026-10-11.) The owner's decision
  of 2026-10-10 allowed the page's other elements to move for a memo "so long as the result
  is tidy"; this is the one way they move.
- **The memos are set at render time**, where the page's ink is clear
  (`SharedRenderer.DrawTodoMarks`, pass 2). The page's device context is wrapped in an
  `InkRecorder` while it is drawn, so every glyph, text, line and curve is a box the memos can
  ask about; the band only makes sure such a place exists near the note.

## 2. One memo's place

- Off the note's column by one fixed distance (`TodoMemoStyle.Offset`) to the side with
  more room to the system's end — or the other side, when the arrangement (§3) says so.
  Kept on the paper: the page's margins are room too.
- From the system's outermost line (or the ring, where that stands further out) plus a gap,
  the memo climbs by half-spaces until its box — text, underline and pen, with a space of air
  above and below and less sideways — meets no recorded ink and no memo set before it.
- Its leader is not recorded (a long slanted box would fence off the margin). Instead the
  memos set so far and their leaders are kept as boxes and segments, and a place is *clear*
  only when no earlier leader passes through the memo's box and its own leader passes through
  no earlier memo's words (`SegmentMeetsBox`, Liang–Barsky). Two places are found up the climb,
  the first clear of the ink and the first clear of the leaders too, and the cheaper is taken:
  a crossing costs `TodoMemoStyle.LeaderCrossing` (4) spaces of climbing.
- A memo that reaches the page's edge stays there, over whatever is in the way, rather than
  be cut off.

## 3. A system's arrangement

The memos above one system (below, for `.down`) are arranged together:

- in order of width, the short ones first (left to right among equals): a short memo stays
  by its note on the first row, a long one climbs or crosses round it;
- every combination of sides is tried (up to eight memos a side; beyond that each takes the
  cheaper side as it comes), the memos settled in that order, each clear of the ink, of the
  memos set before it in the attempt and of their leaders;
- the arrangement with the least total cost is drawn — the climbs, a crossing for every
  leader through another's words or box across another's leader, and two spaces for every
  memo on its narrower side, so memos still line up on the roomier side and one crosses over
  only where that buys something.

Measured (session 899, Lab `sessions/p899/probe/timing.txt`): the search over six memos on
one system costs under a millisecond of the preview's render.

## 4. What is still open

- A long memo on a note *between* two short ones, where the first row is taken on both
  sides, is placed by cost like any other; the probes in Lab `sessions/p899/probe` show no
  crossing left in the cases tried, but the search varies the sides only, not the order.
- ~~**Under the title**~~ FIXED 2026-10-11 (p902). A memo on the first system climbed OVER
  the title, its leader across the letters (Lab `sessions/p902/probe` `notetodo`, a note's
  memo; `chordtodo`, a chords row's). Three mismatches between the band and the drawer, all
  needed (each one's fix alone left a probe failing): the page's ink recorder floored every
  text's box at 0.22 em under its baseline, so a title without descenders fenced off the room
  under it (now only a text the metrics cannot measure is floored); the band's box stopped at
  the memo's own top, without the Air/2 the drawer keeps round it (the header's padding, 0.5,
  left the drawer's first place blocked by a hair, and its next step was over the title); and
  the band started the box on the ink beside the note, not at the ring's top where the drawer
  starts it (now read off the ring's own box, `SharedRenderer.TodoInkBox`, never the stem).
  Guard: `TodoCalloutTests.AMemo_UnderTheTitle_StaysUnderIt`.
- A memo is never shortened; a very long memo spans the margin (undecided since session 891).
- `lysc png --todo` would draw the preview's picture as pixels; `PngDocumentOptions.EditorMarks`
  is the door, not yet opened in the CLI.

## 5. A mark on a chord row's symbol (owner's decision 2026-10-11)

The owner asked whether `@todo` could mark more than notes, and decided to start with the
symbols of a `chords` row — the doubt a transcriber meets most ("Am7 or C?", a symbol the OMR
could not read), and the one place a mark cannot borrow a nearby note: a chord chart with no
staff has none.

- **Spelling.** The mark is GLUED to the entry, after its symbol and its shapes, as on a note:
  `G@todo`, `Am7@todo("check")`, `F(133211)@todo(o12 "barre?")`, and `r@todo("…")` on a
  no-chord slot. `.down` puts the memo under the system, as on a note. A `.` slot extension
  takes no mark (it is the chord before it, held).
- **Only `@todo`.** A chord row's entry takes no other annotation; any other `@name` there is
  an error naming the one it takes (the parser keeps the tokens, as it does every stray).
- **Reports.** The same LYS4026 as on a note: the squiggle under the memo, Ctrl+. resolving it,
  a key for a tool's side file, "Resolve all" across notes and chords alike.
- **The preview.** The ring goes round the symbol's ink (the same pieces the row reserved and
  drew, ChordNameGlyphRun); the memo is set like a note's — over the system, climbing clear of
  the page's ink. Exports draw nothing, as for a note.
- **Not yet.** The layout's reservation of the memos' room (`TodoMemoBand`, §1) reads the staves'
  items only, so a chord-row memo has no reserved row: it is placed where the page has room,
  and on a crowded page it may stand close to the system above. Lyrics, directives and the form
  were judged after chords (lyrics next; the others borrow a nearby note well enough).

## 5. A mark on a chord symbol (owner's decision 2026-10-11)

A chords row has no notes, and a chord chart (a score of chord rows alone) has none at all, so
a doubt about a chord — by ear, or from the OMR reader — had nowhere to go. The owner asked
whether `@todo` could reach beyond notes; of the places weighed (chord symbols, lyrics,
mid-music `key`/`time`/`tempo`, form items, headers) the chord symbol is the one with no
workaround and the most doubts, and the owner said go.

- **Spelling.** The mark is GLUED to the entry, after its shape group if it writes one:
  `G@todo`, `Am7@todo("Am7 or C?")`, `F(133211)@todo(o12 "…")`, `r@todo` on a no-chord slot,
  `.down` as on a note. The run reads like a note's marks (the same parser routine,
  `ParseArticulations`); a chords row takes no other annotation, and one written there is an
  error naming `@todo` as the only one.
- **Reports.** Nothing new: the mark is the same annotation node, so LYS4026 (underlining the
  memo), "Resolve this TODO", "Resolve all" and the OMR candidates work on it unchanged.
- **The tree.** The annotation is a child NODE of the entry, after its tokens. Everything that
  reads an entry's symbol or shapes reads its direct TOKENS (`SymbolText`, `ShapeWords`,
  `NoteStepper`'s site), so none of them sees the mark — the stepping key inserts a shape
  before it (`G@todo` → `G(320003)@todo`).
- **The preview.** The ring goes round the symbol's ink (the box the row reserved for it), and
  the memo is placed by §2–§3 as a note's is. Nothing is drawn in an export.
- **Room.** The layout reserves a chord memo's room as a note's (`TodoMemoBand.Boxes` reads the
  rows' symbols on their timing columns beside the staves' items).
