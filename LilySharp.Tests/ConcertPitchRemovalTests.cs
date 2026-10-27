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
using LilySharp.Core.Editing;
using LilySharp.Core.Midi;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A composed piece, written at concert pitch, opens without its <c>pitch concert</c>: each part
/// as its player reads it, sounding as before (owner's request 2026-10-28).
/// </summary>
[Trait("Category", "Unit")]
public class ConcertPitchRemovalTests
{
    private static string Sounds(string text)
        => string.Join(" ", new MidiExporter().Export(SyntaxTree.Parse(text)).Tracks
            .SelectMany(t => t.Notes).OrderBy(n => n.StartTick).ThenBy(n => n.Pitch).Select(n => n.Pitch));

    private static string Band(string mode, string bass, string extra = "")
        => $"{mode}\npitch concert\ntime 4/4\npart gtr {{ instrument guitar }}\npart bass {{ instrument bass }}\n{extra}"
         + $"section A {{\n  gtr {{ <e, b, e>2 <a, e a>2 | }}\n  bass {{ {bass} }}\n}}\nform {{ A }}\nscore {{ staff gtr  staff bass }}\n";

    [Theory]
    [InlineData("octave absolute", "e,,4 a,, d, g, |")]   // the bass's strings, as they sound
    [InlineData("", "e,,4 a d g |")]                     // relative: the chain's first note moves
    public void ABassWrittenAtConcertPitch_OpensAsItsPlayerReadsIt(string mode, string bass)
    {
        string text = Band(mode, bass);
        var result = OctaveModeConverter.WithoutConcertPitch(text);
        Assert.NotNull(result.NewText);
        Assert.DoesNotContain("pitch concert", result.NewText);
        Assert.Equal(Sounds(text), Sounds(result.NewText!));
        // The bass is now written an octave up (its low E `e,`), the guitar as it was.
        Assert.Contains("bass { e,4", result.NewText);
        Assert.Contains("<e, b, e>2", result.NewText);
    }

    // A trumpet beside the bass is RESPELLED to its player's letters, a major second up, and
    // reads its player's key — the file's C major moved to D (owner's request 2026-10-28, after
    // the first answer kept its letters under its own `pitch concert`).
    [Fact]
    public void AChromaticTransposer_IsRespelledForItsPlayer_InItsPlayersKey()
    {
        string text = Band("octave absolute", "e,,1 |", "part tp { instrument trumpet }\n")
            .Replace("  bass {", "  tp { c'2 bes2 | }\n  bass {").Replace("staff bass }", "staff bass  staff tp }");
        var result = OctaveModeConverter.WithoutConcertPitch(text);
        Assert.NotNull(result.NewText);
        Assert.DoesNotContain("pitch concert", result.NewText);
        Assert.Contains("part tp { instrument trumpet key d major }", result.NewText);
        Assert.Contains("tp { d'2 c'2 | }", result.NewText);      // bes (B♭3) is written c' (C4)
        Assert.Contains("bass { e,1", result.NewText);
        Assert.Equal(Sounds(text), Sounds(result.NewText!));
    }

    // A key the trumpet shares with the other parts — one in a section's header — cannot move for
    // the trumpet alone: it keeps its letters, under its own `pitch concert`.
    [Fact]
    public void AChromaticTransposerUnderASharedSectionKey_KeepsItsLetters()
    {
        string text = Band("octave absolute", "e,,1 |", "part tp { instrument trumpet }\n")
            .Replace("  bass {", "  key f major\n  tp { c'2 d'2 | }\n  bass {").Replace("staff bass }", "staff bass  staff tp }");
        var result = OctaveModeConverter.WithoutConcertPitch(text);
        Assert.NotNull(result.NewText);
        Assert.Contains("part tp { instrument trumpet pitch concert }", result.NewText);
        Assert.Contains("tp { c'2 d'2 | }", result.NewText);
        Assert.Equal(Sounds(text), Sounds(result.NewText!));
    }

    [Fact]
    public void AFileNotAtConcertPitch_IsLeftAsItIs()
    {
        const string text = "time 4/4\npart bass { instrument bass }\nsection A { bass { e,4 a, d g | } }\nform { A }\nscore { staff bass }\n";
        Assert.Equal(text, OctaveModeConverter.WithoutConcertPitch(text).NewText);
    }
}
