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
using LilySharp.Core.LilyPond;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A chord row's bar is gridded on THAT bar's meter by every reader — the page
/// (<c>ChordNameCollector.MeterAt</c>), the MIDI, the MusicXML, and the LilyPond twin
/// (<c>LilyPondExporter.ChordBarText</c>). Until 2026-11-17 the twin gridded every bar on the
/// SCORE's meter and scaled it to the bar's length: in a 4/4 score, a 6/8 section's
/// dotted-quarter pickup <c>C G</c> went out as <c>c8 g4</c> (the page: 3/16 each), and a 5/8
/// bar's <c>C G</c> as 5/16 each (the page: 3/8 then 1/4, its beats 3+2). Lab sessions/p1290/rowmeter.
/// </summary>
[Trait("Category", "Unit")]
public class ChordRowOwnMeterTwinTests
{
    private const string SixEightPickup = """
        time 4/4
        part melody
        section P { time 6/8 partial 4. melody { c''8 d'' e'' | } chords harmony { C G | } }
        section A { time 6/8 melody { c''4. d''4. | } chords harmony { F G | } }
        form { P A }
        score { staff melody chords harmony }
        """;

    private const string FiveEight = """
        time 4/4
        part melody
        section A { time 5/8 melody { c''4. d''4 | } chords harmony { C G | } }
        form { A }
        score { staff melody chords harmony }
        """;

    /// <summary>The chord names the page places in bar <paramref name="measure"/>, with where.</summary>
    private static (string Text, double Timing)[] PageChords(string src, int measure)
    {
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, src);
        return LilySharp.Core.Svg.SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree))
            .ChordNames.Where(c => c.MeasureIndex == measure).OrderBy(c => c.Timing.ToDouble())
            .Select(c => (c.ChordText, c.Timing.ToDouble())).ToArray();
    }

    /// <summary>The bars of the twin's chord track, as written.</summary>
    private static string[] TwinBars(string src)
    {
        var ly = new LilyPondExporter().Export(SyntaxTree.Parse(src));
        var body = Regex.Match(ly, @"harmonyChords = \\chordmode \{([^}]*)\}").Groups[1].Value;
        return body.Split('|').Select(b => Regex.Replace(b.Trim(), @"\s+", " ")).Where(b => b.Length > 0).ToArray();
    }

    [Fact]
    public void ASixEightPickup_IsGriddedOnItsOwnBeat_OnThePageAndInTheTwin()
    {
        // One dotted-quarter beat, two chords: a half beat each.
        Assert.Equal([("C", 0.0), ("G", 3.0 / 16)], PageChords(SixEightPickup, 0));
        Assert.Equal("c8. g8.", TwinBars(SixEightPickup)[0]);
        Assert.Equal("f4. g4.", TwinBars(SixEightPickup)[1]);
    }

    [Fact]
    public void AFiveEightBar_IsGriddedOnItsThreePlusTwo_OnThePageAndInTheTwin()
    {
        Assert.Equal([("C", 0.0), ("G", 3.0 / 8)], PageChords(FiveEight, 0));
        Assert.Equal("c4. g4", TwinBars(FiveEight)[0]);
    }
}
