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
- A memo is never shortened; a very long memo spans the margin (undecided since session 891).
- `lysc png --todo` would draw the preview's picture as pixels; `PngDocumentOptions.EditorMarks`
  is the door, not yet opened in the CLI.
