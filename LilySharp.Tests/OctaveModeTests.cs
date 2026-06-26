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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Tests for the <c>octave absolute</c> / <c>octave relative</c> directive.
/// Relative is the default; absolute makes <c>'</c>/<c>,</c> offsets from a
/// fixed C4 anchor (bare c = C4), with no carry between notes.
/// </summary>
[Trait("Category", "Unit")]
public class OctaveModeTests
{
    private static string[] Trace(string source)
    {
        var collector = new MeasureCollector();
        collector.Collect(SyntaxTree.Parse(source), "melody");
        return collector.PitchTrace.Select(e => e.Pitch).ToArray();
    }

    private static string Wrap(string body, string prefix = "") => $@"
{prefix}part melody {{ clef treble }}
section A {{ melody {{ {body} }} }}
structure {{ A }}
score ""t"" {{ staff {{ melody }} }}
";

    [Fact]
    public void Absolute_IsStateless_OffsetFromC4()
    {
        // octave absolute: bare c = C4, each ' = +1 octave, each , = -1, no carry.
        var trace = Trace(Wrap("cis'4 c'' c' e' | c d e f |", prefix: "octave absolute\n"));
        Assert.Equal(
            new[] { "C#5", "C6", "C5", "E5", "C4", "D4", "E4", "F4" },
            trace);
    }

    [Fact]
    public void Relative_IsDefault_AndAccumulates()
    {
        // No directive ⇒ relative (unchanged): the same text accumulates octaves
        // (each ' adds to the previous note's octave), so it must NOT match the
        // absolute reading. Guards against the default flipping to absolute.
        var trace = Trace(Wrap("cis'4 c'' c' e' |"));
        Assert.Equal(new[] { "C#5", "C7", "C8", "E9" }, trace);
    }

    [Fact]
    public void MidStream_SwitchesBothDirections()
    {
        // relative … then `octave absolute` … then `octave relative` again.
        var trace = Trace(Wrap("c'4 d e octave absolute c' c'' octave relative g a |"));
        //                      C5  D5 E5            C5  C6              G5 A5
        Assert.Equal(new[] { "C5", "D5", "E5", "C5", "C6", "G5", "A5" }, trace);
    }

    [Fact]
    public void Mode_RevertsToFileDefault_AtSectionBoundary()
    {
        // A mid-section `octave absolute` does not leak into the next section:
        // section B starts back in the file-default relative mode.
        var src = @"
part melody { clef treble }
section A { melody { octave absolute  c' c'' | } }
section B { melody { c' c'' | } }
structure { A B }
score ""t"" { staff { melody } }
";
        var collector = new MeasureCollector();
        collector.Collect(SyntaxTree.Parse(src), "melody");
        var trace = collector.PitchTrace.Select(e => e.Pitch).ToArray();

        // A: absolute → C5, C6.  B: relative → c' = C5, then c'' accumulates to
        // C7 (NOT the absolute C6), proving the mode reverted at the boundary.
        Assert.Equal("C5", trace[0]);
        Assert.Equal("C6", trace[1]);
        Assert.Equal("C5", trace[2]);
        Assert.Equal("C7", trace[3]);
    }
}
