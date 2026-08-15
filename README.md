# Lily#

A modern music notation compiler with real-time preview support.

## Overview

Lily# is a new music notation language, designed for:
- **Explicit over implicit**: Clear, readable syntax
- **Completion-friendly**: IDE-first design with full LSP support
- **Single-pass incremental compilation**: Using Roslyn-style Red-Green tree pattern

## Project Structure

```
LilySharp/
├── LilySharp.Core/          # Core compiler (lexer, parser, semantic analysis)
│   ├── Parser/              # Lexer and recursive descent parser
│   ├── Syntax/              # Syntax kinds, green/red tree nodes
│   ├── Semantics/           # Duration calculation, measure validation
│   └── Midi/                # MIDI export
├── LilySharp.Cli/           # Command-line interface
├── LilySharp.Lsp/           # Language Server Protocol implementation
├── LilySharp.Tests/         # Unit + SVG-snapshot tests (1900+ tests)
├── editors/
│   └── vscode/              # VS Code extension
├── samples/                 # Example .lys files
└── docs/
    └── GRAMMAR.md           # Complete grammar specification
```

## Language Features

### Basic Syntax

```lilysharp
// Comments
title "Happy Birthday"
composer "Traditional"
tempo 120
time 3/4
key g major

part melody { clef treble }       // declare each part; clef lives here
section Main { melody { c4 d e f | g2 g | } }
form main { Main }                // print/playback order of sections
score main "out" { staff melody }      // one or more render blocks
```

> Lily# is **not** LilyPond: `\relative`, `<< … \\ … >>`, `\new Staff`, `\version`
> and other backslash constructs are rejected. The compressed grammar in
> [`docs/GRAMMAR_FOR_LLM.md`](docs/GRAMMAR_FOR_LLM.md) is the canonical single-file spec.

### Pitch Names

Standard pitch names with accidentals:
- `c`, `d`, `e`, `f`, `g`, `a`, `b`
- Sharp: `cis`, `dis`, `fis`, etc.
- Flat: `des`, `ees`, `bes`, etc.
- Double: `cisis`, `deses`, etc.

### Durations

- `1` = whole, `2` = half, `4` = quarter, `8` = eighth, etc.
- Dots: `4.` = dotted quarter, `4..` = double-dotted

### Octave Marks

- `c'` = one octave up
- `c''` = two octaves up
- `c,` = one octave down

### Articulations and Dynamics

```lilysharp
c4@staccato d@accent e@fermata f@tenuto
c4@p d@f e@ff f@mf
```

### Tuplets

```lilysharp
tuplet 3/2 { c8 d e }  // Triplet
tuplet 5/4 { c16 d e f g }  // Quintuplet
```

### Grace Notes

```lilysharp
grace { c16 d } e4  // Grace notes before e
```

### Slurs (notes or chords)

```lilysharp
c4( d e f)          // slur over single notes
<c e>4( <d f>)      // a slur can bind chords too
```

### Lyrics

Lyrics live inside a section and align to that part's notes; `-` joins syllables of
one word and `|` mirrors the music's barlines.

```lilysharp
section Main {
    melody { c4 d e f | g2 g | }
    lyrics { Hap- py birth- day | to you | }
}
```

### Lead sheets (chords and/or lyrics, no staff)

A `chords NAME { … }` and/or `lyrics NAME { … }` part, placed in a `score` with
`chords NAME` / `lyrics NAME` (instead of `staff NAME`), renders without a staff: a
grid of measure barlines with the chord symbols between them (at their timing) and
the lyrics below. Chord entries are `root[duration][:quality][/bass]`.

```lilysharp
section Main {
    chords prog  { c2 g:7 | a:m f | c1 :| }
    lyrics words { Twin- kle | lit- tle | star | }
}
form main { Main }
score main "sheet" { chords prog lyrics words }
```

### Repeats and Alternatives

Volta repeats use the symbolic `|: … :|` barlines with inline volta endings
`[1. …] [2. …]`. The repeat count defaults to 2 (or the highest volta number);
state it explicitly with `|: … :|*N`.

```lilysharp
{ |: c4 d e f | [1. g2 g | ] :| [2. a2 a | ] }
```

(The `repeat` keyword remains for `unfold` / `percent` / `tremolo`, which are not
volta repeats.)

### Parallel Voices (one staff)

```lilysharp
voice { c'2 d } voice { e2 f }   // each voice { } is a simultaneous voice
```

### Named music (phrases)

Named music is declared with `phrase` and referenced by its bare name:

```lilysharp
phrase motif { c4 d e f }

part melody { clef treble }
section Main { melody { motif g2 g | } }
form main { Main }
score main "out" { staff melody }
```

## Building

### Requirements

- .NET 10 SDK
- Node.js (for VS Code extension)

### Build

```bash
dotnet build
dotnet test
```

### CLI

```bash
# Check syntax
dotnet run --project LilySharp.Cli -- check samples/simple.lys

# Export to MIDI
dotnet run --project LilySharp.Cli -- midi samples/simple.lys -o output.mid
```

### Run LSP Server

```bash
dotnet run --project LilySharp.Lsp
```

### VS Code Extension

```bash
cd editors/vscode
npm install
npm run compile
```

## VS Code Features

The VS Code extension provides comprehensive language support:

| Feature | Description |
|---------|-------------|
| **Diagnostics** | Real-time error and warning display |
| **Completion** | Auto-complete for keywords, pitches, dynamics |
| **Hover** | Information on hover for syntax elements |
| **Document Symbols** | Outline view with score structure |
| **Go to Definition** | Navigate to variable declarations |
| **Find References** | Find all uses of a variable |
| **Semantic Highlighting** | Syntax-aware coloring for pitches, dynamics |
| **Folding** | Collapse music blocks and structures |
| **Rename** | Rename variables across the document |
| **Formatting** | Auto-format document |
| **Code Actions** | Quick fixes and refactoring |
| **Signature Help** | Parameter hints for keywords |
| **Document Highlight** | Highlight variable references |

## Compilation Architecture

### Red-Green Tree Pattern

Lily# uses the Roslyn-style Red-Green tree pattern for efficient incremental compilation:

- **Green Nodes**: Immutable, position-independent syntax nodes
- **Red Nodes**: Lazily created wrappers with position information
- **Incremental Updates**: Only affected portions are re-parsed

### Single-Pass Compilation

The compiler performs lexing and parsing in a single pass:
1. Lexer tokenizes source text
2. Parser builds green tree
3. Red nodes created on-demand
4. Semantic analysis validates structure

### Incremental Sync

The LSP server supports incremental text synchronization:
- `TextChange` API for partial updates
- `SyntaxTree.WithChanges()` for efficient re-parsing

## Status

### Implemented

- [x] Lexer with all token types
- [x] Recursive descent parser
- [x] Red-Green tree architecture
- [x] Incremental parsing with TextChange API
- [x] Duration calculation
- [x] Measure validation
- [x] MIDI export with dynamics and articulations
- [x] Full LSP support (13+ features)
- [x] VS Code extension with semantic highlighting
- [x] Key signatures, clefs, tuplets
- [x] Grace notes
- [x] Lyrics support
- [x] SVG music engraving (Emmentaler font, beams, ties, slurs — including slurs over chords, tuplets, volta brackets, multi-measure rests)
- [x] Multi-system layout with Knuth-Plass line breaking
- [x] Multi-staff / GrandStaff rendering (cross-staff beam layout is not yet implemented)
- [x] Lead sheets — staff-less chord rows and lyric rows drawn as a measure grid (chords, lyrics, or both)
- [x] MusicXML export (notes, ties, slurs, grace notes, dynamics, articulations, ornaments, multi-part) — lyrics and tuplet numbers are not yet emitted
- [x] CLI tool

### Planned

- [ ] Cross-staff beam layout
- [ ] MusicXML export: lyrics and tuplet output
- [ ] Multi-file projects
- [ ] LilyPond → LilySharp conversion tool

## Relationship to LilyPond

Lily# is an independent project. It is **not** affiliated with, endorsed by, or a
release of the LilyPond project, and its language is deliberately not LilyPond's.

Its engraving engine, however, is in part a **port of LilyPond**, the GNU music
typesetter: beam quanting, slur and tie scoring, skylines, springs, page breaking
and other layout algorithms are modified translations of LilyPond's C++ and Scheme
rather than independent implementations. Those files carry the copyright notices of
the LilyPond files they were ported from, and
[LILYPOND-ATTRIBUTION.md](LILYPOND-ATTRIBUTION.md) lists every one of them.

Most `LILYPOND-REF` comments elsewhere in the source are citations rather than
ports: they record where LilyPond decides something so that Lily#'s own code can be
checked against it.

## License

Copyright (C) 2025-2026 Yoshifumi Tsuda &lt;ytsuda@gmail.com&gt;.

This program is free software: you can redistribute it and/or modify it under the
terms of the [GNU General Public License v3.0](LICENSE) or later. It contains
modified code from LilyPond, which is under the same licence; the modifications are
Lily#'s and are marked in the files that carry them.

The per-file headers name the copyright holder without an address; this line is the
one place the address is kept, so it stays correct if it ever changes.

**Source for the binaries.** The CLI archives and the VS Code extension are built
from this repository. The complete corresponding source for any released binary is
the tagged commit it was built from, available at
<https://github.com/yotsuda/LilySharp>.

## Acknowledgments

- LilyPond — the engraving algorithms this engine ports, and the reference its
  output is measured against
- Roslyn for the Red-Green tree pattern
- Emmentaler font (from LilyPond; GPL-3.0-or-later / SIL OFL dual license, redistributed here under the GPL) — music glyphs; see `LilySharp.Core/Fonts/Emmentaler-LICENSE.txt`
- TeX Gyre Schola / TeX Gyre Heros fonts (GUST Font License, i.e. LPPL 1.3c) — all non-music text, and the metrics the engine spaces it by; the same faces LilyPond sets text in. See `LilySharp.Core/Fonts/TeXGyre-LICENSE.GUST.txt`