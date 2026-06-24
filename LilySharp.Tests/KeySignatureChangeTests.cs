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
/// Tests for mid-measure key signature changes.
/// LILYPOND-REF: lily/key-engraver.cc
/// </summary>
[Trait("Category", "Unit")]
public class KeySignatureChangeTests
{
    private readonly ITestOutputHelper _output;

    public KeySignatureChangeTests(ITestOutputHelper output) => _output = output;

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
section A { melody { $m } }
structure { A }
render score ""test.svg"" { staff { melody } }
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
section A { melody { $m } }
structure { A }
render score ""test.svg"" { staff { melody } }
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
section A { melody { $m } }
structure { A }
render score ""test.svg"" { staff { melody } }
";
        var tree = SyntaxTree.Parse(source);
        var options = new SvgRenderOptions { EmbedFont = false };
        var svg = SvgGenerator.Generate(tree, options);

        _output.WriteLine(svg);

        // Should contain sharp glyphs for D major key signature (U+E013 = AccidentalSharp)
        Assert.Contains("\uE013", svg);
    }

    [Fact]
    public void KeyChange_CancellationNaturals_DifferentType()
    {
        // Change from sharps to flats: should show cancellation naturals
        var source = @"
part melody { clef treble }
key d major
phrase m { fis4 g a b | key f major c'4 bes a g | }
section A { melody { $m } }
structure { A }
render score ""test.svg"" { staff { melody } }
";
        var tree = SyntaxTree.Parse(source);
        var options = new SvgRenderOptions { EmbedFont = false };
        var svg = SvgGenerator.Generate(tree, options);

        _output.WriteLine(svg);

        // Should contain natural glyphs (U+E01D = AccidentalNatural) for cancellation
        Assert.Contains("\uE01D", svg);
        // Should contain flat glyphs (U+E021 = AccidentalFlat) for new F major key
        Assert.Contains("\uE021", svg);
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
section A { melody { $m } }
structure { A }
render score ""test.svg"" { staff { melody } }
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
section A { melody { $m } }
structure { A }
render score ""test.svg"" { staff { melody } }
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
        Assert.Contains("\uE01D", svg);
    }
}
