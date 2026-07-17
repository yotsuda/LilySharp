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
            form main { Main }
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
            form main { Main }
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
            form main { Main }
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
            form main { Main }
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
            form main { Main }
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
            form main { Main }
            """);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.ConflictingTimeSignatures);
    }

    [Fact]
    public void PartMajor_SectionBarCountDiffers_Warns()
    {
        // The same `section A` is written part-major in two parts with different bar
        // counts (melody 2, bass 1). The collector pads bass to align, but the
        // differing count is usually a miscount — warn (LYS2007), anchored on the
        // shorter part's section.
        var diags = Validate("""
            time 4/4
            part melody { section A { c4 d e f | g4 a b c' | } }
            part bass { section A { c2 e2 | } }
            form main { A }
            """);
        var m = diags.Where(d => d.Code == DiagnosticCodes.SectionBarCountMismatch).ToList();
        Assert.Single(m);
        Assert.Contains("Section 'A'", m[0].Message);
        Assert.Contains("bass", m[0].Message);
    }

    [Fact]
    public void PartMajor_SectionBarCountsMatch_Silent()
    {
        // Equal bar counts across parts — no mismatch warning.
        var diags = Validate("""
            time 4/4
            part melody { section A { c4 d e f | g4 a b c' | } }
            part bass { section A { c2 e2 | c2 e2 | } }
            form main { A }
            """);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.SectionBarCountMismatch);
    }

    [Fact]
    public void SectionMajor_ShorterPart_WarnsBarCountMismatch()
    {
        // lh runs one bar short of rh inside a section-major section: the per-measure
        // loop only reaches the shared index, so the missing bar is caught by the
        // count check.
        var diags = Validate("""
            time 4/4
            section Main { rh { c4 d e f | g4 a b c' | } lh { c4 d e f | } }
            form main { Main }
            """);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.SectionBarCountMismatch);
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
            form main { Main }
            """);
        Assert.Single(diags.Where(d => d.Code == DiagnosticCodes.MeasureIncomplete));
        // The bare quarter-note pickup additionally gets the declare-it nudge.
        Assert.Single(diags.Where(d => d.Code == DiagnosticCodes.PickupWithoutPartial));
    }

    [Fact]
    public void EmptyPlaceholderBars_CountLikeRealBars_NoFalseMismatch()
    {
        // `| | | |` is three explicit empty measures (the bare-barline rule),
        // matching melody2's three bars — so the cross-part pass must be silent:
        // no bar-count mismatch, and an empty placeholder must not read as
        // "0 beats, misaligned" (the collector's own underfull LYS2001 owns that).
        var diags = Validate("""
            section B {
              melody {| | | |}
              melody2 { c1 | c1 | c1 | }
            }
            form main { B }
            """);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.SectionBarCountMismatch);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.MeasureDurationMismatch);
    }

    [Fact]
    public void EmptyPlaceholderBars_ShorterThanSibling_StillWarnsCount()
    {
        // Two empty measures (`| | |`) against melody2's three: a genuine count
        // mismatch survives the empty-measure fix — proving the empties are being
        // counted (2 != 3), not silently swallowed to 0.
        var diags = Validate("""
            section B {
              melody {| | |}
              melody2 { c1 | c1 | c1 | }
            }
            form main { B }
            """);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.SectionBarCountMismatch);
    }
}
