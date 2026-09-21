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

using LilySharp.Core.Svg;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A CHORD's augmentation dots against its own FLAG — the second of the two places Lily#
/// assembles a dot column's supports.
/// </summary>
/// <remarks>
/// <para>
/// <c>Dot_column</c> builds a rightward skyline over the column's supports and the flag is
/// one of them, so the dot of an unbeamed flagged note stands right of the FLAG whenever the
/// flag's ink reaches the row the dot ended up on. Lily# writes that rule out twice — once in
/// <c>SharedRenderer.DrawNote</c> and once in <c>SharedRenderer.DrawChord</c>, because a
/// chord's stem is reckoned from its far head and neither branch can borrow the other's.
/// </para>
/// <para>
/// ⚠️ THIS EXISTS BECAUSE ONLY THE FIRST OF THE TWO WAS OBSERVED. Session 450 handed
/// <c>DrawChord</c> an EMPTY support span and all 8,774 tests stayed green, while the same
/// poison on <c>DrawNote</c> turned five red
/// (Fixtures/test/dotted-flag-dot-column, test/cue-flag-dot, test/grace-dot-flag-column and
/// two <see cref="GraceBodyValidatorTests"/>) — every one of them a SINGLE note or a grace.
/// Measured again in session 452: the poison moves three of the four columns below by
/// 0.7632, the width of the flag's reach past the head.
/// </para>
/// <para>
/// The numbers are LilyPond 2.26.0's own, from
/// audit/lp-geometry/probes/chord-flag-dot-column.ly (this book exported by <c>lysc ly</c>),
/// read as the Dots grob's left minus its own NoteHead's left — the same quantity, and the
/// same four answers, as the single-note book in Fixtures/test/dotted-flag-dot-column.lys:
/// </para>
/// <code>
/// &lt;g b&gt;8.   heads 8.585000   dots 11.102400   2.5174   top head ON A LINE, dot lifted
/// &lt;f a&gt;8.   heads 17.791155  dots 19.545355   1.7542   top head IN A SPACE, not lifted
/// &lt;g b&gt;16.  heads 35.587310  dots 38.104710   2.5174
/// &lt;f a&gt;16.  heads 42.493465  dots 45.010865   2.5174   the 16th's flag is deeper
/// </code>
/// where 2.5174 = 1.2392 (the up stem's centre) + 0.8282 (the flag's own right edge) + 0.4500
/// (one dot width), and 1.7542 = 1.3042 (the black head's ink right) + 0.4500.
/// LILYPOND-REF: lily/dot-column.cc:100-141 Dot_column::calc_positioning_done — its loop over
///   Stem::flag (stem); lily/dot-configuration.cc for the rows.
/// </remarks>
[Trait("Category", "Unit")]
public class ChordDotColumnTests
{
    /// <summary>
    /// A CHORD's dot clears the flag exactly when the flag is on its row — the same gate the
    /// single note is held to, asked of the branch that draws a chord.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE RESTS ARE BEAM BREAKERS, not decoration: Lily# auto-beams
    /// <c>&lt;g b&gt;8. &lt;f a&gt;8.</c> where LilyPond does not, and a beamed column has no
    /// flag at all — the single-note fixture's header records the first probe written for it
    /// measuring nothing for exactly that reason.
    /// ⚠️ THE PAIR IS THE POINT. <c>&lt;f a&gt;8.</c> is the control: its flag does NOT reach
    /// the unlifted dot, so it answers the head's own width, and it is the one column of the
    /// four that the empty-support poison leaves alone. A test written on the other three
    /// alone could not tell "the supports are read" from "the dots are always pushed".
    /// </remarks>
    [Fact]
    public void AChordsDotClearsTheFlagOnlyWhenTheFlagIsOnItsRow()
    {
        var g = RenderedGeometry.Render(Book(
            "<g b>8. r16 <f a>8. r16 r2 | <g b>16. r32 <f a>16. r32 r2 r4 |", "CFD"));

        // Both heads of a third share one column X, and so does the chord's one dot column;
        // Distinct therefore reduces each to one reading per chord.
        double[] heads = [.. g.Glyphs.Where(x => x.Glyph == EmmentalerGlyphs.NoteheadBlack)
            .Select(x => x.X).Distinct().Order()];
        double[] dots = [.. g.Glyphs.Where(x => x.Glyph == EmmentalerGlyphs.AugmentationDot)
            .Select(x => x.X).Distinct().Order()];
        Assert.Equal(4, heads.Length);
        Assert.Equal(4, dots.Length);

        const double Head = 1.7542;   // the black head's ink right + one dot
        const double Flag = 2.5174;   // the flag's own right edge + the same dot
        Assert.Equal(Flag, dots[0] - heads[0], 4);
        Assert.Equal(Head, dots[1] - heads[1], 4);
        Assert.Equal(Flag, dots[2] - heads[2], 4);
        Assert.Equal(Flag, dots[3] - heads[3], 4);
    }

    /// <summary>The one-staff, one-system book the probe's twin is written in.</summary>
    private static string Book(string music, string name) => $$"""
        octave absolute
        time 4/4
        key c major

        part melody { clef treble }

        section Main {
          melody { {{music}} }
        }

        form main { ~Main }

        score main "{{name}}" {
          staff melody
        }
        """;
}
