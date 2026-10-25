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

using System;
using System.IO;
using System.Linq;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// The <c>lilysharp/ranges</c> request behind the preview's Range panel (owner's request
/// 2026-10-25): each part of the score SHOWN, its lowest and highest note, and where each is
/// written — the drums left out.
/// </summary>
public class RangesRequestTests
{
    private const string Doc =
        "part vn { midiInstrument \"violin\" }\n" +
        "part pno\n" +
        "part kit { clef percussion }\n" +
        "section A {\n" +
        "  vn { c'4 d e f | }\n" +
        "  pno { e f g a | }\n" +
        "  kit { bd4 bd sn bd | }\n" +
        "}\n\nform { A }\n\n" +
        "score {\n  staff vn\n  staff pno\n  staff kit\n}\n\n" +
        "score pno {\n  staff pno\n}\n";

    private static RangesResponse Ranges(string? renderName)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new Uri("file:///ranges.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = Doc, Version = 1, LanguageId = "lilysharp" },
        });
        return server.GetRanges(new RangesParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            RenderName = renderName,
        });
    }

    [Fact]
    public void NamesEachPitchedPart_NotTheDrums()
    {
        var r = Ranges(null);
        Assert.Null(r.Error);
        Assert.Equal(new[] { "vn", "pno" }, r.Parts!.Select(p => p.Name));
        var vn = r.Parts![0];
        Assert.StartsWith("f |", Doc[vn.High.Positions.Single()..].TrimStart());
        Assert.Equal(vn.Low.Pitch + 5, vn.High.Pitch);
    }

    [Fact]
    public void ReadsTheScoreShown()
    {
        var r = Ranges("pno");
        Assert.Equal(new[] { "pno" }, r.Parts!.Select(p => p.Name));
        Assert.Equal(("E", "A"), (r.Parts![0].Low.Name[..1], r.Parts![0].High.Name[..1]));
    }
}
