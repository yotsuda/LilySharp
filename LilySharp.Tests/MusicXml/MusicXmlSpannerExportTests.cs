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

using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using LilySharp.Core.MusicXml;
using LilySharp.Core.MusicXmlImport;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.MusicXml;

/// <summary>
/// The text spanner (<c>@rit … @!rit</c>, <c>@accel</c>, <c>@rall</c>, <c>@textSpan("…")</c>) and
/// the trill spanner (<c>@trillSpan … @!trillSpan</c>) in the exported MusicXML. Until session
/// 1236 neither reached the document on a note of any size, and <c>lysc xml</c> said nothing:
/// the page drew the word, the dashed line and the wavy line, and the file had bare notes.
/// </summary>
/// <remarks>
/// The spelling: a text spanner is its word and <c>&lt;dashes type="start"/&gt;</c> in one
/// <c>&lt;direction&gt;</c> at its note, and a <c>&lt;dashes type="stop"/&gt;</c> where the note
/// that ends it ends; a trill spanner is <c>&lt;trill-mark/&gt;&lt;wavy-line type="start"/&gt;</c>
/// in its note's <c>&lt;ornaments&gt;</c> and a <c>&lt;wavy-line type="stop"/&gt;</c> on the last
/// trilled note. The pairing is the page's (LYS4018; MeasureCollector.PairTrillSpannerEvents).
/// </remarks>
public class MusicXmlSpannerExportTests
{
    private static XDocument Export(string source)
        => new MusicXmlExporter().Export(SyntaxTree.Parse(source)).ToXml();

    private static string Book(string music)
        => "octave absolute\ntime 4/4\npart m { clef treble }\nsection A { m {\n" + music + "\n} }\n"
           + "form { A }\nscore { staff m }\n";

    /// <summary>Each direction that holds words or dashes, as "words|dashes-type#number+offset".</summary>
    private static List<string> Lines(XDocument doc)
        => doc.Descendants("direction")
            .Where(d => d.Descendants("dashes").Any() || d.Descendants("words").Any())
            .Select(d => string.Concat(d.Descendants("words").Select(w => w.Value)) + "|"
                + string.Concat(d.Descendants("dashes").Select(x => x.Attribute("type")!.Value + "#" + x.Attribute("number")!.Value))
                + "+" + (d.Element("offset")?.Value ?? "0"))
            .ToList();

    /// <summary>Each note's trill elements, in document order: "tr" a trill-mark, "start#N" /
    /// "stop#N" a wavy-line; "" for a note with none.</summary>
    private static List<string> Trills(XDocument doc)
        => doc.Descendants("note")
            .Select(n => string.Join(" ", (n.Element("notations")?.Elements("ornaments").Elements() ?? [])
                .Where(o => o.Name.LocalName is "trill-mark" or "wavy-line")
                .Select(o => o.Name.LocalName == "trill-mark" ? "tr"
                    : o.Attribute("type")!.Value + "#" + o.Attribute("number")!.Value)))
            .ToList();

    private static int Quarter(XDocument doc) => (int)doc.Descendants("note").First().Element("duration")!;

    [Theory]
    [InlineData("@rit", "@!rit", "rit.")]
    [InlineData("@accel", "@!accel", "accel.")]
    [InlineData("@rall", "@!rall", "rall.")]
    [InlineData("@textSpan(\"poco rit.\")", "@!textSpan", "poco rit.")]
    [InlineData("@textSpan", "@!textSpan", "")]
    public void ATextSpanner_IsItsWordAndDashesToTheEndOfItsLastNote(string start, string end, string word)
    {
        var doc = Export(Book($"c''4{start} d'' e'' f''{end} | g''1 |"));
        int quarter = Quarter(doc);
        Assert.Equal([$"{word}|start#1+0", $"|stop#1+{4 * quarter}"], Lines(doc));
        // The word and the line's start are one direction, the word first.
        var first = doc.Descendants("direction").First(d => d.Descendants("dashes").Any());
        Assert.Equal(word.Length > 0 ? ["words", "dashes"] : ["dashes"],
            first.Elements("direction-type").Select(t => t.Elements().Single().Name.LocalName));
    }

    /// <summary>The start stands at its note, the end where its note ends, mid-bar and across a bar.</summary>
    [Fact]
    public void ATextSpanner_StartsAtItsNoteAndEndsAfterItsLast()
    {
        var doc = Export(Book("c''4 d''@rit e'' f'' | g''2 a''@!rit |"));
        int quarter = Quarter(doc);
        Assert.Equal([$"rit.|start#1+{quarter}", $"|stop#1+{4 * quarter}"], Lines(doc));
        Assert.Single(doc.Descendants("measure").Last().Descendants("dashes"));
    }

    /// <summary>A spanner nobody closes draws nothing on the page (LYS4018), an end with nothing
    /// open closes nothing, and a second start in the voice is ignored: the file follows.</summary>
    [Fact]
    public void AnUnpairedTextSpanner_WritesNothing()
    {
        Assert.Empty(Lines(Export(Book("c''4@accel d'' e'' f'' | g''1 |"))));
        Assert.Empty(Lines(Export(Book("c''4 d'' e'' f''@!rit | g''1 |"))));
        var nested = Export(Book("c''4@rit d''@accel e'' f''@!rit | g''1 |"));
        Assert.Equal(["rit.|start#1+0", $"|stop#1+{4 * Quarter(nested)}"], Lines(nested));
    }

    /// <summary>Two voices' spanners overlap in time and are numbered apart; a grand staff's
    /// lower staff counts on from 5, as its voices do.</summary>
    [Fact]
    public void TextSpannersOfTwoVoicesAndTwoStaves_AreNumberedApart()
    {
        var voices = Export(Book("voice { c''4@rit d'' e'' f''@!rit } { c'4 d'@rall e'@!rall f' } | g''1 |"));
        int quarter = Quarter(voices);
        Assert.Equal(["rit.|start#1+0", $"|stop#1+{4 * quarter}", $"rall.|start#2+{quarter}", $"|stop#2+{3 * quarter}"],
            Lines(voices));

        var grand = Export("octave absolute\ntime 4/4\npart rh { instrument piano-right }\npart lh { instrument piano-left }\n"
            + "section A { rh { c''4@rit d'' e'' f''@!rit | } lh { c4@rall d e f@!rall | } }\n"
            + "form { A }\nscore { grandStaff { staff rh staff lh } }\n");
        Assert.Equal(["1", "1", "5", "5"], grand.Descendants("dashes").Select(d => d.Attribute("number")!.Value));
        Assert.Equal(["1", "1", "2", "2"],
            grand.Descendants("direction").Where(d => d.Descendants("dashes").Any()).Select(d => d.Element("staff")!.Value));
    }

    /// <summary>A spanner opened in one section and closed in the next is one line, as on the page.</summary>
    [Fact]
    public void ATextSpannerAcrossASectionsEnd_IsOneLine()
    {
        var doc = Export("octave absolute\ntime 4/4\npart m { clef treble }\n"
            + "section A { m { c''4 d'' e''@rit f'' | } }\nsection B { m { g''2 a''@!rit | } }\n"
            + "form { A B }\nscore { staff m }\n");
        int quarter = Quarter(doc);
        Assert.Equal([$"rit.|start#1+{2 * quarter}", $"|stop#1+{4 * quarter}"], Lines(doc));
    }

    /// <summary>The "tr" and the line's start on the first note, the stop on the last trilled
    /// note — the note the end is written on (owner's decision 2026-10-28).</summary>
    [Fact]
    public void ATrillSpanner_IsItsTrillMarkAndAWavyLineToItsLastNote()
    {
        Assert.Equal(["tr start#1", "", "stop#1", "", ""], Trills(Export(Book("c''4@trillSpan d'' e''@!trillSpan f'' | g''1 |"))));
        // One note long: started and stopped on it.
        Assert.Equal(["tr start#1 stop#1", "", "", "", ""], Trills(Export(Book("c''4@trillSpan@!trillSpan d'' e'' f'' | g''1 |"))));
    }

    /// <summary>A new start ends the trill running, on the new start's note and before its own
    /// "tr"; a trill nobody ends runs to the end of the score; an end with nothing open is nothing.</summary>
    [Fact]
    public void TrillSpanners_ChainAndRunToTheEnd()
    {
        Assert.Equal(["tr start#1", "", "stop#1 tr start#1", "stop#1", ""],
            Trills(Export(Book("c''4@trillSpan d'' e''@trillSpan f''@!trillSpan | g''1 |"))));
        Assert.Equal(["", "tr start#1", "", "", "stop#1"], Trills(Export(Book("c''4 d''@trillSpan e'' f'' | g''1 |"))));
        Assert.Equal(["", "", "", "", ""], Trills(Export(Book("c''4 d'' e''@!trillSpan f'' | g''1 |"))));
    }

    /// <summary><c>.down</c> is the placement of the "tr" and of its line; a <c>@trill</c> beside
    /// the spanner's start is the one trill-mark.</summary>
    [Fact]
    public void ATrillSpannersSideAndItsTrill_AreWrittenOnce()
    {
        var below = Export(Book("c''4@trillSpan.down d''@!trillSpan e'' f'' |"));
        Assert.Equal("below", (string?)below.Descendants("trill-mark").Single().Attribute("placement"));
        Assert.Equal(["below", null], below.Descendants("wavy-line").Select(w => (string?)w.Attribute("placement")));

        var both = Export(Book("c''4@trill@trillSpan d''@!trillSpan e'' f'' |"));
        Assert.Single(both.Descendants("trill-mark"));
        Assert.Single(both.Descendants("note").First().Element("notations")!.Elements("ornaments"));
    }

    /// <summary>Two voices' trills are numbered apart, and the lower staff's count on from 5.</summary>
    [Fact]
    public void TrillSpannersOfTwoVoicesAndTwoStaves_AreNumberedApart()
    {
        var voices = Export(Book("voice { c''1@trillSpan@!trillSpan } { c'1@trillSpan@!trillSpan } |"));
        Assert.Equal(["1", "1", "2", "2"], voices.Descendants("wavy-line").Select(w => w.Attribute("number")!.Value));

        var grand = Export("octave absolute\ntime 4/4\npart rh { instrument piano-right }\npart lh { instrument piano-left }\n"
            + "section A { rh { c''2@trillSpan d''@!trillSpan | } lh { c2@trillSpan d@!trillSpan | } }\n"
            + "form { A }\nscore { grandStaff { staff rh staff lh } }\n");
        Assert.Equal(["1", "1", "5", "5"], grand.Descendants("wavy-line").Select(w => w.Attribute("number")!.Value));
    }

    private static string ComesBack(string music)
    {
        string xml = Export(Book(music)).ToString();
        var (lys, _) = new MusicXmlImporter().Import(xml);
        Assert.Empty(SemanticValidation.Run(SyntaxTree.Parse(lys)).Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message));
        // (The section's label comes back as a mark on its first note.)
        return lys.Replace("\r", "").Replace("@mark(\"A\")", "");
    }

    /// <summary>What is written is read back: the spanner by the name it opened with, its end
    /// on the note that carries it.</summary>
    [Theory]
    [InlineData("c''4@rit d'' e'' f''@!rit | g''1 |", "c''4@rit d''4 e''4 f''4@!rit")]
    [InlineData("c''4 d''@accel e'' f'' | g''2 a''@!accel |", "g''2 a''2@!accel")]
    [InlineData("c''4@textSpan(\"poco rit.\") d'' e'' f''@!textSpan | g''1 |", "c''4@textSpan(\"poco rit.\") d''4 e''4 f''4@!textSpan")]
    [InlineData("c''4@textSpan d'' e'' f''@!textSpan | g''1 |", "c''4@textSpan d''4 e''4 f''4@!textSpan")]
    [InlineData("c''4@trillSpan d'' e''@!trillSpan f'' | g''1 |", "c''4@trillSpan d''4 e''4@!trillSpan f''4")]
    [InlineData("c''4@trillSpan d'' e''@trillSpan f''@!trillSpan | g''1 |", "c''4@trillSpan d''4 e''4@trillSpan f''4@!trillSpan")]
    [InlineData("c''4@trillSpan.down d''@!trillSpan e'' f'' | g''1 |", "c''4@trillSpan.down d''4@!trillSpan")]
    [InlineData("c''4@trillSpan@!trillSpan d'' e'' f'' | g''1 |", "c''4@trillSpan@!trillSpan d''4")]
    public void TheSpanners_ComeBackFromTheFile(string music, string comesBack)
        => Assert.Contains(comesBack, ComesBack(music));

    /// <summary>The words of a dashed line are the spanner's and no text beside it, and a
    /// trill's "tr" is the spanner's and no <c>@trill</c> beside it.</summary>
    [Fact]
    public void TheSpannersWordAndTrillMark_AreReadOnce()
    {
        Assert.DoesNotContain("@text", ComesBack("c''4@rit d'' e'' f''@!rit | g''1 |"));
        Assert.DoesNotContain("@trill ", ComesBack("c''4@trillSpan d'' e''@!trillSpan f'' | g''1 |").Replace("@trillSpan", "@span"));
    }

    private static string Foreign(string measure)
    {
        var (lys, _) = new MusicXmlImporter().Import(
            "<score-partwise version=\"4.0\"><part-list><score-part id=\"P1\"><part-name>P</part-name></score-part></part-list>"
            + "<part id=\"P1\"><measure number=\"1\"><attributes><divisions>1</divisions><time><beats>4</beats><beat-type>4</beat-type></time>"
            + "<clef><sign>G</sign><line>2</line></clef></attributes>" + measure + "</measure></part></score-partwise>");
        Assert.Empty(SemanticValidation.Run(SyntaxTree.Parse(lys)).Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message));
        return lys.Replace("\r", "");
    }

    private static string Note(string step, string notations = "")
        => $"<note><pitch><step>{step}</step><octave>4</octave></pitch><duration>1</duration><voice>1</voice><type>quarter</type>"
            + (notations.Length > 0 ? $"<notations><ornaments>{notations}</ornaments></notations>" : "") + "</note>";

    /// <summary>A dashed line another program starts and never stops stays the words on the page:
    /// a Lily# spanner with no end is an error and draws nothing (LYS4018).</summary>
    [Fact]
    public void ADashedLineNeverStopped_StaysItsWords()
    {
        string lys = Foreign("<direction placement=\"above\"><direction-type><words>rit.</words></direction-type>"
            + "<direction-type><dashes type=\"start\" number=\"1\"/></direction-type></direction>"
            + Note("C") + Note("D") + Note("E") + Note("F"));
        Assert.Contains("c'4@text(\"rit.\").up d'4", lys);
        Assert.DoesNotContain("@rit", lys);
    }

    /// <summary>A wavy line started and stopped on one note with no trill-mark is a vibrato's
    /// wave (Lily# writes a fretted part's so), and is no trill spanner.</summary>
    [Fact]
    public void AOneNoteWaveWithNoTrillMark_IsNoTrillSpanner()
    {
        string lys = Foreign(Note("C", "<wavy-line type=\"start\" number=\"1\"/><wavy-line type=\"stop\" number=\"1\"/>")
            + Note("D") + Note("E") + Note("F"));
        Assert.DoesNotContain("trillSpan", lys);
    }
}
