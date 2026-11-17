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

using System.IO;
using System.Linq;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;
using DiagnosticSeverity = LilySharp.Core.Syntax.DiagnosticSeverity;

namespace LilySharp.Tests;

/// <summary>
/// A break is the SCORE's (owner's decision 2026-11-13): <c>break</c> / <c>noBreak</c> /
/// <c>pageBreak</c> / <c>noPageBreak</c> may be written in a <c>chords</c> track, at a bar
/// line, and one written in ANY part or chords track of the piece — shown by the score being
/// drawn or not — is honoured. Until then a chords track refused the words (LYS1028) and the
/// layout read the first staff's measures alone, so a break in a second part was dropped in
/// silence (grammar evaluation 4, the chamber writer's r-06d).
/// </summary>
/// <remarks>
/// The page (every voice's <see cref="Measure.LineBreakPermission"/>, which is what the SVG,
/// the PDF and the PNG are laid out from), <c>lysc layout</c> and the <c>.ly</c> twin are held
/// to one answer here. MusicXML writes no <c>&lt;print new-system&gt;</c> and the MIDI has no
/// page, so both are held to "unchanged by the words".
/// </remarks>
[Trait("Category", "Unit")]
public sealed class BreaksAnywhereTests
{
    private static MultiStaffScore Collect(string src, string? score = null)
    {
        var tree = SyntaxTree.Parse(src);
        Assert.Empty(tree.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        return SvgGenerator.CollectScore(tree,
            score == null ? RenderSpecParser.FindFirst(tree) : RenderSpecParser.FindByName(tree, score));
    }

    /// <summary>Each voice of the score with its bars' (line, page) permissions.</summary>
    private static (string Voice, BreakPermission[] Line, BreakPermission[] Page)[] Permissions(MultiStaffScore score)
        => score.StaffGroups.SelectMany(g => g.Staves).SelectMany(s => s.Voices)
            .Select(v => (v.Name, v.Measures.Select(m => m.LineBreakPermission).ToArray(),
                v.Measures.Select(m => m.PageBreakPermission).ToArray()))
            .ToArray();

    /// <summary>The 1-based bars a line break is forced after, as every voice has them.</summary>
    private static int[] ForcedAfter(MultiStaffScore score)
    {
        var all = Permissions(score);
        Assert.NotEmpty(all);
        var first = Forced(all[0].Line);
        foreach (var (voice, line, _) in all)
            Assert.True(first.SequenceEqual(Forced(line)), $"voice {voice} breaks elsewhere");
        return first;

        static int[] Forced(BreakPermission[] line)
            => line.Select((p, i) => (p, i)).Where(t => t.p == BreakPermission.Force).Select(t => t.i + 1).ToArray();
    }

    private static string Report(string src, bool all = false) => LayoutReport.Generate(SyntaxTree.Parse(src), all);

    private const string ChordsOnly = """
        time 4/4
        chords h { C | F | break G | C | Am break | F | G pageBreak | C | noBreak C | }
        score { chords h }
        """;

    // ───────────────────────── a chords track takes the words ─────────────────────────

    [Fact]
    public void AChordsOnlyScore_BreaksWhereItsTrackSays()
    {
        var score = Collect(ChordsOnly);
        // `| break G` is the bar before's, `Am break |` and `G pageBreak |` their own bar's.
        Assert.Equal([2, 5, 7], ForcedAfter(score));
        var (_, line, page) = Permissions(score)[0];
        Assert.Equal(BreakPermission.Force, page[6]);
        Assert.Equal(BreakPermission.Allow, page[1]);
        // `| noBreak C`: the bar line before it is closed.
        Assert.Equal(BreakPermission.Forbid, line[7]);

        string report = Report(ChordsOnly);
        Assert.Contains("4 systems, 9 bars", report);
        Assert.Contains("pages: 2", report);
        Assert.Contains("forced breaks after bar: 2, 5, 7", report);
    }

    [Theory]
    [InlineData("C | F break | G | C |", 2)]            // after the bar's chords
    [InlineData("C | F | break G | C |", 2)]            // at the head of the next bar
    [InlineData("C | F | break | G | C |", 2)]          // between two bar lines: an empty bar, the bar before's
    [InlineData("break C | F | G | C |", 1)]            // nothing before it: the first bar's own
    [InlineData("C | F | G | C | break", 4)]            // after the last bar line
    public void WhereTheWordStandsInItsBar_IsWhatAPartsMusicReads(string row, int after)
    {
        var score = Collect("time 4/4\nchords h { " + row + " }\nscore { chords h }");
        Assert.Contains(after, ForcedAfter(score));
        Assert.Single(ForcedAfter(score));

        // …the same word at the same place in a part's music breaks after the same bar.
        string music = row.Replace("Am", "a1").Replace("C", "c1").Replace("F", "f1").Replace("G", "g1");
        var part = Collect("time 4/4\npart m { clef treble }\nsection A { m { " + music + " } }\nform { A }\nscore { staff m }");
        Assert.Equal(ForcedAfter(score), ForcedAfter(part));
    }

    [Fact]
    public void AChordsTrackOverAStaff_BreaksTheStaff_AttachedOrAsARow()
    {
        const string Book = """
            octave absolute
            time 4/4
            part m { clef treble }
            section A { m { c'1 | d'1 | e'1 | f'1 | c'1 | d'1 | e'1 | f'1 | } }
            chords h { section A { C | F | break G | C | Am break | F | G | C | } }
            form { A }
            score { chords h  staff m }
            score sheet { staff m  chords h }
            score bare { staff m }
            """;
        Assert.Equal([2, 5], ForcedAfter(Collect(Book)));
        Assert.Equal([2, 5], ForcedAfter(Collect(Book, "sheet")));
        // …and where the score does not place the track at all: the writer broke the piece there.
        Assert.Equal([2, 5], ForcedAfter(Collect(Book, "bare")));
        string report = Report(Book, all: true);
        Assert.Equal(3, report.Split("forced breaks after bar: 2, 5").Length - 1);
    }

    [Fact]
    public void ASectionedTrack_BreaksAtEveryPlayOfItsSection_AndASectionTheFormLeavesOutBreaksNothing()
    {
        const string Book = """
            octave absolute
            time 4/4
            part m { clef treble }
            section A { m { c'1 | d'1 | } }
            section B { m { e'1 | f'1 | } }
            section Unplayed { m { g'1 | } }
            chords h { section A { C | F break | } section B { G | C | } section Unplayed { C pageBreak | } }
            form { A B A }
            score { chords h  staff m }
            """;
        Assert.Equal([2, 6], ForcedAfter(Collect(Book)));
        Assert.All(Permissions(Collect(Book)), v => Assert.DoesNotContain(BreakPermission.Force, v.Page));
    }

    [Theory]
    [InlineData("break")]
    [InlineData("noBreak")]
    [InlineData("pageBreak")]
    [InlineData("noPageBreak")]
    public void InsideABarOfAChordsTrack_TheWordIsAnError_ThatSaysWhereToWriteIt(string word)
    {
        var tree = SyntaxTree.Parse("time 4/4\nchords h { C " + word + " F | G | }\nscore { chords h }");
        var error = Assert.Single(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(DiagnosticCodes.ChordsBreakInsideBar, error.Code);
        Assert.Equal(word, tree.Text.Substring(error.Span.Start, error.Span.Length));
        Assert.Contains("at a bar line only", error.Message);
        Assert.Contains("in a part's music", error.Message);
        // Kept in the tree (the source round-trips) and read by nobody.
        Assert.Equal(tree.Text, tree.GetRoot().ToFullString());
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        Assert.All(Permissions(score), v =>
        {
            Assert.All(v.Line, p => Assert.Equal(BreakPermission.Allow, p));
            Assert.All(v.Page, p => Assert.Equal(BreakPermission.Allow, p));
        });
        Assert.DoesNotContain("reak", new LilyPondExporter().Export(tree));
    }

    [Fact]
    public void TheWordsInAChordsTrack_ChangeNeitherTheSoundNorTheMusicXml()
    {
        string plain = ChordsOnly.Replace("break ", "").Replace("noBreak ", "").Replace("pageBreak ", "").Replace("noPageBreak ", "");
        Assert.DoesNotContain("reak", plain);
        Assert.Equal(new MusicXmlExporter().Export(SyntaxTree.Parse(plain)).ToXml().ToString(),
            new MusicXmlExporter().Export(SyntaxTree.Parse(ChordsOnly)).ToXml().ToString());
        using var a = new MemoryStream();
        using var b = new MemoryStream();
        new MidiExporter().Export(SyntaxTree.Parse(plain)).WriteTo(a);
        new MidiExporter().Export(SyntaxTree.Parse(ChordsOnly)).WriteTo(b);
        Assert.Equal(a.ToArray(), b.ToArray());
    }

    // ───────────────────────── any part, shown or not ─────────────────────────

    // (LF whatever this file's own line endings are: the tests cut lines out of it by "\n", and a
    // checkout with autocrlf hands the raw string CRLF — the twin's test was red on a fresh Windows clone.)
    private static readonly string ThreeParts = """
        octave absolute
        time 4/4
        part a { clef treble }
        part b { clef treble }
        part c { clef bass }
        section A {
          a { c'1 | d'1 | e'1 | f'1 | g'1 | a'1 | }
          b { c'1 | d'1 break | e'1 | f'1 | g'1 | a'1 | }
          c { c1 | d1 | e1 | f1 pageBreak | g1 | a1 noBreak | }
        }
        section B { a { c'1 | } b { c'1 | } c { c1 | } }
        form { A B }
        score { staff a  staff b  staff c }
        score duo { staff a  staff b }
        score solo { staff a }
        """.ReplaceLineEndings("\n");

    [Fact]
    public void ABreakInTheSecondAndThirdPart_BreaksTheScore()
    {
        var score = Collect(ThreeParts);
        Assert.Equal([2, 4], ForcedAfter(score));
        Assert.All(Permissions(score), v =>
        {
            Assert.Equal(BreakPermission.Force, v.Page[3]);
            Assert.Equal(BreakPermission.Forbid, v.Line[5]);
        });
        string report = Report(ThreeParts);
        Assert.Contains("pages: 2", report);
        Assert.Contains("forced breaks after bar: 2, 4", report);
    }

    [Theory]
    [InlineData("duo")]         // the multi-staff road: part c is not shown
    [InlineData("solo")]        // the single-staff road (a part sheet): neither b nor c is
    public void ABreakInAPartTheScoreDoesNotShow_BreaksItToo(string name)
    {
        var score = Collect(ThreeParts, name);
        Assert.Equal([2, 4], ForcedAfter(score));
        Assert.All(Permissions(score), v =>
        {
            Assert.Equal(BreakPermission.Force, v.Page[3]);
            Assert.Equal(BreakPermission.Forbid, v.Line[5]);
        });
    }

    [Fact]
    public void TheChamberWritersBook_BreaksWhereItsSecondPartSays()
    {
        // Grammar evaluation 4, r-06d: both of part b's words were dropped, in both scores.
        const string Book = """
            octave absolute
            part a { clef treble }
            part b { clef treble }
            section A { a { c'1 | d'1 | e'1 | f'1 | } b { c'1 | d'1 pageBreak | e'1 | f'1 break | } }
            section B { a { c'1 | d'1 pageBreak | e'1 | f'1 | } b { c'1 | d'1 | e'1 | f'1 | } }
            form { ~A ~B }
            score { staff a staff b }
            score onlyB { staff b }
            """;
        Assert.Equal([2, 4, 6], ForcedAfter(Collect(Book)));
        Assert.Equal([2, 4, 6], ForcedAfter(Collect(Book, "onlyB")));
        Assert.Contains("pages: 3", Report(Book));
    }

    [Fact]
    public void AScoresOwnBreakControls_StillDecide()
    {
        // breaksOnly closes every bar line the words leave open — the words of every part.
        string only = ThreeParts.Replace("score { staff a  staff b  staff c }",
            "paper p { breaksOnly }\nscore { paper p  staff a  staff b  staff c }");
        var score = Collect(only);
        Assert.Equal([2, 4], ForcedAfter(score));
        Assert.All(Permissions(score), v => Assert.Equal(BreakPermission.Forbid, v.Line[0]));
    }

    // ───────────────────────── two places, one bar line ─────────────────────────

    [Fact]
    public void ABreakAgainstANoBreak_Warns_NamingBothPlaces_AndTheBreakWins()
    {
        const string Book = """
            octave absolute
            time 4/4
            part a { clef treble }
            part b { clef treble }
            part c { clef bass }
            section A {
              a { c'1 | d'1 noBreak | e'1 | f'1 | }
              b { c'1 | d'1 break | e'1 | f'1 | }
              c { c1 | d1 | e1 pageBreak | f1 | }
            }
            chords h { section A { C | F | G noPageBreak | C | } }
            form { A }
            score { staff a  staff b }
            """;
        var tree = SyntaxTree.Parse(Book);
        var warnings = SemanticValidation.Run(tree).Where(d => d.Code == DiagnosticCodes.BreakAgainstNoBreak).ToList();
        Assert.Equal(2, warnings.Count);
        Assert.All(warnings, w => Assert.Equal(DiagnosticSeverity.Warning, w.Severity));

        var line = Assert.Single(warnings, w => tree.Text.Substring(w.Span.Start, w.Span.Length) == "noBreak");
        Assert.Contains("'noBreak' in part 'a'", line.Message);
        Assert.Contains("'break' in part 'b' (line 8)", line.Message);
        Assert.Contains("the break wins", line.Message);

        // A part the score does not show against a chords track it does not place.
        var page = Assert.Single(warnings, w => tree.Text.Substring(w.Span.Start, w.Span.Length) == "noPageBreak");
        Assert.Contains("'noPageBreak' in chords track 'h'", page.Message);
        Assert.Contains("'pageBreak' in part 'c' (line 9)", page.Message);
        Assert.Contains("the pageBreak wins", page.Message);

        var score = Collect(Book);
        Assert.Equal([2, 3], ForcedAfter(score));
        Assert.All(Permissions(score), v => Assert.Equal(BreakPermission.Force, v.Page[2]));
    }

    /// <summary>The break words are looked up in the text only at a bar line somebody forbids
    /// (session 1291: every <c>break</c> of a book was looked up on every keystroke's collect,
    /// conflict or none — the collect stage 1.34 → 1.89 ms a keystroke over the reader's corpus).
    /// Counted, not timed.</summary>
    [Fact]
    public void TheBreakWords_AreLookedUpOnlyWhereSomeoneForbids()
    {
        const string Book = """
            octave absolute
            time 4/4
            part a { clef treble }
            part b { clef treble }
            section A {
              a { c'1 | d'1 break | e'1 | f'1 pageBreak | g'1 | }
              b { c'1 break | d'1 break | e'1 | f'1 | g'1 | }
            }
            form { A }
            score { staff a  staff b }
            """;
        var tree = SyntaxTree.Parse(Book);
        var collector = new MeasureCollector();
        var score = SvgGenerator.CollectScore(collector, tree, RenderSpecParser.FindFirst(tree));
        Assert.Equal([1, 2, 4], ForcedAfter(score));
        Assert.Empty(collector.BreakConflicts);
        Assert.Equal(0, collector.BreakWordLookups);

        // One bar line forbidden: its places are looked up, and the conflict is recorded.
        var against = SyntaxTree.Parse(Book.Replace("c'1 break | d'1 break |", "c'1 break | d'1 noBreak |"));
        var asked = new MeasureCollector();
        SvgGenerator.CollectScore(asked, against, RenderSpecParser.FindFirst(against));
        Assert.Single(asked.BreakConflicts);
        Assert.InRange(asked.BreakWordLookups, 2, 2);
    }

    [Fact]
    public void TwoPlacesThatAgree_OrOnePartSayingBoth_IsNoConflict()
    {
        const string Agree = """
            octave absolute
            part a { clef treble }
            part b { clef treble }
            section A { a { c'1 | d'1 break | e'1 | } b { c'1 | d'1 break | e'1 noBreak | } }
            chords h { section A { C | F pageBreak | G | } }
            form { A }
            score { staff a staff b }
            """;
        Assert.DoesNotContain(SemanticValidation.Run(SyntaxTree.Parse(Agree)), d => d.Code == DiagnosticCodes.BreakAgainstNoBreak);
        // One part's own two words at one bar line: its last word, as it always was.
        const string Own = "octave absolute\npart a { clef treble }\nsection A { a { c'1 | break noBreak d'1 | } }\nform { A }\nscore { staff a }";
        Assert.DoesNotContain(SemanticValidation.Run(SyntaxTree.Parse(Own)), d => d.Code == DiagnosticCodes.BreakAgainstNoBreak);
    }

    // ───────────────────────── the .ly twin ─────────────────────────

    [Fact]
    public void TheTwin_WritesAPlacedTracksWordsInItsChordmode_Once()
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(ChordsOnly));
        string chords = ly[ly.IndexOf("\\chordmode", System.StringComparison.Ordinal)..];
        chords = chords[..chords.IndexOf('}')];
        string flat = string.Join(' ', chords.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal("\\chordmode { c1 | f1 | \\break g1 | c1 | a1:m \\break | f1 | g1 \\pageBreak | c1 | \\noBreak c1 |", flat);
        // …and nowhere else: the track is placed, so no silent stream repeats them.
        Assert.DoesNotContain("Devnull \\breaksOffTheStaves", ly);
        Assert.Equal(1, ly.Split("\\pageBreak").Length - 1);
    }

    [Fact]
    public void TheTwin_WritesTheWordsOfWhatTheScoreDoesNotShow_InASilentStream()
    {
        // `solo` first: the exporter writes the first score, which shows part a alone.
        string book = ThreeParts.Replace("score { staff a  staff b  staff c }\n", "")
            .Replace("score duo { staff a  staff b }\n", "");
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(book));
        string stream = ly[ly.IndexOf("breaksOffTheStaves = {", System.StringComparison.Ordinal)..];
        stream = stream[..stream.IndexOf('}')];
        string flat = string.Join(' ', stream.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal("breaksOffTheStaves = { s1 s1 \\break s1 s1 \\pageBreak s1 s1 \\noBreak", flat);
        Assert.Contains("\\new Devnull \\breaksOffTheStaves", ly);
        // The three-staff score writes each word once, in the part that wrote it, and no stream.
        string full = new LilyPondExporter().Export(SyntaxTree.Parse(ThreeParts));
        Assert.DoesNotContain("breaksOffTheStaves", full);
        Assert.Equal(1, full.Split("\\pageBreak").Length - 1);
        Assert.Equal(1, full.Split("\\break").Length - 1);
        Assert.Equal(1, full.Split("\\noBreak").Length - 1);
    }

    // ───────────────────────── a file with none of the words ─────────────────────────

    [Fact]
    public void AFileWithNoBreakWord_IsLeftAsItWas()
    {
        string plain = ThreeParts.Replace(" break", "").Replace(" pageBreak", "").Replace(" noBreak", "")
            + "\nchords h { section A { C | F | G | C | Am | F | } }\n";
        foreach (string? name in new[] { null, "duo", "solo" })
        {
            var tree = SyntaxTree.Parse(plain);
            var spec = name == null ? RenderSpecParser.FindFirst(tree) : RenderSpecParser.FindByName(tree, name);
            var collector = new MeasureCollector();
            var score = SvgGenerator.CollectScore(collector, tree, spec);
            Assert.All(Permissions(score), v =>
            {
                Assert.All(v.Line, p => Assert.Equal(BreakPermission.Allow, p));
                Assert.All(v.Page, p => Assert.Equal(BreakPermission.Allow, p));
            });
            Assert.Empty(collector.BreakConflicts);
            Assert.Empty(collector.BreaksOffTheStaves);
        }
        Assert.DoesNotContain("reak", new LilyPondExporter().Export(SyntaxTree.Parse(plain)));
        Assert.DoesNotContain("forced breaks", Report(plain, all: true));
    }

    // ───────────────────────── the editor ─────────────────────────

    private static string[] Offered(string marked)
    {
        int at = marked.IndexOf('▮');
        string text = marked.Remove(at, 1);
        return LilySharpLanguageServer.GetChordsTrackCompletions(text, at).Items.Select(i => i.Label).ToArray();
    }

    [Theory]
    [InlineData("chords h { C | F | ▮ }")]              // after a bar line
    [InlineData("chords h { C | F ▮ | G | }")]          // before one
    [InlineData("chords h { C | F | ▮ G | }")]          // at the head of a bar
    [InlineData("chords h { ▮ }")]
    [InlineData("chords h { C | F | pa▮ G | }")]        // the word being typed is not "something before"
    public void Completion_OffersTheFourWords_AtABarLine(string marked)
    {
        var offered = Offered("time 4/4\n" + marked + "\nscore { chords h }");
        Assert.Contains("break", offered);
        Assert.Contains("noBreak", offered);
        Assert.Contains("pageBreak", offered);
        Assert.Contains("noPageBreak", offered);
        // …after the chords: a chart is mostly chords.
        Assert.True(System.Array.IndexOf(offered, "C") < System.Array.IndexOf(offered, "break"));
    }

    [Fact]
    public void Completion_LeavesTheWordsOut_InsideABar_WhereTheyAreAnError()
    {
        var offered = Offered("time 4/4\nchords h { C | F ▮ G | }\nscore { chords h }");
        Assert.Contains("C", offered);
        Assert.DoesNotContain("break", offered);
        Assert.DoesNotContain("noPageBreak", offered);
    }

    [Theory]
    [InlineData("C | br‸eak G |", +1, "C | pageBreak G |")]
    [InlineData("C | F pageBr‸eak | G |", -1, "C | F break | G |")]
    [InlineData("C | noBr‸eak G |", +1, "C | noPageBreak G |")]
    public void TheStepKeys_TradeTheWords_InAChordsTrack(string row, int direction, string stepped)
    {
        static string Book(string r) => "time 4/4\nchords h { " + r + " }\nscore { chords h }\n";
        string marked = Book(row);
        int at = marked.IndexOf('‸');
        string text = marked.Remove(at, 1);
        var uri = new System.Uri("file:///breaks.lys");
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        var response = server.Step(new StepParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Selections = [new StepSelection { Start = at, End = at }],
            Direction = direction,
            Semitone = true,
        });
        foreach (var e in response.Edits.OrderByDescending(e => e.Start))
            text = text[..e.Start] + e.NewText + text[e.End..];
        Assert.Equal(Book(stepped), text);
    }
}
