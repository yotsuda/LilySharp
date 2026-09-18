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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <see cref="AnnotationNameValidator.AnnotationKinds"/> against the type switch it mirrors.
/// The validator reads the tree's descendant index for those kinds instead of walking the
/// whole book, so a kind missing from the list is a whole family of annotations the
/// validator stops seeing — and this validator exists precisely to keep an unreadable
/// annotation from being dropped in silence. A second spelling on purpose (RULES §7.7:
/// where a fold is not possible, a differential net), the same trade session 401 made for
/// <see cref="SymbolReferenceValidator"/>'s kind lists.
/// </summary>
public sealed class AnnotationKindsTests
{
    /// <summary>One book writing both spellings the switch has a case for: a bare
    /// articulation and every shape of music mark (argument-less, argumented, dotted,
    /// terminator), on a note, a chord and a '&lt;&lt; &gt;&gt;' group.</summary>
    private const string EverySpelling = """
        melody {
          c4@staccato d4@accent e4@finger(1) f4@fig(6 4) |
          <c e g>4@chord g4@text("dolce") a4@ottava.bassa b4@rit |
          c'4@!rit << c e g >>@chord r4 |
        }
        """;

    private static IEnumerable<CompilationUnitSyntax> Roots()
    {
        foreach (var path in CollectResumeTests.NetBooks())
            yield return SyntaxTree.Parse(File.ReadAllText(path)).GetRoot();
        yield return SyntaxTree.Parse(EverySpelling).GetRoot();
    }

    /// <summary>
    /// The kinds yield exactly the nodes the switch matches — the same nodes, one for one,
    /// in the same order. Order is part of the answer: the diagnostics come out in the
    /// order the nodes are visited, and the census pins that order across the corpus.
    /// </summary>
    [Fact]
    public void TheListedKinds_YieldExactlyWhatTheSwitchMatches_InTheSameOrder()
    {
        int matched = 0;
        foreach (var root in Roots())
        {
            var walked = root.DescendantNodes()
                .Where(n => n is ArticulationSyntax or MusicMarkSyntax)
                .ToList();
            var listed = root.DescendantNodesOfKinds(AnnotationNameValidator.AnnotationKinds).ToList();

            Assert.Equal(walked.Count, listed.Count);
            for (int i = 0; i < walked.Count; i++)
                Assert.Same(walked[i], listed[i]);
            matched += walked.Count;
        }
        Assert.True(matched >= 300, $"only {matched} annotations in the net"); // 377 when written
    }

    /// <summary>
    /// Both listed kinds are LOAD-BEARING: each bucket carries a diagnostic of its own, so
    /// dropping either kind from the list goes quiet here rather than in a reader's book.
    /// </summary>
    [Fact]
    public void EachListedKind_CarriesADiagnosticOfItsOwn()
    {
        // The hand-written book must really write both kinds, or the half of the net
        // below that poisons them is weaker than it claims.
        var present = SyntaxTree.Parse(EverySpelling).GetRoot().DescendantNodes()
            .Select(n => n.Kind).ToHashSet();
        foreach (var kind in AnnotationNameValidator.AnnotationKinds)
            Assert.True(present.Contains(kind), $"the hand-written book writes no {kind}");

        Assert.Empty(Diagnostics(EverySpelling));

        // Articulation: a typo in a bare name. MusicMark: a typo in an argumented one.
        var articulation = Assert.Single(Diagnostics("melody { c4@stacato d4 | }"));
        Assert.Equal(DiagnosticCodes.UnknownAnnotation, articulation.Code);
        var mark = Assert.Single(Diagnostics("melody { c4@notehed(x) d4 | }"));
        Assert.Equal(DiagnosticCodes.UnknownAnnotation, mark.Code);
    }

    private static IReadOnlyList<Diagnostic> Diagnostics(string source)
    {
        var validator = new AnnotationNameValidator();
        validator.Validate(SyntaxTree.Parse(source));
        return validator.Diagnostics;
    }
}
