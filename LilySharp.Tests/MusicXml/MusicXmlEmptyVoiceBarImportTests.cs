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
using LilySharp.Core.MusicXmlImport;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.MusicXml;

/// <summary>
/// A bar a voice has nothing in (owner's report 2026-10-27, homr.musicxml: a 6/8 piece's second
/// voice, silent in most bars, came in as <c>r1</c> — too long for the bar, and a rest the page
/// does not draw): a further voice writes an empty bar (a spacer of its bar), the staff's first
/// voice a bar rest <c>R</c>, which lasts its bar in any meter.
/// </summary>
public class MusicXmlEmptyVoiceBarImportTests
{
    private static string Measure(int number, string notes, bool attributes = false) => $"""
        <measure number="{number}">
          {(attributes ? """
          <attributes><divisions>2</divisions>
            <key><fifths>0</fifths></key>
            <time><beats>6</beats><beat-type>8</beat-type></time>
            <clef><sign>G</sign><line>2</line></clef>
          </attributes>
          """ : "")}
          {notes}
        </measure>
        """;

    private static string Note(string step, int octave, int duration, string type, int voice) => $"""
        <note><pitch><step>{step}</step><octave>{octave}</octave></pitch>
        <duration>{duration}</duration><type>{type}</type><dot/><voice>{voice}</voice></note>
        """;

    private static string Import(string measures)
    {
        var report = new ImportReport();
        var doc = MusicXmlReader.Read($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <score-partwise version="4.0">
              <part-list><score-part id="P1"><part-name>M</part-name></score-part></part-list>
              <part id="P1">{measures}</part>
            </score-partwise>
            """, report);
        return LysWriter.Write(doc, report);
    }

    private static Diagnostic[] LengthReports(string lys)
        => [.. SemanticValidation.Run(SyntaxTree.Parse(lys))
            .Where(d => d.Code is DiagnosticCodes.MeasureIncomplete or DiagnosticCodes.MeasureOverflow)];

    [Fact]
    public void ASecondVoiceSilentInABar_WritesAnEmptyBar_NotAWholeRest()
    {
        // Bar 1: two voices of a dotted half each; bar 2: the first voice alone.
        string lys = Import(
            Measure(1, Note("E", 5, 6, "half", 1) + "<backup><duration>6</duration></backup>" + Note("C", 5, 6, "half", 2), attributes: true)
            + Measure(2, Note("D", 5, 6, "half", 1)));
        Assert.DoesNotContain("r1", lys);
        Assert.Matches(@"voice \{ e''2\. \| d''2\. \| \}\s*\{ c''2\. \|\s*\| \}", lys);
        Assert.Empty(LengthReports(lys));
    }

    // Owner's request 2026-10-27: a further voice that stops short of its bar is filled out with
    // spacers (homr: `<cis b,>4.` alone in a bar of 6/8, no <forward> after it).
    [Fact]
    public void ASecondVoiceThatStopsShort_IsFilledOutWithSpacers()
    {
        string lys = Import(
            Measure(1, Note("E", 5, 6, "half", 1) + "<backup><duration>6</duration></backup>"
                + "<note><pitch><step>C</step><octave>5</octave></pitch><duration>3</duration><type>quarter</type><dot/><voice>2</voice></note>",
                attributes: true));
        Assert.Matches(@"\{ c''4\. s4\.", lys);
        Assert.Empty(LengthReports(lys));
    }

    [Fact]
    public void TheOnlyVoiceSilentInABar_WritesABarRest_ThatLastsItsBar()
    {
        string lys = Import(
            Measure(1, Note("E", 5, 6, "half", 1), attributes: true)
            + Measure(2, "")
            + Measure(3, Note("D", 5, 6, "half", 1)));
        Assert.DoesNotContain("r1", lys);
        Assert.Matches(@"e''2\. \| R \| d''2\.", lys);
        Assert.Empty(LengthReports(lys));
    }
}
