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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The render path of a keystroke never builds the tree's <see cref="DescendantIndex"/>: that
/// materializes every red in the book, on every keystroke (its remarks say so). A question about
/// the whole book asked there goes through a green finder (<see cref="SyntaxNode.KindSites"/>,
/// <see cref="SyntaxNode.GreenSites"/>). Twice a whole-book question slipped in unnoticed, because
/// the output does not change: SectionMeterPlan.WritesTimeInMusic and SectionBarCounts.PhraseBodies.
/// </summary>
public class KeystrokeStaysRedFreeTests
{
    private const string Head = "octave absolute\ntime 4/4\npart v { clef treble }\nsection Main {\n  v {\n";
    private const string Tail = "  }\n}\n\nform { ~Main }\n\nscore { staff v }\n";

    private static bool RenderBuildsTheIndex(string text, string edited)
    {
        var options = new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false };
        var compiler = new IncrementalCompiler(SyntaxTree.Parse(text), options);
        compiler.RenderIncremental(SyntaxTree.Parse(text));
        var tree = SyntaxTree.Parse(edited);
        compiler.RenderIncremental(tree);
        return ((CompilationUnitSyntax)tree.GetRoot()).DescendantIndexBuilt;
    }

    private static string Bars(string bar, int count)
        => Head + string.Concat(Enumerable.Repeat("    " + bar + " |\n", count)) + Tail;

    [Fact]
    public void AKeystroke_InABookOfNotes_BuildsNoIndex()
    {
        var text = Bars("c'4 d'4 e'4 f'4", 40);
        int at = text.LastIndexOf("d'4", System.StringComparison.Ordinal);
        Assert.False(RenderBuildsTheIndex(text, text.Remove(at, 3).Insert(at, "e'4")));
    }

    [Fact]
    public void AKeystroke_InABarThatAsksItsWrittenBar_BuildsNoIndex()
    {
        // The last bar has no bar line after its fill, so the fill asks MeasureCollector.WrittenBarAt
        // about the written bars, which reads the phrase table.
        var text = Head + string.Concat(Enumerable.Repeat("    c'4 d'4 e'4 f'4 |\n", 40)) + "    c'4 d'4 e'4 f'4\n" + Tail;
        int at = text.LastIndexOf("d'4", System.StringComparison.Ordinal);
        Assert.False(RenderBuildsTheIndex(text, text.Remove(at, 3).Insert(at, "e'4")));
    }

    [Fact]
    public void ABookThatWritesATimeInItsMusic_MayBuildIt()
    {
        // The other side of the first test: the plan is really built here, so the question is
        // still answered, and the seam the tests above read does report a build.
        var text = Bars("c'4 d'4 e'4 f'4", 4).Replace("    c'4", "    time 3/4 c'4 d'4 e'4 |\n    time 4/4 c'4");
        int at = text.LastIndexOf("d'4", System.StringComparison.Ordinal);
        Assert.True(RenderBuildsTheIndex(text, text.Remove(at, 3).Insert(at, "e'4")));
    }
}
