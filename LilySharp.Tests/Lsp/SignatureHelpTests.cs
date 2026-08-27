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
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// SignatureHelp's keyword match and activeParameter (2026-08-26 review, appendix F
/// finding 7). The trigger character is ' ', so the old raw-substring match actually
/// fired on every space: typing `title "Ragtime" ` summoned <c>time</c>'s signature
/// from inside the string, and a quoted tempo marking with spaces pushed
/// activeParameter past its own argument. The keyword now has to stand as its own
/// word in a code-only view of the line (strings blanked, comments cut), and the
/// arguments are counted as whitespace runs OUTSIDE string literals.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SignatureHelpTests
{
    /// <summary>Signature help at the END of <paramref name="line"/> (the position
    /// the ' ' trigger fires at while typing it).</summary>
    private static SignatureHelp? HelpAtEndOf(string line)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new Uri("file:///signature-help.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri, Text = line, Version = 1, LanguageId = "lilysharp",
            },
        });
        return server.GetSignatureHelp(new SignatureHelpParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(0, line.Length),
        });
    }

    [Fact]
    public void AKeyword_StillAnswers()
    {
        var help = HelpAtEndOf("time ");

        Assert.NotNull(help);
        Assert.Contains("time", help!.Signatures[0].Label, StringComparison.Ordinal);
        Assert.Equal(0, help.ActiveParameter);
    }

    [Fact]
    public void AKeywordInsideAString_DoesNotAnswer()
    {
        // `title "Ragtime" ` — the ' ' trigger fires right after the closing quote,
        // and the old substring match found `time` INSIDE the title.
        Assert.Null(HelpAtEndOf("title \"Ragtime\" "));
    }

    [Fact]
    public void AKeywordInsideAnIdentifier_DoesNotAnswer()
    {
        // `overtime` contains `time`; nothing else on the line is a keyword. (The
        // first draft wrote `phrase overtime { … }` and asserted null — and failed,
        // correctly: `phrase` IS a table keyword. The net must isolate the identifier.)
        Assert.Null(HelpAtEndOf("overtime "));
    }

    [Fact]
    public void AKeywordBehindALineComment_DoesNotAnswer()
    {
        Assert.Null(HelpAtEndOf("// tempo "));
    }

    [Fact]
    public void AQuotedMarkingWithSpaces_IsOneArgument()
    {
        // After the marking the caret is on the SECOND parameter (duration). The old
        // per-character count included the marking's own spaces and pushed
        // activeParameter to the clamp.
        var help = HelpAtEndOf("tempo \"Allegro con brio\" ");

        Assert.NotNull(help);
        Assert.Contains("tempo", help!.Signatures[0].Label, StringComparison.Ordinal);
        Assert.Equal(1, help.ActiveParameter);
    }

    [Fact]
    public void TheParameterAdvancesPerArgument_NotPerSpace()
    {
        Assert.Equal(0, HelpAtEndOf("tempo ")!.ActiveParameter);
        Assert.Equal(1, HelpAtEndOf("tempo \"Vivo\" ")!.ActiveParameter);
        // `4 = 120`: the '=' is its own token, so the count runs past the parameter
        // list and the clamp holds it on the last one (bpm) — the shape the label
        // `duration = bpm` advertises.
        Assert.Equal(2, HelpAtEndOf("tempo \"Vivo\" 4 = ")!.ActiveParameter);
    }
}
