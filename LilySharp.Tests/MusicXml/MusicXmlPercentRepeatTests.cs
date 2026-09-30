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
using System.Xml.Linq;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.MusicXml;

/// <summary>
/// <c>repeat percent</c> as MusicXML's <c>&lt;measure-repeat&gt;</c>: the written bar(s) are
/// plain, the repetitions run from a <c>start</c> — carrying how many measures one repetition
/// spans, 1 for <c>%</c> and 2 for <c>%%</c> — to a <c>stop</c> on the first bar after them.
/// Until 2026-09-30 the stop was lost whenever that bar stated attributes of its own, a
/// two-bar body was not a percent at all, and the start carried no count (LilySharp-Omr
/// docs/repro/musicxml-exporter-bugs.md #3).
/// </summary>
[Trait("Category", "Unit")]
public class MusicXmlPercentRepeatTests
{
    private static XDocument Export(string sections, string form)
    {
        var tree = SyntaxTree.Parse(
            $"octave absolute\ntime 4/4\npart m {{ clef treble }}\n{sections}\nform main {{ {form} }}\nscore main {{ staff m }}");
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        return new MusicXmlExporter().Export(tree).ToXml();
    }

    /// <summary>Each measure-repeat as "m{number}:{type}" plus "({count})" when it has one.</summary>
    private static string[] Marks(XDocument doc) =>
        doc.Descendants("measure")
            .SelectMany(m => m.Descendants("measure-repeat").Select(r =>
                $"m{m.Attribute("number")!.Value}:{r.Attribute("type")!.Value}"
                + (r.Value.Length > 0 ? $"({r.Value})" : "")))
            .ToArray();

    [Fact]
    public void AOneBarPercent_StartsOnTheFirstRepetition_AndStopsAfterTheLast()
    {
        var doc = Export("section A { m { repeat percent 3 { c'1 | } d'1 | } }", "A");
        Assert.Equal(new[] { "m2:start(1)", "m4:stop" }, Marks(doc));
        Assert.Equal("1", doc.Descendants("measure-repeat").First().Attribute("slashes")!.Value);
        // The repeated bars keep their notes, for playback and for readers without the sign.
        Assert.Equal(4, doc.Descendants("note").Count());
    }

    [Fact]
    public void ATwoBarPercent_IsOneMeasureRepeatSpanningTwoMeasures()
        => Assert.Equal(new[] { "m3:start(2)", "m5:stop" },
            Marks(Export("section A { m { repeat percent 2 { c'1 | d'1 | } e'1 | } }", "A")));

    /// <summary>A run that ends its section stops on the part's next bar — here the next
    /// section's opening bar, which states a new meter: the stop joins those attributes
    /// instead of being dropped for them.</summary>
    [Fact]
    public void ARunEndingItsSection_StopsOnTheNextSectionsFirstBar_BesideItsAttributes()
    {
        var doc = Export(
            "section A { m { c'1 | repeat percent 2 { d'1 | } } }\nsection B { time 3/4\n m { e'2. | } }", "A B");
        Assert.Equal(new[] { "m3:start(1)", "m4:stop" }, Marks(doc));
        var m4 = doc.Descendants("measure").Single(m => m.Attribute("number")!.Value == "4");
        Assert.Equal("3", m4.Descendants("beats").Single().Value);
    }
}
