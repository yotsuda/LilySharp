# Email Draft: lilypond-devel

**To:** lilypond-devel@gnu.org
**Subject:** Lily# — A C# port of LilyPond's engraving engine with LSP support

---

Dear LilyPond developers,

I am writing to introduce Lily#, a project that ports LilyPond's C++ engraving engine to C#. I wanted to inform the community about this derivative work and express my gratitude for LilyPond.

## What is Lily#?

Lily# is a music notation compiler that translates LilyPond's core layout and engraving algorithms into C#/.NET. The primary goals are:

- **Real-time preview and LSP support in VS Code** — Edit music notation and see the engraved score update live, with full IDE features including diagnostics, auto-completion, hover information, and go-to-definition
- **Simplified syntax** — A declarative grammar without TeX or Scheme dependencies, aiming to lower the barrier to entry for new users
- **Cross-platform .NET ecosystem** — Leveraging .NET's tooling and library ecosystem
- **Multiple output formats** — SVG, PDF, PNG, MIDI, and MusicXML

The layout and spacing implementation closely follows LilyPond's source code (lily/*.cc), and each ported section references the corresponding LilyPond source for traceability.

## Licensing

Lily# is released under the **GNU General Public License v3 (or later)**, the same license as LilyPond. All original LilyPond copyright notices are preserved, and the Emmentaler font is included under its original GPL + Font Exception license.

## Current status

- Lexer, parser, and semantic analysis (Roslyn-style Red-Green tree)
- SVG engraving with Emmentaler font (beams, ties, slurs, dynamics, tuplets, volta brackets, lyrics)
- Multi-system layout with Knuth-Plass line breaking
- Multi-staff / GrandStaff rendering
- Full LSP server with 13+ features
- VS Code extension with semantic highlighting
- CLI tool (`lysc`) for batch compilation

## Acknowledgments

LilyPond has been an incredible achievement in open-source music engraving for over two decades. Lily# would not exist without the extensive work by Han-Wen Nienhuys, Jan Nieuwenhuizen, and all LilyPond contributors. I have the deepest respect for the project and its community.

I welcome any feedback, suggestions, or concerns from the community.

Best regards,
Yoshifumi Tsuda
