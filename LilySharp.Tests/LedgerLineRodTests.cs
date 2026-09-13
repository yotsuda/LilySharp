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
using Xunit;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;

namespace LilySharp.Tests;

/// <summary>
/// LilyPond's ledger-line spacing rod: two consecutive columns whose heads carry ledger lines on
/// the same side stand at least <c>2 × head width × 0.25 + head width</c> apart.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/ledger-line-spanner.cc:39-61 set_rods. Ported in session 379 (owner
/// approval) after scratch/p380/incr/cols.ps1 put floor2.lys's whole 0.45 shortfall on the three
/// gaps between the ledgered 32nds: LilyPond 1.9563 each, Lily# 1.80 (the skyline minimum plus
/// merge_springs' headroom), constant over increments 1.0-1.5.
/// </remarks>
[Trait("Category", "Unit")]
public class LedgerLineRodTests
{
    private const string Floor2 = """
        paper { raggedRight }
        octave absolute
        part m { clef treble
          section A { c'8 d' e' f' g' a' b' c'' | d''8 c'' b' a' g' f' e' d' | c'8 d' e' f' g'32 a' b' c'' d''8 e'' f'' | e''8 d'' c'' b' a' g' f' e' | }
        }
        form main { A }
        score main { staff m }
        """;

    private static string Svg(string source) =>
        SvgGenerator.Generate(SyntaxTree.Parse(source), new SvgRenderOptions { EmbedFont = false });

    /// <summary>Note-head X positions (the leftmost glyph of each note's data-pos, the clef dropped).</summary>
    private static double[] HeadXs(string svg)
    {
        var byPos = new System.Collections.Generic.Dictionary<int, double>();
        foreach (Match m in Regex.Matches(svg, "<text class=\"music\" x=\"([-\\d.]+)\" y=\"[-\\d.]+\"[^>]*data-pos=\"(\\d+)\""))
        {
            double x = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            int p = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (!byPos.TryGetValue(p, out double old) || x < old)
                byPos[p] = x;
        }
        return byPos.OrderBy(kv => kv.Key).Skip(1).Select(kv => kv.Value).ToArray();
    }

    [Fact]
    public void LedgeredThirtySecondNotes_StandAtTheRodLilyPondRaises()
    {
        var xs = HeadXs(Svg(Floor2));
        Assert.Equal(35, xs.Length);
        // heads 20..24 are g'32 a' b' c'' d''8 of bar 3 (octave absolute: C5..D6 — a'' and above
        // carry ledger lines). MEASURED, LilyPond 2.26.0 (scratch/p380/incr/cols.ps1). The svg
        // writes X to two decimals, so a gap between two written positions is good to ±0.01.
        const double tolerance = 0.011;
        Assert.InRange(xs[21] - xs[20], 1.80 - tolerance, 1.80 + tolerance);     // g'' → a'': g'' has no ledger, no rod
        Assert.InRange(xs[22] - xs[21], 1.9563 - tolerance, 1.9563 + tolerance); // a'' → b''
        Assert.InRange(xs[23] - xs[22], 1.9563 - tolerance, 1.9563 + tolerance); // b'' → c'''
        Assert.InRange(xs[24] - xs[23], 1.9563 - tolerance, 1.9563 + tolerance); // c''' → d'''
    }
}
