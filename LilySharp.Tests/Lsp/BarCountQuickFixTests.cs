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

    /// <summary>
    /// Since the warning is one per section, its fix is one action for every short layer:
    /// one edit per layer, and the section agrees once they are applied. The lightbulb is on
    /// the odd one out (the long flute), where the warning stands.
    /// </summary>
    [Fact]
    public void ManyShortLayers_AreAllPaddedByOneAction()
    {
        const string text = """
            part flute { section A { c1 | c1 | c1 | } }
            part oboe { section A { c1 | c1 | } }
            chords harmony { section A { C | F | } }
            form main { A }
            score main { chords harmony  staff flute  staff oboe }
            """;
        var action = PadAction(text, text.IndexOf("section A") + "section ".Length + 1);
        Assert.NotNull(action);
        Assert.Equal("Add bar lines to section A of oboe and section A of chords harmony", action!.Title);
        var edits = action.Edit!.Changes!.Values.Single();
        Assert.Equal(2, edits.Length);
        // Applied back to front, as a client applies non-overlapping edits.
        string fixedText = text;
        foreach (var e in edits.OrderByDescending(e => OffsetOf(text, e.Range.Start)))
        {
            int at = OffsetOf(fixedText, e.Range.Start);
            fixedText = fixedText.Substring(0, at) + e.NewText + fixedText.Substring(at);
        }
        Assert.Equal(0, Lys2007Count(fixedText));
    }

    /// <summary>The Problems panel gets every layer as a link: the warning's related locations
    /// travel as LSP relatedInformation, each on its layer's section name.</summary>
    [Fact]
    public void TheWarningsLayers_ReachTheEditorAsRelatedInformation()
    {
        const string text = """
            part flute { section A { c1 | c1 | c1 | } }
            part oboe { section A { c1 | c1 | } }
            form main { A }
            score main { staff flute  staff oboe }
            """;
        var core = SemanticValidation.Run(SyntaxTree.Parse(text))
            .Single(d => d.Code == DiagnosticCodes.SectionBarCountMismatch);
        var uri = new System.Uri("file:///pad.lys");
        var lsp = LilySharpLanguageServer.ConvertDiagnostic(core, text, uri);
        Assert.NotNull(lsp.RelatedInformation);
        Assert.Equal(2, lsp.RelatedInformation!.Length);
        Assert.All(lsp.RelatedInformation, r => Assert.Equal(uri, r.Location.Uri));
        var oboe = lsp.RelatedInformation.Single(r => r.Message.StartsWith("part 'oboe'"));
        Assert.Equal(1, oboe.Location.Range.Start.Line);
    }

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
        Assert.Equal("Add 1 bar line to section A of melody (|)", action!.Title);
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
        Assert.Equal("Add 2 bar lines to section A of chords prog (| |)", action!.Title);
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
    public void PartMajor_ShortLyricsCell_GetsBarLine()
    {
        // A lyrics track's cell one bar short of the melody (user request, 2026-09-10):
        // anchored on the cell's section name, padded with one empty lyric bar.
        const string text = """
            part melody {
              section A { g2 g | g2 g | }
            }
            lyrics words {
              section A { la la | }
            }
            form main { A | }
            score main { staff melody with lyrics words }
            """;
        Assert.Equal(1, Lys2007Count(text));
        var action = PadAction(text, text.LastIndexOf("section A") + "section ".Length);
        Assert.NotNull(action);
        Assert.Equal("Add 1 bar line to section A of lyrics words (|)", action!.Title);
        var fixedText = Apply(text, action);
        Assert.Contains("section A { la la | | }", fixedText);
        Assert.Equal(0, Lys2007Count(fixedText));
    }

    [Fact]
    public void SectionMajor_ShortLyricsBlock_GetsBarLines()
    {
        // Anchored on the block's track name. `la la` leaves its bar open (the parser closes
        // it with a zero-width bar line): one `|` closes it, one more is the empty bar.
        const string text = """
            part vocal { }
            section A {
              vocal { g2 g | g2 g | }
              lyrics words sings vocal { la la }
            }
            form main { A | }
            score main { lyrics words }
            """;
        var action = PadAction(text, text.IndexOf("lyrics words") + "lyrics ".Length + 1);
        Assert.NotNull(action);
        Assert.Equal("Add 2 bar lines to lyrics words (| |)", action!.Title);
        var fixedText = Apply(text, action);
        Assert.Contains("lyrics words sings vocal { la la | | }", fixedText);
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
