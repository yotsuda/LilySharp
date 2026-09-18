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
/// The kind lists the tail of the diagnostics pass now asks the tree's descendant index
/// with, against the switches they stand beside. Each of these validators used to walk the
/// whole tree and offer every node to a type switch; a book of 234,030 nodes handed them
/// between 0 and 8,000 answers (MEASURED, session 409), so each now asks the index for the
/// kinds its switch can answer on. A kind missing from a list makes that validator go
/// SILENT on a spelling it used to report — no test of the diagnostic itself need fail, so
/// this is the net.
/// </summary>
/// <remarks>
/// Two shapes, because the switches come in two shapes. Where the switch is a separable
/// predicate it is ASKED (the <see cref="Predicates"/> table): any node it answers on must
/// be of a listed kind — the shape <see cref="SymbolKindsTests"/> uses. Where the cases
/// carry typed bindings and their messages (<c>RepeatStructureScopeValidator</c>,
/// <c>PhraseCycleValidator</c>), the test spells the TYPES itself and compares the plain
/// walk's answer with the index's (RULES §7.7: where a fold is not possible, a differential
/// net).
/// </remarks>
public sealed class TailValidatorKindsTests
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

    /// <summary>One book that writes a spelling for every list below — so a list is not
    /// declared safe by a net that never exercises it. Deliberately full of the very
    /// mistakes these validators report, so it is read for its NODES, never for silence.
    /// </summary>
    private const string EverySpelling = """
        title "Kinds"
        composer "Lily#"
        octave absolute
        tempo 92
        time 4/4
        key g major
        font { }
        paper { }
        layout { }
        variable riffBody = { c'4 d' e' f' | }
        phrase riff { c'4 d' e' f' | }
        part melody { clef treble  tempo 100  time 3/4 }
        section A {
          melody {
            override NoteHead.color = "red"
            once override Stem.thickness = 2
            revert NoteHead.color
            c'4 q 4 r8 d'8 <e' g'>4 | c'4 d' e' f' :| [1. g'1 ]
          }
        }
        form main { A }
        score main { staff melody }
        """;

    private static readonly (string Validator, SyntaxKind[] Kinds, Func<SyntaxNode, bool> Answers)[]
        Predicates =
        [
            ("DurationValidator", DurationValidator.DurationBearingKinds,
                n => DurationValidator.DurationOf(n) != null),
            ("DuplicateGlobalSettingValidator", DuplicateGlobalSettingValidator.GlobalSettingKinds,
                n => DuplicateGlobalSettingValidator.SettingKindOf(n) != null),
            ("OverrideVocabularyValidator", OverrideVocabularyValidator.CommandKinds,
                n => OverrideVocabularyValidator.CommandOf(n).Keyword != null),
            ("RevertContextValidator", RevertContextValidator.DirectiveKinds,
                n => RevertContextValidator.DirectiveKindOf(n) != null),
            ("ScoreSettingInPartHeaderValidator", ScoreSettingInPartHeaderValidator.ScoreSettingKinds,
                n => ScoreSettingInPartHeaderValidator.SettingKindOf(n) != null),
        ];

    [Fact]
    public void EveryNodeAPredicateAnswersOn_IsOfAListedKind()
    {
        var answers = new Dictionary<string, int>();
        foreach (var (book, root) in Roots())
            foreach (var node in root.DescendantNodes())
                foreach (var (validator, kinds, answersOn) in Predicates)
                {
                    if (!answersOn(node))
                        continue;
                    Assert.True(Array.IndexOf(kinds, node.Kind) >= 0,
                        $"{validator}: a {node.Kind} at {node.Position} of {book} is answered "
                        + $"on but not listed in [{string.Join(", ", kinds)}]");
                    answers[validator] = answers.GetValueOrDefault(validator) + 1;
                }

        // … and every list was actually exercised: a list no book reaches is not netted.
        foreach (var (validator, _, _) in Predicates)
            Assert.True(answers.GetValueOrDefault(validator) > 0, $"{validator}: no answer in the net");
    }

    [Fact]
    public void TheSwitchesWithNoPredicate_GetWhatThePlainWalkFound()
    {
        int volta = 0, phrases = 0;
        foreach (var (_, root) in Roots())
        {
            Assert.Equal(Walk(root, n => n is BarlineSyntax || n is InlineVoltaSyntax),
                root.DescendantNodesOfKinds(RepeatStructureScopeValidator.RepeatStructureKinds).ToList());
            Assert.Equal(
                Walk(root, n => n is PhraseDeclarationSyntax || n is VariableDeclarationSyntax),
                root.DescendantNodesOfKinds(PhraseCycleValidator.DeclaringKinds).ToList());
            volta += root.DescendantNodes().Count(n => n is InlineVoltaSyntax);
            phrases += root.DescendantNodes().Count(
                n => n is PhraseDeclarationSyntax || n is VariableDeclarationSyntax);
        }
        Assert.True(volta > 0, "no inline volta in the net");
        Assert.True(phrases > 0, "no phrase or variable declaration in the net");
    }

    /// <summary>The two single-type validators keep no list at all — they ask the index for
    /// their type — so what is netted is that the typed answer IS the walk's.</summary>
    [Fact]
    public void TheSingleTypeValidators_GetWhatThePlainWalkFound()
    {
        int seen = 0;
        foreach (var (_, root) in Roots())
        {
            Assert.Equal(Walk(root, n => n is ChordRepetitionSyntax),
                root.DescendantNodes<ChordRepetitionSyntax>().ToList<SyntaxNode>());
            Assert.Equal(Walk(root, n => n is BareDurationSyntax),
                root.DescendantNodes<BareDurationSyntax>().ToList<SyntaxNode>());
            seen += root.DescendantNodes().Count(n => n is ChordRepetitionSyntax or BareDurationSyntax);
        }
        Assert.True(seen > 0, "no bare duration or chord repetition in the net");
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
