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

    // The rest of P4: in a book with `using`s the sound read the expanded tree and the place the
    // document's own, so each score was collected twice — and the place was that of a text the
    // page never draws (the document with nothing it includes: here no part `m`, no `Intro`).
    [Fact]
    public void TheCaretOnANote_OfABookWithIncludes_CollectsOnce_AndStandsWhereThePageDrawsIt()
    {
        string dir = Path.Combine(Path.GetTempPath(), "lys-caret-using-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "intro.lys"),
                "part m { clef treble }\nsection Intro { m { c'1 | d'1 | } }\n");
            string text = "using \"intro.lys\"\nsection A { m { e'4 f g a | } }\nform { Intro A }\nscore { staff m }\n";
            var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
            var uri = new Uri(Path.Combine(dir, "main.lys"));
            server.DidOpen(new DidOpenTextDocumentParams
            {
                TextDocument = new TextDocumentItem { Uri = uri, Text = text, Version = 1, LanguageId = "lilysharp" },
            });
            var ask = new AuditionAtParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                Offset = text.IndexOf("f g a", StringComparison.Ordinal),
            };
            int before = LilySharp.Core.Svg.SvgGenerator.CollectsOnThisThread;
            var info = server.CaretInfo(ask);
            Assert.Equal(1, LilySharp.Core.Svg.SvgGenerator.CollectsOnThisThread - before);
            // The Intro's two bars are before it on the page.
            Assert.Equal("m · bar 3 · beat 2 · F4", info.Text);
            // …and asked again of the same text, nothing is collected.
            // (The Problems panel's pass expands the same text beside this: one tree for both,
            // UsingExpansionCacheTests.)
            server.CaretInfo(ask);
            Assert.Equal(1, LilySharp.Core.Svg.SvgGenerator.CollectsOnThisThread - before);

            // The place a caret is asked for (the chat's pointer, the scan view) and a mark's bar
            // are the page's too: they were the bars of the document's own text (bar 1).
            var placed = server.PlaceAt(new PlaceAtParams { TextDocument = new TextDocumentIdentifier { Uri = uri }, Offset = ask.Offset });
            Assert.Equal(3, placed.Measure);
            Assert.Equal("m", placed.Part);
            Assert.Equal(1, server.PlacesCollects);

            // An include saved with a bar more: the document's version stands, the piece does not.
            File.WriteAllText(Path.Combine(dir, "intro.lys"),
                "part m { clef treble }\nsection Intro { m { c'1 | d'1 | e'1 | } }\n");
            Assert.Equal(4, server.PlaceAt(new PlaceAtParams { TextDocument = new TextDocumentIdentifier { Uri = uri }, Offset = ask.Offset }).Measure);
            Assert.Equal("m · bar 4 · beat 2 · F4", server.CaretInfo(ask).Text);

            string marked = text.Replace("f g a", "f@todo(\"here?\") g a");
            server.DidChange(new DidChangeTextDocumentParams
            {
                TextDocument = new VersionedTextDocumentIdentifier { Uri = uri, Version = 2 },
                ContentChanges = [new TextDocumentContentChangeEvent { Text = marked }],
            });
            var mark = Assert.Single(server.Todos(new TodosParams { TextDocument = new TextDocumentIdentifier { Uri = uri } }).Todos!);
            Assert.Equal(4, mark.Measure);
            Assert.Equal("m", mark.Part);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
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
