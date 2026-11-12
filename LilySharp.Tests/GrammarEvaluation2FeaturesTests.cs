// This file is part of Lily#.
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Lily# is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// Lily# is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.IO;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using LilySharp.Tests.LpFidelity;
using Xunit;
using Diagnostic = LilySharp.Core.Syntax.Diagnostic;
using DiagnosticSeverity = LilySharp.Core.Syntax.DiagnosticSeverity;

namespace LilySharp.Tests;

/// <summary>
/// The spellings four large scores written to try the grammar asked for, and the owner approved
/// (2026-11-12, grammar evaluation 2): a tempo whose count is played and not printed, a mark over
/// a run of notes, an accidental on an ornament, an arpeggio across both staves of a grand staff.
/// Each is held from the text to the page and the sound.
/// </summary>
public class GrammarEvaluation2FeaturesTests
{
    private static List<Diagnostic> Said(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return [.. tree.Diagnostics, .. SemanticValidation.Run(tree)];
    }

    private static void Clean(string source) => Assert.Empty(Said(source).Select(d => d.Message));

    /// <summary>The text the tree holds is the file's, to the character.</summary>
    private static void RoundTrips(string source)
        => Assert.Equal(source, SyntaxTree.Parse(source).GetRoot().ToFullString());

    private static LilySharp.Core.Midi.MidiFile Midi(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindFirst(tree);
        return new LilySharp.Core.Midi.MidiExporter { Form = spec?.Form, Score = spec }.Export(tree);
    }

    private static readonly Uri DocUri = new("file:///features.lys");

    /// <summary>The labels the completion request answers at <c>‸</c>.</summary>
    private static string[] Offered(string marked)
    {
        int offset = marked.IndexOf('‸');
        string text = marked.Remove(offset, 1);
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = DocUri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        return [.. (server.Completion(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocUri },
            Position = new Position(line, character),
        })?.Items ?? []).Select(i => i.Label!)];
    }

    // ───────────────────────── 1. tempo "Tempo I" 4 = 92 bare ─────────────────────────

    private const string BareTempos = "time 4/4\ntempo \"Allegro\" 4 = 120 bare\npart m { }\n"
        + "section A { m { c'4 d' e' f' | tempo \"Meno mosso\" 2 = 40 bare g'4 a' b' c'' | } }\n"
        + "section B { tempo \"Tempo I\" 4 = 92 bare\n m { c'1 | } }\n"
        + "form { ~A ~B }\nscore { staff m }\n";

    /// <summary>The run's last word: the count is the sound's, the words are the page's — at the
    /// top level, in a section header and in the music.</summary>
    [Fact]
    public void ABareTempo_IsPlayedAtItsCountAndPrintsItsWordsAlone()
    {
        Clean(BareTempos);
        RoundTrips(BareTempos);

        var value = SyntaxTree.Parse(BareTempos).GetRoot().DescendantNodes().OfType<TempoDeclarationSyntax>().First().Value;
        Assert.True(value.CountHidden);
        Assert.Equal(120, value.Bpm);
        Assert.Null(value.PrintedBpm);

        // The sound: every count, the minim's as crotchets.
        Assert.Equal([120, 80, 92],
            Midi(BareTempos).Tracks[0].TempoChanges.Select(t => (int)Math.Round(60_000_000.0 / t.MicrosecondsPerBeat)));

        // The page: the three markings, and no count, no equals sign, no parenthesis.
        var g = RenderedGeometry.Render(BareTempos);
        foreach (string words in new[] { "Allegro", "Meno mosso", "Tempo I" })
            Assert.Contains(g.Texts, t => t.Text == words);
        Assert.DoesNotContain(g.Texts, t => t.Text.Contains('=') || t.Text.Contains('(') || t.Text.Contains(')')
            || t.Text.Contains("120") || t.Text.Contains("40") || t.Text.Contains("92"));

        // The same tempos without the word print their counts: the test above is not vacuous.
        var counted = RenderedGeometry.Render(BareTempos.Replace(" bare", ""));
        Assert.Contains(counted.Texts, t => t.Text.Contains("92"));
    }

    /// <summary>The model: a bare tempo's mark is what the words alone make.</summary>
    [Fact]
    public void ABareTempo_IsTheMarkItsWordsAloneMake()
    {
        static List<(string Text, string? Words)> Marks(string source)
        {
            var tree = SyntaxTree.Parse(source);
            var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
            return [.. score.MusicMarks.Where(m => m.Type == LilySharp.Core.Svg.Model.MusicMarkType.Tempo)
                .Select(m => (m.Text, m.TempoText))];
        }
        string wordsOnly = BareTempos.Replace(" 4 = 120 bare", "").Replace(" 2 = 40 bare", "").Replace(" 4 = 92 bare", "");
        Assert.Equal(Marks(wordsOnly), Marks(BareTempos));
        Assert.All(Marks(BareTempos), m => Assert.Equal("", m.Text));
    }

    /// <summary>With no words the tempo would print nothing, with no count there is nothing to
    /// hide: each is said, at the word, with what to write.</summary>
    [Theory]
    [InlineData("tempo 4 = 92 bare", "prints nothing")]
    [InlineData("tempo 92 bare", "prints nothing")]
    [InlineData("tempo \"Tempo I\" bare", "writes none")]
    public void ABareTempoThatHidesOrLeavesNothing_IsSaid(string tempo, string says)
    {
        string source = $"time 4/4\n{tempo}\npart m {{ }}\nsection A {{ m {{ c'1 | }} }}\nform {{ A }}\nscore {{ staff m }}\n";
        var said = Assert.Single(Said(source));
        Assert.Equal(DiagnosticCodes.TempoBareHidesNothing, said.Code);
        Assert.Equal(DiagnosticSeverity.Warning, said.Severity);
        Assert.Contains(says, said.Message);
        Assert.Contains("tempo \"Tempo I\" 4 = 92 bare", said.Message);
        Assert.Equal("bare", source.Substring(said.Span.Start, 4));
        RoundTrips(source);
    }

    /// <summary>The word is not reserved: first in the run it is a marking, before an '=' a
    /// variable, and a part may be called that.</summary>
    [Fact]
    public void Bare_IsAWordOnlyAtTheEndOfATempo()
    {
        var marking = SyntaxTree.Parse("tempo bare 4 = 60\n").GetRoot().DescendantNodes().OfType<TempoDeclarationSyntax>().Single().Value;
        Assert.Equal("bare", marking.Marking);
        Assert.False(marking.CountHidden);

        string source = "time 4/4\ntempo \"Largo\" 4 = 50\npart bare { }\n"
            + "section A { bare { c'4 d' e' f' | } }\nform { A }\nscore { staff bare }\n";
        Clean(source);
        // The refused `name = { … }` form is still told about itself, not swallowed by the tempo.
        var legacy = SyntaxTree.Parse("tempo \"Largo\" 4 = 50\nbare = { c'4 }\n");
        Assert.False(legacy.GetRoot().DescendantNodes().OfType<TempoDeclarationSyntax>().Single().Value.CountHidden);
        Assert.Contains(legacy.Diagnostics, d => d.Code == DiagnosticCodes.LegacyDeclarationForm);
    }

    /// <summary>A swing equation in the same run is still drawn: the word hides the count.</summary>
    [Fact]
    public void ABareTempoWithAFeelWord_KeepsItsSwing()
    {
        var value = SyntaxTree.Parse("tempo \"Medium\" 4 = 120 swing 16 bare\n").GetRoot()
            .DescendantNodes().OfType<TempoDeclarationSyntax>().Single().Value;
        Assert.Equal((120, 16, true), (value.Bpm, value.SwingSubdivision, value.CountHidden));
        Clean("time 4/4\ntempo 4 = 120 swing bare\npart m { }\nsection A { m { c'1 | } }\nform { A }\nscore { staff m }\n");
    }

    /// <summary>LilyPond's own property keeps the count off its page, and MusicXML writes the
    /// words with the sound and no metronome.</summary>
    [Fact]
    public void ABareTempo_GoesToLilyPondAndMusicXmlAsWordsWithASound()
    {
        var tree = SyntaxTree.Parse(BareTempos);
        string ly = new LilySharp.Core.LilyPond.LilyPondExporter().Export(tree);
        Assert.Contains("\\once \\set Score.tempoHideNote = ##t \\tempo \"Tempo I\" 4 = 92", ly);
        Assert.Contains("\\once \\set Score.tempoHideNote = ##t \\tempo \"Meno mosso\" 2 = 40", ly);

        string xml = new LilySharp.Core.MusicXml.MusicXmlExporter().Export(tree).ToXml().ToString();
        Assert.DoesNotContain("<metronome", xml);
        Assert.Contains(">Tempo I</words>", xml);
        Assert.Contains("<sound tempo=\"92\"", xml);
        Assert.Contains("<sound tempo=\"80\"", xml);
    }

    /// <summary>…and MusicXML read back writes them as they were: the words with a sound and no
    /// metronome came back as <c>tempo 92</c> and a <c>@text</c> (a leftover of the same day).
    /// The count comes back as the quarter's, which is what the sound holds.</summary>
    [Fact]
    public void ABareTempo_ComesBackFromMusicXmlAsWritten()
    {
        string xml = new LilySharp.Core.MusicXml.MusicXmlExporter().Export(SyntaxTree.Parse(BareTempos)).ToXml().ToString();
        var (lys, _) = new LilySharp.Core.MusicXmlImport.MusicXmlImporter().Import(xml);
        Assert.Contains("\ntempo \"Allegro\" 4 = 120 bare\n", lys);
        Assert.Contains("| tempo \"Meno mosso\" 4 = 80 bare g", lys);
        Assert.Contains("tempo \"Tempo I\" 4 = 92 bare c'1", lys);
        Assert.DoesNotContain("@text", lys);
        Clean(lys);
        var tempos = SyntaxTree.Parse(lys).GetRoot().DescendantNodes().OfType<TempoDeclarationSyntax>().Select(t => t.Value).ToList();
        Assert.Equal([("Allegro", 120, true), ("Meno mosso", 80, true), ("Tempo I", 92, true)],
            tempos.Select(v => (v.Marking!, v.Bpm!.Value, v.CountHidden)));

        // A printed count is a count still, and an italic word with a sound is an expression.
        string printed = xml.Replace("<words font-weight=\"bold\">Tempo I</words>",
            "<metronome><beat-unit>quarter</beat-unit><per-minute>92</per-minute></metronome>");
        Assert.Contains("tempo 92 c'1", new LilySharp.Core.MusicXmlImport.MusicXmlImporter().Import(printed).Item1);
        string italic = xml.Replace("<words font-weight=\"bold\">Tempo I</words>", "<words font-style=\"italic\">meno</words>");
        string read = new LilySharp.Core.MusicXmlImport.MusicXmlImporter().Import(italic).Item1;
        Assert.Contains("tempo 92 c'1@text(\"meno\").up", read);
    }

    /// <summary>A tempo that prints its words and its count, <c>tempo "Andante" 4 = 88</c>, was
    /// exported as the metronome alone and the words were lost (a leftover of the same day). It
    /// is the words and the metronome in one direction — at the opening, at a section's head and
    /// in the music — and read back it is the tempo with its marking, the count the quarter's.</summary>
    [Fact]
    public void ATemposWords_GoToMusicXmlWithItsCount_AndComeBack()
    {
        string source = BareTempos.Replace(" bare", "");
        Clean(source);
        string xml = new LilySharp.Core.MusicXml.MusicXmlExporter().Export(SyntaxTree.Parse(source)).ToXml()
            .ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
        foreach (var (words, unit, count, sound) in new[] { ("Allegro", "quarter", 120, 120), ("Meno mosso", "half", 40, 80), ("Tempo I", "quarter", 92, 92) })
            Assert.Contains($"<direction-type><words font-weight=\"bold\">{words}</words></direction-type>"
                + $"<direction-type><metronome><beat-unit>{unit}</beat-unit><per-minute>{count}</per-minute></metronome></direction-type>"
                + $"<sound tempo=\"{sound}\" />", xml);

        var (lys, _) = new LilySharp.Core.MusicXmlImport.MusicXmlImporter().Import(xml);
        Assert.Contains("\ntempo \"Allegro\" 4 = 120\n", lys);
        Assert.Contains("| tempo \"Meno mosso\" 4 = 80 g", lys);
        Assert.Contains("tempo \"Tempo I\" 4 = 92 c'1", lys);
        Assert.DoesNotContain("@text", lys);
        Assert.DoesNotContain("bare", lys);
        Clean(lys);

        // Another program's file: the words and the metronome in one direction, the words
        // plain, with a space after them, no sound (the count is read off the metronome).
        string other = xml.Replace("<words font-weight=\"bold\">Tempo I</words>", "<words>Tempo I </words>")
            .Replace("<sound tempo=\"92\" />", "");
        Assert.Contains("tempo \"Tempo I\" 4 = 92 c'1", new LilySharp.Core.MusicXmlImport.MusicXmlImporter().Import(other).Item1);
        // Italic words beside a metronome are an expression still, and a count alone a count.
        string italic = xml.Replace("<words font-weight=\"bold\">Tempo I</words>", "<words font-style=\"italic\">meno</words>");
        Assert.Contains("tempo 92 c'1@text(\"meno\").up", new LilySharp.Core.MusicXmlImport.MusicXmlImporter().Import(italic).Item1);
        string alone = xml.Replace("<direction-type><words font-weight=\"bold\">Tempo I</words></direction-type>", "");
        Assert.Contains("tempo 92 c'1", new LilySharp.Core.MusicXmlImport.MusicXmlImporter().Import(alone).Item1);
    }


    /// <summary>A lead sheet's twin writes its tempos on a timing track built from the page's
    /// marks, and a mark with a hidden count has none: the words were written alone and LilyPond
    /// played its default tempo (a leftover of the same day). At the opening, at a section's
    /// head, and where a section with no tempo returns to the file's.</summary>
    [Fact]
    public void ALeadSheetsTwin_PlaysABareTempoAtItsCount()
    {
        string source = "time 4/4\ntempo \"Allegro\" 4 = 120 bare\n"
            + "chords h {\n section A { C | F | }\n section B { G | C | }\n section C { Am | F | }\n}\n"
            + "section B { tempo \"Meno mosso\" 2 = 40 bare }\nform { A B C }\nscore { chords h }\n";
        Clean(source);
        string ly = new LilySharp.Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(source));
        string timing = ly[ly.IndexOf("leadSheetTiming = {", StringComparison.Ordinal)..];
        timing = timing[..timing.IndexOf("\n}", StringComparison.Ordinal)];
        Assert.Equal([
                "\\once \\set Score.tempoHideNote = ##t \\tempo \"Allegro\" 4 = 120",
                "\\once \\set Score.tempoHideNote = ##t \\tempo \"Meno mosso\" 2 = 40",
                "\\once \\set Score.tempoHideNote = ##t \\tempo \"Allegro\" 4 = 120",
            ],
            System.Text.RegularExpressions.Regex.Matches(timing, @"\\once \\set Score\.tempoHideNote = ##t \\tempo ""[^""]*"" \d+ = \d+").Select(m => m.Value));
        Assert.DoesNotMatch(@"\\tempo ""[^""]*"" (?!\d)", timing);
        // A printed count is written as it was.
        string printed = new LilySharp.Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(source.Replace(" bare", "")));
        Assert.Contains("\\tempo \"Allegro\" 4 = 120 s1", printed);
        Assert.DoesNotContain("tempoHideNote", printed);
    }

    /// <summary>The editor offers the word after a tempo's count.</summary>
    [Theory]
    [InlineData("tempo \"Tempo I\" 4 = 92 ‸")]
    [InlineData("tempo 92 ‸")]
    [InlineData("part m { }\nsection A { tempo \"Tempo I\" 4 = 92 ‸\n m { c'1 | } }")]
    public void AfterATemposCount_BareIsOffered(string marked) => Assert.Contains("bare", Offered(marked));

    /// <summary>…and after its feel word, where only 8 and 16 were (a leftover of the same day):
    /// <c>tempo "Swing" 4 = 120 swing bare</c>. A run with no count has none to hide.</summary>
    [Fact]
    public void AfterATemposFeelWord_BareIsOfferedWhenThereIsACount()
    {
        Assert.Equal(["8", "16", "bare"], Offered("tempo \"Swing\" 4 = 120 swing ‸"));
        Assert.Equal(["8", "16", "bare"], Offered("tempo 120 shuffle ‸"));
        Assert.Equal(["8", "16"], Offered("tempo swing ‸"));
        // Touching the word, the row writes the word again with it.
        var item = LilySharpLanguageServer.GetTempoSubdivisionCompletions("swing", touching: true, bare: true).Items[^1];
        Assert.Equal(("bare", "swing bare"), (item.Label, item.InsertText));
        Clean("time 4/4\ntempo \"Swing\" 4 = 120 swing bare\npart m { }\nsection A { m { c'1 | } }\nform { A }\nscore { staff m }\n");
    }

    // ───────────────────────── 2. c'8@staccato d' e' f'@!staccato ─────────────────────────

    private static string Book(string music, string more = "")
        => $"octave absolute\ntime 4/4\npart m {{ }}\n{more}section A {{ m {{ {music} }} }}\nform {{ A }}\nscore {{ staff m }}\n";

    private static List<(LilySharp.Core.Syntax.ArticulationType Type, int Measure, int Item, bool Above)> Scripts(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return [.. score.Articulations.Select(a => (a.Type, a.MeasureIndex, a.ItemIndex, a.IsAbove))
            .OrderBy(a => a.MeasureIndex).ThenBy(a => a.ItemIndex).ThenBy(a => a.Type)];
    }

    /// <summary>A run is, on the page and in the sound, the mark written on each of its notes —
    /// chords, a <c>q</c>, a tuplet's notes and a bar line inside it included, a rest left bare.</summary>
    [Theory]
    [InlineData("c'8@staccato d' e' f'@!staccato g'4 a' |", "c'8@staccato d'@staccato e'@staccato f'@staccato g'4 a' |")]
    [InlineData("c'4@accent <e' g'> q b'@!accent |", "c'4@accent <e' g'>@accent q@accent b'@accent |")]
    [InlineData("c'4@tenuto r d'2 | e'4@!tenuto f' g'2 |", "c'4@tenuto r d'2@tenuto | e'4@tenuto f' g'2 |")]
    [InlineData("tuplet 3/2 { c'8@marcato d' e' } f'4 g'2@!marcato |", "tuplet 3/2 { c'8@marcato d'@marcato e'@marcato } f'4@marcato g'2@marcato |")]
    [InlineData("c''4@staccatissimo.down b' a'@!staccatissimo g' |", "c''4@staccatissimo.down b'@staccatissimo.down a'@staccatissimo.down g' |")]
    [InlineData("c'4@staccato d' e'@staccato f' | g'4 a'@!staccato b'2 |", "c'4@staccato d' e'@staccato f'@staccato | g'4@staccato a'@staccato b'2 |")]
    [InlineData("c'4@staccato@accent d' e'@!accent f'@!staccato |", "c'4@staccato@accent d'@staccato@accent e'@staccato@accent f'@staccato |")]
    [InlineData("c'4@staccato grace { d'16 } e'4 f'@!staccato g' |", "c'4@staccato grace { d'16 } e'4@staccato f'@staccato g' |")]
    public void ARunOfMarks_IsTheMarkOnEachOfItsNotes(string run, string spelledOut)
    {
        Clean(Book(run));
        RoundTrips(Book(run));
        Assert.NotEmpty(Scripts(Book(run)));
        Assert.Equal(Scripts(Book(spelledOut)), Scripts(Book(run)));

        static List<(int Key, int Velocity, int Start, int Ticks)> Notes(string source)
            => [.. Midi(source).Tracks.SelectMany(t => t.Notes).Select(n => (n.Pitch, n.Velocity, n.StartTick, n.DurationTicks))];
        Assert.Equal(Notes(Book(spelledOut)), Notes(Book(run)));

        // The exports write the mark on each note: the same file as the one spelled out, the
        // source positions aside.
        Assert.Equal(new LilySharp.Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(Book(spelledOut))),
            new LilySharp.Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(Book(run))));
        Assert.Equal(new LilySharp.Core.MusicXml.MusicXmlExporter().Export(SyntaxTree.Parse(Book(spelledOut))).ToXml().ToString(),
            new LilySharp.Core.MusicXml.MusicXmlExporter().Export(SyntaxTree.Parse(Book(run))).ToXml().ToString());
    }

    /// <summary>The staccato of a run is played short and its accent loud, note for note.</summary>
    [Fact]
    public void ARunOfStaccato_IsPlayedShort()
    {
        var plain = Midi(Book("c'4 d' e' f' |")).Tracks.SelectMany(t => t.Notes).ToList();
        var run = Midi(Book("c'4@staccato d' e'@!staccato f' |")).Tracks.SelectMany(t => t.Notes).ToList();
        Assert.Equal(4, run.Count);
        for (int i = 0; i < 3; i++)
            Assert.Equal(plain[i].DurationTicks / 2, run[i].DurationTicks);
        Assert.Equal(plain[3].DurationTicks, run[3].DurationTicks);
    }

    /// <summary>Without its end a mark is one note's, as it always was.</summary>
    [Fact]
    public void AMarkWithNoEnd_IsOneNotes()
    {
        var scripts = Scripts(Book("c'4@staccato d' e' f' | g'4@accent a' b'2 |"));
        Assert.Equal(2, scripts.Count);
        Assert.Empty(LilySharp.Core.Semantics.ArticulationRuns.Of(SyntaxTree.Parse(Book("c'4@staccato d' e' f' |")).GetRoot()).Runs);
    }

    /// <summary>A run opens and closes in one part's music of one section (one phrase, one voice):
    /// an end with nothing before it there is an error that says what to write.</summary>
    [Theory]
    [InlineData("section A { m { c'4 d' e' f'@!staccato | } }\n", "form { A }")]
    [InlineData("section A { m { c'4@staccato d' e' f' | } }\nsection B { m { c'4 d' e' f'@!staccato | } }\n", "form { A B }")]
    [InlineData("section A { m { c'4@accent d' e' f'@!staccato | } }\n", "form { A }")]
    [InlineData("section A { m { c'4@staccato d' voice { e'4 f'@!staccato } { c'2 } | } }\n", "form { A }")]
    public void AnEndWithNoOpening_IsAnError(string sections, string form)
    {
        string source = $"octave absolute\ntime 4/4\npart m {{ }}\n{sections}{form}\nscore {{ staff m }}\n";
        var said = Assert.Single(Said(source));
        Assert.Equal(DiagnosticCodes.UnpairedSpan, said.Code);
        Assert.Equal(DiagnosticSeverity.Error, said.Severity);
        Assert.Contains("c8@staccato d e f@!staccato", said.Message);
        Assert.Equal("@!staccato", source.Substring(said.Span.Start, said.Span.Length));
        RoundTrips(source);
    }

    /// <summary>A run inside a phrase goes wherever the phrase is called; a phrase called inside
    /// a run is not reached, and that is said.</summary>
    [Fact]
    public void ARunAndAPhrase()
    {
        string inside = Book("riff riff", "phrase riff { c'4@staccato d'@!staccato e'2 | }\n");
        Clean(inside);
        Assert.Equal(4, Scripts(inside).Count);

        string across = Book("c'2@staccato riff d'2@!staccato r2 |", "phrase riff { e'2 | }\n");
        var said = Assert.Single(Said(across));
        Assert.Equal(DiagnosticSeverity.Warning, said.Severity);
        Assert.Contains("does not reach into 'riff'", said.Message);
        Assert.Equal(2, Scripts(across).Count);
    }

    /// <summary>The page draws one staccato dot a note: four for the run, none on the plain notes.</summary>
    [Fact]
    public void ARunOfStaccato_DrawsADotOnEachNote()
    {
        // Counted against the same bar with no mark: the glyphs it adds are its dots.
        static int Glyphs(string music) => RenderedGeometry.Render(Book(music)).Glyphs.Count;
        int plain = Glyphs("c'8 d' e' f' g'4 a' |");
        Assert.Equal(plain + 4, Glyphs("c'8@staccato d' e' f'@!staccato g'4 a' |"));
        Assert.Equal(plain + 1, Glyphs("c'8@staccato d' e' f' g'4 a' |"));
    }

    /// <summary>
    /// The end typed after its opening is already on the page changes the notes BEFORE the
    /// keystroke: the edited file's preview is the page a fresh compile draws, both ways (the
    /// collect resume adopts nothing in a file that writes a run — CollectResumePlanner.Plan).
    /// </summary>
    [Fact]
    public void TypingARunsEnd_RedrawsTheNotesBeforeIt()
    {
        string bars = string.Concat(Enumerable.Repeat("c'4 d' e' f' | ", 6));
        string before = Book("c'4@staccato d' e' f' | g'4 a' b' c'' | " + bars);
        string after = before.Replace("b' c'' |", "b'@!staccato c'' |");
        var options = new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false };
        var compiler = new IncrementalCompiler(SyntaxTree.Parse(before), options);
        compiler.Render();
        foreach (string text in new[] { after, before, after })
            Assert.Equal(SvgGenerator.Generate(SyntaxTree.Parse(text), options),
                compiler.RenderIncremental(SyntaxTree.Parse(text)));
    }

    /// <summary>After <c>@!</c> the editor offers the marks that may run, with the spans.</summary>
    [Fact]
    public void AfterTheEndMark_TheRunningMarksAreOffered()
    {
        var offered = Offered("part m { }\nsection A { m { c'8@staccato d' e' f'@!‸ } }");
        foreach (string name in LilySharp.Core.Semantics.ArticulationRuns.Names)
            Assert.Contains(name, offered);
        Assert.Contains("8va", offered);
        Assert.DoesNotContain("fermata", offered);
    }

    /// <summary>The text after the step keys are pressed with the caret at <c>‸</c>.</summary>
    private static string Stepped(string marked, int direction)
    {
        int offset = marked.IndexOf('‸');
        string text = marked.Remove(offset, 1);
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = DocUri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        var response = server.Step(new StepParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocUri },
            Selections = [new StepSelection { Start = offset, End = offset }],
            Direction = direction,
            Semitone = true,
        });
        foreach (var e in response.Edits.OrderByDescending(e => e.Start))
            text = text[..e.Start] + e.NewText + text[e.End..];
        return text;
    }

    /// <summary>The step keys on a run's opening rename its end in the same edit: <c>@staccato</c>
    /// stepped to <c>@tenuto</c> left <c>@!staccato</c>, an end that closed nothing (a leftover
    /// of the same day). A run's opening steps along the marks that may run — portato, which
    /// stands between the two for one note's mark, is not one. The end follows and is not stepped.</summary>
    [Fact]
    public void SteppingARunsOpening_RenamesItsEnd()
    {
        static string Book(string music) => "octave absolute\ntime 4/4\npart m { }\nsection A { m { " + music + " } }\nform { A }\nscore { staff m }\n";
        string stepped = Stepped(Book("c'8@stac‸cato d' e' f'@!staccato g'2 |"), +1);
        Assert.Equal(Book("c'8@tenuto d' e' f'@!tenuto g'2 |"), stepped);
        Clean(stepped);
        Assert.Equal(Book("c'8@staccatissimo d' e' f'@!staccatissimo g'2 |"), Stepped(Book("c'8@stac‸cato d' e' f'@!staccato g'2 |"), -1));
        Assert.Equal(Book("c'8@marcato d' e' f'@!marcato g'2 |"), Stepped(Book("c'8@acc‸ent d' e' f'@!accent g'2 |"), +1));
        // Only its own end: a single mark before the run, and the run after it, keep theirs.
        Assert.Equal(Book("c'8@portato d'@staccato e' f'@!staccato g'2 |"), Stepped(Book("c'8@stac‸cato d'@staccato e' f'@!staccato g'2 |"), +1));
        // The end is not a step of its own: the run stays as written.
        string onEnd = Stepped(Book("c'8@staccato d' e' f'@!stac‸cato g'2 |"), +1);
        Assert.Contains("@staccato", onEnd);
        Assert.Contains("@!staccato", onEnd);
    }

    // ───────────────────────── 3. c''2@trill(sharp) ─────────────────────────

    private static bool SameGlyph(DrawnGlyph a, DrawnGlyph b)
        => a.Glyph == b.Glyph && Math.Abs(a.X - b.X) < 1e-6 && Math.Abs(a.Y - b.Y) < 1e-6;

    /// <summary>Each ornament takes each of the three words: the sign as the bare mark draws it,
    /// and over it — the next script out in its column — the small accidental.</summary>
    [Theory]
    [InlineData("trill", "sharp")]
    [InlineData("trill", "flat")]
    [InlineData("trill", "natural")]
    [InlineData("turn", "flat")]
    [InlineData("reverseTurn", "sharp")]
    [InlineData("mordent", "natural")]
    [InlineData("prall", "sharp")]
    [InlineData("pralltriller", "flat")]
    public void AnOrnamentsAccidental_StandsOverItsSign(string ornament, string accidental)
    {
        string source = Book($"c''2@{ornament}({accidental}) e''2 |");
        Clean(source);
        RoundTrips(source);

        var tree = SyntaxTree.Parse(source);
        var mark = tree.GetRoot().DescendantNodes().OfType<MusicMarkSyntax>().Single();
        Assert.Equal((ArticulationRegistry.Resolve(ornament), accidental), AnnotationValues.OrnamentAccidental(mark));

        // The model: the bare ornament's item, then the accidental's, which names its ornament.
        var onNote = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)).Articulations.ToList();
        Assert.Equal(2, onNote.Count);
        Assert.Equal(ArticulationRegistry.Resolve(ornament), onNote[0].Type);
        Assert.Null(onNote[0].AccidentalOfOrnament);
        Assert.Equal(LilySharp.Core.Svg.Model.ArticulationItem.EditorialTypeFor(accidental), onNote[1].Type);
        Assert.Equal(onNote[0].Type, onNote[1].AccidentalOfOrnament);
        Assert.True(onNote[0].IsAbove && onNote[1].IsAbove);

        // The page: the bare ornament's page and one glyph more — small, and above the sign
        // (device Y grows down), which the bare page shows as the topmost glyph in that column.
        var bare = RenderedGeometry.Render(Book($"c''2@{ornament} e''2 |"));
        var drawn = RenderedGeometry.Render(source);
        // (The taller column moves the system down the page, so the two pages are compared by
        // what they draw — glyph and size — and the heights are read on the one page.)
        Assert.Equal(bare.Glyphs.Count + 1, drawn.Glyphs.Count);
        var added = Assert.Single(drawn.Glyphs,
            g => !bare.Glyphs.Any(b => b.Glyph == g.Glyph && Math.Abs(b.FontSize - g.FontSize) < 1e-9));
        var sign = drawn.Glyphs.Where(g => !SameGlyph(g, added) && Math.Abs(g.X - added.X) < 1.5).MinBy(g => g.Y);
        Assert.True(added.Y < sign.Y,
            $"the accidental stands at {added.Y:F3} and the ornament sign at {sign.Y:F3}: it has to be above the sign.");
        Assert.True(added.FontSize < sign.FontSize, "the accidental is drawn small");
    }

    /// <summary>With a fermata on the same note the accidental stays on its ornament, under the
    /// fermata: it takes the ornament's place in the column, not the outermost one.</summary>
    [Fact]
    public void AnOrnamentsAccidental_StaysWithItsOrnamentUnderAFermata()
    {
        var plain = RenderedGeometry.Render(Book("c''1@fermata@trill |"));
        var drawn = RenderedGeometry.Render(Book("c''1@fermata@trill(sharp) |"));
        var top = drawn.Glyphs.MinBy(g => g.Y);
        // The topmost glyph is one the plain page draws too (its fermata, pushed up), never the accidental.
        Assert.Contains(plain.Glyphs, g => g.Glyph == top.Glyph && Math.Abs(g.X - top.X) < 1e-6);
        Assert.Equal(plain.Glyphs.Count + 1, drawn.Glyphs.Count);
    }

    /// <summary>A word that is not one of the three is reported with them, and the mark is ignored;
    /// a slur that opens on a trilled note is what it always was.</summary>
    [Fact]
    public void AnOrnamentsArgument_IsOneOfThreeWords()
    {
        var said = Assert.Single(Said(Book("c''2@trill(up) e''2 |")));
        Assert.Equal(DiagnosticCodes.UnknownAnnotation, said.Code);
        Assert.Contains("'up' is not a value of '@trill'", said.Message);
        foreach (string word in AnnotationValues.OrnamentAccidentalWords)
            Assert.Contains($"'@trill({word})'", said.Message);
        RoundTrips(Book("c''2@trill(up) e''2 |"));

        foreach (string slur in new[] { "c''4@trill( d'' e'') f'' |", "c''4@trill(d'' e'') f'' |" })
        {
            Clean(Book(slur));
            var root = SyntaxTree.Parse(Book(slur)).GetRoot();
            Assert.Single(root.DescendantNodes().OfType<ArticulationSyntax>(), a => a.Name == "trill");
            Assert.Equal(2, root.DescendantNodes().OfType<SlurSyntax>().Count());
        }
    }

    /// <summary>MusicXML holds the accidental as the ornament's accidental-mark and gives it back;
    /// LilyPond gets a small markup over the ornament; the sound does not change.</summary>
    [Fact]
    public void AnOrnamentsAccidental_GoesToTheExportsAndComesBack()
    {
        string source = Book("c''2@trill(sharp) e''2@turn(flat) |");
        var xml = new LilySharp.Core.MusicXml.MusicXmlExporter().Export(SyntaxTree.Parse(source)).ToXml();
        string text = xml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
        Assert.Contains("<trill-mark /><accidental-mark>sharp</accidental-mark>", text);
        Assert.Contains("<turn /><accidental-mark>flat</accidental-mark>", text);

        var report = new LilySharp.Core.MusicXmlImport.ImportReport();
        string back = LilySharp.Core.MusicXmlImport.LysWriter.Write(
            LilySharp.Core.MusicXmlImport.MusicXmlReader.Read(xml.ToString(), report), report);
        Assert.Contains("@trill(sharp)", back);
        Assert.Contains("@turn(flat)", back);

        string ly = new LilySharp.Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(source));
        Assert.Contains("\\trill^\\markup { \\teeny \\sharp }", ly);
        Assert.Contains("\\turn^\\markup { \\teeny \\flat }", ly);

        static List<(int, int, int, int)> Notes(string s)
            => [.. Midi(s).Tracks.SelectMany(t => t.Notes).Select(n => (n.Pitch, n.Velocity, n.StartTick, n.DurationTicks))];
        Assert.Equal(Notes(Book("c''2@trill e''2@turn |")), Notes(source));
    }

    /// <summary>Inside an ornament's parentheses the editor offers the three words.</summary>
    [Theory]
    [InlineData("trill")]
    [InlineData("turn")]
    [InlineData("mordent")]
    [InlineData("prall")]
    public void InsideAnOrnamentsParentheses_TheThreeWordsAreOffered(string ornament)
        => Assert.Equal(AnnotationValues.OrnamentAccidentalWords,
            Offered($"part m {{ }}\nsection A {{ m {{ c''2@{ornament}(‸) }} }}"));

    // ───────────────────────── 4. <c' e' g'>2@arpeggio(cross) ─────────────────────────

    private const string GrandStaff = "grandStaff { staff rh  staff lh }";

    /// <summary>A right hand and a left hand, one bar each unless more is written.</summary>
    private static string Piano(string rh, string lh, string score = GrandStaff)
        => "octave absolute\ntime 4/4\npart rh { clef treble }\npart lh { clef bass }\n"
           + $"section A {{\n  rh {{ {rh} }}\n  lh {{ {lh} }}\n}}\nform {{ A }}\nscore {{ {score} }}\n";

    private static LilySharp.Core.Svg.Model.MultiStaffScore Collected(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
    }

    /// <summary>The text: the word, with <c>bracket</c> in either order, is the mark's — read
    /// by the one reader, kept to the character.</summary>
    [Theory]
    [InlineData("cross", true, false)]
    [InlineData("cross bracket", true, true)]
    [InlineData("bracket cross", true, true)]
    [InlineData("bracket", false, true)]
    public void TheWordsOfAnArpeggio_AreReadAndKept(string words, bool cross, bool bracket)
    {
        string source = Piano($"<c' e' g'>1@arpeggio({words}) |", "<c e g>1 |");
        Clean(source);
        RoundTrips(source);
        var mark = SyntaxTree.Parse(source).GetRoot().DescendantNodes().OfType<MusicMarkSyntax>().Single();
        Assert.Equal((cross, bracket), AnnotationValues.ArpeggioWords(mark));
        Assert.Equal(cross, AnnotationValues.IsArpeggioCross(mark));
        Assert.Equal(bracket, AnnotationValues.IsArpeggioBracket(mark));
    }

    /// <summary>The model: ONE arpeggio, the upper chord's, told the staff it runs down to and the
    /// lowest head there; the lower chord — which wrote nothing — is stamped as a column the line
    /// runs through. A lower chord that wrote an arpeggio of its own is taken into the line.</summary>
    [Theory]
    [InlineData("<e c g>1 |")]            // the lowest head, not the first written
    [InlineData("c,1 |")]                 // a single note
    [InlineData("<c e g>1@arpeggio |")]   // its own arpeggio is this one
    public void ACrossArpeggio_IsConnectedToTheStaffBelow(string lh)
    {
        var score = Collected(Piano("<c' e' g'>1@arpeggio(cross) |", lh));
        var arpeggio = Assert.Single(score.Arpeggios);
        Assert.True(arpeggio.Cross);
        var below = score.StaffGroups[0].Staves[1].Voices[0].Measures[0].Items[0];
        int lowest = below switch
        {
            LilySharp.Core.Svg.Model.ChordItem chord => chord.Notes.Min(n => n.StaffPosition),
            LilySharp.Core.Svg.Model.NoteItem note => note.StaffPosition,
            _ => throw new InvalidOperationException("the staff below holds a note or a chord"),
        };
        Assert.Equal((0, 1, lowest), (arpeggio.StaffIndex, arpeggio.PartnerStaffIndex, arpeggio.PartnerLowestHead));

        Assert.NotEqual(LilySharp.Core.Svg.Model.SpanArpeggioPart.None,
            LilySharp.Core.Svg.Layout.SpacingRules.ArpeggioPartOf(below));
        // The upper chord keeps its room as any rolled chord does, not by a stamp.
        var above = score.StaffGroups[0].Staves[0].Voices[0].Measures[0].Items[0];
        Assert.Equal(LilySharp.Core.Svg.Model.SpanArpeggioPart.None, above.SpanArpeggio);
    }

    /// <summary>
    /// The page: ONE wavy line, from the upper chord's top note to the lower chord's bottom
    /// note, left of both chords' ink — and when the staves stand further apart it is that much
    /// longer, still reaching both.
    /// </summary>
    [Theory]
    [InlineData("<cis' e' gis'>1@arpeggio(cross) |", "<cis e gis>1 |")]
    [InlineData("<c' e' g'>1@arpeggio(cross) |", "<cis e gis>1 |")]   // only the lower chord reaches left
    [InlineData("c''1@arpeggio(cross) |", "<c, g, e>1 |")]            // a single note above
    [InlineData("<e' g' c''>1@arpeggio(cross) |", "bes,1 |")]         // a single note below
    public void ACrossArpeggio_IsOneLineFromTheTopNoteToTheBottomNote(string rh, string lh)
    {
        var page = RenderedGeometry.Render(Piano(rh, lh));
        var pile = Assert.Single(page.ArpeggioPiles());
        var heads = page.Noteheads;

        // Device Y grows down: the top head is the smallest Y, the bottom head the largest.
        Assert.True(pile.Top <= heads.Min(h => h.Y) + 1e-6,
            $"the line's top {pile.Top:F3} has to reach the top note at {heads.Min(h => h.Y):F3}.");
        Assert.True(pile.Bottom >= heads.Max(h => h.Y) + 1e-6,
            $"the line's foot {pile.Bottom:F3} has to reach under the bottom note at {heads.Max(h => h.Y):F3}.");
        // …and no further than a glyph past either (whole glyphs are stacked up from the foot).
        Assert.True(pile.Bottom <= heads.Max(h => h.Y) + 1.0 && pile.Top >= heads.Min(h => h.Y) - 1.0 - 1e-6);

        // Left of every head and every accidental of BOTH staves, by the arpeggio's padding.
        double leftmostInk = Math.Min(heads.Min(h => h.X), page.AccidentalInkLefts().DefaultIfEmpty(double.MaxValue).Min());
        Assert.True(pile.Right <= leftmostInk - LilySharp.Core.Svg.Layout.ArpeggioEngraver.Padding + 1e-6,
            $"the line's right edge {pile.Right:F3} has to stand the padding left of the leftmost ink {leftmostInk:F3}.");
        Assert.True(pile.Right >= leftmostInk - LilySharp.Core.Svg.Layout.ArpeggioEngraver.Padding - 0.05,
            "…and no further than that: it is placed against the chord that reaches furthest left.");

        // The two chords each rolled on their own draw two lines, shorter together than the one.
        var apart = RenderedGeometry.Render(Piano(rh.Replace("(cross)", ""), lh.Replace("1 |", "1@arpeggio |")));
        if (rh.StartsWith('<') && lh.StartsWith('<'))
            Assert.True(apart.ArpeggioPiles().Sum(p => p.Copies) < pile.Copies);
    }

    /// <summary>The line follows the distance between the staves the page gives them: with the
    /// staves pushed apart it has more glyphs, and still reaches both chords.</summary>
    [Fact]
    public void ACrossArpeggio_FollowsTheDistanceBetweenTheStaves()
    {
        const string rh = "<c' e' g'>2@arpeggio(cross) ";
        var close = RenderedGeometry.Render(Piano(rh + "g'2 |", "<c e g>2 c2 |"));
        // A note far under the upper staff and one far over the lower one push them apart.
        var far = RenderedGeometry.Render(Piano(rh + "c2 |", "<c e g>2 c''2 |"));
        var near = Assert.Single(close.ArpeggioPiles());
        var wide = Assert.Single(far.ArpeggioPiles());
        Assert.True(wide.Copies > near.Copies, $"{wide.Copies} glyphs against {near.Copies}: the staves moved apart and the line did not.");

        // The chord's own six heads are the leftmost column of each page.
        foreach (var (page, pile) in new[] { (close, near), (far, wide) })
        {
            var chord = page.Noteheads.OrderBy(h => h.X).Take(6).ToList();
            Assert.True(pile.Top <= chord.Min(h => h.Y) + 1e-6 && pile.Bottom >= chord.Max(h => h.Y));
        }
    }

    /// <summary>The bracket across both staves: one spine with its two ticks, no wiggle, where the
    /// two brackets of the chords bracketed apart are two.</summary>
    [Fact]
    public void ACrossBracket_IsOneBracket()
    {
        static int Strokes(string source) => RenderedGeometry.Render(source).Lines.Count;
        int bare = Strokes(Piano("<c' e' g'>1 |", "<c e g>1 |"));
        string crossed = Piano("<c' e' g'>1@arpeggio(cross bracket) |", "<c e g>1 |");
        Clean(crossed);
        Assert.Empty(RenderedGeometry.Render(crossed).ArpeggioPiles());
        Assert.Equal(bare + 3, Strokes(crossed));
        Assert.Equal(bare + 6, Strokes(Piano("<c' e' g'>1@arpeggio(bracket) |", "<c e g>1@arpeggio(bracket) |")));

        // The spine is the one vertical stroke added: it runs from above the top head to under the bottom one.
        var page = RenderedGeometry.Render(crossed);
        var spine = page.Lines.Where(l => Math.Abs(l.X1 - l.X2) < 1e-9)
            .OrderByDescending(l => Math.Abs(l.Y1 - l.Y2)).First(l => l.X1 < page.Noteheads.Min(h => h.X)
                && Math.Abs(l.StrokeWidth - LilySharp.Core.Svg.Layout.ArpeggioEngraver.BracketThickness) < 1e-9);
        Assert.True(Math.Min(spine.Y1, spine.Y2) < page.Noteheads.Min(h => h.Y)
            && Math.Max(spine.Y1, spine.Y2) > page.Noteheads.Max(h => h.Y));
    }

    /// <summary>With nothing to run down to the arpeggio is its own chord's — the page of the plain
    /// mark — and one warning at the mark says which of the three it was.</summary>
    [Theory]
    [InlineData("<c' e' g'>1@arpeggio(cross) |", "<c e g>1 |", "staff rh  staff lh", "in no grand staff")]
    [InlineData("<c' e' g'>1 |", "<c e g>1@arpeggio(cross) |", GrandStaff, "lowest staff of its grand staff")]
    [InlineData("<c' e' g'>1@arpeggio(cross) |", "r1 |", GrandStaff, "nothing begins on the staff below")]
    [InlineData("c'2 <c' e' g'>2@arpeggio(cross) |", "<c e g>1 |", GrandStaff, "nothing begins on the staff below")]
    [InlineData("c'2 <c' e' g'>2@arpeggio(cross) |", "c4 d2 e4 |", GrandStaff, "nothing begins on the staff below")]
    public void ACrossArpeggioWithNothingBelow_IsItsOwnChords_AndSaysWhy(string rh, string lh, string score, string says)
    {
        string source = Piano(rh, lh, score);
        var said = Assert.Single(Said(source));
        Assert.Equal(DiagnosticCodes.CrossArpeggioWithoutPartner, said.Code);
        Assert.Equal(DiagnosticSeverity.Warning, said.Severity);
        Assert.Contains(says, said.Message);
        Assert.Contains("drawn on its own chord", said.Message);
        Assert.Equal("@arpeggio(cross)", source.Substring(said.Span.Start, said.Span.Length));
        RoundTrips(source);

        var arpeggio = Assert.Single(Collected(source).Arpeggios);
        Assert.Equal((true, -1), (arpeggio.Cross, arpeggio.PartnerStaffIndex));
        var drawn = Assert.Single(RenderedGeometry.Render(source).ArpeggioPiles());
        var plain = Assert.Single(RenderedGeometry.Render(source.Replace("(cross)", "")).ArpeggioPiles());
        Assert.Equal(plain.Copies, drawn.Copies);
    }

    /// <summary>A connected one says nothing; a word that is neither of the two, or one written
    /// twice, is reported with what the parentheses take, and the mark is ignored.</summary>
    [Theory]
    [InlineData("cross cross")]
    [InlineData("cross up")]
    [InlineData("both")]
    [InlineData("cross bracket bracket")]
    public void AnArpeggiosWords_AreCrossAndBracket_EachOnce(string words)
    {
        string source = Piano($"<c' e' g'>1@arpeggio({words}) |", "<c e g>1 |");
        var said = Assert.Single(Said(source));
        Assert.Equal(DiagnosticCodes.UnknownAnnotation, said.Code);
        Assert.Contains($"'{words}' is not a value of '@arpeggio'", said.Message);
        Assert.Contains("'@arpeggio(cross bracket)'", said.Message);
        RoundTrips(source);
        Assert.Empty(Collected(source).Arpeggios);
    }

    /// <summary>The room: the column of the staff below keeps the arpeggio's reach past ITS ink —
    /// the line stands the padding clear of the lower chord's accidentals, which the upper chord
    /// has none of, and clear of the notes before it; in a bar too full to have that room to
    /// spare, the columns move apart to make it.</summary>
    [Fact]
    public void TheLowerChordsAccidental_DoesNotTouchTheLine()
    {
        // Sixteen sixteenths in the bar: the columns stand as close as their ink allows.
        const string run = "c'16 d' e' f' ";
        const string low = "c16 d e f ";
        string Book(string mark) => Piano(
            $"{run}<c' e' g'>16{mark} d' e' f' {run}{run}|", $"{low}<cis eis gis>16 d e f {low}{low}|");
        var with = RenderedGeometry.Render(Book("@arpeggio(cross)"));
        var without = RenderedGeometry.Render(Book(""));
        var piles = with.ArpeggioPiles();
        Assert.Single(piles);

        var accidentals = with.AccidentalInkLefts();
        var heads = with.Noteheads;
        foreach (var pile in piles)
        {
            // The chord's three sharps are the page's only accidentals.
            double accidental = accidentals.Min();
            Assert.True(accidental - pile.Right >= LilySharp.Core.Svg.Layout.ArpeggioEngraver.Padding - 1e-6,
                $"the lower chord's accidental at {accidental:F3} stands {accidental - pile.Right:F3} from the line: it needs the padding.");
            Assert.True(accidental - pile.Right < LilySharp.Core.Svg.Layout.ArpeggioEngraver.Padding + 0.05);
            // The note before it: its head (the black head is 1.3 wide) ends left of the line.
            double before = heads.Where(h => h.X < pile.Left).Max(h => h.X);
            Assert.True(before + 1.3 <= pile.Left + 1e-6,
                $"the head before the line at {before:F3} runs into it at {pile.Left:F3}.");
        }

        // The same music without the line: the chord's column stands closer to the note before it.
        static double Tightest(RenderedGeometry g)
        {
            var xs = g.Noteheads.Select(h => Math.Round(h.X, 6)).Distinct().OrderBy(x => x).ToList();
            return xs.Zip(xs.Skip(1), (a, b) => b - a).Where(d => d > 0.5).Min();
        }
        Assert.True(Tightest(with) > 0 && Tightest(without) > 0);
        double RoomBefore(RenderedGeometry g, double accidentalLeft)
            => accidentalLeft - g.Noteheads.Where(h => h.X < accidentalLeft).Max(h => h.X);
        Assert.True(RoomBefore(with, accidentals.Min())
            > RoomBefore(without, without.AccidentalInkLefts().Min()) + 0.5,
            "the column moved right to make the arpeggio's room");
    }

    /// <summary>LilyPond is told to connect the arpeggios of that moment and no other, and both
    /// staves write theirs; MusicXML numbers the notes of the one line alike; the sound is the
    /// chords', as with the plain mark.</summary>
    [Fact]
    public void ACrossArpeggio_GoesToLilyPondAndMusicXmlOnBothStaves()
    {
        string source = Piano("<c' e' g'>2@arpeggio(cross) <c' e' g'>2@arpeggio(cross bracket) | <c' e' g'>2@arpeggio(cross) <c' e' g'>2@arpeggio |",
            "<c e g>2 <c e g>2 | r2 <c e g>2@arpeggio |");
        var tree = SyntaxTree.Parse(source);
        string ly = new LilySharp.Core.LilyPond.LilyPondExporter().Export(tree);
        Assert.Contains("\\once \\set GrandStaff.connectArpeggios = ##t <c' e' g'>2\\arpeggio "
            + "\\once \\set GrandStaff.connectChordBrackets = ##t <c' e' g'>2\\nonArpeggiato |", ly);
        Assert.Contains("<c e g>2\\arpeggio <c e g>2\\nonArpeggiato |", ly);
        // The one over a rest is the plain arpeggio, and two plain ones at a moment stay two.
        Assert.Contains("\n  <c' e' g'>2\\arpeggio <c' e' g'>2\\arpeggio |", ly);
        Assert.Contains("r2 <c e g>2\\arpeggio |", ly);
        Assert.Equal(2, ly.Split("\\once \\set GrandStaff.").Length - 1);

        string xml = new LilySharp.Core.MusicXml.MusicXmlExporter().Export(tree).ToXml()
            .ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
        // Bar 1: three notes above and three below on the one line; bar 2: three alone, three and three apart.
        Assert.Equal(6, xml.Split("<arpeggiate number=\"2\" />").Length - 1);
        Assert.Equal(9, xml.Split("<arpeggiate number=\"1\" />").Length - 1);
        Assert.Equal(1, xml.Split("<non-arpeggiate type=\"top\" number=\"2\" />").Length - 1);
        Assert.Equal(1, xml.Split("<non-arpeggiate type=\"bottom\" number=\"2\" />").Length - 1);

        static List<(int, int, int, int)> Notes(string s)
            => [.. Midi(s).Tracks.SelectMany(t => t.Notes).Select(n => (n.Pitch, n.Velocity, n.StartTick, n.DurationTicks))];
        Assert.Equal(Notes(source.Replace("(cross bracket)", "").Replace("(cross)", "")), Notes(source));
    }

    /// <summary>A plain <c>@arpeggio</c> on a single note drew nothing and said nothing (a leftover
    /// of the same day). LilyPond draws it (measured, 2.26.0): the wavy line — or the bracket —
    /// one head tall, which is what the cross spelling drew on a note with nothing below it.
    /// It takes its room, and goes to LilyPond and MusicXML and comes back.</summary>
    [Fact]
    public void APlainArpeggioOnASingleNote_IsDrawn()
    {
        const string head = "octave absolute\ntime 4/4\npart m { }\nsection A { m { ";
        const string tail = " } }\nform { A }\nscore { staff m }\n";
        string source = head + "g'2@arpeggio e'2@arpeggio(bracket) |" + tail;
        Clean(source);
        var score = Collected(source);
        Assert.Equal([(false, false), (false, true)], score.Arpeggios.Select(a => (a.Cross, a.Bracket)));
        Assert.Equal([LilySharp.Core.Svg.Model.SpanArpeggioPart.Wiggle, LilySharp.Core.Svg.Model.SpanArpeggioPart.Bracket],
            score.StaffGroups[0].Staves[0].Voices[0].Measures[0].Items.OfType<LilySharp.Core.Svg.Model.NoteItem>().Select(n => n.SpanArpeggio));

        // The wavy line is the one the cross spelling draws on its own note, where it stands.
        var drawn = Assert.Single(RenderedGeometry.Render(head + "g'1@arpeggio |" + tail).ArpeggioPiles());
        var cross = Assert.Single(RenderedGeometry.Render(head + "g'1@arpeggio(cross) |" + tail).ArpeggioPiles());
        Assert.Equal(cross.Copies, drawn.Copies);
        Assert.Empty(RenderedGeometry.Render(head + "g'1 |" + tail).ArpeggioPiles());

        var tree = SyntaxTree.Parse(source);
        Assert.Contains("g'2\\arpeggio", new LilySharp.Core.LilyPond.LilyPondExporter().Export(tree));
        string xml = new LilySharp.Core.MusicXml.MusicXmlExporter().Export(tree).ToXml().ToString();
        Assert.Contains("<arpeggiate number=\"1\" />", xml);
        Assert.Contains("@arpeggio e'2 |", new LilySharp.Core.MusicXmlImport.MusicXmlImporter().Import(xml).Item1);
    }

    /// <summary>…and MusicXML read back is the one line again: the shared number on both staves
    /// came back as a plain <c>@arpeggio</c> on each (a leftover of the same day). The chords
    /// arpeggiated each for itself at one moment stay two, and so does a number one staff holds.</summary>
    [Fact]
    public void ACrossArpeggio_ComesBackFromMusicXmlOnTheUpperStaff()
    {
        string source = Piano("<c' e' g'>2@arpeggio(cross) <d' f'>2@arpeggio | g'1@arpeggio(cross) |", "<c e g>2 <d f>2@arpeggio | <c g>1 |");
        Clean(source);
        var tree = SyntaxTree.Parse(source);
        string xml = new LilySharp.Core.MusicXml.MusicXmlExporter { Score = RenderSpecParser.FindFirst(tree) }
            .Export(tree).ToXml().ToString();
        var (lys, _) = new LilySharp.Core.MusicXmlImport.MusicXmlImporter().Import(xml);
        Assert.Contains("@arpeggio(cross) <d' f'>2@arpeggio | g'1@arpeggio(cross) |", lys);
        Assert.Contains("<c e g>2 <d f>2@arpeggio | <c g>1 |", lys);
        Clean(lys);
        // The page it draws is the one line again.
        var back = SyntaxTree.Parse(lys);
        var score = SvgGenerator.CollectScore(back, RenderSpecParser.FindFirst(back));
        Assert.Equal([1, 1, -1, -1], score.Arpeggios.Select(a => a.PartnerStaffIndex).OrderDescending());

        // A number of 2 on one staff alone is that staff's own arpeggio.
        string oneStaff = System.Text.RegularExpressions.Regex.Replace(xml,
            @"(<staff>2</staff>(?:(?!</note>).)*?)<arpeggiate number=""2"" />", "$1<arpeggiate number=\"3\" />",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        string apart = new LilySharp.Core.MusicXmlImport.MusicXmlImporter().Import(oneStaff).Item1;
        Assert.DoesNotContain("cross", apart);
        Assert.Contains("<c e g>2@arpeggio", apart);
    }

    /// <summary>An arpeggio bracket did not come back from MusicXML at all (a leftover of the same
    /// day): <c>&lt;non-arpeggiate&gt;</c> on a chord's top and bottom notes is its
    /// <c>@arpeggio(bracket)</c>, and one bracket through both staves — the top above, the bottom
    /// below, one number — is <c>@arpeggio(cross bracket)</c> on the upper staff and nothing below.
    /// A bracket on each staff at one moment stays two.</summary>
    [Fact]
    public void AnArpeggioBracket_ComesBackFromMusicXml()
    {
        string source = Piano("<c' e' g'>2@arpeggio(bracket) <d' f'>2@arpeggio(cross bracket) | <c' e' g'>2@arpeggio(bracket) g'2@arpeggio(cross bracket) |",
            "<c e g>2 <d f>2 | <c e g>2@arpeggio(bracket) <c g>2 |");
        Clean(source);
        var tree = SyntaxTree.Parse(source);
        string xml = new LilySharp.Core.MusicXml.MusicXmlExporter { Score = RenderSpecParser.FindFirst(tree) }
            .Export(tree).ToXml().ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
        var (lys, _) = new LilySharp.Core.MusicXmlImport.MusicXmlImporter().Import(xml);
        Assert.Contains("@arpeggio(bracket) <d' f'>2@arpeggio(cross bracket) | <c' e' g'>2@arpeggio(bracket) g'2@arpeggio(cross bracket) |", lys);
        Assert.Contains("<c e g>2 <d f>2 | <c e g>2@arpeggio(bracket) <c g>2 |", lys);
        Clean(lys);
        // The page it draws is the page again: two brackets through both staves, three of one chord.
        var back = SyntaxTree.Parse(lys);
        Assert.Equal(Collected(source).Arpeggios.Select(a => (a.Cross, a.Bracket, a.PartnerStaffIndex)),
            SvgGenerator.CollectScore(back, RenderSpecParser.FindFirst(back)).Arpeggios.Select(a => (a.Cross, a.Bracket, a.PartnerStaffIndex)));

        // Another program's one bracket through both staves, with the number every bracket has:
        // no staff holds both ends.
        Assert.Equal(2, xml.Split("<non-arpeggiate type=\"top\" number=\"2\" />").Length - 1);
        string defaulted = xml.Replace("number=\"2\" />", "number=\"1\" />");
        Assert.Equal(lys, new LilySharp.Core.MusicXmlImport.MusicXmlImporter().Import(defaulted).Item1);
    }


    /// <summary>
    /// What the lower staff plays decides what the upper staff draws, so an edit there redraws the
    /// line: the edited file's preview is the page a fresh compile draws, each way — the chord
    /// below taken away, put back, the word typed and deleted.
    /// </summary>
    [Fact]
    public void EditingEitherStaff_RedrawsTheLine()
    {
        string bars = string.Concat(Enumerable.Repeat("c'4 d' e' f' | ", 6));
        string low = string.Concat(Enumerable.Repeat("c4 d e f | ", 6));
        string connected = Piano("<c' e' g'>2@arpeggio(cross) g'2 | " + bars, "<cis e g>2 c2 | " + low);
        string alone = connected.Replace("<cis e g>2 c2 |", "r2 c2 |");
        string plain = connected.Replace("@arpeggio(cross)", "@arpeggio");
        string none = connected.Replace("@arpeggio(cross)", "");
        var options = new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false };
        var compiler = new IncrementalCompiler(SyntaxTree.Parse(connected), options);
        compiler.Render();
        foreach (string text in new[] { alone, connected, plain, connected, none, connected, alone, plain })
            Assert.Equal(SvgGenerator.Generate(SyntaxTree.Parse(text), options),
                compiler.RenderIncremental(SyntaxTree.Parse(text)));
    }

    /// <summary>Inside an arpeggio's parentheses the editor offers its two words, and the '@' list
    /// offers the cross-staff arpeggio beside the bracket.</summary>
    [Fact]
    public void TheEditorOffersCross()
    {
        Assert.Equal(AnnotationValues.ArpeggioArgumentWords,
            Offered("part m { }\nsection A { m { <c' e' g'>2@arpeggio(‸) } }"));
        var marks = Offered("part m { }\nsection A { m { <c' e' g'>2@‸ } }");
        Assert.Contains("arpeggio(cross)", marks);
        Assert.Contains("arpeggio(bracket)", marks);
    }
}
