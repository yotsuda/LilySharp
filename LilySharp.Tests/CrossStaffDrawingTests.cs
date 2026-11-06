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

    /// <summary>LilyPond 2.26.0's shifts (Lab sessions/p1111/x1, -dbackend=svg): a moved note
    /// collides with the heads of the staff it stands on, as two voices of one staff do. The down
    /// head a second below the up one steps right by 1.30; a second above, the up head does (1.04).
    /// </summary>
    [Theory]
    [InlineData(1, 0.0, 1.30)]   // e''@stemUp over d''@cross@stemDown
    [InlineData(2, 1.04, 0.0)]   // e''@stemUp under f''@cross@stemDown
    [InlineData(3, 0.0, 1.30)]   // f''@stemUp over e''@cross@stemDown
    [InlineData(0, 0.0, 0.0)]    // the unison shares its column
    public void AMovedNoteCollidesWithTheHeadsOfTheStaffItStandsOn(int item, double up, double down)
    {
        var layout = new LayoutEngine().Layout(Score(Book(
            "e''4@stemUp e''@stemUp e''@stemUp f''@stemUp | c''1 |",
            "e''4@cross@stemDown d''4@cross@stemDown f''4@cross@stemDown e''4@cross@stemDown | c1 |")));
        Assert.InRange(layout.GetVoiceOffset(0, 0, 1, item), up - 0.02, up + 0.02);
        Assert.InRange(layout.GetVoiceOffset(1, 0, 1, item), down - 0.02, down + 0.02);
    }

    [Fact]
    public void AMovedNoteTheSameWayStandsBesideTheHeadItMeets()
    {
        // Both stems down, a unison: Lily# gives the moved voice a slot of its own (LilyPond keeps
        // them in one and warns), so the heads stand side by side as LilyPond drew them (p1093/rc).
        var layout = new LayoutEngine().Layout(Score(Book("e''4 e'' r2 |", "e''4@cross e''@cross r2 |")));
        Assert.True(System.Math.Abs(layout.GetVoiceOffset(0, 0, 1, 0) - layout.GetVoiceOffset(1, 0, 1, 0)) > 1.2);
    }

    /// <summary>Lab sessions/p1111/x3: the gaps LilyPond 2.26.0 spaces the shared columns at
    /// (-dbackend=svg). The wish of the moved voice is averaged with the wishes of the staff it
    /// stands on, and its heads meet theirs as rods.</summary>
    private const string SharedColumnBook = """
        octave absolute
        time 4/4
        part rh { clef treble }
        part lh { clef bass }
        section A { rh { e''4@stemUp e''@stemUp e''@stemUp f''@stemUp | a'2@stemUp g'2@stemUp | e''4 e'' e'' e'' | e''4@stemUp e''@stemUp r2 | e''4@stemUp e''@stemUp e''@stemUp r4 | c1 | } lh { r4 d''4@cross@stemDown f''4@cross@stemDown e''4@cross@stemDown | g'2@cross@stemDown g'2@cross@stemDown | r4 e''4@cross e''4@cross r4 | d''8@cross@stemDown c''@cross@stemDown f''@cross@stemDown e''@cross@stemDown r2 | r4 d''4@cross@stemDown r2 | c1 | } }
        form { ~A }
        score { grandStaff { staff rh staff lh } }
        """;

    [Theory]
    [InlineData(0, 3, 6.60)]     // bar 1: the first column to the third (LilyPond 6.60)
    [InlineData(1, 3, 3.78)]     // …the second, shared, to the third (3.78)
    [InlineData(0, 5, 10.12)]    // …the first to the fourth (10.12)
    [InlineData(5, 7, 4.65)]     // …the fourth, shared, to the next bar: the closing wish (4.65)
    [InlineData(11, 13, 3.66)]   // bar 3: the second column to the third, both same-way pairs (3.66)
    [InlineData(19, 21, 2.65)]   // bar 4: f''8 to the e''8 beside the shifted e''4 (2.65)
    [InlineData(23, 25, 3.66)]   // bar 5: only the moved head shifts, its next item on its own staff (3.66)
    public void ASharedColumnIsSpacedAsLilyPondSpacesIt(int from, int to, double gap)
    {
        // The black and half heads (U+E0FE, U+E0FD) in X order; equal X counts once.
        var heads = System.Text.RegularExpressions.Regex
            .Matches(LilySharp.Core.Svg.SvgGenerator.Generate(SyntaxTree.Parse(SharedColumnBook)),
                "<text class=\"music\" x=\"([-\\d.]+)\"[^>]*>[]</text>")
            .Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .Distinct().OrderBy(x => x).ToArray();
        Assert.InRange(heads[to] - heads[from], gap - 0.03, gap + 0.03);
    }

    /// <summary>Lab sessions/p1111/b1, LilyPond 2.26.0: a beam whose every note moved is that
    /// staff's beam, not a cross-staff one — `d''8 c'' f'' e''` (concave) beams flat, at the
    /// height the same notes beam at on their own staff.</summary>
    [Theory]
    [InlineData("d''8@cross@stemDown c''@cross@stemDown f''@cross@stemDown e''@cross@stemDown r2", "d''8@stemDown c''@stemDown f''@stemDown e''@stemDown r2", true)]
    [InlineData("d''8@cross c''@cross f''@cross e''@cross r2", "d''8 c'' f'' e'' r2", true)]
    // Sloped: the slope is charged as a one-staff beam's, and quanted against that staff's lines.
    [InlineData("g'8@cross a'@cross b'@cross d''@cross r2", "g'8 a' b' d'' r2", false)]
    [InlineData("c''8@cross b'@cross a'@cross e'@cross r2", "c''8 b' a' e' r2", false)]
    [InlineData("e'8@cross f'@cross g'@cross c''@cross r2", "e'8 f' g' c'' r2", false)]
    public void ABeamWhoseNotesAllMovedIsTheirStaffsBeam(string moved, string home, bool flat)
    {
        // The beam's top edge at its two ends: points="x,yLeft x,yRight …".
        System.Text.RegularExpressions.Match Beam(string book) => System.Text.RegularExpressions.Regex.Match(
            LilySharp.Core.Svg.SvgGenerator.Generate(SyntaxTree.Parse(book)),
            "<polygon points=\"[-\\d.]+,([-\\d.]+) [-\\d.]+,([-\\d.]+) ");
        var m = Beam(Book("r1 |", moved + " |"));
        var h = Beam(Book(home + " |", "r1 |"));
        Assert.True(m.Success && h.Success);
        Assert.Equal(flat, m.Groups[1].Value == m.Groups[2].Value);
        Assert.Equal(h.Groups[1].Value, m.Groups[1].Value);   // where it stands at home
        Assert.Equal(h.Groups[2].Value, m.Groups[2].Value);
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
    public void ABeamClearsTheHeadsMovedOntoItsStaff()
    {
        // MEASURED, LilyPond 2.26.0 (Lab sessions/p1111/c1, -dbackend=svg): the treble staff's
        // beam under the a's moved up from the bass stands at 16.88 (its middle), clear of their
        // heads; read through them it stood at 16.19. The other beams stood where LilyPond's do.
        string book = Book(
            "e'8@stemDown f'@stemDown e'@stemDown f'@stemDown r2 | d'8@stemDown e'@stemDown d'@stemDown e'@stemDown r2 | c1 |",
            "c8 a@cross c a@cross r2 | e8 f'@cross e f'@cross r2 | c1 |");
        var tops = System.Text.RegularExpressions.Regex.Matches(
                LilySharp.Core.Svg.SvgGenerator.Generate(SyntaxTree.Parse(book)),
                "<polygon points=\"([-\\d.]+),([-\\d.]+) ")
            .Select(m => (X: double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                          Y: double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)))
            .OrderBy(b => b.X).ToArray();
        // The first beam from the left is the treble staff's in bar 1; its middle is its top + 0.24.
        Assert.InRange(tops[0].Y + 0.24, 16.86, 16.90);
    }

    [Fact]
    public void ACrossStaffBeamClearsWhatAnotherVoiceMovedWhereItsNotesWent()
    {
        // MEASURED, LilyPond 2.26.0 (Lab sessions/p1111/e1, -dbackend=svg): the bass voice's beam
        // to the a's moved up rises clear of the e' the second voice moved up — its left end's
        // middle at 12.60; through the head it stood at 14.29 (without the e', 13.60 in both).
        // ⚠️ Lily# stands at 12.79, 0.19 off. The e' is dotted and crosses the a, and Lily# meshes
        // a dotted head by the undotted 0.17 (NoteCollision, LILYSHARP-OWN, owner 2026-09-26) where
        // LilyPond takes 0.1 — the a stands 0.44 right of the e' against LilyPond's 0.26, and the
        // beam's least squares sees that stem. With LilyPond's 0.1 bar 2 of e1 lands exactly on
        // LilyPond's beam, but this bar goes to 11.98: a second cause here, not found.
        string book = Book("r1 | c1 |", "voice { c8 a@cross c a@cross r2 } { r8 e'4.@cross r2 } | c1 |");
        var beam = System.Text.RegularExpressions.Regex.Match(
            LilySharp.Core.Svg.SvgGenerator.Generate(SyntaxTree.Parse(book)),
            "<polygon points=\"[-\\d.]+,([-\\d.]+) ");
        Assert.True(beam.Success);
        double leftMiddle = double.Parse(beam.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) + 0.24;
        Assert.InRange(leftMiddle, 12.55, 12.85);
    }

    [Fact]
    public void AWholeNoteLendsThePureDistanceNoStem()
    {
        // MEASURED, LilyPond 2.26.0 (Lab sessions/p1111/c2): with a `c1` on each staff the pure
        // distance is 10.10 and `c8 a@cross c a@cross` does not knee — every stem up. Counting a
        // stem on each whole note made it 11.0, and the beam kneed.
        var notes = Notes(Score(Book("r1 | c1 |", "c8 a@cross c a@cross r2 | c1 |")), 1);
        Assert.All(notes.Take(4), n => Assert.True(n.StemUp));
        Assert.InRange(notes[1].CrossPureShift, 19.9, 20.3);
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

    /// <summary>
    /// Each drawn dot as "staff:row@dx": its staff (0 the upper), its row in half
    /// spaces off that staff's middle line, and its x from the leftmost head within 3.5 spaces to
    /// its left; sorted.
    /// </summary>
    private static string DotsOfBar(string svg)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var lines = System.Text.RegularExpressions.Regex
            .Matches(svg, "<line x1=\"([-\\d.]+)\" y1=\"([-\\d.]+)\" x2=\"([-\\d.]+)\" y2=\"([-\\d.]+)\"")
            .Where(m => m.Groups[2].Value == m.Groups[4].Value
                && double.Parse(m.Groups[3].Value, inv) - double.Parse(m.Groups[1].Value, inv) > 5)
            .Select(m => double.Parse(m.Groups[2].Value, inv)).Distinct().OrderBy(y => y).ToArray();
        var middles = Enumerable.Range(0, lines.Length / 5).Select(s => lines[5 * s + 2]).ToArray();
        var glyphs = System.Text.RegularExpressions.Regex
            .Matches(svg, "<text class=\"music\" x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"[^>]*>(.)</text>")
            .Select(m => (C: (int)m.Groups[3].Value[0], X: double.Parse(m.Groups[1].Value, inv), Y: double.Parse(m.Groups[2].Value, inv)))
            .ToArray();
        var heads = glyphs.Where(g => g.C is 0xE0FE or 0xE0FD or 0xE0FC or 0xE0FB).ToArray();
        return string.Join(" ", glyphs.Where(g => g.C == 0xE038).Select(dot =>
        {
            int staff = Array.IndexOf(middles, middles.OrderBy(y => Math.Abs(y - dot.Y)).First());
            double left = heads.Where(h => h.X <= dot.X && h.X > dot.X - 3.5).Min(h => h.X);
            return $"{staff}:{(int)Math.Round((middles[staff] - dot.Y) * 2)}@{(dot.X - left).ToString("F2", inv)}";
        }).OrderBy(s => s, StringComparer.Ordinal));
    }

    /// <summary>
    /// MEASURED, LilyPond 2.26.0 (Lab sessions/p1112/d1, d2; the Dots grobs' X off the left head):
    /// a moved note's dots join the dot column of the staff it stands on — one configuration of
    /// rows, one X. Lily# read them apart, both on row 3 at 1.76 and 3.06 (the first case).
    /// </summary>
    [Theory]
    [InlineData("e''4.@stemUp f''8@stemUp e''2@stemUp |", "d''4.@cross@stemDown c''8@cross@stemDown d''2@cross@stemDown |",
        "0:1@3.06 0:3@3.06")]
    [InlineData("voice { g''4. a''8 g''2 } { c''4. d''8 c''2 } |", "e''4.@cross f''8@cross e''2@cross |",
        "0:1@2.41 0:3@2.41 0:5@2.41")]
    [InlineData("<e'' g''>4.@stemUp f''8@stemUp e''2@stemUp |", "<c'' d''>4.@cross@stemDown c''8@cross@stemDown d''2@cross@stemDown |",
        "0:-1@3.06 0:1@3.06 0:3@3.06 0:5@3.06")]
    [InlineData("r4. f''8 e''2 |", "d''4.@cross b'8@cross c''2@cross |",
        "0:-9@1.75 0:3@1.75")]
    [InlineData("c'4.@cross@stemUp d'8@cross@stemUp c'2@cross@stemUp |", "b4.@stemDown a8@stemDown b2@stemDown |",
        "1:5@3.06 1:7@3.06")]
    // The moved dot takes row 3, so the staff's own d'' gives it up for row 1 (Lab sessions/p1112/d3).
    [InlineData("voice { d''4. c''8 d''2 } { g'4. a'8 g'2 } |", "e''4.@cross@stemDown d''8@cross e''2@cross |",
        "0:-3@3.06 0:1@3.06 0:3@3.06")]
    [InlineData("voice { e''4. c''8 d''2 } { b'4. a'8 g'2 } |", "c''4.@cross@stemDown r8 r2 |",
        "0:-1@3.06 0:1@3.06 0:3@3.06")]
    // A moved up stem after the staff's own up voice is a support of its dots.
    [InlineData("e''4.@stemUp f''8@stemUp e''2@stemUp |", "c''4@cross@stemUp c''4@cross@stemUp c''2@cross@stemUp |",
        "0:3@2.41")]
    public void AMovedNotesDotsJoinTheColumnOfTheStaffItStandsOn(string rh, string lh, string dots)
    {
        var svg = LilySharp.Core.Svg.SvgGenerator.Generate(SyntaxTree.Parse(Book("r1 | " + rh, "r1 | " + lh)));
        var expected = dots.Split(' ');
        var actual = DotsOfBar(svg).Split(' ');
        Assert.Equal(expected.Length, actual.Length);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var (e, a) in expected.OrderBy(s => s, StringComparer.Ordinal).Zip(actual))
        {
            Assert.Equal(e.Split('@')[0], a.Split('@')[0]);
            Assert.InRange(double.Parse(a.Split('@')[1], inv), double.Parse(e.Split('@')[1], inv) - 0.02, double.Parse(e.Split('@')[1], inv) + 0.02);
        }
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
        // Not the last: it ends the line (ABarClosedWithoutAWishLosesTheBarLineAtALinesEnd).
        var gaps = lastHeads.Select(h => bars.First(b => b > h) - h).SkipLast(1).ToArray();
        Assert.All(gaps, g => Assert.InRange(g, gaps[0] - 0.02, gaps[0] + 0.02));
    }

    [Fact]
    public void AMovedLastNoteMeetsTheBarLineOfTheStaffItStandsOn()
    {
        // MEASURED, LilyPond 2.26.0 (Lab sessions/p1093/w2): 2.40, the bare duration spring.
        string svg = LilySharp.Core.Svg.SvgGenerator.Generate(SyntaxTree.Parse(WidthBook));
        var heads = BlackHeads(svg);
        var bars = System.Text.RegularExpressions.Regex
            .Matches(svg, "<rect x=\"([\\d.]+)\" y=\"[\\d.]+\" width=\"0.19\"")
            .Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .Distinct().OrderBy(x => x).ToArray();
        double gap = bars.First(b => b > heads[5]) - heads[5];
        Assert.InRange(gap, 2.38, 2.42);
    }

    [Theory]
    // MEASURED, LilyPond 2.26.0 (Lab sessions/p1094/w3/end.ly): 2.400 mid-line, 2.210 at a line's
    // end: a wish takes the bar line's extent back, and no wish reaches this bar line.
    [InlineData("r2 r4 c8 g@cross | c1 |", true)]    // the voice has left
    [InlineData("r2 r4 c8 a | c1 |", false)]         // a home note's wish does take it back
    public void ABarClosedWithoutAWishLosesTheBarLineAtALinesEnd(string lh, bool shrinks)
    {
        var score = Score(Book("r1 | r1 |", lh));
        var measure = score.PrimaryContentStaff.PrimaryVoice.Measures[0];
        // The primary staff is rh: what is asked of the score's last column is the lh voice's.
        var springs = SpacingRules.CreateSpringsForMeasure(
            LilySharp.Core.Rendering.ScoreTextMetrics.Bundled, measure, SpacingOptions.Default);
        var ended = MultiStaffLayouter.LineEndBeforeGraceSprings(score, 0, springs);
        double taken = springs[^1].IdealDistance - ended[^1].IdealDistance;
        Assert.Equal(shrinks ? SpacingRules.GetBarlineWidth(measure.EndBarline) : 0.0, taken, 6);
    }

    [Fact]
    public void AMovedLastNoteBeforeTheFinalBarStandsWhereLilyPondsDoes()
    {
        // Lab sessions/p1094/w3/end2.lys, its last line against its twin's: 12.48 to the thin
        // stroke, 12.97 to the thick (both justified: the spring's share, not the bare 2.40).
        const string Text = """
            octave absolute
            time 2/4
            part rh { clef treble }
            part lh { clef bass }
            section A { rh { s2 | s2 break | s2 | s2 break | s2 | s2 | s2 break | s2 | s2 | } lh {
              r4 c8 g@cross | c2 |
              r4 c8 g@cross break | c2 |
              r4 c8 a break | c2 |
              r4 c8 g@cross || break c2 |
              r4 c8 g@cross |.
            } }
            form { ~A }
            score { grandStaff { staff rh staff lh } }
            """;
        string svg = LilySharp.Core.Svg.SvgGenerator.Generate(SyntaxTree.Parse(Text));
        double Value(string s) => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        var thick = System.Text.RegularExpressions.Regex.Matches(svg, "<rect x=\"([\\d.]+)\" y=\"([\\d.]+)\" width=\"0.60\"")
            .Select(m => (X: Value(m.Groups[1].Value), Y: Value(m.Groups[2].Value))).Last();
        double lastHead = System.Text.RegularExpressions.Regex
            .Matches(svg, "<text class=\"music\" x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"[^>]*></text>")
            .Select(m => (X: Value(m.Groups[1].Value), Y: Value(m.Groups[2].Value)))
            .Where(h => System.Math.Abs(h.Y - thick.Y) < 14 && h.X < thick.X)
            .Max(h => h.X);
        Assert.InRange(thick.X - lastHead, 12.95, 12.99);
    }

    [Theory]
    // Whether the voice stands on another staff at the bar line, not whether the note moved.
    [InlineData("r2 r4 c8 g@cross | c1 |", true)]    // moved, then home
    [InlineData("r2 r4 c8 g@cross | a1@cross |", false)]  // moved, and still there
    [InlineData("r2 r4 c8 a | a1@cross |", true)]    // home, then gone
    [InlineData("r2 r4 c8 a | c1 |", false)]         // home, and still there
    public void ABarClosesWithoutAWishOnlyWhereTheVoiceHasLeft(string lh, bool without)
    {
        var last = Score(Book("r1 | r1 |", lh)).EnumerateStaves().ToList()[1].Staff.PrimaryVoice
            .Measures[0].Items.Last();
        Assert.Equal(without, last.ClosesWithoutWish);
    }

    [Fact]
    public void TheMarkDoesNotOutliveTheCross()
    {
        // The unmoved last note is marked by copy: its own @cross written away, the book is clean.
        var opt = new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false };
        string Text(string next) => Book("r1 | r1 | r1 |", "r2 r4 c8 a | " + next + " | c1 |");
        string seed = Text("a1@cross");
        var session = new LilySharp.Core.Svg.IncrementalCompiler(SyntaxTree.Parse(seed), opt);
        session.Render();
        int at = seed.IndexOf("a1@cross", System.StringComparison.Ordinal);
        string incremental = session.Edit(new TextChange(new TextSpan(at, "a1@cross".Length), "a1")).Replace("\r\n", "\n");
        string full = LilySharp.Core.Svg.SvgGenerator.Generate(SyntaxTree.Parse(Text("a1")), opt).Replace("\r\n", "\n");
        Assert.Equal(full, incremental);
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
    [InlineData("b'8@cross c'8 r4 r2 | c1", "c''8@cross c'8 r4 r2 | c1")] // a second, then a unison, with c''1
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

    /// <summary>The other staff's beam reads the notes moved under it (<see cref="Score.CrossedIn"/>):
    /// an edit of the moved note re-lays a beam on the staff it was not written on.</summary>
    [Theory]
    [InlineData("c8 a@cross c a@cross r2", "c8 b@cross c b@cross r2")]
    [InlineData("c8 a@cross c a@cross r2", "c8 a c a r2")]
    public void AnEditOfAMovedNoteReLaysTheBeamItStandsUnder(string before, string after)
    {
        var opt = new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false };
        string bars = string.Concat(Enumerable.Repeat("c8 e g c' e g c' e | ", 16));
        string Text(string lh) => Book(
            "e'8@stemDown f'@stemDown e'@stemDown f'@stemDown r2 | " + string.Concat(Enumerable.Repeat("c''1 | ", 17)),
            lh + " | " + bars + "c1 |");
        string seed = Text(before);
        var session = new LilySharp.Core.Svg.IncrementalCompiler(SyntaxTree.Parse(seed), opt);
        session.Render();
        int at = seed.IndexOf(before, System.StringComparison.Ordinal);
        string incremental = session.Edit(new TextChange(new TextSpan(at, before.Length), after)).Replace("\r\n", "\n");
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
