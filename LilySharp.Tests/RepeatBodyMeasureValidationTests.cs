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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The bars a percent mark repeats validate in the frame they are written in — the running
/// default note value and the running meter — not as a standalone block in a fresh
/// quarter-note 4/4 frame; a bar of them that is not a whole bar is LYS1049.
/// </summary>
/// <remarks>
/// The collector's <c>ProcessRepeatExpression</c> walks the body with the running
/// <c>_defaultDuration</c>: bare notes inherit the note value from BEFORE the repeat
/// and across its turns, and the note after the repeat inherits from inside it. The
/// validator used to recurse into the body as a standalone <c>MusicBlock</c>, so
/// <c>c8 … | a a … | % | % | % |</c> counted the bare a's as quarters and
/// flagged duration-2 bars the renderer fills exactly (reported 2026-08-13,
/// scratch/ベースタブLy/1stbarline.lys — the render was correct, only the diagnostic
/// was wrong).
/// </remarks>
[Trait("Category", "Unit")]
public sealed class RepeatBodyMeasureValidationTests
{
    private static IReadOnlyList<Diagnostic> Diagnose(string music)
    {
        var tree = SyntaxTree.Parse(
            $"part mel {{\n  section A {{ {music} }}\n}}\nform {{ A }}\nscore {{ staff mel }}\n");
        var validator = new MeasureValidator();
        validator.Validate(tree);
        return validator.Diagnostics;
    }

    private static void AssertClean(string music) =>
        Assert.DoesNotContain(Diagnose(music), d =>
            d.Code == DiagnosticCodes.MeasureIncomplete
            || d.Code is DiagnosticCodes.MeasureOverflow or DiagnosticCodes.MissingBarLine
            || d.Code == DiagnosticCodes.PickupWithoutPartial);

    [Fact]
    public void BareNotesInAPercentBodyInheritTheEighthFromBeforeTheRepeat() =>
        // The reported file's shape: every bar is exactly full when the a's and d's
        // inherit c8's eighth — the old fresh-quarter frame said "duration 2".
        AssertClean("c8 c c c c c c c | " +
            "a a a a a a a a | d d d d e e e e | %2 | %2 | %2 | time 2/4 d,,16 c8 a16~ a d, a8 |");

    [Fact]
    public void BareNotesInAVoltaBodyInheritTheSameWay() =>
        AssertClean("c8 c c c c c c c | repeat volta 2 { d d d d d d d d } |");

    [Fact]
    public void BareNotesAfterTheRepeatInheritTheBodysExitValue() =>
        // The collector's default threads THROUGH the walked body: the e's inherit
        // the body's eighth, not the quarter from before the repeat.
        AssertClean("c4 c c c | d8 d d d d d d d | % | e e e e e e e e |");

    [Fact]
    public void TheBodyValidatesAgainstTheMeterRunningAtTheRepeat() =>
        // A mid-block time 2/4 precedes the repeat: its two-quarter body is a FULL
        // bar there (the old standalone frame checked it against the header 4/4).
        AssertClean("c4 c c c | time 2/4 c4 c | d4 d | % |");

    /// <summary>
    /// A <c>time</c> written AFTER a repeat body, inside the same written bar, governs the
    /// music after it — not the body. The body closes its own rendered bars, so the enclosing
    /// written bar legitimately reads <c>r1 | % | % | % | % | % | % | % | % | time 1/4 r4 |</c> (the tab
    /// corpus's I Love You, 2026-09-04): the collector walks it in order and draws the r1's
    /// in 4/4 and the r4 in 1/4, while the validator adopted the bar's last meter up front
    /// and reported the body's r1 as "1 exceeds 1/4". The two spellings — with and without a
    /// bar line between <c>}</c> and <c>time</c> — must both be clean, as they render alike.
    /// </summary>
    [Fact]
    public void AMeterChangeAfterTheBodyDoesNotReachBackIntoIt()
    {
        AssertClean("c1 | r1 | % | time 1/4 r4 | time 4/4 d1 | e1 |");
        AssertClean("c1 | r1 | % | time 1/4 r4 | time 4/4 d1 | e1 |");
    }

    /// <summary>The control: a meter change BEFORE the body still governs it (the body's
    /// r1 is overfull in 1/4), and one after it still governs the bar's own remainder (a
    /// half rest in 1/4 is overfull).</summary>
    [Fact]
    public void AMeterChangeStillGovernsWhatFollowsIt()
    {
        Assert.Contains(Diagnose("c1 | time 1/4 r1 | % | time 4/4 d1 |"),   // r1 is four bars of 1/4
            d => d.Code == DiagnosticCodes.PercentBodyNotWholeBars && d.Message.Contains("1/4"));
        Assert.Contains(Diagnose("c1 | r1 | % | time 1/4 r2 | time 4/4 d1 |"),   // r2 is two bars of 1/4
            d => d.Code == DiagnosticCodes.MissingBarLine && d.Message.Contains("1/2 is about 2 bars of 1/4"));
    }

    [Fact]
    public void AGenuinelyOverfullBodyBarStillWarns()
    {
        // Nine inherited eighths in 4/4 — a real overflow inside the body.
        var diags = Diagnose("c8 c c c c c c c | a a a a a a a a a | % |");
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.PercentBodyNotWholeBars);
    }

    [Fact]
    public void AGenuinelyShortInteriorBodyBarStillWarns()
    {
        // The body's MIDDLE bar is short — a real defect the frame must not swallow.
        var diags = Diagnose(
            "c8 c c c c c c c | a a a a a a a a | d d d d | e e e e e e e e | %3 |");
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.PercentBodyNotWholeBars && d.Message.Contains("1/2"));
    }

    /// <summary>
    /// The control: with ordinary music after the open bar instead of a repeat, the address
    /// was already right and stays right. The enclosing bar counts the following note
    /// itself there, so nothing is scoped and no lead-in span is in play.
    /// </summary>
    [Fact]
    public void TheSameOverflowWithoutARepeatKeepsItsAddress()
    {
        const string music = "c1 | r1 r1 r1 c1 | c1 |";
        var over = Assert.Single(Diagnose(music), d => d.Code == DiagnosticCodes.MissingBarLine);
        string source = $"part mel {{\n  section A {{ {music} }}\n}}\n";
        Assert.Equal(source.IndexOf("r1", System.StringComparison.Ordinal), over.Span.Start);
    }
}
