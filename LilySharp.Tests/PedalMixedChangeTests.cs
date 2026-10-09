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

using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A MIXED pedal prints its "Ped." at the first engage only: each re-pedalling inside the
/// span is the bracket's notch. The TEXT style still prints a word at every engage.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/piano-pedal-engraver.cc:291-303 create_text_grobs — under the mixed
///   style a STOP and a START together make no text. MEASURED on 2.26.0 (Lab
///   sessions/p733/ped pm): one "Ped." and two notches. Until session 733 Lily# printed a
///   "Ped." at every change, over the end of the line before it.
/// </remarks>
[Trait("Category", "Unit")]
public class PedalMixedChangeTests
{
    private static string Book(string style) => $$"""
        octave absolute
        time 3/4
        part lh { clef bass pedal {{style}} }
        section S { lh { c4@sustain e g | c4@sustain e g | c4@sustain e g | c2.@!sustain | } }
        form { ~S }
        score { staff lh }
        """;

    private static int PedWords(string style)
    {
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(Book(style)),
            new SvgRenderOptions { EmbedFont = false });
        return Regex.Matches(svg, Regex.Escape(EmmentalerGlyphs.PedalPed.ToString())).Count;
    }

    [Fact]
    public void AMixedPedal_PrintsItsWordOnceAcrossItsChanges() => Assert.Equal(1, PedWords("mixed"));

    [Fact]
    public void ATextPedal_StillPrintsAWordAtEveryEngage() => Assert.Equal(3, PedWords("text"));
}
