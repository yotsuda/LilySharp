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
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Where the bar lines a span left out belong — the quick fix for LYS's missing bar line (owner's
/// report 2026-10-27: an OMR reader read tabi's bars 32 and 33 as one, and Lily# knew the cut).
/// </summary>
[Trait("Category", "Unit")]
public class MissingBarLinesTests
{
    private static (string Text, SyntaxTree Tree, LilySharp.Core.Syntax.Diagnostic Diagnostic) Read(string music, string time = "6/8")
    {
        string text = $"time {time}\noctave absolute\npart p1 {{ clef treble }}\nsection A {{\n  p1 {{\n    {music}\n  }}\n}}\nform {{ A }}\nscore {{ staff p1 }}\n";
        var tree = SyntaxTree.Parse(text);
        var diagnostic = SemanticValidation.Run(tree)
            .First(d => d.Code == DiagnosticCodes.MissingBarLine);
        return (text, tree, diagnostic);
    }

    [Fact]
    public void TabisBar32_IsCutAfterItsFirstBar()
    {
        // The line as the reader wrote it: two bars of 6/8, the bar line between missed.
        var (text, tree, d) = Read("gis4. fis8 cis'8( dis'8 <gis cis' eis'>16 fis'16 eis'4~ eis'8) cis'8( dis'8 |");
        var at = MissingBarLines.Offsets(tree, d.Span, new Fraction(6, 8));
        Assert.NotNull(at);
        Assert.Equal("dis'8", text[..Assert.Single(at!)].Split(' ')[^1]);
    }

    [Fact]
    public void ThreeBarsInOne_GetTwoBarLines()
    {
        var (text, tree, d) = Read("c'4 d' e' f' | g'4 a' b' c'' d''4 e'' f'' g'' a''4 g'' f'' e'' |", "4/4");
        var at = MissingBarLines.Offsets(tree, d.Span, new Fraction(4, 4));
        Assert.Equal(["c''", "g''"], at!.Select(o => text[..o].Split(' ')[^1]));
    }

    [Fact]
    public void ANoteHeldAcrossTheBarsEnd_LeavesNoCut()
    {
        // 6/8: a dotted half then a dotted half held from the middle — the second starts mid-bar.
        var (_, tree, d) = Read("c'4. d'2. e'4. |");
        Assert.Null(MissingBarLines.Offsets(tree, d.Span, new Fraction(6, 8)));
    }

    [Fact]
    public void TheQuickFix_WritesTheBarLine_AndTheErrorIsGone()
    {
        var (text, _, d) = Read("gis4. fis8 cis'8( dis'8 <gis cis' eis'>16 fis'16 eis'4~ eis'8) cis'8( dis'8 |");
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new Uri("file:///missing-bar-line.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, d.Span.Start + 1);
        var actions = server.GetCodeActions(new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Range = new LilySharp.Lsp.Protocol.Range { Start = new Position(line, character), End = new Position(line, character) },
        }) ?? [];
        var fix = Assert.Single(actions, a => a.Title == "Write the missing bar line");
        var edit = fix.Edit!.Changes!.Values.Single().Single();
        var lines = text.Split('\n');
        int start = lines.Take(edit.Range.Start.Line).Sum(l => l.Length + 1) + edit.Range.Start.Character;
        string after = text[..start] + edit.NewText + text[start..];
        Assert.Contains("cis'8( dis'8 | <gis cis' eis'>16", after);
        Assert.DoesNotContain(SemanticValidation.Run(SyntaxTree.Parse(after)), x => x.Code == DiagnosticCodes.MissingBarLine);
    }

    [Fact]
    public void ALineEditedBesideTheScan_GetsItsBarLineWritten()
    {
        var (text, _, d) = Read("gis4. fis8 cis'8( dis'8 <gis cis' eis'>16 fis'16 eis'4~ eis'8) cis'8( dis'8 |");
        int start = text.IndexOf("gis4.", StringComparison.Ordinal), end = text.IndexOf("dis'8 |", start + 30, StringComparison.Ordinal) + 7;
        Assert.Equal("gis4. fis8 cis'8( dis'8 | <gis cis' eis'>16 fis'16 eis'4~ eis'8) cis'8( dis'8 |", MissingBarLines.WrittenIn(text, start, end));
        Assert.Null(MissingBarLines.WrittenIn(text, 0, 10));
        Assert.Equal(new Fraction(6, 8), MissingBarLines.MeterOf(d.Message));
    }
}
