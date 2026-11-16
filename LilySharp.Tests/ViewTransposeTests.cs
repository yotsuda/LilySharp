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
using System.IO;
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The preview's view-only transpose (owner's request 2026-11-16): the recording panel's
/// semitones move the score's picture and its playback, the file unedited. The key it lands in
/// is the one with fewer accidentals (sharps at the tritone), C major without a <c>key</c>; where
/// that key has a twin (B / C♭, F♯ / G♭, D♭ / C♯) the panel's ♯/♭ takes the other.
/// </summary>
[Trait("Category", "Unit")]
public class ViewTransposeTests
{
    private static SyntaxNode Root(string text) => SyntaxTree.Parse(text).GetRoot();

    [Theory]
    [InlineData("key c major\n", 1, ViewSpelling.Auto, "D♭", true)]
    [InlineData("key c major\n", 1, ViewSpelling.Sharp, "C♯", true)]
    [InlineData("key c major\n", 6, ViewSpelling.Auto, "F♯", true)]
    [InlineData("key c major\n", 6, ViewSpelling.Flat, "G♭", true)]
    [InlineData("key c major\n", -1, ViewSpelling.Auto, "B", true)]
    [InlineData("key c major\n", -1, ViewSpelling.Flat, "C♭", true)]
    [InlineData("key c major\n", 2, ViewSpelling.Auto, "D", false)]
    [InlineData("key c major\n", 2, ViewSpelling.Flat, "D", false)]     // no twin: the toggle does nothing
    [InlineData("key a minor\n", 1, ViewSpelling.Auto, "B♭m", true)]     // A♯m is its twin
    [InlineData("key a minor\n", -1, ViewSpelling.Auto, "G♯m", true)]
    [InlineData("key a minor\n", -1, ViewSpelling.Flat, "A♭m", true)]
    [InlineData("", 3, ViewSpelling.Auto, "E♭", false)]                 // no key: C major, as a staff draws it
    [InlineData("key d dorian\n", 2, ViewSpelling.Auto, "E dorian", false)]
    [InlineData("key c major\nshift 2\n", 1, ViewSpelling.Auto, "E♭", false)]   // after the file's own shift
    public void TheKeyItLandsIn(string head, int semitones, ViewSpelling spelling, string key, bool twin)
    {
        var root = Root(head + "part m { }\n");
        var view = new ViewTranspose(semitones, spelling);
        Assert.Equal(key, view.KeyName(root, null));
        Assert.Equal(twin, view.Keys(root, null).HasTwin);
        var (step, alt, oct) = view.Interval(root, null);
        Assert.Equal(semitones, PitchTransposer.IntervalSemitones(step, alt, oct));   // the sound moves exactly
    }

    private const string Chart = "key c major\nchords prog { section A { C | F G | } }\nform { ~A }\nscore { chords prog }\n";

    private static string[] Names(string text, ViewTranspose? view)
    {
        var tree = SyntaxTree.Parse(text);
        var (_, layout) = SvgGenerator.CollectAndLayout(tree, RenderSpecParser.FindFirst(tree), view: view);
        return [.. layout.ChordNameLayouts.OrderBy(c => c.X).Select(c => c.ChordText)];
    }

    [Fact]
    public void TheChordRowIsDrawnMoved_TheFileIsNot()
    {
        Assert.Equal(["C", "F", "G"], Names(Chart, null));
        Assert.Equal(["D", "G", "A"], Names(Chart, new ViewTranspose(2)));
        Assert.Equal(["G♭", "C♭", "D♭"], Names(Chart, new ViewTranspose(6, ViewSpelling.Flat)));   // IV of G♭ is C♭
        Assert.Equal(["F♯", "B", "C♯"], Names(Chart, new ViewTranspose(6)));
    }

    [Fact]
    public void ItComposesOutsideTheScoresOwnTranspose()
    {
        // `score transpose d` (+2) and a view of +1: the chart reads a minor third up.
        string text = Chart.Replace("score {", "score transpose d {");
        Assert.Equal(["D", "G", "A"], Names(text, null));
        Assert.Equal(["E♭", "A♭", "B♭"], Names(text, new ViewTranspose(1)));
    }

    private static LilySharpLanguageServer Open(string text, out Uri uri)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        uri = new Uri("file:///view-transpose.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        return server;
    }

    [Fact]
    public void ThePreviewSaysTheKey_AndANewViewIsANewPicture()
    {
        var server = Open(Chart, out var uri);
        var plain = server.GetSvg(new SvgParams { TextDocument = new() { Uri = uri } });
        Assert.Null(plain.ViewKey);
        var moved = server.GetSvg(new SvgParams { TextDocument = new() { Uri = uri }, ViewSemitones = 6, ViewSpelling = "flat" });
        Assert.Equal("G♭", moved.ViewKey);
        Assert.True(moved.ViewKeyHasTwin);
        Assert.Equal(-6, moved.ViewKeySharps);
        Assert.NotEqual(plain.Svg, moved.Svg);
        // …and back: the same picture as before (the session made with the view is dropped).
        var back = server.GetSvg(new SvgParams { TextDocument = new() { Uri = uri } });
        Assert.Equal(plain.Svg, back.Svg);
    }

    [Fact]
    public void ThePlaybackSoundsMoved_ButNotTheDrums()
    {
        var notes = new[]
        {
            new PlaybackNote { P = 60, C = 0 },
            new PlaybackNote { P = 38, C = 9 },
            new PlaybackNote { P = PlaybackNote.PedalPitch, C = 0 },
        };
        LilySharpLanguageServer.MovedBy(new PlaybackResponse { Notes = notes }, 3);
        Assert.Equal([63, 38, PlaybackNote.PedalPitch], notes.Select(n => n.P));
    }
}
