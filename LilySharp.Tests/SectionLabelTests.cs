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
/// Per-occurrence section display labels in structure
/// (<c>structure { First Second First "First (reprise)" }</c>) and Unicode
/// section/part/phrase identifiers.
/// </summary>
[Trait("Category", "Unit")]
public class SectionLabelTests
{
    private static string?[] SectionLabels(string source)
    {
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(source));
        return score.Voice.Measures.Select(m => m.SectionLabel).ToArray();
    }

    private const string ReuseSource = """
        part melody
        phrase pa { c4 d e f | }
        section First { melody { $pa } }
        section Second { melody { $pa } }
        structure { First Second First "First (reprise)" }
        render score "x.svg" { staff { melody } }
        """;

    [Fact]
    public void OccurrenceLabel_OverridesSectionNameForThatOccurrence()
    {
        var labels = SectionLabels(ReuseSource);

        Assert.Equal(3, labels.Length);
        Assert.Equal("First", labels[0]);
        Assert.Equal("Second", labels[1]);
        Assert.Equal("First (reprise)", labels[2]);
    }

    [Fact]
    public void EmptyLabel_SuppressesTheMark()
    {
        var labels = SectionLabels(ReuseSource.Replace("\"First (reprise)\"", "\"\""));

        Assert.Equal("First", labels[0]);
        Assert.Null(labels[2]);
    }

    [Fact]
    public void UnicodeIdentifiers_ParseAndLabel()
    {
        var labels = SectionLabels("""
            part メロディ
            phrase 動機 { c4 d e f | }
            section イントロ { メロディ { $動機 } }
            structure { イントロ イントロ "イントロ(再現)" }
            render score "x.svg" { staff { メロディ } }
            """);

        Assert.Equal("イントロ", labels[0]);
        Assert.Equal("イントロ(再現)", labels[1]);
    }

    [Fact]
    public void UnicodeIdentifiers_NoDiagnostics()
    {
        var tree = SyntaxTree.Parse("""
            part メロディ
            phrase 動機 { c4 d e f | }
            section イントロ { メロディ { $動機 } }
            structure { イントロ }
            render score "x.svg" { staff { メロディ } }
            """);
        Assert.Empty(tree.Diagnostics);
    }
}
