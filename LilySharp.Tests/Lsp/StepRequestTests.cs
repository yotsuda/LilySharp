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
        form main { A }
        score main { staff gt }
        """;

    private static string Plain(string music) => MusicSource.Wrap(music, "octave absolute");

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
        bool includeStretch = false)
    {
        var (text, selections) = Marked(marked);
        var response = Open(text).Step(new StepParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocUri },
            Selections = selections.Select(s => new StepSelection { Start = s.Start, End = s.End }).ToArray(),
            Direction = direction,
            IncludeStretch = includeStretch,
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
            form main { A }
            score main { staff uk }
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
        form main { A }
        score main { chords prog  staff gt }
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
            form main { A }
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

    [Fact]
    public void AShapeWithNoSymbol_IsNotSteppable()
        => Assert.True(Step(Guitar("c'1@chord(x3x5‸46)"), +1).Response.Fallback);

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
        Assert.Equal([88], Step(Plain("<c' e'‸ g'>4"), +1).Response.Pitches);      // e'' = E6
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

    [Fact]
    public void TheCaret_SoundsWhatItIsOn()
    {
        var voicing = AuditionAt(Guitar("c'1@chord(Cm7 ‸x35343)"));
        Assert.Equal("voicing", voicing.Kind);
        Assert.Equal(Cm7X35343, voicing.Pitches);

        var note = AuditionAt(Plain("c'4 d'‸4"));
        Assert.Equal("note", note.Kind);
        Assert.Equal([74], note.Pitches);

        var member = AuditionAt(Plain("<c' ‸e' g'>4"));
        Assert.Equal("member", member.Kind);
        Assert.Equal([76], member.Pitches);

        var chord = AuditionAt(Plain("<c' e' g'>‸4"));
        Assert.Equal("chord", chord.Kind);
        Assert.Equal([72, 76, 79], chord.Pitches);

        Assert.Equal(-1, AuditionAt(Plain("c'4 r‸4")).Key);
        // A name with no shape written sounds the shape drawn for it (K3: every step sounds).
        Assert.Equal(Cm7X35343, AuditionAt(Guitar("c'1@chord(Cm7‸)")).Pitches);
        Assert.Equal(-1, AuditionAt(Guitar("c'1@chord(\"N.C.\"‸)")).Key);   // no shape, no sound
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
