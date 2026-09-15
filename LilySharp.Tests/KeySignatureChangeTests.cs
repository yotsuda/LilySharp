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

using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;
using Xunit.Abstractions;

namespace LilySharp.Tests;

/// <summary>
/// Tests for mid-measure key signature changes.
/// LILYPOND-REF: lily/key-engraver.cc
/// </summary>
[Trait("Category", "Unit")]
public class KeySignatureChangeTests
{
    private readonly ITestOutputHelper _output;

    public KeySignatureChangeTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A key change AFTER the first note of a measure is engraved mid-measure, not in
    /// the system-head prefix — so a system STARTING at that measure still reserves the
    /// old key's ink. The head picks a change up only when it OPENS the measure (the
    /// opening-change branch in <see cref="SpacingRules.ActiveKeyInkForStaff"/>).
    /// </summary>
    /// <remarks>
    /// ⚠️ Pins the per-voice active-key table's PREFIX boundary (finding 4-1's memo,
    /// 2026-08-27): <c>tbl[k]</c> must exclude measure k's own changes. Measured hole:
    /// moving the table's record point below the measure's item scan (the off-by-one a
    /// rewrite naturally reaches for) left the whole suite green — only a MID-measure
    /// change in a system-start measure distinguishes the two, and nothing covered it.
    /// </remarks>
    [Fact]
    public void ActiveKeyInk_MidMeasureChangeInTheStartMeasure_IsNotYetActive()
    {
        var source = """
            time 4/4
            key g major
            part melody { clef treble }
            phrase mel { c'4 d' e' f' | g'4 a' key d major b' c'' | d''4 c'' b' a' | }
            section Main { melody { mel } }
            form main { Main }
            score main "x" { staff melody }
            """;
        var tree = SyntaxTree.Parse(source);
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var staff = score.PrimaryContentStaff;

        double atOpening = SpacingRules.ActiveKeyInkForStaff(score, staff, 0);
        double atMidChangeMeasure = SpacingRules.ActiveKeyInkForStaff(score, staff, 1);
        double afterChange = SpacingRules.ActiveKeyInkForStaff(score, staff, 2);

        // Measure 1's change sits after its second note: a system starting there still
        // opens on G major (1 sharp), bit-identical to the first system's head.
        Assert.Equal(atOpening, atMidChangeMeasure);
        // From measure 2 the change is active: D major (2 sharps) reserves wider ink.
        Assert.True(afterChange > atMidChangeMeasure,
            $"expected D major ({afterChange}) wider than G major ({atMidChangeMeasure})");
    }

    /// <summary>
    /// A key change carries the clef in effect at its moment, and a clef change of the SAME
    /// moment counts whichever the source wrote first: break alignment prints the clef
    /// before the signature, and the accidentals take their positions from it.
    /// </summary>
    /// <remarks>
    /// MEASURED (2.26.0, scratch/p390/keyw order-a.ly / order-b.ly): `\key d \major \clef tenor`
    /// and `\clef tenor \key d \major` render byte-identical SVG, the accidentals at tenor
    /// positions (naturals +1 / +4, sharps −2 / +2) — the page matches to 0.01.
    /// LILYPOND-REF: scm/output-lib.scm:1056 key-signature-interface::alteration-positions — reads the staff's c0-position
    /// </remarks>
    [Theory]
    [InlineData("key d major clef tenor")]
    [InlineData("clef tenor key d major")]
    public void AKeyChange_CarriesTheClefOfItsMoment_WhicheverIsWrittenFirst(string changes)
    {
        var source = $$"""
            time 4/4
            key bes major
            part melody { clef treble }
            phrase mel { c'4 d' e' f' | {{changes}} c'4 d' e' f' | }
            section Main { melody { mel } }
            form main { Main }
            score main "x" { staff melody }
            """;
        var tree = SyntaxTree.Parse(source);
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var change = Assert.Single(score.PrimaryContentStaff.PrimaryVoice.Measures[1].Items
            .OfType<KeySignatureChangeItem>());

        Assert.Equal(ClefType.Tenor, change.Clef);
        // The change before it in the piece had no clef change beside it: the running clef.
        var opening = new KeySignatureChangeItem(change.NewKey, change.PreviousKey, 0);
        Assert.Equal(ClefType.Treble, opening.Clef);
    }

    /// <summary>
    /// The bar line's optical correction for a DOWN stem just after it applies only when the
    /// bar line is the column's last grob: a key change opening the bar stands between them,
    /// and the down-stem first note sits where an up-stem one does.
    /// </summary>
    /// <remarks>
    /// MEASURED (2.26.0, scratch/p390/keyw kn-b.ly / kn-u.ly, `\key b \major` opening the bar):
    /// the first note 9.00 off the bar line's ink right with its stem down and with it up; the
    /// page drew the down-stem note at 9.19. After a bare bar line the correction stays, so the
    /// control pair below must still differ.
    /// LILYPOND-REF: lily/staff-spacing.cc:72-93 Staff_spacing::bar_y_positions — empty unless bar-line-interface
    /// </remarks>
    [Fact]
    public void ADownStemAfterAKeyChange_GetsNoBarLineOpticalCorrection()
    {
        static double FirstColumnX(string pitch, bool withKey)
        {
            string key = withKey ? "key b major " : "";
            var source = $$"""
                octave absolute
                time 4/4
                key c major
                part m
                section A { m { f4 f f f | {{key}}{{pitch}}8 {{pitch}} {{pitch}}4 {{pitch}}2 | } }
                form main { ~A }
                score main "x" { staff m }
                """;
            var tree = SyntaxTree.Parse(source);
            var multi = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
            var layout = new LayoutEngine(new LayoutOptions()).Layout(multi);
            var bar = Assert.Single(layout.Systems).Measures.Single(m => m.MeasureIndex == 1);
            return bar.GetXForTiming(Fraction.Zero);
        }

        // e' (E5) stems down, e (E4) stems up.
        Assert.Equal(FirstColumnX("e", withKey: true), FirstColumnX("e'", withKey: true), 6);
        double down = FirstColumnX("e'", withKey: false), up = FirstColumnX("e", withKey: false);
        Assert.True(down > up + 0.1, $"control: after a bare bar line the down stem keeps its correction ({down} vs {up})");
    }

    /// <summary>
    /// The bar line → first column spring is ONE Staff_spacing wish PER STAFF, merged: a key
    /// change opening the bar on the upper staff only is averaged with the lower staff's wish off
    /// its bar line, so the first column sits closer than when every staff carries the change —
    /// and when every staff carries it, exactly where one staff puts it.
    /// </summary>
    /// <remarks>
    /// MEASURED (2.26.0, scratch/p390/ks, key-signature-space's 4 flats → 5 sharps): the first
    /// note 12.22 off the bar line's ink right with the lower staff resting (ksb.ly), 12.77 with
    /// the change on both staves (ksd.ly) and on one staff alone (ksa.ly); ly:paper-column::print
    /// reads the bar's non-musical column ideal 12.90 against 13.45.
    /// LILYPOND-REF: lily/spacing-spanner.cc:478-536 Spacing_spanner::breakable_column_spacing — one Staff_spacing wish per staff
    /// LILYPOND-REF: lily/spring.cc:104-129 merge_springs — ideals averaged, the largest minimum
    /// </remarks>
    [Fact]
    public void AKeyChangeOnOneStaffOnly_IsAveragedWithTheOtherStaffsBarLineWish()
    {
        static double FirstColumnX(bool lowerStaff, bool lowerKey)
        {
            string lower = lowerStaff
                ? "vtwo { r1 | " + (lowerKey ? "key b major " : "") + "r1 | }"
                : "";
            string staves = lowerStaff ? "staff vone staff vtwo" : "staff vone";
            var source = $$"""
                octave absolute
                time 4/4
                key f minor
                part vone
                part vtwo
                section Main {
                  vone { f4@stemUp f@stemUp f@stemUp f@stemUp | key b major e'8@stemUp e'@stemUp e'4@stemUp e'2@stemUp | }
                  {{lower}}
                }
                form main { ~Main }
                score main "x" { {{staves}} }
                """;
            var tree = SyntaxTree.Parse(source);
            var multi = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
            var layout = new LayoutEngine(new LayoutOptions()).Layout(multi);
            var bar = Assert.Single(layout.Systems).Measures.Single(m => m.MeasureIndex == 1);
            return bar.GetXForTiming(Fraction.Zero);
        }

        double one = FirstColumnX(lowerStaff: false, lowerKey: false);
        double both = FirstColumnX(lowerStaff: true, lowerKey: true);
        double upperOnly = FirstColumnX(lowerStaff: true, lowerKey: false);
        Assert.Equal(one, both, 6);
        Assert.True(upperOnly < both - 0.3, $"the lower staff's bar-line wish pulls the average in: {upperOnly} vs {both}");
    }

    [Fact]
    public void KeySignatureChangeItem_ZeroDuration()
    {
        var keyChange = new KeySignatureChangeItem(
            new KeySignature(2), new KeySignature(0), 0);
        Assert.Equal(Fraction.Zero, keyChange.Duration);
    }

    [Fact]
    public void KeySignatureChangeItem_StoresNewAndPreviousKey()
    {
        var newKey = new KeySignature(-3);  // 3 flats
        var prevKey = new KeySignature(2);  // 2 sharps
        var keyChange = new KeySignatureChangeItem(newKey, prevKey, 42);

        Assert.Equal(-3, keyChange.NewKey.Sharps);
        Assert.Equal(2, keyChange.PreviousKey.Sharps);
        Assert.Equal(42, keyChange.SourcePosition);
    }

    [Fact]
    public void KeyChange_InMeasure_DetectedCorrectly()
    {
        var source = @"
part melody { clef treble }
phrase m { c'4 d e f | key d major fis4 g a b | }
section A { melody { m } }
form main { A }
score main ""test"" { staff melody }
";
        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);

        var collector = new MeasureCollector();
        var score = collector.CollectMultiStaff(tree, spec);

        var staff = score.StaffGroups[0].Staves[0];
        var voice = staff.Voices[0];

        // Measure 0: no key change
        Assert.Empty(voice.Measures[0].Items.OfType<KeySignatureChangeItem>());

        // Measure 1: key d major change at start
        var m1Changes = voice.Measures[1].Items.OfType<KeySignatureChangeItem>().ToList();
        Assert.Single(m1Changes);
        Assert.Equal(2, m1Changes[0].NewKey.Sharps);  // D major = 2 sharps
        Assert.Equal(0, m1Changes[0].PreviousKey.Sharps);  // Previous was C major

        _output.WriteLine($"Key change: {m1Changes[0].PreviousKey.Sharps} → {m1Changes[0].NewKey.Sharps}");
    }

    [Fact]
    public void KeyChange_MultipleMeasures_TrackedCorrectly()
    {
        var source = @"
part melody { clef treble }
phrase m { c'4 d e f | key d major fis4 g a b | key bes major bes4 a g f | }
section A { melody { m } }
form main { A }
score main ""test"" { staff melody }
";
        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);

        var collector = new MeasureCollector();
        var score = collector.CollectMultiStaff(tree, spec);

        var voice = score.StaffGroups[0].Staves[0].Voices[0];

        for (int m = 0; m < voice.Measures.Length; m++)
        {
            var measure = voice.Measures[m];
            _output.WriteLine($"Measure {m}: {measure.Items.Length} items");
            foreach (var item in measure.Items)
            {
                if (item is KeySignatureChangeItem kc)
                    _output.WriteLine($"  KeyChange: {kc.PreviousKey.Sharps} → {kc.NewKey.Sharps}");
                else if (item is NoteItem note)
                    _output.WriteLine($"  Note: staffPos={note.StaffPosition}");
            }
        }

        // Measure 0: no key change
        Assert.Empty(voice.Measures[0].Items.OfType<KeySignatureChangeItem>());

        // Measure 1: key d major (2 sharps)
        var m1Changes = voice.Measures[1].Items.OfType<KeySignatureChangeItem>().ToList();
        Assert.Single(m1Changes);
        Assert.Equal(2, m1Changes[0].NewKey.Sharps);

        // Measure 2: key bes major (-2 flats)
        var m2Changes = voice.Measures[2].Items.OfType<KeySignatureChangeItem>().ToList();
        Assert.Single(m2Changes);
        Assert.Equal(-2, m2Changes[0].NewKey.Sharps);
        Assert.Equal(2, m2Changes[0].PreviousKey.Sharps);  // Previous was D major
    }

    [Fact]
    public void KeyChange_RenderedInSvg_ContainsGlyphs()
    {
        var source = @"
part melody { clef treble }
phrase m { c'4 d e f | key d major fis4 g a b | }
section A { melody { m } }
form main { A }
score main ""test"" { staff melody }
";
        var tree = SyntaxTree.Parse(source);
        var options = new SvgRenderOptions { EmbedFont = false };
        var svg = SvgGenerator.Generate(tree, options);

        _output.WriteLine(svg);

        // Should contain sharp glyphs for D major key signature (U+E013 = AccidentalSharp)
        Assert.Contains(EmmentalerGlyphs.AccidentalSharp.ToString(), svg);
    }

    [Fact]
    public void KeyChange_CancellationNaturals_DifferentType()
    {
        // Change from sharps to flats: should show cancellation naturals
        var source = @"
part melody { clef treble }
key d major
phrase m { fis4 g a b | key f major c'4 bes a g | }
section A { melody { m } }
form main { A }
score main ""test"" { staff melody }
";
        var tree = SyntaxTree.Parse(source);
        var options = new SvgRenderOptions { EmbedFont = false };
        var svg = SvgGenerator.Generate(tree, options);

        _output.WriteLine(svg);

        // Should contain natural glyphs (U+E01D = AccidentalNatural) for cancellation
        Assert.Contains(EmmentalerGlyphs.AccidentalNatural.ToString(), svg);
        // Should contain flat glyphs (U+E021 = AccidentalFlat) for new F major key
        Assert.Contains(EmmentalerGlyphs.AccidentalFlat.ToString(), svg);
    }

    [Fact]
    public void KeyChange_ScoreKeySignature_PreservesInitial()
    {
        // Initial key is D major; mid-measure change to C major.
        // Score.KeySignature should reflect initial D major, not final C major.
        var source = @"
part melody { clef treble }
key d major
phrase m { fis4 g a b | key c major c'4 d e f | }
section A { melody { m } }
form main { A }
score main ""test"" { staff melody }
";
        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);

        var collector = new MeasureCollector();
        var score = collector.CollectMultiStaff(tree, spec);

        Assert.Equal(2, score.KeySignature.Sharps);  // D major = 2 sharps (initial key preserved)
    }

    [Fact]
    public void KeyChange_SameType_FewerSharps_ShowsCancellation()
    {
        // D major (2 sharps) → G major (1 sharp): should cancel C# with natural
        var source = @"
part melody { clef treble }
key d major
phrase m { fis4 g a b | key g major c'4 d e fis | }
section A { melody { m } }
form main { A }
score main ""test"" { staff melody }
";
        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);

        var collector = new MeasureCollector();
        var score = collector.CollectMultiStaff(tree, spec);

        var voice = score.StaffGroups[0].Staves[0].Voices[0];
        var keyChanges = voice.Measures[1].Items.OfType<KeySignatureChangeItem>().ToList();
        Assert.Single(keyChanges);
        Assert.Equal(1, keyChanges[0].NewKey.Sharps);  // G major = 1 sharp
        Assert.Equal(2, keyChanges[0].PreviousKey.Sharps);  // D major = 2 sharps

        // Render and check for cancellation natural
        var options = new SvgRenderOptions { EmbedFont = false };
        var svg = SvgGenerator.Generate(tree, options);
        _output.WriteLine(svg);

        // Should contain natural for the cancelled C#
        Assert.Contains(EmmentalerGlyphs.AccidentalNatural.ToString(), svg);
    }
}
