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
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// lilysharp/convertOctaves on a text held outside any editor — a piece Compose with AI wrote in
/// absolute octaves, opened in relative ones (owner's request 2026-10-27).
/// </summary>
[Trait("Category", "Unit")]
public class ConvertOctavesTextTests
{
    [Fact]
    public void APieceInAbsoluteOctaves_ComesBackRelative_WithNoDocumentOpen()
    {
        const string text = "title \"Little Tune\"\noctave absolute\ntime 4/4\npart p1 { clef treble }\nsection A {\n  p1 {\n    c'4 e'4 g'4 c''4 | b'2 g'2 |\n  }\n}\nform { A }\nscore { staff p1 }\n";
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var response = server.ConvertOctaves(new ConvertOctavesParams { Text = text, Absolute = false });
        Assert.True(response.Success, response.Error);
        Assert.DoesNotContain("octave absolute", response.NewText);
        Assert.Contains("title \"Little Tune\"", response.NewText);
        Assert.True(response.ChangedNotes > 0);
    }

    [Fact]
    public void NeitherATextNorAnOpenDocument_IsSaidSo()
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var response = server.ConvertOctaves(new ConvertOctavesParams { Absolute = false });
        Assert.False(response.Success);
    }
}
