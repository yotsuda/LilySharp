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
            "structure { A }\n" +
            "score \"s\" { staff m }\n";
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
    }

    [Theory]
    [InlineData("swing")]
    [InlineData("shuffle")]
    public void TempoWithFeelWord_SetsSwingTempo(string word)
    {
        Assert.True(Collect($"tempo 120 {word}").SwingTempo);
    }

    [Fact]
    public void PlainTempo_IsNotSwing()
    {
        Assert.False(Collect("tempo 120").SwingTempo);
    }

    [Fact]
    public void SwingTempo_ReachesTheTempoMarkLayout()
    {
        // Locks the whole chain: parse -> collect -> Score.SwingTempo -> the laid-out
        // Tempo mark carries TempoSwing so the renderer draws the equation.
        var layout = new LayoutEngine().Layout(Collect("tempo 120 swing"));
        var tempo = layout.MusicMarkLayouts.Single(m => m.MarkType == MusicMarkType.Tempo);
        Assert.True(tempo.TempoSwing);

        var plain = new LayoutEngine().Layout(Collect("tempo 120"));
        Assert.False(plain.MusicMarkLayouts.Single(m => m.MarkType == MusicMarkType.Tempo).TempoSwing);
    }

    [Fact]
    public void SwingAndShuffle_AreNotReservedWords()
    {
        // The feel words are recognized only right after a tempo BPM; elsewhere they
        // stay free as part / phrase identifiers.
        var tree = SyntaxTree.Parse(
            "part swing { clef treble }\n" +
            "phrase shuffle { c'4 d' e' f' | }\n" +
            "section A { swing { $shuffle } }\n" +
            "structure { A }\n" +
            "score \"s\" { staff swing }\n");
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
    }
}
