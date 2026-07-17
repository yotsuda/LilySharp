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

namespace LilySharp.Tests.Lsp;

/// <summary>
/// A composite node's Span reaches to its FULL span (GreenSyntaxNode does not
/// compute its own leading/trailing trivia), so a diagnostic anchored on one
/// used to squiggle the whitespace BEFORE its first token — the underline
/// visibly overhung to the left of the code. TrimSpanToInk shrinks the reported
/// range to the ink, and is a no-op for the token-derived spans that were
/// already tight.
/// </summary>
public class DiagnosticSpanTrimTests
{
    [Fact]
    public void TrimsLeadingWhitespace()
        => Assert.Equal((3, 6), LilySharpLanguageServer.TrimSpanToInk("   abc", 0, 6));

    [Fact]
    public void TrimsTrailingWhitespaceAndNewline()
        => Assert.Equal((0, 3), LilySharpLanguageServer.TrimSpanToInk("abc \n", 0, 5));

    [Fact]
    public void NoOp_ForATightTokenSpan()
        => Assert.Equal((2, 5), LilySharpLanguageServer.TrimSpanToInk("  abc  ", 2, 5));

    [Fact]
    public void AllWhitespaceSpan_IsLeftUntouched()
        => Assert.Equal((1, 4), LilySharpLanguageServer.TrimSpanToInk("a    b", 1, 4));

    [Fact]
    public void RealCompositeDiagnostic_StartsOnInk_NotTheLeadingSpace()
    {
        // '<< c e g >>@staccato' warns (LYS4008) on the mark, a composite node
        // whose raw Span reaches back over the space before it. The trimmed
        // range must start on a non-whitespace character.
        const string text = "{ << c e g >>@staccato }";
        var tree = SyntaxTree.Parse(text);
        var diag = SemanticValidation.Run(tree)
            .First(d => d.Code == DiagnosticCodes.ArpeggioAnnotationUnsupported);

        var (start, end) = LilySharpLanguageServer.TrimSpanToInk(
            text, diag.Span.Start, diag.Span.Start + diag.Span.Length);

        Assert.False(char.IsWhiteSpace(text[start]), "squiggle starts on whitespace");
        Assert.True(start >= diag.Span.Start, "trim only moves the start rightward");
        Assert.True(end <= diag.Span.Start + diag.Span.Length);
    }
}
