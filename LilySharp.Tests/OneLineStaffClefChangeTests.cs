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

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A one-line staff (<c>as lines 1</c>, LilyPond's RhythmicStaff, which removes the
/// Clef_engraver) engraves no clef — not at the line start (SpacingRules.EngravesClef) and,
/// since session 913, not for a <c>clef</c> written in its music either: the change is blanked
/// (ClefChangeItem.Blanked, set by MeterStencil.Blank), drawn nowhere and given no column, while
/// it still moves the clef the notes after it are placed by. A five-line staff beside it keeps
/// its own change. Until then the one-line staff drew the small change clef (and its 8) and
/// paid its column (HANDOFF §1.0 ⒜, session 910's one-to-five-line sweep).
/// </summary>
[Trait("Category", "Unit")]
public sealed class OneLineStaffClefChangeTests
{
    private static string Svg(string book)
        => SvgGenerator.Generate(TestPaper.ParseAtIndentZero(book), new SvgRenderOptions { EmbedFont = false });

    private static string Book(string music, string staves)
        => "octave absolute\npart m { clef treble }\npart n { clef treble }\nsection A { m { " + music + " } n { " + music + " } }\n"
           + "form { ~A }\nscore { " + staves + " }\n";

    /// <summary>Every black note head on the page, (x, y) in order.</summary>
    private static List<(double X, double Y)> Heads(string svg)
        => Regex.Matches(svg, @"<text class=""music"" x=""([\d.]+)"" y=""([\d.]+)""[^>]*>" + EmmentalerGlyphs.NoteheadBlack + "</text>")
            .Select(m => (double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                          double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)))
            .ToList();

    private static int Count(string svg, char glyph) => svg.Count(c => c == glyph);

    [Fact]
    public void OnAOneLineStaff_AClefChange_IsDrawnNowhere_AndTakesNoRoom_ButStillPlacesTheNotes()
    {
        // The control has the same stem directions (the stem pattern spaces the notes too):
        // b' a' down-up, then c'' b' down-down as f e are in the bass clef.
        string with = Svg(Book("b'4 a' clef bass f e |", "staff m as lines 1"));
        string without = Svg(Book("b'4 a' c'' b' |", "staff m as lines 1"));
        Assert.Equal(0, Count(with, EmmentalerGlyphs.FClefChange));
        Assert.Equal(0, Count(with, EmmentalerGlyphs.GClef));
        var heads = Heads(with);
        var plain = Heads(without);
        Assert.Equal(4, heads.Count);
        // The column is ABSENT, not zero-wide: every head stands where it does with no change.
        Assert.Equal(plain.Select(h => h.X), heads.Select(h => h.X));
        // …and the clef still moved: measured from the first head (the page's own y follows
        // the skyline), f in the bass clef does not stand where c'' does in the treble.
        Assert.NotEqual(plain[2].Y - plain[0].Y, heads[2].Y - heads[0].Y, 2);
    }

    [Fact]
    public void BesideAFiveLineStaff_TheFiveLineStaffKeepsItsChange_AndTheOneLineStaffDrawsNone()
    {
        string svg = Svg(Book("b'4 a' clef bass f e |", "staff m as lines 1\n  staff n"));
        Assert.Equal(1, Count(svg, EmmentalerGlyphs.FClefChange));   // n's change only
        Assert.Equal(1, Count(svg, EmmentalerGlyphs.GClef));         // n's line-start clef only
        // One column for both staves: the heads of the two staves share their x.
        var heads = Heads(svg);
        Assert.Equal(8, heads.Count);
        Assert.Equal(heads.Take(4).Select(h => h.X), heads.Skip(4).Select(h => h.X));
    }
}
