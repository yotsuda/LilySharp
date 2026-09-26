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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A section can carry its own time / tempo (beside key) — stated section-major
/// (<c>section A { time 3/4  melody { … } }</c>) or in a standalone part-major header
/// (<c>section A { time 3/4 }</c>). They must render: the meter re-arms the section's
/// measures, the tempo prints a metronome mark.
/// </summary>
[Trait("Category", "Unit")]
public class SectionHeaderDirectiveTests
{
    private static Score Collect(string src)
        => new MeasureCollector().Collect(SyntaxTree.Parse(src), "melody");

    private static bool HasMeter(Score score, int beats, int beatType)
        => score.Voice.Measures.SelectMany(m => m.Items).OfType<TimeSignatureChangeItem>()
            .Any(t => t.NewTime.Beats == beats && t.NewTime.BeatType == beatType);

    /// <summary>The piece's OPENING meter is <paramref name="beats"/>/<paramref name="beatType"/>
    /// and no change item restates it in the first bar — the first section's header `time`
    /// replaces the initial signature, as a `time` before the first note does.</summary>
    private static void OpensIn(Score score, int beats, int beatType)
    {
        Assert.Equal(beats, score.TimeSignature.Beats);
        Assert.Equal(beatType, score.TimeSignature.BeatType);
        Assert.DoesNotContain(score.Voice.Measures[0].Items, i => i is TimeSignatureChangeItem);
    }

    [Fact]
    public void SectionMajorTime_EmitsTheSectionMeter()
    {
        var score = Collect("""
            time 4/4
            section A { time 3/4  melody { c4 d e | } }
            form main { A }
            score main { staff melody }
            """);
        OpensIn(score, 3, 4);
    }

    /// <summary>
    /// The first section's header meter is the OPENING signature, drawn once — also when it
    /// restates the file's meter and when the form opens with a repeat.
    /// </summary>
    /// <remarks>
    /// Until 2026-09-26 the header always added a change item, so the page opened "C C" (or
    /// "C 3/4"), the second one after a leading `|:`.
    /// LILYPOND-REF: lily/time-signature-engraver.cc:94-122 process_music — one TimeSignature
    /// per timestep.
    /// </remarks>
    [Theory]
    [InlineData("time 4/4", "form main { A }", 4, 4)]
    [InlineData("time 4/4", "form main { |: A :| }", 4, 4)]
    [InlineData("time 3/4", "form main { |: A :| }", 3, 4)]
    public void TheOpeningSectionsHeaderTime_IsTheOpeningSignature_NotAChange(
        string headerTime, string form, int beats, int beatType)
    {
        string body = beats == 3 ? "c4 d e |" : "c4 d e f |";
        var score = Collect($$"""
            section A { {{headerTime}}  melody { {{body}} } }
            {{form}}
            score main { staff melody }
            """);
        OpensIn(score, beats, beatType);
        Assert.DoesNotContain(score.Voice.Measures.SelectMany(m => m.Items), i => i is TimeSignatureChangeItem);
    }

    [Fact]
    public void SectionMajorTempo_EmitsAMetronomeMark()
    {
        // On a NON-first section, so it does not coincide with the score's initial tempo.
        var score = Collect("""
            tempo 100
            section A { melody { c4 d e f | } }
            section B { tempo 140  melody { g4 a b c' | } }
            form main { A B }
            score main { staff melody }
            """);
        Assert.Contains(score.MusicMarks, m => m.Type == MusicMarkType.Tempo && m.Text == "140");
    }

    [Fact]
    public void SectionTempo_OnAGrandStaff_EngravesOnceNotOncePerStaff()
    {
        // CollectMultiStaff walks the section once PER STAFF; a section tempo is a
        // score-level mark, so a two-staff score must NOT stack two identical metronome
        // marks. Regression: the grand staff printed "140" twice, overlapping.
        var tree = SyntaxTree.Parse("""
            tempo 100
            part rh { section A { c'4 d' e' f' } section B { g'4 a' g' f' } }
            part lh { clef bass  section A { c4 e g e } section B { c4 g, c g, } }
            section A { }
            section B { tempo 140 }
            form main { A B }
            score main { staff rh  staff lh }
            """);
        var renderSpec = RenderSpecParser.FindFirst(tree)!;
        var score = new MeasureCollector().CollectMultiStaff(tree, renderSpec);
        Assert.Single(score.MusicMarks, m => m.Type == MusicMarkType.Tempo && m.Text == "140");
    }

    [Fact]
    public void FirstSectionTempo_ReplacesTheScoresOpeningTempo()
    {
        // Section A is first, so its `tempo 140` becomes the piece's opening metronome
        // mark (replacing the score's `tempo 100`) instead of being hidden behind it.
        var score = Collect("""
            tempo 100
            section A { tempo 140  melody { c4 d e f | } }
            section B { melody { g4 a b c' | } }
            form main { A B }
            score main { staff melody }
            """);
        Assert.Equal(140, score.Tempo);
        // It replaced the opening mark — no second Tempo mark stacked on top of it.
        Assert.DoesNotContain(score.MusicMarks, m => m.Type == MusicMarkType.Tempo);
    }

    [Fact]
    public void StandalonePartMajorHeaderTime_AppliesToTheSection()
    {
        var score = Collect("""
            time 4/4
            part melody { section A { c4 d e | } }
            section A { time 3/4 }
            form main { A }
            score main { staff melody }
            """);
        OpensIn(score, 3, 4);
    }

    [Fact]
    public void StandalonePartMajorHeaderTempo_AppliesToTheSection()
    {
        var score = Collect("""
            tempo 100
            part melody { section A { c4 d e f | } section B { g4 a b c' | } }
            section B { tempo 140 }
            form main { A B }
            score main { staff melody }
            """);
        Assert.Contains(score.MusicMarks, m => m.Type == MusicMarkType.Tempo && m.Text == "140");
    }

    [Fact]
    public void SectionPartial_ShortensTheSectionsFirstMeasure()
    {
        // `partial 4` gives section A a quarter-note pickup, so its first bar holds a
        // single quarter rather than a full 4/4 measure.
        var score = Collect("""
            time 4/4
            section A { partial 4  melody { g4 | c' d' e' f' | } }
            form main { A }
            score main { staff melody }
            """);
        Assert.Equal(Fraction.Quarter, score.Voice.Measures[0].TotalDuration);
        Assert.Equal(new Fraction(4, 4), score.Voice.Measures[1].TotalDuration);
    }

    [Fact]
    public void StandalonePartMajorHeaderPartial_AppliesToTheSection()
    {
        var score = Collect("""
            time 4/4
            part melody { section A { g4 | c' d' e' f' | } }
            section A { partial 4 }
            form main { A }
            score main { staff melody }
            """);
        Assert.Equal(Fraction.Quarter, score.Voice.Measures[0].TotalDuration);
    }

    /// <summary>
    /// On a grand staff an opening 3/4 — in the section header or written in each part's
    /// music — opens BOTH staves in 3/4 with no change item, and BOTH revert to the score's
    /// 4/4 at the next section.
    /// </summary>
    /// <remarks>
    /// The staves are collected one after the other, and the first staff's opening `time`
    /// rewrote the running meter the second one then took as its score meter: it never
    /// reverted at B, and with the time written in the music it also opened "3/4 C"
    /// (2026-09-26).
    /// </remarks>
    [Theory]
    [InlineData("section A { time 3/4  rh { c'2. | }  lh { c2. | } }")]
    [InlineData("section A { rh { time 3/4 c'2. | }  lh { time 3/4 c2. | } }")]
    public void OnAGrandStaff_EveryStaffOpensInTheSectionMeter_AndReverts(string sectionA)
    {
        var tree = SyntaxTree.Parse($$"""
            time 4/4
            part rh { clef treble }
            part lh { clef bass }
            {{sectionA}}
            section B { rh { c'1 | }  lh { c1 | } }
            form main { ~A ~B }
            score main { grandStaff { staff rh  staff lh } }
            """);
        var score = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);

        Assert.Equal(3, score.TimeSignature.Beats);
        foreach (var (_, staff, _) in score.EnumerateStaves())
        {
            var measures = staff.PrimaryVoice.Measures;
            Assert.DoesNotContain(measures[0].Items, i => i is TimeSignatureChangeItem);
            var revert = Assert.Single(measures[1].Items.OfType<TimeSignatureChangeItem>());
            Assert.Equal(4, revert.NewTime.Beats);
        }
    }

    [Fact]
    public void NextSectionWithoutATime_RevertsToTheScoreMeter()
    {
        // Section A is 3/4; B states nothing, so it reverts to 4/4.
        var score = Collect("""
            time 4/4
            section A { time 3/4  melody { c4 d e | } }
            section B { melody { c4 d e f | } }
            form main { A B }
            score main { staff melody }
            """);
        OpensIn(score, 3, 4);               // A's meter opens the piece
        Assert.True(HasMeter(score, 4, 4)); // B reverted
    }
}
