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
/// The chords { } block: structured chord entry (root + quality → interval
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

    // Fifthless voicings: a chord with the perfect 5th dropped still names as that
    // chord — <1 3 7> in C = C-E-B = Cmaj7, and the root+3rd dyad <c e>/<c ees> = C/Cm.
    [Theory]
    [InlineData(new[] { 0, 4 }, ChordQuality.Major)]
    [InlineData(new[] { 0, 3 }, ChordQuality.Minor)]
    [InlineData(new[] { 0, 4, 11 }, ChordQuality.Major7)]
    [InlineData(new[] { 0, 4, 10 }, ChordQuality.Dominant7)]
    [InlineData(new[] { 0, 3, 10 }, ChordQuality.Minor7)]
    [InlineData(new[] { 0, 3, 11 }, ChordQuality.MinorMajor7)]
    public void Recognize_FifthlessSeventhShells(int[] intervals, ChordQuality expected)
    {
        Assert.True(ChordQualityRegistry.TryRecognize(intervals, out var q));
        Assert.Equal(expected, q);
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
        "  chords {\n    c2 a2:m | f2 g2:7 |\n  }\n}\n" +
        "form main { Main }\nscore \"x\" { staff m }\n";

    [Fact]
    public void ChordNames_ParseWithoutErrors()
    {
        var tree = SyntaxTree.Parse(LeadSheet);
        Assert.DoesNotContain(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Single(tree.GetRoot().DescendantNodes().OfType<ChordPartBlockSyntax>().Where(b => b.PartName == null));
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

    // ---- Note-expansion (editor completion) --------------------------------

    [Theory]
    [InlineData("cmaj7", "<c e g b>")]
    [InlineData("am", "<a c e>")]
    [InlineData("g7", "<g b d f>")]
    [InlineData("cm", "<c ees g>")]      // minor third spells e-flat, not d-sharp
    [InlineData("bes7", "<bes d f aes>")] // Bb7: B♭ D F A♭, correctly spelled
    [InlineData("dm7", "<d f a c>")]
    [InlineData("csus4", "<c f g>")]
    [InlineData("c9", "<c e g bes d>")]   // the 9th voices an octave up via relative
    public void TryParseSymbol_SpellsNoteChord(string word, string expected)
    {
        Assert.True(ChordStructure.TryParseSymbol(word, out var chord));
        Assert.Equal(expected, chord.ToNoteChord());
    }

    [Theory]
    [InlineData("c")]      // bare note — no quality, not a chord
    [InlineData("ees")]    // bare accidental note
    [InlineData("cx")]     // unknown quality token
    [InlineData("h7")]     // not a pitch letter
    public void TryParseSymbol_RejectsNonChords(string word)
    {
        Assert.False(ChordStructure.TryParseSymbol(word, out _));
    }

    [Fact]
    public void Tones_AreSpelledByDegree()
    {
        // Cm: C, E♭, G — letter steps 0/2/4 with the third flattened.
        var tones = new ChordStructure(0, 0, ChordQuality.Minor).Tones;
        Assert.Equal(new[] { 0, 2, 4 }, tones.Select(t => t.Step));
        Assert.Equal(new[] { 0, -1, 0 }, tones.Select(t => t.Alter));
    }

    [Theory]
    // A quality that lexes as several tokens (dot + minus, or number + word) must
    // be captured WHOLE, not truncated to its first token (the "Gm7" bug).
    [InlineData("g:m7.5-", "Gm7♭5")] // half-diminished, dot+minus → resolves
    [InlineData("g:7sus4", "G7sus4")]      // number + word → resolves
    [InlineData("g:m7.5-7", "Gm7.5-7")]    // unknown extended chord → full text, not "Gm7"
    public void MultiTokenQuality_IsCapturedWhole(string entry, string expected)
    {
        var src = "section Main {\n  m { time 4/4 c4 d e f | }\n  chords { " + entry + " }\n}\n" +
                  "form main { Main }\nscore \"x\" { staff m }\n";
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(src));
        var chord = Assert.Single(score.ChordNames);
        Assert.Equal(expected, chord.ChordText);
    }

    [Fact]
    public void Collector_UnknownQuality_KeepsRawSuffix_NoTones()
    {
        // An extended chord not in the vocabulary still displays (root + raw token)
        // and now resolves a Roman degree from its root, but carries no interval set
        // (unknown tones → no note expansion).
        var src = "section Main {\n  m { time 4/4 c4 d e f | }\n  chords { c1:weird9 }\n}\n" +
                  "form main { Main }\nscore main \"x\" { staff m }\n";
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(src));
        var chord = Assert.Single(score.ChordNames);
        Assert.Equal("Cweird9", chord.ChordText);
        Assert.NotNull(chord.Structure);
        Assert.Empty(chord.Structure!.Intervals);                      // tones are unknown
        Assert.Equal("Iweird9", chord.Structure.ToRomanNumeral(0, 0)); // root still gives a degree
    }

    [Fact]
    public void ChordStructure_RawSuffix_RomanKeepsTypedSuffix()
    {
        // `c:M7` (jazz major-7th shorthand, not a registry token) keeps its typed
        // suffix and converts the root to a Roman degree: "CM7" → "IM7", not "CM7".
        var s = new LilySharp.Core.Music.ChordStructure(
            0, 0, LilySharp.Core.Music.ChordQuality.Major, RawSuffix: "M7");
        Assert.Equal("CM7", s.DisplayName);
        Assert.Equal("IM7", s.ToRomanNumeral(0, 0)); // C in C major → I, suffix verbatim
    }
}
