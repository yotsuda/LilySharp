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
using System.Text.RegularExpressions;
using LilySharp.Core.MusicXml;
using LilySharp.Core.MusicXmlImport;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.MusicXml;

/// <summary>
/// An imported piece is cut into a section at every rehearsal mark, and its music is written
/// a few bars to a line — the book reads by its form and its bars can be found.
/// </summary>
/// <remarks>
/// Owner's request of 2026-10-01 (session 734): an import came back as one section holding
/// every bar, each part's music on ONE line with <c>@mark("A")</c> … <c>@mark("H")</c> inline.
/// </remarks>
[Trait("Category", "Unit")]
public class MusicXmlSectionCutTests
{
    // A 3/4 opening, then 4/4 from mark B on: the marks fall on bars 1, 3 and 5.
    private const string Source = """
        octave absolute
        time 3/4
        part m { clef treble }
        section S {
          m {
            c'2.@mark("A") | d'2. | time 4/4 e'1@mark("B") | f'1 |
            g'1@mark("C") | a'1 | b'1 | c''1 | d''1 | e''1 |
          }
        }
        form main { ~S }
        score main { staff m }
        """;

    private static string Import(string lys)
    {
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse(lys)).ToXml().ToString();
        var (imported, _) = new MusicXmlImporter().Import(xml);
        return imported;
    }

    private static string SectionBody(string lys, string name)
        => Regex.Match(lys, @"section " + name + @" \{\n(.*?)\n\}\n", RegexOptions.Singleline).Groups[1].Value;

    [Fact]
    public void EachRehearsalMark_OpensASection_AndTheFormPlaysThemInOrder()
    {
        string lys = Import(Source);
        foreach (var name in new[] { "A", "B", "C" })
            Assert.Contains("section " + name + " {", lys);
        Assert.Matches(@"form main \{\s*~A ~B ~C\s*\}", lys);
        // The mark itself stays on its note: the section labels are hidden.
        Assert.Contains("@mark(\"B\")", SectionBody(lys, "B"));
        Assert.DoesNotContain(SyntaxTree.Parse(lys).Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>
    /// A section boundary resets the meter, so a section that starts in anything but the
    /// header's restates it — and the header is the meter most sections start in (counting
    /// the ones that do not change it on their own bar), since every restatement prints a
    /// signature.
    /// </summary>
    [Fact]
    public void TheHeaderMeter_IsTheOneMostSectionsStartIn_AndOnlyTheOthersRestateIt()
    {
        string lys = Import(Source);
        Assert.Matches(@"(?m)^time 4/4$", lys);
        Assert.StartsWith("time 3/4 ", SectionBody(lys, "A").Split('\n')[1].TrimStart());
        Assert.DoesNotContain("time", SectionBody(lys, "B"));
        Assert.DoesNotContain("time", SectionBody(lys, "C"));

        // …and the page draws the meters the source drew: a 3/4 at the start and ONE change,
        // at bar 3 — no second 4/4 where C begins.
        string part = Regex.Match(lys, @"part (\w+) \{").Groups[1].Value;
        var measures = new MeasureCollector().Collect(SyntaxTree.Parse(lys), part).Voice.Measures.ToArray();
        Assert.Equal(10, measures.Length);
        var changes = measures.Select((m, i) => (i, n: m.Items.OfType<TimeSignatureChangeItem>().Count()))
            .Where(x => x.n > 0).ToArray();
        Assert.Equal([(2, 1)], changes);
    }

    [Fact]
    public void TheMusic_IsWrittenFourBarsToALine()
    {
        string lys = Import(Source);
        // C holds six bars: four on its first line, two on its second.
        var lines = SectionBody(lys, "C").Split('\n').Skip(1).Take(2).ToArray();
        Assert.Equal(4, Regex.Matches(lines[0], @"\|").Count);
        Assert.Equal(2, Regex.Matches(lines[1], @"\|").Count);
    }

    [Fact]
    public void ADoubleBar_EndsItsLine()
    {
        string lys = Import("""
            octave absolute
            part m { clef treble }
            section S { m { c'1 | d'1 || e'1 | f'1 | g'1 | a'1 | b'1 |. } }
            form main { ~S }
            score main { staff m }
            """);
        var lines = SectionBody(lys, "A").Split('\n').Where(l => l.Contains('|')).ToArray();
        Assert.EndsWith("||", lines[0].TrimEnd());
        Assert.Equal(4, Regex.Matches(lines[1], @"\|").Count);
    }

    /// <summary>
    /// A mark where a section would have to restate its meter does not cut: the restatement
    /// would print a second 4/4 the source never drew. It stays on its note, in the section
    /// before it, and the report says why.
    /// </summary>
    /// <remarks>After the round trip of section-meter-resets-to-global-meter.lys (Lab
    /// sessions/p734/imp): B opens in the 4/4 that A ends in, while C and D — which change
    /// nothing on their own bars — open in 3/4, so the header is 3/4 and only B would restate.</remarks>
    [Fact]
    public void AMarkThatWouldRestateTheMeter_DoesNotCut()
    {
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse("""
            octave absolute
            time 3/4
            part m { clef treble }
            section S {
              m {
                c'4@mark("A") d'4 e'4 | time 4/4 f'1 | g1@mark("B") | time 3/4 c'2. |
                c'2.@mark("C") | d'2.@mark("D") | e'2. |
              }
            }
            form main { ~S }
            score main { staff m }
            """)).ToXml().ToString();
        var (lys, report) = new MusicXmlImporter().Import(xml);
        Assert.Matches(@"(?m)^time 3/4$", lys);
        Assert.Matches(@"form main \{\s*~A ~C ~D\s*\}", lys);
        Assert.Contains("@mark(\"B\")", SectionBody(lys, "A"));
        Assert.Contains(report.Warnings, w => w.Contains("'B' does not start a section"));
    }

    /// <summary>
    /// A mark on a bar that changes the meter writes the change whatever the header is, so it
    /// casts no vote for the header: B and C both change to 4/4, and D — the one mark that
    /// changes nothing — still gets the 3/4 header it opens in, and its section.
    /// </summary>
    [Fact]
    public void AMarkThatChangesTheMeter_DoesNotVoteForTheHeader()
    {
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse("""
            octave absolute
            time 3/4
            part m { clef treble }
            section S {
              m {
                c'2.@mark("A") | time 4/4 c'1@mark("B") | time 3/4 c'2. | time 4/4 c'1@mark("C") |
                time 3/4 c'2. | c'2.@mark("D") | c'2. |
              }
            }
            form main { ~S }
            score main { staff m }
            """)).ToXml().ToString();
        var (lys, report) = new MusicXmlImporter().Import(xml);
        Assert.Matches(@"(?m)^time 3/4$", lys);
        Assert.Matches(@"form main \{\s*~A ~B ~C ~D\s*\}", lys);
        Assert.Empty(report.Warnings);
    }

    /// <summary>
    /// A mark where a hairpin open before it would run on through the whole section after it
    /// does not cut: a carried span must end in the section it is carried into (LYS4023). An
    /// imported hairpin ends only at the next dynamic or hairpin.
    /// </summary>
    /// <remarks>The round trip of hairpin-in-a-repeated-section.lys (Lab sessions/p734/imp).</remarks>
    [Fact]
    public void AMarkAHairpinWouldRunThrough_DoesNotCut()
    {
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse("""
            octave absolute
            part m { clef treble }
            section S {
              m { c'2@mark("A")@cresc c'2 | c'2 c'2 | d'2@mark("B") d'2 | d'2 d'2 | c'2@mark("C")@p c'2 | c'2 c'2 | }
            }
            form main { ~S }
            score main { staff m }
            """)).ToXml().ToString();
        var (lys, report) = new MusicXmlImporter().Import(xml);
        Assert.Matches(@"form main \{\s*~A ~C\s*\}", lys);
        Assert.Contains(report.Warnings, w => w.Contains("'B' does not start a section"));
        Assert.DoesNotContain(SemanticValidation.Run(SyntaxTree.Parse(lys)),
            d => d.Code == DiagnosticCodes.SpanAcrossSectionBoundary);
    }

    /// <summary>A mark whose text cannot name a section still cuts one, under a name of the
    /// importer's; digits alone name <c>M</c> and the digits.</summary>
    [Fact]
    public void AMarkThatCannotNameASection_StillCutsOne()
    {
        string lys = Import("""
            octave absolute
            part m { clef treble }
            section S { m { c'1 | d'1@mark("12") | e'1@mark("verse") | f'1 | } }
            form main { ~S }
            score main { staff m }
            """);
        Assert.Matches(@"form main \{\s*~Intro ~M12 ~\w+\s*\}", lys);
        Assert.DoesNotContain(SyntaxTree.Parse(lys).Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }
}
