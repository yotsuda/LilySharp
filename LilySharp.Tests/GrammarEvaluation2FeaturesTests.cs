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

    /// <summary>The editor offers the word after a tempo's count.</summary>
    [Theory]
    [InlineData("tempo \"Tempo I\" 4 = 92 ‸")]
    [InlineData("tempo 92 ‸")]
    [InlineData("part m { }\nsection A { tempo \"Tempo I\" 4 = 92 ‸\n m { c'1 | } }")]
    public void AfterATemposCount_BareIsOffered(string marked) => Assert.Contains("bare", Offered(marked));

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
}
