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
/// The part-major chord track form: <c>chords name { section A { c1 } section B { c1 } }</c>
/// — a chord progression written per section and replayed by the structure, the dual
/// of an in-section chords block. It must parse and collect to the SAME chord row as
/// the equivalent section-major file.
/// </summary>
public class ChordPartSectionsTests
{
    private const string SectionMajor = """
        time 4/4
        key c major
        part melody { clef treble }
        section A { melody { c4 c g' g | a a g2 | } chords harmony { c1 | f1 | } }
        section B { melody { g'4 g f f | } chords harmony { c1 | } }
        structure { A B }
        score "s" { staff melody with chords harmony }
        """;

    private const string PartMajor = """
        time 4/4
        key c major
        part melody { clef treble
          section A { c4 c g' g | a a g2 | }
          section B { g'4 g f f | }
        }
        chords harmony {
          section A { c1 | f1 | }
          section B { c1 | }
        }
        structure { A B }
        score "s" { staff melody with chords harmony }
        """;

    [Fact]
    public void PartMajorChordTrack_ParsesClean()
    {
        Assert.False(SyntaxTree.Parse(PartMajor).HasErrors);
    }

    [Fact]
    public void PartMajorChordTrack_CollectsSameChordsAsSectionMajor()
    {
        // A: C (m0), F (m1); B: C (m2).
        Assert.Equal("0:C 1:F 2:C", ChordSignature(PartMajor));
        Assert.Equal(ChordSignature(SectionMajor), ChordSignature(PartMajor));
    }

    [Fact]
    public void ChordInnerSection_IsNotTreatedAsAStructureSection()
    {
        // The chord track's `section A/B` must not shadow the melody's sections: the
        // melody still collects its own notes (2 bars of A, 1 bar of B = 3 measures).
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(PartMajor), "melody");
        Assert.Equal(3, score.Voice.Measures.Length);
    }

    private static string ChordSignature(string src)
    {
        var score = new MeasureCollector()
            .Collect(SyntaxTree.Parse(src), "melody", null, "harmony");
        return string.Join(" ", score.ChordNames
            .OrderBy(c => c.MeasureIndex).ThenBy(c => c.Timing.ToDouble())
            .Select(c => $"{c.MeasureIndex}:{c.ChordText}"));
    }
}
