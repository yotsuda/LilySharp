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
/// positions inside score main { }, offer only what fits there — not the keyword list.
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
    // `octave ` at global scope (and in a part header) offers only its two modes.
    [InlineData("octave ", "AfterOctave")]
    [InlineData("part m { octave ", "AfterOctave")]
    // `override ` — and `once override `, whose previous word is also `override` —
    // offers the grob properties (at global scope and mid-music).
    [InlineData("override ", "AfterOverride")]
    [InlineData("once override ", "AfterOverride")]
    [InlineData("section A { m { c4 override ", "AfterOverride")]
    // `revert ` offers the same grob targets, minus the value.
    [InlineData("revert ", "AfterRevert")]
    [InlineData("section A { m { c4 revert ", "AfterRevert")]
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
    [InlineData("score main \"s\" { ", "ScoreBlock")]
    [InlineData("score main { ", "ScoreBlock")]
    [InlineData("score main \"s\" { staff ", "AfterStaffRef")]
    [InlineData("score main \"s\" { tab ", "AfterStaffRef")]
    [InlineData("score main { grandStaff { staff ", "AfterStaffRef")]
    [InlineData("score main \"s\" { staff melody with ", "AfterWith")]
    [InlineData("score main \"s\" { staff melody with chords ", "AfterChordsRef")]
    [InlineData("score main \"s\" { chords ", "AfterChordsRef")]
    [InlineData("score main \"s\" { lyrics ", "AfterLyricsRef")]
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
    public void TimeKeyword_AutoTriggersTheSignatureList()
    {
        // Completing `time` re-opens the suggest popup so 4/4, 3/4, … appear
        // immediately, without a second Ctrl+Space. Both the top-level directive
        // list and the in-music list carry the retrigger command.
        foreach (var list in new[]
        {
            LilySharpLanguageServer.GetTopLevelCompletions(),
            LilySharpLanguageServer.GetMusicCompletions("", keySharps: 0),
        })
        {
            var time = list.Items.Single(i => i.Label == "time");
            Assert.NotNull(time.Command);
            Assert.Equal("editor.action.triggerSuggest", time.Command!.CommandIdentifier);
        }
    }

    [Fact]
    public void OverrideKeyword_AutoTriggersThePropertyList_EverywhereItIsOffered()
    {
        // Completing `override` inserts a space and re-opens the suggest popup so the
        // grob-property list appears immediately — at the top level and mid-music.
        foreach (var list in new[]
        {
            LilySharpLanguageServer.GetTopLevelCompletions(),
            LilySharpLanguageServer.GetMusicCompletions("", keySharps: 0),
        })
        {
            var ov = list.Items.Single(i => i.Label == "override");
            Assert.Equal("override $0", ov.InsertText);
            Assert.Equal("editor.action.triggerSuggest", ov.Command?.CommandIdentifier);
        }
    }

    [Fact]
    public void OverrideCompletions_OfferOnlyTheRenderedProperties()
    {
        // Only the Grob.property pairs the renderer actually consumes are offered
        // (colour, transparency, force-hshift) — no misleading no-op overrides.
        var labels = LilySharpLanguageServer.GetOverrideCompletions().Items
            .Select(i => i.Label).ToArray();
        Assert.Equal(
            new[] { "NoteHead.color", "Stem.color", "NoteHead.transparent", "NoteColumn.force-hshift" },
            labels);
        // Inserts `Grob.property = ` (no value pre-filled) and re-opens the popup so the
        // value list appears next — for an enumerable value (colour, true/false).
        var color = LilySharpLanguageServer.GetOverrideCompletions().Items.First();
        Assert.Equal("NoteHead.color = ", color.InsertText);
        Assert.Equal("editor.action.triggerSuggest", color.Command?.CommandIdentifier);
        // A numeric value has nothing to enumerate, so it does not retrigger.
        var hshift = LilySharpLanguageServer.GetOverrideCompletions().Items
            .Single(i => i.Label == "NoteColumn.force-hshift");
        Assert.Equal("NoteColumn.force-hshift = ", hshift.InsertText);
        Assert.Null(hshift.Command);
    }

    [Theory]
    [InlineData("override NoteHead.color = ", "AfterOverrideValue")]
    [InlineData("override Stem.color = re", "AfterOverrideValue")]
    [InlineData("once override NoteHead.transparent = ", "AfterOverrideValue")]
    [InlineData("section A { m { c4 override NoteHead.color = ", "AfterOverrideValue")]
    public void OverrideValuePosition_GetsItsOwnContext(string text, string expected)
    {
        Assert.Equal(expected, ContextOf(text).ToString());
    }

    [Fact]
    public void OverrideValueCompletions_MatchTheProperty()
    {
        var colors = LilySharpLanguageServer
            .GetOverrideValueCompletions("override NoteHead.color = ", "override NoteHead.color = ".Length)
            .Items.Select(i => i.Label).ToArray();
        Assert.Contains("red", colors);
        Assert.Contains("blue", colors);
        Assert.DoesNotContain("true", colors);

        var bools = LilySharpLanguageServer
            .GetOverrideValueCompletions("override NoteHead.transparent = ", "override NoteHead.transparent = ".Length)
            .Items.Select(i => i.Label).ToArray();
        Assert.Equal(new[] { "true", "false" }, bools);

        // A numeric property offers nothing to enumerate.
        Assert.Empty(LilySharpLanguageServer
            .GetOverrideValueCompletions("override NoteColumn.force-hshift = ", "override NoteColumn.force-hshift = ".Length)
            .Items);
    }

    [Fact]
    public void RevertCompletions_OfferTheSameTargets_WithoutAValue()
    {
        // revert lists the same grob targets as override, but inserts just
        // `Grob.property` (no `= value`) — you undo an override by picking it back.
        var over = LilySharpLanguageServer.GetOverrideCompletions().Items.Select(i => i.Label).ToArray();
        var revert = LilySharpLanguageServer.GetRevertCompletions().Items.Select(i => i.Label).ToArray();
        Assert.Equal(over, revert);
        var color = LilySharpLanguageServer.GetRevertCompletions().Items.First();
        Assert.Equal("NoteHead.color", color.InsertText);
    }

    [Fact]
    public void RevertKeyword_AutoTriggersThePropertyList_EverywhereItIsOffered()
    {
        foreach (var list in new[]
        {
            LilySharpLanguageServer.GetTopLevelCompletions(),
            LilySharpLanguageServer.GetMusicCompletions("", keySharps: 0),
        })
        {
            var rv = list.Items.Single(i => i.Label == "revert");
            Assert.Equal("revert $0", rv.InsertText);
            Assert.Equal("editor.action.triggerSuggest", rv.Command?.CommandIdentifier);
        }
    }

    [Fact]
    public void OctaveKeyword_IsOfferedAtTopLevel_AndAutoTriggersTheModeList()
    {
        // `octave` completes at global scope and re-opens the suggest popup so
        // absolute / relative appear immediately, without a second Ctrl+Space.
        var octave = LilySharpLanguageServer.GetTopLevelCompletions().Items
            .Single(i => i.Label == "octave");
        Assert.Equal("octave $0", octave.InsertText);
        Assert.NotNull(octave.Command);
        Assert.Equal("editor.action.triggerSuggest", octave.Command!.CommandIdentifier);

        var modes = LilySharpLanguageServer.GetOctaveCompletions().Items
            .Select(i => i.Label).ToArray();
        Assert.Equal(new[] { "absolute", "relative" }, modes);
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
