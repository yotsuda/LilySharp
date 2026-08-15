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
using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A spacing wish's minimum is the SKYLINE distance, not the increment: the wish
/// replaces the base spring's increment minimum before merge_springs, so the merge's
/// +0.3 headroom stands on the skyline and a knee pull can take a pair's ideal under
/// the old increment floor.
/// LILYPOND-REF: lily/note-spacing.cc:78-83 Note_spacing::get_spacing.
/// </summary>
[Trait("Category", "Unit")]
public class KneeSpringFloorTests
{
    [Fact]
    public void DownUpKneePair_PullsUnderTheIncrementFloor()
    {
        // spacing-correction-accidentals.ly: "If right hand stems have accidentals,
        // optical spacing correction is still applied, but only if the stem
        // directions are different." Two kneed pairs (each an auto-beamed eighth
        // pair with opposite stems); the DOWN→UP one is pulled together by the full
        // knee term (−1.1742) to ideal 1.330 — under the old max(increment, sky)
        // minimum the merge headroom froze it at 1.2 + 0.3 = 1.500. The columns
        // share no Y, so the skyline minimum is 0 and LilyPond draws the ideal.
        // The accidental on the last note must NOT change any of it (the claim):
        // its ♯ tucks under the high left head at the same X either way.
        // Pinned against the LP twin (audit\lpreg\spacc-corr.{lys,-gen.ly}).
        // LILYPOND-REF: lily/spring.cc:101-129 merge_springs —
        //   avg_distance = max (min_distance + 0.3, avg_distance);
        // LILYPOND-REF: lily/note-spacing.cc:111-113 — set_ideal_distance
        //   (max (0.0, ideal)), the zero clamp the wish's ideal takes.
        string svg = Render("octave absolute\ntime 2/4\n\nc8 cis'' cis'' cis |\n");

        var heads = MusicGlyphs(svg, EmmentalerGlyphs.NoteheadBlack);
        Assert.Equal(4, heads.Count);
        Assert.Equal(8.49, heads[0].X, 0.02);    // LP 8.4900
        Assert.Equal(12.17, heads[1].X, 0.02);   // LP 12.1680  (up→down knee: widened)
        Assert.Equal(14.67, heads[2].X, 0.02);   // LP 14.6722
        Assert.Equal(16.00, heads[3].X, 0.02);   // LP 16.0022  (down→up knee: 1.330 gap)

        var sharps = MusicGlyphs(svg, EmmentalerGlyphs.AccidentalSharp);
        Assert.Equal(2, sharps.Count);
        Assert.Equal(10.71, sharps[0].X, 0.02);  // LP 10.7080
        Assert.Equal(14.55, sharps[1].X, 0.02);  // LP 14.5540 (tucks under the high head)
    }

    private static string Render(string source) =>
        LilySharp.Core.Svg.SvgGenerator.Generate(
            SyntaxTree.Parse(source),
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });

    /// <summary>All music glyphs of one codepoint: (X, Y) in document order, X-sorted.</summary>
    private static List<(double X, double Y)> MusicGlyphs(string svg, char glyph) =>
        System.Text.RegularExpressions.Regex.Matches(svg,
                "<text class=\"music\" x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"[^>]*>(.)</text>")
            .Where(m => m.Groups[3].Value[0] == glyph)
            .Select(m => (X: double.Parse(m.Groups[1].Value), Y: double.Parse(m.Groups[2].Value)))
            .OrderBy(g => g.X)
            .ToList();
}

