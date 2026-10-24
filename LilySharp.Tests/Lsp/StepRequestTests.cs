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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// <c>lilysharp/step</c> (Ctrl+Shift+Up / Ctrl+Shift+Down) and <c>lilysharp/auditionAt</c> — the
/// owner's decision of 2026-09-28 (<see cref="LilySharp.Core.Editing.NoteStepper"/>). The
/// source is written with its selections marked: <c>‸</c> is a caret, <c>«…»</c> a selection.
/// </summary>
[Trait("Category", "Unit")]
public class StepRequestTests
{
    private const char Caret = '‸';

    /// <summary>A guitar part (the default tuning, E2 A2 D3 G3 B3 E4), absolute octaves.</summary>
    private static string Guitar(string music) => $$"""
        octave absolute
        part gt { clef treble }
        section A { gt { {{music}} } }
        form { A }
        score { staff gt }
        """;

    private static string Plain(string music) => MusicSource.Wrap(music, "octave absolute");

    /// <summary>A flute part (GM program 73, 0-based), absolute octaves.</summary>
    private static string Flute(string music) => $$"""
        octave absolute
        part fl { instrument flute octave 4 }
        section A { fl { {{music}} } }
        form { A }
        score { staff fl }
        """;

    /// <summary>Strips the markers: the text and each selection as (start, end).</summary>
    private static (string Text, List<(int Start, int End)> Selections) Marked(string marked)
    {
        var sb = new System.Text.StringBuilder();
        var selections = new List<(int, int)>();
        int open = -1;
        foreach (char ch in marked)
        {
            if (ch == Caret)
                selections.Add((sb.Length, sb.Length));
            else if (ch == '«')
                open = sb.Length;
            else if (ch == '»')
                selections.Add((open, sb.Length));
            else
                sb.Append(ch);
        }
        return (sb.ToString(), selections);
    }

    private static readonly Uri DocUri = new("file:///step.lys");

    private static LilySharpLanguageServer Open(string text)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = DocUri, Text = text, LanguageId = "lilysharp", Version = 3 },
        });
        return server;
    }

    /// <summary>Steps the marked source; the response and the text with its edits applied.</summary>
    private static (StepResponse Response, string After) Step(string marked, int direction,
        bool includeStretch = false, bool semitone = false)
    {
        var (text, selections) = Marked(marked);
        var response = Open(text).Step(new StepParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocUri },
            Selections = selections.Select(s => new StepSelection { Start = s.Start, End = s.End }).ToArray(),
            Direction = direction,
            IncludeStretch = includeStretch,
            Semitone = semitone,
        });
        string after = text;
        foreach (var e in response.Edits.OrderByDescending(e => e.Start))
            after = after[..e.Start] + e.NewText + after[e.End..];
        return (response, after);
    }

    private static string Up(string marked, Func<string, string> book)
        => Stepped(marked, +1, book);

    private static string Down(string marked, Func<string, string> book)
        => Stepped(marked, -1, book);

    private static string Stepped(string marked, int direction, Func<string, string> book)
    {
        var (response, after) = Step(book(marked), direction);
        Assert.False(response.Fallback, response.Error);
        Assert.Equal(3, response.Version);
        return after;
    }

    // ---------------------------------------------------------------- @chord shapes (K3)

    /// <summary>The order the step walks for Cm7 on the guitar: the drawn default first —
    /// LilyPond's predefined x35343 — then Lily#'s maximal shapes, each once.</summary>
    private static List<string> Cm7Order(bool includeStretch = false) => Order("Cm7", includeStretch);

    private static List<string> Order(string symbol, bool includeStretch)
    {
        var tree = LilySharp.Core.Syntax.SyntaxTree.Parse(Guitar($"c'1@chord({symbol})"));
        var mark = tree.GetNodes<LilySharp.Core.Syntax.MusicMarkSyntax>().Single();
        var chord = LilySharp.Core.Semantics.ChordAnnotation.Of(mark)!.Structure!;
        var (_, order) = LilySharp.Core.Editing.NoteStepper.ShapeOrder(mark, chord, includeStretch);
        return [.. order.Select(o => LilySharp.Core.Music.ChordVoicings.Spell(o))];
    }

    /// <summary>
    /// Cm's normal-rule bases on the guitar, PINNED (owner's decision 2026-09-28): position →
    /// fingers → lexicographic among the maximal shapes, frets 0–15 — the Python prototype's
    /// 29 (scratchpad cm_list.py), frets 10–15 written in the compact form (a '-' on each side
    /// of each two-digit fret; two lone single digits between dashes split: xx-10-8-8-11,
    /// owner's decision 2026-09-28). The step walks them after the drawn default (LilyPond's
    /// predefined x35543, itself the 4th base).
    /// </summary>
    private static readonly string[] CmBases =
    [
        "x31013", "x31043", "x35043", "x35543", "8x58x8", "8x588x", "8650x8", "86508x", "8655x8",
        "86558x", "8658xx", "86x088", "86x8x8", "86x88x", "xx-10-888", "xx-10-8-8-11",
        "8-10-10-888", "8xx888", "8xx88-11", "8x-10-0-8-11", "8x-10-8x8",
        "8-10-x08-11", "8-10-x8x8", "8-10-10-0x-11", "8-10-10-8-8-11",
        "xx-10-12-13-11", "x-15-13-12-x-15", "x-15-13-12-13-x", "x-15-13-x-13-15",
    ];

    [Fact]
    public void CmsOrder_IsPinned_FretsTenToFifteenIncluded()
    {
        var chord = LilySharp.Core.Semantics.ChordAnnotation.Parse(["Cm"]).Structure!;
        var bases = LilySharp.Core.Music.ChordVoicings.For(LilySharp.Core.Tablature.Tunings.Guitar, chord,
            includeStretch: false).Bases.Select(b => LilySharp.Core.Music.ChordVoicings.Spell(b));
        Assert.Equal(CmBases, bases);
        var order = Order("Cm", includeStretch: false);
        Assert.Equal(29, order.Count);
        Assert.Equal(["x35543", "x31013", "x31043", "x35043", "8x58x8"], order.Take(5));
        Assert.Equal(["8-10-10-8-8-11", "xx-10-12-13-11", "x-15-13-12-x-15", "x-15-13-12-13-x", "x-15-13-x-13-15"],
            order.TakeLast(5));
    }

    /// <summary>The step walks on past 8xx888 into frets 10–15 and writes them in the compact
    /// form (owner's decision 2026-09-28); back below fret 10 it writes one character per string
    /// again. A written dash shape — compact or full-dash — steps from its place like any other
    /// (a row entry too).</summary>
    [Fact]
    public void TheStep_WalksIntoFretsTenToFifteen_AndSpellsEachInItsForm()
    {
        var (response, after) = Step(Guitar("c'1@chord(Cm 8xx888‸)"), +1);
        Assert.Equal(Guitar("c'1@chord(Cm 8xx88-11)"), after);
        Assert.Equal("Cm: shape 19 of 29 (8xx88-11)", response.Message);
        Assert.Equal(Guitar("c'1@chord(Cm 8xx888)"), Down("c'1@chord(Cm 8xx88-11‸)", Guitar));
        // The full-dash spelling of the same shape steps from the same place.
        Assert.Equal(Guitar("c'1@chord(Cm 8xx888)"), Down("c'1@chord(Cm 8-x-x-8-8-11‸)", Guitar));
        Assert.Equal(Guitar("c'1@chord(Cm x-15-13-12-13-x)"), Up("c'1@chord(Cm x-15-13-12-x-15‸)", Guitar));
        Assert.Equal(Guitar("c'1@chord(Cm xx-10-12-13-11)"), Up("c'1@chord(Cm 8-10-10-8-8-11‸)", Guitar));
        Assert.Equal(Guitar("c'1@chord(Cm 8-10-10-888)"), Up("c'1@chord(Cm xx-10-8-8-11‸)", Guitar));
        // A written x-3-5-5-4-3 is the default x35543: Up writes the next one-character shape.
        Assert.Equal(Guitar("c'1@chord(Cm x31013)"), Up("c'1@chord(Cm x-3-5-5-4-3‸)", Guitar));
        Assert.Equal(Row("Cm(8xx88-11) |"), Up("Cm(8xx888‸) |", Row));
        Assert.Equal(Row("Cm(8xx888) |"), Down("Cm(8xx88-11‸) |", Row));
    }

    [Fact]
    public void TheOrder_StartsWithTheDrawnDefault_ThenLilySharpsShapes()
    {
        var order = Cm7Order();
        Assert.Equal("x35343", order[0]);                                  // predefined
        Assert.Equal(order.Count, order.Distinct().Count());               // each once
        Assert.Contains("x3x546", order);                                  // the owner's shape
    }

    /// <summary>Owner's decision 2026-09-28: a name alone draws no diagram, so Up WRITES the
    /// default — the diagram appears; Down has nothing to take away (handled, no fallback).</summary>
    [Fact]
    public void TheNameAlone_UpWritesTheDefault_DownDoesNothing()
    {
        var order = Cm7Order();
        var (up, after) = Step(Guitar("c'1@chord(Cm7‸)"), +1);
        Assert.Equal(Guitar("c'1@chord(Cm7 x35343)"), after);
        Assert.Equal($"Cm7: shape 1 of {order.Count} (x35343)", up.Message);
        var (down, same) = Step(Guitar("c'1@chord(Cm7‸)"), -1);
        Assert.False(down.Fallback);   // on a chord: handled, just no move
        Assert.Empty(down.Edits);
        Assert.Equal(Guitar("c'1@chord(Cm7)"), same);
        Assert.Contains("Up adds one", down.Message);
    }

    [Fact]
    public void AWrittenShape_StepsToItsNeighbour_AndDownAtTheDefaultRemovesIt()
    {
        var order = Cm7Order();
        Assert.Equal(Guitar($"c'1@chord(Cm7 {order[3]})"), Up($"c'1@chord(Cm7 {order[2]}‸)", Guitar));
        Assert.Equal(Guitar($"c'1@chord(Cm7 {order[1]})"), Down($"c'1@chord(C‸m7 {order[2]})", Guitar));
        // Down to the default writes it; Down AT it removes the shape, and the diagram goes.
        Assert.Equal(Guitar("c'1@chord(Cm7 x35343)"), Down($"c'1@chord(Cm7 {order[1]}‸)", Guitar));
        var (removed, after) = Step(Guitar("c'1@chord(Cm7 x35343‸)"), -1);
        Assert.Equal(Guitar("c'1@chord(Cm7)"), after);
        Assert.Contains("shape removed, no diagram", removed.Message);
        Assert.Equal(Guitar($"c'1@chord(Cm7 {order[1]})"), Up("c'1@chord(Cm7 x35343‸)", Guitar));
    }

    /// <summary>On a ukulele part the step writes the ukulele's shape (the part's instrument,
    /// with no layout); with several shapes it steps the one for that tuning, the others stay.</summary>
    [Fact]
    public void TheStep_WritesOnThePartsInstrument_AndStepsItsShapeAmongSeveral()
    {
        static string Uke(string music) => $$"""
            octave absolute
            part uk { instrument ukulele }
            section A { uk { {{music}} } }
            form { A }
            score { staff uk }
            """;
        Assert.Equal(Uke("c'1@chord(C 0003)"), Up("c'1@chord(C‸)", Uke));
        Assert.Equal(Uke("c'1@chord(C x32010)"), Down("c'1@chord(C x32010 0003‸)", Uke));
        // The ukulele's order past the table (2026-09-29, K5 ⑥: the rules less V4): 0003 → 0403;
        // a chord the table lacks starts on the order's first shape.
        Assert.Equal(Uke("c'1@chord(C 0403)"), Up("c'1@chord(C 0003‸)", Uke));
        Assert.Equal(Uke("c'1@chord(C 0003)"), Down("c'1@chord(C 0403‸)", Uke));
        Assert.Equal(Uke("c'1@chord(Cmaj9 4203)"), Up("c'1@chord(Cmaj9‸)", Uke));
        Assert.Equal(Guitar("c'1@chord(C 0003)"), Down("c'1@chord(C 0003 x32010‸)", Guitar));
        Assert.Equal(Guitar("c'1@chord(C 0003 x32010)"), Up("c'1@chord(C 0003‸)", Guitar));
    }

    // ---- a layout shape table (2026-09-29): a listed name alone draws the table's shape

    private static string TableGuitar(string music) => "layout { chordDiagrams guitar { Cm7 x35343 } }\n" + Guitar(music);

    /// <summary>Where the layout's table lists the chord, the name alone shows the table's shape,
    /// so the step counts from it exactly as in an <c>all</c> score: Up writes the shape after
    /// it, Down on the name does nothing, Down at the written table shape removes it. A chord
    /// the table does not list steps as in any score.</summary>
    [Fact]
    public void InATableScore_TheNameAloneStandsAtTheTablesShape()
    {
        var order = Cm7Order();
        var (up, after) = Step(TableGuitar("c'1@chord(Cm7‸)"), +1);
        Assert.Equal(TableGuitar($"c'1@chord(Cm7 {order[1]})"), after);
        Assert.Equal($"Cm7: shape 2 of {order.Count} ({order[1]})", up.Message);

        var (down, same) = Step(TableGuitar("c'1@chord(Cm7‸)"), -1);
        Assert.Empty(down.Edits);
        Assert.Equal(TableGuitar("c'1@chord(Cm7)"), same);
        Assert.Contains("this score's layout table lists it, so it shows (guitar: x35343)", down.Message);

        var (atTable, removed) = Step(TableGuitar("c'1@chord(Cm7 x35343‸)"), -1);
        Assert.Equal(TableGuitar("c'1@chord(Cm7)"), removed);
        Assert.Contains("layout table lists it, so the name alone still shows it", atTable.Message);

        Assert.Equal(TableGuitar("c'1@chord(G 320003)"), Up("c'1@chord(G‸)", TableGuitar));
    }

    // ---- chordDiagrams … all (owner's decision 2026-09-28): a name alone already draws the default

    private static string AllGuitar(string music) => "layout { chordDiagrams all }\n" + Guitar(music);

    /// <summary>In an <c>all</c> score the name alone shows the default, so Up writes the shape
    /// AFTER it and Down on the name does nothing; Down at a WRITTEN default removes it, as in
    /// any score (owner, 2026-09-28: <c>@chord(Cm7 x35343)</c> + Down = <c>@chord(Cm7)</c>).</summary>
    [Fact]
    public void InAnAllScore_UpFromTheNameWritesTheNextShape_AndDownAtAWrittenDefaultRemovesIt()
    {
        var order = Cm7Order();
        var (up, after) = Step(AllGuitar("c'1@chord(Cm7‸)"), +1);
        Assert.Equal(AllGuitar($"c'1@chord(Cm7 {order[1]})"), after);
        Assert.Equal($"Cm7: shape 2 of {order.Count} ({order[1]})", up.Message);

        var (down, same) = Step(AllGuitar("c'1@chord(Cm7‸)"), -1);
        Assert.False(down.Fallback);
        Assert.Empty(down.Edits);
        Assert.Equal(AllGuitar("c'1@chord(Cm7)"), same);
        Assert.Contains("this score draws every chord, so it shows the default (guitar: x35343)", down.Message);

        var (atDefault, removed) = Step(AllGuitar("c'1@chord(Cm7 x35343‸)"), -1);
        Assert.False(atDefault.Fallback);
        Assert.Equal(AllGuitar("c'1@chord(Cm7)"), removed);
        Assert.Contains("shape removed", atDefault.Message);

        // From the next shape Down still writes the default, and Up walks on as elsewhere.
        Assert.Equal(AllGuitar("c'1@chord(Cm7 x35343)"), Down($"c'1@chord(Cm7 {order[1]}‸)", AllGuitar));
        Assert.Equal(AllGuitar($"c'1@chord(Cm7 {order[2]})"), Up($"c'1@chord(Cm7 {order[1]}‸)", AllGuitar));

        // A row entry of an `all` score steps the same way.
        static string AllRow(string row) => "layout { chordDiagrams all }\n" + Row(row);
        var gOrder = Order("G", includeStretch: false);
        Assert.Equal(AllRow($"G({gOrder[1]}) | C |"), Up("G‸ | C |", AllRow));
        Assert.Equal(AllRow("G | C |"), Down("G(320003‸) | C |", AllRow));
    }

    // ---------------------------------------------------------------- chords-row entries

    /// <summary>A song with a chords row over a staff whose part frets nothing (so the guitar).</summary>
    private static string Row(string row) => $$"""
        octave absolute
        part gt { clef treble }
        section A {
          gt { c'1 | c'1 | }
          chords prog { {{row}} }
        }
        form { A }
        score { chords prog  staff gt }
        """;

    [Fact]
    public void ARowEntry_Steps_AndDownAtTheDefaultRemovesTheGroup()
    {
        var gOrder = Order("G", includeStretch: false);
        Assert.Equal("320003", gOrder[0]);
        var (up, after) = Step(Row("G‸ | C |"), +1);
        Assert.Equal(Row("G(320003) | C |"), after);
        Assert.Equal($"G: shape 1 of {gOrder.Count} (320003)", up.Message);
        Assert.Equal(Row($"G({gOrder[1]}) | C |"), Up("G(320003‸) | C |", Row));
        Assert.Equal(Row("G | C |"), Down("G(3‸20003) | C |", Row));
        var (down, same) = Step(Row("G‸ | C |"), -1);
        Assert.False(down.Fallback);
        Assert.Empty(down.Edits);
        Assert.Equal(Row("G | C |"), same);
        // Several shapes: the guitar's steps; removing it keeps the other.
        Assert.Equal(Row("F(2010) |"), Down("F(133211‸ 2010) |", Row));
        Assert.Equal(Row("F(2010) |"), Down("F(2010 ‸133211) |", Row));
        Assert.Equal(Row("F(2010 133211) |"), Up("F(2010‸) |", Row));
    }

    /// <summary>The row's tuning is the first score's that places it; the status bar says when
    /// another score draws it on another tuning.</summary>
    [Fact]
    public void ARowEntry_StepsOnTheFirstScoresTuning_AndSaysSo()
    {
        static string TwoScores(string row) => $$"""
            layout uke { chordDiagrams ukulele }
            octave absolute
            part gt { clef treble }
            section A {
              gt { c'1 | }
              chords prog { {{row}} }
            }
            form { A }
            score u { layout uke  chords prog  staff gt }
            score g { chords prog  staff gt }
            """;
        var (response, after) = Step(TwoScores("G‸ |"), +1);
        Assert.Equal(TwoScores("G(0232) |"), after);
        Assert.Contains("another draws guitar", response.Message);
    }

    [Fact]
    public void UpAtTheLastShape_ChangesNothing_AndSaysWhereItIs()
    {
        var order = Cm7Order();
        var (response, after) = Step(Guitar($"c'1@chord(Cm7 {order[^1]}‸)"), +1);
        Assert.False(response.Fallback);
        Assert.Empty(response.Edits);
        Assert.Equal($"Cm7: shape {order.Count} of {order.Count} ({order[^1]})", response.Message);
    }

    /// <summary>A muted copy of a shape steps from that shape, and its x goes (the owner:
    /// mutes are put back once the shape is chosen).</summary>
    [Fact]
    public void AMutedShape_StepsFromTheShapeItMutes()
    {
        // x3534x is the default x35343 with its top string muted: it stands where x35343 does.
        var order = Cm7Order();
        Assert.Equal(Guitar($"c'1@chord(Cm7 {order[1]})"), Up("c'1@chord(Cm7 x3534x‸)", Guitar));
    }

    // ---- stretch shapes (owner's decision 2026-09-28: off by default, a setting turns them on)

    /// <summary>With the setting off the order holds no stretch shape; on, it holds the
    /// stretch-inclusive rule's — the same drawn default first either way.</summary>
    [Fact]
    public void TheOrder_LeavesStretchShapesOut_UnlessAskedFor()
    {
        var off = Cm7Order(includeStretch: false);
        var on = Cm7Order(includeStretch: true);
        Assert.Equal("x35343", off[0]);
        Assert.Equal("x35343", on[0]);
        Assert.DoesNotContain(off, s => LilySharp.Core.Music.ChordVoicings.IsStretch(ChordVoicingTests.Shape(s)));
        // The README's example, "Cm7: shape 4 of 33 (x3x546)": the normal rule's 33 bases, the
        // default among them — the 14 reaching fret 10 included since 2026-09-28 (the shape
        // grammar writes them dash-separated; until then the step left them out, 19).
        Assert.Equal(33, off.Count);
        Assert.Equal(3, off.IndexOf("x3x546"));
        Assert.Contains("8xx546", on);                    // frets 4 to 8
        Assert.DoesNotContain("8xx546", off);
        Assert.True(on.Count > off.Count);
    }

    /// <summary>The cycle differs: from x35346 Up is the normal order's next, 8x58x6, with the
    /// setting off, and the stretch shape 8xx546 with it on.</summary>
    [Fact]
    public void TheStep_WalksTheOrderTheSettingChooses()
    {
        var off = Cm7Order(includeStretch: false);
        var on = Cm7Order(includeStretch: true);
        Assert.Equal("8x58x6", off[off.IndexOf("x35346") + 1]);
        Assert.Equal("8xx546", on[on.IndexOf("x35346") + 1]);
        Assert.Equal(Guitar("c'1@chord(Cm7 8x58x6)"), Step(Guitar("c'1@chord(Cm7 x35346‸)"), +1).After);
        var (response, after) = Step(Guitar("c'1@chord(Cm7 x35346‸)"), +1, includeStretch: true);
        Assert.Equal(Guitar("c'1@chord(Cm7 8xx546)"), after);
        Assert.Equal($"Cm7: shape {on.IndexOf("8xx546") + 1} of {on.Count} (8xx546)", response.Message);
    }

    /// <summary>A WRITTEN stretch shape with the setting off is not in the order, yet steps from
    /// where it would sort: 8xx546 (position 4) goes Up to the first normal shape after it,
    /// 8x58x6 (position 5), and Down to the last before it, x35346 (position 3).</summary>
    [Fact]
    public void AWrittenStretchShape_StepsToTheNormalShapesAroundIt()
    {
        Assert.Equal(Guitar("c'1@chord(Cm7 8x58x6)"), Up("c'1@chord(Cm7 8xx546‸)", Guitar));
        Assert.Equal(Guitar("c'1@chord(Cm7 x35346)"), Down("c'1@chord(Cm7 8xx546‸)", Guitar));
        // A muted copy of it sorts as the stretch shape it mutes.
        Assert.Equal(Guitar("c'1@chord(Cm7 8x58x6)"), Up("c'1@chord(Cm7 8xx54x‸)", Guitar));
    }

    /// <summary>An <c>@chord</c> with no symbol — a bare shape — has nothing of its own to step, so
    /// the keys step the note it is written on (owner's request 2026-10-17); it used to fall back
    /// to the editor's command.</summary>
    [Fact]
    public void AShapeWithNoSymbol_StepsItsNote()
    {
        Assert.Equal(Guitar("d'1@chord(x3x546)"), Step(Guitar("c'1@chord(x3x5‸46)"), +1, semitone: true).After);
        Assert.Equal(Guitar("c''1@chord(x3x546)"), Up("c'1@chord(x3x5‸46)", Guitar));
    }

    // ---------------------------------------------------------------- notes

    [Fact]
    public void ANote_GainsOrLosesOneMark_ItsDurationAndDotsKept()
    {
        Assert.Equal(Plain("c''4. d'8"), Up("c'4.‸ d'8", Plain));
        Assert.Equal(Plain("c4 d'8"), Up("c,‸4 d'8", Plain));            // a comma goes first
        Assert.Equal(Plain("c4 d'8"), Down("‸c'4 d'8", Plain));
        Assert.Equal(Plain("c,4 d'8"), Down("c‸4 d'8", Plain));
        Assert.Equal(Plain("cis'''16 r"), Up("ci‸s''16 r", Plain));     // a third '
        Assert.Equal(Plain("c,,,2 r"), Down("c,,‸2 r", Plain));
    }

    [Fact]
    public void AChordMember_MovesAlone()
    {
        Assert.Equal(Plain("<c' e'' g'>4"), Up("<c' e‸' g'>4", Plain));
        Assert.Equal(Plain("<c' e g'>4"), Down("<c' e'‸ g'>4", Plain));
    }

    [Fact]
    public void AChordAsAWhole_StepsTheMarksAfterItsBracket()
        => Assert.Equal(Plain("<c' e' g'>'4"), Up("<c' e' g'>‸4", Plain));

    [Fact]
    public void EveryCaret_Steps()
        => Assert.Equal(Plain("c''4 d'4 e''4"), Up("c'‸4 d'4 e'4‸", Plain));

    [Fact]
    public void ASelection_StepsEveryNoteInIt_RestsNot()
        => Assert.Equal(Plain("c'4 d''4 r4 <e'' g''>4 f'4"), Up("c'4 «d'4 r4 <e' g'>4» f'4", Plain));

    [Fact]
    public void NothingSteppable_AnswersFallback()
    {
        var (response, after) = Step(Plain("c'4 r‸4 d'2 |"), +1);
        Assert.True(response.Fallback);
        Assert.Empty(response.Edits);
        Assert.Equal(Plain("c'4 r4 d'2 |"), after);
        Assert.True(Step(Plain("c'4 r4 d'2 |‸"), -1).Response.Fallback);
    }

    // ---------------------------------------------------------------- semitones (2026-10-15)

    /// <summary>Relative octaves (the language's default).</summary>
    private static string Rel(string music) => MusicSource.Wrap(music);

    /// <summary>A semitone step of the marked source; the text after it.</summary>
    private static string Semi(string marked, int direction, Func<string, string> book)
    {
        var (response, after) = Step(book(marked), direction, semitone: true);
        Assert.False(response.Fallback, response.Error);
        return after;
    }

    /// <summary>Owner's decision 2026-10-16: the step goes to the next tone of the key, spelled
    /// as the signature spells it (F major: a → bes, bes → c); a note off the scale goes to the
    /// nearest tone of it that way (b → c up, bes down).</summary>
    [Fact]
    public void AScaleStep_InAFlatKey_WalksTheScale()
    {
        Assert.Equal(Plain("key f major bes'4"), Semi("key f major a'‸4", +1, Plain));
        Assert.Equal(Plain("key f major c''4"), Semi("key f major bes'‸4", +1, Plain));
        Assert.Equal(Plain("key f major bes'4"), Semi("key f major c''‸4", -1, Plain));
        Assert.Equal(Plain("key f major g'4"), Semi("key f major a'‸4", -1, Plain));
        Assert.Equal(Plain("key f major c''4"), Semi("key f major b'‸4", +1, Plain));
        Assert.Equal(Plain("key f major bes'4"), Semi("key f major b'‸4", -1, Plain));
    }

    /// <summary>In G major e steps up to fis; with no signature the white keys, and a black key
    /// steps to its neighbour that way.</summary>
    [Fact]
    public void AScaleStep_InASharpKey_AndInCMajor()
    {
        Assert.Equal(Plain("key g major fis'4"), Semi("key g major e'‸4", +1, Plain));
        Assert.Equal(Plain("key g major g'4"), Semi("key g major fis'‸4", +1, Plain));
        Assert.Equal(Plain("key g major fis'4"), Semi("key g major g'‸4", -1, Plain));
        Assert.Equal(Plain("d'4"), Semi("c'‸4", +1, Plain));
        Assert.Equal(Plain("d'4"), Semi("cis'‸4", +1, Plain));
        Assert.Equal(Plain("c'4"), Semi("cis'‸4", -1, Plain));
        Assert.Equal(Plain("f'4"), Semi("e'‸4", +1, Plain));
    }

    /// <summary>A minor key walks its signature and its leading tone (owner's decision
    /// 2026-10-16: D minor c, cis, d); a church mode its own signature (D dorian: b natural).</summary>
    [Fact]
    public void AScaleStep_InAMinorKey_WalksTheSignature_AndTheLeadingTone()
    {
        Assert.Equal(Plain("key d minor cis'4"), Semi("key d minor c'‸4", +1, Plain));
        Assert.Equal(Plain("key d minor d'4"), Semi("key d minor cis'‸4", +1, Plain));
        Assert.Equal(Plain("key d minor cis'4"), Semi("key d minor d'‸4", -1, Plain));
        Assert.Equal(Plain("key d minor c'4"), Semi("key d minor cis'‸4", -1, Plain));
        Assert.Equal(Plain("key d minor bes'4"), Semi("key d minor a'‸4", +1, Plain));
        Assert.Equal(Plain("key a minor gis'4"), Semi("key a minor g'‸4", +1, Plain));
        Assert.Equal(Plain("key f minor e'4"), Semi("key f minor ees'‸4", +1, Plain));
        Assert.Equal(Plain("key a aeolian a'4"), Semi("key a aeolian g'‸4", +1, Plain));
        Assert.Equal(Plain("key d dorian b'4"), Semi("key d dorian a'‸4", +1, Plain));
    }

    /// <summary>The key is the part declaration's, then the file's, when the music writes none.</summary>
    [Fact]
    public void AScaleStep_ReadsThePartsKey_ThenTheFiles()
    {
        Assert.Equal(MusicSource.Wrap("bes'4", "octave absolute\nkey f major"),
            Semi("a'‸4", +1, m => MusicSource.Wrap(m, "octave absolute\nkey f major")));
        string Part(string m) => $$"""
            octave absolute
            key g major
            part melody { key ees major }
            section A { melody { {{m}} } }
            form { A }
            score { staff melody }
            """;
        Assert.Equal(Part("aes'4"), Semi("g'‸4", +1, Part));
    }

    /// <summary>The note keeps its pitch (owner's decision 2026-10-15): a new letter that the
    /// octave rule would put elsewhere gets its marks corrected — in absolute octaves b' → c'',
    /// and in relative ones f b → f c' (the nearest c is below the f).</summary>
    [Fact]
    public void AScaleStep_KeepsThePitch_AcrossTheLettersOctave()
    {
        Assert.Equal(Plain("c''4"), Semi("b'‸4", +1, Plain));
        Assert.Equal(Plain("b'4"), Semi("c''‸4", -1, Plain));
        Assert.Equal(Plain("key ges major ces''4"), Semi("key ges major bes'‸4", +1, Plain));
        Assert.Equal(Rel("f4 c'4"), Semi("f4 b‸4", +1, Rel));
        Assert.Equal(Rel("f4 b4"), Semi("f4 c'‸4", -1, Rel));
        Assert.Equal(Rel("a4 c4"), Semi("a4 b‸4", +1, Rel));
        Assert.Equal([74], Step(Plain("c'‸4"), +1, semitone: true).Response.Pitches);
    }

    [Fact]
    public void AScaleStep_MovesAMember_OrEveryNoteSelected()
    {
        Assert.Equal(Plain("<c' f' g'>4"), Semi("<c' e'‸ g'>4", +1, Plain));
        Assert.Equal(Plain("d'4 r4 <d' g'>4"), Semi("«c'4 r4 <cis' f'>4»", +1, Plain));
    }

    /// <summary>Owner's decision 2026-10-16: on a chord as a whole (its <c>&gt;</c> or its
    /// duration) Ctrl+Shift+Up raises the lowest member an octave — the next inversion — and
    /// Down lowers the highest; the other members keep their pitches. PageUp / PageDown still
    /// move the whole chord (<c>&lt;c e g&gt;'</c>).</summary>
    [Fact]
    public void AChord_Inverts_OnTheKeyPair()
    {
        Assert.Equal(Plain("<c'' e' g'>4"), Semi("<c' e' g'>‸4", +1, Plain));
        Assert.Equal(Plain("<c' e' g>4"), Semi("<c' e' g'>4‸", -1, Plain));
        Assert.Equal(Plain("<e' c g'>4"), Semi("<e' c, g'>‸4", +1, Plain));
        // Relative octaves: the members after the raised one keep sounding where they were.
        int[] was = AuditionAt(Rel("<c' e g>‸4")).Pitches;
        Assert.Equal(3, was.Length);
        var (up, upText) = Step(Rel("<c' e g>‸4"), +1, semitone: true);
        Assert.True(was[1..].Append(was[0] + 12).SequenceEqual(up.Pitches), upText);
        var (down, downText) = Step(Rel("<c' e g>‸4"), -1, semitone: true);
        Assert.True(was[..2].Prepend(was[2] - 12).SequenceEqual(down.Pitches), downText);
        Assert.Contains("an octave up", up.Message);
        Assert.Equal(Plain("<c' e' g'>'4"), Step(Plain("<c' e' g'>‸4"), +1).After);
    }

    /// <summary>The step writes the file's flats: LilyPond's contractions es / as where the file
    /// writes them, the language's ees / aes otherwise.</summary>
    [Fact]
    public void AScaleStep_WritesTheFilesFlats()
    {
        Assert.Equal(Plain("key bes major as'4 es'4"), Semi("key bes major as'4 f'‸4", -1, Plain));
        Assert.Equal(Plain("key bes major ees'4"), Semi("key bes major f'‸4", -1, Plain));
    }

    [Fact]
    public void ASemitone_LeavesAQuarterTone_AndSaysWhy()
    {
        var (response, after) = Step(Plain("cih'‸4"), +1, semitone: true);
        Assert.False(response.Fallback);
        Assert.Empty(response.Edits);
        Assert.Equal(Plain("cih'4"), after);
        Assert.Contains("octave only", response.Message);
    }

    [Fact]
    public void ASemitone_OnAShape_StepsTheShape()
        => Assert.Equal(Guitar("c'1@chord(Cm7 x35343)"),
            Step(Guitar("c'1@chord(Cm7‸)"), +1, semitone: true).After);

    // ---------------------------------------------------------------- a tempo

    [Fact]
    public void ATempo_StepsByOne_OnTheSemitoneKeys_ByTen_OnTheOctaveKeys()
    {
        Assert.Equal(Plain("tempo 121 c'4"), Step(Plain("tem‸po 120 c'4"), +1, semitone: true).After);
        Assert.Equal(Plain("tempo 119 c'4"), Step(Plain("tempo 120‸ c'4"), -1, semitone: true).After);
        Assert.Equal(Plain("tempo 130 c'4"), Step(Plain("‸tempo 120 c'4"), +1).After);
        Assert.Equal(Plain("tempo 110 c'4"), Step(Plain("tempo 1‸20 c'4"), -1).After);
    }

    [Fact]
    public void ATempo_StepsTheBpm_NotTheBeatUnitNorTheSwing()
    {
        Assert.Equal(Plain("tempo \"Allegro\" 4. = 100 c'4"),
            Step(Plain("tempo \"Allegro\" 4‸. = 90 c'4"), +1).After);
        Assert.Equal(Plain("tempo 121 swing 16 c'4"), Step(Plain("tempo 120 swing 1‸6 c'4"), +1, semitone: true).After);
    }

    [Fact]
    public void ATempo_StopsAtOne_AndOneWithNoNumber_SaysWhy()
    {
        var (low, lowAfter) = Step(Plain("‸tempo 5 c'4"), -1);
        Assert.False(low.Fallback);
        Assert.Equal(Plain("tempo 1 c'4"), lowAfter);
        var (none, noneAfter) = Step(Plain("‸tempo swing c'4"), +1);
        Assert.False(none.Fallback);
        Assert.Empty(none.Edits);
        Assert.Equal(Plain("tempo swing c'4"), noneAfter);
        Assert.Contains("no metronome number", none.Message);
        Assert.Empty(none.Pitches);
    }

    // ---------------------------------------------------------------- a key

    [Fact]
    public void AKey_WalksTheCircleOfFifths_OnTheSemitoneKeys()
    {
        Assert.Equal(Plain("key g major c'4"), Step(Plain("k‸ey c major c'4"), +1, semitone: true).After);
        Assert.Equal(Plain("key f major c'4"), Step(Plain("key c major‸ c'4"), -1, semitone: true).After);
        Assert.Equal(Plain("key fis major c'4"), Step(Plain("key b‸ major c'4"), +1, semitone: true).After);
        Assert.Equal(Plain("key bes major c'4"), Step(Plain("key ‸f major c'4"), -1, semitone: true).After);
        Assert.Equal(Plain("key e minor c'4"), Step(Plain("key a mi‸nor c'4"), +1, semitone: true).After);
        Assert.Equal(Plain("key a dorian c'4"), Step(Plain("‸key d dorian c'4"), +1, semitone: true).After);
    }

    [Fact]
    public void AKey_WritesTheFilesFlats_AndStopsAtSeven()
    {
        Assert.Equal(Plain("key es major as'4"), Step(Plain("key ‸bes major as'4"), -1, semitone: true).After);
        Assert.Equal(Plain("key ees major c'4"), Step(Plain("key ‸bes major c'4"), -1, semitone: true).After);
        var (top, after) = Step(Plain("key ‸cis major c'4"), +1, semitone: true);
        Assert.False(top.Fallback);
        Assert.Equal(Plain("key cis major c'4"), after);
        Assert.Contains("7 sharps", top.Message);
    }

    [Fact]
    public void AKey_OnTheOctaveKeys_PageUpTheParallel_PageDownTheRelative()
    {
        Assert.Equal(Plain("key c minor c'4"), Step(Plain("key ‸c major c'4"), +1).After);
        Assert.Equal(Plain("key c major c'4"), Step(Plain("key c min‸or c'4"), +1).After);
        Assert.Equal(Plain("key a minor c'4"), Step(Plain("key ‸c major c'4"), -1).After);
        Assert.Equal(Plain("key c major c'4"), Step(Plain("key a minor‸ c'4"), -1).After);
        Assert.Equal(Plain("key bes major c'4"), Step(Plain("‸key g minor c'4"), -1).After);
        Assert.Equal(Plain("key cis minor c'4"), Step(Plain("‸key e major c'4"), -1).After);
        var (modal, after) = Step(Plain("‸key d dorian c'4"), +1);
        Assert.False(modal.Fallback);
        Assert.Equal(Plain("key d dorian c'4"), after);
        Assert.Contains("major and minor only", modal.Message);
    }

    // ---------------------------------------------------------------- a clef

    // Owner's decisions 2026-10-24: Up / Down one clef at a time in the order Ctrl+Space lists
    // them (treble at the top; Up up the list, Down down it), PageUp / PageDown a group.

    [Fact]
    public void AClef_StepsInTheOrderItsCompletionLists()
    {
        foreach (bool header in new[] { false, true })
        {
            var listed = LilySharpLanguageServer.GetClefCompletions(header).Items
                .OrderBy(i => i.SortText, StringComparer.Ordinal).Select(i => i.Label!).ToList();
            Assert.Equal("treble", listed[0]);
            string Book(string clef) => header
                ? $"part lh {{ clef {clef} }}\nsection A {{ lh {{ c4 d e f }} }}\n"
                : Plain($"clef {clef} c4");
            for (int i = 0; i + 1 < listed.Count; i++)
            {
                Assert.Equal(Book(listed[i + 1]), Step(Book("‸" + listed[i]), -1, semitone: true).After);
                Assert.Equal(Book(listed[i]), Step(Book("‸" + listed[i + 1]), +1, semitone: true).After);
            }
            var (top, after) = Step(Book("‸treble"), +1, semitone: true);
            Assert.False(top.Fallback);
            Assert.Equal(Book("treble"), after);
            Assert.Contains("first", top.Message);
            Assert.Empty(top.Pitches);
        }
    }

    [Fact]
    public void AClef_InMusic_WalksTheFiveItTakes()
    {
        Assert.Equal(Plain("clef treble_8 c4"), Step(Plain("cl‸ef treble c4"), -1, semitone: true).After);
        Assert.Equal(Plain("clef alto c4"), Step(Plain("clef treble_8‸ c4"), -1, semitone: true).After);
        Assert.Equal(Plain("clef tenor c4"), Step(Plain("‸clef alto c4"), -1, semitone: true).After);
        Assert.Equal(Plain("clef bass c4"), Step(Plain("clef tenor‸ c4"), -1, semitone: true).After);
        var (bottom, same) = Step(Plain("clef ‸bass c4"), -1, semitone: true);
        Assert.Equal(Plain("clef bass c4"), same);
        Assert.Contains("last", bottom.Message);
    }

    [Fact]
    public void AClef_OnTheOctaveKeys_JumpsAGroup_ToItsUsualClef()
    {
        // G -> C -> F (-> percussion in a header): PageDown down the list, PageUp up it.
        Assert.Equal(Plain("clef alto c4"), Step(Plain("clef ‸treble c4"), -1).After);
        Assert.Equal(Plain("clef bass c4"), Step(Plain("clef ‸tenor c4"), -1).After);
        Assert.Equal(Plain("clef treble c4"), Step(Plain("clef ‸alto c4"), +1).After);
        Assert.Equal(Plain("clef alto c4"), Step(Plain("clef ‸bass c4"), +1).After);
        // Inside music there is no percussion clef to jump to.
        var (last, same) = Step(Plain("clef ‸bass c4"), -1);
        Assert.False(last.Fallback);
        Assert.Equal(Plain("clef bass c4"), same);
        Assert.Contains("last", last.Message);

        const string Book = "part lh {{ clef {0} octave 3 }}\nsection A {{ lh {{ c4 d e f }} }}\n";
        string S(string clef) => string.Format(Book, clef);
        Assert.Equal(S("percussion"), Step(S("ba‸ss_8"), -1).After);
        Assert.Equal(S("bass"), Step(S("‸percussion"), +1).After);
        Assert.Equal(S("treble"), Step(S("‸soprano"), +1).After);
    }

    // ---------------------------------------------------------------- a music font

    [Fact]
    public void AMusicFont_WalksTheFontsThisMachineHas_OnEitherPair()
    {
        // Owner's request 2026-10-24: Ctrl+Shift+Up on `music` picks a font that is there.
        var fonts = LilySharp.Core.Svg.Layout.MusicFonts.AvailableNames();
        Assert.True(fonts.Count >= 2, string.Join(", ", fonts));
        Assert.Equal("Emmentaler", fonts[0]);
        const string Book = "fonts {{\n  music {0}\n}}\npart m {{ }}\nsection A {{ m {{ c4 d e f }} }}\n";
        string S(string music) => string.Format(Book, music);
        string Q(int i) => $"\"{fonts[i]}\"";

        // Down goes down the list (the owner's decision, the reverse of the first day's).
        var (down, after) = Step(S("‸music \"Emmentaler\""), -1, semitone: true);
        Assert.False(down.Fallback, down.Error);
        Assert.Equal(S("music " + Q(1)), after);
        Assert.Contains($"2 of {fonts.Count}", down.Message);
        Assert.Empty(down.Pitches);
        Assert.Equal(S("music " + Q(1)), Step(S("music \"Emmen‸taler\""), -1).After);
        Assert.Equal(S("music " + Q(0)), Step(S("mu‸sic " + Q(1)), +1, semitone: true).After);

        // At either end it stays, and says so.
        var (first, same) = Step(S("‸music \"Emmentaler\""), +1, semitone: true);
        Assert.False(first.Fallback);
        Assert.Equal(S("music \"Emmentaler\""), same);
        Assert.Contains("first", first.Message);
        // No name reads as the default; a name the machine lacks steps from the ends.
        Assert.Equal(S("music " + Q(1)), Step(S("music‸"), -1).After);
        Assert.Equal(S("music " + Q(0)), Step(S("music \"‸NoSuchFont\""), -1).After);
        Assert.Equal(S("music " + Q(fonts.Count - 1)), Step(S("music \"NoSuchFont\"‸"), +1).After);
        // In a fallback list the name under the caret steps.
        Assert.Equal(S($"music {Q(0)} {Q(1)}"), Step(S($"music {Q(0)} ‸{Q(0)}"), -1).After);
    }

    [Fact]
    public void ATextFont_WalksTheFacesItsCompletionOffers()
    {
        // Owner's request 2026-10-24: the same keys on `bend "TeX Gyre Heros"`.
        var offered = LilySharpLanguageServer.GetFontNameCompletions("bend").Items
            .OrderBy(i => i.SortText ?? i.Label, StringComparer.Ordinal).Select(i => i.Label!)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        int at = offered.FindIndex(f => f == "TeX Gyre Heros");
        Assert.True(at >= 0 && at + 1 < offered.Count, string.Join(", ", offered));
        const string Book = "fonts {{\n  bend {0}\n}}\npart m {{ }}\nsection A {{ m {{ c4 d e f }} }}\n";
        string S(string bend) => string.Format(Book, bend);

        var (down, after) = Step(S("‸bend \"TeX Gyre Heros\""), -1, semitone: true);
        Assert.False(down.Fallback, down.Error);
        Assert.Equal(S($"bend \"{offered[at + 1]}\""), after);
        Assert.Contains($"{at + 2} of {offered.Count}", down.Message);
        Assert.Empty(down.Pitches);
        Assert.Equal(S($"bend \"{offered[at]}\""), Step(S($"bend \"{offered[at + 1]}‸\""), +1).After);
    }

    [Fact]
    public void AFontSize_AndAFontStep_StepTheirNumber()
    {
        // Owner's request 2026-10-24: size in tenths (PageUp / PageDown: whole staff spaces), step by one.
        const string Book = "fonts {{\n  lyrics \"TeX Gyre Schola\" {0}\n}}\npart m {{ }}\nsection A {{ m {{ c4 d e f }} }}\n";
        string S(string attribute) => string.Format(Book, attribute);
        var (up, after) = Step(S("size ‸2.2"), +1, semitone: true);
        Assert.False(up.Fallback, up.Error);
        Assert.Equal(S("size 2.3"), after);
        Assert.Contains("staff spaces", up.Message);
        Assert.Equal(S("size 2.1"), Step(S("si‸ze 2.2"), -1, semitone: true).After);
        Assert.Equal(S("size 3.2"), Step(S("size 2.2‸"), +1).After);
        Assert.Equal(S("size 3"), Step(S("‸size 2.9"), +1, semitone: true).After);
        var (top, same) = Step(S("‸size 20"), +1, semitone: true);
        Assert.Equal(S("size 20"), same);
        Assert.Contains("largest", top.Message);

        Assert.Equal(S("step +2"), Step(S("step ‸+1"), +1, semitone: true).After);
        Assert.Equal(S("step 0"), Step(S("‸step +1"), -1).After);
        Assert.Equal(S("step -1"), Step(S("step 0‸"), -1, semitone: true).After);
        // The entry's name still steps the face.
        var (face, _) = Step(S("size 2.2").Replace("\"TeX", "\"‸TeX"), -1, semitone: true);
        Assert.DoesNotContain("size", face.Message);
    }

    // ---------------------------------------------------------------- midiInstrument

    private const string InstrumentBook = "part m {{ midiInstrument {0} }}\nsection A {{ m {{ c4 d e f }} }}\n";

    private static string Instrument(string name) => string.Format(InstrumentBook, name);

    [Fact]
    public void AMidiInstrument_WalksTheGeneralMidiNames_AndSoundsTheNewOne()
    {
        // Owner's requests 2026-10-24: step it, and hear it. Down goes down the list (program 1
        // at the top), Up up it — the owner's decision, the reverse of the first day's.
        var (down, after) = Step(Instrument("‸\"acoustic grand\""), -1, semitone: true);
        Assert.False(down.Fallback, down.Error);
        Assert.Equal(Instrument("\"bright acoustic\""), after);
        Assert.Contains("program 2 of 128", down.Message);
        Assert.Equal([60], down.Pitches);
        Assert.Equal(1, down.Program);
        Assert.Equal(Instrument("\"acoustic grand\""), Step(Instrument("\"bright ‸acoustic\""), +1, semitone: true).After);

        // The octave pair walks the families of eight: piano -> chromatic percussion -> organ.
        Assert.Equal(Instrument("\"celesta\""), Step(Instrument("\"bright ‸acoustic\""), -1).After);
        Assert.Equal(Instrument("\"acoustic grand\""), Step(Instrument("\"celesta\"‸"), +1).After);
        Assert.Equal(Instrument("\"celesta\""), Step(Instrument("\"music box\"‸"), +1).After);
        var (bass, _) = Step(Instrument("\"fretless ‸bass\""), -1, semitone: true);
        Assert.Equal([43], bass.Pitches);
        Assert.Equal(36, bass.Program);

        // The ends stay; a name off the list steps from them.
        var (first, same) = Step(Instrument("‸\"acoustic grand\""), +1, semitone: true);
        Assert.Equal(Instrument("\"acoustic grand\""), same);
        Assert.Contains("first", first.Message);
        Assert.Equal(Instrument("\"gunshot\""), Step(Instrument("\"no‸ such\""), +1).After);
    }

    [Fact]
    public void AMidiInstrument_AnswersTheAudition_WithItsProgram()
    {
        // Typing or completing the name sounds it: the editor asks auditionAt after the change.
        string text = Instrument("\"viola\"");
        var answer = Open(text).AuditionAt(new AuditionAtParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocUri },
            Offset = text.IndexOf("viola", StringComparison.Ordinal) + 5,
        });
        Assert.Equal("instrument", answer.Kind);
        Assert.Equal([60], answer.Pitches);
        Assert.Equal(41, answer.Program);
        string partial = Instrument("\"viol\"");
        var none = Open(partial).AuditionAt(new AuditionAtParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocUri },
            Offset = partial.IndexOf("viol", StringComparison.Ordinal) + 4,
        });
        Assert.Empty(none.Pitches);
    }

    // ---------------------------------------------------------------- a percent mark

    [Fact]
    public void APercentMark_StepsTheBarsItRepeats()
    {
        // Owner's request 2026-10-24: % -> %2 -> %3, and back.
        const string Bars = "c'4 d' e' f' | g'1 | a'1 | ";
        var (up, after) = Step(Plain(Bars + "‸% |"), +1, semitone: true);
        Assert.False(up.Fallback, up.Error);
        Assert.Equal(Plain(Bars + "%2 |"), after);
        Assert.Contains("2 bars", up.Message);
        Assert.Empty(up.Pitches);
        Assert.Equal(Plain(Bars + "%3 |"), Step(Plain(Bars + "%2‸ |"), +1).After);
        Assert.Equal(Plain(Bars + "% |"), Step(Plain(Bars + "%‸2 |"), -1, semitone: true).After);
        var (one, same) = Step(Plain(Bars + "%‸ |"), -1, semitone: true);
        Assert.False(one.Fallback);
        Assert.Equal(Plain(Bars + "% |"), same);
        Assert.Contains("one bar", one.Message);
    }

    // ---------------------------------------------------------------- a staff group

    private static string Group(string keyword) => Score($"{keyword} {{ staff rh staff lh }}");

    private static string Score(string body) =>
        $"part rh {{ }}\npart lh {{ clef bass }}\nsection A {{ rh {{ c'1 }} lh {{ c1 }} }}\nform {{ A }}\nscore {{ {body} }}\n";

    [Fact]
    public void AGroup_CondensesAndOpensAgain_KeepingTheLayout()
    {
        // Owner's decision 2026-10-24: condensedStaff walks with the groups; its body is bare names.
        Assert.Equal(Score("condensedStaff { rh lh }"), Step(Score("‸choirStaff { staff rh staff lh }"), -1, semitone: true).After);
        Assert.Equal(Score("condensedStaff {\n    rh\n    lh\n  }"),
            Step(Score("choir‸Staff {\n    staff rh\n    staff lh\n  }"), -1, semitone: true).After);
        Assert.Equal(Score("choirStaff { staff rh staff lh }"), Step(Score("condensed‸Staff { rh lh }"), +1, semitone: true).After);
        // A clef written on the condensed staff goes onto each staff.
        Assert.Equal(Score("choirStaff { staff bass rh staff bass lh }"),
            Step(Score("‸condensedStaff bass { rh lh }"), +1, semitone: true).After);
        // Staves of different clefs do not go onto one staff.
        var (refused, same) = Step(Score("‸choirStaff { staff bass rh staff lh }"), -1, semitone: true);
        Assert.False(refused.Fallback);
        Assert.Equal(Score("choirStaff { staff bass rh staff lh }"), same);
        Assert.Contains("different clefs", refused.Message);
        // The same clef on every staff goes after the keyword, once (owner's report 2026-10-24).
        Assert.Equal(Score("condensedStaff treble {\n   rh\n   lh\n  }"),
            Step(Score("‸choirStaff {\n   staff treble rh\n   staff treble lh\n  }"), -1, semitone: true).After);
        Assert.Equal(Score("choirStaff { staff treble rh staff treble lh }"),
            Step(Score("‸condensedStaff treble { rh lh }"), +1, semitone: true).After);
        // A lyrics row stays a group.
        var (lyrics, kept) = Step(Score("‸choirStaff { staff rh lyrics v staff lh }"), -1, semitone: true);
        Assert.Equal(Score("choirStaff { staff rh lyrics v staff lh }"), kept);
        Assert.Contains("lyrics row", lyrics.Message);
    }

    [Fact]
    public void AScoreRowsClefWord_StepsLikeAClefInMusic()
    {
        // Owner's request 2026-10-24: the alto of `condensedStaff alto { … }` — the five music clefs.
        Assert.Equal(Score("condensedStaff tenor { rh lh }"), Step(Score("condensedStaff al‸to { rh lh }"), -1, semitone: true).After);
        Assert.Equal(Score("condensedStaff treble_8 { rh lh }"), Step(Score("condensedStaff ‸alto { rh lh }"), +1, semitone: true).After);
        Assert.Equal(Score("condensedStaff bass { rh lh }"), Step(Score("condensedStaff alto‸ { rh lh }"), -1).After);
        Assert.Equal(Score("combinedStaff treble { rh lh }"), Step(Score("combinedStaff ‸alto { rh lh }"), +1).After);
        Assert.Equal(Score("staff tenor rh"), Step(Score("staff ‸bass rh"), +1, semitone: true).After);
        var (top, same) = Step(Score("condensedStaff ‸treble { rh lh }"), +1, semitone: true);
        Assert.Equal(Score("condensedStaff treble { rh lh }"), same);
        Assert.Contains("first", top.Message);
    }

    [Fact]
    public void AStaffGroup_OnTheOctaveKeys_JumpsBetweenTheFamilies()
    {
        // Owner's decision 2026-10-24: PageDown / PageUp — the brackets ⇄ one staff, to the
        // family's usual kind (grandStaff, condensedStaff), the body rewritten as on Up / Down.
        Assert.Equal(Score("condensedStaff { rh lh }"), Step(Score("‸staffGroup { staff rh staff lh }"), -1).After);
        Assert.Equal(Score("condensedStaff { rh lh }"), Step(Score("‸choirStaff { staff rh staff lh }"), -1).After);
        Assert.Equal(Score("grandStaff { staff rh staff lh }"), Step(Score("‸combinedStaff { rh lh }"), +1).After);
        Assert.Equal(Score("grandStaff { staff treble rh staff treble lh }"), Step(Score("‸condensedStaff treble { rh lh }"), +1).After);
        var (first, same) = Step(Score("‸grandStaff { staff rh staff lh }"), +1);
        Assert.Equal(Score("grandStaff { staff rh staff lh }"), same);
        Assert.Contains("first family", first.Message);
        var (last, kept) = Step(Score("‸condensedStaff { rh lh }"), -1);
        Assert.Equal(Score("condensedStaff { rh lh }"), kept);
        Assert.Contains("last family", last.Message);
    }

    [Fact]
    public void ACombinedStaff_WalksWithTheGroups_OnlyForTwoParts()
    {
        // Owner's question 2026-10-24: combinedStaff takes exactly two parts, so the walk offers
        // it to a body of two and passes it by otherwise, saying so.
        Assert.Equal(Score("combinedStaff { rh lh }"), Step(Score("‸condensedStaff { rh lh }"), -1, semitone: true).After);
        Assert.Equal(Score("condensedStaff { rh lh }"), Step(Score("combined‸Staff { rh lh }"), +1, semitone: true).After);
        // The walk down from a group of two, the layout kept, and back up to the group.
        Assert.Equal(Score("condensedStaff {\n  rh\n  lh\n}"),
            Step(Score("‸choirStaff {\n  staff rh\n  staff lh\n}"), -1, semitone: true).After);
        Assert.Equal(Score("combinedStaff {\n  rh\n  lh\n}"),
            Step(Score("‸condensedStaff {\n  rh\n  lh\n}"), -1, semitone: true).After);
        Assert.Equal(Score("choirStaff { staff rh staff lh }"), Step(Score("‸condensedStaff { rh lh }"), +1, semitone: true).After);
        // Between the two one-staff kinds only the word changes, a written clef kept.
        Assert.Equal(Score("condensedStaff bass { rh lh }"), Step(Score("‸combinedStaff bass { rh lh }"), +1, semitone: true).After);
        // Three parts: condensedStaff is the end of the walk.
        const string Three = "part a {{ }}\npart b {{ }}\npart c {{ }}\nsection A {{ a {{ c'1 }} b {{ c'1 }} c {{ c'1 }} }}\nform {{ A }}\nscore {{ {0} }}\n";
        var (end, same) = Step(string.Format(Three, "‸condensedStaff { a b c }"), -1, semitone: true);
        Assert.Equal(string.Format(Three, "condensedStaff { a b c }"), same);
        Assert.Contains("combinedStaff takes exactly two parts", end.Message);
    }

    [Fact]
    public void AScoreRowsPart_StepsThroughTheFilesParts()
    {
        // Owner's request 2026-10-24: `staff melody` — the declared parts in order, Down down the list.
        const string Book = "part melody {{ }}\npart bass {{ clef bass }}\nsection A {{ melody {{ c'1 }} bass {{ c1 }} drums {{ c1 }} }}\nform {{ A }}\nscore {{ {0} }}\n";
        string S(string row) => string.Format(Book, row);
        var (down, after) = Step(S("staff mel‸ody"), -1, semitone: true);
        Assert.False(down.Fallback, down.Error);
        Assert.Equal(S("staff bass"), after);
        Assert.Contains("part 2 of 3", down.Message);
        // A section's cell declares a part too.
        Assert.Equal(S("staff drums"), Step(S("staff ‸bass"), -1).After);
        Assert.Equal(S("staff melody"), Step(S("staff bass‸"), +1, semitone: true).After);
        var (first, same) = Step(S("staff ‸melody"), +1, semitone: true);
        Assert.Equal(S("staff melody"), same);
        Assert.Contains("first", first.Message);
        // The same on a tab's part and a condensed staff's member.
        Assert.Equal(S("tab bass"), Step(S("tab ‸melody"), -1, semitone: true).After);
        Assert.Equal(S("condensedStaff { melody drums }"), Step(S("condensedStaff { melody ‸bass }"), -1, semitone: true).After);
    }

    [Fact]
    public void AStaffRowsLineCount_StepsByOneLine()
    {
        // Owner's request 2026-10-24: the 1 of `staff melody as lines 1`.
        int max = LilySharp.Core.Semantics.LanguageVocabulary.MaxStaffLines;
        var (up, after) = Step(Score("staff rh as lines ‸1"), +1, semitone: true);
        Assert.False(up.Fallback, up.Error);
        Assert.Equal(Score("staff rh as lines 2"), after);
        Assert.Equal(Score("staff rh as lines 2"), Step(Score("staff rh as lines ‸3"), -1).After);
        var (fewest, same) = Step(Score("staff rh as lines 1‸"), -1, semitone: true);
        Assert.Equal(Score("staff rh as lines 1"), same);
        Assert.Contains("fewest", fewest.Message);
        var (most, kept) = Step(Score($"staff rh as lines ‸{max}"), +1, semitone: true);
        Assert.Equal(Score($"staff rh as lines {max}"), kept);
        Assert.Contains("most", most.Message);
        // The row's part still steps the part.
        Assert.Equal(Score("staff lh as lines 1"), Step(Score("staff ‸rh as lines 1"), -1, semitone: true).After);
    }

    [Fact]
    public void AStaffRowsSelector_StepsItsWord_AndItsValue()
    {
        // Owner's request 2026-10-24: the word after `staff melody as` — lines, removeEmpty,
        // pitch, Down down the list as the completion shows them, each with its first value.
        var (down, after) = Step(Score("staff rh as ‸lines 1"), -1, semitone: true);
        Assert.False(down.Fallback, down.Error);
        Assert.Equal(Score("staff rh as removeEmpty true"), after);
        Assert.Equal(Score("staff rh as pitch transposed"), Step(Score("staff rh as remove‸Empty all"), -1).After);
        Assert.Equal(Score("staff rh as lines 1"), Step(Score("staff rh as removeEmpty‸ true"), +1, semitone: true).After);
        var (first, same) = Step(Score("staff rh as ‸lines 3"), +1, semitone: true);
        Assert.Equal(Score("staff rh as lines 3"), same);
        Assert.Contains("first", first.Message);
        // A selector the row already writes is passed by.
        Assert.Equal(Score("staff rh as pitch transposed removeEmpty true"),
            Step(Score("staff rh as ‸lines 1 removeEmpty true"), -1, semitone: true).After);
        Assert.Equal(Score("staff rh as removeEmpty true lines 1"),
            Step(Score("staff rh as ‸pitch concert lines 1"), +1, semitone: true).After);

        // The values: removeEmpty true, all, false; pitch transposed, concert.
        Assert.Equal(Score("staff rh as removeEmpty all"), Step(Score("staff rh as removeEmpty ‸true"), -1, semitone: true).After);
        Assert.Equal(Score("staff rh as removeEmpty false"), Step(Score("staff rh as removeEmpty all‸"), -1).After);
        Assert.Equal(Score("staff rh as pitch concert"), Step(Score("staff rh as pitch ‸transposed"), -1, semitone: true).After);
        var (lastValue, kept) = Step(Score("staff rh as pitch ‸concert"), -1, semitone: true);
        Assert.Equal(Score("staff rh as pitch concert"), kept);
        Assert.Contains("last", lastValue.Message);
    }

    [Fact]
    public void AStaffRow_AndATabRow_SwitchToEachOther()
    {
        // Owner's request 2026-10-24.
        var (tab, after) = Step(Score("‸staff rh"), +1, semitone: true);
        Assert.False(tab.Fallback, tab.Error);
        Assert.Equal(Score("tab rh"), after);
        Assert.Equal(Score("staff rh"), Step(Score("ta‸b rh"), -1).After);
        // The word only one of them reads goes: a staff's clef, a tab's tuning.
        Assert.Equal(Score("tab rh"), Step(Score("‸staff bass rh"), -1, semitone: true).After);
        Assert.Equal(Score("staff rh"), Step(Score("tab‸ bass4 rh"), +1, semitone: true).After);
        Assert.Equal(Score("staff rh as numbers"), Step(Score("‸tab rh as numbers"), -1).After);
        // Inside a group a staff stays a staff.
        var (inGroup, same) = Step(Score("grandStaff { ‸staff rh staff lh }"), +1, semitone: true);
        Assert.Equal(Score("grandStaff { staff rh staff lh }"), same);
        Assert.Contains("not a tab", inGroup.Message);
    }

    [Fact]
    public void AStaffGroup_StepsItsKeyword_InTheOrderItsCompletionLists()
    {
        // Owner's request 2026-10-24: grandStaff, staffGroup, choirStaff — Down down the list.
        var (down, after) = Step(Group("grand‸Staff"), -1, semitone: true);
        Assert.False(down.Fallback, down.Error);
        Assert.Equal(Group("staffGroup"), after);
        Assert.Contains("bracket", down.Message);
        Assert.Equal(Group("choirStaff"), Step(Group("‸staffGroup"), -1, semitone: true).After);
        Assert.Equal(Group("staffGroup"), Step(Group("choirStaff‸"), +1, semitone: true).After);
        var (last, stays) = Step(Score("‸combinedStaff { rh lh }"), -1, semitone: true);
        Assert.Equal(Score("combinedStaff { rh lh }"), stays);
        Assert.Contains("last", last.Message);
        var (first, same) = Step(Group("‸grandStaff"), +1, semitone: true);
        Assert.False(first.Fallback);
        Assert.Equal(Group("grandStaff"), same);
        Assert.Contains("first", first.Message);
        // Inside the braces the keys are not the group's.
        var (inside, unchanged) = Step(Group("grandStaff").Replace("{ staff rh", "{ ‸staff rh"), -1, semitone: true);
        Assert.Equal(Group("grandStaff"), unchanged);
        Assert.DoesNotContain("staffGroup", inside.Message ?? "");
    }

    // ---------------------------------------------------------------- value words (owner's request 2026-10-24)

    private static string Header(string property) => $"part m {{ {property} }}\nsection A {{ m {{ c'4 d' e' f' }} }}\nform {{ A }}\nscore {{ staff m }}\n";

    /// <summary>A value word walks its keyword's list as the completion lists it, Down down it.</summary>
    private static void WalksTheList(Func<string, string> book, System.Collections.Generic.IReadOnlyCollection<string> values,
        Func<string, string>? marked = null)
    {
        var list = values.ToList();
        marked ??= v => "‸" + v;
        for (int i = 0; i + 1 < list.Count; i++)
        {
            Assert.Equal(book(list[i + 1]), Step(book(marked(list[i])), -1, semitone: true).After);
            Assert.Equal(book(list[i]), Step(book(marked(list[i + 1])), +1).After);
        }
        var (first, same) = Step(book(marked(list[0])), +1, semitone: true);
        Assert.False(first.Fallback, first.Error);
        Assert.Equal(book(list[0]), same);
        Assert.Contains("first", first.Message);
    }

    [Fact]
    public void APartHeadersValueWords_WalkTheirLists()
    {
        var V = typeof(LilySharp.Core.Semantics.LanguageVocabulary);
        WalksTheList(v => Header("transposition " + v), LilySharp.Core.Semantics.LanguageVocabulary.TranspositionMarkers);
        WalksTheList(v => Header("pedal " + v), LilySharp.Core.Semantics.LanguageVocabulary.PedalStyles);
        WalksTheList(v => Header("pitch " + v), LilySharp.Core.Semantics.LanguageVocabulary.PitchModes);
    }

    [Fact]
    public void AScoresPitchWalksThePagesModes_AndTheFilesTheNotesModes()
    {
        WalksTheList(v => $"part m {{ }}\nsection A {{ m {{ c'4 }} }}\nform {{ A }}\nscore full pitch {v} {{ staff m }}\n",
            LilySharp.Core.Semantics.LanguageVocabulary.PagePitchModes);
        WalksTheList(v => $"pitch {v}\npart m {{ }}\nsection A {{ m {{ c'4 }} }}\n",
            LilySharp.Core.Semantics.LanguageVocabulary.PitchModes);
    }

    [Fact]
    public void ALayoutsValueWords_WalkTheirLists()
    {
        string L(string entry) => $"layout {{ {entry} }}\npart m {{ }}\nsection A {{ m {{ c'4 }} }}\n";
        var vocab = new (string Key, System.Collections.Generic.IReadOnlyCollection<string> Words)[]
        {
            ("markTempo", LilySharp.Core.Semantics.LanguageVocabulary.MarkArrangements),
            ("accidentals", LilySharp.Core.Semantics.LanguageVocabulary.AccidentalStyleWords),
            ("sectionLabels", LilySharp.Core.Semantics.LanguageVocabulary.SectionLabelStyles),
            ("partCombineText", LilySharp.Core.Semantics.LanguageVocabulary.PartCombineTextWords),
            ("chordQualities", LilySharp.Core.Semantics.LanguageVocabulary.ChordQualityStyleWords),
            ("minorChords", LilySharp.Core.Semantics.LanguageVocabulary.MinorChordWords),
            ("chordNames", LilySharp.Core.Semantics.LanguageVocabulary.ChordNameWords),
            ("chordList", LilySharp.Core.Semantics.LanguageVocabulary.ChordListWords),
            ("compressRests", LilySharp.Core.Semantics.LanguageVocabulary.CompressRestsWords),
        };
        foreach (var (key, words) in vocab)
            WalksTheList(v => L($"{key} {v}"), words);
    }

    [Fact]
    public void BarNumbers_StepTheirPolicy_AndEverysNumber()
    {
        string L(string entry) => $"layout {{ {entry} }}\npart m {{ }}\nsection A {{ m {{ c'4 }} }}\n";
        var words = LilySharp.Core.Semantics.LanguageVocabulary.BarNumberPolicies.ToList();
        int every = words.IndexOf("every");
        Assert.True(every > 0, string.Join(", ", words));
        // Arriving at `every` writes a number; leaving it takes the number away.
        Assert.Equal(L("barNumbers every 4"), Step(L("barNumbers ‸" + words[every - 1]), -1, semitone: true).After);
        Assert.Equal(L("barNumbers " + words[every - 1]), Step(L("barNumbers ‸every 4"), +1, semitone: true).After);
        // The number steps by one, from 1.
        Assert.Equal(L("barNumbers every 5"), Step(L("barNumbers every ‸4"), +1, semitone: true).After);
        var (fewest, same) = Step(L("barNumbers every 1‸"), -1, semitone: true);
        Assert.Equal(L("barNumbers every 1"), same);
        Assert.Contains("fewest", fewest.Message);
    }

    [Fact]
    public void AScoreRowsAsWords_AndAFontsFamily_Step()
    {
        Assert.Equal(Score("tab rh as full"), Step(Score("tab rh as ‸numbers"), -1, semitone: true).After);
        Assert.Equal(Score("tab rh as numbers"), Step(Score("tab rh as full‸"), +1).After);
        const string Chords = "part m {{ }}\nchords c {{ C1 }}\nsection A {{ m {{ c'1 }} }}\nform {{ A }}\nscore {{ staff m chords c as {0} }}\n";
        Assert.Equal(string.Format(Chords, "names"), Step(string.Format(Chords, "‸roman"), -1, semitone: true).After);
        const string Fonts = "fonts {{\n  chord as {0}\n}}\npart m {{ }}\nsection A {{ m {{ c'4 }} }}\n";
        Assert.Equal(string.Format(Fonts, "sans"), Step(string.Format(Fonts, "‸serif"), -1, semitone: true).After);
    }

    [Fact]
    public void APapersSize_WalksThePages()
    {
        var sizes = LilySharp.Core.Semantics.LanguageVocabulary.PaperSizeNames.ToList();
        int a4 = sizes.FindIndex(s => s == "a4");
        Assert.True(a4 >= 0 && a4 + 1 < sizes.Count, string.Join(", ", sizes));
        string P(string size) => $"paper {{ size {size} }}\npart m {{ }}\nsection A {{ m {{ c'4 }} }}\n";
        // `a4` lexes as a pitch and a duration; the two are one name.
        Assert.Equal(P(sizes[a4 + 1]), Step(P("a‸4"), -1, semitone: true).After);
        Assert.Equal(P("a4"), Step(P(sizes[a4 + 1] + "‸"), +1).After);
    }

    [Fact]
    public void AnInstrumentPreset_WalksThePresets_AndSounds()
    {
        var presets = LilySharp.Core.Semantics.LanguageVocabulary.InstrumentPresets.ToList();
        Assert.True(presets.Count > 2);
        var (down, after) = Step(Header("instrument ‸" + presets[0]), -1, semitone: true);
        Assert.False(down.Fallback, down.Error);
        Assert.Equal(Header("instrument " + presets[1]), after);
        Assert.Contains($"2 of {presets.Count}", down.Message);
        Assert.Single(down.Pitches);
        Assert.Equal(LilySharp.Core.Svg.Model.InstrumentDefaults.GetMidiProgram(presets[1]), down.Program);
        // A hyphenated preset is one word; a display name after it stays.
        int sax = presets.FindIndex(p => p.Contains('-'));
        Assert.True(sax > 0);
        Assert.Equal(Header($"instrument {presets[sax - 1]} \"Sax\""),
            Step(Header($"instrument ‸{presets[sax]} \"Sax\""), +1, semitone: true).After);
        // Typing it sounds it too: the audition answers on it.
        string text = Header("instrument " + presets[1]);
        var answer = Open(text).AuditionAt(new AuditionAtParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocUri },
            Offset = text.IndexOf(presets[1], StringComparison.Ordinal) + 1,
        });
        Assert.Equal("instrumentpreset", answer.Kind);
        Assert.Single(answer.Pitches);
    }

    [Fact]
    public void ATupletsRatio_WalksTheUsualRatios()
    {
        Assert.Equal(Plain("tuplet 4/3 { c'8 d' e' }"), Step(Plain("tuplet ‸3/2 { c'8 d' e' }"), +1, semitone: true).After);
        Assert.Equal(Plain("tuplet 5/4 { c'8 d' e' }"), Step(Plain("tuplet ‸4/3 { c'8 d' e' }"), +1).After);
        Assert.Equal(Plain("tuplet 3/2 { c'8 d' e' }"), Step(Plain("tuplet 2/‸3 { c'8 d' e' }"), +1, semitone: true).After);
        Assert.Equal(Plain("tuplet 3/2 { c'8 d' e' }"), Step(Plain("tuplet 4/3‸ { c'8 d' e' }"), -1, semitone: true).After);
        var (fewest, same) = Step(Plain("tuplet ‸2/3 { c'8 d' }"), -1, semitone: true);
        Assert.Equal(Plain("tuplet 2/3 { c'8 d' }"), same);
        Assert.Contains("fewest", fewest.Message);
        // A ratio off the list steps from where its notes sort.
        Assert.Equal(Plain("tuplet 6/4 { c'8 d' e' }"), Step(Plain("tuplet ‸5/3 { c'8 d' e' }"), +1, semitone: true).After);
    }

    [Fact]
    public void ATremolosCount_AndARestsBars_StepByOne()
    {
        Assert.Equal(Plain("repeat tremolo 5 { c'16 }"), Step(Plain("repeat tremolo ‸4 { c'16 }"), +1, semitone: true).After);
        Assert.Equal(Plain("repeat tremolo 3 { c'16 }"), Step(Plain("repeat tremolo 4‸ { c'16 }"), -1).After);
        Assert.Equal(Plain("R1*5"), Step(Plain("R1*‸4"), +1, semitone: true).After);
        var (one, same) = Step(Plain("R1*‸1"), -1, semitone: true);
        Assert.Equal(Plain("R1*1"), same);
        Assert.Contains("fewest", one.Message);
    }

    [Fact]
    public void APartialsLength_WalksTheNoteValues_LongerOnUp()
    {
        const string Book = "partial {0}\npart m {{ }}\nsection A {{ m {{ c'4 | c'1 }} }}\n";
        string S(string length) => string.Format(Book, length);
        Assert.Equal(S("2"), Step(S("‸4"), +1, semitone: true).After);
        Assert.Equal(S("8"), Step(S("4‸"), -1).After);
        Assert.Equal(S("8."), Step(S("‸4."), -1, semitone: true).After);
        var (longest, same) = Step(S("‸1"), +1, semitone: true);
        Assert.Equal(S("1"), same);
        Assert.Contains("longest", longest.Message);
    }

    [Fact]
    public void AFormsSection_WalksTheDeclaredSections()
    {
        // Owner's request 2026-10-24: the sections in the order written, Down down the list.
        const string Book = "part m {{ }}\nsection A {{ m {{ c'1 }} }}\nsection B {{ m {{ d'1 }} }}\nsection C {{ m {{ e'1 }} }}\nform {{ {0} }}\n";
        string S(string form) => string.Format(Book, form);
        var (down, after) = Step(S("A ‸A"), -1, semitone: true);
        Assert.False(down.Fallback, down.Error);
        Assert.Equal(S("A B"), after);
        Assert.Contains("section 2 of 3", down.Message);
        Assert.Equal(S("A C"), Step(S("A ‸B"), -1).After);
        // The silent spelling keeps its ~.
        Assert.Equal(S("~A"), Step(S("~‸B"), +1, semitone: true).After);
        var (first, same) = Step(S("‸A"), +1, semitone: true);
        Assert.Equal(S("A"), same);
        Assert.Contains("first", first.Message);
    }

    [Fact]
    public void ALyricsTracksSings_WalksThePartsThenTheVoices()
    {
        const string Book = "part sop {{ }}\npart alt {{ }}\nlyrics v sings {0} {{ la la }}\nsection A {{ sop {{ c''1 }} alt {{ a'1 }} }}\nform {{ A }}\nscore {{ staff sop lyrics v }}\n";
        string S(string target) => string.Format(Book, target);
        Assert.Equal(S("alt"), Step(S("‸sop"), -1, semitone: true).After);
        Assert.Equal(S("sop"), Step(S("alt‸"), +1).After);
    }

    [Fact]
    public void ASelectedLength_Steps_ACaretStillStepsThePitch()
    {
        // Owner's request 2026-10-24: the length steps when it alone is selected.
        Assert.Equal(Plain("c'2 d'4"), Step(Plain("c'«4» d'4"), +1, semitone: true).After);
        Assert.Equal(Plain("c'8 d'4"), Step(Plain("c'«4» d'4"), -1).After);
        Assert.Equal(Plain("c'8. d'16"), Step(Plain("c'«4». d'16"), -1, semitone: true).After);
        Assert.Equal(Plain("r2 d'4"), Step(Plain("r«4» d'4"), +1, semitone: true).After);
        Assert.Equal(Plain("<c' e'>8"), Step(Plain("<c' e'>«4»"), -1, semitone: true).After);
        var (longest, same) = Step(Plain("c'«1»"), +1, semitone: true);
        Assert.Equal(Plain("c'1"), same);
        Assert.Contains("longest", longest.Message);
        // A caret on the length is the note's, as before: the pitch moves.
        Assert.NotEqual(Plain("c'2 d'4"), Step(Plain("c'4‸ d'4"), +1, semitone: true).After);
    }

    [Fact]
    public void ASelectedChordName_StepsItsQuality_ACaretStillStepsTheShape()
    {
        const string Book = "part gt {{ }}\nchords c {{ section A {{ {0} | }} }}\nsection A {{ gt {{ c'1 | }} }}\nform {{ A }}\nscore {{ chords c staff gt }}\n";
        string S(string entry) => string.Format(Book, entry);
        Assert.Equal(S("Cm"), Step(S("«C»"), -1, semitone: true).After);
        Assert.Equal(S("C7"), Step(S("«Cm»"), -1).After);
        Assert.Equal(S("C"), Step(S("«Cm»"), +1, semitone: true).After);
        // The root and a bass stay.
        Assert.Equal(S("Fm/A"), Step(S("«F/A»"), -1, semitone: true).After);
        // A caret on the entry still steps its diagram's shape (it writes one).
        Assert.Contains("{ C(", Step(S("C‸"), +1, semitone: true).After);
        // On an @chord in the music too.
        Assert.Equal(Guitar("c'1@chord(C7)"), Step(Guitar("c'1@chord(«Cm»)"), -1, semitone: true).After);
    }

    // ---------------------------------------------------------------- tuning

    private static string Tuning(string word) => $"part b {{ tuning {word} }}\nsection A {{ b {{ c4 d e f }} }}\n";

    [Fact]
    public void ATuning_WalksTheTunings_AndSoundsItsOpenStrings()
    {
        // Owner's request 2026-10-24: `tuning bass` as midiInstrument.
        // Down goes down the list (the owner's decision, the reverse of the first day's).
        var (down, after) = Step(Tuning("‸bass"), -1, semitone: true);
        Assert.False(down.Fallback, down.Error);
        Assert.Equal(Tuning("bassdropd"), after);
        Assert.Contains("D1 A1 D2 G2", down.Message);
        Assert.Equal([26, 33, 38, 43], down.Pitches);
        Assert.Equal(33, down.Program);
        Assert.True(down.Strum);   // the strings one after another, lowest first
        Assert.Equal(Tuning("guitarasus4"), Step(Tuning("bass‸"), +1, semitone: true).After);
        // A double steps from its tuning's place.
        Assert.Equal(Tuning("bassdropd"), Step(Tuning("‸bass4"), -1, semitone: true).After);

        // The octave pair: a family at a time; Up inside one goes to its first.
        Assert.Equal(Tuning("violin"), Step(Tuning("ba‸ss"), -1).After);
        Assert.Equal(Tuning("bass"), Step(Tuning("bass‸dropd"), +1).After);
        Assert.Equal(Tuning("guitar"), Step(Tuning("‸bass"), +1).After);
        var (first, same) = Step(Tuning("‸guitar"), +1);
        Assert.Equal(Tuning("guitar"), same);
        Assert.Contains("first", first.Message);
        var (last, stays) = Step(Tuning("‸tenorukulele"), -1);
        Assert.Equal(Tuning("tenorukulele"), stays);
        Assert.Contains("last", last.Message);
    }

    [Fact]
    public void ATuning_AnswersTheAudition_WithItsOpenStrings()
    {
        string text = Tuning("cello");
        var answer = Open(text).AuditionAt(new AuditionAtParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocUri },
            Offset = text.IndexOf("cello", StringComparison.Ordinal) + 5,
        });
        Assert.Equal("tuning", answer.Kind);
        Assert.Equal([36, 43, 50, 57], answer.Pitches);
        Assert.Equal(42, answer.Program);
        Assert.True(answer.Strum);
    }

    // ---------------------------------------------------------------- a dynamic, a time, octave N, transpose

    [Fact]
    public void ADynamic_StepsALevel_AndAHairpinTurnsWithTheKeys()
    {
        Assert.Equal(Plain("c'4@mf d'"), Step(Plain("c'4@m‸p d'"), +1, semitone: true).After);
        Assert.Equal(Plain("c'4@pp d'"), Step(Plain("c'4@p‸ d'"), -1).After);
        Assert.Equal(Plain("c'4@f.up d'"), Step(Plain("c'4@m‸f.up d'"), +1, semitone: true).After);
        Assert.Equal(Plain("c'4@cresc d'"), Step(Plain("c'4@de‸cresc d'"), +1, semitone: true).After);
        Assert.Equal(Plain("c'4@decresc d'"), Step(Plain("c'4@cresc‸ d'"), -1, semitone: true).After);
        Assert.Equal(Plain("c'4@cresc d'"), Step(Plain("c'4@‸dim d'"), +1, semitone: true).After);
        var (again, after) = Step(Plain("c'4@cre‸sc d'"), +1, semitone: true);
        Assert.False(again.Fallback);
        Assert.Equal(Plain("c'4@cresc d'"), after);
        // Just before the '@' the caret is on the note.
        Assert.Equal(Plain("d'4@p d'"), Step(Plain("c'4‸@p d'"), +1, semitone: true).After);
    }

    [Fact]
    public void AnArticulation_StepsAlongItsGroup_OnEitherPair()
    {
        // The owner's two pairs: Up is the up-bow and the hammer-on, Down the other; a press
        // past the end stays (owner's decision 2026-10-16: small groups do not rotate).
        Assert.Equal(Plain("c'4@upBow d'"), Step(Plain("c'4@down‸Bow d'"), +1, semitone: true).After);
        Assert.Equal(Plain("c'4@downBow d'"), Step(Plain("c'4@upBow‸ d'"), -1, semitone: true).After);
        Assert.Equal(Plain("c'4@upBow d'"), Step(Plain("c'4@down‸Bow d'"), +1).After);
        Assert.Equal(Plain("c'4@hammerOn d'"), Step(Plain("c'4@p‸ullOff d'"), +1, semitone: true).After);
        Assert.Equal(Plain("c'4@pullOff d'"), Step(Plain("c'4@hammer‸On d'"), -1, semitone: true).After);
        var (end, same) = Step(Plain("c'4@up‸Bow d'"), +1, semitone: true);
        Assert.False(end.Fallback);
        Assert.Equal(Plain("c'4@upBow d'"), same);
        Assert.Equal("upBow / downBow", end.Message);
        // A longer group walks in order, the placement stays, and the ends stop.
        Assert.Equal(Plain("c'4@portato.up d'"), Step(Plain("c'4@stacc‸ato.up d'"), +1, semitone: true).After);
        Assert.Equal(Plain("c'4@staccatissimo d'"), Step(Plain("c'4@stacc‸ato d'"), -1, semitone: true).After);
        Assert.Equal(Plain("c'4@tenuto d'"), Step(Plain("c'4@ten‸uto d'"), +1, semitone: true).After);
        Assert.Equal(Plain("c'4@longFermata d'"), Step(Plain("c'4@ferm‸ata d'"), +1, semitone: true).After);
        Assert.Equal(Plain("c'4@longFermata d'"), Step(Plain("c'4@longFermata‸ d'"), +1, semitone: true).After);
        Assert.Equal(Plain("c'4@prall d'"), Step(Plain("c'4@mor‸dent d'"), +1, semitone: true).After);
        // The stem's direction (owner's request 2026-10-17): Up is the stem up, on either pair.
        Assert.Equal(Plain("c'4@stemUp d'"), Step(Plain("c'4@stem‸Down d'"), +1, semitone: true).After);
        Assert.Equal(Plain("c'4@stemDown d'"), Step(Plain("c'4@stemUp‸ d'"), -1).After);
        var (stem, top) = Step(Plain("c'4@stem‸Up d'"), +1, semitone: true);
        Assert.False(stem.Fallback);
        Assert.Equal(Plain("c'4@stemUp d'"), top);
        Assert.Equal("stemUp / stemDown", stem.Message);
        // The status bar lists the group, nothing more (owner's decision 2026-10-16), from the
        // top down (owner's request 2026-10-17: Up above Down, the way the keys go).
        var (said, _) = Step(Plain("c'4@ferm‸ata d'"), +1, semitone: true);
        Assert.Equal("longFermata / fermata / shortFermata", said.Message);
        // A mark in no group steps the note it is written on instead (owner's request 2026-10-17),
        // on either pair; so does a dynamic on no ladder, and a member's mark its member. A bare
        // @chord on a <…> chord is the chord's: the key pair inverts it, the octave pair moves it.
        var (alone, after) = Step(Plain("c'4@t‸ap d'"), +1, semitone: true);
        Assert.False(alone.Fallback);
        Assert.Equal(Plain("d'4@tap d'"), after);
        Assert.Equal(Plain("c''4@tap d'"), Step(Plain("c'4@t‸ap d'"), +1).After);
        Assert.Equal(Plain("d'4@sfz d'"), Step(Plain("c'4@s‸fz d'"), +1, semitone: true).After);
        Assert.Equal(Plain("<c' f'@tap g'>4"), Step(Plain("<c' e'@t‸ap g'>4"), +1, semitone: true).After);
        Assert.Equal(Plain("<c'' e' g'>4@chord"), Step(Plain("<c' e' g'>4@ch‸ord"), +1, semitone: true).After);
        Assert.Equal(Plain("<c' e' g'>'4@chord"), Step(Plain("<c' e' g'>4@ch‸ord"), +1).After);
        // A mark that steps keeps the keys even where it stays (@upBow Up, above).
        // Just before the '@' the caret is on the note.
        Assert.Equal(Plain("d'4@downBow d'"), Step(Plain("c'4‸@downBow d'"), +1, semitone: true).After);
    }

    [Fact]
    public void ATime_StepsItsBeats_AndWalksTheCommonMetersOnTheOctaveKeys()
    {
        Assert.Equal(Plain("time 4/4 c'4"), Step(Plain("ti‸me 3/4 c'4"), +1, semitone: true).After);
        Assert.Equal(Plain("time 2/4 c'4"), Step(Plain("time 3/4‸ c'4"), -1, semitone: true).After);
        // 3/8 2/4 6/8 3/4 2/2 4/4 9/8 12/8, by the length of the bar.
        Assert.Equal(Plain("time 3/4 c'4"), Step(Plain("time 6/‸8 c'4"), +1).After);
        Assert.Equal(Plain("time 2/4 c'4"), Step(Plain("time 6/‸8 c'4"), -1).After);
        Assert.Equal(Plain("time 4/4 c'4"), Step(Plain("‸time 2/2 c'4"), +1).After);
        // Off the list: the next bar longer or shorter.
        Assert.Equal(Plain("time 9/8 c'4"), Step(Plain("time 5/‸4 c'4"), -1).After);
        Assert.Equal(Plain("time 6/8 c'4"), Step(Plain("time 5/‸8 c'4"), +1).After);
        var (last, same) = Step(Plain("time 12/‸8 c'4"), +1);
        Assert.False(last.Fallback);
        Assert.Equal(Plain("time 12/8 c'4"), same);
        var (additive, after) = Step(Plain("time 3+2/‸8 c'8"), +1, semitone: true);
        Assert.False(additive.Fallback);
        Assert.Equal(Plain("time 3+2/8 c'8"), after);
    }

    [Fact]
    public void APartsOctave_StepsByOne()
    {
        const string Book = "part lh {{ clef bass {0} }}\nsection A {{ lh {{ c4 d e f }} }}\n";
        string S(string octave) => string.Format(Book, octave);
        Assert.Equal(S("octave 4"), Step(S("oct‸ave 3"), +1, semitone: true).After);
        Assert.Equal(S("octave 2"), Step(S("octave 3‸"), -1).After);
        var (low, after) = Step(S("octave ‸0"), -1, semitone: true);
        Assert.False(low.Fallback);
        Assert.Equal(S("octave 0"), after);
    }

    [Fact]
    public void AnOctaveMode_UpConvertsToAbsolute_DownToRelative()
    {
        // Owner's request and decision 2026-10-17: on the file's directive or one inside music,
        // Up runs Convert Octaves to Absolute and Down Convert Octaves to Relative (the editor's
        // command, every note keeping its pitch) — the pitches go with the word; a word alone
        // is typed by hand. The step itself edits nothing, and a direction each (not a toggle):
        // Up on `absolute` still asks for absolute. In a part header the mode is an error (a
        // part's `octave` takes a number), so there the keys convert nothing and say so.
        const string Book = "{0}\npart lh {{ clef bass {1} }}\nsection A {{ lh {{ {2}c4 d e f }} }}\n";
        string S(string top = "octave relative", string part = "octave 3", string music = "")
            => string.Format(Book, top, part, music);
        void Converts(string marked, int direction, string mode, bool semitone = false)
        {
            var (response, after) = Step(marked, direction, semitone: semitone);
            Assert.False(response.Fallback);
            Assert.Equal(mode, response.ConvertOctaves);
            Assert.Empty(response.Edits);
            Assert.Equal(marked.Replace("‸", ""), after);
        }
        Converts(S("octave rel‸ative"), +1, "absolute", semitone: true);
        Converts(S("oct‸ave absolute"), -1, "relative");
        Converts(S("octave abso‸lute"), +1, "absolute", semitone: true);
        Converts(S(music: "octave relative‸ "), +1, "absolute");
        Converts(S(music: "‸octave absolute "), -1, "relative", semitone: true);
        // The part's `octave 3` still steps its number, and asks for no conversion.
        var (number, stepped) = Step(S(part: "octave ‸3"), +1);
        Assert.Equal(S(part: "octave 4"), stepped);
        Assert.Null(number.ConvertOctaves);
        var (header, unchanged) = Step(S(part: "octave ‸absolute"), -1);
        Assert.False(header.Fallback);
        Assert.Null(header.ConvertOctaves);
        Assert.Equal(S(part: "octave absolute"), unchanged);
        Assert.Contains("at the top of the file or in the music", header.Message);
    }

    [Fact]
    public void AShift_StepsASemitone_AndNamesTheKeyItLandsIn()
    {
        const string Book = "key {0}\n{1}\npart m {{ clef treble }}\nsection A {{ m {{ c4 d e f }} }}\nform {{ A }}\nscore {{ staff m }}\n";
        string S(string shift, string key = "c major") => string.Format(Book, key, shift);
        Assert.Equal(S("shift +2"), Step(S("sh‸ift +1"), +1, semitone: true).After);
        Assert.Equal(S("shift 0"), Step(S("shift +1‸"), -1, semitone: true).After);
        Assert.Equal(S("shift -1"), Step(S("shift ‸0"), -1).After);
        Assert.Equal(S("shift -3"), Step(S("shift -‸2"), -1, semitone: true).After);
        var (up, _) = Step(S("shift ‸0"), +1, semitone: true);
        Assert.Equal("shift +1: key c major -> des major", up.Message);
        var (down, _) = Step(S("shift ‸0", "g minor"), -1, semitone: true);
        Assert.Equal("shift -1: key g minor -> fis minor", down.Message);
        var (top, same) = Step(S("shift +1‸1"), +1, semitone: true);
        Assert.False(top.Fallback);
        Assert.Equal(S("shift +11"), same);
        // The completion leaves `+1` SELECTED (owner's report 2026-10-17): a selection within
        // one steppable thing steps it, as does one covering the whole line.
        Assert.Equal(S("shift +2"), Step(S("shift «+1»"), +1, semitone: true).After);
        Assert.Equal(S("shift -1"), Step(S("«shift 0»"), -1).After);
    }

    [Fact]
    public void ASelectionWithinOneValue_StepsIt_AndOneAcrossTwoDoesNot()
    {
        Assert.Equal(Plain("c'4@mf d'"), Step(Plain("c'4@«mp» d'"), +1, semitone: true).After);
        // Across two dynamics there is no ONE thing to step (and no note's pitch starts
        // inside the selection), so the key's own command runs; a selection that reaches a
        // note's pitch steps the note, as before.
        Assert.True(Step(Plain("c'4@«mp@f» d'"), +1, semitone: true).Response.Fallback);
        Assert.Equal(Plain("c'4@mp e'"), Step(Plain("c'4@«mp d»'"), +1, semitone: true).After);
    }

    [Fact]
    public void ATranspose_StepsASemitone_SpelledForTheFewestAccidentals()
    {
        const string Book = "{0}part cl {{ clef treble transpose {1} }}\nsection A {{ cl {{ c'4 d e f }} }}\n";
        string S(string key, string target) => string.Format(Book, key, target);
        Assert.Equal(S("", "d"), Step(S("", "de‸s"), +1, semitone: true).After);
        Assert.Equal(S("", "ees"), Step(S("", "d‸"), +1, semitone: true).After);
        Assert.Equal(S("", "c'"), Step(S("", "‸b"), +1, semitone: true).After);
        Assert.Equal(S("", "bes,"), Step(S("", "b‸,"), -1, semitone: true).After);
        // The spelling keeps the transposed key smallest: E major up a semitone is F major
        // (des), not E-sharp major (cis); B-flat major's is B major (cis), not C-flat major.
        Assert.Equal(S("key e major\n", "des"), Step(S("key e major\n", "‸c"), +1, semitone: true).After);
        Assert.Equal(S("key bes major\n", "cis"), Step(S("key bes major\n", "‸c"), +1, semitone: true).After);
        // A tie (F major: G-flat or F-sharp major, six each) goes to the flat.
        Assert.Equal(S("key f major\n", "des"), Step(S("key f major\n", "‸c"), +1, semitone: true).After);
        Assert.Equal(S("", "d'"), Step(S("", "‸d"), +1).After);
        Assert.Equal(S("", "bes"), Step(S("", "bes‸'"), -1).After);
        var (said, _) = Step(S("key f major\n", "‸c"), +1, semitone: true);
        Assert.Contains("up 1 semitone", said.Message);
    }

    [Fact]
    public void ANoteAfterATempo_StillStepsAsANote()
        => Assert.Equal(Plain("tempo 120 d'4"), Step(Plain("tempo 120 ‸c'4"), +1, semitone: true).After);

    // ---------------------------------------------------------------- what sounds

    /// <summary>Cm7's predefined shape x35343 on the standard tuning, counted by hand string by
    /// string: A2+3 = C3 (48), D3+5 = G3 (55), G3+3 = B♭3 (58), B3+4 = E♭4 (63), E4+3 = G4 (67);
    /// the low E is muted, so silent.</summary>
    private static readonly int[] Cm7X35343 = [48, 55, 58, 63, 67];

    [Fact]
    public void AStepToAShape_SoundsItsStrings()
    {
        // Down from the second shape writes the default: x35343 sounds.
        var (response, _) = Step(Guitar($"c'1@chord(Cm7 {Cm7Order()[1]}‸)"), -1);
        Assert.Equal(Cm7X35343, response.Pitches);
    }

    [Fact]
    public void ASteppedNote_SoundsItsNewPitch()
    {
        Assert.Equal([84], Step(Plain("c'‸4 d'4"), +1).Response.Pitches);          // c'' = C6 (Lily#'s absolute c is C4)
        // A stepped member sounds its WHOLE chord with the member moved (owner's decision
        // 2026-10-10: `<…>` is one sound; it used to strike the moved member alone).
        Assert.Equal([72, 79, 88], Step(Plain("<c' e'‸ g'>4"), +1).Response.Pitches); // c' g' e'' = C5 G5 E6
        Assert.Equal([48], Step(Plain("c‸4 d'4"), -1).Response.Pitches);           // c = C4, c, = C3
    }

    private static AuditionAtResponse AuditionAt(string marked)
    {
        var (text, selections) = Marked(marked);
        return Open(text).AuditionAt(new AuditionAtParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocUri },
            Offset = selections[0].Start,
        });
    }

    /// <summary>
    /// The audition and the step name the General MIDI program the preview's SoundFont synth
    /// strikes with: the part's, as the .mid plays it (the flute's 73; a part naming no sound
    /// plays LilyPond's default, 0). A voicing on a rest sounds from no part: the guitar
    /// preset's program, with the guitar timbre, as before.
    /// </summary>
    [Fact]
    public void TheAudition_NamesTheProgramThePartPlays()
    {
        var flute = AuditionAt(Flute("c'4 d'‸4"));
        Assert.Equal([74], flute.Pitches);
        Assert.Equal(73, flute.Program);
        Assert.Equal(1, flute.Timbre);
        Assert.Equal(0, AuditionAt(Plain("c'4 d'‸4")).Program);
        Assert.Equal(73, Step(Flute("c'‸4 d'4"), +1).Response.Program);

        var voicing = AuditionAt(Guitar("r1@chord(Cm7 ‸x35343)"));
        Assert.Equal(4, voicing.Timbre);
        Assert.Equal(24, voicing.Program);
    }

    /// <summary>The target of a tie is no onset in the export, so the audition sounds its
    /// written pitch — in the part's voice, not the default piano (owner's report 2026-10-11:
    /// "the a in the second bar sounds wrong" on <c>e f g a~ | a b c d</c>).</summary>
    [Fact]
    public void ATiedNote_StrikesThePartsInstrument()
    {
        var tied = AuditionAt(Flute("e'4 f' g' a'~ | ‸a' b' c'' d'' |"));
        Assert.Equal([81], tied.Pitches);
        Assert.Equal(73, tied.Program);
        Assert.Equal(1, tied.Timbre);
        // The same note where it is struck, for comparison.
        var struck = AuditionAt(Flute("e'4 f' g' ‸a'~ | a' b' c'' d'' |"));
        Assert.Equal(tied.Pitches, struck.Pitches);
        Assert.Equal(struck.Program, tied.Program);
    }

    /// <summary>A guitar sounds an octave below what is written, a clarinet two semitones:
    /// the tie's target sounds where the struck note does (owner's report 2026-10-11: it rang
    /// an octave high, and wrong on a transposing instrument).</summary>
    [Theory]
    [InlineData("guitar", 62)]      // d' written = 74; the guitar's octave below
    [InlineData("clarinet", 72)]    // the B♭ clarinet: two semitones below
    public void ATiedNote_SoundsWhereThePartsStruckNotesDo(string preset, int sounding)
    {
        string Book(string music) => $$"""
            octave absolute
            part p { instrument {{preset}} }
            section A { p { {{music}} } }
            form { A }
            score { staff p }
            """;
        var struck = AuditionAt(Book("c'4 ‸d'~ | d' e' |"));
        var tied = AuditionAt(Book("c'4 d'~ | ‸d' e' |"));
        Assert.Equal([sounding], struck.Pitches);
        Assert.Equal(struck.Pitches, tied.Pitches);
        Assert.Equal(struck.Program, tied.Program);
    }

    [Fact]
    public void TheCaret_SoundsWhatItIsOn()
    {
        var voicing = AuditionAt(Guitar("c'1@chord(Cm7 ‸x35343)"));
        Assert.Equal("voicing", voicing.Kind);
        Assert.Equal(Cm7X35343, voicing.Pitches);

        var note = AuditionAt(Plain("c'4 d'‸4"));
        Assert.Equal("note", note.Kind);
        Assert.Equal([74], note.Pitches);

        // The caret on a MEMBER of `<…>` sounds the whole chord, under the chord's own key —
        // so arrowing across the members strikes it once (owner's decision 2026-10-10; it
        // used to sound the member alone, as "member").
        var member = AuditionAt(Plain("<c' ‸e' g'>4"));
        Assert.Equal("chord", member.Kind);
        Assert.Equal([72, 76, 79], member.Pitches);

        var chord = AuditionAt(Plain("<c' e' g'>‸4"));
        Assert.Equal("chord", chord.Kind);
        Assert.Equal([72, 76, 79], chord.Pitches);
        Assert.Equal(chord.Key, member.Key);

        Assert.Equal(-1, AuditionAt(Plain("c'4 r‸4")).Key);
        // A name with no shape written sounds the shape drawn for it (K3: every step sounds).
        Assert.Equal(Cm7X35343, AuditionAt(Guitar("c'1@chord(Cm7‸)")).Pitches);
        // No shape to sound: the keys step (and the caret sounds) the note the mark is written on.
        Assert.Equal("note", AuditionAt(Guitar("c'1@chord(\"N.C.\"‸)")).Kind);
    }

    /// <summary>An <c>@chord</c> on a rest or a spacer (owner's decision 2026-09-28: a chord symbol
    /// belongs to the beat) steps and sounds as on a note — the rest itself still does not step.</summary>
    [Fact]
    public void AnAtChordOnARestOrASpacer_StepsAndSounds()
    {
        var order = Cm7Order();
        Assert.Equal(Guitar("r1@chord(Cm7 x35343)"), Up("r1@chord(Cm7‸)", Guitar));
        Assert.Equal(Guitar($"s1@chord(Cm7 {order[1]})"), Up("s1@chord(Cm7 x35343‸)", Guitar));
        Assert.Equal(Guitar("s1@chord(Cm7)"), Down("s1@chord(Cm7 x35343‸)", Guitar));
        var voicing = AuditionAt(Guitar("r1@chord(Cm7 ‸x35343)"));
        Assert.Equal("voicing", voicing.Kind);
        Assert.Equal(Cm7X35343, voicing.Pitches);
        Assert.Equal(Cm7X35343, AuditionAt(Guitar("s1@chord(Cm7‸)")).Pitches);
    }

    [Fact]
    public void TheKey_IsTheSameAnywhereOnOneNote_AndDiffersBetweenNotes()
    {
        int a = AuditionAt(Plain("‸cis'4 d'4")).Key;
        Assert.Equal(a, AuditionAt(Plain("cis'‸4 d'4")).Key);
        Assert.Equal(a, AuditionAt(Plain("cis'4‸ d'4")).Key);
        Assert.NotEqual(a, AuditionAt(Plain("cis'4 ‸d'4")).Key);
    }
}
