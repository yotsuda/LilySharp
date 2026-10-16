# Lily# VS Code Extension

Write sheet music as plain text and watch it engrave beside you. A `.lys` file gets a
live score preview that plays back, and the editor gets completion, diagnostics,
formatting and navigation over it — the compiler and its language server are bundled, so
there is nothing else to install.

The layout engine is in part a **port of LilyPond**, the GNU music typesetter: beam
quanting, slur and tie scoring, skylines, springs and page breaking are modified
translations of LilyPond's own algorithms rather than independent approximations. So the
engraving is derived from LilyPond's — without LilyPond, or anything else, having to be
installed. The language, by contrast, is deliberately not LilyPond's.

**Version 0.12.0** — the bundled language server and the `lysc` compiler carry the
same number. See the [changelog](https://github.com/yotsuda/LilySharp/blob/master/editors/vscode/CHANGELOG.md) for what is in this release.

## Features

### Editor Features

| Feature | Description |
|---------|-------------|
| **Syntax Highlighting** | Full semantic highlighting for pitches, dynamics, articulations |
| **Diagnostics** | Real-time error and warning display |
| **Code Completion** | Auto-complete for keywords, pitches, durations, dynamics |
| **Hover Information** | Documentation on hover for syntax elements |
| **Document Outline** | Navigate score structure in the outline view |
| **Go to Definition** | Jump to variable declarations (F12) |
| **Find References** | Find all uses of a variable (Shift+F12) |
| **Rename Symbol** | Rename variables across document (F2) |
| **Code Folding** | Collapse music blocks and structures |
| **Document Formatting** | Auto-format with configurable indentation |
| **Code Actions** | Quick fixes and refactoring suggestions |
| **Signature Help** | Parameter hints while typing keywords |
| **Typing Aids** | An octave mark, duration digit, dot, `\` or `@` typed anywhere on a note lands in its slot on that note; a digit always leaves a valid duration (`c1` + `2` → `c2`, `c4` + `6` → `c64`; 5, 7, 9 and 0 leave the note unchanged); chords, slurs, beams and ties keep their two ends paired. Works per cursor with several cursors. Turn it all off with `"lilysharp.typingAids.enabled": false` |
| **Document Highlight** | Highlight all occurrences of selected variable |
| **Split Sections to Match a Part** | One part has cut a section into several (`vn1: section A` + `section B`) while the others still write it whole: cuts the others at the same bars into the same sections (chord rows and lyrics tracks too) and makes the forms play `A B` where they played `A`. Palette command, and a quick fix on the "not the same length" warning. One confirmation shows the plan; it is checked first (every cut part sounds and counts as before) and applied as one edit. A tie, slur or other span across a cut, or a cut mid-bar, is reported instead. Files grouped by part |
| **Section CodeLens** | Over a section's first declaration: its length, who writes it (parts, chord rows, lyrics tracks — by name, or counted when many) — or each length with who writes it when they disagree — and how often each form names it. A later declaration shows a line only when its length differs from what most of the others write. Click to list everything that writes it |

### Stepping and Audition

| Key | What it does |
|-----|--------------|
| `Ctrl+Shift+Up` / `Ctrl+Shift+Down` on a **note** | Moves it to the next note of the key in force, spelled as the signature spells it: `key c major` walks the white keys (`c` → `d` → `e` → `f`), `key f major` steps `a` → `bes` → `c`, `key g major` `e` → `fis` → `g`. A minor key walks its signature and its leading tone too (`key a minor`: `g` → `gis` → `a`), a church mode its own signature, `key custom …` the notes it writes. A note off the key steps to the nearest note of the key that way (`key c major`: `cis` Up → `d`, Down → `c`). The note keeps its pitch: when the new letter would land in another octave (`b'` → `c''`; in relative octaves `f b` → `f c'`) the octave marks are corrected. Inside `< >` / `<< >>` only the member under the caret moves; on the chord's `>` or its duration the chord **inverts**: Up raises its lowest member an octave (`<c' e' g'>` → `<c'' e' g'>`), Down lowers its highest, and the other members keep their pitches (`Ctrl+Shift+PageUp` / `PageDown` there move the whole chord: `<c' e' g'>'`). With a selection, every note in it steps. A scale degree or a quarter tone moves by octave only |
| `Ctrl+Shift+PageUp` / `Ctrl+Shift+PageDown` on a **note** | Adds one `'` (Up) or `,` (Down) after the pitch — or takes away one of the other mark if the note has it. Inside `< >` / `<< >>` only the member under the caret moves; on the chord's `>` or its duration, the marks after the bracket move the whole chord. With a selection, every note in it steps. It is a plain mark edit: in relative octave mode the notes after it follow |
| Either pair on a **chord** — an `@chord(…)` or a `chords` row entry | A chord draws a diagram only where its shape is written — unless the score's layout says `chordDiagrams all` (or `chordDiagrams guitar all`), where every chord draws, its usual shape when none is written; there Up on a name alone writes the NEXT shape and Down at the usual shape does nothing. On a name alone, Up writes its usual shape — LilyPond's predefined one, else Lily#'s first — and the diagram appears: `@chord(Cm7)` → `@chord(Cm7 x35343)`, `G` → `G(320003)`. Further Ups (and Downs) walk Lily#'s shapes, frets 10–15 included — written with a `-` on each side of a two-digit fret (`@chord(Cm 8xx888)` → `@chord(Cm 8xx88-11)`; frets 10, 9, 9 are `10-9-9`), and one character per string whenever every fret is 9 or less; Down back at the usual shape removes it, and the diagram goes; Down on a name alone does nothing. With several shapes written (`F(133211 2010)`) the one for the chord's tuning steps. A shape with muted strings steps from the shape it mutes. The status bar says where it is (`Cm7: shape 4 of 33 (x3x546)`). Stretch shapes — fretted frets five apart, hard to play — are left out unless `"lilysharp.chordShapes.includeStretch": true`; a stretch shape already written steps to the shapes that sort around it. Hover a chord with no shape to see what Up would write |
| Either pair on a **`tempo`** — the caret anywhere from `tempo` to its last value | `Ctrl+Shift+Up` / `Ctrl+Shift+Down` change the metronome number by 1, `Ctrl+Shift+PageUp` / `Ctrl+Shift+PageDown` by 10 (`tempo "Allegro" 4 = 120` → `130`). Only the number of beats per minute steps, never the beat unit or a `swing 16`; it stops at 1. A tempo with no number (`tempo "Allegro"`, `tempo swing`) steps nothing, and the status bar says so |
| `Ctrl+Shift+Up` / `Ctrl+Shift+Down` on a **`key`** — the caret anywhere from `key` to its mode | Moves it a fifth round the circle, one sharp more (Up) or one flat more (Down): `key c major` → `key g major` → `key d major`, Down → `key f major`. The mode stays (`a minor` → `e minor`, `d dorian` → `a dorian`); it stops at seven sharps or flats; flats are written the file's way (`ees` / `es`). Only the signature changes, not the notes after it. `key custom …` steps nothing |
| `Ctrl+Shift+PageUp` / `Ctrl+Shift+PageDown` on a **`key`** | PageUp switches to the **parallel** key — the same tonic, the other mode (`key c major` ⇄ `key c minor`); PageDown to the **relative** key — the same signature, the other mode (`key c major` ⇄ `key a minor`, `key g minor` ⇄ `key bes major`). Major and minor only; a church mode (`dorian` …) says so. Only the signature changes, not the notes |
| `Ctrl+Shift+Up` / `Ctrl+Shift+Down` on a **`clef`** | Steps it to the clef for higher (Up) or lower (Down) notes: `clef bass` → `clef tenor` → `clef alto` → `clef treble`, and stops at either end. Works on a part header's `clef` (`part lh { clef bass }`) and on a `clef` inside music. Any other clef steps from where it sorts (`clef treble_8`: Up → `alto`, Down → `tenor`; `clef bass_8` Up → `bass`). `Ctrl+Shift+PageUp` / `PageDown` move `treble` and `bass` an octave: `treble_8` ⇄ `treble` ⇄ `treble^8`, `bass_8` ⇄ `bass` (inside music only `treble` ⇄ `treble_8`, the one octave clef music takes). Only the clef changes, not the notes |
| `Ctrl+Shift+Up` / `Ctrl+Shift+Down` on a **dynamic** — the caret after its `@` | Steps it a level, `pp` → `p` → `mp` → `mf` → `f` → `ff` (`ppppp` to `fffff`; `Ctrl+Shift+PageUp` / `PageDown` do the same). On `@cresc` / `@decresc` / `@dim`, Up writes `@cresc` and Down `@decresc` — a second press stays. `@sfz`, `@fp` and the like step nothing |
| `Ctrl+Shift+Up` / `Ctrl+Shift+Down` on an **articulation** — the caret after its `@` | Steps it along its group (either pair), Up towards the up-bow, the hammer-on, the longer, the stronger, the one above; it stops at either end: `@downBow` → `@upBow`, `@pullOff` → `@hammerOn`, `@staccatissimo` → `@staccato` → `@portato` → `@tenuto`, `@accent` → `@marcato`, `@shortFermata` → `@fermata` → `@longFermata`, `@mordent` → `@prall`, `@reverseTurn` → `@turn`, `@trill` → `@pralltriller`, `@heel` → `@toe`, `@scoop` → `@plop`, `@fall` → `@doit`, `@breath` → `@caesura` (Down goes back). A `.up` / `.down` stays. `@tap`, `@harmonic` and the other marks in no group step nothing, and the status bar says so |
| `Ctrl+Shift+Up` / `Ctrl+Shift+Down` on a **`time`** | Steps the beats by 1 (`time 3/4` → `time 4/4`, never below 1); `Ctrl+Shift+PageUp` / `PageDown` walk the common meters by the length of the bar — `3/8` `2/4` `6/8` `3/4` `2/2` `4/4` `9/8` `12/8`, so `6/8` ⇄ `3/4` and `2/2` ⇄ `4/4` are one press apart; a meter off the list steps to the next longer or shorter one. An additive meter (`3+2/8`) and `time none` step nothing |
| `Ctrl+Shift+Up` / `Ctrl+Shift+Down` on a part's **`octave N`** | Steps N by 1, 0 to 8 (either pair) — every bare note of the part moves with it |
| `Ctrl+Shift+Up` / `Ctrl+Shift+Down` on a **`transpose`** (a part's or the top-level one) | Moves the target a semitone (`transpose d` → `ees`), spelled so the transposed key has the fewest sharps or flats (from the file's first `key`; a tie goes to the flat); the status bar says how far (`up 3 semitones`) and, with a key, what it becomes. `Ctrl+Shift+PageUp` / `PageDown` add or take an octave mark (`d` → `d'`). `transpose` still takes a pitch, not a number: a number would leave the spelling open (+1 from C major is D♭ or C♯) |
| Either pair on the file's **`shift`** | Steps it a semitone (`shift +1` → `shift +2`; −11 to +11) and the status bar names the key the piece lands in (`shift +1: key c major -> des major`). The `+1` the completion leaves selected steps too: a selection that lies within one steppable thing (a tempo's number, a dynamic) steps that thing. A top-level `shift N` moves the whole piece — every part, its own `transpose` included, the key signatures and the chord rows — the karaoke move |
| `Ctrl+Shift+Up` / `Ctrl+Shift+Down` **anywhere else** | Nothing on Windows and macOS (VS Code binds these keys to nothing in the editor there); on Linux, VS Code's own *Add Cursor Above / Below*, which these keys are the second binding of |
| `Ctrl+Shift+PageUp` / `Ctrl+Shift+PageDown` **anywhere else** | VS Code's own *Move Editor Left / Right in Group* on Windows and Linux; nothing on macOS |

The chord's tuning is the diagram's: the score's `layout { chordDiagrams TUNING [all] }`, else the
part's instrument when it is fretted (for a `chords` row, the staff it stands above), else the
guitar — read in the first score that renders the chord; the status bar says when another
score draws it on another tuning.

With a preview open, edits sound through the preview's synth: the step's result, the
note (or chord, or `@chord` shape) the caret lands on when you move it — once per
note, not on every arrow-key repeat — and a note as you type it, once its pitch and
octave marks are in. Switch this off with `"lilysharp.audition.enabled": false`; `Alt+P`
and `Alt+M` keep working either way. Nothing sounds while no preview is open.

### Playback

The preview's synth is a **SoundFont synthesizer** (spessasynth_lib, in an audio worklet):
each part plays the General MIDI instrument its `midiInstrument` or preset names, as the
exported MIDI does — drums on the drum channel, a quarter tone as a pitch bend — and the
step keys and the caret strike the part's instrument too. A General MIDI sound font is
bundled; `"lilysharp.playback.soundFont"` names an `.sf2`, `.sf3` or `.dls` of your own and
takes effect in open previews. While no font is loaded the preview plays plain oscillator
tones instead. The sound is allowed to start only once you have clicked or typed in the
preview itself (the browser's autoplay rule); the status bar says so when a strike comes
first.

To use other keys, open **Keyboard Shortcuts** (`Ctrl+K Ctrl+S`), search for
`lilysharp.stepSemitoneUp` / `lilysharp.stepSemitoneDown` (a note of the key; the ids keep an earlier name) or `lilysharp.stepUp` /
`lilysharp.stepDown` (octave), and bind them where you like — and, on Linux, if you want
`Ctrl+Shift+Up/Down` back as plain *Add Cursor Above / Below*, remove the two bindings there
(Mac: the defaults are the same keys — the Control key — not yet tried on a Mac).

### Semantic Token Colors

The extension provides custom semantic highlighting:

- **Pitches** (c, d, e, f, g, a, b): Teal
- **Articulations** (@staccato, @accent): Yellow
- **Dynamics** (@p, @f, @ff): Purple

Colors can be customized in settings.

## Requirements

None. Each platform's package bundles its own .NET runtime, so nothing has to be
installed alongside it — install the extension, open a `.lys` file, and the language
server starts.

(Building from source needs the .NET 10 SDK and Node.js 18+.)

## Installation

### From VS Code

Open the Extensions view (`Ctrl+Shift+X` / `Cmd+Shift+X`), search for **Lily#**, and
install `yotsuda.lilysharp`. Then open any `.lys` file. VS Code picks the package
built for your platform; nothing else is needed.

### From a `.vsix`

Download the `.vsix` from
[Releases](https://github.com/yotsuda/LilySharp/releases), then Extensions view →
`…` → *Install from VSIX…*, or:

```bash
code --install-extension lilysharp-*.vsix
```

### From Source

1. Build the LSP server:
   ```bash
   cd ../..
   dotnet build LilySharp.Lsp
   ```

2. Build the extension:
   ```bash
   npm install
   npm run compile
   ```

3. Option A - Development:
   - Open VS Code in this folder
   - Press F5 to launch Extension Development Host

4. Option B - Install locally:
   ```bash
   # Create VSIX package
   npm install -g vsce
   vsce package
   
   # Install the generated .vsix file
   code --install-extension lilysharp-*.vsix
   ```

### Configuration

Configure the path to the language server in VS Code settings:

```json
{
    "lilysharp.serverPath": "/path/to/lilysharp-lsp"
}
```

If not set, the extension looks for `lilysharp-lsp` in PATH.

## Usage

1. Create a file with `.lys` extension
2. Start typing Lily# notation
3. Use `Ctrl+Space` for completion suggestions
4. Hover over elements for documentation
5. Press `Ctrl+Shift+O` to jump to a symbol, or open the **Outline** view in the Explorer sidebar for the score structure
6. Use `F12` to go to variable definition
7. Use `Shift+Alt+F` to format document
8. The preview opens beside the file by itself (`lilysharp.preview.openOnOpen`; `Ctrl+K V` opens it by hand) and follows whichever `.lys` is in front — the lock in its title bar keeps it on one file. Then **hold `Alt+P`** to hear the note under the caret, or press `Alt+M` to play the measure the caret is in (the preview panel is the synth — a sound font, see [Playback](#playback)); the preview's ▶ plays the score
9. Press `Ctrl+Shift+Up` / `Ctrl+Shift+Down` on a note to move it to the next note of the key (`Ctrl+Shift+PageUp` / `PageDown`: an octave), or on `@chord(Cm7)` (or a chords-row `Cm7`) to add a chord diagram and try the next shape — see [Stepping and Audition](#stepping-and-audition)

## Example

```lilysharp
title "Example"
tempo 120
time 4/4
key c major

// A reusable phrase, referenced by its bare name.
phrase theme { c4 d e f | g2 g | }

part melody { clef treble }
section Main { melody { theme } }
form { Main }
score { staff melody }
```

## Troubleshooting

### Language server not starting

1. Check the Output panel (View → Output → Lily# Language Server) — the first lines
   name the server it chose and how it launched it.
2. Enable tracing: set `lilysharp.trace.server` to `verbose`
3. **Built from source?** A plain `dotnet publish` is framework-dependent, so that
   server runs via `dotnet` and needs the .NET 10 runtime on `PATH`
   (`dotnet --list-runtimes` should list a `Microsoft.NETCore.App 10.*`). Released
   packages are self-contained and do not. Check `lilysharp.serverPath` if you set it.

### No syntax highlighting

1. Ensure file has `.lys` extension
2. Check that the extension is activated (look for Lily# in status bar)

## Development

### Build

```bash
npm install
npm run compile
```

### Watch Mode

```bash
npm run watch
```

### Debug

1. Open this folder in VS Code
2. Press F5 to launch Extension Development Host
3. Set breakpoints in TypeScript files

## License

GPL-3.0-or-later. The extension bundles the Lily# language server, the
Emmentaler music font (GPL/OFL dual license) and MIT-licensed libraries;
see [LICENSE](https://github.com/yotsuda/LilySharp/blob/master/LICENSE) and
[THIRD-PARTY-NOTICES](https://github.com/yotsuda/LilySharp/blob/master/THIRD-PARTY-NOTICES.md)
in the repository.

**Corresponding source.** This extension and the language server it bundles are
built from <https://github.com/yotsuda/LilySharp>; the complete corresponding
source for a published version is the tagged commit it was built from.

**LilyPond.** Lily# is an independent project, not affiliated with or endorsed by
the LilyPond project. Parts of its engraving engine are ported from LilyPond (GPL
v3 or later) and carry its copyright notices; the full list is in
[LILYPOND-ATTRIBUTION](https://github.com/yotsuda/LilySharp/blob/master/LILYPOND-ATTRIBUTION.md).