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
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A slur mark that pairs with nothing draws no slur: <c>SlurDetector</c> discards a
/// <c>)</c> it cannot pop for and everything still open when a voice ends. LYS4010 says
/// so, because the score just loses a phrase mark otherwise.
/// </summary>
[Trait("Category", "Unit")]
public class SlurPairingValidatorTests
{
    private static IReadOnlyList<Diagnostic> Warnings(string music)
    {
        var source = $"part m {{ section A {{ {music} }} }} form main {{ A }} score main {{ staff m }}";
        var validator = new SlurPairingValidator();
        validator.Validate(SyntaxTree.Parse(source));
        return validator.Diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnpairedSlur
                        && d.Severity == DiagnosticSeverity.Warning)
            .ToList();
    }

    private static int WarningCount(string music) => Warnings(music).Count;

    [Theory]
    [InlineData("c4( d e f)")]                  // the plain slur
    [InlineData("c4( d) e( f)")]                // two in a row
    [InlineData("c4( d( e) f)")]                // nested
    [InlineData("c2.( | d1)")]                  // across the barline
    [InlineData("<c e>4( <d f>2.)")]            // chords
    [InlineData("c4( d) e f")]                  // plain notes after a closed slur
    [InlineData("c4 d e f")]                    // no slur at all
    [InlineData("c4( r4 d2)")]                  // a rest under the slur is transparent
    public void PairedSlurs_NoWarning(string music) =>
        Assert.Equal(0, WarningCount(music));

    [Theory]
    [InlineData("c4( d e f")]                   // never closed
    [InlineData("c4( d( e) f")]                 // the outer one never closed
    [InlineData("c2.( | d1")]                   // still open at the end, across a barline
    public void UnclosedSlur_Warns(string music) =>
        Assert.Equal(1, WarningCount(music));

    [Theory]
    [InlineData("c4 d) e f")]                   // nothing was open
    [InlineData("c4( d) e) f")]                 // one too many
    public void SurplusClose_Warns(string music) =>
        Assert.Equal(1, WarningCount(music));

    [Fact]
    public void OpenBeforeAnyNote_WarnsThroughItsClose() =>
        // `(e c4 d)` — the '(' annotates the note BEFORE it and there is none, so it never
        // becomes a mark at all (MeasureCollector.MusicWalk PeekMarkers). What is left is a
        // ')' with nothing open, which is the bar of music that silently lost its slur and
        // the reason this diagnostic exists.
        Assert.Equal(1, WarningCount("(e c4 d)"));

    [Fact]
    public void AnUnclosedSlurAndASurplusClose_AreDistinguished()
    {
        Assert.True(Warnings("c4( d e f").Single().Message.Contains("never closed"));
        Assert.True(Warnings("c4 d) e f").Single().Message.Contains("no '(' open"));
    }

    [Fact]
    public void EachUnpairedMarkWarnsOnce() =>
        Assert.Equal(2, WarningCount("c4( d e( f"));

    [Fact]
    public void ASlurDoesNotCarryIntoAnotherVoice() =>
        // SlurDetector clears its stack at each voice change (Slur_engraver lives in the
        // Voice context), so voice one's '(' can never pair with voice two's ')' — both
        // marks are dropped, and both are reported.
        Assert.Equal(2, WarningCount("voice { c4( d e f } voice { g4 a b c') }"));

    [Fact]
    public void RunsViaSemanticValidation() =>
        // Registered in SemanticValidation.CreateAll so the CLI's check and the LSP's live
        // diagnostics both surface it.
        Assert.Contains(
            SemanticValidation.Run(SyntaxTree.Parse(
                "part m { section A { c4( d e f } } form main { A } score main { staff m }")),
            d => d.Code == DiagnosticCodes.UnpairedSlur);
}
