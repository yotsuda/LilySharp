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

    [Theory]
    [InlineData("LilySharp.Tests/Fixtures/showcase/06-meter-changes.lys")]   // a time in the music
    [InlineData("LilySharp.Lsp/Lessons/en/lesson-07-lyrics.lys")]            // lyrics
    [InlineData("LilySharp.Tests/Fixtures/test/lead-sheet-lyrics.lys")]      // lyrics on a lead sheet
    [InlineData("LilySharp.Lsp/Lessons/en/lesson-08-chords.lys")]            // a chord track
    [InlineData("LilySharp.Tests/Fixtures/test/arpeggio-second.lys")]        // a staff that may name a part
    public void AKeystroke_InABookThatAsksAboutTheWholeBook_BuildsNoIndex(string book)
    {
        var text = System.IO.File.ReadAllText(System.IO.Path.Combine(CollectResumeTests.FindRepoRoot(), book));
        var note = new System.Text.RegularExpressions.Regex(@"(?<=[ \t\r\n|{(<])([a-f])([',]*)(1|2|4|8|16)?(?=[ \t\r\n|)>~(\]\[@.])");
        var m = note.Match(text, text.Length / 2) is { Success: true } later ? later : note.Match(text);
        Assert.True(m.Success, "no note to edit");
        Assert.False(RenderBuildsTheIndex(text, text.Remove(m.Index, 1).Insert(m.Index, ((char)(m.Value[0] + 1)).ToString())));
    }

    [Fact]
    public void TheSeamReportsABuild_AndTheQuestionAnswersTheSameEitherWay()
    {
        var root = (CompilationUnitSyntax)SyntaxTree.Parse(Bars("c'4 d'4 e'4 f'4", 2)).GetRoot();
        var kinds = new[] { SyntaxKind.Note, SyntaxKind.Barline, SyntaxKind.PartDeclaration };
        var green = root.SitesOfKinds(kinds).ToArray();
        Assert.False(root.DescendantIndexBuilt);
        var red = root.DescendantNodesOfKinds(kinds).ToArray();
        Assert.True(root.DescendantIndexBuilt);
        Assert.Equal(red.Length, green.Length);
        Assert.NotEmpty(green);
        for (int i = 0; i < red.Length; i++)
            Assert.Same(red[i], green[i]);
        Assert.Equal(red.Length, root.SitesOfKinds(kinds).Count());
    }
}
