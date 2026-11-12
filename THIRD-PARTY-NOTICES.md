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

### Bravura, Petaluma and Leland (SMuFL music fonts)

Bravura: Copyright © 2026 Steinberg Media Technologies GmbH, with Reserved
Font Name "Bravura" (https://github.com/steinbergmedia/bravura — the SMuFL
reference font). Petaluma: Copyright © 2018 Steinberg Media Technologies GmbH,
with Reserved Font Name "Petaluma" (https://github.com/steinbergmedia/petaluma).
Leland: Copyright (c) 2025 MuseScore BVBA, with Reserved Font Name "Leland"
(https://github.com/MuseScoreFonts/Leland).

All three are licensed under the SIL Open Font License, Version 1.1. The full
text ships next to each font as `Fonts/Bravura-LICENSE.txt`,
`Fonts/Petaluma-LICENSE.txt` and `Fonts/Leland-LICENSE.txt`, with the FONTLOG
each project publishes. They are redistributed unmodified, under their
reserved names, together with their SMuFL metadata (`*_metadata.json`), which
is what `fonts { music "…" }` selects a score's music font from. The OFL
permits bundling and embedding in documents and forbids selling the fonts by
themselves, which Lily# does not do.

Petaluma Script (`Fonts/PetalumaScript.otf`, `Fonts/PetalumaScript.woff2`) is
Petaluma's companion text face from the same repository, the same copyright
holder and the same licence — its `OFL.txt` and `FONTLOG.txt` are the files
shipped as `Fonts/Petaluma-LICENSE.txt` and `Fonts/Petaluma-FONTLOG.txt`. A
score in Petaluma draws its chord symbols' letters in it unless the score
names a face for them (and its rehearsal marks); it is redistributed unmodified, under its
reserved name. `Petaluma.woff2` and `PetalumaScript.woff2` are the `.otf` files wrapped
in WOFF2 unchanged (fontTools; the format drops only the `DSIG` signature table).

## .NET libraries (bundled as DLLs)

Present in the `lysc` archives, in the language server the VS Code extension
bundles, or in both.

| Package | Copyright | License |
|---|---|---|
| SkiaSharp, SkiaSharp.NativeAssets.{Win32,Linux,macOS} | © Microsoft Corporation | MIT |
| HarfBuzzSharp, HarfBuzzSharp.NativeAssets.{Win32,Linux,macOS} | © Microsoft Corporation | MIT |
| PdfSharpCore | © 2005-2007 empira Software GmbH, Cologne (Germany); modified work © 2016 David Dunscombe | MIT |
| SharpZipLib | © 2000-2022 SharpZipLib Contributors | MIT |
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
| Markdig | © Alexandre Mutel | BSD-2-Clause |
| MeltySynth | © 2021 Nobuaki Tanaka; including C# Synth © 2014 Alex Veltsistas, TinySoundFont © 2017, 2018 Bernhard Schelling, based on SFZero © 2012 Steve Folta | MIT |

SixLabors.ImageSharp, which PdfSharpCore depends on for raster images, is not
among them: Lily#'s PDFs carry no images, and the package is referenced for its
version only, with no DLL in any output (`Directory.Build.props`).

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
| spessasynth_lib, spessasynth_core | © Spessasus | Apache-2.0 |
| @breezystack/lamejs (lamejs) | © Alex Zhukov and the lamejs contributors; a port of LAME, © the LAME project | LGPL-3.0 |

spessasynth_lib is the preview's SoundFont synthesizer (`docs/playback-design.md`): its
browser half is bundled into `out/synth/preview-synth.js` and its AudioWorklet processor
ships unmodified as `out/synth/spessasynth_processor.min.js`. Both carry MIT-licensed code
the project vendors (hajimehoshi's stbvorbis.js, 101arrowz's fflate), noticed in the
processor file's own legal comments. spessasynth_core renders the WAV and MP3 exports
through the preview's sound font (`editors/vscode/src/audioRender.ts`).

lamejs encodes the MP3 export. It is the JavaScript port of **LAME** (<https://lame.sourceforge.io/>,
formerly lame.sourceforge.net), published on npm as `@breezystack/lamejs` 1.2.7 (source:
<https://github.com/shijinyu/lamejs>, forked from <https://github.com/zhuker/lamejs>). It is
bundled unmodified into `out/extension.js`, under the GNU Lesser General Public License,
version 3, whose text ships as [LICENSE-LGPL-3.0.txt](LICENSE-LGPL-3.0.txt) beside the GNU GPL
it supplements ([LICENSE](LICENSE)). The extension is free software under the GPL with its
complete source published, so anyone may change and rebuild the library inside it.

## Sound font (none bundled)

The VS Code extension bundles no sound font (owner's decision, 2026-10-12). The preview plays
the General MIDI bank the operating system ships for its own synthesizer — Windows'
`gm.dls`, macOS's DLS bank — read in place where it is installed, never copied or
redistributed; or a sound font the user downloads and names in `lilysharp.playback.soundFont`.
The extension's WAV, MP3 and MP4 exports play through that same font. `lysc wav` plays through a
SoundFont 2 the user names (`--soundfont`); MeltySynth, above, is the synthesizer.

## ffmpeg (none bundled)

The extension's **Export as MP4 Video** encodes the video with an **ffmpeg** the user installs
(the one on the `PATH`, or the program `lilysharp.export.ffmpegPath` names). Lily# runs it as a
separate program, with arguments, and neither bundles, links nor redistributes it; its licence
and its codecs' are the user's copy's own.

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

## BSD 2-Clause License

Applies to the component marked "BSD-2-Clause" above (Markdig, the CommonMark reader
for a score's fenced text), with its copyright holder:

```
Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

## Apache License 2.0

Applies to the components marked "Apache-2.0" above (the Six Labors
libraries, and spessasynth_lib / spessasynth_core in the VS Code extension).
The full text ships as [LICENSE-Apache-2.0.txt](LICENSE-Apache-2.0.txt), as
section 4(a) requires. The Six Labors libraries are redistributed unmodified,
in binary form, exactly as published on NuGet; spessasynth's processor file
is redistributed unmodified as published on npm, and its browser half is
bundled (minified, not otherwise changed) with Lily#'s own page code.
