// This file is part of Lily#.
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Lily# is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// Lily# is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.IO;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using LilySharp.Tests.LpFidelity;
using Xunit;
using Diagnostic = LilySharp.Core.Syntax.Diagnostic;
using DiagnosticSeverity = LilySharp.Core.Syntax.DiagnosticSeverity;

namespace LilySharp.Tests;

/// <summary>
/// The spellings the four scores of grammar evaluation 4 asked for and the owner approved
/// (2026-11-15; the proposals are the Lab's <c>sessions/p1227/proposals-eval4.md</c>). Each is
/// held from the text to the page and the files.
/// </summary>
public class GrammarEvaluation4FeaturesTests
{
    private static List<Diagnostic> Said(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return [.. tree.Diagnostics, .. SemanticValidation.Run(tree)];
    }

    private static void Clean(string source) => Assert.Empty(Said(source).Select(d => d.Message));

    private static string Book(string music, string more = "")
        => $"octave absolute\ntime 4/4\npart m {{ }}\n{more}section A {{ m {{ {music} }} }}\nform {{ A }}\nscore {{ staff m }}\n";

    /// <summary>Every glyph of the page, where it is drawn.</summary>
    private static List<(char Glyph, double X, double Y)> Glyphs(string source)
        => [.. RenderedGeometry.Render(source).Glyphs.Select(g => (g.Glyph, Math.Round(g.X, 6), Math.Round(g.Y, 6)))];

    private static string Ly(string source, out IReadOnlyList<string> warnings)
    {
        var exporter = new LilySharp.Core.LilyPond.LilyPondExporter();
        string ly = exporter.Export(SyntaxTree.Parse(source));
        warnings = [.. exporter.Warnings];
        return ly;
    }

    private static string Xml(string source)
        => new LilySharp.Core.MusicXml.MusicXmlExporter().Export(SyntaxTree.Parse(source)).ToXml().ToString();

    private static List<(int Key, int Velocity, int Start, int Ticks)> Notes(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindFirst(tree);
        var midi = new LilySharp.Core.Midi.MidiExporter { Form = spec?.Form, Score = spec }.Export(tree);
        return [.. midi.Tracks.SelectMany(t => t.Notes).Select(n => (n.Pitch, n.Velocity, n.StartTick, n.DurationTicks))];
    }

    // ───────────────────────── 6. c'8@notehead(cross) d' e' f'@!notehead ─────────────────────────

    /// <summary>A run of a head style is, on the page and in the files, the style written on each
    /// of its notes — chords, a <c>q</c>, a tuplet's notes and a bar line inside it included, a
    /// rest and a grace note left as they are, a run of marks beside it its own.</summary>
    [Theory]
    [InlineData("c'8@notehead(cross) d' e' f'@!notehead g'4 a' |",
        "c'8@notehead(cross) d'@notehead(cross) e'@notehead(cross) f'@notehead(cross) g'4 a' |", "c'8 d' e' f' g'4 a' |")]
    [InlineData("c'4@notehead(diamond) <e' g'> q b'@!notehead |",
        "c'4@notehead(diamond) <e' g'>@notehead(diamond) q@notehead(diamond) b'@notehead(diamond) |", "c'4 <e' g'> q b' |")]
    [InlineData("c'4@notehead(x) r d'2 | e'4@!notehead f' g'2 |",
        "c'4@notehead(x) r d'2@notehead(x) | e'4@notehead(x) f' g'2 |", "c'4 r d'2 | e'4 f' g'2 |")]
    [InlineData("tuplet 3/2 { c'8@notehead(slash) d' e' } f'4 g'2@!notehead |",
        "tuplet 3/2 { c'8@notehead(slash) d'@notehead(slash) e'@notehead(slash) } f'4@notehead(slash) g'2@notehead(slash) |",
        "tuplet 3/2 { c'8 d' e' } f'4 g'2 |")]
    [InlineData("c'4@notehead(cross) grace { d'16 } e'4 f'@!notehead g' |",
        "c'4@notehead(cross) grace { d'16 } e'4@notehead(cross) f'@notehead(cross) g' |", "c'4 grace { d'16 } e'4 f' g' |")]
    [InlineData("c'4@notehead(xcircle)@staccato d' e'@!staccato f'@!notehead |",
        "c'4@notehead(xcircle)@staccato d'@notehead(xcircle)@staccato e'@notehead(xcircle)@staccato f'@notehead(xcircle) |",
        "c'4@staccato d'@staccato e'@staccato f' |")]
    // The notes of a broken chord are each a note of the run.
    [InlineData("<< c'@notehead(cross) e' g' c''@!notehead >>1 |",
        "<< c'@notehead(cross) e'@notehead(cross) g'@notehead(cross) c''@notehead(cross) >>1 |", "<< c' e' g' c'' >>1 |")]
    // The nearest opening is the one an end closes, and a note in two runs takes the inner one's.
    [InlineData("c'4@notehead(cross) d' e'@notehead(triangle) f'@!notehead | g'1@!notehead |",
        "c'4@notehead(cross) d'@notehead(cross) e'@notehead(triangle) f'@notehead(triangle) | g'1@notehead(cross) |",
        "c'4 d' e' f' | g'1 |")]
    public void ARunOfAHeadStyle_IsTheStyleOnEachOfItsNotes(string run, string spelledOut, string plain)
    {
        Clean(Book(run));
        Assert.Equal(Book(run), SyntaxTree.Parse(Book(run)).GetRoot().ToFullString());

        Assert.Equal(Glyphs(Book(spelledOut)), Glyphs(Book(run)));
        Assert.NotEqual(Glyphs(Book(plain)), Glyphs(Book(run)));

        // The files write the style on each note: the same as the one spelled out. And nothing
        // is reported lost — the end is not "dropped".
        Assert.Equal(Ly(Book(spelledOut), out _), Ly(Book(run), out var warnings));
        Assert.Empty(warnings);
        Assert.Equal(Xml(Book(spelledOut)), Xml(Book(run)));

        // A head style is the page's: the run sounds as the notes with no style do.
        Assert.Equal(Notes(Book(plain)), Notes(Book(run)));
    }

    /// <summary>Without its end a head style is one note's, as it always was.</summary>
    [Fact]
    public void AHeadStyleWithNoEnd_IsOneNotes()
    {
        string one = Book("c'4@notehead(cross) d' e' f' |");
        Assert.Empty(ArticulationRuns.Of(SyntaxTree.Parse(one).GetRoot()).Runs);
        Assert.Single(Xml(one).Split("<notehead>").Skip(1));
        Assert.Equal(4, Xml(Book("c'4@notehead(cross) d' e' f'@!notehead |")).Split("<notehead>").Length - 1);
    }

    /// <summary>The end is written bare, and one with no opening before it in its own music is an
    /// error that says what to write — a mark's run does not open a head's.</summary>
    [Theory]
    [InlineData("c'4 d' e' f'@!notehead |")]
    [InlineData("c'4@staccato d' e' f'@!notehead |")]
    [InlineData("c'4@notehead(cross) d' voice { e'4 f'@!notehead } { c'2 } |")]
    public void AHeadStylesEndWithNoOpening_IsAnError(string music)
    {
        string source = Book(music);
        var said = Assert.Single(Said(source));
        Assert.Equal(DiagnosticCodes.UnpairedSpan, said.Code);
        Assert.Equal(DiagnosticSeverity.Error, said.Severity);
        Assert.Contains("c8@notehead(cross) d e f@!notehead", said.Message);
        Assert.Contains("no '@notehead(style)' is written before it", said.Message);
        Assert.Equal("@!notehead", source.Substring(said.Span.Start, said.Span.Length));
    }

    /// <summary>A phrase called inside the run is not reached, and that is said.</summary>
    [Fact]
    public void AHeadStylesRunAndAPhrase()
    {
        string across = Book("c'2@notehead(cross) riff d'2@!notehead r2 |", "phrase riff { e'2 | }\n");
        var said = Assert.Single(Said(across));
        Assert.Equal(DiagnosticSeverity.Warning, said.Severity);
        Assert.Contains("the '@notehead' run that '@!notehead' closes does not reach into 'riff'", said.Message);
    }

    /// <summary>The end typed after its opening is already on the page changes the notes before
    /// the keystroke: the preview is the page a fresh compile draws, both ways.</summary>
    [Fact]
    public void TypingAHeadStylesEnd_RedrawsTheNotesBeforeIt()
    {
        string bars = string.Concat(Enumerable.Repeat("c'4 d' e' f' | ", 6));
        string before = Book("c'4@notehead(cross) d' e' f' | g'4 a' b' c'' | " + bars);
        string after = before.Replace("b' c'' |", "b'@!notehead c'' |");
        var options = new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false };
        var compiler = new LilySharp.Core.Svg.IncrementalCompiler(SyntaxTree.Parse(before), options);
        compiler.Render();
        foreach (string text in new[] { after, before, after })
            Assert.Equal(LilySharp.Core.Svg.SvgGenerator.Generate(SyntaxTree.Parse(text), options),
                compiler.RenderIncremental(SyntaxTree.Parse(text)));
    }

    /// <summary>After <c>@!</c> the editor offers the head style's end with the marks'.</summary>
    [Fact]
    public void AfterTheEndMark_TheHeadStyleIsOffered()
    {
        const string marked = "part m { }\nsection A { m { c'8@notehead(cross) d' e' f'@!‸ } }";
        int offset = marked.IndexOf('‸');
        string text = marked.Remove(offset, 1);
        var uri = new Uri("file:///features4.lys");
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        var offered = (server.Completion(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(line, character),
        })?.Items ?? []).Select(i => i.Label!).ToList();
        Assert.Contains("notehead", offered);
        Assert.Contains("staccato", offered);
    }
}
