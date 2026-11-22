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
/// A part sheet's compressed rest ends where another part's rit. starts and where it stops, so
/// the rit. stands over its own bar (evaluation 5, orchestra r-01). Until 2026-11-22 the horn's
/// R*3 ran through the cello's rit. and the rit. was pushed onto the next bar, over its tempo
/// mark. LilyPond 2.26.0 (Lab sessions/p1366/a15/twin.ly): R1*3 beside a rit. from its 2nd to
/// its 3rd bar prints 1, 1 with the rit., 1.
/// </summary>
[Trait("Category", "Unit")]
public class MultiRestSplitAtTextSpanTests
{
    private const string Book =
        "octave absolute\npitch concert\npart hn { instrument horn }\npart vc { instrument cello }\n" +
        "section A { hn { c'1 | R*3 | } vc { c1 | c1 | c1@rit | c1@!rit | } }\n" +
        "section B { tempo \"Allegro\" 4 = 132  hn { c'1 | c'1 | } vc { c1 | c1 | } }\n" +
        "form { ~A B }\nscore { staff hn  staff vc }\n" +
        "score horn pitch transposed { layout { compressRests true } staff hn }\n";

    [Fact]
    public void TheHornsRest_EndsAtTheRitsStartAndStop()
    {
        var tree = SyntaxTree.Parse(Book);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var horn = RenderSpecParser.FindAll(tree).ElementAt(1);
        var layout = new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, horn));
        Assert.Equal(new[] { (1, 1), (2, 1), (3, 1) },
            layout.MultiMeasureRestLayouts.OrderBy(r => r.StartMeasureIndex).Select(r => (r.StartMeasureIndex, r.MeasureCount)));
    }
}
