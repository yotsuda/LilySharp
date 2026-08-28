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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// LYS2014: a <c>repeat percent</c> body of three or more WHOLE measures earns a sign that
/// says something other than what the music does — <c>%</c> means "repeat the previous
/// measure", so four signs read as the body's last bar four times over.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ WHAT MAKES THIS WARNING SAFE IS THAT IT FIRES EXACTLY WHERE THE COLLECTOR SIGNS PER
/// MEASURE, and the two reach that conclusion by different routes: the collector reads the
/// body's length off <c>MeasureBuilder</c>, this side walks the body itself. The shared piece
/// is <c>PercentRepeatShape</c>, the RULE. The MEASUREMENTS are cross-checked by corpus sweep
/// — 2026-08-29, 899 books: 30 books carry a whole-measure run by the collector's census, and
/// the warning lands on exactly those 30, no book missed and none spurious.
/// </para>
/// <para>
/// ★ THE SWEEP EARNED ITS KEEP TWICE on the way there. The first draft warned on 6 of the 30,
/// because it borrowed the flow accounting's gate and a body holding a <c>break</c> or a tie
/// fails it — both occupy no time, so neither changes a length, and of the corpus's 472
/// percent bodies every single one of the 114 that fail the strict gate fails on exactly
/// those two. The second draft reached 28, because this layer's lead-in is a running sum over
/// a WRITTEN bar and can exceed the meter when the music leaves bar lines out
/// (<c>r1 r1 r1 r1 break repeat percent 2 { … }</c>, a real book), where the collector's
/// equivalent is always a position inside the open bar.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class PercentBodyLengthValidatorTests
{
    /// <summary>
    /// The music, in the shape a book writes it. ⚠️ A BARE NOTE STREAM IS NOT ENOUGH: the
    /// per-block measure pass runs over a part's music block, so a top-level fragment reaches
    /// none of these checks and every one of these tests passed vacuously against it.
    /// </summary>
    private static string Book(string music)
        => "part m { }\nsection A { m { " + music + " } }\n"
         + "form main { ~A }\nscore main { staff m }\n";

    private static Diagnostic[] All(string music)
        => SemanticValidation.Run(SyntaxTree.Parse(Book(music))).ToArray();

    private static Diagnostic[] Warnings(string music)
        => All(music).Where(d => d.Code == DiagnosticCodes.PercentBodyTooLong).ToArray();

    [Fact]
    public void ThreeWholeMeasures_AreWarned()
    {
        var d = Assert.Single(Warnings("repeat percent 2 { c1 | d1 | e1 | }"));
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("3 measures long", d.Message);
        // The message says what is wrong with the PAGE and that the sound is fine, because
        // that is the whole content of the report: nothing is broken, one thing is unsayable.
        Assert.Contains("repeat the previous measure", d.Message);
        Assert.Contains("Playback is unaffected", d.Message);
    }

    /// <summary>
    /// The count in the message is the body's, not the repetition's — a reader checking the
    /// warning against the page counts bars in the braces.
    /// </summary>
    [Theory]
    [InlineData("repeat percent 2 { c1 | d1 | e1 | }", "3 measures")]
    [InlineData("repeat percent 2 { c1 | d1 | e1 | f1 | }", "4 measures")]
    [InlineData("repeat percent 5 { c1 | d1 | e1 | f1 | }", "4 measures")]
    public void TheMessageNamesTheBodysMeasureCount(string src, string expected)
        => Assert.Contains(expected, Assert.Single(Warnings(src)).Message);

    /// <summary>
    /// The three shapes that HAVE an exact sign are silent — one measure (the single
    /// percent), two (the double), and shorter than a measure (beat slashes). A warning on
    /// any of them would be a false alarm on a picture that is already right.
    /// </summary>
    [Theory]
    [InlineData("repeat percent 4 { c4 d e f }")]              // one measure
    [InlineData("repeat percent 4 { c4 d e f | }")]            // one measure, closed
    [InlineData("repeat percent 4 { c1 | d1 | }")]             // two measures
    [InlineData("repeat percent 4 { c16 d e f }")]             // beat slash
    [InlineData("repeat percent 4 { g8. c16 }")]               // beat slash, mixed durations
    public void ShapesWithAnExactSign_AreSilent(string src)
        => Assert.Empty(Warnings(src));

    /// <summary>
    /// A body longer than a measure but not a whole number of them is NOT warned: its bars
    /// are already drawing underfull or overfull warnings, and those name the real mistake.
    /// Two books in the corpus are this shape and both already carry bar diagnostics.
    /// </summary>
    /// <remarks>
    /// ⚠️ IT IS THE MISSING TRAILING BAR LINE THAT MAKES IT RAGGED, and the first draft of
    /// this test got that backwards. <c>{ c1 | d1 | e2 | }</c> is THREE measures, not two and
    /// a half: a written bar line closes the bar it reaches however short that bar is, so the
    /// half-note bar is a bar, Lily# prints three percent signs for it, and the warning is
    /// right to fire. Both the collector and this validator agree, which is what the shared
    /// rule buys. Drop the closing bar line and the last half-measure stays open — THAT is
    /// the shape with no whole number of measures in it.
    /// </remarks>
    [Fact]
    public void ARaggedBody_IsLeftToTheBarDiagnostics()
    {
        Assert.Empty(Warnings("repeat percent 2 { c1 | d1 | e2 }"));
        // …and the bar checks did speak, so the writer is not left with nothing.
        Assert.NotEmpty(All("repeat percent 2 { c1 | d1 | e2 }"));
    }

    /// <summary>
    /// The counterpart to the remark above: a short bar CLOSED by a bar line is still a bar,
    /// so a body of three of them is a whole-measure run and is warned about — the same
    /// answer the collector gives when it signs each of those three measures.
    /// </summary>
    [Fact]
    public void AShortBarClosedByABarLine_StillCountsAsAMeasure()
        => Assert.Contains("3 measures",
            Assert.Single(Warnings("repeat percent 2 { c1 | d1 | e2 | }")).Message);

    /// <summary>
    /// Only <c>percent</c>. <c>unfold</c> prints the music out in full and <c>volta</c> draws
    /// brackets — neither substitutes a sign for a bar, so neither can misstate one.
    /// </summary>
    [Theory]
    [InlineData("repeat unfold 2 { c1 | d1 | e1 | }")]
    [InlineData("repeat volta 2 { c1 | d1 | e1 | }")]
    public void OtherRepeatKinds_AreSilent(string src)
        => Assert.Empty(Warnings(src));

    /// <summary>
    /// A zero-duration mark in the body does not change its length, and must not silence the
    /// warning. This is the first of the two corrections the corpus sweep forced: the strict
    /// flow-accounting gate refuses a body holding a <c>break</c> or a tie, and borrowing it
    /// found 6 books where the collector's census says 30.
    /// </summary>
    [Theory]
    [InlineData("repeat percent 2 { c1 | d1 | e1 | break }")]
    [InlineData("repeat percent 2 { c1 | d1 | break e1 | }")]
    [InlineData("repeat percent 2 { c1~ | c1 | e1 | }")]
    public void AZeroDurationMarkInTheBody_DoesNotSilenceIt(string src)
        => Assert.Contains("3 measures", Assert.Single(Warnings(src)).Message);

    /// <summary>
    /// The second correction: when the music leaves its bar lines out, this layer's running
    /// tally arrives at the repeat holding SEVERAL bars' worth of beats, where the collector's
    /// equivalent has auto-completed and holds only the position inside the open bar. Reduce
    /// it, or the first body item closes a bar immediately and the length comes out nonsense.
    /// Both books this missed open exactly this way.
    /// </summary>
    [Fact]
    public void ALeadInLongerThanTheMeter_IsReducedIntoItsBar()
    {
        // Four whole rests with no bar lines, then a four-measure percent body.
        var d = Assert.Single(Warnings("r1 r1 r1 r1 repeat percent 2 { c1 | d1 | e1 | f1 | }"));
        Assert.Contains("4 measures", d.Message);
    }

    /// <summary>
    /// The warning marks the <c>repeat</c> itself, so the editor squiggle sits on the
    /// construct the writer would edit rather than on one of its bars.
    /// </summary>
    [Fact]
    public void TheWarningMarksTheRepeat()
    {
        const string music = "c1 | repeat percent 2 { c1 | d1 | e1 | }";
        var d = Assert.Single(Warnings(music));
        Assert.Equal(Book(music).IndexOf("repeat percent", System.StringComparison.Ordinal),
            d.Span.Start);
    }

    /// <summary>
    /// <c>time none</c> has no measures to count, so there is no whole-measure run to report.
    /// The senza-misura guard is shared with every other bar-length check here.
    /// </summary>
    [Fact]
    public void SenzaMisura_IsSilent()
        => Assert.Empty(Warnings("time none repeat percent 2 { c1 | d1 | e1 | }"));
}
