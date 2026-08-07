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
/// laissez-vibrer-chords.ly: l.v. ties work on individual chord members and obey
/// ^/_ direction forcing; the half-tie's X span is LilyPond's own numbers.
/// LILYPOND-REF: lily/laissez-vibrer-engraver.cc acknowledge_note_head — one tie
/// per marked head; lily/tie-formatting-problem.cc:436-441 from_semi_ties.
/// </summary>
[Trait("Category", "Unit")]
public class LaissezVibrerChordTests
{
    [Fact]
    public void ChordMembers_GetTies_WithForcedDirections_AtHeadEdgeSpan()
    {
        // The regression book's two chords: one member-level l.v., then both
        // members with forced opposite directions. Whole notes make the old
        // slot-right anchor bug visible (the slot spans the bar).
        var svg = LiveRender.SvgFromRenderSpec("""
            octave absolute
            time 4/4
            part v { }
            section Main {
              v {
                <d@laissezVibrer g>1 |
                <d@laissezVibrer.up g@laissezVibrer.down>1 |
              }
            }
            form main { ~Main }
            score main { staff ~v }
            """);

        // Tie curves: "M sx,sy C c1x,c1y ..." — curvature sign from c1y vs sy.
        var ties = new List<(double StartX, double StartY, bool CurvesUp)>();
        foreach (Match m in Regex.Matches(svg,
            "<path d=\"M ([-\\d.]+),([-\\d.]+) C [-\\d.]+,([-\\d.]+)"))
            ties.Add((
                double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture)
                    < double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)));
        // 1 tie on the first chord (this used to be a SILENT DROP: chords never
        // produced l.v. ties at all), 2 on the second.
        Assert.Equal(3, ties.Count);

        // Second chord: both members tied, directions forced opposite — the
        // ^-forced d (the LOWER note, larger device Y) curves up toward the g,
        // the _-forced g (higher, smaller Y) curves down: the two bows face
        // each other between the heads, as LP prints them.
        var m2 = ties.Skip(1).OrderBy(t => t.StartY).ToList();
        Assert.Equal(2, m2.Count);
        Assert.Equal(m2[0].StartX, m2[1].StartX, 2);
        Assert.False(m2[0].CurvesUp, "the _-forced upper member must curve down");
        Assert.True(m2[1].CurvesUp, "the ^-forced lower member must curve up");

        // X span: head ink right + x-gap (0.2), per LP's from_semi_ties numbers —
        // NOT the item slot's right edge. Heads = music glyphs with data-pos.
        var headXs = Regex.Matches(svg,
                "<text class=\"music\" x=\"([-\\d.]+)\" y=\"[-\\d.]+\"[^>]*data-pos[^>]*>")
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .Distinct().OrderBy(x => x).ToList();
        double chord1HeadX = headXs[^2]; // last two distinct X = chord 1, chord 2 columns
        double expectedStart = chord1HeadX + GlyphMetrics.GetNoteheadBBox(1).Right
            + TieDetails.Default.XGap;
        Assert.Equal(expectedStart, ties[0].StartX, 2);
    }
}
