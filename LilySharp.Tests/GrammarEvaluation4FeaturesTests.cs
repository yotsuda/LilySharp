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

    // ───────────────────────── 2. transposition bes,  ·  range d, bes'' ─────────────────────────

    /// <summary>A part with <paramref name="header"/> playing <paramref name="music"/>.</summary>
    private static string Player(string header, string music = "c'1 |", string top = "", string score = "score")
        => $"{top}octave absolute\ntime 4/4\nkey c major\npart m {{ {header} }}\nsection A {{ m {{ {music} }} }}\nform {{ A }}\n"
            + score + " { staff m }\n";

    private static List<string> RangeWarnings(string source)
        => [.. Said(source).Where(d => d.Code == DiagnosticCodes.NoteOutsideInstrumentRange).Select(d => d.Message)];

    /// <summary>A <c>transposition</c> written as a pitch is the pitch a written middle C sounds,
    /// named absolutely (<c>c'</c> is middle C) — LilyPond's <c>\transposition</c>.</summary>
    [Theory]
    [InlineData("bes", 58)]     // a B♭ clarinet
    [InlineData("bes,", 46)]    // a bass clarinet, a tenor saxophone
    [InlineData("a", 57)]       // a clarinet in A
    [InlineData("g", 55)]       // an alto flute
    [InlineData("f", 53)]       // a horn, a cor anglais
    [InlineData("ees", 51)]     // an alto saxophone
    [InlineData("ees'", 63)]    // an E♭ clarinet
    [InlineData("ees,", 39)]    // a baritone saxophone
    [InlineData("fis", 54)]
    [InlineData("ais", 58)]     // only the semitones are kept: ais is bes
    [InlineData("c'", 60)]      // none
    [InlineData("c", 48)]       // 8vb
    [InlineData("c''", 72)]     // 8va
    [InlineData("c,", 36)]      // 15mb
    public void ATranspositionWrittenAsAPitch_IsWhatAWrittenMiddleCSounds(string pitch, int sounds)
    {
        string book = Player("transposition " + pitch);
        Clean(book);
        Assert.Equal(sounds, Assert.Single(Notes(book)).Key);
        Assert.Equal(sounds - 60, PartHeaderDefaults.Read(SyntaxTree.Parse(book).GetNodes<PartDeclarationSyntax>().Single()).TranspositionSemitones);
    }

    /// <summary>The octave pitches are the four markers, and a pitch replaces the preset's
    /// transposition as a marker does — <c>c'</c> says "none", which no marker could.</summary>
    [Fact]
    public void APitch_ReplacesThePresetsTransposition_AsAMarkerDoes()
    {
        foreach (var (pitch, marker) in new[] { ("c", "8vb"), ("c''", "8va"), ("c,", "15mb"), ("c'''", "15ma") })
            Assert.Equal(Notes(Player("transposition " + marker)), Notes(Player("transposition " + pitch)));

        Assert.Equal(58, Assert.Single(Notes(Player("instrument clarinet"))).Key);
        Assert.Equal(46, Assert.Single(Notes(Player("instrument clarinet transposition bes,"))).Key);
        Assert.Equal(48, Assert.Single(Notes(Player("instrument bass"))).Key);
        Assert.Equal(60, Assert.Single(Notes(Player("instrument bass transposition c'"))).Key);
    }

    /// <summary>A tab frets against a pitch as against the marker of the same octaves, on the
    /// page and in the twin — the pitch read with its marks (<c>c,</c> is two octaves, not the
    /// one of its letter).</summary>
    [Fact]
    public void ATab_FretsAgainstAPitchAsAgainstItsMarker()
    {
        static string Tab(string transposition)
            => $"octave absolute\ntime 4/4\npart m {{ tuning guitar transposition {transposition} }}\n"
                + "section A { m { e''4 g'' b'' e''' | } }\nform { A }\nscore { tab m }\n";
        // The fret numbers are text, not glyphs: the page is compared as its SVG, less the
        // source offsets (the two spellings differ in length).
        static string Page(string source)
            => System.Text.RegularExpressions.Regex.Replace(
                LilySharp.Core.Svg.SvgGenerator.Generate(SyntaxTree.Parse(source),
                    new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false }),
                "data-pos=\"[^\"]*\"", "");
        Clean(Tab("c,"));
        Assert.Equal(Page(Tab("15mb")), Page(Tab("c,")));
        Assert.NotEqual(Page(Tab("8vb")), Page(Tab("c,")));
        Assert.Equal(Ly(Tab("15mb"), out _), Ly(Tab("c,"), out var warnings));
        Assert.Empty(warnings);
        Assert.NotEqual(Ly(Tab("8vb"), out _), Ly(Tab("c,"), out _));
    }

    /// <summary>A part that states a tenor saxophone's transposition is, on every page and in the
    /// files, the tenor saxophone preset's part but for the instrument's name and sound: the
    /// player's page a ninth up in D major, the conductor's as typed, MusicXML's
    /// <c>&lt;transpose&gt;</c>, the twin's <c>\transpose</c>.</summary>
    [Fact]
    public void AStatedTransposition_IsThePresetsOnThePagesAndInTheFiles()
    {
        const string Music = "c'4 d' e' f' | g'1 |";
        const string Conductors = "score", Players = "score player pitch transposed";
        string preset = Player("instrument tenor-sax", Music, "pitch concert\n");
        string stated = Player("clef treble transposition bes,", Music, "pitch concert\n");
        Clean(stated);

        foreach (string score in new[] { Conductors, Players })
            Assert.Equal(Glyphs(Player("instrument tenor-sax", Music, "pitch concert\n", score)),
                Glyphs(Player("clef treble transposition bes,", Music, "pitch concert\n", score)));
        // The player's page is not the conductor's, nor a B♭ clarinet's: the pages above moved.
        var players = Glyphs(Player("clef treble transposition bes,", Music, "pitch concert\n", Players));
        Assert.NotEqual(Glyphs(stated), players);
        Assert.NotEqual(Glyphs(Player("clef treble transposition bes", Music, "pitch concert\n", Players)), players);
        Assert.Equal(Glyphs(stated), Glyphs(Player("clef treble", Music, "pitch concert\n", Players)));

        Assert.Equal(Notes(preset).Select(n => (n.Key, n.Start, n.Ticks)), Notes(stated).Select(n => (n.Key, n.Start, n.Ticks)));

        string xml = Xml(stated);
        Assert.Contains("<chromatic>-2</chromatic>", xml);
        Assert.Contains("<octave-change>-1</octave-change>", xml);
        static string Transpose(string x) => x[x.IndexOf("<transpose>", StringComparison.Ordinal)..x.IndexOf("</transpose>", StringComparison.Ordinal)];
        Assert.Equal(Transpose(Xml(preset)), Transpose(xml));

        string ly = Ly(stated, out var warnings);
        Assert.Empty(warnings);
        Assert.Equal(Ly(preset, out _).Replace(" \\with { midiInstrument = \"tenor sax\" }", ""), ly);
    }

    /// <summary>What is neither a pitch nor a marker is refused, case included, and the message
    /// names both.</summary>
    [Theory]
    [InlineData("Bes")]
    [InlineData("h")]
    [InlineData("bes,'")]
    [InlineData("bess")]
    [InlineData("banana")]
    [InlineData("8VB")]
    [InlineData("-2")]
    public void ATranspositionThatIsNoPitchAndNoMarker_IsAnError(string value)
    {
        var said = Said(Player("transposition " + value));
        var error = Assert.Single(said, d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("transposition"));
        Assert.Equal(DiagnosticCodes.UnknownSymbolCase, error.Code);
        Assert.Contains("the pitch a written middle C sounds", error.Message);
        Assert.Contains("15ma, 15mb, 8va, 8vb", error.Message);
    }

    /// <summary>A part's <c>range</c> is the lowest and highest pitch it sounds: LYS1052 measures
    /// its notes against it, with or without a preset, and by what they SOUND.</summary>
    [Fact]
    public void APartsRange_IsWhatItsNotesAreMeasuredAgainst()
    {
        // No preset: a part that names none was never judged.
        Assert.Empty(RangeWarnings(Player("clef treble", "b4 c' g' a' |")));
        var kids = RangeWarnings(Player("range c' g'", "b4 c' g' a' |"));
        Assert.Equal(2, kids.Count);
        Assert.Equal("this note sounds B3, below the part's range (C4-G4, as it sounds) - check its octave, or the part's range", kids[0]);
        Assert.StartsWith("this note sounds A4, above the part's range (C4-G4", kids[1]);

        // It replaces the preset's: a bass clarinet, written a ninth above its sound. Written E3
        // sounds D2, the range's low end; written D3 sounds C2; written C7 sounds B♭5, the top.
        const string Music = "e4 d c' c'''' |";
        var clarinet = RangeWarnings(Player("instrument clarinet transposition bes,", Music));
        Assert.Equal(3, clarinet.Count);   // the preset's range has not moved: all but the top note
        Assert.All(clarinet, w => Assert.Contains("below the range of the Clarinet (D3-Bb6, as it sounds)", w));
        Assert.All(clarinet, w => Assert.EndsWith("or state the part's own range beside its transposition, e.g. 'range d, bes'''", w));
        var bass = Assert.Single(RangeWarnings(Player("instrument clarinet transposition bes, range d, bes''", Music)));
        Assert.StartsWith("this note sounds C2, below the part's range (D2-Bb5", bass);
        // The preset's own advice stands where the part states no transposition of its own.
        Assert.EndsWith("check its octave, or the part's instrument",
            Assert.Single(RangeWarnings(Player("instrument clarinet", "d4 e f g |"))));

        // A written range is the writer's number: a tuning does not move it, as it moves a preset's.
        Assert.Empty(RangeWarnings(Player("instrument guitar tuning guitarDropD", "d,1 |")));
        Assert.Single(RangeWarnings(Player("instrument guitar tuning guitarDropD range e, e'''", "d,1 |")));
    }

    /// <summary>A range is two pitches, the lower first; anything else is said at the property,
    /// and the property after it is read as it stands.</summary>
    [Theory]
    [InlineData("range c'", "'c'' is not a range")]
    [InlineData("range g' c'", "'g' c'' is not a range")]
    [InlineData("range C' g'", "'C' g'' is not a range")]
    [InlineData("range 60 72", "'60' is not a range")]
    public void ARangeThatIsNotTwoPitches_IsAnError(string header, string says)
    {
        var said = Said(Player(header + " clef bass"));
        Assert.Contains(said, d => d.Code == DiagnosticCodes.UnknownSymbolCase && d.Message.StartsWith(says, StringComparison.Ordinal));
    }

    [Fact]
    public void TheRangesNeighbours_AreReadAsTheyStand()
    {
        string book = Player("clef bass range c, c'' instrument cello \"Vc.\"", "c,1 |");
        Clean(book);
        var header = PartHeaderDefaults.Read(SyntaxTree.Parse(book).GetNodes<PartDeclarationSyntax>().Single());
        Assert.Equal((36, 72), header.Range);
        Assert.Equal("cello", header.Preset);
        Assert.Equal("bass", header.ClefWord);
        // No range written: the preset's; neither: none.
        Assert.Equal((36, 84), PartHeaderDefaults.Read(SyntaxTree.Parse(Player("instrument cello")).GetNodes<PartDeclarationSyntax>().Single()).Range);
        Assert.Null(PartHeaderDefaults.Read(SyntaxTree.Parse(Player("clef bass")).GetNodes<PartDeclarationSyntax>().Single()).Range);
        // A range of one pitch is a range.
        Clean(Player("range c' c'"));
    }

    /// <summary>A chord item's close voicing is lifted into the part's range, a written one as a
    /// preset's.</summary>
    [Fact]
    public void AChordItem_SoundsInsideThePartsWrittenRange()
    {
        int Lowest(string header) => Notes(Player(header, "chord(G)1 |")).Min(n => n.Key);
        int free = Lowest("clef treble");
        Assert.True(free < 60, $"the unranged chord's lowest note is {free}");
        Assert.Equal(free + 12, Lowest("clef treble range c' c'''"));
    }

    /// <summary>The caret's status line on a pitch says what the written middle C sounds.</summary>
    [Theory]
    [InlineData("bes,", "transposition bes,: written C4 sounds B♭2, 14 semitones below the page - the written notes do not move")]
    [InlineData("ees'", "transposition ees': written C4 sounds E♭4, 3 semitones above the page - the written notes do not move")]
    [InlineData("c'", "transposition c': sounds as written - the part has no transposition, whatever its instrument")]
    [InlineData("8vb", "transposition 8vb: sounds an octave below the page (written C4 sounds C3) - the written notes do not move")]
    public void TheStatusLine_SaysWhatAPitchSounds(string value, string says)
    {
        string book = Player("instrument clarinet transposition " + value);
        Assert.Equal(says, LilySharp.Core.Editing.NoteStepper.Describe(book, SyntaxTree.Parse(book), book.IndexOf("transposition", StringComparison.Ordinal) + 15));
    }

    // ───────────────────────── 4. key none ─────────────────────────

    /// <summary>A B♭ clarinet typed at concert pitch beside a piano, under the file's
    /// <paramref name="key"/> line and the clarinet's own <paramref name="partKey"/>.</summary>
    private static string Atonal(string key, string score = "score { staff cl staff pf }", string partKey = "")
        => $"pitch concert\noctave absolute\ntime 4/4\n{key}\npart cl {{ instrument clarinet {partKey} }}\npart pf {{ clef treble }}\n"
            + "section A { cl { c'4 fis' bes' e' | f'1 | } pf { c'4 fis' bes' e' | f'1 | } }\nform { A }\n" + score + "\n";

    private const string ThePlayers = "score player pitch transposed { staff cl }";
    // The accidentals as the music font draws them (Emmentaler's own code points).
    private const char Sharp = '', Natural = '', Flat = '';

    /// <summary>`key none` prints no key signature — on the transposed page too, where a file
    /// that writes no key prints the transposition of C major — and every accidental at its
    /// note. It is the custom signature of no pitches, and the sound is the keyless file's.</summary>
    [Fact]
    public void KeyNone_PrintsNoSignature_OnATransposedPageToo()
    {
        Clean(Atonal("key none"));
        // The player's page: D G♯ C F♯ | G. No key: two sharps of D major, then G♯ and a
        // natural on the C; `key none`: the G♯ and the F♯ at their notes, no signature.
        var keyless = Glyphs(Atonal("", ThePlayers)).Select(g => g.Glyph).ToList();
        var none = Glyphs(Atonal("key none", ThePlayers)).Select(g => g.Glyph).ToList();
        Assert.Equal((3, 1), (keyless.Count(g => g == Sharp), keyless.Count(g => g == Natural)));
        Assert.Equal((2, 0), (none.Count(g => g == Sharp), none.Count(g => g == Natural)));

        foreach (string score in new[] { "score { staff cl staff pf }", ThePlayers })
            Assert.Equal(Glyphs(Atonal("key custom", score)), Glyphs(Atonal("key none", score)));
        // A `transpose` moves the notes and leaves the signature empty: no D major's two sharps.
        var moved = Glyphs(Book("c'4 e' g' b' |", "transpose d\nkey none\n")).Select(g => g.Glyph).ToList();
        Assert.Equal(2, moved.Count(g => g == Sharp));      // F♯ and C♯, at their notes
        Assert.Equal(2, Glyphs(Book("c'4 e' g' b' |", "transpose d\n")).Count(g => g.Glyph == Sharp));   // the signature's
        Assert.NotEqual(Glyphs(Book("c'4 e' g' b' |", "transpose d\n")), Glyphs(Book("c'4 e' g' b' |", "transpose d\nkey none\n")));
        Assert.Equal(Notes(Atonal("")), Notes(Atonal("key none")));
        Assert.Equal(Notes(Atonal("", ThePlayers)), Notes(Atonal("key none", ThePlayers)));
    }

    /// <summary>The word stands wherever a key does — the file's top, a section's head, a part's
    /// header, the music — and a part's own `key none` is that part's alone.</summary>
    [Fact]
    public void KeyNone_StandsWhereverAKeyDoes()
    {
        string own = Atonal("key f major", partKey: "key none");
        Clean(own);
        Assert.Equal(Glyphs(Atonal("key f major", partKey: "key custom")), Glyphs(own));
        var flats = Glyphs(own).Where(g => g.Glyph == Flat).ToList();
        // The piano's signature, and the clarinet's B♭ at its note (the piano's is in its key).
        Assert.Equal(2, flats.Count);
        Assert.NotEqual(Glyphs(Atonal("key f major")), Glyphs(own));

        string inSection = Atonal("key f major").Replace("section A { cl", "section A { key none cl");
        Clean(inSection);
        Assert.Equal(Glyphs(Atonal("key none")), Glyphs(inSection));

        // In the music: the signature is cancelled where the word stands.
        string inMusic = Book("c'4 d' e' f' | key none fis'4 g' a' b' |", "key d major\n");
        Clean(inMusic);
        Assert.Equal(Glyphs(inMusic.Replace("key none", "key custom")), Glyphs(inMusic));
        Assert.NotEqual(Glyphs(inMusic.Replace("key none ", "")), Glyphs(inMusic));

        var key = SyntaxTree.Parse(Atonal("key none")).GetNodes<KeySignatureSyntax>().Single();
        Assert.True(key.IsNone);
        Assert.True(key.IsCustom);
        Assert.Empty(key.CustomAlterations);
        Assert.Equal("none", key.DisplayName);
        Assert.False(SyntaxTree.Parse(Atonal("key custom")).GetNodes<KeySignatureSyntax>().Single().IsNone);
    }

    /// <summary>The word takes nothing after it, and is case-sensitive.</summary>
    [Theory]
    [InlineData("key none major")]
    [InlineData("key None")]
    [InlineData("key none fis")]
    public void KeyNone_TakesNothingAfterIt(string key)
        => Assert.Contains(Said(Atonal(key)), d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>The files say no signature, and MusicXML read back says `key none` again.</summary>
    [Fact]
    public void KeyNone_IsNoSignatureInTheFiles_AndComesBack()
    {
        string book = Atonal("key none");
        string ly = Ly(book, out var warnings);
        Assert.Empty(warnings);
        Assert.Contains("\\set Staff.keyAlterations = #`()", ly);
        Assert.DoesNotContain("\\key", ly);

        string xml = Xml(book);
        Assert.Contains("<key />", xml);
        Assert.DoesNotContain("<fifths>", xml);
        var (back, _) = new LilySharp.Core.MusicXmlImport.MusicXmlImporter().Import(xml);
        Assert.Contains("\nkey none\n", back.ReplaceLineEndings("\n"));
        // A signature that names pitches is still not read, and says so.
        var (_, report) = new LilySharp.Core.MusicXmlImport.MusicXmlImporter().Import(Xml(Atonal("key custom fis")));
        Assert.Contains(report.Warnings, w => w.Contains("non-traditional key signature dropped"));
    }

    /// <summary>`lysc review` says the piece has no key and guesses none; the editor's status
    /// line and its list after `key` know the word.</summary>
    [Fact]
    public void KeyNone_IsSaidByTheReviewAndTheEditor()
    {
        // Long enough for the review to hear a key in it.
        const string Row = "c'4 cis' d' ees' | e' f' fis' g' | aes' a' bes' b' | c''4 fis' b' f' | ees'4 a' d' aes' | des'1 |";
        static string Review(string key) => LilySharp.Core.Editing.ScoreReview.Text(Book(Row, key)).ReplaceLineEndings("\n");
        string head = Review("key none\n");
        Assert.Contains(" - no key, 4/4", head.Split('\n')[0]);
        Assert.DoesNotContain("Heard in:", head);
        Assert.Contains("Heard in:", Review(""));
        Assert.Contains("Heard in:", Review("key custom\n"));
        Assert.DoesNotContain("no key", Review("key custom\n"));

        string book = Atonal("key none");
        Assert.Equal("key none: no key signature - on a transposed page too; every accidental is written at its note",
            LilySharp.Core.Editing.NoteStepper.Describe(book, SyntaxTree.Parse(book), book.IndexOf("key none", StringComparison.Ordinal) + 5));

        var offered = LilySharpLanguageServer.GetKeyTonicCompletions().Items;
        Assert.Equal("none", offered[^1].Label);
        Assert.Null(offered[^1].Command);
        Assert.Equal(16, offered.Length);
    }
}
