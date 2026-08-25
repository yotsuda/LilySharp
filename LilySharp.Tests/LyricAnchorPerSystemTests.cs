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
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A lyric block hangs off its own staff, and how far that staff sits below its SYSTEM's
/// origin is a fact about the system — not about the score. What stands above the staff can
/// differ from one system to the next, and it takes no hara-kiri to differ: a chords row
/// printed on one system and absent on another moves the staff under it by the row's band.
/// </summary>
/// <remarks>
/// <para>
/// LILYPOND-REF: lily/page-layout-problem.cc:896-914 <c>find_system_offsets</c> — a staff is
/// translated by THAT system's solution entry, and :1046-1053 translates the loose lines
/// below it in the same frame.
/// </para>
/// <para>
/// ⚠️ THE DEFECT WAS DECLARED IN A COMMENT AND LEFT, and the comment said why: the anchor
/// <c>LayoutEngine.BuildStaffAnchorTables</c> publishes is read off <c>systemsArray[0]</c>,
/// and its own remark called that a simplification reached only "where hara-kiri leaves
/// different staves alive on different systems AND the block hangs from a non-last group; no
/// fixture and no ledger point reaches that". A chords row reaches it without hara-kiri at
/// all. MEASURED on the reported book (scratch/ベースタブLy/Untitled-6.lys with
/// <c>lyrics verse sings melody</c>, user report 2026-08-25): the chain solved
/// 0.000000 / 5.175000 / 7.975000 / 12.033515 and the syllables were DRAWN 1.895000 below
/// every one of those, so the last verse landed 0.160000 above the next staff's top line and
/// its descenders crossed the staff. 1.895000 is the chords row's band, which system 0 has
/// and system 1 has not.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class LyricAnchorPerSystemTests
{
    // Two systems of IDENTICAL music and IDENTICAL syllables, so the block's own solve is the
    // same on both and any difference between them is the frame. The chords row is declared
    // for section A alone, which is what makes the two systems' staff offsets differ.
    private const string Source = """
        key c major
        part melody {
          clef treble
          section A { c4 c g' g | a a g2 | break}
          section B { c4 c g' g | a a g2 | }
        }
        chords prog { section A { C | Am | } }
        lyrics verse {
          section A { Twin- kle twin- kle | lit- tle star | }
          section B { Twin- kle twin- kle | lit- tle star | }
        }
        form main { A B }
        score main {
          chords prog as names
          staff melody
          lyrics verse sings melody
          staff melody
        }
        """;

    /// <summary>
    /// The syllables sit the same distance below their own staff on both systems, and that is
    /// the whole claim: same music, same syllables, same run, so the only thing that could
    /// separate them is which system's geometry the block was placed in.
    /// </summary>
    [Fact]
    public void TheSameBlockOnTwoSystems_SitsTheSameDistanceBelowItsOwnStaff()
    {
        string svg = Render();
        var staves = StaffLineGeometry.Staves(svg);
        Assert.Equal(4, staves.Count);   // two systems of two staves

        double first = Below(svg, staves[0], staves[1]);
        double second = Below(svg, staves[2], staves[3]);
        Assert.Equal(first, second, 6);
    }

    /// <summary>
    /// …and neither of them is drawn through the staff below it. This is the arm the reader
    /// cares about: the frame error was worth 1.895000 and it spent all of it on the closing
    /// gap, which is where a reader sees it.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE FLOOR IS THE SPEC'S OWN PADDING, NOT A MEASURED LILY# NUMBER: LilyPond declares
    /// <c>nonstaff-unrelatedstaff-spacing.padding = 1.5</c> for a Lyrics context
    /// (ly/engraver-init.ly:695), so a solved block clears the staff below it by at least
    /// that much minus the half-staff the reading is taken over — asserted as "positive and
    /// not vanishing" rather than as a literal, because the syllable's own descent is a font
    /// quantity and this is the sum of the two.
    /// </remarks>
    [Fact]
    public void NeitherSystemsBlock_IsDrawnThroughTheStaffBelowIt()
    {
        string svg = Render();
        var staves = StaffLineGeometry.Staves(svg);
        foreach (var (upper, lower) in new[] { (staves[0], staves[1]), (staves[2], staves[3]) })
        {
            var lines = StaffLineGeometry.Baselines(svg, upper.Bottom, lower.Top);
            double clearance = lower.Top - lines[^1];
            Assert.True(clearance > 1.0,
                $"the last syllable's baseline is {clearance:F6} above the staff below it; "
                + "its descenders are in the staff");
        }
    }

    private static double Below(string svg, (double Top, double Bottom) upper,
        (double Top, double Bottom) lower)
    {
        var lines = StaffLineGeometry.Baselines(svg, upper.Bottom, lower.Top);
        Assert.NotEmpty(lines);
        return lines[0] - upper.Bottom;
    }

    private static string Render()
    {
        var tree = SyntaxTree.Parse(Source);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        return SvgGenerator.Generate(tree, new SvgRenderOptions { EmbedFont = false });
    }
}
