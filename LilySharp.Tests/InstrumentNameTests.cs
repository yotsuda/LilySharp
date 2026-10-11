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

using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Tests for instrument name display.
/// LILYPOND-REF: lily/instrument-name-engraver.cc
/// </summary>
[Trait("Category", "Unit")]
public class InstrumentNameTests
{
    [Fact]
    public void StaffSpec_InstrumentName_FromInlineDisplayName()
    {
        var source = @"
part violin ""Violin""
phrase m { c4 d e f }
section A { violin { m } }
form { A }
score test { staff violin }
";
        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);
        var single = Assert.IsType<SingleStaffSpec>(spec.Items[0]);
        Assert.Equal("Violin", single.Staff.InstrumentName);
    }

    [Fact]
    public void StaffSpec_InstrumentName_StringLiteral()
    {
        var source = @"
part vln1 ""Violin I""
phrase m { c4 d e f }
section A { vln1 { m } }
form { A }
score test { staff vln1 }
";
        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);
        var single = Assert.IsType<SingleStaffSpec>(spec.Items[0]);
        Assert.Equal("Violin I", single.Staff.InstrumentName);
    }

    [Fact]
    public void StaffSpec_NoInstrumentName_ReturnsNull()
    {
        var source = @"
part melody { clef treble }
phrase m { c4 d e f }
section A { melody { m } }
form { A }
score test { staff melody }
";
        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);
        var single = Assert.IsType<SingleStaffSpec>(spec.Items[0]);
        Assert.Null(single.Staff.InstrumentName);
    }

    [Fact]
    public void GrandStaff_InstrumentName_CenteredInSvg()
    {
        var source = @"
part rh ""Piano"" { clef treble }
part lh { clef bass }
phrase rhM { c4 d e f }
phrase lhM { c,4 d e f }
section A { rh { rhM } lh { lhM } }
form { A }
score test { grandStaff { staff rh staff lh } }
";
        var tree = SyntaxTree.Parse(source);
        var options = new SvgRenderOptions { EmbedFont = false };
        var svg = SvgGenerator.Generate(tree, options);

        // Should contain "Piano" text
        Assert.Contains("Piano", svg);
        // The root font-family names the bundled face with the generic as its fallback.
        Assert.Contains(", serif\"", svg);
        // LILYPOND-REF: scm/define-grobs.scm:1711 — self-alignment-X = CENTER
        Assert.Contains("text-anchor=\"middle\"", svg);
    }

    [Fact]
    public void MultiStaff_EachStaffHasOwnName()
    {
        var source = @"
part vln ""Violin"" { clef treble }
part vla ""Viola"" { clef alto }
phrase m { c4 d e f }
section A { vln { m } vla { m } }
form { A }
score test { staff vln staff vla }
";
        var tree = SyntaxTree.Parse(source);
        var options = new SvgRenderOptions { EmbedFont = false };
        var svg = SvgGenerator.Generate(tree, options);

        Assert.Contains("Violin", svg);
        Assert.Contains("Viola", svg);
    }

    [Fact]
    public void ViewBox_UsesIndentForInstrumentNames()
    {
        // Multi-staff render spec needed for instrument name display
        var source = @"
part vln ""Violin I"" { clef treble }
part vla ""Viola"" { clef alto }
phrase m { c4 d e f }
section A { vln { m } vla { m } }
form { A }
score test { staff vln staff vla }
";
        var tree = SyntaxTree.Parse(source);
        var options = new SvgRenderOptions { EmbedFont = false };
        var svg = SvgGenerator.Generate(tree, options);

        // LILYPOND-REF: ly/paper-defaults-init.ly — indent creates space for instrument names
        // ViewBox starts at 0 (no negative X); indent shifts staff lines right
        Assert.Contains("viewBox=\"0 0", svg);
        // Instrument name text should be present
        Assert.Contains("Violin I", svg);
        Assert.Contains("Viola", svg);
    }

    // ===== A preset's printed name, and where it shows (owner's decision 2026-10-11) =====
    // Until then the preset WORD was the label — "alto-sax", "piano-right", lesson 1's
    // lower-case "cello" — on every staff, a score of one staff included.

    private static List<string?> Labels(string book)
    {
        var spec = RenderSpecParser.FindFirst(SyntaxTree.Parse(book));
        Assert.NotNull(spec);
        var labels = new List<string?>();
        void Walk(IEnumerable<RenderItemSpec> items)
        {
            foreach (var item in items)
                switch (item)
                {
                    case SingleStaffSpec s: labels.Add(s.Staff.InstrumentName); break;
                    case GrandStaffRenderSpec g: Walk(g.GrandStaff.Members); break;
                }
        }
        Walk(spec!.Items);
        return labels;
    }

    [Fact]
    public void APreset_PrintsItsName_BesideAnotherInstrument_AndNotAlone()
    {
        const string parts = "part sx { instrument alto-sax section S { c'1 } }\npart vc { instrument cello section S { c1 } }\nform { ~S }\n";
        Assert.Equal(new[] { "Alto Saxophone", "Cello" }, Labels(parts + "score { staff sx staff vc }"));
        // One instrument: no label — as a part name gives none on a score of one staff.
        Assert.Equal(new string?[] { null }, Labels(parts + "score { staff vc }"));
        // …and a tab of the same part is not a second instrument.
        Assert.Equal(new string?[] { null }, Labels("part b { instrument bass section S { c1 } }\nform { ~S }\nscore { staff b  tab b }"));
    }

    [Fact]
    public void TheWritersOwnLabel_PrintsAlways_AndWinsOverThePresetsName()
    {
        // `instrument trumpet "Tpt."` and `part X "…"` are the writer's words: printed as
        // written, even alone.
        Assert.Equal(new[] { "Tpt." }, Labels("part t { instrument trumpet \"Tpt.\" section S { c'1 } }\nform { ~S }\nscore { staff t }"));
        Assert.Equal(new[] { "Lead" }, Labels("part t \"Lead\" { instrument trumpet section S { c'1 } }\nform { ~S }\nscore { staff t }"));
        // `staff ~t` still says no.
        Assert.Equal(new string?[] { null, "Cello" }, Labels(
            "part t { instrument trumpet section S { c'1 } }\npart vc { instrument cello section S { c1 } }\nform { ~S }\nscore { staff ~t staff vc }"));
    }

    [Fact]
    public void AGrandStaff_IsOneInstrument_ThatShowsItsSharedNameOnce()
    {
        const string piano = "part rh { instrument piano-right section S { c'1 } }\npart lh { instrument piano-left section S { c1 } }\n";
        // A solo piano: one instrument, no label (it used to print "piano-right" and "piano-left").
        Assert.Equal(new string?[] { null, null }, Labels(piano + "form { ~S }\nscore { grandStaff { staff rh staff lh } }"));
        // Beside a singer: "Piano" once, on the upper staff — the page centres a group's one
        // name across its brace (GrandStaff_InstrumentName_CenteredInSvg).
        Assert.Equal(new string?[] { "Soprano", "Piano", null }, Labels(piano
            + "part v { instrument soprano section S { c'1 } }\nform { ~S }\nscore { staff v  grandStaff { staff rh staff lh } }"));
        // A choirStaff is its members: each voice is an instrument and shows its name.
        Assert.Equal(new[] { "Soprano", "Alto" }, Labels(
            "part s { instrument soprano section S { c'1 } }\npart a { instrument alto section S { c'1 } }\nform { ~S }\nscore { choirStaff { staff s staff a } }"));
    }

    [Fact]
    public void ASoloPresetsPart_IsStillNamedInMusicXml_UnprintedAsThePageIs()
    {
        // A reader's part list still calls a solo cello "Cello" — with print-object="no",
        // since the page prints no label beside it.
        string xml = new LilySharp.Core.MusicXml.MusicXmlExporter()
            .Export(SyntaxTree.Parse("part vc { instrument cello section S { c1 } }\nform { ~S }\nscore { staff vc }"))
            .ToXml().ToString();
        Assert.Contains("<part-name print-object=\"no\">Cello</part-name>", xml);
    }

    [Fact]
    public void Staff_InstrumentName_PropagatedFromStaffSpec()
    {
        // Use MeasureCollector to create a valid voice
        var tree = SyntaxTree.Parse("{ c4 }");
        var collector = new MeasureCollector();
        var score = collector.Collect(tree, null);
        var voice = score.Voice;

        var staff = Staff.Create(ClefType.Treble, voice, "Violin");
        Assert.Equal("Violin", staff.InstrumentName);
    }

    [Fact]
    public void Staff_InstrumentName_DefaultNull()
    {
        var tree = SyntaxTree.Parse("{ c4 }");
        var collector = new MeasureCollector();
        var score = collector.Collect(tree, null);
        var voice = score.Voice;

        var staff = Staff.Create(ClefType.Treble, voice);
        Assert.Null(staff.InstrumentName);
    }
}
