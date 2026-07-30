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

using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A below-staff annotation belonging to the UPPER staff of two must not extend the room
/// below the LAST staff. Its ink lives BETWEEN the staves, so the room it needs is the staff
/// spring's to give.
/// </summary>
/// <remarks>
/// <para>
/// This is the machine for a claim no ledger reading could take. The corpus measures the
/// foot of a page's spring chain (audit/lp-geometry <c>dynamic.page.*</c>,
/// <c>hairpin.page.*</c>, <c>figbass.page.*</c>) and that reading only means the ink under
/// the last staff while the foot spring sits on its FLOOR — which needs a full page. A
/// two-staff system is tall enough that LilyPond puts seven of them on the page and stretches
/// the rest, so books DYPU / DYPHU in <c>dynamic-page.ly</c> were measured and left
/// unentered: their foot reads f ≈ 0.378 against a block of 0.068, i.e. the page's force and
/// not the ink. That header says what a ledger entry would need instead (a two-staff page
/// that COMPRESSES).
/// </para>
/// <para>
/// So the claim gets a test in the commit that relies on it (HANDOFF §5.0 — a re-based
/// snapshot is not an observer). What it guards is the defect
/// <c>EstimateLooseLineExtents</c> had: the estimate was taken per SYSTEM from the ITEMS,
/// with no staff anywhere in the sentence, so a dynamic on the upper staff of two charged its
/// 2.0 (a hairpin its 1.5) below the WHOLE system — the same shape as the figured-bass drop
/// that had no staff in it either. Five committed fixtures shortened by 0.33 to 0.67 of page
/// height when it went, every one of them multi-staff.
/// </para>
/// <para>
/// LILYPOND-REF: lily/page-layout-problem.cc:1070-1127 <c>build_system_skyline</c> — a
/// system's bottom skyline is built from what is BELOW its last spaceable staff; a grob
/// between two staves is inside the alignment and reaches it through
/// <c>Align_interface</c>'s translation instead (lily/align-interface.cc:228-238), which is
/// the second assertion here.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class LooseLineExtentScopeTests
{
    /// <summary>Two staves, the upper one optionally carrying <paramref name="mark"/>.</summary>
    /// <remarks>The two halves are the same string with one substitution, so they cannot drift
    /// apart the way a hand-copied pair can (HANDOFF §5.0).</remarks>
    private static string TwoStaffScore(string mark) => $$"""
        octave absolute
        time 4/4
        key c major

        part up { clef treble }
        part down { clef bass }

        section Main {
          up { c'4{{mark}} d' e' f' | g'1 | }
          down { c4 d e f | g1 | }
        }

        form main { ~Main }

        score main "loose-line-scope" {
          staff ~up
          staff ~down
        }
        """;

    [Theory]
    [InlineData("@f")]      // a dynamic — EstimateLooseLineExtents' 2.0
    [InlineData("@cresc")]  // a hairpin — its 1.5, the branch gated on there being no dynamics
    public void AnUpperStaffAnnotationDoesNotExtendTheRoomBelowTheLastStaff(string mark)
    {
        var bare = RenderedGeometry.Render(TwoStaffScore(""));
        var annotated = RenderedGeometry.Render(TwoStaffScore(mark));

        // THE CLAIM: nothing hangs below the LOWER staff in either score, so the page leaves
        // the same room under it. An estimate that charges an upper-staff annotation to the
        // system's down extent breaks this by exactly its own constant.
        Assert.Equal(bare.LastStaffRefpointToFoot(), annotated.LastStaffRefpointToFoot(), 9);

        // ...AND THE TEST IS NOT VACUOUS, which needs saying separately because the assertion
        // above is an EQUALITY: a mark that never reached the layout at all would satisfy it
        // perfectly. ⚠️ The witness is the PLACED grob and not a second distance — measured
        // while writing this, neither the staff gap nor the room under a lone staff moves for
        // a hairpin (9.000000 and 9.230551 with it and without), because its thin ink loses to
        // the staff spring's ideal on one side and to the notes' own reach on the other. A
        // pair whose witness is a distance that does not move proves nothing.
        var layout = Layout(TwoStaffScore(mark));
        Assert.True(layout.DynamicLayouts.Length + layout.HairpinLayouts.Length > 0,
            $"no {mark} reached the layout at all, so the equality above is vacuous.");
    }

    private static LilySharp.Core.Svg.Layout.ScoreLayout Layout(string source)
    {
        var tree = LilySharp.Core.Syntax.SyntaxTree.Parse(source);
        var spec = LilySharp.Core.Svg.Collector.RenderSpecParser.FindFirst(tree)!;
        var score = new LilySharp.Core.Svg.Collector.MeasureCollector().CollectMultiStaff(tree, spec);
        return new LilySharp.Core.Svg.Layout.LayoutEngine().Layout(score);
    }
}
