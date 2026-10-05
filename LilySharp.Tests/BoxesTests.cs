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

using System.Text.Json;
using LilySharp.Core.Rendering.Boxes;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>lysc boxes</c> (LilySharp-Omr's proposal of 2026-10-02, P5): every drawn symbol with its
/// kind, its ink box, its source offset and its staff.
/// </summary>
[Trait("Category", "Unit")]
public class BoxesTests
{
    private const string Book = """
        octave absolute
        time 3/4
        part m { clef treble
          section A { c'4( d'8 e'~ e' f') | g'4 a'16 b' c'' d'' e''4 | fis'2. | }
        }
        form main { A }
        score main { staff m }
        """;

    private static IReadOnlyList<BoxPage> Pages(string book, params string[] settings)
    {
        var tree = SyntaxTree.Parse(book);
        return BoxesGenerator.GeneratePages(tree, RenderSpecParser.FindFirst(tree),
            settings.Length == 0 ? null : Core.Semantics.PaperOverrides.Parse(settings, out _));
    }

    private static int Count(BoxPage page, string kind) => page.Symbols.Count(s => s.Kind == kind);

    [Fact]
    public void EverySymbolHasAKind_AndTheLinesSayWhatTheyAre()
    {
        var page = Assert.Single(Pages(Book));
        Assert.Equal(5, Count(page, "staffLine"));
        Assert.Equal(12, Count(page, "notehead"));
        Assert.Equal(12, Count(page, "stem"));
        Assert.Equal(3, Count(page, "beam"));
        Assert.Equal(3, Count(page, "barLine"));
        Assert.Equal(1, Count(page, "tie"));
        Assert.Equal(1, Count(page, "slur"));
        Assert.Equal(1, Count(page, "clef"));
        Assert.Equal(1, Count(page, "accidental"));
        Assert.Equal(2, Count(page, "timeSignature"));
        Assert.DoesNotContain(page.Symbols, s => s.Kind is "line" or "quad" or "curve");
    }

    /// <summary>A notehead's box is the glyph's ink: the layout's own notehead box, placed.</summary>
    [Fact]
    public void ANoteheadsBox_IsTheGlyphsInk()
    {
        var head = Pages(Book)[0].Symbols.First(s => s.Kind == "notehead");
        Assert.Equal("NoteheadBlack", head.Glyph);
        var ink = GlyphMetrics.GetNoteheadBBox(4);
        Assert.Equal(ink.Right - ink.Left, head.Box[2] - head.Box[0], 2);
        Assert.Equal(ink.Top - ink.Bottom, head.Box[3] - head.Box[1], 2);
    }

    /// <summary>The source offset is the written note's; a tie and a slur also give their ends.</summary>
    [Fact]
    public void SymbolsCiteTheirSource_AndBowsTheirEnds()
    {
        var page = Pages(Book)[0];
        var head = page.Symbols.First(s => s.Kind == "notehead");
        Assert.Equal("c'4", Book.Substring(head.Pos, 3));
        var tie = page.Symbols.Single(s => s.Kind == "tie");
        Assert.Equal('~', Book[tie.Pos]);
        Assert.NotNull(tie.Ends);
        Assert.Equal('(', Book[page.Symbols.Single(s => s.Kind == "slur").Pos]);
        Assert.All(page.Symbols.Where(s => s.Kind != "mark" && s.Kind != "rect"), s => Assert.Equal(0, s.Staff));
    }

    /// <summary>Every box lies on its page, and each bar carries the number the page prints.</summary>
    [Fact]
    public void BoxesLieOnThePage_AndBarsAreNumbered()
    {
        var page = Pages(Book)[0];
        Assert.All(page.Symbols, s =>
        {
            Assert.True(s.Box[0] <= s.Box[2] && s.Box[1] <= s.Box[3]);
            Assert.InRange(s.Box[0], 0, page.Width);
            Assert.InRange(s.Box[3], 0, page.Height);
        });
        Assert.Equal([1, 2, 3], page.Bars.Select(b => b.Bar));
    }

    /// <summary>Two staves: each symbol names the staff it was drawn on.</summary>
    [Fact]
    public void EachSymbolNamesItsStaff()
    {
        const string two = """
            octave absolute
            part up { clef treble }
            part down { clef bass }
            section A { up { c''4 d'' e'' f'' | } down { c4 d e f | } }
            form main { A }
            score main { staff up  staff down }
            """;
        var page = Pages(two)[0];
        Assert.Equal(4, page.Symbols.Count(s => s.Kind == "notehead" && s.Staff == 0));
        Assert.Equal(4, page.Symbols.Count(s => s.Kind == "notehead" && s.Staff == 1));
        Assert.Equal(5, page.Symbols.Count(s => s.Kind == "staffLine" && s.Staff == 1));
    }

    /// <summary>The settings reach the boxes: the same pages the PNG of those settings draws.</summary>
    [Fact]
    public void TheSettingsReachTheBoxes()
    {
        string sixty = "octave absolute\npart m { clef treble\n  section A { "
            + string.Concat(Enumerable.Repeat("c'8 d' e' f' g'4 a' | ", 60))
            + "}\n}\nform main { A }\nscore main { staff m }\n";
        Assert.Single(Pages(sixty));
        Assert.Equal(4, Pages(sixty, "systemsPerPage=3").Count);
    }

    [Fact]
    public void TheJsonCarriesItsVersion()
    {
        using var json = JsonDocument.Parse(BoxesGenerator.ToJson(Pages(Book)));
        Assert.Equal(BoxesGenerator.FormatVersion, json.RootElement.GetProperty("version").GetInt32());
        var symbol = json.RootElement.GetProperty("pages")[0].GetProperty("symbols")[0];
        Assert.True(symbol.TryGetProperty("kind", out _));
        Assert.Equal(4, symbol.GetProperty("box").GetArrayLength());
    }
}
