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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// The preview's score picker: every entry it offers must draw the score it names.
/// </summary>
/// <remarks>
/// The picker's VALUE is a score's OUTPUT NAME and the renderer resolves that same word
/// (<see cref="RenderSpecParser.MatchesName(string, string, string)"/>), so the list and
/// the resolution have to come from one rule. They did not once: the language server built
/// the value one way and the renderer resolved another, so an entry could be offered under a
/// word that matched nothing — and choosing it fell back to the FIRST score, silently
/// (2026-09-06, owner report: "the preview will not switch scores").
/// <para>
/// Since 2026-10-09 a score has one name (docs/anonymous-blocks-design.md §7): the label is
/// that name, or <c>(Default)</c> for the unnamed score, whose value is empty — and the empty
/// value picks the unnamed score wherever the file declares it, not the first score.
/// </para>
/// <para>
/// Poisons: make <c>ChooseIndex</c> fall back to the first score ⇒ the picker test and the
/// stale-selection test go red; drop the <c>SelectedRender</c> echo ⇒ the stale-selection
/// test goes red; label the unnamed score with its empty name ⇒ the picker test goes red.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class ScorePickerTests
{
    /// <summary>Three scores that DRAW differently, the unnamed one SECOND — the order in
    /// which "the empty value is the first score" and "the empty value is the default" part.</summary>
    private const string ThreeScores = """
        octave absolute
        time 4/4
        key c major
        part melody { section A { c'4 d' e' f' } }
        part bass { section A { c4 d e f } }
        form { A }
        score take1 { staff bass }
        score { staff melody }
        score take2 { staff melody staff bass }
        """;

    private static LilySharpLanguageServer Opened(Uri uri, string text)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri, Text = text, Version = 1, LanguageId = "lilysharp",
            },
        });
        return server;
    }

    private static SvgParams Ask(Uri uri, string? renderName) => new()
    {
        TextDocument = new TextDocumentIdentifier { Uri = uri },
        RenderName = renderName,
    };

    [Fact]
    public void EveryPickerEntry_DrawsItsOwnScore()
    {
        var uri = new Uri("file:///picker.lys");
        var server = Opened(uri, ThreeScores);

        var first = server.GetSvg(Ask(uri, null));
        Assert.Null(first.Error);
        // No selection draws the DEFAULT score, though it is declared second.
        Assert.Equal("", first.SelectedRender);
        var entries = (first.Renders ?? Array.Empty<RenderInfo>()).Where(r => r.Type == "score").ToArray();
        Assert.Equal(3, entries.Length);
        // The LABEL is the score's name, (Default) for the unnamed one; the VALUE is the output name.
        Assert.Equal(new[] { "take1", "(Default)", "take2" }, entries.Select(r => r.Name));
        Assert.Equal(new[] { "take1", "", "take2" }, entries.Select(r => r.Filename));

        // Each entry draws a different picture, and the answer says which it drew.
        var drawn = new System.Collections.Generic.List<string>();
        foreach (var entry in entries)
        {
            var response = server.GetSvg(Ask(uri, entry.Filename));
            Assert.Null(response.Error);
            Assert.NotNull(response.Svg);
            Assert.Equal(entry.Filename, response.SelectedRender);
            drawn.Add(response.Svg!);
        }
        Assert.Equal(3, drawn.Distinct().Count());
    }

    [Fact]
    public void PickerValues_AreTheOutputNamesTheRendererResolves()
    {
        // ACROSS THE LAYERS on purpose: the values come from the SERVER's picker list and
        // the specs from the renderer's own parse. The defect lived exactly in that seam —
        // a Core-against-Core comparison would have stayed green through it.
        var uri = new Uri("file:///values.lys");
        var server = Opened(uri, ThreeScores);
        var offered = (server.GetSvg(Ask(uri, null)).Renders ?? Array.Empty<RenderInfo>())
            .Where(r => r.Type == "score").Select(r => r.Filename).ToArray();

        var tree = SyntaxTree.Parse(ThreeScores);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));
        var specs = RenderSpecParser.FindAll(tree);

        // One picker entry per parsed spec, in document order, carrying its output name:
        // the list the client shows and the specs the renderer chooses from are the same
        // sequence, so an entry's ordinal and its value name the same score.
        Assert.Equal(specs.Count, offered.Length);
        for (int i = 0; i < specs.Count; i++)
        {
            Assert.Equal(specs[i].OutputFile, offered[i]);
            // ...and asking for that value picks THAT score, not an earlier one — the empty
            // value included, which is the unnamed score's (index 1).
            var (_, chosen) = RenderSpecParser.ScoreIndex(tree, offered[i]);
            Assert.Equal(i, chosen);
            Assert.Same(specs[i], RenderSpecParser.Choose(specs, offered[i]));
        }
    }

    [Fact]
    public void AStaleSelection_DrawsTheDefaultScoreAndSaysSo()
    {
        // A selection left over from an edit that renamed the block: the renderer falls
        // back to the default score by design, and the response names what it drew so the
        // picker can follow instead of claiming a score that is not the picture.
        var uri = new Uri("file:///stale.lys");
        var server = Opened(uri, ThreeScores);

        var response = server.GetSvg(Ask(uri, "take9"));

        Assert.NotNull(response.Svg);
        Assert.Equal("", response.SelectedRender);
        Assert.Equal(server.GetSvg(Ask(uri, null)).Svg, response.Svg);
    }

    [Fact]
    public void AFileWithNoUnnamedScore_StartsOnItsFirst()
    {
        var tree = SyntaxTree.Parse("""
            part melody { section A { c'4 d' e' f' } }
            form { A }
            score both { staff melody }
            score tab { tab melody }
            """);

        var (scores, chosen) = RenderSpecParser.ScoreIndex(tree, null);

        Assert.Equal(new[] { "both", "tab" }, scores.Select(s => s.Label));
        Assert.Equal(0, chosen);
    }

    [Fact]
    public void TwoUnnamedScores_AreADuplicate()
    {
        // Both would be the (Default) entry and both would write <input>.svg.
        var v = new DuplicateScoreNameValidator();
        v.Validate(SyntaxTree.Parse("""
            part melody { section A { c'4 d' e' f' } }
            form { A }
            score { staff melody }
            score { staff melody }
            """));

        var d = Assert.Single(v.Diagnostics);
        Assert.Equal(DiagnosticCodes.DuplicateScoreName, d.Code);
    }

    [Theory]
    [InlineData("score \"tab\" { tab melody }", "write 'score tab'")]
    [InlineData("score \"guitar-chart\" { tab melody }", "write 'score guitarChart'")]
    [InlineData("score tab \"both\" { tab melody }", "delete the quoted \"both\"")]
    public void AQuotedName_IsOneErrorThatNamesTheBareWord(string score, string fix)
    {
        var tree = SyntaxTree.Parse("part melody { section A { c'4 d' e' f' } }\nform { A }\n" + score + "\n");

        var d = Assert.Single(tree.Diagnostics);
        Assert.Equal(DiagnosticCodes.ScoreNameQuoted, d.Code);
        Assert.Contains(fix, d.Message);
    }

    [Fact]
    public void AQuotedName_StillPicksItsScore()
    {
        // The parser's recovery: the string's text reads as the name, so the preview keeps
        // drawing the score the stale book meant while the error says how to write it.
        var tree = SyntaxTree.Parse("""
            part melody { section A { c'4 d' e' f' } }
            form { A }
            score { staff melody }
            score "tab" { tab melody }
            """);

        var (scores, chosen) = RenderSpecParser.ScoreIndex(tree, "tab");

        Assert.Equal(new[] { "(Default)", "tab" }, scores.Select(s => s.Label));
        Assert.Equal(1, chosen);
    }
}
