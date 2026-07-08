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
            var label = string.IsNullOrEmpty(item.InsertText) ? item.Label : item.InsertText;
            var tree = SyntaxTree.Parse("{ c4@" + label + " }");

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
}
