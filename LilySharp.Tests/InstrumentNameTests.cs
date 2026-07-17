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
section A { violin { $m } }
form main { A }
score main ""test"" { staff violin }
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
section A { vln1 { $m } }
form main { A }
score main ""test"" { staff vln1 }
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
section A { melody { $m } }
form main { A }
score main ""test"" { staff melody }
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
section A { rh { $rhM } lh { $lhM } }
form main { A }
score main ""test"" { grandStaff { staff rh staff lh } }
";
        var tree = SyntaxTree.Parse(source);
        var options = new SvgRenderOptions { EmbedFont = false };
        var svg = SvgGenerator.Generate(tree, options);

        // Should contain "Piano" text
        Assert.Contains("Piano", svg);
        Assert.Contains("font-family=\"serif\"", svg);
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
section A { vln { $m } vla { $m } }
form main { A }
score main ""test"" { staff vln staff vla }
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
section A { vln { $m } vla { $m } }
form main { A }
score main ""test"" { staff vln staff vla }
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
