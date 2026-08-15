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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Volta brackets are vertically fit to objects below them
/// (volta-bracket-vertical-skylines.ly). One VoltaBracketSpanner per CHAIN of
/// consecutive endings — not per system — so two repeats on one line
/// side-position independently, and the spanner's profile is the drawn stencil
/// POINTWISE (hooks and number reach deeper only over their own X), so the thin
/// line clears a ledgered note by its padding alone.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/volta-engraver.cc:371-374 make_spanner — a bracket with
///   no open spanner makes one; lily/volta-engraver.cc:493-499 add_support —
///   the chain's last end closes the spanner.
/// LP oracle (audit/lpreg/voltasky twins): the chain over D7 notes sits at
/// 7.085 above the staff top, the chain over A7 notes at 9.085 — a 2.0 split in
/// ONE system (Lily# stacker: 7.070 / 9.070).
/// </remarks>
[Trait("Category", "Unit")]
public class VoltaBracketSkylineTests
{
    [Fact]
    public void TwoRepeatChains_FitTheirOwnNotes()
    {
        var source = "octave absolute\n" +
            "|: r2 a'''4 r4 | [1. r2 d'''4 r4 | ] :| [2. r2 d'''4 r4 | ] :| [3. r2 d'''4 r4 | ] " +
            "|: r2 a'''4 r4 | [1. r2 a'''4 r4 | ] :| [2. r2 a'''4 r4 | ] :| [3. r2 a'''4 r4 | ]";
        var tree = SyntaxTree.Parse(source);
        var score = new MeasureCollector().Collect(tree);
        var layout = new LayoutEngine(new LayoutOptions()).Layout(score);

        var voltas = layout.VoltaBracketLayouts.OrderBy(v => v.StartX).ToArray();
        Assert.Equal(6, voltas.Length);

        // Chain 1 (over d''') and chain 2 (over a''', a fifth higher) each share
        // one Y — and the two Ys differ by the pitch gap 2.0, as LP fits them.
        var chain1 = voltas.Take(3).Select(v => v.YUp).Distinct().ToArray();
        var chain2 = voltas.Skip(3).Select(v => v.YUp).Distinct().ToArray();
        double y1 = Assert.Single(chain1);
        double y2 = Assert.Single(chain2);
        Assert.Equal(2.0, y2 - y1, precision: 2);
    }
}
