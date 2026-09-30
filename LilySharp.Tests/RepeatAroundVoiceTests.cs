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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A repeat whose body is a <c>voice { } { }</c> span draws the span's voices. Until
/// 2026-09-30 the music gather refused any container standing inside a processed container,
/// so both voices of the span gathered nothing: <c>repeat percent 3 { voice { … } { … } | }</c>
/// drew its first bar empty before the % signs (LilySharp-Omr
/// docs/repro/percent-around-voice.lys), and <c>unfold</c> lost every pass.
/// </summary>
[Trait("Category", "Unit")]
public class RepeatAroundVoiceTests
{
    /// <summary>Each measure of the staff's two voices as "N" (holds a note) or "-".</summary>
    private static (string V1, string V2) Voices(string music)
    {
        var tree = SyntaxTree.Parse(
            $"octave absolute\ntime 4/4\npart m {{ clef treble }}\nsection A {{ m {{ {music} }} }}\n"
            + "form main { ~A }\nscore main { staff m\n staff m }");
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var staff = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindAll(tree)[0])
            .StaffGroups.SelectMany(g => g.Staves).First();
        string Row(int v) => v < staff.Voices.Length
            ? string.Concat(staff.Voices[v].Measures.Select(m => m.Items.Any(i => i is NoteItem) ? "N" : "-"))
            : "";
        return (Row(0), Row(1));
    }

    [Theory]
    [InlineData("repeat percent 2 { voice { c'1 } { e1 } | } d'1 |", "N-N", "N--")]
    [InlineData("repeat percent 2 { voice { c'1 } { e1 } } d'1 |", "N-N", "N--")]
    [InlineData("repeat unfold 2 { voice { c'1 } { e1 } | } d'1 |", "NNN", "NN-")]
    public void ARepeatedVoiceSpan_DrawsItsVoices(string music, string voice1, string voice2)
        => Assert.Equal((voice1, voice2), Voices(music));
}
