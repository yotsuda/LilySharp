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

using System;
using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A head's ledger lines are the lines of ITS staff: LilyPond fills from the staff line
/// nearest the head out to the head, every other position of that line's parity, and drops
/// the staff's own lines — so a one-line staff ledgers from ±2, a four-line (−2 … 4) one from
/// −4 below and 6 above, and the timbales pair (±2) puts one through a head on the middle.
/// Lily# had the five-line ±6 written into every reader until 2026-10-10.
/// </summary>
/// <remarks>
/// MEASURED, LilyPond 2.26.0 (Lab sessions/p873/probe/ledger, `b4 d' f' a' | g e c a,` on
/// `as lines 1..5` — treble, so b is the middle): one line draws 16 ledgers (1+2+3 above,
/// 1+2+3+4 below), the pair 10 (the b's one, 1+2 above, 1+2+3 below), five lines 4.
/// LILYPOND-REF: lily/staff-symbol.cc:122-319 Staff_symbol::ledger_positions;
///   lily/ledger-line-spanner.cc:216-274 Ledger_line_spanner::print (one request per head
///   with ledgers or outside the staff), :354-356 (a line per ledger position).
/// </remarks>
[Trait("Category", "Unit")]
public class LedgerPositionsTests
{
    [Theory]
    [InlineData(0, 1, new int[0])]
    [InlineData(1, 1, new int[0])]
    [InlineData(2, 1, new[] { 2 })]
    [InlineData(6, 1, new[] { 2, 4, 6 })]
    [InlineData(-8, 1, new[] { -8, -6, -4, -2 })]
    [InlineData(0, 2, new[] { 0 })]
    [InlineData(2, 2, new int[0])]
    [InlineData(4, 2, new[] { 4 })]
    [InlineData(6, 2, new[] { 4, 6 })]
    [InlineData(-4, 2, new[] { -4 })]
    [InlineData(4, 3, new[] { 4 })]
    [InlineData(-4, 4, new[] { -4 })]
    [InlineData(6, 4, new[] { 6 })]
    [InlineData(8, 4, new[] { 6, 8 })]
    [InlineData(5, 5, new int[0])]
    [InlineData(6, 5, new[] { 6 })]
    [InlineData(-7, 5, new[] { -6 })]
    [InlineData(10, 5, new[] { 6, 8, 10 })]
    public void LedgerPositions_FillFromTheNearestStaffLine(int position, int lines, int[] expected)
    {
        Span<int> into = stackalloc int[EngravingDefaults.LedgerCapacity(position)];
        int n = EngravingDefaults.LedgerPositions(position, lines, into);
        Assert.Equal(expected, into[..n].ToArray());
    }

    /// <summary>A head far out keeps EVERY ledger — test/tab-below-range's c, in relative mode
    /// stands 54 positions under its staff and carries 25; a capped buffer dropped the one
    /// nearest the staff (2026-10-10, the first cut).</summary>
    [Fact]
    public void AFarHead_KeepsEveryLedger()
    {
        Span<int> into = stackalloc int[EngravingDefaults.LedgerCapacity(-54)];
        int n = EngravingDefaults.LedgerPositions(-54, 5, into);
        Assert.Equal(25, n);
        Assert.Equal(-54, into[0]);
        Assert.Equal(-6, into[n - 1]);
    }

    [Theory]
    [InlineData(1, 16)]
    [InlineData(2, 10)]
    [InlineData(3, 9)]
    [InlineData(4, 7)]
    [InlineData(5, 4)]
    public void TheDrawnLedgers_AreTheStaffsOwn(int lines, int ledgers)
    {
        var g = RenderedGeometry.RenderProduct($$"""
            octave absolute
            time 4/4
            part p { }
            section A { p { b4 d' f' a' | g e c a, | } }
            form { A }
            score { staff p as lines {{lines}} }
            """);
        // A ledger is a short horizontal rule of the ledger thickness.
        int drawn = g.Lines.Count(l => Math.Abs(l.Y1 - l.Y2) < 1e-9
                                       && !l.IsDashed
                                       && Math.Abs(l.StrokeWidth - EngravingDefaults.LegerLineThickness) < 1e-9
                                       && Math.Abs(l.X2 - l.X1) < 4.0);
        Assert.Equal(ledgers, drawn);
    }

    /// <summary>A slash has no pitch and carries no ledger — on the two-line staff its middle is
    /// the pair's gap, where a pitched head (the <c>b</c> here) does carry one (owner's report
    /// 2026-10-12: a ledger was drawn through each slash's centre).</summary>
    [Theory]
    [InlineData(2, "/4 / / / |", 0)]
    [InlineData(2, "b4 b b b |", 4)]
    [InlineData(1, "/4 / / / |", 0)]
    [InlineData(5, "/4 / / / |", 0)]
    public void ASlashCarriesNoLedger(int lines, string music, int ledgers)
    {
        var g = RenderedGeometry.RenderProduct($$"""
            octave absolute
            part p { }
            section A { p { {{music}} } }
            form { A }
            score { staff p as lines {{lines}} }
            """);
        int drawn = g.Lines.Count(l => Math.Abs(l.Y1 - l.Y2) < 1e-9
                                       && !l.IsDashed
                                       && Math.Abs(l.StrokeWidth - EngravingDefaults.LegerLineThickness) < 1e-9
                                       && Math.Abs(l.X2 - l.X1) < 4.0);
        Assert.Equal(ledgers, drawn);
    }
}
