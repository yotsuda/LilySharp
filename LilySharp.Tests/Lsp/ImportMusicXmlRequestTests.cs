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

using System.IO;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// The <c>lilysharp/importMusicXml</c> request handler. The constructor does not
/// start listening until <c>RunAsync</c>, so the handler can be exercised directly
/// with null streams; it delegates to the well-tested importer, so these just guard
/// the request wiring (text vs file path, error surfacing).
/// </summary>
public class ImportMusicXmlRequestTests
{
    private static LilySharpLanguageServer Server() => new(Stream.Null, Stream.Null);

    [Fact]
    public void FromXmlText_ReturnsParseableLys()
    {
        var xml = new MusicXmlExporter().Export(SyntaxTree.Parse("""
            octave absolute
            time 4/4
            key c major
            c'4 d' e' f' | g'1 |
            """)).ToXml().ToString();

        var response = Server().ImportMusicXml(new ImportMusicXmlParams { XmlText = xml });

        Assert.Null(response.Error);
        Assert.NotNull(response.Lys);
        Assert.False(SyntaxTree.Parse(response.Lys!).HasErrors);
        Assert.Contains("octave absolute", response.Lys);
    }

    [Fact]
    public void MissingFile_ReturnsError()
    {
        var response = Server().ImportMusicXml(
            new ImportMusicXmlParams { FilePath = "no-such-file-9f3a2b.mxl" });

        Assert.NotNull(response.Error);
        Assert.Null(response.Lys);
    }

    [Fact]
    public void NoInput_ReturnsError()
    {
        var response = Server().ImportMusicXml(new ImportMusicXmlParams());

        Assert.NotNull(response.Error);
        Assert.Null(response.Lys);
    }
}
