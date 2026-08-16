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

using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A token the parser consumes without storing is a token the TREE NO LONGER
/// CONTAINS — and a node's position is the running sum of the green widths
/// before it, so a dropped token slides every position after it. The '.' of an
/// annotation's '.up' / '.down' qualifier was dropped exactly that way:
/// <c>@staccato.up</c> came back out of the tree as <c>@staccatoup</c> with no
/// diagnostic at all, and every note after it reported a source position one
/// character early. That reaches the SVG's data-pos, the LSP's jump targets and
/// <c>PartSectionLayoutConverter</c>, which WRITES .lys back out of the tree.
/// </summary>
[Trait("Category", "Unit")]
public class AnnotationRoundTripTests
{
    [Theory]
    [InlineData("c4@staccato.up d4 e4 f4 |")]     // articulation, above
    [InlineData("c4@staccato.down d4 e4 f4 |")]   // articulation, below
    [InlineData("c4@f.up d4 e4 f4 |")]            // dynamic
    [InlineData("c4@feather.up d4 e4 f4 |")]      // unknown name — still spelled back
    [InlineData("c4@staccato.up.down d4 e4 f4 |")] // rejected second side, kept anyway
    [InlineData("c4@cresc.up d4 e4 f4 |")]        // rejected on a hairpin, kept anyway
    public void ADottedPlacementQualifier_SurvivesTheTree(string source)
        => Assert.Equal(source, SyntaxTree.Parse(source).GetRoot().ToFullString());

    /// <summary>
    /// The defect itself, stated as the quantity it corrupts: every node must
    /// stand where it says it stands — its text is the source slice at its own
    /// position. A dropped token slides everything after it, so this fails one
    /// character at a time and never at the node that lost the token. The
    /// control (same bar, no qualifier) says the mapping is right to begin with;
    /// without it, a test that pins positions passes just as well when NOTHING
    /// maps correctly.
    /// </summary>
    [Theory]
    [InlineData("melody { c4@staccato.up d4 e4 f4 | }")]
    [InlineData("melody { c4@staccato d4 e4 f4 | }")]     // control
    public void EveryNode_StandsWhereItSaysItStands(string source)
    {
        var root = SyntaxTree.Parse(source).GetRoot();
        Assert.Equal(source.Length, root.FullWidth);

        foreach (var node in root.DescendantNodes())
        {
            var text = node.ToFullString();
            Assert.True(
                node.Position + text.Length <= source.Length
                && source.AsSpan(node.Position, text.Length).SequenceEqual(text),
                $"{node.Kind} at {node.Position} spells [{text}] but the source there is "
                + $"[{source.Substring(node.Position, Math.Min(text.Length, source.Length - node.Position))}]");
        }
    }

    /// <summary>
    /// The hole itself, measured: every book in the corpus and the fixtures must
    /// spell itself back out of its own tree. The named books below are the ones
    /// that do NOT, each for a reason that lives in a different island; they are
    /// listed so the number cannot grow silently, and so the remaining islands
    /// have an address instead of a memory.
    /// </summary>
    [Fact]
    public void EveryBook_SpellsItselfBackOutOfItsTree()
    {
        // Known-broken, one island each:
        //   annotation vs slur/tie ORDER — the mark is re-emitted before the
        //   '(' / ')~' it was written after (empty-chord, script-stack-order1,
        //   slur-vertical-skylines, feature-tour).
        // ⚠️ volta-labels came OFF this list on 2026-08-16: "the '|' before a '[1. …]'
        // label is not stored" was the last island where a token no form rule claimed was
        // consumed by a bare Advance(). ParseFormItem now parses every barline as the
        // BarlineSyntax it is (LYS0031 warns for the ones nothing engraves yet), and the
        // three containers around it report and keep what they cannot place (LYS0030).
        // The four that remain are ONE island, and it is not this one.
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "empty-chord.lys", "script-stack-order1.lys", "slur-vertical-skylines.lys",
            "feature-tour.lys",
        };

        var root = CollectResumeTests.FindRepoRoot();
        var broken = new List<string>();
        int books = 0;
        foreach (var dir in new[]
        {
            Path.Combine(root, "audit", "lp-regression", "lys"),
            Path.Combine(root, "LilySharp.Tests", "Fixtures"),
        })
        {
            if (!Directory.Exists(dir))
                continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.lys", SearchOption.AllDirectories))
            {
                books++;
                var source = File.ReadAllText(file);
                if (SyntaxTree.Parse(source).GetRoot().ToFullString() != source)
                    broken.Add(Path.GetFileName(file));
            }
        }

        Assert.True(books > 250, $"only {books} books found — the corpus paths moved");
        var news = broken.Where(b => !known.Contains(b)).ToList();
        Assert.True(news.Count == 0, "these books stopped spelling themselves back: " + string.Join(", ", news));
        var fixedUp = known.Where(k => !broken.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList();
        Assert.True(fixedUp.Count == 0,
            "these books round-trip now — take them off the known-broken list: " + string.Join(", ", fixedUp));
    }
}
