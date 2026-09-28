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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// <c>lilysharp/step</c> (Ctrl+Alt+Up / Ctrl+Alt+Down) and <c>lilysharp/auditionAt</c> — the
/// owner's decision of 2026-09-28 (<see cref="LilySharp.Core.Editing.NoteStepper"/>). The
/// source is written with its selections marked: <c>‸</c> is a caret, <c>«…»</c> a selection.
/// </summary>
[Trait("Category", "Unit")]
public class StepRequestTests
{
    private const char Caret = '‸';

    /// <summary>A guitar part (the default tuning, E2 A2 D3 G3 B3 E4), absolute octaves.</summary>
    private static string Guitar(string music) => $$"""
        octave absolute
        part gt { clef treble }
        section A { gt { {{music}} } }
        form main { A }
        score main { staff gt }
        """;

    private static string Plain(string music) => MusicSource.Wrap(music, "octave absolute");

    /// <summary>Strips the markers: the text and each selection as (start, end).</summary>
    private static (string Text, List<(int Start, int End)> Selections) Marked(string marked)
    {
        var sb = new System.Text.StringBuilder();
        var selections = new List<(int, int)>();
        int open = -1;
        foreach (char ch in marked)
        {
            if (ch == Caret)
                selections.Add((sb.Length, sb.Length));
            else if (ch == '«')
                open = sb.Length;
            else if (ch == '»')
                selections.Add((open, sb.Length));
            else
                sb.Append(ch);
        }
        return (sb.ToString(), selections);
    }

    private static readonly Uri DocUri = new("file:///step.lys");

    private static LilySharpLanguageServer Open(string text)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = DocUri, Text = text, LanguageId = "lilysharp", Version = 3 },
        });
        return server;
    }

    /// <summary>Steps the marked source; the response and the text with its edits applied.</summary>
    private static (StepResponse Response, string After) Step(string marked, int direction)
    {
        var (text, selections) = Marked(marked);
        var response = Open(text).Step(new StepParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocUri },
            Selections = selections.Select(s => new StepSelection { Start = s.Start, End = s.End }).ToArray(),
            Direction = direction,
        });
        string after = text;
        foreach (var e in response.Edits.OrderByDescending(e => e.Start))
            after = after[..e.Start] + e.NewText + after[e.End..];
        return (response, after);
    }

    private static string Up(string marked, Func<string, string> book)
        => Stepped(marked, +1, book);

    private static string Down(string marked, Func<string, string> book)
        => Stepped(marked, -1, book);

    private static string Stepped(string marked, int direction, Func<string, string> book)
    {
        var (response, after) = Step(book(marked), direction);
        Assert.False(response.Fallback, response.Error);
        Assert.Equal(3, response.Version);
        return after;
    }

    // ---------------------------------------------------------------- @chord voicings

    [Fact]
    public void AVoicingIndex_StepsByOne()
    {
        Assert.Equal(Guitar("c'1@chord(Cm7 3)"), Up("c'1@chord(Cm7 2‸)", Guitar));
        Assert.Equal(Guitar("c'1@chord(Cm7 1)"), Down("c'1@chord(C‸m7 2)", Guitar));
        // The mute words stay where they are.
        Assert.Equal(Guitar("c'1@chord(Cm7 3 mute 1)"), Up("c'1@chord(Cm7 2 mute‸ 1)", Guitar));
    }

    [Fact]
    public void UpAtTheLastIndex_ChangesNothing_AndSaysTheRange()
    {
        var (response, after) = Step(Guitar("c'1@chord(Cm7 51‸)"), +1);
        Assert.False(response.Fallback);
        Assert.Empty(response.Edits);
        Assert.Equal("Cm7: voicings 0–51", response.Message);
        Assert.Equal(Guitar("c'1@chord(Cm7 51)"), after);
    }

    [Fact]
    public void DownAtZero_GoesBackToTheNameAlone_TheMuteWithIt()
    {
        Assert.Equal(Guitar("c'1@chord(Cm7)"), Down("c'1@chord(Cm7 0‸)", Guitar));
        Assert.Equal(Guitar("c'1@chord(Cm7)"), Down("c'1@chord(Cm7 0 mute 1‸)", Guitar));
        // `mute` alone is index 0: Down takes it back to the name too.
        Assert.Equal(Guitar("d'1@chord(D)"), Down("d'1@chord(D mute‸ 5)", Guitar));
    }

    [Fact]
    public void TheNameAlone_UpShowsVoicingZero_DownDoesNothing()
    {
        Assert.Equal(Guitar("c'1@chord(Cm7 0)"), Up("c'1@chord(Cm7‸)", Guitar));
        var (response, after) = Step(Guitar("c'1@chord(Cm7‸)"), -1);
        Assert.False(response.Fallback);   // on a chord: handled, just no move
        Assert.Empty(response.Edits);
        Assert.Equal(Guitar("c'1@chord(Cm7)"), after);
    }

    [Fact]
    public void TheImplicitZeroOfMute_StepsUpToOne()
        => Assert.Equal(Guitar("d'1@chord(D 1 mute 5)"), Up("d'1@chord(D‸ mute 5)", Guitar));

    [Fact]
    public void AWrittenOutDiagram_IsNotSteppable()
        => Assert.True(Step(Guitar("c'1@chord(x3x5‸46)"), +1).Response.Fallback);

    // ---------------------------------------------------------------- notes

    [Fact]
    public void ANote_GainsOrLosesOneMark_ItsDurationAndDotsKept()
    {
        Assert.Equal(Plain("c''4. d'8"), Up("c'4.‸ d'8", Plain));
        Assert.Equal(Plain("c4 d'8"), Up("c,‸4 d'8", Plain));            // a comma goes first
        Assert.Equal(Plain("c4 d'8"), Down("‸c'4 d'8", Plain));
        Assert.Equal(Plain("c,4 d'8"), Down("c‸4 d'8", Plain));
        Assert.Equal(Plain("cis'''16 r"), Up("ci‸s''16 r", Plain));     // a third '
        Assert.Equal(Plain("c,,,2 r"), Down("c,,‸2 r", Plain));
    }

    [Fact]
    public void AChordMember_MovesAlone()
    {
        Assert.Equal(Plain("<c' e'' g'>4"), Up("<c' e‸' g'>4", Plain));
        Assert.Equal(Plain("<c' e g'>4"), Down("<c' e'‸ g'>4", Plain));
    }

    [Fact]
    public void AChordAsAWhole_StepsTheMarksAfterItsBracket()
        => Assert.Equal(Plain("<c' e' g'>'4"), Up("<c' e' g'>‸4", Plain));

    [Fact]
    public void EveryCaret_Steps()
        => Assert.Equal(Plain("c''4 d'4 e''4"), Up("c'‸4 d'4 e'4‸", Plain));

    [Fact]
    public void ASelection_StepsEveryNoteInIt_RestsNot()
        => Assert.Equal(Plain("c'4 d''4 r4 <e'' g''>4 f'4"), Up("c'4 «d'4 r4 <e' g'>4» f'4", Plain));

    [Fact]
    public void NothingSteppable_AnswersFallback()
    {
        var (response, after) = Step(Plain("c'4 r‸4 d'2 |"), +1);
        Assert.True(response.Fallback);
        Assert.Empty(response.Edits);
        Assert.Equal(Plain("c'4 r4 d'2 |"), after);
        Assert.True(Step(Plain("c'4 r4 d'2 |‸"), -1).Response.Fallback);
    }

    // ---------------------------------------------------------------- what sounds

    /// <summary>Cm7 voicing 2 is x35343 on the standard tuning, counted by hand string by string:
    /// A2+3 = C3 (48), D3+5 = G3 (55), G3+3 = B♭3 (58), B3+4 = E♭4 (63), E4+3 = G4 (67); the low E
    /// is muted, so silent. (The request that asked for this net listed C4 (60) for the third
    /// string; the diagram's third fret on G3 is B♭3 — the seventh of Cm7.)</summary>
    private static readonly int[] Cm7Voicing2 = [48, 55, 58, 63, 67];

    [Fact]
    public void AStepToAVoicing_SoundsItsStrings()
    {
        var (response, _) = Step(Guitar("c'1@chord(Cm7 1‸)"), +1);
        Assert.Equal(Cm7Voicing2, response.Pitches);
    }

    [Fact]
    public void ASteppedNote_SoundsItsNewPitch()
    {
        Assert.Equal([84], Step(Plain("c'‸4 d'4"), +1).Response.Pitches);          // c'' = C6 (Lily#'s absolute c is C4)
        Assert.Equal([88], Step(Plain("<c' e'‸ g'>4"), +1).Response.Pitches);      // e'' = E6
        Assert.Equal([48], Step(Plain("c‸4 d'4"), -1).Response.Pitches);           // c = C4, c, = C3
    }

    private static AuditionAtResponse AuditionAt(string marked)
    {
        var (text, selections) = Marked(marked);
        return Open(text).AuditionAt(new AuditionAtParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocUri },
            Offset = selections[0].Start,
        });
    }

    [Fact]
    public void TheCaret_SoundsWhatItIsOn()
    {
        var voicing = AuditionAt(Guitar("c'1@chord(Cm7 ‸2)"));
        Assert.Equal("voicing", voicing.Kind);
        Assert.Equal(Cm7Voicing2, voicing.Pitches);

        var note = AuditionAt(Plain("c'4 d'‸4"));
        Assert.Equal("note", note.Kind);
        Assert.Equal([74], note.Pitches);

        var member = AuditionAt(Plain("<c' ‸e' g'>4"));
        Assert.Equal("member", member.Kind);
        Assert.Equal([76], member.Pitches);

        var chord = AuditionAt(Plain("<c' e' g'>‸4"));
        Assert.Equal("chord", chord.Kind);
        Assert.Equal([72, 76, 79], chord.Pitches);

        Assert.Equal(-1, AuditionAt(Plain("c'4 r‸4")).Key);
        Assert.Equal(-1, AuditionAt(Guitar("c'1@chord(Cm7‸)")).Key);   // no diagram, no sound
    }

    [Fact]
    public void TheKey_IsTheSameAnywhereOnOneNote_AndDiffersBetweenNotes()
    {
        int a = AuditionAt(Plain("‸cis'4 d'4")).Key;
        Assert.Equal(a, AuditionAt(Plain("cis'‸4 d'4")).Key);
        Assert.Equal(a, AuditionAt(Plain("cis'4‸ d'4")).Key);
        Assert.NotEqual(a, AuditionAt(Plain("cis'4 ‸d'4")).Key);
    }
}
