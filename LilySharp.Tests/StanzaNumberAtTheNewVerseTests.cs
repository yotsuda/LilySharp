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
/// A written verse number stands where its verse begins, also when the next section's verse 1
/// begins on the system the earlier section's verse 1 runs on to (evaluation 5, jpop r-06).
/// Until 2026-11-22 the system's "1." and "2." went to its first syllables — the earlier
/// section's continuing words — and none stood where the new verses began.
/// </summary>
[Trait("Category", "Unit")]
public class StanzaNumberAtTheNewVerseTests
{
    private const string Book =
        "octave absolute\npart vo { instrument soprano  lyrics words }\n" +
        "section A {\n  vo { d'4 e' fis' g' | a'1 | break b'4 a' g' fis' | e'1 | }\n" +
        "  lyrics words {\n    verse 1 { あ い う え | お | か き く け | こ | }\n" +
        "    verse 2 { た ち つ て | と | な に ぬ ね | の | }\n  }\n}\n" +
        "section B {\n  vo { a'4 b' cis'' d'' | e''1 | }\n" +
        "  lyrics words { verse 1 { ま み む め | も | } verse 2 { や ゆ よ わ | を | } }\n}\n" +
        "form { A B }\nscore { staff vo  lyrics words }\n";

    [Fact]
    public void TheSecondSystemsNumbers_StandAtSectionB()
    {
        var tree = SyntaxTree.Parse(Book);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var layout = new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
        var numbers = layout.StanzaNumberLayouts.OrderBy(n => n.SystemIndex).ThenBy(n => n.Text).ToList();
        // System 1: A's "1." and "2."; system 2: B's, at bar 5 (index 4) — none at A's bar 3.
        Assert.Equal(new[] { (0, "1.", 0), (0, "2.", 0), (1, "1.", 4), (1, "2.", 4) },
            numbers.Select(n => (n.SystemIndex, n.Text, n.MeasureIndex)));
    }
}
