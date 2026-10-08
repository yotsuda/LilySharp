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

using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A quarter-tone accidental is placed by its own glyph's box and outline, like the five
/// others (session 868).
/// </summary>
/// <remarks>
/// ⚠️ THE DEFECT: the generators emitted no row for the four quarter-tone glyphs, so
/// <see cref="GlyphMetrics.GetAccidentalBBox(MusicFontDesign, string?)"/> answered an EMPTY box
/// and the accidental column reserved no width — the sign was drawn over its note head (and,
/// in a chord, over its neighbour's). Its skyline borrowed the natural's.
/// </remarks>
public class QuarterToneAccidentalTests
{
    [Theory]
    [InlineData("quarterSharp", "AccidentalQuarterToneSharpStein")]
    [InlineData("threeQuarterSharp", "AccidentalThreeQuarterTonesSharpStein")]
    [InlineData("quarterFlat", "AccidentalQuarterToneFlatStein")]
    [InlineData("threeQuarterFlat", "AccidentalThreeQuarterTonesFlatZimmermann")]
    public void TheAccidental_IsPlacedByItsOwnGlyph(string kind, string glyphName)
    {
        var glyph = System.Enum.Parse<MusicGlyph>(glyphName);
        var design = MusicFont.Current.FullSize;
        var box = GlyphMetrics.GetAccidentalBBox(design, kind);
        Assert.True(box.Width > 0.5, $"{kind}: {box}");
        Assert.Equal(design.Box(glyph), box);
        Assert.Equal(MusicGlyphs.Accidental(kind), glyph);
        // Its own outline, not the natural's.
        var (left, _) = design.HorizontalSkylinePair(glyph);
        var (natLeft, _) = design.HorizontalSkylinePair(MusicGlyph.AccidentalNatural);
        Assert.NotSame(natLeft, left);
    }

    /// <remarks>LILYPOND-REF: mf/feta-sharps.mf:243-280 fet_beginchar sharp.slashslash.stem (−0.8, 1);
    /// mf/feta-sharps.mf:421-471 fet_beginchar sharp.slashslash.stemstemstem (−0.8, 1);
    /// mf/feta-flats.mf:540-555 fet_beginchar mirroredflat (−0.8, 2); mf/feta-flats.mf:422-536 fet_beginchar mirroredflat.flat (0, 0.8).</remarks>
    [Theory]
    [InlineData("quarterSharp", -0.8, 1.0)]
    [InlineData("threeQuarterSharp", -0.8, 1.0)]
    [InlineData("quarterFlat", -0.8, 2.0)]
    [InlineData("threeQuarterFlat", 0.0, 0.8)]
    public void TheLedgerShortening_IsTheGlyphsOwn(string kind, double bottom, double top)
        => Assert.Equal((bottom, top), GlyphMetrics.AccidentalLedgerShorteningRange(kind, parenthesized: false));
}
