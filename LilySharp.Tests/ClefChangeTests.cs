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
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;
using Xunit.Abstractions;

namespace LilySharp.Tests;

/// <summary>
/// Tests for mid-measure clef changes.
/// LILYPOND-REF: lily/clef-engraver.cc, lily/clef.cc
/// </summary>
[Trait("Category", "Unit")]
public class ClefChangeTests
{
    private readonly ITestOutputHelper _output;

    public ClefChangeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void ClefChangeItem_InMeasure_DetectedCorrectly()
    {
        var source = @"
part melody { clef treble }
phrase m { c'4 d clef bass c,4 d | }
section A { melody { $m } }
form main { A }
score main ""test"" { staff melody }
";
        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);

        var collector = new MeasureCollector();
        var score = collector.CollectMultiStaff(tree, spec);

        // Should have 1 staff group, 1 staff, 1 voice
        Assert.Single(score.StaffGroups);
        var staff = score.StaffGroups[0].Staves[0];
        Assert.Single(staff.Voices);
        var voice = staff.Voices[0];

        // Measure should contain a ClefChangeItem
        Assert.True(voice.Measures.Length >= 1);
        var measure = voice.Measures[0];
        _output.WriteLine($"Measure 0: {measure.Items.Length} items");
        foreach (var item in measure.Items)
        {
            _output.WriteLine($"  {item.GetType().Name}: Duration={item.Duration}");
            if (item is ClefChangeItem cc)
                _output.WriteLine($"    NewClef={cc.NewClef}");
        }

        var clefChanges = measure.Items.OfType<ClefChangeItem>().ToList();
        Assert.Single(clefChanges);
        Assert.Equal(ClefType.Bass, clefChanges[0].NewClef);
    }

    [Fact]
    public void ClefChange_ToTreble8_ParsesAndIsRecognized()
    {
        // treble_8 is a valid clef name mid-music, not only in a part header
        // (the parser used to reject it everywhere but headers).
        var source = @"
part melody { clef treble }
phrase m { c'4 d clef treble_8 c4 d | }
section A { melody { $m } }
form main { A }
score main ""x"" { staff melody }
";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);

        var spec = RenderSpecParser.FindFirst(tree);
        var score = new MeasureCollector().CollectMultiStaff(tree, spec!);
        var clefChanges = score.StaffGroups[0].Staves[0].Voices[0]
            .Measures[0].Items.OfType<ClefChangeItem>().ToList();
        Assert.Single(clefChanges);
        Assert.Equal(ClefType.Treble8Below, clefChanges[0].NewClef);
    }

    [Fact]
    public void Treble8_HeaderClef_RendersOctaveModifier8()
    {
        // A treble_8 part-header clef must draw the ClefModifier "8" below the
        // clef (LILYPOND-REF: ClefModifier grob). The "8" is plain italic text,
        // the only such glyph here, so its presence is unambiguous.
        var source = @"
part gtr { clef treble_8 }
phrase m { c'4 d e f | }
section A { gtr { $m } }
form main { A }
score main ""x"" { staff gtr }
";
        var tree = SyntaxTree.Parse(source);
        var svg = SvgGenerator.Generate(tree, new SvgRenderOptions { EmbedFont = false });
        _output.WriteLine(svg);

        Assert.Contains(">8</text>", svg);
        var modLine = svg.Split('\n').First(l => l.Contains(">8</text>"));
        Assert.Contains("font-style=\"italic\"", modLine);
    }

    [Fact]
    public void Treble8_MidMusicClefChange_RendersOctaveModifier8()
    {
        // A mid-music clef change to treble_8 also draws the "8" modifier
        // (DrawClefChange path), not just the header clef.
        var source = @"
part melody { clef treble }
phrase m { c'4 d clef treble_8 c4 d | }
section A { melody { $m } }
form main { A }
score main ""x"" { staff melody }
";
        var tree = SyntaxTree.Parse(source);
        var svg = SvgGenerator.Generate(tree, new SvgRenderOptions { EmbedFont = false });
        _output.WriteLine(svg);

        Assert.Contains(">8</text>", svg);
        var modLine = svg.Split('\n').First(l => l.Contains(">8</text>"));
        Assert.Contains("font-style=\"italic\"", modLine);
    }

    [Fact]
    public void ClefChangeItem_ZeroDuration()
    {
        var clefChange = new ClefChangeItem(ClefType.Bass, 0);
        Assert.Equal(Fraction.Zero, clefChange.Duration);
    }

    [Fact]
    public void ClefChange_MultipleMeasures_TrackedCorrectly()
    {
        var source = @"
part melody { clef treble }
phrase m { c'4 d e f | clef bass c,4 d e f | clef treble c'4 d e f | }
section A { melody { $m } }
form main { A }
score main ""test"" { staff melody }
";
        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);

        var collector = new MeasureCollector();
        var score = collector.CollectMultiStaff(tree, spec);

        var voice = score.StaffGroups[0].Staves[0].Voices[0];
        _output.WriteLine($"Total measures: {voice.Measures.Length}");

        for (int m = 0; m < voice.Measures.Length; m++)
        {
            var measure = voice.Measures[m];
            _output.WriteLine($"Measure {m}: {measure.Items.Length} items");
            foreach (var item in measure.Items)
            {
                if (item is ClefChangeItem cc)
                    _output.WriteLine($"  ClefChange -> {cc.NewClef}");
                else if (item is NoteItem note)
                    _output.WriteLine($"  Note: staffPos={note.StaffPosition}");
            }
        }

        // Measure 0: no clef change (treble from start)
        Assert.Empty(voice.Measures[0].Items.OfType<ClefChangeItem>());

        // Measure 1: clef bass change at start
        var m1Changes = voice.Measures[1].Items.OfType<ClefChangeItem>().ToList();
        Assert.Single(m1Changes);
        Assert.Equal(ClefType.Bass, m1Changes[0].NewClef);

        // Measure 2: clef treble change at start
        var m2Changes = voice.Measures[2].Items.OfType<ClefChangeItem>().ToList();
        Assert.Single(m2Changes);
        Assert.Equal(ClefType.Treble, m2Changes[0].NewClef);
    }

    [Fact]
    public void ClefChange_RenderedInSvg_ChangeGlyphs()
    {
        var source = @"
part melody { clef treble }
phrase m { c'4 d e f | g4 a clef bass c,4 d | }
section A { melody { $m } }
form main { A }
score main ""test"" { staff melody }
";
        var tree = SyntaxTree.Parse(source);
        var options = new SvgRenderOptions { EmbedFont = false };
        var svg = SvgGenerator.Generate(tree, options);

        _output.WriteLine(svg);

        // Should contain the bass clef change glyph (U+E084 = FClefChange)
        Assert.Contains(EmmentalerGlyphs.FClefChange.ToString(), svg);  // FClefChange glyph
    }

    [Fact]
    public void ClefChange_SystemStartClef_MatchesActiveClef()
    {
        // Use enough measures to force a natural system break.
        // The clef changes to bass at measure 3, so system 2 should start with bass clef.
        // The break is EXPLICIT: what this test asserts is that a system's prefix clef
        // follows the active clef, not where the breaker happens to cut. It used to rely on
        // eight bars not fitting one line, which stopped being true when the breaker's
        // springs were corrected — the assertion then failed on a premise, not on its point.
        var source = @"
part melody { clef treble }
phrase m { c'4 d e f | g4 a b c' | clef bass c,4 d e f | g4 a b c | break e4 f g a | b4 c d e | g4 a b c | e4 f g a | }
section A { melody { $m } }
form main { A }
score main ""test"" { staff melody }
";
        var tree = SyntaxTree.Parse(source);
        var options = new SvgRenderOptions { EmbedFont = false };
        var svg = SvgGenerator.Generate(tree, options);

        _output.WriteLine(svg);

        // Find system-start clef glyphs: a system's prefix clef sits at the left
        // edge (x="0.80" = the LeftEdge→Clef break-align space, EngravingDefaults.
        // ClefGlyphXOffset), distinguishing it from a mid-piece clef change further
        // right. (The prefix clef now also carries a data-pos for click-to-jump, so
        // it can no longer be told apart by the absence of one.)
        var lines = svg.Split('\n');
        var systemClefs = new List<string>();
        foreach (var line in lines)
        {
            if (line.Contains("class=\"music\"") && line.Contains("x=\"0.80\""))
            {
                foreach (char c in line)
                {
                    if (c == EmmentalerGlyphs.GClef) systemClefs.Add("Treble");
                    else if (c == EmmentalerGlyphs.FClef) systemClefs.Add("Bass");
                    else if (c == EmmentalerGlyphs.CClef) systemClefs.Add("Alto");
                }
            }
        }

        _output.WriteLine($"System-start clefs: {string.Join(", ", systemClefs)}");
        Assert.True(systemClefs.Count >= 2, $"Expected at least 2 system clefs, got {systemClefs.Count}");
        Assert.Equal("Treble", systemClefs[0]);  // System 1 starts with treble
        Assert.Equal("Bass", systemClefs[1]);     // System 2 starts with bass (after clef change)
    }

    [Fact]
    public void TrailingClefChange_SharesTheClosingBarMoment()
    {
        // clef-change-at-end.ly: a clef change at the very END of the music is
        // printed — and LilyPond engraves it on the SAME break-align column as the
        // piece's closing bar (the unbroken order is `… clef, staff-bar …`), so
        // there is ONE bar moment: the small change clef hangs back into the last
        // measure's closing gap, and no extra measure bar appears beside the final
        // barline. MEASURED on the paper-aligned pair
        // scratch/lpreg/clef-change-at-end.{ly,svg} (LilyPond 2.26.0, \bar "|."):
        //   note left → change-clef left: 20.334 − 17.121 = 3.213
        //   clef left → thin bar left:    23.181 − 20.334 = 2.847
        //     (= change-clef width + Clef.space-alist staff-bar extra-space 0.7)
        //   thin left → thick left:       23.671 − 23.181 = 0.490 (0.19 + kern 0.3)
        // The note→clef / clef→bar split differs by 0.003: Lily#'s F_change ink
        // width is 2.150 (font bbox) where LilyPond's stencil extent is 2.147 —
        // the known glyph-extent systematic. The bar itself sits LP-exact
        // (note→thin 6.060 both), so only the split moves.
        // Before the fix the clef drew ON the final barline and the trailing
        // placeholder minted a second thin bar beside it.
        // LILYPOND-REF: scm/define-grobs.scm:650-664 break-align-orders
        // 'time' is a file default; the trailing 'clef bass' is the subject and stays
        // at the END of the music. (The wrapper is geometry-neutral, so the measured
        // distances below are unchanged.)
        string svg = SvgGenerator.Generate(
            MusicSource.Parse("g'1 clef bass", "time 4/4"),
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });

        var glyphs = System.Text.RegularExpressions.Regex.Matches(svg,
                "<text class=\"music\" x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"[^>]*>(&#x([0-9A-Fa-f]+);|.)</text>")
            .Select(m => (
                X: double.Parse(m.Groups[1].Value),
                Glyph: m.Groups[4].Success
                    ? (char)Convert.ToInt32(m.Groups[4].Value, 16)
                    : m.Groups[3].Value[0]))
            .ToList();
        double note = Assert.Single(glyphs.Where(g => g.Glyph == EmmentalerGlyphs.NoteheadWhole)).X;
        double clef = Assert.Single(glyphs.Where(g => g.Glyph == EmmentalerGlyphs.FClefChange)).X;

        // Exactly the final barline's two pieces — no extra measure bar.
        var bars = System.Text.RegularExpressions.Regex.Matches(svg,
                "<rect x=\"([-\\d.]+)\" y=\"[-\\d.]+\" width=\"(0\\.19|0\\.60)\"")
            .Select(m => (X: double.Parse(m.Groups[1].Value), W: m.Groups[2].Value))
            .OrderBy(b => b.X).ToList();
        Assert.Equal(2, bars.Count);
        Assert.Equal("0.19", bars[0].W);
        Assert.Equal("0.60", bars[1].W);

        Assert.Equal(3.210, clef - note, 2);            // LP 3.213 (ink systematic 0.003)
        Assert.Equal(2.850, bars[0].X - clef, 2);       // LP 2.847 (same 0.003, other side)
        Assert.Equal(6.060, bars[0].X - note, 2);       // LP 6.060 — the bar is LP-exact
        Assert.Equal(0.490, bars[1].X - bars[0].X, 2);
    }

    [Fact]
    public void UnchangedClef_PrintsNothingAndTakesNoSpace()
    {
        // clef-unchanged.ly: a `clef` command whose resolved clef equals the one
        // already in force engraves NOTHING — only the system-start clef appears,
        // and the redundant command takes no space either. MEASURED on the
        // paper-aligned pair scratch/lpreg/clef-unchanged.{ly,svg} (LP 2.26.0):
        // whole-note heads sit 8.150 apart in both bars, with or without the
        // redundant `\clef "treble"` between them.
        // LILYPOND-REF: lily/clef-engraver.cc:139-166 inspect_clef_properties
        string svg = SvgGenerator.Generate(
            SyntaxTree.Parse("c1 | clef treble c1 | clef treble c1"),
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });

        var glyphs = System.Text.RegularExpressions.Regex.Matches(svg,
                "<text class=\"music\" x=\"([-\\d.]+)\"[^>]*>(&#x([0-9A-Fa-f]+);|.)</text>")
            .Select(m => (
                X: double.Parse(m.Groups[1].Value),
                Glyph: m.Groups[3].Success
                    ? (char)Convert.ToInt32(m.Groups[3].Value, 16)
                    : m.Groups[2].Value[0]))
            .ToList();

        Assert.Single(glyphs.Where(g => g.Glyph == EmmentalerGlyphs.GClef));
        Assert.Empty(glyphs.Where(g => g.Glyph == EmmentalerGlyphs.GClefChange));

        var heads = glyphs.Where(g => g.Glyph == EmmentalerGlyphs.NoteheadWhole)
            .Select(g => g.X).OrderBy(x => x).ToList();
        Assert.Equal(3, heads.Count);
        Assert.Equal(8.150, heads[1] - heads[0], 2);
        Assert.Equal(8.150, heads[2] - heads[1], 2);
    }
}
