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

using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Everything the '@' completion offers must actually parse as an annotation on a
/// note AND be recognized by the collector — otherwise the editor suggests
/// something that either errors the moment it is accepted (e.g. the structure-only
/// jump directive 'ds.al.coda') or is silently dropped with an "Unknown annotation"
/// warning (e.g. '@harmonic' was offered while unregistered).
/// </summary>
[Trait("Category", "Unit")]
public class AtCompletionParsesTests
{
    [Fact]
    public void EveryAtCompletionItem_ParsesAndIsRecognized()
    {
        var list = LilySharpLanguageServer.GetArticulationCompletions();
        var failures = new System.Collections.Generic.List<string>();
        foreach (var item in list.Items)
        {
            var raw = string.IsNullOrEmpty(item.InsertText) ? item.Label : item.InsertText;
            // Strip snippet placeholders ($0, $1, ${1:…}) — editor cursors, not text.
            // e.g. the 'chord($0)' item becomes '@chord()' (a recognized empty chord).
            var label = System.Text.RegularExpressions.Regex.Replace(raw, @"\$\{[^}]*\}|\$\d+", "");
            var tree = MusicSource.Parse("c4@" + label);

            var problems = tree.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message)
                .ToList();

            // A completion must never offer a name the collector then ignores: run
            // the annotation validator and reject any "Unknown annotation" warning.
            var validator = new AnnotationNameValidator();
            validator.Validate(tree);
            problems.AddRange(validator.Diagnostics
                .Where(d => d.Code == DiagnosticCodes.UnknownAnnotation)
                .Select(d => d.Message));

            if (problems.Count > 0)
                failures.Add($"@{label} -> {string.Join("; ", problems)}");
        }
        Assert.True(failures.Count == 0,
            "'@' completion offers items that don't parse or aren't recognized:\n"
            + string.Join("\n", failures));
    }

    [Fact]
    public void ChordCompletion_OnNote_InsertsParensForExplicitEntry()
    {
        var chord = LilySharpLanguageServer.GetArticulationCompletions(afterChord: false)
            .Items.Single(i => i.Label == "chord");
        Assert.Equal("chord($0)", chord.InsertText);
        Assert.NotNull(chord.Command); // re-triggers the diatonic-chord suggestions
    }

    [Fact]
    public void ChordCompletion_OnChord_InsertsBareAutoForm()
    {
        var chord = LilySharpLanguageServer.GetArticulationCompletions(afterChord: true)
            .Items.Single(i => i.Label == "chord");
        Assert.Equal("chord", chord.InsertText); // no '(…)': auto-derive from the notes
        Assert.Null(chord.Command);

        var tree = MusicSource.Parse("<c e g>@chord");
        Assert.DoesNotContain(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    // ----- which chord form the '@' completion offers -----

    private static bool AutoNames(string source)
    {
        int at = source.IndexOf('@') + 1;
        return LilySharpLanguageServer.AtFollowsChord(source, at)
            && LilySharpLanguageServer.GroupBeforeAtAutoNames(source, at);
    }

    [Theory]
    [InlineData("{ <c e g>@ }")]          // recognizable chord → bare @chord
    [InlineData("{ <c e g>4@ }")]         // duration between '>' and '@'
    [InlineData("{ <d f a c>@ }")]
    [InlineData("{ << c e g >>@ }")]      // arpeggio auto-names the same way
    [InlineData("{ << c e g >>4@ }")]
    [InlineData("{ <c 3 5>@ }")]          // degrees: key-dependent → collector's call
    [InlineData("{ << <c e> g >>@ }")]    // nested chord member
    public void RecognizableGroup_OffersBareChord(string source)
        => Assert.True(AutoNames(source));

    [Theory]
    [InlineData("{ <c cis d>@ }")]        // a cluster: no known quality
    [InlineData("{ <c>@ }")]              // a single pitch derives nothing
    [InlineData("{ << c cis d >>@ }")]
    public void UnrecognizableGroup_FallsBackToParenForm(string source)
        => Assert.False(AutoNames(source));

    [Fact]
    public void NoteBeforeAt_IsNotAChord()
        => Assert.False(LilySharpLanguageServer.AtFollowsChord("{ c4@ }", 5));
}
