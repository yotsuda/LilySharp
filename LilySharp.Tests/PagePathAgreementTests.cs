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
using LilySharp.Core.Svg.Layout;
using LilySharp.Tests.LpFidelity;
using Xunit;
using Xunit.Abstractions;

namespace LilySharp.Tests;

/// <summary>
/// Lily# spaces a page in two different files, and both must answer the same. This asks them
/// the same book and compares, staff by staff.
/// </summary>
/// <remarks>
/// <para>
/// THE TWO SPELLINGS. <c>LayoutEngine.CreatePages</c> stacks the systems itself, and
/// <c>PageLayouter.CreatePagesWithOptimalBreaking</c> solves a spring chain;
/// <c>UseOptimalPageBreaking</c> decides which runs and DEFAULTS TO FALSE, so what a user's
/// <c>lysc svg</c> and the editor preview take is the stacking one. They cannot be folded —
/// one is a stack, the other a solver — so per HANDOFF 7.7 the guard is a DIFFERENTIAL NET:
/// ask both and compare, instead of writing down what either should say.
/// </para>
/// <para>
/// ⚠️ THIS NET IS WRITTEN BECAUSE ITS ABSENCE COST A SESSION. On 2026-08-25 the stacking path
/// was found flooring an ORIGIN-to-origin distance by <c>system-system-spacing</c>'s numbers,
/// which are stated staff-to-staff — while the chain had done exactly that conversion all
/// along, carried the reference for it, and said so in a comment. Two staves asked of both
/// would have printed 10.540000 against 12.000000 and named the file. Instead it took a user
/// report, a LilyPond probe and three books. ★ A DIFFERENTIAL ARM NEEDS NO EXPECTED VALUE,
/// which is exactly what makes it writable before anyone knows which side is wrong.
/// </para>
/// <para>
/// ⚠️ THE REGIME IS ASSERTED, NOT ASSUMED (HANDOFF 5.0 trap 7), and it is EQUAL PAGE COUNTS
/// rather than one page. Optimal breaking is allowed to break differently — that is its job —
/// and if it ever does, this net would be comparing page breaking rather than spacing and
/// must be re-aimed rather than relaxed.
/// </para>
/// <para>
/// ⚠️ AND THE BOOK IS PROVED TO BE IN ITS REGIME AT THE END, not assumed: the quantity under
/// test is the distance BETWEEN systems, so a paper that puts one system on each page
/// compares nothing at all and the last assertion says so. That trap is real here — the
/// first draft of this file ran on justified paper, where the springs stretch to fill and a
/// two-staff system takes a page to itself.
/// </para>
/// <para>
/// ⚠️ THE STAFF COUNT IS THE VARIABLE THAT MATTERS. The frame error the net is named after was
/// invisible at one staff — a four-space body and a 12.000000 basic distance leave the
/// 8.000000 LilyPond draws either way — and only parted company at two. One book would have
/// proved nothing; this runs 1, 2 and 3.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class PagePathAgreementTests
{
    private readonly ITestOutputHelper _output;

    public PagePathAgreementTests(ITestOutputHelper output) => _output = output;

    // Ragged bottom, so the springs sit at their natural length and what is compared is
    // SPACING rather than how two solvers divide the same slack.
    private static readonly LayoutOptions Ragged = LayoutOptions.Default with
    {
        PageBreaking = LayoutOptions.Default.PageBreaking with { RaggedBottom = true },
    };

    private static LayoutOptions Chain(LayoutOptions o) => o with { UseOptimalPageBreaking = true };

    /// <summary>The ledger's own book (the Lily# half of probes/system-indent-floor.ly),
    /// which is where the frame error showed: one part on N staves, so every system carries
    /// an instrument name and an indent, and a boxed section label leads each system.</summary>
    private static string Book(int staffCount, int systems) =>
        $$"""
        octave absolute
        key c major

        part melody {
          clef treble
          section A { c'4 c' g'' g'' | a'' a'' g''2 | break }
          section B { g''4 g'' f'' f'' | e'' e'' d''2 | break }
        }

        form main { {{string.Join(" ", Enumerable.Range(0, systems).Select(i => i % 2 == 0 ? "A" : "B"))}} }

        score main {
        {{string.Join("\n", Enumerable.Repeat("  staff melody", staffCount))}}
        }
        """;

    public static TheoryData<int> StaffCounts() => new() { 1, 2, 3 };

    [Theory]
    [MemberData(nameof(StaffCounts))]
    public void BothPagePaths_PlaceEveryStaffAtTheSameY(int staffCount)
    {
        string source = Book(staffCount, systems: 4);
        var stacked = RenderedGeometry.Render(source, Ragged);
        var chained = RenderedGeometry.Render(source, Chain(Ragged));

        string what = $"{staffCount} stav(es)";

        Assert.True(stacked.PageCount == chained.PageCount,
            $"{what}: the two paths broke the book differently — {stacked.PageCount} page(s) "
            + $"stacked, {chained.PageCount} chained. This net compares SPACING and needs the "
            + "same pages on both sides; re-aim it rather than relaxing this.");

        int mostStavesOnAPage = 0;
        for (int page = 0; page < stacked.PageCount; page++)
        {
            var a = stacked.StaffRefpoints(page);
            var b = chained.StaffRefpoints(page);
            _output.WriteLine($"{what} p{page}: stacked [{string.Join(", ", a.Select(y => y.ToString("F4")))}]");
            _output.WriteLine($"{what} p{page}: chained [{string.Join(", ", b.Select(y => y.ToString("F4")))}]");

            Assert.True(a.Count == b.Count,
                $"{what} page {page}: {a.Count} staves stacked, {b.Count} chained.");
            for (int i = 0; i < a.Count; i++)
            {
                Assert.True(System.Math.Abs(a[i] - b[i]) < 1e-6,
                    $"{what} page {page} staff {i}: the stacking path puts it at {a[i]:F6} and "
                    + $"the spring chain at {b[i]:F6}. One book, two answers — the two files "
                    + "have drifted, and the shipped path is the stacking one.");
            }
            mostStavesOnAPage = System.Math.Max(mostStavesOnAPage, a.Count);
        }

        // THE BOOK IS IN ITS REGIME. The quantity under test is the distance BETWEEN systems,
        // so some page must carry two of them — otherwise every assertion above passed on
        // pages that never exercised it.
        Assert.True(mostStavesOnAPage >= staffCount * 2,
            $"{what}: no page carries two systems ({mostStavesOnAPage} staves at most), so no "
            + "inter-system distance was compared. Re-aim the book — this is the trap of a "
            + "green that measures nothing.");
    }
}
