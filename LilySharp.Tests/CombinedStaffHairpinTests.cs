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

    private static string Tips(MultiStaffScore score)
        => string.Join(" | ", ScoreSideTables.HairpinsByStaff(score).At(0)
            .OrderBy(h => h.StartMeasureIndex).ThenBy(h => h.StartItemIndex)
            .Select(h => (h.AdjacentAtStart ? ">" : "") + h.Direction + (h.AdjacentAtEnd ? ">" : "")
                + (h.MetInAnotherVoice ? " (met in another voice)" : "")));

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
    /// solo): a start goes with its rest, as a level there does; an end ends its hairpin where
    /// it was written — dropped, the hairpin would run on to the next level.</summary>
    [Theory]
    [InlineData("c''2 d''2 | c''4 d'' e'' f'' | c''1 |", "e'4 f' e'@cresc f' | R1@!cresc | e'1@f |",
        "v1 0.2 note@1/2 .. v0 1.0 note@0")]
    [InlineData("c''2 d''2 | c''4 d'' e'' f'' | c''1 |", "e'4 f' e' f' | r2@cresc r2 | e'1@f |", "")]
    public void AMarkOnARestNobodyEngraves(string fl1, string fl2, string expected)
        => Assert.Equal(expected, Hairpins(Collect(fl1, fl2)));

    /// <summary>
    /// An END on such a rest stands on what the staff engraves at its moment (session 1323):
    /// the hairpin stops on the beat it was written on. Until then it kept the part's own
    /// numbers, which name nothing on the staff, and the hairpin ran to the bar's end (Lab
    /// sessions/p1323/probe n2, n5, n6 — out-base against out).
    /// </summary>
    [Theory]
    // The soloist has a note on that beat.
    [InlineData("c''2 d''2 | c''4 d'' e'' f'' | c''1 |", "e'4 f' e'@cresc f' | r2 r2@!cresc | e'1@f |",
        "v1 0.2 note@1/2 .. v0 1.2 note@1/2")]
    // …and the hairpin's own voice has a level further on: the part's stop comes first.
    [InlineData("c''2 d''2 | c''4 d'' e'' f'' | c''2 d''2 | c''2 d''2 |", "e'4 f' e'@cresc f' | r2 r2@!cresc | e'4 f' e' f' | e'4@f f' e' f' |",
        "v1 0.2 note@1/2 .. v0 1.2 note@1/2")]
    // A grace note before the soloist's note on that beat: the note, not the grace.
    [InlineData("c''2 d''2 | c''4 d'' grace { d''8 } e''4 f'' | c''1 |", "e'4 f' e'@cresc f' | r2 r2@!cresc | e'1@f |",
        "v1 0.2 note@1/2 .. v0 1.3 note@1/2")]
    // The soloist holds a note through it: nothing is engraved on that beat, and the stop goes
    // inside that note, keeping its beat (session 1327; until then it stayed where it was
    // collected — the bar's end).
    [InlineData("c''2 d''2 | c''1 | c''1 |", "e'4 f' e'@cresc f' | r2 r2@!cresc | e'1@f |",
        "v1 0.2 note@1/2 .. v0 1.0 note@0")]
    // …and its start still goes with its note (a bar part two plays alone), the stop still its
    // own, before the level its start's voice has further on.
    [InlineData("R1 | c''1 | c''1 |", "g'4 g'@cresc g' g' | r2 r2@!cresc | e'1@f |",
        "v0 0.1 note@1/4 .. v0 1.0 note@0")]
    public void AnEndOnARestNobodyEngraves_StopsOnItsBeat(string fl1, string fl2, string expected)
        => Assert.Equal(expected, Hairpins(Collect(fl1, fl2)));

    /// <summary>
    /// A hairpin's stop on a rest under the other part's long note ends on the beat it is
    /// written on: that beat's share of the way from the long note's column to the next column,
    /// or to the bar's end (owner's decision 2026-11-19, HairpinItem.EndWithin). Until session
    /// 1327 it ran to the bar's end (Lab sessions/p1327/probe n6, n10 — out-base against out).
    /// LilyPond draws no hairpin there (sessions/p1324/lp n6, n10).
    /// </summary>
    [Theory]
    // On the third beat of a whole note: half way from its column to the bar's end.
    [InlineData("c''2 d''2 | c''1 | c''1 |", "e'4 f' e'@cresc f' | r2 r2@!cresc | e'1@f |", 1, 2)]
    [InlineData("R1 | c''1 | c''1 |", "g'4 g'@cresc g' g' | r2 r2@!cresc | e'1@f |", 1, 2)]
    // On the second beat of a half note: half way from its column to the next note's.
    [InlineData("c''2 d''2 | c''2 d''2 | c''1 |", "e'4 f' e'@cresc f' | r4 r4@!cresc r2 | e'1@f |", 1, 2)]
    public void AnEndOnARestUnderTheOtherPartsLongNote_StopsOnItsBeat(string fl1, string fl2, int share, int of)
    {
        var score = Collect(fl1, fl2);
        var layout = new LayoutEngine(score.Paper).Layout(score);
        var hairpin = Assert.Single(layout.HairpinLayouts);
        var item = Assert.Single(ScoreSideTables.HairpinsByStaff(score).At(0));
        Assert.NotNull(item.EndWithin);
        var bar = layout.Systems.SelectMany(s => s.Measures).First(m => m.MeasureIndex == item.EndMeasureIndex);
        var notes = score.StaffGroups[0].Staves[0].Voices[item.EndVoiceIndex].Measures[item.EndMeasureIndex];
        double left = bar.X + LayoutUtilities.GetItemXOffset(score.StaffGroups[0].Staves[0].Voices[item.EndVoiceIndex].Measures,
            item.EndMeasureIndex, item.EndItemIndex, bar);
        double right = item.EndItemIndex + 1 < notes.Items.Length
            ? bar.X + LayoutUtilities.GetItemXOffset(score.StaffGroups[0].Staves[0].Voices[item.EndVoiceIndex].Measures,
                item.EndMeasureIndex, item.EndItemIndex + 1, bar)
            : bar.X + bar.Width - 1.0; // a bound-padding short of the bar line (HairpinEngraver.BoundPadding)
        Assert.Equal(left + ((right - left) * share / of), hairpin.EndX, 6);
        Assert.True(hairpin.EndX < bar.X + bar.Width - 1.0, $"{hairpin.EndX} against the bar's end {bar.X + bar.Width}");
    }

    /// <summary>The same for a text spanner's end and a pedal's release; an ottava's end
    /// includes the rest it is written on, and stays past it.</summary>
    [Theory]
    [InlineData("c''2 d''2 | c''4 d'' e'' f'' | c''1 |", "e'4 f' e'@rit f' | r2 r2@!rit | e'1 |",
        "TextSpanStart v1 0.2 note@1/2 | TextSpanStop v0 1.2 note@1/2")]
    [InlineData("c''2 d''2 | c''4 d'' e'' f'' | c''1 |", "e'4 f' e'@sustain f' | r2 r2@!sustain | e'1 |",
        "SustainOn v1 0.2 note@1/2 | SustainOff v0 1.2 note@1/2")]
    [InlineData("c''2 d''2 | c''4 d'' e'' f'' | c''1 |", "e'4 f' e'@8va f' | r2 r2@!8va | e'1 |",
        "OttavaUp v1 0.2 note@1/2 | OttavaStop v1 1.2 end@0")]
    // Under the soloist's long note a text spanner's end stays where it was collected (the
    // stop inside the note is a hairpin's alone, session 1327).
    [InlineData("c''2 d''2 | c''1 | c''1 |", "e'4 f' e'@rit f' | r2 r2@!rit | e'1 |",
        "TextSpanStart v1 0.2 note@1/2 | TextSpanStop v1 1.1 end@0")]
    public void AnotherLinesEndOnARestNobodyEngraves_StopsOnItsBeat(string fl1, string fl2, string expected)
        => Assert.Equal(expected, Marks(Collect(fl1, fl2)));

    /// <summary>
    /// A hairpin's stop is its PART's: where the part changed streams since the start, the stop
    /// is still that hairpin's own, before a level its start's voice has further on.
    /// </summary>
    [Theory]
    [InlineData("c''2 d''2 | R1 | c''2 d''2 | c''2 d''2 |", "e'4 f'@cresc e' f' | g'4 g' g'@!cresc g' | e'4 f' e' f' | e'4@f f' e' f' |",
        "v1 0.1 note@1/4 .. v0 1.2 note@1/2")]
    public void AHairpinsStopAfterItsPartChangedStreams_IsItsOwn(string fl1, string fl2, string expected)
        => Assert.Equal(expected, Hairpins(Collect(fl1, fl2)));

    /// <summary>
    /// A hairpin ends at the EARLIEST end of its own voice, though a voice carrying part two's
    /// first half bar and part one's second lists part one's marks first (session 1328,
    /// HairpinEngraver.FirstOf). Until then part two's crescendo ran past its own decrescendo
    /// on the bar's first beat to part one's on the third (Lab sessions/p1328/probe f4).
    /// </summary>
    [Theory]
    // Part two alone, then part one alone, in one bar of one voice.
    [InlineData("R1 | r2 c''2@decresc | c''1@p |", "e'4 f'@cresc e' f' | e'4@decresc f' r2 | e'1@p |",
        "v0 0.1 note@1/4 .. v0 1.0 note@0")]
    // The other way round (control: part one is listed first and heard first).
    [InlineData("R1 | c''2@decresc r2 | c''1@p |", "e'4 f'@cresc e' f' | r2 e'4@decresc f' | e'1@p |",
        "v0 0.1 note@1/4 .. v0 1.0 note@0")]
    public void AHairpin_EndsAtItsVoicesEarliestEnd_WhicheverPartIsListedFirst(string fl1, string fl2, string expected)
        => Assert.Equal(expected, Hairpins(Collect(fl1, fl2)).Split(" | ")[0]);

    /// <summary>
    /// In a unison part two's notes are engraved by nobody, and its hairpin goes with them —
    /// BOTH ends. Until session 1323 the stop was left behind in a voice of its own, where it
    /// ended that voice's earlier hairpin at its bar's end: across the unison's own hairpin (Lab
    /// sessions/p1323/probe s2, s3).
    /// </summary>
    [Theory]
    // Part two's open crescendo ends where the unison's begins.
    [InlineData("c''2 d''2 | c'4 d'@cresc e' f'@!cresc | g'1 |", "e'4 f' e'@cresc f' | c'4 d'@cresc e' f'@!cresc | g'1 |",
        "v1 0.2 note@1/2 .. v0 1.1 note@1/4 | v0 1.1 note@1/4 .. v0 1.3 note@3/4")]
    [InlineData("c''2 d''2 | c'4 d'@decresc e' f'@!decresc | g'2 a'2 |", "e'4 f'@cresc e' f' | c'4 d'@decresc e' f'@!decresc | e'2 f'2@!cresc |",
        "v1 0.1 note@1/4 .. v0 1.1 note@1/4 | v0 1.1 note@1/4 .. v0 1.3 note@3/4")]
    public void AUnisonsSecondStop_GoesWithItsStart(string fl1, string fl2, string expected)
        => Assert.Equal(expected, Hairpins(Collect(fl1, fl2)));

    /// <summary>
    /// A part's hairpin and that part's next one are back to back, wherever the combiner
    /// engraves the part by then (session 1324; LilyPond 2.26.0 draws them tip to tip, Lab
    /// sessions/p1324/lp s2, s3). Until then the earlier one was "met in another voice" and
    /// ended on the far edge of the note the next begins on, overlapping it by a note head.
    /// </summary>
    [Theory]
    // The next is the unison's: part two's own start is dropped, part one's stands for it.
    [InlineData("c''2 d''2 | c'4 d'@cresc e' f'@!cresc | g'1 |", "e'4 f' e'@cresc f' | c'4 d'@cresc e' f'@!cresc | g'1 |",
        "Crescendo> | >Crescendo")]
    [InlineData("c''2 d''2 | c'4 d'@decresc e' f'@!decresc | g'2 a'2 |", "e'4 f'@cresc e' f' | c'4 d'@decresc e' f'@!decresc | e'2 f'2@!cresc |",
        "Crescendo> | >Decrescendo")]
    // The next is in a bar part two plays alone: its own start, in the staff's first voice.
    [InlineData("c''2 d''2 | R1 | c''1 |", "e'4 f'@cresc e' f' | g'4 g'@decresc g' g'@!decresc | e'1 |",
        "Crescendo> | >Decrescendo")]
    // …and the other way: begun in a bar part two plays alone, followed where the parts play apart.
    [InlineData("R1 | c''2 d''2 | c''1 |", "g'4 g'@cresc g' g' | e'4 f'@decresc e' f'@!decresc | e'1 |",
        "Crescendo> | >Decrescendo")]
    // Part ONE's hairpin under part two's open one is another part's: met in another voice.
    [InlineData("c''2 d''2 | c''4 d''@cresc e'' f''@!cresc | c''1 |", "e'4 f'@cresc e' f' | e'1 | e'1 |",
        "Crescendo (met in another voice) | Crescendo")]
    // The unison's hairpin stands for part two's too: part two's next follows it, part one ending
    // it nowhere (owner's decision 2026-11-19; Lab sessions/p1327/probe h4)…
    [InlineData("c'4 d'@cresc e' f' | c''2 d''2 | c''1 |", "c'4 d'@cresc e' f' | e'4 f'@decresc e' f'@!decresc | e'1 |",
        "Crescendo> | >Decrescendo")]
    // …but where part one ends it itself, it runs to that end, as before (h4f)…
    [InlineData("c'4 d'@cresc e' f' | c''2 d''2@f | c''1 |", "c'4 d'@cresc e' f' | e'4 f'@decresc e' f'@!decresc | e'1 |",
        "Crescendo | Decrescendo")]
    // …a hairpin of its own being such an end too (its fourth beat is one chord of both parts,
    // where part two's stop meets it)…
    [InlineData("c'4 d'@cresc e' f' | c''2. d''4@decresc | c''1@p |", "c'4 d'@cresc e' f' | e'4 f' e'@decresc f'@!decresc | e'1 |",
        "Crescendo> | >Decrescendo | Decrescendo>")]
    // …also a bar later, past part two's next…
    [InlineData("c'4 d'@cresc e' f' | c''1 | c''2 d''2@decresc | c''1 |", "c'4 d'@cresc e' f' | e'4 f'@decresc e' f'@!decresc | e'1 | e'1 |",
        "Crescendo> | Decrescendo | >Decrescendo")]
    // …and a level part one wrote BEFORE the unison's start none.
    [InlineData("c'4@p d'@cresc e' f' | c''2 d''2 | c''1 |", "c'4@p d'@cresc e' f' | e'4 f'@decresc e' f'@!decresc | e'1 |",
        "Crescendo> | >Decrescendo")]
    public void APartsNextHairpinInAnotherVoice_IsAdjacent(string fl1, string fl2, string expected)
        => Assert.Equal(expected, Tips(Collect(fl1, fl2)));

    /// <summary>
    /// …and the two ride ONE line, at one height (DynamicAlignEngraver.RunsOfLines, session
    /// 1325): each in its own voice's line they met tip to tip at two heights, as much as a
    /// staff space apart (Lab sessions/p1325/probe h1 to h3). The heights are the wedges'
    /// centres, left to right, then the levels'.
    /// </summary>
    [Theory]
    // Part two's crescendo where the parts play apart, its next in the unison.
    [InlineData("c''2 d''2 | c'4 d'@cresc e' f'@!cresc | g'1 |", "e'4 f' e'@cresc f' | c'4 d'@cresc e' f'@!cresc | g'1 |",
        "-7.33 -7.33")]
    // The next begins at the unison's bar's head.
    [InlineData("c''2 d''2 | c'4@cresc d' e' f'@!cresc | g'1 |", "e'4 f' e'@cresc f' | c'4@cresc d' e' f'@!cresc | g'1 |",
        "-7.39 -7.39")]
    // The level that closes the second is on the line too (the p before the first is not).
    [InlineData("c''2 d''2 | c'4 d'@decresc e' f' | g'1@p |", "e'4@p f' e'@cresc f' | c'4 d'@decresc e' f' | g'1@p |",
        "-7.33 -7.33 / p-6.39 p-5.93")]
    // Begun in a bar part two plays alone, followed where the parts play apart.
    [InlineData("R1 | c''2 d''2 | c''1 |", "g'4 g'@cresc g' g' | e'4 f'@decresc e' f'@!decresc | e'1 |",
        "-7.77 -7.77")]
    // The unison's hairpin and part two's next (part one ending it nowhere).
    [InlineData("c'4 d'@cresc e' f' | c''2 d''2 | c''1 |", "c'4 d'@cresc e' f' | e'4 f'@decresc e' f'@!decresc | e'1 |",
        "-7.77 -7.77")]
    // Another part's hairpin under part two's open one is another line.
    [InlineData("c''2 d''2 | c''4 d''@cresc e'' f''@!cresc | c''1 |", "e'4 f'@cresc e' f' | e'1 | e'1 |",
        "-7.46 -5.37")]
    // The next written on the other side of the staff is another line too.
    [InlineData("c''2 d''2 | c'4 d'@cresc.up e' f'@!cresc | g'1 |", "e'4 f' e'@cresc f' | c'4 d'@cresc.up e' f'@!cresc | g'1 |",
        "-7.33 1.37")]
    public void APartsHairpinsInTwoVoices_RideOneLine(string fl1, string fl2, string expected)
    {
        var score = Collect(fl1, fl2);
        var layout = new LayoutEngine(score.Paper).Layout(score);
        string wedges = string.Join(" ", layout.HairpinLayouts.OrderBy(h => h.StartX).Select(h => $"{h.YUp:F2}"));
        string levels = string.Join(" ", layout.DynamicLayouts.OrderBy(d => d.X).Select(d => $"{d.Text}{d.YUp:F2}"));
        Assert.Equal(expected, levels.Length > 0 ? $"{wedges} / {levels}" : wedges);
    }

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

    /// <summary>A text spanner and an ottava bracket are paired within one voice, so their two
    /// ends go as a pair: left where they were collected when the part changes streams between
    /// them (moved one by one they would name two voices, and no line is drawn), and dropped
    /// together when the start's note is engraved by nobody — part two's in a unison.</summary>
    [Theory]
    [InlineData("c''2 d''2 | R1 | c''1 |", "e'4 f'@rit e' f' | g'4 g' g' g'@!rit | e'1 |", "TextSpanStart:v1:0.1 TextSpanStop:v0:1.3")]
    [InlineData("c''2 d''2 | R1 | c''1 |", "e'4 f'@8va e' f' | g'4 g' g' g'@!8va | e'1 |", "OttavaUp:v1:0.1 OttavaStop:v0:1.4")]
    [InlineData("c'4 d'@rit e' f'@!rit | g'1 |", "c'4 d'@rit e' f'@!rit | g'1 |", "TextSpanStart:v0:0.1 TextSpanStop:v0:0.3")]
    [InlineData("c'4 d'@8va e' f'@!8va | g'1 |", "c'4 d'@8va e' f'@!8va | g'1 |", "OttavaUp:v0:0.1 OttavaStop:v0:0.4")]
    public void ALinePairedInOneVoice_GoesAsAPair(string fl1, string fl2, string expected)
        => Assert.Equal(expected, string.Join(" ", Collect(fl1, fl2).MusicMarks.Where(m => m.AnchorItemIndex >= 0)
            .Select(m => $"{m.Type}:v{m.VoiceIndex}:{m.MeasureIndex}.{m.AnchorItemIndex}")));

    /// <summary>A text spanner or an ottava whose part changes streams between its two ends is
    /// one line, from the note its start is written on to the note its end is: the end is paired
    /// in the start's voice (MusicMarkItem.LineVoice) and read in its own.</summary>
    [Theory]
    // Apart, then alone.
    [InlineData("c''2 d''2 | R1 | c''1 |", "e'4 f'@rit e' f' | g'4 g' g' g'@!rit | e'1 |", "rit", "20.37..39.61")]
    [InlineData("c''2 d''2 | R1 | c''1 |", "e'4 f'@8va e' f' | g'4 g' g' g'@!8va | e'1 |", "8va", "19.07..40.88")]
    // Alone, then apart.
    [InlineData("c''2 d''2 | R1 | c''2 d''2 |", "e'4 f' e' f' | g'4 g'@rit g' g' | e'4 f' e'@!rit f' |", "rit", "33.45..49.90")]
    [InlineData("c''2 d''2 | R1 | c''2 d''2 |", "e'4 f' e' f' | g'4 g'@8va g' g' | e'4 f' e'@!8va f' |", "8va", "32.15..51.10")]
    public void ALineWhosePartChangesStreams_IsOneLine(string fl1, string fl2, string line, string expected)
    {
        var score = Collect(fl1, fl2);
        var layout = new LayoutEngine(score.Paper).Layout(score);
        string said = line == "rit"
            ? string.Join(" ", layout.TextSpannerLayouts.Select(t => $"{t.StartX:F2}..{t.EndX:F2}"))
            : string.Join(" ", layout.OttavaBracketLayouts.Select(t => $"{t.StartX:F2}..{t.EndX:F2}"));
        Assert.Equal(expected, said);
    }

    /// <summary>…and the room the row above the staff keeps for that line
    /// (TextSpannerEngraver.InkAboveStaff, entered before the systems exist) ends where the
    /// drawn line does, not at the bar's end the start's voice would give.</summary>
    [Theory]
    [InlineData("c''2 d''2 | R1 | c''1 |", "e'4 f'@rit e' f' | g'4 g' g' g'@!rit | e'1 |")]
    [InlineData("c''2 d''2 | R1 | c''2 d''2 |", "e'4 f' e' f' | g'4 g'@rit g' g' | e'4 f' e'@!rit f' |")]
    public void TheRoomOfALineWhosePartChangesStreams_EndsWhereTheLineDoes(string fl1, string fl2)
    {
        var score = Collect(fl1, fl2);
        var layout = new LayoutEngine(score.Paper).Layout(score);
        var drawn = Assert.Single(layout.TextSpannerLayouts);
        var system = Assert.Single(layout.Systems);
        var ink = TextSpannerEngraver.InkAboveStaff(score.TextMetrics, ScoreSideTables.TextSpannersByStaff(score).At(0),
            system.Measures, new VerticalSkyline(VerticalDirection.Up), score.StaffGroups[0].Staves[0].Voices);
        Assert.False(ink.IsEmpty);
        double right = ink.Buildings.Where(b => !double.IsNegativeInfinity(ink.Height((b.Start + b.End) / 2))).Max(b => b.End);
        Assert.True(System.Math.Abs(right - drawn.EndX) < 0.3, $"ink to {right:F2}, the line to {drawn.EndX:F2}");
    }

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
