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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A tab staff carries EVERY voice of its part: a <c>voice { } { }</c> span's second
/// voice prints fret digits too, on the columns the voices share.
/// LILYPOND-REF: ly/engraver-init.ly TabStaff — defaultchild TabVoice, accepts TabVoice.
/// </summary>
[Trait("Category", "Unit")]
public class TabPolyphonyTests
{
    [Fact]
    public void SecondVoiceFretsPrintOnTheSharedColumns()
    {
        // automatic-polyphony-tabstaff.ly: c'1 | << { c'4 d' e' f' } \\ { g,1 } >> | c'1
        // on a treble_8 guitar part, tab as numbers (LP's default digits-only TabStaff).
        // LP 2.26.0 prints SEVEN digits — frets 1,1,3,0,1,1 on the upper strings and the
        // g,1 as fret 3 on the LOWEST string, sharing the t=0 column with voice one's c'.
        // Before Staff.CreateTab carried all voices, the tab dropped voice two entirely.
        // Pinned against the LP twin (audit\lpreg\apts-lp.{ly,svg}).
        string svg = Render(
            "octave absolute\n\npart gt { clef treble_8 tuning guitar }\n\n"
            + "section Main {\n  gt {\n    c'1 |\n"
            + "    voice { c'4 d' e' f' } { g,1 } |\n    c'1 |\n  }\n}\n\n"
            + "form main { ~Main }\n\nscore main { staff gt tab gt as numbers }\n");

        var digits = System.Text.RegularExpressions.Regex.Matches(svg,
                "<text x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"[^>]*font-weight=\"bold\"[^>]*>(\\d+)</text>")
            .Select(m => (X: double.Parse(m.Groups[1].Value),
                          Y: double.Parse(m.Groups[2].Value),
                          Fret: m.Groups[3].Value))
            .ToList();

        Assert.Equal(7, digits.Count);                       // LP: seven digits, both voices

        // The g,1: fret 3 on the LOWEST string (largest device Y), four string spaces
        // below the B-string row (LP 25.8632 − 19.8755 = 5.99).
        var low = digits.OrderByDescending(d => d.Y).First();
        Assert.Equal("3", low.Fret);
        double mainRow = digits.Where(d => d != low).Max(d => d.Y);
        Assert.Equal(5.99, low.Y - mainRow, 0.05);

        // ...and it shares the t=0 column with voice one's c' (LP: both at one X).
        var vOneC = digits.Where(d => d != low).OrderBy(d => d.X).ElementAt(1);
        Assert.Equal(vOneC.X, low.X, 0.011);
    }

    private static string Render(string source) =>
        LilySharp.Core.Svg.SvgGenerator.Generate(
            SyntaxTree.Parse(source),
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });
}
