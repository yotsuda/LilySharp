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
using LilySharp.Core.Semantics;
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
    // `octave ` at global scope offers only its two modes. NOT in a part header — see
    // OctaveModes_AreNotOfferedInsideAPartHeader below, which is where that row went.
    [InlineData("octave ", "AfterOctave")]
    // `pitch ` offers its two modes in all three homes of the word: the top-level directive,
    // a part header, and a score header (before the brace, so the block stack is empty).
    [InlineData("pitch ", "AfterPitch")]
    [InlineData("pitch wr", "AfterPitch")]
    [InlineData("part sax { pitch ", "AfterPitch")]
    [InlineData("score full pitch ", "AfterPitch")]
    [InlineData("score out transpose d pitch ", "AfterPitch")]
    // …and NOT in a music body, where `pitch` is no directive; nor inside a string.
    [InlineData("section A { m { pitch ", "MusicBlock")]
    [InlineData("title \"perfect pitch ", "AfterTitleText")]
    // `repeat ` in music offers its three kinds — in a section cell, mid-bar, in a voice,
    // and in a by-part inner section alike.
    [InlineData("section A { m { repeat ", "AfterRepeat")]
    [InlineData("section A { m { c4 d repeat un", "AfterRepeat")]
    [InlineData("section A { m { voice { repeat ", "AfterRepeat")]
    [InlineData("part m { section A { repeat ", "AfterRepeat")]
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

    /// <summary>
    /// After a tempo's bpm the feel words may follow (`tempo 100 swing`). Until 2026-09-26
    /// Ctrl+Space there offered nothing a tempo takes: the number ended every value context.
    /// </summary>
    [Theory]
    [InlineData("tempo 100 ", "100", false)]
    [InlineData("tempo 100", "100", true)]
    [InlineData("tempo \"Allegro\" 132 ", "132", false)]
    [InlineData("tempo \"Grave\" 4. = 54", "54", true)]
    [InlineData("tempo Comodo 84 ", "84", false)]
    [InlineData("section A { m { c4 tempo 96 ", "96", false)]
    [InlineData("part m { tempo 120 ", "120", false)]
    public void AfterATemposBpm_TheFeelWordsAreOffered(string text, string bpm, bool touching)
    {
        Assert.Equal("AfterTempoBpm", ContextOf(text).ToString());
        var run = LilySharpLanguageServer.TempoBpmBeforeCaret(text, text.Length);
        Assert.Equal((bpm, touching), run);

        var items = LilySharpLanguageServer.GetTempoFeelCompletions(bpm, touching).Items;
        Assert.Contains(items, i => i.Label == "swing");
        Assert.Contains(items, i => i.Label == "shuffle");
        var swing = items.First(i => i.Label == "swing");
        // Touching the number, the row re-types it: the editor filters by the word under
        // the caret, which is the number.
        Assert.Equal(touching ? $"{bpm} swing" : "swing", swing.InsertText);
        Assert.Equal(touching ? $"{bpm} swing" : "swing", swing.FilterText);
    }

    private static string[] LabelsAtEnd(string text)
    {
        var server = new LilySharpLanguageServer(System.IO.Stream.Null, System.IO.Stream.Null);
        var uri = new System.Uri("file:///tempo.lys");
        server.DidOpen(new LilySharp.Lsp.Protocol.DidOpenTextDocumentParams
        {
            TextDocument = new LilySharp.Lsp.Protocol.TextDocumentItem
            { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, text.Length);
        var list = server.Completion(new LilySharp.Lsp.Protocol.CompletionParams
        {
            TextDocument = new LilySharp.Lsp.Protocol.TextDocumentIdentifier { Uri = uri },
            Position = new LilySharp.Lsp.Protocol.Position(line, character),
        });
        return (list?.Items ?? []).Select(i => i.Label!).ToArray();
    }

    /// <summary>End to end: a header's `tempo 100 ` offers the feel words alone, and a
    /// mid-music `tempo 96 ` offers them AHEAD of the music list — a note may follow there.</summary>
    [Fact]
    public void TheFeelWords_AloneInAHeader_BesideTheNotesInMusic()
    {
        var header = LabelsAtEnd("tempo 100 ");
        Assert.Equal("swing", header[0]);
        Assert.All(header, l => Assert.Contains(l.Split(' ')[0], new[] { "swing", "shuffle" }));

        var music = LabelsAtEnd("part m { clef treble }\nsection A { m { c4 tempo 96 ");
        Assert.Equal("swing", music[0]);
        Assert.Contains("c", music);
    }

    /// <summary>
    /// After a tempo's feel word the swung note value may follow (`tempo 100 swing 16`).
    /// User requests 2026-09-26: Ctrl+Space after the feel word offers 8 and 16, with a
    /// bpm before it or without.
    /// </summary>
    [Theory]
    [InlineData("tempo 120 swing ", "swing", false)]
    [InlineData("tempo 100 swing", "swing", true)]
    [InlineData("tempo \"Grave\" 4. = 54 shuffle ", "shuffle", false)]
    [InlineData("tempo Comodo 84 swing ", "swing", false)]
    [InlineData("section A { m { c4 tempo 96 swing ", "swing", false)]
    [InlineData("tempo swing ", "swing", false)]
    [InlineData("tempo swing", "swing", true)]
    [InlineData("tempo \"Medium\" shuffle ", "shuffle", false)]
    [InlineData("section A { m { c4 tempo swing ", "swing", false)]
    public void AfterATemposFeelWord_TheSwungValuesAreOffered(string text, string feel, bool touching)
    {
        Assert.Equal("AfterTempoFeel", ContextOf(text).ToString());
        Assert.Equal((feel, touching), LilySharpLanguageServer.TempoFeelBeforeCaret(text, text.Length));

        var items = LilySharpLanguageServer.GetTempoSubdivisionCompletions(feel, touching).Items;
        Assert.Equal(["8", "16"], items.Select(i => i.Label));
        foreach (var row in items)
            Assert.Equal(touching ? $"{feel} {row.Label}" : row.Label, row.InsertText);
    }

    [Fact]
    public void TheSwungValues_AloneInAHeader_BesideTheNotesInMusic()
    {
        Assert.Equal(["8", "16"], LabelsAtEnd("tempo 120 swing "));
        Assert.Equal(["8", "16"], LabelsAtEnd("tempo swing "));
        var music = LabelsAtEnd("part m { clef treble }\nsection A { m { c4 tempo 96 swing ");
        Assert.Equal("8", music[0]);
        Assert.Equal("16", music[1]);
        Assert.Contains("c", music);
    }

    /// <summary>`tempo |` offers the feel word alone — the equation with no metronome mark
    /// (user request 2026-09-26) — beside the forms with a bpm.</summary>
    [Fact]
    public void AfterTempo_TheFeelWordAloneIsOffered()
    {
        var labels = LabelsAtEnd("tempo ");
        foreach (string feel in LanguageVocabulary.TempoFeelWords)
        {
            Assert.Contains(feel, labels);
            Assert.Contains($"120 {feel}", labels);
        }
    }

    [Theory]
    [InlineData("tempo 100 swing 16 ")]
    [InlineData("tempo 100 ")]
    [InlineData("tempo Comodo ")]
    [InlineData("tempo swing 100 ")]
    [InlineData("section A { m { tempo 100 swing c4 ")]
    public void ElsewhereSixteenIsNot(string text)
    {
        Assert.NotEqual("AfterTempoFeel", ContextOf(text).ToString());
    }

    [Theory]
    // A feel word is already there — nothing more of that kind fits.
    [InlineData("tempo 100 swing ")]
    [InlineData("tempo 100 swing 16")]
    [InlineData("tempo swing 100 ")]
    // Not a tempo's bpm: an equation still missing its bpm, a time signature, a title.
    [InlineData("tempo 4 = ")]
    [InlineData("time 4/4 ")]
    [InlineData("title \"tempo 100 ")]
    // The bpm ended and music began.
    [InlineData("section A { m { tempo 100 c4 ")]
    public void ElsewhereTheFeelWordsAreNot(string text)
    {
        Assert.NotEqual("AfterTempoBpm", ContextOf(text).ToString());
    }

    [Fact]
    public void InsideATitleString_TempoIsNotHijacked()
    {
        Assert.NotEqual(
            LilySharpLanguageServer.CompletionContext.AfterTempo,
            ContextOf("title \"tempo "));
    }

    [Theory]
    [InlineData("score s { ", "ScoreBlock")]
    [InlineData("score { ", "ScoreBlock")]
    [InlineData("score s { staff ", "AfterStaffRef")]
    // `tab` has its own list since 2026-09-10: the parts AND the tunings that may precede one.
    [InlineData("score s { tab ", "AfterTabRef")]
    [InlineData("score { grandStaff { staff ", "AfterStaffRef")]
    [InlineData("score { grandStaff { staff m  lyrics ", "AfterLyricsRef")]
    [InlineData("score s { chords ", "AfterChordsRef")]
    [InlineData("score s { lyrics ", "AfterLyricsRef")]
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
        // (colour, transparency) — no misleading no-op overrides. force-hshift left the
        // list 2026-08-23 with its vocabulary row: LYS1029 now refuses it while its
        // implementation is disabled, and offering a refused spelling would mislead.
        var labels = LilySharpLanguageServer.GetOverrideCompletions().Items
            .Select(i => i.Label).ToArray();
        Assert.Equal(
            new[] { "NoteHead.color", "Stem.color", "NoteHead.transparent", "Stem.transparent" },
            labels);
        // Inserts `Grob.property = ` (no value pre-filled) and re-opens the popup so the
        // value list appears next — for an enumerable value (colour, true/false).
        var color = LilySharpLanguageServer.GetOverrideCompletions().Items.First();
        Assert.Equal("NoteHead.color = ", color.InsertText);
        Assert.Equal("editor.action.triggerSuggest", color.Command?.CommandIdentifier);
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

        // A property outside the offered set (force-hshift is refused by LYS1029 while
        // its implementation is disabled) enumerates nothing.
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
    public void RevertAndOnce_AreMusicOnly_NotOfferedAtTopLevel()
    {
        // `revert` / `once` are positional (music-only) — a top-level revert is LYS1023 —
        // so the top-level list omits them, while the in-music list keeps them (with the
        // property-list retrigger).
        var top = LilySharpLanguageServer.GetTopLevelCompletions().Items;
        Assert.DoesNotContain(top, i => i.Label == "revert");
        Assert.DoesNotContain(top, i => i.Label is "once" or "once override");

        var music = LilySharpLanguageServer.GetMusicCompletions("", keySharps: 0).Items;
        var rv = music.Single(i => i.Label == "revert");
        Assert.Equal("revert $0", rv.InsertText);
        Assert.Equal("editor.action.triggerSuggest", rv.Command?.CommandIdentifier);
        Assert.Contains(music, i => i.Label == "once override");
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
    public void PitchKeyword_IsOfferedAtTopLevel_AndAutoTriggersTheModeList()
    {
        // `pitch` completes at global scope, inserts the bare keyword and re-opens the
        // suggest popup so written / concert appear immediately — the same motion as
        // `octave`. Until 2026-09-03 it inserted a snippet CHOICE instead, a private copy of
        // the two words that the part-header item never had.
        var pitch = LilySharpLanguageServer.GetTopLevelCompletions().Items
            .Single(i => i.Label == "pitch");
        Assert.Equal("pitch $0", pitch.InsertText);
        Assert.Equal("editor.action.triggerSuggest", pitch.Command?.CommandIdentifier);

        var modes = LilySharpLanguageServer.GetPitchModeCompletions().Items
            .Select(i => i.Label).ToArray();
        Assert.Equal(new[] { "written", "concert" }, modes);
    }

    [Fact]
    public void LayoutKeyword_IsOfferedAtTopLevel_PrefilledWithTheDefaults_AndInAScoreAsAReference()
    {
        // `layout` completes at global scope as the block pre-filled with LilyPond's
        // defaults (the paper snippet's rule: accepting it and changing nothing does not
        // move the page), and inside a score body as the bare keyword that re-opens the
        // popup on the declared block names.
        var top = LilySharpLanguageServer.GetTopLevelCompletions().Items
            .Single(i => i.Label == "layout");
        Assert.Contains("markTempo ${1:stacked}", top.InsertText, StringComparison.Ordinal);
        Assert.Contains("barNumbers ${2:lines}", top.InsertText, StringComparison.Ordinal);

        var inScore = LilySharpLanguageServer.GetScoreBlockCompletions().Items
            .Single(i => i.Label == "layout");
        Assert.Equal("layout $0", inScore.InsertText);
        Assert.Equal("editor.action.triggerSuggest", inScore.Command?.CommandIdentifier);

        // The word keys re-open the popup on their own words, which come from the compiler; a
        // NUMBER key (the line thicknesses, 2026-10-05) inserts its default as the placeholder
        // and opens nothing.
        var keys = LilySharpLanguageServer.GetLayoutBlockCompletions().Items;
        Assert.All(keys.Where(k => LanguageVocabulary.LayoutNumberKeyDefault(k.Label) == null),
            k => Assert.Equal("editor.action.triggerSuggest", k.Command?.CommandIdentifier));
        var stem = keys.Single(k => k.Label == "Stem.thickness");
        Assert.Null(stem.Command);
        Assert.Equal("Stem.thickness ${1:1.3}", stem.InsertText);
        Assert.Contains("Stem.thickness ${", top.InsertText, StringComparison.Ordinal);
        Assert.Equal(new[] { "stacked", "beside" },
            LilySharpLanguageServer.GetMarkArrangementCompletions().Items.Select(i => i.Label));
        Assert.Equal(new[] { "lines", "none", "every" },
            LilySharpLanguageServer.GetBarNumberPolicyCompletions().Items.Select(i => i.Label));
    }

    [Theory]
    [InlineData("layout ", "AfterLayoutKeyword")]
    [InlineData("tempo 120\nlayout ", "AfterLayoutKeyword")]
    [InlineData("score { layout ", "AfterLayoutBlockRef")]
    [InlineData("score { staff m  layout ch", "AfterLayoutBlockRef")]
    [InlineData("layout {", "LayoutBlock")]
    [InlineData("layout { ", "LayoutBlock")]
    [InlineData("layout { markTempo beside\n  ", "LayoutBlock")]
    [InlineData("layout chart { ", "LayoutBlock")]
    [InlineData("score { layout chart { ", "LayoutBlock")]
    [InlineData("layout { markTempo ", "AfterLayoutMarks")]
    [InlineData("layout { markTempo be", "AfterLayoutMarks")]
    [InlineData("layout { barNumbers ", "AfterLayoutBarNumbers")]
    [InlineData("layout { markTempo beside  barNumbers ev", "AfterLayoutBarNumbers")]
    [InlineData("layout { Stem.thickness ", "AfterLayoutNumberKey")]
    [InlineData("layout { lineThickness ", "AfterLayoutNumberKey")]
    [InlineData("layout { Stem.thickness 1.5\n  ", "LayoutBlock")]
    public void TheLayoutBlock_ServesItsKeysAndTheirWords(string text, string expected)
        => Assert.Equal(expected, ContextOf(text).ToString());

    [Theory]
    // Not a part header, not a music body, not a string: `marks` is a plain word there,
    // and the arrangements are not offered.
    [InlineData("part m { marks ")]
    [InlineData("section A { m { marks ")]
    [InlineData("title \"rehearsal marks ")]
    [InlineData("marks ")]
    [InlineData("score { marks ")]
    public void MarksArrangements_AreNotOfferedOutsideTheLayoutBlock(string text)
        => Assert.NotEqual(LilySharpLanguageServer.CompletionContext.AfterLayoutMarks, ContextOf(text));

    [Theory]
    // `repeat` is an English word: as a LYRIC (a top-level track's inner section, and a
    // note-bound lyrics cell), as a stray in a part header, and at the top level it is not
    // the directive, and the kinds are not offered.
    [InlineData("lyrics v sings m { section A { repeat ")]
    [InlineData("lyrics v { section A { repeat ")]
    [InlineData("section A { m { c } lyrics { repeat ")]
    [InlineData("part m { repeat ")]
    [InlineData("repeat ")]
    [InlineData("title \"repeat ")]
    public void RepeatKinds_AreNotOfferedWhereTheWordIsNotTheDirective(string text)
    {
        Assert.NotEqual(LilySharpLanguageServer.CompletionContext.AfterRepeat, ContextOf(text));
    }

    [Fact]
    public void RepeatKeyword_AutoTriggersTheKindList_InBothMusicLists()
    {
        // Completing `repeat` inserts the bare keyword and re-opens the popup so unfold /
        // percent / tremolo appear at once (owner request 2026-09-03). Until then the pitched
        // list committed the writer to `repeat unfold 2 { }` and the drum list to `percent`,
        // and the other kinds were only named in the Detail.
        foreach (var list in new[]
        {
            LilySharpLanguageServer.GetMusicCompletions("", keySharps: 0),
            LilySharpLanguageServer.GetDrumCompletions(),
        })
        {
            var repeat = list.Items.Single(i => i.Label == "repeat");
            Assert.Equal("repeat $0", repeat.InsertText);
            Assert.Equal("editor.action.triggerSuggest", repeat.Command?.CommandIdentifier);
        }

        var kinds = LilySharpLanguageServer.GetRepeatKindCompletions().Items
            .Select(i => i.Label).ToArray();
        Assert.Equal(new[] { "unfold", "percent", "tremolo" }, kinds);
    }

    [Theory]
    [InlineData("section A { m { c4\\")]
    [InlineData("section A { m { c4\\3 d\\")]
    public void AfterBackslash_OffersOnlyTabStringNumbers(string text)
    {
        // Owner report 2026-09-03: Ctrl+Space right after `\` offered the LilyPond dynamic
        // names (`cresc`, `dim`, `ppp` …), every one of which the parser refuses — only a
        // digit follows a backslash in Lily#. The list is the string numbers 1..N where N is
        // the most strings any tuning has, and each compiles on a note.
        Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterBackslash, ContextOf(text));
        var labels = LilySharpLanguageServer.GetStringNumberCompletions().Items
            .Select(i => i.Label!).ToArray();
        // Seven since LilyPond's whole tuning table landed (2026-09-13): the offer is 1..N
        // for the WIDEST tuning, so the part below is that tuning and not the plain guitar —
        // otherwise `\7` would be offered and compiled against six strings.
        Assert.Equal(new[] { "1", "2", "3", "4", "5", "6", "7" }, labels);
        foreach (string n in labels)
        {
            string doc = $"part gtr {{ clef treble_8 tuning guitar7 }}\nsection A {{ gtr {{ c4\\{n} d e f | }} }}\n"
                + "form { A }\nscore { tab gtr }";
            var tree = LilySharp.Core.Syntax.SyntaxTree.Parse(doc);
            var errors = tree.Diagnostics.Concat(LilySharp.Core.Semantics.SemanticValidation.Run(tree))
                .Where(d => d.Severity == LilySharp.Core.Syntax.DiagnosticSeverity.Error)
                .Select(d => d.Message).ToArray();
            Assert.True(errors.Length == 0, $"`c4\\{n}` is offered and refused: {string.Join("; ", errors)}");
        }
    }

    [Fact]
    public void OctaveModes_AreNotOfferedInsideAPartHeader()
    {
        // ★ `octave` is two productions. At the top level (and in a section) it is the mode
        // directive and the two words are the whole vocabulary; in a PART HEADER it takes a
        // number, and since 2026-08-19 the mode words are an error there. An editor that
        // still offered them would be completing straight into a red squiggle — which it did,
        // because this context test only ever asked what follows the word `octave`, never
        // where the word is.
        const string inPart = "part vln { octave ";
        const string atTop = "octave ";

        Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterOctave,
            LilySharpLanguageServer.GetCompletionContext(atTop, atTop.Length));
        Assert.NotEqual(LilySharpLanguageServer.CompletionContext.AfterOctave,
            LilySharpLanguageServer.GetCompletionContext(inPart, inPart.Length));
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
