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
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The '@chord(…)' completion offers the current key's seven diatonic chords, in
/// scale order, spelled as chord symbols (C, Dm, …, Bdim).
/// </summary>
[Trait("Category", "Unit")]
public class DiatonicChordCompletionTests
{
    private static string[] Chords(string text)
    {
        int offset = text.IndexOf("()") + 1;   // caret between the parens
        Assert.True(LilySharpLanguageServer.IsInsideChordAnnotation(text, offset));
        return LilySharpLanguageServer.GetDiatonicChordCompletions(text, offset)
            .Items.Select(i => i.Label).ToArray();
    }

    [Fact]
    public void CMajor()
        => Assert.Equal(new[] { "C", "Dm", "Em", "F", "G", "Am", "Bdim" },
            Chords("key c major\nc'4@chord()"));

    [Fact]
    public void DMajor_SpellsSharps()
        => Assert.Equal(new[] { "D", "Em", "F#m", "G", "A", "Bm", "C#dim" },
            Chords("key d major\nc'4@chord()"));

    [Fact]
    public void BbMajor_SpellsFlats()
        => Assert.Equal(new[] { "Bb", "Cm", "Dm", "Eb", "F", "Gm", "Adim" },
            Chords("key bes major\nc'4@chord()"));

    [Fact]
    public void AMinor()
        => Assert.Equal(new[] { "Am", "Bdim", "C", "Dm", "Em", "F", "G" },
            Chords("key a minor\nc'4@chord()"));

    [Fact]
    public void NoKey_DefaultsToCMajor()
        => Assert.Equal(new[] { "C", "Dm", "Em", "F", "G", "Am", "Bdim" },
            Chords("c'4@chord()"));

    [Fact]
    public void IsInsideChordAnnotation_TracksPosition()
    {
        var t = "c'4@chord(Cm) d'4@fig(6)";
        Assert.True(LilySharpLanguageServer.IsInsideChordAnnotation(t, t.IndexOf("Cm") + 1));   // inside @chord
        Assert.False(LilySharpLanguageServer.IsInsideChordAnnotation(t, t.IndexOf(") d") + 2)); // after ')'
        Assert.False(LilySharpLanguageServer.IsInsideChordAnnotation(t, t.IndexOf("6)")));       // inside @fig, not @chord
    }
}
