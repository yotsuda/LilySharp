# Third-Party Notices

Lily# is licensed under the GNU General Public License v3.0 or later
(see [LICENSE](LICENSE)). Binary distributions (the CLI archives and the
VS Code extension) additionally bundle the third-party components below.
All of them are GPL-compatible; their license terms and copyright notices
are reproduced here, or in the files this one points at, as those licenses
require.

**Ported code.** Parts of the engraving engine are ported from LilyPond and carry
its copyright notices in the files themselves;
[LILYPOND-ATTRIBUTION.md](LILYPOND-ATTRIBUTION.md) is the full list. That is source
Lily# incorporates rather than a component it bundles, so it is recorded there
rather than in the table below. That file ships with the binaries too.

**Corresponding source.** Every binary Lily# distributes is built from
<https://github.com/yotsuda/LilySharp>; the complete corresponding source for a
release is the tagged commit it was built from.

**How this list is kept honest.** It is derived from the assemblies actually
present in `dotnet publish` output, not from the direct dependency list — a
transitive package ships just as surely as a named one. Re-check it whenever a
dependency changes.

## Fonts

### Emmentaler (music glyphs)

Copyright (C) 1997-2023 Han-Wen Nienhuys, Jan Nieuwenhuizen, Werner Lemberg,
and the LilyPond project contributors.

Part of LilyPond, the GNU music typesetter (https://lilypond.org).
Dual-licensed: GNU GPL v3 or later, or the SIL Open Font License.
Lily# redistributes it under the GPL v3-or-later branch — see
`Fonts/Emmentaler-LICENSE.txt` alongside the font files, and the copy in
the extension's `media/fonts/` beside the two WOFF2 faces the preview loads.

### TeX Gyre Schola and TeX Gyre Heros (all non-music text)

Copyright (C) 2007-2009 by GUST e-Foundry, based on the URW++ fonts
released for Ghostscript.

Licensed under the GUST Font License, which places the fonts under the
LaTeX Project Public License 1.3c — the full text ships next to the font
files as `Fonts/TeXGyre-LICENSE.GUST.txt`. Redistributed unmodified; the
licence's request to rename applies to derived works.

These are the faces LilyPond itself sets text in: its `LilyPond Serif`
alias prefers URW's C059, and C059 and TeX Gyre Schola agree on every
advance measured, so Lily# reserves and draws the same metrics LilyPond
does without taking on C059's AGPL terms. Heros stands in for Nimbus Sans
on the same footing.

## .NET libraries (bundled as DLLs)

Present in the `lysc` archives, in the language server the VS Code extension
bundles, or in both.

| Package | Copyright | License |
|---|---|---|
| SkiaSharp, SkiaSharp.NativeAssets.{Win32,Linux,macOS} | © Microsoft Corporation | MIT |
| HarfBuzzSharp, HarfBuzzSharp.NativeAssets.{Win32,Linux,macOS} | © Microsoft Corporation | MIT |
| PdfSharpCore | © 2005-2007 empira Software GmbH, Cologne (Germany); modified work © 2016 David Dunscombe | MIT |
| SharpZipLib | © 2000-2022 SharpZipLib Contributors | MIT |
| SixLabors.ImageSharp | © Six Labors | Apache-2.0 |
| SixLabors.Fonts | © Six Labors | Apache-2.0 |
| StreamJsonRpc | © Microsoft Corporation | MIT |
| Microsoft.VisualStudio.Threading | © Microsoft Corporation | MIT |
| Microsoft.VisualStudio.Validation | © Microsoft Corporation | MIT |
| Microsoft.NET.StringTools | © Microsoft Corporation | MIT |
| System.IO.Pipelines | © Microsoft Corporation | MIT |
| Nerdbank.Streams | © Andrew Arnott | MIT |
| Nerdbank.MessagePack | © Andrew Arnott | MIT |
| MessagePack, MessagePack.Annotations | © Yoshifumi Kawai and contributors | MIT |
| PolyType | © Eirik Tsarpalis | MIT |
| Newtonsoft.Json | © James Newton-King 2008 | MIT |

The self-contained builds (every `lysc` archive, and the platform-specific
Marketplace VSIXs) also bundle the **.NET runtime**, © .NET Foundation and
Contributors, MIT.

### Native binaries inside those packages

`libSkiaSharp` and `libHarfBuzzSharp` are compiled from Skia, HarfBuzz, and the
libraries those two vendor in (ANGLE, FreeType, libpng, libjpeg-turbo, libwebp,
ICU, zlib and others). The upstream copyright notices and license terms for all
of them are reproduced verbatim, as received from the SkiaSharp project, in
[THIRD-PARTY-NOTICES-SkiaSharp.txt](THIRD-PARTY-NOTICES-SkiaSharp.txt), which
ships with the binaries.

## Node libraries (bundled in the VS Code extension)

| Package | Copyright | License |
|---|---|---|
| vscode-languageclient | © Microsoft Corporation | MIT |

## Deliberately NOT bundled

Two dependencies were removed rather than shipped, and both would come back
unnoticed as transitive dependencies. Check for them when adding packages.

- **Svg.Skia** (and its `Svg.Custom`, which is **MS-PL**). MS-PL is free
  software but GPL-incompatible, so it cannot live inside a GPL binary. It was
  used only to rasterize an SVG string; every format Lily# ships now renders
  from the layout model straight through Skia. It remains a **test-only**
  reference (`LilySharp.Tests`), which is never distributed, because the
  visual-diff harness has to rasterize LilyPond's SVG baselines.
- **Microsoft.VisualStudio.LanguageServer.Protocol**, which is not open source
  at all: its license is the "Microsoft Visual Studio Add-ons and Extensions"
  license terms, which forbid sharing or publishing the software and combining
  it with an application for others to use, and forbid reverse engineering.
  17.2.8 (2022) is its final version, so there was nothing to upgrade to. The
  LSP wire types Lily# needs are now its own
  (`LilySharp.Lsp/Protocol/LspTypes.cs`); the protocol itself is an open
  specification.

## MIT License

The following terms apply to every component marked "MIT" above, with the
respective copyright holder:

```
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```

## Apache License 2.0

Applies to the components marked "Apache-2.0" above (the Six Labors
libraries). The full text ships as
[LICENSE-Apache-2.0.txt](LICENSE-Apache-2.0.txt), as section 4(a) requires.
Those libraries are redistributed unmodified, in binary form, exactly as
published on NuGet.
