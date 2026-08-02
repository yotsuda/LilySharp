// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using PdfSharpCore.Fonts;

namespace LilySharp.Core.Rendering.Pdf;

/// <summary>
/// PdfSharpCore font resolver that loads Emmentaler from
/// <c>LilySharp.Core/Fonts/</c> (copied to output as content) and falls
/// back to PdfSharpCore's default resolver for everything else.
/// </summary>
/// <remarks>
/// Family name <c>"Emmentaler"</c> resolves to <c>emmentaler-20.otf</c>;
/// <c>"Emmentaler-Brace"</c> resolves to <c>emmentaler-brace.otf</c>.
/// PdfSharpCore reads the OTF/TTF bytes via <see cref="GetFont"/> and
/// embeds them in the produced PDF.
/// </remarks>
internal sealed class EmmentalerFontResolver : IFontResolver
{
    private readonly string? _fontDirectory;
    private readonly IFontResolver? _fallback;

    // The document's configured text font (`font "X"`) and, when it asked to EMBED
    // (`font "X" embedded`) and the licence allows, that font's bytes loaded from the
    // system via SkiaSharp. Set per-document by PdfDocumentContext. When _embedBytes
    // is null the configured font is NOT embedded — it resolves to the bundled serif
    // instead, so a non-`embedded` document never silently embeds a system font.
    private string? _textFamily;
    private byte[]? _embedBytes;

    public EmmentalerFontResolver(string? fontDirectory = null, IFontResolver? fallback = null)
    {
        _fontDirectory = fontDirectory;
        _fallback = fallback;
    }

    /// <summary>
    /// Sets (or clears) the document's text font and whether to embed it. When
    /// <paramref name="embed"/> is true and the font's licence permits embedding —
    /// Free, or Gray (an explicit <c>embedded</c> honours a gray font, having already
    /// warned) — its bytes are loaded for subset-embedding; a Forbidden/not-installed
    /// font is not embedded. When embed is false the font is a reference only (it maps
    /// to the bundled serif in PDF, so nothing proprietary is embedded without asking).
    /// </summary>
    public void SetTextFont(string? family, bool embed)
    {
        _textFamily = string.IsNullOrEmpty(family) ? null : family;
        _embedBytes = null;
        if (_textFamily == null || !embed)
            return;
        var cls = FontEmbedInfo.Classify(_textFamily);
        if (cls is FontEmbedInfo.FontEmbedClass.Forbidden or FontEmbedInfo.FontEmbedClass.NotFound)
            return;
        _embedBytes = FontEmbedInfo.TryGetFontBytes(_textFamily);
    }

    // The bundled TeX Gyre Schola (GUST Font License / LPPL 1.3c) face for a weight/slant
    // — the PDF stand-in for the CSS-generic "serif" and for any non-embedded text font.
    // It is LilyPond's own text face by metrics: "LilyPond Serif" prefers URW's C059, and
    // C059 and Schola agree on every advance measured, so this is what the layout reserved
    // for through TextFontMetrics. Was Liberation Serif, which is Times-metric and 9%
    // narrower — the PDF then disagreed with what the engine had spaced.
    private static FontResolverInfo SerifFace(bool isBold, bool isItalic) =>
        new((isBold, isItalic) switch
        {
            (true, true) => "ScholaBoldItalic#",
            (true, false) => "ScholaBold#",
            (false, true) => "ScholaItalic#",
            _ => "Schola#",
        });

    // The sans stand-in, likewise metric-identical to the Nimbus Sans that LilyPond's
    // "LilyPond Sans Serif" alias prefers. Chord symbols are the caller.
    private static FontResolverInfo SansFace(bool isBold, bool isItalic) =>
        new((isBold, isItalic) switch
        {
            (true, true) => "HerosBoldItalic#",
            (true, false) => "HerosBold#",
            (false, true) => "HerosItalic#",
            _ => "Heros#",
        });

    public string DefaultFontName => "Emmentaler";

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var name = familyName.ToLowerInvariant();
        // Every Emmentaler DESIGN is its own face — the font is optically sized, so a grace's
        // 14 is not the 20 scaled (see EmmentalerFaces). The default design keeps the bare
        // "Emmentaler#" face name so existing PDFs are unchanged.
        if (EmmentalerFaces.TryParseFamily(name, out int design))
            return new FontResolverInfo(design == EmmentalerFaces.DefaultDesign
                ? "Emmentaler#"
                : EmmentalerFaces.Family(design) + "#");
        if (name == "emmentaler-brace")
            return new FontResolverInfo("EmmentalerBrace#");
        // SharedRenderer asks for the CSS generics for titles/lyrics/dynamics/chord
        // symbols. PdfSharpCore has no face for them and would substitute an arbitrary
        // installed font (e.g. the sans-serif "Agency"), so PDFs looked nothing like the
        // rest — and embedded a proprietary system font. They resolve to the bundled
        // TeX Gyre faces, which is also what TextFontMetrics measured when the engine
        // spaced this score, and which are licensed for embedding and redistribution.
        if (name is "serif")
            return SerifFace(isBold, isItalic);
        if (name is "sans" or "sans-serif")
            return SansFace(isBold, isItalic);
        // The document's configured text font (`font "X"`, which the renderer maps
        // every generic family onto). With `embedded` and a permitted licence we
        // serve X's own bytes so PdfSharpCore subsets and embeds them (a portable
        // PDF; bold/italic reuse the one face and PdfSharpCore synthesises emphasis).
        // WITHOUT embedding, X resolves to the bundled serif — so a plain reference
        // never silently embeds a system (possibly proprietary) font.
        if (_textFamily != null && string.Equals(familyName, _textFamily, StringComparison.OrdinalIgnoreCase))
            return _embedBytes != null ? new FontResolverInfo("LysEmbed#") : SerifFace(isBold, isItalic);
        return _fallback?.ResolveTypeface(familyName, isBold, isItalic);
    }

    public byte[] GetFont(string faceName)
    {
        if (faceName == "LysEmbed#")
            return _embedBytes ?? throw new InvalidOperationException(
                "Embed font requested but its bytes were not loaded.");

        // "Emmentaler-14#" and friends — the face name is the family plus the '#' this
        // resolver marks its own faces with, so one bundled OTF answers per design.
        if (faceName.EndsWith("#", StringComparison.Ordinal)
            && EmmentalerFaces.TryParseFamily(faceName[..^1], out int design)
            && design != EmmentalerFaces.DefaultDesign)
        {
            var designPath = ResolveFontPath(EmmentalerFaces.OtfFile(design));
            if (designPath != null) return File.ReadAllBytes(designPath);
            throw new FileNotFoundException(
                $"Font file not found: {EmmentalerFaces.OtfFile(design)}");
        }

        var fileName = faceName switch
        {
            "Emmentaler#" => EmmentalerFaces.OtfFile(EmmentalerFaces.DefaultDesign),
            "EmmentalerBrace#" => "emmentaler-brace.otf",
            "Schola#" => "texgyreschola-regular.otf",
            "ScholaBold#" => "texgyreschola-bold.otf",
            "ScholaItalic#" => "texgyreschola-italic.otf",
            "ScholaBoldItalic#" => "texgyreschola-bolditalic.otf",
            "Heros#" => "texgyreheros-regular.otf",
            "HerosBold#" => "texgyreheros-bold.otf",
            "HerosItalic#" => "texgyreheros-italic.otf",
            "HerosBoldItalic#" => "texgyreheros-bolditalic.otf",
            _ => null
        };
        if (fileName != null)
        {
            var path = ResolveFontPath(fileName);
            if (path != null) return File.ReadAllBytes(path);
            throw new FileNotFoundException($"Font file not found: {fileName}");
        }
        return _fallback?.GetFont(faceName) ?? throw new InvalidOperationException(
            $"No font data available for face '{faceName}' and no fallback resolver configured.");
    }

    private string? ResolveFontPath(string fileName) =>
        FontLocator.ResolveFile(fileName, _fontDirectory);
}
