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

using LilySharp.Core.Editing;
using LilySharp.Core.Midi;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A named singer and the staff of what that singer sings (owner's decisions 2026-11-12,
/// docs/lyrics-singer-design.md §13): <c>singer carol female sings on lead up 3 in Chorus</c>
/// and <c>score { singer carol }</c> — the harmony's notes are the singer line's to know, and
/// the score prints them, with the words, where it says so.
/// </summary>
[Trait("Category", "Unit")]
public class SingerPartsTests
{
    /// <summary>Verse e e d c, Chorus g g a g; a syllable a note.</summary>
    private static string Book(string singers, string scores, string key = "key c major", string mode = "octave relative",
        string chorus = "g'4 g a g |") => $$"""
        {{mode}}
        time 4/4
        {{key}}
        language english
        part lead { instrument alto  lyrics words }
        section Verse {
          lead { e'4 e d c | }
          lyrics words { one two three four | }
        }
        section Chorus {
          lead { {{chorus}} }
          lyrics words { la la la la | }
        }
        form { Verse Chorus }
        {{singers}}
        {{scores}}
        """;

    private const string Band = """
        singer alice female sings on lead in Verse
        singer bob male sings on lead octave -1 in Chorus
        singer carol female sings on lead up 3 in Chorus
        """;

    private static (string Text, SyntaxTree Tree, IReadOnlyList<Diagnostic> Said) Expanded(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        string text = SingerParts.Expand(source, tree, out var said);
        return (text, ReferenceEquals(text, source) ? tree : SyntaxTree.Parse(text), said);
    }

    private static string Made(string source)
    {
        var (text, _, said) = Expanded(source);
        Assert.Empty(said.Select(d => d.Message));
        return text[SingerParts.MadeFrom(text)..];
    }

    private static MidiFile Midi(SyntaxTree tree, string? score = null)
    {
        var spec = RenderSpecParser.Choose(RenderSpecParser.FindAll(tree), score);
        return new MidiExporter { Form = spec?.Form, Score = spec }.Export(tree);
    }

    private static int[] PitchesOf(MidiFile midi, string part)
        => [.. midi.Tracks.SelectMany(t => t.Notes).Where(n => n.Part == part).OrderBy(n => n.StartTick).Select(n => n.Pitch)];

    [Fact]
    public void ASingerLine_HasTheSingersOwnNameBeforeTheGender()
    {
        var tree = SyntaxTree.Parse(Book("singer carol female \"x/y\" sings on lead up 3 in Chorus\nsinger carol sings on lead in Verse\nsinger male sings on lead", ""));
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        var lines = tree.GetRoot().DescendantNodes<SingerDeclarationSyntax>().ToList();
        Assert.Equal(["carol", "carol", null], lines.Select(l => l.PersonToken?.Text));
        Assert.Equal(["female", null, "male"], lines.Select(l => l.GenderToken?.Text));
        Assert.Equal(["x/y", null, "male"], lines.Select(l => l.Singer));
        // Lines of one name are one singer: the voice said on one is the other's.
        Assert.Equal(["x/y", "x/y", "male"], LilySharp.Core.Music.LyricSinger.LinesOf(tree.GetRoot()).Select(l => l.Singer));
    }

    [Fact]
    public void TheStaffOfASinger_IsTheLinesNotesMovedAlongTheKey_AndRestsElsewhere()
    {
        string made = Made(Book(Band, "score { staff lead  lyrics words  singer carol }"));
        Assert.Contains("part carol \"Carol\" { instrument alto lyrics __words_carol", made);
        Assert.Contains("section Verse { R*1 }", made);
        Assert.Contains("section Chorus { b'4 b c b | }", made);
        Assert.Contains("lyrics __words_carol {", made);
        Assert.Contains("section Chorus { la la la la | }", made);
        // Only the singers a score prints are made.
        Assert.DoesNotContain("part bob", made);
        Assert.DoesNotContain("part alice", made);
    }

    [Fact]
    public void TheNotesLandAThirdAbove_InEitherOctaveMode()
    {
        foreach (string mode in new[] { "octave relative", "octave absolute" })
        {
            var (_, tree, said) = Expanded(Book(Band, "score { staff lead  singer carol }", mode: mode,
                chorus: mode == "octave relative" ? "g'4 b c a |" : "g'4 b' c'' a' |"));
            Assert.Empty(said.Select(d => d.Message));
            Assert.Empty(SingerParts.OwnOf(SemanticValidation.Run(tree), tree.Text).Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message));
            // g b c' a → b d' e' c'
            Assert.Equal([71, 74, 76, 72], PitchesOf(Midi(tree), "carol"));
        }
    }

    [Fact]
    public void TheKeysOwnNoteIsLandedOn_AndAnAlteredNoteIsReadByItsLetter()
    {
        // G major: over d a third above is f-sharp; over c-sharp (letter c) it is e.
        var (text, tree, said) = Expanded(Book(Band, "score { singer carol }", key: "key g major", chorus: "d'4 cis d b |"));
        Assert.Empty(said.Select(d => d.Message));
        Assert.Contains("section Chorus { fis'4 e fis d | }", text);
        Assert.Equal([66, 64, 66, 62], PitchesOf(Midi(tree), "carol"));
    }

    [Fact]
    public void AnOctaveDown_IsWrittenInTheTrebleClefAnOctaveDown()
    {
        var (text, tree, said) = Expanded(Book(Band, "score { singer bob }"));
        Assert.Empty(said.Select(d => d.Message));
        Assert.Contains("part bob \"Bob\" { instrument alto clef treble_8 lyrics __words_bob", text);
        Assert.Equal([55, 55, 57, 55], PitchesOf(Midi(tree), "bob"));
    }

    [Fact]
    public void SeveralLinesOfOneSinger_AreOneStaff()
    {
        string made = Made(Book("singer alice female sings on lead in Verse\nsinger alice sings on lead up 3 in Chorus", "score { singer alice }"));
        Assert.Contains("section Verse { e'4 e d c | }", made);
        Assert.Contains("section Chorus { b'4 b c b | }", made);
        Assert.Contains("section Verse { one two three four | }", made);
    }

    [Fact]
    public void AScoreThatPrintsASinger_SingsWhatItShows_AndOneThatDoesNotSingsTheLine()
    {
        var (_, tree, _) = Expanded(Book(Band, "score { staff lead  lyrics words  singer carol }\nscore plain { staff lead  lyrics words }"));
        List<SungVoice> Sung(string? score)
        {
            var midi = Midi(tree, score);
            return SungVoices.Read(midi, MidiClock.SecondsOf(midi));
        }

        var printed = Sung(null);
        Assert.Equal(["lead", "lead", "carol"], printed.Select(v => v.Part).OrderBy(p => p == "carol"));
        var carol = Assert.Single(printed, v => v.Part == "carol");
        Assert.Equal("female", carol.Singer);
        Assert.Equal([71.0, 71, 72, 71], carol.Notes.Select(n => n.Pitch));
        Assert.Equal(["la", "la", "la", "la"], carol.Notes.Select(n => n.Lyric));

        // The score with no such staff sings the line on the lead's notes, as before.
        var plain = Sung("plain");
        Assert.All(plain, v => Assert.Equal("lead", v.Part));
        Assert.Contains(plain, v => v.Notes.Select(n => n.Pitch).SequenceEqual([71.0, 71, 72, 71]));
    }

    [Theory]
    [InlineData("score { singer dave }", "No singer is called dave")]
    [InlineData("part carol { instrument alto }\nscore { singer carol }", "is a part and a singer")]
    public void WhatCannotBeMade_IsSaidAtTheScoresItem(string scores, string says)
    {
        string source = Book(Band, scores);
        var (text, _, said) = Expanded(source);
        var problem = Assert.Single(said);
        Assert.Equal(DiagnosticCodes.SingerNotPrinted, problem.Code);
        Assert.Contains(says, problem.Message);
        Assert.Equal("singer " + (scores.Contains("dave") ? "dave" : "carol"), source.Substring(problem.Span.Start, problem.Span.Length));
        Assert.Same(source, text);
    }

    [Fact]
    public void AFileWithAPartCalledSinger_KeepsItsTwoMidiOnlyParts()
    {
        var tree = SyntaxTree.Parse("""
            time 4/4
            part singer { instrument alto }
            part click { instrument alto }
            section A { singer { c'1 | }  click { c'1 | } }
            form { A }
            score { singer click }
            """);
        Assert.False(tree.HasErrors);
        Assert.False(SingerParts.Wanted(tree));
        Assert.Equal(["singer", "click"], RenderSpecParser.FindAll(tree)[0].MidiOnlyParts);
    }

    [Fact]
    public void AFileThatPrintsNoSinger_IsLeftAsItIs()
    {
        string source = Book(Band, "score { staff lead  lyrics words }");
        var tree = SyntaxTree.Parse(source);
        Assert.False(SingerParts.Wanted(tree));
        Assert.Same(source, SingerParts.Expand(source, tree, out _));
    }
}
