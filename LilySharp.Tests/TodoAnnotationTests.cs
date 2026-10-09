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
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Midi;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;
using Diagnostic = LilySharp.Core.Syntax.Diagnostic;
using DiagnosticSeverity = LilySharp.Core.Syntax.DiagnosticSeverity;

namespace LilySharp.Tests;

/// <summary>
/// <c>@todo</c> — a note, rest or chord left to check (owner's decision 2026-10-05,
/// LilySharp-Omr proposal A1): its four spellings, its diagnostics, the <c>data-todo</c> the
/// page carries and nothing else, and the quick fix that resolves it.
/// </summary>
[Trait("Category", "Unit")]
public class TodoAnnotationTests
{
    private static string Book(string music) => $$"""
        octave absolute
        part m { }
        section A { m { {{music}} } }
        form { A }
        score { staff m }
        """;

    private static Diagnostic[] Diagnostics(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return tree.Diagnostics.Concat(SemanticValidation.Run(tree)).ToArray();
    }

    private static string Svg(string source)
        => SvgGenerator.Generate(SyntaxTree.Parse(source), new SvgRenderOptions { EmbedFont = false });

    [Theory]
    [InlineData("c'4@todo", null, null)]
    [InlineData("c'4@todo(\"F# or F?\")", null, "F# or F?")]
    [InlineData("c'4@todo(o1203)", "o1203", null)]
    [InlineData("c'4@todo(b4-lh \"bar 4\")", "b4-lh", "bar 4")]
    [InlineData("c'4@todo(o1, \"comma too\")", "o1", "comma too")]
    public void TheFourSpellings_ReadTheirKeyAndMemo(string note, string? key, string? memo)
    {
        var tree = SyntaxTree.Parse(Book(note + " d'2. |"));
        var todo = tree.GetRoot().DescendantNodes().Select(TodoAnnotation.Of).Single(t => t != null)!;
        Assert.Equal((key, memo, (string?)null), (todo.Key, todo.Memo, todo.Problem));
        // Each is a known annotation: no LYS1008, one LYS4026 with the memo.
        var diagnostics = Diagnostics(Book(note + " d'2. |"));
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCodes.UnknownAnnotation);
        var mark = Assert.Single(diagnostics, d => d.Code == DiagnosticCodes.TodoMark);
        Assert.Equal(DiagnosticSeverity.Warning, mark.Severity);
        Assert.Equal(memo == null ? "TODO" : "TODO: " + memo, mark.Message);
    }

    [Theory]
    [InlineData("c'4@todo(1x)")]                 // not a key
    [InlineData("c'4@todo(\"a\" \"b\")")]        // two memos
    [InlineData("c'4@todo(\"memo\" o1)")]        // memo first
    [InlineData("c'4@todo(a b)")]                // two keys
    public void AnArgumentThatDoesNotRead_IsReported_AndTheMarkStillCounts(string note)
    {
        var diagnostics = Diagnostics(Book(note + " d'2. |"));
        Assert.Single(diagnostics, d => d.Code == DiagnosticCodes.TodoArgument);
        Assert.Single(diagnostics, d => d.Code == DiagnosticCodes.TodoMark);
        Assert.Contains("data-todo=\"\"", Svg(Book(note + " d'2. |")));
    }

    [Fact]
    public void AKeyWrittenTwice_IsReported_Once()
    {
        var diagnostics = Diagnostics(Book("c'4@todo(o1) d'4@todo(o2) e'4@todo(o1) f'4 |"));
        Assert.Single(diagnostics, d => d.Code == DiagnosticCodes.TodoKeyRepeated);
        Assert.Equal(3, diagnostics.Count(d => d.Code == DiagnosticCodes.TodoMark));
    }

    /// <summary>The mark draws nothing and plays nothing: with <c>data-todo</c> masked the
    /// page is the unmarked page, and the MIDI is the same notes — on a note, a chord, a rest
    /// and a whole-bar rest, each of which carries the attribute.</summary>
    [Fact]
    public void TheMark_IsOnlyDataTodo_OnTheHeadsAndRests()
    {
        string marked = Book("c'4@todo(n1) <e' g'>4@todo r4@todo(\"why\") g'4 | R1@todo(b2) |");
        string plain = Book("c'4 <e' g'>4 r4 g'4 | R1 |");

        string svg = Svg(marked);
        Assert.Contains("data-todo=\"n1\"", svg);
        Assert.Contains("data-todo=\"b2\"", svg);
        Assert.Equal(3, Regex.Matches(svg, "data-todo=\"\"").Count);   // both chord heads and the rest
        static string Masked(string s) => Regex.Replace(s, @"\s(data-pos|data-todo)=""[^""]*""", "");
        Assert.Equal(Masked(Svg(plain)), Masked(svg));

        static string Played(string source) => string.Join(",", new MidiExporter().Export(SyntaxTree.Parse(source))
            .Tracks.SelectMany(t => t.Notes).Select(n => $"{n.Pitch}:{n.StartTick}:{n.Channel}:{n.Velocity}"));
        Assert.Equal(Played(plain), Played(marked));
    }

    // ---- the quick fix ----

    private static CodeAction[] ActionsAt(string text, int offset, string path = "/todo.lys")
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new System.Uri(Path.IsPathRooted(path) && path.Length > 1 && path[1] == ':' ? path : "file://" + path);
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        var at = new Position(line, character);
        return server.GetCodeActions(new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Range = new LilySharp.Lsp.Protocol.Range { Start = at, End = at },
        }) ?? [];
    }

    private static string Apply(string text, CodeAction action)
    {
        foreach (var edit in action.Edit!.Changes!.Values.Single()
                     .OrderByDescending(e => (e.Range.Start.Line, e.Range.Start.Character)))
        {
            int start = Offset(text, edit.Range.Start), end = Offset(text, edit.Range.End);
            text = text[..start] + edit.NewText + text[end..];
        }
        return text;

        static int Offset(string t, Position p)
        {
            int line = 0, i = 0;
            while (line < p.Line) { if (t[i++] == '\n') line++; }
            return i + p.Character;
        }
    }

    [Fact]
    public void Resolve_DeletesTheMark_AndResolveAll_DeletesEveryOne()
    {
        string text = Book("c'4@todo(o1 \"x\") d'4@todo e'4 f'4@todo(\"y\") |");
        var actions = ActionsAt(text, text.IndexOf("@todo(o1", System.StringComparison.Ordinal) + 2);

        var one = Assert.Single(actions, a => a.Title.StartsWith("Resolve this TODO", System.StringComparison.Ordinal));
        Assert.Equal(Book("c'4 d'4@todo e'4 f'4@todo(\"y\") |"), Apply(text, one));

        var all = Assert.Single(actions, a => a.Title == "Resolve all 3 TODOs in this file");
        string clean = Apply(text, all);
        Assert.Equal(Book("c'4 d'4 e'4 f'4 |"), clean);
        Assert.DoesNotContain(Diagnostics(clean), d => d.Code == DiagnosticCodes.TodoMark);
    }

    /// <summary>
    /// The OMR reader's candidates (proposal B2): <c>x.omr.json</c> beside <c>x.lys</c> lists,
    /// per key, what the marked item might be; each is a quick fix that writes it over the
    /// item, the mark with it. A key the file does not list, and a mark without a key, get none.
    /// </summary>
    [Fact]
    public void TheReadersCandidates_AreQuickFixes_ThatWriteTheItemWithoutTheMark()
    {
        string dir = Directory.CreateTempSubdirectory("lys-omr-").FullName;
        string lys = Path.Combine(dir, "x.lys");
        File.WriteAllText(Path.ChangeExtension(lys, ".omr.json"), """
            { "version": 1, "todos": [
              { "key": "o12", "kind": "note", "candidates": [
                { "label": "F♯", "text": "fis'8" }, { "label": "F♮", "text": "f'8" }, { "text": "" } ] },
              { "key": "o13", "candidates": [] } ] }
            """);
        string text = Book("c'4 fis'8@todo(o12 \"faint\")@staccato e'8 g'4@todo(o13) a'4@todo |");

        var actions = ActionsAt(text, text.IndexOf("@todo(o12", System.StringComparison.Ordinal) + 2, lys);
        var writes = actions.Where(a => a.Title.StartsWith("Write ", System.StringComparison.Ordinal)).ToList();
        Assert.Equal(["Write F♯: fis'8 (resolves the TODO)", "Write F♮: f'8 (resolves the TODO)"],
            writes.Select(a => a.Title));
        Assert.Equal(Book("c'4 f'8 e'8 g'4@todo(o13) a'4@todo |"), Apply(text, writes[1]));

        Assert.DoesNotContain(ActionsAt(text, text.IndexOf("@todo(o13", System.StringComparison.Ordinal) + 2, lys),
            a => a.Title.StartsWith("Write ", System.StringComparison.Ordinal));
        Assert.DoesNotContain(ActionsAt(text, text.LastIndexOf("@todo", System.StringComparison.Ordinal) + 2, lys),
            a => a.Title.StartsWith("Write ", System.StringComparison.Ordinal));
    }
}
