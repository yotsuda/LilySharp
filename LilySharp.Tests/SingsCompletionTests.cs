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
/// Completing the `sings` binding (`lyrics ja sings vocal { }` — the track
/// binds to its melody at the DEFINITION, user decision 2026-08-19): after
/// `lyrics NAME` the editor offers the keyword; after `sings` it offers the
/// declared parts and named voices. The guards matter as much as the offers —
/// a lyric BODY holds free English text, so the words `lyrics` and `sings`
/// inside one must never re-open code completion.
/// </summary>
[Trait("Category", "Unit")]
public class SingsCompletionTests
{
    private static LilySharpLanguageServer.CompletionContext Ctx(string text)
        => LilySharpLanguageServer.GetCompletionContext(text, text.Length);

    [Theory]
    [InlineData("lyrics verse ")]                                    // track-major, top level
    [InlineData("lyrics verse si")]                                  // typing the keyword
    [InlineData("section A { melody { c4 d } lyrics words ")]        // by-section cell
    public void AfterTheTrackName_OffersTheBindingKeyword(string text)
        => Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterLyricsTrackName, Ctx(text));

    [Theory]
    [InlineData("lyrics verse sings ")]
    [InlineData("lyrics verse sings vo")]                            // typing the target
    [InlineData("section A { melody { c4 d } lyrics words sings ")]
    public void AfterSings_OffersTheBindingTargets(string text)
        => Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterSingsTarget, Ctx(text));

    [Theory]
    [InlineData("lyrics verse { ")]                                  // the body, not the header
    [InlineData("score { staff m  lyrics w ")]                  // a score ROW, not a definition
    public void NeitherTheBodyNorAScoreRow_OffersTheKeyword(string text)
        => Assert.NotEqual(LilySharpLanguageServer.CompletionContext.AfterLyricsTrackName, Ctx(text));

    /// <summary>An English syllable "sings" inside a body (`he sings ▮`) must not
    /// re-open part-name completion — the guard is the word TWO back being
    /// `lyrics`, which a body's brace has already emptied.</summary>
    [Fact]
    public void ASyllableSpelledSings_DoesNotOfferBindingTargets()
        => Assert.NotEqual(LilySharpLanguageServer.CompletionContext.AfterSingsTarget,
            Ctx("lyrics verse sings melody { section A { he sings "));

    [Fact]
    public void TrackNameCompletions_ContainSings()
        => Assert.Contains(LilySharpLanguageServer.GetLyricsTrackNameCompletions().Items,
            i => i.Label == "sings");

    [Fact]
    public void SingsTargets_AreTheDeclaredPartsAndNamedVoices()
    {
        var labels = LilySharpLanguageServer.GetVoiceBindingNameCompletions(
                "part vocal { }\npart bass { }\nsection A { vocal { voice sop { c4 } } }",
                "detail")
            .Items.Select(i => i.Label).ToArray();
        Assert.Contains("vocal", labels);
        Assert.Contains("bass", labels);
        Assert.Contains("sop", labels);
    }

    // ── the ROW spelling: `score { … lyrics w sings m }` ──

    [Theory]
    [InlineData("score { staff m  lyrics w ")]                  // after the row's name
    [InlineData("score { staff m  lyrics w si")]                // typing the keyword
    public void AScoreRowAfterItsName_OffersTheBindingKeyword(string text)
        => Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterLyricsRowAttachName, Ctx(text));

    /// <summary>The owner's report (2026-10-25, public-domain-scores sakura-sakura.lys):
    /// `lyrics words ▮` as the last row of a NAMED score, under a one-line staff row and with
    /// the closing brace already typed, offered no `sings`.</summary>
    [Theory]
    [InlineData("score chords {\n  subtitle \"さくらさくら — chords & lyrics\"\n  chords harmony\n  staff comp as lines 1\n  lyrics words ", "\n}\n")]
    [InlineData("score chords {\n  staff comp as lines 1\n  lyrics words ", "\n}\n")]
    [InlineData("score {\n  staff m\n  lyrics words ", "\n}\n")]
    [InlineData("score { lyrics words ", " }")]
    public void ARowInARealScoreAfterItsName_OffersTheBindingKeyword(string head, string tail)
        => Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterLyricsRowAttachName,
            LilySharpLanguageServer.GetCompletionContext(head + tail, head.Length));

    /// <summary>A score NAMED like a block keyword (`score chords {`) is still a score: its
    /// body offers the score's rows, and its `sings` the binding targets.</summary>
    [Theory]
    [InlineData("score chords {\n  ", "ScoreBlock")]
    [InlineData("score lyrics {\n  ", "ScoreBlock")]
    [InlineData("score tab {\n  ", "ScoreBlock")]
    [InlineData("score chords {\n  lyrics words sings ", "AfterSingsTarget")]
    public void AScoreNamedLikeABlockKeyword_IsAScore(string head, string expected)
        => Assert.Equal(System.Enum.Parse<LilySharpLanguageServer.CompletionContext>(expected),
            LilySharpLanguageServer.GetCompletionContext(head + "\n}\n", head.Length));

    /// <summary>The same report end to end, through the server's completion handler on the
    /// whole file: `sings` after the row's name, then the parts after `sings `.</summary>
    [Fact]
    public void TheReportedFile_OffersSingsThenTheParts()
    {
        const string head = """
            title "Sakura Sakura"
            part guitar { instrument guitar }
            part comp
            section Song {
              guitar { a'4 a b2 | }
              lyrics words sings guitar { さ く ら | }
              chords harmony { Am | }
              comp { /4 4 4 4 | }
            }
            form { ~Song }
            score {
              staff guitar
              tab guitar
            }
            score chords {
              subtitle "さくらさくら — chords & lyrics"
              chords harmony
              staff comp as lines 1
              lyrics words
            """;
        const string tail = "\n}\n";

        Assert.Contains("sings", LabelsAt(head + " " + tail, head.Length + 1));
        string afterSings = head + " sings ";
        var parts = LabelsAt(afterSings + tail, afterSings.Length);
        Assert.Contains("guitar", parts);
        Assert.Contains("comp", parts);
    }

    /// <summary>Picking the track on a score's lyrics row writes `sings ` after it and opens the
    /// part list (the owner's request, 2026-10-25) — unless the row already says `sings`.</summary>
    [Fact]
    public void PickingTheRowsTrack_GoesOnToTheBinding()
    {
        const string head = "part guitar\nlyrics words { section A { la | } }\nsection A { guitar { c4 | } }\nform { A }\nscore {\n  staff guitar\n  lyrics ";
        var item = Assert.Single(LilySharpLanguageServer.GetLyricsRowNameCompletions(head + "\n}\n", head.Length).Items);
        Assert.Equal("words sings $0", item.InsertText);
        Assert.Equal("editor.action.triggerSuggest", item.Command?.CommandIdentifier);

        string bound = head + "wo sings guitar\n}\n";
        var plain = Assert.Single(LilySharpLanguageServer.GetLyricsRowNameCompletions(bound, head.Length + 2).Items);
        Assert.Null(plain.Command);
        Assert.NotEqual("words sings $0", plain.InsertText);
    }

    private static string[] LabelsAt(string text, int offset)
    {
        var server = new LilySharpLanguageServer(System.IO.Stream.Null, System.IO.Stream.Null);
        var uri = new System.Uri("file:///sakura-sakura.lys");
        server.DidOpen(new LilySharp.Lsp.Protocol.DidOpenTextDocumentParams
        {
            TextDocument = new LilySharp.Lsp.Protocol.TextDocumentItem { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        return [.. (server.Completion(new LilySharp.Lsp.Protocol.CompletionParams
        {
            TextDocument = new LilySharp.Lsp.Protocol.TextDocumentIdentifier { Uri = uri },
            Position = new LilySharp.Lsp.Protocol.Position(line, character),
        })?.Items ?? []).Select(i => i.Label!)];
    }

    [Fact]
    public void AGroupRowAfterItsName_OffersTheBindingKeyword()
        => Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterGroupLyricsRowAttachName,
            Ctx("score { grandStaff { staff m  lyrics w "));

    [Theory]
    [InlineData("score { staff m  lyrics w sings ")]
    [InlineData("score { staff m  lyrics w sings vo")]          // typing the target
    [InlineData("score { grandStaff { staff m  lyrics w sings ")]
    public void AScoreRowsSings_OffersTheBindingTargets(string text)
        => Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterSingsTarget, Ctx(text));

    /// <summary>`score { lyrics ▮ }` offers the declared lyrics TRACKS — those that state
    /// their binding at the definition too (`lyrics words sings melody { … }`), which the
    /// plain `KEYWORD name {` scan read as a declaration of `sings` (reported 2026-10-06:
    /// the row offered nothing). Parts are not tracks and are not offered.</summary>
    [Fact]
    public void AScoreRowsName_OffersEveryDeclaredTrack_BoundOrNot()
    {
        const string head = """
            part melody { clef treble }
            part bass { clef bass }
            lyrics words sings melody { section A { la la | } }
            lyrics verse2 { section A { lo lo | } }
            section A { melody { c4 d | } bass { c2 | } lyrics cell sings bass { do | } }
            score { staff melody  lyrics
            """ + " ";
        string text = head + " }";
        Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterLyricsRef,
            LilySharpLanguageServer.GetCompletionContext(text, head.Length));
        var labels = LilySharpLanguageServer.GetDeclaredNameCompletions(text, "lyrics", "Lyrics part")
            .Items.Select(i => i.Label).ToArray();
        Assert.Equal(new[] { "words", "verse2", "cell" }, labels);
    }

    [Fact]
    public void RowAttachCompletions_LeadWithSings()
    {
        Assert.Contains(LilySharpLanguageServer.GetLyricsRowAttachNameCompletions().Items,
            i => i.Label == "sings");
        Assert.Contains(LilySharpLanguageServer.GetGroupLyricsRowAttachNameCompletions().Items,
            i => i.Label == "sings");
    }
}
