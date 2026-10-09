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
/// A text-style pedal word under a system is where the page's system spacing reads it —
/// at its solved row, the one the draw uses.
/// </summary>
/// <remarks>
/// Until 2026-09-26 the preliminary annotation pass (the one whose extents the page
/// spaces systems by) had no solved pedal rows and placed every "Ped." by the legacy
/// stack, closer under the staff than the final pass draws it: the page left room for
/// the word where it was not, and fantasia.lys drew bar 7's "Ped." through the next
/// system's "Agitato (♩ = 112)". MEASURED on LilyPond 2.26.0 (the twin of the book below,
/// an A4 page): the systems' facing staff middles stand 12.0 apart with only the pedal
/// or only the tempo mark, and 13.6 with both — the pair pushes them apart.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class PedalTextPageSpacingTests
{
    private static string Book(string rh, string lh) => $$"""
        octave absolute
        part rh { clef treble }
        part lh { clef bass  pedal text }
        section A {
          rh { {{rh}} }
          lh { {{lh}} }
        }
        form { ~A }
        score { grandStaff { staff rh  staff lh } }
        """;

    private const string TempoRh = "c'1 | break tempo \"Agitato\" 4 = 112 c'1 |";
    private const string PlainRh = "c'1 | break c'1 |";
    private const string PedalLh = "c,8@sustain g, c e c,@sustain g, c e | c1@!sustain |";
    private const string PlainLh = "c,8 g, c e c, g, c e | c1 |";

    /// <summary>The blank between system 1's lowest staff line and system 2's highest.</summary>
    private static double SystemGap(string source)
    {
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(source), new SvgRenderOptions { EmbedFont = false });
        var ys = Regex.Matches(svg, "<line x1=\"[^\"]+\" y1=\"([0-9.]+)\" x2=\"[^\"]+\" y2=\"\\1\"[^>]*stroke-width=\"0\\.1")
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .Distinct().OrderBy(y => y).ToList();
        // The widest step between consecutive staff lines is the one between the systems.
        int at = Enumerable.Range(1, ys.Count - 1).MaxBy(i => ys[i] - ys[i - 1]);
        return ys[at] - ys[at - 1];
    }

    [Fact]
    public void APedalWordUnderASystem_PushesTheNextSystemsTempoMarkAway()
    {
        double both = SystemGap(Book(TempoRh, PedalLh));
        double tempoOnly = SystemGap(Book(TempoRh, PlainLh));
        double pedalOnly = SystemGap(Book(PlainRh, PedalLh));

        // Alone, neither reaches the other system: the basic distance holds (LilyPond: 12.0
        // between the facing staff middles, i.e. 8.0 between the facing outer lines).
        Assert.Equal(8.0, tempoOnly, 2);
        Assert.Equal(8.0, pedalOnly, 2);
        // Together they meet, and the page makes room (LilyPond 13.6 → 9.6 between the lines).
        Assert.InRange(both, 9.4, 9.8);
    }
}
