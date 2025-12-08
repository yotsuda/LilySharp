# Lilysharp VS Code Extension

Language support for Lilysharp music notation files.

## Features

- Syntax highlighting
- Diagnostics (parse errors, measure validation)
- Code completion
- Hover information

## Requirements

- The Lilysharp language server (`lilysharp-lsp`) must be installed and available in PATH
- Or configure `lilysharp.serverPath` to point to the executable

## Building

```bash
npm install
npm run compile
```

## Installation

1. Build the LSP server: `dotnet build ../Lilysharp.Lsp`
2. Build the extension: `npm run compile`
3. Copy the extension folder to `~/.vscode/extensions/lilysharp`
4. Or use `vsce package` to create a `.vsix` file