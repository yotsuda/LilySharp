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

using System;
using System.Linq;
using LilySharp.Core.Rendering;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The collisions four more large scores found on 2026-11-13 (grammar evaluation 4; Lab
/// sessions/p1226/md/eval4/fixB2). As in <see cref="EngravingCollisions3Tests"/>, each test
/// holds two pieces of drawn ink apart — never a coordinate.
/// </summary>
[Trait("Category", "Unit")]
public sealed class EngravingCollisions4Tests
{
    // Device Y grows DOWN on the recorded page, so "below" is the larger Y.

    private static double[] StaffLineYs(RenderedGeometry g)
    {
        // The staff lines are the longest horizontal rules of the page.
        var horizontal = g.Lines.Where(l => Math.Abs(l.Y1 - l.Y2) < 1e-9 && !l.IsDashed).ToArray();
        double longest = horizontal.Max(l => l.X2 - l.X1);
        return [.. horizontal.Where(l => l.X2 - l.X1 > longest - 0.5).Select(l => Math.Round(l.Y1, 6)).Distinct().OrderBy(y => y)];
    }

    /// <summary>
    /// rock/r-08 — <c>score { staff v  chords h  staff g  tab g }</c>: the guitar's let ring,
    /// barre and palm-mute lines were drawn INSIDE the staff above. A <c>chords</c> row between
    /// two staves hangs on the staff under it, and its symbols were seeded into that staff's
    /// outside-staff support as if they were inline <c>@chord</c> names
    /// (<c>OutsideStaffStacker</c>), so every mover of the staff — these lines, a
    /// <c>@rit</c>, a text, a fermata — climbed over the row while the room reserved for them
    /// under the row stood empty. And two lines over the same notes were reserved as one
    /// (<c>TextSpannerEngraver.InkAboveStaff</c>), so even a row at the top of the score was
    /// printed through the upper one. The lines stand between their own staff and the row; the
    /// row and the staff distance make room.
    /// </summary>
    [Theory]
    [InlineData("staff v  chords h  staff g  tab g", true)]
    [InlineData("staff v  chords h  staff g", true)]
    [InlineData("chords h  staff g  tab g", false)]
    public void AStaffsGuitarLines_StandBetweenItAndTheChordsRowOverIt(string score, bool staffAbove)
    {
        var g = RenderedGeometry.Render("""
            octave absolute
            part v { clef treble }
            part g { instrument guitar }
            section A {
              v { R*2 | }
              g { c8@barre(3)@letRing g c' e' g' e' c' g@!letRing@!barre | e,8@palmMute e, e, e, e, e, e, e,@!palmMute | }
              chords h { C | E5 | }
            }
            form { ~A }
            """ + "\nscore { " + score + " }\n");
        var lines = StaffLineYs(g);
        // The guitar's notation staff: the second five-line staff under a staff above, else the first.
        double guitarTop = staffAbove ? lines[5] : lines[0];
        double rowBaseline = g.Texts.Where(t => t.Role == TextRole.ChordName && t.Text is "C" or "E").Max(t => t.Y);
        foreach (string words in new[] { "let ring", "CIII", "P.M." })
        {
            var text = g.Texts.Single(t => t.Text == words);
            // Over-read: 0.8 em is above the ascenders of any text face.
            double top = text.Y - 0.8 * text.FontSize;
            Assert.True(top > rowBaseline,
                $"\"{words}\" reaches up to {top:F3} and the chords row stands on {rowBaseline:F3}: the row is over the lines, not in them.");
            Assert.True(text.Y < guitarTop, $"\"{words}\" on {text.Y:F3} is not over its own staff ({guitarTop:F3}).");
        }
        if (staffAbove)
        {
            // …and the row, with everything under it, is clear of the staff above.
            double upperBottom = lines[4];
            double rowTop = g.Texts.Where(t => t.Role == TextRole.ChordName).Min(t => t.Y - 0.8 * t.FontSize);
            Assert.True(rowTop > upperBottom, $"the row reaches up to {rowTop:F3}, into the staff above ({upperBottom:F3}).");
        }
    }

    /// <summary>The same row, the staff's other movers: a text written <c>.up</c> and a
    /// <c>@rit</c> line of the lower staff stood over the row too, in the staff above.</summary>
    [Fact]
    public void AStaffsWordsAndRit_StandUnderTheChordsRowBetweenTheStaves()
    {
        var g = RenderedGeometry.Render("""
            octave absolute
            part v { clef treble }
            part g { instrument guitar }
            section A {
              v { R*3 | }
              g { c8@text("hey").up g c' e' g' e' c' g | e,1@rit | e,1@!rit | }
              chords h { C | E5 | E5 | }
            }
            form { ~A }
            score { staff v  chords h  staff g }
            """);
        var lines = StaffLineYs(g);
        double rowBaseline = g.Texts.Where(t => t.Role == TextRole.ChordName && t.Text is "C" or "E").Max(t => t.Y);
        foreach (string words in new[] { "hey", "rit." })
        {
            var text = g.Texts.Single(t => t.Text == words);
            Assert.True(text.Y - 0.8 * text.FontSize > rowBaseline,
                $"\"{words}\" on {text.Y:F3} is over the chords row ({rowBaseline:F3}).");
            Assert.True(text.Y < lines[5]);
        }
    }
}
