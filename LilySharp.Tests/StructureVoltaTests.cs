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
/// A structure-level repeat may write its endings the same way the inline volta
/// does — <c>|: A [1. D] :| [2. O]</c> — with the repeat barline between the
/// first and second endings, not after both.
/// </summary>
[Trait("Category", "Unit")]
public sealed class StructureVoltaTests
{
    private const string Head =
        "part m { clef treble }\n" +
        "section A { m { c4 c c c | } }\n" +
        "section D { m { d4 d d d | } }\n" +
        "section O { m { e4 e e e | } }\n";

    private const string Tail = "\nscore { staff m }\n";

    private static int MeasureCount(string structure)
    {
        var tree = SyntaxTree.Parse(Head + structure + Tail);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        return new MeasureCollector().Collect(tree, "m").Voice.Measures.Count();
    }

    [Fact]
    public void InlineVoltaForm_ParsesAndRenders()
    {
        // Repeat barline between the endings — the unified form.
        Assert.True(MeasureCount("structure { |: A [1. D] :| [2. O] }") > 0);
    }

    [Fact]
    public void InlineForm_EquivalentToTrailingBarlineForm()
    {
        // Both spellings describe the same repeat (A D | A O), so they expand to the
        // same number of measures.
        int trailing = MeasureCount("structure { |: A [1. D] [2. O] :| }");
        int inline = MeasureCount("structure { |: A [1. D] :| [2. O] }");
        Assert.Equal(trailing, inline);
    }

    [Fact]
    public void FirstEnding_BeforeRepeatBarline_Closes()
    {
        // The 1st ending sits before the :|, so its bracket must close with a down
        // hook at the repeat (regression: it used to stay open — only the last
        // ending closed).
        var tree = SyntaxTree.Parse(
            Head + "structure { |: A [1. D] :| [2. O] }\nscore { staff m  tab m }\n");
        var spec = RenderSpecParser.FindFirst(tree)!;
        var layout = new LayoutEngine().Layout(new MeasureCollector().CollectMultiStaff(tree, spec));

        var firstEnding = layout.VoltaBracketLayouts.First(v => v.VoltaText == "1.");
        Assert.True(firstEnding.IsClosed, "1st ending must close at the following :|");
    }

    [Theory]
    [InlineData("|: A [1. D] :| [2. O]")]   // repeat barline between endings
    [InlineData("|: A [1. D] [2. O] :|")]   // repeat barline after both endings
    public void FirstEnding_Closes_InEitherSpelling(string structure)
    {
        // The 1st ending closes (down hook) regardless of where the :| is written.
        var tree = SyntaxTree.Parse(Head + "structure { " + structure + " }\nscore { staff m  tab m }\n");
        var spec = RenderSpecParser.FindFirst(tree)!;
        var layout = new LayoutEngine().Layout(new MeasureCollector().CollectMultiStaff(tree, spec));

        Assert.True(layout.VoltaBracketLayouts.First(v => v.VoltaText == "1.").IsClosed);
    }
}
