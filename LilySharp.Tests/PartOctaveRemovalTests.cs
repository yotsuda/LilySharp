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
/// A composed piece's parts lose their <c>octave N</c> before it opens, every note sounding where
/// it did (owner's request 2026-10-28: the bass is written under <c>octave 4</c> so the AI can name
/// the pitches it sounds, and the line is the AI's crutch, not the musician's).
/// </summary>
[Trait("Category", "Unit")]
public class PartOctaveRemovalTests
{
    private static string Sounds(string text)
        => string.Join(" ", new MidiExporter().Export(SyntaxTree.Parse(text)).Tracks
            .SelectMany(t => t.Notes).OrderBy(n => n.StartTick).ThenBy(n => n.Pitch).Select(n => n.Pitch));

    [Theory]
    // Absolute: every pitch is re-marked.
    [InlineData("octave absolute\n", "e,,4 a,, d, g, | e,,1 |")]
    // Relative: only where a frame opens — each section's first note, a phrase body's.
    [InlineData("", "e,,4 a d g | e1 |")]
    public void TheBassLosesItsOctave_AndSoundsAsBefore(string mode, string music)
    {
        string text = $"{mode}time 4/4\nphrase riff {{ e,,8 b, e b, }}\npart bass \"Bass\" {{ instrument bass octave 4 }}\n"
            + $"section A {{ bass {{ {music} }} }}\nsection B {{ bass {{ riff riff | a,,1 | }} }}\nform {{ A B }}\nscore {{ staff bass }}\n";
        var result = OctaveModeConverter.WithoutPartOctaves(text);
        Assert.NotNull(result.NewText);
        Assert.DoesNotContain("octave 4", result.NewText);
        Assert.Contains("instrument bass }", result.NewText);
        Assert.Equal(Sounds(text), Sounds(result.NewText!));
        Assert.True(result.ChangedPitches > 0);
    }

    [Fact]
    public void APieceWithNoPartOctave_IsLeftAsItIs()
    {
        const string text = "time 4/4\npart bass { instrument bass }\nsection A { bass { e,4 a, d g | } }\nform { A }\nscore { staff bass }\n";
        var result = OctaveModeConverter.WithoutPartOctaves(text);
        Assert.Equal(text, result.NewText);
        Assert.Equal(0, result.ChangedPitches);
    }
}
