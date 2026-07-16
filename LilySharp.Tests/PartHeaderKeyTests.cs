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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A key in a part header (<c>part p { key bes major  section A { … } }</c>) must parse
/// as a real KeySignature. It used to fall through the part-property parser to a skipped
/// token: its text width vanished from the tree, so EVERY following note's source offset
/// shifted left by the directive's length — which desynced the editor's caret→note
/// highlighting (the bug this guards). A key is legitimately per-part (unlike the
/// score-wide time/tempo), so it is kept, not rejected.
/// </summary>
[Trait("Category", "Unit")]
public class PartHeaderKeyTests
{
    private const string WithKey =
        "part melody { key bes major section A { c d e f | g a b c } } form main { A } score main { staff melody }";

    [Fact]
    public void PartHeaderKey_RoundTripsExactly()
    {
        // ToFullString != source means tokens were dropped — the exact failure that
        // shifted every downstream position.
        var root = SyntaxTree.Parse(WithKey).GetRoot();
        Assert.Equal(WithKey, root.ToFullString());
    }

    [Fact]
    public void PartHeaderKey_IsParsedAsAKeySignature()
    {
        var root = SyntaxTree.Parse(WithKey).GetRoot();
        var keys = root.DescendantNodes().Where(n => n.Kind == SyntaxKind.KeySignature).ToList();
        Assert.Single(keys);
        Assert.Equal("key bes major", keys[0].ToFullString().Trim());
    }

    [Fact]
    public void NotesAfterPartHeaderKey_KeepTheirTrueSourceOffsets()
    {
        // The first note 'c' sits at this absolute offset; its Pitch node must report it
        // (not a value reduced by the key directive's length).
        int cAbs = WithKey.IndexOf("c d e f", System.StringComparison.Ordinal);
        var root = SyntaxTree.Parse(WithKey).GetRoot();
        var atC = root.FindNode(cAbs);
        Assert.NotNull(atC);
        Assert.Equal(cAbs, atC!.Position);
        // The node at the note's offset is the pitch 'c' itself (PitchC), not a barline
        // or the key — the tell-tale of the old position shift.
        Assert.StartsWith("Pitch", atC.Kind.ToString());
    }

    [Fact]
    public void PartHeaderKey_DoesNotShiftPositions_VersusNoKey()
    {
        // Removing the key must move the first note by EXACTLY the key text's length —
        // proving the key contributes its full width (no silent drop).
        const string noKey =
            "part melody { section A { c d e f | g a b c } } form main { A } score main { staff melody }";
        int withPos = WithKey.IndexOf("c d e f", System.StringComparison.Ordinal);
        int withoutPos = noKey.IndexOf("c d e f", System.StringComparison.Ordinal);
        int keyLen = "key bes major ".Length;
        Assert.Equal(keyLen, withPos - withoutPos);

        var withRoot = SyntaxTree.Parse(WithKey).GetRoot();
        var withoutRoot = SyntaxTree.Parse(noKey).GetRoot();
        Assert.Equal(withPos, withRoot.FindNode(withPos)!.Position);
        Assert.Equal(withoutPos, withoutRoot.FindNode(withoutPos)!.Position);
    }

    [Theory]
    // A key with no mode is assumed major, in a part header AND in a music stream.
    [InlineData("part melody { key bes section A { c d e f } } form main { A } score main { staff melody }")]
    [InlineData("part melody { section A { key bes c d e f } } form main { A } score main { staff melody }")]
    public void KeyWithoutMode_WarnsAndIsNotAnError(string source)
    {
        var tree = SyntaxTree.Parse(source);
        // Assumed-major is a soft nudge, never a hard error — the piece still renders.
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        var w = Assert.Single(tree.Diagnostics.Where(d => d.Code == DiagnosticCodes.KeyModeAssumedMajor));
        Assert.Equal(DiagnosticSeverity.Warning, w.Severity);
        Assert.Contains("assuming major", w.Message);
    }

    [Fact]
    public void KeyWithExplicitMode_HasNoModeWarning()
    {
        var tree = SyntaxTree.Parse(WithKey);
        Assert.DoesNotContain(tree.Diagnostics, d => d.Code == DiagnosticCodes.KeyModeAssumedMajor);
    }
}
