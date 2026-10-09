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
using LilySharp.Core.MusicXml;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.MusicXml;

/// <summary>
/// The document states what the page draws: a <c>time</c>, <c>key</c> or <c>clef</c> that
/// changes nothing writes no <c>&lt;attributes&gt;</c>, and <c>key!</c> / <c>time!</c> /
/// <c>clef!</c> write them all the same (owner's decision 2026-10-02, HANDOFF §1.1 第737 ⑸).
/// </summary>
public class MusicXmlForceMarkTests
{
    private static MusicXmlMeasure[] Measures(string music)
    {
        string source = $$"""
            part m { clef treble }
            section A { m { {{music}} } }
            form { ~A }
            score { staff m }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        return new MusicXmlExporter().Export(tree).Parts.Single().Measures.ToArray();
    }

    [Theory]
    [InlineData("key g major c1 | key g major c1 |")]
    [InlineData("time 3/4 c2. | time 3/4 c2. |")]
    [InlineData("clef bass c1 | clef bass c1 |")]
    public void ARestatement_WritesNoAttributes(string music)
    {
        Assert.Null(Measures(music)[1].Attributes);
    }

    [Fact]
    public void AForcedKey_IsWrittenAgain()
    {
        var attrs = Measures("key g major c1 | key! g major c1 |")[1].Attributes;
        Assert.NotNull(attrs);
        Assert.Equal(1, attrs!.KeyFifths);
        Assert.Null(attrs.TimeBeats);
        Assert.Null(attrs.ClefSign);
    }

    [Fact]
    public void AForcedTime_IsWrittenAgain()
    {
        var attrs = Measures("time 3/4 c2. | time! 3/4 c2. |")[1].Attributes;
        Assert.NotNull(attrs);
        Assert.Equal(3, attrs!.TimeBeats);
        Assert.Null(attrs.KeyFifths);
    }

    [Fact]
    public void AForcedClef_IsWrittenAgain()
    {
        var attrs = Measures("clef bass c1 | clef! bass c1 |")[1].Attributes;
        Assert.NotNull(attrs);
        Assert.Equal("F", attrs!.ClefSign);
        Assert.Null(attrs.KeyFifths);
    }

    [Fact]
    public void AForcedKeyAfterNotes_WaitsForTheNextBar()
    {
        // Seen after the bar's notes, the change is held to the next bar's head, as any is.
        var measures = Measures("key g major c2 key! g major c2 | c1 |");
        Assert.Equal(1, measures[1].Attributes?.KeyFifths);
    }
}
