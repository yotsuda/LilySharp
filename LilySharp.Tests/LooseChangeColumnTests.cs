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
using LilySharp.Core.Svg;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A mid-measure change column whose own-staff neighbor pair disagrees with the column
/// list — another staff's column stands in between — is LOOSE: pruned from the spring
/// chain and draped back from its right neighbor after the line is solved, so it may
/// share X room with the other staff's column instead of pushing it away.
/// LILYPOND-REF: lily/spacing-determine-loose-columns.cc:45-133 is_loose_column;
/// LILYPOND-REF: lily/spacing-loose-columns.cc:33-222 set_loose_columns.
/// </summary>
[Trait("Category", "Unit")]
public class LooseChangeColumnTests
{
    [Fact]
    public void MidMeasureClef_HangsLooseBetweenTheOtherStaffsColumns()
    {
        // spacing-loose-polyphony.ly: a grand staff whose lower staff changes clef
        // mid-measure while the upper staff's tuplet puts a column (the A4 half note
        // at t = 1/6) BETWEEN the clef's own-staff neighbors (t = 1/8 and t = 1/4).
        // Three port pieces meet here, pinned against the LP twin
        // (scratch\lpreg\sploose.{lys,-gen.ly,svg,-lp.svg}):
        //  - the clef column is pruned, so the two cross-staff pairs price BARE
        //    (springs.empty hemiola branch: raw duration ideals 0.80/1.60, min 0);
        //  - the pruned column's room comes back as a rod over its neighbors, whose
        //    BLOCKING force stretches the two springs by stiffness to 1.25/2.50
        //    (simple-spacer.cc add_rod — not a proportional ideal scale);
        //  - the clef itself hangs from its right neighbor at the tight distance
        //    (scale factor 0: the A4's ink eats the permissible room), landing at
        //    14.26 — INSIDE the A4 half note's span, which only a loose column may.
        string svg = Render(
            "octave absolute\n\n" +
            "part rh { clef treble }\n" +
            "part lh { clef bass }\n" +
            "section S {\n" +
            "  rh { tuplet 3/2 { g4 a2 } | }\n" +
            "  lh { fis,,8 cis, clef treble g8 fis, | }\n" +
            "}\n" +
            "form main { S }\n" +
            "score main \"loose\" { grandStaff { staff rh staff lh } }\n");

        var blacks = MusicGlyphs(svg, EmmentalerGlyphs.NoteheadBlack);
        Assert.Equal(5, blacks.Count);
        Assert.Equal(9.45, blacks[0].X, 0.02);   // LP 9.45  (both staves' first heads)
        Assert.Equal(9.45, blacks[1].X, 0.02);   // LP 9.45
        Assert.Equal(12.81, blacks[2].X, 0.02);  // LP 12.81 (C#3, the clef's left neighbor)
        Assert.Equal(16.56, blacks[3].X, 0.02);  // LP 16.56 (G4, the clef's right neighbor)
        Assert.Equal(19.07, blacks[4].X, 0.02);  // LP 19.07 (F#3)

        var half = MusicGlyphs(svg, EmmentalerGlyphs.NoteheadHalf);
        var clef = MusicGlyphs(svg, EmmentalerGlyphs.GClefChange);
        Assert.Single(half);
        Assert.Single(clef);
        Assert.Equal(14.06, half[0].X, 0.02);    // LP 14.06 (A4 — the intervening column)
        Assert.Equal(14.26, clef[0].X, 0.02);    // LP 14.26 (hung at tight from G4)

        // The loose behavior itself: the clef stands inside the A4's ink span — a
        // spring-chained column could never start past that column's origin yet
        // before its ink ends.
        Assert.True(clef[0].X > half[0].X);
        Assert.True(clef[0].X < half[0].X + EngravingDefaults.NoteheadHalfWidth);
    }

    private static string Render(string source) =>
        LilySharp.Core.Svg.SvgGenerator.Generate(
            SyntaxTree.Parse(source),
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });

    /// <summary>All music glyphs of one codepoint: (X, Y) in document order, X-sorted.</summary>
    private static List<(double X, double Y)> MusicGlyphs(string svg, char glyph) =>
        System.Text.RegularExpressions.Regex.Matches(svg,
                "<text class=\"music\" x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"[^>]*>(.)</text>")
            .Where(m => m.Groups[3].Value[0] == glyph)
            .Select(m => (X: double.Parse(m.Groups[1].Value), Y: double.Parse(m.Groups[2].Value)))
            .OrderBy(g => g.X)
            .ToList();
}
