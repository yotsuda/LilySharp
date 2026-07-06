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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Builds glyph-shape-aware horizontal skylines for accidentals, replacing the
/// coarser bounding-box approximation used previously.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/accidental-placement.cc:254-301 — set_ape_skylines uses the
/// glyph's own <c>horizontal-skylines</c> property derived from font metrics.
/// LP queries <c>get_property(a, "horizontal-skylines")</c> on each accidental Grob,
/// which in turn comes from the Emmentaler font. LilySharp does not embed full
/// glyph outlines, so we approximate each accidental as a small set of
/// rectangular sub-boxes that capture the silhouette's shape (e.g. a flat is a
/// wide round bowl on top + a narrow vertical stem on the bottom). These
/// sub-boxes feed <see cref="Skyline.FromBoxes"/> which already produces a
/// stepped skyline; the result is tighter packing for flats and double-flats
/// (where the body is much narrower than the bowl) at the cost of a tiny
/// per-call overhead.
///
/// Sharps, naturals and double-sharps benefit little from sub-division because
/// their silhouettes are nearly rectangular; we keep the BBox fallback for them.
/// </remarks>
internal static class AccidentalGlyphSkyline
{
    // Fractions describe the flat glyph's bowl-on-top + stem-on-bottom shape as
    // ratios of the supplied BBox. Tuned via outline sampling against the bundled
    // Emmentaler-20 font (the 2-rect approximation matches LP's stencil-skyline
    // intent without slavish per-pixel reproduction).
    // LILYPOND-REF: mf/feta-flats.mf — flat shape (bowl + vertical stem)
    // LILYPOND-REF: scm/define-grobs.scm Accidental — uses glyph stencil for skyline.

    /// <summary>Y-fraction at which a flat's bowl meets its stem (0 = BBox bottom, 1 = BBox top).</summary>
    private const double FlatBowlSplitFrac = 0.448;

    /// <summary>X-fraction occupied by the flat's stem (the rest is hollow below the bowl).</summary>
    private const double FlatStemXFrac = 0.277;

    /// <summary>
    /// Builds a stepped horizontal skyline for an accidental glyph at the given placement.
    /// </summary>
    /// <param name="accidental">Accidental kind: "sharp", "flat", "natural", "doubleSharp", "doubleFlat".</param>
    /// <param name="yBottom">Bottom Y of the glyph's bounding box (after vertical placement).</param>
    /// <param name="yTop">Top Y of the glyph's bounding box.</param>
    /// <param name="xLeft">Left X of the glyph's bounding box.</param>
    /// <param name="xRight">Right X of the glyph's bounding box.</param>
    /// <param name="direction">Which side of the glyph the skyline should describe.</param>
    /// <returns>A skyline that approximates the glyph silhouette with one or more sub-boxes.</returns>
    public static Skyline Build(
        string accidental,
        double yBottom, double yTop, double xLeft, double xRight,
        Skyline.Direction direction)
    {
        return accidental switch
        {
            "flat" => BuildFlat(yBottom, yTop, xLeft, xRight, direction),
            "doubleFlat" => BuildDoubleFlat(yBottom, yTop, xLeft, xRight, direction),
            _ => Skyline.FromBox(yBottom, yTop, xLeft, xRight, direction),
        };
    }

    /// <summary>
    /// Tiny epsilon used to keep stacked sub-boxes from touching at their Y boundary,
    /// which would otherwise be merged into a single rectangle by the skyline's
    /// segment-merger.
    /// </summary>
    private const double SeamEpsilon = 1e-6;

    /// <summary>
    /// Flat = wide bowl on top + narrow stem on the bottom.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: mf/feta-flats.mf — flat shape definition; bowl is a curve attached
    /// to a vertical stem. We approximate as two stacked rectangles.
    /// </remarks>
    private static Skyline BuildFlat(
        double yBottom, double yTop, double xLeft, double xRight,
        Skyline.Direction direction)
    {
        double height = yTop - yBottom;
        double width = xRight - xLeft;
        double splitY = yBottom + height * FlatBowlSplitFrac;
        double stemXRight = xLeft + width * FlatStemXFrac;

        return Skyline.FromBoxes(new[]
        {
            (splitY + SeamEpsilon, yTop, xLeft, xRight),    // bowl: full width above the split
            (yBottom, splitY,             xLeft, stemXRight),// stem: narrow strip below
        }, direction);
    }

    /// <summary>
    /// Double flat = two flats side-by-side. Each contributes its own bowl + stem.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: mf/feta-flats.mf — double-flat is two flat glyphs.
    /// </remarks>
    private static Skyline BuildDoubleFlat(
        double yBottom, double yTop, double xLeft, double xRight,
        Skyline.Direction direction)
    {
        double height = yTop - yBottom;
        double width = xRight - xLeft;
        // Derive the doubleflat composition from the actual Emmentaler glyph BBoxes
        // (auto-extracted into GlyphMetrics) so this stays in sync with the font.
        // LILYPOND-REF: mf/feta-flats.mf — flatflat draws two flats side-by-side with overlap.
        double FlatWidthFrac = GlyphMetrics.AccidentalFlat.Width / GlyphMetrics.AccidentalDoubleFlat.Width;
        double SecondFlatStartFrac = (GlyphMetrics.AccidentalDoubleFlat.Right - GlyphMetrics.AccidentalFlat.Right)
            / GlyphMetrics.AccidentalDoubleFlat.Width;

        double splitY = yBottom + height * FlatBowlSplitFrac;
        double flat1Right = xLeft + width * FlatWidthFrac;
        double flat1StemRight = xLeft + width * (FlatStemXFrac * FlatWidthFrac);
        double flat2Left = xLeft + width * SecondFlatStartFrac;
        double flat2StemRight = flat2Left + width * (FlatStemXFrac * FlatWidthFrac);

        // Bowls and stems are each given their own segment. The bowls (full-width)
        // would otherwise dominate the merged silhouette and erase the stem
        // narrowing — so we keep them on separate Y bands with a tiny seam.
        // For Right-direction skylines the merger keeps the outermost (max) X, so
        // we emit ONE merged bowl segment spanning [xLeft, xRight] in the bowl band,
        // and ONE merged stem segment whose right edge is the SECOND stem (the
        // first stem is occluded by the second's column).
        return Skyline.FromBoxes(new[]
        {
            (splitY + SeamEpsilon, yTop, xLeft, xRight),     // combined bowl band: outermost = xRight
            (yBottom, splitY,             xLeft,     flat1StemRight), // first stem
            (yBottom, splitY,             flat2Left, flat2StemRight), // second stem
        }, direction);
    }
}
