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

    private static StepResponse Stepped(LilySharpLanguageServer server, System.Uri uri, int offset, int direction, int count, bool semitone) => server.Step(new StepParams
    {
        TextDocument = new TextDocumentIdentifier { Uri = uri },
        Selections = [new StepSelection { Start = offset, End = offset }],
        Direction = direction,
        Semitone = semitone,
        Count = count,
    });

    [Theory]
    [InlineData("c'4@mf d e f |", "mf", 1, 2, true, "c'4@ff d")]
    [InlineData("c'4@mf d e f |", "mf", -1, 1, true, "c'4@mp d")]
    [InlineData("c'4 d e f |", "c'4", 1, 2, false, "c'''4 d")]
    [InlineData("c'4 d e f |", "c'4", -1, 1, false, "c4 d")]
    public void ACount_IsThatManySteps_InOneEdit(string music, string at, int direction, int count, bool semitone, string landed)
    {
        string text = Book(music);
        var server = Open(text, out var uri);
        var response = Stepped(server, uri, text.IndexOf(at, text.IndexOf("m {", System.StringComparison.Ordinal), System.StringComparison.Ordinal), direction, count, semitone);
        Assert.Single(response.Edits);
        Assert.Contains(landed, Applied(text, response));
    }

    [Fact]
    public void ACount_StopsAtTheEndOfTheLadder()
    {
        string text = Book("c'4@mf d e f |");
        var server = Open(text, out var uri);
        var response = Stepped(server, uri, text.IndexOf("mf", System.StringComparison.Ordinal), 1, 60, true);
        Assert.Contains("c'4@", Applied(text, response));
        Assert.DoesNotContain("@mf", Applied(text, response));
    }

    [Fact]
    public void ThePlacement_IsTheOctavePair()
    {
        string text = Book("c'4@staccato d e f |");
        var server = Open(text, out var uri);
        Assert.Contains("@staccato.up", Applied(text, Stepped(server, uri, text.IndexOf("staccato", System.StringComparison.Ordinal), 1, 1, false)));
    }

    [Fact]
    public void TheMarks_AreThoseWithANameToStep_AndStandAtTheirAt()
    {
        string text = Book("c'4@mf d@staccato e@fermata f@text(\"x\") g@stopped |");
        var server = Open(text, out var uri);
        var marks = server.NoteHeads(new NoteHeadsParams { TextDocument = new TextDocumentIdentifier { Uri = uri } }).Marks;
        string[] words = [.. marks.OrderBy(p => p).Select(p => new string(text[p..].TakeWhile(c => !" |".Contains(c)).ToArray()))];
        Assert.Contains("@mf", words);
        Assert.Contains("@staccato", words);
        Assert.DoesNotContain(words, w => w.StartsWith("@text", System.StringComparison.Ordinal));
        Assert.DoesNotContain("@stopped", words);   // an articulation alone in the world
        // A step on anything else in the list must not fall through to the note.
        foreach (int at in marks)
        {
            var stepped = Applied(text, Stepped(server, uri, at + 1, 1, 1, true));
            Assert.Contains("c'4@", stepped);
        }
    }

    private static StepSelection At(int offset) => new() { Start = offset, End = offset };

    private static string TheMusic(string text)
    {
        const string open = "section A { m { ";
        int from = text.IndexOf(open, System.StringComparison.Ordinal) + open.Length;
        return text[from..text.IndexOf(" } }", from, System.StringComparison.Ordinal)];
    }

    // By octaves (positions 0): the owner's chord, and the note after the chord too. A chord's
    // members hang on its first (probed: <c' e' g> is C4 E5 G4), so moving another moves none.
    [Theory]
    [InlineData("<c' e g>4 <d e g> |", "c'", 1, 0, "<c'' e, g,>4 <d, e g> |")]
    [InlineData("<c' e g>4 d |", "c'", -1, 0, "<c e' g'>4 d' |")]
    [InlineData("<c' e g>4 d |", "e g", 1, 0, "<c' e' g>4 d |")]
    [InlineData("c'4 d e f |", "c'", 1, 0, "c''4 d, e f |")]
    [InlineData("c'4 d e f | g a b c |", "f |", 1, 0, "c'4 d e f' | g, a b c |")]
    // By staff positions: a leap, down past the note before; and none to correct.
    [InlineData("c'4 d e f |", "c'", 1, 5, "a'4 d, e f |")]
    [InlineData("c'4 e g c |", "c'", -1, 2, "a4 e' g c |")]
    [InlineData("c'4 d e f |", "c'", 1, 1, "d'4 d e f |")]
    public void ADraggedNote_MovesAlone(string music, string word, int direction, int positions, string expected)
        => Assert.Equal(expected, TheMusic(DraggedAt(music, word, direction, positions, hold: true)));

    [Theory]
    [InlineData("<c' e g>4 <d e g> |", "c'", 1, 0, "<c'' e g>4 <d e g> |")]
    [InlineData("c'4 d e f |", "c'", 1, 0, "c''4 d e f |")]
    public void TheKeys_LeaveTheNotesAfterToFollow(string music, string word, int direction, int positions, string expected)
        => Assert.Equal(expected, TheMusic(DraggedAt(music, word, direction, positions, hold: false)));

    [Fact]
    public void AbsoluteOctaves_HaveNothingToHold()
    {
        string text = "octave absolute\n" + Book("<c' e' g'>4 d' |");
        var server = Open(text, out var uri);
        int at = text.IndexOf("c'", System.StringComparison.Ordinal);
        var r = server.Step(new StepParams { TextDocument = new TextDocumentIdentifier { Uri = uri }, Selections = [At(at)], Direction = 1, HoldOthers = true });
        Assert.Contains("<c'' e' g'>4 d' |", Applied(text, r));
    }

    private static string DraggedAt(string music, string word, int direction, int positions, bool hold)
    {
        string text = Book(music);
        var server = Open(text, out var uri);
        int music0 = text.IndexOf("{ m {", System.StringComparison.Ordinal);
        var r = server.Step(new StepParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Selections = [At(text.IndexOf(word, music0, System.StringComparison.Ordinal))],
            Direction = direction,
            Semitone = positions > 0,
            StaffPositions = positions,
            HoldOthers = hold,
        });
        return Applied(text, r);
    }

    private static string Chromatic(string music, string word, int direction, int semitones, string part = "clef treble", bool hold = true)
    {
        string text = Book(music, part);
        var server = Open(text, out var uri);
        int music0 = text.IndexOf("{ m {", System.StringComparison.Ordinal);
        var r = server.Step(new StepParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Selections = [At(text.IndexOf(word, music0, System.StringComparison.Ordinal))],
            Direction = direction,
            Count = semitones,
            Chromatic = true,
            HoldOthers = hold,
        });
        return TheMusic(Applied(text, r));
    }

    [Theory]
    // Up by sharps and down by flats, a white key as it is.
    [InlineData("c'4 d e f |", "c'", 1, 1, "cis'4 d e f |")]
    [InlineData("c'4 d e f |", "c'", 1, 2, "d'4 d e f |")]
    [InlineData("c'4 d e f |", "d e", -1, 1, "c'4 des e f |")]
    [InlineData("c'4 d e f |", "e f", 1, 1, "c'4 d f f |")]
    [InlineData("c'4 d e f |", "c'", -1, 1, "b4 d e f |")]
    // Past an octave, and the notes after held.
    [InlineData("c'4 d e f |", "c'", 1, 12, "c''4 d, e f |")]
    [InlineData("c'4 d e f |", "c'", 1, 13, "cis''4 d, e f |")]
    // A chord's first: its members held, and the note after it.
    [InlineData("<c' e g>2 d2 |", "c'", 1, 12, "<c'' e, g,>2 d,2 |")]
    // A member alone.
    [InlineData("<c' e g>2 d2 |", "e g", 1, 1, "<c' f g>2 d2 |")]
    public void AltDrag_GoesBySemitones(string music, string word, int direction, int semitones, string expected)
        => Assert.Equal(expected, Chromatic(music, word, direction, semitones));

    [Theory]
    [InlineData("f'4 g a b |", "f'", 1, 1, "fis'4 g a b |")]    // the key's own, going up
    [InlineData("g'4 a b c |", "g'", -1, 1, "fis'4 a b c |")]   // the key's own, going down
    [InlineData("fis'4 g a b |", "fis'", -1, 1, "f'4 g a b |")] // a natural against the key
    public void AltDrag_SpellsByTheKey(string music, string word, int direction, int semitones, string expected)
        => Assert.Equal(expected, Chromatic(music, word, direction, semitones, "clef treble\n  key g major"));

    [Fact]
    public void AltDrag_OnNoNote_IsNoEdit()
    {
        string text = Book("r4 d e f |");
        var server = Open(text, out var uri);
        var r = server.Step(new StepParams { TextDocument = new TextDocumentIdentifier { Uri = uri }, Selections = [At(text.IndexOf("r4", System.StringComparison.Ordinal))], Direction = 1, Count = 1, Chromatic = true });
        Assert.Empty(r.Edits);
    }

    [Fact]
    public void AltDrag_ComesWithItsSound()
    {
        string text = Book("c'4 d e f |");
        var server = Open(text, out var uri);
        var r = server.Step(new StepParams { TextDocument = new TextDocumentIdentifier { Uri = uri }, Selections = [At(text.IndexOf("c'", text.IndexOf("{ m {", System.StringComparison.Ordinal), System.StringComparison.Ordinal))], Direction = 1, Count = 2, Chromatic = true });
        Assert.Equal(62, Assert.Single(r.Pitches));
    }

    private const string ChordBook = "tempo 100\npart guitar { clef treble }\nchords prog { section A { C G } }\nsection A { guitar { c'4@chord(C) d e@diagram(x32010) f | <c e g>2@chord g2@chord(G) | } }\nform { A }\nscore { chords prog\n staff guitar }\n";

    [Fact]
    public void TheMarks_NameATempoAndAChordsShape_AndNotAChordsInversion()
    {
        string text = ChordBook;
        var server = Open(text, out var uri);
        var answer = server.NoteHeads(new NoteHeadsParams { TextDocument = new TextDocumentIdentifier { Uri = uri } });
        string[] words = [.. answer.Marks.OrderBy(p => p).Select(p => new string(text[p..].TakeWhile(c => !" |\n".Contains(c)).ToArray()))];
        Assert.Contains("100", words);
        Assert.Contains("C", words);
        Assert.Contains("G", words);
        Assert.Contains("@chord(C)", words);
        Assert.Contains("@diagram(x32010)", words);
        Assert.Contains("@chord(G)", words);
        Assert.DoesNotContain("@chord", words);   // bare, on <c e g>: its step inverts the chord
        Assert.Equal([text.IndexOf("100", System.StringComparison.Ordinal)], answer.FineMarks);
        // Each moves itself, never a note.
        foreach (int at in answer.Marks)
        {
            string stepped = Applied(text, Stepped(server, uri, at + 1, 1, 1, true));
            Assert.Contains("<c e g>2", stepped);
            Assert.Contains("c'4", stepped);
        }
    }

    [Theory]
    [InlineData(true, 3, "tempo 103\n")]
    [InlineData(false, 2, "tempo 120\n")]
    public void ATempo_GoesByTheBeat_AndByTenWithCtrl(bool semitone, int count, string expected)
    {
        string text = ChordBook;
        var server = Open(text, out var uri);
        Assert.Contains(expected, Applied(text, Stepped(server, uri, text.IndexOf("100", System.StringComparison.Ordinal), 1, count, semitone)));
    }

    // The drag's one walk comes to what the keys' run of steps does, then every other note held.
    [Theory]
    [InlineData("c'4 d e f | g a b c |", "c'", 1, 1, "clef treble")]
    [InlineData("c'4 d e f | g a b c |", "c'", 1, 5, "clef treble")]
    [InlineData("c'4 d e f | g a b c |", "c'", 1, 9, "clef treble")]
    [InlineData("c'4 d e f | g a b c |", "c'", -1, 3, "clef treble")]
    [InlineData("c'4 d e f | g a b c |", "f |", -1, 8, "clef treble")]
    [InlineData("fis'4 g a b |", "fis'", 1, 1, "clef treble\n  key g major")]
    [InlineData("f'4 g a b |", "f'", 1, 1, "clef treble\n  key g major")]   // off the key: its own letter first
    [InlineData("f'4 g a b |", "f'", -1, 3, "clef treble\n  key g major")]
    [InlineData("bes'4 c d es |", "bes'", 1, 2, "clef treble\n  key bes major")]
    [InlineData("<c' e g>2 <d f a>2 |", "c'", 1, 3, "clef treble")]
    [InlineData("<c' e g>2 <d f a>2 |", "e g", 1, 2, "clef treble")]
    [InlineData("<c' e g>2 <d f a>2 |", "d f", -1, 4, "clef treble")]
    public void TheWalk_ComesToWhatTheStepsDo(string music, string word, int direction, int positions, string part)
    {
        string text = Book(music, part);
        int at = text.IndexOf(word, text.IndexOf("{ m {", System.StringComparison.Ordinal), System.StringComparison.Ordinal);
        var server = Open(text, out var uri);
        string stepped = Applied(text, Step(server, uri, at, direction, positions));
        string keys = LilySharp.Core.Editing.NoteStepper.HoldOtherPitches(text, stepped, at);
        string walked = Applied(text, server.Step(new StepParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Selections = [At(at)],
            Direction = direction,
            Semitone = true,
            StaffPositions = positions,
            HoldOthers = true,
        }));
        Assert.Equal(keys, walked);
    }

    [Fact]
    public void AHeldNote_IsDrawn_AndTheDocumentStaysAsItIs()
    {
        string text = Book("c'4 d e f |");
        string landed = Book("e'4 d, e f |");
        var server = Open(text, out var uri);
        var doc = new TextDocumentIdentifier { Uri = uri };
        string SvgOf(string? drag) => server.GetSvg(new SvgParams { TextDocument = doc, DragText = drag }).Svg!;
        string first = SvgOf(null);
        string drawn = SvgOf(landed);
        Assert.NotEqual(first, drawn);
        // The same text written is the same picture.
        var other = Open(landed, out var otherUri);
        Assert.Equal(other.GetSvg(new SvgParams { TextDocument = new TextDocumentIdentifier { Uri = otherUri } }).Svg, drawn);
        // ...and the document is as it was: its own picture, its own steps.
        Assert.Equal(first, SvgOf(null));
        Assert.Equal(1, server.Step(new StepParams { TextDocument = doc, Selections = [At(text.IndexOf("c'", text.IndexOf("{ m {", System.StringComparison.Ordinal), System.StringComparison.Ordinal))], Direction = 1, Semitone = true, HoldOthers = true, StaffPositions = 1 }).Edits.Length);
    }

    [Fact]
    public void ADrum_IsNoPitch()
    {
        string text = "part k { clef percussion }\nsection A { k { bd4 sn hh cb | } }\nform { A }\nscore { staff k }\n";
        var server = Open(text, out var uri);
        Assert.Empty(server.NoteHeads(new NoteHeadsParams { TextDocument = new TextDocumentIdentifier { Uri = uri } }).Positions);
    }
}
