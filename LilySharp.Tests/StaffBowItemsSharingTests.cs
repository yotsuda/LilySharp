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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// One staff's slurs and ties are detected ONCE and shared by the preliminary pass and the
/// per-staff skylines (<c>MultiStaffLayouter.StaffBowItemsOf</c>, session 434), although the
/// two passes hand the layout two differently built <see cref="Score"/> objects:
/// <c>LayoutEngine.StaffSpannerScoreOf</c> carries the staff's tuplet brackets,
/// <c>MultiStaffLayouter.StaffLocalScore</c> carries none, and the surrounding score's clef,
/// time, key, tempo and titles reach them by different routes as well.
/// </summary>
/// <remarks>
/// ⚠️ THIS IS THE PREMISE THE SHARING STANDS ON, AND NOTHING ELSE PINS IT: the detectors read
/// the score's VOICES and nothing else about it — <c>VoiceScan.WalkVoiceItems</c> for the items
/// and <c>score.Voices.Length</c> for the bow's default direction. The day a detector starts
/// reading the tuplet list (or the time signature, or the clef), the two passes would detect
/// different bows from the same staff and the difference would show up as SPACING, not as a
/// wrong drawing — the preliminary pass's bows are thrown away and only their extents survive
/// (<c>RunPreliminaryAnnotationPass</c>'s remark records one such divergence that went
/// unnoticed for a session). So the test varies everything a Score carries EXCEPT the voices.
/// <para>
/// The music is chosen so that the varied field is one a detector could plausibly reach for:
/// slurs and ties that START AND END INSIDE a tuplet, in both voices of a polyphonic staff
/// (where the direction rule reads <c>Voices.Length</c>) and on a single-voice staff.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class StaffBowItemsSharingTests
{
    private static Score Collect(string body, string part = "melody")
    {
        string src = $$"""
            time 4/4
            key c major
            part melody { clef treble }
            section Main { melody { {{body}} } }
            form main { Main }
            score main "x" { staff melody }
            """;
        return new MeasureCollector().Collect(SyntaxTree.Parse(src), part);
    }

    /// <summary>The same voices, dressed as the OTHER pass dresses them: no tuplet list, and
    /// every other score-level field different.</summary>
    private static Score Redressed(Score score)
        => new(
            score.Voices,
            new TimeSignature(7, 8),
            new KeySignature(-3),
            "bass",
            tempo: 144,
            title: "another title",
            composer: "another composer");

    private static void AssertSameBows(Score a, Score b)
    {
        // ⚠️ BY VALUE, WHICH THESE RECORDS DO NOT DO THEMSELVES: SlurItem and TieItem
        // declare identity equality on purpose (ModelIdentity), and ImmutableArray's own
        // IEquatable compares the underlying array by REFERENCE — so both of the obvious
        // spellings pass only when the two answers are the same instance, which is the
        // opposite of what is claimed here. The generated ToString prints every property,
        // so comparing it compares the whole item.
        Assert.Equal(
            new SlurDetector().DetectSlurs(a).Select(s => s.ToString()).ToArray(),
            new SlurDetector().DetectSlurs(b).Select(s => s.ToString()).ToArray());
        Assert.Equal(
            new TieDetector().DetectTies(a).Select(t => t.ToString()).ToArray(),
            new TieDetector().DetectTies(b).Select(t => t.ToString()).ToArray());
    }

    [Fact]
    public void APolyphonicStaffsBows_AreTheSameOnBothPassesScores()
    {
        var score = Collect(
            "voice up { tuplet 3/2 { g8( a b) } c4~ c4 g4 | } "
            + "down { tuplet 3/2 { e8( f g) } a4~ a4 e4 | }");
        Assert.True(score.Voices.Length > 1, "the fixture must be polyphonic");
        var slurs = new SlurDetector().DetectSlurs(score);
        var ties = new TieDetector().DetectTies(score);
        Assert.Equal(2, slurs.Length);
        Assert.Equal(2, ties.Length);
        AssertSameBows(score, Redressed(score));
    }

    [Fact]
    public void ASingleVoiceStaffsBows_AreTheSameOnBothPassesScores()
    {
        var score = Collect("tuplet 3/2 { c8( d e) } f4~ f4 c4 |");
        Assert.Single(score.Voices);
        Assert.Single(new SlurDetector().DetectSlurs(score));
        Assert.Single(new TieDetector().DetectTies(score));
        AssertSameBows(score, Redressed(score));
    }

    [Fact]
    public void TheTupletsThemselves_AreWhatTheTwoScoresDisagreeAbout()
    {
        // The guard on the test above: without this, "the same bows" could be read as
        // "the two scores are the same score".
        var score = Collect("tuplet 3/2 { c8( d e) } f4~ f4 c4 |");
        Assert.NotEmpty(score.TupletBrackets);
        Assert.Empty(Redressed(score).TupletBrackets);
        Assert.NotEqual(score.TimeSignature, Redressed(score).TimeSignature);
        Assert.NotEqual(score.Clef, Redressed(score).Clef);
    }
}
