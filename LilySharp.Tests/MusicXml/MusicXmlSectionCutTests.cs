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
    /// A mark where a section has to restate its meter cuts all the same: the restatement
    /// states what the section before left, and a section head that does draws nothing
    /// (MeasureBuilder.SectionHead, owner's decision 2026-10-02). Until then the mark was
    /// refused — the restatement printed a second 4/4 the source never drew.
    /// </summary>
    /// <remarks>After the round trip of section-meter-resets-to-global-meter.lys (Lab
    /// sessions/p734/imp): B opens in the 4/4 that A ends in, while C and D — which change
    /// nothing on their own bars — open in 3/4, so the header is 3/4 and only B restates.</remarks>
    [Fact]
    public void AMarkThatRestatesTheMeter_CutsAndDrawsNoSecondSignature()
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
        Assert.Matches(@"form main \{\s*~A ~B ~C ~D\s*\}", lys);
        Assert.Contains("time 4/4", SectionBody(lys, "B"));
        Assert.DoesNotContain(report.Warnings, w => w.Contains("does not start a section"));
        // The page draws the source's two meter changes and no third: B's 4/4 is no change.
        var tree = SyntaxTree.Parse(lys);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var drawn = new MeasureCollector().Collect(tree, "m").Voice.Measures
            .SelectMany(m => m.Items).OfType<TimeSignatureChangeItem>()
            .Select(t => $"{t.NewTime.Beats}/{t.NewTime.BeatType}").ToArray();
        Assert.Equal(new[] { "4/4", "3/4" }, drawn);
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

    // Every time, key and clef the page draws for part m, by bar.
    private static string[] Drawn(string lys)
    {
        var tree = SyntaxTree.Parse(lys);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        return new MeasureCollector().Collect(tree, "m").Voice.Measures
            .SelectMany((m, i) => m.Items.Select(item => item switch
            {
                TimeSignatureChangeItem t => $"{i}: time {t.NewTime.Beats}/{t.NewTime.BeatType}",
                KeySignatureChangeItem k => $"{i}: key {k.NewKey.Sharps}",
                ClefChangeItem c => $"{i}: clef {c.NewClef}",
                _ => null,
            }))
            .OfType<string>().ToArray();
    }

    /// <summary>
    /// A key, time or clef the document states again without changing it is there on purpose
    /// (MusicXML prints what <c>&lt;attributes&gt;</c> states), so it comes back forced, and a
    /// section opens on its bar (owner's decision 2026-10-02). Until then it was dropped, and
    /// <c>key!</c> did not survive its own round trip.
    /// </summary>
    [Fact]
    public void AForcedRestatement_ComesBackForced_AtTheHeadOfASection()
    {
        const string source = """
            octave absolute
            time 4/4
            key g major
            part m { clef treble }
            section S { m { g'1 | a'1 | key! g major b'1 | c''1 | time! 4/4 d''1 | clef! treble e''1 | } }
            form main { ~S }
            score main { staff m }
            """;
        string lys = Import(source);
        Assert.Matches(@"form main \{\s*~A ~A2 ~A3 ~A4\s*\}", lys);
        Assert.StartsWith("key! g major b'1", SectionBody(lys, "A2").Trim().Split('\n')[1].Trim());
        Assert.Contains("time! 4/4 d''1", SectionBody(lys, "A3"));
        Assert.Contains("clef! treble e''1", SectionBody(lys, "A4"));
        Assert.Equal(Drawn(source), Drawn(lys));
        Assert.Equal(3, Drawn(lys).Length);
    }

    /// <summary>In a piece with marks, the section a restatement opens continues the name of
    /// the mark's: B, then B2.</summary>
    [Fact]
    public void ARestatementInAMarkedPiece_ContinuesTheMarksName()
    {
        const string source = """
            octave absolute
            key d major
            part m { clef treble }
            section S { m { d'1@mark("A") | e'1 | fis'1@mark("B") | key! d major g'1 | a'1 | } }
            form main { ~S }
            score main { staff m }
            """;
        string lys = Import(source);
        Assert.Matches(@"form main \{\s*~A ~B ~B2\s*\}", lys);
        Assert.Contains("key! d major g'1", SectionBody(lys, "B2"));
        Assert.Equal(Drawn(source), Drawn(lys));
    }

    /// <summary>
    /// A restatement on a bar that opens a system (<c>&lt;print new-system="yes"&gt;</c>) is the
    /// courtesy one the line's head prints — a writer copying the printed page (an optical
    /// reader) restates the clef and key on every line — and is dropped as before: a section per
    /// line would be absurd (owner's decision 2026-10-02).
    /// </summary>
    [Fact]
    public void ARestatementOnANewSystem_IsDroppedAndCutsNothing()
    {
        var doc = new MusicXmlExporter().Export(SyntaxTree.Parse("""
            octave absolute
            key g major
            part m { clef treble }
            section S { m { g'1 | a'1 | key! g major clef! treble b'1 | c''1 | } }
            form main { ~S }
            score main { staff m }
            """)).ToXml();
        var third = doc.Descendants().Where(e => e.Name.LocalName == "measure").ElementAt(2);
        third.AddFirst(new System.Xml.Linq.XElement(third.Name.Namespace + "print",
            new System.Xml.Linq.XAttribute("new-system", "yes")));
        var (lys, _) = new MusicXmlImporter().Import(doc.ToString());
        Assert.Matches(@"form main \{\s*~A\s*\}", lys);
        Assert.DoesNotContain("!", SectionBody(lys, "A"));
        Assert.Empty(Drawn(lys));
    }

    /// <summary>
    /// A change of key, time or clef opens a section only in a piece with no rehearsal mark: the
    /// marks are the score's own sections, and a change inside one is often a bar's detour
    /// (Bohemian Rhapsody's one-bar 6/4, 2/4 and 6/8). Either way the page draws what it drew.
    /// </summary>
    [Theory]
    [InlineData("c'1 | time 3/4 c'2. | key d major d'2. | clef bass d2. | time 4/4 key c major clef treble c'1 |",
                @"~A ~A2 ~A3 ~A4 ~A5")]
    [InlineData("c'1@mark(\"A\") | time 3/4 c'2. | key d major d'2. | clef bass d2. | time 4/4 key c major clef treble c'1 |",
                @"~A")]
    public void AChange_OpensASectionOnlyWhereNoMarkDoes(string music, string form)
    {
        string source = "octave absolute\ntime 4/4\nkey c major\npart m { clef treble }\n"
                        + $"section S {{ m {{ {music} }} }}\nform main {{ ~S }}\nscore main {{ staff m }}\n";
        string lys = Import(source);
        Assert.Matches(@"form main \{\s*" + form + @"\s*\}", lys);
        Assert.DoesNotContain("!", lys.Replace("@mark", ""));
        Assert.Equal(Drawn(source), Drawn(lys));
    }
}
