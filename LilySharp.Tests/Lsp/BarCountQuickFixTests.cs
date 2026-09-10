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
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// The LYS2007 quick fix: a section voice shorter than the section's longest voice gets
/// bare bar lines appended, one per missing bar, and the warning is gone once the edit is
/// applied. Asked for with the caret INSIDE the squiggled name (as an editor does), in
/// every spelling the warning is anchored on, and never offered where nothing is short.
/// </summary>
public class BarCountQuickFixTests
{
    private static CodeAction[] ActionsAt(string text, int offset)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new System.Uri("file:///pad.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        var at = new Position(line, character);
        return server.GetCodeActions(new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Range = new LilySharp.Lsp.Protocol.Range { Start = at, End = at },
        }) ?? [];
    }

    private static CodeAction? PadAction(string text, int offset)
        => ActionsAt(text, offset).SingleOrDefault(a => a.Title.StartsWith("Add ") && a.Title.Contains(" bar line"));

    /// <summary>Applies the action's single edit and returns the edited text.</summary>
    private static string Apply(string text, CodeAction action)
    {
        var edit = action.Edit!.Changes!.Values.Single().Single();
        int start = OffsetOf(text, edit.Range.Start);
        int end = OffsetOf(text, edit.Range.End);
        return text.Substring(0, start) + edit.NewText + text.Substring(end);
    }

    private static int OffsetOf(string text, Position p)
    {
        int offset = 0;
        for (int line = 0; line < p.Line; line++)
            offset = text.IndexOf('\n', offset) + 1;
        return offset + p.Character;
    }

    private static int Lys2007Count(string text)
        => SemanticValidation.Run(SyntaxTree.Parse(text))
            .Count(d => d.Code == DiagnosticCodes.SectionBarCountMismatch);

    [Fact]
    public void PartMajor_ShortMelody_GetsOneBarAndTheWarningGoes()
    {
        // ★ scratch/ベースタブLy/tooLongChords.lys: melody's A is one bar, the chord row's two.
        const string text = """
            part melody {
              section A { g2 g | }
              section B { c2 c | }
            }
            chords prog {
              section A { Dm7 | G7 }
              section B { Cmaj7 | }
            }
            form main { A | B | }
            score main { chords prog  staff melody }
            """;
        Assert.Equal(1, Lys2007Count(text));
        // Caret on the second letter of the name — inside the squiggle, not at its start.
        var action = PadAction(text, text.IndexOf("section A") + "section ".Length);
        Assert.NotNull(action);
        Assert.Equal("Add 1 bar line to section A (|)", action!.Title);
        Assert.Equal(CodeActionKind.QuickFix, action.Kind);
        var fixedText = Apply(text, action);
        Assert.Contains("section A { g2 g | | }", fixedText);
        Assert.Equal(0, Lys2007Count(fixedText));
    }

    [Fact]
    public void UnclosedLastBar_GetsOneMoreBarLineToCloseIt()
    {
        // `{ g2 g }` — the trailing bar is open, so the first `|` only closes it; the fix
        // has to find that out by re-validating and add one more.
        const string text = """
            part melody {
              section A { g2 g }
            }
            chords prog {
              section A { Dm7 | G7 | }
            }
            form main { A | }
            score main { chords prog  staff melody }
            """;
        var action = PadAction(text, text.IndexOf("section A") + "section ".Length);
        Assert.NotNull(action);
        var fixedText = Apply(text, action);
        Assert.Contains("section A { g2 g | | }", fixedText);
        Assert.Equal(0, Lys2007Count(fixedText));
    }

    [Fact]
    public void ShortChordTrack_GetsBarLines()
    {
        // The other direction: the row is short by two bars.
        const string text = """
            part melody {
              section A { g2 g | g2 g | g2 g | }
            }
            chords prog {
              section A { Dm7 | }
            }
            form main { A | }
            score main { chords prog  staff melody }
            """;
        int anchor = text.LastIndexOf("section A") + "section ".Length;
        var action = PadAction(text, anchor);
        Assert.NotNull(action);
        Assert.Equal("Add 2 bar lines to section A (| |)", action!.Title);
        var fixedText = Apply(text, action);
        Assert.Contains("section A { Dm7 | | | }", fixedText);
        Assert.Equal(0, Lys2007Count(fixedText));
    }

    [Fact]
    public void SectionMajor_ShortPartBlock_GetsBarLines()
    {
        // Anchored on the part block's name; its braces belong to the block's body node.
        const string text = """
            section A {
              melody { g2 g | }
              chords prog { Dm7 | G7 | }
            }
            form main { A | }
            score main { chords prog  staff melody }
            """;
        var action = PadAction(text, text.IndexOf("melody {") + 1);
        Assert.NotNull(action);
        Assert.Equal("Add 1 bar line to part melody (|)", action!.Title);
        var fixedText = Apply(text, action);
        Assert.Contains("melody { g2 g | | }", fixedText);
        Assert.Equal(0, Lys2007Count(fixedText));
    }

    [Fact]
    public void SectionMajor_ShortChordBlock_GetsBarLines()
    {
        const string text = """
            section A {
              melody { g2 g | g2 g | }
              chords prog { Dm7 }
            }
            form main { A | }
            score main { chords prog  staff melody }
            """;
        var action = PadAction(text, text.IndexOf("chords prog") + "chords ".Length + 1);
        Assert.NotNull(action);
        // `Dm7` leaves its bar open: one `|` closes it, one more is the empty bar — so the
        // title counts two bar lines for one missing bar.
        Assert.Equal("Add 2 bar lines to chords prog (| |)", action!.Title);
        var fixedText = Apply(text, action);
        Assert.Contains("chords prog { Dm7 | | }", fixedText);
        Assert.Equal(0, Lys2007Count(fixedText));
    }

    [Fact]
    public void NothingShort_NoAction()
    {
        const string text = """
            part melody {
              section A { g2 g | g2 g | }
            }
            chords prog {
              section A { Dm7 | G7 | }
            }
            form main { A | }
            score main { chords prog  staff melody }
            """;
        Assert.Null(PadAction(text, text.IndexOf("section A") + "section ".Length));
    }
}
