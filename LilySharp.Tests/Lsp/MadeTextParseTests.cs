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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// The texts the server MAKES of a document — its <c>using</c>s resolved, its singers' parts
/// written after it — on a keystroke (review of session 1291, P8). Each was parsed whole on
/// every keystroke, and so were the main text and every include, only to find their
/// <c>using</c>s. Now no text is parsed whole: the made text is parsed from the made tree of
/// the keystroke before (<c>RecentTrees</c>), and what comes out is the tree a whole parse
/// makes. The poisons: <c>SyntaxTree.Parse</c> in the place of <c>RecentTrees.Parse</c>, and
/// the expander left to find the directives itself.
/// </summary>
[Trait("Category", "Unit")]
public sealed class MadeTextParseTests : IDisposable
{
    // A store of this test's own: the server's is every test's at once, and it keeps few trees
    // (xUnit makes the instance, runs the test and disposes of it on one thread: none awaits).
    public MadeTextParseTests() => RecentTrees.OfThisThread = new RecentTrees();

    public void Dispose() => RecentTrees.OfThisThread = null;

    private const string Main =
        """
        using "parts.lys"

        time 4/4

        form { ~A B }

        score { staff melody }
        """;

    private const string Parts =
        """
        part melody { clef treble }

        section A {
          melody { c'4 d e f | }
        }

        section B {
          melody { g'4 f e d | }
        }
        """;

    private static (SyntaxTree Tree, IReadOnlyList<Diagnostic> Diagnostics) Expand(
        SyntaxTree main, Dictionary<string, string> files)
        => LilySharpLanguageServer.ExpandUsings(main.Text, main, "C:/proj/main.lys",
            p => files.TryGetValue(Path.GetFileName(p), out var t) ? t : null);

    /// <summary>The tree after one keystroke: <paramref name="find"/> becomes <paramref name="typed"/>.</summary>
    private static SyntaxTree Typed(SyntaxTree tree, string find, string typed)
    {
        int at = tree.Text.IndexOf(find, StringComparison.Ordinal);
        Assert.True(at >= 0, find);
        return tree.WithChange(new TextChange(new TextSpan(at, find.Length), typed));
    }

    [Fact]
    public void AKeystrokeInABookWithIncludes_ParsesNoTextWhole_AndTheTreeIsTheWholeParses()
    {
        var files = new Dictionary<string, string> { ["parts.lys"] = Parts };
        // (A text of this test's own, so that the first call is not another test's.)
        var main = SyntaxTree.Parse(Main + "\n// made text parse: includes\n");
        Expand(main, files);

        var typed = Typed(main, "4/4", "3/4");
        int before = SyntaxTree.FullParsesOnThisThread;
        int twoWindows = RecentTrees.TwoWindowParsesOnThisThread;
        var (tree, _) = Expand(typed, files);
        int parses = SyntaxTree.FullParsesOnThisThread - before;

        // Not the main text, not the include, not the two together.
        Assert.Equal(0, parses);
        // …and the include stands as it was: one window, the keystroke's, and no second text
        // of the book and its includes made to find it.
        Assert.Equal(0, RecentTrees.TwoWindowParsesOnThisThread - twoWindows);
        // form, score, and the include's part and two sections: taken as they stood.
        Assert.True(tree.AdoptedTopLevelItems >= 5, $"adopted {tree.AdoptedTopLevelItems}");
        Assert.Contains("section B", tree.Text, StringComparison.Ordinal);
        IncrementalParseTests.AssertTreesEquivalent(SyntaxTree.Parse(tree.Text), tree);
    }

    [Fact]
    public void AnIncludeSaved_IsReadForItsUsingsOnce_AndTheTreeIsTheWholeParses()
    {
        var files = new Dictionary<string, string> { ["parts.lys"] = Parts + "\n// made text parse: saved\n" };
        var main = SyntaxTree.Parse(Main + "\n// made text parse: saved\n");
        Expand(main, files);

        // The include is saved with a section more, and a file of its own to include.
        files["parts.lys"] = "using \"deep.lys\"\n" + files["parts.lys"] + "\nsection C {\n  melody { c'1 | }\n}\n";
        files["deep.lys"] = "part bass { clef bass }\n";
        int before = SyntaxTree.FullParsesOnThisThread;
        var (tree, _) = Expand(main, files);
        // The two new contents, each for its directives.
        Assert.Equal(2, SyntaxTree.FullParsesOnThisThread - before);
        Assert.Contains("part bass", tree.Text, StringComparison.Ordinal);
        IncrementalParseTests.AssertTreesEquivalent(SyntaxTree.Parse(tree.Text), tree);

        // …and a keystroke in the document after it reads neither again.
        var typed = Typed(main, "4/4", "3/4");
        before = SyntaxTree.FullParsesOnThisThread;
        var (next, _) = Expand(typed, files);
        Assert.Equal(0, SyntaxTree.FullParsesOnThisThread - before);
        IncrementalParseTests.AssertTreesEquivalent(SyntaxTree.Parse(next.Text), next);
    }

    private const string Singers =
        """
        octave relative
        time 4/4
        key c major
        language english
        part lead { instrument alto  lyrics words }
        section Verse {
          lead { e'4 e d c | }
          lyrics words { one two three four | }
        }
        section Chorus {
          lead { g'4 g a g | }
          lyrics words { la la la la | }
        }
        form { Verse Chorus }
        singer carol female sings on lead up 3 in Chorus
        score { staff lead  staff carol  lyrics carol }
        """;

    [Fact]
    public void AKeystrokeInABookThatPrintsASinger_ParsesTheMadeTextFromTheOneBefore()
    {
        var main = SyntaxTree.Parse(Singers + "\n// made text parse: singer\n");
        var (first, _) = Expand(main, []);
        Assert.Contains(LilySharp.Core.Editing.SingerParts.Marker, first.Text, StringComparison.Ordinal);

        // A note of the Verse, which carol does not sing: her part's bars there stay at rest.
        var typed = Typed(main, "e'4 e d c", "e'4 e d d");
        int twoWindows = RecentTrees.TwoWindowParsesOnThisThread;
        var (tree, _) = Expand(typed, []);
        // What is written after the text did not move: one window.
        Assert.Equal(0, RecentTrees.TwoWindowParsesOnThisThread - twoWindows);

        Assert.Contains("e'4 e d d", tree.Text, StringComparison.Ordinal);
        Assert.Contains(LilySharp.Core.Editing.SingerParts.Marker, tree.Text, StringComparison.Ordinal);
        // The Chorus, the form, the singer's line, the score, and what was written after them.
        Assert.True(tree.AdoptedTopLevelItems >= 5, $"adopted {tree.AdoptedTopLevelItems}");
        IncrementalParseTests.AssertTreesEquivalent(SyntaxTree.Parse(tree.Text), tree);

        // A note of the Chorus, which she sings a third above: it is written again in her part
        // at the file's end, and the two places are two windows, not one from the Chorus to
        // there — the form, her line and the score between them are taken as they stood.
        var sung = Typed(typed, "g'4 g a g", "g'4 g a b");
        var (moved, _) = Expand(sung, []);
        Assert.Equal(1, RecentTrees.TwoWindowParsesOnThisThread - twoWindows);
        Assert.Contains("b'4 b c d", moved.Text[LilySharp.Core.Editing.SingerParts.MadeFrom(moved.Text)..], StringComparison.Ordinal);
        Assert.True(moved.AdoptedTopLevelItems >= 9, $"adopted {moved.AdoptedTopLevelItems}");
        IncrementalParseTests.AssertTreesEquivalent(SyntaxTree.Parse(moved.Text), moved);
    }

    /// <summary>
    /// The made trees of one keystroke after another share what the keystroke left alone, as
    /// a document's own trees do, so the preview keeps what it drew of that
    /// (<see cref="LilySharp.Core.Svg.IncrementalCompiler"/>): each page is the full render's.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThePreviewOfAMadeText_DrawsWhatTheFullRenderDraws_KeystrokeAfterKeystroke(bool singers)
    {
        var options = new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false };
        var files = new Dictionary<string, string> { ["parts.lys"] = Parts };
        (string Find, string Typed)[] keys = singers
            ?
            [
                ("e'4 e d c", "e'4 e d d"),          // a note carol does not sing
                ("g'4 g a g", "g'4 g a b"),          // one she sings a third above
                ("la la la la", "la la la lo"),      // her words
                ("up 3", "up 4"),                    // her line
                ("g'4 g a b |", "g'4 g a b | g1 |"), // a bar more
                ("form { Verse Chorus }", "form { Verse Chorus Verse }"),
            ]
            :
            [
                ("4/4", "3/4"),
                ("3/4", "4/4"),
                ("form { ~A B }", "form { ~A B ~A }"),
                ("score { staff melody }", "score { staff melody  staff melody }"),
            ];

        var main = SyntaxTree.Parse((singers ? Singers : Main) + "\n// made text parse: preview\n");
        var (made, _) = Expand(main, files);
        var typing = new LilySharp.Core.Svg.IncrementalCompiler(made, options);
        typing.Render();
        int step = 0;
        foreach (var (find, typed) in keys)
        {
            main = Typed(main, find, typed);
            (made, _) = Expand(main, files);
            Assert.False(made.HasErrors, $"step {step}: " + string.Join("\n", made.Diagnostics.Select(d => d.Message)));
            Assert.True(made.AdoptedTopLevelItems > 0, $"step {step}: nothing taken from the tree before");
            string full = LilySharp.Core.Svg.SvgGenerator.Generate(SyntaxTree.Parse(made.Text), options);
            Assert.True(full == typing.RenderIncremental(made), $"step {step}: the preview is not the full render ({find} -> {typed})");
            step++;
        }
    }

    [Fact]
    public void TwoBooksTypedInTurn_EachIsParsedFromItsOwnTreeBefore()
    {
        // Two previews open: what is kept is not one tree, and each text finds its own.
        var files = new Dictionary<string, string> { ["parts.lys"] = Parts };
        var book = SyntaxTree.Parse(Main + "\n// made text parse: in turn\n");
        var choir = SyntaxTree.Parse(Singers + "\n// made text parse: in turn\n");
        Expand(book, files);
        Expand(choir, []);

        (string Find, string Typed)[] bookKeys = [("4/4", "3/4"), ("3/4", "2/4"), ("2/4", "4/4")];
        (string Find, string Typed)[] choirKeys = [("e'4 e d c", "e'4 e d d"), ("e'4 e d d", "e'4 e d e"), ("e'4 e d e", "e'4 e d c")];
        for (int i = 0; i < bookKeys.Length; i++)
        {
            book = Typed(book, bookKeys[i].Find, bookKeys[i].Typed);
            int before = SyntaxTree.FullParsesOnThisThread;
            var (made, _) = Expand(book, files);
            Assert.Equal(0, SyntaxTree.FullParsesOnThisThread - before);
            Assert.True(made.AdoptedTopLevelItems >= 5, $"key {i}: the book adopted {made.AdoptedTopLevelItems}");

            choir = Typed(choir, choirKeys[i].Find, choirKeys[i].Typed);
            var (sung, _) = Expand(choir, []);
            Assert.True(sung.AdoptedTopLevelItems >= 5, $"key {i}: the choir adopted {sung.AdoptedTopLevelItems}");
            IncrementalParseTests.AssertTreesEquivalent(SyntaxTree.Parse(sung.Text), sung);
        }
    }

    [Fact]
    public void TheSameMadeTextOfAnotherDocument_IsATreeOfItsOwn()
    {
        // What is kept by the tree (the diagnostics of a document) is kept for one document.
        var files = new Dictionary<string, string> { ["parts.lys"] = Parts };
        var one = SyntaxTree.Parse(Main + "\n// made text parse: twice\n");
        var other = SyntaxTree.Parse(new string(one.Text.AsSpan()));
        var (first, _) = Expand(one, files);
        var (second, _) = Expand(other, files);

        Assert.Equal(first.Text, second.Text);
        Assert.NotSame(first, second);
    }

    [Theory]
    [InlineData("time 4/4\nscore { staff a }\n", "time 4/4\nscore { staff a }\n")]
    [InlineData("time 4/4\nscore { staff a }\n", "time 3/4\nscore { staff a }\n")]
    [InlineData("part a { }\npart b { }\n", "part a { }\npart b { }\npart b { }\n")]
    [InlineData("part a { }\npart b { }\npart b { }\n", "part a { }\npart b { }\n")]
    [InlineData("aaaa", "aa")]
    [InlineData("", "part a { clef treble }\n")]
    [InlineData("part a { clef treble }\n", "")]
    [InlineData("part a { clef treble }\nsection A { a { c'4 | } }\n", "part a { clef bass }\nsection A { a { c'2 | } }\n")]
    public void ParseChanged_FindsTheWindowItself_AndMatchesTheWholeParse(string before, string after)
    {
        var old = SyntaxTree.Parse(before);
        var changed = SyntaxTree.ParseChanged(old, after);

        IncrementalParseTests.AssertTreesEquivalent(SyntaxTree.Parse(after), changed);
        if (before == after)
            Assert.Same(old, changed);

        var (head, tail) = SyntaxTree.SharedEnds(before, after);
        Assert.True(head + tail <= Math.Min(before.Length, after.Length));
        Assert.Equal(before[..head], after[..head]);
        Assert.Equal(before[^tail..], after[^tail..]);
    }
}
