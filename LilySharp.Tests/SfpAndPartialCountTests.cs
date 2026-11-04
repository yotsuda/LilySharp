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
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.MusicXmlImport;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary><c>@sfp</c> and <c>partial 8*5</c> (owner's decision 2026-11-04), LilyPond's
/// <c>\sfp</c> and <c>\partial 8*5</c>, through the language, the files and the import.</summary>
[Trait("Category", "Unit")]
public class SfpAndPartialCountTests
{
    private const string Head = "octave absolute\ntime 9/8\npart m { clef treble }\n";
    private const string Tail = "form { A }\nscore { staff m }\n";
    private const string Sfp = "octave absolute\ntime 4/4\npart m { clef treble }\nsection A { m { c''4@sfp d'' e'' f'' | } }\n" + Tail;
    private const string Five = Head + "section A { partial 8*5\n m { c''2 d''8 | e''1 r8 | } }\n" + Tail;

    private static SyntaxTree Parse(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        return tree;
    }

    [Fact]
    public void Sfp_IsADynamic_WithNoWarning()
    {
        var tree = Parse(Sfp);
        Assert.Equal(DynamicLevel.SFP, tree.GetRoot().DescendantNodes<DynamicSyntax>().Single().Level);
        Assert.Empty(SemanticValidation.Run(tree));
    }

    [Fact]
    public void Sfp_GoesToTheFiles()
    {
        Assert.Contains("\\sfp", new LilyPondExporter().Export(Parse(Sfp)));
        Assert.Single(new MusicXmlExporter().Export(Parse(Sfp)).ToXml().Descendants("sfp"));
        Assert.Equal(101, new MidiExporter().Export(Parse(Sfp)).Tracks.SelectMany(t => t.Notes).First().Velocity);
    }

    [Fact]
    public void Sfp_IsDrawn_WithItsLetters()
    {
        var tree = Parse(Sfp);
        var page = new LilySharp.Core.Svg.Collector.MeasureCollector().CollectMultiStaff(tree, LilySharp.Core.Svg.Collector.RenderSpecParser.FindFirst(tree)!);
        Assert.Equal("sfp", page.Dynamics.Single().Text);
    }

    [Fact]
    public void SfAndPAtOneMoment_ImportAsSfp()
    {
        var (lys, _) = new MusicXmlImporter().Import(Book(
            Dir("<sf/>") + Dir("<p/>") + Note("C") + Note("D") + Note("E") + Note("F")));
        Assert.Contains("@sfp", lys);
        Assert.Empty(SemanticValidation.Run(SyntaxTree.Parse(lys)).Where(d => d.Message.Contains("not printed")));
    }

    [Fact]
    public void FAndPAtOneMoment_ImportAsFp()
    {
        var (lys, _) = new MusicXmlImporter().Import(Book(
            Dir("<f/>") + Dir("<p/>") + Note("C") + Note("D") + Note("E") + Note("F")));
        Assert.Contains("@fp", lys);
    }

    [Fact]
    public void TheSoftLevelFirst_IsNoSfp()
    {
        var (lys, _) = new MusicXmlImporter().Import(Book(
            Dir("<p/>") + Dir("<sf/>") + Note("C") + Note("D") + Note("E") + Note("F")));
        Assert.Contains("@p@sf", lys);
    }

    [Fact]
    public void SfpFromTheSource_RoundTrips()
    {
        var (lys, _) = new MusicXmlImporter().Import(Book(Dir("<sfp/>") + Note("C") + Note("D") + Note("E") + Note("F")));
        Assert.Contains("@sfp", lys);
        Assert.False(SyntaxTree.Parse(lys).HasErrors);
        Assert.Single(new MusicXmlExporter().Export(SyntaxTree.Parse(lys)).ToXml().Descendants("sfp"));
    }

    [Fact]
    public void ACountedPartial_IsThatLong_AndWarnsOfNothing()
    {
        var tree = Parse(Five);
        var partial = tree.GetRoot().DescendantNodes<PartialDeclarationSyntax>().Single();
        Assert.Equal(new Fraction(5, 8), partial.ToFraction());
        Assert.Equal(5, partial.Count);
        Assert.Empty(SemanticValidation.Run(tree).Where(d => d.Severity != DiagnosticSeverity.Hidden));
    }

    [Fact]
    public void ACountedPartial_RoundTripsInTheTree()
        => Assert.Equal(Five, Parse(Five).GetRoot().ToFullString());

    [Fact]
    public void ACountedPartial_GoesToTheFiles()
    {
        Assert.Contains("\\partial 8*5", new LilyPondExporter().Export(Parse(Five)));
        var midi = new MidiExporter().Export(Parse(Five));
        // The second bar's first note: five eighths in.
        Assert.Equal(1200, midi.Tracks.SelectMany(t => t.Notes).OrderBy(n => n.StartTick).ElementAt(2).StartTick);
        var doc = new MusicXmlExporter().Export(Parse(Five)).ToXml();
        Assert.Equal("yes", doc.Descendants("measure").First().Attribute("implicit")?.Value);
    }

    [Fact]
    public void ACountOfZero_IsRefused()
        => Assert.True(SyntaxTree.Parse(Head + "section A { partial 8*0\n m { c''1 | } }\n" + Tail).HasErrors);

    [Fact]
    public void AMissingCount_IsRefused()
        => Assert.True(SyntaxTree.Parse(Head + "section A { partial 8*\n m { c''1 | } }\n" + Tail).HasErrors);

    [Fact]
    public void ALengthNoOneValueSpells_ImportsAsACount()
    {
        const string attrs = "<attributes><divisions>2</divisions><time><beats>9</beats><beat-type>8</beat-type></time><clef><sign>G</sign><line>2</line></clef></attributes>";
        static string N(string step, int dur, string type) => $"<note><pitch><step>{step}</step><octave>4</octave></pitch><duration>{dur}</duration><voice>1</voice><type>{type}</type></note>";
        var xml = "<score-partwise version=\"4.0\"><part-list><score-part id=\"P1\"><part-name>P</part-name></score-part></part-list><part id=\"P1\">"
            + "<measure number=\"0\" implicit=\"yes\">" + attrs + N("C", 4, "half") + N("D", 1, "eighth") + "</measure>"
            + "<measure number=\"1\">" + N("E", 9, "whole") + "</measure></part></score-partwise>";
        var (lys, _) = new MusicXmlImporter().Import(xml);
        Assert.Contains("partial 8*5", lys);
        Assert.Empty(SemanticValidation.Run(SyntaxTree.Parse(lys)).Where(d => d.Message.Contains("shorter than the meter")));
    }

    /// <summary>A length that is no count of a note value (two triplet eighths) keeps its warning.</summary>
    [Fact]
    public void ATupletsLength_IsNoCount()
    {
        const string attrs = "<attributes><divisions>3</divisions><time><beats>4</beats><beat-type>4</beat-type></time><clef><sign>G</sign><line>2</line></clef></attributes>";
        static string T(string step, string bracket) => $"<note><pitch><step>{step}</step><octave>4</octave></pitch><duration>1</duration><voice>1</voice><type>eighth</type><time-modification><actual-notes>3</actual-notes><normal-notes>2</normal-notes></time-modification><notations><tuplet type=\"{bracket}\"/></notations></note>";
        var xml = "<score-partwise version=\"4.0\"><part-list><score-part id=\"P1\"><part-name>P</part-name></score-part></part-list><part id=\"P1\">"
            + "<measure number=\"0\" implicit=\"yes\">" + attrs + T("C", "start") + T("D", "stop") + "</measure>"
            + "<measure number=\"1\"><note><pitch><step>E</step><octave>4</octave></pitch><duration>12</duration><voice>1</voice><type>whole</type></note></measure></part></score-partwise>";
        Assert.DoesNotContain("partial", new MusicXmlImporter().Import(xml).Item1);
    }

    /// <summary>Like @fp, @sfp says how loud the bars after it are: a level for the checks.</summary>
    [Fact]
    public void Sfp_IsALevel_ForTheChecks()
        => Assert.Contains("m", LilySharp.Core.Editing.CompositionChecks.DynamicLevelsByPart(Parse(Sfp).GetRoot()));

    private static string Note(string step)
        => $"<note><pitch><step>{step}</step><octave>4</octave></pitch><duration>1</duration><voice>1</voice><type>quarter</type></note>";

    private static string Dir(string level) => $"<direction><direction-type><dynamics>{level}</dynamics></direction-type></direction>";

    private static string Book(string measure)
        => "<score-partwise version=\"4.0\"><part-list><score-part id=\"P1\"><part-name>P</part-name></score-part></part-list><part id=\"P1\">"
           + "<measure number=\"1\"><attributes><divisions>1</divisions><time><beats>4</beats><beat-type>4</beat-type></time><clef><sign>G</sign><line>2</line></clef></attributes>"
           + measure + "</measure></part></score-partwise>";
}
