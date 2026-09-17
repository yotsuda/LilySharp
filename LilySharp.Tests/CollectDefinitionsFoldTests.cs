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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The collector's definitions walk reads three things it used to walk the tree again for
/// (session 400): the file's <c>transpose</c> default and <c>pitch</c> convention — asked per
/// PART before, two whole-tree walks each time — and the section / phrase / variable
/// declarations the page's canonical bar count is computed over (three more walks). The
/// whole-tree readers stay the spelling of record; the walk must answer exactly as they do.
/// </summary>
public class CollectDefinitionsFoldTests
{
    private static MeasureCollector Collect(string source, out SyntaxNode root)
    {
        var tree = SyntaxTree.Parse(source);
        root = tree.GetRoot();
        var collector = new MeasureCollector();
        SvgGenerator.CollectScore(collector, tree, RenderSpecParser.FindFirst(tree));
        return collector;
    }

    [Fact]
    public void TheWalk_ReadsATopLevelTransposeAndPitch_AsTheFilesDefaults()
    {
        var c = Collect("""
            transpose d
            pitch concert
            part sax { instrument alto-sax  section A { c4 d e f | } }
            form main { A }
            score main { staff sax }
            """, out var root);
        var (transpose, concert) = c.FileDefaultsForTest;
        Assert.Equal(PartTranspose.ReadScoreDefault(root), transpose);
        Assert.Equal((1, 0, 0), transpose);
        Assert.True(concert);
        Assert.True(ConcertPitch.FileIsConcert(root));
    }

    [Fact]
    public void APartHeadersAndAScoreBlocksProperties_AreNotTheFiles()
    {
        // The walk meets both sites (the score's `transpose d`, the part's `pitch concert`)
        // and passes them over, as the whole-tree readers do.
        var c = Collect("""
            part sax { instrument alto-sax  pitch concert  section A { c4 d e f | } }
            form main { A }
            score main transpose d { staff sax }
            """, out var root);
        var (transpose, concert) = c.FileDefaultsForTest;
        Assert.Null(transpose);
        Assert.False(concert);
        Assert.Null(PartTranspose.ReadScoreDefault(root));
        Assert.False(ConcertPitch.FileIsConcert(root));
    }

    [Fact]
    public void TheWalk_AnswersAsTheWholeTreeReaders_OnEveryNetBook()
    {
        int books = 0, transposing = 0, sections = 0;
        foreach (var path in SectionVoicePaddingExportTests.NetAndAuditBooks())
        {
            SyntaxTree tree;
            try { tree = SyntaxTree.Parse(System.IO.File.ReadAllText(path)); }
            catch { continue; }
            var root = tree.GetRoot();
            var collector = new MeasureCollector();
            try { SvgGenerator.CollectScore(collector, tree, RenderSpecParser.FindFirst(tree)); }
            catch { continue; }
            books++;
            string book = System.IO.Path.GetFileName(path);

            var expectedTranspose = PartTranspose.ReadScoreDefault(root);
            var (transpose, concert) = collector.FileDefaultsForTest;
            Assert.True(expectedTranspose == transpose, $"{book}: transpose default {expectedTranspose} vs {transpose}");
            Assert.True(ConcertPitch.FileIsConcert(root) == concert, $"{book}: file pitch convention");
            if (expectedTranspose != null) transposing++;

            var expectedBars = SectionBarCounts.CanonicalByNameSyntactic(root)
                .OrderBy(kv => kv.Key, System.StringComparer.Ordinal).ToList();
            var bars = collector.CanonicalByNameForTest()
                .OrderBy(kv => kv.Key, System.StringComparer.Ordinal).ToList();
            Assert.True(expectedBars.SequenceEqual(bars), $"{book}: canonical bar counts differ");
            sections += bars.Count;
        }
        Assert.True(books >= 50 && transposing >= 1 && sections >= books,
            $"the net must hold the shapes: books {books} transposing {transposing} sections {sections}");
    }
}
