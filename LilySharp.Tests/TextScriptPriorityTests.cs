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

using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Free expressive text (<c>@text</c>) is LilyPond's TextScript, placed in the outside-staff
/// pass at its own priority 450 — after the dynamics (250) and the hairpins, on both sides of
/// the staff. Until 2026-10-02 it stood in the dynamics' 250 turn in source order, so a text
/// on one note pushed the dynamic of the next note away from the staff, where LilyPond seats
/// the dynamic by the staff and the text beyond it.
/// </summary>
/// <remarks>
/// The numbers are LilyPond 2.26.0's, read off its svg with the text faces pinned
/// (<c>lysc ly --pin-fonts</c>; Lab sessions/p739/textosp, books u2, u3, h4, h2). Without
/// the pin LilyPond sets the text in a fallback serif, and session 644 read a 0.12 difference
/// on h2 that was the face, not the order (pinned, h2 agrees to the hundredth).
/// LILYPOND-REF: scm/define-grobs.scm:3800-3807 TextScript outside-staff-priority 450.
/// </remarks>
[Trait("Category", "Unit")]
public class TextScriptPriorityTests
{
    private static readonly SvgRenderOptions Opt = new() { EmbedFont = false };

    private static double Y(string music, string text)
    {
        string book = "octave absolute\npart m { clef treble }\n"
                      + $"section A {{ m {{ {music} }} }}\nform main {{ ~A }}\nscore main {{ staff m }}\n";
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(book), Opt);
        var m = Regex.Match(svg, $"<text x=\"[\\d.]+\" y=\"([\\d.]+)\"[^>]*>{Regex.Escape(text)}</text>");
        Assert.True(m.Success, $"no <text> '{text}' in the page");
        return double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    [Theory]
    // A text on one note, a dynamic on the next under it: the p by the staff, the text below.
    [InlineData("c'4@text(\"dolce\") d'@p e' f' |", "p", 15.9132)]
    [InlineData("c'4@text(\"dolce\") d'@p e' f' |", "dolce", 18.5744)]
    // The same above the staff.
    [InlineData("c''4@text(\"dolce\").up d''@f.up e'' f'' |", "f", 10.7011)]
    [InlineData("c''4@text(\"dolce\").up d''@f.up e'' f'' |", "dolce", 8.3120)]
    // A hairpin from the next note: the text clears it.
    [InlineData("c'4@text(\"dolce\") d'@cresc e' f' | g'1@f |", "dolce", 17.7944)]
    // The same note: unchanged, and LilyPond's.
    [InlineData("c'4@p@cresc@text(\"dolce\") d' e' f' | g'1@f |", "dolce", 18.4936)]
    public void TheText_StandsWhereLilyPondPutsIt(string music, string text, double lilyPond)
    {
        Assert.Equal(lilyPond, Y(music, text), 0.011);
    }
}
