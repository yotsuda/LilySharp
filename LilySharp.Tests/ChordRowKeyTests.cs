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
/// A chords track's <c>key</c> at a bar's head (owner's decision 2026-11-16) is THAT TRACK'S alone,
/// as a part's <c>key</c> is that part's: the key its degrees are read in from that bar to the run's
/// end. The chart prints "Key: D" over the bar; a pitched part of the section that changes key
/// nowhere is told (LYS of <see cref="DiagnosticCodes.KeyChangeInSomeParts"/>). And a rows-only
/// chart reads its degrees in each section header's key — until 2026-11-16 it read the file's.
/// </summary>
public class ChordRowKeyTests
{
    private const string Chart = "time 4/4\nkey c major\n"
        + "section A {\n  chords harmony { I | V | key d major I | V | }\n}\n"
        + "section B {\n  key d major\n  chords harmony { I | V | }\n}\n"
        + "section C {\n  chords harmony { I | V | }\n}\n"
        + "form { A B C }\nscore { chords harmony }\n";

    private static MultiStaffScore Collect(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
    }

    [Fact]
    public void TheDegrees_AreReadInTheRowsKey_ThenTheHeadersKey_ThenTheHomeKey()
    {
        Assert.DoesNotContain(SyntaxTree.Parse(Chart).Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(new[] { "C", "G", "D", "A", "D", "A", "C", "G" }, Collect(Chart).ChordNames.Select(c => c.ChordText));
    }

    [Fact]
    public void TheChart_SaysTheKeyOverTheBar_OnceEachChange()
    {
        var words = Collect(Chart).MusicMarks.Where(m => m.Type == MusicMarkType.KeyName)
            .OrderBy(m => m.MeasureIndex).Select(m => (m.MeasureIndex, m.Text)).ToArray();
        Assert.Equal(new[] { (0, "Key: C"), (2, "Key: D"), (6, "Key: C") }, words);
    }

    [Fact]
    public void TheMidi_TheMusicXml_AndTheTwin_ReadTheSameKeys()
    {
        var roots = new MidiExporter().Export(SyntaxTree.Parse(Chart)).Tracks
            .Where(t => t.Name == "harmony (chords)").SelectMany(t => t.Notes)
            .GroupBy(n => n.StartTick).OrderBy(g => g.Key).Select(g => g.Min(n => n.Pitch) % 12).ToArray();
        // The lowest tone of each window voicing: C(G) G(G) D(A) A(A) D(A) A(A) C(G) G(G) — read as pitch classes.
        Assert.Equal(new[] { 7, 7, 9, 9, 9, 9, 7, 7 }, roots);

        var xml = new MusicXmlExporter().Export(SyntaxTree.Parse(Chart)).ToXml();
        Assert.Equal("CGDADACG", string.Concat(xml.Descendants("root-step").Select(r => r.Value)));

        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(Chart)).Replace("\r\n", "\n");
        Assert.Contains("c1 |\n  g1 |\n  d1 |\n  a1 |", ly);
    }

    [Fact]
    public void AKeyAfterAChordOfItsBar_IsTheSameError()
    {
        var tree = SyntaxTree.Parse(Chart.Replace("| key d major I |", "| I key d major |"));
        var error = Assert.Single(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(DiagnosticCodes.ChordsTimeInsideBar, error.Code);
        Assert.Equal("key", tree.Text.Substring(error.Span.Start, error.Span.Length));
    }

    [Theory]
    [InlineData("c''1 | d''1 | e''1 |", true)]
    [InlineData("c''1 | d''1 | key d major e''1 |", false)]
    public void APitchedPartThatDoesNotChangeKey_IsTold(string melody, bool told)
    {
        string book = "time 4/4\nkey c major\noctave absolute\npart mel { }\nsection A {\n"
            + $"  mel {{ {melody} }}\n  chords harmony {{ I | V | key d major I | }}\n}}\n"
            + "form { A }\nscore { chords harmony\n staff mel }\n";
        var v = new MeasureValidator();
        v.Validate(SyntaxTree.Parse(book));
        var warnings = v.Diagnostics.Where(d => d.Code == DiagnosticCodes.KeyChangeInSomeParts).ToList();
        if (told)
            Assert.Contains("Chords 'harmony' changes key here, and 'mel'", Assert.Single(warnings).Message);
        else
            Assert.Empty(warnings);
    }
}
