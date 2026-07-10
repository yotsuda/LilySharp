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
/// Per-score main transpose: `score main "Bb" transpose d { ... }` renders a transposed copy
/// of the piece, composing on top of any per-part transpose.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PerScoreTransposeTests
{
    private static string[] Trace(string source, (int, int, int)? scoreTranspose = null)
    {
        var collector = new MeasureCollector { ScoreTranspose = scoreTranspose };
        collector.Collect(SyntaxTree.Parse(source), "vln");
        return collector.PitchTrace.Select(e => e.Pitch).ToArray();
    }

    private const string Concert =
        "part vln { clef treble section A { c4 d e f | } }\nform main { A }\nscore { staff vln }\n";

    [Fact]
    public void Parses_ScoreTranspose_WithAndWithoutName()
    {
        foreach (var head in new[] { "score main transpose d {", "score main \"Bb\" transpose d {" })
        {
            var tree = SyntaxTree.Parse(
                "part vln { clef treble section A { c4 } }\nform main { A }\n" + head + " staff vln }\n");
            Assert.False(tree.HasErrors, head + " => " + string.Join("\n", tree.Diagnostics));
        }
    }

    [Fact]
    public void RenderSpec_CarriesScoreTranspose()
    {
        var tree = SyntaxTree.Parse(
            "part vln { clef treble section A { c4 } }\nform main { A }\nscore \"Bb\" transpose d { staff vln }\n");
        var spec = RenderSpecParser.FindFirst(tree)!;
        Assert.Equal((1, 0, 0), spec.ScoreTranspose); // d = diatonic step 1, no accidental, same octave
    }

    [Fact]
    public void UnnamedScore_HasNoTranspose()
    {
        var spec = RenderSpecParser.FindFirst(SyntaxTree.Parse(Concert))!;
        Assert.Null(spec.ScoreTranspose);
    }

    [Fact]
    public void ScoreTranspose_ShiftsEveryPitch_UpAMajorSecond()
    {
        // c d e f, transposed up to d -> d e fis g (name-based, chromatic-preserving).
        var trace = Trace(Concert, scoreTranspose: (1, 0, 0));
        Assert.Equal(new[] { "D4", "E4", "F#4", "G4" }, trace);
    }

    [Fact]
    public void ScoreTranspose_ComposesOnTopOfPartTranspose()
    {
        // Part already transposes up a major second (c->d); the score adds another
        // (d->e), so the written c sounds as e — composed once more, not ignored.
        const string src =
            "part vln { clef treble transpose d section A { c4 | } }\nform main { A }\nscore { staff vln }\n";
        Assert.Equal("D4", Trace(src)[0]);                          // part transpose only
        Assert.Equal("E4", Trace(src, scoreTranspose: (1, 0, 0))[0]); // + score main transpose
    }
}
