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
/// A PART-MAJOR section (<c>part mel { section A { c d f | } }</c>) keeps its music
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
        var diags = Diagnose("time 4/4\npart mel {\n  section A { c4 d | c4 | c4 d | }\n}\n");
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.MeasureIncomplete);
    }

    [Fact]
    public void PartMajorSection_ShortFirstMeasure_WarnsPickup()
    {
        // The reported case: section A { c d f | } in 4/4 — a 3/4 first measure.
        // It now warns (before, part-major sections were never bar-checked).
        var diags = Diagnose("time 4/4\npart mel {\n  section A { c d f | }\n}\n");
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.PickupWithoutPartial);
    }

    /// <summary>
    /// A CHORD track's sections are not bar streams: an entry carries no duration, and a
    /// slot's <c>s</c> / <c>r</c> is a beat-grid slot, not a quarter rest. The part-major
    /// chord form wraps its cells in the same SectionDeclaration a part does, and the
    /// inline-music pass priced every rest slot of a 2/4 row as "1/4 is less than 2/4"
    /// (reported 2026-09-04 on the Lambada proposal, then spelled with the `s` spacer). The
    /// control is the same row spelled in a PART, where a bare rest IS a quarter and the
    /// warning is right.
    /// </summary>
    [Fact]
    public void ChordTrackSections_AreNotBarChecked()
    {
        const string chords =
            "time 2/4\nchords prog {\n  section A { . | C | | r | G . | }\n}\n";
        Assert.DoesNotContain(Diagnose(chords), d => d.Code == DiagnosticCodes.MeasureIncomplete
            || d.Code == DiagnosticCodes.MeasureOverflow
            || d.Code == DiagnosticCodes.PickupWithoutPartial);

        const string part = "time 2/4\npart mel {\n  section A { s | c2 | r | }\n}\n";
        Assert.Contains(Diagnose(part), d => d.Code == DiagnosticCodes.PickupWithoutPartial
            || d.Code == DiagnosticCodes.MeasureIncomplete);
    }

    [Fact]
    public void PartMajorSection_FullMeasures_NoWarning()
    {
        // Full 4/4 bars must NOT be flagged (no false positives from the new pass).
        var diags = Diagnose("time 4/4\npart mel {\n  section A { c4 d e f | c4 d e f | }\n}\n");
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.MeasureIncomplete
            || d.Code == DiagnosticCodes.MeasureOverflow
            || d.Code == DiagnosticCodes.PickupWithoutPartial);
    }

    [Fact]
    public void PartMajorSection_InlinePartial_DoesNotLeakToOtherSections()
    {
        // `partial 2.` INSIDE part-major section A (no MusicBlock wrapper) declares
        // A's own 3/4 pickup. It must NOT be mistaken for a file-wide partial that
        // then re-targets every later section to 3/4 and hides B's short measure.
        // Regression: before the fix this returned no diagnostics at all.
        var diags = Diagnose(
            "time 4/4\npart mel {\n" +
            "  section A { partial 2. c d f }\n" +   // declared 3/4 pickup, filled -> clean
            "  section B { | a' b c | }\n" +          // 3/4 bar in 4/4 -> must warn
            "  section C { a' b c d }\n}\n");         // full 4/4 -> clean
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.PickupWithoutPartial);
    }

    [Fact]
    public void PartMajorSection_ShortFinalMeasure_NoPickup_Warns()
    {
        // Reported case: section A's closing `d d` (2/4 in 4/4) is only "last"
        // within A's own text — a structure reuses A mid-form, so it is really
        // an interior bar. A opens with a FULL bar, so there is no pickup for
        // `d d` to complete: it is a genuine short bar and must warn. (Before,
        // the unconditional last-measure exemption silently swallowed it.)
        var diags = Diagnose("time 4/4\npart mel {\n  section A { f4 f e e | d d }\n}\n");
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.MeasureIncomplete);
    }

    [Fact]
    public void PartMajorSection_ShortFinalMeasure_CompletingPickup_StaysExempt()
    {
        // Genuine anacrusis: a 1/4 pickup and a 3/4 closing bar sum to one 4/4
        // bar. The closing bar completes the pickup, so it is NOT flagged short
        // (only the bare pickup gets its declare-with-partial nudge).
        var diags = Diagnose("time 4/4\npart mel {\n  section A { c4 | c4 d e f | c4 d e }\n}\n");
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.MeasureIncomplete);
    }
}
