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
using Xunit;

namespace LilySharp.Tests.Svg;

/// <summary>
/// The two aliasing promises <c>PagingAugmentProgram.Execute</c> makes to
/// <c>SystemLayoutCache.GetOrComputePagingAugment</c>: it never writes through to the
/// baseline it merged FROM, and a side no step touched comes back as the caller's own
/// instance.
/// </summary>
/// <remarks>
/// ⚠️ WHY THIS IS A NET AND NOT A REMARK. Until session 191 both promises were free: every
/// step opened with a fresh wrapper skyline merged from the running pair, so nothing could
/// reach the baseline even by accident — and that per-step copy was the single biggest term
/// in a script-dense keystroke's allocation (1908 MB of perf-fingstack1k's 3305 MB), because
/// a system with N scripts copied its whole silhouette N times. The copy is now per SIDE,
/// taken on the first write, which makes both promises load-bearing instead of accidental:
/// drop the copy-on-first-write and Execute starts mutating a skyline the memo has stored by
/// REFERENCE and the un-augmented consumers still read, and neither the suite nor a snapshot
/// would say so on a single render — the damage is to the NEXT keystroke's cache hit.
/// <para>
/// The equivalence itself (per-step vs per-side copy) was checked, not argued: 566-book SVG
/// A/B, 0 moved, with the branches' positive controls firing (6 books observe ScriptUp, 1
/// ScriptDown, 37 the box/bow/tuplet steps).
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class PagingAugmentProgramTests
{
    private static List<(double Start, double End, double Value)> Shape(VerticalSkyline s)
        => s.Buildings.Select(b => (b.Start, b.End, b.ValueAt(b.Start))).ToList();

    /// <summary>An UP-only program leaves the caller's up skyline untouched and hands the
    /// DOWN one straight back — the reference the memo keys on.</summary>
    [Fact]
    public void Execute_DoesNotWriteThroughToTheBaseline()
    {
        var baseUp = VerticalSkyline.FromBox(0, 100, 1, 1, VerticalDirection.Up);
        var baseDown = VerticalSkyline.FromBox(0, 100, 1, 1, VerticalDirection.Down);
        var before = Shape(baseUp);

        var builder = new PagingAugmentProgram.Builder();
        builder.AddVoltaBox(10, 20, 4, 4);
        builder.AddBarNumberBox(30, 40, 6, 6);   // a SECOND up step: the side is already owned
        var (up, down) = builder.Build().Execute((baseUp, baseDown));

        Assert.NotSame(baseUp, up);
        Assert.Equal(before, Shape(baseUp));
        // Nothing merged into DOWN, so the caller's instance comes back as itself.
        Assert.Same(baseDown, down);
        // ...and the augment really happened (the assertions above would also hold for a
        // program that did nothing at all).
        Assert.True(up.MaxProtrusionInRange(10, 20) > 3.9);
        Assert.True(up.MaxProtrusionInRange(30, 40) > 5.9);
    }

    /// <summary>A step that writes BOTH sides owns both, and still leaves both baselines
    /// as they were.</summary>
    [Fact]
    public void Execute_ThatTouchesBothSides_CopiesBothAndMutatesNeither()
    {
        var baseUp = VerticalSkyline.FromBox(0, 100, 1, 1, VerticalDirection.Up);
        var baseDown = VerticalSkyline.FromBox(0, 100, 1, 1, VerticalDirection.Down);
        var beforeUp = Shape(baseUp);
        var beforeDown = Shape(baseDown);

        var builder = new PagingAugmentProgram.Builder();
        builder.AddMarkBox(10, 20, 7, 7);
        var (up, down) = builder.Build().Execute((baseUp, baseDown));

        Assert.NotSame(baseUp, up);
        Assert.NotSame(baseDown, down);
        Assert.Equal(beforeUp, Shape(baseUp));
        Assert.Equal(beforeDown, Shape(baseDown));
        Assert.True(up.MaxProtrusionInRange(10, 20) > 6.9);
    }

    /// <summary>An empty program is the identity, instances included — what leaves an
    /// unannotated system's originals in place.</summary>
    [Fact]
    public void Execute_OfAnEmptyProgram_HandsBothInstancesBack()
    {
        var baseUp = VerticalSkyline.FromBox(0, 100, 1, 1, VerticalDirection.Up);
        var baseDown = VerticalSkyline.FromBox(0, 100, 1, 1, VerticalDirection.Down);

        var (up, down) = new PagingAugmentProgram.Builder().Build().Execute((baseUp, baseDown));

        Assert.Same(baseUp, up);
        Assert.Same(baseDown, down);
    }
}
