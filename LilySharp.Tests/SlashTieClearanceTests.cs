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

using System.Globalization;
using System.Text.RegularExpressions;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A tie between SLASH heads clears the slashes' slanted ink. The tied head's
/// one-staff-space box let the tie start inside the first slash (the owner's report,
/// session 905: `s2. /4~ | 4 r2.` on a one-line staff — "the tie sticks to the /"); the
/// slash's ink now enters the tie outline (ElementCoordinator.AddSlashInk). LILYSHARP-OWN:
/// LilyPond 2.26 draws the same overlap.
/// </summary>
[Trait("Category", "Unit")]
public class SlashTieClearanceTests
{
    [Theory]
    [InlineData("staff m as lines 1")]
    [InlineData("staff m")]
    public void ATieBetweenSlashes_StartsAndEndsClearOfTheirInk(string staff)
    {
        var svg = LiveRender.SvgFromRenderSpec($$"""
            time 4/4
            part m { clef treble }
            section A { m { s2. /4~ | 4 r2. | } }
            form { ~A }
            score { {{staff}} }
            """);

        // The tie: the one filled bow path.
        var tie = Regex.Match(svg, "<path d=\"M ([-\\d.]+),([-\\d.]+) C [^\"]* ([-\\d.]+),([-\\d.]+) C");
        Assert.True(tie.Success, "no tie drawn");
        double x0 = D(tie.Groups[1].Value), y = D(tie.Groups[2].Value), x1 = D(tie.Groups[3].Value);

        // The two slash heads: the music glyphs on the tie's staff, after the time signature.
        var heads = Regex.Matches(svg, "<text class=\"music\" x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"[^>]*data-pos")
            .Select(m => (X: D(m.Groups[1].Value), Y: D(m.Groups[2].Value)))
            .Where(h => h.X < x1 + 2).OrderBy(h => h.X).ToList();
        var first = heads.Last(h => h.X < x0);
        var second = heads.First(h => h.X > x0);

        // The slash's ink at the tie's height (Y up from the head's centre line): the
        // parallelogram's right edge for the first head, its left edge for the second.
        var box = GlyphMetrics.GetNoteheadBBox(GlyphMetrics.StaffHeadFont, NoteheadStyle.Slash, 4);
        double width = box.Right - box.Left, stroke = width * 100.0 / 420.0, run = width - stroke;
        double f = System.Math.Clamp(((first.Y - y) - box.Bottom) / (box.Top - box.Bottom), 0, 1);
        double firstRight = first.X + box.Left + f * run + stroke;
        double secondLeft = second.X + box.Left + f * run;

        Assert.True(x0 > firstRight, $"the tie starts at {x0}, inside the first slash (ink to {firstRight:F2})");
        Assert.True(x1 < secondLeft, $"the tie ends at {x1}, inside the second slash (ink from {secondLeft:F2})");

        static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);
    }
}
