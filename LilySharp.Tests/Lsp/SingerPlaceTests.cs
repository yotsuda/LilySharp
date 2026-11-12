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
using LilySharp.Core.Midi;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;
using DiagnosticSeverity = LilySharp.Core.Syntax.DiagnosticSeverity;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// What of its part a singer line sings (owner's decisions 2026-11-12, docs/lyrics-singer-design.md
/// §12): <c>up N</c> / <c>down N</c> an interval in the key, <c>octave N</c>, <c>in SECTION …</c>
/// the sections — "this section A sings, this one B, and here C comes in a third above", as a
/// band says it. Lily# moves the notes, so every engine sings it alike.
/// </summary>
[Trait("Category", "Unit")]
public class SingerPlaceTests
{
    /// <summary>C major: Verse c d e f, Chorus g a b c'; one syllable a note.</summary>
    private static string Book(string singers, string key = "key c major", string chorus = "g'4 a b c |") => $$"""
        tempo 4 = 120
        time 4/4
        {{key}}
        octave relative
        part lead { clef treble  instrument soprano }
        section Verse {
          lead { c'4 d e f | }
          lyrics words { ら ら ら ら | }
        }
        section Chorus {
          lead { {{chorus}} }
          lyrics words { な な な な | }
        }
        {{singers}}
        form { Verse Chorus Verse }
        score { staff lead  lyrics words }
        """;

    private static SungVoiceDto[] Sung(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        Assert.Empty(SemanticValidation.Run(tree).Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message));
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new Uri("file:///place.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = source, Version = 1, LanguageId = "lilysharp" },
        });
        var sung = server.GetSung(new SungParams { TextDocument = new TextDocumentIdentifier { Uri = uri } });
        Assert.Null(sung.Error);
        return sung.Voices!;
    }

    private static double[] Pitches(SungVoiceDto voice) => [.. voice.Notes.Select(n => n.P)];

    private const int C4 = 60;

    [Fact]
    public void WithNoneOfThem_TheLineSingsItsPartAsBefore()
    {
        var voice = Assert.Single(Sung(Book("singer female sings words on lead")));
        Assert.Equal(12, voice.Notes.Length);
        Assert.Equal([60, 62, 64, 65, 67, 69, 71, 72, 60, 62, 64, 65], Pitches(voice));
        Assert.Null(voice.Sing);
    }

    [Fact]
    public void In_NamesTheSectionsTheVoiceSings_EveryTimeTheyArePlayed()
    {
        var voices = Sung(Book(
            "singer \"neutrino/A\" sings words on lead in Verse\nsinger \"neutrino/B\" sings words on lead in Chorus"));
        Assert.Equal(2, voices.Length);
        var a = voices.Single(v => v.Singer == "neutrino/A");
        var b = voices.Single(v => v.Singer == "neutrino/B");
        // A: both Verses; B: the Chorus between them.
        Assert.Equal([60, 62, 64, 65, 60, 62, 64, 65], Pitches(a));
        Assert.Equal([67, 69, 71, 72], Pitches(b));
        Assert.Equal(["ら", "ら", "ら", "ら", "ら", "ら", "ら", "ら"], a.Notes.Select(n => n.L));
        Assert.Equal(["な", "な", "な", "な"], b.Notes.Select(n => n.L));
        // A leaves after the first Verse: its last note there ends a phrase, and it comes back
        // where the second Verse is played.
        Assert.True(a.Notes[3].B);
        Assert.Equal(4.0, a.Notes[4].T, 6);
        Assert.Equal(2.0, b.Notes[0].T, 6);
        // The place is no setting of the voice.
        Assert.Null(a.Sing);
        Assert.Null(b.Sing);
    }

    [Fact]
    public void SeveralSections_AreNamedAfterOneIn()
    {
        var voice = Assert.Single(Sung(Book("singer female sings words on lead in Chorus Verse")));
        Assert.Equal(12, voice.Notes.Length);
        var only = Assert.Single(Sung(Book("singer female sings words on lead in Chorus")));
        Assert.Equal([67, 69, 71, 72], Pitches(only));
    }

    [Fact]
    public void AThirdAbove_IsInTheKey_AndComesInWhereItIsAsked()
    {
        var voices = Sung(Book(
            "singer \"neutrino/A\" sings words on lead\nsinger \"neutrino/B\" sings words on lead up 3 in Chorus"));
        var lead = voices.Single(v => v.Singer == "neutrino/A");
        var harmony = voices.Single(v => v.Singer == "neutrino/B");
        Assert.Equal(12, lead.Notes.Length);
        // g a b c' → b c' d' e': a major third, two minor thirds, a major third — the scale's own.
        Assert.Equal([71, 72, 74, 76], Pitches(harmony));
        // On the lead's beats, with its words.
        Assert.Equal(lead.Notes.Skip(4).Take(4).Select(n => n.T), harmony.Notes.Select(n => n.T));
        Assert.Equal(["な", "な", "な", "な"], harmony.Notes.Select(n => n.L));
        Assert.Equal("lead", lead.Key);
        Assert.Equal("lead#2", harmony.Key);
    }

    [Theory]
    [InlineData("up 3", new[] { 64, 65, 67, 69 })]           // c d e f → e f g a
    [InlineData("down 3", new[] { 57, 59, 60, 62 })]         // → a, b, c d
    [InlineData("up 6", new[] { 69, 71, 72, 74 })]
    [InlineData("down 6", new[] { 52, 53, 55, 57 })]
    [InlineData("up 8", new[] { 72, 74, 76, 77 })]           // an octave, by the scale
    [InlineData("up 2", new[] { 62, 64, 65, 67 })]
    [InlineData("octave -1", new[] { 48, 50, 52, 53 })]
    [InlineData("octave 1", new[] { 72, 74, 76, 77 })]
    [InlineData("up 3 octave -1", new[] { 52, 53, 55, 57 })] // a sixth below, said the other way
    public void TheIntervals(string place, int[] verse)
        => Assert.Equal(verse.Select(p => (double)p), Pitches(Assert.Single(Sung(Book($"singer female sings words on lead {place} in Verse")))).Take(4));

    [Fact]
    public void TheKeyInForce_IsTheScaleSteppedAlong()
    {
        // G major: c d e f♯... the Verse's c d e f, a third up, lands on e f♯ g a.
        var g = Assert.Single(Sung(Book("singer female sings words on lead up 3 in Verse", key: "key g major")));
        Assert.Equal([64, 66, 67, 69], Pitches(g).Take(4));
        // A minor is C major's scale.
        var a = Assert.Single(Sung(Book("singer female sings words on lead up 3 in Verse", key: "key a minor")));
        Assert.Equal([64, 65, 67, 69], Pitches(a).Take(4));
        // A key written inside the music takes over from its note.
        var changed = Assert.Single(Sung(Book("singer female sings words on lead up 3 in Chorus",
            chorus: "g'4 a key g major b c |")));
        Assert.Equal([71, 72, 74, 76], Pitches(changed));       // b c' | d' e' (G major: c' → e')
    }

    [Theory]
    [InlineData(60, 3, 0, 64)]      // c → e in C
    [InlineData(61, 3, 0, 64)]      // c♯, a raised first → e (the chord of A)
    [InlineData(66, 3, 0, 69)]      // f♯, a raised fourth → a
    [InlineData(68, 3, 0, 71)]      // g♯, a raised fifth - a minor's leading note → b
    [InlineData(70, 3, 0, 74)]      // b♭, a lowered seventh → d'
    [InlineData(63, 3, 0, 67)]      // e♭, a lowered third → g
    [InlineData(70, -3, 0, 67)]     // b♭ → g below
    [InlineData(61, 8, 0, 73)]      // an octave moves the note as it is
    [InlineData(61, -8, 0, 49)]
    [InlineData(59, 3, 0, 62)]      // b → d'
    [InlineData(60, -3, 0, 57)]
    [InlineData(60, 1, 0, 60)]
    [InlineData(60, 0, 0, 60)]
    [InlineData(62, 3, 2, 66)]      // d → f♯ in D
    [InlineData(60, 10, 0, 76)]     // a tenth
    public void AStepAlongTheScale(int pitch, int interval, int tonic, int expected)
        => Assert.Equal(expected, SungVoices.Step(pitch, interval, tonic));

    [Fact]
    public void AQuarterToneKeepsItsBend()
        => Assert.Equal(64.5, SungVoices.Step(60.5, 3, 0));

    [Fact]
    public void AVoiceWhoseSectionsAreNotPlayed_SingsNothing()
    {
        string source = Book("singer \"neutrino/A\" sings words on lead\nsinger \"neutrino/B\" sings words on lead in Chorus")
            .Replace("form { Verse Chorus Verse }", "form { Verse }");
        Assert.Equal("neutrino/A", Assert.Single(Sung(source)).Singer);
    }

    [Fact]
    public void TheLineReadsBack_AndItsWordsAreItsOwn()
    {
        string source = Book("singer female \"neutrino/B\" sings words on lead down 6 octave 1 in Chorus Verse with breathiness 3");
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        Assert.Equal(source, tree.GetRoot().ToFullString());
        var line = Assert.Single(tree.GetRoot().DescendantNodes<SingerDeclarationSyntax>());
        Assert.Equal(-6, line.Interval);
        Assert.Equal(1, line.Octaves);
        Assert.Equal(["Chorus", "Verse"], line.Sections.Select(s => s.Text));
        Assert.Equal("words", line.TrackName);
        Assert.Equal("lead", line.OnToken!.Text);
        Assert.Equal("breathiness", Assert.Single(line.Settings).Name);
        // Without a name for the words, and with no part written.
        var bare = Assert.Single(SyntaxTree.Parse("singer sings up 3 in Chorus\n").GetRoot().DescendantNodes<SingerDeclarationSyntax>());
        Assert.Null(bare.TrackToken);
        Assert.Equal(3, bare.Interval);
        Assert.Equal("Chorus", Assert.Single(bare.Sections).Text);
        // The words are still names where no number or name follows them.
        var named = Assert.Single(SyntaxTree.Parse("singer sings up on in\n").GetRoot().DescendantNodes<SingerDeclarationSyntax>());
        Assert.Equal("up", named.TrackName);
        Assert.Equal("in", named.OnToken!.Text);
        Assert.Equal(0, named.Interval);
        Assert.Empty(named.Sections);
        // The settings carry the sections' end.
        var voice = Assert.Single(Sung(source));
        Assert.Equal(3.0, Convert.ToDouble(Assert.Single(voice.Sing!).Value));
    }

    [Theory]
    [InlineData("singer female sings words on lead in Nowhere", "No section is named 'Nowhere'")]
    [InlineData("singer female sings words on lead up 1", "no interval")]
    [InlineData("singer female sings words on lead down 16", "no interval")]
    [InlineData("singer female sings words on lead octave 4", "one to three octaves")]
    [InlineData("singer female sings words on lead octave 0", "one to three octaves")]
    public void WhatCannotBeSung_IsAnError(string line, string says)
    {
        var d = Assert.Single(SemanticValidation.Run(SyntaxTree.Parse(Book(line))), d => d.Code == DiagnosticCodes.SingerPlaceMalformed);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Contains(says, d.Message);
    }

    [Fact]
    public void TheSameVoiceInOtherSections_OrAtAnotherInterval_IsNotARepeat()
    {
        var apart = SemanticValidation.Run(SyntaxTree.Parse(Book(
            "singer \"neutrino/A\" sings words on lead in Verse\nsinger \"neutrino/A\" sings words on lead up 3 in Chorus")));
        Assert.DoesNotContain(apart, d => d.Code == DiagnosticCodes.SingerRepeated);
        var same = SemanticValidation.Run(SyntaxTree.Parse(Book(
            "singer \"neutrino/A\" sings words on lead in Verse\nsinger \"neutrino/A\" sings words on lead in Verse")));
        Assert.Contains(same, d => d.Code == DiagnosticCodes.SingerRepeated);
    }
}
