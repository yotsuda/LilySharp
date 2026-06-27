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
using LilySharp.Core.Syntax.InternalSyntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Incremental reparse (SyntaxTree.WithChanges): the result must be
/// indistinguishable from a full parse of the edited text — same green
/// structure and same diagnostics — while top-level items outside the edit
/// are REUSED (reference-equal greens), which is the whole point.
/// </summary>
[Trait("Category", "Unit")]
public class IncrementalParseTests
{
    private const string Source = """
        title "Incremental"
        time 4/4

        phrase intro { c4 d e f | g2 g | }

        phrase theme { e4 d c d | e1 | }

        section Main { melody { $intro $theme } }

        structure { Main }

        score "x" { staff melody }
        """;

    // ---------- helpers ----------

    private static void AssertTreesEquivalent(SyntaxTree expected, SyntaxTree actual)
    {
        Assert.Equal(expected.Text, actual.Text);
        AssertGreenEqual(expected.Root, actual.Root);

        Assert.Equal(expected.Diagnostics.Count, actual.Diagnostics.Count);
        for (int i = 0; i < expected.Diagnostics.Count; i++)
        {
            Assert.Equal(expected.Diagnostics[i].Code, actual.Diagnostics[i].Code);
            Assert.Equal(expected.Diagnostics[i].Span, actual.Diagnostics[i].Span);
            Assert.Equal(expected.Diagnostics[i].Message, actual.Diagnostics[i].Message);
        }
    }

    private static void AssertGreenEqual(GreenNode expected, GreenNode actual)
    {
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.FullWidth, actual.FullWidth);
        Assert.Equal(expected.SlotCount, actual.SlotCount);
        if (expected.IsToken)
        {
            Assert.Equal(expected.Text, actual.Text);
            // Trivia ATTACHMENT and content must match too — incremental
            // lexing splices token streams, and a wrong splice would first
            // show up as trivia migrating between neighbouring tokens.
            Assert.Equal(expected.LeadingTrivia?.ToFullString(),
                actual.LeadingTrivia?.ToFullString());
            Assert.Equal(expected.TrailingTrivia?.ToFullString(),
                actual.TrailingTrivia?.ToFullString());
        }

        for (int i = 0; i < expected.SlotCount; i++)
        {
            var e = expected.GetSlot(i);
            var a = actual.GetSlot(i);
            Assert.Equal(e == null, a == null);
            if (e != null && a != null)
                AssertGreenEqual(e, a);
        }
    }

    private static SyntaxTree Edit(SyntaxTree old, int start, int length, string newText,
        out SyntaxTree full)
    {
        var incremental = old.WithChange(new TextChange(new TextSpan(start, length), newText));
        full = SyntaxTree.Parse(incremental.Text);
        return incremental;
    }

    // ---------- equivalence across edit shapes ----------

    [Theory]
    // Edit inside the second phrase (replace e4 → a8).
    [InlineData("e4 d c d", "a8 d c d")]
    // Insert text (grow).
    [InlineData("g2 g", "g2 g a4 b")]
    // Delete text (shrink).
    [InlineData(" | g2 g", "")]
    // Edit the title at the very start.
    [InlineData("Incremental", "X")]
    // Edit at the very end.
    [InlineData("{ staff melody }", "{ staff melody staff melody }")]
    // Introduce a parse error.
    [InlineData("time 4/4", "time 4/")]
    // Edit that changes how a token lexes at the boundary.
    [InlineData("c4 d e f", "c44 d e f")]
    public void WithChange_MatchesFullParse(string find, string replace)
    {
        var old = SyntaxTree.Parse(Source);
        int start = Source.IndexOf(find, StringComparison.Ordinal);
        Assert.True(start >= 0, $"snippet not found: {find}");

        var incremental = Edit(old, start, find.Length, replace, out var full);
        AssertTreesEquivalent(full, incremental);
    }

    [Fact]
    public void WithChange_OnTreeWithErrors_MatchesFullParse()
    {
        // The old tree contains an error in 'intro'; editing 'theme' must
        // re-parse the erroneous item too (reuse is refused for items with
        // diagnostics) and reproduce its diagnostics exactly.
        var broken = Source.Replace("c4 d e f | g2 g |", "c4 d e f | g2 |");
        var old = SyntaxTree.Parse(broken);

        int start = broken.IndexOf("e4 d c d", StringComparison.Ordinal);
        var incremental = Edit(old, start, "e4".Length, "b8", out var full);
        AssertTreesEquivalent(full, incremental);
    }

    [Fact]
    public void WithChanges_MultipleEdits_MatchesFullParse()
    {
        var old = SyntaxTree.Parse(Source);
        int p1 = Source.IndexOf("c4 d e f", StringComparison.Ordinal);
        int p2 = Source.IndexOf("e4 d c d", StringComparison.Ordinal);

        var incremental = old.WithChanges(
            new TextChange(new TextSpan(p1, 2), "a8"),
            new TextChange(new TextSpan(p2, 2), "b2"));
        var full = SyntaxTree.Parse(incremental.Text);
        AssertTreesEquivalent(full, incremental);
    }

    [Fact]
    public void WithChange_RandomizedEdits_MatchFullParse()
    {
        // Deterministic fuzz: random spans replaced with random snippets.
        // The comment snippets are the incremental LEXER's worst case — a new
        // "//" or "/*" (or a deleted "*/") rewrites how all following text
        // lexes, so the splicer must refuse to resync or resync correctly.
        var rng = new Random(20260611);
        string[] snippets =
        [
            "c4 ", "phrase z { d8 e } ", "|", "", "  ", "render ", "}", "{ a2 | }",
            "// note\n", "/* x */", "/*", "*/", "\n\n",
        ];

        var tree = SyntaxTree.Parse(Source);
        for (int i = 0; i < 300; i++)
        {
            var text = tree.Text;
            int start = rng.Next(text.Length + 1);
            int length = Math.Min(rng.Next(8), text.Length - start);
            string replacement = snippets[rng.Next(snippets.Length)];

            tree = tree.WithChange(new TextChange(new TextSpan(start, length), replacement));
            var full = SyntaxTree.Parse(tree.Text);
            AssertTreesEquivalent(full, tree);
        }
    }

    [Fact]
    public void WithChange_CommentBoundaryEdits_MatchFullParse()
    {
        // Targeted lexical-bleed cases around comments.
        var src = "phrase a { c4 d } // tail\nphrase b { e4 f }\n/* block\n spans */ phrase c { g4 }\n";
        var cases = new (string find, string replace)[]
        {
            ("// tail", "x4"),          // comment becomes music
            ("c4 d", "c4 d // rest"),   // music grows a comment to EOL
            ("/* block", "block"),      // block comment opener removed
            ("spans */", "spans"),      // block comment closer removed
            ("e4", "e4 /* zz */"),      // inline block comment inserted
        };

        foreach (var (find, replace) in cases)
        {
            var old = SyntaxTree.Parse(src);
            int start = src.IndexOf(find, StringComparison.Ordinal);
            Assert.True(start >= 0);
            var incremental = old.WithChange(
                new TextChange(new TextSpan(start, find.Length), replace));
            var full = SyntaxTree.Parse(incremental.Text);
            AssertTreesEquivalent(full, incremental);
        }
    }

    // ---------- the actual incrementality ----------

    [Fact]
    public void WithChange_ReusesGreensOutsideTheEdit()
    {
        var old = SyntaxTree.Parse(Source);
        int start = Source.IndexOf("e4 d c d", StringComparison.Ordinal);
        var incremental = old.WithChange(new TextChange(new TextSpan(start, 2), "a8"));

        // Collect top-level member greens of both roots.
        static List<GreenNode> Members(SyntaxTree t)
        {
            var list = new List<GreenNode>();
            for (int i = 0; i < t.Root.SlotCount - 1; i++)
                if (t.Root.GetSlot(i) is { } g)
                    list.Add(g);
            return list;
        }

        var oldMembers = Members(old);
        var newMembers = Members(incremental);
        Assert.Equal(oldMembers.Count, newMembers.Count);

        int reused = 0;
        for (int i = 0; i < oldMembers.Count; i++)
            if (ReferenceEquals(oldMembers[i], newMembers[i]))
                reused++;

        // Everything must be ADOPTED (reference equality is the proof) except:
        // the edited 'phrase theme' itself, and 'phrase intro' — the item
        // immediately before the damage is deliberately re-parsed because its
        // FOLLOWING text changed (greedy-continuation safety).
        Assert.Equal(oldMembers.Count - 2, reused);
    }
}
