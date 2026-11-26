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

using System.Linq;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>steps { hh "x.x. x.x." … }</c> — a drum chart written as steps (Orecchiante's request 24,
/// owner's decision 2026-11-26, docs/drum-steps-design.md): each row a drum and its steps,
/// a beat a run of steps between spaces. It reads as the <c>drums { }</c> chart of the same
/// lines — so most of these say "the same as that chart".
/// </summary>
[Trait("Category", "Unit")]
public class DrumStepsTests
{
    private static string Book(string music, string time = "") => $$"""
        {{time}}
        part kit { clef percussion }
        section A { kit { {{music}} } }
        form { ~A }
        score { staff kit }
        """;

    private static SyntaxTree Clean(string text)
    {
        var tree = SyntaxTree.Parse(text);
        Assert.DoesNotContain(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        return tree;
    }

    private static string Ly(string music, string time = "") => new LilyPondExporter().Export(Clean(Book(music, time)));

    private static string Midi(string music, string time = "") => string.Join(" ",
        new MidiExporter().Export(Clean(Book(music, time))).Tracks.SelectMany(t => t.Notes)
            .OrderBy(n => n.StartTick).ThenBy(n => n.Pitch)
            .Select(n => $"{n.Pitch}@{n.StartTick}/{n.DurationTicks}v{n.Velocity}"));

    private static List<Diagnostic> Problems(string music, string time = "")
        => [.. SyntaxTree.Parse(Book(music, time)).Diagnostics.Where(d => d.Code is "LYS0048" or "LYS0049")];

    [Theory]
    [InlineData("""steps { hh "x.x. x.x. x.x. x.x." sn ".... x... .... x..." bd "x... ..x. x.x. ...." }""",
                "drums { { hh8 hh8 hh8 hh8 hh8 hh8 hh8 hh8 } { r4 sn4 r4 sn4 } { bd4 r8 bd8 bd8 bd8 r4 } }")]
    // 16ths, a dotted eighth, a rest before the first hit, the half-bar rest of two empty beats.
    [InlineData("""steps { sn "x..x .x.. xxxx ...." }""", "drums { { sn8. sn16 r16 sn8. sn16 sn16 sn16 sn16 r4 } }")]
    [InlineData("""steps { sn "x... .... .... ...." }""", "drums { { sn4 r4 r2 } }")]
    [InlineData("""steps { sn ".... .... .... ...." }""", "drums { { r1 } }")]
    // A triplet beat, and one whose hits sit on a coarser grid (two eighths, no triplet).
    [InlineData("""steps { hh "xxx x.x x..x.. x.." }""",
                "drums { { tuplet 3/2 { hh8 hh8 hh8 } tuplet 3/2 { hh4 hh8 } hh8 hh8 hh4 } }")]
    // 8 steps a beat are 32nds; bar lines inside the pattern.
    [InlineData("""steps { sn "xxxxxxxx x... x... x... | x... x... x... x..." }""",
                "drums { { sn32 sn32 sn32 sn32 sn32 sn32 sn32 sn32 sn4 sn4 sn4 | sn4 sn4 sn4 sn4 } }")]
    // The symbols: an accent, a ghost, the open hi-hat, a flam.
    [InlineData("""steps { hh "x.o. x.x. x.x. x.x." sn "X... g... f... ...." }""",
                "drums { { hh8 hho8 hh8 hh8 hh8 hh8 hh8 hh8 } { sn4@accent sn4@parenthesize acciaccatura { sn16 } sn4 r4 } }")]
    public void ARow_ReadsAsTheLineItStandsFor(string steps, string drums)
    {
        Assert.Equal(Ly(drums), Ly(steps));
        Assert.Equal(Midi(drums), Midi(steps));
    }

    [Fact]
    public void ADottedBeat_SplitsInThrees_WithNoTriplet()
    {
        const string time = "time 6/8";
        Assert.Equal(Ly("drums { { hh8 hh8 hh8 hh8 hh8 hh8 } { bd4. r8 bd4 } }", time),
                     Ly("""steps 4. { hh "xxx xxx" bd "x.. .x." }""", time));
        Assert.Equal(Ly("drums { { sn16 sn16 sn16 sn16 sn8 sn4. } }", time),
                     Ly("""steps 4. { sn "xxxxx. x.." }""", time));
    }

    [Fact]
    public void ABeatOfAnEighth_WritesSixteenthsInTwos()
        => Assert.Equal(Ly("drums { { hh16 hh16 hh8 hh8 tuplet 3/2 { hh16 hh16 hh16 } hh8 hh8 } }", "time 7/8"),
                        Ly("""steps 8 { hh "xx x. x. xxx x. x." }""", "time 7/8"));

    [Fact]
    public void TheSource_RoundTrips_AndEachHitSitsOnItsStep()
    {
        string text = Book("steps 4 { hh \"x.x. x.xX\" sn  /* ghosts */\n   \".... gf.o\" // flam\n }");
        var tree = SyntaxTree.Parse(text);
        Assert.Equal(text, tree.GetRoot().ToFullString());

        var chart = tree.GetRoot().DescendantNodes().OfType<ParallelExpressionSyntax>().Single();
        Assert.True(chart.IsDrums);
        Assert.Equal(2, chart.Voices.Count());
        int hh = text.IndexOf("\"x.x.", StringComparison.Ordinal) + 1;
        var hits = chart.DrumLines.First().Line.DescendantNodes().OfType<DrumNoteSyntax>().ToList();
        Assert.Equal(new[] { hh, hh + 2, hh + 5, hh + 7, hh + 8 }, hits.Select(n => n.Span.Start));
        Assert.All(hits, n => Assert.Equal(1, n.NameToken.Span.Length));
        Assert.Equal(new[] { "hh", "hh", "hh", "hh", "hh" }, hits.Select(n => n.DrumName));
    }

    [Fact]
    public void APhrase_MayStillBeNamedSteps()
    {
        var tree = SyntaxTree.Parse($$"""
            phrase steps { sn4 sn sn sn | }
            {{Book("steps")}}
            """);
        Assert.DoesNotContain(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(tree.GetRoot().DescendantNodes().OfType<ParallelExpressionSyntax>());
    }

    [Fact]
    public void AGhostNote_IsStruckSoftly_HoweverItIsWritten()
    {
        int Velocity(string music) => new MidiExporter().Export(Clean(Book(music)))
            .Tracks.SelectMany(t => t.Notes).OrderBy(n => n.StartTick).First().Velocity;
        int plain = Velocity("sn4 r2. |");
        Assert.Equal(plain * 2 / 5, Velocity("sn4@parenthesize r2. |"));
        Assert.Equal(plain * 2 / 5, Velocity("""steps { sn "g... .... .... ...." }"""));
        Assert.Equal(plain * 2 / 5, Velocity("<sn@parenthesize>4 r2. |"));
        // A pitched note in parentheses is an editor's note and plays as written.
        Assert.Equal(
            new MidiExporter().Export(Clean("part p { }\nsection A { p { c'4 r2. | } }\nform { ~A }\nscore { staff p }")).Tracks.SelectMany(t => t.Notes).Single().Velocity,
            new MidiExporter().Export(Clean("part p { }\nsection A { p { c'4@parenthesize r2. | } }\nform { ~A }\nscore { staff p }")).Tracks.SelectMany(t => t.Notes).Single().Velocity);
    }

    [Fact]
    public void AStepThatIsNoSymbol_IsNamedByRowBarBeatAndStep()
    {
        var d = Assert.Single(Problems("""steps { hh "x.x. x.x. | x.y. x.x." }"""));
        Assert.Contains("'y' is not a step (step 3 of beat 1 of bar 2 in the hh row)", d.Message);
        Assert.Contains(". x X g o f", d.Message);
        Assert.Equal(1, d.Span.Length);
    }

    [Theory]
    [InlineData("""steps { sn "x.x.x x..." }""", "beat 1 of bar 1 in the sn row has 5 steps")]
    [InlineData("""steps 4. { sn "x.x. x.." }""", "a dotted beat splits into 1, 3 or 6")]
    [InlineData("""steps { hh "x.x. x.x. | x.x." sn ".... x..." }""", "the sn row has 1 bar and the hh row 2 bars")]
    [InlineData("""steps { hh "x.x. x.x. x.x. x.x." sn ".... x... x..." }""", "bar 1: the sn row has 3 beats and the hh row 4 beats")]
    [InlineData("""steps { kick "x... x..." }""", "'kick' is not a drum")]
    [InlineData("""steps { hh }""", "the hh row has no steps")]
    [InlineData("""steps { "x... x..." }""", "these steps have no drum")]
    [InlineData("""steps { hh "x.x. || x.x." }""", "bar 2 of the hh row has no beats")]
    [InlineData("""steps 3 { hh "x.x." }""", "a steps chart's beat is a note value")]
    public void AChartThatCannotBeRead_SaysWhere(string music, string message)
    {
        var problems = Problems(music);
        Assert.Contains(problems, p => p.Severity == DiagnosticSeverity.Error && p.Message.Contains(message, StringComparison.Ordinal));
    }

    private static List<Diagnostic> Validate(string text)
        => [.. Core.Semantics.SemanticValidation.Run(SyntaxTree.Parse(text))];

    [Theory]
    // A bar of the wrong length is said in the chart's terms — written in the bar or in a phrase
    // it plays — and where the meter's beat is of the other kind, the beat to write.
    [InlineData("""steps { hh "x.x. x.x. x.x. x.x." } | steps { hh "x.x. x.x. x.x." } |""", "", "LYS2001",
                "the steps chart has 3 beats of a quarter, and this bar holds 4")]
    [InlineData("""steps { hh "x.x. x.x. x.x. x.x." } | steps { hh "x.x. x.x. x.x. x.x. x.x." } |""", "", "LYS2002",
                "the steps chart has 5 beats of a quarter, and this bar holds 4")]
    [InlineData("""steps { hh "x.x. x.x. x.x. x.x." } | steps 4. { hh "xxx xxx" } |""", "", "LYS2001",
                "a 4/4 bar's beat is not dotted — write steps { … }")]
    [InlineData("""steps 4. { hh "xxx xxx xxx xxx" } | steps { hh "x.x. x.x. x.x. x.x." } |""", "time 12/8", "LYS2001",
                "the steps chart has 4 beats of a quarter, and this bar holds 6; in 12/8 write steps 4. { … }")]
    public void ABarOfTheWrongLength_IsSaidInBeats(string music, string time, string code, string message)
    {
        var d = Assert.Single(Validate(Book(music, time)), d => d.Code == code);
        Assert.Contains(message, d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AChartInAPhrase_IsSaidInBeatsWhereItPlays()
    {
        var d = Assert.Single(Validate("""
            part kit { clef percussion }
            phrase short { steps { hh "x.x. x.x. x.x." } }
            section A { kit { steps { hh "x.x. x.x. x.x. x.x." } | short | } }
            form { ~A }
            score { staff kit }
            """), d => d.Code == "LYS2001");
        Assert.Contains("the steps chart has 3 beats of a quarter, and this bar holds 4", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void QuartersFillingACompoundBar_Warn_Once()
    {
        // Three quarters fill a 6/8 bar, so no length is wrong — the beat is.
        var found = Validate(Book("""steps { hh "x.x. x.x. x.x." } | steps { hh "x.x. x.x. x.x." } | steps 4. { hh "xxx xxx" } |""", "time 6/8"))
            .Where(d => d.Code == "LYS2024").ToList();
        Assert.Equal(2, found.Count);
        Assert.All(found, d => Assert.Contains("write steps 4. { … }", d.Message, StringComparison.Ordinal));
        // A chart that says its beat is not second-guessed; nor is a simple meter.
        Assert.DoesNotContain(Validate(Book("""steps 8 { hh "x. x. x. x. x. x." } |""", "time 6/8")), d => d.Code == "LYS2024");
        Assert.DoesNotContain(Validate(Book("""steps { hh "x.x. x.x. x.x." } |""", "time 3/4")), d => d.Code == "LYS2024");
        // A phrase's chart played in many bars is spoken of once.
        Assert.Single(Validate("""
            time 6/8
            part kit { clef percussion }
            phrase beat { steps { hh "x.x. x.x. x.x." } }
            section A { kit { beat | beat | % | } }
            form { ~A }
            score { staff kit }
            """), d => d.Code == "LYS2024");
    }

    [Fact]
    public void TheOpenHiHatOnAnotherRow_Warns_AndPlaysAsAHit()
    {
        var d = Assert.Single(Problems("""steps { sn "o... .... .... ...." }"""));
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        Assert.Equal("LYS0049", d.Code);
        Assert.Equal(Midi("drums { { sn4 r4 r2 } }"), Midi("""steps { sn "o... .... .... ...." }"""));
    }

    [Theory]
    // A step typed inside the pattern, a beat added, the phrase left alone while the file's
    // head changes above it (its tree adopted, every position shifted).
    [InlineData("x.x. x.x. x.x. x.x.\" bd", "x.X. x.x. x.x. x.x.\" bd")]
    [InlineData("x.x. x.x. x.x. x.x.\" bd", "x.x. x.x. x.x. x.x. x.x.\" bd")]
    [InlineData("part kit {", "time 4/4\npart kit {")]
    public void AnEdit_ReadsAsTheFreshParseOfTheNewText(string find, string replacement)
    {
        string text = """
            part kit { clef percussion }
            phrase beat { steps { hh "x.x. x.x. x.x. x.x." bd "x... .... x... ...." } }
            section A { kit { beat | % | } }
            form { ~A }
            score { staff kit }
            """;
        int at = text.IndexOf(find, StringComparison.Ordinal);
        var edited = SyntaxTree.Parse(text).WithChange(new TextChange(new TextSpan(at, find.Length), replacement));
        string newText = text[..at] + replacement + text[(at + find.Length)..];
        var fresh = SyntaxTree.Parse(newText);
        Assert.Equal(newText, edited.GetRoot().ToFullString());
        Assert.Equal(fresh.Diagnostics.Select(d => (d.Code, d.Span)), edited.Diagnostics.Select(d => (d.Code, d.Span)));
        Assert.Equal(new LilyPondExporter().Export(fresh), new LilyPondExporter().Export(edited));
        Assert.Equal(
            fresh.GetRoot().DescendantNodes().OfType<DrumNoteSyntax>().Select(n => (n.DrumName, n.Span.Start)),
            edited.GetRoot().DescendantNodes().OfType<DrumNoteSyntax>().Select(n => (n.DrumName, n.Span.Start)));
    }

    [Fact]
    public void AChartInAPhrase_RepeatsAsAnyPhraseDoes()
    {
        string text = """
            part kit { clef percussion }
            phrase beat { steps { hh "x.x. x.x. x.x. x.x." bd "x... .... x... ...." } }
            section A { kit { beat | % | } }
            form { ~A }
            score { staff kit }
            """;
        var notes = new MidiExporter().Export(Clean(text)).Tracks.SelectMany(t => t.Notes).ToList();
        Assert.Equal(2 * (8 + 2), notes.Count);
    }
}
