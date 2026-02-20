# Lily#

A modern music notation compiler with real-time preview support.

## Overview

Lily# is a new music notation language inspired by LilyPond, designed for:
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
├── LilySharp.Tests/         # Unit tests (450+ tests)
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
tempo "Allegro" 4 = 120
time 3/4
key g major
clef treble

score {
    part Melody {
        relative c' {
            c4 d e f | g2 g |
        }
    }
}
```

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

### Lyrics

```lilysharp
{ c4 d e f }
lyrics { Hap -- py birth -- day }
```

### Repeats and Alternatives

```lilysharp
repeat volta 2 {
    c4 d e f |
}
alternative {
    { g2 g | }
    { a2 a | }
}
```

### Parallel Voices

```lilysharp
<< { c2 d } \\ { e2 f } >>
```

### Variables

```lilysharp
let melody = { c4 d e f }
let bass = { c2 g }

score {
    part { use $melody }
}
```

## Building

### Requirements

- .NET 9 SDK
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
- [x] SVG music engraving (Emmentaler font, beams, ties, slurs, tuplets, volta brackets)
- [x] Multi-system layout with Knuth-Plass line breaking
- [x] Multi-staff / GrandStaff rendering
- [x] CLI tool

### Planned

- [ ] SVG dynamics/articulations/lyrics rendering
- [ ] MusicXML export (full section/structure support)
- [ ] Chord notation
- [ ] Multi-file projects
- [ ] LilyPond → LilySharp conversion tool

## License

MIT

## Acknowledgments

- LilyPond for inspiration on music notation syntax
- Roslyn for the Red-Green tree pattern
- Emmentaler font (SIL Open Font License)