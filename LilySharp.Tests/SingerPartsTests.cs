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

        // …and the note names are asked of no part: they are made from the notes, and a part
        // cannot list them (the evaluation of the same day; PartOwnWordsTests has the rest).
        var plain = SyntaxTree.Parse(Book(Band, "score { staff lead  lyrics solfege }"));
        Assert.DoesNotContain(SemanticValidation.Run(plain), d => d.Code == DiagnosticCodes.TrackNotListedByPart);
    }

    /// <summary>Owner's question 2026-11-12 (how a score has alice sing solfege): a singer line
    /// that sings the note names, and <c>lyrics alice</c> is then those names under her staff —
    /// no track is written for words made from the notes.</summary>
    [Fact]
    public void ASingerWhoSingsTheNoteNames_HasThemForHerWords()
    {
        var (text, tree, said) = Expanded(Book("singer carol female sings solfege on lead up 3 in Chorus", "score { staff carol  lyrics carol }"));
        Assert.Empty(said.Select(d => d.Message));
        Assert.DoesNotContain("__words_carol", text);
        Assert.Empty(SingerParts.OwnOf(SemanticValidation.Run(tree), tree.Text).Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message));
        var staff = Assert.IsType<SingleStaffSpec>(Assert.Single(RenderSpecParser.FindAll(tree)[0].Items)).Staff;
        Assert.Equal("carol__0", staff.VoiceName);
        Assert.Equal(["solfege"], staff.WithLyrics);

        var midi = Midi(tree);
        var voice = Assert.Single(SungVoices.Read(midi, MidiClock.SecondsOf(midi)));
        Assert.Equal("carol__0", voice.Part);
        // g g a g a third above: b b c b — ti ti do ti.
        Assert.Equal(4, voice.Notes.Count);
        Assert.All(voice.Notes, n => Assert.False(string.IsNullOrEmpty(n.Lyric)));
    }

    /// <summary>Owner's decision 2026-11-12: the part before the words. <c>singer alice female on
    /// lead in Verse</c> is a singer line — twice that day the owner wrote it so, and twice it
    /// was an error for its missing <c>sings</c>. <c>on PART</c> comes first; <c>sings WORDS</c>
    /// follows it only where the words have to be named; the older order is read as it was.</summary>
    [Fact]
    public void ThePartComesFirst_AndSingsOnlyNamesTheWords()
    {
        SingerDeclarationSyntax Line(string line)
        {
            var tree = SyntaxTree.Parse(Book("// the singers\n" + line, "score { staff lead  lyrics words }"));
            Assert.Empty(tree.Diagnostics.Select(d => d.Message));
            Assert.Empty(SemanticValidation.Run(tree).Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message));
            return tree.GetRoot().DescendantNodes<SingerDeclarationSyntax>().Single();
        }

        var plain = Line("singer carol female on lead");
        Assert.Equal(("carol", "female", "lead"), (plain.PersonToken?.Text, plain.GenderToken?.Text, plain.OnToken?.Text));
        Assert.Null(plain.SingsKeyword);
        Assert.Null(plain.TrackToken);

        var placed = Line("singer carol female \"x/y\" on lead up 3 octave -1 in Chorus with breathiness 2");
        Assert.Equal(("x/y", "lead", 3, -1), (placed.Singer, placed.OnToken?.Text, placed.Interval, placed.Octaves));
        Assert.Equal(["Chorus"], placed.Sections.Select(s => s.Text));
        Assert.Equal("breathiness", Assert.Single(placed.Settings).Name);

        var named = Line("singer carol on lead sings words up 3 in Chorus");
        Assert.Equal(("lead", "words", 3), (named.OnToken?.Text, named.TrackToken?.Text, named.Interval));
        Assert.Equal(["Chorus"], named.Sections.Select(s => s.Text));

        // The older order, as it was.
        var older = Line("singer carol female sings words on lead up 3 in Chorus");
        Assert.Equal(("lead", "words", 3), (older.OnToken?.Text, older.TrackToken?.Text, older.Interval));

        // …and the two orders are one line to everything that reads it.
        var a = LilySharp.Core.Music.LyricSinger.LinesOf(SyntaxTree.Parse(Book("singer carol female on lead up 3 in Chorus", "")).GetRoot())[0];
        var b = LilySharp.Core.Music.LyricSinger.LinesOf(SyntaxTree.Parse(Book("singer carol female sings words on lead up 3 in Chorus", "")).GetRoot())[0];
        Assert.Equal((b.Singer, b.Track, b.Part, b.Person), (a.Singer, a.Track, a.Part, a.Person));
    }

    [Theory]
    [InlineData("singer carol female lead")]
    [InlineData("singer carol female \"x/y\"")]
    public void ALineWithNeitherOnNorSings_SaysSoOnce(string line)
    {
        string source = Book("// the singers\n" + line, "score { staff lead  lyrics words }");
        var tree = SyntaxTree.Parse(source);
        Assert.Contains(tree.Diagnostics, d => d.Code is DiagnosticCodes.SingerNeedsSings or DiagnosticCodes.SingerGenderUnknown);
        // Nothing is made of a file that does not parse, nor said at the score's items.
        Assert.Same(source, SingerParts.Expand(source, tree, out var said));
        Assert.Empty(said);
    }

    /// <summary>Owner's decisions 2026-11-12: "alice sings lead, and lead names its English words:
    /// the line need not say en" — of a part's several lyrics a line that names none sings the
    /// one in the singer's language, the line's own <c>language</c> or the file's.</summary>
    [Fact]
    public void OfAPartsSeveralLyrics_ALineThatNamesNone_SingsTheOneInItsLanguage()
    {
        string Bilingual(string singers) => $$"""
            octave relative
            time 4/4
            language english
            part lead { instrument alto  lyrics en  lyrics jp  lyrics fr }
            section Verse {
              lead { e'4 e d c | }
              lyrics en { one two three four | }
              lyrics jp language japanese { あ さ の み | }
              lyrics fr { un deux trois quatre | }
            }
            form { Verse }
            {{singers}}
            score { staff lead  lyrics en }
            """;
        string TrackOf(string singers)
        {
            var tree = SyntaxTree.Parse(Bilingual(singers));
            Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
            return LilySharp.Core.Music.LyricSinger.LinesOf(tree.GetRoot())[0].Track;
        }
        IEnumerable<Diagnostic> Ambiguous(string singers)
            => SemanticValidation.Run(SyntaxTree.Parse(Bilingual(singers))).Where(d => d.Code == DiagnosticCodes.SingerWordsAmbiguous);

        // The line's own language, a bare word of its head as the gender is: the one track in it.
        Assert.Equal("jp", TrackOf("singer alice female japanese sings on lead"));
        Assert.Empty(Ambiguous("singer alice female japanese sings on lead"));
        Assert.Equal("jp", TrackOf("singer female japanese \"neutrino/SEVEN\" sings on lead"));
        Assert.Equal("jp", TrackOf("singer japanese sings on lead"));
        // A name written is the name, whatever the language.
        Assert.Equal("en", TrackOf("singer alice female japanese sings en on lead"));

        // No language on the line: the file's — and here two tracks are in it (en, fr), so the line must say.
        var two = Assert.Single(Ambiguous("singer alice female sings on lead"));
        Assert.Contains("2 of them are in english, the file's language", two.Message);
        Assert.Contains("say the singer's language as its gender is said", two.Message);
        // A language no track is in.
        var none = Assert.Single(Ambiguous("singer alice female korean sings on lead"));
        Assert.Contains("none of them is in korean, the language this line says", none.Message);

        // A word that is no language.
        var tree2 = SyntaxTree.Parse(Bilingual("singer alice female klingon sings on lead"));
        Assert.Contains(tree2.Diagnostics, d => d.Code == DiagnosticCodes.SingerGenderUnknown && d.Message.Contains("no language"));
        // …and the head's other readers are not confused by the clause.
        var node = SyntaxTree.Parse(Bilingual("singer alice japanese female \"x/y\" sings on lead")).GetRoot().DescendantNodes<SingerDeclarationSyntax>().Single();
        Assert.Equal("alice", node.PersonToken?.Text);
        Assert.Equal("female", node.GenderToken?.Text);
        Assert.Equal("x/y", node.Singer);
        Assert.Equal("japanese", node.LanguageToken?.Text);
        Assert.Null(node.TrackToken);
    }

    /// <summary>Owner's decision 2026-11-12: a verse in Japanese and a chorus in English is one
    /// song. A part sings one set of words IN A SECTION; lines that sing other words in other
    /// sections stand together, each its own voice in its own language.</summary>
    [Fact]
    public void APartSingsOtherWordsInOtherSections()
    {
        string Song(string singers) => $$"""
            octave relative
            time 4/4
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
            {{singers}}
            score { staff lead  lyrics en }
            """;
        IEnumerable<Diagnostic> Collisions(string singers)
            => SemanticValidation.Run(SyntaxTree.Parse(Song(singers))).Where(d => d.Code == DiagnosticCodes.SingerDuplicatePart);

        string mixed = "singer alice female japanese sings on lead in Verse\nsinger bob male sings on lead in Chorus\nsinger carol female sings on lead up 3 in Chorus";
        Assert.Empty(Collisions(mixed));
        var tree = SyntaxTree.Parse(Song(mixed));
        Assert.Empty(SemanticValidation.Run(tree).Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message));
        var midi = Midi(tree);
        var voices = SungVoices.Read(midi, MidiClock.SecondsOf(midi));
        // Three voices of the one part, each with its own key, its own words and their language.
        Assert.Equal(3, voices.Select(v => v.Key).Distinct().Count());
        Assert.All(voices, v => Assert.Equal("lead", v.Part));
        var japanese = Assert.Single(voices, v => v.Language == "ja");
        Assert.Equal("jp", japanese.Track);
        Assert.Equal(["あ", "さ", "の", "み"], japanese.Notes.Select(n => n.Lyric));
        var english = voices.Where(v => v.Language == "en").ToList();
        Assert.Equal(2, english.Count);
        Assert.All(english, v => Assert.Equal(["la", "la", "la", "la"], v.Notes.Select(n => n.Lyric)));
        Assert.Equal([0, 1], english.Select(v => v.Line));

        // Two sets of words in one section are still refused, and the message says where.
        var same = Assert.Single(Collisions("singer alice female japanese sings on lead in Verse\nsinger bob male sings on lead in Verse Chorus"));
        Assert.Contains("already sings 'jp' in Verse", same.Message);
        // …and so is a line that names no sections beside one that sings other words.
        var whole = Assert.Single(Collisions("singer alice female japanese sings on lead\nsinger bob male sings on lead in Chorus"));
        Assert.Contains("say which on both lines", whole.Message);
    }

    // ── Found writing five scores to try the grammar (owner's request 2026-11-12) ──

    /// <summary>A singer who sings a verse in Japanese and a chorus in English: in the score that
    /// prints her staff the chorus went to the voice as Japanese — her made words are one track,
    /// and its language was the first section's.</summary>
    [Fact]
    public void OnHerOwnStaff_ASingerSingsEachSectionInItsLanguage()
    {
        string source = """
            octave relative
            time 4/4
            language english
            part her { instrument soprano  lyrics herJp  lyrics herEn }
            section Verse {
              her { e'4 e d c | }
              lyrics herJp language japanese { あ さ の み | }
            }
            section Chorus {
              her { g'4 g a g | }
              lyrics herEn { Sing it o- ver | }
            }
            form { Verse Chorus }
            singer aya female japanese on her in Verse
            singer aya female english on her in Chorus
            score { staff aya  lyrics aya }
            """;
        var (_, tree, said) = Expanded(source);
        Assert.Empty(said.Select(d => d.Message));
        Assert.Empty(SingerParts.OwnOf(SemanticValidation.Run(tree), tree.Text).Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message));
        var midi = Midi(tree);
        var voices = SungVoices.Read(midi, MidiClock.SecondsOf(midi));
        Assert.Equal(["ja", "en"], voices.Select(v => v.Language));
        Assert.All(voices, v => Assert.Equal("aya__0", v.Part));
        Assert.Equal(["あ", "さ", "の", "み"], voices[0].Notes.Select(n => n.Lyric));
        Assert.Equal(["Sing", "it", "o", "ver"], voices[1].Notes.Select(n => n.Lyric));
    }

    /// <summary><c>with breathiness 2 in B</c> gave the voice a setting called in, and the line
    /// sang every section: what of the part is sung is said before <c>with</c>.</summary>
    [Theory]
    [InlineData("singer aya female on lead with breathiness 2 in Chorus", "in")]
    [InlineData("singer aya female on lead with breathiness 2 up 3", "up")]
    [InlineData("singer aya female on lead with octave -1", "octave")]
    public void APlaceWordAfterWith_IsSaid(string line, string word)
    {
        var said = Assert.Single(SemanticValidation.Run(SyntaxTree.Parse(Book(line, "score { staff lead  lyrics words }"))),
            d => d.Code == DiagnosticCodes.SingerPlaceMalformed);
        Assert.Contains($"'{word}' after 'with' is read as a setting of the voice", said.Message);
    }

    /// <summary><c>singer aya</c> and nothing more was "music at the top level".</summary>
    [Theory]
    [InlineData("singer aya")]
    [InlineData("singer aya female")]
    [InlineData("singer aya female \"x/y\"")]
    public void ASingersHeadAlone_IsASingerLineThatLacksItsPart(string line)
    {
        var tree = SyntaxTree.Parse(Book("// the singers\n" + line, "score { staff lead  lyrics words }"));
        Assert.Equal(DiagnosticCodes.SingerNeedsSings, Assert.Single(tree.Diagnostics).Code);
    }

    /// <summary>To change one singer in a score the others are not written again: a named line of
    /// the score replaces the file's lines of that name, and the file's other singers go on.</summary>
    [Fact]
    public void ANamedLineInAScore_ReplacesThatSingerAlone()
    {
        string scores = "score { staff lead  lyrics words }\n"
            + "score practice {\n  singer alice female on lead sings solfege in Verse\n  staff lead  lyrics words  staff carol\n}";
        var (_, tree, said) = Expanded(Book(Band, scores));
        Assert.Empty(said.Select(d => d.Message));
        var midi = Midi(tree, "practice");
        var voices = SungVoices.Read(midi, MidiClock.SecondsOf(midi));
        // alice is the score's (the note names), bob and carol the file's still.
        Assert.Equal("solfege", Assert.Single(voices, v => v.Singer == "female" && v.Part == "lead").Track);
        Assert.Contains(voices, v => v.Singer == "male" && v.Part == "lead");
        Assert.Contains(voices, v => v.Part == "carol__0");
        Assert.Equal(3, voices.Count);

        // A line with no name still takes the place of every line for its part.
        var (_, plain, _) = Expanded(Book(Band, "score { singer male on lead\n staff lead  lyrics words }"));
        var all = Midi(plain);
        var one = Assert.Single(SungVoices.Read(all, MidiClock.SecondsOf(all)));
        Assert.Equal("male", one.Singer);
        Assert.Equal(8, one.Notes.Count);
    }

    /// <summary><c>in Chorus up 3</c> is how it is said, and was two errors ("no section is named
    /// up", "'3' is not a word of a singer line"): what of the part is sung comes in any order.</summary>
    [Theory]
    [InlineData("singer carol female on lead in Chorus up 3")]
    [InlineData("singer carol female on lead octave -1 in Chorus up 3")]
    [InlineData("singer carol female on lead up 3 octave -1 in Chorus")]
    [InlineData("singer carol female on lead in Chorus octave -1 up 3 with breathiness 2")]
    public void WhatOfThePartIsSung_ComesInAnyOrder(string line)
    {
        var tree = SyntaxTree.Parse(Book(line, "score { staff lead  lyrics words }"));
        Assert.Empty(tree.Diagnostics.Select(d => d.Message));
        Assert.Empty(SemanticValidation.Run(tree).Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message));
        var node = tree.GetRoot().DescendantNodes<SingerDeclarationSyntax>().Single();
        Assert.Equal(3, node.Interval);
        Assert.Equal(["Chorus"], node.Sections.Select(s => s.Text));
        if (line.Contains("octave"))
            Assert.Equal(-1, node.Octaves);
    }

    /// <summary>The slips the evaluation scores made, each said where it is and how to write it.</summary>
    [Theory]
    [InlineData("singer carol \"x/y\" female on lead", "'female' comes before the voice's name")]
    [InlineData("singer carol female male on lead", "a singer line says one gender")]
    [InlineData("singer carol japanese english on lead", "a singer line says one language")]
    [InlineData("singer carol female on lead up 3 sings words", "'sings WORDS' comes right after the part")]
    [InlineData("singer carol female on lead up 3 down 2", "one interval")]
    [InlineData("singer carol female on lead in Verse in Chorus", "'in' is written twice")]
    public void ASlipOfOrder_IsSaidAsWhatItIs(string line, string says)
    {
        var tree = SyntaxTree.Parse(Book(line, "score { staff lead  lyrics words }"));
        Assert.Contains(says, Assert.Single(tree.Diagnostics).Message);
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
