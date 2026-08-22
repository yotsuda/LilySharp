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

            form main { Main }

            score main "test" {
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
        Assert.Equal("main", renderSpec.Name);       // Name = the form reference
        Assert.Equal("test", renderSpec.OutputFile); // basename (extension dropped; CLI picks format)
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

    // A part score restates the header for itself: `score main "vln" { title "Violin I" … }`.
    // Written once and read by all three tests below, so they cannot drift apart on what
    // the file says versus what each score says.
    private const string PerScoreHeaderSource = """
        title "File Title"
        composer "File Composer"
        octave absolute
        part vln
        part vla
        section A {
          vln { c''1 }
          vla { c'1 }
        }
        form main { ~A }
        score main { staff vln staff vla }
        score main "vln" {
          title "Violin I"
          composer "Score Composer"
          staff vln
        }
        score main "vla" {
          title "Viola"
          staff vla
        }
        """;

    private static MultiStaffScore CollectByName(string name)
    {
        var tree = SyntaxTree.Parse(PerScoreHeaderSource);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));
        var spec = RenderSpecParser.FindByName(tree, name);
        Assert.NotNull(spec);
        return LilySharp.Core.Svg.SvgGenerator.CollectScore(tree, spec);
    }

    [Fact]
    public void ScoreWithoutItsOwnHeader_KeepsTheFileHeader()
    {
        var score = CollectByName("main");
        Assert.Equal("File Title", score.Title);
        Assert.Equal("File Composer", score.Composer);
    }

    [Fact]
    public void ScoreHeader_OverridesTheFileHeaderForThatScoreOnly()
    {
        var vln = CollectByName("vln");
        Assert.Equal("Violin I", vln.Title);
        Assert.Equal("Score Composer", vln.Composer);

        // …and does not leak: the full score, collected from the same tree, is untouched.
        var main = CollectByName("main");
        Assert.Equal("File Title", main.Title);
        Assert.Equal("File Composer", main.Composer);
    }

    [Fact]
    public void ScoreStatingOnlyOne_InheritsTheOtherFromTheFile()
    {
        // `score main "vla"` restates the title and says nothing about the composer.
        var vla = CollectByName("vla");
        Assert.Equal("Viola", vla.Title);
        Assert.Equal("File Composer", vla.Composer);
    }

    [Fact]
    public void ScoreHeader_KeepsEveryOtherSourceOffsetIntact()
    {
        // A render item the parser does not recognise is SKIPPED by ParseList's
        // Advance(), which drops its width and shifts every following source offset —
        // the failure mode that once broke note highlighting after a part-header `key`.
        // So the header must round-trip and the notes must keep their real positions.
        var tree = SyntaxTree.Parse(PerScoreHeaderSource);
        Assert.Equal(PerScoreHeaderSource, tree.GetRoot().ToFullString());

        var score = CollectByName("vln");
        // The title's data-pos points INSIDE this score's own string, not the file's.
        int at = score.Header.Title;
        Assert.Equal("Violin I", PerScoreHeaderSource.Substring(at + 1, "Violin I".Length));
    }

    [Fact]
    public void StaffReferencingAPartNamedLikeAClef_UsesThePartsClef()
    {
        // `staff bass` where a part is literally named "bass": the lone clef-name word
        // is the PART name, not a bass-clef modifier with a missing part. Regression:
        // the part's declared clef was dropped and the staff fell back to treble.
        var tree = SyntaxTree.Parse("""
            octave absolute
            part bass { clef bass }
            section A { bass { c1 } }
            form main { A }
            score main { staff bass }
            """);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));

        var spec = RenderSpecParser.FindFirst(tree)!;
        var single = Assert.IsType<SingleStaffSpec>(Assert.Single(spec.Items));
        Assert.Equal("bass", single.Staff.VoiceName);
        Assert.Equal(ClefType.Bass, single.Staff.Clef);
    }

    [Fact]
    public void EnsembleWithAPitchNamedStaff_ReportsErrorWithoutCrashing()
    {
        // `staff a` / `staff b5` name a part with a pitch-letter token, which is not
        // a valid part name — the parser reports it and leaves a zero-width (empty)
        // VoiceName. With two+ plain staves the ensemble default-name pass runs;
        // it must SKIP the empty name rather than index [0] into "" and throw
        // IndexOutOfRangeException (found via TabRangeValidator -> FindAll).
        var tree = SyntaxTree.Parse("""
            part foo { clef treble }
            part a { clef treble }
            section A { foo { c'1 } a { c'1 } }
            form main { A }
            score main { staff foo staff a }
            """);
        Assert.True(tree.HasErrors);

        var specs = RenderSpecParser.FindAll(tree); // must not throw
        var single = Assert.IsType<SingleStaffSpec>(specs[0].Items[0]);
        Assert.Equal("Foo", single.Staff.InstrumentName); // valid name still auto-labeled
    }

    [Fact]
    public void OmittedFilename_ParsesWithEmptyOutputFile()
    {
        // The output filename is optional: `score main { … }` is valid and
        // yields an empty OutputFile, signalling the consumer to derive the name
        // from the input file (<input>.<ext>).
        var source = """
            title "Test"
            time 4/4

            phrase melody { c'4 d' e' f' | }

            section Main {
              melody { melody }
            }

            form main { Main }

            score main {
              staff treble melody
            }
            """;

        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));

        var renderSpec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(renderSpec);
        Assert.Equal("main", renderSpec.Name);
        Assert.Equal("", renderSpec.OutputFile);
        Assert.Single(renderSpec.Items); // the staff still parses
    }

    [Fact]
    public void ResolveOutputStem_MainIsStem_EveryOtherScoreAppendsItsName()
    {
        var tree = SyntaxTree.Parse("""
            part m { section A { c4 d e f | } }
            form main { A }
            form other { A A }
            score main { staff m }
            score other { staff m }
            score other "custom" { staff m }
            """);
        var specs = RenderSpecParser.FindAll(tree);

        // main → the input stem itself; every other score appends its name to the
        // stem — its form name (song + `score other` → song-other) or an explicit
        // basename (song + `score other "custom"` → song-custom).
        Assert.Equal("song", specs[0].ResolveOutputStem("song"));
        Assert.Equal("song-other", specs[1].ResolveOutputStem("song"));
        Assert.Equal("song-custom", specs[2].ResolveOutputStem("song"));
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

            form main { Main }

            score main "test" {
              staff treble guitar
            }
            """;

        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));

        var renderSpec = RenderSpecParser.FindFirst(tree);

        Assert.NotNull(renderSpec);
        Assert.Equal("main", renderSpec.Name);
        Assert.False(renderSpec.HasGrandStaff);
        Assert.Single(renderSpec.Items);

        var singleStaff = renderSpec.Items[0] as SingleStaffSpec;
        Assert.NotNull(singleStaff);
        Assert.Equal(ClefType.Treble, singleStaff.Staff.Clef);
        Assert.Equal("guitar", singleStaff.Staff.VoiceName);
    }

    [Fact]
    public void Instrument_PresetPlusLabel_LabelShown_PresetDrivesClef()
    {
        // `instrument cello "Cello I"` — the cello preset drives the default clef
        // (bass), while the quoted label overrides the displayed instrument name.
        var tree = SyntaxTree.Parse(
            "part vc { instrument cello \"Cello I\" }\n" +
            "section A { vc { c4 d e f } }\nform main { A }\nscore \"s\" { staff vc }");
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));

        var staff = (RenderSpecParser.FindFirst(tree)!.Items[0] as SingleStaffSpec)!.Staff;
        Assert.Equal("Cello I", staff.InstrumentName);   // quoted label is shown
        Assert.Equal(ClefType.Bass, staff.Clef);         // cello preset → bass clef
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

            form main { Main }

            score main "test" {
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
        Assert.Equal("main", renderSpec.Name);
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

            form main { Main }

            score main "test" {
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

            form main { Main }

            score main "test" {
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

            form main { Main }

            score main "test" {
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
