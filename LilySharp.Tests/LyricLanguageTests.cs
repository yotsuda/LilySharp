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
using LilySharp.Core.Music;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The language lyrics are written in (owner's decisions 2026-11-09, session 1167): the file's
/// <c>language japanese</c> and a track's <c>lyrics en language english { … }</c>, names from a
/// closed lower-case vocabulary, the track's winning, Japanese when nothing is written.
/// </summary>
[Trait("Category", "Unit")]
public class LyricLanguageTests
{
    private static string Book(string head, string lyrics) => $$"""
        {{head}}
        part m { clef treble }
        section A {
          m { c'4 d e f | }
          {{lyrics}}
        }
        form { A }
        score { staff m  lyrics w }
        """;

    private static SyntaxTree Parsed(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        Assert.Equal(source, tree.GetRoot().ToFullString());
        return tree;
    }

    private static SungVoiceDto Sung(string source)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new Uri("file:///language.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = source, Version = 1, LanguageId = "lilysharp" },
        });
        var sung = server.GetSung(new SungParams { TextDocument = new TextDocumentIdentifier { Uri = uri } });
        Assert.Null(sung.Error);
        return Assert.Single(sung.Voices!);
    }

    [Theory]
    [InlineData("lyrics w language english sings m { la la la la | }")]
    [InlineData("lyrics w sings m language english { la la la la | }")]
    [InlineData("lyrics w language english { la la la la | }")]
    public void ATracksLanguage_ComesEitherSideOfSings_AndIsSung(string lyrics)
    {
        string source = Book("", lyrics);
        var block = Assert.Single(Parsed(source).GetRoot().DescendantNodes<LyricsBlockSyntax>());
        Assert.Equal("w", block.VoiceName);
        Assert.Equal("english", block.Language);
        if (lyrics.Contains("sings"))
            Assert.Equal("m", block.SingsTarget);
        var voice = Sung(source);
        Assert.Equal("en", voice.Language);
        Assert.Empty(voice.Warnings);               // the kana check is the Japanese words'
    }

    [Fact]
    public void TheFilesLanguage_IsEveryTracks_UnlessTheTrackNamesItsOwn()
    {
        Assert.Equal("ko", Sung(Book("language korean", "lyrics w sings m { la la la la | }")).Language);
        Assert.Equal("en", Sung(Book("language korean", "lyrics w language english sings m { la la la la | }")).Language);
    }

    [Fact]
    public void NothingWritten_IsJapanese_WhoseSyllablesAreChecked()
    {
        var kana = Sung(Book("", "lyrics w sings m { さ く ら ら | }"));
        Assert.Equal("ja", kana.Language);
        Assert.Empty(kana.Warnings);
        Assert.NotEmpty(Sung(Book("", "lyrics w sings m { la la la la | }")).Warnings);
    }

    [Theory]
    [InlineData("language japanes", "lyrics w sings m { さ く ら ら | }")]
    [InlineData("", "lyrics w language English sings m { la la la la | }")]    // the names are lower-case
    public void AWordThatIsNoLanguage_IsAnError_OnTheWord(string head, string lyrics)
    {
        string source = Book(head, lyrics);
        var error = Assert.Single(SyntaxTree.Parse(source).Diagnostics, d => d.Severity == LilySharp.Core.Syntax.DiagnosticSeverity.Error);
        Assert.Equal(DiagnosticCodes.LyricLanguageUnknown, error.Code);
        Assert.Contains(source[error.Span.Start..error.Span.End], new[] { "japanes", "English" });
    }

    [Theory]
    [InlineData("lyrics language { la la la la | }", null)]
    [InlineData("lyrics language sings m { la la la la | }", "m")]
    public void ATrackMayStillBeCalledLanguage(string lyrics, string? target)
    {
        var block = Assert.Single(Parsed(Book("", lyrics)).GetRoot().DescendantNodes<LyricsBlockSyntax>());
        Assert.Equal("language", block.VoiceName);
        Assert.Null(block.Language);
        Assert.Equal(target, block.SingsTarget);
    }

    [Fact]
    public void AnUnnamedTrack_TakesTheClauses()
    {
        var block = Assert.Single(Parsed(Book("", "lyrics language english sings m { la la la la | }")).GetRoot().DescendantNodes<LyricsBlockSyntax>());
        Assert.Equal(SyntaxFacts.UnnamedLyricsName, block.VoiceName);
        Assert.Equal("english", block.Language);
        Assert.Equal("m", block.SingsTarget);
    }

    [Theory]
    [InlineData("language ")]
    [InlineData("language ja")]
    [InlineData("lyrics w language ")]
    [InlineData("lyrics w sings m language en")]
    [InlineData("section A { m { c4 d } lyrics w language ")]
    public void TheEditor_OffersTheNames(string text)
        => Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterLanguage, LilySharpLanguageServer.GetCompletionContext(text, text.Length));

    [Theory]
    [InlineData("lyrics w { the language ")]                 // a body's words
    [InlineData("score { staff m  lyrics w language ")]      // a score's row takes none
    public void TheEditor_OffersNoNames_Elsewhere(string text)
        => Assert.NotEqual(LilySharpLanguageServer.CompletionContext.AfterLanguage, LilySharpLanguageServer.GetCompletionContext(text, text.Length));

    // ── singer (owner's decisions 2026-11-09, sessions 1166 and 1168) ──

    private const string Parts = """
        part m { clef treble  singer "soprano" }
        section A {
          m { c'4 d e f | }
          {{lyrics}}
        }
        form { A }
        score { {{row}} }
        """;

    private static string Sheet(string lyrics, string row) => Parts.Replace("{{lyrics}}", lyrics).Replace("{{row}}", row);

    [Theory]
    [InlineData("lyrics w sings m { さ く ら ら | }", "staff m  lyrics w", "soprano")]                                     // the part's
    [InlineData("lyrics w singer \"alto\" sings m { さ く ら ら | }", "staff m  lyrics w", "alto")]                       // the track's over the part's
    [InlineData("lyrics w sings m singer \"alto\" { さ く ら ら | }", "staff m  lyrics w", "alto")]                       // either side of sings
    [InlineData("lyrics w singer \"alto\" sings m { さ く ら ら | }", "staff m  lyrics w singer \"neutrino/MERROW\" sings m", "neutrino/MERROW")]  // the row's over all (the owner's shape)
    [InlineData("lyrics w singer \"alto\" sings m { さ く ら ら | }", "staff m  lyrics w sings m singer \"neutrino/MERROW\"", "neutrino/MERROW")]
    public void TheNearestSinger_Wins_AndReachesTheVoice(string lyrics, string row, string expected)
    {
        string source = Sheet(lyrics, row);
        Parsed(source);
        Assert.Equal(expected, Sung(source).Singer);
    }

    [Fact]
    public void NoSingerWritten_IsNull()
        => Assert.Null(Sung(Book("", "lyrics w sings m { さ く ら ら | }")).Singer);

    [Theory]
    [InlineData("lyrics singer { la la la la | }")]
    [InlineData("lyrics singer sings m { la la la la | }")]
    public void ATrackMayStillBeCalledSinger(string lyrics)
    {
        var block = Assert.Single(Parsed(Book("", lyrics)).GetRoot().DescendantNodes<LyricsBlockSyntax>());
        Assert.Equal("singer", block.VoiceName);
        Assert.Null(block.Singer);
    }

    [Fact]
    public void AnEmptySinger_IsAnError()
    {
        var error = Assert.Single(SyntaxTree.Parse(Book("", "lyrics w singer \"\" { さ く ら ら | }")).Diagnostics,
            d => d.Severity == LilySharp.Core.Syntax.DiagnosticSeverity.Error);
        Assert.Equal(DiagnosticCodes.SingerEmpty, error.Code);
    }

    private static string? SlotLanguage(string source, string marker = "§")
    {
        int at = source.IndexOf(marker, StringComparison.Ordinal);
        string text = source.Remove(at, marker.Length);
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new Uri("file:///slot.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, Version = 1, LanguageId = "lilysharp" },
        });
        var slot = server.GetSingerSlot(new SingerSlotParams { TextDocument = new TextDocumentIdentifier { Uri = uri }, Offset = at });
        return slot.InSlot ? slot.Language : null;
    }

    [Theory]
    [InlineData("part m { singer \"§\" }\nsection A { m { c'4 | } lyrics w sings m { la | } }\n", "ja")]               // nothing written
    [InlineData("language english\npart m { singer \"§\" }\nsection A { m { c'4 | } lyrics w sings m { la | } }\n", "en")]   // the file's
    [InlineData("part m { singer \"§\" }\nsection A { m { c'4 | } lyrics w language korean sings m { la | } }\n", "ko")]     // the track that sings the part
    [InlineData("section A { lyrics w language english singer \"§\" { la | } }\n", "en")]                              // the head's own
    [InlineData("section A { lyrics w singer \"neu§\" language english { la | } }\n", "en")]                           // written after the caret
    [InlineData("language korean\nscore { staff m  lyrics w singer \"§\" sings m }\n", "ko")]                         // a score's row
    public void TheSlot_SaysTheLanguageOfTheWords(string source, string expected)
        => Assert.Equal(expected, SlotLanguage(source));

    [Theory]
    [InlineData("lyrics w { singer \"§\" | }\n")]            // a syllable of the body
    [InlineData("singer \"§\"\n")]                           // at the top level, nothing takes one
    [InlineData("title \"§\"\n")]
    public void ElsewhereIsNoSlot(string source)
        => Assert.Null(SlotLanguage(source));

    // ── the head goes on after a clause (owner's request 2026-11-09, session 1169) ──

    private const string Declared = "part sop { clef treble }\n";

    private static string[] Offered(string text)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new Uri("file:///head.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, Version = 1, LanguageId = "lilysharp" },
        });
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, text.Length);
        return [.. (server.Completion(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(line, character),
        })?.Items ?? []).Select(i => i.Label!)];
    }

    [Theory]
    [InlineData("lyrics words sings sop ", new[] { "singer", "language" }, new[] { "sings" })]      // the owner's position
    [InlineData("lyrics words sings sop si", new[] { "singer", "language" }, new[] { "sings" })]    // being typed
    [InlineData("lyrics sings sop ", new[] { "singer", "language" }, new[] { "sings" })]            // an unnamed track
    [InlineData("section A { lyrics words sings sop ", new[] { "singer", "language" }, new[] { "sings" })]
    [InlineData("lyrics words language english sings sop ", new[] { "singer" }, new[] { "sings", "language" })]
    [InlineData("lyrics words singer \"neutrino/MERROW\" ", new[] { "sings", "language" }, new[] { "singer" })]
    public void AfterAClause_TheHeadOffersTheOthers(string head, string[] offered, string[] gone)
    {
        var labels = Offered(Declared + head);
        Assert.All(offered, l => Assert.Contains(l, labels));
        Assert.All(gone, l => Assert.DoesNotContain(l, labels));
    }

    [Theory]
    [InlineData("score { lyrics words sings sop ", "singer", "sings")]
    [InlineData("score { lyrics words singer \"x\" ", "sings", "singer")]
    [InlineData("score { grandStaff { staff sop  lyrics words sings sop ", "singer", "sings")]
    public void AfterAClause_AScoresRowOffersTheOther(string row, string offered, string gone)
    {
        var labels = Offered(Declared + row);
        Assert.Contains(offered, labels);
        Assert.DoesNotContain(gone, labels);
    }

    [Theory]
    [InlineData("lyrics words singer \"x\" sings ")]
    [InlineData("lyrics words language english sings ")]
    [InlineData("score { lyrics words singer \"x\" sings ")]
    public void ASingsAfterAClause_StillTakesThePart(string head)
        => Assert.Contains("sop", Offered(Declared + head));

    [Theory]
    [InlineData("lyrics words sings sop { la ")]             // the words themselves
    [InlineData("lyrics { lyrics sings sop ")]               // a syllable that reads like a head
    [InlineData("title \"the lyrics w sings sop ")]          // a string
    [InlineData("// lyrics w sings sop ")]                   // a comment
    public void TheBodyAndOtherText_AreNoHead(string text)
        => Assert.DoesNotContain("singer", Offered(Declared + text));

    [Fact]
    public void TheSingerItem_LeavesTheCaretBetweenTheQuotes_AndOpensTheList()
    {
        var item = Assert.Single(LilySharpLanguageServer.GetLyricsTrackNameCompletions(new System.Collections.Generic.HashSet<string> { "sings", "language" }).Items, i => i.Label == "singer");
        Assert.Equal("singer \"$0\"", item.InsertText);
        Assert.Equal(InsertTextFormat.Snippet, item.InsertTextFormat);
        Assert.Equal("editor.action.triggerSuggest", item.Command!.CommandIdentifier);
    }

    [Theory]
    [InlineData("lyrics words sings sop singer \"§\"\n", "ja")]
    [InlineData("language korean\nlyrics words sings sop singer \"§\"\n", "ko")]
    [InlineData("lyrics words language english sings sop singer \"§\"\n", "en")]
    public void TheOwnersPosition_IsASlot_ThatKnowsTheLanguage(string source, string expected)
        => Assert.Equal(expected, SlotLanguage(source));

    // ── singer male "…" (owner's decision 2026-11-09, session 1173) ──

    [Theory]
    [InlineData("lyrics w singer male \"neutrino/SOMA\" sings m { さ く ら ら | }", "staff m  lyrics w", "neutrino/SOMA")]
    [InlineData("lyrics w sings m singer female \"neutrino/REINA\" { さ く ら ら | }", "staff m  lyrics w", "neutrino/REINA")]
    [InlineData("lyrics singer male \"neutrino/SOMA\" sings m { さ く ら ら | }", "staff m  lyrics", "neutrino/SOMA")]
    [InlineData("lyrics w sings m { さ く ら ら | }", "staff m  lyrics w singer male \"neutrino/SOMA\" sings m", "neutrino/SOMA")]
    [InlineData("lyrics w sings m { さ く ら ら | }", "staff m  lyrics w sings m singer female \"neutrino/REINA\"", "neutrino/REINA")]
    [InlineData("lyrics w sings m { さ く ら ら | }", "staff m  lyrics singer male \"neutrino/SOMA\" sings m", null)]
    public void AGender_MayStandBeforeTheName(string lyrics, string row, string? expected)
    {
        string source = Sheet(lyrics, row);
        Parsed(source);
        if (expected != null)
            Assert.Equal(expected, Sung(source).Singer);
    }

    [Fact]
    public void AGender_MayStandBeforeThePartsSinger()
    {
        string source = Sheet("lyrics w sings m { さ く ら ら | }", "staff m  lyrics w").Replace("singer \"soprano\"", "singer male \"neutrino/SOMA\"");
        Parsed(source);
        Assert.Equal("neutrino/SOMA", Sung(source).Singer);
    }

    [Theory]
    [InlineData("lyrics w singer man \"x\" { さ く ら ら | }", DiagnosticCodes.SingerGenderUnknown)]
    [InlineData("lyrics w singer Male \"x\" { さ く ら ら | }", DiagnosticCodes.SingerGenderUnknown)]
    [InlineData("lyrics w singer \"\" { さ く ら ら | }", DiagnosticCodes.SingerEmpty)]
    public void ABadGenderOrAnEmptyName_IsAnError(string lyrics, string code)
    {
        var error = Assert.Single(SyntaxTree.Parse(Book("", lyrics)).Diagnostics, d => d.Severity == LilySharp.Core.Syntax.DiagnosticSeverity.Error);
        Assert.Equal(code, error.Code);
    }

    [Theory]
    [InlineData("man \"x\"", DiagnosticCodes.SingerGenderUnknown)]
    [InlineData("Male \"x\"", DiagnosticCodes.SingerGenderUnknown)]
    public void APartsSinger_RefusesAnotherWord(string value, string code)
        => Assert.Contains(SyntaxTree.Parse($"part m {{ clef treble  singer {value} }}\nsection A {{ m {{ c'4 | }} }}\nform {{ A }}\nscore {{ staff m }}\n").Diagnostics,
            d => d.Code == code);

    private static string? SlotGender(string source)
    {
        int at = source.IndexOf('§');
        return LilySharpLanguageServer.SingerSlotAt(source.Remove(at, 1), at)?.Gender;
    }

    [Theory]
    [InlineData("part m { singer male \"§\" }\n", "male")]
    [InlineData("lyrics w singer female \"neu§\" { la | }\n", "female")]
    [InlineData("score { staff m  lyrics w sings m singer male \"§\" }\n", "male")]
    [InlineData("lyrics w singer \"§\" { la | }\n", null)]
    public void TheSlot_SaysTheGender(string source, string? expected)
        => Assert.Equal(expected, SlotGender(source));

    [Theory]
    [InlineData("lyrics words singer male \"neutrino/SOMA\" ", new[] { "sings", "language" }, new[] { "singer", "singer male", "singer female" })]
    [InlineData("lyrics words sings sop singer male \"x\" ", new[] { "language" }, new[] { "sings", "singer male" })]
    [InlineData("lyrics words sings sop ", new[] { "singer", "singer male", "singer female" }, new[] { "sings" })]
    public void AGenderedSinger_IsOneClause(string head, string[] offered, string[] gone)
    {
        var labels = Offered(Declared + head);
        Assert.All(offered, l => Assert.Contains(l, labels));
        Assert.All(gone, l => Assert.DoesNotContain(l, labels));
    }

    // ── one track under several staves, a singer each (owner's report, session 1176) ──

    private static string Choir(string rows) => $$"""
        part sop { clef treble }
        part alt { clef treble }
        section A {
          sop { c'4 d e f | }
          alt { a4 b c d | }
          lyrics words sings sop { さ く ら ら | }
        }
        form { A }
        score { {{rows}} }
        """;

    private static System.Collections.Generic.Dictionary<string, string?> SingersByPart(string source)
    {
        Parsed(source);
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new Uri("file:///choir.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = source, Version = 1, LanguageId = "lilysharp" },
        });
        var sung = server.GetSung(new SungParams { TextDocument = new TextDocumentIdentifier { Uri = uri } });
        Assert.Null(sung.Error);
        return sung.Voices!.ToDictionary(v => v.Part, v => v.Singer);
    }

    [Fact]
    public void TwoRowsOfOneTrack_SingWithTheirOwnSingers()
    {
        var singers = SingersByPart(Choir("staff sop  lyrics words singer \"a/A\" sings sop  staff alt  lyrics words singer \"b/B\" sings alt"));
        Assert.Equal("a/A", singers["sop"]);
        Assert.Equal("b/B", singers["alt"]);
    }

    [Fact]
    public void ARowsSinger_IsNotAnotherRowsOfTheSameTrack()
    {
        var singers = SingersByPart(Choir("staff sop  lyrics words singer \"a/A\" sings sop  staff alt  lyrics words sings alt"));
        Assert.Equal("a/A", singers["sop"]);
        Assert.Null(singers["alt"]);
    }

    [Fact]
    public void ARowNamingNoPart_IsTheTracksEverywhere()
        => Assert.Equal("a/A", SingersByPart(Choir("staff sop  lyrics words singer \"a/A\"  staff alt"))["sop"]);

    // ── which default voice sings a part (owner's decision 2026-11-09, session 1187) ──

    private static string? GenderOf(string partHead, string? singer = null)
    {
        string clause = singer == null ? "" : $" singer {singer}";
        return Sung($$"""
            part m { {{partHead}}{{clause}} }
            section A {
              m { c'4 d e f | }
              lyrics w sings m { さ く ら ら | }
            }
            form { A }
            score { staff m  lyrics w }
            """).Gender;
    }

    [Theory]
    [InlineData("instrument soprano", "female")]
    [InlineData("instrument alto", "female")]
    [InlineData("instrument voice-alto", "female")]
    [InlineData("instrument tenor", "male")]
    [InlineData("instrument baritone", "male")]
    [InlineData("instrument voice-bass", "male")]
    [InlineData("clef treble", "female")]
    [InlineData("clef treble^8", "female")]
    [InlineData("clef treble_8", "male")]
    [InlineData("clef bass", "male")]
    [InlineData("clef tenor", "male")]
    public void APart_TellsItsDefaultVoice(string head, string expected)
        => Assert.Equal(expected, GenderOf(head));

    [Theory]
    [InlineData("clef bass", "female", "female")]
    [InlineData("clef treble", "male", "male")]
    [InlineData("clef bass", "\"soprano\"", "female")]
    [InlineData("clef treble", "\"tenor\"", "male")]
    [InlineData("clef bass", "\"neutrino/MERROW\"", "male")]              // a named voice says nothing: the part does
    [InlineData("instrument soprano", "\"someone\"", "female")]
    public void TheSingersWord_WinsOverThePart(string head, string singer, string expected)
        => Assert.Equal(expected, GenderOf(head, singer));

    // ── a gender with no name: the engine picks such a voice (session 1175) ──

    [Theory]
    [InlineData("lyrics w singer male sings m { さ く ら ら | }", "staff m  lyrics w", "male")]
    [InlineData("lyrics w sings m singer female { さ く ら ら | }", "staff m  lyrics w", "female")]
    [InlineData("lyrics w singer male \"\" sings m { さ く ら ら | }", "staff m  lyrics w", "male")]       // what taking `singer male` and leaving the list writes
    [InlineData("lyrics singer male sings m { さ く ら ら | }", "staff m  lyrics", "male")]
    [InlineData("lyrics w sings m { さ く ら ら | }", "staff m  lyrics w singer female sings m", "female")]
    [InlineData("lyrics w sings m { さ く ら ら | }", "staff m  lyrics w sings m singer male", "male")]
    public void AGenderAlone_IsTheSinger(string lyrics, string row, string expected)
    {
        string source = Sheet(lyrics, row);
        Parsed(source);
        Assert.Equal(expected, Sung(source).Singer);
    }

    [Theory]
    [InlineData("singer male")]
    [InlineData("singer female")]
    [InlineData("singer male \"\"")]
    public void AGenderAlone_IsThePartsSinger(string written)
    {
        string source = Sheet("lyrics w sings m { さ く ら ら | }", "staff m  lyrics w").Replace("singer \"soprano\"", written);
        Parsed(source);
        Assert.StartsWith(written.Split(' ')[1], Sung(source).Singer);
    }

    [Fact]
    public void ATrackMayStillBeCalledSinger_BeforeOtherWords()
    {
        var block = Assert.Single(Parsed(Book("", "lyrics singer sings m { la la la la | }")).GetRoot().DescendantNodes<LyricsBlockSyntax>());
        Assert.Equal("singer", block.VoiceName);
        Assert.Equal("m", block.SingsTarget);
    }

    [Fact]
    public void AGenderAlone_IsAWholeClause()
        => Assert.Equal(new[] { "singer" }, LilySharpLanguageServer.LyricsHeadAt("lyrics words singer male ", "lyrics words singer male ".Length)!.Value.Written);

    [Fact]
    public void AnotherWordAfterSinger_IsNoClause()
        => Assert.Null(LilySharpLanguageServer.LyricsHeadAt("lyrics words singer so ", "lyrics words singer so ".Length));

    [Fact]
    public void TheGenderedItem_WritesTheWordAndOpensTheList()
    {
        var item = Assert.Single(LilySharpLanguageServer.GetLyricsTrackNameCompletions().Items, i => i.Label == "singer male");
        Assert.Equal("singer male \"$0\"", item.InsertText);
        Assert.Equal("editor.action.triggerSuggest", item.Command!.CommandIdentifier);
    }

    [Fact]
    public void TheNames_AreTheLowerCaseVocabulary()
    {
        Assert.Equal(new[] { "japanese", "english", "chinese", "korean" }, LyricLanguage.Names);
        Assert.Equal("ja", LyricLanguage.CodeOf("japanese"));
        Assert.Null(LyricLanguage.CodeOf("Japanese"));
    }
}
