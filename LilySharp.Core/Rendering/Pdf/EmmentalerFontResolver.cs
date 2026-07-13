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

    // The bundled Liberation Serif (SIL OFL 1.1) face for a weight/slant — the PDF
    // stand-in for the CSS-generic "serif" and for any non-embedded text font.
    private static FontResolverInfo SerifFace(bool isBold, bool isItalic) =>
        new((isBold, isItalic) switch
        {
            (true, true) => "LiberationSerif-BoldItalic#",
            (true, false) => "LiberationSerif-Bold#",
            (false, true) => "LiberationSerif-Italic#",
            _ => "LiberationSerif#",
        });

    public string DefaultFontName => "Emmentaler";

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var name = familyName.ToLowerInvariant();
        if (name == "emmentaler" || name == "emmentaler-20")
            return new FontResolverInfo("Emmentaler#");
        if (name == "emmentaler-brace")
            return new FontResolverInfo("EmmentalerBrace#");
        // SharedRenderer asks for the CSS-generic "serif" for titles/lyrics/
        // dynamics. SVG/PNG let the viewer/Skia map that to a real serif, but
        // PdfSharpCore's fallback has no "serif" face and substitutes an
        // arbitrary installed font (e.g. the sans-serif "Agency"), so PDFs looked
        // nothing like the SVG — and embedded a proprietary system font. Resolve
        // it to the bundled Liberation Serif (metric-compatible with Times),
        // which is licensed for both embedding and redistribution.
        if (name is "serif")
            return SerifFace(isBold, isItalic);
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

        var fileName = faceName switch
        {
            "Emmentaler#" => "emmentaler-20.otf",
            "EmmentalerBrace#" => "emmentaler-brace.otf",
            "LiberationSerif#" => "LiberationSerif-Regular.ttf",
            "LiberationSerif-Bold#" => "LiberationSerif-Bold.ttf",
            "LiberationSerif-Italic#" => "LiberationSerif-Italic.ttf",
            "LiberationSerif-BoldItalic#" => "LiberationSerif-BoldItalic.ttf",
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
