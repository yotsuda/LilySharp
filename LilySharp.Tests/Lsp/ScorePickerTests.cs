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
/// the resolution have to come from one rule. They did not: the language server built the
/// value from the RAW basename while the renderer dropped the extension, so a score named
/// <c>"Take 1.0"</c> was offered under a word that matched nothing — and choosing it fell
/// back to the FIRST score, silently. That is what "the preview will not switch scores;
/// it stays on main" looks like from the outside (2026-09-06, owner report). No book in
/// the corpus writes a dotted basename, which is why nothing had noticed.
/// <para>
/// Poisons: return the raw basename from <c>OutputNameOf</c> ⇒ the two picker tests go
/// red; drop the <c>SelectedRender</c> echo ⇒ the stale-selection test goes red; compare
/// raw basenames in <c>DuplicateScoreNameValidator</c> ⇒ the duplicate test goes red.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class ScorePickerTests
{
    /// <summary>Three scores that DRAW differently, one of them named with a dot in it —
    /// the case the picker could offer but not select.</summary>
    private const string ThreeScores = """
        octave absolute
        time 4/4
        key c major
        part melody { section A { c'4 d' e' f' } }
        part bass { section A { c4 d e f } }
        form main { A }
        score main { staff melody }
        score main "Take 1.0" { staff bass }
        score main "Take 2.0" { staff melody staff bass }
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
        var entries = (first.Renders ?? Array.Empty<RenderInfo>()).Where(r => r.Type == "score").ToArray();
        Assert.Equal(3, entries.Length);
        // The LABEL is the word the writer wrote; the VALUE is the output name.
        Assert.Equal(new[] { "main", "Take 1.0", "Take 2.0" }, entries.Select(r => r.Name));
        Assert.Equal(new[] { "", "Take 1", "Take 2" }, entries.Select(r => r.Filename));

        // Each entry draws a different picture, and the answer says which it drew.
        var drawn = new System.Collections.Generic.List<string>();
        foreach (var entry in entries)
        {
            var response = server.GetSvg(Ask(uri, entry.Filename.Length == 0 ? null : entry.Filename));
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
            // ...and asking for that value picks THAT score, not an earlier one.
            var (_, chosen) = RenderSpecParser.ScoreIndex(tree, offered[i]);
            Assert.Equal(i, chosen);   // i == 0 is the empty value: the first score
        }
    }

    [Fact]
    public void AStaleSelection_DrawsTheFirstScoreAndSaysSo()
    {
        // A selection left over from an edit that renamed the block: the renderer falls
        // back to the first score by design, and the response names what it drew so the
        // picker can follow instead of claiming a score that is not the picture.
        var uri = new Uri("file:///stale.lys");
        var server = Opened(uri, ThreeScores);

        var response = server.GetSvg(Ask(uri, "Take 9"));

        Assert.NotNull(response.Svg);
        Assert.Equal("", response.SelectedRender);
        Assert.Equal(server.GetSvg(Ask(uri, null)).Svg, response.Svg);
    }

    [Fact]
    public void TwoBasenamesSharingAnOutputName_AreADuplicate()
    {
        // "Take 1.0" and "Take 1.1" are two words but one output name, so they collide
        // on disk and in the picker — the check reads the same rule now, and sees it.
        var v = new DuplicateScoreNameValidator();
        v.Validate(SyntaxTree.Parse("""
            part melody { section A { c'4 d' e' f' } }
            form main { A }
            score main "Take 1.0" { staff melody }
            score main "Take 1.1" { staff melody }
            """));

        var d = Assert.Single(v.Diagnostics);
        Assert.Equal(DiagnosticCodes.DuplicateScoreName, d.Code);
    }
}
