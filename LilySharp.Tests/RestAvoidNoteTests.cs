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
/// twin (audit\lpreg\restavoid, digit-for-digit).
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
            if (cp is "E001" or "E003" or "E008" or "E00B")
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
        // ⚠️ That last one is E003 (rests.1o), not E001: it is the only rest here
        // that misses every staff line, so it prints the cut of the glyph with a
        // ledger line through it. LilyPond's own page for this twin does the same
        // (measured: `expr=(named-glyph … rests.1o)` for that grob and rests.1 for
        // the other three half rests).
        // LILYPOND-REF: lily/rest.cc:166-185 Rest::glyph_name.
        var expected = new[]
        {
            ("E008", 3.0), ("E00B", -3.0),
            ("E001", 2.0), ("E001", -2.0), ("E001", -2.0), ("E003", 5.5),
        };
        Assert.Equal(
            expected.OrderBy(e => e.Item1).ThenBy(e => e.Item2),
            rests.OrderBy(r => r.Glyph).ThenBy(r => r.Y).Select(r => (r.Glyph, r.Y)));
    }

    /// <summary>
    /// rest-avoid-note.ly as its author wrote it: two of its four rests are PITCHED
    /// (<c>a4\rest</c>, <c>f2\rest</c>), which is how a writer overrules the collision.
    /// Every pin is LilyPond 2.26.0's own print for that file — measured, not derived.
    /// LILYPOND-REF: lily/rest-engraver.cc:62-80 process_music;
    /// LILYPOND-REF: lily/rest.cc:53-74 staff_position_internal — position_override;
    /// LILYPOND-REF: lily/rest-collision.cc:228-233 calc_positioning_done.
    /// </summary>
    /// <remarks>
    /// The pitched pair is what makes this the same music as the plain-rest version
    /// above and a different page, in exactly two places. The quarter rest goes to −8,
    /// where a written <c>a</c> sits, instead of the −6 the collision gives it; and the
    /// third voice's half rest goes to +4 — a staff line, so it prints the BARE glyph —
    /// while the SECOND voice's, still unpitched, is still pushed to −11 and still
    /// prints the ledgered one. Two half rests in one measure, told apart by the glyph.
    /// <para>⚠️ LilyPond warns "too many colliding rests" TWICE on the all-plain twin
    /// and NOT AT ALL on this one (measured on both). That is the other half of what
    /// writing the pitch buys: a pre-positioned rest is not counted.</para>
    /// </remarks>
    [Fact]
    public void PitchedRests_TakeTheWrittenPitchAndNoCollisionShift()
    {
        var svg = LiveRender.SvgFromRenderSpec("""
            octave absolute
            time 4/4
            part v { }
            section Main {
              v {
                voice
                { g'8 g' g' r8 r2 | }
                { a,4@rest c r2 | }
                { c'4 c' f'2@rest | }
                { r2 g | }
              }
            }
            form main { ~Main }
            score main { staff ~v }
            """);

        var lineYs = Regex.Matches(svg,
                "<line x1=\"0\\.00\" y1=\"([-\\d.]+)\" x2=\"[-\\d.]+\" y2=\"\\1\"")
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .OrderBy(y => y).ToList();
        Assert.Equal(5, lineYs.Count);
        double middleY = lineYs[2];

        var rests = new List<(string Glyph, double Y)>();
        foreach (Match m in Regex.Matches(svg,
            "<text class=\"music\" x=\"[-\\d.]+\" y=\"([-\\d.]+)\"[^>]*>(.)</text>"))
        {
            string cp = ((int)m.Groups[2].Value[0]).ToString("X4");
            if (cp is "E001" or "E003" or "E008" or "E00B")
                rests.Add((cp, Math.Round(
                    double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) - middleY, 2)));
        }

        // What LilyPond prints for input/regression/rest-avoid-note.ly itself:
        //   rests.2  at staff position −8   the pitched a4\rest, at its own pitch
        //   rests.3  +6, rests.1 −4, +4     the unpitched ones, unchanged
        //   rests.1o −11                    still pushed out, still ledgered
        //   rests.1  +4                     the pitched f2\rest, on a line
        // Down-positive here, so the signs flip and a position is half a space.
        var expected = new[]
        {
            ("E008", 4.0), ("E00B", -3.0),
            ("E001", 2.0), ("E001", -2.0), ("E001", -2.0), ("E003", 5.5),
        };
        Assert.Equal(
            expected.OrderBy(e => e.Item1).ThenBy(e => e.Item2),
            rests.OrderBy(r => r.Glyph).ThenBy(r => r.Y).Select(r => (r.Glyph, r.Y)));
    }
}
