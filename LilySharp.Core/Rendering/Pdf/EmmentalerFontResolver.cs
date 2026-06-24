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

    public EmmentalerFontResolver(string? fontDirectory = null, IFontResolver? fallback = null)
    {
        _fontDirectory = fontDirectory;
        _fallback = fallback;
    }

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
        // it to the bundled Liberation Serif (SIL OFL 1.1, metric-compatible with
        // Times), which is licensed for both embedding and redistribution.
        if (name is "serif")
            return new FontResolverInfo((isBold, isItalic) switch
            {
                (true, true) => "LiberationSerif-BoldItalic#",
                (true, false) => "LiberationSerif-Bold#",
                (false, true) => "LiberationSerif-Italic#",
                _ => "LiberationSerif#",
            });
        return _fallback?.ResolveTypeface(familyName, isBold, isItalic);
    }

    public byte[] GetFont(string faceName)
    {
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

    private string? ResolveFontPath(string fileName)
    {
        if (!string.IsNullOrEmpty(_fontDirectory))
        {
            var p = Path.Combine(_fontDirectory, fileName);
            if (File.Exists(p)) return p;
        }
        var baseDir = AppContext.BaseDirectory;
        foreach (var sub in new[] { "Fonts", "fonts", "" })
        {
            var p = Path.Combine(baseDir, sub, fileName);
            if (File.Exists(p)) return p;
        }
        return null;
    }
}
