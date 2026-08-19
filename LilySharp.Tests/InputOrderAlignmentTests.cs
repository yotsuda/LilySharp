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
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// input-order-alignment.ly: items attached to a chord with a suspended second
/// align on the MAIN notehead (the one on the stem's own side, kept at the
/// column origin), regardless of the members' input order.
/// LILYPOND-REF: lily/note-column.cc:179-204 calc_main_extent;
/// lily/self-alignment-interface.cc:143-145 X-align-on-main-noteheads.
/// </summary>
[Trait("Category", "Unit")]
public class InputOrderAlignmentTests
{
    [Fact]
    public void SuspendedChordLyric_CentresOnMainHead_InputOrderInvariant()
    {
        // Two staves, identical music except chord member order; one syllable each.
        var svg = LiveRender.SvgFromRenderSpec("""
            octave absolute
            time 4/4
            part one { }
            part two { }
            lyrics wa { section Main { blah | } }
            lyrics wb { section Main { blah | } }
            section Main {
              one { <b c'>2 s2 | }
              two { <c' b>2 s2 | }
            }
            form main { ~Main }
            score main { staff ~one  lyrics wa staff ~two  lyrics wb }
            """);

        // Music-text glyphs WITH data-pos: per staff, the (authored) time
        // signature and the two chord heads. The time signature sits left of the
        // note column, so the four RIGHTMOST glyphs are the heads.
        var glyphs = new List<(double X, double Y)>();
        foreach (Match m in Regex.Matches(svg,
            "<text class=\"music\" x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"[^>]*data-pos[^>]*>"))
            glyphs.Add((
                double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)));
        Assert.Equal(6, glyphs.Count);
        var heads = glyphs.OrderByDescending(g => g.X).Take(4).OrderBy(g => g.Y).ToList();
        // Per staff (two heads each): the chord is stem-down (b/c' straddle the
        // middle line), so the MAIN head is the upper one (smaller device Y) and
        // the suspended b is reversed LEFT of it.
        var (upper1, lower1) = (heads[0], heads[1]);
        var (upper2, lower2) = (heads[2], heads[3]);
        Assert.True(lower1.X < upper1.X, "suspended head must sit left of the main head");
        // Input order must not matter: both staves print identical head X pairs.
        Assert.Equal(upper1.X, upper2.X, 3);
        Assert.Equal(lower1.X, lower2.X, 3);

        var blahs = new List<double>();
        foreach (Match m in Regex.Matches(svg, "<text x=\"([-\\d.]+)\"[^>]*>blah</text>"))
            blahs.Add(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
        Assert.Equal(2, blahs.Count);
        // The syllable (anchor=middle) centres on the MAIN head's ink centre —
        // not on the two-head union's centre, which sits half a head further left.
        double mainHeadCentre = upper1.X + GlyphMetrics.GetNoteheadBBox(2).CenterX;
        Assert.Equal(mainHeadCentre, blahs[0], 2);
        Assert.Equal(mainHeadCentre, blahs[1], 2);
    }
}
