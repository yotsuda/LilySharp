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
/// repeat-tie-chords.ly: \repeatTie works on individual chord members — member-level
/// marks fan one tie per marked head, ^/_ forces the curve side, and a chord-level
/// mark half-ties every member. The mirror of <see cref="LaissezVibrerChordTests"/>:
/// Repeat_tie_engraver IS a Laissez_vibrer_engraver with the event class and grob
/// names swapped.
/// LILYPOND-REF: lily/repeat-tie-engraver.cc:27-33 Repeat_tie_engraver;
/// lily/laissez-vibrer-engraver.cc:66-108 acknowledge_note_head — one tie per head.
/// </summary>
[Trait("Category", "Unit")]
public class RepeatTieChordTests
{
    [Fact]
    public void ChordMembers_GetTies_ForcedAndStandardDirections_AtHeadEdgeSpan()
    {
        // The regression book's three chords: one member-level repeat tie, both
        // members with forced opposite directions, then a chord-level mark with no
        // directions. Whole notes make a slot-anchored X visible (the slot spans
        // the bar). This used to be a SILENT DROP: chords never produced repeat
        // ties at all.
        var svg = LiveRender.SvgFromRenderSpec("""
            octave absolute
            time 4/4
            part v { }
            section Main {
              v {
                <d@repeatTie g>1 |
                <d@repeatTie.up g@repeatTie.down>1 |
                <d g>@repeatTie |
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
        // 1 tie on the first chord, 2 on the second, 2 on the third.
        Assert.Equal(5, ties.Count);

        // First chord: the marked d only (the lower member) — a single unforced
        // tie takes sign(position), below the middle line = DOWN (LP prints it at
        // head Y + 0.5, bulging down — rtchords twin).
        Assert.False(ties[0].CurvesUp, "the lone unforced member tie must curve down");

        // Second chord: directions forced opposite — the ^-forced d (the LOWER
        // note, larger device Y) curves up toward the g, the _-forced g curves
        // down: the two bows face each other between the heads, as LP prints them.
        var m2 = ties.Skip(1).Take(2).OrderBy(t => t.StartY).ToList();
        Assert.Equal(m2[0].StartX, m2[1].StartX, 2);
        Assert.False(m2[0].CurvesUp, "the _-forced upper member must curve down");
        Assert.True(m2[1].CurvesUp, "the ^-forced lower member must curve up");

        // Third chord, chord-level mark, nothing forced: the standard-directions
        // rule — bottom tie DOWN, top tie UP (LP prints d at +3.0 down / g at
        // −0.6588 up of the middle line — rtchords twin).
        // LILYPOND-REF: lily/tie-formatting-problem.cc:1026-1066
        //   set_ties_config_standard_directions.
        var m3 = ties.Skip(3).Take(2).OrderBy(t => t.StartY).ToList();
        Assert.Equal(m3[0].StartX, m3[1].StartX, 2);
        Assert.True(m3[0].CurvesUp, "the top tie of an unforced column must curve up");
        Assert.False(m3[1].CurvesUp, "the bottom tie of an unforced column must curve down");

        // X span: the repeat tie hangs LEFT of the head — its open end starts at
        // head ink left − (OpenReach − x-gap) = −1.3, per LP's from_semi_ties
        // numbers (the LP twin's paths span exactly −1.3..−0.2 of the column).
        // Heads = music glyphs with data-pos; 3 chords → 3 distinct column Xs.
        var headXs = Regex.Matches(svg,
                "<text class=\"music\" x=\"([-\\d.]+)\" y=\"[-\\d.]+\"[^>]*data-pos[^>]*>")
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .Distinct().OrderBy(x => x).ToList();
        double chord1HeadX = headXs[^3];
        double expectedStart = chord1HeadX
            - TieVariantEngraver.OpenReach + TieDetails.Default.XGap;
        Assert.Equal(expectedStart, ties[0].StartX, 2);
    }
}
