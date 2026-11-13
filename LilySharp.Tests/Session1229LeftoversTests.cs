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

using System.Linq;
using LilySharp.Core.MusicXml;
using LilySharp.Core.MusicXmlImport;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>What sessions 1227 and 1228 found and left (Lab sessions/p1228/leftovers.md), closed in session 1229.</summary>
public class Session1229LeftoversTests
{
    private static string Book(string music)
        => "time 4/4\noctave absolute\npart mel { }\nsection Main { mel { " + music + " } }\nform { Main }\nscore { staff mel }\n";

    private static System.Collections.Generic.List<(string Step, double Alter, string? Accidental)> Pitched(string music)
        => [.. new MusicXmlExporter().Export(SyntaxTree.Parse(Book(music))).Parts.Single().Measures.SelectMany(m => m.Notes)
            .Where(n => n.Step != null && !n.IsRest).Select(n => (n.Step!, n.Alter ?? 0, n.AccidentalName))];

    /// <summary>
    /// A quarter tone in a chord, in a repeated chord and in a grace note went to MusicXML as the
    /// plain letter (found by session 1228: only a single note and a <c>&lt;&lt; &gt;&gt;</c> member carried
    /// the half alter).
    /// </summary>
    [Fact]
    public void MusicXml_KeepsTheQuarterTonesOfChordsAndGraceNotes()
    {
        Assert.Equal([("C", 0.5, "quarter-sharp"), ("E", -0.5, "quarter-flat"), ("C", 0.5, "quarter-sharp"), ("E", -0.5, "quarter-flat")],
            Pitched("<cih' eeh'>2 q2 |"));
        Assert.Equal([("A", -0.5, "quarter-flat"), ("C", 0.0, null)], Pitched("grace { aeh'16 } c'1 |"));
        Assert.Equal([("C", 1.5, "three-quarters-sharp"), ("E", 0.0, null), ("D", 0.0, null)], Pitched("grace { <cisih' e'>16 } d'1 |"));
        // The control: the single note that always carried it.
        Assert.Equal([("C", 0.5, "quarter-sharp")], Pitched("cih'1 |"));
    }

    /// <summary>…so the round trip through <c>lysc import</c> holds for them too.</summary>
    [Fact]
    public void QuarterTonesInAChord_ComeBackFromMusicXml()
    {
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse(Book("<cih' eeh'>2 grace { aeh'16 } c'2 |"))).ToXml().ToString();
        var (lys, _) = new MusicXmlImporter().Import(xml);
        Assert.Contains("cih'", lys);
        Assert.Contains("eeh'", lys);
        Assert.Contains("aeh'", lys);
    }

    private const string TwoPedalledStaves = """
        tempo 120
        time 4/4
        key c major
        part pa { clef treble }
        part pb { clef bass }
        section Main {
          pa { c'2@sustain e | g2 e@!sustain | c1 | c2@sustain e@!sustain | }
          pb { c1 | c2@sustain e | g2@!sustain e | c1 | }
        }
        form { Main }
        score x { staff pa staff pb }
        """;

    private static readonly LilySharp.Core.Svg.Renderer.SvgRenderOptions NoFont = new() { EmbedFont = false };

    private static string FullSvg(string text)
        => LilySharp.Core.Svg.SvgGenerator.Generate(SyntaxTree.Parse(text), NoFont).Replace("\r\n", "\n");

    /// <summary>
    /// With a pedal on two staves, the live preview drew each bracket with another staff's
    /// source position: a bracket's index counted its own staff's brackets and was looked up in
    /// a list paired over every staff at once.
    /// </summary>
    [Fact]
    public void PedalBracketsOfTwoStaves_KeepTheirOwnSourcePositions_InTheLivePreview()
    {
        string src = TwoPedalledStaves;
        var session = new LilySharp.Core.Svg.IncrementalCompiler(SyntaxTree.Parse(src), NoFont);
        string first = session.Render().Replace("\r\n", "\n");
        Assert.Equal(FullSvg(src), first);
        // Each staff's brackets carry the position of their own `@sustain`: three lines a bracket.
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(src, "@sustain"))
            Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(first, $"<line [^>]*data-pos=\"{m.Index}\"").Count);

        // An edit that moves every offset and changes nothing else.
        var shift = new TextChange(new TextSpan(0, 0), "\n");
        Assert.Equal(FullSvg("\n" + src), session.Edit(shift).Replace("\r\n", "\n"));

        // The control: one pedalled staff always agreed.
        string one = src.Replace("c2@sustain e | g2@!sustain e", "c2 e | g2 e");
        Assert.Equal(FullSvg(one), new LilySharp.Core.Svg.IncrementalCompiler(SyntaxTree.Parse(one), NoFont).Render().Replace("\r\n", "\n"));
    }

    /// <summary>…and an edit to one staff's pedal redraws what a full render draws.</summary>
    [Fact]
    public void EditingOneStaffsPedal_MatchesAFullRender_WhenAnotherStaffIsPedalledToo()
    {
        string src = TwoPedalledStaves;
        foreach (var (find, replacement) in new[]
                 {
                     ("g2@!sustain e", "g2 e@!sustain"),   // the lower staff's release moves
                     ("g2@!sustain e", "g2 e"),            // …or goes
                     ("g2 e@!sustain", "g2@!sustain e"),   // the upper staff's release moves
                     ("c2@sustain e@!sustain", "c2 e"),    // the upper staff's second pedal goes
                 })
        {
            var session = new LilySharp.Core.Svg.IncrementalCompiler(SyntaxTree.Parse(src), NoFont);
            session.Render();
            int at = src.IndexOf(find, System.StringComparison.Ordinal);
            Assert.True(at >= 0, find);
            var change = new TextChange(new TextSpan(at, find.Length), replacement);
            string edited = src.Remove(at, find.Length).Insert(at, replacement);
            Assert.Equal(FullSvg(edited), session.Edit(change).Replace("\r\n", "\n"));
        }
    }

    /// <summary>
    /// The measure keys held the spans of every staff's marks paired together. Here the upper
    /// staff's long bracket goes with its release, and paired together the marks gave the same
    /// spans before and after (the lower staff's pedal sat between), so the systems in the
    /// middle kept the bracket's room.
    /// </summary>
    [Fact]
    public void DeletingOneStaffsPedalRelease_RedrawsTheSystemsItsBracketSpanned_WhenAnotherStaffIsPedalledToo()
    {
        string upper =
            "c'4 d e f | g4 a@sustain b c | d4 e f g | a4 b c d | "
            + "c4 b a g | f4 e d c | c4 d e f | g4 a b c | "
            + "d4 e f g | a4 b c d | c4 b@!sustain a g | f4 e d c |";
        string Lower(bool pedal) => (pedal
            ? "c1 | c1 | c1 | c2@sustain c | c1 | c2@!sustain c | "
            : "c1 | c1 | c1 | c2 c | c1 | c2 c | ") + "c1 | c1 | c1 | c1 | c1 | c1 |";
        string sylls = string.Concat(Enumerable.Repeat("la la la la | ", 12)).TrimEnd();
        string Text(bool lowerPedal) => $$"""
            time 4/4
            part pa { clef treble }
            part pb { clef bass }
            section Main {
              pa { {{upper}} }
              lyrics w { {{sylls}} }
              pb { {{Lower(lowerPedal)}} }
            }
            form { Main }
            score x { staff pa  lyrics w on pa  staff pb }
            """.Replace("\r\n", "\n");

        // With the lower staff pedalled, and the control without.
        foreach (bool lowerPedal in new[] { true, false })
        {
            string text = Text(lowerPedal);
            var tree = SyntaxTree.Parse(text);
            var session = new LilySharp.Core.Svg.IncrementalCompiler(tree, NoFont);
            Assert.Equal(FullSvg(text), session.RenderIncremental(tree).Replace("\r\n", "\n"));

            int at = text.IndexOf("b@!sustain", System.StringComparison.Ordinal) + 1;
            tree = tree.WithChange(new TextChange(new TextSpan(at, "@!sustain".Length), ""));
            text = text.Remove(at, "@!sustain".Length);
            Assert.Equal(FullSvg(text), session.RenderIncremental(tree).Replace("\r\n", "\n"));
        }
    }

    /// <summary>Part <c>a</c> changes to 3/4 at its fourth bar (or not); part <c>b</c> rests.</summary>
    private static string RestBesideAMeterChange(string rest, bool change = true)
        => "time 4/4\noctave absolute\npart a { }\npart b { }\nsection Main {\n"
            + (change
                ? "  a { c'1 | c'1 | c'1 | time 3/4 c'2. | c'2. | c'2. | c'2. | c'2. | }\n"
                : "  a { c'1 | c'1 | c'1 | c'1 | c'1 | c'1 | c'1 | c'1 | }\n")
            + "  b { " + rest + " }\n}\nform { Main }\nscore { staff a staff b }\n";

    /// <summary>
    /// <c>R1*8</c> says eight whole-note bars. Where another part's <c>time 3/4</c> lands inside
    /// it, that was reported once a bar after the change (five LYS2004); it is said once, at the
    /// rest, with the bare <c>R*8</c> that follows the meter.
    /// </summary>
    [Fact]
    public void AValuedBarRestAcrossAMeterChange_IsToldOnce_AndWhatToWrite()
    {
        static System.Collections.Generic.List<Diagnostic> Warnings(string text)
            => [.. LilySharp.Core.Semantics.SemanticValidation.Run(SyntaxTree.Parse(text))
                .Where(d => d.Severity != DiagnosticSeverity.Info)];

        string valued = RestBesideAMeterChange("R1*8");
        var told = Assert.Single(Warnings(valued));
        Assert.Equal(DiagnosticCodes.MeasureDurationMismatch, told.Code);
        Assert.Equal(valued.IndexOf("R1*8", System.StringComparison.Ordinal), told.Span.Start);
        Assert.Contains("3/4", told.Message);
        Assert.Contains("at measure 4", told.Message);
        Assert.Contains("'R*8'", told.Message);

        // The controls: the bare run follows the meter, and a valued run under one meter is right.
        Assert.Empty(Warnings(RestBesideAMeterChange("R*8")));
        Assert.Empty(Warnings(RestBesideAMeterChange("R1*8", change: false)));
        // A valued run that ends before the change, and the right value after it.
        Assert.Empty(Warnings(RestBesideAMeterChange("R1*3 | R2.*5")));
        // …which was told "Measure duration 3/4 is less than time signature 4/4" when written-out
        // bars came before it; and the music after a run with the change inside is in the new meter.
        Assert.Empty(Warnings(RestBesideAMeterChange("R1 | R1 | R1 | R2.*5")));
        Assert.Empty(Warnings(RestBesideAMeterChange("R*5 | e'2. | e'2. | e'2. |")));
    }

    /// <summary>
    /// MusicXML wrote one <c>&lt;multiple-rest&gt;8</c> on the first bar though the 3/4 stands on
    /// the fourth: a multiple rest cannot hold a meter change, so the run is 3 + 5.
    /// </summary>
    [Fact]
    public void MusicXml_SplitsAMultipleRest_AtAMeterChangeInsideIt()
    {
        static System.Collections.Generic.List<(int Bar, int Count)> Runs(string text)
            => [.. new MusicXmlExporter().Export(SyntaxTree.Parse(text)).Parts[1].Measures
                .Select((m, i) => (Bar: i + 1, Count: m.Attributes?.MultipleRest ?? 0)).Where(r => r.Count != 0)];

        Assert.Equal([(1, 3), (4, 5)], Runs(RestBesideAMeterChange("R*8")));
        Assert.Equal([(1, 3), (4, 5)], Runs(RestBesideAMeterChange("R1*8")));
        // A change on the run's last bar leaves a bar's rest, which is no multiple rest.
        Assert.Equal([(1, 3)], Runs(RestBesideAMeterChange("R*4 | R | R | R | R |")));
        // The control: one meter, one run.
        Assert.Equal([(1, 8)], Runs(RestBesideAMeterChange("R*8", change: false)));
        Assert.Equal([(1, 8)], Runs(RestBesideAMeterChange("R1*8", change: false)));
    }

    /// <summary>
    /// <c>lysc layout</c> named a meter change by its model measure, which counts a bar a section
    /// boundary splits (a short last bar and the next section's pickup — one bar on the page)
    /// twice: every change after the split was a bar late against the page and against the
    /// report's own system lines.
    /// </summary>
    [Fact]
    public void LayoutReport_NamesAMeterChangeByThePagesBar_AfterABarASectionBoundarySplits()
    {
        static string Book(string endOfA, string startOfB)
            => "time 3/4\noctave absolute\npart m { }\n"
                + "section A { m { c'2. | d'2. | " + endOfA + " | } }\n"
                + "section B { " + startOfB + " time 4/4 c'1 | d'1 | time 2/4 e'2 | } }\n"
                + "form { A B }\nscore { staff m }\n";

        // A's last bar is two beats and B's pickup the third: bars 1 2 3 on the page, 4/4 in bar 4.
        string split = Book("e'2", "partial 4 m { g'4 |");
        string report = LilySharp.Core.Svg.LayoutReport.Generate(SyntaxTree.Parse(split));
        Assert.Contains("time 3/4 -> 4/4 (bar 4) -> 2/4 (bar 6)", report);
        Assert.Contains("1 system, 6 bars", report);
        // …which is the number the page prints on that bar.
        var tree = SyntaxTree.Parse(split);
        var measures = LilySharp.Core.Svg.SvgGenerator.CollectScore(tree, LilySharp.Core.Svg.Collector.RenderSpecParser.FindFirst(tree))
            .PrimaryContentStaff.PrimaryVoice.Measures;
        var printed = LilySharp.Core.Svg.Layout.BarNumberEngraver.NumberMeasures(measures, 0);
        int fourFour = Enumerable.Range(0, measures.Length).First(i => measures[i].Items.Any(
            item => item is LilySharp.Core.Svg.Model.TimeSignatureChangeItem));
        Assert.True(measures[fourFour - 1].ContinuesBar);
        Assert.Equal(4, printed[fourFour]);

        // The control: no split bar, and the count was right.
        Assert.Contains("time 3/4 -> 4/4 (bar 4) -> 2/4 (bar 6)",
            LilySharp.Core.Svg.LayoutReport.Generate(SyntaxTree.Parse(Book("e'2.", "m {"))));
    }

    /// <summary>
    /// A section whose header states no key begins in the file's key, so a part's <c>key</c>
    /// that restates the file's changes nothing — it was told "Part 'a' changes key here, and
    /// 'b' of this section does not" (only a header's key was asked).
    /// </summary>
    [Fact]
    public void AKeyThatRestatesTheFilesKey_IsNoKeyChange_InASectionWithNoHeaderKey()
    {
        static System.Collections.Generic.List<Diagnostic> KeyWarnings(string fileKey, string a)
            => [.. LilySharp.Core.Semantics.SemanticValidation.Run(SyntaxTree.Parse(
                    "time 4/4\n" + fileKey + "\noctave absolute\npart a { }\npart b { }\n"
                    + "section A { a { " + a + " d'1 | d'1 | } b { d'1 | d'1 | } }\nform { A }\nscore { staff a staff b }\n"))
                .Where(d => d.Code == DiagnosticCodes.KeyChangeInSomeParts)];

        Assert.Empty(KeyWarnings("key g major", "key g major"));
        // A file that writes no key is in C major.
        Assert.Empty(KeyWarnings("", "key c major"));
        // The control: a key that is not the file's is the part's change, as it was.
        Assert.Single(KeyWarnings("key g major", "key d major"));
        Assert.Single(KeyWarnings("key g major", "key g minor"));
        Assert.Single(KeyWarnings("", "key a minor"));
        // …and the first key that differs is named, past one that restates.
        var said = Assert.Single(KeyWarnings("key g major", "key g major d'1 | key d major"));
        Assert.Contains("'a' changes key", said.Message);
    }

    /// <summary>
    /// <c>lysc layout</c> names every bar by the number the page prints: with a pickup (the
    /// page's bar 0) each label was one over, and bars closed under <c>time none</c> pushed the
    /// labels after them further (owner's instruction 2026-11-14).
    /// </summary>
    [Fact]
    public void LayoutReport_NamesBarsByThePagesNumbers_AfterAPickup()
    {
        static string Report(string music)
            => LilySharp.Core.Svg.LayoutReport.Generate(SyntaxTree.Parse(
                "time 4/4\noctave absolute\npart m { }\nsection A { " + music + " }\nform { A }\nscore { staff m }\n"));

        string pickup = Report("partial 4 m { g'4 | c'1 | d'1 | time 3/4 e'2. | f'2. | }");
        Assert.Contains("time 4/4 -> 3/4 (bar 3)", pickup);
        Assert.Contains("bars 0-4", pickup);
        Assert.Contains("5 bars", pickup);
        // The control: no pickup, and the first bar is bar 1.
        string plain = Report("m { c'1 | d'1 | time 3/4 e'2. | f'2. | }");
        Assert.Contains("time 4/4 -> 3/4 (bar 3)", plain);
        Assert.Contains("bars 1-4", plain);
    }

}
