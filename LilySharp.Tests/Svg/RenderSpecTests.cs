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

            rh = { c''4 d'' e'' f'' | }
            lh = { c4 e g c' | }

            section Main {
              melody { rh }
              bass { lh }
            }

            structure { Main }

            render piano "test.svg" {
              grandStaff {
                staff treble { melody }
                staff bass { bass }
              }
            }
            """;

        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));

        var renderSpec = RenderSpecParser.FindFirst(tree);

        Assert.NotNull(renderSpec);
        Assert.Equal("piano", renderSpec.Name);
        Assert.Equal("test.svg", renderSpec.OutputFile);
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
    public void ParseSingleStaff_ReturnsCorrectStructure()
    {
        var source = """
            title "Test"
            time 4/4

            melody = { c'4 d' e' f' | }

            section Main {
              guitar { melody }
            }

            structure { Main }

            render score "test.svg" {
              staff treble { guitar }
            }
            """;

        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));

        var renderSpec = RenderSpecParser.FindFirst(tree);

        Assert.NotNull(renderSpec);
        Assert.Equal("score", renderSpec.Name);
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

            rh = { c''4 d'' e'' f'' | }
            lh = { c4 e g c' | }
            vocal = { g'4 a' b' c'' | }

            section Main {
              singer { vocal }
              melody { rh }
              bass { lh }
            }

            structure { Main }

            render pianoVocal "test.svg" {
              staff treble { singer }
              grandStaff {
                staff treble { melody }
                staff bass { bass }
              }
            }
            """;

        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));

        var renderSpec = RenderSpecParser.FindFirst(tree);

        Assert.NotNull(renderSpec);
        Assert.Equal("pianoVocal", renderSpec.Name);
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

            dummy = { c'4 | }

            section Main {
              singer { dummy }
              rightHand { dummy }
              leftHand { dummy }
            }

            structure { Main }

            render test "test.svg" {
              staff treble { singer }
              grandStaff {
                staff treble { rightHand }
                staff bass { leftHand }
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

            rh = { c''4 | }
            lh = { c4 | }

            section Main {
              melody { rh }
              bass { lh }
            }

            structure { Main }

            render piano "test.svg" {
              grandStaff {
                staff treble { melody }
                staff bass { bass }
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

            rh = { c''4 | }
            lh = { c4 | }

            section Main {
              melody { rh }
              bass { lh }
            }

            structure { Main }

            render piano "test.svg" {
              grandStaff {
                staff treble { melody }
                staff bass { bass }
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
