# Lily# CLI Reference

The `lysc` command-line tool compiles `.lys` files to various output formats.

## Installation

```bash
# Build from source
dotnet build LilySharp.Cli -c Release

# Run directly
dotnet run --project LilySharp.Cli -- <command> [options] <input>
```

## Global Options

These are read before the command, so they work with every command below.

| Option | Description |
|--------|-------------|
| `-h, --help` | Show help (global, or for one command) |
| `-V, --version` | Show version |
| `--batch <list>` | Run the command over every file in `<list>`, in ONE process (`-` = stdin) |
| `-j, --parallel <n>` | Engrave `n` files of the batch at once (`0` = one per processor) |
| `--verbose, --debug` | Print full stack traces on error |

### `--batch` - many files, one process

Most of what a single `lysc` run costs is starting up. Measured on an idle machine
(ReadyToRun build, median of 7): a **one-bar** file takes 1352 ms through `lysc svg` and
245 ms through `lysc check`, while a real three-page book takes 2390 ms — about a second
of engraving and the rest fixed. `--batch` pays that once for the whole list.

```bash
lysc svg --batch books.txt            # each book -> its own .svg
lysc check --batch books.txt          # syntax-check a whole corpus
dir /b/s *.lys | lysc ly --batch -    # take the list from a pipe
```

The list is one file per line. Blank lines and lines starting with `#` are skipped, and a
**TAB** separates an input from the folder its outputs go to (a tab, not a space, so that
filenames containing spaces still work). The names are fixed as always (see
[Output File Naming](#output-file-naming)); a line's folder wins over a batch-wide `-d`:

```
score.lys                             # -> score.svg (beside it)
suite.lys<TAB>out                     # -> out/suite.svg, out/suite-<alias>.svg
```

Measured speed-up, same machine, `svg -n`, outputs verified byte-identical to the
one-process-per-file runs:

| Population | Per process | One batch | Speed-up |
|---|---|---|---|
| 120 small fixture books | 995 ms/book | 45 ms/book | **22.2x** |
| 40 books from a real bass-tab corpus | 1149 ms/book | 231 ms/book | **5.0x** |

The saving is the same ~950 ms per book either way; the ratio differs because a big book
spends more of its time actually engraving.

### `-j, --parallel <n>` - engrave several at once

Sequential by default. `-j 0` uses one worker per processor. Each file's report is still
printed as one block, so the output stays readable.

```bash
lysc svg --batch books.txt -j 0        # one per processor
lysc ly --batch books.txt -j 0
```

⚠️ **It buys much less than the core count suggests, and for `svg` it is usually the wrong
tool.** Measured, 200 books, 16 processors:

| Command | `-j 1` | `-j 0` | Wall | CPU |
|---|---|---|---|---|
| `check` | 0.6 s | 0.7 s | 1.0x | nothing to overlap — parsing is already ~3 ms/book |
| `ly` | 8.5 s | 3.8 s | **2.2x** | 6.5 → 24.1 s |
| `svg` | 9.7 s | 7.6 s | **1.3x** | 9.1 → 37.6 s |

Rendering serialises on a process-wide lock (HarfBuzz shaping is not thread-safe, so the
font is locked for the duration of each shaping call), which is why `svg` gains little
while spending five times the CPU. **On a machine you are also working on, prefer several
`lysc --batch` processes over one `--batch -j N`** — separate processes have separate locks,
and that is where the scaling actually is: a 597-book two-sided corpus comparison takes
11.1 minutes as one process per book, and 2.6 minutes as ten `--batch` processes.

⚠️ **Not available for `pdf`.** PdfSharpCore allows one font resolver per process and each
document re-points it at its own faces before drawing; sequentially that is correct (and
tested), concurrently a document would embed another's faces. `lysc pdf --batch … -j 2`
refuses rather than producing a file that is wrong under load. Split the list across
processes if you need parallel PDF.

Notes:

- `-d <folder>` sends every book's outputs to one folder; the list's TAB column does it per
  book.
- Paths in the list resolve against the **working directory**, exactly as a path typed on
  the command line does — not against the list file's own directory.
- A file that fails does not stop the rest. The run's exit code is non-zero if any file
  failed, and the closing line reports how many did.

## Commands

### svg - Export to SVG

```bash
lysc svg [options] <input.lys>
```

Writes every score: `score.svg` for the main one, `score-<alias>.svg` for each other
([Output File Naming](#output-file-naming)).

**Options:**
| Option | Description |
|--------|-------------|
| `-d, --out-dir <folder>` | Write into this folder (default: the input's folder; made if missing) |
| `--score <name>` | Write only the named score |
| `-n, --no-embed-font` | Don't embed Emmentaler font (smaller file, requires font installed) |
| `--combined` | Stack every score into ONE `score.svg` (like a `\book`) |
| `-h, --help` | Show help |

`--combined` and `--score` are mutually exclusive.

**Examples:**
```bash
lysc svg score.lys                    # Creates score.svg (+ score-<alias>.svg)
lysc svg -d out score.lys             # The same, into out/
lysc svg --no-embed-font score.lys    # Without embedded font
lysc svg --score tab score.lys        # Only score-tab.svg
lysc svg --combined multi-movement.lys # Every score stacked into one
```

### pdf - Export to PDF

```bash
lysc pdf [options] <input.lys>
```

**Options:**
| Option | Description |
|--------|-------------|
| `-d, --out-dir <folder>` | Write into this folder (default: the input's folder) |
| `--score <name>` | Write only the named score |
| `-h, --help` | Show help |

**Examples:**
```bash
lysc pdf score.lys                    # Creates score.pdf (+ score-<alias>.pdf)
lysc pdf -d out score.lys             # The same, into out/
```

The same score writes the same bytes, except for the creation date, which is the
clock. Set `SOURCE_DATE_EPOCH` (seconds since 1970, the reproducible-builds
convention) to make that a fixed moment too — then two runs are byte-identical and
can be compared.

### png - Export to PNG

```bash
lysc png [options] <input.lys>
```

**Options:**
| Option | Description |
|--------|-------------|
| `-d, --out-dir <folder>` | Write into this folder (default: the input's folder) |
| `--score <name>` | Write only the named score |
| `--scale <factor>` | Scale factor for resolution (default: 2.0 = 192 DPI) |
| `--crop` | Crop each page to its ink instead of keeping the page box |
| `-h, --help` | Show help |

A score of more than one page writes `NAME-page1.png`, `NAME-page2.png`, … (LilyPond's
own naming), where `NAME` is the score's fixed name.

**Scale Values:**
| Scale | DPI | Use Case |
|-------|-----|----------|
| 1.0 | 96 | Screen display |
| 2.0 | 192 | High-quality screen (default) |
| 3.0 | 288 | Print quality |

**Examples:**
```bash
lysc png score.lys                    # Creates score.png at 2x scale
lysc png --scale 3.0 score.lys       # High DPI output
lysc png --scale 1.0 score.lys       # Standard DPI
```

### midi - Export to MIDI

```bash
lysc midi [options] <input.lys>
```

**Options:**
| Option | Description |
|--------|-------------|
| `-d, --out-dir <folder>` | Write into this folder (default: the input's folder) |
| `--score <name>` | Write only the named score's form |
| `-h, --help` | Show help |

One file holds one form, so every score writes its form to a file of its own. Scores that
share a form (`score main`, `score main "tab"`) write the same music under their own names.
A file with no `score` block writes its primary form and names the others in a warning.

**Examples:**
```bash
lysc midi score.lys                   # Creates score.mid (+ score-<alias>.mid)
lysc midi -d out score.lys            # The same, into out/
lysc midi --score movement2 suite.lys # One named movement: suite-movement2.mid
```

### xml - Export to MusicXML

```bash
lysc xml [options] <input.lys>
```

Exports to MusicXML 4.0 partwise format, compatible with Finale, Sibelius, MuseScore, and other notation software.

**Options:**
| Option | Description |
|--------|-------------|
| `-d, --out-dir <folder>` | Write into this folder (default: the input's folder) |
| `--score <name>` | Write only the named score's form |
| `-h, --help` | Show help |

The same one-file-one-form rule as `midi` above.

**Examples:**
```bash
lysc xml score.lys                    # Creates score.xml (+ score-<alias>.xml)
lysc xml -d out score.lys             # The same, into out/
```

### ly - Export to LilyPond

```bash
lysc ly [options] <input.lys>
```

Writes a LilyPond `.ly` twin of the score — the file used to compare Lily#'s engraving
against real LilyPond. The octave marks you wrote are preserved verbatim: an
`octave absolute` source is wrapped in `\fixed c'`, a relative one in `\relative c'`, so
the pitches stay identical in LilyPond.

**Options:**
| Option | Description |
|--------|-------------|
| `-d, --out-dir <folder>` | Write into this folder (default: the input's folder) |
| `--score <name>` | Write only the named score |
| `--pin-fonts` | Write a `\paper` block pinning `property-defaults.fonts.serif` / `.sans` to LilyPond's bundled faces |
| `-h, --help` | Show help |

The same one-file-one-form rule as `midi` above.

The twin writes no `\paper` block by default. LilyPond 2.26 replaces the serif and sans
faces with generic names under its **svg backend only**, and fontconfig then picks a
machine-dependent font, so text widths measured from a twin's svg drift from its pdf.
`--pin-fonts` writes the two lines the LP-fidelity probes carry for that reason — use it
when you measure a twin through `lilypond -dbackend=svg`, not when you print it.

**Examples:**
```bash
lysc ly score.lys                     # Creates score.ly (+ score-<alias>.ly)
lysc ly -d out score.lys              # The same, into out/
lysc ly --pin-fonts score.lys         # A twin to measure through the svg backend
```

### vsqx - Export to VOCALOID

```bash
lysc vsqx [options] <input.lys>
```

Writes a VOCALOID4 sequence, `song.vsqx`. The first part carrying lyrics becomes the
vocal track (Piapro Studio and VOCALOID4+ import this directly): kana lyrics get VOCALOID
phonemes, ties merge, and rests become gaps.

**Options:**
| Option | Description |
|--------|-------------|
| `-d, --out-dir <folder>` | Write into this folder (default: the input's folder) |
| `-h, --help` | Show help |

**Examples:**
```bash
lysc vsqx song.lys                    # Creates song.vsqx
```

### import - Import MusicXML

```bash
lysc import [options] <input.(xml|musicxml|mxl)>
```

Reads a MusicXML score (or an `.mxl` zip) and writes an idiomatic Lily# source file,
`song.lys`, that renders the same music. Import is an opinionated, non-unique mapping: the result is a
faithful **starting point to edit**, not a byte round-trip. Anything not representable is
reported, never emitted wrong.

**Options:**
| Option | Description |
|--------|-------------|
| `-d, --out-dir <folder>` | Write into this folder (default: the input's folder) |
| `-r, --relative` | Emit relative-octave notes (default: absolute) |
| `-h, --help` | Show help |

**Examples:**
```bash
lysc import song.xml                  # Creates song.lys
lysc import -d books song.mxl         # From a compressed MusicXML, into books/
lysc import --relative song.xml       # Relative-octave output
```

### octave - Convert between absolute and relative octaves

```bash
lysc octave (--absolute | --relative) [options] <input.lys>
```

Rewrites a whole file into the other octave mode, keeping every note at the pitch it sounds
now. The `octave absolute` / `octave relative` directives are replaced (one file-level
`octave absolute`, or none for relative) and each note's `'` / `,` marks are recomputed;
nothing else in the file changes. In absolute mode a chord's or arpeggio's group mark
(`<c e g>'`) is folded into its members. The result is compiled and compared note by note
with the original before anything is written.

A file is left alone, with the reason printed, when it has syntax errors, pulls in another
file with `using`, or contains a note no score plays (a section missing from the form, a
file with no `score`) — nothing says what octave such a note means.

**Options:**
| Option | Description |
|--------|-------------|
| `-a, --absolute` | Convert to `octave absolute` (bare `c` = C4, or the part's `octave N`) |
| `-r, --relative` | Convert to relative octaves (the default mode) |
| `-i, --in-place` | Overwrite the input |
| `-d, --out-dir <folder>` | Write `song.lys` into this folder |
| `-h, --help` | Show help |

Without `-i` or `-d` the result goes to stdout.

**Examples:**
```bash
lysc octave --absolute song.lys              # Prints the absolute version
lysc octave --relative -i song.lys           # Converts in place
lysc octave --absolute -d absolute song.lys  # Writes absolute/song.lys
```

### harmonize - Suggest a chord track

```bash
lysc harmonize <input.lys>
```

Reads the melody and key and prints a `chords harmony { … }` part — one diatonic chord per
measure — to stdout. A starting point to drop into your section (referenced with
`staff <melody> with chords harmony`) and edit.

**Options:**
| Option | Description |
|--------|-------------|
| `-h, --help` | Show help |

**Examples:**
```bash
lysc harmonize song.lys               # Prints a chords part
```

### check - Syntax Check

```bash
lysc check <input.lys>
```

Validates syntax without producing output. Reports errors with line and column numbers.

**Options:**
| Option | Description |
|--------|-------------|
| `-p, --pitches` | Also print each note's resolved absolute pitch (written → resolved), so relative-octave mistakes are visible before rendering |
| `--todo-as-error` | Report each `@todo` mark (LYS4026) as an error, so the check fails while any is left |
| `--no-todo` | Do not report `@todo` marks (a malformed or repeated one is still reported) |
| `-h, --help` | Show help |

**Exit Codes:**
| Code | Meaning |
|------|---------|
| 0 | No errors |
| 1 | Syntax errors found (or a `@todo` left, with `--todo-as-error`) |

**Examples:**
```bash
lysc check score.lys
# Output: No errors. (12 measures, 48 notes)
lysc check score.lys --pitches    # also print each note's resolved absolute pitch
lysc check score.lys --todo-as-error    # fail while a @todo mark is left
```

### layout - Layout Summary

```bash
lysc layout [--all] <input.lys>
```

Prints a plain-text summary of the engine's layout decisions to stdout: the staves,
the meter (with any mid-piece changes), the system count, the page count with the
number of systems on each page, which bars landed in each system, and where the line
breaker split the music. This exposes the facts a source file does not reveal — where
breaks actually fell, how the bars are distributed, how the systems fell onto pages —
so you can verify the layout without rendering an image. (For resolved pitches, use
`check --pitches`.)

**Options:**
| Option | Description |
|--------|-------------|
| `--all` | Report every score block (default: first only) |
| `-h, --help` | Show help |

By default the first score block is reported; `--all` reports every score block.

Consecutive systems with the same bar count collapse into one line, so a long
score (real songs run to 100+ bars) stays a few lines; irregular systems (e.g. a
short final line) print on their own. Explicit `break`s are listed separately
(summarized when there are many) so you can confirm the author's breaks landed.

**Example:**
```bash
lysc layout score.lys
# score main "demo"
#   staves: treble, bass  |  time 4/4  |  10 systems, 40 bars
#   pages: 2  |  systems per page: 6, 4
#   system 1: bars 1-4     (4 bars)
#   systems 2-3: 5 bars each (bars 5-14)
#   systems 4-5: 4 bars each (bars 15-22)
#   systems 6-8: 5 bars each (bars 23-37)
#   system 9: bars 38-39   (2 bars)
#   system 10: bar 40      (1 bar)
#   forced breaks after bar: 37, 39
```

## Output File Naming

An output is always named for its book; no option names a file. Every score is written
unless `--score` picks one, and `-d <folder>` chooses where (default: the input's folder):

| The score in `song.lys` | Output (`svg`; the others the same with their extension) |
|---|---|
| `score main { … }` — the main score | `song.svg` |
| `score main "tab" { … }` — the same form, another alias | `song-tab.svg` |
| `score coda { … }` — another form | `song-coda.svg` |
| no `score` block at all | `song.svg` |

Two scores that would get one name (two unlabelled `score main`) are refused rather than
written over each other — give one an alias. `-o/--output`, an output argument and
`--all` are gone: an old script using them is told what to write instead.

## Font Requirements

SVG output embeds the Emmentaler music font by default. For PNG and PDF output, the font files must be in one of these locations:

1. `fonts/` directory next to the executable
2. `../fonts/` relative to the executable
3. The `--no-embed-font` flag (SVG only) produces smaller files but requires the Emmentaler font to be installed on the viewing system
