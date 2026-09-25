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
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The line-break DP's row-prefix resume keeps the rows whose inputs did not change. Row j
/// reads springs[j] as well as the springs before it — a candidate line ending before
/// measure j reserves that measure's courtesy key / meter
/// (<c>MeasureSpringData.LineEndCourtesyWidth</c>, read in KnuthPlassBreaker.LineEdgeWidths)
/// — so when the vectors agree on their first e springs only, row e must be solved again.
/// Until session 595 it was kept: the preview broke lines where SvgGenerator.Generate did not
/// (a random edit of a corpus book).
/// </summary>
[Trait("Category", "Unit")]
public class LineBreakDpSessionTests
{
    private static MeasureSpringData[] Vector(double courtesyAtThree) =>
    [
        new(10, 8, 1), new(10, 8, 1), new(10, 8, 1),
        new(10, 8, 1, LineEndCourtesyWidth: courtesyAtThree),
        new(10, 8, 1), new(10, 8, 1),
    ];

    private static int Run(LineBreakDpSession session, MeasureSpringData[] springs)
    {
        int first = session.Begin(springs, springs.Length, 100, 5, 4, 0, false,
            out var dp, out var prev, out var force, out var min, out var max);
        session.Store(springs, 100, 5, 4, 0, false, dp, prev, force, min, max);
        return first;
    }

    [Fact]
    public void ASpringThatDiffersOnlyInItsCourtesy_ResolvesTheRowThatEndsBeforeIt()
    {
        var session = new LineBreakDpSession();
        Run(session, Vector(0));
        // Springs 0..2 agree, spring 3 differs only in the courtesy a line ending before
        // measure 3 reserves: row 3 reads it, so the first row solved again is 3 at most.
        int first = Run(session, Vector(4.5));
        Assert.True(first <= 3, $"the resume kept row {first - 1}, which reads spring 3");
        // Liveness: the rows before it are kept.
        Assert.True(first >= 2, $"the resume kept nothing ({first})");
    }
}
