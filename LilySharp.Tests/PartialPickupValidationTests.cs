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
/// The <c>partial</c> (pickup) validation: a directives-only section header
/// (<c>section A { partial 2 }</c>) declares the section's pickup, and the pickup hint
/// points at the placements that are actually legal (a section directive, or a leading
/// <c>partial</c> in bare music — never the top level or a voice).
/// </summary>
[Trait("Category", "Unit")]
public class PartialPickupValidationTests
{
    private static IReadOnlyList<Diagnostic> Diags(string src)
    {
        var v = new MeasureValidator();
        v.Validate(SyntaxTree.Parse(src));
        return v.Diagnostics;
    }

    private const string Tail = "\nform main { A }\nscore main { staff melody }";

    [Fact]
    public void StandaloneSectionHeaderPartial_AppliesToPartMajorMusic_NoWarning()
    {
        // `section A { partial 2 }` declares the section pickup; the part-major cell's
        // half-note first bar IS that pickup — so no short-bar / incomplete warning.
        // (Regression: the header's `partial` was misread as inline music and dropped.)
        var d = Diags("section A { partial 2 }\npart melody { section A { c2 | a1 } }" + Tail);
        Assert.DoesNotContain(d, x => x.Code == DiagnosticCodes.PickupWithoutPartial);
        Assert.DoesNotContain(d, x => x.Code == DiagnosticCodes.MeasureIncomplete);
    }

    /// <summary>
    /// A section header's pickup checks THAT section's first bar strictly — a FULL first bar
    /// under `partial 2` is an overfull pickup. It used to pass: the header's `partial` was read
    /// as the file-wide value, which is applied only to a first bar that does not already fill
    /// the meter (it cannot tell which section opens the piece), so the very mistake the
    /// declaration exists to catch was exempt. (User report 2026-09-24,
    /// LilySharp-Lab corpora/ベースタブLy/partial.lys.)
    /// </summary>
    [Theory]
    [InlineData("section A { partial 2 }\npart melody { section A { c'4 d e f | g2 g | } }")]
    [InlineData("part melody { clef treble }\nsection A { partial 2  melody { c'4 d e f | g2 g | } }")]
    public void SectionHeaderPartial_AFullFirstBar_IsAnOverfullPickup(string body)
    {
        var d = Diags(body + Tail);
        var w = Assert.Single(d, x => x.Code == DiagnosticCodes.MeasureOverflow);
        Assert.Contains("exceeds the declared partial 1/2", w.Message);
    }

    [Fact]
    public void SectionHeaderPartial_AShortFirstBar_IsAnUnderfullPickup()
    {
        var d = Diags("section A { partial 2 }\npart melody { section A { c4 | a1 | } }" + Tail);
        Assert.Contains(d, x => x.Code == DiagnosticCodes.MeasureIncomplete
                                && x.Message.Contains("less than the declared partial 1/2"));
    }

    [Fact]
    public void SectionHeaderPartial_ReachesTheLaterVoicesOfASpanInTheOpeningBar()
    {
        // voice 2 of a span opened in the pickup bar is the same rendered bar, so the same
        // pickup (fixtures test/chord-flag.lys and the corpus's blogger2.lys): a first cut of
        // the section-scoped pickup reached only voice 1 and called voice 2's eighth short.
        var d = Diags("section A { partial 8 }\npart melody { section A { voice { f'8 } { <bes' ges c>8 } } }" + Tail);
        Assert.DoesNotContain(d, x => x.Code == DiagnosticCodes.MeasureIncomplete);
    }

    [Fact]
    public void SectionHeaderPartial_BelongsToItsSection_NotToTheNext()
    {
        // B's short first bar is B's own affair (a bare-pickup nudge at most), never a
        // mismatch against A's pickup.
        var d = Diags("section A { partial 2 }\npart melody { section A { c2 | a1 | } section B { c4 d e | f1 | } }"
                      + "\nform main { A B }\nscore main { staff melody }");
        Assert.DoesNotContain(d, x => x.Message.Contains("declared partial"));
    }

    [Fact]
    public void PickupHint_Structured_PointsToSectionDirective_NotTopLevelOrVoice()
    {
        // A short first bar with no declared pickup nudges toward the section header —
        // NOT "top level", "in the voice" or a part's music, which may not hold an opening
        // pickup (LYS1024; the part's first bar since 2026-09-15).
        var msg = Diags("part melody\nsection A { melody { c2 | a1 } }" + Tail)
            .Single(x => x.Code == DiagnosticCodes.PickupWithoutPartial).Message;
        Assert.Contains("section header", msg);
        Assert.DoesNotContain("top level", msg);
        Assert.DoesNotContain("in the voice", msg);
        Assert.DoesNotContain("part's music", msg);
    }

    [Fact]
    public void PickupHint_BareMusic_SuggestsLeadingPartial()
    {
        // In a bare note stream a leading `partial` is the right (and only) place.
        var msg = Diags("{ c4 c4 c4 }")
            .Single(x => x.Code == DiagnosticCodes.PickupWithoutPartial).Message;
        Assert.Contains("leading 'partial 2.'", msg);
        Assert.DoesNotContain("section directive", msg);
    }
}
