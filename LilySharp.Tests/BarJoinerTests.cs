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
using System.Text.RegularExpressions;
using LilySharp.Core.Editing;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Join Bars in Pairs (Orecchiante's request 21, owner's decisions 2026-11-21,
/// docs/join-bars-design.md): the bars of a chords row the selection touches are joined two by
/// two, each becoming half of the joined bar; the chords keep their place in time, halved, and
/// the file beside a chart is told which bars played were joined.
/// </summary>
public class BarJoinerTests
{
    /// <summary>Joins the bars the text between the two markers <c>«</c> and <c>»</c> touches
    /// (the markers removed first). A source with no score is given one that places its rows —
    /// a chords row sounds only where a score places it.</summary>
    private static BarJoiner.Result Run(string marked)
    {
        int a = marked.IndexOf('«'), b = marked.IndexOf('»') - 1;
        string source = marked.Replace("«", "").Replace("»", "");
        if (!source.Contains("score"))
        {
            var rows = Regex.Matches(source, @"\bchords(\s+\w+)?\s*\{").Select(m => "chords" + m.Groups[1].Value).Distinct();
            source += "\nscore { " + string.Join("  ", rows) + " }\n";
        }
        return BarJoiner.Join(source, a, b);
    }

    private static (string Text, BarJoiner.Result Result) Joined(string marked)
    {
        var r = Run(marked);
        Assert.True(r.NewText != null, r.Error);
        return (r.NewText!, r);
    }

    [Fact]
    public void PairsBecomeOneBar_HalfEach_OnTheBeatGrid()
    {
        var (text, r) = Joined("""
            time 4/4
            chords { «C | D | C . F . | G | E F# | G#m» | A | }
            """);
        Assert.Contains("chords { C D | C F G . | E F# G#m . | A | }", text);
        Assert.Equal([0, 2, 4], r.Played);
        Assert.StartsWith("Joined 6 bars into 3", r.Plan);
    }

    // Orecchiante's request 22: a chart writes a chord a bar, so `C | C |` says "these two bars
    // are C" — joined, the chord is held, not struck again on beat 3. The same where the second
    // bar opens on the chord the first ends on. Only alike as written (`@or(…)` too), only at
    // the pair's middle: a strike written inside a bar stays.
    [Fact]
    public void ThePairsSecondBar_OpeningOnTheChordTheFirstEndsOn_HoldsIt()
    {
        var (text, r) = Joined("""
            time 4/4
            chords { «C | C | A | A E/G# | F F | F | Am@or(A) | Am@or(A) | Am@or(A) | Am@or(E) | G | G7» | }
            """);
        Assert.Contains("chords { C | A . . E/G# | F F . . | Am@or(A) | Am@or(A) Am@or(E) | G G7 | }", text);
        Assert.Equal([0, 2, 4, 6, 8, 10], r.Played);
    }

    [Fact]
    public void AnOddBarOut_IsLeftAsItIs()
    {
        var (text, r) = Joined("chords { «C | D | E» | F | }");
        Assert.Contains("chords { C D | E | F | }", text);
        Assert.Contains("odd bar out", r.Plan);
        Assert.Equal([0], r.Played);
    }

    [Fact]
    public void ASectionPlayedTwice_JoinsInEveryPlaying()
    {
        var (text, r) = Joined("""
            section A {
              chords prog { G | «C | D» | }
            }
            section B {
              chords prog { E | F | }
            }
            form { A B A }
            """);
        Assert.Contains("chords prog { G | C D | }", text);
        // A: bars 0 1 2, B: 3 4, A again: 5 6 7 — the pair is played at 1 and 6.
        Assert.Equal([1, 6], r.Played);
    }

    [Fact]
    public void ThreeQuarter_KeepsEachHalfOnItsBeats()
    {
        var (text, _) = Joined("""
            time 3/4
            chords { «C | D G .» | }
            """);
        // C for 3/2 beats, then D for 1/2 and G for 1: six slots of an eighth.
        Assert.Contains("chords { C . . D G . | }", text);
    }

    [Fact]
    public void AMeterChangingAtAPairsFirstBar_OpensTheJoinedBar()
    {
        // The second pair in 3/4: each half one and a half beats, on eighths.
        var (text, r) = Joined("chords { «C | D | time 3/4 E | F» | }");
        Assert.Contains("chords { C D | time 3/4 E . . F . . | }", text);
        Assert.Equal([0, 2], r.Played);
    }

    [Fact]
    public void PercentIsOpened_AndTwoInAPairStayOne()
    {
        // (a `%` repeats the bar's chord, so the pair holds it: request 22)
        var (text, _) = Joined("chords { «C | % | % | %» | }");
        Assert.Contains("chords { C | % | }", text);
        var (text2, _) = Joined("chords { «C | D | % | %» | }");
        Assert.Contains("chords { C D | D | }", text2);
        // A pair of two bars C D (no `%`) again after one: that bar, not a `%`.
        var (text3, _) = Joined("chords { «C D | % | % | %» | }");
        Assert.Contains("chords { C D C D | % | }", text3);
    }

    [Fact]
    public void MarksOnAChord_TravelWithIt()
    {
        var (text, _) = Joined("chords { «F(133211)@or(Dm) | G7@todo(\"or G?\")» | }");
        Assert.Contains("chords { F(133211)@or(Dm) G7@todo(\"or G?\") | }", text);
    }

    [Fact]
    public void ASecondBarOpeningSilent_AfterAChord_IsRefused()
    {
        var r = Run("chords { «C | . D» | }");
        Assert.Null(r.NewText);
        Assert.Contains("opens silent", r.Error);
        var r2 = Run("chords { «C | |» E | }");
        Assert.Null(r2.NewText);
        Assert.Contains("is empty", r2.Error);
        // A first bar that is silent stays silent: its '.' is still the bar's head.
        var (text, _) = Joined("chords { «. | D» | }");
        Assert.Contains("chords { . D | }", text);
    }

    [Fact]
    public void ABreakInsideTheJoinedBar_IsDropped_AndOneAfterIsKept()
    {
        var (text, r) = Joined("chords { «C break | D | E | F break» | }");
        Assert.Contains("chords { C D | E F break | }", text);
        Assert.Contains("dropped", r.Plan);
    }

    [Fact]
    public void TheRowRunningOntoTheNextLine_KeepsItsLines()
    {
        var (text, _) = Joined("""
            section A {
              chords prog {
                «C | D | E |
                F | G | A» |
              }
            }
            """);
        Assert.Contains("    C D | E F |\n    G A |", text.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void WhatWouldStandInsideTheJoinedBar_IsRefused()
    {
        var r = Run("chords { «C | tempo 4 = 60 D» | }");
        Assert.Contains("would stand inside the joined bar", r.Error);
        var r2 = Run("chords { «C | time 2/4 D» | }");
        Assert.Contains("would stand inside the joined bar", r2.Error);
        var r3 = Run("chords { «C || D» | }");
        Assert.Contains("plain bar lines", r3.Error);
    }

    [Fact]
    public void RepeatSignsAndPartsBeside_AreRefused()
    {
        var r = Run("chords { «C |: D | E :| F» | }");
        Assert.Contains("repeat signs", r.Error);
        var r2 = Run("""
            part bass { instrument bass }
            section A {
              chords prog { «C | D» | }
              bass { c1 | d1 | }
            }
            """);
        Assert.Contains("beside the chords", r2.Error);
        var r3 = Run("""
            section A {
              partial 4
              chords prog { «C | D | E» | }
            }
            """);
        Assert.Contains("pickup", r3.Error);
    }

    [Fact]
    public void ACaretAlone_OrOneBar_IsNotARange()
    {
        Assert.Contains("Select", BarJoiner.Join("chords { C | D | }", 10, 10).Error);
        Assert.Contains("Select", Run("chords { «C» | D | }").Error);
    }

    [Fact]
    public void TheWholeChart_HalvesItsTempo()
    {
        var (text, r) = Joined("""
            tempo 4 = 158
            «chords { C | D | E | F | }»
            """);
        Assert.Contains("tempo 4 = 79", text);
        Assert.Contains("halved the tempo", r.Plan);
        // A part of it leaves the tempo alone.
        var (text2, _) = Joined("""
            tempo 4 = 158
            chords { «C | D» | E | F | }
            """);
        Assert.Contains("tempo 4 = 158", text2);
    }

    [Fact]
    public void ASectionsOwnTempo_IsHalved_WhenWhatFollowsSaysItsOwn()
    {
        const string src = """
            tempo 4 = 100
            section A {
              tempo 4 = 131.8
              chords prog { «C | D | E | F» | }
            }
            section B {
              tempo 4 = 100
              chords prog { G | A | }
            }
            form { A B }
            """;
        var (text, _) = Joined(src);
        Assert.Contains("tempo 4 = 65.9", text);
        Assert.Contains("tempo 4 = 100\nsection A", text.ReplaceLineEndings("\n"));
        // B no longer saying its own: A's tempo reaches into B, which is not joined.
        var (text2, _) = Joined(src.Replace("  tempo 4 = 100\n  chords prog { G", "  chords prog { G").Replace("  tempo 4 = 100\r\n  chords prog { G", "  chords prog { G"));
        Assert.Contains("tempo 4 = 131.8", text2);
    }

    /// <summary>The lightbulb offers it on a selection across two bars of a chords row (not on a
    /// caret, not on one bar), and lilysharp/joinBars answers with the bars played that were joined
    /// and how many the score played before — what the file beside a chart is lined up by.</summary>
    [Fact]
    public void TheLightbulb_AndTheRequest()
    {
        const string text = "chords { C | D | E | F | }\nscore { chords }\n";
        var server = new LilySharp.Lsp.LilySharpLanguageServer(System.IO.Stream.Null, System.IO.Stream.Null);
        var uri = new System.Uri("file:///join-bars.lys");
        server.DidOpen(new LilySharp.Lsp.Protocol.DidOpenTextDocumentParams
        {
            TextDocument = new LilySharp.Lsp.Protocol.TextDocumentItem { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        bool Offered(int from, int to)
        {
            var (l1, c1) = LilySharp.Lsp.LilySharpLanguageServer.GetLineAndCharacter(text, from);
            var (l2, c2) = LilySharp.Lsp.LilySharpLanguageServer.GetLineAndCharacter(text, to);
            var actions = server.GetCodeActions(new LilySharp.Lsp.Protocol.CodeActionParams
            {
                TextDocument = new LilySharp.Lsp.Protocol.TextDocumentIdentifier { Uri = uri },
                Range = new LilySharp.Lsp.Protocol.Range { Start = new LilySharp.Lsp.Protocol.Position(l1, c1), End = new LilySharp.Lsp.Protocol.Position(l2, c2) },
            }) ?? [];
            return actions.Any(a => a.Title == "Join Bars in Pairs…");
        }
        int c = text.IndexOf('C'), e = text.IndexOf('E');
        Assert.True(Offered(c, e));
        Assert.False(Offered(c, c));
        Assert.False(Offered(c, c + 1));
        var r = server.JoinBarsAsync(new LilySharp.Lsp.JoinBarsParams
        {
            TextDocument = new LilySharp.Lsp.Protocol.TextDocumentIdentifier { Uri = uri }, Start = c, End = text.IndexOf('F') + 1,
        }, System.Threading.CancellationToken.None).Result;
        Assert.True(r.Success, r.Error);
        Assert.StartsWith("chords { C D | E F | }", r.NewText);
        Assert.Equal([0, 2], r.Played);
        Assert.Equal(4, r.BarsPlayed);
    }
}
