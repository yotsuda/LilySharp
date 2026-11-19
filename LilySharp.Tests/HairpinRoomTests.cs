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

using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The room a system keeps for a staff's hairpins (HairpinEngraver.CalculateOnSystem, which
/// MultiStaffLayouter enters into the staff's silhouette before the systems are placed) is the
/// room of the wedges the page draws: each from the column of its own voice's item to the
/// column of the item its end is counted in, in whichever voice that is.
/// </summary>
/// <remarks>
/// The reservation reads the hairpin's voice since session 1238, and had no net of its own:
/// reading the staff's first voice under the same item numbers moved no test (Lab
/// sessions/p1321/poisons.ps1 R1 and R2 are that reading). The two entrances are compared here
/// on one staff, where the drawn wedge's columns are the system's own.
/// </remarks>
[Trait("Category", "Unit")]
public class HairpinRoomTests
{
    [Theory]
    // The second voice's hairpin and its own stop, under a first voice of another rhythm.
    [InlineData("voice { c''2 c''8 c'' c'' c'' } { c'4 c'4@cresc c'4 c'4@!cresc } |")]
    [InlineData("voice { c''8 c'' c'' c'' c''2 } { c'2 c'4@cresc c'4@!cresc } |")]
    // …ending in the next bar.
    [InlineData("voice { c''8 c'' c'' c'' c''2 } { c'2 c'2@cresc } | voice { c''2 c''2 } { c'4 c'4@!cresc c'2 } |")]
    // The first voice's hairpin, ended by the second voice's level: the end is the second's item.
    [InlineData("voice { c''4@cresc c''4 c''2 } { c'8 c' c' c' c'2@f } |")]
    // The second voice's, ended by the first's.
    [InlineData("voice { c''8 c'' c'' c'' c''4 c''4@f } { c'2@cresc c'2 } |")]
    public void TheRoomsWedge_StandsWhereTheDrawnOneDoes(string bars)
    {
        var tree = SyntaxTree.Parse("octave absolute\ntime 4/4\npaper { raggedRight }\npart m { clef treble }\nsection A { m {\n"
            + bars + "\n} }\nform { A }\nscore { staff m }\n");
        Assert.DoesNotContain(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine(score.Paper).Layout(score);
        var drawn = Assert.Single(layout.HairpinLayouts);

        var system = Assert.Single(layout.Systems);
        var staff = score.StaffGroups[0].Staves[0];
        var keyed = system.Measures.OrderBy(m => m.MeasureIndex).ToImmutableArray();
        Assert.Equal(Enumerable.Range(0, keyed.Length), keyed.Select(m => m.MeasureIndex));
        var room = Assert.Single(HairpinEngraver.CalculateOnSystem(
            ScoreSideTables.HairpinsByStaff(score).At(0), system, staff.PrimaryVoice.Measures.Length, keyed,
            staff.Voices, staff.PrimaryVoice.Measures, layout.BeamLayouts, layout.DynamicLayouts));

        Assert.Equal(drawn.StartX, room.StartX, 3);
        Assert.Equal(drawn.EndX, room.EndX, 3);
        Assert.True(room.EndX - room.StartX > 2.0, $"a wedge of {room.EndX - room.StartX:F2}");
    }
}
