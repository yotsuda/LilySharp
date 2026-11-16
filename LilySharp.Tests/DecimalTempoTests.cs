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
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A tempo's count may have a fraction — a tempo read off a recording (owner's decision
/// 2026-11-16): `tempo 4 = 131.8`. The sound keeps the fraction (MIDI, MusicXML's sound and
/// metronome); the page prints the count rounded after a wave, "♩ ≈ 132", as a metronome mark
/// is printed in whole beats; the LilyPond twin writes the whole count. A fraction anywhere else
/// in the run — a beat unit, a swung value — is still refused (LYS0022).
/// </summary>
[Trait("Category", "Unit")]
public class DecimalTempoTests
{
    private const string Book = """
        title "T"
        tempo 4 = 131.8
        time 4/4
        part m { }
        section A {
          m { c'4 d' e' f' | g'1 | }
        }
        section B {
          tempo "Slower" 4 = 90.5
          m { e'4 d' c' d' | c'1 | }
        }
        form { A B }
        score { staff m }
        """;

    private static TempoValue Value(string run)
        => SyntaxTree.Parse($"tempo {run}\n").GetRoot().DescendantNodes().OfType<TempoDeclarationSyntax>().First().Value;

    [Theory]
    [InlineData("4 = 131.8", 132, 131.8)]
    [InlineData("131.8", 132, 131.8)]
    [InlineData("\"Andante\" 4 = 90.5", 91, 90.5)]
    [InlineData("4 = 120", 120, null)]
    public void TheCount_KeepsItsFraction_AndRoundsForTheWholeBeatReaders(string run, int bpm, double? exact)
    {
        var v = Value(run);
        Assert.Equal(bpm, v.Bpm);
        Assert.Equal(exact, v.BpmExact);
        Assert.Equal(exact is not null, v.IsApproximate);
        Assert.Equal(exact ?? bpm, v.QuarterBpm!.Value, 9);
    }

    [Theory]
    [InlineData("tempo 4 = 131.8", false)]
    [InlineData("tempo 131.8", false)]
    [InlineData("tempo 4.5 = 116", true)]        // a beat unit is a note value
    [InlineData("tempo 120 swing 8.5", true)]    // a swung value is 8 or 16
    public void AFraction_IsTakenOnlyAsTheCount(string line, bool refused)
    {
        var tree = SyntaxTree.Parse(line + "\npart m { }\nsection A { m { c'1 | } }\nform { ~A }\nscore { staff m }\n");
        Assert.Equal(refused, tree.Diagnostics.Any(d => d.Code == DiagnosticCodes.FractionalTempoValue));
    }

    [Fact]
    public void ThePage_PrintsTheCountRounded_AfterAWave()
    {
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(Book), SvgRenderOptions.Default);
        Assert.Contains(">≈ 132<", svg);
        Assert.Contains(">≈ 91)<", svg);
        Assert.DoesNotContain(">= 132<", svg);
        // A whole count keeps its "=".
        Assert.Contains(">= 120<", SvgGenerator.Generate(SyntaxTree.Parse(Book.Replace("131.8", "120")), SvgRenderOptions.Default));
    }

    [Fact]
    public void TheSound_KeepsTheFraction()
    {
        var tempos = new MidiExporter().Export(SyntaxTree.Parse(Book)).Tracks.SelectMany(t => t.TempoChanges).OrderBy(t => t.Tick).ToList();
        Assert.Contains(tempos, t => t.Tick == 0 && t.MicrosecondsPerBeat == (int)(60_000_000 / 131.8));
        Assert.Contains(tempos, t => t.MicrosecondsPerBeat == (int)(60_000_000 / 90.5));
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse(Book)).ToXml().ToString();
        Assert.Contains("<per-minute>131.8</per-minute>", xml);
        Assert.Contains("<sound tempo=\"131.8\"", xml);
        Assert.Contains("<per-minute>90.5</per-minute>", xml);
    }

    [Fact]
    public void TheLilyPondTwin_WritesTheWholeCount()
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(Book));
        Assert.Contains(@"\tempo 4 = 132", ly);
        Assert.Contains(@"\tempo ""Slower"" 4 = 91", ly);
    }
}
