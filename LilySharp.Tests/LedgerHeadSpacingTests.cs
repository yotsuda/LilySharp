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
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A note head outside the staff carries its SPACING box to the first ledger line — LilyPond's
/// <c>NoteHead.extra-spacing-height = ly:note-head::include-ledger-line-height</c>
/// (lily/note-head.cc:124-148, "only the interval between the note and the first ledger line").
/// The band matters when the column before it hangs an up-stem flag: inside the staff the
/// flag's own band already meets a rising neighbour and misses a falling one; on ledger lines
/// the falling neighbour's box climbs to where the flag hangs, and LilyPond prices every pair
/// the same. Measured on 2.26.0 (scratch/p359/lp/pair.ps1, 2026-09-09; the bar lines of a
/// ragged line, staff spaces from the system's left edge):
/// <list type="bullet">
/// <item>flag-low.ly — <c>time none g8 a b c d c b a | g4 f e2 |</c> below the staff, after a
/// 4/4 bar: bars 20.357 / 41.986 / 53.499 / 64.199 (the cadenza bar 21.629; Lily# drew 20.24
/// before this, the three falling pairs 2.11 apart where LilyPond has 2.567).</item>
/// <item>flag-high.ly — the same figure an octave up, inside the staff: 20.357 / 39.67 /
/// 51.272 / 61.972 (exact before and after).</item>
/// <item>ledger-beamed.ly — beamed eighths on ledger lines, falling and rising: 28.761 /
/// 50.064 / 71.624 / 92.515 (exact before and after: the duration space prices them).</item>
/// </list>
/// </summary>
[Trait("Category", "Unit")]
public class LedgerHeadSpacingTests
{
    private const string Header = "paper { raggedRight }\ntime 4/4\nkey c major\npart melody { clef treble }\n";

    private static List<double> BarXs(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        string svg = SvgGenerator.Generate(tree, new SvgRenderOptions { EmbedFont = false });
        var rows = Regex.Matches(svg, "<rect x=\"([0-9.-]+)\" y=\"([0-9.-]+)\" width=\"([0-9.-]+)\" height=\"([0-9.-]+)\"")
            .Select(m => (X: double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                          W: double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
                          H: double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture)))
            .Where(r => r.W < 0.5 && r.H > 3)
            .Select(r => r.X).Distinct().OrderBy(x => x).ToList();
        return rows;
    }

    /// <summary>The bar lines up to the LAST one: a cadenza's return (`time 4/4 c1 |`) prices the
    /// whole note after the reprinted meter 1.0 wider than LilyPond does — a separate, open
    /// island (HANDOFF §1 session 358 ⑸) — so the closing bar is not pinned here.</summary>
    private static void AssertBars(string source, params double[] lilypond)
    {
        var bars = BarXs(source);
        Assert.Equal(lilypond.Length, bars.Count);
        for (int i = 0; i + 1 < lilypond.Length; i++)
            Assert.InRange(bars[i], lilypond[i] - 0.05, lilypond[i] + 0.05);
    }

    /// <summary>
    /// The cadenza bar: 21.629 in LilyPond. Lily# stood at 20.24 before the ports, 21.45 with
    /// the ledger reach and the intrinsic 0.15 padding, and 21.63 once the flag's box hung
    /// from the stem's real end half a blot inside it (ItemSkylineFactory.FlagInkBand) — the
    /// last 0.18 was two pairs whose flag foot stopped 0.105 short of the next head's roof.
    /// </summary>
    [Fact]
    public void FlaggedEighthsBelowTheStaff_AreHeldApartLikeLilyPonds()
        => AssertBars(Header
            + "section A { melody { c'4 d e f | } }\n"
            + "section B { time none  melody { g8 a b c d c b a | g4 f e2 | } }\n"
            + "section C { melody { c1 | } }\n"
            + "form main { A B C }\nscore main { staff melody }\n",
            20.357, 41.986, 53.499, 64.199);

    /// <summary>
    /// flag-metered.ly — flagged eighths in 4/4, each followed by a lower (bars 1–2) or higher
    /// (bars 3–6) note, in and below the staff: 22.469 / 37.506 / 52.543 / 67.767 / 82.741 /
    /// 97.812. Bar 1 (`c'4. b8 a4. g8`) read 22.47 → 37.44 before the flag band was the glyph's
    /// (−0.07) and 37.51 after; the rest were exact throughout.
    /// </summary>
    [Fact]
    public void FlaggedEighthsInMeteredBars_AreExact()
        => AssertBars(Header
            + "section Main { melody { c'4. b8 a4. g8 | f4. e8 d4. c8 | c4. d8 e4. f8 | g4. a8 b4. c'8 | g,4. a8 b4. c8 | d4. e8 f4. g8 | } }\n"
            + "form main { Main }\nscore main { staff melody }\n",
            22.469, 37.506, 52.543, 67.767, 82.741, 97.812);

    [Fact]
    public void TheSameFigureInsideTheStaff_IsUnchanged()
        => AssertBars(Header
            + "section A { melody { c'4 d e f | } }\n"
            + "section B { time none  melody { g'8 a b c d c b a | g4 f e2 | } }\n"
            + "section C { melody { c1 | } }\n"
            + "form main { A B C }\nscore main { staff melody }\n",
            20.357, 39.67, 51.272, 61.972);

    [Fact]
    public void BeamedEighthsOnLedgerLines_AreUnchanged()
        => AssertBars(Header
            + "section Main { melody { c8 b a g f e d c | c8 d e f g a b c | f'8 e d c b a g f | f8 g a b c d e f | } }\n"
            + "form main { Main }\nscore main { staff melody }\n",
            28.761, 50.064, 71.624, 92.515);
}
