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

using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// `part vn1 "Violin I" "Vn. I"`: a short name printed from the second system on — LilyPond's
/// shortInstrumentName (grammar feedback B6, 2026-10-13; on the part only, owner's decision).
/// MEASURED (LilyPond 2.26.0, Lab sessions/p903/b6): with the default short-indent of 0 the
/// short names still print, hanging into the margin; the first system keeps the long name.
/// </summary>
[Trait("Category", "Unit")]
public class ShortInstrumentNameTests
{
    private static string Book(string vn, string vc, string scoreItems = "staff vn1 staff vc") =>
        "octave absolute\n"
        + $"part vn1 {vn} {{ instrument violin }}\npart vc {vc} {{ instrument cello }}\n"
        + "section A { vn1 { " + string.Concat(Enumerable.Repeat("c''1 | ", 40))
        + "} vc { " + string.Concat(Enumerable.Repeat("c1 | ", 40)) + "} }\n"
        + "form { A }\nscore { " + scoreItems + " }";

    private static int Count(string svg, string text) => Regex.Matches(svg, ">" + Regex.Escape(text) + "<").Count;

    [Fact]
    public void ItParses_OnThePart()
    {
        var tree = SyntaxTree.Parse(Book("\"Violin I\" \"Vn. I\"", "\"Cello\""));
        Assert.DoesNotContain(tree.Diagnostics.Concat(SemanticValidation.Run(tree)), d => d.Severity == DiagnosticSeverity.Error);
        var parts = tree.GetRoot().DescendantNodes<PartDeclarationSyntax>().ToList();
        Assert.Equal(("Violin I", "Vn. I"), (parts[0].DisplayName, parts[0].ShortName));
        Assert.Equal(("Cello", (string?)null), (parts[1].DisplayName, parts[1].ShortName));
    }

    [Fact]
    public void TheFirstSystem_PrintsTheLongName_AndEveryLaterOneTheShort()
    {
        string all = SvgGenerator.Generate(SyntaxTree.Parse(Book("\"Violin I\" \"Vn. I\"", "\"Cello\" \"Vc.\"")));
        int systems = Regex.Matches(all, @">\d+<").Count; // the bar numbers that open systems 2..N
        Assert.True(systems >= 1, "the book should wrap to more than one system");
        Assert.Equal(1, Count(all, "Violin I"));
        Assert.Equal(1, Count(all, "Cello"));
        Assert.True(Count(all, "Vn. I") >= 1 && Count(all, "Vn. I") == Count(all, "Vc."));
    }

    [Fact]
    public void WithoutAShortName_LaterSystemsStayBlank()
    {
        string all = SvgGenerator.Generate(SyntaxTree.Parse(Book("\"Violin I\"", "\"Cello\"")));
        Assert.Equal(1, Count(all, "Violin I"));
        Assert.Equal(1, Count(all, "Cello"));
    }

    [Fact]
    public void ATildeOnTheStaff_SilencesBothNames()
    {
        string all = SvgGenerator.Generate(SyntaxTree.Parse(
            Book("\"Violin I\" \"Vn. I\"", "\"Cello\" \"Vc.\"", "staff ~vn1 staff vc")));
        Assert.Equal(0, Count(all, "Violin I") + Count(all, "Vn. I"));
        Assert.True(Count(all, "Vc.") >= 1);
    }

    [Fact]
    public void TheTwin_WritesShortInstrumentName_AndMusicXmlThePartAbbreviation()
    {
        var tree = SyntaxTree.Parse(Book("\"Violin I\" \"Vn. I\"", "\"Cello\""));
        string ly = new LilyPondExporter().Export(tree);
        Assert.Contains("instrumentName = \"Violin I\" shortInstrumentName = \"Vn. I\"", ly);
        Assert.DoesNotContain("shortInstrumentName = \"Cello\"", ly);
        string xml = new LilySharp.Core.MusicXml.MusicXmlExporter().Export(tree).ToXml().ToString();
        Assert.Matches(@"<part-name>Violin I</part-name>\s*<part-abbreviation>Vn\. I</part-abbreviation>", xml);
        Assert.Single(Regex.Matches(xml, "<part-abbreviation>"));
    }
}
