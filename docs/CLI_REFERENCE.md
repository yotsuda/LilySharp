# Lily# CLI Reference

The `lysc` command-line tool compiles `.lys` files to various output formats.

## Installation

```bash
# Build from source
dotnet build LilySharp.Cli -c Release

# Run directly
dotnet run --project LilySharp.Cli -- <command> [options] <input>
```

## Commands

### svg - Export to SVG

```bash
lysc svg [options] <input.lys> [output.svg]
```

**Options:**
| Option | Description |
|--------|-------------|
| `-o, --output <file>` | Output file path |
| `--no-embed-font, -n` | Don't embed Emmentaler font (smaller file, requires font installed) |
| `-h, --help` | Show help |

**Examples:**
```bash
lysc svg score.lys                    # Creates score.svg
lysc svg score.lys output.svg         # Specify output name
lysc svg -o sheet.svg score.lys       # With -o flag
lysc svg --no-embed-font score.lys    # Without embedded font
```

### pdf - Export to PDF

```bash
lysc pdf [options] <input.lys> [output.pdf]
```

**Options:**
| Option | Description |
|--------|-------------|
| `-o, --output <file>` | Output file path |
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
| `-h, --help` | Show help |

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
| `-h, --help` | Show help |

**Examples:**
```bash
lysc midi score.lys                   # Creates score.mid
lysc midi -o audio.mid score.lys      # With -o flag
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
| `-h, --help` | Show help |

**Examples:**
```bash
lysc xml score.lys                    # Creates score.xml
lysc xml -o export.xml score.lys      # With -o flag
```

### check - Syntax Check

```bash
lysc check <input.lys>
```

Validates syntax without producing output. Reports errors with line and column numbers.

**Exit Codes:**
| Code | Meaning |
|------|---------|
| 0 | No errors |
| 1 | Syntax errors found |

**Examples:**
```bash
lysc check score.lys
# Output: No errors. (12 measures, 48 notes)
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
