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
/// rest-pitched-beam.ly: a rest written at a pitch is not moved by the beam over it.
/// LilyPond's callback answers with the chained offset the moment it finds a numeric
/// <c>staff-position</c> — before it has read the beam at all — so the push that moves
/// an ordinary rest out from under a beam never happens to a pitched one.
/// LILYPOND-REF: lily/beam.cc:1336-1338 Beam::rest_collision_callback.
/// </summary>
/// <remarks>
/// The book was SKIPPED in the corpus until this spelling existed: with plain rests it
/// re-tests the beamed-rest machinery instead of its own claim, since the claim is
/// entirely about the rests being pitched (audit/lp-regression/status.json).
/// <para>
/// ⚠️ AND THE BOOK'S OWN MUSIC DOES NOT REACH THE PUSH — measured on LilyPond, not
/// assumed: in <c>a8[ a\rest b]</c> the beam is far enough above that an UNPITCHED rest
/// in that slot is not moved either (LilyPond leaves both on the middle line). So the
/// first test below pins the book, and the second is what actually holds the branch
/// down: the same slot in music where the beam does push, with a plain rest beside the
/// pitched one to show that it does.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class RestPitchedBeamTests
{
    /// <summary>Every rest in the SVG as (glyph, staff position, x), left to right.</summary>
    private static List<(string Glyph, double Position, double X)> Rests(string svg)
    {
        var lineYs = Regex.Matches(svg,
                "<line x1=\"0\\.00\" y1=\"([-\\d.]+)\" x2=\"[-\\d.]+\" y2=\"\\1\"")
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .OrderBy(y => y).ToList();
        Assert.Equal(5, lineYs.Count);
        double middleY = lineYs[2];

        var rests = new List<(string, double, double)>();
        foreach (Match m in Regex.Matches(svg,
            "<text class=\"music\" x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"[^>]*>(.)</text>"))
        {
            string cp = ((int)m.Groups[3].Value[0]).ToString("X4");
            if (cp is not ("E008" or "E00B"))
                continue;
            double x = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            double y = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            rests.Add((cp, Math.Round(-2 * (y - middleY), 2), Math.Round(x, 2)));
        }
        return rests.OrderBy(r => r.Item3).ToList();
    }

    /// <summary>
    /// The corpus book itself (audit/lp-regression/lys/rest-pitched-beam.lys), with the
    /// measure filled out. Every number is LilyPond 2.26.0's print for the twin
    /// <c>lysc ly</c> writes from this source.
    /// </summary>
    [Fact]
    public void TheBook_PutsBothPitchedRestsAtTheWrittenPitch()
    {
        var rests = Rests(LiveRender.SvgFromRenderSpec("""
            octave absolute
            time 4/4
            part v { }
            section Main {
              v { a4@rest a8[ a8@rest b8] r4 r8 | }
            }
            form main { ~Main }
            score main { staff ~v }
            """));

        // LilyPond: staff-position −1 for both pitched rests (Xrel 8.585 and 14.4392),
        // and its middle line for the two that fill the measure (19.1434, 22.4934).
        Assert.Equal(
            new[]
            {
                ("E008", -1.0, 8.59),
                ("E00B", -1.0, 14.44),
                ("E008", 0.0, 19.14),
                ("E00B", 0.0, 22.49),
            },
            rests.Select(r => (r.Glyph, r.Position, r.X)));
    }

    /// <summary>
    /// The branch itself: two beams that DO push a rest — one with the beam below the
    /// heads, one above — each carrying a plain rest and a pitched rest in the same
    /// slot. The plain ones move; the pitched ones do not.
    /// </summary>
    /// <remarks>
    /// All four are LilyPond 2.26.0's own print for the twin of this very source
    /// (`lysc ly`, then the Rest dump): plain +2 under the lower beam, plain −2 under
    /// the upper one, and both pitched rests at −1, which is where the written `a` is.
    /// <para>⚠️ Removing either half of the guard — it has to be set on the automatic
    /// beam path AND the manual one, which is what this music uses — puts the pitched
    /// rests at +3 and −1 instead. That second spelling is why this test is written
    /// against music where the push is real: the corpus book cannot see it.</para>
    /// </remarks>
    [Fact]
    public void WhereABeamDoesPushARest_ThePitchedOneIsStillNotPushed()
    {
        var rests = Rests(LiveRender.SvgFromRenderSpec("""
            octave absolute
            time 4/4
            part v { }
            section Main {
              v {
                g'8[ r8 g'8] r8 g'8[ a8@rest g'8] r8 |
                d8[ r8 d8] r8 d8[ a8@rest d8] r8 |
              }
            }
            form main { ~Main }
            score main { staff ~v }
            """));

        var positions = rests.Select(r => r.Position).ToArray();
        Assert.Equal(new[] { 2.0, 0.0, -1.0, 0.0, -2.0, 0.0, -1.0, 0.0 }, positions);
    }
}
