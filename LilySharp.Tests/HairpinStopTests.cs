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
using LilySharp.Core.LilyPond;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// `@!cresc` / `@!decresc` / `@!dim` end a hairpin with no dynamic there — LilyPond's <c>\!</c>
/// (grammar feedback A3, 2026-10-12). Until then a hairpin ran to the next dynamic or hairpin,
/// and `@!cresc` was refused as "nothing of that name can be ended".
/// </summary>
[Trait("Category", "Unit")]
public class HairpinStopTests
{
    private const string Book = """
        part m
        section A { m { c''4@p@cresc d e f@!cresc | g1 | a4@decresc g f e | d1@!dim | c4@cresc d e f | g1@f | } }
        form { ~A }
        score { staff m }
        """;

    [Fact]
    public void AStop_EndsTheHairpinOnItsNote()
    {
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(Book));
        var pins = HairpinEngraver.DetectHairpins(score.MusicMarks, score.Dynamics);
        Assert.Equal(3, pins.Length);
        // The crescendo stops ON f (bar 0, its fourth item) and binds to that note's column…
        Assert.Equal((0, 3, true), (pins[0].EndMeasureIndex, pins[0].EndItemIndex, pins[0].EndsAtStop));
        // …the decrescendo on the head of bar 3 — the bar line, LilyPond's to-barline…
        Assert.Equal((3, 0, true), (pins[1].EndMeasureIndex, pins[1].EndItemIndex, pins[1].EndsAtStop));
        // …and a hairpin with no stop still runs to its dynamic.
        Assert.Equal((5, false), (pins[2].EndMeasureIndex, pins[2].EndsAtStop));
    }

    private static System.Collections.Immutable.ImmutableArray<LilySharp.Core.Svg.Model.HairpinItem> Pins(string music)
    {
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(
            "part m\nsection A { m { " + music + " } }\nform { ~A }\nscore { staff m }\n"));
        return HairpinEngraver.DetectHairpins(score.MusicMarks, score.Dynamics);
    }

    [Fact]
    public void AStopWrittenAfterTheStartOnOneNote_SpansThatNote()
    {
        var pin = Assert.Single(Pins("c''2@cresc@!cresc d | e1 | f1@f |"));
        Assert.Equal((0, 0, 0, 1), (pin.StartMeasureIndex, pin.StartItemIndex, pin.EndMeasureIndex, pin.EndItemIndex));
    }

    [Fact]
    public void AStopWrittenBeforeTheStartOnOneNote_EndsTheOneBefore()
    {
        var pins = Pins("c''4@cresc d e@!cresc@decresc f | g1@p |");
        Assert.Equal(2, pins.Length);
        Assert.Equal((0, 2, true), (pins[0].EndMeasureIndex, pins[0].EndItemIndex, pins[0].EndsAtStop));
        Assert.Equal((1, 0), (pins[1].EndMeasureIndex, pins[1].EndItemIndex));
    }

    [Fact]
    public void ANoteHairpinAfterAnOpenOne_EndsThatOneAtItsStart()
    {
        var pins = Pins("c''4@cresc d e@decresc@!decresc f | g1 |");
        Assert.Equal(2, pins.Length);
        // The crescendo stops at the mark, not at the decrescendo's own stop.
        Assert.False(pins[0].EndsAtStop);
        Assert.Equal((0, 3), (pins[1].EndMeasureIndex, pins[1].EndItemIndex));
    }

    [Fact]
    public void AHairpinMetByAnotherInItsBar_RunsToThatOnesNote()
    {
        var pins = Pins("c''4@cresc d e@decresc f | g1@p |");
        Assert.Equal(2, pins.Length);
        Assert.Equal((0, 2), (pins[0].EndMeasureIndex, pins[0].EndItemIndex));
    }

    [Fact]
    public void AHairpinMetByAnotherLaterInABar_RunsToThatOnesNote()
    {
        var pins = Pins("c''4@cresc d e f | g2 a@decresc | g1@p |");
        Assert.Equal(2, pins.Length);
        Assert.Equal((1, 1), (pins[0].EndMeasureIndex, pins[0].EndItemIndex));
    }

    [Fact]
    public void ANoteHairpinOnASectionsLastNote_CrossesNoRepeat()
    {
        const string book = """
            part m
            section A { m { c''2 d@cresc@!cresc | } }
            section B { m { e1 | } }
            form { |: ~A :| ~B }
            score { staff m }
            """;
        Assert.DoesNotContain(SemanticValidation.Run(SyntaxTree.Parse(book)), d => d.Code == DiagnosticCodes.SpanAcrossSectionBoundary);
    }

    [Fact]
    public void ANoteHairpin_ReachesLilyPondAsAWedgeToTheNotesEnd()
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(
            "part m\nsection A { m { c''2@cresc@!cresc d | e1 | } }\nform { ~A }\nscore { staff m }\n"));
        Assert.Contains("endpoint-alignments", ly);
        Assert.Contains("\\< <>\\!", ly);
    }

    [Fact]
    public void ANoteHairpin_StopsAtTheNotesEndInMusicXml()
    {
        var xml = new LilySharp.Core.MusicXml.MusicXmlExporter().Export(SyntaxTree.Parse(
            "part m\nsection A { m { c''2@cresc@!cresc d | e1 | } }\nform { ~A }\nscore { staff m }\n")).ToXml();
        var wedges = xml.Descendants("wedge").ToArray();
        Assert.Equal(new[] { "crescendo", "stop" }, wedges.Select(w => (string?)w.Attribute("type")));
        int divisions = int.Parse(xml.Descendants("divisions").First().Value);
        Assert.Equal(2 * divisions, int.Parse(wedges[1].Parent!.Parent!.Element("offset")!.Value));
    }

    [Fact]
    public void AStop_IsAKnownTerminator()
    {
        var tree = SyntaxTree.Parse(Book);
        Assert.DoesNotContain(SemanticValidation.Run(tree), d => d.Code == DiagnosticCodes.UnknownAnnotation);
    }

    [Fact]
    public void MusicXml_StopsTheWedgeAtTheStop()
    {
        string xml = new LilySharp.Core.MusicXml.MusicXmlExporter().Export(SyntaxTree.Parse(Book)).ToXml().ToString();
        var wedges = System.Text.RegularExpressions.Regex.Matches(xml, "<measure number=\"(\\d+)\"|<wedge type=\"(\\w+)\"")
            .Select(m => m.Groups[1].Success ? "m" + m.Groups[1].Value : m.Groups[2].Value);
        Assert.Equal("m1 crescendo stop m2 m3 diminuendo m4 stop m5 crescendo m6 stop", string.Join(" ", wedges));
    }
    [Fact]
    public void TheTwin_WritesTheStopAfterItsNote()
    {
        var exporter = new LilyPondExporter();
        string ly = exporter.Export(SyntaxTree.Parse(Book));
        Assert.Contains("f\\! |", ly);
        Assert.Contains("d1\\! |", ly);
        Assert.DoesNotContain(exporter.Warnings, w => w.Contains("dropped"));
    }
}
