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

using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The words of a <c>pedal text</c> staff stand as LilyPond 2.26.0 sets them (Lab
/// sessions/p1384/m): a word's line spanner lives one timestep past it, so words on
/// consecutive timesteps share one row, and a change is ONE word, "*Ped.", centred on its
/// column.
/// </summary>
public class PedalTextRowTests
{
    private static ScoreLayout Laid(string music)
    {
        string source = "octave absolute\ntime 4/4\npaper { raggedRight }\npart m { clef treble  pedal text }\n"
            + $"section A {{ m {{\n{music}\n}} }}\nform {{ A }}\nscore {{ staff m }}\n";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        return new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
    }

    private static MusicMarkLayout[] Words(ScoreLayout layout) => [.. layout.MusicMarkLayouts
        .Where(m => m.MarkType is MusicMarkType.SustainOn or MusicMarkType.SustainOff)
        .OrderBy(m => m.X)];

    /// <summary>The "*" of the note right after its "Ped." (t5): LilyPond sets both on the
    /// lower word's baseline (ink bottoms −6.300); Lily# solved each alone and left "Ped."
    /// 0.35 higher.</summary>
    [Theory]
    [InlineData("c''2@sustain c'2@!sustain | g''1 |")]          // t5: the next note, in the bar
    [InlineData("c'1@sustain | d'1@!sustain |")]                // t2: the next note, past a bar line
    public void AReleaseOnTheNextNote_SharesItsEngagesRow(string music)
    {
        var words = Words(Laid(music));
        Assert.Equal(2, words.Length);
        Assert.Equal(words[0].YUp, words[1].YUp, 9);
    }

    /// <summary>Not on the next timestep (t4): two spanners, two rows — each word's own.</summary>
    [Fact]
    public void AReleaseLaterOn_KeepsItsOwnRow()
    {
        var words = Words(Laid("c''2@sustain d''2 | e''2 f''2@!sustain |"));
        Assert.Equal(2, words.Length);
        Assert.True(words[1].YUp - words[0].YUp > 0.5, $"{words[0].YUp:F3} / {words[1].YUp:F3}");
    }

    /// <summary>A change (t6): "*Ped." over the column 3.002 right of the first "Ped.", the
    /// "*" (1.5556 wide) at 0.488..2.044 and the "Ped." (3.472) at 2.044..5.516, all three
    /// words on the first one's row. Lily# kept the change's "Ped." centred on the column,
    /// set the "*" 0.4 to its left, and stacked that "Ped." 2.27 under the first, which it
    /// overlaps.</summary>
    [Fact]
    public void AChange_IsOneWordOnItsColumn_OnTheEngagesRow()
    {
        var words = Words(Laid("c''4@sustain d''4@!sustain@sustain e''4@!sustain c'4 | g''1 |"));
        Assert.Equal(4, words.Length);
        var first = words[0];
        var star = words.Single(w => w.MarkType == MusicMarkType.SustainOff && w.X < 3.0 + first.X);
        var engage = words.Single(w => w.MarkType == MusicMarkType.SustainOn && w != first);
        Assert.Equal(1.266, star.X - first.X, 3);
        Assert.Equal(3.780, engage.X - first.X, 3);
        Assert.All(words.Where(w => w.X <= engage.X), w => Assert.Equal(first.YUp, w.YUp, 9));
    }
}
