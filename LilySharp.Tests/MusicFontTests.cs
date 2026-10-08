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
using System.Reflection;
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The SMuFL foundation (docs/smufl-design.md §6 ①): the <see cref="MusicGlyph"/> vocabulary and
/// Emmentaler wrapped in the common shape — complete, and losing nothing the generated tables hold.
/// </summary>
public class MusicFontTests
{
    [Fact]
    public void EveryMember_HasExactlyOneRow()
    {
        var members = Enum.GetValues<MusicGlyph>();
        var rows = MusicGlyphs.Table.Select(e => e.Glyph).ToList();
        Assert.Equal(members.Length, rows.Count);
        Assert.Equal(members.OrderBy(m => m), rows.OrderBy(m => m));
        foreach (var m in members)
            Assert.Equal(m, MusicGlyphs.Of(m).Glyph);
    }

    [Fact]
    public void TheSmuflColumn_IsANameOrAFetaName_AndRoundTrips()
    {
        Assert.Equal(MusicGlyphs.Table.Length, MusicGlyphs.Table.Select(e => e.SmuflName).Distinct().Count());
        foreach (var e in MusicGlyphs.Table)
        {
            bool feta = e.SmuflName.StartsWith("feta.", StringComparison.Ordinal);
            // A feta. row has no SMuFL code point; every other row has one in SMuFL's PUA.
            if (feta)
            {
                Assert.Equal(0, e.SmuflCodepoint);
                Assert.Equal("feta." + e.FetaName, e.SmuflName);
                Assert.StartsWith("Feta", e.Glyph.ToString(), StringComparison.Ordinal);
            }
            else
            {
                Assert.InRange(e.SmuflCodepoint, 0xE000, 0xF8FF);
                // The member is the SMuFL name in PascalCase (a leading digit spelled out).
                string expected = e.SmuflName == "6stringTabClef"
                    ? "SixStringTabClef"
                    : char.ToUpperInvariant(e.SmuflName[0]) + e.SmuflName[1..];
                Assert.Equal(expected, e.Glyph.ToString());
            }
            Assert.True(MusicGlyphs.TryFromSmuflName(e.SmuflName, out var back));
            Assert.Equal(e.Glyph, back);
        }
        Assert.Equal(MusicGlyphs.Table.Where(e => e.SmuflCodepoint != 0).Count(),
            MusicGlyphs.Table.Where(e => e.SmuflCodepoint != 0).Select(e => e.SmuflCodepoint).Distinct().Count());
    }

    [Fact]
    public void EveryEmmentalerGlyphConstant_HasARow()
    {
        var codes = MusicGlyphs.Table.Select(e => e.EmmentalerCode).ToHashSet();
        var missing = typeof(EmmentalerGlyphs)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(char))
            .Where(f => !codes.Contains((char) f.GetRawConstantValue()!))
            .Select(f => f.Name)
            .ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void TheWrappedEmmentaler_DrawsEveryGlyph_WithItsOwnCharacter()
    {
        var font = EmmentalerMusicFont.Instance;
        foreach (var e in MusicGlyphs.Table)
        {
            Assert.True(font.Has(e.Glyph));
            Assert.Equal(e.EmmentalerCode, font.Codepoint(e.Glyph));
        }
    }

    /// <summary>
    /// ⚠️ "Nothing Emmentaler has is lost" (docs/smufl-design.md §0 3), held mechanically: every
    /// metric the generator writes into a design table is answered, in all eight designs at once,
    /// by some glyph of the wrapped font. A property added to the generator and not wired into
    /// <c>EmmentalerMusicFont.MetricsOf</c> fails here by name.
    /// </summary>
    [Fact]
    public void EveryGeneratedMetric_IsReachedByAGlyph()
    {
        var designs = GlyphMetrics.AllDesigns;
        var props = typeof(GlyphMetrics.DesignMetrics)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(GlyphMetrics.BBox) || p.PropertyType == typeof(double)
                || p.PropertyType == typeof((double, double)))
            .Where(p => p.Name is not ("DesignSize" or "Magnification"))
            .ToList();
        // The stem anchors are the one tuple-typed family (§3 #2); a test that looked at boxes
        // and advances only would pass with every anchor dropped — the first version did.
        Assert.Equal(22, props.Count(p => p.PropertyType == typeof((double, double))));
        var glyphs = Enum.GetValues<MusicGlyph>();
        var answers = glyphs.ToDictionary(
            g => g, g => designs.Select(d => EmmentalerMusicFont.MetricsOf(g, d)).ToArray());

        var orphans = new List<string>();
        foreach (var p in props)
        {
            object?[] want = designs.Select(d => p.GetValue(d)).ToArray();
            Func<MusicGlyphMetrics, object?> field =
                p.Name.EndsWith("StemAttachmentDown", StringComparison.Ordinal) ? m => m.StemDown
                : p.Name.EndsWith("StemAttachment", StringComparison.Ordinal) ? m => m.StemUp
                : p.Name.EndsWith("Outline", StringComparison.Ordinal) ? m => m.OutlineBox
                : p.PropertyType == typeof(double) ? m => m.Advance
                : m => m.DesignBox;
            bool reached = answers.Values.Any(a =>
                Enumerable.Range(0, designs.Length).All(i => Equals(field(a[i]), want[i])));
            if (!reached)
                orphans.Add(p.Name);
        }
        Assert.Empty(orphans);
    }

    /// <summary>
    /// A reader moved off a flat constant (<c>GlyphMetrics.ClefG</c>, the 20 design) onto
    /// <c>MusicFont.Current.FullSize</c> reads the same number: every generated metric's flat
    /// constant equals the full-size design's, bit for bit.
    /// </summary>
    [Fact]
    public void TheFullSizeDesign_IsTheFlatConstants()
    {
        var full = GlyphMetrics.AtFontSize(0);
        var mismatches = new List<string>();
        foreach (var p in typeof(GlyphMetrics.DesignMetrics).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.PropertyType == typeof(GlyphMetrics.BBox) || p.PropertyType == typeof(double)
                         || p.PropertyType == typeof((double, double)))
                     .Where(p => p.Name is not ("DesignSize" or "Magnification")))
        {
            var flat = typeof(GlyphMetrics).GetField(p.Name, BindingFlags.Public | BindingFlags.Static);
            Assert.True(flat != null, $"no flat constant named {p.Name}");
            object? a = flat!.IsLiteral ? flat.GetRawConstantValue() : flat.GetValue(null);
            if (!Equals(a, p.GetValue(full)))
                mismatches.Add(p.Name);
        }
        Assert.Empty(mismatches);
        Assert.Equal(GlyphMetrics.ClefG, EmmentalerMusicFont.Instance.FullSize.Box(MusicGlyph.GClef));
    }

    [Fact]
    public void TheCurrentFont_IsEmmentaler_AndAScopeRestoresIt()
    {
        Assert.Same(EmmentalerMusicFont.Instance, MusicFont.Current);
        using (MusicFont.Use(EmmentalerMusicFont.Instance))
            Assert.Same(EmmentalerMusicFont.Instance, MusicFont.Current);
        Assert.Same(EmmentalerMusicFont.Instance, MusicFont.Current);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-3)]
    [InlineData(-6)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(-2.5)]
    public void TheDesign_IsTheOneTheLayoutReads(double step)
    {
        var table = GlyphMetrics.ForFontSizeStep(step);
        var design = EmmentalerMusicFont.Instance.DesignAt(step);
        Assert.Equal(table.DesignSize, design.DesignSize);
        Assert.Equal(table.NoteheadBlack, design.Metrics(MusicGlyph.NoteheadBlack).DesignBox);
        Assert.Equal(table.NoteheadBlackOutline, design.Metrics(MusicGlyph.NoteheadBlack).OutlineBox);
        Assert.Equal(table.NoteheadBlackAdvance, design.Metrics(MusicGlyph.NoteheadBlack).Advance);
    }

    /// <summary>
    /// The baked skylines and GPOS kerns (<c>GlyphSkylinesGenerated.cs</c>) are reached through
    /// the wrap, table for table — "nothing Emmentaler has is lost" for the outline tables and
    /// docs/smufl-design.md §3 #7. Reference equality: the wrap hands out the generated arrays
    /// themselves, which the readers' identity-keyed caches rely on.
    /// </summary>
    [Fact]
    public void EveryBakedSkylineAndKern_IsReachedByAGlyph()
    {
        var font = EmmentalerMusicFont.Instance;
        var accidentals = new[]
        {
            ("sharp", MusicGlyph.AccidentalSharp), ("flat", MusicGlyph.AccidentalFlat),
            ("natural", MusicGlyph.AccidentalNatural), ("doubleSharp", MusicGlyph.AccidentalDoubleSharp),
            ("doubleFlat", MusicGlyph.AccidentalDoubleFlat),
        };
        foreach (var (rounded, _) in EmmentalerDesignSize.Designs)
        {
            var design = font.Design(rounded);
            Assert.Equal(rounded, design.Rounded);
            foreach (var (kind, glyph) in accidentals)
            {
                var want = GlyphMetrics.AccidentalSkylinePair(kind, rounded);
                var got = design.HorizontalSkylinePair(glyph);
                Assert.Same(want.Left, got.Left);
                Assert.Same(want.Right, got.Right);
            }
            foreach (var (leftParen, glyph) in new[]
                     { (true, MusicGlyph.AccidentalParensLeft), (false, MusicGlyph.AccidentalParensRight) })
            {
                var want = GlyphMetrics.AccidentalParenSkylinePair(leftParen, rounded);
                var got = design.HorizontalSkylinePair(glyph);
                Assert.Same(want.Left, got.Left);
                Assert.Same(want.Right, got.Right);
            }
            // The quarter-tone accidentals have no baked pair and read the natural's, as before.
            Assert.Same(GlyphMetrics.AccidentalSkylinePair("natural", rounded).Left,
                design.HorizontalSkylinePair(MusicGlyph.AccidentalQuarterToneSharpStein).Left);
        }

        var full = font.FullSize;
        foreach (var (kind, glyph) in new[] { ("G", MusicGlyph.GClef), ("F", MusicGlyph.FClef), ("C", MusicGlyph.CClef) })
        {
            var want = GlyphMetrics.ClefVerticalSkylineQuads(kind);
            var got = full.VerticalSkylineQuads(glyph);
            Assert.Same(want.Down, got.Down);
            Assert.Same(want.Up, got.Up);
        }
        Assert.Same(GlyphMetrics.TrillElementVerticalSkylineQuads().Up,
            full.VerticalSkylineQuads(MusicGlyph.WiggleTrill).Up);
        foreach (char c in "pmfrszn")
        {
            var want = GlyphMetrics.DynamicLetterVerticalSkylineQuads(c);
            var got = full.VerticalSkylineQuads(MusicGlyphs.DynamicLetter(c)!.Value);
            Assert.NotNull(want.Up);
            Assert.Same(want.Down, got.Down);
            Assert.Same(want.Up, got.Up);
        }
        foreach (var glyph in new[] { MusicGlyph.KeyboardPedalPed, MusicGlyph.KeyboardPedalDot, MusicGlyph.KeyboardPedalUp })
        {
            var want = GlyphMetrics.PedalGlyphVerticalSkylineQuads(font.Codepoint(glyph));
            var got = full.VerticalSkylineQuads(glyph);
            Assert.NotNull(want.Up);
            Assert.Same(want.Down, got.Down);
            Assert.Same(want.Up, got.Up);
        }
        Assert.Equal(default, full.VerticalSkylineQuads(MusicGlyph.NoteheadBlack));

        foreach (char a in "pmfrszn")
            foreach (char b in "pmfrszn")
                Assert.Equal(GlyphMetrics.DynamicLetterKern(a, b),
                    font.Kern(MusicGlyphs.DynamicLetter(a)!.Value, MusicGlyphs.DynamicLetter(b)!.Value));
        foreach (char a in "0123456789")
            foreach (char b in "0123456789")
                Assert.Equal(GlyphMetrics.MeterDigitKern(a, b),
                    font.Kern(MusicGlyphs.TimeSigDigit(a)!.Value, MusicGlyphs.TimeSigDigit(b)!.Value));
        // Across the two cuts, and for any other pair, no kern; a non-digit is no digit.
        Assert.Equal(0.0, font.Kern(MusicGlyph.DynamicForte, MusicGlyph.TimeSig4));
        Assert.Equal(0.0, font.Kern(MusicGlyph.Fingering1, MusicGlyph.Fingering0));
        Assert.Null(MusicGlyphs.TimeSigDigit('+'));
    }

    /// <summary>The outline the skyline walk flattens is the design's own file, and the
    /// character a layout still carries comes back as its glyph.</summary>
    [Fact]
    public void TheOutline_IsTheDesignsOwnFile_AndACharacterComesBackAsItsGlyph()
    {
        var font = EmmentalerMusicFont.Instance;
        var path = font.OutlinePath(MusicGlyph.NoteheadBlack, 20);
        var want = TextFontMetrics.MusicGlyphPath(font.Codepoint(MusicGlyph.NoteheadBlack), 20);
        Assert.NotNull(path);
        Assert.NotNull(want);
        Assert.Equal(want!.Bounds, path!.Bounds);
        Assert.Equal(MusicGlyph.NoteheadBlack, font.GlyphOf(font.Codepoint(MusicGlyph.NoteheadBlack)));
        Assert.Equal(MusicGlyph.DynamicForte, font.GlyphOf('f'));
        // The two dots share one Emmentaler character; the first row of the table answers.
        Assert.Equal(MusicGlyph.AugmentationDot, font.GlyphOf(font.Codepoint(MusicGlyph.RepeatDot)));
        Assert.Null(font.GlyphOf('Q'));
    }

    /// <summary>The faces the backends draw from are the bundled Emmentaler files, named as
    /// they always were (§6 ① ⑷): the bare family for the score's own design.</summary>
    [Fact]
    public void TheFaces_AreTheBundledEmmentalerFiles()
    {
        var font = EmmentalerMusicFont.Instance;
        Assert.Equal(EmmentalerFaces.DefaultDesign, font.DefaultDesign);
        Assert.Equal("Emmentaler", font.FaceFamily(font.DefaultDesign));
        foreach (var (r, _) in EmmentalerDesignSize.Designs)
        {
            Assert.Equal(EmmentalerFaces.Family(r), font.FaceFamily(r));
            Assert.Equal(EmmentalerFaces.OtfFile(r), font.FaceFile(r));
            Assert.Equal(EmmentalerFaces.Woff2File(r), font.WebFaceFile(r));
            Assert.True(font.TryParseFamily(font.FaceFamily(r), out int back));
            Assert.Equal(r, back);
        }
        Assert.False(font.TryParseFamily("Emmentaler-Brace", out _));
        Assert.False(font.TryParseFamily("serif", out _));
    }
}
