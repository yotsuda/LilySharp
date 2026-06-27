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
using LilySharp.Core.Music;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The chordnames { } block: structured chord entry (root + quality → interval
/// set + auto-named symbol), displayed above the staff and timing-aligned.
/// LILYPOND-REF: scm/chord-entry.scm; ly/engraver-init.ly ChordNames.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ChordNamesTests
{
    private const string Sharp = "♯"; // ♯
    private const string Flat = "♭";  // ♭

    // ---- Structured model: naming ------------------------------------------

    [Theory]
    [InlineData(0, 0, ChordQuality.Major, "C")]
    [InlineData(5, 0, ChordQuality.Minor, "Am")]
    [InlineData(4, 0, ChordQuality.Dominant7, "G7")]
    [InlineData(1, 0, ChordQuality.Minor7, "Dm7")]
    [InlineData(0, 0, ChordQuality.Major7, "Cmaj7")]
    [InlineData(4, 0, ChordQuality.Sus4, "Gsus4")]
    [InlineData(0, 0, ChordQuality.Diminished, "Cdim")]
    public void DisplayName_FromStructure(int step, int alter, ChordQuality q, string expected)
    {
        Assert.Equal(expected, new ChordStructure(step, alter, q).DisplayName);
    }

    [Fact]
    public void DisplayName_RendersAccidentalsAndBass()
    {
        Assert.Equal("C" + Sharp, new ChordStructure(0, 1, ChordQuality.Major).DisplayName);
        Assert.Equal("B" + Flat + "7", new ChordStructure(6, -1, ChordQuality.Dominant7).DisplayName);
        // C/G slash bass.
        Assert.Equal("C/G", new ChordStructure(0, 0, ChordQuality.Major, BassStep: 4).DisplayName);
    }

    [Theory]
    [InlineData(ChordQuality.Major, new[] { 0, 4, 7 })]
    [InlineData(ChordQuality.Minor, new[] { 0, 3, 7 })]
    [InlineData(ChordQuality.Dominant7, new[] { 0, 4, 7, 10 })]
    [InlineData(ChordQuality.Major7, new[] { 0, 4, 7, 11 })]
    [InlineData(ChordQuality.Diminished7, new[] { 0, 3, 6, 9 })]
    public void Intervals_AreTheChordTones(ChordQuality q, int[] expected)
    {
        Assert.Equal(expected, new ChordStructure(0, 0, q).Intervals);
    }

    [Theory]
    [InlineData("m", ChordQuality.Minor)]
    [InlineData("min", ChordQuality.Minor)]
    [InlineData("maj7", ChordQuality.Major7)]
    [InlineData("m7", ChordQuality.Minor7)]
    [InlineData("7", ChordQuality.Dominant7)]
    [InlineData("sus4", ChordQuality.Sus4)]
    [InlineData("m7b5", ChordQuality.HalfDiminished7)]
    public void Registry_ResolvesQualityTokens(string token, ChordQuality expected)
    {
        Assert.True(ChordQualityRegistry.TryResolve(token, out var q));
        Assert.Equal(expected, q);
    }

    [Fact]
    public void Registry_EmptyTokenIsMajor_UnknownFails()
    {
        Assert.True(ChordQualityRegistry.TryResolve(null, out var q));
        Assert.Equal(ChordQuality.Major, q);
        Assert.False(ChordQualityRegistry.TryResolve("nonsense", out _));
    }

    // ---- Parsing + collection + timing alignment ---------------------------

    private const string LeadSheet =
        "key c major\npart m { clef treble }\n" +
        "section Main {\n  m {\n    time 4/4\n    c4 d e f | g a b c |\n  }\n" +
        "  chordnames {\n    c2 a2:m | f2 g2:7 |\n  }\n}\n" +
        "structure { Main }\nscore \"x\" { staff { m } }\n";

    [Fact]
    public void ChordNames_ParseWithoutErrors()
    {
        var tree = SyntaxTree.Parse(LeadSheet);
        Assert.DoesNotContain(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Single(tree.GetRoot().DescendantNodes().OfType<ChordNamesBlockSyntax>());
    }

    [Fact]
    public void Collector_EmitsTimingAlignedAutoNamedChords()
    {
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(LeadSheet));
        var chords = score.ChordNames.OrderBy(c => c.MeasureIndex).ThenBy(c => c.Timing.ToDouble()).ToList();

        Assert.Equal(4, chords.Count);
        Assert.Equal(new[] { "C", "Am", "F", "G7" }, chords.Select(c => c.ChordText));
        // Mid-bar chords (Am, G7) land at timing 1/2; bar-start chords at 0.
        Assert.Equal(new[] { 0, 0, 1, 1 }, chords.Select(c => c.MeasureIndex));
        Assert.Equal(new[] { 0.0, 0.5, 0.0, 0.5 }, chords.Select(c => c.Timing.ToDouble()));
        Assert.All(chords, c => Assert.True(c.UseTiming));
        // The structure (interval set) is carried for future notes / fret diagrams.
        Assert.Equal(new[] { 0, 4, 7, 10 }, chords[3].Structure!.Intervals); // G7
    }

    [Fact]
    public void Collector_UnknownQuality_FallsBackToRawText()
    {
        // An extended chord not in the vocabulary still displays (root + raw token),
        // just without a structure.
        var src = "section Main {\n  m { time 4/4 c4 d e f | }\n  chordnames { c1:weird9 }\n}\n" +
                  "structure { Main }\nscore \"x\" { staff { m } }\n";
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(src));
        var chord = Assert.Single(score.ChordNames);
        Assert.Equal("Cweird9", chord.ChordText);
        Assert.Null(chord.Structure);
    }
}
