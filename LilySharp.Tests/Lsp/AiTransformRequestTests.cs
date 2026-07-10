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
using Microsoft.VisualStudio.LanguageServer.Protocol;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// The custom AI collaborative-editing requests (docs/ai-collab-design):
/// <c>lilysharp/checkCandidate</c> (validate-and-self-repair), <c>lilysharp/renderText</c>
/// (offscreen candidate render), and <c>lilysharp/factsForRange</c> (resolved facts).
/// The constructor does not start listening until <c>RunAsync</c>, so handlers are
/// exercised directly with null streams (as ImportMusicXmlRequestTests does).
/// </summary>
public class AiTransformRequestTests
{
    private static LilySharpLanguageServer Server() => new(Stream.Null, Stream.Null);

    private const string ValidDoc = """
        octave absolute
        time 4/4
        key c major
        c'4 d' e' f' | g'1 |
        """;

    // ---- checkCandidate ----

    [Fact]
    public void CheckCandidate_CleanText_HasNoErrors()
    {
        var response = Server().CheckCandidate(new CheckCandidateParams { Text = ValidDoc });

        Assert.False(response.HasErrors);
        Assert.DoesNotContain(response.Diagnostics, d => d.Severity == "error");
    }

    [Fact]
    public void CheckCandidate_BrokenText_ReportsErrorWithPosition()
    {
        // An unterminated brace / stray token the parser rejects.
        var response = Server().CheckCandidate(new CheckCandidateParams
        {
            Text = "octave absolute\npart m { section A { c'4 \n",
        });

        Assert.True(response.HasErrors);
        var error = response.Diagnostics.First(d => d.Severity == "error");
        Assert.NotEqual(string.Empty, error.Message);
        Assert.True(error.Offset >= 0);
    }

    [Fact]
    public void CheckCandidate_RejectsLilyPondConstructs()
    {
        // Backslash annotations are a Lily# hard error (backslash is tab-only) — this
        // is exactly the kind of mistake the self-repair loop catches before showing.
        var response = Server().CheckCandidate(new CheckCandidateParams
        {
            Text = "octave absolute\ntime 4/4\nc'4\\staccato d' e' f' |",
        });

        Assert.True(response.HasErrors);
    }

    // ---- renderText ----

    [Fact]
    public void RenderText_ValidDoc_ReturnsSvg()
    {
        var response = Server().RenderText(new RenderTextParams { Text = ValidDoc });

        Assert.Null(response.Error);
        Assert.NotNull(response.Svg);
        Assert.Contains("<svg", response.Svg);
    }

    [Fact]
    public void RenderText_BrokenDoc_ReturnsErrorNotSvg()
    {
        var response = Server().RenderText(new RenderTextParams
        {
            Text = "octave absolute\npart m { section A { c'4 \n",
        });

        Assert.Null(response.Svg);
        Assert.NotNull(response.Error);
    }

    // ---- factsForRange ----

    [Fact]
    public void FactsForRange_FullRange_ReturnsResolvedPitches()
    {
        var server = Server();
        var uri = new Uri("file:///facts-test.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = ValidDoc, Version = 1, LanguageId = "lilysharp" },
        });

        var response = server.FactsForRange(new FactsForRangeParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Start = 0,
            End = ValidDoc.Length,
        });

        Assert.Null(response.Error);
        // c d e f g = 5 notes across the two measures.
        Assert.True(response.Pitches.Length >= 5, $"expected >=5 pitches, got {response.Pitches.Length}");
        Assert.Contains(response.Pitches, p => p.Written == "c'");
        // octave-absolute c' resolves to a concrete absolute pitch (letter + octave).
        Assert.All(response.Pitches, p => Assert.False(string.IsNullOrWhiteSpace(p.Resolved)));
    }

    [Fact]
    public void FactsForRange_Subrange_LimitsToRange()
    {
        var server = Server();
        var uri = new Uri("file:///facts-sub.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = ValidDoc, Version = 1, LanguageId = "lilysharp" },
        });

        // Start just before "g'1" (the second measure) so only g is in range.
        int gStart = ValidDoc.IndexOf("g'1", StringComparison.Ordinal);
        var response = server.FactsForRange(new FactsForRangeParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Start = gStart,
            End = ValidDoc.Length,
        });

        Assert.Null(response.Error);
        Assert.Single(response.Pitches);
        Assert.Equal("g'", response.Pitches[0].Written);
    }

    [Fact]
    public void FactsForRange_UnknownDocument_ReturnsError()
    {
        var response = Server().FactsForRange(new FactsForRangeParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = new Uri("file:///nope.lys") },
            Start = 0,
            End = 10,
        });

        Assert.NotNull(response.Error);
    }
}
