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
/// A clef dragged in the preview (owner's request 2026-11-03): what Ctrl+Shift+Up can step may be
/// dragged. The score's glyph says where it was written, and that is the place the page must be told
/// it can take hold of and the place a step must work from. Not a key signature: tried and taken out
/// by the owner, the staves moving under the hand (ROADMAP), nor a time signature.
/// </summary>
[Trait("Category", "Unit")]
public class ClefDragTests
{
    private const string Music = "section A { m { c'4 d e f | } }\nform { A }\nscore { staff m }\n";

    private static int[] Marks(string text) => [.. NoteStepper.SteppingMarks(text, SyntaxTree.Parse(text))];

    private static bool Drawn(string text, int at) =>
        SvgGenerator.Generate(SyntaxTree.Parse(text), SvgRenderOptions.Preview()).Contains($"data-pos=\"{at}\"", System.StringComparison.Ordinal);

    [Theory]
    [InlineData("part m { clef bass }\n" + Music, "bass")]
    [InlineData("clef alto\npart m { }\n" + Music, "alto")]
    [InlineData("part m { clef treble^8 }\n" + Music, "treble^8")]
    [InlineData("part m { clef treble }\nsection A { m { c'4 d e f | clef bass c d e f | } }\nform { A }\nscore { staff m }\n", "bass")]
    public void WhatTheScoreDraws_CanBeTakenHoldOf(string text, string word)
    {
        int at = text.IndexOf(word, System.StringComparison.Ordinal);
        Assert.Contains(at, Marks(text));
        Assert.True(Drawn(text, at), "the glyph does not stand at " + at);
        Assert.Equal(NoteStepper.TargetKind.Clef, NoteStepper.TargetAt(text, SyntaxTree.Parse(text), at)?.Kind);
    }

    [Theory]
    [InlineData("key d major\npart m { clef treble }\n" + Music, "d major")]
    [InlineData("part m { clef treble key g major }\n" + Music, "g major")]
    [InlineData("part m { clef treble }\nsection A { m { c'4 d e f | key d major c d e f | } }\nform { A }\nscore { staff m }\n", "d major")]
    [InlineData("time 4/4\npart m { clef treble }\n" + Music, "4/4")]
    public void NotAKey_NorATime_TheKeysAloneStepThem(string text, string word)
        => Assert.DoesNotContain(text.IndexOf(word, System.StringComparison.Ordinal), Marks(text));

    private static string Dragged(string text, string word, int direction, int count, bool semitone)
    {
        var server = new LilySharpLanguageServer(System.IO.Stream.Null, System.IO.Stream.Null);
        var uri = new System.Uri("file:///clef.lys");
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
    public void AClef_WithCtrl_GoesToTheNextGroup()
        => Assert.Contains("clef alto }", Dragged("part m { clef treble }\n" + Music, "treble", -1, 1, semitone: false));

    [Fact]
    public void TheNotes_StayAsWritten()
        => Assert.Contains("{ m { c'4 d e f | } }", Dragged("part m { clef treble }\n" + Music, "treble", -1, 2, semitone: true));
}
