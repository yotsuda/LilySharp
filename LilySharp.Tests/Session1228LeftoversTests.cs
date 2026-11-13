// This file is part of Lily#.
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Lily# is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// Lily# is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Four defects session 1227 found and left (2026-11-14, Lab sessions/p1227/leftovers.md): a
/// phrase named like an English flat could not be called, the LilyPond twin wrote dynamics
/// LilyPond does not have, a scoop on a rest and on a grace note, and quarter tones in
/// <c>check --pitches</c> and <c>import</c>.
/// </summary>
public class Session1228LeftoversTests
{
    private static List<Diagnostic> Said(string book)
    {
        var tree = SyntaxTree.Parse(book);
        return tree.Diagnostics.Concat(SemanticValidation.Run(tree)).ToList();
    }

    private static string Book(string declarations, string music)
        => "time 4/4\noctave absolute\n" + declarations + "\npart mel { }\nsection Main { mel { " + music + " } }\n"
            + "form { Main }\nscore { staff mel }\n";

    // ========== 1. A phrase named like an English flat ==========

    /// <summary>
    /// <c>eb</c>, <c>bb</c> and <c>cB</c> are no pitches of this grammar (the flats are
    /// <c>ees</c>, <c>bes</c>), so a phrase declared under one of them is called by it.
    /// </summary>
    [Theory]
    [InlineData("eb")]
    [InlineData("bb")]
    [InlineData("cB")]
    [InlineData("gflat")]
    public void APhraseNamedLikeAnEnglishFlat_IsCalledByItsName(string name)
    {
        string book = Book($"phrase {name} {{ g'4 a' b' c'' | }}", $"c'1 | {name} | {name}' |");
        Assert.DoesNotContain(Said(book), d => d.Severity == DiagnosticSeverity.Error);

        var tree = SyntaxTree.Parse(book);
        Assert.Equal(2, tree.GetNodes<VariableReferenceSyntax>().Count(r => r.Name.Text == name));
        // The phrase's four notes are on the page beside the part's own one.
        Assert.Equal(5, ResolvedPitches.ForFile(tree)!.Count);

        // The control: with no such phrase the word is a mistyped pitch, said once.
        var slip = Said(Book("", $"c'1 | {name} |")).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        var one = Assert.Single(slip);
        Assert.Equal(DiagnosticCodes.BareReferenceRequiresDollar, one.Code);
        Assert.Contains("did you mean the pitch '" + name[..1] + "es'", one.Message);
    }

    // ========== 2. The twin's dynamics LilyPond has no command for ==========

    private static string Twin(string music) => new LilyPondExporter().Export(SyntaxTree.Parse(Book("", music)));

    /// <summary>
    /// LilyPond 2.26 declares no <c>\sffz</c> and no <c>\rf</c> (ly/dynamic-scripts-init.ly), so
    /// the twin builds them as LilyPond builds its own; every other level keeps its command.
    /// </summary>
    [Fact]
    public void TheTwin_WritesNoDynamicCommandLilyPondLacks()
    {
        Assert.Contains("c'4-#(make-dynamic-script \"sffz\")", Twin("c'4@sffz d' e' f' |"));
        Assert.Contains("c'4-#(make-dynamic-script \"rf\")", Twin("c'4@rf d' e' f' |"));
        Assert.Contains("c'4^#(make-dynamic-script \"sffz\")", Twin("c'4@sffz.up d' e' f' |"));
        Assert.Contains("c'4_#(make-dynamic-script \"rf\")", Twin("c'4@rf.down d' e' f' |"));
        // The control: a level LilyPond has is its command.
        Assert.Contains("c'4\\sfz", Twin("c'4@sfz d' e' f' |"));

        // Every level the language reads, so that a new one is asked the same question.
        // `niente` is no level of LilyPond's and is written as text (EmitNiente).
        foreach (string level in SyntaxFacts.DynamicTextLevels.Keys.Where(k => k != "niente"))
        {
            string twin = Twin($"c'4@{level} d' e' f' |");
            bool command = twin.Contains($"c'4\\{level} ", StringComparison.Ordinal);
            bool built = twin.Contains($"c'4-#(make-dynamic-script \"{level}\")", StringComparison.Ordinal);
            Assert.True(command != built, level);
            Assert.True(built == level is "sffz" or "rf", level);
        }
    }
}
