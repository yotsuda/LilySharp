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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// In a chords row, an ending's <c>]</c> with no bar line after it closes the bar it stands in,
/// as a bar line would — <c>| A | [1. A7] [2. ]</c> is A, A7 and an empty bar, one bracket over
/// each — and <c>[2. ]</c> holding nothing is one empty bar. <c>[2. F ] |</c> is the bar line's,
/// as it always was (<c>ChordRowBarLines.EndCloses</c>).
/// </summary>
/// <remarks>
/// Until 2026-11-15 a row's <c>]</c> closed nothing: <c>[1. A7] [2. ]</c> stacked both brackets
/// over A7's bar and the second ending had no bar at all (owner's report).
/// </remarks>
public class ChordRowEndingBarTests
{
    private static SyntaxTree Parse(string row)
        => SyntaxTree.Parse("octave absolute\ntime 4/4\nchords prog { " + row + " }\nscore { chords prog }\n");

    private static (int From, int To, string Text)[] Brackets(string row)
    {
        var tree = Parse(row);
        var score = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        return [.. score.VoltaBrackets.OrderBy(v => v.StartMeasureIndex).Select(v => (v.StartMeasureIndex, v.EndMeasureIndex, v.VoltaText))];
    }

    private static int PageBars(string row)
    {
        var tree = Parse(row);
        return new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!)
            .StaffGroups.SelectMany(g => g.Staves).SelectMany(s => s.Voices).Max(v => v.Measures.Length);
    }

    [Theory]
    [InlineData("C | A | [1. A7] [2. ]")]
    [InlineData("|: C | A | [1. A7 :| [2. ]")]
    [InlineData("C | A | [1. A7] [2. ] break")]
    [InlineData("C | A | [1. A7 | ] [2. | ]")]       // the bar lines written: the same bars
    [InlineData("C | A | [1. A7 ] | [2. ] |")]       // …and after the brackets
    public void AnEndingsBracketClosesItsBar_WhenNoBarLineFollows(string row)
    {
        Assert.Equal([(2, 2, "1."), (3, 3, "2.")], Brackets(row));
        Assert.Equal(4, PageBars(row));
    }

    [Fact]
    public void TwoChordsInOneEnding_StayInItsOneBar()
    {
        Assert.Equal([(1, 1, "1."), (2, 2, "2.")], Brackets("C | [1. A7 D7] [2. G]"));
        Assert.Equal(3, PageBars("C | [1. A7 D7] [2. G]"));
    }

    /// <summary>The sound takes the same bars: the second ending's chord starts a bar of its own.</summary>
    [Fact]
    public void TheSecondEndingsChord_SoundsInABarOfItsOwn()
    {
        var tree = Parse("C | A | [1. A7] [2. D]");
        var spec = RenderSpecParser.FindFirst(tree);
        var midi = new MidiExporter { Form = spec?.Form, Score = spec }.Export(tree);
        var onsets = midi.Tracks.SelectMany(t => t.Notes).Select(n => n.StartTick / (midi.TicksPerQuarterNote * 4)).Distinct().OrderBy(b => b);
        Assert.Equal([0, 1, 2, 3], onsets);
    }

    private const string Melody = "octave absolute\ntime 4/4\npart m { section S { c''1 | d''1 | } section T { e''1 | f''1 | } }\n";

    /// <summary>A section a row alone lays out is as long as the row's bars: the next one opens after the empty ending.</summary>
    [Fact]
    public void ARowOnlySection_CountsTheEmptyEndingsBar()
    {
        var tree = SyntaxTree.Parse("octave absolute\ntime 4/4\nchords prog { section S { C | A | [1. A7] [2. ] } section T { G | } }\n"
            + "form { S T }\nscore { chords prog }\n");
        var voices = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!)
            .StaffGroups.SelectMany(g => g.Staves).SelectMany(s => s.Voices).ToList();
        Assert.Equal(5, voices.Max(v => v.Measures.Length));
    }

    /// <summary>A break written after the brackets breaks after the bar they closed.</summary>
    [Fact]
    public void ABreakAfterTheBracket_BreaksAfterItsBar()
    {
        var tree = Parse("C | A | [1. A7] [2. D] break G |");
        var voices = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!)
            .StaffGroups.SelectMany(g => g.Staves).SelectMany(s => s.Voices).ToList();
        Assert.Contains(voices, v => v.Measures.Length > 4 && v.Measures[3].HasBreakAfter);
    }

    /// <summary>A row written flat over sections of music sounds the same bars.</summary>
    [Fact]
    public void AFlatRow_SoundsTheSecondEndingInItsOwnBar()
    {
        var tree = SyntaxTree.Parse(Melody + "chords prog { C | A | [1. A7] [2. D] }\nform { S T }\nscore { chords prog staff m }\n");
        var spec = RenderSpecParser.FindFirst(tree);
        var midi = new MidiExporter { Form = spec?.Form, Score = spec }.Export(tree);
        var bar = midi.TicksPerQuarterNote * 4;
        var d = midi.Tracks.SelectMany(t => t.Notes).Where(n => n.Part != "m" && n.Pitch % 12 == 2).Select(n => n.StartTick / bar).Distinct();
        Assert.Equal([3], d);
    }

    /// <summary>The LilyPond twin writes the second ending's chord as a bar of its own.</summary>
    [Fact]
    public void TheTwin_WritesTheSecondEndingsBar()
    {
        string ly = new LilySharp.Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(Melody
            + "chords prog { section S { C | A } section T { [1. A7] [2. D] } }\nform { S T }\nscore { chords prog staff m }\n"));
        Assert.Matches(@"a1:7\s+d1", ly);
    }

    /// <summary>A part and a row that write the same signs agree bar line by bar line (LYS1070
    /// and the ending's check count a row's bars the one way).</summary>
    [Fact]
    public void ARowsBracketedBars_AgreeWithAPartsSigns()
    {
        var tree = SyntaxTree.Parse("octave absolute\ntime 4/4\n"
            + "part m { section S { c''1 | d''1 | } section T { |: e''1 | [1. f''1 :| [2. g''1 ] |: a''1 :| } }\n"
            + "chords prog { section S { C | A | } section T { |: Em | [1. A7 :| [2. D] |: G :| } }\n"
            + "form { S T }\nscore { chords prog staff m }\n");
        Assert.Empty(LilySharp.Core.Semantics.SemanticValidation.Run(tree).Select(d => d.Message));
    }

    /// <summary>A row over a staff puts the second ending's chord over the staff's fourth bar.</summary>
    [Theory]
    [InlineData("chords prog staff m")]
    [InlineData("staff m chords prog")]
    public void ARowOverAStaff_PutsTheSecondEndingsChordOverItsBar(string score)
    {
        var tree = SyntaxTree.Parse(Melody + "chords prog { section S { C | A | } section T { [1. A7] [2. D] } }\n"
            + "form { S T }\nscore { " + score + " }\n");
        var names = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!).ChordNames;
        Assert.Equal([0, 1, 2, 3], names.OrderBy(c => c.MeasureIndex).Select(c => c.MeasureIndex));
    }

    /// <summary>A row written flat takes its bars section by section — the bars the brackets close too.</summary>
    [Fact]
    public void AFlatRow_TakesTheBracketedBarsSectionBySection()
    {
        // T is in 3/4: its bars are B's and A flat's, each three beats — S takes F and A7 only.
        var tree = SyntaxTree.Parse("octave absolute\ntime 4/4\npart m { section S { c''1 | d''1 | } section T { time 3/4 e''2. | f''2. | } }\n"
            + "chords prog { F | [1. A7] [2. B] Ab | }\nform { S T }\nscore { chords prog staff m }\n");
        var spec = RenderSpecParser.FindFirst(tree);
        var midi = new MidiExporter { Form = spec?.Form, Score = spec }.Export(tree);
        int q = midi.TicksPerQuarterNote;
        int Onset(int root) => midi.Tracks.SelectMany(t => t.Notes).Where(n => n.Part != "m" && n.Pitch % 12 == root).Min(n => n.StartTick);
        Assert.Equal(8 * q, Onset(11));      // B opens section T
        Assert.Equal(11 * q, Onset(8));      // A flat three beats on
    }

    /// <summary>MusicXML writes the bars the page draws, each ending over its own.</summary>
    [Fact]
    public void MusicXml_WritesTheEmptyEndingsBar()
    {
        var xml = new MusicXmlExporter().Export(SyntaxTree.Parse("octave absolute\ntime 4/4\n"
            + "part m { section S { c''1 | d''1 | e''1 | f''1 | } }\nchords prog { section S { C | A | [1. A7] [2. ] } }\n"
            + "form { S }\nscore { chords prog staff m }\n")).ToXml();
        var measures = xml.Descendants("part").First().Elements("measure").ToList();
        Assert.Equal(4, measures.Count);
        Assert.Equal(["1", "2"], measures.SelectMany(m => m.Descendants("ending"))
            .Where(e => e.Attribute("type")?.Value == "start").Select(e => e.Attribute("number")!.Value));
    }
}
