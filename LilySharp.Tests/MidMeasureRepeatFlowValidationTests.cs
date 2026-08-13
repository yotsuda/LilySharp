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
/// A percent/volta/unfold repeat opening MID-BAR (or whose body is no whole number of
/// bars) flows its played content ACROSS the bar boundary, exactly as the collector's
/// <c>MeasureBuilder</c> auto-completes rendered bars at the meter. The written bar
/// around the repeat is judged by what remains in the bar its barline actually closes —
/// not by pretending the repeat is zero-length, which nudged
/// <c>c2 repeat percent 2 { d4 d } |</c> with "first measure is 1/2 of 4/4 — declare a
/// pickup" while the render (and LilyPond's own bar check on the twin) has bar 1
/// exactly full and the SECOND bar short (reported 2026-08-13) — and not by summing
/// body×count into one written bar, which would call the same input overfull.
/// </summary>
[Trait("Category", "Unit")]
public sealed class MidMeasureRepeatFlowValidationTests
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
    public void MidMeasureRepeatFlowsAcrossTheBar_TheFullFirstBarIsNotAPickup()
    {
        // The reported shape: c2 + turn 1 fill bar 1 exactly; turn 2 leaves the bar
        // the written `|` closes at 1/2. LilyPond's bar check on the twin fails "at
        // 1/2" — the accurate claim is a SHORT bar there, never a pickup nudge on a
        // first bar that is full on the page.
        var diags = Diagnose("c2 repeat percent 2 { d4 d } |");
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.PickupWithoutPartial);
        var incomplete = diags.Where(d => d.Code == DiagnosticCodes.MeasureIncomplete).ToList();
        Assert.Single(incomplete);
        Assert.Contains("1/2", incomplete[0].Message);
    }

    [Fact]
    public void MidMeasureRepeatCompletingTheBarExactly_IsClean() =>
        // c2 + d4 + d4 = one full 4/4 bar; the `|` confirms the auto-completed close.
        // (LilyPond compiles the twin with no bar-check warning.)
        AssertClean("c2 repeat percent 2 { d4 } |");

    [Fact]
    public void MidMeasureRepeatSpanningWholeBars_IsClean() =>
        // e2 + 3 × (f4 f) = exactly two full bars; the flow crosses one bar boundary.
        AssertClean("e2 repeat percent 3 { f4 f } |");

    [Fact]
    public void NotesAfterTheRepeat_CompleteTheFlowedRemainder() =>
        // The remainder turn 2 leaves (1/2) plus the e2 after the repeat fill the
        // second bar exactly — the tally continues across the repeat, it does not
        // restart at the pre-repeat value.
        AssertClean("c2 repeat percent 2 { d4 d } e2 |");

    [Fact]
    public void AHalfBarBodyRepeatedToAFullBarAtTheBarStart_IsClean() =>
        // The repeat opens ON the bar boundary and its two half-bar turns fill one
        // bar. The body's trailing chunk (it has no barline) is not a closed bar —
        // checking it alone said "1/2 of 4/4, declare a pickup" for a page whose
        // every bar is full.
        AssertClean("c4 c c c | repeat percent 2 { d4 d }");

    [Fact]
    public void AVoltaBodyFlowsTheSameWay() =>
        // The collector unfolds volta bodies the same count× way; so does the flow.
        AssertClean("c2 repeat volta 2 { d4 } |");

    [Fact]
    public void AnOverlongBodyChunkStillReadsOverfull()
    {
        // leadIn 1/2 + d1 = 3/2 in 4/4: a chunk LONGER than the meter can never fit
        // any rendered bar (the render closes bar 1 overlong at 3/2). The open-tail
        // exemption is for the UNDERFULL side only.
        var diags = Diagnose("c2 repeat percent 2 { d1 } |");
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.MeasureOverflow);
    }

    [Fact]
    public void CrossPart_ARepeatOfWholeBars_CountsItsRenderedBars()
    {
        // rh: 1 written bar + a repeat whose two turns are one full bar each = 3
        // rendered bars; lh writes 3 bars. Counting the repeat's turns into one
        // written bar reported "spans 2 bars but 3" while the page has 3 against 3.
        var tree = SyntaxTree.Parse("""
            time 4/4
            section Main {
              rh { c4 c c c | repeat percent 2 { d8 d d d d d d d } }
              lh { c1 | c1 | c1 | }
            }
            form main { Main }
            """);
        var validator = new MeasureValidator();
        validator.Validate(tree);
        Assert.Empty(validator.Diagnostics);
    }
}
