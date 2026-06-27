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

using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Voice selection in files with MULTIPLE render blocks: the collector must
/// honor the caller's voice, and infer from the FIRST render when none is
/// given. (ExtractVoiceName used to run for every render block and clobber
/// both, so a two-render file always collected the LAST render's part.)
/// </summary>
[Trait("Category", "Unit")]
public class RenderVoiceSelectionTests
{
    private const string TwoRenderSource = """
        part melody
        part chords
        phrase pa { c4 d e f | }
        phrase pb { <c e g>1 | }
        section Main {
          melody { $pa }
          chords { $pb }
        }
        structure { Main }
        score "first" { staff melody }
        score "second" { staff chords }
        """;

    [Fact]
    public void ExplicitVoice_IsNotClobberedByLaterRenderBlocks()
    {
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(TwoRenderSource), "melody");

        Assert.Equal(4, score.Voice.Measures[0].Items.Length);
        Assert.All(score.Voice.Measures[0].Items, i => Assert.IsType<NoteItem>(i));
    }

    [Fact]
    public void NoVoiceGiven_FirstRenderBlockWins()
    {
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(TwoRenderSource));

        Assert.All(score.Voice.Measures[0].Items, i => Assert.IsType<NoteItem>(i));
    }

    [Fact]
    public void SecondRenderVoice_StillSelectable()
    {
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(TwoRenderSource), "chords");

        var chord = Assert.Single(score.Voice.Measures[0].Items);
        Assert.IsType<ChordItem>(chord);
    }
}
