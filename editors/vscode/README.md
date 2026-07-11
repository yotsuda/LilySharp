# Lily# VS Code Extension

Language support for Lily# music notation files (`.lys`).

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
| **Document Highlight** | Highlight all occurrences of selected variable |

### Semantic Token Colors

The extension provides custom semantic highlighting:

- **Pitches** (c, d, e, f, g, a, b): Teal
- **Articulations** (@staccato, @accent): Yellow
- **Dynamics** (\p, \f, \ff): Purple

Colors can be customized in settings.

## Requirements

None. The language server is **self-contained** — each platform's build bundles its
own .NET runtime, so nothing needs to be installed. Just install the extension and open
a `.lys` file.

(Building from source needs the .NET 9 SDK and Node.js 18+.)

## Installation

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
form main { Main }
score main { staff melody }
```

## Troubleshooting

### Language server not starting

1. Check if `lilysharp-lsp` is in PATH or configure `lilysharp.serverPath`
2. Check Output panel (View → Output → Lily# Language Server)
3. Enable tracing: Set `lilysharp.trace.server` to `verbose`

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