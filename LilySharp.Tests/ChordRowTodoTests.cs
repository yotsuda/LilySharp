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
using System.Text.RegularExpressions;
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
/// <c>@todo</c> on a chords-row symbol or no-chord slot (owner's decision 2026-10-11,
/// docs/todo-memo-design.md §5): a chord chart has no notes, so a doubt about a chord sits on
/// the symbol — <c>Am7@todo("Am7 or C?")</c> — reported as a note's mark is, and ringed in the
/// preview round the symbol's ink.
/// </summary>
[Trait("Category", "Unit")]
public class ChordRowTodoTests
{
    private static string Sheet(string row, bool staff = true) => staff
        ? $"part m {{ clef treble }}\nsection A {{\n  m {{ c'1 | d'1 | }}\n  chords prog {{ {row} }}\n}}\nform {{ ~A }}\nscore {{ chords prog  staff m }}\n"
        : $"chords prog {{ section A {{ {row} }} }}\nform {{ ~A }}\nscore {{ chords prog }}\n";

    private static System.Collections.Generic.List<Diagnostic> Messages(string text)
    {
        var tree = SyntaxTree.Parse(text);
        return [.. tree.Diagnostics.Concat(SemanticValidation.Run(tree))];
    }

    [Theory]
    [InlineData("G@todo | C |", null)]
    [InlineData("Am7@todo(\"Am7 or C?\") | C |", "Am7 or C?")]
    [InlineData("F(133211)@todo(o12 \"barre?\") | C |", "barre?")]
    [InlineData("C . G7@todo(\"or G?\") . | C |", "or G?")]
    [InlineData("r@todo(\"a chord here?\") | C |", "a chord here?")]
    [InlineData("C@todo(\"x\").down | C |", "x")]
    public void TheSpellings_ReadAsAMark_AndRaiseOnlyTheTodo(string row, string? memo)
    {
        string text = Sheet(row);
        Assert.Equal(text, SyntaxTree.Parse(text).GetRoot().ToFullString());   // nothing dropped
        var messages = Messages(text);
        var mark = Assert.Single(messages);
        Assert.Equal(DiagnosticCodes.TodoMark, mark.Code);
        Assert.Equal(memo == null ? "TODO" : "TODO: " + memo, mark.Message);
        if (memo != null)
            Assert.Equal(memo, text.Substring(mark.Span.Start, mark.Span.Length));   // the memo is underlined
    }

    [Fact]
    public void TheMark_IsNotPartOfTheSymbolOrItsShapes()
    {
        // The entry's symbol and shapes are read from its direct tokens; the mark is a child
        // NODE after them, so `F(133211)@todo(…)` still names F with the shape 133211.
        var tree = SyntaxTree.Parse(Sheet("F(133211)@todo(o12 \"barre?\") | C |"));
        var entry = tree.GetRoot().DescendantNodes().OfType<ChordEntrySyntax>().First();
        Assert.Equal("F", entry.SymbolText);
        Assert.Equal(["133211"], entry.ShapeWords.Select(w => w.Text));
    }

    [Theory]
    [InlineData("G@staccato | C |", "@staccato")]
    [InlineData("G@p | C |", "@p")]
    [InlineData("r@fermata | C |", "@fermata")]
    public void AnyOtherMarkOnARowEntry_IsAnError(string row, string written)
    {
        var bad = Assert.Single(Messages(Sheet(row)), d => d.Code == DiagnosticCodes.ChordBlockBadMember);
        Assert.Equal(DiagnosticSeverity.Error, bad.Severity);
        Assert.Contains(written, bad.Message);
        Assert.Contains("@todo", bad.Message);
    }

    [Fact]
    public void AMarkAfterASpace_BelongsToNoEntry()
    {
        // As before: a '@' after a space is a stray in a chords body.
        Assert.Contains(Messages(Sheet("G @todo | C |")), d => d.Code == DiagnosticCodes.ChordBlockBadMember);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ThePreviewRingsTheSymbol_AndSetsTheMemo_TheExportDoesNot(bool staff)
    {
        // Over a staff and in a chord chart with no staff at all — the case a note's mark
        // cannot reach. The ring and the memo carry the entry's own data-pos.
        string text = Sheet("C | Am7@todo(\"Am7 or C?\") |", staff);
        int entryAt = text.IndexOf("Am7@todo", StringComparison.Ordinal);
        var tree = SyntaxTree.Parse(text);
        string preview = SvgGenerator.Generate(tree, SvgRenderOptions.Preview());
        Assert.Matches($"data-todo=\"\"[^>]*data-pos=\"{entryAt}\"", preview);
        Assert.Contains("Am7 or C?", preview);
        string export = SvgGenerator.Generate(tree, SvgRenderOptions.Default);
        Assert.DoesNotContain("data-todo", export);
    }

    [Fact]
    public void ResolveThisTodo_DeletesTheMarkFromTheSymbol()
    {
        string text = Sheet("C | Am7@todo(\"Am7 or C?\") |");
        var server = Open(text, out var uri);
        int at = text.IndexOf("Am7 or C?", StringComparison.Ordinal);
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, at);
        var actions = server.GetCodeActions(new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Range = new LilySharp.Lsp.Protocol.Range { Start = new Position(line, character), End = new Position(line, character) },
        }) ?? [];
        var resolve = Assert.Single(actions, a => a.Title.StartsWith("Resolve this TODO", StringComparison.Ordinal));
        var edit = resolve.Edit!.Changes!.Values.Single().Single();
        int start = Offset(text, edit.Range.Start), end = Offset(text, edit.Range.End);
        Assert.Equal(Sheet("C | Am7 |"), text[..start] + edit.NewText + text[end..]);
    }

    [Fact]
    public void TheSteppingKey_WritesTheShapeBeforeTheMark()
    {
        // Ctrl+Shift+Up on a marked name writes its usual shape after the SYMBOL — the mark
        // stays where it was, after the new group.
        string text = Sheet("C | G@todo(\"check\") |");
        var server = Open(text, out var uri);
        int caret = text.IndexOf("G@todo", StringComparison.Ordinal);
        var response = server.Step(new StepParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Selections = [new StepSelection { Start = caret, End = caret }],
            Direction = 1,
        });
        string after = text;
        foreach (var e in response.Edits.OrderByDescending(e => e.Start))
            after = after[..e.Start] + e.NewText + after[e.End..];
        Assert.Contains("G(320003)@todo(\"check\")", after);
    }

    private static string[] CompletionLabelsAt(string text, int offset)
    {
        var server = Open(text, out var uri);
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        return [.. (server.Completion(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(line, character),
        })?.Items ?? []).Select(i => i.Label!)];
    }

    [Fact]
    public void AnAtAfterARowSymbol_OffersTodoAlone()
    {
        // The chords body answers every request with chord names; after `G@` it offers the one
        // mark a row takes.
        string text = Sheet("C | G@ |");
        int at = text.IndexOf("G@", StringComparison.Ordinal) + 2;
        Assert.Equal(["todo"], CompletionLabelsAt(text, at));
    }

    [Fact]
    public void InsideTheMemo_NoChordNamesAreOffered_AndAfterItThePlacement()
    {
        string text = Sheet("C | G@todo(\"ch\") |");
        int inMemo = text.IndexOf("ch\")", StringComparison.Ordinal) + 2;
        Assert.Empty(CompletionLabelsAt(text, inMemo));
        int afterMark = text.IndexOf("\")", StringComparison.Ordinal) + 2;
        var labels = CompletionLabelsAt(text, afterMark);
        Assert.Contains(".down", labels);
        Assert.DoesNotContain("Am", labels);
    }

    private static LilySharpLanguageServer Open(string text, out Uri uri)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        uri = new Uri("file:///chord-todo.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        return server;
    }

    private static int Offset(string t, Position p)
    {
        int line = 0, i = 0;
        while (line < p.Line) { if (t[i++] == '\n') line++; }
        return i + p.Character;
    }
}
