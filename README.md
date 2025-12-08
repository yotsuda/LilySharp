# Lilysharp

A modern music notation compiler with real-time preview support.

## Overview

Lilysharp is a new music notation language inspired by LilyPond, designed for:
- **Explicit over implicit**: Clear, readable syntax
- **Completion-friendly**: IDE-first design with LSP support
- **Single-pass incremental compilation**: Using Roslyn-style Red-Green tree pattern

## Project Structure

```
Lilysharp/
├── Lilysharp.Core/          # Core compiler (lexer, parser, semantic analysis)
│   ├── Parser/              # Lexer and recursive descent parser
│   ├── Syntax/              # Syntax kinds, green/red tree nodes
│   └── Semantics/           # Duration calculation, measure validation
├── Lilysharp.Lsp/           # Language Server Protocol implementation
├── Lilysharp.Tests/         # Unit tests (86 tests)
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
c4@staccato d@accent e@fermata
c4\p d\cresc e\f
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

## Building

### Requirements

- .NET 10 SDK
- Node.js (for VS Code extension)

### Build

```bash
dotnet build
dotnet test
```

### Run LSP Server

```bash
dotnet run --project Lilysharp.Lsp
```

### VS Code Extension

```bash
cd editors/vscode
npm install
npm run compile
```

## Status

### Implemented

- [x] Lexer with all token types
- [x] Recursive descent parser
- [x] Red-Green tree architecture
- [x] Duration calculation
- [x] Measure validation (4/4, 3/4, etc.)
- [x] LSP server (diagnostics, completion, hover)
- [x] VS Code extension (syntax highlighting, LSP client)

### Planned

- [ ] Incremental parsing
- [ ] Music engraving (PDF/SVG output)
- [ ] MIDI export
- [ ] More articulations and ornaments
- [ ] Lyrics support
- [ ] Tuplets

## License

MIT

## Acknowledgments

- LilyPond for inspiration on music notation syntax
- Roslyn for the Red-Green tree pattern
- Emmentaler font (SIL Open Font License)