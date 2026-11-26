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
using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// "Convert to Drum Steps" / "Convert to Drum Lines" (<see cref="DrumStepsConverter"/>,
/// Orecchiante's request 24 §4): a drum chart rewritten between its two spellings, the hits it
/// plays kept.
/// </summary>
[Trait("Category", "Unit")]
public class DrumStepsConverterTests
{
    private static string Book(string music, string head = "") => $$"""
        {{head}}
        part kit { clef percussion }
        section A { kit { {{music}} } }
        form { ~A }
        score { staff kit }
        """;

    private static (string Text, DrumStepsConverter.Rewrite? Rewrite) Convert(string text, string at = "s {")
    {
        var tree = SyntaxTree.Parse(text);
        int offset = text.IndexOf(at, StringComparison.Ordinal);
        var rewrite = DrumStepsConverter.At(tree, offset);
        return (rewrite == null ? text : text[..rewrite.Start] + rewrite.NewText + text[rewrite.End..], rewrite);
    }

    /// <summary>What a file strikes: the drum, the moment and the strength of every hit.</summary>
    private static string Hits(string text)
    {
        var tree = SyntaxTree.Parse(text);
        Assert.DoesNotContain(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        return string.Join(" ", new MidiExporter().Export(tree).Tracks.SelectMany(t => t.Notes)
            .OrderBy(n => n.StartTick).ThenBy(n => n.Pitch).Select(n => $"{n.Pitch}@{n.StartTick}v{n.Velocity}"));
    }

    [Fact]
    public void Lines_BecomeRows_OnTheCoarsestGridThatFits()
    {
        string text = Book("drums { { hh8 hh hh hh hh hh hho hh } { r4 sn r sn@accent } { bd4 r8 bd bd4 r } }");
        var (converted, rewrite) = Convert(text);
        Assert.Equal(DrumStepsConverter.ToStepsTitle, rewrite!.Title);
        Assert.Contains("""steps { hh "xx xx xx ox" sn ".. x. .. X." bd "x. .x x. .." }""", converted);
        Assert.Equal(Hits(text), Hits(converted));
    }

    [Fact]
    public void PhraseLines_AreWrittenOut_AndAMultilineChartStaysOneRowALine()
    {
        string text = """
            phrase hats { hh16 hh hh hh  hh hh hh hh  hh hh hh hh  hh hh hh hh | }
            phrase back { r4 sn@parenthesize r acciaccatura { sn16 } sn4 | }
            phrase kick { bd4 r8 bd bd4 r | }
            part kit { clef percussion }
            section A {
              kit {
                drums {
                  hats
                  back
                  kick
                }
              }
            }
            form { ~A }
            score { staff kit }
            """;
        var (converted, _) = Convert(text, "drums {");
        Assert.Contains("""
                steps {
                  hh "xxxx xxxx xxxx xxxx"
                  sn ".... g... .... f..."
                  bd "x... ..x. x... ...."
                }
            """.ReplaceLineEndings("\n"), converted.ReplaceLineEndings("\n"));
        Assert.Equal(Hits(text), Hits(converted));
    }

    [Fact]
    public void BeatsOfDifferentGrids_EachTakeTheCoarsestThatFits_TheSameInEveryRow()
    {
        string text = Book("drums { { hh8 hh tuplet 3/2 { hh8 hh hh } hh4 hh } { sn16 sn sn8 r4 r2 } }");
        var (converted, _) = Convert(text);
        Assert.Contains("""steps { hh "x.x. xxx x x" sn "xxx. ... . ." }""", converted);
        Assert.Equal(Hits(text), Hits(converted));
    }

    [Theory]
    [InlineData("time 6/8", "drums { { hh8 hh hh hh hh hh } { bd4. r8 bd r } }", """steps 4. { hh "xxx xxx" bd "x.. .x." }""")]
    [InlineData("time 7/8", "drums { { hh8 hh hh hh hh hh hh } }", """steps 8 { hh "x x x x x x x" }""")]
    [InlineData("time 2/2", "drums { { hh4 hh hh hh } }", """steps 2 { hh "xx xx" }""")]
    public void TheBeat_IsTheMeters(string time, string drums, string steps)
    {
        var (converted, _) = Convert(Book(drums, time));
        Assert.Contains(steps, converted);
        Assert.Equal(Hits(Book(drums, time)), Hits(converted));
    }

    [Fact]
    public void BarLines_CarryOver()
    {
        string text = Book("drums { { hh4 hh hh hh | hh8 hh hh hh hh2 | } { bd2 bd | bd1 | } }");
        var (converted, _) = Convert(text);
        Assert.Contains("""steps { hh "x. x. x. x. | xx xx x. .." bd "x. .. x. .. | x. .. .. .." }""", converted);
        Assert.Equal(Hits(text), Hits(converted));
    }

    [Theory]
    [InlineData("drums { down { bd4 bd bd bd } }")]            // a side steps cannot say
    [InlineData("drums { { sn4@f sn sn sn } }")]               // a dynamic
    [InlineData("drums { { sn4:16 sn sn sn } }")]              // a tremolo
    [InlineData("drums { { tuplet 5/4 { sn16 sn sn sn sn } sn4 sn sn } }")]   // off every grid
    [InlineData("drums { { sn4 sn sn sn | sn1 | } { bd2 | bd2 bd1 | } }")]           // bar lines that disagree
    public void AChartStepsCannotWrite_IsNotOffered(string chart)
        => Assert.Null(Convert(Book(chart), "drums {").Rewrite);

    [Theory]
    [InlineData("""steps { hh "x.x. x.o. xxx x..." sn ".... X... .g.. f..." bd "x... ..x. x.x. ...." }""")]
    [InlineData("""steps { sn "xxxxxxxx x... x... x... | .... .... .... ...." }""")]
    public void Steps_BecomeTheLinesTheyStandFor(string steps)
    {
        string text = Book(steps);
        var (converted, rewrite) = Convert(text);
        Assert.Equal(DrumStepsConverter.ToLinesTitle, rewrite!.Title);
        Assert.StartsWith("drums {", converted[rewrite.Start..]);
        var tree = SyntaxTree.Parse(converted);
        Assert.DoesNotContain(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(new LilyPondExporter().Export(SyntaxTree.Parse(text)), new LilyPondExporter().Export(tree));
        Assert.Equal(Hits(text), Hits(converted));
    }

    [Fact]
    public void TheLightbulb_RewritesTheChartInPlace()
    {
        string text = Book("drums { { hh8 hh hh hh hh hh hh hh } { bd4 r bd r } }");
        var server = new LilySharp.Lsp.LilySharpLanguageServer(System.IO.Stream.Null, System.IO.Stream.Null);
        var uri = new System.Uri("file:///drum-steps.lys");
        server.DidOpen(new LilySharp.Lsp.Protocol.DidOpenTextDocumentParams
        {
            TextDocument = new LilySharp.Lsp.Protocol.TextDocumentItem { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        LilySharp.Lsp.Protocol.CodeAction? At(int offset)
        {
            var (l, c) = LilySharp.Lsp.LilySharpLanguageServer.GetLineAndCharacter(text, offset);
            return (server.GetCodeActions(new LilySharp.Lsp.Protocol.CodeActionParams
            {
                TextDocument = new LilySharp.Lsp.Protocol.TextDocumentIdentifier { Uri = uri },
                Range = new LilySharp.Lsp.Protocol.Range { Start = new LilySharp.Lsp.Protocol.Position(l, c), End = new LilySharp.Lsp.Protocol.Position(l, c) },
            }) ?? []).FirstOrDefault(a => a.Title == DrumStepsConverter.ToStepsTitle);
        }
        var action = At(text.IndexOf("bd4", StringComparison.Ordinal));
        Assert.NotNull(action);
        var edit = Assert.Single(Assert.Single(action!.Edit!.Changes!).Value);
        Assert.Equal("""steps { hh "xx xx xx xx" bd "x. .. x. .." }""", edit.NewText);
        Assert.Null(At(text.IndexOf("part", StringComparison.Ordinal)));
    }

    [Fact]
    public void ThereAndBack_IsTheSameChart()
    {
        string steps = Book("""steps { hh "x.x. x.x. x.x. x.o." sn ".... x... ..g. x..f" bd "x... ..x. x.x. ...." }""");
        var (lines, _) = Convert(steps);
        var (back, _) = Convert(lines, "drums {");
        Assert.Equal(steps, back);
    }
}
