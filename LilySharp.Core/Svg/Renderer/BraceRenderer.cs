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

namespace LilySharp.Core.Svg.Renderer;

/// <summary>
/// Renders brace (curly bracket) for grand staff using Emmentaler-Brace font glyphs.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-markup-commands.scm (left-brace)
/// The Emmentaler-Brace font contains 576 brace glyphs (brace0 to brace575)
/// at code points U+E000 to U+E23F. Larger numbers = larger braces.
/// </remarks>
public static class BraceRenderer
{
    // Brace glyph range in Emmentaler-Brace font
    private const int BraceGlyphStart = 0xE000;  // brace0
    private const int BraceGlyphCount = 576;     // brace0 to brace575

    // Font metrics (from emmentaler-brace.svg)
    private const double UnitsPerEm = 1000.0;

    // Approximate glyph heights in font units (from SVG font analysis)
    // These values are derived from the bounding box of each glyph
    // brace0 ≈ 263, brace575 ≈ 11493
    private const double MinGlyphHeight = 263.0;   // brace0 height in font units
    private const double MaxGlyphHeight = 11493.0; // brace575 height in font units

    /// <summary>
    /// Renders a brace as an SVG text element using Emmentaler-Brace font.
    /// </summary>
    /// <param name="x">X position of the brace (right edge, where it connects to staff)</param>
    /// <param name="yTop">Y position of the top of the brace in staff spaces</param>
    /// <param name="yBottom">Y position of the bottom of the brace in staff spaces</param>
    /// <returns>SVG text element string</returns>
    public static string RenderBrace(double x, double yTop, double yBottom)
    {
        double height = yBottom - yTop;
        double yMid = (yTop + yBottom) / 2;

        // Select appropriate brace glyph based on height
        int glyphIndex = SelectBraceGlyph(height);
        int codePoint = BraceGlyphStart + glyphIndex;

        // Use XML numeric character reference for PUA characters
        string braceGlyph = $"&#x{codePoint:X4};";

        // Calculate font-size based on the glyph's native height
        // The glyph height in em units = glyphHeightUnits / unitsPerEm
        // font-size = desiredHeight / glyphHeightInEm
        // Scale factor empirically determined to match visual output
        const double ScaleFactor = 0.76;
        double glyphHeightUnits = GetGlyphHeight(glyphIndex);
        double glyphHeightEm = glyphHeightUnits / UnitsPerEm;
        double fontSize = (height / glyphHeightEm) * ScaleFactor;

        // X position: brace tip is at left, connects to staff at right
        double braceX = x;

        // Invariant culture: SVG requires '.' decimals; a comma locale would emit
        // "12,34" and break the coordinate.
        return FormattableString.Invariant(
            $"<text x=\"{braceX:F2}\" y=\"{yMid:F2}\" font-family=\"Emmentaler-Brace\" font-size=\"{fontSize:F2}\" dominant-baseline=\"middle\" text-anchor=\"end\">{braceGlyph}</text>");
    }

    /// <summary>
    /// Selects the appropriate brace glyph index based on desired height.
    /// </summary>
    /// <param name="height">Desired brace height in staff spaces</param>
    /// <returns>Glyph index (0-575)</returns>
    private static int SelectBraceGlyph(double height)
    {
        // Convert desired height to font units (assuming font-size = 1)
        double targetHeightUnits = height * UnitsPerEm;

        // Find the best matching glyph
        // Use linear interpolation between min and max glyph heights
        double ratio = (targetHeightUnits - MinGlyphHeight) / (MaxGlyphHeight - MinGlyphHeight);
        ratio = Math.Clamp(ratio, 0.0, 1.0);

        // Apply slight non-linear adjustment for better matching
        double adjustedRatio = Math.Pow(ratio, 0.8);

        int glyphIndex = (int)(adjustedRatio * (BraceGlyphCount - 1));
        return Math.Clamp(glyphIndex, 0, BraceGlyphCount - 1);
    }

    /// <summary>
    /// Gets the approximate height of a brace glyph in font units.
    /// </summary>
    private static double GetGlyphHeight(int glyphIndex)
    {
        // Linear interpolation between known heights
        double ratio = glyphIndex / (double)(BraceGlyphCount - 1);
        return MinGlyphHeight + ratio * (MaxGlyphHeight - MinGlyphHeight);
    }

    /// <summary>
    /// Calculates the width required for a brace in staff spaces.
    /// </summary>
    public static double GetBraceWidth(double height = 11.0)
    {
        // Brace width scales with height, approximately
        double heightRatio = Math.Clamp(height / 20.0, 0.0, 1.0);
        return 1.5 + 1.0 * heightRatio;
    }

    /// <summary>
    /// Calculates the width required for a brace in staff spaces.
    /// </summary>
    public static double GetBraceWidth()
    {
        return GetBraceWidth(11.0);
    }
}

