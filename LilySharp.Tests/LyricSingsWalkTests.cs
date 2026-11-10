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

using LilySharp.Core.Editing;
using LilySharp.Core.Music;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The differential net for the lyric-binding walks. <see cref="LyricSingsValidator"/> and
/// <see cref="LyricBindings"/> used to open with whole-tree walks and ask a predicate of
/// every node; they now ask the tree's descendant index for the kinds each predicate can
/// answer on. The index is only as right as those kind lists, so the PLAIN WALK stands here
/// as the reference: for every net book and for a book that writes every lyric spelling,
/// the sequence the index hands back must be the sequence the walk found — same nodes, same
/// order — and the bindings must be the same bindings.
/// </summary>
/// <remarks>
/// Spelled with the node types, not with the kind lists, so a kind dropped from a list
/// cannot make both sides agree by falling out of both (RULES §7.7: where a fold is not
/// possible, a differential net). <see cref="SymbolKindsTests"/> is the other half — it
/// pins the lists to the PREDICATES beside them.
/// </remarks>
public sealed class LyricSingsWalkTests
{
    /// <summary>One book with every spelling these walks answer on: a part whose music
    /// names two voices, a lyrics definition that sings a part and one that sings a voice,
    /// a row that sings on its own, a plain row, and a group with a row under its staff.
    /// </summary>
    private const string EverySpelling = """
        part melody { clef treble }
        part alt { clef treble }
        part low { clef bass }
        section A {
          melody { voice hi { c''4 d' e' f' | } lo { c'4 d e f | } }
          alt { g4 a b c' | }
          low { c4 d, e, f, | }
        }
        lyrics verse { section A { la la la la | } }
        lyrics inner { section A { ka ka ka ka | } }
        lyrics plain { section A { na na na na | } }
        form { A }
        score {
          staff melody
          lyrics verse on melody
          lyrics plain
          grandStaff {
            staff alt
            lyrics verse on alt
          }
          staff low
        }
        """;

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

    /// <summary>The reference: the whole-tree walk, filtered by the node type the switch
    /// names — what each of these loops was before the index answered it.</summary>
    private static List<SyntaxNode> Walk<T>(CompilationUnitSyntax root) where T : SyntaxNode
    {
        var found = new List<SyntaxNode>();
        foreach (var node in root.DescendantNodes())
            if (node is T)
                found.Add(node);
        return found;
    }

    private static List<SyntaxNode> Walk2<T1, T2>(CompilationUnitSyntax root)
        where T1 : SyntaxNode where T2 : SyntaxNode
    {
        var found = new List<SyntaxNode>();
        foreach (var node in root.DescendantNodes())
            if (node is T1 || node is T2)
                found.Add(node);
        return found;
    }

    [Fact]
    public void TheHandWrittenBook_Parses_AndWritesEverySpelling()
    {
        var tree = SyntaxTree.Parse(EverySpelling);
        Assert.Empty(tree.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var root = tree.GetRoot();
        foreach (var kind in new[]
                 {
                     SyntaxKind.PartDeclaration, SyntaxKind.PartBlock, SyntaxKind.ParallelExpression,
                     SyntaxKind.LyricsBlock, SyntaxKind.LyricsRowRender, SyntaxKind.GrandStaffRender,
                 })
            Assert.Contains(kind, root.DescendantNodes().Select(n => n.Kind));

        // … and it is a book the validator accepts, so a diagnostic below is the walk's.
        var validator = new LyricSingsValidator();
        validator.Validate(tree);
        Assert.Empty(validator.Diagnostics);
    }

    [Fact]
    public void TheIndexedWalks_VisitWhatThePlainWalkVisited()
    {
        int checkedNodes = 0;
        foreach (var (name, root) in Roots())
        {
            // Validator walk 1: the part declarations, and the parallel expressions that
            // introduce named voices.
            Assert.Equal(Walk2<PartDeclarationSyntax, PartBlockSyntax>(root),
                root.DescendantNodesOfKinds(PartReferenceFinder.DeclaringKinds).ToList());
            Assert.Equal(Walk<ParallelExpressionSyntax>(root),
                root.DescendantNodesOfKinds(PartReferenceFinder.VoiceIntroducingKinds).ToList());

            // Validator walk 2: both sites that name a melody, in document order — the order the
            // unknown-target diagnostics come out in.
            Assert.Equal(Walk2<LyricsRowRenderSyntax, SingerDeclarationSyntax>(root),
                root.DescendantNodesOfKinds(PartReferenceFinder.SingsKinds).ToList());

            // LyricBindings: the definition blocks (Conflicts, BuildMap).
            Assert.Equal(Walk<LyricsBlockSyntax>(root),
                root.DescendantNodes<LyricsBlockSyntax>().ToList());

            checkedNodes += root.DescendantNodes().Count();
            Assert.False(string.IsNullOrEmpty(name));
        }
        Assert.True(checkedNodes >= 40_000, $"only {checkedNodes} nodes in the net");
    }

    [Fact]
    public void EveryNodeTheLyricPredicatesAnswerOn_IsOfAListedKind()
    {
        int answered = 0;
        var voices = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, root) in Roots())
            foreach (var node in root.DescendantNodes())
            {
                if (PartReferenceFinder.SingsTargetToken(node) != null)
                {
                    Assert.Contains(node.Kind, PartReferenceFinder.SingsKinds);
                    answered++;
                }
                voices.Clear();
                PartReferenceFinder.CollectVoiceNames(node, voices);
                if (voices.Count > 0)
                {
                    Assert.Contains(node.Kind, PartReferenceFinder.VoiceIntroducingKinds);
                    answered++;
                }
            }
        Assert.True(answered >= 20, $"only {answered} predicate answers in the net"); // 44 when written
    }

    [Fact]
    public void TheBindings_AreWhatTheWholeTreeWalkFound()
    {
        foreach (var (name, root) in Roots())
        {
            // The named voices of each part, against the plain walk's answer.
            foreach (var part in root.DescendantNodes()
                         .Select(PartReferenceFinder.DeclaredName)
                         .Where(t => t != null).Select(t => t!.Text).Distinct())
            {
                var expected = new HashSet<string>(StringComparer.Ordinal);
                foreach (var n in root.DescendantNodes())
                {
                    if (PartReferenceFinder.DeclaredName(n)?.Text != part)
                        continue;
                    foreach (var par in n.DescendantNodes().OfType<ParallelExpressionSyntax>())
                        foreach (var (vn, _) in par.NamedVoices)
                            if (vn is { Length: > 0 })
                                expected.Add(vn);
                }
                Assert.Equal(expected.OrderBy(v => v, StringComparer.Ordinal),
                    LyricBindings.VoicesOfPart(root, part).OrderBy(v => v, StringComparer.Ordinal));
                Assert.False(string.IsNullOrEmpty(name));
            }
        }
    }

    /// <summary>The walks still REPORT: a row whose <c>on</c> names nothing, and a group row
    /// with no <c>on</c> and no staff above it.</summary>
    [Theory]
    [InlineData("lyrics verse on alt\n", "lyrics verse on nobody\n", DiagnosticCodes.SingsTargetUnknown)]
    [InlineData("staff alt\n    lyrics verse on alt\n", "lyrics verse\n    staff alt\n", DiagnosticCodes.GroupRowNotBoundToStaffAbove)]
    public void PoisoningTheBook_StillReports(string from, string to, string code)
    {
        var poisoned = EverySpelling.Replace("\r\n", "\n").Replace(from, to);
        Assert.NotEqual(EverySpelling.Replace("\r\n", "\n"), poisoned);
        var validator = new LyricSingsValidator();
        validator.Validate(SyntaxTree.Parse(poisoned));
        Assert.Contains(code, validator.Diagnostics.Select(d => d.Code));
    }
}
