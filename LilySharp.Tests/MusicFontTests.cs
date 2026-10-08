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
            .Where(p => p.PropertyType == typeof(GlyphMetrics.BBox) || p.PropertyType == typeof(double))
            .Where(p => p.Name is not ("DesignSize" or "Magnification"))
            .ToList();
        var glyphs = Enum.GetValues<MusicGlyph>();
        var answers = glyphs.ToDictionary(
            g => g, g => designs.Select(d => EmmentalerMusicFont.MetricsOf(g, d)).ToArray());

        var orphans = new List<string>();
        foreach (var p in props)
        {
            object?[] want = designs.Select(d => p.GetValue(d)).ToArray();
            Func<MusicGlyphMetrics, object?> field =
                p.Name.EndsWith("Outline", StringComparison.Ordinal) ? m => m.OutlineBox
                : p.PropertyType == typeof(double) ? m => m.Advance
                : m => m.DesignBox;
            bool reached = answers.Values.Any(a =>
                Enumerable.Range(0, designs.Length).All(i => Equals(field(a[i]), want[i])));
            if (!reached)
                orphans.Add(p.Name);
        }
        Assert.Empty(orphans);
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
}
