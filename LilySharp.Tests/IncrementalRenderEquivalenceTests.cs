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

using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// F3e correctness gate (the safety net for the incremental query-DAG work).
///
/// Two end-to-end invariants over the WHOLE pipeline (parse → collect → layout
/// → SVG), one layer above the green-tree equivalence asserted by
/// <see cref="IncrementalParseTests"/>:
///
/// 1. DETERMINISM — rendering the same source twice is byte-identical. Pins
///    down hidden global mutable state / nondeterministic ordering BEFORE the
///    F3 stages (S2+) start memoizing and reusing compilation results; a cache
///    that returns a different-but-"equal" result would surface here first.
///
/// 2. INCREMENTAL == FULL — for any edit, rendering the incrementally-reparsed
///    tree equals rendering a full parse of the edited text (same SVG, or the
///    same exception). TODAY this exercises the already-shipped incremental
///    PARSER end-to-end (render is a pure function of the tree). It is also the
///    SEAM the real F3e gate fills in: when S5 makes semantics/layout
///    incremental, the left-hand side switches from "full render of an
///    incrementally-parsed tree" to "incremental (cached) render", and this
///    same assertion becomes the load-bearing incremental==full gate that
///    catches a missed dependency edge (the one failure mode the design doc
///    flags as un-spottable by eye).
///
/// The assertion compares OUTCOMES including exceptions, so randomized fuzz
/// edits can never make the net flaky — a malformed edit must merely fail the
/// same way on both sides.
/// </summary>
[Trait("Category", "Visual")]
public class IncrementalRenderEquivalenceTests
{
    private static readonly string FixturesDir = FindFixturesDir();

    private static string FindFixturesDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "LilySharp.Tests", "Fixtures");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Cannot find LilySharp.Tests/Fixtures/ directory");
    }

    private static string LoadFixture(string name) =>
        File.ReadAllText(Path.Combine(FixturesDir, name + ".lys")).Replace("\r\n", "\n");

    private static string RenderTree(SyntaxTree tree) =>
        SvgGenerator.Generate(tree, new SvgRenderOptions { EmbedFont = false })
            .Replace("\r\n", "\n");

    private static string Render(string source) => RenderTree(SyntaxTree.Parse(source));

    /// <summary>
    /// Assert that the incrementally-reparsed tree and a full parse of the same
    /// text render identically — comparing outcome INCLUDING exceptions, so an
    /// edit that produces unrenderable garbage must fail identically on both
    /// sides rather than making this guard flaky.
    /// </summary>
    private static void AssertRenderEquivalent(SyntaxTree incremental, SyntaxTree full)
    {
        Assert.Equal(full.Text, incremental.Text); // precondition: same text both sides

        string? incSvg = null, fullSvg = null;
        Exception? incEx = null, fullEx = null;
        try { incSvg = RenderTree(incremental); } catch (Exception e) { incEx = e; }
        try { fullSvg = RenderTree(full); } catch (Exception e) { fullEx = e; }

        Assert.Equal(fullEx?.GetType(), incEx?.GetType());
        if (fullEx == null)
            Assert.Equal(fullSvg, incSvg);
    }

    /// <summary>
    /// Representative fixtures spanning the cross-measure dependencies F3's
    /// context chain must carry: bare notes, key/accidentals, cross-measure
    /// ties/slurs, polyphony, transpose, multi-staff piano, a complex showcase,
    /// and a multi-movement piece (multiple collector instances).
    /// </summary>
    public static IEnumerable<object[]> RepresentativeFixtures()
    {
        yield return new object[] { "test/notes" };
        yield return new object[] { "test/accidentals" };
        yield return new object[] { "test/ties-slurs" };
        yield return new object[] { "test/multi-voice" };
        yield return new object[] { "test/transpose" };
        yield return new object[] { "test/keysig-change" };
        yield return new object[] { "test/multi-movement" };
        yield return new object[] { "showcase/03-piano" };
        yield return new object[] { "showcase/04-advanced" };
    }

    [Theory]
    [MemberData(nameof(RepresentativeFixtures))]
    public void FullRender_IsDeterministic(string fixture)
    {
        var source = LoadFixture(fixture);
        Assert.Equal(Render(source), Render(source));
    }

    [Theory]
    [MemberData(nameof(RepresentativeFixtures))]
    public void IncrementalEdit_ThenRender_MatchesFullRender(string fixture)
    {
        // A representative single edit (insert a note mid-document), then assert
        // the incremental reparse renders identically to a full parse.
        var source = LoadFixture(fixture);
        var old = SyntaxTree.Parse(source);
        int at = source.Length / 2;

        var incremental = old.WithChange(new TextChange(new TextSpan(at, 0), " c4"));
        var full = SyntaxTree.Parse(incremental.Text);
        AssertRenderEquivalent(incremental, full);
    }

    [Fact]
    public void IncrementalEdit_RandomizedEdits_MatchFullRender()
    {
        // Deterministic fuzz, chained edits, full render on both sides. Mirrors
        // IncrementalParseTests' fuzz but one layer up (render, not just tree).
        // Fewer iterations because a full render per step is heavier than a
        // reparse; outcomes-including-exceptions comparison keeps it non-flaky.
        var rng = new Random(20260629);
        string[] snippets =
        [
            "c4 ", "d8 e ", "|", "", " ", "r4 ", "g'2 ", "\n", "<c e>4 ", ".",
            "// x\n", "{ a2 }",
        ];

        var tree = SyntaxTree.Parse(LoadFixture("test/notes"));
        for (int i = 0; i < 50; i++)
        {
            var text = tree.Text;
            int start = rng.Next(text.Length + 1);
            int length = Math.Min(rng.Next(6), text.Length - start);
            string replacement = snippets[rng.Next(snippets.Length)];

            var incremental = tree.WithChange(new TextChange(new TextSpan(start, length), replacement));
            var full = SyntaxTree.Parse(incremental.Text);
            AssertRenderEquivalent(incremental, full);
            tree = incremental;
        }
    }
}
