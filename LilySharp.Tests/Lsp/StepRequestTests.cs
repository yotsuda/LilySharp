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

    private static string Up(string marked, Func<string, string> book, bool semitone = false)
        => Stepped(marked, +1, book, semitone);

    private static string Down(string marked, Func<string, string> book, bool semitone = false)
        => Stepped(marked, -1, book, semitone);

    private static string Stepped(string marked, int direction, Func<string, string> book, bool semitone = false)
    {
        var (response, after) = Step(book(marked), direction, semitone: semitone);
        Assert.False(response.Fallback, response.Error);
        Assert.Equal(3, response.Version);
        return after;
    }

    // ---------------------------------------------------------------- @chord shapes (K3)

    /// <summary>The order the step walks for Cm7 on the guitar: the drawn default first —
    /// LilyPond's predefined x35343 — then Lily#'s maximal shapes, each once.</summary>
    private static List<string> Cm7Order(bool includeStretch = false) => Order("Cm7", includeStretch);

    // Owner's decision 2026-11-11: the step offers a guitar's shapes on its four highest strings, the
    // root not always lowest, unless a shape it already offers holds them.
    [Fact]
    public void TheStep_OffersTheUpperStrings_InversionsIncluded_NoMutedCopy()
    {
        var c = Order("C", includeStretch: false);
        Assert.Contains("xx2553", c);                 // E lowest
        Assert.DoesNotContain("xx2010", c);           // x32010 with its fifth string muted
        Assert.DoesNotContain("xx5553", c);           // x35553 with its fifth string muted
        Assert.Equal("x32010", c[0]);                 // the default stays the first
        Assert.Contains("xx3231", Order("Dm", includeStretch: false));   // F lowest
        // Every upper-string shape of A minor is a muted copy of one offered already: none is added.
        var am = Order("Am", includeStretch: false);
        Assert.DoesNotContain("xx2210", am);          // x02210 muted
        Assert.DoesNotContain("xx7555", am);          // x07555 muted
        // After the shapes of its own position: the root lowest comes first there.
        int upper = c.IndexOf("xx2553");
        Assert.All(c.Take(upper).Skip(1), s => Assert.True(Position(s) <= 2, s));
        Assert.All(c.Skip(upper + 1).Where(s => !s.StartsWith("xx")), s => Assert.True(Position(s) > 2, s));

        static int Position(string shape) => LilySharp.Core.Music.ChordVoicings.Position(ChordVoicingTests.Shape(shape));
    }

    private static List<string> Order(string symbol, bool includeStretch)
    {
        var tree = LilySharp.Core.Syntax.SyntaxTree.Parse(Guitar($"c'1@chord({symbol})"));
        var mark = tree.GetNodes<LilySharp.Core.Syntax.MusicMarkSyntax>().Single();
        var chord = LilySharp.Core.Semantics.ChordAnnotation.Of(mark)!.Structure!;
        var (_, order) = LilySharp.Core.Editing.NoteStepper.ShapeOrder(mark, chord, includeStretch);
        return [.. order.Select(o => LilySharp.Core.Music.ChordVoicings.Spell(o))];
    }

    /// <summary>
    /// An inversion walks the chord's other shapes too (owner's decision 2026-11-11: a band's
    /// bass plays the bass note, so on the guitar it is the chord's tones that matter): Dm/A's
    /// order holds its own shapes, A lowest, the first of them still the default, and Dm's — D
    /// or F lowest — each after the shapes of its position. None of them is warned under the
    /// name Dm/A (LYS1039 does not ask the bass of a chord tone). A bass outside the chord is
    /// the reason for its slash: C/D keeps D in every shape.
    /// </summary>
    [Fact]
    public void AnInversion_WalksTheChordsOtherShapesToo()
    {
        var slash = Order("Dm/A", includeStretch: false);
        var plain = Order("Dm", includeStretch: false);
        // Its own first, as before; and every shape of Dm is in it.
        Assert.Equal("x00231", slash[0]);
        // …or a muted copy of one that is (xx0231 is x00231 with the fifth string damped: the player's to make).
        static bool Holds(string full, string part)
        {
            var a = LilySharp.Core.Music.ChordShapes.Frets(full);
            var b = LilySharp.Core.Music.ChordShapes.Frets(part);
            return a.Length == b.Length && b.Select((fret, s) => fret < 0 || fret == a[s]).All(held => held);
        }
        Assert.All(plain, shape => Assert.Contains(slash, s => Holds(s, shape)));
        var own = Order("Dm/A", includeStretch: false).Where(s =>
        {
            var frets = LilySharp.Core.Music.ChordShapes.Frets(s);
            var guitarStrings = LilySharp.Core.Tablature.Tunings.GetTuning(LilySharp.Core.Tablature.Tunings.Parse("guitar"));
            return Enumerable.Range(0, frets.Length).Where(i => frets[i] >= 0).Min(i => guitarStrings[i] + frets[i]) % 12 == 9;
        }).ToList();
        Assert.True(own.Count > 0 && own.Count < slash.Count, $"{own.Count} with A lowest of {slash.Count}");
        Assert.Equal(slash.Count, slash.Distinct().Count());
        // After the default, the positions only rise.
        var positions = slash.Skip(1).Select(s => LilySharp.Core.Music.ChordVoicings.Position(LilySharp.Core.Music.ChordShapes.Frets(s))).ToList();
        Assert.Equal(positions.OrderBy(p => p), positions);
        // None is warned under the slash chord's name.
        var tree = LilySharp.Core.Syntax.SyntaxTree.Parse(Guitar("c'1@chord(Dm/A)"));
        var chord = LilySharp.Core.Semantics.ChordAnnotation.Of(tree.GetNodes<LilySharp.Core.Syntax.MusicMarkSyntax>().Single())!.Structure!;
        var guitar = LilySharp.Core.Tablature.Tunings.GetTuning(LilySharp.Core.Tablature.Tunings.Parse("guitar"));
        Assert.All(slash, s => Assert.Null(LilySharp.Core.Music.ChordShapes.Mismatch(LilySharp.Core.Music.ChordShapes.Frets(s), guitar, chord)));
        // The keys reach them: Down from the default goes on through the list.
        Assert.Equal(Guitar($"c'1@chord(Dm/A {slash[1]})"), Step(Guitar("c'1@chord(Dm/A x00‸231)"), -1, semitone: true).After);

        // A bass that is no tone of the chord stays in every shape.
        var outside = Order("C/D", includeStretch: false);
        Assert.NotEmpty(outside);
        Assert.All(outside, s =>
        {
            var frets = LilySharp.Core.Music.ChordShapes.Frets(s);
            Assert.Contains(Enumerable.Range(0, frets.Length), i => frets[i] >= 0 && (guitar[i] + frets[i]) % 12 == 2);
        });
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
        // The 29 and, since 2026-11-11, the six upper-string shapes no other one holds.
        Assert.Equal(35, order.Count);
        Assert.Equal(["x35543", "x31013", "x31043", "x35043", "8x58x8"], order.Take(5));
        Assert.Equal(["x-15-13-12-x-15", "x-15-13-12-13-x", "xx-13-12-13-15", "x-15-13-x-13-15", "xx-13-0-13-15"],
            order.TakeLast(5));
        Assert.Equal(CmBases.Length + 6, order.Count);
    }

    /// <summary>The step walks on past 8xx888 into frets 10–15 and writes them in the compact
    /// form (owner's decision 2026-09-28); back below fret 10 it writes one character per string
    /// again. A written dash shape — compact or full-dash — steps from its place like any other
    /// (a row entry too).</summary>
    [Fact]
    public void TheStep_WalksIntoFretsTenToFifteen_AndSpellsEachInItsForm()
    {
        var (response, after) = Step(Guitar("c'1@chord(Cm 8xx888‸)"), -1, semitone: true);
        Assert.Equal(Guitar("c'1@chord(Cm 8xx88-11)"), after);
        Assert.Equal("Cm: shape 20 of 35 (8xx88-11)", response.Message);
        Assert.Equal(Guitar("c'1@chord(Cm 8xx888)"), Up("c'1@chord(Cm 8xx88-11‸)", Guitar, semitone: true));
        // The full-dash spelling of the same shape steps from the same place.
        Assert.Equal(Guitar("c'1@chord(Cm 8xx888)"), Up("c'1@chord(Cm 8-x-x-8-8-11‸)", Guitar, semitone: true));
        Assert.Equal(Guitar("c'1@chord(Cm x-15-13-12-13-x)"), Down("c'1@chord(Cm x-15-13-12-x-15‸)", Guitar, semitone: true));
        Assert.Equal(Guitar("c'1@chord(Cm xx-10-12-13-11)"), Down("c'1@chord(Cm 8-10-10-8-8-11‸)", Guitar, semitone: true));
        Assert.Equal(Guitar("c'1@chord(Cm 8-10-10-888)"), Down("c'1@chord(Cm xx-10-8-8-11‸)", Guitar, semitone: true));
        // A written x-3-5-5-4-3 is the default x35543: Down writes the next one-character shape.
        Assert.Equal(Guitar("c'1@chord(Cm x31013)"), Down("c'1@chord(Cm x-3-5-5-4-3‸)", Guitar, semitone: true));
        Assert.Equal(Row("Cm(8xx88-11) |"), Down("Cm(8xx888‸) |", Row, semitone: true));
        Assert.Equal(Row("Cm(8xx888) |"), Up("Cm(8xx88-11‸) |", Row, semitone: true));
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
        var (up, after) = Step(Guitar("c'1@chord(Cm7‸)"), -1, semitone: true);
        Assert.Equal(Guitar("c'1@chord(Cm7 x35343)"), after);
        Assert.Equal($"Cm7: shape 1 of {order.Count} (x35343)", up.Message);
        var (down, same) = Step(Guitar("c'1@chord(Cm7‸)"), +1, semitone: true);
        Assert.False(down.Fallback);   // on a chord: handled, just no move
        Assert.Empty(down.Edits);
        Assert.Equal(Guitar("c'1@chord(Cm7)"), same);
        Assert.Contains("Down adds one", down.Message);
    }

    [Fact]
    public void AWrittenShape_StepsToItsNeighbour_AndDownAtTheDefaultRemovesIt()
    {
        var order = Cm7Order();
        Assert.Equal(Guitar($"c'1@chord(Cm7 {order[3]})"), Down($"c'1@chord(Cm7 {order[2]}‸)", Guitar, semitone: true));
        Assert.Equal(Guitar($"c'1@chord(Cm7 {order[1]})"), Up($"c'1@chord(C‸m7 {order[2]})", Guitar, semitone: true));
        // Up to the default writes it; Up AT it removes the shape, and the diagram goes.
        Assert.Equal(Guitar("c'1@chord(Cm7 x35343)"), Up($"c'1@chord(Cm7 {order[1]}‸)", Guitar, semitone: true));
        var (removed, after) = Step(Guitar("c'1@chord(Cm7 x35343‸)"), +1, semitone: true);
        Assert.Equal(Guitar("c'1@chord(Cm7)"), after);
        Assert.Contains("shape removed, no diagram", removed.Message);
        Assert.Equal(Guitar($"c'1@chord(Cm7 {order[1]})"), Down("c'1@chord(Cm7 x35343‸)", Guitar, semitone: true));
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
        Assert.Equal(Uke("c'1@chord(C 0003)"), Down("c'1@chord(C‸)", Uke, semitone: true));
        Assert.Equal(Uke("c'1@chord(C x32010)"), Up("c'1@chord(C x32010 0003‸)", Uke, semitone: true));
        // The ukulele's order past the table (2026-09-29, K5 ⑥: the rules less V4): 0003 → 0403;
        // a chord the table lacks starts on the order's first shape.
        Assert.Equal(Uke("c'1@chord(C 0403)"), Down("c'1@chord(C 0003‸)", Uke, semitone: true));
        Assert.Equal(Uke("c'1@chord(C 0003)"), Up("c'1@chord(C 0403‸)", Uke, semitone: true));
        Assert.Equal(Uke("c'1@chord(Cmaj9 4203)"), Down("c'1@chord(Cmaj9‸)", Uke, semitone: true));
        Assert.Equal(Guitar("c'1@chord(C 0003)"), Up("c'1@chord(C 0003 x32010‸)", Guitar, semitone: true));
        Assert.Equal(Guitar("c'1@chord(C 0003 x32010)"), Down("c'1@chord(C 0003‸)", Guitar, semitone: true));
    }

    // ---- a layout shape table (2026-09-29): a listed name alone draws the table's shape

    private static string TableGuitar(string music) => "chordShapes { Cm7 x35343 }\n" + Guitar(music);

    /// <summary>Where the layout's table lists the chord, the name alone shows the table's shape,
    /// so the step counts from it exactly as in an <c>all</c> score: Up writes the shape after
    /// it, Down on the name does nothing, Down at the written table shape removes it. A chord
    /// the table does not list steps as in any score.</summary>
    [Fact]
    public void InATableScore_TheNameAloneStandsAtTheTablesShape()
    {
        var order = Cm7Order();
        var (up, after) = Step(TableGuitar("c'1@chord(Cm7‸)"), -1, semitone: true);
        Assert.Equal(TableGuitar($"c'1@chord(Cm7 {order[1]})"), after);
        Assert.Equal($"Cm7: shape 2 of {order.Count} ({order[1]})", up.Message);

        var (down, same) = Step(TableGuitar("c'1@chord(Cm7‸)"), +1, semitone: true);
        Assert.Empty(down.Edits);
        Assert.Equal(TableGuitar("c'1@chord(Cm7)"), same);
        Assert.Contains("this score's layout table lists it, so it shows (guitar: x35343)", down.Message);

        var (atTable, removed) = Step(TableGuitar("c'1@chord(Cm7 x35343‸)"), +1, semitone: true);
        Assert.Equal(TableGuitar("c'1@chord(Cm7)"), removed);
        Assert.Contains("layout table lists it, so the name alone still shows it", atTable.Message);

        Assert.Equal(TableGuitar("c'1@chord(G 320003)"), Down("c'1@chord(G‸)", TableGuitar, semitone: true));
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
        var (up, after) = Step(AllGuitar("c'1@chord(Cm7‸)"), -1, semitone: true);
        Assert.Equal(AllGuitar($"c'1@chord(Cm7 {order[1]})"), after);
        Assert.Equal($"Cm7: shape 2 of {order.Count} ({order[1]})", up.Message);

        var (down, same) = Step(AllGuitar("c'1@chord(Cm7‸)"), +1, semitone: true);
        Assert.False(down.Fallback);
        Assert.Empty(down.Edits);
        Assert.Equal(AllGuitar("c'1@chord(Cm7)"), same);
        Assert.Contains("this score draws every chord, so it shows the default (guitar: x35343)", down.Message);

        var (atDefault, removed) = Step(AllGuitar("c'1@chord(Cm7 x35343‸)"), +1, semitone: true);
        Assert.False(atDefault.Fallback);
        Assert.Equal(AllGuitar("c'1@chord(Cm7)"), removed);
        Assert.Contains("shape removed", atDefault.Message);

        // From the next shape Up still writes the default, and Down walks on as elsewhere.
        Assert.Equal(AllGuitar("c'1@chord(Cm7 x35343)"), Up($"c'1@chord(Cm7 {order[1]}‸)", AllGuitar, semitone: true));
        Assert.Equal(AllGuitar($"c'1@chord(Cm7 {order[2]})"), Down($"c'1@chord(Cm7 {order[1]}‸)", AllGuitar, semitone: true));

        // A row entry of an `all` score steps the same way.
        static string AllRow(string row) => "layout { chordDiagrams all }\n" + Row(row);
        var gOrder = Order("G", includeStretch: false);
        Assert.Equal(AllRow($"G({gOrder[1]}) | C |"), Down("G‸ | C |", AllRow, semitone: true));
        Assert.Equal(AllRow("G | C |"), Up("G(320003‸) | C |", AllRow, semitone: true));
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

    /// <summary>The semitone pair (Ctrl+Shift+Up / Down) on a row entry steps its shape; the octave
    /// pair walks the key's chords instead (<see cref="ARowEntry_OnTheOctavePair_WalksTheKeysSeventhChords"/>).</summary>
    [Fact]
    public void ARowEntry_Steps_AndDownAtTheDefaultRemovesTheGroup()
    {
        var gOrder = Order("G", includeStretch: false);
        Assert.Equal("320003", gOrder[0]);
        var (up, after) = Step(Row("G‸ | C |"), -1, semitone: true);
        Assert.Equal(Row("G(320003) | C |"), after);
        Assert.Equal($"G: shape 1 of {gOrder.Count} (320003)", up.Message);
        Assert.Equal(Row($"G({gOrder[1]}) | C |"), Down("G(320003‸) | C |", Row, semitone: true));
        Assert.Equal(Row("G | C |"), Up("G(3‸20003) | C |", Row, semitone: true));
        var (down, same) = Step(Row("G‸ | C |"), +1, semitone: true);
        Assert.False(down.Fallback);
        Assert.Empty(down.Edits);
        Assert.Equal(Row("G | C |"), same);
        // Several shapes: the guitar's steps; removing it keeps the other.
        Assert.Equal(Row("F(2010) |"), Up("F(133211‸ 2010) |", Row, semitone: true));
        Assert.Equal(Row("F(2010) |"), Up("F(2010 ‸133211) |", Row, semitone: true));
        Assert.Equal(Row("F(2010 133211) |"), Down("F(2010‸) |", Row, semitone: true));
    }

    /// <summary>The same walk on a named @chord in a part (owner's request 2026-11-03): the note is
    /// left alone, and a shape goes with the old chord. An @chord with no name has nothing to walk
    /// from and keeps its own steps.</summary>
    [Theory]
    [InlineData("key c major\n", "c'4@chord(Cmaj7‸) d", +1, "c'4@chord(Dm7) d")]
    [InlineData("key c major\n", "c'4@chord(‸Cmaj7) d", +1, "c'4@chord(Dm7) d")]
    [InlineData("key c major\n", "c'4@chord(Dm7‸) d", -1, "c'4@chord(Cmaj7) d")]
    [InlineData("key c major\n", "c'4@chord(C‸) d", +1, "c'4@chord(Dm7) d")]             // C, C7, Cmaj7: all on I
    [InlineData("key c major\n", "c'4@chord(G‸7) d", +1, "c'4@chord(Am7) d")]
    [InlineData("key c major\n", "c'4@chord(C‸ x32010) d", +1, "c'4@chord(Dm7) d")]      // the shape was C's
    [InlineData("key c major\n", "c'4@chord(C x32‸010) d", +1, "c'4@chord(Dm7) d")]      // the caret on the shape
    [InlineData("key c major\n", "c'4@chord(C x32010 ukulele 0003‸) d", +1, "c'4@chord(Dm7) d")]
    [InlineData("", "c'4@chord(Cmaj7‸) d", +1, "c'4@chord(Dm7) d")]                      // no key: C major
    [InlineData("key a minor\n", "c'4@chord(Am7‸) d", +1, "c'4@chord(Bm7-5) d")]
    [InlineData("key g major\n", "c'4@chord(Gmaj7‸) d", +1, "c'4@chord(Am7) d")]
    [InlineData("key g major\n", "c'4@chord(Em7‸) d", +1, "c'4@chord(F#m7-5) d")]
    [InlineData("key c major\n", "c'4 d@chord(Cmaj7‸) e", +1, "c'4 d@chord(Dm7) e")]
    [InlineData("key c major\n", "r4@chord(Cmaj7‸) d", +1, "r4@chord(Dm7) d")]           // on a rest
    public void ANamedChordMark_OnTheOctavePair_WalksTheKeysSeventhChords(string key, string music, int direction, string expected)
    {
        string Book(string m) => key + "part gt { clef treble }\nsection A { gt { " + m + " e f | } }\nform { A }\nscore { staff gt }\n";
        var (response, after) = Step(Book(music), direction);
        Assert.False(response.Fallback, response.Error);
        Assert.Equal(Book(expected), after);
        Assert.False(LilySharp.Core.Syntax.SyntaxTree.Parse(after).HasErrors, after);
    }

    [Fact]
    public void ANamedChordMark_SaysWhereItIs_SoundsIt_AndStopsAtTheEnds()
    {
        string Book(string m) => "key c major\npart gt { clef treble }\nsection A { gt { " + m + " e f | } }\nform { A }\nscore { staff gt }\n";
        var (up, _) = Step(Book("c'4@chord(Cmaj7‸) d"), +1);
        Assert.Equal("Dm7: IIm7 of c major (2 of 7)", up.Message);
        Assert.NotEmpty(up.Pitches);
        var (top, atTop) = Step(Book("c'4@chord(Bm7-5‸) d"), +1);
        Assert.Equal(Book("c'4@chord(Bm7-5) d"), atTop);
        Assert.Contains("is the last chord of c major", top.Message);
        var (bottom, atBottom) = Step(Book("c'4@chord(Cmaj7‸) d"), -1);
        Assert.Equal(Book("c'4@chord(Cmaj7) d"), atBottom);
        Assert.Contains("is the first chord of c major", bottom.Message);
        var (off, offAfter) = Step(Book("c'4@chord(Db7‸) d"), +1);
        Assert.Equal(Book("c'4@chord(Db7) d"), offAfter);
        Assert.Contains("its root is not a tone of c major", off.Message);
    }

    /// <summary>The key in force where the mark is written: the part's, or one earlier in its music.</summary>
    [Fact]
    public void ANamedChordMark_WalksTheKeyWrittenForIt()
    {
        string InPart(string m) => "key c major\npart gt { clef treble key g major }\nsection A { gt { " + m + " e f | } }\nform { A }\nscore { staff gt }\n";
        Assert.Equal(InPart("c'4@chord(Em7) d"), Step(InPart("c'4@chord(‸D7) d"), -1).After.Replace("@chord(Cmaj7)", "@chord(Em7)"));
        Assert.Equal(InPart("c'4@chord(F#m7-5) d"), Step(InPart("c'4@chord(‸Em7) d"), +1).After);
        string Changed(string m) => "part gt { clef treble }\nsection A { gt { c'4 d e f | key g major " + m + " e f | } }\nform { A }\nscore { staff gt }\n";
        Assert.Equal(Changed("c'4@chord(F#m7-5) d"), Step(Changed("c'4@chord(‸Em7) d"), +1).After);
    }

    /// <summary>What has no name to walk from keeps its steps, and the semitone pair its shape.</summary>
    [Fact]
    public void AChordMarkWithNoName_KeepsItsSteps_AndTheSemitonePairTheShape()
    {
        string Book(string m) => "key c major\npart gt { clef treble }\nsection A { gt { " + m + " e f | } }\nform { A }\nscore { staff gt }\n";
        // A shape alone: nothing moves on this pair, and it says why; Down and Up step its shape.
        foreach (string at in new[] { "c'4@chord(x32010‸) d", "c'4@chord(x32‸010) d", "c'4@cho‸rd(x32010) d", "r4@chord(x32010‸) d" })
            foreach (int direction in new[] { +1, -1 })
            {
                var (said, same) = Step(Book(at), direction);
                Assert.False(said.Fallback);
                Assert.Equal(Book(at.Replace("‸", "")), same);
                Assert.Contains("no chord name", said.Message);
            }
        Assert.NotEqual(Book("c'4@chord(x32010) d"), Step(Book("c'4@chord(x32010‸) d"), -1, semitone: true).After);
        // A bare @chord on <…> is its chord's: the octave pair moves the chord, as it did.
        var (_, bare) = Step(Book("<c' e g>4@chord‸ d"), -1);
        Assert.Contains("@chord d", bare);
        Assert.DoesNotContain("Dm7", bare);
        // Down / Up on a named one: its shape.
        var (_, shape) = Step(Book("c'4@chord(C‸) d"), -1, semitone: true);
        Assert.Contains("@chord(C ", shape);
        Assert.DoesNotContain("Dm7", shape);
    }

    /// <summary>The same walk on a named diagram(…) or chord(…) item (owner's request 2026-11-03):
    /// its shape goes with the old chord, its length and marks stay. A shape alone has no name to
    /// walk from: nothing moves (owner's decision the same day). An @diagram takes a shape alone, and
    /// its octave pair is its placement.</summary>
    [Theory]
    [InlineData("diagram(Cmaj7‸)1 |", +1, "diagram(Dm7)1 |")]
    [InlineData("diagram(‸Cmaj7 x32000)1 |", +1, "diagram(Dm7)1 |")]
    [InlineData("diagram(Cmaj7 x32‸000)1 |", +1, "diagram(Dm7)1 |")]
    [InlineData("diag‸ram(Cmaj7 x32000)1 |", +1, "diagram(Dm7)1 |")]              // from its word
    [InlineData("diagram(Dm7‸)1 |", -1, "diagram(Cmaj7)1 |")]
    [InlineData("diagram(C‸)2. r4 |", +1, "diagram(Dm7)2. r4 |")]                 // its length stays
    [InlineData("diagram(Cmaj7 x32000 ukulele 0002‸)1 |", +1, "diagram(Dm7)1 |")]
    [InlineData("chord(Cmaj7‸)1 |", +1, "chord(Dm7)1 |")]
    [InlineData("chord(Cmaj7 x32‸000)1 |", +1, "chord(Dm7)1 |")]
    [InlineData("diagram(Cmaj7 x32000)1@chord‸ |", +1, "diagram(Dm7)1@chord |")]  // a bare @chord on it is its name
    public void ANamedShapeItem_OnTheOctavePair_WalksTheKeysSeventhChords(string music, int direction, string expected)
    {
        string Book(string m) => "key c major\npart gt { tuning guitar }\nsection A { gt { " + m + " } }\nform { A }\nscore { staff gt }\n";
        var (response, after) = Step(Book(music), direction);
        Assert.False(response.Fallback, response.Error);
        Assert.Equal(Book(expected), after);
        Assert.False(LilySharp.Core.Syntax.SyntaxTree.Parse(after).HasErrors, after);
    }

    [Fact]
    public void ANamedShapeItem_SaysWhereItIs_SoundsIt_StopsAtTheEnds_AndReadsTheKey()
    {
        string Book(string m, string key = "key c major\n") => key + "part gt { tuning guitar }\nsection A { gt { " + m + " } }\nform { A }\nscore { staff gt }\n";
        var (up, _) = Step(Book("diagram(Cmaj7‸)1 |"), +1);
        Assert.Equal("Dm7: IIm7 of c major (2 of 7)", up.Message);
        Assert.NotEmpty(up.Pitches);
        var (top, atTop) = Step(Book("diagram(Bm7-5‸)1 |"), +1);
        Assert.Equal(Book("diagram(Bm7-5)1 |"), atTop);
        Assert.Contains("is the last chord of c major", top.Message);
        Assert.Equal(Book("diagram(F#m7-5)1 |", "key g major\n"), Step(Book("diagram(Em7‸)1 |", "key g major\n"), +1).After);
    }

    [Fact]
    public void WhatHasNoName_MovesNothingOnTheOctavePair_AndTheSemitonePairTheShape()
    {
        string Book(string m) => "key c major\npart gt { tuning guitar }\nsection A { gt { " + m + " } }\nform { A }\nscore { staff gt }\n";
        // A shape alone: nothing moves, and it says why.
        foreach (string item in new[] { "diagram(x32‸010)1 |", "diag‸ram(x32010)1 |", "chord(x32‸010)1 |", "diagram(x32010)1@chord‸ |" })
            foreach (int direction in new[] { +1, -1 })
            {
                var (said, same) = Step(Book(item), direction);
                Assert.False(said.Fallback);
                Assert.Equal(Book(item.Replace("‸", "")), same);
                Assert.Contains("no chord name", said.Message);
            }
        // Its shape is the semitone pair's.
        Assert.NotEqual(Book("diagram(x32010)1 |"), Step(Book("diagram(x32‸010)1 |"), -1, semitone: true).After);
        // An @diagram: its placement.
        Assert.Equal(Book("c'1@diagram(133211).up |"), Step(Book("c'1@diagram(133‸211) |"), +1).After);
        // Down / Up on a named item: its shape.
        var (_, shape) = Step(Book("diagram(Cmaj7‸)1 |"), -1, semitone: true);
        Assert.Contains("diagram(Cmaj7 ", shape);
    }

    /// <summary>The octave pair (Ctrl+Shift+PageUp / PageDown) on a row entry walks the key's
    /// SEVENTH chords (owner's request 2026-10-31): in C major Cmaj7 → Dm7 → Em7 → Fmaj7 → G7 →
    /// Am7 → Bm7-5, from the degree the symbol's root stands on, stopping at I and VII (the
    /// owner: no going round); a written shape goes with the old chord; a minor key walks its
    /// own; a root off the key, or a degree, says so.</summary>
    [Fact]
    public void ARowEntry_OnTheOctavePair_WalksTheKeysSeventhChords()
    {
        static string InKey(string key, string row) => $$"""
            key {{key}}
            octave absolute
            part gt { clef treble }
            section A {
              gt { c'1 | c'1 | }
              chords prog { {{row}} }
            }
            form { A }
            score { chords prog  staff gt }
            """;
        var (up, after) = Step(InKey("c major", "Cmaj7‸ | C |"), +1);
        Assert.Equal(InKey("c major", "Dm7 | C |"), after);
        Assert.Equal("Dm7: IIm7 of c major (2 of 7)", up.Message);
        Assert.NotEmpty(up.Pitches);   // the stepped entry sounds
        // Up the scale to VII, where it stops; Down at I stops too.
        var walked = new List<string>();
        string symbol = "Cmaj7";
        for (int i = 0; i < 6; i++)
        {
            string stepped = Step(InKey("c major", symbol + "‸ | C |"), +1).After;
            symbol = stepped.Substring(stepped.IndexOf("chords prog { ", StringComparison.Ordinal) + 14).Split(' ')[0];
            walked.Add(symbol);
        }
        Assert.Equal(["Dm7", "Em7", "Fmaj7", "G7", "Am7", "Bm7-5"], walked);
        var (top, atTop) = Step(InKey("c major", "Bm7-5‸ | C |"), +1);
        Assert.Empty(top.Edits);
        Assert.Equal(InKey("c major", "Bm7-5 | C |"), atTop);
        Assert.Contains("VIIm7-5 is the last chord of c major", top.Message);
        var (bottom, atBottom) = Step(InKey("c major", "Cmaj7‸ | C |"), -1);
        Assert.Empty(bottom.Edits);
        Assert.Equal(InKey("c major", "Cmaj7 | C |"), atBottom);
        Assert.Contains("Imaj7 is the first chord of c major", bottom.Message);
        Assert.Equal(InKey("c major", "Am7 | C |"), Step(InKey("c major", "Bm7-5‸ | C |"), -1).After);
        // A triad or a seventh stands on its degree; the shape written for it goes with it.
        Assert.Equal(InKey("c major", "Dm7 | C |"), Step(InKey("c major", "C‸ | C |"), +1).After);
        Assert.Equal(InKey("c major", "Am7 | C |"), Step(InKey("c major", "G7(320001)‸ | C |"), +1).After);
        Assert.Equal(InKey("c major", "G7 | C |"), Step(InKey("c major", "F‸ | C |"), +1).After);
        // The key in force: E-flat major spells its own, A minor walks the natural minor.
        Assert.Equal(InKey("ees major", "Fm7 | C |"), Step(InKey("ees major", "Ebmaj7‸ | C |"), +1).After);
        Assert.Equal(InKey("a minor", "Bm7-5 | C |"), Step(InKey("a minor", "Am7‸ | C |"), +1).After);
        // No key written: C major.
        Assert.Equal(Row("Dm7 | C |"), Step(Row("C‸ | C |"), +1).After);
        // A root off the key, or a degree, steps nothing and says why.
        var (off, same) = Step(InKey("c major", "Bb7‸ | C |"), +1);
        Assert.Empty(off.Edits);
        Assert.Contains("not a tone of c major", off.Message);
        Assert.Equal(InKey("c major", "Bb7 | C |"), same);
        var (degree, _) = Step(InKey("c major", "IIm7‸ | C |"), +1);
        Assert.Empty(degree.Edits);
        Assert.Contains("not a degree", degree.Message);
    }

    /// <summary>The row's tuning is the first score's that places it; the status bar says when
    /// another score draws it on another tuning.</summary>
    [Fact]
    public void ARowEntry_StepsOnTheFirstScoresTuning_AndSaysSo()
    {
        static string TwoScores(string row) => $$"""
            layout uke { chordTuning ukulele }
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
        var (response, after) = Step(TwoScores("G‸ |"), -1, semitone: true);
        Assert.Equal(TwoScores("G(0232) |"), after);
        Assert.Contains("another draws guitar", response.Message);
    }

    [Fact]
    public void UpAtTheLastShape_ChangesNothing_AndSaysWhereItIs()
    {
        var order = Cm7Order();
        var (response, after) = Step(Guitar($"c'1@chord(Cm7 {order[^1]}‸)"), -1, semitone: true);
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
        Assert.Equal(Guitar($"c'1@chord(Cm7 {order[1]})"), Down("c'1@chord(Cm7 x3534x‸)", Guitar, semitone: true));
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
        Assert.Equal(34, off.Count);                      // and xx5546 on the upper strings, since 2026-11-11
        Assert.Equal(3, off.IndexOf("x3x546"));
        Assert.Contains("8xx546", on);                    // frets 4 to 8
        Assert.DoesNotContain("8xx546", off);
        Assert.True(on.Count > off.Count);
    }

    /// <summary>The cycle differs: from x35346 Up is the normal order's next, xx5546 (on the upper
    /// strings, since 2026-11-11), with the setting off, and the stretch shape 8xx546 with it on.</summary>
    [Fact]
    public void TheStep_WalksTheOrderTheSettingChooses()
    {
        var off = Cm7Order(includeStretch: false);
        var on = Cm7Order(includeStretch: true);
        Assert.Equal("xx5546", off[off.IndexOf("x35346") + 1]);
        Assert.Equal("8xx546", on[on.IndexOf("x35346") + 1]);
        Assert.Equal(Guitar("c'1@chord(Cm7 xx5546)"), Step(Guitar("c'1@chord(Cm7 x35346‸)"), -1, semitone: true).After);
        var (response, after) = Step(Guitar("c'1@chord(Cm7 x35346‸)"), -1, includeStretch: true, semitone: true);
        Assert.Equal(Guitar("c'1@chord(Cm7 8xx546)"), after);
        Assert.Equal($"Cm7: shape {on.IndexOf("8xx546") + 1} of {on.Count} (8xx546)", response.Message);
    }

    /// <summary>A WRITTEN stretch shape with the setting off is not in the order, yet steps from
    /// where it would sort: 8xx546 (position 4) goes Down to the first normal shape after it,
    /// 8x58x6 (position 5), and Up to the last before it, xx5546 (position 4, on the upper strings).</summary>
    [Fact]
    public void AWrittenStretchShape_StepsToTheNormalShapesAroundIt()
    {
        Assert.Equal(Guitar("c'1@chord(Cm7 8x58x6)"), Down("c'1@chord(Cm7 8xx546‸)", Guitar, semitone: true));
        Assert.Equal(Guitar("c'1@chord(Cm7 xx5546)"), Up("c'1@chord(Cm7 8xx546‸)", Guitar, semitone: true));
        // A muted copy of it sorts as the stretch shape it mutes.
        Assert.Equal(Guitar("c'1@chord(Cm7 8x58x6)"), Down("c'1@chord(Cm7 8xx54x‸)", Guitar, semitone: true));
    }

    /// <summary>An <c>@chord</c> with no symbol — a bare shape — steps its shape through the shapes
    /// of the chord it spells, the shape alone rewritten, with the caret on the word or the shape
    /// (owner's request 2026-10-31, over 2026-10-17's "the keys step the note it is on").</summary>
    [Fact]
    public void AShapeWithNoSymbol_StepsItsShape()
    {
        Assert.Equal(Guitar("c'1@chord(x32013)"), Step(Guitar("c'1@ch‸ord(x32010)"), -1, semitone: true).After);
        Assert.Equal(Guitar("c'1@chord(x32013)"), Step(Guitar("c'1@chord(x32‸010)"), -1, semitone: true).After);
        Assert.Equal(Guitar("c'1@chord(x35046)"), Step(Guitar("c'1@chord(x3x5‸46)"), -1, semitone: true).After);
        // Up at the chord's first shape keeps it: no symbol is written, the shape is all there is.
        Assert.Equal(Guitar("c'1@chord(x32010)"), Step(Guitar("c'1@chord(x32‸010)"), +1, semitone: true).After);
    }

    /// <summary>An <c>@diagram(SHAPE)</c> on a single note or a rest steps its shape through the
    /// shapes of the chord it spells, as a symbol-less <c>@chord</c> does; on a <c>&lt;…&gt;</c> chord
    /// it walks that chord's fingerings instead (owner's decision 2026-10-31). The octave pair still
    /// places it.</summary>
    [Fact]
    public void ADiagramOnANote_StepsItsShape()
    {
        Assert.Equal(Guitar("c'1@diagram(xx3211)"), Step(Guitar("c'1@diagram(133‸211)"), -1, semitone: true).After);
        Assert.Equal(Guitar("c'1@diagram(xx3211)"), Step(Guitar("c'1@di‸agram(133211)"), -1, semitone: true).After);
        Assert.Equal(Guitar("r1@diagram(x32013)"), Step(Guitar("r1@diagram(x32‸010)"), -1, semitone: true).After);
        Assert.Equal(Guitar("c'1@diagram(133211)"), Step(Guitar("c'1@diagram(133‸211)"), +1, semitone: true).After);
        Assert.Equal(Guitar("c'1@diagram(133211).up"), Up("c'1@diagram(133‸211)", Guitar));
    }

    /// <summary>A bare <c>@diagram</c> on a single note draws where that note is held, its lowest fret
    /// first (owner's decision 2026-10-31: c'@diagram is xxxx1x); the keys walk the other places,
    /// as a chord's fingerings. A shape that is not where the note is held — a chord's diagram over
    /// a melody note — still walks its chord's shapes (ADiagramOnANote_StepsItsShape).</summary>
    [Fact]
    public void ADiagramOnASingleNote_WalksWhereTheNoteIsHeld()
    {
        Assert.Equal(Guitar("c'1@diagram(xxxx1x)"), Step(Guitar("c'1@diag‸ram"), -1, semitone: true).After);
        Assert.Equal(Guitar("c'1@diagram(xx-10-xxx)"), Step(Guitar("c'1@diagram(xxx‸5xx)"), -1, semitone: true).After);
        Assert.Equal(Guitar("c'1@diagram(xxx5xx)"), Step(Guitar("c'1@diagram(xx-1‸0-xxx)"), +1, semitone: true).After);
        Assert.Equal(Guitar("c'1@diagram"), Step(Guitar("c'1@diagram(xxxx‸1x)"), +1, semitone: true).After);
        Assert.Equal(Guitar("c'1@diagram"), Step(Guitar("c'1@diag‸ram"), +1, semitone: true).After);
        // Just past the ')' — where the editor leaves the caret after Down wrote the shape in — the
        // keys go on walking (owner's report 2026-10-31: the second Down did nothing there).
        Assert.Equal(Guitar("c'1@diagram(xxx5xx)"), Step(Guitar("c'1@diagram(xxxx1x)‸"), -1, semitone: true).After);
        Assert.Equal(Guitar("c'1@diagram"), Step(Guitar("c'1@diagram(xxxx1x)‸"), +1, semitone: true).After);
        string inside = Step(Guitar("<c' e'>1@diagram(x320‸1x)"), -1, semitone: true).After;
        Assert.NotEqual(Guitar("<c' e'>1@diagram(x3201x)"), inside);
        Assert.Equal(inside, Step(Guitar("<c' e'>1@diagram(x3201x)‸"), -1, semitone: true).After);
        // A space after the ')' is no longer the mark: the next note steps.
        Assert.Equal(Guitar("c'1@diagram(xxxx1x) d''4"), Step(Guitar("c'1@diagram(xxxx1x) ‸d'4"), +1).After);
        // The page draws it: one dot, the B string's first fret.
        var tree = LilySharp.Core.Syntax.SyntaxTree.Parse(Guitar("c'1@diagram |"));
        Assert.Equal("xxxx1x", Assert.Single(LilySharp.Core.Svg.SvgGenerator.CollectScore(tree,
            LilySharp.Core.Svg.Collector.RenderSpecParser.FindFirst(tree)).Articulations,
            a => a.Type == LilySharp.Core.Syntax.ArticulationType.FretFrame).FrameSpec);
    }

    /// <summary>Up at a chord(…) item's usual shape takes the shape away — chord(F 133211) →
    /// chord(F), the name alone, played close — as at an @chord's; a diagram(…) item's too
    /// (diagram(Cmaj7 x32000) → diagram(Cmaj7), which still draws and plays the usual shape;
    /// owner's requests 2026-10-31). A shape with no symbol stays.</summary>
    [Fact]
    public void DownAtAnItemsUsualShape_LeavesTheNameAlone()
    {
        static string Gt(string music) => $$"""
            part gt { instrument guitar }
            section A { gt { {{music}} } }
            form { A }
            score { staff gt }
            """;
        Assert.Equal(Gt("chord(F)1 |"), Step(Gt("chord(F 1332‸11)1 |"), +1, semitone: true).After);
        Assert.Equal(Gt("chord(F 133211)1 |"), Step(Gt("chord(‸F)1 |"), -1, semitone: true).After);
        Assert.Equal(Gt("diagram(F)1 |"), Step(Gt("diagram(F 1332‸11)1 |"), +1, semitone: true).After);
        Assert.Equal(Gt("diagram(Cmaj7)1 |"), Step(Gt("diagram(Cmaj7 x320‸00)1 |"), +1, semitone: true).After);
        Assert.Equal(Gt("diagram(Cmaj7 x32000)1 |"), Step(Gt("diagram(Cmaj‸7)1 |"), -1, semitone: true).After);
        Assert.Equal(Gt("diagram(x32010)1 |"), Step(Gt("diagram(x320‸10)1 |"), +1, semitone: true).After);
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
        // (A bar line steps its look since 2026-10-24 — ABarLine_WalksItsLooks; the space between
        // two notes is still nothing.)
        Assert.True(Step(Plain("c'4  ‸ r4 d'2 |"), -1).Response.Fallback);
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

    /// <summary>A minor key walks its signature — its natural minor, A minor the notes of C
    /// major (owner's decision 2026-10-27, reversing 2026-10-16's leading tone walked besides) —
    /// and a church mode its own (D dorian: b natural). A note off the key, the leading tone
    /// written, steps to the nearest note of the key either way.</summary>
    [Fact]
    public void AScaleStep_InAMinorKey_WalksItsSignature_TheNaturalMinor()
    {
        Assert.Equal(Plain("key d minor d'4"), Semi("key d minor c'‸4", +1, Plain));
        Assert.Equal(Plain("key d minor c'4"), Semi("key d minor d'‸4", -1, Plain));
        Assert.Equal(Plain("key d minor bes'4"), Semi("key d minor a'‸4", +1, Plain));
        Assert.Equal(Plain("key a minor a'4"), Semi("key a minor g'‸4", +1, Plain));
        Assert.Equal(Plain("key a minor g'4"), Semi("key a minor a'‸4", -1, Plain));
        Assert.Equal(Plain("key f minor f'4"), Semi("key f minor ees'‸4", +1, Plain));
        Assert.Equal(Plain("key a aeolian a'4"), Semi("key a aeolian g'‸4", +1, Plain));
        Assert.Equal(Plain("key d dorian b'4"), Semi("key d dorian a'‸4", +1, Plain));
        // The leading tone written by hand is off the signature: it steps on to the key's notes.
        Assert.Equal(Plain("key a minor a'4"), Semi("key a minor gis'‸4", +1, Plain));
        Assert.Equal(Plain("key a minor g'4"), Semi("key a minor gis'‸4", -1, Plain));
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
        Assert.Equal([62], Step(Plain("c'‸4"), +1, semitone: true).Response.Pitches);
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
            Step(Guitar("c'1@chord(Cm7‸)"), -1, semitone: true).After);

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
            Assert.Equal(header ? "treble^8" : "treble", listed[0]);     // the part header takes treble^8, music not
            Assert.Equal("treble", Assert.Single(LilySharpLanguageServer.GetClefCompletions(header).Items, i => i.Preselect == true).Label);
            string Book(string clef) => header
                ? $"part lh {{ clef {clef} }}\nsection A {{ lh {{ c4 d e f }} }}\n"
                : Plain($"clef {clef} c4");
            for (int i = 0; i + 1 < listed.Count; i++)
            {
                Assert.Equal(Book(listed[i + 1]), Step(Book("‸" + listed[i]), -1, semitone: true).After);
                Assert.Equal(Book(listed[i]), Step(Book("‸" + listed[i + 1]), +1, semitone: true).After);
            }
            var (top, after) = Step(Book("‸" + listed[0]), +1, semitone: true);
            Assert.False(top.Fallback);
            Assert.Equal(Book(listed[0]), after);
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
        // A bass's sound strikes its open strings, apart (AStringedMidiInstrument_StrumsItsOpenStrings).
        var (bass, _) = Step(Instrument("\"fretless ‸bass\""), -1, semitone: true);
        Assert.Equal([28, 33, 38, 43], bass.Pitches);
        Assert.True(bass.Apart);
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
        Assert.Equal([48, 55, 62, 69], answer.Pitches);   // a viola's open strings, strummed
        Assert.True(answer.Strum);
        Assert.Equal(41, answer.Program);
        string partial = Instrument("\"viol\"");
        var none = Open(partial).AuditionAt(new AuditionAtParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocUri },
            Offset = partial.IndexOf("viol", StringComparison.Ordinal) + 4,
        });
        Assert.Empty(none.Pitches);
    }

    /// <summary>Owner's request 2026-10-30: a note selected on a percussion staff was silent — the
    /// caret found pitched notes only. A drum note sounds its drum (General MIDI's key) on the
    /// kit's channel (timbre 9); a drum chord, its drums together; a drums { } chart's hits too.</summary>
    [Fact]
    public void ADrumNote_AnswersTheAudition_WithItsDrum()
    {
        var At = (string text, string mark) => Open(text).AuditionAt(new AuditionAtParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocUri },
            Offset = text.IndexOf(mark, StringComparison.Ordinal) + 1,
        });
        const string kit = "part kit { clef percussion }\nsection A {\n  kit { voice { hh8 hh hh hh <hh sn> hh hh hh | } { bd4 sn bd sn | } }\n}\nform { A }\n";
        var hat = At(kit, "hh8");
        Assert.Equal("drum", hat.Kind);
        Assert.Equal([42], hat.Pitches);      // a closed hi-hat
        Assert.Equal(9, hat.Timbre);
        Assert.Equal([38, 42], At(kit, "hh sn>").Pitches);
        Assert.Equal([36], At(kit, "bd4").Pitches);
        const string chart = "part kit { clef percussion }\nsection A {\n  kit { drums { { hh8 hh hh hh hh hh hh hh | } { bd4 sn bd sn | } } }\n}\nform { A }\n";
        Assert.Equal([38], At(chart, "sn bd sn").Pitches);
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

    // Owner's report 2026-11-12: "make the number after `transpose` step with Ctrl+Shift+Up".
    [Theory]
    [InlineData("singer \"neutrino/MERROW\" sings words on lead with transpose 3‸", +1, "singer \"neutrino/MERROW\" sings words on lead with transpose 4")]
    [InlineData("singer \"neutrino/MERROW\" sings words on lead with transpose ‸3", -1, "singer \"neutrino/MERROW\" sings words on lead with transpose 2")]
    [InlineData("singer male sings words on lead with transpose 0‸", +1, "singer male sings words on lead with transpose 1")]
    [InlineData("singer male sings words on lead with transpose 0‸", -1, "singer male sings words on lead with transpose -1")]
    [InlineData("singer sings words on lead with transpose -3‸", +1, "singer sings words on lead with transpose -2")]
    [InlineData("singer sings words with transpose 12‸", +1, "singer sings words with transpose 13")]
    [InlineData("singer female \"neutrino/MERROW\" sings words on lead with transpose 3‸ support KIRITAN", +1, "singer female \"neutrino/MERROW\" sings words on lead with transpose 4 support KIRITAN")]
    [InlineData("singer \"a/b\" sings words on lead with key 2‸ tempo 3", +1, "singer \"a/b\" sings words on lead with key 3 tempo 3")]
    public void TheNumberAfterTranspose_Steps(string line, int direction, string expected)
    {
        const string Book = "part lead {{ }}\nsection A {{ lead {{ c'1 }} }}\nlyrics words {{ la }}\n{0}\nform {{ A }}\nscore {{ staff lead  lyrics words }}\n";
        var (reply, after) = Step(string.Format(Book, line), direction, semitone: true);
        Assert.False(reply.Fallback, reply.Error);
        Assert.Equal(string.Format(Book, expected), after);
        // The same with Windows line ends, and as the file's last line.
        var (crlf, afterCrlf) = Step(string.Format(Book, line).Replace("\n", "\r\n"), direction, semitone: true);
        Assert.False(crlf.Fallback, crlf.Error);
        Assert.Equal(string.Format(Book, expected).Replace("\n", "\r\n"), afterCrlf);
    }

    // The same name inside a note's mark.
    [Fact]
    public void TheNumberAfterTranspose_StepsInsideANotesSingMarkToo()
    {
        const string Book = "part lead {{ }}\nsection A {{ lead {{ {0} }} }}\nlyrics words {{ la }}\nsinger \"neutrino/MERROW\" sings words on lead\nform {{ A }}\nscore {{ staff lead  lyrics words }}\n";
        var tree = LilySharp.Core.Syntax.SyntaxTree.Parse(string.Format(Book, "c'1@sing(transpose 2)"));
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        var (reply, after) = Step(string.Format(Book, "c'1@sing(transpose 2‸)"), +1, semitone: true);
        Assert.False(reply.Fallback, reply.Error);
        Assert.Equal(string.Format(Book, "c'1@sing(transpose 3)"), after);
        Assert.Equal(string.Format(Book, "c'1@sing(transpose to -1)"), Step(string.Format(Book, "c'1@sing(transpose to ‸0)"), -1, semitone: true).After);
    }

    // What the report uncovered: `transpose` is a keyword, and a singer line took only identifiers
    // after `with` — so the line ended at `with` and the FILE was transposed by the number.
    [Fact]
    public void ASettingNamedByAKeyword_IsTheSingerLines_NotTheFiles()
    {
        const string Source = "part lead { }\nsection A { lead { c'1 } }\nlyrics words { la }\n" +
            "singer \"neutrino/MERROW\" sings words on lead with transpose 3 support KIRITAN\nform { A }\nscore { staff lead  lyrics words }\n";
        var tree = LilySharp.Core.Syntax.SyntaxTree.Parse(Source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        Assert.Equal(Source, tree.GetRoot().ToFullString());
        var singer = Assert.Single(tree.GetRoot().DescendantNodes<LilySharp.Core.Syntax.SingerDeclarationSyntax>());
        Assert.Equal(["transpose", "support"], singer.Settings.Select(s => s.Name));
        Assert.Equal(3, singer.Settings[0].Number);
        // Nothing at the file level transposes.
        Assert.DoesNotContain(tree.GetRoot().ChildNodes(), n => n.Kind == LilySharp.Core.Syntax.SyntaxKind.PropertyAssignment);
        // A word value that is a keyword too, and a keyword after a dot.
        var other = LilySharp.Core.Syntax.SyntaxTree.Parse(Source.Replace("with transpose 3 support KIRITAN", "with mix.key 0.3 mode major"));
        Assert.False(other.HasErrors, string.Join("\n", other.Diagnostics.Select(d => d.Message)));
        Assert.Equal(["mix.key", "mode"], Assert.Single(other.GetRoot().DescendantNodes<LilySharp.Core.Syntax.SingerDeclarationSyntax>()).Settings.Select(s => s.Name));
        // The next line is still its own.
        var next = LilySharp.Core.Syntax.SyntaxTree.Parse(Source.Replace("\nform { A }", "\ntranspose d\nform { A }"));
        Assert.Contains(next.GetRoot().ChildNodes(), n => n.Kind == LilySharp.Core.Syntax.SyntaxKind.PropertyAssignment);
    }

    // Owner's decisions 2026-11-12: a singer line's `up 3` / `octave -1` — the numbers step.
    [Theory]
    [InlineData("singer sings words on lead up 3‸ in A", +1, "singer sings words on lead up 4 in A", "up 4: a fourth above")]
    [InlineData("singer sings words on lead up ‸3", -1, "singer sings words on lead up 2", "a second above")]
    [InlineData("singer sings words on lead up 2‸", -1, "singer sings words on lead up 2", "already the least")]
    [InlineData("singer sings words on lead down 6‸", +1, "singer sings words on lead down 7", "a seventh below")]
    [InlineData("singer sings words on lead down 15‸", +1, "singer sings words on lead down 15", "already the most")]
    [InlineData("singer sings words on lead octave -1‸", +1, "singer sings words on lead octave 1", "1 octave above")]
    [InlineData("singer sings words on lead octave 1‸", -1, "singer sings words on lead octave -1", "1 octave below")]
    [InlineData("singer sings words on lead octave -1‸", -1, "singer sings words on lead octave -2", "2 octaves below")]
    [InlineData("singer sings words on lead octave 3‸", +1, "singer sings words on lead octave 3", "already the highest")]
    [InlineData("singer sings words on lead up 3 octave ‸-1 in A", +1, "singer sings words on lead up 3 octave 1 in A", "1 octave above")]
    public void ASingerLinesInterval_AndItsOctave_Step(string line, int direction, string expected, string says)
    {
        const string Book = "part lead {{ }}\nsection A {{ lead {{ c'1 }} }}\nlyrics words {{ la }}\n{0}\nform {{ A }}\nscore {{ staff lead  lyrics words }}\n";
        var (reply, after) = Step(string.Format(Book, line), direction, semitone: true);
        Assert.False(reply.Fallback, reply.Error);
        Assert.Equal(string.Format(Book, expected), after);
        Assert.Contains(says, reply.Message);
    }

    // Owner's request 2026-11-12, on `singer alice female "diffsinger/Nishiren" on ▮`: the step
    // keys write a part where the line has none yet — the first, whichever key — and step it after.
    [Theory]
    [InlineData("singer alice female \"diffsinger/Nishiren\" on ‸", +1, "singer alice female \"diffsinger/Nishiren\" on lead")]
    [InlineData("singer alice female \"diffsinger/Nishiren\" on ‸", -1, "singer alice female \"diffsinger/Nishiren\" on lead")]
    [InlineData("singer alice female on‸", +1, "singer alice female on lead")]
    [InlineData("singer on ‸", -1, "singer on lead")]
    public void ASingerLinesOnWithNoPartYet_TakesTheFirstPartAtAKey(string line, int direction, string expected)
    {
        const string Book = "part lead {{ }}\npart bass {{ }}\nsection A {{ lead {{ c'1 }}  bass {{ c1 }} }}\nlyrics words {{ la }}\n{0}\nsinger bob male on bass\nform {{ A }}\nscore {{ staff lead  lyrics words }}\n";
        var (reply, after) = Step(string.Format(Book, line), direction, semitone: true);
        Assert.False(reply.Fallback, reply.Error);
        Assert.Equal(string.Format(Book, expected), after);
        Assert.Contains("lead: part 1 of 2 (lead, bass)", reply.Message);
        // …and the next key steps the part it wrote.
        var (next, then) = Step(string.Format(Book, expected.Replace("on lead", "on lead‸")), -1, semitone: true);
        Assert.Equal(string.Format(Book, expected.Replace("on lead", "on bass")), then);
    }

    // Owner's request 2026-11-12, on `singer male sings words on lead`: the step keys on its
    // `male` go to `female`, and back - Up to female, Down to male, stopping at either end (the
    // same day: "they must not go round").
    [Fact]
    public void ASingerLinesGender_StepsUpToFemaleAndDownToMale_AndStops()
    {
        const string Book = "part lead {{ }}\nsection A {{ lead {{ c'1 }} }}\n" +
            "lyrics words {{ la }}\n{0}\nform {{ A }}\nscore {{ staff lead  lyrics words }}\n";
        string S(string line) => string.Format(Book, line);

        var (up, after) = Step(S("singer ma‸le sings words on lead"), +1, semitone: true);
        Assert.False(up.Fallback, up.Error);
        Assert.Equal(S("singer female sings words on lead"), after);
        Assert.Contains("singer female (male, female)", up.Message);
        Assert.Equal(S("singer male sings words on lead"), Step(S("singer ‸female sings words on lead"), -1, semitone: true).After);
        Assert.Equal(S("singer male sings words on lead"), Step(S("singer female‸ sings words on lead"), -1).After);
        // They stop at either end.
        var top = Step(S("singer fe‸male sings words on lead"), +1, semitone: true);
        Assert.Equal(S("singer female sings words on lead"), top.After);
        Assert.Contains("singer female: already the last (male, female)", top.Response.Message);
        var bottom = Step(S("singer male‸ sings words on lead"), -1, semitone: true);
        Assert.Equal(S("singer male sings words on lead"), bottom.After);
        Assert.Contains("singer male: already the first (male, female)", bottom.Response.Message);
        // The octave keys as well.
        Assert.Equal(S("singer female sings words on lead"), Step(S("singer ‸male sings words on lead"), +1).After);
        // Before a name, in a score, with settings: only the word changes.
        Assert.Equal(S("singer female \"neutrino/SOMA\" sings words on lead with transpose 2"),
            Step(S("singer ma‸le \"neutrino/SOMA\" sings words on lead with transpose 2"), +1, semitone: true).After);
        // The `sings` beside it still steps the words, not the gender.
        Assert.Equal(S("singer male sings hum on lead"), Step(S("singer male si‸ngs words on lead"), -1, semitone: true).After);
    }

    // Owner's request 2026-11-11, on `singer female "diffsinger/Nishiren/2P" sings words on lead`:
    // the step keys on its `sings` walk the file's lyrics.
    [Fact]
    public void ASingerLinesWords_StepThroughTheFilesLyrics_OnItsSingsOrOnTheName()
    {
        const string Book = "part lead {{ }}\nsection A {{ lead {{ c'1 }} }}\n" +
            "lyrics words {{ la }}\nlyrics verse2 {{ la }}\nlyrics chorus {{ la }}\n{0}\nform {{ A }}\nscore {{ staff lead  lyrics words }}\n";
        string S(string line) => string.Format(Book, line);
        const string Who = "singer female \"diffsinger/Nishiren/2P\" ";

        // On the `sings`: Down is the next of the file's lyrics, the voice and the part kept.
        var (down, after) = Step(S(Who + "si‸ngs words on lead"), -1, semitone: true);
        Assert.False(down.Fallback, down.Error);
        Assert.Equal(S(Who + "sings verse2 on lead"), after);
        Assert.Contains("sings verse2: lyrics 2 of 5 (words, verse2, chorus, hum, solfege)", down.Message);
        Assert.Equal(S(Who + "sings chorus on lead"), Step(S(Who + "‸sings verse2 on lead"), -1).After);
        Assert.Equal(S(Who + "sings words on lead"), Step(S(Who + "sings‸ verse2 on lead"), +1, semitone: true).After);
        // On the name itself, anywhere in it.
        Assert.Equal(S(Who + "sings verse2 on lead"), Step(S(Who + "sings wo‸rds on lead"), -1, semitone: true).After);
        Assert.Equal(S(Who + "sings verse2 on lead"), Step(S(Who + "sings chorus‸ on lead"), +1, semitone: true).After);
        // Past the file's own come the two made from the notes, as the list offers them.
        Assert.Equal(S(Who + "sings hum on lead"), Step(S(Who + "‸sings chorus on lead"), -1, semitone: true).After);
        Assert.Equal(S(Who + "sings solfege on lead"), Step(S(Who + "‸sings hum on lead"), -1, semitone: true).After);

        // The ends stop, and say so.
        var (first, same) = Step(S(Who + "‸sings words on lead"), +1, semitone: true);
        Assert.Equal(S(Who + "sings words on lead"), same);
        Assert.Contains("already the first lyrics", first.Message);
        Assert.Contains("already the last lyrics", Step(S(Who + "‸sings solfege on lead"), -1, semitone: true).Response.Message);

        // The unnamed words (`sings on lead`): a name is written, the first on Down.
        Assert.Equal(S(Who + "sings words on lead"), Step(S(Who + "‸sings on lead"), -1, semitone: true).After);
        Assert.Equal(S(Who + "sings solfege on lead"), Step(S(Who + "‸sings on lead"), +1, semitone: true).After);
        // A name the file does not declare steps from the ends.
        Assert.Equal(S(Who + "sings words on lead"), Step(S(Who + "‸sings nothere on lead"), -1, semitone: true).After);

        // The part after `on` is not the words: its own step (or none) is untouched by this one.
        Assert.DoesNotContain("lyrics", Step(S(Who + "sings words on le‸ad"), -1, semitone: true).Response.Message ?? "");
        // In a score the line steps the same way.
        const string InScore = "part lead {{ }}\nsection A {{ lead {{ c'1 }} }}\nlyrics words {{ la }}\nlyrics verse2 {{ la }}\nform {{ A }}\nscore {{ staff lead  lyrics words\n  {0}\n}}\n";
        Assert.Equal(string.Format(InScore, "singer \"a/b\" sings verse2 on lead"),
            Step(string.Format(InScore, "singer \"a/b\" ‸sings words on lead"), -1, semitone: true).After);
    }

    // Owner's request 2026-11-12: the numbers of how a voice sings step under the step keys, on
    // the singer line and on a note.
    [Fact]
    public void AVoicesSettingsNumber_StepsByItsLastWrittenPlace()
    {
        const string Book = "part lead {{ }}\nsection A {{ lead {{ {1} }} }}\nlyrics words {{ la }}\n{0}\nform {{ A }}\nscore {{ staff lead  lyrics words }}\n";
        string S(string line, string music = "c'1") => string.Format(Book, line, music);
        const string Who = "singer \"a/b\" sings words on lead with ";

        // A whole number by one, a decimal by one of its last place; the octave pair by ten of them.
        var (up, after) = Step(S(Who + "breathiness ‸3 gender -0.2"), +1, semitone: true);
        Assert.False(up.Fallback, up.Error);
        Assert.Equal(S(Who + "breathiness 4 gender -0.2"), after);
        Assert.Equal(S(Who + "breathiness 2 gender -0.2"), Step(S(Who + "breathiness 3‸ gender -0.2"), -1, semitone: true).After);
        Assert.Equal(S(Who + "breathiness 13 gender -0.2"), Step(S(Who + "breathiness ‸3 gender -0.2"), +1).After);
        Assert.Equal(S(Who + "breathiness 3 gender -0.1"), Step(S(Who + "breathiness 3 gender -0.‸2"), +1, semitone: true).After);
        Assert.Equal(S(Who + "breathiness 3 gender -0.3"), Step(S(Who + "breathiness 3 gender ‸-0.2"), -1, semitone: true).After);
        Assert.Equal(S(Who + "velocity 1.26"), Step(S(Who + "velocity 1.2‸5"), +1, semitone: true).After);
        // Across 0 the sign is written or dropped, and 0 has none.
        Assert.Equal(S(Who + "gender 0.0"), Step(S(Who + "gender -0.‸1"), +1, semitone: true).After);
        Assert.Equal(S(Who + "gender -0.1"), Step(S(Who + "gender 0.‸0"), -1, semitone: true).After);
        Assert.Equal(S(Who + "breathiness -1"), Step(S(Who + "breathiness ‸0"), -1, semitone: true).After);

        // On a note: held, and arrived at.
        const string Line = "singer \"a/b\" sings words on lead";
        Assert.Equal(S(Line, "c'1@sing(breathiness 7 tension -2)"), Step(S(Line, "c'1@sing(breathiness ‸6 tension -2)"), +1, semitone: true).After);
        Assert.Equal(S(Line, "c'1@sing(breathiness 6 tension -3)"), Step(S(Line, "c'1@sing(breathiness 6 tension -‸2)"), -1, semitone: true).After);
        Assert.Equal(S(Line, "c'1@sing(breathiness to 8.5)"), Step(S(Line, "c'1@sing(breathiness to 9.‸5)"), -1).After);
        // A setting's name or word is no number: the note under the caret keeps its own step.
        Assert.Equal(S(Who + "support KIRITAN"), Step(S(Who + "support KIRI‸TAN"), +1, semitone: true).After);
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

    private const string LanguageBook = "part m { }\nsection A { m { c'4 } }\n";

    [Fact]
    public void ALanguage_WalksTheNames_OnTheFileAndOnATrack()
    {
        WalksTheList(v => $"language {v}\n{LanguageBook}", LilySharp.Core.Music.LyricLanguage.Names);
        WalksTheList(v => $"{LanguageBook}lyrics w language {v} {{ a }}\n", LilySharp.Core.Music.LyricLanguage.Names);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheWordLanguage_StepsItsName_ByEitherPair(bool semitone)
    {
        Assert.Equal($"language japanese\n{LanguageBook}", Step($"‸language english\n{LanguageBook}", +1, semitone: semitone).After);
        Assert.Equal($"language chinese\n{LanguageBook}", Step($"lang‸uage english\n{LanguageBook}", -1, semitone: semitone).After);
        Assert.Equal($"{LanguageBook}lyrics w language japanese {{ a }}\n", Step($"{LanguageBook}lyrics w ‸language english {{ a }}\n", +1, semitone: semitone).After);
    }

    [Fact]
    public void ALanguage_StopsAtItsEnds_AndSaysSo()
    {
        var (first, same) = Step($"language ‸japanese\n{LanguageBook}", +1, semitone: true);
        Assert.Equal($"language japanese\n{LanguageBook}", same);
        Assert.Contains("first", first.Message);
        var (last, stays) = Step($"language ‸latin\n{LanguageBook}", -1, semitone: true);
        Assert.Equal($"language latin\n{LanguageBook}", stays);
        Assert.Contains("last", last.Message);
    }

    [Fact]
    public void AnotherKeyword_IsNotSteppedFromItsOwnWord()
    {
        var words = LilySharp.Core.Semantics.LanguageVocabulary.CompressRestsWords.ToList();
        string L(string caret) => $"layout {{ {caret}compressRests {words[1]} }}\n{LanguageBook}";
        Assert.Equal(L(""), Step(L("‸"), +1, semitone: true).After);
    }

    [Fact]
    public void ATrackOrAPartNamedLanguage_IsNotALanguage()
    {
        string track = $"{LanguageBook}lyrics language {{ a }}\n";
        Assert.Equal(track, Step($"{LanguageBook}lyrics ‸language {{ a }}\n", +1, semitone: true).After);
        string part = "part language { }\nsection A { language { c'4 } }\n";
        Assert.Equal(part, Step("part ‸language { }\nsection A { language { c'4 } }\n", +1, semitone: true).After);
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
        // The octave pair steps a phrase, four bars, and stops at the least.
        Assert.Equal(L("barNumbers every 8"), Step(L("barNumbers every ‸4"), +1, semitone: false).After);
        Assert.Equal(L("barNumbers every 1"), Step(L("barNumbers every ‸3"), -1, semitone: false).After);
    }

    /// <summary>Owner's request 2026-10-30: every value in a layout block steps — the number keys
    /// by a small step on Up / Down and a large one on PageUp / PageDown, each on its own grid and
    /// never under its least; voltaBracket's bars as counts (and the file's capo fret, a line of its
    /// own since 2026-10-31); voltaBracket's, chordDiagrams' and chordTuning's words along their lists.</summary>
    [Fact]
    public void ALayoutsNumbers_Step_SmallOnUpDown_LargeOnPageUpDown()
    {
        string L(string entry) => $"layout {{ {entry} }}\npart m {{ }}\nsection A {{ m {{ c'4 }} }}\n";
        Assert.Equal(L("Stem.thickness 1.4"), Step(L("Stem.thickness ‸1.3"), +1, semitone: true).After);
        Assert.Equal(L("Stem.thickness 1.8"), Step(L("Stem.thickness ‸1.3"), +1, semitone: false).After);
        Assert.Equal(L("lineThickness 0.11"), Step(L("lineThickness ‸0.1"), +1, semitone: true).After);
        Assert.Equal(L("Dots.padding 0.4"), Step(L("Dots.padding ‸0.45"), -1, semitone: true).After);
        Assert.Equal(L("NoteHead.scale 1.05"), Step(L("NoteHead.scale ‸1.0"), +1, semitone: true).After);
        // cueSize is LilyPond's font-size step, written with its sign: one step on Up / Down, two
        // on the page pair, never over 0 and never under -8 (2026-10-31).
        Assert.Equal(L("cueSize -3"), Step(L("cueSize ‸-4"), +1, semitone: true).After);
        Assert.Equal(L("cueSize -2"), Step(L("cueSize -‸4"), +1, semitone: false).After);
        Assert.Equal(L("cueSize 0"), Step(L("cueSize ‸-1"), +1, semitone: true).After);
        Assert.Equal(L("cueSize -1"), Step(L("cueSize ‸0"), -1, semitone: true).After);
        var (most, atMost) = Step(L("cueSize ‸0"), +1, semitone: true);
        Assert.Equal(L("cueSize 0"), atMost);
        Assert.Contains("most", most.Message);
        Assert.Equal(L("cueSize -8"), Step(L("cueSize ‸-7"), -1, semitone: false).After);
        // Each of LedgerLine.thickness's two numbers on its own steps.
        Assert.Equal(L("LedgerLine.thickness 1.0 0.11"), Step(L("LedgerLine.thickness 1.0 ‸0.1"), +1, semitone: true).After);
        Assert.Equal(L("LedgerLine.thickness 1.1 0.1"), Step(L("LedgerLine.thickness ‸1.0 0.1"), +1, semitone: true).After);
        // Never under the least.
        var (least, kept) = Step(L("lineThickness ‸0.01"), -1, semitone: false);
        Assert.Equal(L("lineThickness 0.01"), kept);
        Assert.Contains("least", least.Message);
        // Counts.
        Assert.Equal(L("voltaBracket 3"), Step(L("voltaBracket ‸2"), +1, semitone: true).After);
        Assert.Equal(L("voltaBracket 6"), Step(L("voltaBracket ‸2"), +1, semitone: false).After);
        // The file's `capo N` line (2026-10-31): a fret a step, 1 to 11.
        string T(string line) => $"{line}\npart m {{ }}\nsection A {{ m {{ c'4 }} }}\n";
        Assert.Equal(T("capo 4"), Step(T("capo ‸3"), +1, semitone: true).After);
        Assert.Equal(T("capo 11"), Step(T("capo ‸11"), +1, semitone: true).After);
        // Words.
        Assert.Equal(L("voltaBracket line"), Step(L("voltaBracket ‸all"), -1, semitone: true).After);
        Assert.Equal(L("chordDiagrams none"), Step(L("chordDiagrams ‸written"), -1, semitone: true).After);
        Assert.Equal(L("chordDiagrams all"), Step(L("chordDiagrams ‸none"), -1, semitone: true).After);
        Assert.NotEqual(L("chordTuning guitar"), Step(L("chordTuning ‸guitar"), -1, semitone: true).After);
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
        Assert.NotEmpty(answer.Pitches);
    }

    [Fact]
    public void AnInstrumentPreset_WithStrings_StrumsThemAsATuningDoes()
    {
        // Owner's request 2026-10-24: like `tuning`, string after string from the highest-numbered,
        // in the preset's own sound — a ukulele's high 4th-string G first.
        AuditionAtResponse Answer(string header)
        {
            string text = Header(header);
            int word = header.IndexOf(' ') + 1;
            return Open(text).AuditionAt(new AuditionAtParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = DocUri },
                Offset = text.IndexOf(header, StringComparison.Ordinal) + word + 1,
            });
        }
        string Sounded(string preset)
        {
            string text = Header("instrument " + preset);
            var answer = Open(text).AuditionAt(new AuditionAtParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = DocUri },
                Offset = text.IndexOf("instrument ", StringComparison.Ordinal) + "instrument ".Length + 1,
            });
            Assert.Equal(LilySharp.Core.Svg.Model.InstrumentDefaults.GetMidiProgram(preset), answer.Program);
            return $"{string.Join(",", answer.Pitches)} strum={answer.Strum}";
        }
        Assert.Equal("67,60,64,69 strum=True", Sounded("ukulele"));
        Assert.Equal("28,33,38,43 strum=True", Sounded("bass"));
        Assert.Equal("40,45,50,55,59,64 strum=True", Sounded("guitar"));
        Assert.Equal("55,62,69,76 strum=True", Sounded("violin"));
        Assert.Equal("67,50,55,59,62 strum=True", Sounded("banjo"));
        // A piano hand: a Cmaj7 struck together (owner's request 2026-10-24), open in the left.
        Assert.Equal("60,64,67,71 strum=False", Sounded("piano-right"));
        Assert.Equal("36,43,47,52 strum=False", Sounded("piano-left"));
        // No strings: the one note, struck once.
        Assert.EndsWith("strum=False", Sounded("flute"));
        Assert.DoesNotContain(",", Sounded("flute"));

        // A bass's strings sound apart, each stopping as the next sounds (owner's request
        // 2026-10-24: a low chord is muddy); the others ring on together.
        Assert.True(Answer("instrument bass").Apart);
        Assert.True(Answer("instrument bass5").Apart);
        Assert.True(Answer("tuning bassDropD").Apart);
        Assert.True(Answer("tuning doubleBass").Apart);
        Assert.False(Answer("instrument ukulele").Apart);
        Assert.False(Answer("tuning guitar").Apart);
        Assert.False(Answer("tuning cello").Apart);
        Assert.True(Step(Header("instrument ‸bass6"), +1, semitone: true).Response.Apart);   // stepped onto bass5

        // A step onto one strums it too.
        var presets = LilySharp.Core.Semantics.LanguageVocabulary.InstrumentPresets.ToList();
        int uke = presets.IndexOf("ukulele");
        var (step, _) = Step(Header("instrument ‸" + presets[uke - 1]), -1, semitone: true);
        Assert.Equal([67, 60, 64, 69], step.Pitches);
        Assert.True(step.Strum);
    }

    [Fact]
    public void AStringedMidiInstrument_StrumsItsOpenStrings()
    {
        // Owner's request 2026-10-24: a midiInstrument with strings sounds them as a tuning does.
        AuditionAtResponse Answer(string name)
        {
            string text = Header($"midiInstrument \"{name}\"");
            return Open(text).AuditionAt(new AuditionAtParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = DocUri },
                Offset = text.IndexOf(name, StringComparison.Ordinal) + 1,
            });
        }
        var steel = Answer("acoustic guitar (steel)");
        Assert.Equal([40, 45, 50, 55, 59, 64], steel.Pitches);
        Assert.True(steel.Strum);
        Assert.False(steel.Apart);
        Assert.Equal(25, steel.Program);
        var slap = Answer("slap bass 1");
        Assert.Equal([28, 33, 38, 43], slap.Pitches);
        Assert.True(slap.Strum && slap.Apart);
        Assert.Equal([36, 43, 50, 57], Answer("cello").Pitches);
        Assert.Equal([67, 50, 55, 59, 62], Answer("banjo").Pitches);
        // No strings: one note.
        Assert.Single(Answer("synth bass 1").Pitches);
        Assert.False(Answer("flute").Strum);
    }

    [Fact]
    public void ARepeatsCount_StepsHowOftenItPlays()
    {
        // Owner's request 2026-10-24: the 3 of `:|*3` steps, from 2 (what `:|` alone plays).
        string Form(string bar) => "part m { }\nsection A { m { c'4 } }\nform { |: A " + bar + " }\n";
        var (up, after) = Step(Form(":|*‸3"), +1, semitone: true);
        Assert.False(up.Fallback, up.Error);
        Assert.Equal(Form(":|*4"), after);
        Assert.Equal(":|*4: plays it 4 times", up.Message);
        Assert.Equal(Form(":|*2"), Step(Form(":|*3‸"), -1, semitone: true).After);
        var (fewest, stays) = Step(Form(":|*‸2"), -1, semitone: true);
        Assert.Equal(Form(":|*2"), stays);
        Assert.Contains("already the fewest", fewest.Message);
        // The bar line itself still steps its look.
        Assert.Equal(Form(":|:"), Step(Form("‸:|*3"), -1, semitone: true).After);
        // A :|*N with no |: (it repeats from the top) is a bar line of its own: the same.
        string Alone(string bar) => "part m { }\nsection A { m { c'4 } }\nform { A " + bar + " }\n";
        Assert.Equal(Alone(":|*5"), Step(Alone(":|*‸4"), +1, semitone: true).After);
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
        const string Book = "part sop {{ }}\npart alt {{ }}\nlyrics v {{ la la }}\nsection A {{ sop {{ c''1 }} alt {{ a'1 }} }}\nform {{ A }}\nscore {{ staff sop lyrics v on {0} }}\n";
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
        Assert.Contains("{ C(", Step(S("C‸"), -1, semitone: true).After);
        // On an @chord in the music too.
        Assert.Equal(Guitar("c'1@chord(C7)"), Step(Guitar("c'1@chord(«Cm»)"), -1, semitone: true).After);
    }

    [Fact]
    public void ABarLine_WalksItsLooks_ANoteTouchingItKeepsTheKeys()
    {
        // Owner's request 2026-10-24: | || |. ! — Down down the completion's list.
        Assert.Equal(Plain("c'1 || d'1"), Step(Plain("c'1 ‸| d'1"), -1, semitone: true).After);
        Assert.Equal(Plain("c'1 |. d'1"), Step(Plain("c'1 ||‸ d'1"), -1).After);
        Assert.Equal(Plain("c'1 ! d'1"), Step(Plain("c'1 ‸|. d'1"), -1, semitone: true).After);
        Assert.Equal(Plain("c'1 | d'1"), Step(Plain("c'1 ‸|| d'1"), +1, semitone: true).After);
        var (last, same) = Step(Plain("c'1 ‸! d'1"), -1, semitone: true);
        Assert.Equal(Plain("c'1 ! d'1"), same);
        Assert.Contains("last", last.Message);
        // A caret between a note and a bar line it touches steps the note.
        Assert.DoesNotContain("||", Step(Plain("c'1‸| d'1"), -1, semitone: true).After);
    }

    [Fact]
    public void AFormsBarLines_WalkTheFormsSigns_TheUsualFirst()
    {
        // Owner's order 2026-10-24: | || |: :| :|: |. ! — the form's completion lists them so too.
        var walk = LilySharp.Core.Editing.NoteStepper.FormBarlineWalk;
        Assert.Equal(new[] { "|", "||", "|:", ":|", ":|:", "|.", "!" }, walk);
        const string Book = "part m {{ }}\nsection A {{ m {{ c'1 }} }}\nsection B {{ m {{ d'1 }} }}\nform {{ A {0} B }}\n";
        string S(string bar) => string.Format(Book, bar);
        for (int i = 0; i + 1 < walk.Count; i++)
        {
            Assert.Equal(S(walk[i + 1]), Step(S("‸" + walk[i]), -1, semitone: true).After);
            Assert.Equal(S(walk[i]), Step(S(walk[i + 1] + "‸"), +1, semitone: true).After);
        }
        var (last, same) = Step(S("‸!"), -1, semitone: true);
        Assert.Equal(S("!"), same);
        Assert.Contains("last", last.Message);
        // What each says: the divider draws nothing, an opening wants its close.
        Assert.Contains("divider", Step(S("‸||"), +1, semitone: true).Response.Message);
        Assert.Contains("close it with :|", Step(S("‸||"), -1, semitone: true).Response.Message);
        // The form's completion lists the engraved bar lines in the same order.
        var listed = LilySharpLanguageServer.GetFormCompletions("form { ").Items
            .Where(i => walk.Contains(i.Label)).OrderBy(i => i.SortText, StringComparer.Ordinal).Select(i => i.Label).ToList();
        Assert.Equal(walk.Where(w => w != "|"), listed);
    }

    [Fact]
    public void AFormsBarLines_InsideARepeat_AndARepeatCount()
    {
        const string Inside = "part m {{ }}\nsection A {{ m {{ c'1 }} }}\nsection B {{ m {{ d'1 }} }}\nform {{ |: A {0} B :| }}\n";
        Assert.Equal(string.Format(Inside, "||"), Step(string.Format(Inside, "‸|"), -1, semitone: true).After);
        Assert.Equal(string.Format(Inside, ":|"), Step(string.Format(Inside, "‸:|:"), +1, semitone: true).After);
        // A `:|*3` leaving the repeat's close takes its count with it.
        const string Count = "part m {{ }}\nsection A {{ m {{ c'1 }} }}\nsection B {{ m {{ d'1 }} }}\nform {{ A {0} B }}\n";
        Assert.Equal(string.Format(Count, ":|:"), Step(string.Format(Count, "‸:|*3"), -1, semitone: true).After);
        // The music's walk stops at `!`: a music body takes no repeat sign.
        var (music, kept) = Step(Plain("c'1 ‸! d'1"), -1, semitone: true);
        Assert.Equal(Plain("c'1 ! d'1"), kept);
        Assert.Contains("last", music.Message);
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
        Assert.Equal(Tuning("bassDropD"), after);
        Assert.Contains("D1 A1 D2 G2", down.Message);
        Assert.Equal([26, 33, 38, 43], down.Pitches);
        Assert.Equal(33, down.Program);
        Assert.True(down.Strum);   // the strings one after another, string after string
        Assert.Equal(Tuning("guitarAsus4"), Step(Tuning("bass‸"), +1, semitone: true).After);
        // A double steps from its tuning's place.
        Assert.Equal(Tuning("bassDropD"), Step(Tuning("‸bass4"), -1, semitone: true).After);

        // The octave pair: a family at a time; Up inside one goes to its first.
        Assert.Equal(Tuning("violin"), Step(Tuning("ba‸ss"), -1).After);
        Assert.Equal(Tuning("bass"), Step(Tuning("bass‸DropD"), +1).After);
        Assert.Equal(Tuning("guitar"), Step(Tuning("‸bass"), +1).After);
        var (first, same) = Step(Tuning("‸guitar"), +1);
        Assert.Equal(Tuning("guitar"), same);
        Assert.Contains("first", first.Message);
        var (last, stays) = Step(Tuning("‸tenorUkulele"), -1);
        Assert.Equal(Tuning("tenorUkulele"), stays);
        Assert.Contains("last", last.Message);
    }

    [Fact]
    public void ATuningsStrum_GoesStringAfterString_NotLowestFirst()
    {
        // Owner's report 2026-10-24: a re-entrant ukulele's 4th string (G4) is higher than its 3rd
        // (C4) and is struck first — G4 C4 E4 A4, not C4 E4 G4 A4.
        var (ukulele, _) = Step(Tuning("‸ukuleleD"), +1, semitone: true);
        Assert.True(ukulele.Strum);
        Assert.Equal([67, 60, 64, 69], ukulele.Pitches);
        // The 5-string banjo's 5th string is its high drone, struck first too.
        Assert.Equal([67, 50, 55, 59, 62], Step(Tuning("‸banjoC"), +1, semitone: true).Response.Pitches);
        // On a guitar string order and pitch order agree.
        Assert.Equal([40, 45, 50, 55, 59, 64], Step(Tuning("‸guitar7"), +1, semitone: true).Response.Pitches);
        // Typed or completed: the audition answers in the same order.
        string text = Tuning("ukulele");
        var answer = Open(text).AuditionAt(new AuditionAtParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocUri },
            Offset = text.IndexOf("ukulele", StringComparison.Ordinal) + 7,
        });
        Assert.Equal([67, 60, 64, 69], answer.Pitches);
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

    // ---------------------------------------------------------------- the ottava's spellings

    /// <summary>On <c>@8va</c> the keys step the printed spellings, lowest first — 15mb, 8vb, 8va,
    /// 15ma — stopping at the ends, and the <c>@!8va</c> that closes it is renamed with it
    /// (owner's request 2026-10-28), so the pair keeps one name (LYS4032).</summary>
    [Fact]
    public void AnOttava_StepsItsSpelling_AndItsEndWithIt()
    {
        Assert.Equal(Plain("c''4@15ma d'' e'' f''@!15ma | g'1 |"), Step(Plain("c''4@8‸va d'' e'' f''@!8va | g'1 |"), +1, semitone: true).After);
        Assert.Equal(Plain("c''4@8vb d'' e'' f''@!8vb | g'1 |"), Step(Plain("c''4@8‸va d'' e'' f''@!8va | g'1 |"), -1, semitone: true).After);
        var (top, same) = Step(Plain("c''4@15‸ma d'' e'' f''@!15ma | g'1 |"), +1, semitone: true);
        Assert.False(top.Fallback);
        Assert.Equal(Plain("c''4@15ma d'' e'' f''@!15ma | g'1 |"), same);
        Assert.Equal("15ma / 8va / 8vb / 15mb", top.Message);
        // An end already named otherwise is left as written (LYS4032 says so).
        Assert.Equal(Plain("c''4@15ma d'' e'' f''@!8vb | g'1 |"), Step(Plain("c''4@8‸va d'' e'' f''@!8vb | g'1 |"), +1, semitone: true).After);
    }

    /// <summary>The tempo words step slowest to fastest — rall, rit, accel — stopping at the ends,
    /// and the @! that closes the stepped one follows it (owner's decisions 2026-10-28).</summary>
    [Fact]
    public void ATempoWord_StepsRallRitAccel_AndItsEndWithIt()
    {
        Assert.Equal(Plain("c'4@accel d' e' f'@!accel | g'1 |"), Step(Plain("c'4@ri‸t d' e' f'@!rit | g'1 |"), +1, semitone: true).After);
        Assert.Equal(Plain("c'4@rall d' e' f'@!rall | g'1 |"), Step(Plain("c'4@ri‸t d' e' f'@!rit | g'1 |"), -1, semitone: true).After);
        Assert.Equal(Plain("c'4@rit d' e' f'@!rit | g'1 |"), Step(Plain("c'4@ra‸ll d' e' f'@!rall | g'1 |"), +1).After);
        var (top, same) = Step(Plain("c'4@acc‸el d' e' f'@!accel | g'1 |"), +1, semitone: true);
        Assert.False(top.Fallback);
        Assert.Equal(Plain("c'4@accel d' e' f'@!accel | g'1 |"), same);
        Assert.Equal("accel / rit / rall", top.Message);
    }

    /// <summary>Groups whose order means little, so a related mark is a key away (owner's request
    /// 2026-10-28): the pedals, their end following; the bracketed accidentals, the half ties, the
    /// harmonic circles, the arpeggio and its bracket.</summary>
    [Fact]
    public void RelatedMarks_StepEvenWhereTheOrderMeansLittle()
    {
        Assert.Equal(Plain("c'4@sostenuto d' e' f'@!sostenuto | g'1 |"),
            Step(Plain("c'4@sus‸tain d' e' f'@!sustain | g'1 |"), -1, semitone: true).After);
        Assert.Equal(Plain("c'4@unaCorda d' e' f'@!unaCorda | g'1 |"),
            Step(Plain("c'4@soste‸nuto d' e' f'@!sostenuto | g'1 |"), -1, semitone: true).After);

        foreach (var (from, to, dir) in new[]
        {
            ("cis'4@cour‸tesy", "cis'4@editorial", -1), ("c'4@repeat‸Tie", "c'4@laissezVibrer", +1),
            ("c'4@harm‸onic", "c'4@flageolet", -1), ("<c' e' g'>4@arpe‸ggio", "<c' e' g'>4@arpeggio(bracket)", +1),
            ("<c' e' g'>4@arpeggio(brac‸ket)", "<c' e' g'>4@arpeggio", -1),
        })
            Assert.Equal(Plain(to + " d'2. |"), Step(Plain(from + " d'2. |"), dir, semitone: true).After);
        // Every pair stops at its ends.
        var (stays, same) = Step(Plain("c'4@laissez‸Vibrer d'2. |"), +1, semitone: true);
        Assert.False(stays.Fallback);
        Assert.Equal(Plain("c'4@laissezVibrer d'2. |"), same);
        Assert.Equal("laissezVibrer / repeatTie", stays.Message);
    }

    /// <summary>The trills step shortest to longest — pralltriller, trill, trillSpan (owner's
    /// decision 2026-10-28) — and the span's end goes with it: stepped to, an @!trillSpan on the
    /// bar's last note, after its marks; stepped away, its @!trillSpan deleted.</summary>
    [Theory]
    [InlineData("c''4@tri‸ll d'' e'' f'' |", +1, "c''4@trillSpan d'' e'' f''@!trillSpan |")]
    [InlineData("c''4@tri‸ll d'' e'' f''@staccato |", +1, "c''4@trillSpan d'' e'' f''@staccato@!trillSpan |")]
    [InlineData("c''4@tri‸ll d''8 r8 e''4 <f'' a''>4 |", +1, "c''4@trillSpan d''8 r8 e''4 <f'' a''>4@!trillSpan |")]
    [InlineData("c''2.@tri‸ll r4 |", +1, "c''2.@trillSpan r4@!trillSpan |")]
    [InlineData("c''4@trill‸Span d'' e''@!trillSpan f'' |", -1, "c''4@trill d'' e'' f'' |")]
    [InlineData("c''4@pralltr‸iller d'' e'' f'' |", +1, "c''4@trill d'' e'' f'' |")]
    [InlineData("c''4@tri‸ll d'' e'' f'' |", -1, "c''4@pralltriller d'' e'' f'' |")]
    // An end already written (a stray one) is the span's: none is added.
    [InlineData("c''4@tri‸ll d'' e''@!trillSpan f'' |", +1, "c''4@trillSpan d'' e''@!trillSpan f'' |")]
    public void TheTrills_Step_AndTheSpansEndGoesWithIt(string from, int dir, string to)
        => Assert.Equal(Plain(to), Step(Plain(from), dir, semitone: true).After);

    [Fact]
    public void ATrillSpanStepped_CompilesClean()
    {
        string after = Step(Plain("c''4@tri‸ll d'' e'' f'' | g''1 |"), +1, semitone: true).After;
        Assert.DoesNotContain(LilySharp.Core.Semantics.SemanticValidation.Run(LilySharp.Core.Syntax.SyntaxTree.Parse(after)),
            d => d.Severity != LilySharp.Core.Syntax.DiagnosticSeverity.Info);
        var (top, _) = Step(Plain("c''4@trillSp‸an d''@!trillSpan e'' f'' |"), +1, semitone: true);
        Assert.Equal("trillSpan / trill / pralltriller", top.Message);
    }

    /// <summary>A span start that nothing closes, stepped, gets its end on the last note of its
    /// bar — of the next bar when it stands on that last one (owner's decision 2026-10-28) — for
    /// every family the keys step: a text span, an ottava, a pedal. One already closed, by its
    /// end or by a later start of its family, is left alone.</summary>
    [Theory]
    [InlineData("c'4@ri‸t d' e' f' | g'1 |", +1, "c'4@accel d' e' f'@!accel | g'1 |")]
    [InlineData("c'4 d' e' f'@ri‸t | g'2 a' | b'1 |", +1, "c'4 d' e' f'@accel | g'2 a'@!accel | b'1 |")]
    [InlineData("c'4@ra‸ll d' e' f'@fermata | g'1 |", +1, "c'4@rit d' e' f'@fermata@!rit | g'1 |")]
    [InlineData("c''4@8‸va d'' e'' f'' | g'1 |", +1, "c''4@15ma d'' e'' f''@!15ma | g'1 |")]
    [InlineData("c4@8‸va d e f | g1 |", -1, "c4@8vb d e f@!8vb | g1 |")]
    [InlineData("c'4@sus‸tain d' e' f' | g'1 |", -1, "c'4@sostenuto d' e' f'@!sostenuto | g'1 |")]
    // Already closed: by its end (renamed with it), a later start of its family (an ottava's change).
    [InlineData("c'4@ri‸t d' | e' f'@!rit g'2 |", +1, "c'4@accel d' | e' f'@!accel g'2 |")]
    [InlineData("c''4@8‸va d'' e''@15ma f'' | g''1@!15ma |", +1, "c''4@15ma d'' e''@15ma f'' | g''1@!15ma |")]
    // Not a span: nothing is added.
    [InlineData("c'4@staccato‸ d' e' f' | g'1 |", +1, "c'4@portato d' e' f' | g'1 |")]
    public void AnUnclosedSpan_Stepped_IsClosedAtItsBarsLastNote(string from, int dir, string to)
        => Assert.Equal(Plain(to), Step(Plain(from), dir, semitone: true).After);

    [Fact]
    public void AnUnclosedSpan_Stepped_CompilesClean()
    {
        string after = Step(Plain("c'4@ri‸t d' e' f' | g'1 |"), +1, semitone: true).After;
        Assert.DoesNotContain(LilySharp.Core.Semantics.SemanticValidation.Run(LilySharp.Core.Syntax.SyntaxTree.Parse(after)),
            d => d.Severity != LilySharp.Core.Syntax.DiagnosticSeverity.Info);
    }

    /// <summary>On an @! end the keys step the start it closes, and the end with it (owner's
    /// request 2026-10-28: "@!accel も ctrl+shift+down で動くように") — a text span, an ottava,
    /// a pedal, a hairpin, a trill span (stepped away, its end goes).</summary>
    [Theory]
    [InlineData("c'4@accel d' e' f'@!acc‸el | g'1 |", -1, "c'4@rit d' e' f'@!rit | g'1 |")]
    [InlineData("c'4@rit d' e' f'@!r‸it | g'1 |", +1, "c'4@accel d' e' f'@!accel | g'1 |")]
    [InlineData("c''4@8va d'' e'' f''@!8‸va | g'1 |", +1, "c''4@15ma d'' e'' f''@!15ma | g'1 |")]
    [InlineData("c4@8vb d e f@!8‸vb | g'1 |", -1, "c4@15mb d e f@!15mb | g'1 |")]
    [InlineData("c'4@sustain d' e' f'@!sus‸tain | g'1 |", -1, "c'4@sostenuto d' e' f'@!sostenuto | g'1 |")]
    [InlineData("c'4@cresc d' e' f'@!cre‸sc | g'1 |", -1, "c'4@decresc d' e' f'@!decresc | g'1 |")]
    [InlineData("c''4@trillSpan d'' e''@!trill‸Span f'' |", -1, "c''4@trill d'' e'' f'' |")]
    public void AnEnd_StepsItsStart_AndItself(string from, int dir, string to)
        => Assert.Equal(Plain(to), Step(Plain(from), dir, semitone: true).After);

    /// <summary>The accent dynamics step among themselves, the lighter first (owner's request
    /// 2026-10-28) — they stepped the note before.</summary>
    [Fact]
    public void AnAccentDynamic_StepsAmongTheAccents()
    {
        Assert.Equal(Plain("c'4@sfz d'2. |"), Step(Plain("c'4@f‸z d'2. |"), +1, semitone: true).After);
        Assert.Equal(Plain("c'4@sf d'2. |"), Step(Plain("c'4@f‸z d'2. |"), -1, semitone: true).After);
        var (top, same) = Step(Plain("c'4@sff‸z d'2. |"), +1, semitone: true);
        Assert.False(top.Fallback);
        Assert.Equal(Plain("c'4@sffz d'2. |"), same);
        Assert.Equal("sffz / sfz / fz / sf / rfz / rf / sfp / fp", top.Message);
    }

    /// <summary>An argument from a list steps along it (owner's request 2026-10-28: "notehead の
    /// かっこの中なども up/down で操作できる方が便利"): a notehead's shape and a plucking finger down
    /// the completion's list, a finger and a bend Up to the larger, a feather Up to the faster.</summary>
    [Theory]
    [InlineData("c'4@notehead(‸x) d'2. |", -1, "c'4@notehead(cross) d'2. |")]
    [InlineData("c'4@notehead( dia‸mond ) d'2. |", -1, "c'4@notehead( triangle ) d'2. |")]
    [InlineData("c'4@notehead(cr‸oss).up d'2. |", +1, "c'4@notehead(x).up d'2. |")]
    [InlineData("c'4@finger(‸3) d'2. |", +1, "c'4@finger(4) d'2. |")]
    [InlineData("c'4@pluck(‸p) d'2. |", -1, "c'4@pluck(i) d'2. |")]
    [InlineData("c'4@bend(‸full) d'2. |", +1, "c'4@bend(3) d'2. |")]
    [InlineData("c'4@bend(‸full) d'2. |", -1, "c'4@bend(half) d'2. |")]
    [InlineData("c'8@feather(‸left)[ d' e' f'] g'2 |", +1, "c'8@feather(right)[ d' e' f'] g'2 |")]
    // A bow's line, from the name or the word (owner's request 2026-11-13).
    // …and the solid one is a word too, so the three can be compared in the preview
    // (owner's request 2026-11-13).
    [InlineData("c'4@slur(so‸lid)( d' e' f') |", -1, "c'4@slur(dashed)( d' e' f') |")]
    [InlineData("c'4@slur(‸dashed)( d' e' f') |", +1, "c'4@slur(solid)( d' e' f') |")]
    [InlineData("c'2@tie(‸solid)~ c'2 |", -1, "c'2@tie(dashed)~ c'2 |")]
    [InlineData("c'4@phrasingSlur(‸dashed) d' e' f'@!phrasingSlur |", +1, "c'4@phrasingSlur(solid) d' e' f'@!phrasingSlur |")]
    [InlineData("c'4@sl‸ur(dashed)( d' e' f') |", -1, "c'4@slur(dotted)( d' e' f') |")]
    [InlineData("c'4@slur(dot‸ted)( d' e' f') |", +1, "c'4@slur(dashed)( d' e' f') |")]
    [InlineData("c'2@‸tie(dashed)~ c'2 |", -1, "c'2@tie(dotted)~ c'2 |")]
    [InlineData("c'2@tie(‸dotted)~ c'2 |", +1, "c'2@tie(dashed)~ c'2 |")]
    // …and a phrasing slur's (owner-approved 2026-11-13).
    [InlineData("c'4@phrasing‸Slur(dashed) d' e' f'@!phrasingSlur |", -1, "c'4@phrasingSlur(dotted) d' e' f'@!phrasingSlur |")]
    [InlineData("c'4@phrasingSlur(dot‸ted).up d' e' f'@!phrasingSlur |", +1, "c'4@phrasingSlur(dashed).up d' e' f'@!phrasingSlur |")]
    // A bare one stands at solid: Up writes that word out, Down the next one down
    // (owner's request 2026-11-13).
    [InlineData("c'4@sl‸ur( d' e' f') |", +1, "c'4@slur(solid)( d' e' f') |")]
    [InlineData("c'4@slur‸( d' e' f') |", -1, "c'4@slur(dashed)( d' e' f') |")]
    [InlineData("c'2@ti‸e~ c'2 |", +1, "c'2@tie(solid)~ c'2 |")]
    [InlineData("c'4@phrasing‸Slur d' e' f'@!phrasingSlur |", +1, "c'4@phrasingSlur(solid) d' e' f'@!phrasingSlur |")]
    [InlineData("c'4@phrasing‸Slur d' e' f'@!phrasingSlur |", -1, "c'4@phrasingSlur(dashed) d' e' f'@!phrasingSlur |")]
    // The END of one has nothing to step, so the keys step the note it is written on
    // (owner's request 2026-11-13: "@!phrasingSlur の上で … 直前の音符の音高が変わるように").
    [InlineData("c'4@phrasingSlur d' e' f'@!phrasing‸Slur |", +1, "c'4@phrasingSlur d' e' g'@!phrasingSlur |")]
    [InlineData("c'4@phrasingSlur(dashed) d' e' f'@!phrasingSlur‸ |", -1, "c'4@phrasingSlur(dashed) d' e' e'@!phrasingSlur |")]
    public void AnArgumentFromAList_StepsAlongIt(string from, int dir, string to)
        => Assert.Equal(Plain(to), Step(Plain(from), dir, semitone: true).After);

    [Fact]
    public void AnArgument_StopsAtItsEnds_AndSaysItsList()
    {
        var (top, same) = Step(Plain("c'4@finger(‸5) d'2. |"), +1, semitone: true);
        Assert.False(top.Fallback);
        Assert.Equal(Plain("c'4@finger(5) d'2. |"), same);
        Assert.Equal("@finger(…): 5 / 4 / 3 / 2 / 1 / 0", top.Message);
    }

    /// <summary>The music keywords step among their kin (owner's request 2026-10-28): the grace
    /// notes down the completion's order, a break Up to the page's, stopping at the ends.</summary>
    [Theory]
    [InlineData("gr‸ace { d'16 } c'4 d'2. |", -1, "acciaccatura { d'16 } c'4 d'2. |")]
    [InlineData("acciacca‸tura { d'16 } c'4 d'2. |", -1, "appoggiatura { d'16 } c'4 d'2. |")]
    [InlineData("appoggi‸atura { d'16 } c'4 d'2. |", +1, "acciaccatura { d'16 } c'4 d'2. |")]
    [InlineData("c'1 | br‸eak d'1 |", +1, "c'1 | pageBreak d'1 |")]
    [InlineData("c'1 | pageBr‸eak d'1 |", -1, "c'1 | break d'1 |")]
    [InlineData("c'1 | noBr‸eak d'1 |", +1, "c'1 | noPageBreak d'1 |")]
    public void AMusicKeyword_StepsAmongItsKin(string from, int dir, string to)
        => Assert.Equal(Plain(to), Step(Plain(from), dir, semitone: true).After);

    [Fact]
    public void AMusicKeyword_StopsAtItsEnds()
    {
        var (top, same) = Step(Plain("c'1 | pageBr‸eak d'1 |"), +1, semitone: true);
        Assert.False(top.Fallback);
        Assert.Equal(Plain("c'1 | pageBreak d'1 |"), same);
        Assert.Equal("pageBreak / break", top.Message);
        var (_, grace) = Step(Plain("gr‸ace { d'16 } c'4 d'2. |"), +1, semitone: true);
        Assert.Equal(Plain("grace { d'16 } c'4 d'2. |"), grace);
    }

    /// <summary>A figured bass steps along the figures the completion offers, Down down its list;
    /// a rehearsal mark's letter or number steps, Up the next (owner's request 2026-10-28).</summary>
    [Theory]
    [InlineData("c4@figuredBass(‸6) d2. |", -1, "c4@figuredBass(6 4) d2. |")]
    [InlineData("c4@figuredBass(6  ‸4) d2. |", +1, "c4@figuredBass(6) d2. |")]
    [InlineData("c'4@mark(\"‸A\") d'2. |", +1, "c'4@mark(\"B\") d'2. |")]
    [InlineData("c'4@mark(\"‸C\") d'2. |", -1, "c'4@mark(\"B\") d'2. |")]
    [InlineData("c'4@mark(\"1‸2\") d'2. |", +1, "c'4@mark(\"13\") d'2. |")]
    [InlineData("c'4@mark(‸\"b\") d'2. |", +1, "c'4@mark(\"c\") d'2. |")]
    public void AFigureAndARehearsalMark_Step(string from, int dir, string to)
        => Assert.Equal(Plain(to), Step(Plain(from), dir, semitone: true).After);

    [Fact]
    public void ARehearsalMark_StopsAtItsEnds_AndAWordIsTheWriters()
    {
        var (first, same) = Step(Plain("c'4@mark(\"‸A\") d'2. |"), -1, semitone: true);
        Assert.False(first.Fallback);
        Assert.Equal(Plain("c'4@mark(\"A\") d'2. |"), same);
        Assert.Equal("@mark(\"A\"): already the first", first.Message);
        // A word is not a letter: the label is the writer's, and nothing is stepped there.
        Assert.Equal(Plain("c'4@mark(\"Coda\") d'2. |"), Step(Plain("c'4@mark(\"Co‸da\") d'2. |"), +1, semitone: true).After);
    }

    private static string Form(string form) => $$"""
        octave absolute
        part p { clef treble }
        section A { p { c'1 | } }
        section B { p { d'1 | } }
        form { {{form}} }
        score { staff p }
        """;

    /// <summary>A navigation mark steps among its kin in the completion's order, Down the next:
    /// segno / coda, to coda / fine, and the six repeat instructions (owner's request 2026-10-28).</summary>
    [Theory]
    [InlineData("A seg‸no B", -1, "A coda B")]
    [InlineData("A B to co‸da A", -1, "A B fine A")]
    [InlineData("A B d‸c", -1, "A B ds")]
    [InlineData("A B dc al fi‸ne", -1, "A B dc al coda")]
    [InlineData("A B ds  al co‸da", +1, "A B ds al fine")]
    public void ANavigationMark_StepsAmongItsKin(string from, int dir, string to)
        => Assert.Equal(Form(to), Step(Form(from), dir, semitone: true).After);

    [Fact]
    public void ANavigationMark_StopsAtItsEnds()
    {
        var (last, same) = Step(Form("A B ds al co‸da"), -1, semitone: true);
        Assert.False(last.Fallback);
        Assert.Equal(Form("A B ds al coda"), same);
        Assert.Equal("dc / ds / dc al fine / dc al coda / ds al fine / ds al coda", last.Message);
    }

    /// <summary>The stepped arguments are the completion's: every value it offers can be stepped
    /// to, so the two cannot drift apart.</summary>
    [Fact]
    public void TheSteppedArguments_AreTheCompletionsValues()
    {
        foreach (var (name, values) in LilySharp.Core.Editing.NoteStepper.ArgumentSteps)
        {
            var offered = LilySharpLanguageServer.GetAnnotationArgumentCompletions(name)!.Items.Select(i => i.Label).ToList();
            Assert.Empty(offered.Except(values));
            if (name != "bend")
                Assert.Equal(offered.OrderBy(v => v), values.OrderBy(v => v));
        }
    }

    // ---------------------------------------------------------------- a mark's .up / .down

    /// <summary>The octave pair on a mark that takes a placement (owner's request 2026-10-28):
    /// PageUp raises it — .down, none, .up — PageDown lowers it, stopping at the ends; the
    /// semitone pair still steps the name or the level and keeps the placement.</summary>
    [Fact]
    public void TheOctavePair_PlacesAMark_DownNoneUp()
    {
        Assert.Equal(Plain("c'4@staccato.up d'"), Step(Plain("c'4@stacc‸ato d'"), +1).After);
        Assert.Equal(Plain("c'4@staccato.down d'"), Step(Plain("c'4@stacc‸ato d'"), -1).After);
        Assert.Equal(Plain("c'4@staccato d'"), Step(Plain("c'4@staccato.u‸p d'"), -1).After);
        Assert.Equal(Plain("c'4@staccato d'"), Step(Plain("c'4@stacc‸ato.down d'"), +1).After);
        var (top, same) = Step(Plain("c'4@fer‸mata.up d'"), +1);
        Assert.False(top.Fallback);
        Assert.Equal(Plain("c'4@fermata.up d'"), same);
        Assert.Equal("@fermata: .up / its own side / .down", top.Message);
        // A dynamic level, its subito kept; a hairpin takes none, so the octave pair turns it as before.
        Assert.Equal(Plain("c'4@f.up d'"), Step(Plain("c'4@‸f d'"), +1).After);
        Assert.Equal(Plain("c'4@p.subito.down d'"), Step(Plain("c'4@p‸.subito d'"), -1).After);
        Assert.Equal(Plain("c'4@cresc d'"), Step(Plain("c'4@de‸cresc d'"), +1).After);
        // The value annotations that take it: after their argument.
        Assert.Equal(Plain("c'4@text(\"dolce\").down d'"), Step(Plain("c'4@te‸xt(\"dolce\") d'"), -1).After);
        Assert.Equal(Plain("c'4@todo(\"memo\").down d'"), Step(Plain("c'4@todo(\"me‸mo\") d'"), -1).After);
        // The semitone pair keeps the name's step, the placement with it.
        Assert.Equal(Plain("c'4@portato.up d'"), Step(Plain("c'4@stacc‸ato.up d'"), +1, semitone: true).After);
    }

    // ---------------------------------------------------------------- a dynamic, a time, octave N, transpose

    [Fact]
    public void ADynamic_StepsALevel_AndAHairpinTurnsWithTheKeys()
    {
        Assert.Equal(Plain("c'4@mf d'"), Step(Plain("c'4@m‸p d'"), +1, semitone: true).After);
        // The octave pair places a dynamic level instead (owner's request 2026-10-28; below).
        Assert.Equal(Plain("c'4@p.down d'"), Step(Plain("c'4@p‸ d'"), -1).After);
        Assert.Equal(Plain("c'4@f.up d'"), Step(Plain("c'4@m‸f.up d'"), +1, semitone: true).After);
        Assert.Equal(Plain("c'4@cresc d'"), Step(Plain("c'4@de‸cresc d'"), +1, semitone: true).After);
        Assert.Equal(Plain("c'4@decresc d'"), Step(Plain("c'4@cresc‸ d'"), -1, semitone: true).After);
        Assert.Equal(Plain("c'4@cresc d'"), Step(Plain("c'4@‸dim d'"), +1, semitone: true).After);
        // A direction per key, a second press staying (the owner, 2026-10-28); the @! that ends the
        // hairpin changes with it, and one a dynamic ended is left alone.
        var (again, after) = Step(Plain("c'4@cre‸sc d'"), +1, semitone: true);
        Assert.False(again.Fallback);
        Assert.Equal(Plain("c'4@cresc d'"), after);
        Assert.Equal(Plain("c'4@decresc d' e' f'@!decresc | g'1 |"), Step(Plain("c'4@cre‸sc d' e' f'@!cresc | g'1 |"), -1, semitone: true).After);
        Assert.Equal(Plain("c'4@cresc d' e' f'@!cresc | g'1 |"), Step(Plain("c'4@decre‸sc d' e' f'@!decresc | g'1 |"), +1, semitone: true).After);
        Assert.Equal(Plain("c'4@cresc d' e' f'@!cresc | g'1 |"), Step(Plain("c'4@cre‸sc d' e' f'@!cresc | g'1 |"), +1, semitone: true).After);
        Assert.Equal(Plain("c'4@decresc d' e'@f f' | g'1@!cresc |"), Step(Plain("c'4@cre‸sc d' e'@f f' | g'1@!cresc |"), -1, semitone: true).After);
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
        // The octave pair places the mark instead (owner's request 2026-10-28; below).
        Assert.Equal(Plain("c'4@downBow.up d'"), Step(Plain("c'4@down‸Bow d'"), +1).After);
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
        // The octave pair places a mark that takes a placement instead (owner's request 2026-10-28).
        Assert.Equal(Plain("c'4@tap.up d'"), Step(Plain("c'4@t‸ap d'"), +1).After);
        Assert.Equal(Plain("d'4@niente d'"), Step(Plain("c'4@nie‸nte d'"), +1, semitone: true).After);
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
        // Up from the second shape writes the default: x35343 sounds.
        var (response, _) = Step(Guitar($"c'1@chord(Cm7 {Cm7Order()[1]}‸)"), +1, semitone: true);
        Assert.Equal(Cm7X35343, response.Pitches);
    }

    [Fact]
    public void ASteppedNote_SoundsItsNewPitch()
    {
        Assert.Equal([72], Step(Plain("c'‸4 d'4"), +1).Response.Pitches);          // c'' = C5 (Lily#'s absolute c is C3, owner's decision 2026-10-28)
        // A stepped member sounds its WHOLE chord with the member moved (owner's decision
        // 2026-10-10: `<…>` is one sound; it used to strike the moved member alone).
        Assert.Equal([60, 67, 76], Step(Plain("<c' e'‸ g'>4"), +1).Response.Pitches); // c' g' e'' = C4 G4 E5
        Assert.Equal([36], Step(Plain("c‸4 d'4"), -1).Response.Pitches);           // c = C3, c, = C2
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
    [InlineData("guitar", 62)]      // d' = D4: treble_8 is written at sounding pitch (owner's decision 2026-10-28)
    [InlineData("clarinet", 60)]    // d' = D4; the B♭ clarinet: two semitones below
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
        Assert.Equal([62], note.Pitches);

        // The caret on a MEMBER of `<…>` sounds the whole chord, under the chord's own key —
        // so arrowing across the members strikes it once (owner's decision 2026-10-10; it
        // used to sound the member alone, as "member").
        var member = AuditionAt(Plain("<c' ‸e' g'>4"));
        Assert.Equal("chord", member.Kind);
        Assert.Equal([60, 64, 67], member.Pitches);

        var chord = AuditionAt(Plain("<c' e' g'>‸4"));
        Assert.Equal("chord", chord.Kind);
        Assert.Equal([60, 64, 67], chord.Pitches);
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
        Assert.Equal(Guitar("r1@chord(Cm7 x35343)"), Down("r1@chord(Cm7‸)", Guitar, semitone: true));
        Assert.Equal(Guitar($"s1@chord(Cm7 {order[1]})"), Down("s1@chord(Cm7 x35343‸)", Guitar, semitone: true));
        Assert.Equal(Guitar("s1@chord(Cm7)"), Up("s1@chord(Cm7 x35343‸)", Guitar, semitone: true));
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

    // ---------------------------------------------------------------- a fenced text's indent

    /// <summary>A piece under a fenced text whose opening line is <paramref name="opening"/>.</summary>
    private static string Fenced(string opening, string body = "A note of 12 words.", string paper = "")
        => $"{paper}title \"T\"\n{opening}\n{body}\n```\npart m {{ clef treble }}\nsection A {{ m {{ c'1 | }} }}\nscore {{ staff m }}\n";

    private static string[] SaidOf(string source)
    {
        var tree = LilySharp.Core.Syntax.SyntaxTree.Parse(source);
        return [.. tree.Diagnostics.Concat(LilySharp.Core.Semantics.SemanticValidation.Run(tree)).Select(d => d.Code + " " + d.Message)];
    }

    /// <summary>Owner's request 2026-11-13: "数字を入力した後は、その数字を ctrl+shift+up/down で
    /// 動かせるようにして" — Up is more white, Down less; the unit and the rest of the line stay.</summary>
    [Theory]
    [InlineData("```md indent 20‸mm", +1, true, "```md indent 21mm")]
    [InlineData("```md indent ‸20mm", -1, true, "```md indent 19mm")]
    [InlineData("```md indent 20mm‸", +1, true, "```md indent 21mm")]            // touching its end
    [InlineData("```md indent 20mm‸ box", +1, true, "```md indent 21mm box")]
    [InlineData("```md end indent 20m‸m center", +1, false, "```md end indent 25mm center")]   // the large step: PageUp
    [InlineData("```md indent 20‸mm", -1, false, "```md indent 15mm")]
    [InlineData("```md indent 12.5‸mm", +1, true, "```md indent 13.5mm")]
    [InlineData("```md indent 2‸cm", +1, true, "```md indent 2.1cm")]
    [InlineData("```md indent 2.9‸cm", +1, true, "```md indent 3cm")]
    [InlineData("```md indent 2‸cm", -1, false, "```md indent 1.5cm")]
    [InlineData("```md indent 0.5‸in", +1, true, "```md indent 0.55in")]
    [InlineData("```md indent 0.5‸in", +1, false, "```md indent 0.75in")]
    // A bare number is staff spaces: a tenth and one, as a fonts entry's `size N`.
    [InlineData("```md indent 5‸", +1, true, "```md indent 5.1")]
    [InlineData("```md indent 5‸", +1, false, "```md indent 6")]
    [InlineData("```md indent 5.1‸", -1, true, "```md indent 5")]
    // A selection within the one length (a double-clicked number) steps it too.
    [InlineData("```md indent «20»mm", +1, true, "```md indent 21mm")]
    [InlineData("```md indent «20mm»", -1, true, "```md indent 19mm")]
    public void OnAnIndentsLength_TheKeysStepIt_KeepingItsUnit(string marked, int direction, bool semitone, string after)
    {
        var (response, stepped) = Step(Fenced(marked), direction, semitone: semitone);
        Assert.False(response.Fallback, response.Error);
        Assert.Equal(Fenced(after), stepped);
        // One edit, and inside the length alone: the fence is one token.
        var edit = Assert.Single(response.Edits);
        string source = Fenced(marked).Replace("‸", "").Replace("«", "").Replace("»", "");
        Assert.Matches(@"^[\d.]+(mm|cm|in)?$", source[edit.Start..edit.End]);
        Assert.StartsWith("indent ", response.Message);
        Assert.Contains("mm wide", response.Message);
        Assert.Empty(SaidOf(stepped));
    }

    [Fact]
    public void WithTwoLengths_TheOneUnderTheCaretSteps_WithOne_ItIsBothSides()
    {
        Assert.Equal(Fenced("```md indent 21mm 10mm"), Step(Fenced("```md indent 20‸mm 10mm"), +1, semitone: true).After);
        Assert.Equal(Fenced("```md indent 20mm 11mm"), Step(Fenced("```md indent 20mm 10‸mm"), +1, semitone: true).After);
        Assert.Equal(Fenced("```md indent 21mm 10mm"), Step(Fenced("```md indent 20mm‸ 10mm"), +1, semitone: true).After);
        Assert.Equal(Fenced("```md indent 20mm 9mm"), Step(Fenced("```md indent 20mm ‸10mm"), -1, semitone: true).After);
        Assert.Contains("the left", Step(Fenced("```md indent 20‸mm 10mm"), +1, semitone: true).Response.Message);
        Assert.Contains("the right", Step(Fenced("```md indent 20mm 10‸mm"), +1, semitone: true).Response.Message);
        Assert.Contains("both sides", Step(Fenced("```md indent 20‸mm"), +1, semitone: true).Response.Message);
        // Each keeps its own unit.
        Assert.Equal(Fenced("```md indent 2cm 6"), Step(Fenced("```md indent 2cm 5‸"), +1).After);
    }

    /// <summary>On the word itself the first length steps, as the word <c>tempo</c> steps its
    /// number and a fonts entry's <c>size</c> its own.</summary>
    [Fact]
    public void OnTheWordIndent_ItsFirstLengthSteps()
    {
        Assert.Equal(Fenced("```md indent 21mm 10mm"), Step(Fenced("```md in‸dent 20mm 10mm"), +1, semitone: true).After);
        Assert.Equal(Fenced("```md box indent 19mm"), Step(Fenced("```md box ‸indent 20mm"), -1, semitone: true).After);
        Assert.Equal(Fenced("```md indent 21mm"), Step(Fenced("```md indent‸ 20mm"), +1, semitone: true).After);
        var (bare, same) = Step(Fenced("```md inden‸t"), +1, semitone: true);
        Assert.False(bare.Fallback);
        Assert.Equal(Fenced("```md indent"), same);
        Assert.Contains("write its length first", bare.Message);
    }

    [Fact]
    public void AnIndent_StopsAtZero_AndBeforeTheWidthTheValidatorRefuses()
    {
        // The floor: the large step stops on it, and then the keys say so.
        Assert.Equal(Fenced("```md indent 0mm"), Step(Fenced("```md indent 3‸mm"), -1, semitone: false).After);
        var (least, kept) = Step(Fenced("```md indent 0‸mm"), -1, semitone: true);
        Assert.False(least.Fallback);
        Assert.Equal(Fenced("```md indent 0mm"), kept);
        Assert.Contains("already the least", least.Message);

        // The ceiling, on the paper the fence stands under: Up until it stays.
        foreach (string paper in new[] { "", "paper { size a5 }\n" })
            foreach (string shape in new[] { "", " box" })
            {
                string at = Fenced($"```md indent 30mm{shape}", paper: paper);
                string opening = "";
                for (int presses = 0; presses < 200; presses++)
                {
                    opening = at.Split('\n').First(l => l.StartsWith("```md"));
                    var (response, next) = Step(at.Replace("mm", "‸mm"), +1, semitone: presses % 2 == 0);
                    Assert.False(response.Fallback);
                    Assert.Empty(SaidOf(next));                 // never a width LYS4057 refuses
                    if (next == at)
                    {
                        Assert.Contains("already the most", response.Message);
                        Assert.Contains("at least", response.Message);
                        break;
                    }
                    at = next;
                }
                // It did stop, and where one more millimetre is refused.
                int reached = int.Parse(System.Text.RegularExpressions.Regex.Match(opening, @"(\d+)mm").Groups[1].Value);
                Assert.True(reached > 30, opening);
                Assert.Contains(SaidOf(at.Replace($"{reached}mm", $"{reached + 1}mm")), s => s.StartsWith("LYS4057"));
                // The large step from just under it takes the small steps that still fit.
                string under = at.Replace($"{reached}mm", $"{reached - 2}‸mm");
                Assert.Equal(at, Step(under, +1, semitone: false).After);
            }
    }

    [Fact]
    public void InAFencesBody_AndOnItsOtherWords_TheKeysDoWhatTheyDid()
    {
        // A number in the text is prose: no step, and the editor runs the key's own command.
        foreach (string marked in new[]
                 {
                     Fenced("```md indent 20mm", body: "Play 20‸mm from the edge."),
                     Fenced("```md indent 20mm", body: "indent 20‸mm"),
                     Fenced("```md indent 20mm", body: "in‸dent 20mm"),
                     Fenced("```md", body: "```md indent 20‸mm".Replace("```", "~~~")),
                     Fenced("```md bo‸x indent 20mm"),          // another word of the line
                     Fenced("```m‸d indent 20mm"),
                     Fenced("```md indent 20mm ‸ box"),         // between two words
                     Fenced("```md indent 20mm").Replace("```\n", "``‸`\n"),   // the closing fence
                 })
        {
            var (response, after) = Step(marked, +1, semitone: true);
            Assert.True(response.Fallback, marked);
            Assert.Empty(response.Edits);
            Assert.Equal(marked.Replace("‸", ""), after);
        }
        // A length that is none says why and changes nothing.
        var (bad, kept) = Step(Fenced("```md indent 20‸MM"), +1, semitone: true);
        Assert.False(bad.Fallback);
        Assert.Equal(Fenced("```md indent 20MM"), kept);
        Assert.Contains("case-sensitive", bad.Message);
        // A section's fence takes printings, not a shape.
        string inSection = "part m { clef treble }\nsection A {\n```md indent 20‸mm\nA note.\n```\n  m { c'1 | }\n}\nscore { staff m }\n";
        Assert.True(Step(inSection, +1, semitone: true).Response.Fallback);
    }

    // ---------------------------------------------------------------- a paper block's lengths

    /// <summary>A piece under a <c>paper { }</c> block of one entry.</summary>
    private static string Paper(string entry)
        => $"paper {{ {entry} }}\npart m {{ clef treble }}\nsection A {{ m {{ c'1 | }} }}\nscore {{ staff m }}\n";

    /// <summary>Owner's request 2026-11-13: "paper { margin xx } も動くようにして" — a length of a
    /// paper block steps as a fenced text's indent does: the same steps for each unit, the unit
    /// kept, a bare number as staff spaces.</summary>
    [Theory]
    [InlineData("leftMargin 15‸mm", +1, true, "leftMargin 16mm")]
    [InlineData("leftMargin ‸15mm", -1, true, "leftMargin 14mm")]
    [InlineData("leftMargin 15mm‸", +1, false, "leftMargin 20mm")]                // touching its end; PageUp
    [InlineData("rightMargin 15m‸m", -1, false, "rightMargin 10mm")]
    [InlineData("topMargin 12.5‸mm", +1, true, "topMargin 13.5mm")]
    [InlineData("bottomMargin 1‸cm", +1, true, "bottomMargin 1.1cm")]
    [InlineData("paperWidth 21‸cm", -1, false, "paperWidth 20.5cm")]
    [InlineData("paperHeight 11‸in", +1, true, "paperHeight 11.05in")]
    [InlineData("topMargin 0.5‸in", +1, false, "topMargin 0.75in")]
    // A bare number is staff spaces: a tenth and one.
    [InlineData("indent 5‸", +1, true, "indent 5.1")]
    [InlineData("indent 5‸", +1, false, "indent 6")]
    [InlineData("shortIndent 5.1‸", -1, true, "shortIndent 5")]
    [InlineData("spacingIncrement 1.2‸", +1, true, "spacingIncrement 1.3")]
    // A spacing block's lengths, each its own.
    [InlineData("systemSystemSpacing { basicDistance 12‸ padding 1 }", +1, false, "systemSystemSpacing { basicDistance 13 padding 1 }")]
    [InlineData("systemSystemSpacing { basicDistance 12 padding 1‸mm }", +1, true, "systemSystemSpacing { basicDistance 12 padding 2mm }")]
    [InlineData("staffStaffSpacing { minimumDistance ‸8 stretchability 60 }", -1, true, "staffStaffSpacing { minimumDistance 7.9 stretchability 60 }")]
    // A length written negative is stepped where it is; Up brings it back through 0.
    [InlineData("systemSystemSpacing { padding -‸1 }", +1, true, "systemSystemSpacing { padding -0.9 }")]
    [InlineData("systemSystemSpacing { padding ‸-1 }", -1, false, "systemSystemSpacing { padding -2 }")]
    [InlineData("systemSystemSpacing { padding -0.1‸ }", +1, true, "systemSystemSpacing { padding 0 }")]
    // staffSpace is a staff space, not a margin: a tenth of those steps.
    [InlineData("staffSpace 1.5‸mm", +1, true, "staffSpace 1.6mm")]
    [InlineData("staffSpace 1.5‸mm", +1, false, "staffSpace 2mm")]
    [InlineData("staffSpace 1.5‸mm", -1, true, "staffSpace 1.4mm")]
    [InlineData("staffSpace 0.06‸in", +1, true, "staffSpace 0.065in")]
    // A selection within the one length (a double-clicked number) steps it too.
    [InlineData("leftMargin «15»mm", +1, true, "leftMargin 16mm")]
    [InlineData("leftMargin «15mm»", -1, true, "leftMargin 14mm")]
    // On the key the entry's one length steps, as the word `tempo` steps its number.
    [InlineData("left‸Margin 15mm", +1, true, "leftMargin 16mm")]
    [InlineData("leftMargin‸ 15mm", -1, false, "leftMargin 10mm")]
    [InlineData("systemSystemSpacing { basic‸Distance 12 }", +1, true, "systemSystemSpacing { basicDistance 12.1 }")]
    [InlineData("system‸SystemSpacing { basicDistance 12 stretchability 60 }", +1, true, "systemSystemSpacing { basicDistance 12.1 stretchability 60 }")]
    public void OnAPapersLength_TheKeysStepIt_KeepingItsUnit(string marked, int direction, bool semitone, string after)
    {
        var (response, stepped) = Step(Paper(marked), direction, semitone: semitone);
        Assert.False(response.Fallback, response.Error);
        Assert.Equal(Paper(after), stepped);
        // One edit, and of the number alone: the unit is not rewritten.
        var edit = Assert.Single(response.Edits);
        string source = Paper(marked).Replace("‸", "").Replace("«", "").Replace("»", "");
        Assert.Matches(@"^-?[\d.]+$", source[edit.Start..edit.End]);
        Assert.Matches(@"^-?[\d.]+$", edit.NewText);
        Assert.StartsWith(after.Split(' ')[0], response.Message);
        Assert.Empty(SaidOf(stepped));
    }

    [Fact]
    public void APapersLength_SaysWhatItGives_AndStepsWhereverTheBlockStands()
    {
        Assert.Equal("leftMargin 16mm: the line 179 mm wide", Step(Paper("leftMargin 15‸mm"), +1, semitone: true).Response.Message);
        Assert.Equal("staffSpace 1.6mm: a staff 6.4 mm high", Step(Paper("staffSpace 1.5‸mm"), +1, semitone: true).Response.Message);
        Assert.Equal("systemSystemSpacing padding 1.1", Step(Paper("systemSystemSpacing { padding ‸1 }"), +1, semitone: true).Response.Message);

        // A score's own block, and a named one.
        const string Music = "part m { clef treble }\nsection A { m { c'1 | } }\n";
        string InScore(string entry) => $"{Music}score {{ paper {{ {entry} }} staff m }}\n";
        Assert.Equal(InScore("leftMargin 16mm"), Step(InScore("leftMargin 15‸mm"), +1, semitone: true).After);
        Assert.Equal(InScore("indent 4"), Step(InScore("in‸dent 5"), -1, semitone: false).After);
        string Named(string entry) => $"paper wide {{ {entry} }}\n{Music}score {{ paper wide staff m }}\n";
        Assert.Equal(Named("rightMargin 2.1cm"), Step(Named("rightMargin 2‸cm"), +1, semitone: true).After);
        Assert.Empty(SaidOf(Named("rightMargin 2.1cm")));
    }

    [Fact]
    public void APapersLength_StopsAtZero_AndBeforeAPageWithNoLine()
    {
        // The floor: the large step stops on it, and then the keys say so.
        Assert.Equal(Paper("leftMargin 0mm"), Step(Paper("leftMargin 3‸mm"), -1, semitone: false).After);
        var (least, kept) = Step(Paper("leftMargin 0‸mm"), -1, semitone: true);
        Assert.False(least.Fallback);
        Assert.Equal(Paper("leftMargin 0mm"), kept);
        Assert.Equal("leftMargin 0mm: already the least (0)", least.Message);

        // staffSpace takes a POSITIVE length (LYS9003): the keys stop a step above 0.
        Assert.Equal(Paper("staffSpace 0.1mm"), Step(Paper("staffSpace 0.3‸mm"), -1, semitone: false).After);
        var (thin, same) = Step(Paper("staffSpace 0.1‸mm"), -1, semitone: true);
        Assert.False(thin.Fallback);
        Assert.Equal(Paper("staffSpace 0.1mm"), same);
        Assert.Contains("already the least ('staffSpace' takes a positive length", thin.Message);
        Assert.Contains(SaidOf(Paper("staffSpace 0mm")), s => s.StartsWith("LYS9003"));

        // The ceiling: a margin stops while the page still has a line (a4 is 210 mm wide, the
        // other margin 15), a page's width while its margins still fit, and so its height.
        Assert.Equal(Paper("leftMargin 194mm"), Pressed(Paper("leftMargin 150‸mm"), +1, "already the most (the margins would leave the page no line)"));
        Assert.Equal(Paper("paperWidth 31mm"), Pressed(Paper("paperWidth 60‸mm"), -1, "already the least (the margins would leave the page no line)"));
        Assert.Equal(Paper("topMargin 286mm"), Pressed(Paper("topMargin 250‸mm"), +1, "already the most (the margins would leave the page no height)"));
        // The large step from just under it takes the small steps that still fit.
        Assert.Equal(Paper("leftMargin 194mm"), Step(Paper("leftMargin 192‸mm"), +1, semitone: false).After);
    }

    /// <summary>Presses the key (small and large steps in turn) until the text stays, which the
    /// message must say with <paramref name="saying"/>; no press may write a book that says anything.</summary>
    private static string Pressed(string marked, int direction, string saying)
    {
        string at = marked.Replace("‸", "");
        int caret = marked.IndexOf(Caret);
        for (int presses = 0; presses < 400; presses++)
        {
            var (response, next) = Step(at.Insert(caret, Caret.ToString()), direction, semitone: presses % 2 == 0);
            Assert.False(response.Fallback);
            Assert.Empty(SaidOf(next));
            if (next == at)
            {
                Assert.Contains(saying, response.Message);
                return at;
            }
            at = next;
        }
        Assert.Fail("the keys never stopped: " + at.Split('\n')[0]);
        return at;
    }

    /// <summary>A margin is not stepped into a page on which a fenced text's indents leave it
    /// less than its least width — what LYS4057 refuses on the fence.</summary>
    [Fact]
    public void APapersMargin_StopsBeforeTheWidthAFencedTextsIndentsNeed()
    {
        string Book(string margin) => Fenced("```md indent 60mm", paper: $"paper {{ leftMargin {margin} }}\n");
        string at = Pressed(Book("15‸mm"), +1, "already the most (a text's indents would leave");
        int reached = int.Parse(System.Text.RegularExpressions.Regex.Match(at, @"leftMargin (\d+)mm").Groups[1].Value);
        Assert.True(reached > 15 && reached < 194, at.Split('\n')[0]);
        Assert.Contains(SaidOf(Book($"{reached + 1}mm")), s => s.StartsWith("LYS4057"));
        // A book the validator already refuses is stepped all the same: back out of it, or on.
        Assert.Equal(Book($"{reached}mm"), Step(Book($"{reached + 1}‸mm"), -1, semitone: true).After);
        Assert.Equal(Book($"{reached + 2}mm"), Step(Book($"{reached + 1}‸mm"), +1, semitone: true).After);
    }

    [Fact]
    public void OnASpacingBlocksKey_SeveralLengthsAskForTheCaret_AndAKeyWithNoLengthSaysSo()
    {
        foreach (var (marked, saying) in new[]
                 {
                     ("system‸SystemSpacing { basicDistance 12 padding 1 }", "2 lengths - put the caret on the one to step"),
                     ("system‸SystemSpacing { stretchability 60 }", "write a length in it first"),
                     ("left‸Margin", "leftMargin: write its length first (leftMargin 15mm)"),
                     ("systemSystemSpacing { pad‸ding }", "systemSystemSpacing padding: write its length first"),
                     ("leftMargin 15‸MM", "'MM' is not a unit of this language"),
                     ("staffSpace 1.5‸", "write its unit"),
                 })
        {
            var (response, after) = Step(Paper(marked), +1, semitone: true);
            Assert.False(response.Fallback, marked);
            Assert.Empty(response.Edits);
            Assert.Equal(Paper(marked.Replace("‸", "")), after);
            Assert.Contains(saying, response.Message);
        }
    }

    /// <summary>What stepped in a paper block before still steps as it did — <c>size</c> walks the
    /// pages — and what is no length is left to the key's own command, as it was.</summary>
    [Fact]
    public void InAPaperBlock_TheSizeStillWalksThePages_AndWhatIsNoLengthIsLeftAlone()
    {
        var sizes = LilySharp.Core.Semantics.LanguageVocabulary.PaperSizeNames.ToList();
        int a4 = sizes.FindIndex(s => s == "a4");
        Assert.Equal(Paper($"size {sizes[a4 + 1]} leftMargin 15mm"), Step(Paper("size a‸4 leftMargin 15mm"), -1, semitone: true).After);
        foreach (string marked in new[]
                 {
                     "si‸ze a4", "systemsPerPage ‸4", "measuresPerSystem 4‸", "shortestDurationSpace ‸2.5", "ragged‸Right",
                     "systemSystemSpacing { stretchability ‸60 }", "systemSystemSpacing { stretch‸ability 60 }",
                     "systemSystemSpacing { padding 1 ‸}", "leftMargin 15mm ‸ rightMargin 15mm", "‸leftMarginn 15mm",
                 })
        {
            var (response, after) = Step(Paper(marked), +1, semitone: true);
            Assert.True(response.Fallback, marked);
            Assert.Equal(Paper(marked.Replace("‸", "")), after);
        }
        Assert.True(Step(Paper("leftMargin 15mm").Replace("paper", "pa‸per"), +1, semitone: true).Response.Fallback);
    }
}
