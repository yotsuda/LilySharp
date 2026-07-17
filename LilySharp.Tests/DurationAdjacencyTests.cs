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
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The adjacency rule: a duration is GLUED to what it lengthens (<c>c4</c>,
/// <c>&lt;c e g&gt;4</c>). A glued number on a chord/arpeggio MEMBER is therefore a
/// misplaced duration (LYS0015 — members share one, written after the bracket),
/// while a spaced number outside brackets is a detached duration (LYS0016) and a
/// spaced number inside brackets stays a scale degree. This is what keeps
/// <c>&lt;c e g2&gt;</c> from silently reading as C-E-G plus a degree-2 D.
/// </summary>
[Trait("Category", "Unit")]
public class DurationAdjacencyTests
{
    private static bool Has(string source, string code) =>
        SyntaxTree.Parse(source).Diagnostics.Any(d => d.Code == code);

    [Theory]
    [InlineData("{ <c e g2> }")]
    [InlineData("{ <c2 e g> }")]     // on any member, not just the last
    [InlineData("{ <c e g2.> }")]    // glued dots are swallowed with it
    [InlineData("{ << c8 e g >> }")] // arpeggio members carry no durations either
    [InlineData("{ << c e8 g >> }")]
    public void GluedNumberOnAMember_IsADurationError(string source)
        => Assert.True(Has(source, DiagnosticCodes.DurationInsideChord));

    [Theory]
    [InlineData("{ c 4 }")]           // the spaced number is not a duration...
    [InlineData("{ <c e g> 2 }")]     // ...on a chord either
    public void SpacedNumberInMusic_IsADetachedDurationError(string source)
        => Assert.True(Has(source, DiagnosticCodes.DetachedDuration));

    [Theory]
    [InlineData("{ c4 d4. e2 }")]     // glued durations, the normal form
    [InlineData("{ <c e g>2 }")]
    [InlineData("{ <c e g>'4 }")]     // glued through the postfix octave mark
    [InlineData("{ << c e g >>2 }")]
    [InlineData("{ <c 3 5>4 }")]      // spaced numbers inside brackets are degrees
    [InlineData("{ <c e g 2> }")]
    [InlineData("{ <1 3 5>2 }")]      // a first-member number is the degree anchor
    [InlineData("{ << c 3 5 >> }")]
    [InlineData("{ r2. R1*4 }")]
    public void GluedDurationsAndSpacedDegrees_StayClean(string source)
    {
        Assert.False(Has(source, DiagnosticCodes.DurationInsideChord), source);
        Assert.False(Has(source, DiagnosticCodes.DetachedDuration), source);
    }

    [Fact]
    public void GluedMemberDuration_IsSwallowed_NotReadAsADegree()
    {
        // Best-effort recovery: <c e g2> stays a three-note chord — the old
        // behavior silently ADDED a degree-2 note (a D) to it.
        var chord = new MeasureCollector()
            .Collect(SyntaxTree.Parse("{ <c e g2> }")).Voice.Measures
            .SelectMany(m => m.Items).OfType<ChordItem>().First();
        Assert.Equal(new[] { 60, 64, 67 }, chord.Notes.Select(n => n.Midi).ToArray());
    }

    [Fact]
    public void ErroneousSource_StillRendersBestEffort()
    {
        // The preview's contract: a file with parse errors renders whatever DID
        // parse (the CLI, by contrast, gates on errors and writes nothing). The
        // erroneous chord keeps its real notes and the following music survives.
        var src = "part m { clef treble }\n"
                + "section A { m { <c e g2>2 d 4 e2 } }\n"
                + "form main { A }\nscore main { staff m }";
        var tree = SyntaxTree.Parse(src);
        Assert.True(tree.HasErrors); // LYS0015 + LYS0016 are both in there
        var svg = LilySharp.Core.Svg.SvgGenerator.Generate(tree);
        Assert.Contains("data-pos", svg); // real engraved content, not a blank page
    }
}
