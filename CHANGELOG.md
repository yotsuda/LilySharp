# Changelog

Notable changes to Lily# are recorded here, newest first. Release notes are taken
from this file: the topmost section is the version being tagged, and the release
workflow attaches that section to the GitHub Release verbatim.

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
