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

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>time none</c> — senza misura — is engraved (owner's decision 2026-09-08, HANDOFF §3).
/// LilyPond's <c>\cadenzaOn</c> is <c>\set Timing.timing = ##f</c> (ly/property-init.ly:283),
/// and <c>timing</c> is "keep administration of measure length, position, bar number, etc.?"
/// (scm/define-context-properties.scm:805-806). So from <c>time none</c> to the next metered
/// <c>time</c>: no automatic measure boundary, no meter drawn, no automatic beam, no
/// measure-length check, and the bar number stands still. Until session 353 the page read
/// the keyword and kept filling 4/4 bars under it (the fixture had 7 bars; the twin has 5).
/// <para>
/// The LilyPond numbers are 2.26.0's on the twin (scratch/p354/lp/pair.ps1 on the fixture,
/// 2026-09-08): bar lines at 20.357 / 48.501 / 73.533 on the first line and 14.645 / 22.795 on
/// the second (five drawn, the cadenza's two being the written <c>\bar "|"</c>), a second-line
/// BarNumber of 2, one TimeSignature grob per <c>\time</c> event (the returning 4/4 prints),
/// and no Beam grob under the cadenza where the same eighths in 4/4 make two
/// (senza-fixed.ly against senza-control.ly, the positive control).
/// </para>
/// </summary>
[Trait("Category", "Unit")]
public class SenzaMisuraTests
{
    /// <summary>test/senza-misura, headers aside.</summary>
    private const string Fixture = """
        time 4/4
        key c major
        part melody { clef treble }
        section Main {
          melody {
            c'4 d e f |
            time none g8 a b c d c b a g4 f e2 |
            d4 e f g a g f e | break
            time 4/4 c1 |
            d1 |
          }
        }
        form main { Main }
        score main "senza-misura" { staff melody }
        """;

    [Fact]
    public void AnUnmeteredSpan_ClosesItsBarsOnlyAtWrittenBarlines()
    {
        var measures = Collect(Fixture).Voice.Measures;

        // 4/4 | cadenza | cadenza | 4/4 | 4/4 — the two cadenza bars are the written `|`;
        // the engine used to auto-fill 4/4 under the keyword and make seven.
        Assert.Equal(5, measures.Length);
        Assert.False(measures[0].Unmetered);
        Assert.True(measures[1].Unmetered);
        Assert.True(measures[2].Unmetered);
        Assert.False(measures[3].Unmetered);
        Assert.False(measures[4].Unmetered);

        // Eleven sounding items in the first cadenza bar — a 4/4 clock would have closed
        // it after the eighth eighth.
        Assert.Equal(11, measures[1].Items.Count(i => i is NoteItem));
        Assert.Equal(8, measures[2].Items.Count(i => i is NoteItem));
    }

    [Fact]
    public void TheChangeToNone_IsAGrobWithNoInkAndNoWidth_AndTheReturnPrints()
    {
        var measures = Collect(Fixture).Voice.Measures;

        // \cadenzaOn is a property set, not a TimeSignature grob: the change item carries
        // the flag and is BLANKED (no ink, no column width — TimeSignatureChangeItem.Blanked).
        var none = Assert.Single(measures[1].Items.OfType<TimeSignatureChangeItem>());
        Assert.True(none.NewTime.SenzaMisura);
        Assert.True(none.Blanked);

        // The returning `time 4/4` prints, as every \time event does in LilyPond 2.26.0
        // (senza-reprint.ly: `\time 4/4 … \time 4/4` makes two grobs).
        var back = Assert.Single(measures[3].Items.OfType<TimeSignatureChangeItem>());
        Assert.False(back.NewTime.SenzaMisura);
        Assert.False(back.Blanked);
        Assert.Equal(new TimeSignature(4, 4), back.NewTime);
    }

    [Fact]
    public void TheBarNumber_StandsStillAcrossTheUnmeteredBars()
    {
        var measures = Collect(Fixture).Voice.Measures;

        // LilyPond: currentBarNumber is frozen with timing, so the cadenza's bars and the
        // bar after them are all "2" and the one after that is 3.
        Assert.Equal(new[] { 1, 2, 2, 2, 3 }, BarNumberEngraver.NumberMeasures(measures, 0));

        // …and on the page: the second system opens with bar 4 (index 3) and is numbered 2,
        // where the index alone would have said 4. LilyPond's PROBEBN on the twin: (2).
        var svg = Render(Fixture);
        var numbers = Regex.Matches(svg, "<text[^>]*font-weight=\"bold\"[^>]*>(\\d+)</text>")
            .Select(m => m.Groups[1].Value).ToList();
        Assert.Contains("2", numbers);
        Assert.DoesNotContain("4", numbers);
    }

    [Fact]
    public void TheUnmeteredBars_MakeNoAutomaticBeam_AndKeepAWrittenOne()
    {
        // Eight eighths under time none: no Beam grob in LilyPond (measured), none here.
        var score = Collect(Fixture);
        var groups = new BeamDetector().DetectBeamGroups(score);
        Assert.DoesNotContain(groups, g => g.MeasureIndex == 1);

        // Positive control — the same eighths in 4/4 beam (LilyPond: two Beam grobs).
        const string metered = """
            time 4/4
            part melody { clef treble }
            section Main { melody { c'4 d e f | g8 a b c d c b a | g4 f e2 | } }
            form main { Main }
            score main { staff melody }
            """;
        var control = new BeamDetector().DetectBeamGroups(Collect(metered));
        Assert.Equal(2, control.Count(g => g.MeasureIndex == 1));

        // A beam the author writes survives: only the AUTOMATIC ones stop.
        const string manual = """
            time none
            part melody { clef treble }
            section Main { melody { g'8[ a' b' c''] d''4 e''8 f'' | } }
            form main { Main }
            score main { staff melody }
            """;
        var written = new BeamDetector().DetectBeamGroups(Collect(manual));
        var one = Assert.Single(written);
        Assert.Equal(4, one.Members.Length);
    }

    [Fact]
    public void TimeNone_AtTheTop_IsUnmeteredFromTheFirstNote()
    {
        const string source = """
            time none
            part melody { clef treble }
            section Main { melody { c'4 d e f g a b c' | d'1 e' f' | } }
            form main { Main }
            score main { staff melody }
            """;
        var score = Collect(source);
        Assert.True(score.TimeSignature.SenzaMisura);
        Assert.Equal(2, score.Voice.Measures.Length);
        Assert.All(score.Voice.Measures, m => Assert.True(m.Unmetered));
        Assert.Equal(8, score.Voice.Measures[0].Items.Count(i => i is NoteItem));
        // The whole piece is one bar number: LayoutReport spells the meter `none`.
        Assert.Contains("time none  |  1 system, 2 bars", LayoutReport.Generate(SyntaxTree.Parse(source)));
    }

    [Fact]
    public void ASectionHeader_TimeNone_IsTheSectionsMeter_AndTheNextSectionRevertsToTheScores()
    {
        const string source = """
            time 4/4
            part melody { clef treble }
            section A { time none  melody { c'4 d e f g | a b c' | } }
            section B { melody { c'4 d e f | g a b c' | } }
            form main { A B }
            score main { staff melody }
            """;
        var measures = Collect(source).Voice.Measures;
        Assert.Equal(4, measures.Length);
        Assert.True(measures[0].Unmetered);
        Assert.True(measures[1].Unmetered);
        Assert.False(measures[2].Unmetered);
        Assert.False(measures[3].Unmetered);

        // A's header time is the blanked change at its head; B states no time, so the
        // boundary reverts to the score's 4/4 — and DRAWS it, because `none` and 4/4 differ.
        var aHead = Assert.Single(measures[0].Items.OfType<TimeSignatureChangeItem>());
        Assert.True(aHead.NewTime.SenzaMisura);
        var bHead = Assert.Single(measures[2].Items.OfType<TimeSignatureChangeItem>());
        Assert.False(bHead.NewTime.SenzaMisura);
        Assert.Equal(new[] { 1, 1, 1, 2 }, BarNumberEngraver.NumberMeasures(measures, 0));
    }

    [Fact]
    public void ThePage_DrawsTheWrittenBars_AndBreaksWhereTold()
    {
        // Five bar lines, as LilyPond draws on the twin (three on the first line, two on
        // the second); the line breaks after the cadenza's second written bar.
        var svg = Render(Fixture);
        var bars = Regex.Matches(svg, "<rect x=\"([0-9.-]+)\" y=\"([0-9.-]+)\" width=\"([0-9.-]+)\" height=\"([0-9.-]+)\"")
            .Select(m => (W: double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture),
                          H: double.Parse(m.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture)))
            .Count(r => r.W < 0.5 && r.H > 3);
        Assert.Equal(5, bars);

        var report = LayoutReport.Generate(SyntaxTree.Parse(Fixture));
        Assert.Contains("time 4/4 -> none (bar 2) -> 4/4 (bar 4)  |  2 systems, 5 bars", report);
        Assert.Contains("system 1: bars 1-3", report);
        Assert.Contains("system 2: bars 4-5", report);
    }

    /// <summary>
    /// Ragged-right on both sides, the bar lines land where LilyPond's do: 20.357 / 48.501 /
    /// 73.533 on the cadenza's line and 14.645 / 22.795 on the next (scratch/p354/lp/pair.ps1
    /// on senza-ragged.lys, 2026-09-08: Lily# 20.36 / 48.50 / 73.53 and 14.65 / 22.80). A
    /// cadenza bar is spaced by its notes alone, and the two engines agree on that.
    /// </summary>
    [Fact]
    public void TheCadenzaBars_AreAsWideAsLilyPonds()
    {
        var svg = Render("paper { raggedRight }\n" + Fixture);
        var rows = Regex.Matches(svg, "<rect x=\"([0-9.-]+)\" y=\"([0-9.-]+)\" width=\"([0-9.-]+)\" height=\"([0-9.-]+)\"")
            .Select(m => (X: double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                          Y: double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
                          W: double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture),
                          H: double.Parse(m.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture)))
            .Where(r => r.W < 0.5 && r.H > 3)
            .GroupBy(r => System.Math.Round(r.Y, 1))
            .Select(g => g.Select(r => r.X).Distinct().OrderBy(x => x).ToList())
            .ToList();
        var first = Assert.Single(rows, r => r.Count == 3);
        var second = Assert.Single(rows, r => r.Count == 2);
        Assert.InRange(first[0], 20.357 - 0.05, 20.357 + 0.05);
        Assert.InRange(first[1], 48.501 - 0.05, 48.501 + 0.05);
        Assert.InRange(first[2], 73.533 - 0.05, 73.533 + 0.05);
        Assert.InRange(second[0], 14.645 - 0.05, 14.645 + 0.05);
        Assert.InRange(second[1], 22.795 - 0.05, 22.795 + 0.05);
    }

    [Fact]
    public void TheTwin_WritesCadenzaOnAndOff_AndAWrittenBarAsTheGlyph()
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(Fixture));
        Assert.Contains("\\cadenzaOn g8 a b c d c b a g4 f e2 \\bar \"|\"", ly);
        Assert.Contains("f e \\bar \"|\"", ly);
        Assert.Contains("\\cadenzaOff \\time 4/4 c1 |", ly);
        // The metered bars keep the bare bar check.
        Assert.Contains("c'4 d e f |", ly);
    }

    [Fact]
    public void TheMeasureValidator_IsSilentUnderTimeNone()
    {
        var tree = SyntaxTree.Parse(Fixture);
        Assert.False(tree.HasErrors);
        var validator = new MeasureValidator();
        validator.Validate(tree);
        Assert.DoesNotContain(validator.Diagnostics, d => d.Code is "LYS2001" or "LYS2002");
    }

    /// <summary>
    /// The MIDI conductor track writes no time-signature event for <c>time none</c> and keeps
    /// the last meter: LilyPond's \cadenzaOn sets Timing.timing, not timeSignature, and the
    /// performer emits only on a \time event or a changed fraction
    /// (lily/time-signature-performer.cc, Time_signature_performer::process_music). Before
    /// session 353 the 4/4 the syntax falls back to was written, flipping a DAW's grid.
    /// </summary>
    [Fact]
    public void TheMidi_WritesNoMeterForTimeNone_AndKeepsTheLastOne()
    {
        const string source = """
            time 3/4
            part melody { clef treble }
            section A { melody { c'4 d e | time none f8 g a b c' d' e' f' g'4 | time 3/4 a2. | } }
            section B { time none  melody { c'4 d e f g | } }
            form main { A B }
            score main { staff melody }
            """;
        var midi = new LilySharp.Core.Midi.MidiExporter().Export(SyntaxTree.Parse(source));
        var meters = midi.Tracks[0].TimeSignatures;
        Assert.DoesNotContain(meters, ts => ts.Numerator == 4 && ts.Denominator == 4);
        Assert.All(meters, ts => Assert.Equal((3, 4), (ts.Numerator, ts.Denominator)));
    }

    [Fact]
    public void APlaceholderGap_UnderTimeNone_HoldsItsSlot_AndRenders()
    {
        const string source = """
            time none
            part melody { clef treble }
            section Main { melody { c'4 d | | e'4 f' | } }
            form main { Main }
            score main { staff melody }
            """;
        var measures = Collect(source).Voice.Measures;
        Assert.Equal(3, measures.Length);
        Assert.True(measures[1].IsEmptyPlaceholder);
        Assert.True(measures[1].Unmetered);
        Assert.NotEmpty(Render(source));
    }

    private static Score Collect(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        var spec = RenderSpecParser.FindFirst(tree);
        string? voiceName = spec is { Items.Length: 1 } && spec.Items[0] is SingleStaffSpec single
            ? single.Staff.VoiceName
            : null;
        return new MeasureCollector { ScoreTranspose = spec?.ScoreTranspose }
            .Collect(tree, voiceName, spec?.Form);
    }

    private static string Render(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        return SvgGenerator.Generate(tree, new SvgRenderOptions { EmbedFont = false });
    }
}
