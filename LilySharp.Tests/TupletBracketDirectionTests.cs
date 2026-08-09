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
/// Tuplet bracket direction is the majority of the stem directions under the
/// bracket. Equal counts tiebreak on the extremal head positions — the side
/// whose extreme head protrudes deeper past the staff edge in its own direction
/// wins (a down-stem C6 outweighs an up-stem F4) — and a tuplet with no stems
/// at all goes UP.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/tuplet-bracket.cc:779-817 get_default_dir.
/// LP oracle: the nine tuplets of tuplet-bracket-direction.ly render
/// UP DOWN UP DOWN UP UP DOWN UP DOWN (scratch/lpreg/tupdir* twins, hooks read
/// from the SVG). The fourth (C6 + F4, one stem each way) is the tiebreak's
/// pinned case: bare "equal → UP" put it above.
/// </remarks>
[Trait("Category", "Unit")]
public class TupletBracketDirectionTests
{
    [Fact]
    public void NineBookTuplets_MatchLpDirections()
    {
        // The book's music in the bare fragment's relative-from-C4 (the LP
        // original is \relative c'', so only the FIRST pitched note gains an
        // apostrophe; every later spelling is the book's own).
        var source =
            "tuplet 3/2 { r4 r4 r4 } tuplet 3/2 { r4 c'4 r4 } | " +
            "tuplet 3/2 { r4 a4 r4 } tuplet 3/2 { c'4 f,,4 r4 } | " +
            "tuplet 3/2 { f,4 c''4 r4 } tuplet 3/2 { a4 a4 c4 } | " +
            "tuplet 3/2 { c4 c4 a4 } tuplet 3/2 { a4 a4 a4 } | " +
            "tuplet 3/2 { c4 c4 c4 } r2 |";
        var tree = SyntaxTree.Parse(source);
        var score = new MeasureCollector().Collect(tree);
        var layout = new LayoutEngine(new LayoutOptions()).Layout(score);

        var dirs = layout.TupletBracketLayouts
            .OrderBy(b => b.StartX).Select(b => b.IsStemUp).ToArray();
        Assert.Equal(
            new[] { true, false, true, false, true, true, false, true, false },
            dirs);
    }
}
