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
/// The spellings the four scores of grammar evaluation 4 asked for and the owner approved
/// (2026-11-15; the proposals are the Lab's <c>sessions/p1227/proposals-eval4.md</c>). Each is
/// held from the text to the page and the files.
/// </summary>
public class GrammarEvaluation4FeaturesTests
{
    private static List<Diagnostic> Said(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return [.. tree.Diagnostics, .. SemanticValidation.Run(tree)];
    }

    private static void Clean(string source) => Assert.Empty(Said(source).Select(d => d.Message));

    private static string Book(string music, string more = "")
        => $"octave absolute\ntime 4/4\npart m {{ }}\n{more}section A {{ m {{ {music} }} }}\nform {{ A }}\nscore {{ staff m }}\n";

    /// <summary>Every glyph of the page, where it is drawn.</summary>
    private static List<(char Glyph, double X, double Y)> Glyphs(string source)
        => [.. RenderedGeometry.Render(source).Glyphs.Select(g => (g.Glyph, Math.Round(g.X, 6), Math.Round(g.Y, 6)))];

    private static string Ly(string source, out IReadOnlyList<string> warnings)
    {
        var exporter = new LilySharp.Core.LilyPond.LilyPondExporter();
        string ly = exporter.Export(SyntaxTree.Parse(source));
        warnings = [.. exporter.Warnings];
        return ly;
    }

    private static string Xml(string source)
        => new LilySharp.Core.MusicXml.MusicXmlExporter().Export(SyntaxTree.Parse(source)).ToXml().ToString();

    private static List<(int Key, int Velocity, int Start, int Ticks)> Notes(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindFirst(tree);
        var midi = new LilySharp.Core.Midi.MidiExporter { Form = spec?.Form, Score = spec }.Export(tree);
        return [.. midi.Tracks.SelectMany(t => t.Notes).Select(n => (n.Pitch, n.Velocity, n.StartTick, n.DurationTicks))];
    }

    // ───────────────────────── 6. c'8@notehead(cross) d' e' f'@!notehead ─────────────────────────

    /// <summary>A run of a head style is, on the page and in the files, the style written on each
    /// of its notes — chords, a <c>q</c>, a tuplet's notes and a bar line inside it included, a
    /// rest and a grace note left as they are, a run of marks beside it its own.</summary>
    [Theory]
    [InlineData("c'8@notehead(cross) d' e' f'@!notehead g'4 a' |",
        "c'8@notehead(cross) d'@notehead(cross) e'@notehead(cross) f'@notehead(cross) g'4 a' |", "c'8 d' e' f' g'4 a' |")]
    [InlineData("c'4@notehead(diamond) <e' g'> q b'@!notehead |",
        "c'4@notehead(diamond) <e' g'>@notehead(diamond) q@notehead(diamond) b'@notehead(diamond) |", "c'4 <e' g'> q b' |")]
    [InlineData("c'4@notehead(x) r d'2 | e'4@!notehead f' g'2 |",
        "c'4@notehead(x) r d'2@notehead(x) | e'4@notehead(x) f' g'2 |", "c'4 r d'2 | e'4 f' g'2 |")]
    [InlineData("tuplet 3/2 { c'8@notehead(slash) d' e' } f'4 g'2@!notehead |",
        "tuplet 3/2 { c'8@notehead(slash) d'@notehead(slash) e'@notehead(slash) } f'4@notehead(slash) g'2@notehead(slash) |",
        "tuplet 3/2 { c'8 d' e' } f'4 g'2 |")]
    [InlineData("c'4@notehead(cross) grace { d'16 } e'4 f'@!notehead g' |",
        "c'4@notehead(cross) grace { d'16 } e'4@notehead(cross) f'@notehead(cross) g' |", "c'4 grace { d'16 } e'4 f' g' |")]
    [InlineData("c'4@notehead(xcircle)@staccato d' e'@!staccato f'@!notehead |",
        "c'4@notehead(xcircle)@staccato d'@notehead(xcircle)@staccato e'@notehead(xcircle)@staccato f'@notehead(xcircle) |",
        "c'4@staccato d'@staccato e'@staccato f' |")]
    // The notes of a broken chord are each a note of the run.
    [InlineData("<< c'@notehead(cross) e' g' c''@!notehead >>1 |",
        "<< c'@notehead(cross) e'@notehead(cross) g'@notehead(cross) c''@notehead(cross) >>1 |", "<< c' e' g' c'' >>1 |")]
    // The nearest opening is the one an end closes, and a note in two runs takes the inner one's.
    [InlineData("c'4@notehead(cross) d' e'@notehead(triangle) f'@!notehead | g'1@!notehead |",
        "c'4@notehead(cross) d'@notehead(cross) e'@notehead(triangle) f'@notehead(triangle) | g'1@notehead(cross) |",
        "c'4 d' e' f' | g'1 |")]
    public void ARunOfAHeadStyle_IsTheStyleOnEachOfItsNotes(string run, string spelledOut, string plain)
    {
        Clean(Book(run));
        Assert.Equal(Book(run), SyntaxTree.Parse(Book(run)).GetRoot().ToFullString());

        Assert.Equal(Glyphs(Book(spelledOut)), Glyphs(Book(run)));
        Assert.NotEqual(Glyphs(Book(plain)), Glyphs(Book(run)));

        // The files write the style on each note: the same as the one spelled out. And nothing
        // is reported lost — the end is not "dropped".
        Assert.Equal(Ly(Book(spelledOut), out _), Ly(Book(run), out var warnings));
        Assert.Empty(warnings);
        Assert.Equal(Xml(Book(spelledOut)), Xml(Book(run)));

        // A head style is the page's: the run sounds as the notes with no style do.
        Assert.Equal(Notes(Book(plain)), Notes(Book(run)));
    }

    /// <summary>Without its end a head style is one note's, as it always was.</summary>
    [Fact]
    public void AHeadStyleWithNoEnd_IsOneNotes()
    {
        string one = Book("c'4@notehead(cross) d' e' f' |");
        Assert.Empty(ArticulationRuns.Of(SyntaxTree.Parse(one).GetRoot()).Runs);
        Assert.Single(Xml(one).Split("<notehead>").Skip(1));
        Assert.Equal(4, Xml(Book("c'4@notehead(cross) d' e' f'@!notehead |")).Split("<notehead>").Length - 1);
    }

    /// <summary>The end is written bare, and one with no opening before it in its own music is an
    /// error that says what to write — a mark's run does not open a head's.</summary>
    [Theory]
    [InlineData("c'4 d' e' f'@!notehead |")]
    [InlineData("c'4@staccato d' e' f'@!notehead |")]
    [InlineData("c'4@notehead(cross) d' voice { e'4 f'@!notehead } { c'2 } |")]
    public void AHeadStylesEndWithNoOpening_IsAnError(string music)
    {
        string source = Book(music);
        var said = Assert.Single(Said(source));
        Assert.Equal(DiagnosticCodes.UnpairedSpan, said.Code);
        Assert.Equal(DiagnosticSeverity.Error, said.Severity);
        Assert.Contains("c8@notehead(cross) d e f@!notehead", said.Message);
        Assert.Contains("no '@notehead(style)' is written before it", said.Message);
        Assert.Equal("@!notehead", source.Substring(said.Span.Start, said.Span.Length));
    }

    /// <summary>A phrase called inside the run is not reached, and that is said.</summary>
    [Fact]
    public void AHeadStylesRunAndAPhrase()
    {
        string across = Book("c'2@notehead(cross) riff d'2@!notehead r2 |", "phrase riff { e'2 | }\n");
        var said = Assert.Single(Said(across));
        Assert.Equal(DiagnosticSeverity.Warning, said.Severity);
        Assert.Contains("the '@notehead' run that '@!notehead' closes does not reach into 'riff'", said.Message);
    }

    /// <summary>The end typed after its opening is already on the page changes the notes before
    /// the keystroke: the preview is the page a fresh compile draws, both ways.</summary>
    [Fact]
    public void TypingAHeadStylesEnd_RedrawsTheNotesBeforeIt()
    {
        string bars = string.Concat(Enumerable.Repeat("c'4 d' e' f' | ", 6));
        string before = Book("c'4@notehead(cross) d' e' f' | g'4 a' b' c'' | " + bars);
        string after = before.Replace("b' c'' |", "b'@!notehead c'' |");
        var options = new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false };
        var compiler = new LilySharp.Core.Svg.IncrementalCompiler(SyntaxTree.Parse(before), options);
        compiler.Render();
        foreach (string text in new[] { after, before, after })
            Assert.Equal(LilySharp.Core.Svg.SvgGenerator.Generate(SyntaxTree.Parse(text), options),
                compiler.RenderIncremental(SyntaxTree.Parse(text)));
    }

    /// <summary>After <c>@!</c> the editor offers the head style's end with the marks'.</summary>
    [Fact]
    public void AfterTheEndMark_TheHeadStyleIsOffered()
    {
        const string marked = "part m { }\nsection A { m { c'8@notehead(cross) d' e' f'@!‸ } }";
        int offset = marked.IndexOf('‸');
        string text = marked.Remove(offset, 1);
        var uri = new Uri("file:///features4.lys");
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        var offered = (server.Completion(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(line, character),
        })?.Items ?? []).Select(i => i.Label!).ToList();
        Assert.Contains("notehead", offered);
        Assert.Contains("staccato", offered);
    }

    // ───────────────────────── 7. condensedStaff "Tpts 1-2" "Tpts" { tp1 tp2 } ─────────────────────────

    /// <summary>A score of a flute and one staff of two parts, long enough for a second system.</summary>
    private static string Ensemble(string sharedStaff)
    {
        string bars = string.Concat(Enumerable.Repeat("c''4 d'' e'' f'' | ", 24));
        string low = string.Concat(Enumerable.Repeat("c'4 d' e' f' | ", 24));
        return "octave absolute\ntime 4/4\npart fl \"Flute\" \"Fl.\" { }\npart tp1 { }\npart tp2 { }\n"
            + $"section A {{ fl {{ {bars}}} tp1 {{ {bars}}} tp2 {{ {low}}} }}\nform {{ A }}\n"
            + $"score {{ staff fl {sharedStaff} }}\n";
    }

    /// <summary>The texts the page draws, by how many times each is drawn.</summary>
    private static Dictionary<string, int> Drawn(string source)
        => RenderedGeometry.Render(source).Texts.GroupBy(t => t.Text).ToDictionary(g => g.Key, g => g.Count());

    /// <summary>The strings before the brace are the staff's name and short name, after the clef
    /// word when there is one; the text reads back as written and the parts are the same parts.</summary>
    [Theory]
    [InlineData("condensedStaff \"Tpts 1-2\" \"Tpts\" { tp1 tp2 }", null, "Tpts 1-2", "Tpts")]
    [InlineData("condensedStaff \"Tpts 1-2\" { tp1 tp2 }", null, "Tpts 1-2", null)]
    [InlineData("condensedStaff bass \"Vc. / Cb.\" { tp1 tp2 }", "bass", "Vc. / Cb.", null)]
    [InlineData("combinedStaff \"Tpts 1-2\" \"Tpts\" { tp1 tp2 }", null, "Tpts 1-2", "Tpts")]
    [InlineData("combinedStaff alto \"A \\\"B\\\"\" \"A\" { tp1 tp2 }", "alto", "A \"B\"", "A")]
    [InlineData("condensedStaff bass { tp1 tp2 }", "bass", null, null)]
    [InlineData("combinedStaff { tp1 tp2 }", null, null, null)]
    public void AStaffOfSeveralParts_TakesItsNameBeforeItsBrace(string row, string? clef, string? name, string? shortName)
    {
        string source = Ensemble(row);
        Clean(source);
        var tree = SyntaxTree.Parse(source);
        Assert.Equal(source, tree.GetRoot().ToFullString());

        var read = tree.GetNodes<SyntaxNode>().Select(n => n switch
        {
            CondensedStaffRenderSyntax c => (c.ClefToken?.Text, c.DisplayName, c.ShortName, string.Join(" ", c.PartNames)),
            CombinedStaffRenderSyntax c => (c.ClefToken?.Text, c.DisplayName, c.ShortName, string.Join(" ", c.PartNames)),
            _ => ((string?, string?, string?, string)?)null,
        }).Single(r => r != null);
        Assert.Equal((clef, name, shortName, "tp1 tp2"), read);
    }

    /// <summary>The name stands left of the first system and the short name left of each later
    /// one, as a part's two names do; one string names the first system alone; none — or an
    /// empty one — names nothing, and the other staves' names stay what they were.</summary>
    [Theory]
    [InlineData("condensedStaff")]
    [InlineData("combinedStaff")]
    public void TheName_IsOnTheFirstSystem_AndTheShortNameOnTheLaterOnes(string keyword)
    {
        var unnamed = Drawn(Ensemble(keyword + " { tp1 tp2 }"));
        int laterSystems = unnamed["Fl."];
        Assert.True(laterSystems >= 1, "the probe needs a second system");
        Assert.Equal(1, unnamed["Flute"]);

        var both = Drawn(Ensemble(keyword + " \"Tpts 1-2\" \"Tpts\" { tp1 tp2 }"));
        Assert.Equal(1, both["Tpts 1-2"]);
        Assert.Equal(laterSystems, both["Tpts"]);
        Assert.Equal((1, laterSystems), (both["Flute"], both["Fl."]));

        var one = Drawn(Ensemble(keyword + " \"Tpts 1-2\" { tp1 tp2 }"));
        Assert.Equal(1, one["Tpts 1-2"]);
        Assert.Equal(unnamed.Count + 1, one.Count);

        // No name written is no name printed: never the parts' own, never a default.
        Assert.DoesNotContain(unnamed.Keys, t => t.Contains("tp", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(unnamed, Drawn(Ensemble(keyword + " \"\" { tp1 tp2 }")));
    }

    /// <summary>The name makes room for itself: the first system's music starts further right
    /// under a long name than under none, as it does for a staff's.</summary>
    [Fact]
    public void ALongName_IndentsTheFirstSystem()
    {
        // The first system is the top one: its leftmost glyph is its first clef.
        static double FirstClefX(string source)
        {
            var glyphs = RenderedGeometry.Render(source).Glyphs;
            double top = glyphs.Min(g => g.Y);
            return glyphs.Where(g => g.Y < top + 10).Min(g => g.X);
        }
        Assert.True(FirstClefX(Ensemble("condensedStaff \"Trumpets in B-flat 1 and 2\" { tp1 tp2 }"))
            > FirstClefX(Ensemble("condensedStaff { tp1 tp2 }")) + 1.0);
    }

    /// <summary>A staff of several parts that is the score's only staff prints its name too.</summary>
    [Fact]
    public void TheOnlyStaffOfAScore_PrintsItsName()
    {
        string alone = Ensemble("condensedStaff \"Tpts 1-2\" \"Tpts\" { tp1 tp2 }").Replace("staff fl ", "");
        Clean(alone);
        var drawn = Drawn(alone);
        Assert.Equal(1, drawn["Tpts 1-2"]);
        Assert.True(drawn["Tpts"] >= 1);
    }

    /// <summary>LilyPond's file names the staff the same way; MIDI is the unnamed score's.</summary>
    [Theory]
    [InlineData("condensedStaff", "<< ")]
    [InlineData("combinedStaff", "\\partCombine ")]
    public void TheTwinNamesTheStaff_AndTheSoundIsTheSame(string keyword, string music)
    {
        string named = Ensemble(keyword + " \"Tpts 1-2\" \"Tpts\" { tp1 tp2 }");
        string ly = Ly(named, out var warnings);
        Assert.Empty(warnings);
        Assert.Contains("\\new Staff \\with { instrumentName = \"Tpts 1-2\" shortInstrumentName = \"Tpts\" } { " + music, ly);
        Assert.Contains("\\new Staff \\with { instrumentName = \"Tpts 1-2\" } { " + music,
            Ly(Ensemble(keyword + " \"Tpts 1-2\" { tp1 tp2 }"), out _));

        string plain = Ensemble(keyword + " { tp1 tp2 }");
        Assert.Contains("\\new Staff { " + music, Ly(plain, out _));
        Assert.Equal(Notes(plain), Notes(named));
    }

    /// <summary>The caret after the name is still before this staff's brace, and inside the
    /// braces the editor still offers bare part names.</summary>
    [Theory]
    [InlineData("score { condensedStaff \"Tpts 1-2\" \"Tpts\" { ")]
    [InlineData("score { combinedStaff bass \"Vc. / Cb.\" { tp1 ")]
    public void InsideANamedStaffsBraces_ThePartNamesAreOffered(string text)
    {
        Assert.Equal(LilySharpLanguageServer.CompletionContext.BarePartNameList,
            LilySharpLanguageServer.GetCompletionContext(text, text.Length));
    }

    // ───────────────────────── 8-1. |: … :|*4 in the music prints "4×" ─────────────────────────

    /// <summary>Two parts, an upper and a lower, each with its own music of section A.</summary>
    private static string Duo(string upper, string lower, string score = "staff a staff b")
        => "octave absolute\ntime 4/4\npart a { }\npart b { clef bass }\n"
            + $"section A {{ a {{ {upper} }} b {{ {lower} }} }}\nform {{ A }}\nscore {{ {score} }}\n";

    /// <summary>The count marks of the laid-out page: text and where it stands.</summary>
    private static List<(string Text, double X, double YUp)> Counts(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var layout = new LilySharp.Core.Svg.Layout.LayoutEngine()
            .Layout(LilySharp.Core.Svg.SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
        return [.. layout.MusicMarkLayouts
            .Where(m => m.MarkType == LilySharp.Core.Svg.Model.MusicMarkType.RepeatCount)
            .Select(m => (m.Text ?? "", Math.Round(m.X, 6), Math.Round(m.YUp, 6)))];
    }

    /// <summary>A repeat the music writes says its count where a form's does: "4×" over the bar
    /// its <c>:|*4</c> closes, once a score — the same mark, at the same place, as the same bars
    /// repeated by the form. Twice, said or not, prints nothing.</summary>
    [Fact]
    public void ARepeatOfTheMusicPlayedMoreThanTwice_PrintsItsCountAsAFormsDoes()
    {
        string inMusic = Duo("c'1 |: d'1 | e'1 :|*4 f'1 |", "c1 | d1 | e1 | f1 |");
        Clean(inMusic);
        string byForm = "octave absolute\ntime 4/4\npart a { }\npart b { clef bass }\n"
            + "section A { a { c'1 | } b { c1 | } }\nsection B { a { d'1 | e'1 | } b { d1 | e1 | } }\nsection C { a { f'1 | } b { f1 | } }\n"
            + "form { A |: B :|*4 C }\nscore { staff a staff b }\n";
        var count = Assert.Single(Counts(inMusic));
        Assert.Equal("4×", count.Text);
        var formCount = Assert.Single(Counts(byForm));
        Assert.Equal((formCount.Text, formCount.X), (count.Text, count.X));
        Assert.Equal(1, Drawn(inMusic)["4×"]);

        Assert.Empty(Counts(Duo("c'1 |: d'1 | e'1 :| f'1 |", "c1 | d1 | e1 | f1 |")));
        Assert.Empty(Counts(Duo("c'1 |: d'1 | e'1 :|*2 f'1 |", "c1 | d1 | e1 | f1 |")));

        // Each repeat says its own, at its own bar.
        var two = Counts(Duo("|: c'1 :|*3 d'1 |: e'1 :|*5 f'1 |", "c1 | d1 | e1 | f1 |"));
        Assert.Equal(new[] { "3×", "5×" }, two.OrderBy(c => c.X).Select(c => c.Text));

        // MusicXML's own count, on every part; and four passes sound.
        Assert.Equal(2, Xml(inMusic).Split("<repeat direction=\"backward\" times=\"4\" />").Length - 1);
        Assert.Equal(2 * (1 + 4 * 2 + 1), Notes(inMusic).Count);
    }

    /// <summary>The repeat and the marks of a section are the score's: written in every part
    /// they are printed once, and written in a part the score does not draw they are printed
    /// on the staff it does. (Two parts' <c>fine</c> printed "Fine" twice until session 1271.)</summary>
    [Theory]
    [InlineData("|: c'1 | d'1 :|*3 e'1 fine |", "|: c1 | d1 :|*3 e1 fine |", "staff a staff b")]
    [InlineData("c'1 | d'1 | e'1 |", "|: c1 | d1 :|*3 e1 fine |", "staff a staff b")]
    [InlineData("|: c'1 | d'1 :|*3 e'1 fine |", "c1 | d1 | e1 |", "staff a staff b")]
    [InlineData("c'1 | d'1 | e'1 |", "|: c1 | d1 :|*3 e1 fine |", "staff a")]
    [InlineData("|: c'1 | d'1 :|*3 e'1 fine |", "|: c1 | d1 :|*3 e1 fine |", "staff a")]
    public void TheCountAndTheMarksOfTheScore_ArePrintedOnce_WhoeverWritesThem(string upper, string lower, string score)
    {
        string source = Duo(upper, lower, score);
        Clean(source);
        var drawn = Drawn(source);
        Assert.Equal((1, 1), (drawn["3×"], drawn["Fine"]));
        Assert.Equal("3×", Assert.Single(Counts(source)).Text);
    }

    /// <summary>One part on a staff and on a tab is walked twice, and its marks are still the
    /// score's: a <c>segno</c> and a count of its music are printed once. (Four of the owner's
    /// bass books printed the sign twice, one above the other, until session 1271.)</summary>
    [Fact]
    public void APartOnAStaffAndATab_PrintsItsMarksOnce()
    {
        string source = "octave absolute\ntime 4/4\npart m { instrument bass }\n"
            + "section A { m { |: e,1 | a,1 :|*3 segno d1 | g1 fine | } }\nform { A }\nscore { staff m tab m }\n";
        Clean(source);
        var tree = SyntaxTree.Parse(source);
        var layout = new LilySharp.Core.Svg.Layout.LayoutEngine()
            .Layout(LilySharp.Core.Svg.SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
        foreach (var type in new[] { LilySharp.Core.Svg.Model.MusicMarkType.Segno,
                     LilySharp.Core.Svg.Model.MusicMarkType.Fine, LilySharp.Core.Svg.Model.MusicMarkType.RepeatCount })
            Assert.Single(layout.MusicMarkLayouts, m => m.MarkType == type);
    }

    /// <summary>A chords row's signs are the score's too: the count of its <c>:|*N</c> and its
    /// navigation marks are printed where a part's would be — the same marks at the same
    /// places as the part writing them — and once when a part writes them as well. (A row's
    /// signs repeated and jumped, and the page said neither, until session 1271.)</summary>
    [Fact]
    public void AChordsRowsCountAndMarks_ArePrintedAsAPartsAre()
    {
        static string Lead(string row, string melody)
            => "octave absolute\ntime 4/4\npart a { }\n"
                + $"chords prog {{ section A {{ {row} }} }}\nsection A {{ a {{ {melody} }} }}\nform {{ A }}\nscore {{ chords prog staff a }}\n";
        static List<(string, double, double)> Marks(string source)
        {
            var tree = SyntaxTree.Parse(source);
            var layout = new LilySharp.Core.Svg.Layout.LayoutEngine()
                .Layout(LilySharp.Core.Svg.SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
            return [.. layout.MusicMarkLayouts
                .Where(m => m.MarkType is LilySharp.Core.Svg.Model.MusicMarkType.RepeatCount
                    or LilySharp.Core.Svg.Model.MusicMarkType.Fine or LilySharp.Core.Svg.Model.MusicMarkType.Segno)
                .Select(m => (m.Text ?? m.MarkType.ToString(), Math.Round(m.X, 4), Math.Round(m.YUp, 4)))
                .OrderBy(m => m)];
        }

        const string Signs = "|: {0} | {1} :|*4 segno {2} | {3} fine |";
        string byRow = Lead(string.Format(Signs, "C", "G", "Am", "F"), "c'1 | d'1 | e'1 | f'1 |");
        string byPart = Lead("C | G | Am | F |", string.Format(Signs, "c'1", "d'1", "e'1", "f'1"));
        string byBoth = Lead(string.Format(Signs, "C", "G", "Am", "F"), string.Format(Signs, "c'1", "d'1", "e'1", "f'1"));
        Clean(byRow);
        Clean(byBoth);

        var marks = Marks(byPart);
        Assert.Equal(3, marks.Count);
        Assert.Contains(marks, m => m.Item1 == "4×");
        Assert.Equal(marks, Marks(byRow));
        Assert.Equal(marks, Marks(byBoth));

        // A text mark written right after a bar line ends the bar before it, in a row as in a part.
        const string AtTheBar = "{0} | {1} | fine {2} | {3} |";
        string rowAtTheBar = Lead(string.Format(AtTheBar, "C", "G", "Am", "F"), "c'1 | d'1 | e'1 | f'1 |");
        Clean(rowAtTheBar);
        var atTheBar = Marks(Lead("C | G | Am | F |", string.Format(AtTheBar, "c'1", "d'1", "e'1", "f'1")));
        Assert.Single(atTheBar);
        Assert.Equal(atTheBar, Marks(rowAtTheBar));
        Assert.NotEqual(atTheBar, Marks(Lead("C | G | Am fine | F |", "c'1 | d'1 | e'1 | f'1 |")));
    }

    /// <summary>Typing the count onto a repeat already on the page, and taking it off, redraws
    /// the page a fresh compile draws.</summary>
    [Fact]
    public void TypingTheCount_RedrawsThePage()
    {
        string bars = string.Concat(Enumerable.Repeat("c'4 d' e' f' | ", 6));
        string before = Duo("|: c'1 | d'1 :| " + bars, "|: c1 | d1 :| " + bars.Replace("'", ""));
        string after = before.Replace("d'1 :|", "d'1 :|*4");
        var options = new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false };
        var compiler = new LilySharp.Core.Svg.IncrementalCompiler(SyntaxTree.Parse(before), options);
        compiler.Render();
        foreach (string text in new[] { after, before, after })
            Assert.Equal(LilySharp.Core.Svg.SvgGenerator.Generate(SyntaxTree.Parse(text), options),
                compiler.RenderIncremental(SyntaxTree.Parse(text)));
    }
}
