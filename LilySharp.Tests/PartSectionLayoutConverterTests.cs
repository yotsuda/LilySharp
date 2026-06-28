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

using LilySharp.Core.Editing;
using LilySharp.Core.Syntax;
using Xunit;
using Xunit.Abstractions;

namespace LilySharp.Tests;

/// <summary>
/// Converting a .lys document between the section-major and part-major layouts.
/// </summary>
[Trait("Category", "Unit")]
public class PartSectionLayoutConverterTests
{
    private readonly ITestOutputHelper _output;
    public PartSectionLayoutConverterTests(ITestOutputHelper output) => _output = output;

    private const string SectionMajor = """
        part low { clef bass }
        part high { clef treble }
        section A { low { c4 d } high { e'4 f' } }
        section B { low { g,4 a, } high { b'4 c'' } }
        structure { A B }
        score "x" { staff low  staff high }
        """;

    [Fact]
    public void Detect_IdentifiesBothLayouts()
    {
        Assert.Equal(LayoutForm.SectionMajor, PartSectionLayoutConverter.Detect(SectionMajor));
        var pm = PartSectionLayoutConverter.Convert(SectionMajor);
        Assert.Equal(LayoutForm.PartMajor, PartSectionLayoutConverter.Detect(pm!));
    }

    [Fact]
    public void Convert_SectionMajor_ToPartMajor_KeepsCellsAndPassthrough()
    {
        var pm = PartSectionLayoutConverter.Convert(SectionMajor);
        Assert.NotNull(pm);
        _output.WriteLine(pm);

        // Part-major shape: each part owns its inner sections, music preserved.
        Assert.Contains("part low {", pm);
        Assert.Contains("section A { c4 d }", pm);
        Assert.Contains("section B { g,4 a, }", pm);
        Assert.Contains("part high {", pm);
        Assert.Contains("section A { e'4 f' }", pm);
        // Attributes and every other top-level item survive verbatim.
        Assert.Contains("clef bass", pm);
        Assert.Contains("structure { A B }", pm);
        Assert.Contains("score \"x\" { staff low  staff high }", pm);
        // The result is valid .lys.
        Assert.False(SyntaxTree.Parse(pm!).HasErrors);
    }

    [Fact]
    public void Convert_RoundTrips_BackToSectionMajor()
    {
        var pm = PartSectionLayoutConverter.Convert(SectionMajor);
        var sm2 = PartSectionLayoutConverter.Convert(pm!);
        Assert.NotNull(sm2);
        _output.WriteLine(sm2);

        Assert.Equal(LayoutForm.SectionMajor, PartSectionLayoutConverter.Detect(sm2!));
        Assert.Contains("section A {", sm2);
        Assert.Contains("low { c4 d }", sm2);
        Assert.Contains("high { e'4 f' }", sm2);
        Assert.Contains("structure { A B }", sm2);
        Assert.False(SyntaxTree.Parse(sm2!).HasErrors);
    }

    [Fact]
    public void Convert_Unknown_ReturnsNull()
    {
        // No part blocks / inner sections — nothing to transpose.
        Assert.Null(PartSectionLayoutConverter.Convert("title \"x\"\nsection A { c4 d e f }\n"));
    }

    [Fact]
    public void Convert_FileWithSyntaxError_ReturnsNull_NoCorruption()
    {
        // Unbalanced braces — must NOT be transposed (the caller overwrites the
        // whole document, so a malformed file would be mangled).
        var broken = "part low { clef bass }\nsection A { low { c4 d } \n";
        Assert.Null(PartSectionLayoutConverter.Convert(broken));
    }

    [Fact]
    public void Convert_CellEndingInLineComment_DoesNotSwallowBrace()
    {
        var src = "part low { clef bass }\nsection A { low { c4 d e f // melody\n} }\nstructure { A }\n";
        var converted = PartSectionLayoutConverter.Convert(src);
        Assert.NotNull(converted);
        // The // comment must not comment out the regenerated closing brace.
        Assert.False(SyntaxTree.Parse(converted!).HasErrors);
        Assert.Contains("// melody", converted);
    }

    [Fact]
    public void Convert_KeepsCommentAboveFirstStructuralBlock()
    {
        var src = "// verse arrangement\npart low { clef bass }\nsection A { low { c4 d } }\nstructure { A }\n";
        var converted = PartSectionLayoutConverter.Convert(src);
        Assert.NotNull(converted);
        Assert.Contains("// verse arrangement", converted);
        Assert.False(SyntaxTree.Parse(converted!).HasErrors);
    }
}
