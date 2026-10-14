# The percent repeat written where it is drawn — `%` and `%N`

Owner's decision, 2026-10-14 (session 904): `repeat percent N { … }` and `repeat unfold N { … }`
were hard to use. A percent repeat is drawn AFTER the bars it repeats — the bars once, then the
sign — so it is written there too: the bars, then the mark. The same day's rule "the bar lines
are the bars" (`GRAMMAR.md`, the note on `Barline`; `Semantics.WrittenBar`) is the other half of
the reason: `repeat percent 4 { c4 d e f | }` is four bars with no bar line between them, one of
the last places whose bars closed where the beats filled.

## 1. The language

```
c4 d e f | % | % | % |            the one-bar pattern three more times (old: repeat percent 4)
a2 b | c d | %2 | %2 |            the two-bar pattern twice more      (old: repeat percent 3)
… eight bars … | %8 |             the eight-bar pattern once more     (old: repeat percent 2)
```

- **`%N` is ONE repetition of the N bars written just before it** (`%` = `%1`), and it occupies
  those N bars of time. One written mark is one drawn sign: a `%` in its bar, the double percent
  over the bar line of a `%2`, and for `%3` and up the multi-bar sign (session 905, owner's
  decision — LilyPond draws a single slash in the first bar): min(N, 4) slashes at the middle
  of the N bars, on the middle bar line for an even N and in the middle bar for an odd one, with
  the count N above from `%2` up (`PercentRepeatItem.Bars`); the bars' inner bar lines forbid a
  line break, so the sign's bars stay on one line. Formerly LilyPond's single slash in the first bar with
  the others blank (the three shapes the page already draws — `PercentRepeatShape`). It is the
  only item of its span: written between two bar lines, `| %4 |`, as `R1*4` is — the closing
  one may be the music's end, and a `break` may stand beside the mark (`… | break % |`, the
  shape the owner's books write between repetitions).
- **Several marks in a row repeat the same bars again**: `x | %2 | %2 |` is `x` three times. A
  mark of ANOTHER length right after them is an error — the bars a mark repeats are written
  bars, not repetitions (`a | % | %2 |` would have to mean `a a`, which nobody reads off it).
- **"The bars before it" are the written bars of the same music** — the same block, part cell or
  phrase body — counted back from the mark over its bar lines. Fewer than N bars there is an
  error (LYS1048). The bars must be whole bars of the meter (they are what the sign stands
  for); a pickup or a short bar among them is an error (LYS1049). The directives at the head
  of the first of them — `break`, `key`, `time`, `clef`, `tempo` … — are written BEFORE the
  bars and stay outside the repeat (`break key g d4 e f g | %`: the key is not repeated).
- `%` takes no space before its count (`%2`); `%0` and a count past 64 are no number of bars.
- **There is no count suffix** (`%*7`): seven more turns of a one-bar pattern are seven marks —
  each a bar the reader sees. (`R1*N` is the one multi-bar token left; a rest has nothing to
  write in each bar.)
- **A beat repeat (a body shorter than a bar) is gone.** Four bodies in the owner's 334 books;
  the migration writes their notes out.
- **`repeat unfold` is gone.** One use in 334 books; a phrase written N times says the same and
  is drawn the same (`ground | ground |`).
- **`repeat tremolo` stays** — it is not a repeat of bars but a way of writing a tremolo.

## 2. The old spelling

`repeat percent` and `repeat unfold` are an error that names the new spelling, with a quick fix
that rewrites the item (`… | % | % |` after the body; the unfold's body written out N times).
`lysc percent <book>` rewrites a whole book (the `lysc octave` family: the source text in, the
same text out with only those items changed). The repository's own books, fixtures, samples,
lessons and documents are rewritten by it; the owner's books are the owner's call.

## 3. The tree

The parser reads `%` and `%N` as one token, `PercentMark`, an item of music. After a music item
list is read, each run of marks is GROUPED with the bars it repeats into one node,
`PercentRepeatSyntax`: its children are the body's items (bar lines between them included), the
bar line before the first mark, and the marks with their bar lines between them — the source's
own nodes in the source's order, so every position and the full text are unchanged. The bar
line after the last mark stays outside, as `repeat percent N { … } |` writes it.

The node answers what `RepeatExpressionSyntax` answered for a percent repeat: the body's items,
the count (marks + 1), and where each repetition's sign is written (its mark). Every reader that
handled `repeat percent` handles this node through the same code, parameterized by those three
— the page (`MeasureCollector.ProcessRepeatExpression`), the bar model and the validators
(`MeasureModel`, `MeasureValidator`, `MeasureDurations`), the playback (`MidiExporter`), the
MusicXML (`measure-repeat`) and the LilyPond twin (`\repeat percent`).

A mark the parser cannot group (too few bars, not alone in its bar, after marks of another
length) stays a bare `PercentMark` item with its diagnostic (LYS1048), and every reader treats
it as the N bars it names, empty — the page draws them empty, nothing is invented. Bars that
are not whole are grouped all the same and reported (LYS1049); the readers price a mark at the
meter.

How the readers walk the group: the page (`MeasureCollector.ProcessPercentRepeat`) walks the
bars and their bar line, then each mark as one repetition of spacers carrying the sign, the bar
lines between the marks as written; the bar model (`MeasureModel`) and the validator
(`MeasureValidator`) read the group's children in place, a mark priced like an `R1*N`; the
playback (`MidiExporter.ProcessPercentRepeat`) sounds the bars again at each mark from the frame
they opened in; the MusicXML writes the repeated measures with their notes under a
`measure-repeat` run (one- and two-bar bodies); the twin writes `\repeat percent N { … }`.

## 4. What it was measured against (session 904)

The owner's 334 books, converted by `lysc percent` on a copy (the books themselves are the
owner's): 472 repeats became marks and 5 were written out (4 beat repeats, 1 repeat that starts
mid-bar). Against the books as written on the build before: every MIDI byte-identical (529
files), every SVG identical with the source positions masked except those 5 books (529
compared), and `lysc check` the same in every book but those (the same bar-line errors, carried
by the written-out bars). The repository's own books, fixtures, samples and audit probes are
rewritten; the same comparison over the 679 tracked books differs only where a beat repeat was
written out and in `repeat volta` (a LilyPond spelling, already an error) now read once.
