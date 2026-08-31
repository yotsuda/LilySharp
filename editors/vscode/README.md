# Lily# VS Code Extension

**Version 0.5.0** — the bundled language server and the `lysc` compiler carry the
same number. See the [changelog](https://github.com/yotsuda/LilySharp/blob/master/editors/vscode/CHANGELOG.md) for what is in this release.

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
8. Open the preview (`Ctrl+Shift+V`), then **hold `Alt+P`** to hear the note under the caret, or press `Alt+M` to play the measure the caret is in (the preview panel is the synth)

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