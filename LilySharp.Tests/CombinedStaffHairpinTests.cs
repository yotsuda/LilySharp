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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// On a <c>combinedStaff</c> the marks a note carries — a hairpin's two ends, a text spanner's,
/// an ottava's, a pedal's — go where the combiner put their note
/// (session 1321, CombinedStaffReaddress.MoveMarks), as the levels and the scripts always did.
/// </summary>
/// <remarks>
/// Until then a mark kept its part's own voice slot and item number. Part two's
/// <c>@p … @cresc … @f</c> in a bar it plays alone began under the p at the bar's head; a part
/// entering mid-bar began its hairpin on the other part's note; and a hairpin both parts write
/// in a unison was drawn twice (Lab sessions/p1321/probe h2, h5, h4 — out-base against out).
/// Each place below is said as voice, bar.item and the item's moment in its bar.
/// </remarks>
[Trait("Category", "Unit")]
public class CombinedStaffHairpinTests
{
    private static MultiStaffScore Collect(string fl1, string fl2)
    {
        var tree = SyntaxTree.Parse("octave absolute\ntime 4/4\npart fl1 { clef treble }\npart fl2 { clef treble }\nsection A {\n"
            + "  fl1 { " + fl1 + " }\n  fl2 { " + fl2 + " }\n}\nform { ~A }\nscore { combinedStaff { fl1 fl2 } }\n");
        Assert.DoesNotContain(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
    }

    private static string Place(MultiStaffScore score, int voice, int measure, int item)
    {
        var voices = score.StaffGroups[0].Staves[0].Voices;
        if (voice >= voices.Length || measure >= voices[voice].Measures.Length)
            return $"v{voice} {measure}.{item} (no such bar)";
        var bar = voices[voice].Measures[measure];
        string what = item >= bar.Items.Length ? "end"
            : bar.Items[item] is NoteItem or ChordItem ? "note" : bar.Items[item].GetType().Name;
        var at = Fraction.Zero;
        for (int i = 0; i < item && i < bar.Items.Length; i++)
            at += bar.Items[i].Duration;
        return $"v{voice} {measure}.{item} {what}@{at}";
    }

    private static string Hairpins(MultiStaffScore score)
        => string.Join(" | ", ScoreSideTables.HairpinsByStaff(score).At(0)
            .OrderBy(h => h.StartMeasureIndex).ThenBy(h => h.StartItemIndex)
            .Select(h => Place(score, h.VoiceIndex, h.StartMeasureIndex, h.StartItemIndex)
                + " .. " + Place(score, h.EndVoiceIndex, h.EndMeasureIndex, h.EndItemIndex)));

    private static string Marks(MultiStaffScore score)
        => string.Join(" | ", score.MusicMarks.Where(m => m.AnchorItemIndex >= 0)
            .Select(m => m.Type + " " + Place(score, m.VoiceIndex, m.MeasureIndex, m.AnchorItemIndex)));

    /// <summary>The hairpin of a part, wherever the combiner engraves that part's notes.</summary>
    [Theory]
    // Apart, part one writes: its own voice, its own numbers — right before too.
    [InlineData("c''4@p d''@cresc e'' f''@f | g''1 |", "e'2 f'2 | g'1 |",
        "v0 0.1 note@1/4 .. v0 0.3 note@3/4")]
    // Apart, part two writes.
    [InlineData("c''2 d''2 | g''1 |", "e'4@p e'@cresc e' e'@f | g'1 |",
        "v1 0.1 note@1/4 .. v1 0.3 note@3/4")]
    // Part two alone in its bar: the bar is in the staff's first voice.
    [InlineData("c''2 d''2 | R1 | c''1 |", "e'4 f' e' f' | g'4@p g'@cresc g' g'@f | e'1 |",
        "v0 1.1 note@1/4 .. v0 1.3 note@3/4")]
    // …and its end in the next bar, apart again.
    [InlineData("c''2 d''2 | R1 | c''1 |", "e'4 f' e' f' | g'4@p g'@cresc g' g' | e'1@f |",
        "v0 1.1 note@1/4 .. v0 2.0 note@0")]
    // …and its start in the bar before, apart.
    [InlineData("c''2 d''2 | R1 | c''1 |", "e'4@p f'@cresc e' f' | g'4 g' g' g'@f | e'1 |",
        "v1 0.1 note@1/4 .. v0 1.3 note@3/4")]
    // Part two enters mid-bar after part one's half note: its item numbers are not the staff's.
    [InlineData("c''2 d''2 | c''2 r2 | c''1 |", "e'4 f' e' f' | r2 g'4@cresc g'@f | e'1 |",
        "v0 1.1 note@1/2 .. v0 1.2 note@3/4")]
    // One column of chords, part two writes.
    [InlineData("c''4 e'' g'' e'' | c''1 |", "g'4@p g'@cresc g' g'@f | g'1 |",
        "v0 0.1 note@1/4 .. v0 0.3 note@3/4")]
    // A unison, both write: one hairpin.
    [InlineData("c'4@p d'@cresc e' f'@f | g'1 |", "c'4@p d'@cresc e' f'@f | g'1 |",
        "v0 0.1 note@1/4 .. v0 0.3 note@3/4")]
    // Apart, part one's own stop past part two's level: its own voice first, as on any staff.
    [InlineData("c''4@p d''@cresc e'' f''@!cresc | g''1 |", "e'2 f'2@mf | g'1 |",
        "v0 0.1 note@1/4 .. v0 0.3 note@3/4")]
    public void AHairpin_StandsOnTheNotesItWasWrittenOn(string fl1, string fl2, string expected)
        => Assert.Equal(expected, Hairpins(Collect(fl1, fl2)));

    /// <summary>A mark on a rest the combiner engraves for nobody (the partner's rest under a
    /// solo): a start goes with its rest, as a level there does; an end stays, and ends its
    /// hairpin where it was written — dropped, the hairpin would run on to the next level.</summary>
    [Theory]
    [InlineData("c''2 d''2 | c''4 d'' e'' f'' | c''1 |", "e'4 f' e'@cresc f' | R1@!cresc | e'1@f |",
        "v1 0.2 note@1/2 .. v1 1.0 end@0")]
    [InlineData("c''2 d''2 | c''4 d'' e'' f'' | c''1 |", "e'4 f' e' f' | r2@cresc r2 | e'1@f |", "")]
    public void AMarkOnARestNobodyEngraves(string fl1, string fl2, string expected)
        => Assert.Equal(expected, Hairpins(Collect(fl1, fl2)));

    /// <summary>The other lines' ends: a text spanner, an ottava (whose end stands PAST its
    /// note), a pedal — in a bar part two plays alone, and after a mid-bar entrance.</summary>
    [Theory]
    [InlineData("c''2 d''2 | R1 | c''1 |", "e'4 f' e' f' | g'4@rit g' g' g'@!rit | e'1 |",
        "TextSpanStart v0 1.0 note@0 | TextSpanStop v0 1.3 note@3/4")]
    [InlineData("c''2 d''2 | c''2 r2 | c''1 |", "e'4 f' e' f' | r2 g'4@rit g'@!rit | e'1 |",
        "TextSpanStart v0 1.1 note@1/2 | TextSpanStop v0 1.2 note@3/4")]
    [InlineData("c''2 d''2 | R1 | c''1 |", "e'4 f' e' f' | g'4@8va g' g' g'@!8va | e'1 |",
        "OttavaUp v0 1.0 note@0 | OttavaStop v0 1.4 end@1")]
    [InlineData("c''2 d''2 | c''2 r2 | c''1 |", "e'4 f' e' f' | r2 g'4@sustain g'@!sustain | e'1 |",
        "SustainOn v0 1.1 note@1/2 | SustainOff v0 1.2 note@3/4")]
    // Two lines' ends on one note are two marks.
    [InlineData("c''2 d''2 | R1 | c''1 |", "e'4 f' e' f' | g'4@cresc@rit g' g' g'@!cresc@!rit | e'1 |",
        "Cresc v0 1.0 note@0 | TextSpanStart v0 1.0 note@0 | HairpinStop v0 1.3 note@3/4 | TextSpanStop v0 1.3 note@3/4")]
    public void TheOtherLinesEnds_GoWithTheirNotes(string fl1, string fl2, string expected)
        => Assert.Equal(expected, Marks(Collect(fl1, fl2)));

    /// <summary>A hairpin both parts write in a unison is drawn once; two different ones at
    /// one place are two marks still.</summary>
    [Theory]
    [InlineData("c'4@p d'@cresc e' f'@f | g'1 |", "c'4@p d'@cresc e' f'@f | g'1 |", 1)]
    [InlineData("c'4 d'@cresc e' f'@!cresc | g'1 |", "c'4 d'@cresc e' f'@!cresc | g'1 |", 1)]
    // One column of chords: both parts' marks land on it.
    [InlineData("c''4 e''@cresc g'' e''@!cresc | c''1 |", "g'4 g'@cresc g' g'@!cresc | g'1 |", 1)]
    [InlineData("c''4 e''@cresc g'' e''@!cresc | c''1 |", "g'4 g'@decresc g' g'@!decresc | g'1 |", 2)]
    [InlineData("c''4 d''@cresc e'' f''@!cresc | g''1 |", "e'2 f'4@cresc f'@!cresc | g'1 |", 2)]
    public void TheSameHairpinOfBothParts_IsOneWedge(string fl1, string fl2, int wedges)
    {
        var score = Collect(fl1, fl2);
        Assert.Equal(wedges, new LayoutEngine(score.Paper).Layout(score).HairpinLayouts.Length);
    }

    /// <summary>The marks of a staff that is not combined are not touched, beside one that is.</summary>
    [Fact]
    public void AnotherStaffsMarks_StayWhereTheyWere()
    {
        var tree = SyntaxTree.Parse("octave absolute\ntime 4/4\npart fl1 { clef treble }\npart fl2 { clef treble }\npart ob { clef treble }\nsection A {\n"
            + "  fl1 { c''2 r2 | }\n  fl2 { r2 g'4@cresc g'@!cresc | }\n  ob { a'4 a' a'@cresc a'@!cresc | }\n}\nform { ~A }\n"
            + "score { combinedStaff { fl1 fl2 } staff ob }\n");
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var oboe = Assert.Single(ScoreSideTables.HairpinsByStaff(score).At(1));
        Assert.Equal((0, 2, 3), (oboe.VoiceIndex, oboe.StartItemIndex, oboe.EndItemIndex));
        var flutes = Assert.Single(ScoreSideTables.HairpinsByStaff(score).At(0));
        Assert.Equal((0, 1, 2), (flutes.VoiceIndex, flutes.StartItemIndex, flutes.EndItemIndex));
    }
}
