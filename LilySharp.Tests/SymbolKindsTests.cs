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
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The kind lists that stand beside the symbol predicates (<see cref="SectionSymbols"/>,
/// <see cref="PartReferenceFinder"/>) against the predicates themselves: a validator that
/// asks the tree's descendant index for the LISTED kinds must reach every node the
/// PREDICATE answers on, or it goes silent on a spelling the editor still colours. A
/// second spelling on purpose (RULES §7.7: where a fold is not possible, a differential
/// net), so this is the net — over every node of every net book, and once by hand over a
/// book that writes every spelling, in case the net lacks one.
/// </summary>
public sealed class SymbolKindsTests
{
    private static IEnumerable<CompilationUnitSyntax> Roots()
    {
        foreach (var path in CollectResumeTests.NetBooks())
            yield return SyntaxTree.Parse(File.ReadAllText(path)).GetRoot();
        yield return SyntaxTree.Parse(EverySpelling).GetRoot();
    }

    /// <summary>One book with every spelling the predicates answer on: a section header and
    /// a section with music, a plain and a silent form reference, a part header and a
    /// section-body part block, each of the six render references, a named chord and lyric
    /// track and both row references.</summary>
    private const string EverySpelling = """
        part melody { clef treble }
        part low { clef bass }
        phrase riff { c4 d e f | }
        section A { key g major }
        section A { melody { riff } low { g,4 a, b, c | } }
        section B { melody { e4 f g a | } low { c4 d e f | } }
        chords prog { section A { | G | } section B { | C | } }
        lyrics verse sings melody { section A { la la la la | } section B { la la la la | } }
        form main { A ~B }
        score main {
          staff melody
          low
          ossia melody
          tab low
          condensedStaff { melody low }
          combinedStaff { melody low }
          chords prog
          lyrics verse
        }
        """;

    [Fact]
    public void EveryNodeAPredicateAnswersOn_IsOfAListedKind()
    {
        int answered = 0;
        foreach (var root in Roots())
        {
            var references = new List<SyntaxTokenNode>();
            foreach (var node in root.DescendantNodes())
            {
                if (SectionSymbols.DeclaredName(node) != null)
                    Listed(SectionSymbols.DeclaringKinds, node, ref answered);
                if (SectionSymbols.ReferencedName(node) != null)
                    Listed(SectionSymbols.ReferencingKinds, node, ref answered);
                if (PartReferenceFinder.DeclaredName(node) != null)
                    Listed(PartReferenceFinder.DeclaringKinds, node, ref answered);
                references.Clear();
                PartReferenceFinder.CollectReferenceTokens(node, references);
                if (references.Count > 0)
                    Listed(PartReferenceFinder.ReferenceKinds, node, ref answered);
                if (PartReferenceFinder.DeclaredTrackName(node) != null
                    || PartReferenceFinder.ReferencedTrackName(node) != null)
                    Listed(PartReferenceFinder.TrackKinds, node, ref answered);
            }
        }
        Assert.True(answered >= 1500, $"only {answered} predicate answers in the net"); // 1781 when written
    }

    private static void Listed(SyntaxKind[] kinds, SyntaxNode node, ref int answered)
    {
        Assert.True(Array.IndexOf(kinds, node.Kind) >= 0,
            $"a {node.Kind} at {node.Position} is answered on but not listed in [{string.Join(", ", kinds)}]");
        answered++;
    }

    [Fact]
    public void TheHandWrittenBook_ExercisesEveryListedKind_AndTheValidatorReadsItAll()
    {
        // The book above must actually contain every listed kind (else the by-hand half of
        // the net is weaker than it claims) …
        var tree = SyntaxTree.Parse(EverySpelling);
        Assert.Empty(tree.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var root = tree.GetRoot();
        var present = root.DescendantNodes().Select(n => n.Kind).ToHashSet();
        foreach (var kind in SectionSymbols.DeclaringKinds.Concat(SectionSymbols.ReferencingKinds)
                     .Concat(PartReferenceFinder.DeclaringKinds).Concat(PartReferenceFinder.ReferenceKinds)
                     .Concat(PartReferenceFinder.TrackKinds))
            Assert.True(present.Contains(kind), $"the hand-written book writes no {kind}");

        // … and the validator, reading through the lists, resolves every one of them: a
        // kind that fell out of a list would surface here as an undefined name.
        var validator = new SymbolReferenceValidator();
        validator.Validate(SyntaxTree.Parse(EverySpelling));
        Assert.Empty(validator.Diagnostics);

        // Poison: break one spelling of each family and the validator names it.
        var broken = new SymbolReferenceValidator();
        // The lyrics ROW is the last item before the closing brace (the declaration above
        // it reads `lyrics verse sings`); matched through the line break so the source
        // file's line-ending style cannot decide whether the poison lands.
        var poisoned = System.Text.RegularExpressions.Regex.Replace(
            EverySpelling
                .Replace("form main { A ~B }", "form main { A ~Nope }")
                .Replace("combinedStaff { melody low }", "combinedStaff { melody lo }")
                .Replace("melody { riff }", "melody { rif }"),
            @"lyrics verse(\s*\})", "lyrics vers$1");
        Assert.Contains("lyrics vers", poisoned);
        broken.Validate(SyntaxTree.Parse(poisoned));
        var codes = broken.Diagnostics.Select(d => d.Code).ToList();
        Assert.Contains(DiagnosticCodes.UndefinedSection, codes);
        Assert.Contains(DiagnosticCodes.UndefinedPart, codes);
        Assert.Contains(DiagnosticCodes.UndefinedVariable, codes);
        Assert.Equal(4, codes.Count);
    }
}
