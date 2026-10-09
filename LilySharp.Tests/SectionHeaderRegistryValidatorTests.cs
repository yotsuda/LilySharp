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
/// The bar check reads the section headers off <see cref="SectionHeaders"/> — the page's
/// registry, offered the declarations the page offers — and not off a table of its own.
/// Until 2026-10-02 it kept two tables by a rule of its own: the LAST declaration of a name
/// won (the registry: the first), and a directives-only cell under a <c>part</c> was no header
/// (the registry: it is — its <c>time</c> is the section's, where the validator let it re-arm
/// the DOCUMENT meter). Neither difference was observed by the suite or the corpus
/// (REFACTOR_PLAN stage C5, decision A3); these are the nets that hold the one spelling.
/// </summary>
[Trait("Category", "Unit")]
public class SectionHeaderRegistryValidatorTests
{
    private static IReadOnlyList<Diagnostic> Diags(string src)
    {
        var v = new MeasureValidator();
        v.Validate(SyntaxTree.Parse(src));
        return v.Diagnostics;
    }

    private static IEnumerable<Diagnostic> BarLength(IEnumerable<Diagnostic> d)
        => d.Where(x => x.Code == DiagnosticCodes.MeasureIncomplete
                     || x.Code == DiagnosticCodes.MeasureOverflow
                     || x.Code == DiagnosticCodes.PickupWithoutPartial);

    /// <summary>
    /// Two headers of one name: the FIRST declares the pickup, as it does for the page, the
    /// MIDI, the MusicXML and the twin. A quarter-note first bar under `partial 4` then
    /// `partial 2` is the declared pickup; the other order makes it a short one (the control
    /// — the second header is read, it just does not win).
    /// </summary>
    [Theory]
    [InlineData("partial 4", "partial 2", false)]
    [InlineData("partial 2", "partial 4", true)]
    public void TwoHeadersOfOneName_TheFirstDeclaresThePickup(string first, string second, bool warns)
    {
        var d = Diags($$"""
            section A { {{first}} }
            section A { {{second}} }
            part melody { section A { c4 | c d e f | } }
            form { A }
            score { staff melody }
            """);
        var short1 = d.Where(x => x.Code == DiagnosticCodes.MeasureIncomplete
                                  && x.Message.Contains("less than the declared partial 1/2"));
        if (warns)
            Assert.Single(short1);
        else
            Assert.Empty(BarLength(d));
    }

    /// <summary>Same for the header meter: `time 3/4` then `time 2/4` checks the section's
    /// bars in 3/4; the other order flags the three-quarter bars as overfull.</summary>
    [Theory]
    [InlineData("time 3/4", "time 2/4", false)]
    [InlineData("time 2/4", "time 3/4", true)]
    public void TwoHeadersOfOneName_TheFirstDeclaresTheMeter(string first, string second, bool warns)
    {
        var d = Diags($$"""
            section A { {{first}} }
            section A { {{second}} }
            part melody { section A { c4 d e | c d e | } }
            form { A }
            score { staff melody }
            """);
        var over = d.Where(x => x.Code == DiagnosticCodes.MeasureOverflow
                                && x.Message.Contains("exceeds time signature 2/4"));
        if (warns)
            Assert.Equal(2, over.Count());
        else
            Assert.Empty(BarLength(d));
    }

    /// <summary>
    /// A directives-only cell under a `part` IS a header of its section — `part m { section A
    /// { time 3/4 } }` gives A the meter 3/4 for every part, and the next section is back in
    /// the score meter (the page: SectionHeaders.Read over every declaration outside a chords /
    /// lyrics track). The validator used to read that `time` by the top-level path, which
    /// re-armed the document meter, so n's 4/4 bar in B was overfull. The control is the same
    /// `time` written at the top level, where it IS the document's and B's bar is overfull.
    /// </summary>
    [Theory]
    [InlineData("part m { section A { time 3/4 } }", "", false)]
    [InlineData("part m { clef treble }", "time 3/4", true)]
    public void ADirectivesOnlyCellUnderAPart_IsItsSectionsHeader(string m, string top, bool warns)
    {
        var d = Diags($$"""
            {{top}}
            {{m}}
            part n { section A { c4 d e | } section B { c4 d e f | } }
            form { A B }
            score { staff m staff n }
            """);
        var over = d.Where(x => x.Code == DiagnosticCodes.MeasureOverflow
                                && x.Message.Contains("exceeds time signature 3/4"));
        if (warns)
            Assert.Single(over);
        else
            Assert.Empty(BarLength(d));
    }
}
