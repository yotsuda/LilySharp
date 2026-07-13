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
/// The beat unit of a metronome mark: a plain <c>tempo 140</c> is ♩ = 140 (the number is
/// the bpm, quarter-note beat), while <c>tempo 8 = 90</c> pins the beat to an eighth.
/// </summary>
[Trait("Category", "Unit")]
public class TempoMetronomeTests
{
    private static MusicMarkItem TempoMark(string tempoClause)
    {
        var src = $"part m {{ section A {{ c4 d {tempoClause} e f | }} }}\n"
                + "form main { A }\nscore main { staff m }";
        return new MeasureCollector().Collect(SyntaxTree.Parse(src), "m")
            .MusicMarks.First(mk => mk.Type == MusicMarkType.Tempo);
    }

    [Fact]
    public void PlainTempoBpm_IsAQuarterNoteMark()
    {
        // `tempo 140` has no '=', so 140 is the bpm and the beat is a quarter note —
        // not a 140th-note (the old bug read the bpm as the beat unit).
        var mark = TempoMark("tempo 140");
        Assert.Equal("140", mark.Text);
        Assert.Equal(4, mark.TempoBeatUnit);
    }

    [Fact]
    public void ExplicitBeatUnit_IsHonored()
    {
        // `tempo 8 = 90` is ♪ = 90.
        var mark = TempoMark("tempo 8 = 90");
        Assert.Equal("90", mark.Text);
        Assert.Equal(8, mark.TempoBeatUnit);
    }
}
