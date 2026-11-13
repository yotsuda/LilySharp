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
}
