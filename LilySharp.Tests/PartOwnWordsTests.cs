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

using LilySharp.Core.Midi;
using LilySharp.Core.Music;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>A part naming its own words, <c>part sop { lyrics words }</c>
/// (docs/lyrics-singer-design.md §10).</summary>
public class PartOwnWordsTests
{
    private const string Music = """
        time 4/4
        section A { sop { c'2 d | }  alt { e'4 e e e | } }
        lyrics words { あ い | }
        lyrics mw { か き く け | }
        form { A }

        """;

    private const string Parts = "part sop { lyrics words }\npart alt { lyrics mw }\n";

    private static IReadOnlyList<Diagnostic> Validate(string src) => SemanticValidation.Run(SyntaxTree.Parse(src));

    private static List<SungVoice> Sung(string src)
    {
        var tree = SyntaxTree.Parse(src);
        var spec = RenderSpecParser.Choose(RenderSpecParser.FindAll(tree), null);
        var midi = new MidiExporter { Form = spec?.Form, Score = spec }.Export(tree);
        return SungVoices.Read(midi, MidiClock.SecondsOf(midi));
    }

    private static void AssertNoErrors(string src)
    {
        Assert.False(SyntaxTree.Parse(src).HasErrors);
        Assert.DoesNotContain(Validate(src), d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void TheHeaderStatement_RoundTrips_AndNamesTheTrack()
    {
        var src = Parts + Music + "score { staff sop  lyrics words  staff alt  lyrics mw }\n";
        var tree = SyntaxTree.Parse(src);
        Assert.Equal(src, tree.GetRoot().ToFullString());
        Assert.Equal(["words"], LyricBindings.TracksOfPart(tree.GetRoot(), "sop"));
        Assert.Equal(["mw"], LyricBindings.TracksOfPart(tree.GetRoot(), "alt"));
        AssertNoErrors(src);
    }

    [Fact]
    public void APartMayNameSeveralTracks_OneStatementEach_AndKeepsItsOtherProperties()
    {
        var src = "part sop { clef treble  lyrics words  lyrics mw  instrument soprano }\n" + Music + "score { staff sop  lyrics words }\n";
        var root = SyntaxTree.Parse(src).GetRoot();
        Assert.Equal(["words", "mw"], LyricBindings.TracksOfPart(root, "sop"));
        Assert.Equal("soprano", PartHeaderDefaults.Read(root.DescendantNodes<PartDeclarationSyntax>().Single()).Preset);
        AssertNoErrors(src);
    }

    [Fact]
    public void ASectionAfterTheStatement_IsStillMusic()
    {
        var src = "part sop { lyrics words  section A { c'2 d | } }\nlyrics words { section A { あ い | } }\nform { A }\nscore { staff sop  lyrics words }\n";
        AssertNoErrors(src);
        var sung = Assert.Single(Sung("singer sings on sop\n" + src));
        Assert.Equal(2, sung.Notes.Count);
    }

    [Fact]
    public void ALyricsWithABody_InAHeader_IsStillRefused()
    {
        foreach (var body in new[] { "lyrics words { あ | }", "lyrics { あ | }", "lyrics words language japanese { あ | }" })
            Assert.Contains(Validate($"part sop {{ {body} }}\n" + Music + "score { staff sop }\n"), d => d.Code == DiagnosticCodes.LyricsInsidePart);
    }

    [Fact]
    public void NoName_IsSaid_AndTheHeaderStillCloses()
    {
        var tree = SyntaxTree.Parse("part sop { lyrics }\npart alt { clef treble }\n");
        Assert.Contains(tree.Diagnostics, d => d.Code == DiagnosticCodes.PartPropertyMissingValue);
        Assert.Equal(2, tree.GetRoot().DescendantNodes<PartDeclarationSyntax>().Count());
        Assert.Empty(LyricBindings.TracksOfPart(tree.GetRoot(), "sop"));
    }

    [Fact]
    public void ANameNoBlockDefines_IsUndefined_AndSaidOnceForASingerLine()
    {
        var ds = Validate("part sop { lyrics nope }\n" + Music + "singer sings on sop\nscore { staff sop }\n");
        Assert.Single(ds, d => d.Code == DiagnosticCodes.UndefinedPart && d.Message.Contains("nope"));
        Assert.DoesNotContain(ds, d => d.Code == DiagnosticCodes.SingerWordsUnknown);
    }

    [Fact]
    public void AGeneratedName_NeedsNoDefinition()
    {
        var src = "part sop { lyrics solfege }\n" + Music + "singer sings on sop\nscore { staff sop }\n";
        AssertNoErrors(src);
        Assert.Equal(GeneratedLyrics.Solfege, Assert.Single(LyricSinger.LinesOf(SyntaxTree.Parse(src).GetRoot())).Track);
    }

    [Fact]
    public void ASingerWithNoName_SingsTheTrackThePartNames()
    {
        var src = Parts + Music + "singer female sings on sop\nsinger male sings on alt\nscore { staff sop  lyrics words  staff alt  lyrics mw }\n";
        AssertNoErrors(src);
        var lines = LyricSinger.LinesOf(SyntaxTree.Parse(src).GetRoot());
        Assert.Equal(["words", "mw"], lines.Select(l => l.Track));

        var voices = Sung(src);
        Assert.Equal(["sop", "alt"], voices.Select(v => v.Part));
        Assert.Equal([2, 4], voices.Select(v => v.Notes.Count));
    }

    [Fact]
    public void ASingerWithNoName_ReadsTheFilesOnlyPartToo()
    {
        var src = "part sop { lyrics words }\nsection A { sop { c'2 d | } }\nlyrics words { あ い | }\nform { A }\nsinger sings\nscore { staff sop  lyrics words }\n";
        AssertNoErrors(src);
        Assert.Equal("words", Assert.Single(LyricSinger.LinesOf(SyntaxTree.Parse(src).GetRoot())).Track);
    }

    [Fact]
    public void ASingerOnAVoice_ReadsItsPartsWords()
    {
        var src = "part sop { lyrics words }\nsection A { sop { voice v1 { c'2 d | } v2 { e'2 f | } } }\nlyrics words { あ い | }\nform { A }\nsinger sings on v1\nscore { staff sop }\n";
        AssertNoErrors(src);
        var line = Assert.Single(LyricSinger.LinesOf(SyntaxTree.Parse(src).GetRoot()));
        Assert.Equal(("words", "sop", "v1"), (line.Track, line.Part, line.Voice));
    }

    [Fact]
    public void ASingerWithNoName_WhereThePartNamesSeveral_IsRefused()
    {
        var ds = Validate("part sop { lyrics words  lyrics mw }\n" + Music + "singer sings on sop\nscore { staff sop }\n");
        Assert.Equal(DiagnosticSeverity.Error, Assert.Single(ds, d => d.Code == DiagnosticCodes.SingerWordsAmbiguous).Severity);
        Assert.DoesNotContain(ds, d => d.Code == DiagnosticCodes.SingerWordsUnknown);
    }

    [Fact]
    public void ASingerWithNoName_WhereThePartNamesNone_IsTheUnnamedTrack()
    {
        var src = "part sop { clef treble }\nsection A { sop { c'2 d | } }\nlyrics { あ い | }\nform { A }\nsinger sings on sop\nscore { staff sop  lyrics }\n";
        AssertNoErrors(src);
        Assert.Equal(SyntaxFacts.UnnamedLyricsName, Assert.Single(LyricSinger.LinesOf(SyntaxTree.Parse(src).GetRoot())).Track);
    }

    [Fact]
    public void ASingerNamingAnUnlistedTrack_IsWarned_NotRefused()
    {
        // The tenor of the owner's report: printing one track, singing another.
        var d = Assert.Single(Validate(Parts + Music + "singer sings words on alt\nscore { staff sop  staff alt }\n"), d => d.Code == DiagnosticCodes.TrackNotListedByPart);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("alt", d.Message);
    }

    [Fact]
    public void ARowOfAnUnlistedTrack_IsWarned_UnderItsStaff_OrWithOn()
    {
        foreach (var score in new[] { "score { staff alt  lyrics words }\n", "score { lyrics words on alt  staff sop }\n" })
            Assert.Single(Validate(Parts + Music + score), d => d.Code == DiagnosticCodes.TrackNotListedByPart && d.Message.Contains("'alt'"));
    }

    [Fact]
    public void AListedTrack_AndAPartThatListsNone_AreNotWarned()
    {
        Assert.DoesNotContain(Validate(Parts + Music + "singer sings mw on alt\nscore { staff sop  lyrics words  staff alt  lyrics mw }\n"), d => d.Code == DiagnosticCodes.TrackNotListedByPart);
        Assert.DoesNotContain(Validate("part sop { lyrics words }\n" + Music + "singer sings words on alt\nscore { staff alt  lyrics words }\n"), d => d.Code == DiagnosticCodes.TrackNotListedByPart);
    }

    [Fact]
    public void AnotherScoreSingingOtherWords_IsSilent_OnceThePartListsBoth()
    {
        var src = "part sop { lyrics words  lyrics mw }\n" + Music + "score a { staff sop  lyrics words  singer sings words on sop }\nscore b { staff sop  lyrics mw  singer sings mw on sop }\n";
        AssertNoErrors(src);
        Assert.DoesNotContain(Validate(src), d => d.Code == DiagnosticCodes.TrackNotListedByPart);
    }

    [Fact]
    public void ARowWithNoStaffAbove_FollowsTheOnePartNamingIt()
    {
        var root = SyntaxTree.Parse(Parts + Music + "score { lyrics words  staff sop  staff alt }\n").GetRoot();
        Assert.Equal("sop", RenderSpecParser.RowTarget(root.DescendantNodes<LyricsRowRenderSyntax>().Single()));
    }

    [Fact]
    public void ARowWithNoStaffAbove_StaysEvenSpread_WhereNoneOrSeveralName()
    {
        foreach (var header in new[] { "", "part sop { lyrics words }\npart alt { lyrics words }\n" })
        {
            var root = SyntaxTree.Parse(header + Music + "score { lyrics words  staff sop }\n").GetRoot();
            Assert.Null(RenderSpecParser.RowTarget(root.DescendantNodes<LyricsRowRenderSyntax>().Single()));
        }
    }

    [Fact]
    public void ARowUnderAStaff_KeepsThatStaff_WhateverThePartsName()
    {
        var root = SyntaxTree.Parse("part sop { lyrics words }\n" + Music + "score { staff alt  lyrics words }\n").GetRoot();
        Assert.Equal("alt", RenderSpecParser.RowTarget(root.DescendantNodes<LyricsRowRenderSyntax>().Single()));
    }

    [Fact]
    public void ARowUnderACondensedStaff_StaysAnError_EvenWhereAPartNamesIt()
    {
        const string header = "part sop { lyrics words }\npart alt { clef treble }\n";
        const string score = "score { condensedStaff { sop alt }  lyrics words }\n";
        Assert.Contains(Validate(header + Music + score), d => d.Code == DiagnosticCodes.LyricsRowNeedsOn);
        var root = SyntaxTree.Parse(header + Music + score).GetRoot();
        Assert.Null(RenderSpecParser.RowTarget(root.DescendantNodes<LyricsRowRenderSyntax>().Single()));
    }

    [Fact]
    public void ASectionAfterTheStatement_IsNotReadAsAWordsBody()
    {
        // A stray character is a syllable's inside a words body and an error in music: the
        // parser's scan of those bodies must not take this brace for one.
        var tree = SyntaxTree.Parse("part sop { lyrics words  section A { c'4? d e f | } }\n");
        Assert.Contains(tree.Diagnostics, d => d.Code == DiagnosticCodes.UnexpectedCharacter);
    }

    [Fact]
    public void AFileWithNoStatement_IsDrawnAsBefore_AndTheStatementChangesNoPage()
    {
        // The statement itself prints nothing: the same page with and without it.
        const string score = "score { staff sop  lyrics words  staff alt  lyrics mw }\n";
        static string Page(string s) => SvgGenerator.Generate(SyntaxTree.Parse(s));
        string bare = "part sop { clef treble }\npart alt { clef treble }\n" + Music + score;
        string own = "part sop { clef treble  lyrics words }\npart alt { clef treble  lyrics mw }\n" + Music + score;
        Assert.Equal(StripPositions(Page(bare)), StripPositions(Page(own)));
    }

    private static string StripPositions(string svg) => System.Text.RegularExpressions.Regex.Replace(svg, @"\sdata-pos=""[^""]*""", "");

    [Fact]
    public void Rename_And_References_SeeTheStatement()
    {
        var root = SyntaxTree.Parse(Parts + Music + "score { staff sop  lyrics words }\n").GetRoot();
        var tracks = LilySharp.Core.Editing.PartReferenceFinder.Tracks(root);
        Assert.Contains(tracks.References, r => r.Token.Text == "words" && r.Token.Parent is PartLyricsDeclarationSyntax);
    }
}
