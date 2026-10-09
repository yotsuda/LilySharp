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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The PLAYED order follows a form's jump texts (session 792; the owner's choice among session
/// 775's three candidates): the page's tie carry, the MusicXML tie stops, the twin's
/// <c>\repeatTie</c>s and the bar-complement adjacency all read ONE expansion
/// (<see cref="PlayedOrder"/>) that takes its route from <see cref="FormRoute"/> — the MIDI's
/// reading — so a tie at the end of the section before a <c>ds al fine</c> reaches the segno's
/// section as the MIDI sustains it. Until then the four readers stopped at the jump texts.
/// </summary>
[Trait("Category", "Unit")]
public sealed class FormJumpTieCarryTests
{
    private static Score Collect(string src) => new MeasureCollector().Collect(SyntaxTree.Parse(src), "vn");

    private static List<NoteItem> Notes(Score score) =>
        score.Voices[0].Measures.SelectMany(m => m.Items).OfType<NoteItem>().ToList();

    private static IReadOnlyList<Diagnostic> Check(string src)
    {
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        return SemanticValidation.Run(tree);
    }

    private static string Xml(string src) => new MusicXmlExporter().Export(SyntaxTree.Parse(src)).ToXml().ToString();

    private static string Twin(string src) => new LilySharp.Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(src));

    private static List<int> Midi(string src) =>
        new MidiExporter().Export(SyntaxTree.Parse(src)).Tracks.SelectMany(t => t.Notes)
            .OrderBy(n => n.StartTick).Select(n => n.Pitch).ToList();

    private static int CountOf(string text, string what)
    {
        int n = 0;
        for (int i = text.IndexOf(what, System.StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(what, i + 1, System.StringComparison.Ordinal))
            n++;
        return n;
    }

    /// <summary>`I segno A fine B ds al fine` plays I A B A: B's tie at the piece's written end
    /// is carried to A's first note (the replay) — a repeat tie there, a hanging tie on B's note
    /// — exactly what the MIDI sustains; A's own tie still arcs into B, which is printed and
    /// played next.</summary>
    private const string DalSegno = """
        part vn {
          section I { c''1 | }
          section A { c''1 | c1~ || }
          section B { c''1 | c1~ || }
        }
        form { I segno A fine B ds al fine }
        score { staff vn }
        """;

    [Fact]
    public void ATieAtTheEndOfTheSectionBeforeADalSegno_ReachesTheSegnosSection()
    {
        Assert.DoesNotContain(Check(DalSegno), d => d.Code is DiagnosticCodes.SpanAcrossSectionBoundary
            or DiagnosticCodes.TieTargetMismatch or DiagnosticCodes.JumpTargetMissing);
        var score = Collect(DalSegno);
        var notes = Notes(score);
        // I, A: c'' c''~, B: c'' c''~  → indices 0 | 1 2 | 3 4
        Assert.True(notes[1].HasRepeatTie, "A's first note takes the repeat tie B's tie reaches on the replay");
        Assert.True(notes[4].HasLaissezVibrer, "B's tied note has nothing printed after it: a hanging tie");
        Assert.False(notes[3].HasRepeatTie, "B's first note is reached by A's ARC, printed and played next");
        var arcs = new TieDetector().DetectTies(score).Select(t => (t.StartMeasureIndex, t.EndMeasureIndex)).ToList();
        Assert.Equal([(2, 3)], arcs);
        // The MIDI plays I A B A — the order the carry follows — and sustains BOTH ties: A's into
        // B's first note and B's into A's first note on the replay, so the seven written notes
        // sound as five.
        Assert.Equal(Enumerable.Repeat(84, 5).ToList(), Midi(DalSegno));
    }

    [Fact]
    public void TheExportsFollowTheSameRoute()
    {
        string xml = Xml(DalSegno);
        // A's tie stops on B's first note; B's stops on A's first note (the carry) and lets ring.
        Assert.Equal(2, CountOf(xml, "<tie type=\"start\""));
        Assert.Equal(2, CountOf(xml, "<tie type=\"stop\""));
        Assert.Equal(1, CountOf(xml, "<tied type=\"let-ring\""));
        Assert.Equal(1, CountOf(Twin(DalSegno), "\\repeatTie"));
    }

    /// <summary>On another pitch the carried tie is LYS4007, as every carried tie is.</summary>
    [Fact]
    public void ATieCarriedOverAJump_OnAnotherPitch_IsLys4007()
    {
        const string src = """
            part vn {
              section I { c''1 | }
              section A { e''1 | c1~ || }
              section B { c''1 | c1~ || }
            }
            form { I segno A fine B ds al fine }
            score { staff vn }
            """;
        Assert.Single(Check(src), d => d.Code == DiagnosticCodes.TieTargetMismatch);
        Assert.DoesNotContain(Notes(Collect(src)), n => n.HasRepeatTie || n.HasLaissezVibrer);
    }

    /// <summary>The played order off the stamps, with the marks between the plays: a D.S. al
    /// fine; a D.C. al coda over a repeat run, which plays once (its last pass) on the replay.</summary>
    [Fact]
    public void ThePlayedOrder_FollowsTheJumps()
    {
        var dalSegno = new List<PrintedPlay>
        {
            new(SectionRepeatRole.None, false, 0, false),                                 // I
            new(SectionRepeatRole.None, false, 0, false, MarksBefore: "Segno"),           // A
            new(SectionRepeatRole.None, false, 0, false, MarksBefore: "Fine", MarksAfter: "DalSegnoAlFine"), // B
        };
        Assert.Equal([0, 1, 2, 1], PlayedOrder.Expand(dalSegno));

        // |: A [1. B] :| [2. C] to coda D dc al coda coda E
        var daCapo = new List<PrintedPlay>
        {
            new(SectionRepeatRole.Body, RunStart: true, Count: 0, Rewinds: false),
            new(SectionRepeatRole.Ending, false, 0, false, PassSet.Of([1])),
            new(SectionRepeatRole.Ending, false, 0, false, PassSet.Of([2])),
            new(SectionRepeatRole.None, false, 0, false, MarksBefore: "ToCoda"),
            new(SectionRepeatRole.None, false, 0, false, MarksBefore: "DaCapoAlCoda|Coda"),
        };
        Assert.Equal([0, 1, 0, 2, 3, 0, 2, 4], PlayedOrder.Expand(daCapo));

        // Without marks the order is the one it always was.
        Assert.Equal([0, 1, 2], PlayedOrder.Expand(dalSegno.Select(p => p with { MarksBefore = null, MarksAfter = null }).ToList()));
    }

    /// <summary>The form-read plays (the twin's and the adjacency's input) carry the same marks
    /// the stamps do, and expand to the same order.</summary>
    [Fact]
    public void ThePlaysReadOffTheForm_CarryTheMarks()
    {
        var tree = SyntaxTree.Parse(DalSegno);
        var items = FormWalk.Read(tree.GetNodes<FormDeclarationSyntax>().Single());
        var names = new List<string>();
        var plays = PlayedOrder.PlaysOf(items, names);
        Assert.Equal(["I", "A", "B"], names);
        Assert.Equal("Segno", plays[1].MarksBefore);
        Assert.Equal("Fine", plays[2].MarksBefore);
        Assert.Equal("DalSegnoAlFine", plays[2].MarksAfter);
        Assert.Equal([0, 1, 2, 1], PlayedOrder.Expand(plays));
    }

    /// <summary>A bar split by a section boundary is judged by the neighbours the ROUTE gives:
    /// in <c>A to coda B dc al coda coda C</c> the plays are A B A C, so C follows A — whose
    /// short last bar C's short first bar completes — and neither half is a short bar. Read in
    /// written order C followed B, which ends full, and C's first bar warned (LYS2006).</summary>
    [Fact]
    public void ASplitBarAcrossAJump_IsJudgedByTheRoutesNeighbours()
    {
        const string head = """
            time 4/4
            key c major
            part m { clef bass }
            section A { m { c4 d e f | g4 a | } }
            section B { m { b4 c' | d'4 e' f' g' | } }
            section C { m { c'4 d' | e'1 | } }
            score { staff m }
            """;
        static string[] BarCodes(string book)
        {
            var validator = new MeasureValidator();
            validator.Validate(SyntaxTree.Parse(book));
            return validator.Diagnostics.Where(d => d.Code is "LYS2001" or "LYS2006").Select(d => d.Code).OrderBy(c => c).ToArray();
        }
        Assert.Empty(BarCodes(head + "form { A to coda B dc al coda coda C }\n"));
        // The control: with no jump C follows B, and C's half bar is a short bar.
        Assert.Equal(new[] { "LYS2006" }, BarCodes(head + "form { A B C }\n"));
    }
}
