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
/// What the editor asks beside the preview's render on every keystroke or caret move must not
/// cost a collect, an export or a validation pass of its own where the answer does not need one
/// (review of session 1291): the <c>@todo</c> list of a file with no mark, the sounding notes
/// the header's length reads, and the code actions of a text the pass has already read.
/// Counted, not timed.
/// </summary>
public class KeystrokeSideWorkTests
{
    private const string Doc = """
        part vn { midiInstrument "violin" }
        part pno
        part kit { clef percussion }
        section A {
          vn { c''4 d e f | }
          pno { e'4 f g a | }
          kit { bd4 bd sn bd | }
        }
        form { A }
        score { staff vn  staff pno  staff kit }
        score solo { staff vn }
        """;

    private static LilySharpLanguageServer Open(string text, out Uri uri, string name = "side")
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        uri = new Uri($"file:///{name}.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, Version = 1, LanguageId = "lilysharp" },
        });
        return server;
    }

    [Fact]
    public void TheTodoList_OfAFileWithNoMark_CollectsNothing()
    {
        var server = Open(Doc, out var uri);
        var answer = server.Todos(new TodosParams { TextDocument = new TextDocumentIdentifier { Uri = uri } });
        Assert.Null(answer.Error);
        Assert.Empty(answer.Todos!);
        Assert.Equal(0, server.PlacesCollects);
    }

    [Fact]
    public void TheTodoList_OfAFileWithAMark_CollectsOnce_AndPlacesTheMark()
    {
        var server = Open(Doc.Replace("d e f |", "d@todo(\"here?\") e f |"), out var uri);
        var ask = new TodosParams { TextDocument = new TextDocumentIdentifier { Uri = uri } };
        var mark = Assert.Single(server.Todos(ask).Todos!);
        Assert.Equal(1, mark.Measure);
        Assert.Equal("vn", mark.Part);
        Assert.Single(server.Todos(ask).Todos!);
        Assert.Equal(1, server.PlacesCollects);
    }

    // P4 of the review: the caret on a note asked for its sound (the resolved pitches) and its
    // place (bar and beat) by two walks, each a full collect of every score.
    [Theory]
    [InlineData(false, 2)]      // two scores, each collected once
    [InlineData(true, 3)]       // …and a score that transposes once more, for the pitch as written
    public void TheCaretOnANote_CollectsEachScoreOnce_ForItsSoundAndItsPlace(bool transposing, int collects)
    {
        string text = transposing ? Doc.Replace("score solo { staff vn }", "score solo transpose d { staff vn }") : Doc;
        var server = Open(text, out var uri, "caret" + transposing);
        var ask = new AuditionAtParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Offset = text.IndexOf("d e f |", StringComparison.Ordinal),
        };
        int before = LilySharp.Core.Svg.SvgGenerator.CollectsOnThisThread;
        var info = server.CaretInfo(ask);
        Assert.Equal(collects, LilySharp.Core.Svg.SvgGenerator.CollectsOnThisThread - before);
        Assert.Contains("bar 1", info.Text);
        Assert.Contains("D5", info.Text);
        // …and asked again of the same text, none.
        server.CaretInfo(ask);
        Assert.Equal(collects, LilySharp.Core.Svg.SvgGenerator.CollectsOnThisThread - before);
    }

    private static string[] Lines(PlaybackResponse pb, Func<PlaybackNote, bool> keep)
        => [.. pb.Notes!.Where(keep).Select(n => $"{n.T}|{n.D}|{n.P}|{n.V}|{n.S}|{n.O}|{n.C}|{n.G}|{n.N}|{n.M}")];

    [Theory]
    [InlineData(null)]
    [InlineData("solo")]
    public void TheSoundingNotes_AreTheScoresOwn_WithNoExtraListed(string? score)
    {
        var server = Open(Doc, out var uri);
        var ask = new PlaybackParams { TextDocument = new TextDocumentIdentifier { Uri = uri }, RenderName = score };
        var full = server.GetPlayback(ask);
        ask.SoundingOnly = true;
        var sounding = server.GetPlayback(ask);

        Assert.Null(sounding.Error);
        Assert.Empty(sounding.Extra!);
        // What the editor keeps of the full answer (soundingOf: the left-out parts dropped) is
        // this answer whole, in the same order.
        var left = (full.Extra ?? []).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(Lines(full, n => !left.Contains(full.Parts![n.N])), Lines(sounding, _ => true));
        Assert.NotEmpty(sounding.Notes!);
        if (score == "solo")
            Assert.True(full.Notes!.Length > sounding.Notes!.Length);
    }

    [Fact]
    public void TheCodeActions_OfOneText_RunThePassOnce()
    {
        // A bar a beat short: the pass has a diagnostic and the caret is on it.
        string text = Doc.Replace("c''4 d e f |", "c''4 d e |");
        var server = Open(text, out var uri);
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, text.IndexOf("c''4", StringComparison.Ordinal));
        var at = new Position(line, character);
        var ask = new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Range = new LilySharp.Lsp.Protocol.Range { Start = at, End = at },
        };

        var first = server.GetCodeActions(ask) ?? [];
        // (the didOpen's own pass may have run beside the first request; none starts after it)
        int passes = server.DiagnosticPasses;
        Assert.InRange(passes, 1, 2);
        for (int i = 0; i < 5; i++)
        {
            var again = server.GetCodeActions(ask) ?? [];
            Assert.Equal(first.Select(a => a.Title), again.Select(a => a.Title));
        }
        Assert.Equal(passes, server.DiagnosticPasses);

        // An edit is another text: its pass runs.
        server.DidChange(new DidChangeTextDocumentParams
        {
            TextDocument = new VersionedTextDocumentIdentifier { Uri = uri, Version = 2 },
            ContentChanges = [new TextDocumentContentChangeEvent { Text = text.Replace("e'4 f g a", "e'4 f g b") }],
        });
        server.GetCodeActions(ask);
        Assert.True(server.DiagnosticPasses > passes);
    }
}
