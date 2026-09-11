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
**TAB** separates an input from the output it should be written to (a tab, not a space, so
that filenames containing spaces still work):

```
score.lys                             # -> score.svg
suite.lys<TAB>out/suite-a.svg         # -> out/suite-a.svg
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

- `--batch` cannot be combined with `-o/--output` — one path cannot name many files. Put
  the output in the list instead.
- Paths in the list resolve against the **working directory**, exactly as a path typed on
  the command line does — not against the list file's own directory.
- A file that fails does not stop the rest. The run's exit code is non-zero if any file
  failed, and the closing line reports how many did.

## Commands

### svg - Export to SVG

```bash
lysc svg [options] <input.lys> [output.svg]
```

**Options:**
| Option | Description |
|--------|-------------|
| `-o, --output <file>` | Output file path |
| `-n, --no-embed-font` | Don't embed Emmentaler font (smaller file, requires font installed) |
| `--all` | Generate all render blocks as separate SVG files |
| `--combined` | Stack all render blocks into ONE SVG (like a `\book`) |
| `--score <name>` | Render the named score block (default: the first) |
| `-h, --help` | Show help |

`--all` and `--combined` are mutually exclusive.

**Examples:**
```bash
lysc svg score.lys                    # Creates score.svg
lysc svg score.lys output.svg         # Specify output name
lysc svg -o sheet.svg score.lys       # With -o flag
lysc svg --no-embed-font score.lys    # Without embedded font
lysc svg --score greensleeves-grid greensleeves.lys
lysc svg --all multi-movement.lys     # One .svg per score block
lysc svg --combined multi-movement.lys # All of them stacked into one
```

### pdf - Export to PDF

```bash
lysc pdf [options] <input.lys> [output.pdf]
```

**Options:**
| Option | Description |
|--------|-------------|
| `-o, --output <file>` | Output file path |
| `--score <name>` | Render the named score block (default: the first) |
| `-h, --help` | Show help |

**Examples:**
```bash
lysc pdf score.lys                    # Creates score.pdf
lysc pdf -o sheet.pdf score.lys       # With -o flag
```

### png - Export to PNG

```bash
lysc png [options] <input.lys> [output.png]
```

**Options:**
| Option | Description |
|--------|-------------|
| `-o, --output <file>` | Output file path |
| `--scale <factor>` | Scale factor for resolution (default: 2.0 = 192 DPI) |
| `--crop` | Crop each page to its ink instead of keeping the page box |
| `--score <name>` | Render the named score block (default: the first) |
| `-h, --help` | Show help |

A score of more than one page writes `BASE-page1.png`, `BASE-page2.png`, … (LilyPond's
own naming).

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
lysc midi [options] <input.lys> [output.mid]
```

**Options:**
| Option | Description |
|--------|-------------|
| `-o, --output <file>` | Output file path |
| `--score <name>` | Write the named score's form (default: the first) |
| `--all` | Write every score to its own `.mid` file |
| `-h, --help` | Show help |

One file holds one form. A `.lys` declaring several movements therefore writes one of
them and names the rest in a warning; `--score` picks one, `--all` writes them all.
`--all` and `--score` are mutually exclusive.

**Examples:**
```bash
lysc midi score.lys                   # Creates score.mid
lysc midi -o audio.mid score.lys      # With -o flag
lysc midi --score movement2 suite.lys # One named movement
lysc midi --all suite.lys             # Every movement, one file each
```

### xml - Export to MusicXML

```bash
lysc xml [options] <input.lys> [output.xml]
```

Exports to MusicXML 4.0 partwise format, compatible with Finale, Sibelius, MuseScore, and other notation software.

**Options:**
| Option | Description |
|--------|-------------|
| `-o, --output <file>` | Output file path |
| `--score <name>` | Write the named score's form (default: the first) |
| `--all` | Write every score to its own `.xml` file |
| `-h, --help` | Show help |

The same one-file-one-form rule as `midi` above.

**Examples:**
```bash
lysc xml score.lys                    # Creates score.xml
lysc xml -o export.xml score.lys      # With -o flag
lysc xml --all suite.lys              # Every movement, one file each
```

### ly - Export to LilyPond

```bash
lysc ly [options] <input.lys> [output.ly]
```

Writes a LilyPond `.ly` twin of the score — the file used to compare Lily#'s engraving
against real LilyPond. The octave marks you wrote are preserved verbatim: an
`octave absolute` source is wrapped in `\fixed c'`, a relative one in `\relative c'`, so
the pitches stay identical in LilyPond.

**Options:**
| Option | Description |
|--------|-------------|
| `-o, --output <file>` | Output file path |
| `--score <name>` | Write the named score's form (default: the first) |
| `--all` | Write every score to its own `.ly` file |
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
lysc ly score.lys                     # Creates score.ly
lysc ly -o export.ly score.lys        # With -o flag
lysc ly --all multi-movement.lys      # Every movement, one file each
lysc ly --pin-fonts score.lys         # A twin to measure through the svg backend
```

### vsqx - Export to VOCALOID

```bash
lysc vsqx <input.lys> [output.vsqx]
```

Writes a VOCALOID4 sequence. The first part carrying lyrics becomes the vocal track
(Piapro Studio and VOCALOID4+ import this directly): kana lyrics get VOCALOID phonemes,
ties merge, and rests become gaps.

**Options:**
| Option | Description |
|--------|-------------|
| `-o, --output <file>` | Output file path |
| `-h, --help` | Show help |

**Examples:**
```bash
lysc vsqx song.lys                    # Creates song.vsqx
```

### import - Import MusicXML

```bash
lysc import [options] <input.(xml|musicxml|mxl)> [output.lys]
```

Reads a MusicXML score (or an `.mxl` zip) and writes an idiomatic Lily# source file that
renders the same music. Import is an opinionated, non-unique mapping: the result is a
faithful **starting point to edit**, not a byte round-trip. Anything not representable is
reported, never emitted wrong.

**Options:**
| Option | Description |
|--------|-------------|
| `-o, --output <file>` | Output file path (default: input with `.lys`) |
| `-r, --relative` | Emit relative-octave notes (default: absolute) |
| `-h, --help` | Show help |

**Examples:**
```bash
lysc import song.xml                  # Creates song.lys
lysc import song.mxl song.lys         # From a compressed MusicXML
lysc import --relative song.xml       # Relative-octave output
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
| `-h, --help` | Show help |

**Exit Codes:**
| Code | Meaning |
|------|---------|
| 0 | No errors |
| 1 | Syntax errors found |

**Examples:**
```bash
lysc check score.lys
# Output: No errors. (12 measures, 48 notes)
lysc check score.lys --pitches    # also print each note's resolved absolute pitch
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

If no output file is specified, the output file uses the input filename with the appropriate extension:

| Command | Input | Default Output |
|---------|-------|----------------|
| `lysc svg score.lys` | score.lys | score.svg |
| `lysc pdf score.lys` | score.lys | score.pdf |
| `lysc png score.lys` | score.lys | score.png |
| `lysc midi score.lys` | score.lys | score.mid |
| `lysc xml score.lys` | score.lys | score.xml |

## Font Requirements

SVG output embeds the Emmentaler music font by default. For PNG and PDF output, the font files must be in one of these locations:

1. `fonts/` directory next to the executable
2. `../fonts/` relative to the executable
3. The `--no-embed-font` flag (SVG only) produces smaller files but requires the Emmentaler font to be installed on the viewing system
