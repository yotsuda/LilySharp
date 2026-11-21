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
/// A rest that writes <c>@cross</c> moves to the other staff as a note does (owner's decision
/// 2026-11-21, docs/cross-rest-design.md), and sits where THAT staff's Rest_collision puts it.
/// Until then the page left it on its own staff while the <c>.ly</c> twin moved it.
/// </summary>
public class CrossStaffRestTests
{
    private static string Book(string treble, string bass) => $$"""
        octave absolute
        time 4/4
        paper { raggedRight }
        part rh { clef treble }
        part lh { clef bass }
        section A { rh { {{treble}} } lh { {{bass}} } }
        form { A }
        score { grandStaff { staff rh staff lh } }
        """;

    /// <summary>The staff lines' Ys, top to bottom, and every rest glyph as spaces above the
    /// treble's middle line.</summary>
    private static (List<double> Lines, double[] Rests) Read(string svg)
    {
        var lines = Regex.Matches(svg,
                "<line x1=\"[-\\d.]+\" y1=\"([-\\d.]+)\" x2=\"[-\\d.]+\" y2=\"\\1\" stroke=\"#000000\" stroke-width=\"0\\.100\"")
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .Distinct().OrderBy(y => y).ToList();
        Assert.Equal(10, lines.Count);
        var rests = Regex.Matches(svg,
                "<text class=\"music\" x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"[^>]*>(.)</text>")
            .Where(m => (int)m.Groups[3].Value[0] is >= 0xE000 and <= 0xE00F)
            .Select(m => (X: double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                Y: double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)))
            .OrderBy(r => r.X)
            .Select(r => Math.Round(lines[2] - r.Y, 2)).ToArray();
        return (lines, rests);
    }

    /// <summary>
    /// The rest stands on the other staff, cleared off what sounds THERE at its moment: an
    /// unvoiced rest goes down (its column's stem has no head) past the treble's held e''
    /// (head only) or past g''4 struck with it (the whole column, stem tip included); a lower
    /// voice's rest goes down past the treble's c''. A bass grace run moved whole carries its
    /// <c>r16@cross</c> up with it, and the rest (a grace stem is up) clears the treble's held
    /// e'' or a'. A grace rest that does not write <c>@cross</c> stays on the bass. Every pin is
    /// LilyPond 2.26.0's print of the twin, in spaces above the treble's middle line (Lab
    /// sessions/p1350/rest n1–n3, c1–c3); the page drew every one of them on the bass.
    /// LILYPOND-REF: lily/change-iterator.cc:74-99 Change_iterator::process;
    ///   lily/rest-collision.cc:222-284 calc_positioning_done.
    /// </summary>
    [Theory]
    [InlineData("e''2 e''2 |", "c4 r4@cross c2 |", -2.0)]
    [InlineData("e''4 g''4 e''2 |", "c4 r4@cross c2 |", -3.5)]
    [InlineData("c''2 c''2 |", "voice { c4 c4 c2 } { g,4 r4@cross g,2 } |", -3.0)]
    [InlineData("e''2 e''2 |", "c4 grace { r16@cross e'16@cross } c4 c2 |", 5.0)]
    [InlineData("a'2 a'2 |", "c4 grace { r16@cross e'16@cross } c4 c2 |", 3.0)]
    [InlineData("e''2 e''2 |", "c4 grace { r16 e'16@cross } c4 c2 |", -6.0)]
    public void ARestWritingCross_StandsOnTheOtherStaff_WhereItsRestCollisionPutsIt(
        string treble, string bass, double expected)
    {
        var (_, rests) = Read(TestPaper.SvgFromRenderSpec(Book(treble, bass)));
        Assert.Equal(expected, Assert.Single(rests));
    }

    /// <summary>
    /// The staff the rest moved to reserves it in its skyline, so the staves part around it:
    /// under the treble's a the moved rest goes down to −7.0 spaces, between the staves, and
    /// the bass's middle line stands 11.33 spaces below the treble's — both LilyPond 2.26.0's
    /// (Lab sessions/p1350/rest s1).
    /// </summary>
    [Fact]
    public void AScriptOnAMovedRest_StandsOnTheStaffTheRestMovedTo()
    {
        // A fermata over the moved rest is read about the staff the rest stands on, as a
        // script on a moved note is (LayoutEngine.ScriptsOnTheirNotesStaff): over the treble,
        // clear of its e'', at 2.58 spaces — LilyPond 2.26.0's 2.586 (Lab sessions/p1351/marks
        // m1). It printed at −6.42, beside the bass, where the rest was before it moved.
        var svg = TestPaper.SvgFromRenderSpec(Book("e''2 e''2 |", "c4 r4@cross@fermata c2 |"));
        var (lines, rests) = Read(svg);
        double restX = Regex.Matches(svg, "<text class=\"music\" x=\"([-\\d.]+)\" y=\"[-\\d.]+\"[^>]*>(.)</text>")
            .Where(m => (int)m.Groups[2].Value[0] is >= 0xE000 and <= 0xE00F)
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Single();
        var marks = Regex.Matches(svg, "<text class=\"music\" x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"[^>]*>(.)</text>")
            .Select(m => (X: double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                Up: Math.Round(lines[2] - double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), 2),
                Glyph: (int)m.Groups[3].Value[0]))
            .Where(m => m.Glyph is < 0xE000 or > 0xE00F && Math.Abs(m.X - restX - 0.65) < 0.5)
            .ToList();
        Assert.Equal(-2.0, Assert.Single(rests));
        Assert.Equal(2.58, Assert.Single(marks).Up);
    }

    /// <summary>
    /// A dynamic or an <c>@text</c> on a moved note or rest hangs off the staff it moved to
    /// (LayoutEngine.DynamicsOnTheirNotesStaff): the twin's <c>\change Staff</c> moves the whole
    /// Voice, its DynamicText and TextScript with it. Under the treble, in spaces above its middle
    /// line — LilyPond 2.26.0's −6.813, −4.931 and −4.774 (Lab sessions/p1352/marks q2, m2, m3).
    /// Until session 1352 each was as far under the BASS, nine spaces lower.
    /// </summary>
    [Theory]
    [InlineData("c4 g4@cross@p c2 |", "p", -6.82)]
    [InlineData("c4 r4@cross@p c2 |", "p", -4.94)]
    [InlineData("c4 r4@cross@text(\"x\") c2 |", "x", -4.78)]
    public void ADynamicOrTextOnAMovedNote_HangsOffTheStaffItMovedTo(string bass, string label, double expected)
    {
        var svg = TestPaper.SvgFromRenderSpec(Book("e''2 e''2 |", bass));
        var (lines, _) = Read(svg);
        var ys = Regex.Matches(svg, "<text x=\"[-\\d.]+\" y=\"([-\\d.]+)\"[^>]*font-style=\"italic\"[^>]*>" + label + "</text>")
            .Select(m => Math.Round(lines[2] - double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), 2))
            .ToList();
        Assert.Equal(expected, Assert.Single(ys));
    }

    /// <summary>
    /// A moved rest inside a bracketed beam reads the beam across the staves
    /// (ElementCoordinator.CalculateRestShifts): a beam under the bass, far from a rest on the
    /// treble, pushes nothing, and the rest stays where the treble's notes put it — LilyPond
    /// 2.26.0's −1.0 and −3.0 (Lab sessions/p1353/beam b5, b8). Until session 1353 the beam was
    /// read in the bass's positions and lifted the rest to +2.0.
    /// LILYPOND-REF: lily/beam.cc:1349-1355 Beam::rest_collision_callback (the common refpoint).
    /// </summary>
    [Theory]
    [InlineData("e''2 e''2 |", -1.0)]
    [InlineData("a'2 a'2 |", -3.0)]
    public void AMovedRestInABeam_ReadsTheBeamFromTheStaffItStandsOn(string treble, double expected)
    {
        var (_, rests) = Read(TestPaper.SvgFromRenderSpec(Book(treble, "c'8[ r8@cross c'8 c'8] c2 |")));
        Assert.Equal(expected, Assert.Single(rests));
    }

    [Fact]
    public void TheStaffItMovedTo_ReservesTheRest()
    {
        var (lines, rests) = Read(TestPaper.SvgFromRenderSpec(Book("a2 a2 |", "c'4 r4@cross c'2 |")));
        Assert.Equal(-7.0, Assert.Single(rests));
        Assert.Equal(11.33, Math.Round(lines[7] - lines[2], 2));
    }
}
