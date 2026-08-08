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
/// rest-avoid-note.ly: rests avoid notes, each moved in the direction of the
/// stems in its voice — the voiced starting position plus the collision
/// translate — and rests of same-direction voices may overlap each other.
/// Every pin below is LilyPond's own printed position from the paper-aligned
/// twin (scratch\lpreg\restavoid, digit-for-digit).
/// LILYPOND-REF: lily/rest.cc:46-141 staff_position_internal;
/// LILYPOND-REF: lily/rest-collision.cc:211-290 calc_positioning_done.
/// </summary>
[Trait("Category", "Unit")]
public class RestAvoidNoteTests
{
    [Fact]
    public void FourVoiceRests_TakeVoicedPositionsAndCollisionShifts()
    {
        var svg = LiveRender.SvgFromRenderSpec("""
            octave absolute
            time 4/4
            part v { }
            section Main {
              v {
                voice
                { g'8 g' g' r8 r2 | }
                { r4 c r2 | }
                { c'4 c' r2 | }
                { r2 g | }
              }
            }
            form main { ~Main }
            score main { staff ~v }
            """);

        // The staff middle line: the 3rd of the five full-width staff lines.
        var lineYs = Regex.Matches(svg,
                "<line x1=\"0\\.00\" y1=\"([-\\d.]+)\" x2=\"[-\\d.]+\" y2=\"\\1\"")
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .OrderBy(y => y).ToList();
        Assert.Equal(5, lineYs.Count);
        double middleY = lineYs[2];

        // Every rest glyph, as (codepoint, staff-space offset below the middle).
        // The glyphs are literal private-use characters in the SVG text.
        var rests = new List<(string Glyph, double Y)>();
        foreach (Match m in Regex.Matches(svg,
            "<text class=\"music\" x=\"[-\\d.]+\" y=\"([-\\d.]+)\"[^>]*>(.)</text>"))
        {
            string cp = ((int)m.Groups[2].Value[0]).ToString("X4");
            if (cp is "E001" or "E008" or "E00B")
                rests.Add((cp, Math.Round(
                    double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) - middleY, 2)));
        }

        // LP prints (staff-relative, down-positive): the lower voices' r4 at +3.0
        // and r2 at +2.0 under the moment's notes; the up voice's r8 pushed to
        // −3.0 by the HELD notes of the middle voices (head-only, the
        // already-happened arm); the two up voices' half rests OVERLAPPING at
        // −2.0 (same direction — the claim allows it); and the second down
        // voice's r2 at +5.5 — an odd position, legal outside the staff where
        // the collision moves by half spaces.
        var expected = new[]
        {
            ("E008", 3.0), ("E00B", -3.0),
            ("E001", 2.0), ("E001", -2.0), ("E001", -2.0), ("E001", 5.5),
        };
        Assert.Equal(
            expected.OrderBy(e => e.Item1).ThenBy(e => e.Item2),
            rests.OrderBy(r => r.Glyph).ThenBy(r => r.Y).Select(r => (r.Glyph, r.Y)));
    }
}
