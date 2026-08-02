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

using LilySharp.Core.Svg.Layout;

namespace LilySharp.Core.Rendering;

/// <summary>
/// The bundled Emmentaler DESIGNS as the three backends name them: a family name to draw
/// with and the file that family resolves to.
/// </summary>
/// <remarks>
/// Emmentaler is optically sized, so "which face" is a real question and not a scale — see
/// <see cref="EmmentalerDesignSize"/> for the selection rule and
/// <see cref="IDrawingContext.MusicFace"/> for the scope that opens one.
/// <para>
/// ⚠️ The DEFAULT design keeps the bare family name <c>Emmentaler</c>. That is not a
/// nicety: it is what every existing SVG/PDF/PNG names, so a score with no small glyph in it
/// is byte-identical to what it was before the other seven faces existed.
/// </para>
/// </remarks>
internal static class EmmentalerFaces
{
    /// <summary>
    /// The design a grob at the score's own size reads — <c>emmentaler-20</c>, because Lily#'s
    /// staff is LilyPond's default 20pt and a 20pt request lands on the 20 design exactly.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/font-select.cc:41-70 best_rounded_design_size, over
    ///   lily/font-select.cc:104-107's base size — asked here rather than written down, so
    ///   the two can never disagree about what "full size" means.
    /// </remarks>
    public static readonly int DefaultDesign =
        EmmentalerDesignSize.BestRounded(EmmentalerDesignSize.BaseSizePoints).Rounded;

    /// <summary>The font family a music glyph of <paramref name="rounded"/> is drawn with.</summary>
    public static string Family(int rounded) =>
        rounded == DefaultDesign ? "Emmentaler" : "Emmentaler-" + rounded;

    /// <summary>The bundled OTF (PDF and PNG read outlines from this).</summary>
    public static string OtfFile(int rounded) => "emmentaler-" + rounded + ".otf";

    /// <summary>The bundled WOFF2 (SVG embeds this as a base64 <c>@font-face</c>).</summary>
    public static string Woff2File(int rounded) => "emmentaler-" + rounded + ".woff2";

    /// <summary>
    /// The design a family name asks for — <c>Emmentaler</c> and <c>Emmentaler-20</c> both
    /// being the default one. False for any family that is not a music face.
    /// </summary>
    public static bool TryParseFamily(string family, out int rounded)
    {
        rounded = DefaultDesign;
        var name = family.ToLowerInvariant();
        if (name == "emmentaler")
            return true;
        if (!name.StartsWith("emmentaler-", StringComparison.Ordinal))
            return false;
        var tail = name["emmentaler-".Length..];
        if (!int.TryParse(tail, System.Globalization.NumberStyles.None,
                          System.Globalization.CultureInfo.InvariantCulture, out int size))
            return false;   // "emmentaler-brace" — a different font, not a design of this one
        foreach (var (r, _) in EmmentalerDesignSize.Designs)
            if (r == size)
            {
                rounded = size;
                return true;
            }
        return false;
    }
}
