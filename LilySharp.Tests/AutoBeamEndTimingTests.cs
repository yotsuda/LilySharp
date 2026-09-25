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

using System.Linq;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// An automatic beam is asked whether it ends at a new note TWICE — first with the shortest
/// note it held before that note, then with the new note folded in — and the first answer
/// can close it.
/// </summary>
/// <remarks>
/// MEASURED (LilyPond 2.26.0, LilySharp-Lab sessions/p577/beamprobe, a Stem hook printing each
/// stem's Beam and autoBeamCheck wrapped to print its calls): in 4/4, a dotted eighth that
/// begins on the second sixteenth of a beat and the eighth after it are both flagged — at the
/// eighth LilyPond asks "end here?" with 3/16 (no exception, so the beat answers yes) before it
/// asks with 1/8 (the half-bar exception, no). The same two notes starting ON the beat, and
/// two straight eighths across the same beat, are beamed.
/// </remarks>
public class AutoBeamEndTimingTests
{
    private static int[] BeamSizes(string music)
    {
        var tree = SyntaxTree.Parse("{ " + music + " }");
        var score = new MeasureCollector().Collect(tree, null);
        return new BeamDetector().DetectBeamGroups(score).Select(g => g.Count).ToArray();
    }

    [Fact]
    public void AnOffBeatDottedEighthAndTheEighthAfterIt_AreNotBeamed()
        => Assert.Empty(BeamSizes("r2 r16 g8. g8 r8 |"));

    [Fact]
    public void TheSameRhythmTied_IsNotBeamedEither()
        => Assert.Equal(new[] { 2 }, BeamSizes("r16 g8.~ g8. g16 r16 f8.~ f8 r16 f16 |"));

    [Fact]
    public void StraightEighthsAcrossTheSameBeat_AreBeamed()
        => Assert.Equal(new[] { 2 }, BeamSizes("r2 r8 g8 g8 r8 |"));

    [Fact]
    public void ADottedEighthOnTheBeat_IsBeamedToTheEighthAfterIt()
        => Assert.Equal(new[] { 2 }, BeamSizes("r2 g8. g8 r16 r8 |"));
}
