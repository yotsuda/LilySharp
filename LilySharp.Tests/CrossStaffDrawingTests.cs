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
using LilySharp.Core.LilyPond;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>@cross</c> draws the note on the other staff (OMR REQUESTS #20): the note's position is the
/// other staff's, a beam joins the two staves, and the twin writes <c>\change Staff</c>.
/// </summary>
/// <remarks>The expected shapes are LilyPond 2.26.0's, read off the twins in Lab
/// sessions/p1093 (r4 = the OMR's repro, p3 = the probe with the chord, tie and script).</remarks>
[Trait("Category", "Unit")]
public class CrossStaffDrawingTests
{
    private static string Book(string rh, string lh) => $$"""
        octave absolute
        time 4/4
        part rh { clef treble }
        part lh { clef bass }
        section A { rh { {{rh}} } lh { {{lh}} } }
        form { ~A }
        score { grandStaff { staff rh staff lh } }
        """;

    private static MultiStaffScore Score(string book)
    {
        var tree = SyntaxTree.Parse(book);
        return new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
    }

    private static NoteItem[] Notes(MultiStaffScore score, int staff) =>
        score.EnumerateStaves().ToList()[staff].Staff.PrimaryVoice.Measures
            .SelectMany(m => m.Items).OfType<NoteItem>().ToArray();

    [Fact]
    public void TheMovedNoteIsReadUnderTheOtherStaffsClef()
    {
        // g on the bass clef's top space is position +3; on the treble clef it hangs below the
        // staff, at -9: the two clefs' middle lines are twelve positions apart.
        var score = Score(Book("r1 |", "g4 g4@cross r2 |"));
        var notes = Notes(score, 1);
        Assert.Equal(-1, notes[0].CrossStaffIndex);
        Assert.Equal(0, notes[1].CrossStaffIndex);
        Assert.Equal(notes[0].StaffPosition - 12, notes[1].StaffPosition);
    }

    [Fact]
    public void ANoteOfTheUpperStaffMovesDown_AndAChordMovesWhole()
    {
        var score = Score(Book("<c' e'>2@cross c''2 |", "c1 |"));
        var chord = score.EnumerateStaves().First().Staff.PrimaryVoice.Measures[0].Items.OfType<ChordItem>().Single();
        Assert.Equal(1, chord.CrossStaffIndex);
        // c' under the bass clef stands a third above the top line's neighbour: +6, and e' +8.
        Assert.Equal(new[] { 6, 8 }, chord.Notes.Select(n => n.StaffPosition).OrderBy(p => p));
    }

    [Fact]
    public void TheClefInForceAtTheNoteDecides()
    {
        var score = Score(Book("clef bass c4 c4 clef treble c4 c4 |", "g4@cross g4@cross g4@cross g4@cross |"));
        var moved = Notes(score, 1);
        Assert.Equal(moved[0].StaffPosition, moved[1].StaffPosition);
        // Treble from the third beat: the same g, twelve positions lower.
        Assert.Equal(moved[1].StaffPosition - 12, moved[2].StaffPosition);
    }

    [Fact]
    public void ABeamJoinsTheTwoStaves_WithEveryStemsHeadOnItsOwnStaff()
    {
        var score = Score(Book("r1 |", "c8 e g@cross c' r2 |"));
        var layout = new LayoutEngine().Layout(score);
        var beam = layout.BeamLayouts.Single(b => b.StaffIndex == 1);
        Assert.True(beam.Group.IsCrossStaff);
        Assert.Equal(new[] { 1, 1, 0, 1 }, beam.MemberStaffIndices);
        Assert.Equal(0, beam.Group.Members[0].StaffOffset);
        // The member on the staff above stands that far above the beam's own staff.
        Assert.True(beam.Group.Members[2].StaffOffset > 8);
    }

    [Fact]
    public void AWideGapKnees_TheStemsPointingIntoIt()
    {
        // LilyPond (p3 bar 2): a chord moved onto the bass staff and notes high on the treble
        // knee, the low stems up and the high ones down.
        var score = Score(Book("c'1 | <c' e'>8@cross g' <e' g'>@cross c'' r2 | r1 |", "c1 | r1 | c4@cross c4@cross r2 |"));
        // The low notes of bar 3 widen the staves' pure boxes: the knee reads those (k2).
        var beam = new LayoutEngine().Layout(score).BeamLayouts.Single();
        Assert.True(beam.Group.IsKnee);
        Assert.Equal(new[] { true, false, true, false },
            beam.Group.Members.Select(m => m.MemberStemUp));
    }

    [Fact]
    public void ANarrowGapDoesNotKnee()
    {
        // LilyPond (r4 bar 1): the moved note is just under the treble staff, all stems up.
        var score = Score(Book("r1 |", "c8 e g@cross c8 e g@cross r4 |"));
        var layout = new LayoutEngine().Layout(score);
        foreach (var beam in layout.BeamLayouts)
            Assert.False(beam.Group.IsKnee);
    }

    [Fact]
    public void TheCollectedStampIsTheMovedBeamsDirection()
    {
        // Before any layout: the spacing reads this stamp. g reads +3 on the bass staff, which
        // would turn the beam DOWN; where it stands, -9, the beam goes UP.
        var notes = Notes(Score(Book("r1 |", "c8 e g@cross r8 r2 |")), 1);
        Assert.All(notes, n => Assert.True(n.StemUp));
    }

    [Fact]
    public void AGlissandoReachesTheMovedNote()
    {
        // The line's far end is on the staff below: past the whole distance between the staves,
        // not at the position read about its own (audit/lpreg/gliss-xstaff.lys).
        var layout = new LayoutEngine().Layout(Score(Book("e''2@glissando a,,2@cross | r1 |", "s1 | s1 |")));
        var line = layout.GlissandoLayouts.Single();
        Assert.True(line.EndYUp < -10);
    }

    [Fact]
    public void ATupletsNumberFollowsTheMovedBeamsDirection()
    {
        // The bake read g as +3 on the bass staff and said DOWN, which put the number under the
        // staff; on the staff it stands on the beam goes UP, as LilyPond's does (r4).
        var score = Score(Book("r1 |", "tuplet 3/2 { c8 e g@cross } r2. |"));
        var layout = new LayoutEngine().Layout(score);
        Assert.True(layout.TupletBracketLayouts.Single().IsStemUp);
    }

    [Fact]
    public void ATieBetweenMovedNotesStandsOnTheirStaff()
    {
        var score = Score(Book("r1 |", "c4@cross~ c4@cross r2 |"));
        var layout = new LayoutEngine().Layout(score);
        var tie = layout.TieLayouts.Single();
        var plain = new LayoutEngine().Layout(Score(Book("r1 |", "c4~ c4 r2 |"))).TieLayouts.Single();
        // The treble clef reads c six spaces lower, and the staff stands further than that
        // above: unmoved, the bow would be under the plain one.
        Assert.True(tie.StartYUp > plain.StartYUp);
    }

    [Fact]
    public void AScriptOnAMovedNoteIsAddressedToItsStaff()
    {
        var score = Score(Book("r1 |", "c4@cross@staccato r2. |"));
        var layout = new LayoutEngine().Layout(score);
        Assert.Equal(0, layout.ArticulationLayouts.Single().StaffIndex);
    }

    [Fact]
    public void WithNoOtherStaffNothingMoves()
    {
        var tree = SyntaxTree.Parse("""
            octave absolute
            part m { clef treble }
            section A { m { c'4@cross d' e' f' | } }
            form { ~A }
            score { staff m }
            """);
        var score = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        Assert.All(Notes(score, 0), n => Assert.Equal(-1, n.CrossStaffIndex));
    }

    [Fact]
    public void TheTwinWritesChangeStaff_AndNamesBothStaves()
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(Book("r1 |", "tuplet 3/2 { c8 e g@cross } r2. |")));
        Assert.Contains("\\new Staff = \"rh\"", ly);
        Assert.Contains("\\new Staff = \"lh\"", ly);
        Assert.Contains("\\change Staff = \"rh\" g \\change Staff = \"lh\" }", ly);
    }
}
