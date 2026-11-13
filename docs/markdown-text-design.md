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

```md
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
| at the top level | ```` ```md ```` | under the title (and the poet / composer line), above the first system |
| at the top level | ```` ```md end ```` | under the last system |
| directly in a section | ```` ```md ```` | above the system that section begins |

- The word is `md`; `markdown` means the same.
- The fence closes with a line that begins ```` ``` ````. Everything between is Markdown: `//`
  and `}` mean nothing there. A fence of four backticks holds a text that writes three
  (Markdown's own rule).
- The CLOSING fence's indentation is taken off every line, so a fence may be indented in a
  section (a C# raw string's rule: where the closing fence stands is the text's margin).
- Two fences for one place print one after the other, in written order.
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

`md` and `md end` are replaced separately. A section's text is not replaced from a score; a
layout switch takes every text off a score (its word is open).

### 2.3 The shape: `indent`, `center`, `box`

Owner-approved spelling, 2026-11-13. More words on the opening line, after `md` and the
optional `end`, in any order, each once:

````
```md box
```md end center
```md indent 20mm
```md indent 20mm 20mm box
````

| Word | The text |
|---|---|
| `indent LEFT [RIGHT]` | white at its left and right, INSIDE the line width: set that much narrower, beginning that much further right. One length is both sides |
| `center` | each line centred in the text's width |
| `box` | a ruled frame around the whole text |

- A length is written as `paper { }` writes one — the number and its unit glued, `20mm`,
  `2cm`, `0.5in`, or a bare number of staff spaces — and goes through the same conversion
  (`PaperPlanReader.ToStaffSpaces`; the list of units is one list). There is no `pt`: the
  language has none.
- The three combine. The text under the title and the `end` text each read their own opening
  line, at the top level and in a score's fence; a score's fence brings its own words with its
  text (it does not inherit the file's shape).
- Two fences for one place are ONE text, set as the first one says; a shape on the second is
  an error.
- Errors, all LYS4057 on the word: a word that is none of these (the message lists them), a
  word twice, `indent` with no length or with three, a negative length, a unit that is not
  `mm` / `cm` / `in`, and indents that leave the text less than 10 staff spaces — judged on the
  paper the fence stands under (the file's `paper { }`, or the score's own when the fence is
  in a score that names one). On any other paper the indents give way in proportion and the
  text keeps the 10.
- A section's text takes none of these: its opening line takes a printing, as before.

**Decided 2026-11-13 (owner).** Margins, centring and a ruled frame are in. A frame IMAGE (a
decorative border from a picture) was judged excessive and is not planned: for a text of any
length it needs nine-slice scaling — the corners kept, the edges stretched or tiled — and that
is a small layout engine for an ornament.

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
- As wide as the line width, from the left margin, ragged right — unless its opening line
  says otherwise (section 5.1). Lines break by themselves: between words, and between
  characters in Japanese, Chinese and Korean.
- A long text runs on to the next page between its lines.
- Nothing here touches playback, MIDI or a voice.

### 5.1 Indented, centred, boxed (built 2026-11-13)

- **`indent`**: the text's left edge is the left margin plus the left indent; its width is
  the line width less both. The lines are broken to that width.
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
  under the last — not around the ink. The frame's outer edge stands on the indent edges: with
  no indent it is at the line width's edges, and the text inside is inset 1.1 a side and broken
  to the narrower width. The band (`ProseBand.Up` / `Down`) reaches **1.0 past the frame's
  outer edges** above and below (`ProseBand.BoxMargin`, LILYSHARP-OWN), so the page's springs
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

The twin against the page (`showcase.lys`, its `lysc ly` twins engraved; staff spaces, y down):

| What | Lily# | LilyPond |
|---|---|---|
| Boxed text under the title: frame's outer left edge, width | 8.54, 102.43 | 8.5358, 102.4299 |
| …its first line's left edge | 9.64 | 9.6358 |
| …frame over the first baseline, under the last | 3.10, 2.10 | 3.1000, 2.1000 |
| …composer's baseline to the first baseline | 4.05 | 4.0705 (the unboxed 2.95 / 2.9705 and 1.1) |
| `end indent 30mm box`: first line's left edge | 26.71 | 26.7075 |
| …the last staff's middle line to its first baseline | 12.00 | 12.0000 |
| …line to line, paragraph to paragraph | 3.00, 4.50 | 3.0000, 4.5000 |
| `center indent 40mm 40mm`: the text's width | 56.91 | `line-width` 56.9055 |
| …a line's left edge ("For my teacher,") | 52.14 | 52.8026 |
| …last baseline to the staff's top line | 5.09 | 4.93 (section 7.1: the book's own, with or without a text) |

The twin's forms:

- indent: `\concat { \hspace #LEFT … }` a line of the `\markuplist \column-lines`.
- center: `\override #'(line-width . WIDTH) \fill-line { … }` — one markup in a `\fill-line`
  is centred — so LilyPond centres by ITS measure of the words. The centred line's left edge
  therefore differs by half the known difference of the two engines' text widths (section
  7.1, open), and its middle does not.
- box: one line of the list, `\concat { \hspace #TEXT-LEFT \override #'(box-padding . 1) \box
  \with-dimension #X #'(0 . WIDTH) \with-dimension #Y #'(-DOWN . 2) \column { … } }`, the white
  between paragraphs a `\vspace #0.5`. The column is given the page's extent, so the frame
  stands where the page rules it whatever LilyPond measures the words. ⚠️ The `\hspace` is to
  the TEXT's left edge: `\concat` sets the box's origin, not its left extent, after the white
  (first written to the frame's edge, the whole box stood 1.1 left of the margin).
- Found on the way: the twin of a score's OWN fence wrote the file's text (its page model was
  collected without the score's fences). Mended with this.

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

**With the shape words.** `indent` and `box` narrow the width the items wrap to and move the
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
| Composer's baseline → a `#` heading first in the text | 3.47 | 3.4903 (the unboxed paragraph's 2.95 / 2.9705 and 0.52) |
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
- MEASURED, `t1.lys` against `t1tw2.ly` / `t1auto.ly`: composer line to the first line 2.95
  (LilyPond 2.9705), line to line 3.00 (3.0000), paragraph to paragraph 4.50 (4.5000), the
  last staff's middle line to the closing text 12.00 (12.0000). The text to the first staff is
  5.09 against 4.93, and the same book WITHOUT a text is 4.54 against 4.40 (`t0.lys`): the
  difference is the page's own in that book (a rehearsal mark over the clef), not the text's.
- Approximations, said: the breaker prices the rows and the text as ONE title line, and the
  closing text as ink under the last system (so the two never part, where LilyPond may send a
  trailing markup to the next page alone); the system-count loop does not see the closing text;
  a text is not broken over a page.
- Open: a text's widths in the lab's LilyPond are about a tenth narrower than Lily# measures
  them ("This piece is for " 14.75 against 16.56, "A. Composer" 11.72 against 12.87) — not
  looked into; the twin's lines are the page's, so only where a piece begins inside a line
  differs. A Japanese line under a face with narrow kana ends short of the margin (the
  machine's face is not measured).

### 7.2 What step 5 built: the editor (2026-11-13)

The reference, approved by the owner that day after its licences were read: VS Code's own
Markdown support (MIT). **Nothing of it is copied**: its grammar is embedded by name, and
what it does not bring is Lily#'s own code.

**Colours** (`editors/vscode/syntaxes/lilysharp.tmLanguage.json`, `#fenced-text`).

- The opening line: the backticks, `md` / `markdown`, the words `end` `box` `center`
  `indent` (held to `FencedText` by `EditorColouringTests`), a length by `paper { }`'s own
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
under node; `pageText.ts` carries them out), off with `lilysharp.typingAids.enabled`:

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
   DONE 2026-11-13 (sections 2.3, 5.1): `indent`, `center`, `box` on the opening line, and
   their completion.
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
