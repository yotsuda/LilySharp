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

using Xunit;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Tests.Svg;

[Trait("Category", "Unit")]
public class RenderSpecTests
{
    [Fact]
    public void ParseGrandStaff_ReturnsCorrectStructure()
    {
        var source = """
            title "Test"
            time 4/4

            phrase rh { c''4 d'' e'' f'' | }
            phrase lh { c4 e g c' | }

            section Main {
              melody { rh }
              bass { lh }
            }

            structure { Main }

            score "test" {
              grandStaff {
                staff treble melody
                staff bass bass
              }
            }
            """;

        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));

        var renderSpec = RenderSpecParser.FindFirst(tree);

        Assert.NotNull(renderSpec);
        Assert.Equal("test", renderSpec.Name);       // name = the basename
        Assert.Equal("test", renderSpec.OutputFile); // extension dropped (CLI picks format)
        Assert.True(renderSpec.HasGrandStaff);
        Assert.Single(renderSpec.Items);

        var grandStaffItem = renderSpec.Items[0] as GrandStaffRenderSpec;
        Assert.NotNull(grandStaffItem);
        Assert.Equal(2, grandStaffItem.GrandStaff.StaffCount);
        Assert.Equal(ClefType.Treble, grandStaffItem.GrandStaff.Staves[0].Clef);
        Assert.Equal("melody", grandStaffItem.GrandStaff.Staves[0].VoiceName);
        Assert.Equal(ClefType.Bass, grandStaffItem.GrandStaff.Staves[1].Clef);
        Assert.Equal("bass", grandStaffItem.GrandStaff.Staves[1].VoiceName);
    }

    [Fact]
    public void OmittedFilename_ParsesWithEmptyOutputFile()
    {
        // The output filename is optional: `score { … }` is valid and
        // yields an empty OutputFile, signalling the consumer to derive the name
        // from the input file (<input>.<ext>).
        var source = """
            title "Test"
            time 4/4

            phrase melody { c'4 d' e' f' | }

            section Main {
              melody { melody }
            }

            structure { Main }

            score {
              staff treble melody
            }
            """;

        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));

        var renderSpec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(renderSpec);
        Assert.Equal("score", renderSpec.Name);
        Assert.Equal("", renderSpec.OutputFile);
        Assert.Single(renderSpec.Items); // the staff still parses
    }

    [Fact]
    public void ParseSingleStaff_ReturnsCorrectStructure()
    {
        var source = """
            title "Test"
            time 4/4

            phrase melody { c'4 d' e' f' | }

            section Main {
              guitar { melody }
            }

            structure { Main }

            score "test" {
              staff treble guitar
            }
            """;

        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));

        var renderSpec = RenderSpecParser.FindFirst(tree);

        Assert.NotNull(renderSpec);
        Assert.Equal("test", renderSpec.Name);
        Assert.False(renderSpec.HasGrandStaff);
        Assert.Single(renderSpec.Items);

        var singleStaff = renderSpec.Items[0] as SingleStaffSpec;
        Assert.NotNull(singleStaff);
        Assert.Equal(ClefType.Treble, singleStaff.Staff.Clef);
        Assert.Equal("guitar", singleStaff.Staff.VoiceName);
    }

    [Fact]
    public void ParseMixedStaffTypes_ReturnsCorrectStructure()
    {
        var source = """
            title "Test"
            time 4/4

            phrase rh { c''4 d'' e'' f'' | }
            phrase lh { c4 e g c' | }
            phrase vocal { g'4 a' b' c'' | }

            section Main {
              singer { vocal }
              melody { rh }
              bass { lh }
            }

            structure { Main }

            score "test" {
              staff treble singer
              grandStaff {
                staff treble melody
                staff bass bass
              }
            }
            """;

        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));

        var renderSpec = RenderSpecParser.FindFirst(tree);

        Assert.NotNull(renderSpec);
        Assert.Equal("test", renderSpec.Name);
        Assert.True(renderSpec.HasGrandStaff);
        Assert.True(renderSpec.IsMultiStaff);
        Assert.Equal(2, renderSpec.Items.Length);

        // First item: single staff for vocal
        var vocalStaff = renderSpec.Items[0] as SingleStaffSpec;
        Assert.NotNull(vocalStaff);
        Assert.Equal("singer", vocalStaff.Staff.VoiceName);

        // Second item: grand staff for piano
        var pianoGrandStaff = renderSpec.Items[1] as GrandStaffRenderSpec;
        Assert.NotNull(pianoGrandStaff);
        Assert.Equal(2, pianoGrandStaff.GrandStaff.StaffCount);
    }

    [Fact]
    public void GetVoiceNames_ReturnsAllVoices()
    {
        var source = """
            title "Test"
            time 4/4

            phrase dummy { c'4 | }

            section Main {
              singer { dummy }
              rightHand { dummy }
              leftHand { dummy }
            }

            structure { Main }

            score "test" {
              staff treble singer
              grandStaff {
                staff treble rightHand
                staff bass leftHand
              }
            }
            """;

        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));

        var renderSpec = RenderSpecParser.FindFirst(tree);

        Assert.NotNull(renderSpec);
        var voiceNames = renderSpec.GetVoiceNames().ToList();
        Assert.Equal(3, voiceNames.Count);
        Assert.Contains("singer", voiceNames);
        Assert.Contains("rightHand", voiceNames);
        Assert.Contains("leftHand", voiceNames);
    }

    [Fact]
    public void Debug_DescendantNodes()
    {
        var source = """
            title "Test"
            time 4/4

            phrase rh { c''4 | }
            phrase lh { c4 | }

            section Main {
              melody { rh }
              bass { lh }
            }

            structure { Main }

            score "test" {
              grandStaff {
                staff treble melody
                staff bass bass
              }
            }
            """;

        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));

        var renders = tree.GetRoot().DescendantNodes().OfType<RenderDeclarationSyntax>().ToList();
        Assert.Single(renders);

        var render = renders[0];
        var descendants = render.DescendantNodes().ToList();
        var types = string.Join(", ", descendants.Select(d => d.GetType().Name));

        // This will fail and show the types
        Assert.Contains("GrandStaffRenderSyntax", types);
    }

    [Fact]
    public void Debug_GrandStaffStaves()
    {
        var source = """
            title "Test"
            time 4/4

            phrase rh { c''4 | }
            phrase lh { c4 | }

            section Main {
              melody { rh }
              bass { lh }
            }

            structure { Main }

            score "test" {
              grandStaff {
                staff treble melody
                staff bass bass
              }
            }
            """;

        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));

        var grandStaffs = tree.GetRoot().DescendantNodes().OfType<GrandStaffRenderSyntax>().ToList();
        Assert.Single(grandStaffs);

        var grandStaff = grandStaffs[0];
        var slotCount = grandStaff.SlotCount;
        var slots = new List<string>();
        for (int i = 0; i < slotCount; i++)
        {
            var child = grandStaff.GetChild(i);
            slots.Add($"[{i}] {child?.GetType().Name ?? "null"} (Kind={child?.Kind})");
        }
        var slotsStr = string.Join(", ", slots);

        var staves = grandStaff.Staves.ToList();
        Assert.True(staves.Count >= 2, $"Expected 2+ staves but got {staves.Count}. SlotCount={slotCount}, Slots: {slotsStr}");
    }
}
