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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.MusicXml;

/// <summary>
/// MusicXML's <c>&lt;cue/&gt;</c> read back into Lily#'s cue REGION.
/// </summary>
/// <remarks>
/// ⚠️ THE TWO FORMATS DISAGREE ON THE UNIT: MusicXML marks each note, Lily# (like LilyPond)
/// has a region. The importer groups each maximal run of consecutive cue notes into one
/// <c>cue { … }</c>, which is the only grouping that can round-trip — a region per note
/// would forbid a beam inside a cue, since a cue region is a voice of its own and a beam
/// cannot cross it (MEASURED, audit/lp-geometry/probes/cue-span.ly book B-BEAM).
/// <para>
/// Until 2026-08-02 a cue note was DROPPED on import ("cue note dropped."), so nothing here
/// regresses anything: the notes were not in the output at all.
/// </para>
/// </remarks>
public class MusicXmlCueImportTests
{
    /// <summary>One measure of 4/4 with the given note elements.</summary>
    private static string Xml(string notes) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <score-partwise version="4.0">
          <part-list><score-part id="P1"><part-name>M</part-name></score-part></part-list>
          <part id="P1"><measure number="1">
            <attributes><divisions>1</divisions>
              <key><fifths>0</fifths></key>
              <time><beats>4</beats><beat-type>4</beat-type></time>
              <clef><sign>G</sign><line>2</line></clef>
            </attributes>
            {notes}
          </measure></part>
        </score-partwise>
        """;

    private static string Note(string step, int octave, bool cue = false) => $"""
        <note>{(cue ? "<cue/>" : "")}<pitch><step>{step}</step><octave>{octave}</octave></pitch>
        <duration>1</duration><type>quarter</type></note>
        """;

    private static string Import(string notes)
    {
        var report = new ImportReport();
        var doc = MusicXmlReader.Read(Xml(notes), report);
        return LysWriter.Write(doc, report);
    }

    [Fact]
    public void ConsecutiveCueNotesBecomeOneRegion()
    {
        string lys = Import(Note("C", 4) + Note("D", 4)
                          + Note("E", 4, cue: true) + Note("F", 4, cue: true));
        Assert.Contains("cue {", lys);
        // ONE region, not one per note — that is the whole point of the grouping.
        Assert.Equal(1, lys.Split("cue {").Length - 1);
    }

    /// <summary>
    /// A gap in the run closes the region and a new one opens: two runs, two regions.
    /// </summary>
    [Fact]
    public void ARunEndsWhereTheCueNotesEnd()
    {
        string lys = Import(Note("C", 4, cue: true) + Note("D", 4)
                          + Note("E", 4, cue: true) + Note("F", 4));
        Assert.Equal(2, lys.Split("cue {").Length - 1);
    }

    /// <summary>
    /// The important one: what the importer writes has to PARSE, and has to collect back to
    /// cue items. A grouping that emitted crossing braces would look right in a diff.
    /// </summary>
    [Fact]
    public void WhatIsWrittenParsesAndCollectsAsCue()
    {
        string lys = Import(Note("C", 4) + Note("D", 4)
                          + Note("E", 4, cue: true) + Note("F", 4, cue: true));
        var tree = SyntaxTree.Parse(lys);
        Assert.False(tree.HasErrors,
            "the importer wrote a cue region that does not parse:\n" + lys);

        var score = new MeasureCollector().Collect(tree, null);
        var notes = score.Voice.Measures.SelectMany(m => m.Items).OfType<NoteItem>().ToList();
        Assert.Equal(4, notes.Count);
        Assert.False(notes[0].IsCue);
        Assert.False(notes[1].IsCue);
        Assert.True(notes[2].IsCue);
        Assert.True(notes[3].IsCue);
    }

    /// <summary>
    /// A run with no note after it still has to be CLOSED — the loop's exit is the only place
    /// that can do it, and an unclosed brace would be a parse error in the imported file.
    /// </summary>
    [Fact]
    public void ACueThatRunsToTheEndOfTheMeasureIsClosed()
    {
        string lys = Import(Note("C", 4, cue: true) + Note("D", 4, cue: true)
                          + Note("E", 4, cue: true) + Note("F", 4, cue: true));
        Assert.False(SyntaxTree.Parse(lys).HasErrors,
            "the importer left a cue region unclosed:\n" + lys);
        Assert.Equal(lys.Count(c => c == '{'), lys.Count(c => c == '}'));
    }

    [Fact]
    public void ACueNoteIsNoLongerDropped()
    {
        var report = new ImportReport();
        var doc = MusicXmlReader.Read(Xml(Note("C", 4) + Note("E", 4, cue: true)), report);
        LysWriter.Write(doc, report);
        Assert.DoesNotContain(report.Warnings, m => m.Contains("cue note dropped"));
    }
}
