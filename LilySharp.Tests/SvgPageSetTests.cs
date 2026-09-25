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
using System.Text.RegularExpressions;
using LilySharp.Core.Rendering.Svg;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The preview's page-wise render (R13⒝, session 404): <see cref="IncrementalCompiler.RenderIncrementalPages"/>
/// splits the interactive document into the pages the preview swaps, shifts and keeps, and
/// classifies each against the previous render. The nets are DIFFERENTIAL (RULES §7.7):
/// the pages joined must be the one-string render, a page called Same must be the previous
/// instance, and a page called Shifted must be reproduced EXACTLY by the rule the viewer
/// applies — every offset attribute mapped through the window — which is what lets the
/// language server withhold its markup.
/// </summary>
/// <remarks>
/// Poisons: classify Shifted without the number scan (return true from SameModuloWindow
/// when only the lengths agree) ⇒ the note-edit net's "after" pages fail the re-stamp
/// equality; drop the single-page wrapper in interactive mode ⇒ the single-page net; map
/// offsets inside the window instead of declining ⇒ the window net.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class SvgPageSetTests
{
    internal static readonly SvgRenderOptions Interactive = new() { EmbedFont = false, Interactive = true };
    private static readonly SvgRenderOptions Export = new() { EmbedFont = false };

    /// <summary>Six systems pinned by <c>break</c>, on pages short enough to hold two or
    /// three of them — a book of several pages whose system structure no edit below moves.</summary>
    internal static string MultiPageBook(string paperHeight = "70mm")
    {
        var bars = new[]
        {
            "c8( d e f) g8 a b c' |",
            "c'8~ c' b a g f e d |",
            "e8 f g a b( c' d' e') |",
            "e'8 d' c' b a g f e | break",
        };
        var sb = new System.Text.StringBuilder(
            "octave absolute\ntime 4/4\nkey c major\ntempo 96\n"
            + "paper { paperHeight " + paperHeight + " }\n"
            + "part melody { clef treble }\n"
            + "section Main { melody {\n");
        for (int i = 0; i < 6; i++)
            foreach (var bar in bars)
                sb.Append(bar).Append('\n');
        sb.Append("} }\nform main { Main }\nscore main \"x\" { staff melody }\n");
        return sb.ToString();
    }

    private static string Norm(string s) => s.Replace("\r\n", "\n");

    private static string Full(string text, SvgRenderOptions options)
        => Norm(SvgGenerator.Generate(SyntaxTree.Parse(text), options));

    /// <summary>The viewer's rule for a Shifted page, spelled independently of the engine:
    /// every <c>data-pos</c>/<c>data-alt</c> number of the page it holds, mapped through the
    /// window. A number the window declines fails the test — the server must not have
    /// called such a page Shifted.</summary>
    internal static string Restamp(string page, SvgEditWindow window)
        => Regex.Replace(page, "( data-(?:pos|alt)=\")([^\"]*)\"", m =>
        {
            var mapped = m.Groups[2].Value.Split(' ').Select(n =>
            {
                Assert.True(window.TryMap(int.Parse(n), out int v), $"offset {n} lies inside the window {window}");
                return v.ToString();
            });
            return m.Groups[1].Value + string.Join(' ', mapped) + "\"";
        });

    private static int ReplaceOnce(ref string text, string find, string replacement, int occurrence)
    {
        int at = -1;
        for (int k = 0; k < occurrence; k++)
        {
            at = text.IndexOf(find, at + 1, StringComparison.Ordinal);
            Assert.True(at >= 0, $"occurrence {occurrence} of '{find}' not found");
        }
        text = text[..at] + replacement + text[(at + find.Length)..];
        return at;
    }

    [Fact]
    public void ThePages_JoinToTheOneStringRender_AndEveryPageIsWrapped()
    {
        string src = MultiPageBook();
        var tree = SyntaxTree.Parse(src);
        var session = new IncrementalCompiler(tree, Interactive);
        var set = session.RenderIncrementalPages(tree, default);

        Assert.True(set.Pages.Length >= 3, $"the book should span several pages, not {set.Pages.Length}");
        Assert.Equal(Full(src, Interactive), Norm(set.ToSvg()));
        Assert.Equal(Norm(new IncrementalCompiler(tree, Interactive).RenderIncremental(tree)), Norm(set.ToSvg()));
        Assert.All(set.Changes, c => Assert.Equal(SvgPageChange.Changed, c));
        string nl = Environment.NewLine;
        foreach (var page in set.Pages)
        {
            Assert.StartsWith("<g class=\"page\" data-page-top=", page, StringComparison.Ordinal);
            Assert.EndsWith("</g>" + nl, page, StringComparison.Ordinal);
        }
        Assert.EndsWith("</svg>" + nl, set.Tail, StringComparison.Ordinal);
        Assert.DoesNotContain("<g class=\"page\"", set.Head, StringComparison.Ordinal);
    }

    [Fact]
    public void ASinglePage_IsWrappedInTheInteractiveDocument_AndBareInTheExport()
    {
        // A one-page score: the interactive document wraps it like any other page (the
        // preview keeps, shifts and swaps pages by their wrapper; without one every
        // keystroke replaced the whole picture); the export document stays bare.
        string src = MultiPageBook("0mm").Replace("break\n", "\n");
        var tree = SyntaxTree.Parse(src);

        var interactive = new IncrementalCompiler(tree, Interactive).RenderIncrementalPages(tree, default);
        Assert.Single(interactive.Pages);
        Assert.StartsWith("<g class=\"page\" data-page-top=\"0.00\"", interactive.Pages[0], StringComparison.Ordinal);
        Assert.Equal(Full(src, Interactive), Norm(interactive.ToSvg()));

        var export = new IncrementalCompiler(tree, Export).RenderIncrementalPages(tree, default);
        Assert.Single(export.Pages);
        Assert.DoesNotContain("<g class=\"page\"", export.ToSvg(), StringComparison.Ordinal);
        Assert.Equal(Full(src, Export), Norm(export.ToSvg()));
    }

    [Fact]
    public void TheSameTree_IsEveryPageSame_ByInstance()
    {
        string src = MultiPageBook();
        var tree = SyntaxTree.Parse(src);
        var session = new IncrementalCompiler(tree, Interactive);
        var first = session.RenderIncrementalPages(tree, default);
        var again = session.RenderIncrementalPages(SyntaxTree.Parse(src), default);

        Assert.Equal(first.Pages.Length, again.Pages.Length);
        for (int i = 0; i < first.Pages.Length; i++)
        {
            Assert.Equal(SvgPageChange.Same, again.Changes[i]);
            Assert.Same(first.Pages[i], again.Pages[i]);
        }
        Assert.Equal(first.Head, again.Head);
        Assert.Equal(first.Tail, again.Tail);
    }

    [Fact]
    public void AnEditThatMovesOnlyOffsets_IsEveryPageShifted_AndTheViewersShiftReproducesIt()
    {
        string src = MultiPageBook();
        var tree = SyntaxTree.Parse(src);
        var session = new IncrementalCompiler(tree, Interactive);
        var before = session.RenderIncrementalPages(tree, default);

        // Two spaces before the first note: nothing draws differently, every offset moves.
        string edited = src.Replace("melody {\n", "melody {\n  ");
        var after = session.RenderIncrementalPages(SyntaxTree.Parse(edited), default);

        Assert.Equal(Full(edited, Interactive), Norm(after.ToSvg()));
        Assert.Equal(before.Pages.Length, after.Pages.Length);
        Assert.Equal(2, after.Window.Delta);
        for (int i = 0; i < after.Pages.Length; i++)
        {
            Assert.Equal(SvgPageChange.Shifted, after.Changes[i]);
            Assert.Equal(after.Pages[i], Restamp(before.Pages[i], after.Window));
            // Negative control: a wrong window does not reproduce the page.
            Assert.NotEqual(after.Pages[i], Restamp(before.Pages[i], after.Window with { Delta = 3 }));
        }
    }

    [Fact]
    public void ANoteEdit_ChangesItsPage_KeepsThePagesBefore_AndShiftsThePagesAfter()
    {
        string src = MultiPageBook();
        var tree = SyntaxTree.Parse(src);
        var session = new IncrementalCompiler(tree, Interactive);
        var before = session.RenderIncrementalPages(tree, default);
        Assert.True(before.Pages.Length >= 3);

        // A note in the fourth system (the third repetition of its bar): one page's drawing
        // changes; the pages before it are untouched, the pages after only move offsets.
        string edited = src;
        ReplaceOnce(ref edited, "e8 f g a b( c' d' e') |", "e8 f gis a b( c' d' e') |", 4);
        var after = session.RenderIncrementalPages(SyntaxTree.Parse(edited), default);

        Assert.Equal(Full(edited, Interactive), Norm(after.ToSvg()));
        Assert.Equal(before.Pages.Length, after.Pages.Length);
        int changed = after.Changes.IndexOf(SvgPageChange.Changed);
        Assert.True(changed > 0 && changed < after.Pages.Length - 1,
            $"the edited page should be an inner one: {string.Join(",", after.Changes)}");
        Assert.Equal(1, after.Changes.Count(c => c == SvgPageChange.Changed));
        for (int i = 0; i < after.Pages.Length; i++)
        {
            if (i < changed)
            {
                Assert.Equal(SvgPageChange.Same, after.Changes[i]);
                Assert.Same(before.Pages[i], after.Pages[i]);
            }
            else if (i > changed)
            {
                Assert.Equal(SvgPageChange.Shifted, after.Changes[i]);
                Assert.Equal(after.Pages[i], Restamp(before.Pages[i], after.Window));
            }
        }
        Assert.NotEqual(before.Pages[changed], after.Pages[changed]);
    }

    [Fact]
    public void AOneStringRender_ForgetsThePreviousPages()
    {
        string src = MultiPageBook();
        var tree = SyntaxTree.Parse(src);
        var session = new IncrementalCompiler(tree, Interactive);
        session.RenderIncrementalPages(tree, default);
        session.RenderIncremental(tree);
        var set = session.RenderIncrementalPages(tree, default);
        Assert.All(set.Changes, c => Assert.Equal(SvgPageChange.Changed, c));
    }

    [Fact]
    public void APageCountChange_IsEveryPageChanged()
    {
        string src = MultiPageBook();
        var tree = SyntaxTree.Parse(src);
        var session = new IncrementalCompiler(tree, Interactive);
        var before = session.RenderIncrementalPages(tree, default);

        string edited = src.Replace("paperHeight 70mm", "paperHeight 45mm");
        var after = session.RenderIncrementalPages(SyntaxTree.Parse(edited), default);
        Assert.NotEqual(before.Pages.Length, after.Pages.Length);
        Assert.All(after.Changes, c => Assert.Equal(SvgPageChange.Changed, c));
        Assert.Equal(Full(edited, Interactive), Norm(after.ToSvg()));
    }

    [Fact]
    public void TheWindowMap_IsTheFragmentMemosRule()
    {
        var w = new SvgEditWindow(Prefix: 10, SuffixStart: 14, Delta: 3);
        Assert.True(w.TryMap(9, out int v) && v == 9);
        Assert.False(w.TryMap(10, out _));
        Assert.False(w.TryMap(13, out _));
        Assert.True(w.TryMap(14, out v) && v == 17);
        var none = SvgEditWindow.None(20);
        Assert.True(none.TryMap(0, out v) && v == 0);
        Assert.True(none.TryMap(19, out v) && v == 19);
    }

    [Theory]
    [InlineData("<t data-pos=\"5\"/>", "<t data-pos=\"5\"/>", true)]
    [InlineData("<t data-pos=\"14\"/>", "<t data-pos=\"17\"/>", true)]
    [InlineData("<t data-pos=\"14\"/>", "<t data-pos=\"16\"/>", false)]   // wrong shift
    [InlineData("<t data-pos=\"11\"/>", "<t data-pos=\"11\"/>", false)]   // inside the window
    [InlineData("<t data-pos=\"5\" data-alt=\"3 14 20\"/>", "<t data-pos=\"5\" data-alt=\"3 17 23\"/>", true)]
    [InlineData("<t data-pos=\"5\" data-alt=\"3 14 20\"/>", "<t data-pos=\"5\" data-alt=\"3 17\"/>", false)] // a number missing
    [InlineData("<t x=\"1\" data-pos=\"5\"/>", "<t x=\"2\" data-pos=\"5\"/>", false)]   // ink differs
    [InlineData("<t data-pos=\"\"/>", "<t data-pos=\"\"/>", false)]       // no number where one belongs
    [InlineData("<t>data-pos=\"14\"</t>", "<t>data-pos=\"17\"</t>", false)] // a lyric spelling the attribute: not the emitted token
    public void SameModuloWindow_AcceptsExactlyTheShiftedText(string previous, string current, bool expected)
    {
        var w = new SvgEditWindow(Prefix: 10, SuffixStart: 14, Delta: 3);
        Assert.Equal(expected, SvgPageSet.SameModuloWindow(previous, current, w));
        Assert.Equal(expected, CharWalk(previous, current, w));
    }

    /// <summary>
    /// The span compare (session 605) answers what the character walk it replaced answered, on
    /// real pages: every net book's pages before and after a trivia insertion in the middle
    /// (whose later pages are Shifted) and a pitch change (whose page is Changed).
    /// </summary>
    [Fact]
    public void SameModuloWindow_AnswersAsTheCharacterWalkDid_OnRenderedPages()
    {
        int pairs = 0, same = 0;
        var failures = new List<string>();
        foreach (var path in CollectResumeTests.NetBooks().Take(60))
        {
            string src;
            try { src = File.ReadAllText(path); } catch { continue; }
            int mid = src.IndexOf('\n', src.Length / 2);
            if (mid < 0) continue;
            var edits = new List<string> { src.Insert(mid + 1, " ") };
            int note = src.IndexOf(" c4", src.Length / 2, StringComparison.Ordinal);
            if (note >= 0) edits.Add(src.Remove(note, 3).Insert(note, " d4"));
            foreach (var edited in edits)
            {
                SvgPageSet before, after;
                try
                {
                    var session = new IncrementalCompiler(SyntaxTree.Parse(src), Interactive);
                    before = session.RenderIncrementalPages(SyntaxTree.Parse(src), default);
                    after = session.RenderIncrementalPages(SyntaxTree.Parse(edited), default);
                }
                catch { continue; }
                var (prefix, suffixStart, delta) = LilySharp.Core.Svg.Collector.CollectResumePlanner.ComputeWindow(src, edited);
                var w = new SvgEditWindow(prefix, suffixStart, delta);
                for (int i = 0; i < Math.Min(before.Pages.Length, after.Pages.Length); i++)
                {
                    bool want = CharWalk(before.Pages[i], after.Pages[i], w);
                    bool got = SvgPageSet.SameModuloWindow(before.Pages[i], after.Pages[i], w);
                    pairs++;
                    if (want) same++;
                    if (want != got)
                        failures.Add($"{Path.GetFileName(path)} page {i}: span {got} != walk {want}");
                }
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(20)));
        Assert.True(pairs >= 60 && same >= 10 && pairs - same >= 10,
            $"the net did not bite: {pairs} pairs, {same} shifted-or-same");
    }

    // The character walk SameModuloWindow was until session 605 — verbatim, the oracle.
    private static bool CharWalk(string previous, string current, SvgEditWindow window)
    {
        const string Pos = " data-pos=\"";
        const string Alt = " data-alt=\"";
        int i = 0, j = 0;
        while (i < previous.Length && j < current.Length)
        {
            char c = previous[i];
            if (c != current[j])
                return false;
            i++;
            j++;
            if (c != '"' || i < Pos.Length)
                continue;
            var opener = previous.AsSpan(i - Pos.Length, Pos.Length);
            bool isAlt = opener.SequenceEqual(Alt);
            if (!isAlt && !opener.SequenceEqual(Pos))
                continue;
            while (true)
            {
                if (!ReadInt(previous, ref i, out int was) || !ReadInt(current, ref j, out int now))
                    return false;
                if (!window.TryMap(was, out int mapped) || mapped != now)
                    return false;
                if (!isAlt || i >= previous.Length || previous[i] != ' ' || j >= current.Length || current[j] != ' ')
                    break;
                i++;
                j++;
            }
        }
        return i == previous.Length && j == current.Length;
    }

    private static bool ReadInt(string s, ref int i, out int value)
    {
        int start = i;
        bool negative = i < s.Length && s[i] == '-';
        if (negative)
            i++;
        long acc = 0;
        while (i < s.Length && s[i] is >= '0' and <= '9')
        {
            acc = acc * 10 + (s[i] - '0');
            i++;
        }
        value = (int)(negative ? -acc : acc);
        return i > start + (negative ? 1 : 0);
    }
}
