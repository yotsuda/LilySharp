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
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A whole rest and a half rest on a REDUCED staff seat themselves on the lines that
/// staff actually draws: the whole hangs from the first line above the middle (THE line
/// on <c>as lines 1</c>), the half sits on the last line at or below the middle (the
/// LOWER line of the <c>as lines 2</c> pair). The owner's report of 2026-09-23
/// (corpora/ベースタブLy/oneline-rest.lys): the whole rest floated a space above the single
/// line, the half rest a space above the pair's lower line — every reader of the rest's
/// resting place assumed five lines. Three, four and five lines were right because the
/// five-line letter happens to land on a drawn line there.
/// LILYPOND-REF: lily/rest.cc:76-133 staff_position_internal — the neutral direction seats
///   a semibreve on <c>upper_bound</c> of line-positions (else the last line), anything
///   longer than a quarter on the line before that (else the first);
/// LILYPOND-REF: lily/multi-measure-rest.cc:241-292 church_rest — a whole-bar rest on a
///   staff with fewer than two lines is lowered onto the line.
/// </summary>
[Trait("Category", "Unit")]
public class ReducedStaffRestTests
{
    private const string Music = """
        part melody {
          section A { r1 | r2 r4 r8 | }
        }
        form main { A }
        """;

    [Theory]
    // (lines, the line the WHOLE rest hangs from, the line the HALF rest sits on) — as
    // indices into the drawn lines, top first. The whole-bar r1 goes through the church
    // rest; the r2 through the ordinary rest draw. Both must land on a drawn line.
    [InlineData(1, 0, 0)]   // one line: both on THE line
    [InlineData(2, 0, 1)]   // the timbales pair: whole from the upper, half on the lower
    [InlineData(3, 0, 1)]   // three lines (2, 0, −2): whole from +2, half on 0
    [InlineData(4, 1, 2)]   // four lines (4, 2, 0, −2): whole from +2, half on 0
    [InlineData(5, 1, 2)]   // five lines: the classic fourth-line hang, middle-line seat
    public void WholeAndHalfRests_SeatOnTheLinesTheStaffDraws(int lines, int wholeLine, int halfLine)
    {
        var svg = TestPaper.SvgFromRenderSpec(Music + $"\nscore main {{ staff melody as lines {lines} }}\n");

        // The drawn staff lines, top first (SVG y grows downward).
        var lineYs = Regex.Matches(svg,
                "<line x1=\"0\\.05\" y1=\"([-\\d.]+)\" x2=\"[-\\d.]+\" y2=\"\\1\"")
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .OrderBy(y => y).ToList();
        Assert.Equal(lines, lineYs.Count);

        // Every rest glyph's origin Y, by codepoint. The bare cuts, never the ledgered
        // ones: a rest seated on a drawn line carries no ledger.
        var rests = Regex.Matches(svg,
                "<text class=\"music\" x=\"[-\\d.]+\" y=\"([-\\d.]+)\"[^>]*>(.)</text>")
            .Select(m => (Glyph: m.Groups[2].Value[0],
                          Y: double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)))
            .ToList();
        var whole = Assert.Single(rests, r => r.Glyph == EmmentalerGlyphs.RestWhole);
        var half = Assert.Single(rests, r => r.Glyph == EmmentalerGlyphs.RestHalf);
        Assert.DoesNotContain(rests, r => r.Glyph is EmmentalerGlyphs.RestWholeLedgered
                                          or EmmentalerGlyphs.RestHalfLedgered);

        // A whole rest's origin IS the line it hangs from; a half rest's the line it sits on.
        Assert.Equal(lineYs[wholeLine], whole.Y, 2);
        Assert.Equal(lineYs[halfLine], half.Y, 2);
    }

    /// <summary>
    /// The neutral letter itself, over the lines each staff draws
    /// (<see cref="EngravingDefaults.StaffLinePositions"/>), digit for digit from
    /// <c>staff_position_internal</c> at CENTER: on five lines the old "+2 whole, 0
    /// otherwise"; the one-line breve two below the line (rest.cc:96-97).
    /// </summary>
    [Theory]
    [InlineData(1, 1, 0.0)] [InlineData(1, 2, 0.0)] [InlineData(1, 0, -2.0)] [InlineData(1, 4, 0.0)]
    [InlineData(2, 1, 2.0)] [InlineData(2, 2, -2.0)] [InlineData(2, 0, -2.0)] [InlineData(2, 8, 0.0)]
    [InlineData(3, 1, 2.0)] [InlineData(3, 2, 0.0)]
    [InlineData(4, 1, 2.0)] [InlineData(4, 2, 0.0)]
    [InlineData(5, 1, 2.0)] [InlineData(5, 2, 0.0)] [InlineData(5, 0, 0.0)] [InlineData(5, 16, 0.0)]
    public void NeutralRestPosition_FollowsTheDrawnLines(int lines, int restValue, double expected)
        => Assert.Equal(expected, ElementCoordinator.NeutralRestPosition(lines, restValue));

    /// <summary>
    /// The voiced arm at direction CENTER is the neutral letter (rest.cc:131-133), and on
    /// five lines the voiced positions are the ones rest-avoid-note.ly pins (±4 for a
    /// half, +4 / −4 for a whole).
    /// </summary>
    [Theory]
    [InlineData(5, 1, 1, 4.0)] [InlineData(5, 1, -1, -4.0)]
    [InlineData(5, 2, 1, 4.0)] [InlineData(5, 2, -1, -4.0)]
    [InlineData(1, 1, 1, 4.0)] [InlineData(1, 1, -1, -4.0)]
    [InlineData(2, 2, 1, 2.0)] [InlineData(2, 2, -1, -6.0)]
    [InlineData(2, 1, 1, 6.0)] [InlineData(2, 1, -1, -2.0)]
    public void VoicedRestPosition_SeatsOnTheDrawnLines(int lines, int restValue, int dir, double expected)
    {
        Assert.Equal(expected, ElementCoordinator.VoicedRestPosition(dir, restValue, lines));
        Assert.Equal(ElementCoordinator.NeutralRestPosition(lines, restValue),
                     ElementCoordinator.VoicedRestPosition(0, restValue, lines));
    }
}
