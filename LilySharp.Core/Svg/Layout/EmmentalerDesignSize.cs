// Lily# — a music notation language and engraver.
// Copyright (C) 2026 yotsuda
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

using System;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Which Emmentaler DESIGN a glyph at a given size is drawn from — Emmentaler is optically
/// sized, so a smaller staff is not the same outline scaled down.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/font-select.cc:41-70 best_rounded_design_size — picks the design whose
///   RATIO to the requested size is closest to 1 (not the nearest by difference), and
///   lily/font-select.cc:179-186 in select_font then loads <c>emmentaler-&lt;rounded&gt;</c> and scales it
///   by <c>requested_size / actual_size</c>.
/// LILYPOND-REF: scm/lily-library.scm:1702-1710 feta-design-size-mapping — the rounded size
///   in the file name against the design size the file really carries.
/// <para>
/// ⚠️ THE DESIGNS ARE NOT SCALES OF EACH OTHER. MEASURED from the LILC tables of LilyPond
/// 2.26.0's own font files, the black notehead's right edge in each design's OWN staff
/// spaces:
/// <code>
///   design   11        13        14        16        18        20        23        26
///   head   1.289478  1.294282  1.298161  1.300819  1.302806  1.304200  1.305122  1.305873
/// </code>
/// A grace asks for font-size −3, i.e. 20·2^(−3/6) = 14.142, which lands on design 14; its
/// head times magstep(−3) is 0.917939, and that is LilyPond's own drawn value to six places.
/// Scaling the 20 design instead gives 0.922209 — the 0.004270 that twelve ledger points in
/// the <c>grace.column</c> island carry.
/// </para>
/// <para>
/// ⚠️ ONE DECISION, TWO READERS. The design chosen here is what the METRICS are looked up in
/// AND what the renderer draws from; they must not be decided separately, or the box a
/// column reserves stops being the box the glyph fills.
/// </para>
/// </remarks>
public static class EmmentalerDesignSize
{
    /// <summary>The staff height a Lily# score is laid out at, in points.</summary>
    /// <remarks>
    /// LILYPOND-REF: lily/font-select.cc:104-107 staff-height over point_constant — the music
    ///   font's base size, and Lily#'s staff is LilyPond's default 20pt.
    /// </remarks>
    public const double BaseSizePoints = 20.0;

    /// <summary>
    /// The rounded size (the number in the file name) against the design size the file
    /// actually carries.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/lily-library.scm:1702-1710 feta-design-size-mapping.</remarks>
    public static readonly (int Rounded, double Actual)[] Designs =
    {
        (11, 11.22),
        (13, 12.60),
        (14, 14.14),
        (16, 15.87),
        (18, 17.82),
        (20, 20.00),
        (23, 22.45),
        (26, 25.20),
    };

    /// <summary>
    /// The design a glyph of <paramref name="requestedSize"/> points is drawn from, with the
    /// design size that file carries.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/font-select.cc:41-70 best_rounded_design_size.
    /// ⚠️ The comparison is a RATIO, always taken the larger way round, and that is not the
    /// same rule as a difference: a ratio switches designs at the GEOMETRIC mean of two design
    /// sizes and a difference at the ARITHMETIC one. Between 12.60 and 14.14 those are 13.3475
    /// and 13.37, so anything asking for a size in that band would be drawn from a different
    /// FILE under the two rules (EmmentalerDesignSizeTests pins it).
    /// </remarks>
    public static (int Rounded, double Actual) BestRounded(double requestedSize)
    {
        double minRatio = double.PositiveInfinity;
        (int Rounded, double Actual) best = Designs[^1];

        foreach (var (rounded, actual) in Designs)
        {
            double ratio = requestedSize > actual
                ? requestedSize / actual
                : actual / requestedSize;
            if (ratio < minRatio)
            {
                minRatio = ratio;
                best = (rounded, actual);
            }
        }
        return best;
    }

    /// <summary>
    /// The design a glyph carrying <paramref name="fontSizeStep"/> is drawn from — the
    /// <c>font-size</c> a grob states, in LilyPond's sixths-of-an-octave steps (a grace is
    /// −3).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/font-select.cc:115-117 requested_size —
    ///   <c>requested_size = base_size * pow (2, font-size / 6)</c>.
    /// </remarks>
    public static (int Rounded, double Actual) ForFontSizeStep(double fontSizeStep)
        => BestRounded(RequestedSize(fontSizeStep));

    /// <summary>The point size a grob with this <c>font-size</c> asks the font for.</summary>
    /// <remarks>LILYPOND-REF: lily/font-select.cc:115-117 requested_size.</remarks>
    public static double RequestedSize(double fontSizeStep)
        => BaseSizePoints * Math.Pow(2.0, fontSizeStep / 6.0);

    /// <summary>
    /// The magnification LilyPond then applies to the chosen file:
    /// <c>requested_size / actual_size</c>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/font-select.cc:185 find_scaled_font (…, requested_size / actual_size).
    /// It is NOT the magstep: the magstep takes the glyph from the design's own staff spaces
    /// to the page's, while this one corrects for the design size not being exactly what was
    /// asked (14.14 against 14.142 for a grace, so 1.00015).
    /// </remarks>
    public static double Magnification(double fontSizeStep)
    {
        double requested = RequestedSize(fontSizeStep);
        return requested / BestRounded(requested).Actual;
    }
}
