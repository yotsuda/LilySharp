# Changelog

All notable changes to the Lily# VS Code extension are documented here.

## 0.5.0

### Language

The extension bundles the compiler, so these change what a `.lys` file means. Each is
diagnosed rather than silent, and the repository's
[CHANGELOG](https://github.com/yotsuda/LilySharp/blob/master/CHANGELOG.md) carries the
reasoning behind each one.

- **Repeat structure is written in a `form { … }` and nowhere else** (`LYS1034`). A repeat
  bar (`|:` `:|` `:|:`) or a volta ending (`[1. … ]`) written in music is an error.
  `repeat percent`, `repeat unfold` and `tremolo` stay in the music — they abbreviate notes
  rather than change the playing order.
- **Every span has to be closed, and `@!X` is how** — `@rit … @!rit`, `@ottava … @!ottava`,
  `@sustain … @!sustain`. An unclosed span draws nothing at all, which is LilyPond's own
  answer, and is an error (`LYS4018`).
- **`@loco`, `@sustainOn`, `@sustainOff`, `@sostenutoOn` and `@sostenutoOff` are retired.**
  The direction moved out of the name and into the `!`. `@treCorde` stays, because unlike
  the others it is a word the page actually prints.
- **A part setting (`clef`, `octave`, `instrument`, `transpose`) written beside a section's
  part cells is refused** (`LYS1035`, `LYS0030`) — it belonged to no cell, so nothing read
  it. **A `form` that plays a section declared only as a header is refused** (`LYS1036`).
- **A phrase reference's interval argument is removed.** `Melody'(3)` no longer plays the
  phrase a third up; the octave marks `Melody'` and `Melody,` are unchanged.
- **A percent repeat prints the sign its body's length earns**, and **a tab staff's style
  follows the score** — `tab NAME` with no `as` clause is `as numbers` beside a notation
  staff and `as full` when it stands alone.

### Added

- **The key's chords are offered in a section's music, and accepting one writes its notes.**
  In C major the list carries `C`, `Cmaj7`, `Dm`, `Dm7` … and the degrees `I`, `IIm7`, `V7` …
  beside the pitch letters; choosing `C` inserts `<c e g>`, `IIm7` inserts `<d f a c>`, in
  the spelling of the key in force at the caret (`F#m` in D major is `<fis a cis>`). The
  `flatSpelling` setting applies to these notes as it does to the pitch rows. This row had
  been lost; it is back with a test that compiles every offered chord.
- **A form's completion carries the whole form vocabulary, in a writer's order.** Sections,
  silent sections, then the repeat block — `|:`, `:|`, `:|:`, `:|*N` — the endings
  `[1. ]` `[2. ]` `[3. ]` `[1-2. ]`, the navigation marks, the engraved barlines `||` `|.`
  `!`, `break` / `nobreak` and `_"…"`. The list used to sort by label, which buried the
  repeat bars under the section names, and lacked `:|:`, the count, the barlines and the
  breaks. A repeat bar typed as far as `|` is replaced by the item rather than appended to
  (`||:` no more). Every plain item is compiled in a form by a test.
- **The music list no longer offers `|: :|`.** Repeat structure is form-only (`LYS1034`), so
  the two volta snippets that still stood in a section's completion taught a spelling the
  compiler refuses; they are gone, and a test now compiles every plain item the list offers.
- **The music list reads in the key's order.** On Ctrl+Space the pitches come in scale order
  from the tonic (`d e fis g a b cis` in D major), then the chord names root by root — triad,
  7th, sus4, sus2 — then the degrees in the same shape (`I Imaj7 Isus4 Isus2 IIm IIm7 …`).
- **A section reference carries octave marks** — `~B'`, `~B,`, `[1. B']` — the same
  spelling a phrase reference already had.
- **`section ~A { … }` declares that the section prints no rehearsal letter**, so a section
  cut solely to carry a repeat edge is silent without saying so at every reference.
- **A form can spell a third volta ending** (`[3. … ]`).

### Fixed

- **Clicking a tie or a slur in the preview jumps to the character that wrote it.** Ordinary
  bows carried no source address, so a caret on `~` used to light up the note in front of it.
  A `~` now cites its `~`, a slur its `(`, and a laissez-vibrer or repeat tie the annotation
  that draws it.

- **A note that opens an indented line is clickable, and a diagnostic on one names its
  column.** Both addresses included the whitespace in front of the note, so a click on the
  first note of an indented line resolved to nothing.

- **Clicking the clef or the key signature jumps to its source line from any staff line, not
  just the top one.** The prefix repeats at the head of every system, but only the first
  system's copy was clickable. Each repeat now carries the position of whatever put it in
  force there, so a line showing a change jumps to the change.

- **A rehearsal letter written inside an inline ending, a tuplet, a repeat or a cue is
  printed.** One chart wrote A, B, C and D and printed only A — the other three stood inside
  a `[2. … ]` and drew nothing, with no diagnostic. A letter that is written and still not
  printed now says where (`LYS4019`).

- **A `@rit` in a repeated section draws the same length both times.** From one written mark
  the first playing covered six bars and the second one, because the spanner was being ended
  by the next playing of itself. The hairpin had the same defect.

- **An `@rit` / `@accel` no longer prints on top of the chord row or the lyric row above its
  staff**, and one above a system's top staff reserves the room it is drawn in. The staff now
  reserves the room the spanner occupies, measured against LilyPond.

- **A lyrics row no longer prints inside the tab staff above it** — one quantity, how tall
  this staff is, was written in three places and two of them answered with the score's
  nominal four staff spaces.

- **A tab staff no longer repeats the markup the notation staff beside it already carries**,
  and the switch is `as numbers` vs `as full` rather than "is it a tab" — so a `@text` on a
  standalone tab appears, and an `@accent` on a numbers tab does not.

- **A TAB technique letter no longer prints on top of its own notehead.** `@tap` (T),
  `@hammeron` (H), `@pulloff` (P) and `@pluck`'s finger letter reserved a symmetric box
  around their anchor while the letter is drawn with its baseline there, so one landing
  below its note grew upward into the head. The room reserved is now the letter's own ink,
  which also gets `@pluck`'s descender right.

- **Grace notes carry what is written inside them.** A grace body is now walked like ordinary
  music, so a chord, a rest, a dot, a phrase reference and a tuplet's notes inside one are
  engraved, heard and exported instead of being dropped in silence. What is still dropped
  says so (`LYS4020`).

- **The chord completion inside `chords { }` lists the names first and the degrees after,**
  instead of interleaving them degree by degree. It now reads all seven names (`C Dm Em F G
  Am Bdim`, each with its 7th and suspensions) and then all seven degrees (`I IIm IIIm IV V
  VIm VIIdim`).

- **"Reveal in Explorer" after an export opens the file's own folder again when the path
  contains a space.** `explorer.exe` reads the raw command line rather than a parsed argv,
  and Node quotes any argument containing a space, so the `/select` switch ended up inside
  the quotes and Explorer fell back to Documents.

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
