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

    /// <summary>Accidentals read by the staff a head is DRAWN on (Lab sessions/p1093/a1,
    /// LilyPond 2.26.0): each of these reads as the wrong pitch without its mark.</summary>
    [Fact]
    public void AMovedNoteOwesTheNaturalTheNewStaffsSharpCalls()
    {
        var score = Score(Book("fis'4 r2. |", "r2 f'4@cross r4 |"));
        Assert.Equal("natural", Notes(score, 1).Single().Accidental);
    }

    [Fact]
    public void AMovedNotePrintsTheSharpItsOldStaffRemembers()
    {
        var notes = Notes(Score(Book("r1 |", "fis4 fis4@cross fis4 r4 |")), 1);
        Assert.Equal("sharp", notes[0].Accidental);
        Assert.Equal("sharp", notes[1].Accidental);
        Assert.Null(notes[2].Accidental);
    }

    [Fact]
    public void TheStaffItLeftDoesNotRememberTheMovedNote()
    {
        var notes = Notes(Score(Book("r1 |", "fis'4@cross fis'4@cross fis'4 fis'4 |")), 1);
        Assert.Equal("sharp", notes[0].Accidental);
        Assert.Null(notes[1].Accidental);
        Assert.Equal("sharp", notes[2].Accidental);
        Assert.Null(notes[3].Accidental);
    }

    [Fact]
    public void TheKeyAnswersWhenTheStaffHasShownNothing()
    {
        // In G major f# prints nothing on either staff, moved or not.
        string book = Book("r1 |", "fis'4@cross fis'4 r2 |").Replace("time 4/4", "time 4/4\nkey g major");
        Assert.All(Notes(Score(book), 1), n => Assert.Null(n.Accidental));
    }

    [Fact]
    public void AReminderAMovedNoteWroteStays()
    {
        // LilyPond drops this one; Lily# keeps a voice's own (StaffAccidentals ⑴, owner 2026-10-02).
        var notes = Notes(Score(Book("fis'4 r2. |", "r4 fis'4@cross r2 |")), 1);
        Assert.Equal("sharp", notes.Single().Accidental);
    }

    [Fact]
    public void AKeyChangeMovesWhatTheKeyAnswers()
    {
        // After G major enters, f# owes nothing on the staff it moved to; before it, a sharp.
        var notes = Notes(Score(Book("r1 | key g major r1 |", "fis'4@cross r2. | key g major fis'4@cross r2. |")), 1);
        Assert.Equal("sharp", notes[0].Accidental);
        Assert.Null(notes[1].Accidental);
    }

    [Fact]
    public void ANoteATieCarriesInPrintsNothing()
    {
        // The new bar's memory is empty and the key says natural; the tie says nothing is owed.
        var notes = Notes(Score(Book("r1 | r1 |", "fis'1@cross~ | fis'1@cross |")), 1);
        Assert.Equal("sharp", notes[0].Accidental);
        Assert.Null(notes[1].Accidental);
    }

    [Fact]
    public void ABarNoNoteMovesInIsLeftAlone()
    {
        // The second bar has no moved note: the voices' own reading stands.
        var score = Score(Book("r1 | fis'4 r2. |", "c4@cross r2. | r4 fis'4 r2 |"));
        var lh = Notes(score, 1);
        Assert.Equal("sharp", lh.Last().Accidental);
    }

    /// <summary>OMR REQUESTS #21: the lower voice's rest stands where it stands without the
    /// @cross, as LilyPond's does in both bars of the OMR's own repro.</summary>
    [Fact]
    public void ALowerVoicesRestDoesNotClearANoteOnTheOtherStaff()
    {
        var layout = new LayoutEngine().Layout(Score(Book("e''2 | e''2 | e''1 |", """
            voice { tuplet 3/2 { d8 g8@cross b8@cross } tuplet 3/2 { d8 g8@cross b8@cross } } { g,8 r8 g,8 r8 } r2 |
            voice { tuplet 3/2 { d8 g8 b8 } tuplet 3/2 { d8 g8 b8 } } { g,8 r8 g,8 r8 } r2 |
            c1 |
            """)));
        double moved = layout.GetRestShift(1, 0, 1, 1);
        double plain = layout.GetRestShift(1, 1, 1, 1);
        Assert.Equal(plain, moved);
    }

    [Fact]
    public void ACrossStaffBeamClearsWhatStandsOnTheOtherStaff()
    {
        // OMR cross-try bar 6, LilyPond 2.26.0: the beam rises clear of the right hand's eighth,
        // which its stems reach (lily/beam-collision-engraver.cc:128-155).
        // Read at the eighth's own x and about ITS staff: a comparison with an empty right hand
        // cannot tell a beam that cleared the note from staves its stems pushed apart.
        var layout = new LayoutEngine().Layout(Score(Book("r8 d''8 c''4 r2 |",
            "tuplet 3/2 { c8 gis@cross b@cross } tuplet 3/2 { c8 gis@cross b@cross } r2 |")));
        var beam = layout.BeamLayouts.Where(b => b.StaffIndex == 1).OrderBy(b => b.LeftX).First();
        var system = layout.Systems[0];
        double staves = LayoutUtilities.FindStaffYInSystem(system, 0) - LayoutUtilities.FindStaffYInSystem(system, 1);
        // The eighth's column (1/8) lies midway between the group's last two notes (1/12, 1/6).
        double x = (beam.MemberXPositions[1] + beam.MemberXPositions[2]) / 2;
        double along = (x - beam.LeftStemX) / (beam.RightStemX - beam.LeftStemX);
        double beamAboutItsStaff = (beam.LeftY + along * (beam.RightY - beam.LeftY)) / 2.0 - staves;
        // d'' stands at +2: its head reaches a space and a half above the middle line.
        Assert.True(beamAboutItsStaff > 1.5, $"beam at {beamAboutItsStaff:F2} about the upper staff");
    }

    [Fact]
    public void ARestsDotIsNotMovedByADotOnTheOtherStaff()
    {
        // The lower voice's dotted rest and a dotted note moved away at one moment: f' reads -3
        // on the treble staff, the space the rest's own dot would take were they on one staff.
        int? DotOf(string upper) => new LayoutEngine()
            .Layout(Score(Book("r1 |", "voice { " + upper + " r8 r2 } { r4. r8 r2 } |")))
            .GetRestDotOffset(0, 1, 0);
        Assert.Equal(DotOf("s4."), DotOf("f'4.@cross"));
    }

    [Fact]
    public void ALowerVoicesBeamDoesNotClearANoteOnTheOtherStaff()
    {
        // LilyPond (v1 bar 5): the same short stems with the upper voice on the other staff.
        BeamLayout Lower(string upper) => new LayoutEngine()
            .Layout(Score(Book("r1 |", "voice { " + upper + " r2 } { c8 d c d r2 } |")))
            .BeamLayouts.Single(b => b.Group.VoiceIndex == 1 && b.StaffIndex == 1);
        var moved = Lower("g8@cross b@cross g@cross b@cross");
        var away = Lower("s2");
        Assert.Equal(away.LeftY, moved.LeftY, 6);
        Assert.Equal(away.RightY, moved.RightY, 6);
    }

    [Fact]
    public void AHeadDoesNotStepAsideForANoteOnTheOtherStaff()
    {
        // c'' reads +1 on the treble staff and d 0 on the bass: a second only if read as one staff.
        var staff = Score(Book("r1 |", "voice { c''4@cross r2. } { d4 r2. } |")).EnumerateStaves().ToList()[1].Staff;
        var table = LilySharp.Core.Svg.Layout.SpacingRules.VoiceCollisionShiftsOf(staff);
        Assert.Equal(0, table.ShiftOf(0, 1, 0));
        Assert.Equal(0, table.ShiftOf(0, 2, 0));
    }

    /// <summary>LilyPond's shapes (Lab sessions/p1093/rc, 2.26.0): a rest under a moved note goes
    /// down past it. <c>r1</c> on the treble staff at c, whose head stands at -13.</summary>
    [Fact]
    public void ARestGoesBelowTheNoteMovedOntoItsStaff()
    {
        var layout = new LayoutEngine().Layout(Score(Book("r1 | r1 |", "c1 | c4@cross r4 r2 |")));
        Assert.True(layout.GetRestShift(0, 1, 0, 0) < -13);
    }

    [Fact]
    public void ARestClearsTheStemOfADownStemNoteAtItsMoment()
    {
        // c'' stands at +1 with its stem down: past the tip, not just the head (rc bar 4).
        var layout = new LayoutEngine().Layout(Score(Book("r1 | r4 r2. |", "c1 | c''4@cross r2. |")));
        Assert.True(layout.GetRestShift(0, 1, 0, 0) < -9);
    }

    [Fact]
    public void ARestClearsANoteThatStillSounds()
    {
        // The half note began before the second rest and still sounds: its head only.
        var layout = new LayoutEngine().Layout(Score(Book("r4 r4 r2 |", "c2@cross r2 |")));
        Assert.True(layout.GetRestShift(0, 0, 0, 1) < -13);
        Assert.Equal(0, layout.GetRestShift(0, 0, 0, 2));
    }

    [Fact]
    public void ARestShiftBelongsToItsOwnStaff()
    {
        // The same address on two staves: (bar 0, voice 0, item 2) is a rest on each. The upper
        // one sounds with the moved note; the lower one does not move for it.
        var layout = new LayoutEngine().Layout(Score(Book("r8 r8 r4 r2 |", "r4 c4@cross r2 |")));
        Assert.True(layout.GetRestShift(0, 0, 0, 2) < -13);
        Assert.Equal(0, layout.GetRestShift(1, 0, 0, 2));
    }

    [Fact]
    public void TheDrawingReadsTheRestShiftOfItsOwnStaff()
    {
        // The page, not the table: the lower staff's half rest stays on its middle line, where
        // the quarter rest before it stands.
        string book = Book("r8 r8 r4 r2 |", "r4 c4@cross r2 |");
        string svg = LilySharp.Core.Svg.SvgGenerator.Generate(SyntaxTree.Parse(book),
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });
        string YOf(int position) => System.Text.RegularExpressions.Regex.Match(svg,
            $"y=\"([\\d.]+)\"[^>]*data-pos=\"{position}\"").Groups[1].Value;
        int lh = book.IndexOf("lh {", book.IndexOf("section", System.StringComparison.Ordinal), System.StringComparison.Ordinal);
        int quarter = book.IndexOf("r4", lh, System.StringComparison.Ordinal);
        int half = book.IndexOf("r2", lh, System.StringComparison.Ordinal);
        Assert.NotEqual("", YOf(quarter));
        Assert.Equal(YOf(quarter), YOf(half));
    }

    /// <summary>The slur's span against LilyPond 2.26.0's (`lysc ly --pin-fonts`, -dbackend=svg,
    /// two decimals: Lab sessions/p1093/snum). <c>dyUp</c> is the end above the start.</summary>
    [Theory]
    [InlineData("c2( e''2@cross) | c1 |", 4.49, 4.50, 0.02)]
    [InlineData("c4( e''@cross e''@cross c) | c1 |", 8.28, 0.00, 0.02)]
    [InlineData("c4( e g'@cross c''@cross) | c1 |", 8.51, 1.00, 0.02)]
    // A kneed pair: the width is the spacing's, which needs the knee BEFORE it (3.68, not 2.25).
    // ⚠️ The rise is 4.05 against LilyPond's 3.90: named, not explained.
    [InlineData("c8( g@cross) r4 c'4@cross( c,4@cross) | c1 |", 3.68, 4.05, 0.02)]
    public void ASlurAcrossTheStavesHasLilyPondsSpan(string lh, double dx, double dyUp, double tolerance)
    {
        var score = Score(Book("r1 | r1 |", lh));
        var slur = new LayoutEngine().Layout(score).SlurLayouts.OrderBy(s => s.StartX).First();
        Assert.InRange(slur.EndX - slur.StartX, dx - tolerance, dx + tolerance);
        Assert.InRange(slur.EndYUp - slur.StartYUp, dyUp - tolerance, dyUp + tolerance);
    }

    [Fact]
    public void AKneeIsStampedBeforeTheSpacing()
    {
        // Before any layout: `c8 g@cross` knees, the low stem up and the moved one down.
        var notes = Notes(Score(Book("r1 |", "c8 g@cross r4 c'4@cross c,4@cross |")), 1);
        Assert.True(notes[0].StemUp);
        Assert.False(notes[1].StemUp);
    }

    /// <summary>Lab sessions/p1094/w3: five bars of 2/4, the last four `tuplet 3/2 { … }` twice,
    /// moved then not, plain then with a sharp.</summary>
    private const string WidthBook = """
        octave absolute
        time 2/4
        part rh { clef treble }
        part lh { clef bass }
        section A { rh { r2 | r2 | r2 | r2 | r2 | } lh { c2 | tuplet 3/2 { c8 g@cross b@cross } tuplet 3/2 { c8 g@cross b@cross } | tuplet 3/2 { c8 gis@cross b@cross } tuplet 3/2 { c8 gis@cross b@cross } | tuplet 3/2 { c8 g b } tuplet 3/2 { c8 g b } | tuplet 3/2 { c8 gis b } tuplet 3/2 { c8 gis b } | } }
        form { ~A }
        score { grandStaff { staff rh staff lh } }
        """;

    private static double[] BlackHeads(string svg) => System.Text.RegularExpressions.Regex
        .Matches(svg, "<text class=\"music\" x=\"([-\\d.]+)\"[^>]*></text>")
        .Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
        .OrderBy(x => x).ToArray();

    [Theory]
    // MEASURED, LilyPond 2.26.0 (Lab sessions/p1093/w2): the sharp's gap is the same moved or not.
    [InlineData(0, 2.254)]   // c g@cross
    [InlineData(6, 3.354)]   // c gis@cross
    [InlineData(9, 2.254)]   // the second gis in the bar writes no sharp
    [InlineData(12, 2.754)]  // c g
    [InlineData(18, 3.354)]  // c gis
    public void AMovedNotesAccidentalTakesTheRoomItTakesAtHome(int head, double gap)
    {
        var heads = BlackHeads(LilySharp.Core.Svg.SvgGenerator.Generate(SyntaxTree.Parse(WidthBook)));
        Assert.InRange(heads[head + 1] - heads[head], gap - 0.02, gap + 0.02);
    }

    [Fact]
    public void AMovedStemSharesItsBeamsPureTip_AcrossTheStaves()
    {
        // MEASURED, LilyPond 2.26.0 (Lab sessions/p1094/w3/pure.ly): `ly:grob-pure-height` of all
        // three up stems tops out at 9.05 spaces: b's own tip (0) on the staff 9.05 above c's.
        var notes = Notes(Score(Book("r1 |", "tuplet 3/2 { c8 gis@cross b@cross } r4 r2 |")), 1);
        Assert.All(notes, n => Assert.True(n.StemUp));
        Assert.All(notes, n => Assert.InRange(n.PureBeamedStemTip!.Value, 18.0, 18.2));
        Assert.Equal(0.0, notes[0].CrossPureShift);
        Assert.InRange(notes[1].CrossPureShift, 18.0, 18.2);
    }

    [Fact]
    public void AMovedLastNoteTakesNoStemCorrectionBeforeTheBarLine()
    {
        // MEASURED, LilyPond 2.26.0 (Lab sessions/p1094/w3/bar.ly): 2.09 up or down, at any height.
        string[] lasts = ["b@cross", "d'@cross", "g'@cross", "d''@cross", "a@cross"];
        string music = string.Concat(lasts.Select(l => $"r2 r4 tuplet 3/2 {{ c8 g@cross {l} }} | "));
        string svg = LilySharp.Core.Svg.SvgGenerator.Generate(SyntaxTree.Parse(Book(
            string.Concat(lasts.Select(_ => "r1 | ")), music)));
        var heads = BlackHeads(svg);
        var bars = System.Text.RegularExpressions.Regex
            .Matches(svg, "<rect x=\"([\\d.]+)\" y=\"[\\d.]+\" width=\"0.19\"")
            .Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .Distinct().OrderBy(x => x).ToArray();
        var lastHeads = heads.Where((_, i) => i % 3 == 2).ToArray();
        Assert.Equal(lasts.Length, lastHeads.Length);
        var gaps = lastHeads.Select(h => bars.First(b => b > h) - h).ToArray();
        Assert.All(gaps, g => Assert.InRange(g, gaps[0] - 0.02, gaps[0] + 0.02));
    }

    [Fact]
    public void AMovedLastNoteMeetsTheBarLineOfTheStaffItStandsOn()
    {
        // ⚠️ 2.50 against LilyPond's 2.40 (Lab sessions/p1094/w3/t2): named, not explained. The
        // bar line stands in the note's frame, so a sharp-less head in the staff reaches it.
        string svg = LilySharp.Core.Svg.SvgGenerator.Generate(SyntaxTree.Parse(WidthBook));
        var heads = BlackHeads(svg);
        var bars = System.Text.RegularExpressions.Regex
            .Matches(svg, "<rect x=\"([\\d.]+)\" y=\"[\\d.]+\" width=\"0.19\"")
            .Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .Distinct().OrderBy(x => x).ToArray();
        double gap = bars.First(b => b > heads[5]) - heads[5];
        Assert.InRange(gap, 2.48, 2.52);
    }

    [Fact]
    public void ACrossStaffSlurDoesNotOpenTheStaves()
    {
        // It is in no skyline, as LilyPond's isn't (lily/axis-group-interface.cc:921
        // skyline_spacing): an arch over the staves leaves their distance where it was.
        double DistanceOf(string lh)
        {
            var system = new LayoutEngine().Layout(Score(Book("r1 |", lh))).Systems[0];
            return LayoutUtilities.FindStaffYInSystem(system, 0) - LayoutUtilities.FindStaffYInSystem(system, 1);
        }
        Assert.Equal(DistanceOf("c8 e g@cross c'@cross e g c e |"),
            DistanceOf("c8( e g@cross c'@cross e g c e) |"), 6);
    }

    [Fact]
    public void ASlurAcrossTheStavesReachesTheMovedNote()
    {
        // LilyPond (s1 bar 3): from the bass staff up to a note on the treble staff. Unmoved, the
        // far end reads the treble position about the bass staff, under the staff.
        var layout = new LayoutEngine().Layout(Score(Book("r1 | r1 |", "c4( e g@cross c'@cross) | c1 |")));
        var slur = layout.SlurLayouts.Single();
        Assert.True(slur.CrossesStaves);
        Assert.True(slur.EndYUp > slur.StartYUp + 1.5);
    }

    [Fact]
    public void ASlurOverOnlyMovedNotesStandsOnTheirStaff()
    {
        var moved = new LayoutEngine().Layout(Score(Book("r1 |", "c4@cross( e@cross) r2 |"))).SlurLayouts.Single();
        var plain = new LayoutEngine().Layout(Score(Book("r1 |", "c4( e) r2 |"))).SlurLayouts.Single();
        Assert.True(moved.StartYUp > plain.StartYUp);
    }

    [Fact]
    public void ASlurOverNoMovedNoteIsNotCrossStaff()
    {
        var slurs = new LayoutEngine().Layout(Score(Book("r1 |", "c4( e) g@cross c' |"))).SlurLayouts;
        Assert.False(Assert.Single(slurs).CrossesStaves);
    }

    /// <summary>The preview's road: an edit on a warm session draws what a full compile draws.
    /// The staff a note moved ONTO is the one a per-staff reuse could leave stale.</summary>
    [Theory]
    [InlineData("g8@cross c'8 r4 r2 | c1", "e8@cross c'8 r4 r2 | c1")]   // the moved note's pitch
    [InlineData("g8@cross c'8 r4 r2 | c1", "g8 c'8 r4 r2 | c1")]         // @cross written away
    [InlineData("g8 c'8 r4 r2 | c1", "g8@cross c'8 r4 r2 | c1")]         // @cross written in
    [InlineData("g8@cross c'8 r4 r2 | c1", "c,8@cross c'8 r4 r2 | c1")]  // far enough to move the staves
    public void AnIncrementalEditMatchesAFullCompile(string before, string after)
    {
        var opt = new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false };
        string bars = string.Concat(Enumerable.Repeat("c8 e g c' e g c' e | ", 16));
        string Text(string lh) => Book("c''1 | c''1 | " + string.Concat(Enumerable.Repeat("c''1 | ", 16)), "c1 | " + lh + " | " + bars);
        string seed = Text(before);
        var tree = SyntaxTree.Parse(seed);
        var session = new LilySharp.Core.Svg.IncrementalCompiler(tree, opt);
        session.Render();
        int at = seed.IndexOf(before, System.StringComparison.Ordinal);
        var change = new TextChange(new TextSpan(at, before.Length), after);
        string incremental = session.Edit(change).Replace("\r\n", "\n");
        string full = LilySharp.Core.Svg.SvgGenerator.Generate(SyntaxTree.Parse(Text(after)), opt).Replace("\r\n", "\n");
        Assert.Equal(full, incremental);
    }

    /// <summary>A moved note's place in the system's frame reads the whole staff's pure box, so
    /// an edit bars away moves it: <see cref="MusicItem.CrossPureShift"/> and the tip.</summary>
    [Theory]
    [InlineData("tuplet 3/2 { c8 gis@cross b@cross } r4 r2 | ")]   // the tip enters the key as well
    [InlineData("c4 gis4@cross r2 | ")]                            // no beam: only the shift does
    public void AnEditThatMovesThePureDistanceReachesAMovedNoteElsewhere(string moved)
    {
        var opt = new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false };
        string bars = string.Concat(Enumerable.Repeat("c8 e g c' e g c' e | ", 16));
        string Text(string far) => Book(string.Concat(Enumerable.Repeat("c''1 | ", 18)), moved + far + " | " + bars + "c1 | ");
        string seed = Text("c1");
        var session = new LilySharp.Core.Svg.IncrementalCompiler(SyntaxTree.Parse(seed), opt);
        session.Render();
        const string FarHigh = "c'''1";
        int bar = seed.IndexOf(moved, System.StringComparison.Ordinal) + moved.Length;
        string incremental = session.Edit(new TextChange(new TextSpan(bar, 2), FarHigh)).Replace("\r\n", "\n");
        string full = LilySharp.Core.Svg.SvgGenerator.Generate(SyntaxTree.Parse(Text(FarHigh)), opt).Replace("\r\n", "\n");
        Assert.Equal(full, incremental);
        // The edit did reach the moved note: the shift of the full compile moved.
        double ShiftOf(string far) => Notes(Score(Text(far)), 1).First(n => n.CrossStaffIndex >= 0).CrossPureShift;
        Assert.True(ShiftOf(FarHigh) > ShiftOf("c1") + 1);
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
