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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Music that stops off the bar stops with no bar line after it: LilyPond engraves a bar line
/// only where measurePosition comes round to zero (or where one is written with \bar), and
/// the position is the RUN's — a bar written short earlier moves every later LilyPond bar line.
/// </summary>
/// <remarks>
/// MEASURED (LilyPond 2.26.0, Lab sessions/p659/bars, a BarLine / NoteHead dump of each
/// fixture's twin, last system): no bar after the last note in test/beamlets, beam-skip,
/// beam-64th-stack, beamlets-over-rests, dense-chromatic, fermata-note-spacing,
/// grace-explicit-slur and showcase/05-special-techniques (all ending off the bar); a bar in
/// test/barcheck (3/4 then 5/4) and test/cross-voice-accidental (four 2/4 bars in 4/4), whose
/// runs come back round to the downbeat. Until session 659 Lily# closed every piece with a thin
/// bar (the declared "STILL NOT LILYPOND" of MeasureBuilder.FinalizeMeasures).
/// </remarks>
public class EndOfMusicBarLineTests
{
    private static BarlineType LastBar(string music, string header = "")
    {
        var tree = SyntaxTree.Parse($$"""
            octave absolute
            part m
            section A { {{header}} m { {{music}} } }
            form main { ~A }
            score main { staff m }
            """);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var (_, staff, _) = score.EnumerateStaves().First();
        return staff.PrimaryVoice.Measures[^1].EndBarline;
    }

    [Fact]
    public void MusicStoppingInsideItsBar_HasNoBarLineAfterIt()
        => Assert.Equal(BarlineType.None, LastBar("c'8 d'"));

    [Fact]
    public void AFullLastBar_KeepsItsBarLine()
        => Assert.Equal(BarlineType.Single, LastBar("c'2 c'8 d' e' f'"));

    [Fact]
    public void AWrittenFinalBar_Stays()
        => Assert.Equal(BarlineType.Final, LastBar("c'8 d' |."));

    [Fact]
    public void AShortBarEarlier_CanBringTheEndRoundToTheDownbeat()
        // 1/2 | 1 | 1/2: LilyPond's bars fall at 1 and 2 — the end is on one.
        => Assert.Equal(BarlineType.Single, LastBar("c'2 | c'2 c'2 | c'2"));

    [Fact]
    public void AShortBarEarlier_CanPutAFullLastBarOffTheBar()
        // 1/2 | 1: LilyPond's bar falls at 1, the music stops at 3/2.
        => Assert.Equal(BarlineType.None, LastBar("c'2 | c'1"));

    [Fact]
    public void APickupRestartsTheCount()
        // partial 4, then a full bar and three quarters: the end is at 3/4 of a bar.
        => Assert.Equal(BarlineType.None, LastBar("c'4 c'1 c'2.", "partial 4"));
}
