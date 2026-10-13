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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>layout { compressRests true }</c> (owner's decision 2026-10-13, grammar feedback B5):
/// whole-bar rests written one after another engrave as one rest with its count, as if
/// written <c>R1*N</c>. LILYSHARP-OWN — MEASURED (LilyPond 2.26.0, Lab sessions/p903/b5):
/// <c>\compressMMRests</c> leaves separately written rests apart, so the twin merges the
/// events with its own music function; t.lys through the twin draws the page below.
/// </summary>
[Trait("Category", "Unit")]
public class CompressRestsTests
{
    // A: c'1 R R  |  B (label): R R R@p R  |  C (label): R R@mark R c'1
    private const string Music =
        "part m { clef treble }\n"
        + "section A { m { c'1 | R1 | R1 | } }\n"
        + "section B { m { R1 | R1 | R1@p | R1 | } }\n"
        + "section C { m { R1 | R1@mark(\"Q\") | R1 | c'1 | } }\n"
        + "form { A B C }\nscore { staff m }";

    private static (int Start, int Count)[] Runs(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.DoesNotContain(tree.Diagnostics.Concat(SemanticValidation.Run(tree)),
            d => d.Severity == DiagnosticSeverity.Error);
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return MultiMeasureRestEngraver.FindRuns(score).Select(r => (r.StartMeasureIndex, r.Count)).ToArray();
    }

    [Fact]
    public void Off_EveryWrittenRestIsItsOwn()
        => Assert.All(Runs(Music), r => Assert.Equal(1, r.Count));

    [Fact]
    public void On_RunsStopAtLabels_AtARestThatCarriesSomething_AndAtMarks()
    {
        // MEASURED page of the twin (2.26.0): 2 | B 2 | p 2 | C 1 | Q 2 — the dynamic on the
        // third rest of B opens its own run, as it would on a written R1*2.
        Assert.Equal(new[] { (1, 2), (3, 2), (5, 2), (7, 1), (8, 2) },
            Runs("layout { compressRests true }\n" + Music));
    }

    [Fact]
    public void On_WithoutLabels_TheRestsRunAcrossTheSections()
        => Assert.Equal(new[] { (1, 4), (5, 3), (8, 2) },
            Runs("layout { compressRests true  sectionLabels none }\n" + Music));

    [Fact]
    public void AnotherStaffPlaying_KeepsTheBarsApart()
    {
        string source = "layout { compressRests true }\npart a { clef treble }\npart b { clef bass }\n"
            + "section A { a { R1 | R1 | R1 | } b { R1 | c1 | R1 | } }\nform { A }\nscore { staff a staff b }";
        Assert.Equal(new[] { (0, 1), (2, 1) }, Runs(source));
    }

    // The places the page and the twin could part company — the twin merges each part's events
    // alone, the page merges only where every staff rests, and a chord symbol lives in its own
    // context there. MEASURED (LilyPond 2.26.0, Lab sessions/p903/b5 e1–e3): the twin's page is
    // this one in all three — LilyPond splits the merged rest where the other staff plays, and
    // engraves no run under a chord symbol, inline or a chords row.
    [Theory]
    [InlineData("part a { clef treble }\npart b { clef bass }\n"
        + "section A { a { R1 | R1 | R1 | c'1 | } b { R1 | R1 | c1 | c1 | } }\nform { A }\nscore { staff a staff b }",
        "0:2")]
    [InlineData("part m { clef treble }\nsection A { m { R1 | R1@chord(F) | R1 | c'1 | } }\nform { A }\nscore { staff m }",
        "0:1 1:1 2:1")]
    [InlineData("part m { clef treble }\nsection A { chords ch { C | F | G | C | } m { R1 | R1 | R1 | c'1 | } }\n"
        + "form { A }\nscore { chords ch staff m }",
        "")]   // the chords row is a staff that never rests: no run at all, each bar its own rest
    public void WhereTheTwinMergesAPartAlone_ThePagesStillAgree(string music, string runs)
        => Assert.Equal(runs, string.Join(" ",
            Runs("layout { compressRests true }\n" + music).Select(r => $"{r.Start}:{r.Count}")));

    [Fact]
    public void TheTwin_WrapsEachPartInTheMergingFunction()
    {
        string on = new LilyPondExporter().Export(SyntaxTree.Parse("layout { compressRests true }\n" + Music));
        Assert.Contains("lysCompressRests =\n#(define-music-function", on);
        Assert.Contains("m = \\lysCompressRests \\relative", on);
        string off = new LilyPondExporter().Export(SyntaxTree.Parse(Music));
        Assert.DoesNotContain("lysCompressRests", off);
    }
}
