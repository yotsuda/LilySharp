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

/// <summary>Owner's review 2026-10-30 of a composed rock piece: written in D-flat minor, its every A
/// was a B double-flat (LYS4034); its last chorus wrote @ff, @decresc, @fff (LYS4035).</summary>
public class TheoreticalKeyAndHairpinTests
{
    private static IReadOnlyList<Diagnostic> Run(string source) => SemanticValidation.Run(SyntaxTree.Parse(source));

    [Theory]
    [InlineData("des minor", "'key des minor' has 8 flats", "Write 'key cis minor' (4 sharps)")]
    [InlineData("aes minor", "'key aes minor' has 7 flats", null)]
    [InlineData("gis major", "'key gis major' has 8 sharps", "Write 'key aes major' (4 flats)")]
    [InlineData("fes major", "'key fes major' has 8 flats", "Write 'key e major' (4 sharps)")]
    public void AKeyPastSevenAccidentals_IsWarned_WithTheKeyThatSoundsTheSame(string key, string said, string? instead)
    {
        var warned = Run($"key {key}\nc'4 d' e' f' |\n").Where(d => d.Code == DiagnosticCodes.TheoreticalKey).ToList();
        if (instead == null)
        {
            Assert.Empty(warned); // seven is the most a signature carries without a double accidental
            return;
        }
        Assert.Contains(warned, d => d.Message.StartsWith(said) && d.Message.Contains(instead));
    }

    [Fact]
    public void AHairpinWhoseNextLevelGoesTheOtherWay_IsWarned()
    {
        static IEnumerable<Diagnostic> Hairpins(string music) => Run(music + "\n").Where(d => d.Code == DiagnosticCodes.HairpinAgainstItsLevel);
        Assert.Contains(Hairpins("c'4@ff d'@decresc e' f'@fff |"), d => d.Message.StartsWith("@decresc leads to @fff, louder than the @ff it starts from"));
        Assert.Contains(Hairpins("c'4@f d'@cresc e' f'@p |"), d => d.Message.StartsWith("@cresc leads to @p, softer than the @f it starts from"));
        Assert.Empty(Hairpins("c'4@ff d'@decresc e' f'@mf |"));
        Assert.Empty(Hairpins("c'4@p d'@cresc e' f'@f |"));
        // Meant: a subito, or no level before the hairpin in its line.
        Assert.Empty(Hairpins("c'4@ff d'@decresc e' f'@fff.subito |"));
        Assert.Empty(Hairpins("c'4 d'@decresc e' f'@fff |"));
        // Closed before the level: it led nowhere, and the level is a new thought.
        Assert.Empty(Hairpins("c'4@f d'@cresc e'@!cresc f' | g'1@p |"));
        Assert.Empty(Hairpins("c'4@f d'@cresc@!cresc e' f' | g'1@p |"));
        // Closed on the level's own note, in either order: it did lead there.
        Assert.Single(Hairpins("c'4@f d'@cresc e' f'@!cresc@p |"));
        Assert.Single(Hairpins("c'4@f d'@cresc e' f'@p@!cresc |"));
        // The line runs through a tuplet's braces, and stops at a voice's.
        Assert.Empty(Hairpins("c'4@f d'@cresc tuplet 3/2 { e'8 f' g'@!cresc } a'4 | b'1@p |"));
        Assert.Single(Hairpins("c'4@f d'@cresc tuplet 3/2 { e'8 f' g' } a'4 | b'1@p |"));
        Assert.Empty(Hairpins("voice { c'4@f d'@cresc e' f' | } { g1@p | }"));
        // A cue is another instrument's line: its level is not this hairpin's.
        Assert.Empty(Hairpins("c'4@p d'@cresc e' f' | cue { g'4@pp a' b' c'' } |"));
        // A closed hairpin leaves the next one to be judged by itself.
        Assert.Single(Hairpins("c'4@f d'@cresc@!cresc e'@cresc f' | g'1@p |"));
    }
}
