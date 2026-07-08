# Changelog

All notable changes to the Lily# VS Code extension are documented here.

## 0.3.0

First public release.

### Language support

- Semantic syntax highlighting for pitches, dynamics, and articulations
- Real-time diagnostics (errors and warnings as you type)
- Code completion for keywords, pitches, durations, dynamics, and `@`-annotations
- Hover documentation, signature help, and document highlight
- Document outline, go-to-definition (F12), find references (Shift+F12), and rename (F2)
- Code folding, document formatting, and quick-fix code actions

### Live score preview

- Rendered score preview that refreshes as you edit
- Click a note in the preview to jump to its source in the editor
- MIDI playback with note-by-note highlighting

### Engraving

- LilyPond-faithful layout: beaming, multi-articulation stacking, fingering,
  accel./rit. text spanners, dynamics and expressive text, volta brackets,
  and multi-staff scores

### Packaging

- Self-contained language server — each platform build bundles its own .NET
  runtime and native rendering, so nothing else needs to be installed
