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

using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The differential net for the last three WHOLE-TREE FLAT WALKS of the diagnostics pass,
/// folded onto the tree's descendant index in session 410 (HANDOFF §2 R13⒮).
/// </summary>
/// <remarks>
/// <para>
/// MEASURED before the fold: the pass ran <c>root.DescendantNodes()</c> three times per
/// settled keystroke — <c>MeasureValidator.CollectPhraseBodies</c>,
/// <see cref="SectionBarCounts.SemanticVoices"/> and <see cref="SectionBarCounts.LyricsCells"/>
/// — in every one of the three perf books AND in all 330 books of the owner's corpus. On
/// perf-fingbeam1k that is 3 x 234,030 = 702,090 node visits to find a few dozen nodes, and
/// it was the reason the index had to materialize every red node in the book. None of the
/// three showed on a millisecond list: a flat array walk costs ~2.2 ns/node (session 408).
/// </para>
/// <para>
/// WHAT IS NETTED. Each loop's body is unchanged, so the only thing the fold could break is
/// WHICH NODES the loop is offered and in WHAT ORDER — a kind missing from a list makes a
/// reader go silent on a spelling it used to report, and no test of the diagnostic itself
/// need fail. So this compares the plain walk's answer with the index's, node for node, in
/// order, over every net book plus a book that writes every spelling. The phrase-body list
/// itself (<c>PhraseCycleValidator.DeclaringKinds</c>, which now has three readers) is
/// already pinned this way by <c>TailValidatorKindsTests</c>.
/// </para>
/// </remarks>
public sealed class FlatWalkKindsTests
{
    private static IEnumerable<(string Name, CompilationUnitSyntax Root)> Roots()
    {
        foreach (var path in CollectResumeTests.NetBooks())
        {
            var text = File.ReadAllText(path);
            if (text.Contains("using \"", StringComparison.Ordinal))
                continue;
            yield return (Path.GetFileName(path), SyntaxTree.Parse(text).GetRoot());
        }
        yield return ("<every spelling>", SyntaxTree.Parse(EverySpelling).GetRoot());
    }

    /// <summary>A book that writes every spelling the three loops look for: a score-level
    /// <c>time</c> and one inside a section, a part-major section, a section-major section
    /// with part and chord blocks, a chords track, a lyrics cell, and a phrase and a
    /// variable — so no list is declared safe by a net that never exercises it.</summary>
    private const string EverySpelling = """
        time 4/4
        variable riffBody = { c'4 d' e' f' | }
        phrase riff { c'4 d' e' f' | }
        part melody { clef treble }
        part bass { clef bass }
        chords rhythm { section A { C4 F4 | } }
        lyrics w { section A { la la la la | } }
        section A {
          time 3/4
          melody { c'4 d' e' | }
          bass { c4 d e | }
        }
        section B {
          melody { c'4 d' e' f' | }
        }
        form main { A B }
        score main { staff melody with lyrics w  staff bass }
        """;

    /// <summary>The nodes <see cref="SectionBarCounts.SemanticVoices"/> is offered: the
    /// old whole-tree walk, kept here as the reference the index is measured against.
    /// Spelled with the TYPES, not with the kind list, so the two cannot agree for free.</summary>
    [Fact]
    public void SemanticVoices_IsOfferedWhatThePlainWalkFound()
    {
        int times = 0, sections = 0;
        foreach (var (book, root) in Roots())
        {
            var walked = Walk(root, n => n is TimeSignatureSyntax or SectionDeclarationSyntax);
            Assert.Equal(walked, root.DescendantNodesOfKinds(SectionBarCounts.SemanticVoiceKinds).ToList());
            times += walked.Count(n => n is TimeSignatureSyntax);
            sections += walked.Count(n => n is SectionDeclarationSyntax);
        }
        Assert.True(times > 0, "no time signature in the net");
        Assert.True(sections > 0, "no section declaration in the net");
    }

    /// <summary>
    /// <see cref="SectionBarCounts.LyricsCells"/> keeps no list at all — it asks the index
    /// for the one type its test names — so what is netted is that the typed answer IS the
    /// walk's. (It also pins that the typed lookup is served from the kind bucket rather
    /// than from a scan of the flat list: a scan would be right, and would give back the
    /// whole-tree visit this fold removed.)
    /// </summary>
    [Fact]
    public void LyricsCells_IsOfferedWhatThePlainWalkFound()
    {
        int seen = 0;
        foreach (var (_, root) in Roots())
        {
            var walked = Walk(root, n => n is SectionDeclarationSyntax);
            Assert.Equal(walked, root.DescendantNodes<SectionDeclarationSyntax>().ToList<SyntaxNode>());
            seen += walked.Count;
        }
        Assert.True(seen > 0, "no section declaration in the net");
        Assert.True(typeof(SectionDeclarationSyntax).IsSealed,
            "SectionDeclarationSyntax stopped being sealed: DescendantIndex.OfType falls back to "
            + "scanning the whole flat list, which is the whole-tree walk this fold removed");
    }

    /// <summary>And the cells <see cref="SectionBarCounts.LyricsCells"/> reports are the
    /// ones the old whole-tree walk found — the expected list is built HERE, from the flat
    /// walk and the type test, so it cannot agree with the production answer for free.
    /// (<see cref="SemanticVoices"/> needs no such test: its loop body is unchanged, so the
    /// node stream the test above pins is its only degree of freedom.)</summary>
    [Fact]
    public void TheCellsThemselves_AreTheOnesThePlainWalkFound()
    {
        int cells = 0;
        foreach (var (_, root) in Roots())
        {
            var expected = Walk(root, n => n is SectionDeclarationSyntax { Parent: LyricsBlockSyntax });
            var actual = SectionBarCounts.LyricsCells(root);
            Assert.Equal(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; i++)
                Assert.Same(expected[i], actual[i].Container);
            cells += actual.Count;
        }
        Assert.True(cells > 0, "no lyrics cell in the net");
    }

    private static List<SyntaxNode> Walk(CompilationUnitSyntax root, Func<SyntaxNode, bool> keep)
    {
        var found = new List<SyntaxNode>();
        foreach (var node in root.DescendantNodes())
            if (keep(node))
                found.Add(node);
        return found;
    }
}
