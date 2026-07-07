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
/// A PART-MAJOR section (<c>part p { section A { c d f | } }</c>) keeps its music
/// INLINE on the SectionDeclaration — there is no MusicBlock wrapper — so the bar
/// check used to skip it entirely and short measures passed silently. It must now
/// be validated exactly like a section-major (part-block) section.
/// </summary>
public sealed class MeasureValidatorSectionTests
{
    private static IReadOnlyList<Diagnostic> Diagnose(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var validator = new MeasureValidator();
        validator.Validate(tree);
        return validator.Diagnostics;
    }

    [Fact]
    public void PartMajorSection_ShortInteriorMeasure_Warns()
    {
        // Interior 1/4 bar in 4/4 — a genuine short measure, not an edge pickup.
        var diags = Diagnose("time 4/4\npart p {\n  section A { c4 d | c4 | c4 d | }\n}\n");
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.MeasureIncomplete);
    }

    [Fact]
    public void PartMajorSection_ShortFirstMeasure_WarnsPickup()
    {
        // The reported case: section A { c d f | } in 4/4 — a 3/4 first measure.
        // It now warns (before, part-major sections were never bar-checked).
        var diags = Diagnose("time 4/4\npart p {\n  section A { c d f | }\n}\n");
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.PickupWithoutPartial);
    }

    [Fact]
    public void PartMajorSection_FullMeasures_NoWarning()
    {
        // Full 4/4 bars must NOT be flagged (no false positives from the new pass).
        var diags = Diagnose("time 4/4\npart p {\n  section A { c4 d e f | c4 d e f | }\n}\n");
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.MeasureIncomplete
            || d.Code == DiagnosticCodes.MeasureOverflow
            || d.Code == DiagnosticCodes.PickupWithoutPartial);
    }
}
