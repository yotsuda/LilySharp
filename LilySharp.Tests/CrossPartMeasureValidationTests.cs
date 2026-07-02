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

using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Cross-part measure validation: the time signature is SCORE-level (like
/// LilyPond's Timing context), so per-part measure lengths are checked
/// against the score meter and against each other — never against a
/// part-local idea of the meter.
/// </summary>
[Trait("Category", "Unit")]
public class CrossPartMeasureValidationTests
{
    private static IReadOnlyList<Diagnostic> Validate(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var validator = new MeasureValidator();
        validator.Validate(tree);
        return validator.Diagnostics;
    }

    [Fact]
    public void ScoreLevelTime_GovernsPartsWithoutOwnDeclaration()
    {
        // A declares nothing special locally; the top-level time 5/4 governs
        // BOTH parts. B writes 5 beats without restating the time — correct,
        // no warning of any kind.
        var diags = Validate("""
            time 5/4
            section Main {
              rh { c4 d e f g | c4 d e f g | c4 d e f g | }
              lh { c2 g2 c4 | c2 g2 c4 | c2 g2 c4 | }
            }
            structure { Main }
            """);
        Assert.Empty(diags);
    }

    [Fact]
    public void ShortMeasureInOnePart_ReportsMismatch()
    {
        // Interior measure: b has 4 beats where a has 5. The per-block pass
        // flags b's measure as incomplete (vs 5/4); the cross-part pass must
        // NOT stack a second warning on the same span.
        var diags = Validate("""
            time 5/4
            section Main {
              rh { c4 d e f g | c4 d e f g | c4 d e f g | }
              lh { c2 g2 c4 | c2 g2 | c2 g2 c4 | }
            }
            structure { Main }
            """);
        var incomplete = diags.Where(d => d.Code == DiagnosticCodes.MeasureIncomplete).ToList();
        var mismatch = diags.Where(d => d.Code == DiagnosticCodes.MeasureDurationMismatch).ToList();
        Assert.Single(incomplete);
        Assert.Empty(mismatch);
    }

    [Fact]
    public void DifferingPickups_ReportMismatch()
    {
        // FIRST measures are exempt from the fullness warning (pickups), but
        // pickups of DIFFERENT lengths can never align vertically — this is
        // exactly the case only the cross-part check can catch.
        var diags = Validate("""
            time 4/4
            section Main {
              rh { c4 | c4 d e f | }
              lh { c2 | c4 d e f | }
            }
            structure { Main }
            """);
        var mismatch = diags.Where(d => d.Code == DiagnosticCodes.MeasureDurationMismatch).ToList();
        Assert.Single(mismatch);
        Assert.Contains("will not align", mismatch[0].Message);
    }

    [Fact]
    public void EqualPickups_WarnOnlyTheUndeclaredPickupNudge()
    {
        // Matching pickups raise no cross-part mismatch; each bare pickup
        // still gets the per-block "declare it with partial" nudge.
        var diags = Validate("""
            time 4/4
            section Main {
              rh { c4 | c4 d e f | }
              lh { e4 | c4 d e f | }
            }
            structure { Main }
            """);
        Assert.All(diags, d => Assert.Equal(DiagnosticCodes.PickupWithoutPartial, d.Code));
    }

    [Fact]
    public void PhraseReferences_AreExpandedForComparison()
    {
        // The mismatch hides inside referenced phrases: pickup lengths
        // differ (quarter vs half). Expansion must see through $refs.
        var diags = Validate("""
            time 4/4
            phrase pa { c4 | c4 d e f | }
            phrase pb { c2 | c4 d e f | }
            section Main {
              rh { $pa }
              lh { $pb }
            }
            structure { Main }
            """);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.MeasureDurationMismatch);
    }

    [Fact]
    public void TimeBetweenPartBlocks_ReportsConflict()
    {
        // A time declared between the part blocks of one section puts the
        // parts in different meters — alignment is undefined.
        var diags = Validate("""
            time 4/4
            section Main {
              rh { c4 d e f | }
              time 3/4
              lh { c4 d e | }
            }
            structure { Main }
            """);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.ConflictingTimeSignatures);
    }

    [Fact]
    public void SingleStaffScores_AreUntouched()
    {
        // No second part — the cross-part pass must stay silent and the
        // per-block behavior (incl. pickup exemption) is unchanged.
        var diags = Validate("""
            time 4/4
            section Main {
              melody { c4 | c4 d e f | g2 a4 | c1 | }
            }
            structure { Main }
            """);
        Assert.Single(diags.Where(d => d.Code == DiagnosticCodes.MeasureIncomplete));
        // The bare quarter-note pickup additionally gets the declare-it nudge.
        Assert.Single(diags.Where(d => d.Code == DiagnosticCodes.PickupWithoutPartial));
    }
}
