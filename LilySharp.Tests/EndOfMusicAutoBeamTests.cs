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
/// The end of the music is not a bar line: an automatic beam still being built when a last
/// bar the meter does not fill runs out is kept only if the beat check ends it there, and is
/// otherwise dropped, its stems keeping their flags.
/// </summary>
/// <remarks>
/// MEASURED (LilyPond 2.26.0, LilySharp-Lab sessions/p658/endbeam/vis2.ly, one score each):
/// `c'8 d'` flagged, `c'16 d' e' f'` beamed, six eighths beamed four and flagged two, a written
/// `\bar "|."` after four eighths beamed, a full bar then `c'8 d'` flagged, `\time 6/8 c'8 d' e'
/// f'` beamed three and flagged one. Found by the S4 bow sweep (session 657): the reader's
/// slur-dot-collision twin (`e''16.( e,,32)`, no closing bar) has flags where Lily# drew a beam,
/// and the slur follows the stems.
/// LILYPOND-REF: lily/auto-beam-engraver.cc:303-310 finalize (junk_beam).
/// </remarks>
public class EndOfMusicAutoBeamTests
{
    private static int[] BeamSizes(string music)
    {
        var tree = SyntaxTree.Parse("{ " + music + " }");
        var score = new MeasureCollector().Collect(tree, null);
        return new BeamDetector().DetectBeamGroups(score).Select(g => g.Count).ToArray();
    }

    [Fact]
    public void TwoEighthsEndingOnTheFirstBeat_KeepTheirFlags()
        => Assert.Empty(BeamSizes("c'8 d'"));

    [Fact]
    public void FourSixteenthsEndingOnTheFirstBeat_AreBeamed()
        => Assert.Equal(new[] { 4 }, BeamSizes("c'16 d' e' f'"));

    [Fact]
    public void TheEighthsAfterTheHalfBar_KeepTheirFlags()
        => Assert.Equal(new[] { 4 }, BeamSizes("c'8 d' e' f' g' a'"));

    [Fact]
    public void AWrittenFinalBarLine_EndsTheBeam()
        => Assert.Equal(new[] { 2 }, BeamSizes("c'8 d' |."));

    [Fact]
    public void OnlyTheLastBar_IsAsked()
        => Assert.Equal(new[] { 4 }, BeamSizes("c'2 c'8 d' e' f' | c'8 d'"));
}
