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
part melody { clef: treble }
phrase m { c'4 d clef bass c,4 d | }
section A { melody { $m } }
structure { A }
render score ""test.svg"" { staff { melody } }
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
    public void ClefChangeItem_ZeroDuration()
    {
        var clefChange = new ClefChangeItem(ClefType.Bass, 0);
        Assert.Equal(Fraction.Zero, clefChange.Duration);
    }

    [Fact]
    public void ClefChange_MultipleMeasures_TrackedCorrectly()
    {
        var source = @"
part melody { clef: treble }
phrase m { c'4 d e f | clef bass c,4 d e f | clef treble c'4 d e f | }
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
part melody { clef: treble }
phrase m { c'4 d e f | g4 a clef bass c,4 d | }
section A { melody { $m } }
structure { A }
render score ""test.svg"" { staff { melody } }
";
        var tree = SyntaxTree.Parse(source);
        var options = new SvgRenderOptions { EmbedFont = false };
        var svg = SvgGenerator.Generate(tree, options);

        _output.WriteLine(svg);

        // Should contain the bass clef change glyph (U+E084 = FClefChange)
        Assert.Contains("\uE084", svg);  // FClefChange glyph
    }

    [Fact]
    public void ClefChange_SystemStartClef_MatchesActiveClef()
    {
        // Use enough measures to force a natural system break.
        // The clef changes to bass at measure 3, so system 2 should start with bass clef.
        var source = @"
part melody { clef: treble }
phrase m { c'4 d e f | g4 a b c' | clef bass c,4 d e f | g4 a b c | e4 f g a | b4 c d e | g4 a b c | e4 f g a | }
section A { melody { $m } }
structure { A }
render score ""test.svg"" { staff { melody } }
";
        var tree = SyntaxTree.Parse(source);
        var options = new SvgRenderOptions { EmbedFont = false };
        var svg = SvgGenerator.Generate(tree, options);

        _output.WriteLine(svg);

        // Find system-start clef glyphs: text elements with class="music" but no data-pos
        var lines = svg.Split('\n');
        var systemClefs = new List<string>();
        foreach (var line in lines)
        {
            if (line.Contains("class=\"music\"") && !line.Contains("data-pos"))
            {
                foreach (char c in line)
                {
                    if (c == '\uE085') systemClefs.Add("Treble");
                    else if (c == '\uE083') systemClefs.Add("Bass");
                    else if (c == '\uE07F') systemClefs.Add("Alto");
                }
            }
        }

        _output.WriteLine($"System-start clefs: {string.Join(", ", systemClefs)}");
        Assert.True(systemClefs.Count >= 2, $"Expected at least 2 system clefs, got {systemClefs.Count}");
        Assert.Equal("Treble", systemClefs[0]);  // System 1 starts with treble
        Assert.Equal("Bass", systemClefs[1]);     // System 2 starts with bass (after clef change)
    }
}
