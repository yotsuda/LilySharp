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
using LilySharp.Core.Semantics;
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
    private static string Book(string head, string lyrics, string singer = "singer sings w") => $$"""
        {{head}}
        part m { clef treble  instrument soprano }
        section A {
          m { c'4 d e f | }
          {{lyrics}}
        }
        {{singer}}
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

    [Fact]
    public void ATracksLanguage_IsSung()
    {
        string source = Book("", "lyrics w language english { la la la la | }");
        var block = Assert.Single(Parsed(source).GetRoot().DescendantNodes<LyricsBlockSyntax>());
        Assert.Equal("w", block.VoiceName);
        Assert.Equal("english", block.Language);
        var voice = Sung(source);
        Assert.Equal("en", voice.Language);
        Assert.Empty(voice.Warnings);               // the kana check is the Japanese words'
    }

    [Fact]
    public void TheFilesLanguage_IsEveryTracks_UnlessTheTrackNamesItsOwn()
    {
        Assert.Equal("ko", Sung(Book("language korean", "lyrics w { la la la la | }")).Language);
        Assert.Equal("en", Sung(Book("language korean", "lyrics w language english { la la la la | }")).Language);
    }

    [Fact]
    public void NothingWritten_IsJapanese_WhoseSyllablesAreChecked()
    {
        var kana = Sung(Book("", "lyrics w { さ く ら ら | }"));
        Assert.Equal("ja", kana.Language);
        Assert.Empty(kana.Warnings);
        Assert.NotEmpty(Sung(Book("", "lyrics w { la la la la | }")).Warnings);
    }

    [Theory]
    [InlineData("language japanes", "lyrics w { さ く ら ら | }")]
    [InlineData("", "lyrics w language English { la la la la | }")]    // the names are lower-case
    public void AWordThatIsNoLanguage_IsAnError_OnTheWord(string head, string lyrics)
    {
        string source = Book(head, lyrics);
        var error = Assert.Single(SyntaxTree.Parse(source).Diagnostics, d => d.Severity == LilySharp.Core.Syntax.DiagnosticSeverity.Error);
        Assert.Equal(DiagnosticCodes.LyricLanguageUnknown, error.Code);
        Assert.Contains(source[error.Span.Start..error.Span.End], new[] { "japanes", "English" });
    }

    [Fact]
    public void ATrackMayStillBeCalledLanguage()
    {
        var block = Assert.Single(Parsed(Book("", "lyrics language { la la la la | }")).GetRoot().DescendantNodes<LyricsBlockSyntax>());
        Assert.Equal("language", block.VoiceName);
        Assert.Null(block.Language);
    }

    [Fact]
    public void AnUnnamedTrack_TakesTheClause()
    {
        var block = Assert.Single(Parsed(Book("", "lyrics language english { la la la la | }")).GetRoot().DescendantNodes<LyricsBlockSyntax>());
        Assert.Equal(SyntaxFacts.UnnamedLyricsName, block.VoiceName);
        Assert.Equal("english", block.Language);
    }

    [Theory]
    [InlineData("language ")]
    [InlineData("language ja")]
    [InlineData("lyrics w language ")]
    [InlineData("section A { m { c4 d } lyrics w language ")]
    public void TheEditor_OffersTheNames(string text)
        => Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterLanguage, LilySharpLanguageServer.GetCompletionContext(text, text.Length));

    [Theory]
    [InlineData("lyrics w { the language ")]                 // a body's words
    [InlineData("score { staff m  lyrics w language ")]      // a score's row takes none
    public void TheEditor_OffersNoNames_Elsewhere(string text)
        => Assert.NotEqual(LilySharpLanguageServer.CompletionContext.AfterLanguage, LilySharpLanguageServer.GetCompletionContext(text, text.Length));

    // ── singer (docs/lyrics-singer-design.md) ──

    private static string Sheet(string lyrics, string singer, string row = "staff m  lyrics w") => $$"""
        part m { clef treble }
        section A {
          m { c'4 d e f | }
          {{lyrics}}
        }
        {{singer}}
        form { A }
        score { {{row}} }
        """;

    private const string Words = "lyrics w { さ く ら ら | }";

    [Theory]
    [InlineData("singer \"neutrino/MERROW\" sings w", "neutrino/MERROW")]
    [InlineData("singer \"neutrino/MERROW\" sings w on m", "neutrino/MERROW")]
    public void TheSingerLine_ReachesTheVoice(string singer, string expected)
    {
        string source = Sheet(Words, singer);
        Parsed(source);
        Assert.Equal(expected, Sung(source).Singer);
    }

    [Fact]
    public void NoVoiceWritten_IsNull()
        => Assert.Null(Sung(Book("", Words)).Singer);

    [Fact]
    public void ATrackMayStillBeCalledSinger()
    {
        var block = Assert.Single(Parsed(Book("", "lyrics singer { la la la la | }", "singer sings singer")).GetRoot().DescendantNodes<LyricsBlockSyntax>());
        Assert.Equal("singer", block.VoiceName);
    }

    [Fact]
    public void AnEmptySinger_IsAnError()
    {
        var error = Assert.Single(SyntaxTree.Parse(Book("", Words, "singer \"\" sings w")).Diagnostics,
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
    [InlineData("singer \"§\" sings w\nlyrics w { la | }\n", "ja")]                                // nothing written
    [InlineData("language english\nsinger \"§\" sings w\nlyrics w { la | }\n", "en")]              // the file's
    [InlineData("singer \"§\" sings w\nlyrics w language korean { la | }\n", "ko")]                // the words'
    [InlineData("singer \"neu§\" sings w on m\nlyrics w language english { la | }\n", "en")]
    [InlineData("language korean\nsinger \"§\"\n", "ko")]                                          // no words yet
    [InlineData("singer \"§\" sings\nlyrics language english { la | }\n", "en")]                   // the unnamed words
    public void TheSlot_SaysTheLanguageOfTheWords(string source, string expected)
        => Assert.Equal(expected, SlotLanguage(source));

    [Theory]
    [InlineData("singer \"§\"\nlyrics a { la | }\nlyrics b language english { la | }\n", true)]       // nothing named, languages differ
    [InlineData("singer \"§\" sings a\nlyrics a { la | }\nlyrics b language english { la | }\n", false)]  // the words are named
    [InlineData("singer \"§\"\nlyrics a { la | }\nlyrics b { la | }\n", false)]                       // one language
    public void EveryVoiceIsOffered_WhereTheFilesLyricsDiffer(string source, bool any)
    {
        int at = source.IndexOf('§');
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new Uri("file:///any.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = source.Remove(at, 1), Version = 1, LanguageId = "lilysharp" },
        });
        Assert.Equal(any, server.GetSingerSlot(new SingerSlotParams { TextDocument = new TextDocumentIdentifier { Uri = uri }, Offset = at }).AnyLanguage);
    }

    [Theory]
    [InlineData("lyrics w { singer \"§\" | }\n")]            // a syllable of the body
    [InlineData("part m { singer \"§\" }\n")]                // a line of its own, at the top level
    [InlineData("score { singer \"§\" }\n")]
    [InlineData("title \"§\"\n")]
    public void ElsewhereIsNoSlot(string source)
        => Assert.Null(SlotLanguage(source));

    // ── the rest of the line ──

    private const string Declared = "part sop { clef treble  instrument soprano }\n";

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
    [InlineData("singer male ", "sings")]
    [InlineData("singer male si", "sings")]
    [InlineData("singer \"a/b\" ", "sings")]
    [InlineData("singer female \"a/b\" ", "sings")]
    [InlineData("lyrics words { la | }\nsinger \"a/b\" sings ", "words")]
    [InlineData("singer \"a/b\" sings ", "on")]                      // the unnamed words
    [InlineData("singer \"a/b\" sings words ", "on")]
    [InlineData("singer \"a/b\" sings words on ", "sop")]
    [InlineData("singer sings words on ", "sop")]
    [InlineData("singer \"a/b\" sings on ", "sop")]
    public void TheLine_OffersWhatComesNext(string line, string offered)
        => Assert.Contains(offered, Offered(Declared + line));

    /// <summary>The items offered where <paramref name="text"/> has a <c>▮</c> (else at its end).</summary>
    private static CompletionItem[] ItemsAt(string text)
    {
        int at = text.IndexOf('▮');
        if (at >= 0)
            text = text.Remove(at, 1);
        else
            at = text.Length;
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new Uri("file:///head.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, Version = 1, LanguageId = "lilysharp" },
        });
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, at);
        return server.Completion(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(line, character),
        })?.Items ?? [];
    }

    // Owner's request 2026-11-11: the words taken after `sings` go on to `on` and the list of
    // parts, so the line is written from lists — the voice, the words, the part.
    [Fact]
    public void TheWordsTaken_GoOnToOn_AndTheListOfParts()
    {
        const string File = Declared + "lyrics words { la | }\n";
        var items = ItemsAt(File + "singer \"a/b\" sings ");
        var words = Assert.Single(items, i => i.Label == "words");
        Assert.Equal("words on ", words.InsertText);
        Assert.Equal("editor.action.triggerSuggest", words.Command!.CommandIdentifier);
        // The words made from the notes are sung on a part too.
        Assert.All(items.Where(i => i.Label != "on"), i =>
        {
            Assert.Equal(i.Label + " on ", i.InsertText);
            Assert.NotNull(i.Command);
        });
        // What the item writes is where the parts are listed.
        Assert.Contains("sop", Offered(File + "singer \"a/b\" sings words on "));

        // A name half typed is replaced by the same item.
        Assert.Equal("words on ", Assert.Single(ItemsAt(File + "singer \"a/b\" sings wo"), i => i.Label == "words").InsertText);

        // The line has its part already: the words alone are changed.
        var kept = Assert.Single(ItemsAt(File + "singer \"a/b\" sings ▮ on sop\n"), i => i.Label == "words");
        Assert.Null(kept.InsertText);
        Assert.Null(kept.Command);
        // …and a comment after the caret is not a part.
        Assert.Equal("words on ", Assert.Single(ItemsAt(File + "singer \"a/b\" sings ▮  // who\n"), i => i.Label == "words").InsertText);

        // Where a part names its own words (`part p { lyrics words }`), no `on` follows.
        Assert.All(ItemsAt(File + "part alt { lyrics ").Where(i => i.Label == "words"), i => Assert.Null(i.InsertText));
    }

    [Fact]
    public void TheFile_OffersTheLine()
    {
        var item = Assert.Single(LilySharpLanguageServer.GetTopLevelCompletions().Items, i => i.Label == "singer");
        Assert.Equal("singer $0", item.InsertText);
        Assert.Equal(InsertTextFormat.Snippet, item.InsertTextFormat);
        Assert.Equal("editor.action.triggerSuggest", item.Command!.CommandIdentifier);
    }

    [Fact]
    public void AScore_OffersTheLine_AtTheHeadOfALine()
    {
        Assert.Contains("singer", Offered(Declared + "score {\n  "));
        Assert.DoesNotContain("singer", Offered(Declared + "score {\n  staff sop "));
    }

    [Theory]
    [InlineData("score {\n  singer male ", "sings")]
    [InlineData("score {\n  singer \"a/b\" sings words on ", "sop")]
    public void AScoresLine_OffersWhatComesNext(string line, string offered)
        => Assert.Contains(offered, Offered(Declared + line));

    [Fact]
    public void AnUnnamedRow_TakesOn()
    {
        var row = Assert.Single(SyntaxTree.Parse("score { staff m  lyrics on m }").GetRoot().DescendantNodes<LyricsRowRenderSyntax>());
        Assert.Null(row.NameToken);
        Assert.Equal("m", row.SingsTarget);
    }

    [Theory]
    [InlineData("score { lyrics words ", new[] { "on" }, new[] { "singer", "sings" })]
    [InlineData("score { lyrics words on sop ", new string[0], new[] { "on", "singer", "sings" })]
    [InlineData("score { grandStaff { staff sop  lyrics words ", new[] { "on" }, new[] { "singer", "sings" })]
    public void AScoresRow_OffersOn_AndNoVoice(string row, string[] offered, string[] gone)
    {
        var labels = Offered(Declared + row);
        Assert.All(offered, l => Assert.Contains(l, labels));
        Assert.All(gone, l => Assert.DoesNotContain(l, labels));
    }

    [Theory]
    [InlineData("score { lyrics words on ")]
    [InlineData("score { grandStaff { staff sop  lyrics words on ")]
    public void AnOn_TakesThePart(string head)
        => Assert.Contains("sop", Offered(Declared + head));

    [Theory]
    [InlineData("lyrics words language english ")]
    public void ADefinitionsHead_OffersNoVoiceAndNoPart(string head)
    {
        var labels = Offered(Declared + head);
        Assert.All(new[] { "singer", "sings", "on", "language" }, l => Assert.DoesNotContain(l, labels));
    }

    [Theory]
    [InlineData("lyrics words { la ")]                       // the words themselves
    [InlineData("lyrics { lyrics on sop ")]                  // a syllable that reads like a head
    [InlineData("title \"the singer \"a\" sings w ")]        // a string
    [InlineData("// singer \"a\" sings w ")]                 // a comment
    public void TheBodyAndOtherText_AreNoLine(string text)
    {
        var labels = Offered(Declared + text);
        Assert.DoesNotContain("on", labels);
        Assert.DoesNotContain("sings", labels);
    }

    // ── singer male "…" (owner's decision 2026-11-09, session 1173) ──

    [Theory]
    [InlineData("singer male \"neutrino/SOMA\" sings w", Words, "staff m  lyrics w", "neutrino/SOMA")]
    [InlineData("singer female \"neutrino/REINA\" sings w on m", Words, "staff m  lyrics w", "neutrino/REINA")]
    [InlineData("singer male \"neutrino/SOMA\" sings", "lyrics { さ く ら ら | }", "staff m  lyrics", "neutrino/SOMA")]
    public void AGender_MayStandBeforeTheName(string singer, string lyrics, string row, string expected)
    {
        string source = Sheet(lyrics, singer, row);
        Parsed(source);
        Assert.Equal(expected, Sung(source).Singer);
    }

    [Theory]
    [InlineData("singer man \"x\" sings w", DiagnosticCodes.SingerGenderUnknown)]
    [InlineData("singer Male \"x\" sings w", DiagnosticCodes.SingerGenderUnknown)]
    [InlineData("singer \"\" sings w", DiagnosticCodes.SingerEmpty)]
    public void ABadGenderOrAnEmptyName_IsAnError(string singer, string code)
    {
        var error = Assert.Single(SyntaxTree.Parse(Book("", Words, singer)).Diagnostics, d => d.Severity == LilySharp.Core.Syntax.DiagnosticSeverity.Error);
        Assert.Equal(code, error.Code);
    }

    private static string? SlotGender(string source)
    {
        int at = source.IndexOf('§');
        return LilySharpLanguageServer.SingerSlotAt(source.Remove(at, 1), at)?.Gender;
    }

    [Theory]
    [InlineData("singer male \"§\"\n", "male")]
    [InlineData("singer female \"neu§\" sings w\n", "female")]
    [InlineData("singer male \"§\" sings w on m\n", "male")]
    [InlineData("singer \"§\" sings w\n", null)]
    public void TheSlot_SaysTheGender(string source, string? expected)
        => Assert.Equal(expected, SlotGender(source));

    // ── before any quote (owner's report 2026-11-09, session 1196) ──

    private static (string? Track, string? Part, string? Language, string? Gender, bool Quoted)? Slot(string source)
    {
        int at = source.IndexOf('§');
        return LilySharpLanguageServer.SingerSlotAt(source.Remove(at, 1), at);
    }

    [Theory]
    [InlineData("singer §\n")]
    [InlineData("singer neu§\n")]
    [InlineData("singer § sings w\n")]
    public void BeforeAnyQuote_IsTheSlot_NotQuoted(string source)
    {
        var slot = Assert.NotNull(Slot(source));
        Assert.False(slot.Quoted);
    }

    /// <summary>One voice a line: after a comma there is no second name to offer.</summary>
    [Theory]
    [InlineData("singer \"a\",§\n")]
    [InlineData("singer \"a\", §\n")]
    [InlineData("singer \"a\",\"§\n")]
    public void AfterACommaThereIsNoSlot(string source) => Assert.Null(Slot(source));

    /// <summary>A score takes a singer line too; a part's block does not.</summary>
    [Theory]
    [InlineData("score {\n  singer §\n}\n", true)]
    [InlineData("score {\n  singer male \"§\"\n}\n", true)]
    [InlineData("part m {\n  singer §\n}\n", false)]
    public void ASingerSlotStandsInAScore(string source, bool slot) => Assert.Equal(slot, Slot(source) != null);

    [Fact]
    public void BetweenTheQuotes_IsQuoted() => Assert.True(Assert.NotNull(Slot("singer \"§\" sings w\n")).Quoted);

    [Theory]
    [InlineData("// singer §\n")]
    [InlineData("part m { title \"a singer §\n")]
    [InlineData("part m { singer §\n")]
    [InlineData("score { singer §\n")]
    [InlineData("singer \"x\" sings §\n")]
    [InlineData("lyrics w { section A { singer §\n")]
    public void NotASlot(string source) => Assert.Null(Slot(source));

    [Fact]
    public void BeforeAnyQuote_NoOtherWordsAreOffered() => Assert.Empty(Offered(Declared + "singer "));

    // ── one track under several staves, a singer each (owner's report, session 1176) ──

    private static string Choir(string singers, string rows) => $$"""
        part sop { clef treble  instrument soprano }
        part alt { clef treble  instrument soprano }
        section A {
          sop { c'4 d e f | }
          alt { a4 b c d | }
          lyrics words { さ く ら ら | }
        }
        {{singers}}
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

    private const string BothPrinted = "staff sop  lyrics words  staff alt  lyrics words";

    [Fact]
    public void TwoLinesOfOneTrack_SingWithTheirOwnSingers()
    {
        var singers = SingersByPart(Choir("singer \"a/A\" sings words on sop\nsinger \"b/B\" sings words on alt", BothPrinted));
        Assert.Equal("a/A", singers["sop"]);
        Assert.Equal("b/B", singers["alt"]);
    }

    [Fact]
    public void ALinesSinger_IsNotAnotherLinesOfTheSameTrack()
    {
        var singers = SingersByPart(Choir("singer \"a/A\" sings words on sop\nsinger sings words on alt", BothPrinted));
        Assert.Equal("a/A", singers["sop"]);
        Assert.Null(singers["alt"]);
    }

    [Fact]
    public void ALineNamingNoPart_FollowsWhereTheWordsArePrinted()
        => Assert.Equal("a/A", SingersByPart(Choir("singer \"a/A\" sings words", "staff sop  lyrics words  staff alt"))["sop"]);

    // ── which default voice sings a part (owner's decision 2026-11-09, session 1187) ──

    private static string? GenderOf(string partHead, string? singer = null)
    {
        string voice = singer == null ? "" : $" {singer}";
        return Sung($$"""
            part m { {{partHead}} }
            section A {
              m { c'4 d e f | }
              lyrics w { さ く ら ら | }
            }
            singer{{voice}} sings w
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
    public void APart_TellsItsDefaultVoice(string head, string expected)
        => Assert.Equal(expected, GenderOf(head));

    [Theory]
    [InlineData("clef bass", "female", "female")]
    [InlineData("clef treble", "male", "male")]
    [InlineData("clef bass", "\"neutrino/MERROW\"", "male")]              // a named voice says nothing: the part does
    [InlineData("clef treble", "\"a/b\"", "female")]
    [InlineData("clef treble^8", "\"a/b\"", "female")]
    [InlineData("clef treble_8", "\"a/b\"", "male")]
    [InlineData("clef tenor", "\"a/b\"", "male")]
    [InlineData("instrument soprano", "\"someone\"", "female")]
    public void TheSingersWord_WinsOverThePart(string head, string singer, string expected)
        => Assert.Equal(expected, GenderOf(head, singer));

    // ── a gender with no name: the engine picks such a voice (session 1175) ──

    [Theory]
    [InlineData("singer male sings w", Words, "staff m  lyrics w", "male")]
    [InlineData("singer female sings w on m", Words, "staff m  lyrics w", "female")]
    [InlineData("singer male \"\" sings w", Words, "staff m  lyrics w", "male")]       // what taking `singer male` and leaving the list writes
    [InlineData("singer male sings", "lyrics { さ く ら ら | }", "staff m  lyrics", "male")]
    public void AGenderAlone_IsTheSinger(string singer, string lyrics, string row, string expected)
    {
        string source = Sheet(lyrics, singer, row);
        Parsed(source);
        Assert.Equal(expected, Sung(source).Singer);
    }

    [Fact]
    public void TheNames_AreTheLowerCaseVocabulary()
    {
        Assert.Equal(new[] { "japanese", "english", "chinese", "korean" }, LyricLanguage.Names);
        Assert.Equal("ja", LyricLanguage.CodeOf("japanese"));
        Assert.Null(LyricLanguage.CodeOf("Japanese"));
    }
}
