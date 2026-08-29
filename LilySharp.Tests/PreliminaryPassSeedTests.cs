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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The PRELIMINARY annotation pass reads the same room tables the final pass reads
/// (<c>AnnotationLayoutContext.StaffSpanners</c> / <c>StaffInside</c>), so the page reserves
/// each outside-staff mover where the final pass draws it.
/// </summary>
/// <remarks>
/// The preliminary pass's annotations are thrown away and only their EXTENTS survive
/// (<c>LayoutEngine.RunPreliminaryAnnotationPass</c>), so a divergence between the two
/// passes is invisible in the drawing and comes out as spacing — the same trap the context's
/// <c>RestCollisionsOf</c> remark records. Until 2026-08-14 the preliminary context carried
/// neither table, so its profile rebuilds held NO spanners, NO scripts and NO fingerings: a
/// dynamic the final pass pushes below a slur was reserved at the slur-free height, and the
/// page under-reserved exactly the ink the drawn pass clears.
/// <para>
/// ★ THE OBSERVABLE IS THE PAGE HEIGHT of a content-sized single-system book, which reads
/// the preliminary down extent DIRECTLY (<c>LayoutEngine.CreatePages</c>:
/// <c>totalHeight = … + CropDown(last) + MarginBottom</c>, and <c>CropDown</c> is a MAX
/// against that same extent, so this pass's ink still reaches it — see
/// <c>LooseBlockCropTests</c>, whose first attempt froze the extent here instead and lost
/// this test and 40 snapshots at once) — no system-system
/// basic-distance to mask the under-reservation, which is what kept the whole suite green
/// while the divergence existed. The extent registers the mover's own ink about the same
/// YUp the drawn layout carries (<c>EnrichExtentsWithAnnotationProtrusions</c>, same
/// <c>DynamicEngraver.InkOf</c>), so with the two passes on one profile the page's growth
/// for a spanner EQUALS the drawn mover's drop — the ink term cancels in the delta.
/// </para>
/// <para>
/// ⚠️ POSITIVE CONTROL (run 2026-08-14, reverted): with the preliminary context handed
/// empty tables instead of <c>placed.StaffSpanners</c>/<c>placed.StaffInside</c>, both
/// facts fail — slur: page grew 0.000000 while the drawn mover dropped 0.884190; tuplet
/// bracket: page grew 1.729279 (the bracket's own ink) while the mover dropped 3.157279
/// (its clearance below the bracket was not reserved) — so the assertion detects the
/// divergence it exists to block, in both of its shapes.
/// </para>
/// <para>
/// ⚠️ THE PERF HALF, COUNTED IN CALLS (HANDOFF 5.3; probe run 2026-08-14, reverted):
/// with the same control in place, the annotation pass's inside-skyline FALLBACK builds
/// per render — each one a walk of a whole system's music, per (system, staff), paid on
/// every keystroke — were multi-page-vertical 33 / grammar-tour 6 / feature-tour 9 /
/// test-notes 2 / 04-advanced 3; with the tables carried they are 0 on every one of
/// those books. That is exactly HALF of the historical per-both-passes counts in the
/// profile-cache remark (66/12/18) — the final pass's half was already saved when it
/// began carrying the room's tables (2026-08-04); this closes the preliminary half.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class PreliminaryPassSeedTests
{
    private static ScoreLayout LayoutOf(string body)
    {
        var tree = SyntaxTree.Parse("octave absolute\n" + body);
        Assert.False(tree.HasErrors,
            string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        return new LayoutEngine().Layout(
            SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
    }

    // One content-sized page, ONE system — the page bottom is this system's down extent —
    // and the lowest below-staff dynamic the final pass drew (YUp about the staff middle).
    private static (double PageHeight, double DynamicYUp) Measure(string body, string time)
    {
        var layout = LayoutOf(
            $"time {time}\n" +
            "part m { clef treble }\n" +
            $"section S {{ m {{ {body} | }} }}\n" +
            "form main { S }\n" +
            "score main \"o\" { staff m }\n");
        Assert.Single(layout.Pages);
        Assert.Single(layout.AllSystems);
        double y = double.MaxValue;
        foreach (var d in layout.DynamicLayouts.Where(d => !d.IsAbove))
            y = System.Math.Min(y, d.YUp);
        Assert.True(y < double.MaxValue, "the book must place a below-staff mover");
        return (layout.Pages[0].Height, y);
    }

    // CONTROL then assertion. The control proves the book observes the seed at all (the
    // DRAWN mover responds to the spanner — the final pass's profile holds it); the
    // assertion is the preliminary pass's half: the page must grow by the same amount,
    // which it only does when the extents pass stacked the mover against the same profile.
    // ⚠️ The 0.05 band is not a fidelity tolerance: the two numbers are the same
    // arithmetic on the same YUp when the passes share one profile, and differ by the
    // whole drawnDrop when they do not.
    private static void PageReservesTheDrawnMover(
        string with, string without, string what, string time)
    {
        var (heightWith, dynWith) = Measure(with, time);
        var (heightWithout, dynWithout) = Measure(without, time);

        double drawnDrop = dynWithout - dynWith;   // YUp: smaller is deeper
        Assert.True(drawnDrop > 0.5,
            $"control: the drawn mover must drop for the {what}: "
            + $"with {dynWith:F6}, without {dynWithout:F6}");

        double reservedGrowth = heightWith - heightWithout;
        Assert.True(System.Math.Abs(reservedGrowth - drawnDrop) < 0.05,
            $"the page must reserve the mover where the final pass draws it: "
            + $"page grew {reservedGrowth:F6}, drawn mover dropped {drawnDrop:F6}");
    }

    /// <summary>A dynamic pushed below a SLUR is reserved below the slur, not at the
    /// slur-free height. Same book shape as
    /// <c>OutsideStaffSeedTests.BelowDynamic_ClearsASlurUnderTheStaff</c>.</summary>
    [Fact]
    public void PageBottom_ReservesTheDynamicBelowASlur()
        => PageReservesTheDrawnMover(
            "g,1@f( g,1)", "g,1@f g,1", "slur", "8/4");

    /// <summary>A dynamic pushed below a second voice's TUPLET BRACKET is reserved below
    /// the bracket. The bracket sits on its voice's stem side, so a second voice puts it
    /// under the staff (same shape as
    /// <c>OutsideStaffSeedTests.BelowDynamic_ClearsATupletBracketUnderTheStaff</c>).</summary>
    [Fact]
    public void PageBottom_ReservesTheDynamicBelowATupletBracket()
        => PageReservesTheDrawnMover(
            "voice { b4@f b b b } { tuplet 3/2 { c4 c c } c2 }",
            "voice { b4@f b b b } { c4 c c2 }", "tuplet bracket", "4/4");
}
