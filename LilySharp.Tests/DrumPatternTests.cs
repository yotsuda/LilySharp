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
using LilySharp.Core.Music;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The stock drum patterns the editor offers (owner's request 2026-10-11): each is one clean
/// bar, and any hi-hat or ride figure, backbeat and kick written as lines of one chart merge
/// into the two voices a drummer reads.
/// </summary>
[Trait("Category", "Unit")]
public class DrumPatternTests
{
    private static string Book(string kit) =>
        string.Join("\n", DrumPatterns.All.Select(p => p.Declaration)) + $$"""

        part kit { clef percussion }
        section A { kit { {{kit}} } }
        form { ~A }
        score { staff kit }
        """;

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var p in DrumPatterns.All)
            data.Add(p.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void EveryPattern_IsOneCleanBar_OfWhatItSaysItIs(string name)
    {
        var tree = SyntaxTree.Parse(Book(name));
        Assert.Empty(SemanticValidation.Run(tree).Where(d => d.Severity != DiagnosticSeverity.Info));
        var summary = Assert.IsType<PhraseSummary>(PhraseSummary.Of(tree, name));
        Assert.Equal("1 bar (4/4)", summary.LengthText);
        Assert.Contains(DrumPatterns.Named(name)!.Family, summary.Families);
        // A pattern's name must stay a name: a drum's would read as the drum (LYS1030).
        Assert.False(DrumNameRegistry.Contains(name));
    }

    public static TheoryData<string, string, string> Grooves()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var hands in DrumPatterns.All.Where(p => p.Family is "hi-hat" or "ride"))
            foreach (var snare in new[] { "back", "halfTime", "flam" })
                foreach (var kick in DrumPatterns.All.Where(p => p.Family == "bass drum"))
                    data.Add(hands.Name, snare, kick.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(Grooves))]
    public void AnyHatsSnareAndKick_MergeIntoTwoVoices(string hands, string snare, string kick)
    {
        var tree = SyntaxTree.Parse(Book($"drums {{ {hands} {snare} {kick} }}"));
        Assert.DoesNotContain(SemanticValidation.Run(tree), d => d.Code == DiagnosticCodes.DrumsUnplayable);
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.Choose(RenderSpecParser.FindAll(tree), null));
        // Merged: two voices. Drawn as written, it would be three.
        Assert.Equal(2, score.EnumerateStaves().Single().Staff.Voices.Length);
    }

    [Theory]
    [InlineData("fillToms")]
    [InlineData("fillTriplet")]
    public void AFill_MergesOverAKick(string fill)
    {
        var tree = SyntaxTree.Parse(Book($"drums {{ {fill} fourFloor }}"));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.Choose(RenderSpecParser.FindAll(tree), null));
        Assert.Equal(2, score.EnumerateStaves().Single().Staff.Voices.Length);
    }
    // ── the editor: the stock patterns in a drum part's popup ──

    [Theory]
    [InlineData("kit { hh4  }", "kit { hh4 ")]                // the part's music
    [InlineData("kit { drums { beat  } }", "kit { drums { beat ")]   // a chart's lines
    public void ADrumPopup_OffersTheStockPatterns_AndAcceptingOneDeclaresIt(string music, string before)
    {
        string text = $$"""
            phrase beat { hh8 8 8 8 8 8 8 8 | }
            part kit { clef percussion }
            section A { {{music}} }
            form { ~A }
            score { staff kit }
            """.Replace("\n", "\r\n");
        int offset = text.IndexOf(before, System.StringComparison.Ordinal) + before.Length;
        var server = new LilySharp.Lsp.LilySharpLanguageServer(System.IO.Stream.Null, System.IO.Stream.Null);
        var uri = new System.Uri("file:///stock.lys");
        server.DidOpen(new LilySharp.Lsp.Protocol.DidOpenTextDocumentParams
        {
            TextDocument = new LilySharp.Lsp.Protocol.TextDocumentItem { Uri = uri, Text = text, Version = 1, LanguageId = "lilysharp" },
        });
        var (line, character) = LilySharp.Lsp.LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        var items = server.Completion(new LilySharp.Lsp.Protocol.CompletionParams
        {
            TextDocument = new LilySharp.Lsp.Protocol.TextDocumentIdentifier { Uri = uri },
            Position = new LilySharp.Lsp.Protocol.Position(line, character),
        })!.Items.OrderBy(i => i.SortText ?? i.Label, System.StringComparer.Ordinal).ToList();

        // The file's own phrase first, then every stock pattern, hi-hats before kicks.
        Assert.Equal("beat", items[0].Label);
        Assert.Equal(DrumPatterns.All.OrderBy(p => System.Array.IndexOf(PhraseSummary.FamilyOrder, p.Family)).ThenBy(p => p.Name)
            .Select(p => p.Name), items.Skip(1).Take(DrumPatterns.All.Count).Select(i => i.Label));

        // Accepting `kick8` writes the name here and the declaration above the part.
        var kick = items.Single(i => i.Label == "kick8");
        var edit = Assert.Single(kick.AdditionalTextEdits!);
        int insertAt = text.IndexOf("part kit", System.StringComparison.Ordinal);
        Assert.Equal(LilySharp.Lsp.LilySharpLanguageServer.GetLineAndCharacter(text, insertAt),
            (edit.Range.Start.Line, edit.Range.Start.Character));
        string accepted = text.Insert(offset, kick.InsertText!).Insert(insertAt, edit.NewText);
        Assert.Contains("phrase kick8 { bd4 r8 bd bd4 r | }\r\npart kit", accepted);
        Assert.DoesNotContain(SemanticValidation.Run(SyntaxTree.Parse(accepted)), d => d.Severity == DiagnosticSeverity.Error);
    }
}