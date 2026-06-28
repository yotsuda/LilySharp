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
/// Inline volta endings (<c>[1. …] [2. …]</c>) inside a <c>|: … :|</c> repeat:
/// the score draws a volta bracket per ending, and the ending music is written
/// once (the body before the endings is not unfolded — the repeat barlines imply
/// repetition). MIDI playback is covered by <see cref="MidiRepeatTests"/>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class InlineVoltaTests
{
    private static Core.Svg.Model.Score Collect(string source) =>
        new MeasureCollector().Collect(SyntaxTree.Parse(source));

    [Fact]
    public void TwoEndings_ProduceTwoVoltaBrackets()
    {
        var score = Collect("{ |: c4 d4 e4 f4 [1. g4 a4 b4 c5] :| [2. c4 d4 e4 f4] }");

        Assert.Equal(2, score.VoltaBrackets.Length);
        Assert.Equal("1.", score.VoltaBrackets[0].VoltaText);
        Assert.Equal("2.", score.VoltaBrackets[1].VoltaText);
    }

    [Fact]
    public void EveryEnding_ClosesAtItsTrueEnd()
    {
        var score = Collect("{ |: c4 d4 e4 f4 [1. g4 a4 b4 c5] :| [2. c4 d4 e4 f4] }");

        // Every ending closes (right hook) at its true end so the boundary before
        // the next ending is clear; the engraver's segment splitter is what leaves
        // a bracket open where a line break cuts it (not the bracket flag itself).
        Assert.True(score.VoltaBrackets[0].IsClosed); // 1st ending: closes at the :|
        Assert.True(score.VoltaBrackets[1].IsClosed); // 2nd ending: closes at its end
    }

    [Fact]
    public void RangeEnding_KeepsRangeLabel()
    {
        var score = Collect("{ |: c4 d4 e4 f4 [1-2. g4 a4 b4 c5] :| [3. c4 d4 e4 f4] }");

        Assert.Equal("1-2.", score.VoltaBrackets[0].VoltaText);
        Assert.Equal("3.", score.VoltaBrackets[1].VoltaText);
    }

    [Fact]
    public void BeamBracket_IsNotMistakenForVolta()
    {
        // '[' before a note stays a beam marker; no volta brackets are produced.
        var score = Collect("{ [c8 d8 e8 f8] }");
        Assert.Empty(score.VoltaBrackets);
    }

    [Fact]
    public void EndingMusic_IsWrittenOnce_NotUnfolded()
    {
        // The body (one measure of 4) + two endings (one measure each) = 3 measures.
        // If MIDI-style unfolding leaked into the score it would be more.
        var score = Collect("{ |: c4 d4 e4 f4 [1. g4 a4 b4 c5] :| [2. c4 d4 e4 f4] }");
        int measureCount = score.Voices.Sum(v => v.Measures.Length);
        Assert.Equal(3, measureCount);
    }
}
