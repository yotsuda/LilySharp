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
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Value positions after tempo / time / partial / title, and the render-spec
/// positions inside score { }, offer only what fits there — not the keyword list.
/// </summary>
[Trait("Category", "Unit")]
public class ValueContextCompletionTests
{
    private static LilySharpLanguageServer.CompletionContext ContextOf(string text)
        => LilySharpLanguageServer.GetCompletionContext(text, text.Length);

    [Theory]
    [InlineData("tempo ", "AfterTempo")]
    [InlineData("time ", "AfterTime")]
    [InlineData("time 4", "AfterTime")]
    [InlineData("partial ", "AfterPartial")]
    [InlineData("title ", "AfterTitleText")]
    [InlineData("composer ", "AfterTitleText")]
    [InlineData("section A { m { time ", "AfterTime")]
    [InlineData("part m { tempo ", "AfterTempo")]
    public void ValueKeywords_GetTheirOwnContext(string text, string expected)
    {
        Assert.Equal(expected, ContextOf(text).ToString());
    }

    [Fact]
    public void InsideATitleString_TempoIsNotHijacked()
    {
        Assert.NotEqual(
            LilySharpLanguageServer.CompletionContext.AfterTempo,
            ContextOf("title \"tempo "));
    }

    [Theory]
    [InlineData("score \"s\" { ", "ScoreBlock")]
    [InlineData("score s { ", "ScoreBlock")]
    [InlineData("score \"s\" { staff ", "AfterStaffRef")]
    [InlineData("score s { grandStaff { staff ", "AfterStaffRef")]
    [InlineData("score \"s\" { staff melody with ", "AfterWith")]
    [InlineData("score \"s\" { staff melody with chords ", "AfterChordsRef")]
    [InlineData("score \"s\" { chords ", "AfterChordsRef")]
    [InlineData("score \"s\" { lyrics ", "AfterLyricsRef")]
    public void InsideAScoreBlock_RenderSpecContexts(string text, string expected)
    {
        Assert.Equal(expected, ContextOf(text).ToString());
    }

    [Fact]
    public void SectionBodies_AreNotScoreBlocks()
    {
        // `section A { m {` is music, not a render spec.
        Assert.Equal(
            LilySharpLanguageServer.CompletionContext.MusicBlock,
            ContextOf("section A { m { c4 d "));
    }

    [Fact]
    public void StaffRef_OffersTheDeclaredPartNames()
    {
        var text = "part melody { clef treble }\npart bass { clef bass }\n";
        var labels = LilySharpLanguageServer.GetDeclaredNameCompletions(text, "part", "Part")
            .Items.Select(i => i.Label).ToArray();
        Assert.Equal(new[] { "melody", "bass" }, labels);
    }

    [Fact]
    public void TimeCompletions_LeadWithCommonTime()
    {
        var labels = LilySharpLanguageServer.GetTimeCompletions().Items
            .Select(i => i.Label).ToArray();
        Assert.Equal("4/4", labels[0]);
        Assert.Contains("6/8", labels);
    }

    [Fact]
    public void TitleContext_OffersOnlyTheQuotePair()
    {
        // The text itself is typed; the single snippet just drops "" and
        // parks the caret inside.
        var items = LilySharpLanguageServer.GetTitleTextCompletions("title").Items;
        var item = Assert.Single(items);
        Assert.Equal("\"$0\"", item.InsertText);
        Assert.Equal("Quoted title text", item.Detail);
        Assert.Equal("Quoted composer name",
            Assert.Single(LilySharpLanguageServer.GetTitleTextCompletions("composer").Items).Detail);
    }
}
