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
using LilySharp.Core;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// HANDOFF §2 F-phrasemeter: a phrase body that plays in place is checked where it is played,
/// in the meter there — a section header's <c>time</c>, another part's (SectionMeterPlan) —
/// not only at its declaration in the document's meter, which read every 3/4 bar of a phrase
/// used under <c>time 3/4</c> as short (LYS2006 / LYS2001; Lab sessions/p849/pm).
/// </summary>
public class PhraseMeterTests
{
    private static IReadOnlyList<Diagnostic> Validate(string source)
    {
        var v = new MeasureValidator();
        v.Validate(SyntaxTree.Parse(source));
        return v.Diagnostics;
    }

    private static string Book(string phrase, string b, string header = "time 3/4") => $$"""
        octave absolute
        time 4/4
        part top { clef treble }
        part bot { clef bass }
        phrase hook { {{phrase}} }
        section A { top { c'1 | } bot { c1 | } }
        section B { {{header}} {{b}} }
        section C { top { g'1 | } bot { c1 | } }
        form { A B C }
        score { staff top staff bot }
        """;

    private static bool IsBarCheck(Diagnostic d) => d.Code is DiagnosticCodes.MeasureIncomplete
        or DiagnosticCodes.MeasureOverflow or DiagnosticCodes.PickupWithoutPartial
        or DiagnosticCodes.MeasureDurationMismatch;

    [Fact]
    public void APhraseUnderItsSectionsMeter_IsMeasuredInIt()
        => Assert.DoesNotContain(Validate(Book("e'2. | f'2. |", "top { hook } bot { g2. | a2. | }")), IsBarCheck);

    [Fact]
    public void APhraseUnderAnotherPartsMeter_IsMeasuredInIt()
        => Assert.DoesNotContain(Validate(Book("e'2. | f'2. |", "top { hook } bot { time 3/4 g2. | a2. | }", header: "")),
            IsBarCheck);

    [Fact]
    public void AShortBarInAPhrase_IsStillReportedWhereItIsPlayed()
    {
        var diags = Validate(Book("e'2. | f'2 |", "top { hook } bot { g2. | a2. | }"));
        var d = Assert.Single(diags, x => x.Code == DiagnosticCodes.MeasureIncomplete);
        Assert.Contains("1/2", d.Message);
    }

    [Fact]
    public void APhraseNoStreamPlays_IsStillCheckedOnItsOwn()
    {
        var diags = Validate(Book("c'1 | d'2 |", "top { e'2. | } bot { g2. | }"));
        Assert.Contains(diags, IsBarCheck);
    }

    [Fact]
    public void APhraseOpeningMidBar_CompletesTheBarItIsPlayedIn()
        => Assert.DoesNotContain(Validate(Book("c'4 |", "top { e'2 hook e'2. | } bot { g2. | a2. | }")), IsBarCheck);
}
