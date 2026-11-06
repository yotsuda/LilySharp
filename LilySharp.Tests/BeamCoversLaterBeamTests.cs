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
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A beam avoids the beams that START AFTER it on its staff — another voice's — and not the
/// ones that start with it or before it: LilyPond's Beam_collision_engraver covers a beam with
/// the later-starting beams (lily/beam-collision-engraver.cc:173-175 finalize), and the quanter
/// reads their drawn boxes (lily/beam-quanting.cc:377-392 init_instance_variables).
/// </summary>
/// <remarks>
/// MEASURED, LilyPond 2.26.0, <c>-dbackend=svg</c> (Lab sessions/p1113/c1–c4): the numbers below
/// are the primary beam line's centre at the beam's two ends, in staff spaces above the middle
/// line, read off LilyPond's polygons; Lily#'s are read the same way off its own svg.
/// <para>
/// ⚠️ ASSERTED AS PAIRS where the rule is a difference (RULES §5.4): the beam that moves is
/// shown beside the one that does not, and the two beams that start together are shown
/// standing where each stands alone — a port that avoided every other beam, or none, fails one
/// side of each pair.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class BeamCoversLaterBeamTests
{
    private static string Book(string voice1, string voice2) => $$"""
        octave absolute
        time 4/4
        part m { clef treble }
        section A { m { voice { {{voice1}} } { {{voice2}} } } }
        form { ~A }
        score { staff m }
        """;

    /// <summary>Every beam polygon of the page, left to right: its left x and the primary
    /// line's centre at its two ends, in staff spaces ABOVE the staff's middle line.</summary>
    private static (double Left, double LeftY, double RightY)[] Beams(string book)
    {
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(book));
        var inv = CultureInfo.InvariantCulture;
        var staffLines = Regex.Matches(svg, "<line x1=\"([-\\d.]+)\" y1=\"([-\\d.]+)\" x2=\"([-\\d.]+)\" y2=\"([-\\d.]+)\"")
            .Where(l => l.Groups[2].Value == l.Groups[4].Value
                && double.Parse(l.Groups[3].Value, inv) - double.Parse(l.Groups[1].Value, inv) > 5)
            .Select(l => double.Parse(l.Groups[2].Value, inv)).Distinct().OrderBy(y => y).ToArray();
        Assert.Equal(5, staffLines.Length);
        double middle = staffLines[2];
        return Regex.Matches(svg, "<polygon points=\"([^\"]+)\"")
            .Select(m =>
            {
                // Four corners: the two at the left x first, then the two at the right x
                // (SharedRenderer.Beams.DrawBeamSegment); the centre of an end is the mean of its two y.
                var p = m.Groups[1].Value.Split(' ', ',').Select(s => double.Parse(s, inv)).ToArray();
                Assert.Equal(8, p.Length);
                double leftX = p[0];
                double leftY = middle - (p[1] + p[7]) / 2.0;
                double rightY = middle - (p[3] + p[5]) / 2.0;
                return (Left: leftX, LeftY: leftY, RightY: rightY);
            })
            .OrderBy(b => b.Left).ThenByDescending(b => b.LeftY).ToArray();
    }

    private static void AssertEnds((double Left, double LeftY, double RightY) beam, double leftY, double rightY)
    {
        Assert.InRange(beam.LeftY, leftY - 0.02, leftY + 0.02);
        Assert.InRange(beam.RightY, rightY - 0.02, rightY + 0.02);
    }

    [Fact]
    public void TheBeamThatStartsFirstClearsTheOneThatStartsLater_WhicheverVoiceEitherIs()
    {
        // c1: voice 1's beam starts first and climbs over voice 2's — 2.81..3.50 above the middle
        // line, stems from c' five spaces long (read through the other beam it stood at
        // 0.19..1.19); voice 2's stands where it stands alone, 1.19..1.81.
        var c1 = Beams(Book("c'8 d' e' f' r2 |", "r8 e'8@stemUp f'@stemUp g'@stemUp r2 |"));
        Assert.Equal(2, c1.Length);
        AssertEnds(c1[0], 2.81, 3.50);
        AssertEnds(c1[1], 1.19, 1.81);

        // c3: the voices swapped — now voice 2's beam starts first and is the one that rises,
        // 1.19..2.19 → 2.00..2.81, under voice 1's at 0.19..0.81.
        var c3 = Beams(Book("r8 c'8 d' e' r2 |", "e'8@stemUp f'@stemUp g'@stemUp a'@stemUp r2 |"));
        Assert.Equal(2, c3.Length);
        AssertEnds(c3[0], 2.00, 2.81);
        AssertEnds(c3[1], 0.19, 0.81);
    }

    [Fact]
    public void TwoBeamsThatStartTogetherAvoidNothing()
    {
        // c2: both start on the downbeat, so neither covers the other, and they run through each
        // other: 1.19..2.19 and 0.19..1.19 — exactly where each stands on its own (the voice 1
        // beam of c1 before it had to move, and voice 2's four notes alone).
        var c2 = Beams(Book("c'8 d' e' f' r2 |", "e'8@stemUp f'@stemUp g'@stemUp a'@stemUp r2 |"));
        Assert.Equal(2, c2.Length);
        AssertEnds(c2[0], 1.19, 2.19);
        AssertEnds(c2[1], 0.19, 1.19);

        var alone = Beams(Book("e'8@stemUp f'@stemUp g'@stemUp a'@stemUp r2 |", "r1 |"));
        Assert.Single(alone);
        AssertEnds(alone[0], 1.19, 2.19);
    }

    [Fact]
    public void TheWholeStackOfTheLaterBeamIsCleared()
    {
        // c4, in sixteenths: voice 1's two-line beam climbs to 3.50..4.19 (its second line
        // 2.69..3.38) over voice 2's two lines at 1.00..1.81 and 0.19..1.00 — the covered box is
        // the other beam's whole stack, not its primary line alone.
        var c4 = Beams(Book("c'16 d' e' f' r4 r2 |", "r16 e'16@stemUp f'@stemUp g'@stemUp r4 r2 |"));
        Assert.Equal(4, c4.Length);
        AssertEnds(c4[0], 3.50, 4.19);
        AssertEnds(c4[1], 2.69, 3.38);
        AssertEnds(c4[2], 1.00, 1.81);
        AssertEnds(c4[3], 0.19, 1.00);
    }

    private static string GrandStaffBook(string rh, string lh) => $$"""
        octave absolute
        time 4/4
        part rh { clef treble }
        part lh { clef bass }
        section A { rh { {{rh}} } lh { {{lh}} } }
        form { ~A }
        score { grandStaff { staff rh staff lh } }
        """;

    /// <summary>Every beam polygon of a two-staff page, left to right, the two end centres in
    /// staff spaces BELOW the upper staff's middle line (the frame the cross-staff probes were
    /// read in).</summary>
    private static (double Left, double LeftY, double RightY)[] BeamsBelowTheUpperStaff(string book)
    {
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(book));
        var inv = CultureInfo.InvariantCulture;
        var staffLines = Regex.Matches(svg, "<line x1=\"([-\\d.]+)\" y1=\"([-\\d.]+)\" x2=\"([-\\d.]+)\" y2=\"([-\\d.]+)\"")
            .Where(l => l.Groups[2].Value == l.Groups[4].Value
                && double.Parse(l.Groups[3].Value, inv) - double.Parse(l.Groups[1].Value, inv) > 5)
            .Select(l => double.Parse(l.Groups[2].Value, inv)).Distinct().OrderBy(y => y).ToArray();
        Assert.Equal(10, staffLines.Length);
        double middle = staffLines[2];
        return Regex.Matches(svg, "<polygon points=\"([^\"]+)\"")
            .Select(m =>
            {
                var p = m.Groups[1].Value.Split(' ', ',').Select(s => double.Parse(s, inv)).ToArray();
                Assert.Equal(8, p.Length);
                return (Left: p[0], LeftY: (p[1] + p[7]) / 2.0 - middle, RightY: (p[3] + p[5]) / 2.0 - middle);
            })
            .OrderBy(b => b.Left).ToArray();
    }

    [Fact]
    public void ACrossStaffBeamClearsALaterBeamOfTheStaffItReaches()
    {
        // MEASURED, LilyPond 2.26.0 (Lab sessions/p1113/b7, -dbackend=svg): the bass voice's beam
        // up to the e' g' c'' moved onto the treble staff starts on the downbeat; the treble's own
        // `r8 e'8@stemDown f' g'` starts after it, and its beam (4.50..3.81 below the treble middle
        // line) is in the cross-staff beam's way — which ends 6.19..4.00 below, where read through
        // the treble beam it ended 6.00..3.81 (b5's shape, whose treble beam stood clear). The
        // treble beam itself does not move: it starts later, so it covers nothing.
        var beams = BeamsBelowTheUpperStaff(GrandStaffBook(
            "r1 | r8 e'8@stemDown f'@stemDown g'@stemDown r2 |",
            "r1 | c8 e'@cross g'@cross c''@cross r2 |"));
        Assert.Equal(2, beams.Length);
        Assert.InRange(beams[0].LeftY, 6.19 - 0.02, 6.19 + 0.02);
        Assert.InRange(beams[0].RightY, 4.00 - 0.02, 4.00 + 0.02);
        Assert.InRange(beams[1].LeftY, 4.50 - 0.02, 4.50 + 0.02);
        Assert.InRange(beams[1].RightY, 3.81 - 0.02, 3.81 + 0.02);
    }

    [Fact]
    public void ACoveredGrobsEdgeOnTheBeamsOwnEndIsStillOverTheBeam()
    {
        // MEASURED, LilyPond 2.26.0 (Lab sessions/p1112/b1 → p1113/b1, -dbackend=svg): over the
        // e' g' c'' moved up from the bass, the treble's `r8 a'8@stemDown b' c''` is beamed
        // 3.50..2.81 below the treble middle line in bar 1 exactly as `r8 g'8 a' b'` is in bar 2.
        // Lily# drew bar 1 at 2.81..2.19 — the c'' moved up ends on the beam's own last column,
        // and its head's right edge reached the beam's last segment by a different sum, an ulp
        // past its end, so that edge was never scored (session 1112 called it "two quants on a
        // knife's edge"). flower's Interval::contains is inclusive; the segment walk now is too.
        var beams = BeamsBelowTheUpperStaff(GrandStaffBook(
            "r1 | r8 a'8@stemDown b'@stemDown c''@stemDown r2 | r8 g'8@stemDown a'@stemDown b'@stemDown r2 |",
            "r1 | c8 e'@cross g'@cross c''@cross r2 | c8 e'@cross g'@cross c''@cross r2 |"));
        Assert.Equal(4, beams.Length);
        var treble = beams.Where(b => b.LeftY < 5).OrderBy(b => b.Left).ToArray();
        Assert.Equal(2, treble.Length);
        Assert.InRange(treble[0].LeftY, 3.50 - 0.02, 3.50 + 0.02);
        Assert.InRange(treble[0].RightY, 2.81 - 0.02, 2.81 + 0.02);
        Assert.InRange(treble[1].LeftY, 3.50 - 0.02, 3.50 + 0.02);
        Assert.InRange(treble[1].RightY, 2.81 - 0.02, 2.81 + 0.02);
    }

    [Fact]
    public void ASecondVoicesBeamTurnedUpByEveryNoteIsBeamedUp()
    {
        // The voice's side is the default inside `voice { } { }`; `@stemUp` on the notes turns
        // the beam, as LilyPond reads `\once \override Stem.direction = #UP` on each of them
        // (the twin `lysc ly` writes). Asserted as a pair: unturned, voice 2's beam hangs below
        // its notes; turned, it stands above them at 1.19..1.81 (c1's second voice).
        var unturned = Beams(Book("r1 |", "r8 e'8 f' g' r2 |"));
        Assert.Single(unturned);
        Assert.True(unturned[0].LeftY < -2.5, $"beamed below: {unturned[0].LeftY}");

        var turned = Beams(Book("r1 |", "r8 e'8@stemUp f'@stemUp g'@stemUp r2 |"));
        Assert.Single(turned);
        AssertEnds(turned[0], 1.19, 1.81);
    }
}
