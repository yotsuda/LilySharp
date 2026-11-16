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
using LilySharp.Core;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A chords track's <c>%</c> (owner's decision 2026-11-16): the bar before it again, a bar of its
/// own — <c>| G C | % | % | D |</c> is G C three times. The page prints the sign in the bar and no
/// names; the MIDI sounds the chords again; the MusicXML and the LilyPond twin write them again (a
/// ChordNames context has no percent engraver). One bar at a time: <c>%N</c> is a part's.
/// </summary>
public class ChordRowPercentTests
{
    private static string RowsOnly(string row = "G C | % | % | D |") =>
        $"time 4/4\nsection A {{\n  chords harmony {{ {row} }}\n}}\nform {{ A }}\nscore {{ chords harmony }}\n";

    private const string Mixed = "time 4/4\noctave absolute\npart mel { }\nsection A {\n"
        + "  chords harmony { G C | % | % | D | }\n  mel { g'1 | a'1 | b'1 | c''1 | }\n}\n"
        + "form { A }\nscore { chords harmony\n staff mel }\n";

    private static IReadOnlyList<Diagnostic> Errors(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var v = new PercentRepeatValidator();
        v.Validate(tree);
        return tree.Diagnostics.Concat(v.Diagnostics).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
    }

    [Theory]
    [InlineData("G C | % | % | D |")]
    [InlineData("G | % |")]                 // at the row's end, closed by the end
    [InlineData("G |: % :| D |")]
    public void APercentInItsOwnBar_AfterABar_IsRead(string row)
    {
        Assert.Empty(Errors(RowsOnly(row)));
        var tree = SyntaxTree.Parse(RowsOnly(row));
        Assert.Equal(tree.Text, tree.GetRoot().ToFullString());
    }

    [Theory]
    [InlineData("% | G |", "none before it")]
    [InlineData("G % | C |", "the only thing in its bar")]
    [InlineData("G | % C |", "the only thing in its bar")]
    [InlineData("G | C | %2 |", "one bar at a time")]
    public void AMisplacedPercent_IsAnError_ThatSaysWhy(string row, string says)
    {
        var error = Assert.Single(Errors(RowsOnly(row)));
        Assert.Equal(DiagnosticCodes.PercentMarkUngrouped, error.Code);
        Assert.Contains(says, error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThePage_PrintsTheSign_AndNoNames(bool withAPart)
    {
        var tree = SyntaxTree.Parse(withAPart ? Mixed : RowsOnly());
        var score = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        Assert.Equal(new[] { 1, 2 }, score.PercentRepeats.Select(p => p.MeasureIndex).OrderBy(i => i));
        Assert.Equal(new[] { "G", "C", "D" }, score.ChordNames.Select(c => c.ChordText));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheMidi_SoundsTheBarAgain(bool withAPart)
    {
        var midi = new MidiExporter().Export(SyntaxTree.Parse(withAPart ? Mixed : RowsOnly()));
        var starts = midi.Tracks.Where(t => t.Name == "harmony (chords)").SelectMany(t => t.Notes)
            .Select(n => n.StartTick).Distinct().OrderBy(t => t).ToArray();
        Assert.Equal(new[] { 0, 2, 4, 6, 8, 10, 12 }.Select(b => b * 480), starts);
    }

    [Fact]
    public void TheMusicXmlAndTheTwin_WriteTheChordsAgain()
    {
        var xml = new MusicXmlExporter().Export(SyntaxTree.Parse(RowsOnly())).ToXml();
        var roots = xml.Descendants("measure")
            .Select(m => string.Concat(m.Descendants("root-step").Select(r => r.Value))).ToArray();
        Assert.Equal(new[] { "GC", "GC", "GC", "D" }, roots);

        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(RowsOnly()));
        Assert.Contains("g2 c2 |\n  g2 c2 |\n  g2 c2 |\n  d1 |", ly.Replace("\r\n", "\n"));
    }
}
