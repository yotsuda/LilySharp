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

using System;
using System.Linq;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The drift net of <see cref="LanguageReference"/>: SignatureHelp once carried a
/// <c>relative</c> entry for grammar the parser rejects (Parser.Directives "Lily#
/// is relative by default — drop it"), and nothing could notice, because the
/// signature table was prose about the grammar with no tie to it. Every table
/// row now carries a compilable <c>Sample</c> of the grammar it advertises;
/// this net parses each one, and requires the construct's hover to answer in
/// the same sample — a signature for dead grammar, or a keyword whose hover
/// was dropped, is a red test, not a popup lying in an editor.
/// </summary>
[Trait("Category", "Unit")]
public class LanguageReferenceTests
{
    [Fact]
    public void EverySignatureSampleCompiles()
    {
        Assert.NotEmpty(LanguageReference.Signatures);
        foreach (var entry in LanguageReference.Signatures)
        {
            // The sample must actually exercise the advertised keyword…
            Assert.Contains(entry.Keyword, entry.Sample, StringComparison.Ordinal);
            // …and the parser must accept it (the `relative` case would fail here).
            var tree = SyntaxTree.Parse(entry.Sample);
            Assert.False(tree.HasErrors,
                $"the signature table advertises '{entry.Keyword}' but its sample "
                + $"does not parse: {string.Join("; ", tree.Diagnostics.Select(d => d.Message))}");
        }
    }

    [Fact]
    public void EverySignatureConstructAnswersItsHover()
    {
        foreach (var entry in LanguageReference.Signatures)
        {
            var tree = SyntaxTree.Parse(entry.Sample);
            bool answered = tree.GetNodes<SyntaxNode>()
                .Select(LanguageReference.Hover)
                .Any(h => h != null && h.StartsWith(entry.HoverMarker, StringComparison.Ordinal));
            Assert.True(answered,
                $"no node of '{entry.Keyword}'s sample hovers as {entry.HoverMarker} — "
                + "the signature table and the hover switch have drifted apart");
        }
    }
}
