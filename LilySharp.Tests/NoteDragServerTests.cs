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
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// What the preview's note drag asks of the server (owner's request 2026-11-02): the key's tone
/// some staff positions away, as one edit, and which marks of the score are pitches to take hold of.
/// </summary>
[Trait("Category", "Unit")]
public class NoteDragServerTests
{
    private static string Book(string music, string part = "clef treble") =>
        "part m { " + part + " }\nsection A { m { " + music + " } }\nform { A }\nscore { staff m }\n";

    private static LilySharpLanguageServer Open(string text, out System.Uri uri)
    {
        var server = new LilySharpLanguageServer(System.IO.Stream.Null, System.IO.Stream.Null);
        uri = new System.Uri("file:///drag.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        return server;
    }

    private static StepResponse Step(LilySharpLanguageServer server, System.Uri uri, int offset, int direction, int count) => server.Step(new StepParams
    {
        TextDocument = new TextDocumentIdentifier { Uri = uri },
        Selections = [new StepSelection { Start = offset, End = offset }],
        Direction = direction,
        Semitone = true,
        StaffPositions = count,
    });

    private static string Applied(string text, StepResponse r)
    {
        foreach (var e in r.Edits.OrderByDescending(e => e.Start))
            text = text[..e.Start] + e.NewText + text[e.End..];
        return text;
    }

    /// <summary>The text after `count` plain steps of the key, each on what the last left.</summary>
    private static string OneByOne(string text, int offset, int direction, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var server = Open(text, out var uri);
            text = Applied(text, server.Step(new StepParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                Selections = [new StepSelection { Start = offset, End = offset }],
                Direction = direction,
                Semitone = true,
            }));
        }
        return text;
    }

    [Theory]
    [InlineData("c'4 d' e' f' |", "c'4", 1, 3)]
    [InlineData("c'4 d' e' f' |", "c'4", -1, 3)]
    [InlineData("c'4 d' e' f' |", "e' f'", 1, 8)]
    [InlineData("c'4 d e f |", "d e", 1, 4)]
    [InlineData("<c' e' g'>4 d e f |", "e' g'", 1, 2)]
    public void OnTheKey_APositionIsAStep_InOneEdit(string music, string at, int direction, int count)
    {
        string text = Book(music);
        int offset = text.IndexOf(at, text.IndexOf("m {", System.StringComparison.Ordinal), System.StringComparison.Ordinal);
        var server = Open(text, out var uri);
        var response = Step(server, uri, offset, direction, count);
        Assert.False(response.Fallback);
        Assert.Single(response.Edits);
        Assert.Equal(OneByOne(text, offset, direction, count), Applied(text, response));
    }

    // The pale head shows a staff position, and the note lands on it, though a note off the key
    // takes two steps of the key for the first of them (F in G major: F sharp, then G).
    [Theory]
    [InlineData("f'4 a b c |", "f'4", "clef treble key g major", 1, 1, "g'4")]
    [InlineData("f'4 a b c |", "f'4", "clef treble key g major", 1, 2, "a'4")]
    [InlineData("g'4 a b c |", "g'4", "clef treble key g major", -1, 1, "fis'4")]
    [InlineData("a'4 a b c |", "a'4", "clef treble key g major", -1, 2, "fis'4")]
    [InlineData("cis'4 d e f |", "cis'4", "clef treble", -1, 1, "b4")]
    [InlineData("cis'4 d e f |", "cis'4", "clef treble", 1, 1, "d'4")]
    [InlineData("ees'4 d e f |", "ees'4", "clef treble", 1, 1, "f'4")]
    [InlineData("b'4 d e f |", "b'4", "clef treble", 1, 1, "c''4")]
    public void ItLands_OnTheStaffPositionShown(string music, string at, string part, int direction, int count, string landed)
    {
        string text = Book(music, part);
        var server = Open(text, out var uri);
        var response = Step(server, uri, text.IndexOf(at, System.StringComparison.Ordinal), direction, count);
        Assert.Single(response.Edits);
        Assert.Contains(landed + " ", Applied(text, response));
    }

    [Fact]
    public void ItSounds_WhereItLands()
    {
        string text = Book("c'4 d e f |");
        var server = Open(text, out var uri);
        var response = Step(server, uri, text.IndexOf("c'4", System.StringComparison.Ordinal), 1, 4);
        Assert.Equal(67, Assert.Single(response.Pitches));   // G4
    }

    [Fact]
    public void OneStep_IsAsItWas()
    {
        string text = Book("c'4 d e f |");
        int offset = text.IndexOf("c'4", System.StringComparison.Ordinal);
        var server = Open(text, out var uri);
        var plain = server.Step(new StepParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Selections = [new StepSelection { Start = offset, End = offset }],
            Direction = 1,
            Semitone = true,
        });
        Assert.Equal(Applied(text, Step(server, uri, offset, 1, 1)), Applied(text, plain));
    }

    [Fact]
    public void NoStaffPositions_IsTheKeysOneStep()
    {
        string text = Book("f'4 a b c |", "clef treble key g major");
        var server = Open(text, out var uri);
        var plain = server.Step(new StepParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Selections = [new StepSelection { Start = text.IndexOf("f'4", System.StringComparison.Ordinal), End = text.IndexOf("f'4", System.StringComparison.Ordinal) }],
            Direction = 1,
            Semitone = true,
        });
        Assert.Contains("fis'4", Applied(text, plain));
    }

    [Fact]
    public void WhatIsNoPitch_IsNotStepped()
    {
        string text = Book("r4 d e f |");
        var server = Open(text, out var uri);
        Assert.Empty(Step(server, uri, text.IndexOf("r4", System.StringComparison.Ordinal), 1, 3).Edits);
    }

    [Fact]
    public void TheHeads_AreThePitches_AndNothingElseWithADataPos()
    {
        string text = Book("fis'4 <e' g'>8 r8 /4 bes |", "clef treble key g major");
        var server = Open(text, out var uri);
        var at = server.NoteHeads(new NoteHeadsParams { TextDocument = new TextDocumentIdentifier { Uri = uri } }).Positions;
        string[] pitches = [.. at.OrderBy(p => p).Select(p => new string(text[p..].TakeWhile(c => !" <>|0123456789".Contains(c)).ToArray()))];
        Assert.Equal(["fis'", "e'", "g'", "bes"], pitches);
    }

    // A ledger line drawn for a dragged note is as thick and as long as the score's.
    [Theory]
    [InlineData("", 0.2, 0.25)]
    [InlineData("layout { LedgerLine.thickness 1.0 0.3 }\n", 0.4, 0.25)]
    [InlineData("layout { LedgerLine.lengthFraction 0.5 }\n", 0.2, 0.5)]
    [InlineData("layout { StaffLine.thickness 2.0 }\n", 0.3, 0.25)]
    public void TheLedgerLine_IsTheFilesOwn(string layout, double thickness, double fraction)
    {
        string text = layout + Book("c'4 d e f |");
        var server = Open(text, out var uri);
        var answer = server.NoteHeads(new NoteHeadsParams { TextDocument = new TextDocumentIdentifier { Uri = uri } });
        Assert.Equal(thickness, answer.LedgerThickness, 6);
        Assert.Equal(fraction, answer.LedgerLengthFraction, 6);
    }

    [Fact]
    public void ADrum_IsNoPitch()
    {
        string text = "part k { clef percussion }\nsection A { k { bd4 sn hh cb | } }\nform { A }\nscore { staff k }\n";
        var server = Open(text, out var uri);
        Assert.Empty(server.NoteHeads(new NoteHeadsParams { TextDocument = new TextDocumentIdentifier { Uri = uri } }).Positions);
    }
}
