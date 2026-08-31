# Changelog

All notable changes to the Lily# VS Code extension are documented here.

## 0.5.0

### Breaking language changes

The extension bundles the compiler, so upgrading changes what your files mean. Each of
these is diagnosed rather than silent, and every one of them will stop some existing books
compiling. The full reasoning, with the measured reach of each, is in the repository's
[CHANGELOG](https://github.com/yotsuda/LilySharp/blob/master/CHANGELOG.md).

- **Repeat structure is written in a `form { … }` and nowhere else** (`LYS1034`). A repeat
  bar line (`|:`, `:|`, `:|:`) or a volta ending (`[1. … ]`) written in music is an error.
  `repeat percent`, `repeat unfold` and `tremolo` stay in music — they abbreviate notes and
  do not change the playing order. This is the change most likely to affect you: 115 of the
  author's own 326 books stop compiling, and there is no rewriter.
- **Every span has to be closed, and `@!X` is how.** `@rit … @!rit`, `@ottava … @!ottava`,
  `@sustain … @!sustain`. An unclosed span draws nothing at all — which is LilyPond's own
  answer for a text spanner — and is now an error (`LYS4018`) rather than a warning.
- **`@loco`, `@sustainOn`, `@sustainOff`, `@sostenutoOn` and `@sostenutoOff` are retired.**
  The direction moved out of the name and into the `!`. `@treCorde` is kept, because unlike
  the others it is a word the page actually prints.
- **A part setting (`clef`, `octave`, `instrument`, `transpose`) written beside a section's
  part cells is refused** (`LYS1035`, `LYS0030`) — it belonged to no cell, so nothing read
  it. **A `form` that plays a section declared only as a header is refused** (`LYS1036`).

### Added

- **A section reference carries octave marks** — `~B'`, `~B,`, `[1. B']` — the same
  spelling a phrase reference already had.
- **`section ~A { … }` declares that the section prints no rehearsal letter**, so a section
  cut solely to carry a repeat edge is silent without saying so at every reference.
- **A form can spell a third volta ending** (`[3. … ]`), which the music spelling could and
  it could not.

### Fixed

- **Clicking a tie or a slur in the preview jumps to the character that wrote it.** Of the
  56 bow-shaped paths in the tracked snapshots exactly 6 carried a source address, so a
  caret on `~` used to light up the note in front of it instead. A `~` now cites its `~`, a
  slur cites its `(`, and a laissez-vibrer or repeat tie cites the annotation that draws it.

- **A rehearsal letter written inside an inline ending, a tuplet, a repeat or a cue is
  printed.** One reader's chart wrote A, B, C and D and printed only A — the other three
  stood inside a `[2. … ]` and drew nothing, with no diagnostic. A letter that is written
  and still not printed now says where (`LYS4019`).

- **A `@rit` in a repeated section draws the same length both times.** From one written
  mark, the first playing covered six bars and the second covered one; the spanner was
  being ended by the next playing of ITSELF. The hairpin had the same defect.

- **A lyrics row no longer prints inside the tab staff above it**, and a `rit.` above a
  system's top staff reserves the room it is drawn in. Both were reported by a reader on
  their own books, on the middle system of three.

- **A tab staff no longer repeats the markup the notation staff beside it already carries**,
  and the switch is `as numbers` vs `as full` rather than "is it a tab" — so a `@text` on a
  standalone tab appears, and an `@accent` on a numbers tab does not.

- **A TAB technique letter no longer prints on top of its own notehead.** `@tap` (T), `@hammeron` (H), `@pulloff` (P) and `@pluck`'s finger letter reserved a symmetric box around their anchor while the letter was actually drawn with its baseline there, so wherever one landed BELOW its note its ink grew upward into the head — 0.383 staff spaces of overlap. The room reserved is now the letter's own ink, measured the way it is drawn, which also gets `@pluck`'s descender (`p`) right.

- **An `@rit` / `@accel` no longer prints on top of the chord row or the lyric row above its staff.** The spanner was placed so that it cleared its own staff, but the row standing above the staff was spaced against a silhouette the spanner was not in, so on a lead sheet the two landed on the same line. The staff now reserves the room the spanner occupies — measured against LilyPond, which opens the gap by exactly the spanner's ink (probe `textspanner-under-row.ly`, books TSCR/TSLR).

- **Clicking the clef or the key signature in the preview jumps to its source line from any staff line, not just the top one.** The prefix repeats at the head of every system, but only the first system's copy was clickable; the rest were inert. Each repeat now carries the position of whatever put it in force there — the declaration, or the last mid-piece `clef`/`key` change before that system, so a line showing a change jumps to the change.

- **The chord completion inside `chords { }` lists the names first and the degrees after,**
  instead of interleaving them degree by degree. It used to read
  `C, Cmaj7, Csus4, Csus2, I, Imaj7, Dm, …` — grouped by harmonic function — so neither
  vocabulary could be scanned on its own. It now reads all seven names (`C Dm Em F G Am
  Bdim`, each with its 7th and suspensions) and then all seven degrees (`I IIm IIIm IV V
  VIm VIIdim`, each with its 7th).

- **"Reveal in Explorer" after an export opens the file's own folder again when the path
  contains a space.** It used to land on the user's Documents folder instead — the
  "sometimes" in the report was exactly "when the path has a space in it".
  `explorer.exe` reads the raw command line rather than a parsed argv, and Node quotes
  any argument containing a space, so `C:\My Scores\a.pdf` went out as
  `explorer.exe "/select,C:\My Scores\a.pdf"` with the switch *inside* the quotes;
  Explorer does not recognise that as `/select` and falls back to Documents. The command
  line is now written by hand — `/select,"…"`, switch bare — and the non-ASCII half of
  the same code path is unchanged (a verbatim command line is still UTF-16 all the way
  to `CreateProcessW`; verified on `日本語 フォルダ\楽譜 テスト.txt`).

## 0.4.0

First Marketplace release — 0.3.0 was tagged and shipped as GitHub binaries only, so
this is the first version installable from the extension page. It is also the first
release with breaking changes to the language; the four below are all diagnosed, so a
0.3.0 file tells you what to change rather than failing silently.

### Breaking changes

- **A chord is written the way it prints.** `Am`, `G7`, `F#m`, `Bb7`, `Gm7-5`,
  `Cmaj7/E` — an uppercase root, optional `#`/`b`, and a bare quality. The lowercase
  `:` entry (`a:m`, `g2:7`) and its per-chord durations are gone: a bar's entries now
  divide it on the beat grid the beams already use, and `.` holds the previous chord
  one more beat. The case *is* the grammar, which is why `R` never collides with a
  rest and every altered tension spells `+`/`-` (`m7-5`, not `m7b5`, so `Bb5` stays
  B-flat's power chord). Diagnostic: `LYS1028`.
- **The `$` sigil is gone.** `$theme` no longer marks a phrase reference; a bare name
  is one. Drum vocabulary and `q` are refused as phrase *names* at the declaration
  (`LYS1030`) rather than being disambiguated by a sigil at every use.
- **A staff's display name must be quoted.** `staff flute "Flute"`. A trailing bare
  word is now always a part reference, so `staff flute click` plays the click track
  instead of silently relabelling the flute.
- **`NoteColumn.force-hshift` now errors** (`LYS1029`) instead of being accepted and
  ignored. In exchange, `NoteHead.color` and `Stem.color` are supported — a correctly
  coloured score used to ship with an error and a non-zero exit.

### New

- **`paper` blocks.** The page's dimensions come from the source. `size b5` (or
  `jisb5`, `letter`, …) sets width, height and all four margins, each margin scaled
  from a4 the way LilyPond's `set-paper-size` scales it.
- **Named `fonts` and `paper` blocks.** Declare `fonts house { … }` or
  `paper wide { … }` once at the top level, reference it per score, and override part
  of it in place — one file can carry a wide conductor page and default part pages.
- **MusicXML round-trip.** Import carries the source's page across as a `paper` block;
  export now emits a form's custom text.

### Engraving

- Lyrics: syllables align on their own voice's notehead, melisma spans reserve the
  range they occupy, a word crossing a barline is spaced as one, and a row standing
  below a multi-staff system clears the staff above it on every system.
- Lead sheets: every line opens with a bar, no bar runs through a word, stanza
  numbers anchor clear of the opening bar, and the grid row prints the meter.
- Pedal brackets and bar numbers hang from their own staff rather than the system.

### Performance

- Typing in a score with lyrics is markedly faster: the loose-line chain caches its
  prefix, verse skylines share one store with the measure layouts, and the lyric band
  joins the per-system memo.

### Packaging

- One VSIX per platform, each carrying its own .NET runtime and native rendering, so
  the extension needs nothing installed but VS Code.

## 0.3.0

First public release.

### Language support

- Semantic syntax highlighting for pitches, dynamics, and articulations
- Real-time diagnostics (errors and warnings as you type)
- Code completion for keywords, pitches, durations, dynamics, and `@`-annotations
- Hover documentation, signature help, and document highlight
- Document outline, go-to-definition (F12), find references (Shift+F12), and rename (F2)
- Code folding, document formatting, and quick-fix code actions

### Live score preview

- Rendered score preview that refreshes as you edit
- Click a note in the preview to jump to its source in the editor
- MIDI playback with note-by-note highlighting

### Engraving

- LilyPond-faithful layout: beaming, multi-articulation stacking, fingering,
  accel./rit. text spanners, dynamics and expressive text, volta brackets,
  and multi-staff scores

### Packaging

- Self-contained language server — each platform build bundles its own .NET
  runtime and native rendering, so nothing else needs to be installed
