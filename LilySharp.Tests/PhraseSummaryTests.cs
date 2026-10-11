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

using System.Linq;
using LilySharp.Core.Music;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The editor's account of a phrase (owner's request 2026-10-11): its length, how often it is
/// played, what it is played on, and for a drum phrase the grid a drum chart draws — measured
/// by the collector on a book that plays only the phrase.
/// </summary>
[Trait("Category", "Unit")]
public class PhraseSummaryTests
{
    private const string Kit = """
        phrase hats    { hh8 8 8 8 8 8 hho hh | }
        phrase back    { r4 sn r sn | }
        phrase kick    { bd4 r bd r | }
        phrase fill    { r2 tuplet 3/2 { sn8 sn sn  tomh tomml tomfh } | }
        phrase theme   { c'4 d e f | g1 | }
        phrase groove  { voice { hats } { kick } }
        part kit { clef percussion }
        section A { kit { hats hats } }
        form { ~A }
        score { staff kit }
        """;

    private static PhraseSummary Of(string name) => Assert.IsType<PhraseSummary>(PhraseSummary.Of(SyntaxTree.Parse(Kit), name));

    [Fact]
    public void AHiHatPattern_IsOneBar_UsedThrice_AndDrawnAsItsGrid()
    {
        var s = Of("hats");
        Assert.Equal("1 bar (4/4)", s.LengthText);
        Assert.Equal(3, s.Uses);   // twice in the section, once inside groove
        Assert.Equal("hi-hat · 1 bar (4/4)", s.Tag);
        Assert.Equal(
            "hh  x x  x x  x x  . x\n" +
            "hho . .  . .  . .  x .", s.Grid);   // one place on the staff: the plain hi-hat first
        Assert.Equal("hh8 8 8 8 8 8 hho hh |", s.Source);
    }

    [Fact]
    public void ASnareAndAKickPattern_SitOnAQuarterGrid()
    {
        Assert.Equal("sn . x . x", Of("back").Grid);
        Assert.Equal("snare · 1 bar (4/4)", Of("back").Tag);
        Assert.Equal("bd x . x .", Of("kick").Grid);
        Assert.Equal(1, Of("kick").Uses);   // inside groove
    }

    [Fact]
    public void ATripletFill_IsCountedOnItsOwnGrid()
    {
        var s = Of("fill");
        Assert.Equal("1 bar (4/4)", s.LengthText);
        Assert.Equal("snare, toms · 1 bar (4/4)", s.Tag);
        // An 8th-triplet grid (twelve steps, three a beat), the toms high to low after the
        // snare by their places on the staff.
        Assert.Equal(
            "tomh  . . .  . . .  . . .  x . .\n" +
            "sn    . . .  . . .  x x x  . . .\n" +
            "tomml . . .  . . .  . . .  . x .\n" +
            "tomfh . . .  . . .  . . .  . . x", s.Grid);
    }

    [Fact]
    public void APhraseOfPhrasesInVoices_IsMeasuredThroughThem()
    {
        var s = Of("groove");
        Assert.True(s.IsDrums);
        Assert.Equal("hi-hat, bass drum · 1 bar (4/4)", s.Tag);
        Assert.Contains("bd  x .  . .  x .  . .", s.Grid);
    }

    // ── the editor: hover and completion ──

    private static LilySharp.Lsp.LilySharpLanguageServer Open(string text, out System.Uri uri)
    {
        var server = new LilySharp.Lsp.LilySharpLanguageServer(System.IO.Stream.Null, System.IO.Stream.Null);
        uri = new System.Uri("file:///phrases.lys");
        server.DidOpen(new LilySharp.Lsp.Protocol.DidOpenTextDocumentParams
        {
            TextDocument = new LilySharp.Lsp.Protocol.TextDocumentItem { Uri = uri, Text = text, Version = 1, LanguageId = "lilysharp" },
        });
        return server;
    }

    [Theory]
    [InlineData("kit { hats", 7)]          // where it is played
    [InlineData("phrase hats", 8)]         // where it is declared
    public void HoveringAPhrasesName_ShowsWhatItHolds(string needle, int into)
    {
        var server = Open(Kit, out var uri);
        int at = Kit.IndexOf(needle, System.StringComparison.Ordinal) + into;
        var (line, character) = LilySharp.Lsp.LilySharpLanguageServer.GetLineAndCharacter(Kit, at);
        string? hover = server.Hover(new LilySharp.Lsp.Protocol.TextDocumentPositionParams
        {
            TextDocument = new LilySharp.Lsp.Protocol.TextDocumentIdentifier { Uri = uri },
            Position = new LilySharp.Lsp.Protocol.Position(line, character),
        })?.Contents.Value;
        Assert.NotNull(hover);
        Assert.StartsWith("**Phrase** `hats` — 1 bar (4/4) · used 3 times", hover);
        Assert.Contains("hh  x x  x x  x x  . x", hover);
    }

    [Fact]
    public void ADrumPartsPopup_OffersThePhrasesFirst_ByKitPiece_WithWhatTheyHold()
    {
        string text = Kit.Replace("kit { hats hats }", "kit { hats  }");
        int offset = text.IndexOf("kit { hats  }", System.StringComparison.Ordinal) + "kit { hats ".Length;
        var server = Open(text, out var uri);
        var (line, character) = LilySharp.Lsp.LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        var items = server.Completion(new LilySharp.Lsp.Protocol.CompletionParams
        {
            TextDocument = new LilySharp.Lsp.Protocol.TextDocumentIdentifier { Uri = uri },
            Position = new LilySharp.Lsp.Protocol.Position(line, character),
        })!.Items.OrderBy(i => i.SortText ?? i.Label, System.StringComparer.Ordinal).ToList();

        // The drum phrases lead, hi-hat before snare before bass drum.
        Assert.Equal(new[] { "groove", "hats", "back", "fill", "kick" }, items.Take(5).Select(i => i.Label));
        var hats = items.Single(i => i.Label == "hats");
        Assert.Equal("hi-hat · 1 bar (4/4)", hats.Detail);
        var doc = Assert.IsType<LilySharp.Lsp.Protocol.MarkupContent>(hats.Documentation);
        Assert.Contains("hh  x x  x x  x x  . x", doc.Value);
    }

    [Fact]
    public void AMelody_SaysItsLengthAndNotes_AndDrawsNoGrid()
    {
        var s = Of("theme");
        Assert.False(s.IsDrums);
        Assert.Equal("2 bars (4/4)", s.LengthText);
        Assert.Equal(5, s.NoteCount);
        Assert.Null(s.Grid);
        Assert.StartsWith("**Phrase** `theme` — 2 bars (4/4) · 5 notes · not used yet", s.ToMarkdown());
    }
}
