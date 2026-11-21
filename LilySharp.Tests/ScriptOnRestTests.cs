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
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A script written on a rest centres on the rest and clears it where Rest_collision left it
/// (ArticulationEngraver: NoteheadHalfWidth's rest arm, and the fold of the script's own rest).
/// </summary>
public class ScriptOnRestTests
{
    private static string Book(string treble) => $$"""
        octave absolute
        time 4/4
        paper { raggedRight }
        part rh { clef treble }
        part lh { clef bass }
        section A { rh { {{treble}} } lh { c4 c4 c2 | } }
        form { A }
        score { grandStaff { staff rh staff lh } }
        """;

    /// <summary>
    /// The script's centre X less the rest's left X, and its Y in spaces above the treble's
    /// middle line. Every pin is LilyPond 2.26.0's print of the twin (Lab sessions/p1352/marks
    /// k1, k3, k4, k5): a fermata under a quarter rest the upper voice pushed down to −2 stands
    /// 0.42 under the rest's ink, over one pushed up to +6 0.41 over it, an accent under an
    /// eighth rest at its stem's foot, a fermata under a half rest off the staff. Until session
    /// 1352 each centred 0.65 right of the rest (a black head's half) and the k1 fermata stood
    /// at −3.22, across the rest's ink.
    /// LILYPOND-REF: scm/output-lib.scm:1883-1927 script-interface::calc-x-offset;
    ///   lily/script-engraver.cc:234-250 acknowledge_rhythmic_head.
    /// </summary>
    [Theory]
    [InlineData("voice { e''2 e''2 } { s4 r4@fermata.down s2 } |", 0.475, -3.75)]
    [InlineData("voice { s4 r4@fermata s2 } { a''2 a''2 } |", 0.475, 8.05)]
    [InlineData("voice { e''2 e''2 } { s4 r8@accent.down s8 s2 } |", 0.5, -3.55)]
    [InlineData("voice { e''2 e''2 } { s4 r2@fermata.down s4 } |", 0.75, -2.59)]
    public void AScriptOnARest_CentresOnTheRest_AndClearsItWhereItWasPushed(
        string treble, double dx, double up)
    {
        var svg = TestPaper.SvgFromRenderSpec(Book(treble));
        var lines = Regex.Matches(svg,
                "<line x1=\"[-\\d.]+\" y1=\"([-\\d.]+)\" x2=\"[-\\d.]+\" y2=\"\\1\" stroke=\"#000000\" stroke-width=\"0\\.100\"")
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .Distinct().OrderBy(y => y).ToList();
        Assert.Equal(10, lines.Count);
        var glyphs = Regex.Matches(svg, "<text class=\"music\" x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"[^>]*>(.)</text>")
            .Select(m => (X: double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                Up: Math.Round(lines[2] - double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), 2),
                IsRest: (int)m.Groups[3].Value[0] is >= 0xE000 and <= 0xE00F))
            .ToList();
        double restX = Assert.Single(glyphs, g => g.IsRest).X;
        var script = Assert.Single(glyphs, g => !g.IsRest && Math.Abs(g.X - restX - dx) < 0.006);
        Assert.Equal(up, script.Up);
    }
}
