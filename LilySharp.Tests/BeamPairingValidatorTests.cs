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
/// A manual beam bracket that pairs with nothing builds no group: <c>BeamDetector</c>
/// discards it and the notes fall back to AUTOMATIC beaming, so the engraved grouping is
/// not the written one. LYS4016 says so — the same shape as the slur's LYS4010, and for
/// the same reason (the score silently stops saying what the file said).
/// </summary>
[Trait("Category", "Unit")]
public class BeamPairingValidatorTests
{
    private static IReadOnlyList<Diagnostic> Warnings(string music)
    {
        var source = $"octave absolute\ntime 4/4\npart m {{ clef treble }}\n"
                     + $"section A {{ m {{ {music} }} }}\nform main {{ ~A }}\nscore main {{ staff m }}\n";
        var validator = new BeamPairingValidator();
        validator.Validate(SyntaxTree.Parse(source));
        return validator.Diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnpairedBeam
                        && d.Severity == DiagnosticSeverity.Warning)
            .ToList();
    }

    [Theory]
    [InlineData("c8[ d8 e8 f8 g8] a8 b8 c8 |")]                    // a five-note manual group
    [InlineData("c8[ d8] e8[ f8] g8 a8 b8 c8 |")]                  // two groups in a row
    [InlineData("c8 d8 e8 f8 g8 a8 b8 c8 |")]                      // no bracket at all
    [InlineData("c4 d4 e4 f4 |")]                                  // nothing beamable
    // A pair may span the barline — the detector matches across measures, so this is not
    // an unclosed bracket.
    [InlineData("c8[ d8 e8 f8 g8 a8 b8 c8 | d8 e8 f8] g8 a8 b8 c8 d8 |")]
    public void PairedBrackets_NoWarning(string music) =>
        Assert.Empty(Warnings(music));

    [Theory]
    [InlineData("c8[ d8 e8 f8 g8 a8 b8 c8 |")]                     // never closed
    [InlineData("c8 d8 e8 f8 g8] a8 b8 c8 |")]                     // nothing open
    public void UnpairedBracket_OneWarning(string music) =>
        Assert.Single(Warnings(music));

    [Fact]
    public void EachUnpairedBracketIsReported()
    {
        // A ']' with nothing open AND a '[' left open: two independent losses, two words.
        Assert.Equal(2, Warnings("c8] d8 e8 f8 g8[ a8 b8 c8 |").Count);
    }

    [Fact]
    public void TheMessageSaysTheGroupingIsLost_NotTheBeam()
    {
        // The distinction from the slur, and it is measured rather than assumed: dropping a
        // bracket does not leave the notes unbeamed, it leaves them beamed AUTOMATICALLY.
        // A message borrowed from the slur ("no beam is drawn") would be false.
        var d = Assert.Single(Warnings("c8[ d8 e8 f8 g8 a8 b8 c8 |"));
        Assert.Contains("grouping is discarded", d.Message);
        Assert.Contains("beamed automatically", d.Message);
        Assert.DoesNotContain("no beam is drawn", d.Message);
    }

    [Fact]
    public void AnOpenBracketDoesNotCarryIntoAnotherVoice()
    {
        // The detector matches per voice, so a '[' left open when a voice ends never pairs.
        // Both voices lose their bracket here, and both are reported.
        Assert.Equal(2, Warnings("voice { c8[ d8 e8 f8 g8 a8 b8 c8 | } { e8[ f8 g8 a8 b8 c8 d8 e8 | }").Count);
    }
}
