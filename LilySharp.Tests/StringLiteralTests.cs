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
using LilySharp.Core.LilyPond;
using LilySharp.Core.MusicXml;
using LilySharp.Core.MusicXmlImport;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Lily#'s string literals follow C#'s grammar (owner's decision 2026-09-30): a regular
/// literal decodes C#'s escapes and refuses any other (LYS0036); a verbatim <c>@"…"</c> takes
/// a backslash as written and <c>""</c> as one quote. Until then no reader decoded anything,
/// and <c>Trim('"')</c> ate an escaped quote at the end: <c>@text("say \"hi\"")</c> printed
/// <c>say \"hi\</c>.
/// </summary>
public sealed class StringLiteralTests
{
    [Theory]
    [InlineData("\"say \\\"hi\\\"\"", "say \"hi\"")]
    [InlineData("\"a\\\\b\"", "a\\b")]
    [InlineData("\"tab\\there\"", "tab\there")]
    [InlineData("\"line\\nbreak\"", "line\nbreak")]
    [InlineData("\"\\u00e9t\\u00e9\"", "été")]
    [InlineData("\"\\x41\\x042\"", "AB")]
    [InlineData("\"\\U0001F3B5\"", "🎵")]
    [InlineData("@\"C:\\tabs\\new\"", "C:\\tabs\\new")]
    [InlineData("@\"say \"\"hi\"\"\"", "say \"hi\"")]
    [InlineData("bare", "bare")]
    public void Value_DecodesAsCSharpDoes(string literal, string expected)
        => Assert.Equal(expected, StringLiteral.Value(literal));

    [Theory]
    [InlineData("\"\\p\"", 1, 2)]
    [InlineData("\"a\\u12\"", 2, 4)]
    [InlineData("\"\\x\"", 1, 2)]
    public void AnEscapeCSharpDoesNotDefine_IsAnError(string literal, int offset, int length)
    {
        var e = Assert.Single(StringLiteral.Errors(literal));
        Assert.Equal((offset, length), (e.Offset, e.Length));
    }

    [Fact]
    public void AVerbatimLiteral_HasNoEscapesToGetWrong()
        => Assert.Empty(StringLiteral.Errors("@\"\\p \\q \\\"")); // @"\p \q \"  — then open

    [Theory]
    [InlineData("say \"hi\"")]
    [InlineData("C:\\path\\to")]
    [InlineData("two\nlines\tand\0nul")]
    [InlineData("\u0007bell")]
    public void Quote_IsReadBackAsTheSameValue(string value)
        => Assert.Equal(value, StringLiteral.Value(StringLiteral.Quote(value)));

    private static SyntaxTree Book(string title, string music)
        => SyntaxTree.Parse($"octave absolute\ntitle {title}\ntime 4/4\npart m {{ clef treble }}\n"
            + $"section S {{ m {{ {music} }} }}\nform main {{ ~S }}\nscore main {{ staff m }}\n");

    [Fact]
    public void TheParsedFile_ReadsTheDecodedValue_AndReportsABadEscape()
    {
        var tree = Book("\"Say \\\"Hi\\\"\"", "c'1@text(@\"C:\\fold\") |");
        Assert.DoesNotContain(tree.Diagnostics, d => d.Code == DiagnosticCodes.InvalidEscape);
        var title = tree.GetRoot().DescendantNodes<MetadataDeclarationSyntax>().Single(m => m.Keyword == "title");
        Assert.Equal("Say \"Hi\"", title.StringValue);
        var text = tree.GetRoot().DescendantNodes<MusicMarkSyntax>().Single();
        Assert.Equal("C:\\fold", Core.Semantics.AnnotationValues.Text(text));

        var bad = Book("\"x\"", "c'1@text(\"a\\qb\") |");
        var d = Assert.Single(bad.Diagnostics, x => x.Code == DiagnosticCodes.InvalidEscape);
        Assert.Equal("\\q", bad.Text.Substring(d.Span.Start, d.Span.Length));
    }

    [Fact]
    public void TheExports_WriteTheValue_InTheirOwnSpelling()
    {
        var tree = Book("\"Say \\\"Hi\\\"\"", "c'1@text(@\"C:\\fold\") |");
        // LilyPond: its own escapes around the decoded value — not the Lily# escapes doubled.
        string ly = new LilyPondExporter().Export(tree);
        Assert.Contains("\"Say \\\"Hi\\\"\"", ly);
        Assert.Contains("\"C:\\\\fold\"", ly);
        // MusicXML: the plain value.
        string xml = new MusicXmlExporter().Export(tree).ToXml().ToString();
        Assert.Contains("C:\\fold</words>", xml);
        Assert.Contains("Say \"Hi\"", System.Net.WebUtility.HtmlDecode(xml));
        // …and the import writes it back as a literal that reads the same value.
        var (lys, _) = new MusicXmlImporter().Import(xml);
        var back = SyntaxTree.Parse(lys);
        Assert.DoesNotContain(back.Diagnostics, d => d.Code == DiagnosticCodes.InvalidEscape);
        Assert.Equal("C:\\fold", Core.Semantics.AnnotationValues.Text(back.GetRoot().DescendantNodes<MusicMarkSyntax>().First(
            m => Core.Semantics.AnnotationValues.Text(m) != null)));
    }
}
