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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A clef and a key signature dragged in the preview (owner's request 2026-11-03): what
/// Ctrl+Shift+Up can step may be dragged. The score's glyph says where it was written, and that
/// is the place the page must be told it can take hold of and the place a step must work from.
/// </summary>
[Trait("Category", "Unit")]
public class ClefKeyDragTests
{
    private const string Music = "section A { m { c'4 d e f | } }\nform { A }\nscore { staff m }\n";

    private static int[] Marks(string text) => [.. NoteStepper.SteppingMarks(text, SyntaxTree.Parse(text))];

    private static bool Drawn(string text, int at) =>
        SvgGenerator.Generate(SyntaxTree.Parse(text), SvgRenderOptions.Preview()).Contains($"data-pos=\"{at}\"", System.StringComparison.Ordinal);

    [Theory]
    [InlineData("part m { clef bass }\n" + Music, "bass")]
    [InlineData("clef alto\npart m { }\n" + Music, "alto")]
    [InlineData("part m { clef treble^8 }\n" + Music, "treble^8")]
    [InlineData("part m { clef treble key g major }\n" + Music, "g major")]
    [InlineData("key d major\npart m { clef treble }\n" + Music, "d major")]
    [InlineData("key! e minor\npart m { clef treble }\n" + Music, "e minor")]
    [InlineData("part m { clef treble }\nsection A { m { c'4 d e f | clef bass c d e f | } }\nform { A }\nscore { staff m }\n", "bass")]
    [InlineData("part m { clef treble }\nsection A { m { c'4 d e f | key d major c d e f | } }\nform { A }\nscore { staff m }\n", "d major")]
    public void WhatTheScoreDraws_CanBeTakenHoldOf(string text, string word)
    {
        int at = text.IndexOf(word, System.StringComparison.Ordinal);
        Assert.Contains(at, Marks(text));
        Assert.True(Drawn(text, at), "the glyph does not stand at " + at);
        Assert.Contains(NoteStepper.TargetAt(text, SyntaxTree.Parse(text), at)?.Kind, new NoteStepper.TargetKind?[] { NoteStepper.TargetKind.Clef, NoteStepper.TargetKind.Key });
    }

    [Theory]
    [InlineData("key custom c 1\npart m { clef treble }\n" + Music, "custom")]  // no tonic to walk from
    [InlineData("time 4/4\npart m { clef treble }\n" + Music, "4/4")]           // the owner's choice: not the time
    public void NotWhatHasNoStep(string text, string word)
        => Assert.DoesNotContain(text.IndexOf(word, System.StringComparison.Ordinal), Marks(text));

    [Fact]
    public void TheKeys_AreNamed_AsDrawnWhereTheyStand()
    {
        string text = "key g major\npart m { clef treble }\nsection A { m { c'4 d | key custom c 1 e f | key d major g a | } }\nform { A }\nscore { staff m }\n";
        var tree = SyntaxTree.Parse(text);
        Assert.Equal([text.IndexOf("g major", System.StringComparison.Ordinal), text.IndexOf("d major", System.StringComparison.Ordinal)], NoteStepper.KeySteppingMarks(tree).Order());
        Assert.All(NoteStepper.KeySteppingMarks(tree), at => Assert.Contains(at, NoteStepper.SteppingMarks(text, tree)));
    }

    private static string Dragged(string text, string word, int direction, int count, bool semitone)
    {
        var server = new LilySharpLanguageServer(System.IO.Stream.Null, System.IO.Stream.Null);
        var uri = new System.Uri("file:///clefkey.lys");
        server.DidOpen(new DidOpenTextDocumentParams { TextDocument = new TextDocumentItem { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 } });
        int at = text.IndexOf(word, System.StringComparison.Ordinal);
        var response = server.Step(new StepParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Selections = [new StepSelection { Start = at, End = at }],
            Direction = direction,
            Semitone = semitone,
            Count = count,
        });
        foreach (var e in response.Edits.OrderByDescending(e => e.Start))
            text = text[..e.Start] + e.NewText + text[e.End..];
        return text;
    }

    [Theory]
    [InlineData("g major", 1, 1, "d major")]
    [InlineData("g major", 1, 2, "a major")]
    [InlineData("g major", -1, 1, "c major")]
    [InlineData("g major", -1, 2, "f major")]
    public void AKey_WalksTheCircle_AllTheWayAsOneEdit(string key, int direction, int count, string expected)
    {
        string text = "key " + key + "\npart m { clef treble }\n" + Music;
        Assert.StartsWith("key " + expected + "\n", Dragged(text, key, direction, count, semitone: true));
    }

    [Fact]
    public void AKey_WithCtrl_DoesWhatPageUpDoes()
    {
        string text = "key g major\npart m { clef treble }\n" + Music;
        Assert.StartsWith("key g minor\n", Dragged(text, "g major", 1, 1, semitone: false));
        Assert.StartsWith("key e minor\n", Dragged(text, "g major", -1, 1, semitone: false));
    }

    [Fact]
    public void AClef_WalksItsList_AndStopsAtTheEnd()
    {
        string text = "part m { clef treble }\n" + Music;
        string once = Dragged(text, "treble", -1, 1, semitone: true);
        Assert.Contains("clef treble^8 }", once);
        Assert.False(SyntaxTree.Parse(once).HasErrors);
        string far = Dragged(text, "treble", -1, 500, semitone: true);
        Assert.False(SyntaxTree.Parse(far).HasErrors);
        Assert.Equal(far, Dragged(far, far[(far.IndexOf("clef ", System.StringComparison.Ordinal) + 5)..far.IndexOf(" }", System.StringComparison.Ordinal)], -1, 1, semitone: true));
    }

    [Fact]
    public void TheNotes_StayAsWritten()
    {
        string text = "key g major\npart m { clef treble }\n" + Music;
        Assert.Contains("{ m { c'4 d e f | } }", Dragged(text, "g major", 1, 3, semitone: true));
        Assert.Contains("{ m { c'4 d e f | } }", Dragged(text, "treble", -1, 2, semitone: true));
    }
}