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

    [Fact]
    public void PickupHint_Structured_PointsToSectionDirective_NotTopLevelOrVoice()
    {
        // A short first bar with no declared pickup nudges toward a section directive —
        // NOT "top level or in the voice", which `partial` may not occupy (LYS1024).
        var msg = Diags("part melody\nsection A { melody { c2 | a1 } }" + Tail)
            .Single(x => x.Code == DiagnosticCodes.PickupWithoutPartial).Message;
        Assert.Contains("section directive", msg);
        Assert.DoesNotContain("top level", msg);
        Assert.DoesNotContain("in the voice", msg);
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
