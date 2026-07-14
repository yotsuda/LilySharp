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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A section can carry its own time / tempo (beside key) — stated section-major
/// (<c>section A { time 3/4  melody { … } }</c>) or in a standalone part-major header
/// (<c>section A { time 3/4 }</c>). They must render: the meter re-arms the section's
/// measures, the tempo prints a metronome mark.
/// </summary>
[Trait("Category", "Unit")]
public class SectionHeaderDirectiveTests
{
    private static Score Collect(string src)
        => new MeasureCollector().Collect(SyntaxTree.Parse(src), "melody");

    private static bool HasMeter(Score score, int beats, int beatType)
        => score.Voice.Measures.SelectMany(m => m.Items).OfType<TimeSignatureChangeItem>()
            .Any(t => t.NewTime.Beats == beats && t.NewTime.BeatType == beatType);

    [Fact]
    public void SectionMajorTime_EmitsTheSectionMeter()
    {
        var score = Collect("""
            time 4/4
            section A { time 3/4  melody { c4 d e | } }
            form main { A }
            score main { staff melody }
            """);
        Assert.True(HasMeter(score, 3, 4));
    }

    [Fact]
    public void SectionMajorTempo_EmitsAMetronomeMark()
    {
        // On a NON-first section, so it does not coincide with the score's initial tempo.
        var score = Collect("""
            tempo 100
            section A { melody { c4 d e f | } }
            section B { tempo 140  melody { g4 a b c' | } }
            form main { A B }
            score main { staff melody }
            """);
        Assert.Contains(score.MusicMarks, m => m.Type == MusicMarkType.Tempo && m.Text == "140");
    }

    [Fact]
    public void FirstSectionTempo_ReplacesTheScoresOpeningTempo()
    {
        // Section A is first, so its `tempo 140` becomes the piece's opening metronome
        // mark (replacing the score's `tempo 100`) instead of being hidden behind it.
        var score = Collect("""
            tempo 100
            section A { tempo 140  melody { c4 d e f | } }
            section B { melody { g4 a b c' | } }
            form main { A B }
            score main { staff melody }
            """);
        Assert.Equal(140, score.Tempo);
        // It replaced the opening mark — no second Tempo mark stacked on top of it.
        Assert.DoesNotContain(score.MusicMarks, m => m.Type == MusicMarkType.Tempo);
    }

    [Fact]
    public void StandalonePartMajorHeaderTime_AppliesToTheSection()
    {
        var score = Collect("""
            time 4/4
            part melody { section A { c4 d e | } }
            section A { time 3/4 }
            form main { A }
            score main { staff melody }
            """);
        Assert.True(HasMeter(score, 3, 4));
    }

    [Fact]
    public void StandalonePartMajorHeaderTempo_AppliesToTheSection()
    {
        var score = Collect("""
            tempo 100
            part melody { section A { c4 d e f | } section B { g4 a b c' | } }
            section B { tempo 140 }
            form main { A B }
            score main { staff melody }
            """);
        Assert.Contains(score.MusicMarks, m => m.Type == MusicMarkType.Tempo && m.Text == "140");
    }

    [Fact]
    public void SectionPartial_ShortensTheSectionsFirstMeasure()
    {
        // `partial 4` gives section A a quarter-note pickup, so its first bar holds a
        // single quarter rather than a full 4/4 measure.
        var score = Collect("""
            time 4/4
            section A { partial 4  melody { g4 | c' d' e' f' | } }
            form main { A }
            score main { staff melody }
            """);
        Assert.Equal(Fraction.Quarter, score.Voice.Measures[0].TotalDuration);
        Assert.Equal(new Fraction(4, 4), score.Voice.Measures[1].TotalDuration);
    }

    [Fact]
    public void StandalonePartMajorHeaderPartial_AppliesToTheSection()
    {
        var score = Collect("""
            time 4/4
            part melody { section A { g4 | c' d' e' f' | } }
            section A { partial 4 }
            form main { A }
            score main { staff melody }
            """);
        Assert.Equal(Fraction.Quarter, score.Voice.Measures[0].TotalDuration);
    }

    [Fact]
    public void NextSectionWithoutATime_RevertsToTheScoreMeter()
    {
        // Section A is 3/4; B states nothing, so it reverts to 4/4.
        var score = Collect("""
            time 4/4
            section A { time 3/4  melody { c4 d e | } }
            section B { melody { c4 d e f | } }
            form main { A B }
            score main { staff melody }
            """);
        Assert.True(HasMeter(score, 3, 4)); // A's meter
        Assert.True(HasMeter(score, 4, 4)); // B reverted
    }
}
