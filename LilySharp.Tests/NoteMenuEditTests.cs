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
using LilySharp.Core.Editing;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The preview's right-click menu on a note (owner's choice 2026-11-03): a note to a rest and back,
/// a chord's member out, a mark on and off.
/// </summary>
[Trait("Category", "Unit")]
public class NoteMenuEditTests
{
    private const string Open = "section A { m { ";
    private static string Book(string music, string part = "clef treble") =>
        "part m { " + part + " }\n" + Open + music + " } }\nform { A }\nscore { staff m }\n";

    private static int At(string text, string word) => text.IndexOf(word, text.IndexOf(Open, System.StringComparison.Ordinal), System.StringComparison.Ordinal);

    private static string MusicOf(string? text)
    {
        Assert.NotNull(text);
        int from = text!.IndexOf(Open, System.StringComparison.Ordinal) + Open.Length;
        return text[from..text.IndexOf(" } }", from, System.StringComparison.Ordinal)];
    }

    private static void IsClean(string text) => Assert.False(SyntaxTree.Parse(text).HasErrors, text);

    private static string Pitches(string text) => string.Join(" ",
        LilySharp.Core.Semantics.ResolvedPitches.ForFile(SyntaxTree.Parse(text))!
            .Where(e => e.Position >= text.IndexOf(Open, System.StringComparison.Ordinal)).Select(e => e.Pitch));

    [Theory]
    [InlineData("c'4 d e f |", "c'", "r4 d' e f |")]                  // the d hung on it
    [InlineData("c'4 d e f |", "d e", "c'4 r e f |")]                 // no length written: the one before's
    [InlineData("c'4 d e f |", "e f", "c'4 d r f |")]
    [InlineData("c'4. d8 e4 f |", "c'", "r4. d'8 e4 f |")]            // dots go with it
    [InlineData("c'4@staccato d e f |", "c'", "r4 d' e f |")]         // what was on it goes
    [InlineData("c'4@mf d e f |", "c'", "r4 d' e f |")]
    [InlineData("c''4 d e f |", "c''", "r4 d'' e f |")]               // the walk begins anew
    [InlineData("<c' e g>4 d e f |", "e g", "r4 d' e f |")]           // a member: the whole chord
    [InlineData("c'4~ c d e |", "c'4", "r4 c' d e |")]                // and its tie
    public void ANote_BecomesARest_TheOthersWhereTheyWere(string music, string word, string expected)
    {
        string text = Book(music);
        string? after = NoteStepper.NoteToRest(text, SyntaxTree.Parse(text), At(text, word));
        Assert.Equal(expected, MusicOf(after));
        IsClean(after!);
    }

    [Theory]
    [InlineData("<c' e g>2 d2 |", "e g", "<c' g>2 d2 |")]
    [InlineData("<c' e g>2 d2 |", "g>", "<c' e>2 d2 |")]
    [InlineData("<c' e g>2 d2 |", "c'", "<e' g>2 d2 |")]              // the anchor out: the rest stay
    [InlineData("<c' e>2 d2 |", "e>", "c'2 d2 |")]                    // one left: a note
    [InlineData("<c' e>2 d2 |", "c'", "e'2 d2 |")]
    public void AChordsMember_ComesOut(string music, string word, string expected)
    {
        string text = Book(music);
        string? after = NoteStepper.RemoveChordMember(text, SyntaxTree.Parse(text), At(text, word));
        Assert.Equal(expected, MusicOf(after));
        IsClean(after!);
    }

    [Fact]
    public void TheNotesStayWhereTheyWere_WhenTheFirstOfAChordGoes()
    {
        string text = Book("<c' e' g>2 <d f>2 |");     // C4 E5 G4, D4 F4
        string? after = NoteStepper.RemoveChordMember(text, SyntaxTree.Parse(text), At(text, "c'"));
        Assert.Equal("E5 G4 D4 F4", Pitches(after!));
    }

    [Fact]
    public void ANote_IsNoMemberToTakeOut()
    {
        string text = Book("c'4 d e f |");
        Assert.Null(NoteStepper.RemoveChordMember(text, SyntaxTree.Parse(text), At(text, "c'")));
    }

    [Theory]
    [InlineData("c'4 r e f |", "r e", "c'4 c e f |")]                 // the note before's pitch
    [InlineData("c'4 r2 f4 |", "r2", "c'4 c2 f4 |")]                  // its length kept
    [InlineData("r4 d' e f |", "r4", "c'4 d e f |")]                  // nothing before: middle C, the d the same
    public void ARest_BecomesANote(string music, string word, string expected)
    {
        string text = Book(music);
        string? after = NoteStepper.RestToNote(text, SyntaxTree.Parse(text), At(text, word));
        Assert.Equal(expected, MusicOf(after));
        IsClean(after!);
    }

    [Fact]
    public void AndThereAndBack_IsWhatItWas()
    {
        string text = Book("g'4 a b c |");
        string? rest = NoteStepper.NoteToRest(text, SyntaxTree.Parse(text), At(text, "a b"));
        string? back = NoteStepper.RestToNote(rest!, SyntaxTree.Parse(rest!), At(rest!, "r b"));
        Assert.Equal("G4 G4 B4 C5", Pitches(back!));   // at the note before's pitch, the others held
    }

    [Theory]
    [InlineData("c'4 d e f |", "c'", "staccato", false, "c'4@staccato d e f |")]
    [InlineData("c' d e f |", "c'", "accent", false, "c'@accent d e f |")]
    [InlineData("c'4@staccato d e f |", "c'", "staccato", false, "c'4 d e f |")]     // off again
    [InlineData("c'4@staccato d e f |", "c'", "accent", false, "c'4@staccato@accent d e f |")]
    [InlineData("c'4@accent@staccato d e f |", "c'", "staccato", false, "c'4@accent d e f |")]
    [InlineData("<c' e g>4 d e f |", "e g", "staccato", false, "<c' e g>4@staccato d e f |")]  // on the chord
    [InlineData("c'4 d e f |", "c'", "mf", true, "c'4@mf d e f |")]
    [InlineData("c'4@mf d e f |", "c'", "mf", true, "c'4 d e f |")]
    [InlineData("c'4@mf d e f |", "c'", "ff", true, "c'4@ff d e f |")]               // one dynamic a note
    [InlineData("c'4@staccato@mf d e f |", "c'", "p", true, "c'4@staccato@p d e f |")]
    [InlineData("c'4~ c d e |", "c'4", "staccato", false, "c'4@staccato~ c d e |")]
    public void AMark_GoesOnAndComesOff(string music, string word, string mark, bool dynamic, string expected)
    {
        string text = Book(music);
        string? after = NoteStepper.ToggleNoteMark(text, SyntaxTree.Parse(text), At(text, word), mark, dynamic);
        Assert.Equal(expected, MusicOf(after));
        IsClean(after!);
    }

    [Fact]
    public void NotOnARest_NorWhereThereIsNoPitch()
    {
        string text = Book("r4 d e f |");
        var tree = SyntaxTree.Parse(text);
        Assert.Null(NoteStepper.NoteToRest(text, tree, At(text, "r4")));
        Assert.Null(NoteStepper.ToggleNoteMark(text, tree, At(text, "r4"), "staccato", false));
        Assert.Null(NoteStepper.RestToNote(text, tree, At(text, "d e")));
    }

    private static string Edited(string text, string kind, string word, string? mark = null)
    {
        var server = new LilySharpLanguageServer(System.IO.Stream.Null, System.IO.Stream.Null);
        var uri = new System.Uri("file:///menu.lys");
        server.DidOpen(new DidOpenTextDocumentParams { TextDocument = new TextDocumentItem { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 } });
        var r = server.NoteEdit(new NoteEditParams { TextDocument = new TextDocumentIdentifier { Uri = uri }, Position = At(text, word), Kind = kind, Mark = mark });
        foreach (var e in r.Edits.OrderByDescending(e => e.Start))
            text = text[..e.Start] + e.NewText + text[e.End..];
        return MusicOf(text);
    }

    [Theory]
    [InlineData("rest", "c'", null, "r4 d' e f |")]
    [InlineData("staccato", "c'", "staccato", "c'4@staccato d e f |")]
    [InlineData("mf", "c'", "mf", "c'4@mf d e f |")]
    public void TheServer_AnswersOneEdit(string kind, string word, string? mark, string expected)
        => Assert.Equal(expected, Edited(Book("c'4 d e f |"), kind == "rest" ? "rest" : "mark", word, mark));

    [Fact]
    public void TheServer_NamesTheRests()
    {
        string text = Book("c'4 r d r2 |");
        var server = new LilySharpLanguageServer(System.IO.Stream.Null, System.IO.Stream.Null);
        var uri = new System.Uri("file:///menu.lys");
        server.DidOpen(new DidOpenTextDocumentParams { TextDocument = new TextDocumentItem { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 } });
        var rests = server.NoteHeads(new NoteHeadsParams { TextDocument = new TextDocumentIdentifier { Uri = uri } }).Rests;
        Assert.Equal([At(text, "r d"), At(text, "r2")], rests);
    }
}
