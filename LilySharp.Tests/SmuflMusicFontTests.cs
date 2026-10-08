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

using System;
using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The import stage (docs/smufl-design.md §6 ②): the bundled SMuFL fonts are found by name and
/// answer the engine's questions out of their own metadata and font programs.
/// </summary>
public class SmuflMusicFontTests
{
    private static SmuflMusicFont Bundled(string name)
    {
        var font = MusicFonts.Find(name, out var tried);
        Assert.True(font is SmuflMusicFont, $"{name} not found; looked in:\n" + string.Join("\n", tried));
        return (SmuflMusicFont)font!;
    }

    [Theory]
    [InlineData("Bravura")]
    [InlineData("bravura")]
    [InlineData("Leland")]
    [InlineData("Petaluma")]
    public void TheBundledFonts_AreFoundByName_WithoutRegardToCase(string name)
    {
        var font = Bundled(name);
        Assert.Equal(name, font.Name, StringComparer.OrdinalIgnoreCase);
        Assert.Same(font, MusicFonts.Find(name.ToUpperInvariant(), out _));
    }

    [Fact]
    public void Emmentaler_IsItself()
        => Assert.Same(EmmentalerMusicFont.Instance, MusicFonts.Find("emmentaler", out _));

    [Fact]
    public void AMissingFont_NamesEveryPlaceLooked()
    {
        Assert.Null(MusicFonts.Find("NoSuchFont", out var tried));
        Assert.NotEmpty(tried);
        Assert.Contains(tried, t => t.EndsWith("nosuchfont_metadata.json", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tried, t => t.Contains("SMuFL", StringComparison.Ordinal));
    }

    [Fact]
    public void Bravura_HasEverySmuflGlyph_WithItsBoxAndAdvance()
    {
        var font = Bundled("Bravura");
        var full = font.FullSize;
        foreach (var e in MusicGlyphs.Table)
        {
            if (e.SmuflCodepoint == 0)
            {
                // A feta. glyph is Emmentaler's alone.
                Assert.False(font.Has(e.Glyph), e.SmuflName);
                continue;
            }
            Assert.True(font.Has(e.Glyph), e.SmuflName);
            Assert.Equal((char)e.SmuflCodepoint, font.Codepoint(e.Glyph));
            Assert.Equal(e.Glyph, font.GlyphOf(font.Codepoint(e.Glyph)));
            var m = full.Metrics(e.Glyph);
            Assert.NotNull(m.DesignBox);
            if (e.Glyph is MusicGlyph.WiggleTrill or MusicGlyph.WiggleArpeggiatoUp)
            {
                // A wiggle's design box is one repetition (its repeatOffset) wide — LILC's
                // trill element — and its outline overhangs it (SmuflPlacementTests).
                Assert.Equal(m.OutlineBox!.Value with { Right = m.DesignBox!.Value.Right }, m.DesignBox);
                Assert.True(m.DesignBox.Value.Right < m.OutlineBox.Value.Right, e.SmuflName);
            }
            else
                Assert.Equal(m.DesignBox, m.OutlineBox);
            Assert.NotNull(m.Advance);
        }
        // Bravura's metadata, verbatim: the black head is 1.18 wide and ±0.5 tall, its advance
        // 1.18, its up stem at the right edge above centre and its down stem at the left below.
        Assert.Equal(new GlyphMetrics.BBox(0.0, -0.5, 1.18, 0.5), full.Box(MusicGlyph.NoteheadBlack));
        Assert.Equal(1.18, full.Advance(MusicGlyph.NoteheadBlack));
        var up = full.StemUpAttachment(MusicGlyph.NoteheadBlack);
        Assert.Equal(1.18, up.X, 6);
        Assert.True(up.Y > 0);
        var down = full.StemDownAttachment(MusicGlyph.NoteheadBlack);
        Assert.Equal(0.0, down.X, 6);
        Assert.True(down.Y < 0);
        Assert.Equal(0.13, font.Metadata.EngravingDefaults["staffLineThickness"]);
        Assert.Equal("Bravura", font.FaceFamily(font.DefaultDesign));
        Assert.Equal("Bravura.otf", font.FaceFile(font.DefaultDesign));
        Assert.Equal("Bravura.woff2", font.WebFaceFile(font.DefaultDesign));
        Assert.True(font.TryParseFamily("bravura", out int rounded));
        Assert.Equal(font.DefaultDesign, rounded);
        Assert.False(font.TryParseFamily("Emmentaler", out _));
    }

    [Fact]
    public void Leland_ReadsAdvancesFromItsProgram_AndLacksWhatItsMetadataLacks()
    {
        var font = Bundled("Leland");
        Assert.Empty(font.Metadata.GlyphAdvanceWidths);
        double advance = font.FullSize.Advance(MusicGlyph.NoteheadBlack);
        Assert.InRange(advance, 1.0, 2.0);
        Assert.True(font.Has(MusicGlyph.NoteheadBlack));
        // Leland's metadata names no figured-bass digits and no X whole head.
        Assert.False(font.Has(MusicGlyph.Figbass0));
        Assert.False(font.Has(MusicGlyph.NoteheadXWhole));
        Assert.Equal(default, font.FullSize.Metrics(MusicGlyph.Figbass0));
        Assert.Throws<KeyNotFoundException>(() => font.Codepoint(MusicGlyph.Figbass0));
        // No WOFF2 ships with Leland: the SVG embeds the OTF.
        Assert.Equal("Leland.otf", font.WebFaceFile(font.DefaultDesign));
    }

    [Fact]
    public void ASizedDesign_IsTheOneDesignMagnified()
    {
        var font = Bundled("Bravura");
        var grace = font.SizedAt(-3);
        Assert.Equal(1.18 * EmmentalerDesignSize.Magstep(-3), grace.Box(MusicGlyph.NoteheadBlack).Width, 12);
        Assert.Equal(EmmentalerDesignSize.Magstep(-3), grace.Magnification, 12);
        Assert.Same(font.DesignAt(-3), font.DesignAt(0));
        Assert.Same(font.FullSize, grace.Unscaled);
        Assert.Equal(font.DefaultDesign, grace.Rounded);
        Assert.Equal(EmmentalerFaces.DefaultDesign, font.DefaultDesign);
    }

    /// <summary>
    /// The runtime walk on the OTHER horizon reproduces the generator's baked pairs: the
    /// 20's sharp, read from the same OTF, at every height of its box to the six decimals
    /// the generator writes.
    /// </summary>
    [Fact]
    public void TheHorizontalWalk_IsTheGenerators_OnEmmentalersSharp()
    {
        var path = TextFontMetrics.MusicGlyphPath(EmmentalerGlyphs.AccidentalSharp, 20);
        Assert.NotNull(path);
        var (left, right) = TextOutlineSkylines.FlattenPathHorizontal(path!, 4.0 / 1000.0);
        var walkedLeft = HorizontalSkyline.FromSignedBuildings(HorizontalDirection.Left, left);
        var walkedRight = HorizontalSkyline.FromSignedBuildings(HorizontalDirection.Right, right);
        var (bakedLeft, bakedRight) = GlyphMetrics.AccidentalSkylinePair("sharp", 20);
        Assert.Equal(bakedLeft.Buildings.Count, walkedLeft.Buildings.Count);
        Assert.Equal(bakedRight.Buildings.Count, walkedRight.Buildings.Count);
        for (double y = -1.6; y <= 1.6; y += 0.005)
        {
            Assert.Equal(bakedLeft.X(y), walkedLeft.X(y), 5);
            Assert.Equal(bakedRight.X(y), walkedRight.X(y), 5);
        }
    }

    /// <summary>The vertical walk is the generator's too: the 20's G clef, quad for quad.</summary>
    [Fact]
    public void TheVerticalWalk_IsTheGenerators_OnEmmentalersGClef()
    {
        var path = TextFontMetrics.MusicGlyphPath(EmmentalerGlyphs.GClef, 20);
        Assert.NotNull(path);
        var (up, down) = TextOutlineSkylines.FlattenPath(path!, 4.0 / 1000.0);
        var (bakedDown, bakedUp) = GlyphMetrics.ClefVerticalSkylineQuads("G");
        AssertSameQuads(bakedUp, up);
        AssertSameQuads(bakedDown, down);
    }

    private static void AssertSameQuads(double[] expected, double[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        static IEnumerable<(double, double, double, double)> Quads(double[] q)
            => Enumerable.Range(0, q.Length / 4).Select(i => (q[4 * i], q[4 * i + 1], q[4 * i + 2], q[4 * i + 3]))
                .OrderBy(t => t.Item1).ThenBy(t => t.Item4).ThenBy(t => t.Item2);
        // The generator writes six decimals and rounds, so a walked value may sit up to half a
        // unit of the sixth decimal from the baked one (the G clef's −0.3551875 does).
        const double tolerance = 1e-6;
        foreach (var (e, a) in Quads(expected).Zip(Quads(actual)))
        {
            Assert.Equal(e.Item1, a.Item1, tolerance);
            Assert.Equal(e.Item2, a.Item2, tolerance);
            Assert.Equal(e.Item3, a.Item3, tolerance);
            Assert.Equal(e.Item4, a.Item4, tolerance);
        }
    }

    [Fact]
    public void ASmuflFont_WalksItsOwnOutlines_AndHandsBackItsBrace()
    {
        var font = Bundled("Bravura");
        var (left, right) = font.FullSize.HorizontalSkylinePair(MusicGlyph.AccidentalSharp);
        Assert.NotEmpty(left.Buildings);
        Assert.NotEmpty(right.Buildings);
        var box = font.FullSize.Box(MusicGlyph.AccidentalSharp);
        // The walked right profile never reaches past the metadata's box.
        for (double y = box.Bottom; y <= box.Top; y += 0.05)
            Assert.True(right.X(y) <= box.Right + 1e-6, $"right profile {right.X(y)} past the box {box.Right} at y={y}");
        var (down, up) = font.FullSize.VerticalSkylineQuads(MusicGlyph.GClef);
        Assert.NotEmpty(up);
        Assert.NotEmpty(down);
        Assert.Equal(default, font.FullSize.VerticalSkylineQuads(MusicGlyph.FetaClefsTabChange));
        var brace = font.Brace(13.0);
        Assert.Equal('', brace.Codepoint);
        Assert.True(brace.Width > 0);
        Assert.Equal(0.0, font.Kern(MusicGlyph.DynamicForte, MusicGlyph.DynamicForte));
    }
}
