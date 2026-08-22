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
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// 'tempo 120 swing' (or 'shuffle') flags the score so the renderer draws the
/// swing-eighths feel equation beside the metronome mark. The words are contextual,
/// not reserved, so they stay usable as ordinary identifiers.
/// </summary>
[Trait("Category", "Unit")]
public class SwingTempoTests
{
    private static MultiStaffScore Collect(string body)
    {
        var src =
            body + "\n" +
            "part m { clef treble }\n" +
            "section A { m { c'4 d' e' f' | } }\n" +
            "form main { A }\n" +
            "score main \"s\" { staff m }\n";
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
    }

    [Theory]
    [InlineData("swing")]
    [InlineData("shuffle")]
    public void BareFeelWord_SwingsEighths(string word)
    {
        Assert.Equal(8, Collect($"tempo 120 {word}").SwingSubdivision);
    }

    [Fact]
    public void Swing16_SwingsSixteenths_AndKeepsTheTempo()
    {
        // 'swing 16' selects sixteenth swing — and the 16 must NOT be read as the BPM.
        var score = Collect("tempo 120 swing 16");
        Assert.Equal(16, score.SwingSubdivision);
        Assert.Equal(120, score.Tempo);
    }

    [Fact]
    public void PlainTempo_IsNotSwing()
    {
        Assert.Equal(0, Collect("tempo 120").SwingSubdivision);
    }

    [Fact]
    public void SwingSubdivision_ReachesTheTempoMarkLayout()
    {
        // Locks the whole chain: parse -> collect -> Score.SwingSubdivision -> the laid-out
        // Tempo mark carries it so the renderer draws the right equation.
        var swung = new LayoutEngine().Layout(Collect("tempo 120 swing 16"))
            .MusicMarkLayouts.Single(m => m.MarkType == MusicMarkType.Tempo);
        Assert.Equal(16, swung.SwingSubdivision);

        var plain = new LayoutEngine().Layout(Collect("tempo 120"))
            .MusicMarkLayouts.Single(m => m.MarkType == MusicMarkType.Tempo);
        Assert.Equal(0, plain.SwingSubdivision);
    }

    [Fact]
    public void SwingAndShuffle_AreNotReservedWords()
    {
        // The feel words are recognized only right after a tempo BPM; elsewhere they
        // stay free as part / phrase identifiers.
        var tree = SyntaxTree.Parse(
            "part swing { clef treble }\n" +
            "phrase shuffle { c'4 d' e' f' | }\n" +
            "section A { swing { shuffle } }\n" +
            "form main { A }\n" +
            "score main \"s\" { staff swing }\n");
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
    }
}
