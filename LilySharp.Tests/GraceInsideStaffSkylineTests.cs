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

using LilySharp.Core.Svg.Layout;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A grace note's ink is INSIDE-STAFF ink: the profile the outside-staff movers clear carries
/// the grace's head, stem and flag at the grace's own fonts, where it stands.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/axis-group-interface.cc:914-935 skyline_spacing — every element with
///   no outside-staff-priority joins the inside profile, a grace's grobs included;
/// LILYPOND-REF: scm/music-functions.scm:636-650 general-grace-settings — their font-size,
///   the stem's length-fraction 0.8 and no-stem-extend; :652-656 score-grace-settings — the
///   stem forced UP.
/// The regime is held by ledger points mark.over-grace.staff-to-baseline (probe MGF) and its
/// control (MGN); these assert the RULE by perturbation — the same book with and without
/// the grace — and against the grace stem the engine itself computes, so no literal can
/// satisfy them. Poisoned by seeding nothing for a grace-time item (the pre-session-568
/// skip), the first test reads the two books alike and goes red.
/// </remarks>
[Trait("Category", "Unit")]
public class GraceInsideStaffSkylineTests
{
    private static string Book(string name, bool withGrace)
    {
        string grace = withGrace ? "grace { gis'16 } " : "";
        return $$"""
            octave absolute
            time 4/4
            key c major

            part melody {
              section A { c'4@mark("A") d' e' f' | g' a' b' c'' | c''4 b' a' g' | f' e' d' c' | break }
              section B { {{grace}}a'4@mark("B") b' a' g' | f'4 e' d' c' | }
            }

            form main { ~A ~B }

            score main "{{name}}" {
              staff melody
            }
            """;
    }

    private static readonly LayoutOptions Paper = LayoutOptions.Default with
    {
        PageBreaking = LayoutOptions.Default.PageBreaking with { RaggedBottom = true },
    };

    [Fact]
    public void TheMarkOverAGraceClearsTheGraceStem_NotTheFirstFullSizeHead()
    {
        double over = RenderedGeometry.Render(Book("MGF", true), Paper)
            .MusicMarkBaselineAboveStaff("B");
        double control = RenderedGeometry.Render(Book("MGN", false), Paper)
            .MusicMarkBaselineAboveStaff("B");

        // The control's mark clears the first head (A5 at position 6): its box term is
        // whatever stands above head top + outside-staff-padding.
        double headTop = 6 * 0.5 + GlyphMetrics.GetNoteheadBBox(4).Top;
        double boxTerm = control - (headTop + OutsideStaffStacker.OutsideStaffPadding);
        Assert.True(boxTerm > 0 && boxTerm < 1.0, $"control box term {boxTerm:F6}");

        // The grace's stem: gis' at position 5, a sixteenth, forced up, the grace details —
        // the same calculator the renderer draws it with.
        double graceStemTop = 5 * 0.5 + StemCalculator.CalculateStemLength(
            stemUp: true, StemCalculator.GetDurationLog(16), 5, GrobFontSize.GraceStemDetails);
        Assert.True(graceStemTop > headTop + 1.0, $"grace stem top {graceStemTop:F6}");

        // ...and the mark over it stands the same box term above THAT top + padding.
        Assert.Equal(graceStemTop + OutsideStaffStacker.OutsideStaffPadding + boxTerm, over, 6);
    }

    [Fact]
    public void WithoutTheGraceTheTwoBooksAgree()
    {
        // The perturbation's other half: removing the grace from the source is the only
        // difference, so a profile that carries no grace reads both books alike — which is
        // exactly what the test above must NOT see.
        double a = RenderedGeometry.Render(Book("MGN", false), Paper)
            .MusicMarkBaselineAboveStaff("B");
        double b = RenderedGeometry.Render(Book("MGN2", false), Paper)
            .MusicMarkBaselineAboveStaff("B");
        Assert.Equal(a, b, 9);
    }
}
