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
/// A percent/volta/unfold repeat body validates in the frame the repeat OPENS in —
/// the running default note value, the running meter, and the elapsed beats — not as
/// a standalone block in a fresh quarter-note 4/4 frame.
/// </summary>
/// <remarks>
/// The collector's <c>ProcessRepeatExpression</c> walks the body with the running
/// <c>_defaultDuration</c>: bare notes inherit the note value from BEFORE the repeat
/// and across its turns, and the note after the repeat inherits from inside it. The
/// validator used to recurse into the body as a standalone <c>MusicBlock</c>, so
/// <c>c8 … | repeat percent 4 { a a … }</c> counted the bare a's as quarters and
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
            $"part mel {{\n  section A {{ {music} }}\n}}\nform main {{ A }}\nscore main {{ staff mel }}\n");
        var validator = new MeasureValidator();
        validator.Validate(tree);
        return validator.Diagnostics;
    }

    private static void AssertClean(string music) =>
        Assert.DoesNotContain(Diagnose(music), d =>
            d.Code == DiagnosticCodes.MeasureIncomplete
            || d.Code == DiagnosticCodes.MeasureOverflow
            || d.Code == DiagnosticCodes.PickupWithoutPartial);

    [Fact]
    public void BareNotesInAPercentBodyInheritTheEighthFromBeforeTheRepeat() =>
        // The reported file's shape: every bar is exactly full when the a's and d's
        // inherit c8's eighth — the old fresh-quarter frame said "duration 2".
        AssertClean("c8 c c c c c c c | repeat percent 4 " +
            "{ a a a a a a a a | d d d d e e e e } | time 2/4 d,,16 c8 a16~ a d, a8 |");

    [Fact]
    public void BareNotesInAVoltaBodyInheritTheSameWay() =>
        AssertClean("c8 c c c c c c c | repeat volta 2 { d d d d d d d d } |");

    [Fact]
    public void BareNotesAfterTheRepeatInheritTheBodysExitValue() =>
        // The collector's default threads THROUGH the walked body: the e's inherit
        // the body's eighth, not the quarter from before the repeat.
        AssertClean("c4 c c c | repeat percent 2 { d8 d d d d d d d } | e e e e e e e e |");

    [Fact]
    public void TheBodyValidatesAgainstTheMeterRunningAtTheRepeat() =>
        // A mid-block time 2/4 precedes the repeat: its two-quarter body is a FULL
        // bar there (the old standalone frame checked it against the header 4/4).
        AssertClean("c4 c c c | time 2/4 c4 c | repeat percent 2 { d4 d } |");

    [Fact]
    public void AGenuinelyOverfullBodyBarStillWarns()
    {
        // Nine inherited eighths in 4/4 — a real overflow inside the body.
        var diags = Diagnose("c8 c c c c c c c | repeat percent 2 { a a a a a a a a a } |");
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.MeasureOverflow);
    }

    [Fact]
    public void AGenuinelyShortInteriorBodyBarStillWarns()
    {
        // The body's MIDDLE bar is short — a real defect the frame change must
        // not swallow. (Its first bar would get the bare-pickup nudge instead,
        // like the first bar of any stream.)
        var diags = Diagnose(
            "c8 c c c c c c c | repeat percent 2 { a a a a a a a a | d d d d | e e e e e e e e } |");
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.MeasureIncomplete);
    }
}
