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

namespace LilySharp.Core.Svg;

/// <summary>
/// Paper sizes following LilyPond conventions.
/// Default unit is millimeters, converted to points for output.
/// </summary>
public enum PaperSize
{
    /// <summary>A4, 210 × 297 mm (the default).</summary>
    A4,
    /// <summary>A5, 148 × 210 mm.</summary>
    A5,
    /// <summary>US Letter, 215.9 × 279.4 mm (8.5 × 11 in).</summary>
    Letter,
    /// <summary>US Legal, 215.9 × 355.6 mm (8.5 × 14 in).</summary>
    Legal,
    /// <summary>B5, 176 × 250 mm.</summary>
    B5,
}

/// <summary>
/// Paper dimensions and margin settings following LilyPond conventions.
/// All values are in millimeters internally, converted as needed for output.
/// </summary>
internal class PaperSettings
{
    // Unit conversion constants
    public const double MmPerPoint = 0.3528; // 1 pt = 0.3528 mm

    // Reference paper size for scaling (A4)
    private const double ReferenceWidth = 210.0;  // A4 width in mm
    private const double ReferenceHeight = 297.0; // A4 height in mm

    /// <summary>
    /// Paper width in mm.
    /// </summary>
    public double PaperWidth { get; set; } = 210.0;

    /// <summary>
    /// Paper height in mm.
    /// </summary>
    public double PaperHeight { get; set; } = 297.0;

    /// <summary>
    /// Top margin in mm. Default: 10mm, scaled to paper size.
    /// </summary>
    public double TopMargin { get; set; } = 10.0;

    /// <summary>
    /// Bottom margin in mm. Default: 10mm, scaled to paper size.
    /// </summary>
    public double BottomMargin { get; set; } = 10.0;

    /// <summary>
    /// Left margin in mm. Default: 15mm, scaled to paper size.
    /// </summary>
    public double LeftMargin { get; set; } = 15.0;

    /// <summary>
    /// Right margin in mm. Default: 15mm, scaled to paper size.
    /// </summary>
    public double RightMargin { get; set; } = 15.0;

    /// <summary>
    /// Indentation for the first system in mm. Default: 15mm.
    /// </summary>
    public double Indent { get; set; } = 15.0;

    /// <summary>
    /// Indentation for subsequent systems in mm. Default: 0mm.
    /// </summary>
    public double ShortIndent { get; set; } = 0.0;

    /// <summary>
    /// Whether to use two-sided layout (different margins for odd/even pages).
    /// </summary>
    public bool TwoSided { get; set; } = false;

    /// <summary>
    /// Inner margin for two-sided printing in mm. Default: 15mm.
    /// </summary>
    public double InnerMargin { get; set; } = 15.0;

    /// <summary>
    /// Outer margin for two-sided printing in mm. Default: 15mm.
    /// </summary>
    public double OuterMargin { get; set; } = 15.0;

    /// <summary>
    /// Binding offset added to inner margin in mm. Default: 5mm.
    /// </summary>
    public double BindingOffset { get; set; } = 5.0;

    /// <summary>
    /// Staff size in points. Default: 20pt.
    /// </summary>
    public double StaffSize { get; set; } = 20.0;

    /// <summary>
    /// Staff space (1/4 of staff height) in points.
    /// </summary>
    public double StaffSpace => StaffSize / 4.0;

    /// <summary>
    /// Line width (printable area width) in mm.
    /// Calculated as paper-width - left-margin - right-margin.
    /// </summary>
    public double LineWidth => PaperWidth - LeftMargin - RightMargin;

    /// <summary>
    /// Creates default A4 paper settings.
    /// </summary>
    public static PaperSettings Default => new();

    /// <summary>
    /// Creates paper settings for a specific paper size with scaled margins.
    /// </summary>
    public static PaperSettings ForPaperSize(PaperSize size)
    {
        var (width, height) = GetPaperDimensions(size);
        var settings = new PaperSettings
        {
            PaperWidth = width,
            PaperHeight = height
        };
        settings.ScaleMarginsToSize();
        return settings;
    }

    /// <summary>
    /// Gets paper dimensions in mm for a given paper size.
    /// </summary>
    private static (double Width, double Height) GetPaperDimensions(PaperSize size) => size switch
    {
        PaperSize.A4 => (210.0, 297.0),
        PaperSize.A5 => (148.0, 210.0),
        PaperSize.Letter => (215.9, 279.4),
        PaperSize.Legal => (215.9, 355.6),
        PaperSize.B5 => (176.0, 250.0),
        _ => (210.0, 297.0)
    };

    /// <summary>
    /// Scales margins based on paper size relative to A4 reference.
    /// LilyPond convention: horizontal dimensions scale by width ratio,
    /// vertical dimensions scale by height ratio.
    /// </summary>
    public void ScaleMarginsToSize()
    {
        double widthRatio = PaperWidth / ReferenceWidth;
        double heightRatio = PaperHeight / ReferenceHeight;

        // Vertical margins (scale by height ratio)
        TopMargin = 10.0 * heightRatio;
        BottomMargin = 10.0 * heightRatio;

        // Horizontal margins and indents (scale by width ratio)
        LeftMargin = 15.0 * widthRatio;
        RightMargin = 15.0 * widthRatio;
        Indent = 15.0 * widthRatio;
        InnerMargin = 15.0 * widthRatio;
        OuterMargin = 15.0 * widthRatio;
        BindingOffset = 5.0 * widthRatio;
    }

    /// <summary>
    /// Gets the effective left margin for a given page number.
    /// </summary>
    public double GetLeftMargin(int pageNumber)
    {
        if (!TwoSided)
            return LeftMargin;

        // Odd pages: outer margin on left
        // Even pages: inner margin + binding offset on left
        return (pageNumber % 2 == 1)
            ? OuterMargin
            : InnerMargin + BindingOffset;
    }

    /// <summary>
    /// Gets the effective right margin for a given page number.
    /// </summary>
    public double GetRightMargin(int pageNumber)
    {
        if (!TwoSided)
            return RightMargin;

        // Odd pages: inner margin + binding offset on right
        // Even pages: outer margin on right
        return (pageNumber % 2 == 1)
            ? InnerMargin + BindingOffset
            : OuterMargin;
    }

    /// <summary>
    /// Calculates the printable height excluding margins, headers, and footers.
    /// </summary>
    public double GetPrintableHeight(double headerHeight = 0, double footerHeight = 0)
    {
        return PaperHeight - TopMargin - BottomMargin - headerHeight - footerHeight;
    }

    /// <summary>
    /// Converts millimeters to points.
    /// </summary>
    public static double MmToPoints(double mm) => mm / MmPerPoint;

    /// <summary>
    /// Converts points to millimeters.
    /// </summary>
    public static double PointsToMm(double pt) => pt * MmPerPoint;
}