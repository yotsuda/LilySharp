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
/// and <c>score { staff carol  lyrics carol }</c> — the harmony's notes are the singer line's to know, and
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
        string made = Made(Book(Band, "score { staff lead  lyrics words  staff carol  lyrics carol }"));
        Assert.Contains("part carol__0 \"Carol\" { instrument alto lyrics __words_carol__0", made);
        Assert.Contains("section Verse { R*1 }", made);
        Assert.Contains("section Chorus { b'4 b c b | }", made);
        Assert.Contains("lyrics __words_carol__0 {", made);
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
            var (_, tree, said) = Expanded(Book(Band, "score { staff lead  staff carol  lyrics carol }", mode: mode,
                chorus: mode == "octave relative" ? "g'4 b c a |" : "g'4 b' c'' a' |"));
            Assert.Empty(said.Select(d => d.Message));
            Assert.Empty(SingerParts.OwnOf(SemanticValidation.Run(tree), tree.Text).Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message));
            // g b c' a → b d' e' c'
            Assert.Equal([71, 74, 76, 72], PitchesOf(Midi(tree), "carol__0"));
        }
    }

    [Fact]
    public void TheKeysOwnNoteIsLandedOn_AndAnAlteredNoteIsReadByItsLetter()
    {
        // G major: over d a third above is f-sharp; over c-sharp (letter c) it is e.
        var (text, tree, said) = Expanded(Book(Band, "score { staff carol  lyrics carol }", key: "key g major", chorus: "d'4 cis d b |"));
        Assert.Empty(said.Select(d => d.Message));
        Assert.Contains("section Chorus { fis'4 e fis d | }", text);
        Assert.Equal([66, 64, 66, 62], PitchesOf(Midi(tree), "carol__0"));
    }

    [Fact]
    public void AnOctaveDown_IsWrittenInTheTrebleClefAnOctaveDown()
    {
        var (text, tree, said) = Expanded(Book(Band, "score { staff bob  lyrics bob }"));
        Assert.Empty(said.Select(d => d.Message));
        Assert.Contains("part bob__0 \"Bob\" { instrument alto clef treble_8 lyrics __words_bob__0", text);
        Assert.Equal([55, 55, 57, 55], PitchesOf(Midi(tree), "bob__0"));
    }

    [Fact]
    public void SeveralLinesOfOneSinger_AreOneStaff()
    {
        string made = Made(Book("singer alice female sings on lead in Verse\nsinger alice sings on lead up 3 in Chorus", "score { staff alice  lyrics alice }"));
        Assert.Contains("section Verse { e'4 e d c | }", made);
        Assert.Contains("section Chorus { b'4 b c b | }", made);
        Assert.Contains("section Verse { one two three four | }", made);
    }

    [Fact]
    public void AScoreThatPrintsASinger_SingsWhatItShows_AndOneThatDoesNotSingsTheLine()
    {
        var (_, tree, _) = Expanded(Book(Band, "score { staff lead  lyrics words  staff carol  lyrics carol }\nscore plain { staff lead  lyrics words }"));
        List<SungVoice> Sung(string? score)
        {
            var midi = Midi(tree, score);
            return SungVoices.Read(midi, MidiClock.SecondsOf(midi));
        }

        var printed = Sung(null);
        Assert.Equal(["lead", "lead", "carol__0"], printed.Select(v => v.Part).OrderBy(p => p == "carol__0"));
        var carol = Assert.Single(printed, v => v.Part == "carol__0");
        Assert.Equal("female", carol.Singer);
        Assert.Equal([71.0, 71, 72, 71], carol.Notes.Select(n => n.Pitch));
        Assert.Equal(["la", "la", "la", "la"], carol.Notes.Select(n => n.Lyric));

        // The score with no such staff sings the line on the lead's notes, as before.
        var plain = Sung("plain");
        Assert.All(plain, v => Assert.Equal("lead", v.Part));
        Assert.Contains(plain, v => v.Notes.Select(n => n.Pitch).SequenceEqual([71.0, 71, 72, 71]));
    }

    /// <summary>Owner's decision 2026-11-12: a score prints what it says, of a singer as of a
    /// part — <c>staff carol</c> her notes, <c>lyrics carol</c> her words, either without the other.</summary>
    [Fact]
    public void TheStaffAlone_TheWordsAlone_OrBoth()
    {
        IReadOnlyList<RenderItemSpec> Items(string scores)
        {
            var (_, tree, said) = Expanded(Book(Band, scores));
            Assert.Empty(said.Select(d => d.Message));
            Assert.Empty(SingerParts.OwnOf(SemanticValidation.Run(tree), tree.Text).Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message));
            return RenderSpecParser.FindAll(tree)[0].Items;
        }

        // The staff alone: her notes, no words under them.
        var alone = Assert.IsType<SingleStaffSpec>(Items("score { staff lead  lyrics words  staff carol }")[1]).Staff;
        Assert.Equal("carol__0", alone.VoiceName);
        Assert.Empty(alone.WithLyrics);

        // Both: the row under her staff is her staff's verse.
        var both = Assert.IsType<SingleStaffSpec>(Items("score { staff carol  lyrics carol }")[0]).Staff;
        Assert.Equal("carol__0", both.VoiceName);
        Assert.Equal(["__words_carol__0"], both.WithLyrics);

        // The words alone, under the lead: a row of its own, at her rhythm.
        var items = Items("score { staff lead  lyrics carol }");
        Assert.Equal("lead", Assert.IsType<SingleStaffSpec>(items[0]).Staff.VoiceName);
        var row = Assert.IsType<LyricsRowSpec>(items[1]);
        Assert.Equal("__words_carol__0", row.PartName);
        Assert.Equal("carol__0", row.Sings);
    }

    [Fact]
    public void APartCalledAsASingerIs_IsThePart_AndANameNobodyHasIsAnUndefinedPart()
    {
        // The part: nothing is made (it is what her notes written out are).
        string written = Book(Band, "part carol { instrument alto }\nscore { staff lead  staff carol }");
        var tree = SyntaxTree.Parse(written);
        Assert.False(SingerParts.Wanted(tree));
        Assert.Same(written, SingerParts.Expand(written, tree, out var said));
        Assert.Empty(said);

        string nobody = Book(Band, "score { staff lead  staff dave }");
        var other = SyntaxTree.Parse(nobody);
        Assert.False(SingerParts.Wanted(other));
        Assert.Contains(SemanticValidation.Run(other), d => d.Code == DiagnosticCodes.UndefinedPart && d.Message.Contains("'dave'"));
    }

    /// <summary>Owner's question 2026-11-12 ("was a part's name not refused for a singer?"): it
    /// is — where the name would hide what she sings. Not where the part is her notes written out.</summary>
    [Fact]
    public void ASingerCalledAsAPartIs_IsRefused_UnlessThePartIsHerNotesWrittenOut()
    {
        IEnumerable<Diagnostic> Named(string singers, string extra = "")
            => SemanticValidation.Run(SyntaxTree.Parse(Book(singers, extra + "score { staff lead  lyrics words }")))
                .Where(d => d.Code == DiagnosticCodes.SingerNamedAsPart);

        // Called as the part she sings a third above: `staff lead` is the part, not her.
        var refused = Assert.Single(Named("singer lead female sings on lead up 3 in Chorus"));
        Assert.Equal(DiagnosticSeverity.Error, refused.Severity);
        Assert.Contains("'lead' is a part's name", refused.Message);
        // Called as another part.
        Assert.Single(Named("singer pad female sings on lead", "part pad { instrument alto }\n"));
        // Her notes written out: the part is hers, and she sings it as it stands.
        Assert.Empty(Named("singer carol female sings words on carol", "part carol { instrument alto  lyrics words }\n"));
        // …but not moved once more.
        Assert.Single(Named("singer carol female sings words on carol up 3", "part carol { instrument alto  lyrics words }\n"));
        // A name no part has.
        Assert.Empty(Named("singer carol female sings on lead up 3 in Chorus"));
    }

    /// <summary>Owner's report 2026-11-12: <c>lyrics solfege</c> under <c>staff alice</c> warned
    /// that "part alice__2" did not list it — a part the writer neither wrote nor can add to.</summary>
    [Fact]
    public void ARowUnderASingersStaff_IsNotAskedOfThePartLilySharpMade()
    {
        var (_, tree, said) = Expanded(Book(Band, "score { staff lead  lyrics words  staff carol  lyrics carol  lyrics solfege }"));
        Assert.Empty(said.Select(d => d.Message));
        Assert.Empty(SingerParts.OwnOf(SemanticValidation.Run(tree), tree.Text).Select(d => d.Message));

        // …while a part the file writes is still asked.
        var plain = SyntaxTree.Parse(Book(Band, "score { staff lead  lyrics solfege }"));
        Assert.Contains(SemanticValidation.Run(plain), d => d.Code == DiagnosticCodes.TrackNotListedByPart);
    }

    [Fact]
    public void ASingerTheScoresOwnLinesLeaveOut_IsSaidAtTheItem()
    {
        // The score's line for lead takes the place of the file's lines for it: carol sings nothing there.
        string source = Book(Band, "score { singer female sings on lead\n staff lead  staff carol }");
        var (text, _, said) = Expanded(source);
        var problem = Assert.Single(said);
        Assert.Equal(DiagnosticCodes.SingerNotPrinted, problem.Code);
        Assert.Contains("carol sings nothing in this score", problem.Message);
        Assert.Equal("staff carol", source.Substring(problem.Span.Start, problem.Span.Length));
        Assert.Same(source, text);
    }

    /// <summary>Owner's report 2026-11-12: a Japanese score with its own singer lines, alice's staff
    /// added — and her words came out English. Her staff was made of every line of her name; it
    /// is made of the lines the score reads, the score's own over the top level's.</summary>
    [Fact]
    public void AScoreWithItsOwnSingerLines_PrintsAndSingsTheSingerByThoseLines()
    {
        string source = """
            octave relative
            time 4/4
            key c major
            language english
            part lead { instrument alto  lyrics en  lyrics jp }
            section Verse {
              lead { e'4 e d c | }
              lyrics en { one two three four | }
              lyrics jp language japanese { あ さ の み | }
            }
            section Chorus {
              lead { g'4 g a g | }
              lyrics en { la la la la | }
              lyrics jp language japanese { う た お う | }
            }
            form { Verse Chorus }
            singer alice female sings en on lead in Verse
            singer carol female sings en on lead up 3 in Chorus
            score { staff lead  lyrics en  staff alice  lyrics alice }
            score jp {
              singer alice female sings jp on lead in Verse
              singer carol female sings jp on lead up 3 in Chorus
              staff lead  lyrics jp  staff alice  lyrics alice  staff carol  lyrics carol
            }
            """;
        var (text, tree, said) = Expanded(source);
        Assert.Empty(said.Select(d => d.Message));
        string made = text[SingerParts.MadeFrom(text)..];
        // Two staves of alice: the top level's, and the second score's own.
        Assert.Contains("part alice__0 \"Alice\"", made);
        Assert.Contains("part alice__2 \"Alice\"", made);
        Assert.Contains("lyrics __words_alice__0 {", made);
        Assert.Contains("section Verse { one two three four | }", made);
        Assert.Contains("lyrics __words_alice__2 language japanese {", made);
        Assert.Contains("section Verse { あ さ の み | }", made);
        Assert.Contains("part carol__2 \"Carol\"", made);
        Assert.Contains("section Chorus { う た お う | }", made);
        Assert.Empty(SingerParts.OwnOf(SemanticValidation.Run(tree), tree.Text).Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message));

        // …and each score sings the words it shows.
        var midi = Midi(tree, "jp");
        var sung = SungVoices.Read(midi, MidiClock.SecondsOf(midi));
        Assert.Equal(["alice__2", "carol__2"], sung.Select(v => v.Part).OrderBy(p => p, StringComparer.Ordinal));
        Assert.Equal(["あ", "さ", "の", "み"], sung.Single(v => v.Part == "alice__2").Notes.Select(n => n.Lyric));
        var english = Midi(tree);
        Assert.Equal(["one", "two", "three", "four"], SungVoices.Read(english, MidiClock.SecondsOf(english)).Single(v => v.Part == "alice__0").Notes.Select(n => n.Lyric));
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
