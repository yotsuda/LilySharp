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

using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A full-notation tab's DRAWN stems and beams are in its skyline, so an outside-staff mark
/// — the metronome mark here — clears an up-beam exactly as it clears the notation staff's.
/// LilyPond: <c>\tabFullNotation</c> reverts the Stem and Beam stencils
/// (ly/property-init.ly:822-839) and the axis group's skyline is its members' stencils
/// (lily/axis-group-interface.cc:914-940); measured 2.26.0, scratch/p321/fx/fx11-hand-tab.ly.
/// Until 2026-09-02 the tab skyline held only its fret digits (owner report, session 321:
/// the tempo of the Billie Jean bassTab book printed through the first bar's beam).
/// </summary>
[Trait("Category", "Unit")]
public sealed class TabTempoOverBeamTests
{
    private const string Book = """
        octave absolute
        tempo 117
        key d major
        time 4/4
        part bl {
          clef bass
          tuning bass
          section A { fis,,8\4 cis,\3 e,\3 fis,\3 e,\3 cis,\3 b,,\4 cis,\3 | fis,,8\4 cis,\3 e,\3 fis,\3 e,\3 cis,\3 b,,\4 cis,\3 | }
        }
        form main { A }
        score main { STAVES }
        """;

    private static (MultiStaffScore Score, ScoreLayout Layout) Lay(string staves)
    {
        var tree = SyntaxTree.Parse(Book.Replace("STAVES", staves));
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var spec = RenderSpecParser.FindFirst(tree);
        var score = SvgGenerator.CollectScore(tree, spec);
        return (score, new LayoutEngine().Layout(score));
    }

    private static MusicMarkLayout Tempo(ScoreLayout layout)
        => Assert.Single(layout.MusicMarkLayouts.Where(m => m.MarkType == MusicMarkType.Tempo));

    [Fact]
    public void FullTab_LiftsTheTempoOverTheFirstBarsUpBeam()
    {
        var (score, layout) = Lay("tab bl as full");
        var tempo = Tempo(layout);
        // The beam's own top edge, in the tab's frame with the middle at device 0 —
        // the same line the renderer draws and the skyline now seeds.
        var staff = score.StaffGroups[0].Staves[0];
        var beam = layout.BeamLayouts.First(b => b.Group.MeasureIndex == 0);
        int strings = Tunings.GetStringCount(staff.Tuning!.Value);
        double tabHeight = (strings - 1) * EngravingDefaults.TabStringSpace(strings);
        var geom = new TabStaffGeometry(staff.Tuning.Value, -tabHeight / 2.0,
            staff.TabSourceClef, staff.Transposition);
        Assert.True(geom.GroupStemUp(beam.Group.Members.Select(m => m.Item)),
            "the fixture's first bar must beam UP (low strings) for the test to mean anything");
        double beamTopYUp = -ArticulationEngraver.TabBeamOuterEdgeY(beam, geom, beam.LeftX);
        // The mark's baseline stands ABOVE the beam's top edge (Y-up about the middle).
        Assert.True(tempo.YUp > beamTopYUp,
            $"tempo baseline {tempo.YUp:F3} must clear the beam top {beamTopYUp:F3}");
    }

    [Fact]
    public void FullTab_SeedsTheFlagBesideAnUnbeamedEighthsStem()
    {
        // A lone eighth on the low string: up stem, and a flag hanging from its tip running
        // RIGHT of the stem (lily/flag.cc:51-69 Flag::width). The tab's inside skyline must be
        // as tall just right of the stem — under the flag — as at the stem itself, and lower
        // again past the flag's width.
        var tree = SyntaxTree.Parse(Book.Replace("STAVES", "tab bl as full")
            .Replace("section A { fis,,8\\4 cis,\\3 e,\\3 fis,\\3 e,\\3 cis,\\3 b,,\\4 cis,\\3 | fis,,8\\4 cis,\\3 e,\\3 fis,\\3 e,\\3 cis,\\3 b,,\\4 cis,\\3 | }",
                     "section A { fis,,8\\4 r8 r2 r4 | }"));
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var spec = RenderSpecParser.FindFirst(tree);
        var score = SvgGenerator.CollectScore(tree, spec);
        var layout = new LayoutEngine().Layout(score);
        var staff = score.StaffGroups[0].Staves[0];
        var measures = layout.Systems[0].Measures;
        var (up, _) = new SkylineBuilder(LayoutOptions.Default.StaffHeight, score.TextMetrics)
            .BuildInsideStaffSkylines(staff, measures);
        double stemX = measures[0].X
            + LayoutUtilities.GetItemXOffset(staff.PrimaryVoice.Measures, 0, 0, measures[0])
            + EngravingDefaults.TabHeadCenterOffset;
        double atStem = up.Height(stemX);
        double underFlag = up.Height(stemX + EngravingDefaults.FlagWidth * 0.7);
        double pastFlag = up.Height(stemX + EngravingDefaults.FlagWidth + 1.0);
        Assert.Equal(atStem, underFlag, 6);
        Assert.True(pastFlag < atStem, $"past the flag {pastFlag:F3} must drop below the stem tip {atStem:F3}");
    }

    [Fact]
    public void NumbersTab_KeepsTheQuietBaseline_ThereIsNoBeamToClear()
    {
        // The positive control: a numbers-only tab draws no stems or beams, so nothing lifts
        // the mark and it stays at its quiet baseline — LOWER than the full tab's.
        var (_, full) = Lay("tab bl as full");
        var (_, numbers) = Lay("tab bl as numbers");
        Assert.True(Tempo(numbers).YUp < Tempo(full).YUp,
            $"numbers {Tempo(numbers).YUp:F3} should sit below full {Tempo(full).YUp:F3}");
    }
}
