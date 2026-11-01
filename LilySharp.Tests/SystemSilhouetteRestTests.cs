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
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A rest another voice pushed out of the staff is in the system's silhouette WHERE IT WAS PUSHED
/// TO, so the page spaces the next system clear of it. The staff's own profile read the push and the
/// system's did not (owner's report 2026-11-01, Cafe-no-Midori: a three-voice guitar's rest under the
/// first system stood on a stem of the second).
/// </summary>
[Trait("Category", "Unit")]
public class SystemSilhouetteRestTests
{
    // The first section of the owner's piece, bar for bar.
    private const string Book = """
        octave relative
        tempo 4 = 128
        time 4/4
        key ees major

        part gtr "Guitar" "Gtr." { instrument guitar midiInstrument "acoustic guitar (nylon)" }

        section A {
          partial 8
          gtr {
            voice {
              bes'8@p~ |
              bes4 g8 f8 ees4 g8 f8 |
              ees2 d4 c4 |
              f4 aes8 g8 f2 |
              d2 c4 aes'4 |
              bes4 g8 f8 ees4 g8 bes8 |
              c2 bes4 g4 |
              aes4 c8 bes8 aes2 |
              d2 c4 bes4 |
              bes4 g8 f8 ees4 g8 f8 |
              ees2 g4 ees4 |
              f4 aes8 g8 f4 c'4 |
              d2 c4 aes4 |
            } {
              r8 |
              <g d>4. q4. q4 |
              r4 <ees bes>4 r8 q4. |
              <aes ees>4. q4. q4 |
              r4 <d, aes>4 r8 q4. |
              <g d>4. q4. q4 |
              r4 <ees bes>4 r8 q4. |
              <aes ees>4. q4. q4 |
              r4 <d, aes>4 r8 q4. |
              <g d>4. q4. q4 |
              r4 <ees bes>4 r8 q4. |
              <aes ees>4. q4. q4 |
              r4 <d, aes>4 r8 q4. |
            } {
              r8 |
              ees4 r4 bes2 |
              c4 r4 c2 |
              f4 r4 c2 |
              bes4 r4 bes2 |
              ees4 r4 bes2 |
              c4 r4 c2 |
              f4 r4 c2 |
              bes4 r4 bes2 |
              ees4 r4 bes2 |
              c4 r4 c2 |
              f4 r4 c2 |
              bes4 r4 bes2 |
            }
          }
        }

        form { A }
        score { staff gtr }
        """;

    private static double Number(Match m, string group)
        => double.Parse(m.Groups[group].Value, CultureInfo.InvariantCulture);

    [Fact]
    public void ARestPushedOutOfTheStaff_KeepsTheNextSystemClearOfIt()
    {
        string book = Book.ReplaceLineEndings("\n");
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(book), SvgRenderOptions.Default);

        // Every rest pushed below its own staff, and every stem of the staff under it, as drawn.
        var staffTops = Regex.Matches(svg, "<line x1=\"[\\d.]+\" y1=\"(?<y>[\\d.]+)\" x2=\"[\\d.]+\" y2=\"\\k<y>\"[^>]*stroke-width=\"0\\.100\"")
            .Select(m => Number(m, "y")).Distinct().Order().Chunk(5).Select(lines => (Top: lines[0], Bottom: lines[^1])).ToList();
        Assert.True(staffTops.Count >= 2, "the piece takes two systems or more");

        var notes = Regex.Matches(svg, "<text class=\"music\" x=\"(?<x>[\\d.]+)\" y=\"(?<y>[\\d.]+)\" font-size=\"4\\.00\" data-pos=\"(?<pos>\\d+)\"")
            .Select(m => (X: Number(m, "x"), Y: Number(m, "y"), Pos: int.Parse(m.Groups["pos"].Value))).ToList();
        var rests = notes.Where(n => book[n.Pos] == 'r').ToList();
        var stems = Regex.Matches(svg, "<line x1=\"(?<x>[\\d.]+)\" y1=\"(?<a>[\\d.]+)\" x2=\"\\k<x>\" y2=\"(?<b>[\\d.]+)\"[^>]*stroke-width=\"0\\.130\"")
            .Select(m => (X: Number(m, "x"), Top: Math.Min(Number(m, "a"), Number(m, "b")), Bottom: Math.Max(Number(m, "a"), Number(m, "b")))).ToList();

        int pushedOut = 0;
        for (int i = 0; i + 1 < staffTops.Count; i++)
        {
            double bottom = staffTops[i].Bottom;
            double nextTop = staffTops[i + 1].Top;
            // The rests hanging under this staff, the stems standing over the next.
            foreach (var rest in rests.Where(r => r.Y > bottom + 1 && r.Y < nextTop))
            {
                pushedOut++;
                const double RestHalfHeight = 1.5, RestWidth = 1.1;
                foreach (var stem in stems.Where(s => s.Top < nextTop && s.Bottom > bottom))
                    Assert.False(stem.X > rest.X - 0.1 && stem.X < rest.X + RestWidth + 0.1
                            && stem.Top < rest.Y + RestHalfHeight && stem.Bottom > rest.Y - RestHalfHeight,
                        $"the rest at ({rest.X}, {rest.Y}) under staff {i + 1} stands on the stem at x {stem.X}, y {stem.Top} to {stem.Bottom}");
            }
        }
        Assert.True(pushedOut > 0, "the piece's rests are pushed under the staff");
    }
}
