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
/// A tie (<c>~</c>) joins two notes of the SAME pitch, binding to the immediately
/// following timed item. When that item repeats none of the tied pitches — a
/// different note, a chord with no matching pitch, or an audible rest — nothing
/// sensible gets tied, so LYS4007 warns (a slur was probably meant, or the target
/// was mistyped).
/// </summary>
[Trait("Category", "Unit")]
public class TieTargetValidatorTests
{
    private static int WarningCount(string music)
    {
        var source = $"part m {{ section A {{ {music} }} }} form main {{ A }} score main {{ staff m }}";
        var validator = new TieTargetValidator();
        validator.Validate(SyntaxTree.Parse(source));
        return validator.Diagnostics.Count(d => d.Code == DiagnosticCodes.TieTargetMismatch
            && d.Severity == DiagnosticSeverity.Warning);
    }

    [Theory]
    [InlineData("c4~ c2.")]                 // the plain tie
    [InlineData("c2.~ | c1")]               // across the barline (the common case)
    [InlineData("c4~ c~ c2")]               // a tie chain
    [InlineData("<c e g>4~ <c e g>2.")]     // chord to identical chord
    [InlineData("<c e g>4~ <c f a>2.")]     // partial chord match: the c still ties (LP-silent)
    [InlineData("<c e g>4~ c2.")]           // chord into a matching single note
    [InlineData("c4~ <c e>2.")]             // note into a chord that contains it
    [InlineData("c4~ time 3/4 c2.")]        // a mid-music change is transparent to the tie
    // `c2.~` used to sit here as "dangling at the end: out of scope, stays quiet". It is
    // not out of scope — the tie is dropped and nothing said so. See DanglingTie_Warns.
    [InlineData("c1@laissezVibrer")]        // the spelling for a tie that hangs into silence
    [InlineData("c1@repeatTie")]            // ...and for one resuming from a repeat
    public void MatchingTieTarget_NoWarning(string music) =>
        Assert.Equal(0, WarningCount(music));

    [Theory]
    [InlineData("c4~ d4 c2")]               // different letter
    [InlineData("c4~ c'4 c,2")]             // same letter, different octave
    [InlineData("c4~ cis4 c2")]             // same staff position, different pitch
    [InlineData("<c e g>4~ <d f a>2.")]     // chords sharing no pitch
    [InlineData("c4~ e2.")]                 // a slur was probably meant
    public void MismatchedTieTarget_Warns(string music) =>
        Assert.Equal(1, WarningCount(music));

    [Fact]
    public void TieIntoRest_Warns() => Assert.Equal(1, WarningCount("c4~ r4 c2"));

    [Theory]
    [InlineData("c2.~")]                    // the tie is the last thing in the voice
    [InlineData("c4 d4 e4 f4~")]            // ...after other notes
    [InlineData("<c e g>2.~")]              // a chord's tie, likewise
    public void DanglingTie_Warns(string music) =>
        // MEASURED: `c4 d4 e4 f4~ |` engraves byte-for-byte as the same bar with no tie at
        // all (1956 bytes either way), while `f4@laissezVibrer` draws the hanging tie
        // (2147). The mark is lost outright, so "nothing to compare" was true of the
        // comparison and false of the loss.
        Assert.Equal(1, WarningCount(music));

    [Fact]
    public void ADanglingTieIsPointedAtTheHangingTieSpelling()
    {
        // The complaint can name what was probably meant, because Lily# already spells it.
        var source = "part m { section A { c2.~ } } form main { A } score main { staff m }";
        var validator = new TieTargetValidator();
        validator.Validate(SyntaxTree.Parse(source));
        var d = Assert.Single(validator.Diagnostics.Where(x => x.Message.Contains("nothing after it")));
        Assert.Contains("@laissezVibrer", d.Message);
        Assert.Contains("no tie is drawn", d.Message);
    }

    [Fact]
    public void ATieDoesNotCarryIntoAnotherVoice() =>
        // The scan is per voice, like the slur's and the beam's: a tie left open when a
        // voice ends never binds to the next voice's first note, however well it matches.
        Assert.Equal(2, WarningCount("voice { c4 d4 e4 c4~ } { c4 d4 e4 c4~ }"));

    [Fact]
    public void TieIntoSpacer_StaysQuiet() =>
        // An invisible spacer is padding, not a target; the renderer draws no tie
        // but this is a layout idiom, not a pitch slip.
        Assert.Equal(0, WarningCount("c4~ s4 c2"));

    [Fact]
    public void EachMismatchWarnsOnce() =>
        Assert.Equal(2, WarningCount("c4~ d4 e4~ f4"));

    [Fact]
    public void RunsViaSemanticValidation() =>
        // Registered in SemanticValidation.CreateAll so the CLI's check and the
        // LSP's live diagnostics both surface it.
        Assert.Contains(
            SemanticValidation.Run(SyntaxTree.Parse(
                "part m { section A { c4~ d4 c2 } } form main { A } score main { staff m }")),
            d => d.Code == DiagnosticCodes.TieTargetMismatch);
}
