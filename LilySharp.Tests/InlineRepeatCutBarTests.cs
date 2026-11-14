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
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A bar a sign written in the music stands INSIDE is one bar for every reader
/// (<see cref="SignCutBars"/>, docs/inline-repeat-design.md §7 stage 4, "the rest, 2"):
/// <c>|: g4 | c d e f | g a b :| g4 | c1 |</c>, the repeat that takes its pickup with it. The
/// check calls neither half short, the MIDI fills neither out, the page and the MusicXML give
/// the two halves one bar number. MEASURED before (2026-11-15, Lab sessions/p1247/probe/m-a-pickup
/// against m-c-formtwin, the form's spelling of the same music): four LYS2001, a beat of
/// silence after the third beat and three after the lone one, five bars for four.
/// </summary>
/// <remarks>⚠️ These books still report LYS1034 (the design lifts the rule last).</remarks>
[Trait("Category", "Unit")]
public sealed class InlineRepeatCutBarTests
{
    private const string Cut = "|: g'4 | c''4 d'' e'' f'' | g''4 a'' b'' :| g''4 | c'''1 |";
    private const string CutBass = "r4 | c1 | g2. | r4 | c1 |";
    // The same bars with a plain line where the sign stood: two short bars, as they always were.
    private const string Plain = "g'4 | c''4 d'' e'' f'' | g''4 a'' b'' | g''4 | c'''1 |";

    private static string Book(string mel, string bas = CutBass) => $$"""
        octave absolute
        time 4/4
        section A { partial 4 }
        part mel { section A { {{mel}} } }
        part bas { clef bass section A { {{bas}} } }
        form { A }
        score { staff mel staff bas }
        """;

    private static int ShortBars(string src) =>
        SemanticValidation.Run(SyntaxTree.Parse(src)).Count(d => d.Code == DiagnosticCodes.MeasureIncomplete);

    /// <summary>The quarter each of the melody's notes starts on.</summary>
    private static string Onsets(string src)
    {
        var midi = new MidiExporter().Export(SyntaxTree.Parse(src));
        return string.Join(" ", midi.Tracks.SelectMany(t => t.Notes).Where(n => n.Part == "mel")
            .OrderBy(n => n.StartTick).Select(n => n.StartTick / midi.TicksPerQuarterNote));
    }

    /// <summary>The first part's measure numbers, an implicit one marked: "0i 1 2 2i 3".</summary>
    private static string Numbers(string src) => string.Join(" ",
        new MusicXmlExporter().Export(SyntaxTree.Parse(src)).ToXml().Descendants("part").First().Elements("measure")
            .Select(m => m.Attribute("number")!.Value + (m.Attribute("implicit") != null ? "i" : "")));

    [Fact]
    public void TheCheckCallsNeitherHalfShort()
    {
        Assert.Equal(0, ShortBars(Book(Cut)));
        Assert.Equal(4, ShortBars(Book(Plain)));
    }

    /// <summary>The sign may be another part's: the bar is the score's.</summary>
    [Fact]
    public void TheSignMayBeAnotherParts()
        => Assert.Equal(0, ShortBars(Book(Plain, "|: r4 | c1 | g2. :| r4 | c1 |")));

    [Fact]
    public void TheMidiFillsNeitherHalfOut()
    {
        // The pickup, seven beats, the pickup again on the eighth, and the lone beat on the sixteenth.
        Assert.Equal("0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17", Onsets(Book(Cut)));
        // The control: with a plain line each short bar is filled out to the meter.
        Assert.Equal("0 1 2 3 4 5 6 7 9 13", Onsets(Book(Plain)));
    }

    [Fact]
    public void ThePageCountsTheTwoHalvesAsOneBar()
    {
        var measures = new MeasureCollector().Collect(SyntaxTree.Parse(Book(Cut)), "mel").Voices[0].Measures;
        Assert.Equal([false, false, false, true, false], measures.Select(m => m.ContinuesBar));
        var plain = new MeasureCollector().Collect(SyntaxTree.Parse(Book(Plain)), "mel").Voices[0].Measures;
        Assert.DoesNotContain(plain, m => m.ContinuesBar);
    }

    /// <summary>The twin: a voice that takes the sign from another voice wrote its own plain
    /// line there, and a bar check half way through a bar fails in LilyPond ("bar check failed
    /// at: 3/4", Lab sessions/p1247/probe/m-a-pickup) — the line is written as nothing.</summary>
    [Fact]
    public void TheTwinWritesNoBarCheckBetweenTheHalves()
    {
        string ly = new LilySharp.Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(Book(Cut)));
        var bas = System.Text.RegularExpressions.Regex.Match(ly, @"(?ms)^bas = [^{]*\{\n(.*?)^\}");
        Assert.True(bas.Success, ly);
        Assert.Contains(@"\repeat volta 2 { r4 | c1 | g2. } r4 | c1 |",
            System.Text.RegularExpressions.Regex.Replace(bas.Groups[1].Value, @"\s+", " "));
    }

    [Fact]
    public void TheMusicXmlNumbersTheSecondHalfWithTheFirst()
    {
        Assert.Equal("0i 1 2 2i 3", Numbers(Book(Cut)));
        Assert.Equal("0i 1 2 3 4", Numbers(Book(Plain)));
    }
}
