# Markdown text in a score: the design

The owner's idea and decisions of 2026-11-12 (session 1226). **Built that day: section 8's
step 1** — the fence, and paragraphs with bold and italic under the title and after the last
system (section 7.1). Section 6 is what LilyPond 2.26.0 was measured to do; section 7 is what
Lily# had and lacked before it.

## 1. The idea

A score has nowhere to put a paragraph: the file writes `title`, `subtitle`, `composer`, `poet`,
and `@text("…")` on a note. Performance notes, practice instructions, an arranger's remarks, a
programme note and a lesson's explanation have no place. They get one, written as Markdown in a
fence — the mirror of the ```` ```lys ```` block a Markdown file already takes.

The line that is kept: a document that is mostly text with music examples in it is written as
Markdown with ```` ```lys ```` blocks. The text inside a `.lys` is what accompanies a score. No
table of contents, no columns, no numbered figures.

## 2. The grammar

````
title "Morning Song"

```md start
This piece is for **four voices**. Keep it slow.

- first time *mf*
- second time *p*
```

section B {
  ```md
  The key changes here.
  ```
  piano { c1 | f1 | }
}

```md end
![fingering](hand.png)
```
````

| Written | Opening line | Printed |
|---|---|---|
| at the top level | ```` ```md start ````, or ```` ```md ```` with no place word | under the title (and the poet / composer line), above the first system |
| at the top level | ```` ```md end ```` | under the last system |
| directly in a section | ```` ```md ```` | above the system that section begins |

- The word is `md`; `markdown` means the same.
- `start` names the place a bare ```` ```md ```` already takes, and changes nothing else: the
  page, the exports and the LilyPond twin are the same with or without it (section 2.4).
- The fence closes with a line that begins ```` ``` ````. Everything between is Markdown: `//`
  and `}` mean nothing there. A fence of four backticks holds a text that writes three
  (Markdown's own rule).
- The CLOSING fence's indentation is taken off every line, so a fence may be indented in a
  section (a C# raw string's rule: where the closing fence stands is the text's margin) — and
  beyond it, the indentation every written line of the text shares (owner, 2026-11-14: a
  text indented four spaces in a score, under a closing fence at the line's start, was one
  Markdown code block, and a title fence said "not inline text"). A page text draws no code,
  so indentation the whole text shares is the file's layout; a line indented four or more
  beyond the text around it is still Markdown's code block, and the warning says so in those
  words.
- Two fences for one place print one after the other, in written order — each a block with
  its own shape (section 5.3).
- The backtick is new to the lexer (nothing uses it today).

**A text is bound to music, never to a system.** "Between the second and third systems" moves
when the paper or the size changes; a section does not. A section that carries a text begins a
system: the line is broken there to make the place (owner's decision).

### 2.1 A section the form prints more than once

With nothing said, the text prints at the section's **first printing** only: an explanation is
read once, and `form { A B A }` printing one paragraph twice reads as a mistake.

| Opening line | Printed at |
|---|---|
| ```` ```md ```` | the section's first printing |
| ```` ```md 2 ```` | its second printing (`1,3` and `2-3` as a `verse` writes them) |
| ```` ```md all ```` | every printing |

PRINTINGS are counted, not playings: `|: A :|` prints once, so its text prints once, and
```` ```md 2 ```` there has no place and is warned about.

This is the opposite default to lyrics (words with no `verse` are sung every time): words to
sing are needed each time, a note to read is not.

Rejected: writing the text inside `form { }` between the section names. The place is exact, but
the form grows long and the text leaves the music it is about.

### 2.2 In a score

A text belongs to the piece, as its title does: it prints in every score with nothing written
in `score { }` (owner's decision). A score replaces it by `singer`'s rule — the top level is
every score's default, a fence inside a score replaces the one for the same place in that score:

````
score parts {
  ```md
  A shorter note for the parts.
  ```
  staff violin
}
score clean {
  ```md
  ```
  staff piano                 // an empty fence: nothing here in this score
}
````

`md` (or `md start`) and `md end` are replaced separately. A section's text is not replaced from a score; a
layout switch takes every text off a score (its word is open).

### 2.3 The shape: `xMargin`, `yMargin`, `center`, `box`

Owner-approved spelling, 2026-11-13. More words on the opening line, after `md` and the
optional `start` or `end`, in any order, each once:

````
```md box
```md end center
```md xMargin 20mm
```md xMargin 20mm 20mm yMargin 3 1 box
````

| Word | The text |
|---|---|
| `xMargin LEFT [RIGHT]` | white at its left and right, INSIDE the line width: set that much narrower, beginning that much further right. One length is both sides. With none written — the same as `xMargin 0` — the text, boxed or not, runs from the page's left margin to its right one; `xMargin 15mm` puts it on a default staff (section 5.5) |
| `yMargin TOP [BOTTOM]` | white above and below it, in place of the default (1.0 each way). One length is both. `yMargin 0` is LilyPond's own spacing (section 5.1) |
| `center` | each line centred in the text's width |
| `box` | a ruled frame around the whole text, the text in its optical middle; with no `xMargin` the frame runs from margin to margin, as a text with no frame does (section 5.5) |

**The two margins are a pair** (owner's decision 2026-11-13). The side white was first the
word `indent`; the owner, when the white above and below came up: "`space` is the y
margin and `indent` the x margin; the words do not match, which is hard to follow" — and
chose `xMargin` / `yMargin`, named as `paper { leftMargin … }` names its own. `indent` was
never released and is no alias: on a fence's line it is an unknown word (LYS4057) whose
sentence says to write `xMargin`. (`paper { indent … }`, the first system's indent, is
another thing and is as it was.)

- A length is written as `paper { }` writes one — the number and its unit glued, `20mm`,
  `2cm`, `0.5in`, or a bare number of staff spaces — and goes through the same conversion
  (`PaperPlanReader.ToStaffSpaces`; the list of units is one list). There is no `pt`: the
  language has none.
- The three combine. The text under the title and the `end` text each read their own opening
  line — each FENCE does (section 5.3) — at the top level and in a score's fence; a score's
  fence brings its own words with its text (it does not inherit the file's shape).
- Two fences for one place are two BLOCKS, each set as its own opening line says (section 5.3).
- Errors, all LYS4057 on the word: a word that is none of these (the message lists them), a
  word twice, a margin with no length or with three, a negative length, a unit that is not
  `mm` / `cm` / `in`, an `xMargin` that leaves the text less than 10 staff spaces — judged on
  the paper the fence stands under (the file's `paper { }`, or the score's own when the fence
  is in a score that names one); on any other paper the side margins give way in proportion
  and the text keeps the 10 — and a `yMargin` over 50 staff spaces (section 5.1: the limit).
- A section's text takes none of these: its opening line takes a printing, as before.

**Decided 2026-11-13 (owner).** Margins, centring and a ruled frame are in. A frame IMAGE (a
decorative border from a picture) was judged excessive and is not planned: for a text of any
length it needs nine-slice scaling — the corners kept, the edges stretched or tiled — and that
is a small layout engine for an ornament.

### 2.4 The place under the title: `start` (owner's decision 2026-11-14)

The text under the title, above the first system, had no word: it was the fence with no place
word, while the text under the last system was ```` ```md end ````. It is now ```` ```md start ````
— `end`'s counterpart. The owner, having weighed `start` against `preface` (and `preface`
against renaming `end` to match): **"start と end で進めて"**. Why position names won: what
stands there varies — performance notes, a dedication, a boxed instruction — and `preface`
names one kind of it; a position names the place whatever is put there, and pairs with `end`,
which is already a position.

- A bare ```` ```md ```` keeps meaning exactly the same; no file changes. `start` is the same
  place, and draws the same to the byte (`MarkdownTextShapeTests.Start_IsTheBareFencesPlace_AndDrawsExactlyAsIt`).
- `start` takes the shape words as a bare fence does (```` ```md start box center ````).
- One place per fence: `start` with `end`, or a second `start`, is LYS4057 ("'end' is a second
  place for one text"); `start` with a header place is LYS4057 naming what the header line takes
  ("'start' does not go with 'title' …").
- A score's ```` ```md start ```` replaces the file's text under the title, as a bare one does.
- The LilyPond twin and the exports: nothing changes (it is the bare fence's place).
- The docs say what the place is — "under the title, above the first system" — not "the top
  of the page" (a header stands above it).

**In the editor.** Completion on an empty line (and after a backtick) offers ONE fence item,
```` ```md ```` — the ```` ```md end ```` item is gone. Taking it writes the opening line
```` ```md ```` and a space, an empty body line, and the closing fence, leaves the caret after
`md ` on the opening line (the snippet's `$1`; the body is `$0`, a Tab away) and opens the list
again there (`editor.action.triggerSuggest`), on `start`, `end`, `title`, `subtitle`,
`composer`, `poet`, `arranger`, `copyright`, then `box`, `center`, `xMargin`, `yMargin`. A
place word taken from that list moves the caret down onto the empty body line (the item's
command is VS Code's `cursorDown`, which keeps the column or stops at the shorter line's end —
the body line's indentation in a score); a shape word leaves it on the line for more words, and
`xMargin` / `yMargin` open their lengths as before. Only where the line under the opening one
is there and empty does a place word move the caret. A writer who moves on with no word leaves
a bare ```` ```md ````. Hover on `start` (and on the other opening-line words) says what it does.
The TextMate rule colours `start` as `end`.

## 3. What is drawn

The text is read as CommonMark, whole. Drawn:

| Drawn | Read, not drawn — said where it is written |
|---|---|
| paragraphs, hard line breaks | tables |
| bold, italic | code blocks and code spans (later: to show Lily# source) |
| headings, two levels | HTML other than `<img>` |
| bullet and numbered lists | block quotes, rules (open: draw them if asked) |
| links (the words; a link in PDF and SVG) | |
| images | |

Nothing is dropped in silence.

**As built (2026-11-13, section 5.2):** paragraphs, hard breaks, bold, italic, headings and
lists are drawn. Still LYS4058: tables, code blocks, block quotes (with whatever stands in
them — a list in a quote is said once, as the quote), rules, HTML, images; a code span's words
are drawn and the span is said. A link's words are drawn as they have been since step 1, with
no link in the PDF or SVG and **no** LYS4058: the address is the one thing still dropped
without a word (open — the link itself was not nearly free, and is left for a later step).

## 4. Images

- `![alt](hand.png)`. Its own size, or the text's width when wider. A size is written as
  `<img src="hand.png" width="40mm">`.
- A path relative to the `.lys`, nothing else: no fetch from the network (the output must be the
  same tomorrow, and a score must not reach out).
- PNG, JPEG, SVG. A file that is not there is a warning, and its alt text is drawn in a frame.
- In SVG, PDF and PNG. Not in MusicXML or MIDI.

## 5. The look

- A text role of its own in the fonts block (`text`); by default the piece's text font at the
  size of the poet / composer line (section 6: that is LilyPond's markup size).
- From the page's left margin to its right one, ragged right, unless its opening line says
  otherwise (sections 5.1 and 5.5 — for a day, 2026-11-13, it stood on the staff instead).
  Lines break by themselves: between words, and between characters in Japanese, Chinese and
  Korean.
- A long text runs on to the next page between its lines.
- Nothing here touches playback, MIDI or a voice.

### 5.1 Margins, centred, boxed (built 2026-11-13)

- **`xMargin`**: the text's left edge is the page's left margin plus the left length; its
  width is the line width less both. The lines are broken to that width. **With no `xMargin`
  written the text runs from margin to margin**, boxed or not — `xMargin 0` is the same text
  (section 5.5).
- **`yMargin`, and the default.** Every text, boxed or not, keeps **1.0 of white above and
  1.0 below** (`ProseBand.DefaultMargin`, LILYSHARP-OWN) beyond LilyPond's own spacing of a
  markup; `yMargin T [B]` replaces it for that text, and `yMargin 0` is LilyPond's spacing
  to the digit. The owner, 2026-11-13: "the white above and below the md text is tight;
  widen it a little, or make it settable" — both. It began the same day as a boxed text's
  alone (below); the two are ONE margin, so a box's frame stands where it stood.
  - **It is extent, not distance.** The margin is in the band's `Up` and `Down`
    (`ProseBand.MarginTop` / `MarginBottom` say how much), so the page's springs and the
    breaker's priced depth see it, and the twin writes it as the first line's and the last
    line's `\with-dimension #Y`. It therefore moves the text exactly where the chain's
    PADDING rules — under the composer's line (2.95 → 3.95), over a system whose clef or
    mark reaches up (5.09 → 6.09) — and is swallowed where a BASIC-DISTANCE already holds
    more: the 12.0 from the last staff's middle line to an `end` text's first baseline
    stays 12.0 at the default, in both engines, and grows only when the margin outgrows it
    (`yMargin 8`: 14.05). ⚠️ So "1.0 further everywhere" is true under the title and NOT
    after the last system. A margin that always moves the text would be a shift of the
    band's reference point (in the twin: an empty line before the first), and a boxed
    `end` text would then move too — **open, for the owner**.
  - **The limit: 50 staff spaces** (`ProseBand.MaximumMargin`, about 88 mm at the default
    staff), an error over it (LYS4057) and a clamp in the band. MEASURED 2026-11-13 with
    the limit lifted (`md/margins/limit`): `yMargin 100`, under the title or on an `end`
    text, drew an eight-bar piece's page with its one system pushed off the paper under
    "page 1 is over-full by 68.5 staff spaces" — a text is not broken over a page and the
    music does not leave it for the next. At 50 above and below the page is whole and
    nothing warns.
- **`center`**: each line stands in the middle of the text's width — a paragraph's lines and a
  line ended by hand alike. The breaking is the ragged text's; a line wider than the text (one
  long word) keeps the left edge and runs over as it always has. **A heading is centred as a
  paragraph is; a list keeps its hanging shape from the text's left edge and is not centred**
  (built 2026-11-13 with section 5.2; LILYSHARP-OWN — a centred hanging list cannot be read —
  **for the owner to confirm**).
- **`box`**: LilyPond's `\box`. The rule is 0.1 thick (`thickness` 1 × the layout's
  line-thickness) and is drawn OUTSIDE the extent widened by the padding (`scm/stencil.scm`
  box-stencil). The padding is **1.0** on all four sides (LILYSHARP-OWN; LilyPond's
  `box-padding` default is 0.2), around the lines' BOXES — 2.0 over the first baseline, 1.0
  under the last — not around the ink; **and the text is then set in the frame's optical
  middle** (section 5.5: 3.314 inside over the first baseline, 1.686 under the last). The
  frame's outer edge stands on the side margins' edges — with no `xMargin`, the line width's
  edges (section 5.5) — and the text inside is inset 1.1 a side and broken
  to the narrower width. The band (`ProseBand.Up` / `Down`) reaches **the text's margin past
  the frame's outer edges** above and below (`yMargin`, or the default 1.0 — which was first
  this frame's alone, `BoxMargin`), so the page's springs
  and the page breaker's priced depth see the frame and that white: a line of text is spaced
  from its neighbours by its box of letters, which reads as room because letters do not fill
  it, and a rule drawn on the box's edge does. Without the margin the composer's line stood
  0.95 over the rule and a rehearsal mark as near under it — LilyPond's own `\box` in a markup
  list does the same (4.0705 from the composer's baseline to the boxed first baseline). The
  owner on seeing it, 2026-11-13: "the white above and below the box looks too tight". The
  twin writes the line's taller extent as `\with-dimension #Y` around the box.
- Least width: 10 staff spaces (`ProseBand.MinimumWidth`, LILYSHARP-OWN).

MEASURED 2026-11-13, LilyPond 2.26.0, `LilySharp-Lab` `sessions/p1226/md/fencelayout`.

The padding (`box-pad.ly`: a two-line block given its line boxes, `\box` at four paddings; the
frame's outer left edge from the text and its outer height):

| `box-padding` | Left of the text | Height | Reads as |
|---|---|---|---|
| 0.2 (default) | 0.3 | 6.6 | the rule touches the eye: the first letter is a hair off it |
| 0.5 | 0.6 | 7.2 | still tight at the sides |
| **1.0** | 1.1 | 8.2 | about half the text's em at the sides, a line's own white above and below |
| 1.5 | 1.6 | 9.2 | loose: the frame reads as a panel, not a rule round a note |
| 0.2 on the bare ink | 0.3 | 5.6 | LilyPond's own picture: the rule on the ascenders |

The twin against the page (`showcase.lys`, its `lysc ly` twins engraved; staff spaces, y down).
⚠️ Measured BEFORE section 5.5: every boxed baseline now stands 0.314 lower in its frame
(3.414 / 1.786 outside over and under one line); section 5.5 has today's numbers.

| What | Lily# | LilyPond |
|---|---|---|
| Boxed text under the title: frame's outer left edge, width | 8.54, 102.43 | 8.5358, 102.4299 |
| …its first line's left edge | 9.64 | 9.6358 |
| …frame over the first baseline, under the last | 3.10, 2.10 | 3.1000, 2.1000 |
| …composer's baseline to the first baseline | 4.05 | 4.0705 (the unboxed 2.95 / 2.9705 and 1.1; measured before the margin — with it the page has 5.05) |
| `end xMargin 30mm box`: first line's left edge | 26.71 | 26.7075 |
| …the last staff's middle line to its first baseline | 12.00 | 12.0000 |
| …line to line, paragraph to paragraph | 3.00, 4.50 | 3.0000, 4.5000 |
| `center xMargin 40mm 40mm`: the text's width | 56.91 | `line-width` 56.9055 |
| …a line's left edge ("For my teacher,") | 52.14 | 52.8026 |
| …last baseline to the staff's top line | 5.09 | 4.93 (section 7.1: the book's own, with or without a text; today's `yMargin 0`) |

The margin above and below (`md/margins`: `p-*.lys`, their `lysc ly` twins engraved in
`lp/`; MEASURED 2026-11-13, LilyPond 2.26.0, staff spaces, y down):

| What | Lily# | LilyPond |
|---|---|---|
| Plain text under the title, default: composer's baseline → first line | 3.95 | 3.9705 |
| …last baseline → the staff's top line | 6.09 | 5.9312 |
| `yMargin 0`: the same two | 2.95, 5.09 | 2.9705, 4.9312 |
| `yMargin 3 1`: the same two | 5.95, 6.09 | 5.9705, 5.9312 |
| Text that begins with a `#` heading, default: composer → heading | 4.47 | 4.4903 |
| …heading → paragraph, paragraph → staff's top line | 4.01, 6.09 | 4.0099, 5.9312 |
| Text that ends with a list, default: item → item, last item → staff's top line | 3.00, 6.09 | 3.0000, 5.9312 |
| `md end`, default: last staff's middle line → first baseline | 12.00 | 12.0000 |
| `md end box`, default: the same (the picture of before the margin) | 12.00 | 12.0000 |
| `md end yMargin 8`: the same | 14.05 | 14.0450 |

(The 0.16 between the engines under the text is the book's own 5.09 / 4.93, at every
margin.)

The twin's forms:

- yMargin: `\with-dimension #Y #'(-(DOWN+BOTTOM) . UP+TOP)` on the list's first line (the
  top) and its last (the bottom) — beside the white a paragraph or a heading already writes
  there; nothing at `yMargin 0`. In a box, the boxed line's own extent (below).
- xMargin: `\concat { \hspace #LEFT … }` a line of the `\markuplist \column-lines`; with
  none written (or `xMargin 0`) the bare line.
- center: `\override #'(line-width . WIDTH) \fill-line { … }` — one markup in a `\fill-line`
  is centred — so LilyPond centres by ITS measure of the words. The centred line's left edge
  therefore differs by half the known difference of the two engines' text widths (section
  7.1, open), and its middle does not.
- box: one line of the list, `\concat { \hspace #TEXT-LEFT \translate #'(0 . -SHIFT)
  \override #'(box-padding . 1) \box \with-dimension #X #'(0 . WIDTH) \with-dimension #Y
  #'(-(DOWN−SHIFT) . 2+SHIFT) \column { … } }` (the shift: section 5.5), the white
  between paragraphs a `\vspace #0.5`. The column is given the page's extent, so the frame
  stands where the page rules it whatever LilyPond measures the words. ⚠️ The `\hspace` is to
  the TEXT's left edge: `\concat` sets the box's origin, not its left extent, after the white
  (first written to the frame's edge, the whole box stood 1.1 left of the margin).
- Found on the way: the twin of a score's OWN fence wrote the file's text (its page model was
  collected without the score's fences). Mended with this.

### 5.3 Blocks: two fences for one place (built 2026-11-13, grammar evaluation 4)

The choral writer wanted a boxed performance note AND a centred dedication under the title,
and met the rule of section 2.3 as first built: "two fences for one place are one text, set
as the first says; a shape on the second is an error". The rule is gone.

- **A place is a sequence of blocks.** Each fence for the place is a block, set as its own
  opening line says (`box`, `center`, `xMargin`, `yMargin`); the blocks stand in written
  order. `Model.ScoreTexts.Blocks`, `ProseBand.Stack` (the band's `Later`, read through
  `Blocks`), `SharedRenderer.DrawProse` block by block.
- **Stacked extent to extent** (LILYSHARP-OWN): the next block's reference point stands the
  first one's `Down` and its own `Up` lower, so between two blocks stand the upper one's white
  below and the lower one's white above (1.0 + 1.0 at the default; `yMargin` on either sets
  its side). To the page the stack is ONE band — `Up` the first block's, `Down` the whole
  stack's — so the springs and the breaker's priced depth are as for one text.
- **Plain fences are one text, as before**: a fence with no shape word after a block with
  none is that block's next paragraph (half a skip of white, not two margins) — two plain
  fences are to the digit what they were. A plain fence after a SHAPED block is a plain block
  under it (for the day the shapes had existed it was a paragraph inside the first one's
  shape — changed with this; a text that wants two paragraphs in one box writes them in one
  fence).
- A score's first fence for a place replaces the file's whole sequence for that place; its
  later ones follow it.
- **The twin** writes every block into the ONE `\markuplist \column-lines`, a boxed block as
  its one line, a plain block's lines each a line: the list stacks its lines extent to
  extent, and each block's first and last line already carry its margins as their extent.

MEASURED 2026-11-13, LilyPond 2.26.0 (`LilySharp-Lab` `sessions/p1226/md/eval4/fixA/i2`,
`r02.lys` — `md box` with a heading and an item, then `md center` with one line — and its
`lysc ly` twin; staff spaces, y down):

| What | Lily# | LilyPond |
|---|---|---|
| Boxed heading → its item | 4.01 | 4.0098 |
| The box's last baseline → the centred line under it (1 + 1.1 + 1 + 1 + 2) | 6.10 | 6.1001 |
| The centred line's left edge | 53.74 | 54.3049 (LilyPond centres by its own measure: section 5.1) |

### 5.4 What stands under the text (mended 2026-11-13, grammar evaluation 4)

The big-band writer's page: a poet line, a text, and `tempo "Medium Swing" 4 = 138 swing`
over nineteen staves — the swing equation's triplet bracket printed through the text's last
line, the text close under the poet line.

- **The equation was not in the system's extent.** The pass that prices the systems for the
  page (`LayoutEngine.Prelim`'s score) was built without the opening tempo's swing
  subdivision, which the final pass has; so on a multi-staff score the first system's ink
  above the staff ended at the metronome note's stem, and whatever stood over the system —
  a text, the title — was cleared by that and not by the bracket. And the equation's own top
  (`MetronomeMarkGeometry.Swing`) left out the bracket's number, 0.49 over its line.
  MEASURED (`eval4/fixA/i4`, the repro cut to eight staves so the page fits, `markTempo
  beside`): the first system's up extent 5.863 → 7.317; the text band's bottom 29.21, the
  number's top 28.25 before (0.96 INSIDE the band) and 29.70 after (the 0.5 of
  markup-system-spacing's padding under it).
- **What remains on the writer's own page is the over-full page**, and it is said: nineteen
  staves at the default staff are 25 staff spaces taller than A4 ("page 1 is over-full …"),
  and an over-full page takes the overflow out of EVERY gap of the chain, the title rows →
  text and text → system ones included (LILYPOND-REF: lily/page-layout-problem.cc:806-822
  solve_rod_spring_problem — the k-th position moved up by (k − 1) × overflow / (count − 2)).
  The text therefore stands 1.19 nearer the poet line there (8.83 for a rod of 10.02); on a
  page that fits it keeps its margin (3.95 from the poet / composer baseline). Not changed:
  the cure is a smaller staff (`paper { staffSpace … }`), which GRAMMAR_FOR_LLM now gives.

### 5.5 The text in its frame, and the text's width (built 2026-11-13; the width undone 2026-11-14)

Two things the owner said on seeing a one-line `md center box` of ほえほえ. The first stands;
the second was built, widened to every text the same day, and undone the next. LILYSHARP-OWN:
LilyPond's `\box` pads whatever extent it is given, wherever the markup stands, and a
top-level markup begins at the page's left margin.

**1. The text stands in the frame's optical middle.** "With one line in the box, the text's
y is not centred in the box; can it be centred?" The frame was the padding outside the
line's BOX — inside it 3.0 over the baseline and 2.0 under — and a line's ink stands almost
all over its baseline.

- **The middle** is a fixed fraction of the line's em over its baseline:
  `ProseBand.OpticalMiddle` = **0.37**. MEASURED (the bundled face through
  `ScoreTextMetrics.Ink`; the Japanese on the PNG at 20 px a staff space, this machine's
  face): capital H 0.722 em over the baseline (middle 0.361), round capitals and ascenders
  0.737, x-height 0.466 (0.481 with overshoot), descenders 0.206 under; a Japanese em
  square 0.88 over / 0.12 under (middle 0.38), ほえほえ as drawn in the renderer's cells
  0.82 over / 0.04 under (middle 0.39).
- **The shift** (`ProseBand.BoxShift`) = the middle less half of what a line box has more
  over its baseline than under: 0.37 em − skip / 6 = 0.814 − 0.5 = **0.314** at the default
  size. Inside the frame: **3.314 over the first baseline, 1.686 under the last**. Several
  lines: the same (first line above, last line below; the step is untouched). A line set
  larger takes it in proportion: a `#` heading first, 0.396 (so that frame is 0.08 taller
  than it was: 0.314 × 0.26); a list or a paragraph last, 0.314.
- **What stays is the FRAME; the text moves in it.** The band's `Up` — where the page stands
  the frame under the composer's line — and the frame's height are to the digit what they
  were (lines of one size), so the white tuned between the composer's line and the rule, and
  the rule and the system, is untouched; every baseline stands 0.314 lower. A boxed band's
  reference point is therefore no longer its first baseline: `Lines[0].Baseline` is the
  shift. Under the last system the same: the frame's top is 8.9 under the last staff's
  middle line as before (score-markup-spacing's 12 less the 3.1), the baseline 12.314 where
  it was 12. A text with no frame is where it was.
- **The white over and under the ink of one boxed line** (at 2.2; before in brackets):

  | Line | Over | Under | |
  |---|---|---|---|
  | ほえほえ, by its em square | 1.38 (1.06) | 1.42 (1.74) | centred; as drawn on the PNG 1.50 / 1.60 |
  | `HOEHOE` | 1.69 (1.38) | 1.65 (1.97) | centred; PNG 1.65 / 1.65 |
  | `Play gently.` | 1.69 (1.38) | 1.23 (1.55) | ink's middle 0.23 low (was 0.08 high) |
  | `moon over a canon` (x-height only) | 2.26 (1.94) | 1.65 (1.97) | 0.30 low (was centred) |
  | `moon over a canyon` (and a descender) | 2.26 (1.94) | 1.23 (1.55) | 0.51 low (was 0.20 low); PNG 2.20 / 1.25 |

  ⚠️ An all-lower-case line was nearer its ink's middle before. It is not special-cased:
  on the PNG it reads a little low, not wrong (the eye discounts the one descender), and a
  middle chosen from each text's own letters would make two frames on one page differ.
  **Open, for the owner**: if it is to be mended, the mend is that — 0.37 where the text has
  a capital or a wide character anywhere, about 0.23 (half the x-height) where it has none.

**2. A text with no `xMargin` runs from the page's left margin to its right one** — boxed or
plain, under the title or after the last system, a centred line centred between the margins
(under the title's middle). `xMargin` is how a writer puts a text elsewhere: `xMargin 15mm`
sets it on the staff of a default first system (LilyPond's 15 mm indent), and any other
length as written. `xMargin 0` is the same text as writing none, so a fence that writes it
beside one that writes nothing is one text with it (`TextShape.IsPlain`), as two fences that
write nothing are.

The history. On 2026-11-13 the owner asked, of a boxed note: "The box's default xMargin
should be the same width as the staff" — the frame ran from margin to margin while the first
system begins its indent further right (15 mm by default, more where a long instrument name
needs it), so it stuck out to the left of the music. It was built (a frame with no `xMargin`
began where the staff lines of the system beside it began, the first system's for the text
under the title, the last system's for `md end`), and later the same day widened to a text
with no frame ("box がつかない ```md の既定の xMargin も同じ値にして"), so that a boxed note
and a plain one under it shared their left edge. On 2026-11-14 the owner undid it: "いま、md
の xMargin は五線の一行めと揃うようになっているけど、なんだか不自然だ。右側と左側のマージンが
違うからだろうか。" — the text sat asymmetrically on the page (an indent's white at its left,
none at its right) and off the poet's and the composer's lines, which stand on the margins —
and, after trying the idea of equal margins: "いや、やはり既定は0にしておいて。気に入らなければ、
ユーザーが好きな値を指定できるから、それで良いか。" The default is the margins again, as it
was before 2026-11-13; the twin writes the bare line (or the frame's inset alone) again.

MEASURED 2026-11-13, LilyPond 2.26.0 (`LilySharp-Lab` `sessions/p1226/md/boxfit`: `owner.lys`,
`heading.lys`, `list.lys`, `names2.lys` — and their `lysc ly --pin-fonts` twins in `lp/`;
paper coordinates, y down). The vertical numbers stand; the frames' left edges were measured
while a frame stood on the staff (today it begins at the margin, 8.54):

| What | Lily# | LilyPond |
|---|---|---|
| ほえほえ: frame's outer top, bottom; baseline | 17.71, 22.91; 21.12 | 17.7087, 22.9087; 21.1227 (3.414 over, 1.786 under) |
| `#` heading first: frame's top, bottom; the two baselines | 17.71, 27.52; 21.72, 25.73 | 17.7086, 27.5200; 21.7241, 25.7339 |
| List last: frame's top, bottom; the three baselines | 17.71, 30.41; 21.12, 25.62, 28.62 | 17.7087, 30.4087; 21.1227, 25.6227, 28.6227 |
| `md end box`: its frame's top under the last staff's middle line; its baseline | 8.90; 12.31 | 8.9000; 12.3140 |

**The twin** of a boxed text: `\translate #'(0 . -SHIFT)` hangs the box the first line's
share under the line's reference point and the column's `\with-dimension #Y` reaches the
share further up and the last line's share less down.

### 5.2 Headings and lists (built 2026-11-13)

The owner's request: "箇条書きと見出しも描けるようにして". LilyPond has neither; every number
here is LILYSHARP-OWN unless it says LILYPOND-REF, and the twin writes each as markup so the
two engines can be compared.

**Headings.** Bold, in the text's face, larger by LilyPond's font-size steps (LILYPOND-REF:
`\fontsize`, a step is 2^(1/6)):

| Written | Size | Against the header |
|---|---|---|
| `#` (and a setext `===`) | +2 steps: 2.2 × 2^(2/6) = 2.77 | the SUBTITLE's size and weight; the title is 3.49 |
| `##` (and `---` under a line) | +1 step: 2.2 × 2^(1/6) = 2.47 | between the subtitle and the text |
| `###` … `######` | drawn as `##` | "two levels": no warning, no third size (a size between 2.47 and 2.2 is not told from bold text) |

- A heading's line is the text's line box grown as the heading is: 2 × scale over the
  baseline, 1 × scale under (2.52 / 1.26 and 2.24 / 1.12). A long heading wraps at its own
  size; its lines step by 3 × scale.
- White: **a whole baseline-skip (3.0) over a heading**, **a quarter (0.75) under it** — the
  heading belongs to what follows. A heading that is the text's first line has no white over
  it: the band's `Up` is its taller box, and the page's springs place it by that.
- A heading in a list item (`- # x`) is set at the item's indent.

**Lists.**

- The marker: a bullet `•` (U+2022) for a bullet list, an en dash `–` (U+2013) one level
  down, alternating deeper (a hyphen where the text's face has neither; the open circle of
  HTML's second level is not in the bundled face). A numbered list's marker is its number and
  the delimiter written (`1.` or `1)`), counting on from the FIRST item's number whatever the
  later items write (CommonMark's start number).
- The indent: each list level moves its items' words **1.5 em (3.3)** right; the marker hangs
  in the indent, flush RIGHT, **0.5 em (1.1)** short of the words, so the stops of `9.` and
  `10.` align. A numbered list whose widest number does not fit takes that number's width and
  the gap instead, for the whole list. Every line of an item begins at the indent (the
  hanging indent): a wrapped line stands under the item's words, and the lines are broken to
  the width the indent leaves — Japanese between characters, as in a paragraph.
- Nesting is as deep as written (tested to three).
- Tight and loose are CommonMark's: with no blank line between items they step by the
  baseline-skip (3.0); with one, the items — and two paragraphs of one item — stand a
  paragraph's white apart (4.5). A list stands a paragraph's white off the paragraph over it
  and the one under it.
- An item that writes no words keeps its marker on a line of its own; so does an item that
  begins with a list of its own (`- - a`).
- A task item (`- [ ] todo`) is an item whose words begin with the brackets: the task-list
  extension is not switched on. A list inside a block quote is the quote's (LYS4058 once).

**With the shape words.** `xMargin` and `box` narrow the width the items wrap to and move the
list right with the text; the frame is around the lines' boxes, a heading's taller one
included. `center`: section 5.1.

**The twin** (in the `\markuplist \column-lines` of section 7.1, the page's own lines):

- a heading: `\with-dimension #Y #'(-DOWN . UP) \fontsize #2 \concat { \bold "…" }` — its box
  and the white over it written as the line's extent, since LilyPond would stand a larger
  line on its ink;
- an item's first line: `\concat { \with-dimension #X #'(0 . INDENT) \translate #'(INDENT−GAP . 0)
  \right-align "•" "…" }` — a box as wide as the hanging indent, so the words begin where the
  page begins them whatever LilyPond measures the marker, the marker right-aligned by
  LilyPond's own measure;
- its later lines: `\concat { \hspace #INDENT "…" }`;
- in a box (`\column`): the same lines, and everything a line stands lower than one
  baseline-skip as a `\vspace`.

MEASURED 2026-11-13, LilyPond 2.26.0, `LilySharp-Lab` `sessions/p1226/md/lists`
(`showcase.lys`, its `lysc ly` twin engraved as `lp-showcase.ly`; staff spaces, y down):

| What | Lily# | LilyPond |
|---|---|---|
| `#` heading: size, left edge | 2.77, 8.54 | 2.7719, 8.5358 |
| `##` heading: size | 2.47 | 2.4697 |
| Composer's baseline → a `#` heading first in the text | 3.47 | 3.4903 (the unboxed paragraph's 2.95 / 2.9705 and 0.52; measured before the margin of section 5.1 — with it 4.47 / 4.4903) |
| `#` heading → the paragraph under it | 4.01 | 4.0099 |
| Paragraph → the list's first item | 4.50 | 4.5000 |
| Item → item (tight), a wrapped item's lines | 3.00 | 3.0000 |
| Last item → a `##` heading | 6.24 | 6.2449 |
| `##` heading → the numbered list's first item | 3.87 | 3.8725 |
| Item's words, level 1 / level 2: left edge | 11.84 / 15.14 | 11.8358 / 15.1358 |
| Marker's RIGHT end, level 1 (by construction) | 10.74 | 10.7358 |
| Bullet's left edge | 9.40 | 9.9505 |
| En dash's left edge (level 2) | 12.81 | 12.9432 |
| `1.`'s left edge | 8.93 | 9.0969 |
| Boxed `end` text: `##` heading's left edge, item's words | 9.64, 12.94 | 9.6358, 12.9358 |
| …heading → first item, item lines, list → paragraph | 3.87, 3.00, 4.50 | 3.8725, 3.0000, 4.5000 |
| …frame's top over the first (heading's) baseline | 3.34 | 3.3449 |
| …the system's rehearsal mark → the boxed heading's baseline | 15.20 | 15.2020 |

The markers' LEFT edges differ because the two engines measure the glyphs differently (the
bullet 1.34 against 0.79, the en dash 1.23 against 1.09, `1.` 1.81 against 1.64): the open
difference of the text widths in section 7.1, at its largest on the bullet. Their right ends
and the words are the page's.

## 6. Measured: LilyPond 2.26.0 (2026-11-12)

`LilySharp-Lab` `sessions/p1226/md` (`e1.ly` … `e12.ly`, SVG backend, a4, 20 pt; lengths in
staff spaces, y down the page; a staff's reference point is its middle line).

LilyPond's shape for this is a top-level `\markup` before or after a `\score`.

| What | Measured |
|---|---|
| The text's size | 2.2000, the poet / composer line's. Its left edge is the left margin (8.5358) |
| A one-line text → the first system | baseline to the staff's middle line 5.0000 (`markup-system-spacing` basic-distance) |
| The last system → a one-line text | the staff's middle line to the baseline 12.0000 (`score-markup-spacing` basic-distance) |
| A text between two scores | 12.0000 above it, 5.0000 below it: the two rows above |
| Composer line → the text | baseline to baseline 2.4960 |
| One `\markup` → the next `\markup` | baseline to baseline 2.5002 / 2.5024 / 2.5003: the ink plus `markup-markup-spacing`'s padding 0.5, not a fixed step |
| Lines of one paragraph (`\wordwrap`, `\column`) | baseline to baseline 3.0000 (baseline-skip) |
| A three-line paragraph → the first system | last baseline to the staff's top line 2.7508: the padding rules, the basic-distance 5 is from the FIRST baseline and would overlap |
| `\vspace #1` in a column | 6.0000 between baselines: one more baseline-skip |
| `\fontsize #2` | 2.7719 (×1.26); the line under it sits 2.8535 lower, its ink being taller |
| With no title, a text first on the page | its baseline at 9.6906 — where the first staff's TOP line stands when nothing is above it |
| `\wordwrap`, Latin text | breaks at the line width, ragged right; `\justify` fills the line |
| `\wordwrap`, Japanese text | **does not break**: a sentence with no spaces is one word and runs off the page |
| A `\markup` longer than the page | not broken: "compressing over-full page" |
| `\markuplist \wordwrap-lines` | breaks between its lines across pages, same 3.0000 step |
| `\markup \image #X #20 "pic.png"` | accepted; 20 wide, height by the picture's shape. In the SVG the picture's data is cut to its first 5 bytes on Windows (the box is right, the picture is lost) |

What follows from it:

- **The spacing is the paper block's own.** `markupSystemSpacing`, `scoreMarkupSpacing` and
  `markupMarkupSpacing` are keys Lily# already reads; a text is a markup to all three.
- **A paragraph's lines step by 3; two paragraphs written as two markups stand CLOSER (2.5) than
  two lines of one.** LilyPond has no paragraph. Lily#'s gap between paragraphs, the bullets'
  indent and a heading's space are Lily#'s own, to be chosen and then written to the twin as
  `\vspace` and `\hspace` so that the two engines can be compared.
- **Lily# breaks the lines, the twin writes them broken.** LilyPond cannot break Japanese, so the
  twin never asks it to: each line Lily# made is a `\line` of a `\markuplist`'s column. That
  form also runs over a page as Lily#'s text must.
- **Images** are compared by their box, not their pixels.

### 6.1 A section's text (`f1.ly` … `f5.ly`)

LilyPond has two ways to put words between two systems, and they do not look alike.

| | The score cut in two, a `\markup` between | One score, a `\textMark` after `\break` |
|---|---|---|
| Text → the staff above | 12.0000 from its middle line to the baseline (`score-markup-spacing`) | the systems stand as far apart as with no text (12.0000 between top lines); the text is in that gap |
| Text → the staff below | 5.0000 baseline to middle line; 5.2013 where the bar number's ink is in the way | baseline 1.3531 above the top line |
| Left edge, size | the left margin, 2.2000 | over the first column (11.9008), 2.3308, never wrapped by itself |
| Two lines of text | room is made: the lower system moves down by the lines | the lower system moves 0.7238 for a second line and a padding of 2 |
| The music | **cut**: bar numbers restart, the time signature prints again, the lower system takes the first-line indent and is not justified, a tie over the cut is dropped | continuous |

Decided from it: **a section's text is spaced as a markup between two scores** — it belongs to
the system under it (5), stands clear of the one over it (12), begins at the left margin at the
text size and makes its own room — **and the music stays one piece**, as Lily# draws it: the bar
numbers run on, no time signature is printed again, the lower system is an ordinary later
system, ties and spanners cross.

The twin writes the cut form with what mends it: `\set Score.currentBarNumber`, `\bar ""` (so
the number prints), `\omit TimeSignature`, the later system's indent and names, and
`ragged-right = ##f`. Measured with those: the number 5 prints and the spacing is the table's.
What crosses the cut — a tie, a slur, a hairpin, a repeat — is lost in the twin and stays in
Lily#: a known difference, written into the twin as a comment where it happens.

## 7. What Lily# has, and what it lacks

- The title is one line of the page's chain, `HeaderBand`, placed by the top spring and
  `TitleToSystemSpring` (`LayoutUtilities`), and the chord list's diagrams are folded into its
  depth (`HeaderBand.WithChordList`). So the page knows exactly one markup, and it is first.
- The page breaker already prices a title line anywhere in the chain: `SystemDetails.IsTitle`,
  and `VerticalSpacingParameters` picks `MarkupMarkup` / `ScoreMarkup` / `MarkupSystem` between
  two lines by which of them is a title, as LilyPond's `page-layout-problem.cc` does. Its note
  says what is absent: "no markup-paragraph model in the breaker" (`PageBreaker`,
  `PageBreakingParameters`).
- Missing, then: making such lines from a text (one a paragraph line, so that a page may end
  between two), handing them to the breaker in their places — after the header, between two
  systems, after the last — and placing and drawing them in the page's chain. The header line
  forbids a page break after it, and a text seems to do the same: with 13 systems filling a
  page (`g13.ly`), the text after them went to page 2 with its system, though 12 systems, the
  text and a 13th fit one page (`g12.ly`). One case, not a proof: to pin when it is built.
- Where the text stands against the chord list and the "Capo N" line: **under them** (proposed,
  the owner has not said). The capo and the diagrams are the piece's data, as the composer is,
  and belong to the title's block; the prose follows the block. It also leaves `HeaderBand`,
  measured to the digit, as it is.
- A CommonMark reader is a new package for the core (it already takes its PDF and text-shaping
  ones).

### 7.1 What step 1 built

- `SyntaxKind.FencedText` (one token, `Lexer.ScanFence`), `MarkdownTextSyntax`, `FencedText`
  (the opening line's words, the body, a place in it back to the file), `MarkdownTextValidator`
  (LYS4057, LYS4058), LYS0043 in `LexicalDiagnostics`.
- `Rendering.Prose` reads the body with Markdig (BSD-2-Clause, in the notices): paragraphs and
  their runs, and what is not drawn, each with its place.
- `Svg.Layout.ProseBand` sets it: Lily#'s own line breaking, and **every line a box** — two
  thirds of the baseline-skip over its baseline, one third under — which is what a
  `\markuplist \column-lines` gives a line (`scm/markup.scm` space-lines) and what a bare
  `\markup` or a bare list of markups does NOT (those stand on their ink: 2.0 a line in
  `t1tw.ly`). The twin writes that form (`LilyPondExporter.EmitText`), a paragraph's white as
  its first line's taller box (`\with-dimension`).
- The text under the title rides `HeaderBand.Text` and is a node of the page's chain of its
  own (`LayoutUtilities.HeaderSprings`: rows, text, system); the text after the last system is
  a node after it (`PageLayouter.PositionSystemsOnPage`), and `PageLayout` carries where both
  were solved to. `SharedRenderer.DrawProse` draws them, SVG, PDF and PNG alike.
- MEASURED, `t1.lys` against `t1tw2.ly` / `t1auto.ly` (a text had no margin then: these are
  today's `yMargin 0`, held by `MarkdownTextShapeTests.YMarginZero_IsLilyPondsOwnSpacing_AsMeasured`;
  the default adds section 5.1's 1.0 where the padding rules): composer line to the first line 2.95
  (LilyPond 2.9705), line to line 3.00 (3.0000), paragraph to paragraph 4.50 (4.5000), the
  last staff's middle line to the closing text 12.00 (12.0000). The text to the first staff is
  5.09 against 4.93, and the same book WITHOUT a text is 4.54 against 4.40 (`t0.lys`): the
  difference is the page's own in that book (a rehearsal mark over the clef), not the text's.
- Approximations, said: the breaker prices the rows and the text as ONE title line, and the
  closing text as ink under the last system — so the two share a page wherever a page holds
  both, where LilyPond weighs sending the markup to the next page alone; the system-count loop
  does not see the closing text; a text is not broken over a page.
- **Mended 2026-11-13 (grammar evaluation 4).** ⑴ The closing text was DRAWN only where the
  title was — on page 1 — so a score of two pages or more printed it nowhere, without a
  word; the layout had placed it on the last page all along (`PageLayout.EndText`). It is
  drawn on the page that holds it (`SharedRenderer`, the page loop). ⑵ A closing text that
  cannot share a page with even the last system (the two are taller than the paper) made
  that page over-full, its system pressed into the top margin. The music is now paged as if
  it had no text and the text opens a page of its own after it
  (`PageLayouter.CreatePagesWithOptimalBreaking`; a `PageLayout` with no systems), its first
  baseline top-markup-spacing under the top of the printable area. A text taller than the
  paper itself still runs off that page (not broken over pages) and the warning says so.
  MEASURED (LilyPond 2.26.0, `eval4/fixA/i1`, `v5.lys`: a forced `pageBreak`, one system a
  page): the text's two baselines on page 2 at 119.00 / 122.87 against LilyPond's 116.34 /
  120.21 — both engines stretch the last page by the first page's force
  (ragged-last-bottom), which is why the text stands far under its system there; the 2.66 is
  that force's, not the text's.
- Open: a text's widths in the lab's LilyPond are about a tenth narrower than Lily# measures
  them ("This piece is for " 14.75 against 16.56, "A. Composer" 11.72 against 12.87) — not
  looked into; the twin's lines are the page's, so only where a piece begins inside a line
  differs. A Japanese line under a face with narrow kana ends short of the margin (the
  machine's face is not measured).
- **A character the text's face lacks is set at its em square** (mended 2026-11-13, grammar
  evaluation 4). The layout always reserved an em a character for kana, kanji and hangul the
  bundled face does not have, but it handed the drawer one piece a STYLE, and the machine's
  face set that run by its own advances: under a face with proportional kana a bold or
  italic run ended short and the next piece stood off it (MEASURED on this machine's PNG,
  `eval4/choral/r-08`: 2.9 of white after seven bold characters, 4.3 after seven italic).
  `ProseBand.Break` now makes every such character a piece of its own at its square, so the
  layout's x is the drawn x. What that guarantees, and what it cannot:
  - **PNG, PDF**: each character begins at its square; no run can drift, and a change of
    style leaves no gap (measured in the PNG: every character's ink begins 0.06–0.6 inside
    its 2.2 square; the PDF takes the same calls and was not rasterised here).
  - **SVG**: one `<text x=…>` a character, so the viewer cannot accumulate a drift either.
    The viewer still chooses the face: one with narrower kana leaves a little white inside
    each square (evenly, the look of a monospaced setting), one wider than an em would
    touch its neighbour. That is not Lily#'s to rule without embedding a CJK face.
  - The twin joins the pieces back into strings: LilyPond sets them by its own face.

### 7.2 What step 5 built: the editor (2026-11-13)

The reference, approved by the owner that day after its licences were read: VS Code's own
Markdown support (MIT). **Nothing of it is copied**: its grammar is embedded by name, and
what it does not bring is Lily#'s own code.

**Colours** (`editors/vscode/syntaxes/lilysharp.tmLanguage.json`, `#fenced-text`).

- The opening line: the backticks, `md` / `markdown`, the words `start` `end` `box` `center`
  `xMargin` `yMargin` (held to `FencedText` by `EditorColouringTests`), a length by `paper { }`'s own
  rule. The closing line: the backticks.
- The body includes `text.html.markdown` — the grammar of VS Code's built-in
  markdown-basics — under `meta.embedded.block.markdown`, which `package.json`'s
  `embeddedLanguages` maps to the language `markdown`.
- The shape is the one VS Code's Markdown grammar gives a fenced block of another language:
  an outer begin/end whose `end` is the closing line, and in it a begin/WHILE rule. A
  `while` is asked at each line's start before any rule of the embedded grammar, and
  failing pops them all. MEASURED with VS Code's tokenizer and its real Markdown grammar
  (`LilySharp-Lab` `sessions/p1226/md/fenceeditor`, `t2.lys`): with a plain begin/end, a
  fence indented two spaces in a score and ending in a list never closed — the list kept
  the closing line and read it as a code block's opening, and the rest of the file was
  Markdown.
- The `while` names the opening line's own backticks (four close on four; three inside
  are the text's) and takes the opening line's indentation off each line before Markdown
  reads it, as the compiler takes the closing fence's: a fence four spaces in would
  otherwise be one indented code block. (The two indentations are the same in a text
  written as section 2 writes it; where they differ the editor goes by the opening one.)
- **Only where a text may begin**: `#fenced-text` is listed at the root and in
  `#score-body`, a new region for `score NAME { … }`. For the grammar to know a line is
  inside some OTHER brace, every brace no block of its own claims is now `#nested-braces`
  (the top level was flat until this), and wherever a list reads the whole language
  inside a brace, `#fenced-text-elsewhere` stands first: it takes the blanks and the
  backticks and scopes nothing, so three backticks in a section's music colour as they
  did. MEASURED on the repository's 702 `.lys` files, token by token against the grammar
  before: one token differs — the `lyrics words` row of `samples/drunken-sailor.lys`'s
  `score lyrics { … }`, which the lyrics block had been swallowing as syllables.
- A section's text (step 3) will want `#fenced-text` listed directly in a section's brace:
  today that brace is the generic `#nested-braces`.
- Not recursive: a grammar included by name brings no injections with it (they are
  gathered for the ROOT scope, `source.lilysharp`), so this extension's own injection of
  Lily# into Markdown's ```` ```lily# ```` blocks does not fire inside a page text; a code
  block there has Markdown's code colour.
- Limits: a `score` whose `{` is on a later line is not the region (its text colours as
  Lily#); a brace left open above a fence keeps it from beginning until it is closed.

**Semantic tokens.** None stand on a text: the fence is one token of a kind the walk does
not colour. `DeclaredNameTokenTests.APageText_GetsNoTokenOverItsMarkdown` holds it.

**What the embedded language brings, and what it does not** (read in VS Code's source,
`src/vs/editor`; not seen in a running editor):

| Follows the language AT THE POSITION — Markdown's in a text | Follows the DOCUMENT's language — Lily#'s, also in a text |
|---|---|
| Toggle Comment (`<!-- -->`): `lineCommentCommand.ts`, `blockCommentCommand.ts` | the pairs a typed character closes: `cursorCommon.ts` builds them from `model.getLanguageId()` |
| bracket matching: `bracketPairsImpl.ts` | the pairs a typed character surrounds a selection with: the same |
| the word pattern, the Enter rules (`enterAction.ts`) | completion, hover and every other provider (the server answers nothing in a body) |

Markdown has no Enter rule: VS Code's text editor does not continue a list
(`markdown-basics/language-configuration.json` has no `onEnterRules`; the
`markdown.editor.smartEnter` of markdown-language-features is its separate custom
editor's). So both aids below are Lily#'s (`editors/vscode/src/pageTextCore.ts`, tested
under node; `pageText.ts` carries them out), off with `lilysharp.editing.typingAids`:

- **Enter in a list.** On an item with words, the caret in or after them: the next line
  begins with the item's indentation and the next marker (the same bullet; the next
  number with its `.` or `)`), and takes what stood right of the caret. On an item with
  no words the list ends: a nested item becomes an item of the list around it, an
  outermost one an empty line at the text's indentation. The later items are not
  renumbered (section 5.2: the count is from the first). Bound to Enter under a context
  key (`lilysharp.inTextList`) set only while the caret is on a list item in a text's
  body; the command asks the document again, and an Enter that is not its own is typed as
  VS Code types it. Not under the suggestion list; an input method's Enter never reaches
  a key binding.
- **`*`, `_`, a backtick over a selection** surround it, the words left selected (a second
  `*` makes `**strong**`). VS Code has replaced the selection by then — these three are
  not Lily#'s surrounding pairs, and must not be in music — so the words are written back
  in the keystroke's undo step.
- **Lily#'s typing aids rest in a body** (`smartTyping.ts` asks `inPageText`): a digit, an
  apostrophe, a bracket or a space at a line's end typed in prose is left as typed.

Where a text is, for these, is read off the document's lines by the lexer's rule
(`textBodies`: three backticks or more begin a line; the next line of as many and nothing
else closes), once a version and only when a rule is about to act.

**Tests.** `editors/vscode/test/pageTextGrammar.test.ts` runs VS Code's tokenizer
(vscode-textmate, vscode-oniguruma: development dependencies, in no package) over the
grammar, Markdown answered by a stand-in written for it — and by the real grammar where
`LILYSHARP_VSCODE_REPO` names a checkout. `pageTextCore.test.ts`: the lines, Enter, the
surround. What only a person in VS Code can see is `editors/vscode/MANUAL-CHECKS.md` 3.8.

## 8. Order of work

1. DONE (section 7.1). The fence, and paragraphs with bold and italic under the title and at
   the end: SVG, PDF, the preview, the twin. The page's chain learns a markup line.
   DONE 2026-11-13 (sections 2.3, 5.1): `xMargin` (first named `indent`), `center`, `box` on
   the opening line, and their completion; then `yMargin` and the default margin of 1.0
   above and below every text, with its completion and the step keys on both margins'
   lengths.
2. DONE 2026-11-13 (section 5.2): headings (two levels) and bullet and numbered lists —
   `Prose` reads them into its flat blocks (`Heading`, `Lists`, `Marker`, `Tight`),
   `ProseBand` sets them (`ProseLine.Scale`, `Marker`, `Indent`), `SharedRenderer.DrawProse`
   and the twin (`EmitMarker`) carry them; LYS4058 no longer names them. NOT done: links (a
   link's words are drawn, the link itself is not made — and nothing says so yet).
3. A section's text (section 6.1).
4. Images.
5. The editor. DONE 2026-11-13 (section 7.2): Markdown colours inside the fence, the body
   Markdown to VS Code (its comments and brackets), Enter continuing a list and `*`, `_`,
   a backtick surrounding a selection. NOT done: Markdown completion inside the fence; a
   click on the text in the preview going to its source; hover.
6. Later: a footnote (`@footnote("…")` on a note, its text at the page's foot), code blocks.
7. DONE 2026-11-13 (section 9): the six header lines written as Markdown.
8. DONE 2026-11-14 (section 2.4): `start`, the place under the title named; one fence item in
   completion, its place chosen on its opening line.

## 9. Header places (owner's decisions 2026-11-13, built that day)

The header has six lines — `title`, `subtitle`, `composer`, `poet`, `arranger`, `copyright` —
each a string (`title "…"`). Each may instead, or as well, be written as Markdown: a place word
on the fence's opening line, as `end` is.

````
title "Morning Song"

```md title
Morning Song\
*for two flutes*
```

```md composer
A. Composer\
1902–1971
```

```md copyright
Copyright 2026 **Somebody**
```
````

The owner's decisions:

1. **Both written** (`title "X"` and a ```` ```md title ````): the PAGE draws the fence; the
   plain string serves everything that is not the page — MusicXML's `work-title`, creators and
   `rights`, the MIDI copyright notice. **The fence alone**: those take its words with the
   formatting stripped, every line break one space (`Rendering.HeaderProse.Plain`;
   `Semantics.HeaderLineText.NonPage`). The model's `Title` stays the plain string (nothing
   that is not the page reads it: the exports walk the syntax).
2. **Drawn**: bold, italic, hard line breaks (several lines), inline text; a paragraph is a line
   of its own (a header line does not wrap, as the plain string does not). A heading, a list, a
   block quote, a code block, an image: LYS4058, "… is not inline text: the title takes inline
   text only …", drawn as plain lines — their words at the line's size and weight, no marker, an
   image its alt text. A rule, a table, HTML: said and not drawn.
3. **Size and face**: the line's own (`HeaderBand.TitleEm` and `TitleStyle` for a title…);
   Markdown adds emphasis within it as an OR over the line's style — `*…*` italic (bold italic
   in the title), `**…**` in an already-bold line stays bold, in the composer's line bold.
   **Several lines step a little closer than the next row** (the inner step rule; owner's
   decision 2026-11-14, "title 内の改行幅 y と、title と subtitle の間の幅 y は、少しだけ差をつけたい";
   LILYSHARP-OWN — LilyPond has no default for a header line of several lines). The WHITE
   between two of its lines — the upper line's descender to the lower's ascender, the faces'
   designed ones (OS/2 typo: 0.798 / −0.202 em in TeX Gyre Schola; `TextFontMetrics.TypoExtents`),
   not the lines' ink — is `HeaderLineSet.InnerWhiteShare` = **0.75** of the white LilyPond's
   3.5 skip leaves between its last line and the next row's ascender; a line with no row under
   it (a copyright at the foot, the last row) takes the white 3.5 would leave between two rows
   of its own size (`HeaderLineSet.InnerStep`; `HeaderBand.Build` re-steps a fence once it
   knows the row under it). The steps at the default sizes: **title 4.1083** over a subtitle
   (4.2695 over a composer line or an arranger, 3.4975 alone), **subtitle 3.4036** over a
   composer line (3.2424 alone), **composer, poet, arranger and copyright 3.175** (over an
   arranger or alone, all at 2.2). Until 2026-11-14 the step was the page text's 3.0 grown as
   the line is (4.7591 for the title), which stood a title's own lines 4.76 apart over a
   subtitle 3.26 under them — the reverse. The twin writes the step as the column's
   `baseline-skip`. The fence is ONE row of the header's column — its reference point the
   first line's baseline, its ink every line's — and **the row under it is skipped from its
   LAST line** by LilyPond's 3.5 (owner's decision 2026-11-14), as under a one-line row; its
   ink is the band's, so the page's springs, the breaker's priced depth and the crop width see
   it. LilyPond's column skips from the reference point, so the twin wraps such a column in
   `\pad-to-box` reaching down to the next row's ink top (`HeaderLineSet.NextRowTop`) and
   LilyPond's extent rule sets that row where the page does. A copyright fence is the first
   page's footer: its lowest line's ink on the bottom margin, the footer that much taller,
   page 1 that much shorter (`footer_height_`).
4. **Position**: the line's own — title and subtitle centred, composer and arranger at the
   right margin, poet at the left margin on the composer's line, copyright centred at the first
   page's foot — each LINE aligned so (a centred title centres each line; a composer's lines
   end at the right margin). Shape words and `end` with a header place: LYS4057 naming what the
   place takes ("'box' does not go with 'title': a header line written as Markdown is set where
   and as the title is: centred, at its size, bold…"); a margin's lengths are one error with it.
   One place word a fence ("'composer' is a second place for one text"). **Two fences for one
   header place** in one scope (the file, or one score): LYS4057 on the second's place word —
   a header line is one text; page texts stack, these do not. The later one is what is drawn.
5. **Inside `score { }`**: for that score alone, replacing the file's. Decided here
   (LILYSHARP-OWN): a score that restates a header line in EITHER spelling restates it whole —
   the file's string and the file's fence for that line are both taken away first
   (`MeasureCollector.ForgetHeaderLine`; the same in `HeaderLineText.NonPage`), so a score's
   `title "Violin I"` is not drawn under the file's ```` ```md title ````, and a score's fence
   does not leave the file's string in its MusicXML.
6. **The twin** writes each as `\markup` in `\header` — one line as itself
   (`\concat { "Copyright 2026 " \bold "Somebody" }`), several as `\override #'(baseline-skip
   . STEP) \center-column { … }`, inside `\pad-to-box #'(0 . 0) #'(-REACH . 0) { … }` where a
   row follows (`\right-column` for the composer and the arranger,
   `\left-column` for the poet), the emphasis the Markdown adds as `\italic` / `\bold` inside the
   weight bookTitleMarkup gives the line. It reads the page's model, so a score's own fence is
   that score's twin's.

**Completion** on the opening line (the owner: "あまり多いと、補完入力が使いにくくなる"):
after ```` ```md ```` and a space, the place words first — `title`, `subtitle`, `composer`,
`poet`, `arranger`, `copyright`, `end` — then `box`, `center`, `xMargin`, `yMargin`. Once a
place word is written no other is offered; after a header place word nothing (it takes no
shape); after a shape word no header place (decided here: it would be an error), `end` still.
No new block snippets. → Since 2026-11-14 (section 2.4) the list begins `start`, `end`, then
the six header places, and the one fence item opens it. **Colours**: the TextMate rule's keyword list takes the six words; the
body stays embedded Markdown.

Out of scope: a MusicXML title or creator of several lines is imported as a string, not a fence.

MEASURED 2026-11-14 after the title block was placed as LilyPond places it (owner, 2026-11-14:
"LilyPond と合わせられる部分は合わせて"; `LilySharp-Lab` `sessions/p1226/md/header2`, the
same showcase and seven single-line books b1…b7, each twin engraved by LilyPond 2.26.0;
`TitleBlockLilyPondTests` pins them all within 0.01):

| What | Lily# | LilyPond |
|---|---|---|
| The title's two lines | 12.2627, 16.3710 | 12.2642, 16.3725 (the step 4.1083 the twin writes) |
| The subtitle's baseline (now 2.4694, `\large`) | 19.8710 | 19.8727 |
| The composer's two lines | 23.3710, 26.5460 | 23.3727, 26.5477 (3.1750 apart) |
| The arranger's baseline | 30.0460 | 30.0478 |
| The copyright's baseline | 162.8656 | 162.8656 |
| The first staff's top line | 32.7686 | 32.7704 |

Every single-line book: each baseline and the first staff within 0.0019 (the residual is the
title string's ink height, Skia's outline against Pango's). `HeaderBand.RowPadding` (0.5,
owner's decision 2026-09-26) STANDS (owner, 2026-11-14, "その案で進めて"): with the row under a
fence skipped from the fence's last line the 3.5 binds again at the default sizes, and the
padding is again only the safety for a row a `fonts { }` size makes taller than the skip.

The table below is the state BEFORE that (kept for the record):

MEASURED 2026-11-13, LilyPond 2.26.0 (`LilySharp-Lab` `sessions/p1226/md/header`:
`md-showcase.lys`, its `lysc ly --pin-fonts` twin engraved as `md-showcase-lp.ly` / `.svg` /
`.png`; staff spaces from the paper's top-left, y down):

| What | Lily# | LilyPond |
|---|---|---|
| The title's two lines (the second italic) | 12.2627, 17.0218 | 12.2642, 17.0233 (the step 4.7591 the twin writes) |
| The subtitle's baseline | 20.2787 | 19.5593 |
| The composer's two lines | 23.7787, 26.7787 | 23.0593, 26.0593 (3.0000 apart) |
| The arranger's baseline | 28.9309 | 27.7116 |
| The copyright's baseline (one line, a bold word) | 162.8656 | 162.8656 |
| The first staff's top line | 31.6535 | 30.4342 |

The steps inside a fence are LilyPond's to the digit. What differs is the column's, not the
fence's: the subtitle under the title's last line and the arranger under the composer's stand
ink to ink, LilyPond with no padding (2.5360 and 1.6523), Lily#'s column with its 0.5
(`HeaderBand.RowPadding`, LILYSHARP-OWN 2026-09-26) — and this LilyPond set the SUBTITLE at
2.4697 (font-size 1) where `HeaderBand.SubtitleFontSize` says 2.77 (font-size 2), its ink top
0.22 taller: 3.2569 against 2.5360 under the title. Every row under the subtitle carries that
0.72, and the arranger its own 0.5 more. ⚠️ The subtitle's size is not this section's, and is
open: found here, and part A's own probe says the same — `p1.svg` sets "Sub" at font-size
2.4697 too, where a plain header's baselines agree only because the 3.5 skip binds over a
subtitle of either size. Not changed (it moves every subtitled page; for the owner).
→ Changed 2026-11-14 (owner): the subtitle is 2.4694, and the rows under a fence are skipped
from its last line — the table above.
