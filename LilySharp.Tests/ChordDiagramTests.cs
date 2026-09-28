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
using System.Text.RegularExpressions;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Music;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Chord diagrams (HANDOFF §2 K, owner's decisions 2026-09-28): a diagram draws ONLY where a
/// shape is written (<c>@chord(Cm7 x3x546)</c>, <c>F(133211 2010)</c>, <c>@chord(x32010)</c>),
/// in every score; on the tuning the score's <c>layout { chordDiagrams TUNING }</c> names, else
/// the instrument of the part (an <c>@chord</c>'s note, the staff a row stands over), else the
/// guitar — <c>none</c> draws nothing. The DEFAULT shape — LilyPond's predefined one
/// (<see cref="PredefinedFretboards"/>), else the first of Lily#'s order (<see cref="ChordVoicings"/>,
/// pinned by <c>ChordVoicingTests</c>) — is what the editor's step writes and its hover offers.
/// </summary>
[Trait("Category", "Unit")]
public class ChordDiagramTests
{
    // ================================================================ the predefined tables

    private static string Predefined(string symbol, TuningType tuning = TuningType.Guitar)
    {
        Assert.True(ChordStructure.TryParseChordEntry(symbol, out var chord), symbol);
        var shape = PredefinedFretboards.Find(Tunings.GetTuning(tuning), chord);
        Assert.NotNull(shape);
        return ChordVoicings.Spell(shape!.Frets);
    }

    /// <summary>What LilyPond 2.26.0 stored, per table — the guitar's 136 plus its 17 ninth
    /// chords, the ukulele's 306, the mandolin's 204.</summary>
    [Fact]
    public void TheTables_HoldWhatLilyPondStored()
    {
        Assert.Equal(153, PredefinedFretboards.Count(PredefinedFretboards.Table.Guitar));
        Assert.Equal(306, PredefinedFretboards.Count(PredefinedFretboards.Table.Ukulele));
        Assert.Equal(204, PredefinedFretboards.Count(PredefinedFretboards.Table.Mandolin));
    }

    [Theory]
    [InlineData("C", "x32010")]
    [InlineData("D", "xx0232")]
    [InlineData("A", "x02220")]
    [InlineData("Am", "x02210")]
    [InlineData("G", "320003")]
    [InlineData("Em", "022000")]
    [InlineData("B7", "x21202")]
    [InlineData("F", "133211")]        // chord-shape 'f — the barre shape
    [InlineData("Cm7", "x35343")]      // offset-fret 2 (chord-shape 'bes:m7)
    [InlineData("Cm", "x35543")]       // offset-fret 2 (chord-shape 'bes:m)
    [InlineData("C9", "x32333")]       // the ninth-chord file
    [InlineData("Db", "xx3121")]       // des — and cis, the same shape
    [InlineData("C#", "xx3121")]
    public void GuitarShapes_AreLilyPonds(string symbol, string frets)
        => Assert.Equal(frets, Predefined(symbol));

    [Theory]
    [InlineData("C", "0003")]
    [InlineData("F", "2010")]
    [InlineData("G", "0232")]
    [InlineData("Am", "2000")]
    public void UkuleleShapes_AreLilyPonds(string symbol, string frets)
        => Assert.Equal(frets, Predefined(symbol, TuningType.Ukulele));

    [Fact]
    public void MandolinShapes_AreFoundOnTheMandolinWord()
        => Assert.NotNull(PredefinedFretboards.Find(Tunings.GetTuning(Tunings.Parse("mandolin")),
            Parse("G")));

    /// <summary>The fingers and barres are kept (a later step may draw them): F is the full
    /// barre at the first fret, fingered 1 3 4 2 1 1.</summary>
    [Fact]
    public void FingersAndBarres_AreKept()
    {
        var f = PredefinedFretboards.Find(Tunings.Guitar, Parse("F"))!;
        Assert.Equal(new[] { 1, 3, 4, 2, 1, 1 }, f.Fingers);
        Assert.Equal(new PredefinedFretboards.Barre(6, 1, 1), Assert.Single(f.Barres));
        Assert.Equal("ly/predefined-guitar-fretboards.ly", f.File);
        Assert.Equal("ly/predefined-guitar-ninth-fretboards.ly",
            PredefinedFretboards.Find(Tunings.Guitar, Parse("C9"))!.File);
    }

    /// <summary>The one LilyPond quality no Lily# quality spells: the ukulele's <c>:m6-</c>
    /// (minor with a minor sixth), all 17 roots of it.</summary>
    [Fact]
    public void TheOnlyUnmappedQuality_IsTheUkulelesMinorFlatSix()
    {
        var unmapped = PredefinedFretboards.Unmapped();
        Assert.Equal(17, unmapped.Count);
        Assert.All(unmapped, u =>
        {
            Assert.Equal(PredefinedFretboards.Table.Ukulele, u.Table);
            Assert.Equal("0 3 7 8", u.Intervals);
        });
    }

    /// <summary>cis and des (and every enharmonic pair) are one pitch class here; LilyPond
    /// stores the same shape under both, so the collapse chooses nothing.</summary>
    [Fact]
    public void EnharmonicEntries_Agree()
        => Assert.Empty(PredefinedFretboards.CollapseConflicts());

    [Fact]
    public void ATableAppliesOnlyToItsOwnTuning()
    {
        Assert.Null(PredefinedFretboards.Find(Tunings.GetTuning(TuningType.GuitarDropD), Parse("C")));
        Assert.Null(PredefinedFretboards.Find(Tunings.Guitar, Parse("C/E")));      // no slash chords
        Assert.Null(PredefinedFretboards.Find(Tunings.Guitar, Parse("C13")));      // not in the table
    }

    private static ChordStructure Parse(string symbol)
    {
        Assert.True(ChordStructure.TryParseChordEntry(symbol, out var chord), symbol);
        return chord;
    }

    // ================================================================ which shape draws

    private static string? Chosen(string symbol, TuningType tuning, params string[] written)
        => ChordShapes.Drawn(tuning, written.Select(w =>
        {
            var parts = w.Split(' ');
            return parts.Length == 2 ? new WrittenShape(parts[0], parts[1]) : new WrittenShape(null, w);
        }).ToList())?.Spelled;

    private static string? DefaultShape(string symbol, TuningType tuning)
        => ChordShapes.Default(tuning, Parse(symbol))?.Spelled;

    /// <summary>Owner's decision 2026-09-28: only a WRITTEN shape draws. The default —
    /// LilyPond's predefined shape, else the first of Lily#'s order — is what the editor's step
    /// writes first; a name alone draws nothing.</summary>
    [Fact]
    public void OnlyAWrittenShapeDraws_TheDefaultIsPredefinedThenTheFirstOfTheOrder()
    {
        Assert.Equal("x3x546", Chosen("Cm7", TuningType.Guitar, "x3x546"));       // written
        Assert.Null(Chosen("Cm7", TuningType.Guitar));                            // a name alone
        Assert.Equal("x35343", DefaultShape("Cm7", TuningType.Guitar));           // predefined
        var first = ChordVoicings.Spell(ChordVoicings.For(Tunings.Guitar, Parse("Cmaj9"), includeStretch: false).Bases[0]);
        Assert.Equal(first, DefaultShape("Cmaj9", TuningType.Guitar));            // first of the order
        Assert.Equal(ShapeSource.FirstOfOrder, ChordShapes.Default(TuningType.Guitar, Parse("Cmaj9"))!.Source);
        // Drop D has no table: the default is the first of the order there.
        Assert.Equal(ChordVoicings.Spell(ChordVoicings.For(Tunings.GetTuning(TuningType.GuitarDropD), Parse("C"), includeStretch: false).Bases[0]),
            DefaultShape("C", TuningType.GuitarDropD));
    }

    /// <summary>An unnamed shape goes to the tuning with as many strings; a tuning word binds
    /// the next shape by name.</summary>
    [Fact]
    public void AShape_GoesToTheTuningWithItsStringCount()
    {
        Assert.Equal("133211", Chosen("F", TuningType.Guitar, "133211", "2010"));
        Assert.Equal("2010", Chosen("F", TuningType.Ukulele, "133211", "2010"));
        Assert.Equal("xx3211", Chosen("F", TuningType.Guitar, "guitar xx3211", "ukulele 2013"));
        Assert.Equal("2013", Chosen("F", TuningType.Ukulele, "guitar xx3211", "ukulele 2013"));
        // A guitar-named shape is not the drop-D guitar's; the unnamed six-string one is.
        Assert.Equal("x33211", Chosen("F", TuningType.GuitarDropD, "guitar 133211", "x33211"));
    }

    /// <summary>The ukulele has no enumeration (re-entrant): a chord its table lacks has no
    /// default there at all; a written shape still draws.</summary>
    [Fact]
    public void OnTheUkulele_TheDefaultIsOnlyTheTables()
    {
        Assert.Null(DefaultShape("C13", TuningType.Ukulele));
        Assert.Equal("0003", DefaultShape("C", TuningType.Ukulele));
        Assert.Equal("0000", Chosen("C13", TuningType.Ukulele, "0000"));
    }

    /// <summary>A fretted part's instrument is its tab's tuning; a piano, a voice and the bowed
    /// strings fret nothing (their diagrams take the guitar).</summary>
    [Theory]
    [InlineData("instrument ukulele", "ukulele")]
    [InlineData("instrument mandolin", "mandolin")]
    [InlineData("instrument bass", "bass")]
    [InlineData("instrument guitar", "guitar")]
    [InlineData("tuning guitardropd", "guitardropd")]
    [InlineData("instrument guitar  tuning ukulele", "ukulele")]
    [InlineData("instrument violin", null)]
    [InlineData("instrument piano", null)]
    [InlineData("clef treble", null)]
    public void APartsFrettedTuning_IsItsTabsTuning(string header, string? word)
    {
        var tree = SyntaxTree.Parse($"part pt {{ {header} }}\n");
        var part = tree.GetRoot().ChildNodes().OfType<PartDeclarationSyntax>().Single();
        Assert.Equal(word, PartHeaderDefaults.Read(part).FrettedTuningWord);
    }

    // ================================================================ the words

    private static ChordAnnotation Words(string argument)
        => ChordAnnotation.Parse(argument.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    [Fact]
    public void TheForms_ReadAsTheOwnerWroteThem()
    {
        var name = Words("Cm7");
        Assert.Equal("Cm7", name.Symbol);
        Assert.Empty(name.Shapes);
        Assert.Empty(name.Problems);
        Assert.Equal(new WrittenShape(null, "x3x546"), Assert.Single(Words("Cm7 x3x546").Shapes));
        Assert.Equal(new[] { new WrittenShape("guitar", "133211"), new WrittenShape("ukulele", "2010") },
            Words("F guitar 133211 ukulele 2010").Shapes);
        var alone = Words("x32010");
        Assert.True(alone.NamesFromDiagram);
        Assert.Null(alone.Symbol);
        Assert.True(ChordAnnotation.Parse([]).IsBare);
    }

    /// <summary>The voicing index and the <c>mute</c> words were never released and are gone:
    /// a digit word shorter than any tuning is a shape of the wrong length.</summary>
    [Theory]
    [InlineData("C 7", "no tuning has strings for")]
    [InlineData("Cm7 2", "no tuning has strings for")]
    [InlineData("D mute 5", "'mute' is neither a shape")]
    [InlineData("C m7", "'m7' is neither a shape")]
    [InlineData("C guitr x32010", "'guitr' is neither a shape")]
    [InlineData("C guitar", "a shape must follow it")]
    [InlineData("C guitar 0003", "'guitar' has 6 strings")]
    [InlineData("C x32010 x35553", "two shapes of 6 strings")]
    [InlineData("C guitar x32010 guitar x35553", "is given two shapes")]
    [InlineData("C x3a010", "is not a shape")]
    public void EachProblem_NamesTheFix(string argument, string fix)
        => Assert.Contains(fix, Words(argument).Problems[0].Message);

    // ================================================================ the layout key

    private static (LayoutPlan Plan, IReadOnlyList<LilySharp.Core.Syntax.Diagnostic> Problems) Layout(string entries)
    {
        var tree = SyntaxTree.Parse($"layout {{ {entries} }}\npart m {{ }}\nsection A {{ m {{ c'1 | }} }}\nform main {{ A }}\nscore main {{ staff m }}\n");
        var problems = SemanticValidation.Run(tree).Where(d => d.Code.StartsWith("LYS", StringComparison.Ordinal)
            && (d.Code == DiagnosticCodes.LayoutEntryBadValue || d.Code == DiagnosticCodes.UnknownLayoutKey)).ToList();
        return (LayoutPlanReader.Resolve(tree.GetRoot(), TopLevelNodes.OfRoot<RenderDeclarationSyntax>(tree.GetRoot()).First()), problems);
    }

    [Theory]
    [InlineData("chordDiagrams guitar", "guitar")]
    [InlineData("chordDiagrams ukulele", "ukulele")]
    [InlineData("chordDiagrams mandolin", "mandolin")]
    [InlineData("chordDiagrams guitardropd", "guitardropd")]
    [InlineData("chordDiagrams bass5", "bass5")]
    [InlineData("chordDiagrams none", "none")]
    [InlineData("", null)]
    public void TheKey_TakesNoneOrATuningWord(string entries, string? word)
    {
        var (plan, problems) = Layout(entries);
        Assert.Empty(problems);
        Assert.Equal(word, plan.ChordDiagrams);
    }

    /// <summary>Which tuning a diagram draws on (owner's decision 2026-09-28): the layout's
    /// word, else the part's fretted instrument, else the guitar; <c>none</c> draws none.</summary>
    [Fact]
    public void TheTuning_IsTheLayoutsThenThePartsThenTheGuitar()
    {
        Assert.Equal(TuningType.Ukulele, ChordDiagramsKey.Resolve("ukulele", TuningType.Bass));
        Assert.Equal(TuningType.Bass, ChordDiagramsKey.Resolve(null, TuningType.Bass));
        Assert.Equal(TuningType.Guitar, ChordDiagramsKey.Resolve(null, null));
        Assert.Null(ChordDiagramsKey.Resolve("none", TuningType.Ukulele));
    }

    [Theory]
    [InlineData("chordDiagrams banjo", "is not a value of 'chordDiagrams'")]
    [InlineData("chordDiagrams Guitar", "is not a value of 'chordDiagrams'")]
    [InlineData("ChordDiagrams guitar", "Keys are case-sensitive: write 'chordDiagrams'")]
    [InlineData("chordDiagrams", "takes none or a tuning name")]
    [InlineData("chordDiagrams guitar ukulele", "one word")]
    public void AWrongWord_IsRefused(string entries, string message)
    {
        var (plan, problems) = Layout(entries);
        Assert.Contains(message, Assert.Single(problems).Message);
        Assert.Null(plan.ChordDiagrams);
    }

    // ================================================================ where diagrams draw

    private const string Guitar = "layout { chordDiagrams guitar }\n";
    private const string Ukulele = "layout { chordDiagrams ukulele }\n";
    private const string NoDiagrams = "layout { chordDiagrams none }\n";

    /// <summary>A song with a chords row above a staff whose part frets nothing (so its
    /// diagrams take the guitar), and a ukulele part <c>uk</c>; the layout decides the rest.</summary>
    private static string Song(string layout, string row, string music = "c'1 | c'1 | c'1 |",
        string score = "chords prog  staff gt") => $$"""
        octave absolute
        {{layout}}part gt { clef treble }
        part uk { instrument ukulele }
        section A {
          gt { {{music}} }
          uk { {{music}} }
          chords prog { {{row}} }
        }
        form main { A }
        score main { {{score}} }
        """;

    /// <summary>C, F and G with their guitar shapes written.</summary>
    private const string WrittenRow = "C(x32010) | F(133211) | G(320003) |";

    private static Core.Svg.Model.MultiStaffScore Collected(string book)
    {
        var tree = SyntaxTree.Parse(book);
        return new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
    }

    private static ScoreLayout Laid(string book)
    {
        var tree = SyntaxTree.Parse(book);
        return new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
    }

    private static string?[] RowFrames(string book)
        => [.. Collected(book).ChordNames.OrderBy(c => c.MeasureIndex).ThenBy(c => c.Timing.ToDouble())
            .Select(c => c.FrameSpec)];

    /// <summary>Owner's decision 2026-09-28: a row entry draws a diagram only where it WRITES a
    /// shape — in every score, whatever its layout says; a name alone draws none.</summary>
    [Fact]
    public void ARow_DrawsOnlyTheShapesItWrites()
    {
        Assert.Equal(new string?[] { null, null, null }, RowFrames(Song(Guitar, "C | F | G |")));
        Assert.Equal(new string?[] { null, null, null }, RowFrames(Song("", "C | F | G |")));
        Assert.Equal(new string?[] { null, "133211", null }, RowFrames(Song(Guitar, "C | F(133211) | G |")));
        Assert.Equal(new string?[] { "0003", null, null }, RowFrames(Song(Ukulele, "C(0003) | F(133211) | G |")));
    }

    /// <summary>With no <c>chordDiagrams</c> a written shape draws on the part's instrument,
    /// else the guitar; <c>chordDiagrams none</c> turns even written shapes off (a piano score
    /// from the same source).</summary>
    [Fact]
    public void AWrittenShape_WithNoLayout_DrawsOnTheGuitar_AndNoneTurnsItOff()
    {
        Assert.Equal(new string?[] { "133211" }, RowFrames(Song("", "F(133211) |", "c'1 |")));
        Assert.Equal(new string?[] { null }, RowFrames(Song(NoDiagrams, "F(133211) |", "c'1 |")));
    }

    /// <summary>A row draws on the instrument of the staff it stands directly above (a leading
    /// row, or an interior one the score folds into that staff): over a ukulele staff four
    /// strings with no layout; a lead-sheet row, or one below the last staff, takes the guitar;
    /// the layout's word wins over the staff's instrument.</summary>
    [Fact]
    public void ARow_DrawsOnTheInstrumentOfTheStaffItStandsOver()
    {
        const string row = "C(x32010 0003) |";
        Assert.Equal(new string?[] { "0003" }, RowFrames(Song("", row, "c'1 |", "chords prog  staff uk")));
        // A row BELOW the staff stands over nothing: the guitar.
        Assert.Equal(new string?[] { "x32010" }, RowFrames(Song("", row, "c'1 |", "staff uk  chords prog")));
        Assert.Equal(new string?[] { "0003" }, RowFrames(Song("", row, "c'1 |", "staff gt  chords prog  staff uk")));
        Assert.Equal(new string?[] { "x32010" }, RowFrames(Song("", row, "c'1 |", "chords prog  staff gt")));
        Assert.Equal(new string?[] { "x32010" }, RowFrames(Song("", row, "c'1 |", "chords prog")));
        Assert.Equal(new string?[] { "x32010" }, RowFrames(Song(Guitar, row, "c'1 |", "chords prog  staff uk")));
        Assert.Equal(new string?[] { null }, RowFrames(Song(NoDiagrams, row, "c'1 |", "chords prog  staff uk")));
    }

    [Fact]
    public void TheRowForms_RouteByStringCount()
    {
        const string row = "F(xx3211 2013) | F(guitar x33211 ukulele 5553) | F/A(x03211) G . |";
        Assert.Equal(new string?[] { "xx3211", "x33211", "x03211", null }, RowFrames(Song(Guitar, row)));
        Assert.Equal(new string?[] { "2013", "5553", null, null }, RowFrames(Song(Ukulele, row)));
    }

    [Fact]
    public void TheRowGrammar_StillReadsHoldsRestsAndDegrees()
    {
        var tree = SyntaxTree.Parse(Song(Guitar, "C(x32010) . G | r | IV(133211) | Am7/G . |"));
        Assert.DoesNotContain(tree.Diagnostics, d => d.Severity == LilySharp.Core.Syntax.DiagnosticSeverity.Error);
        var names = Collected(tree.Text).ChordNames.OrderBy(c => c.MeasureIndex).ThenBy(c => c.Timing.ToDouble()).ToList();
        Assert.Equal(new[] { "C", "G", "N.C.", "F", "Am7/G" }, names.Select(c => c.ChordText));
        // IV in C is F, with its shape written; G and Am7/G write none, so draw none.
        Assert.Equal(new string?[] { "x32010", null, null, "133211" }, names.Take(4).Select(c => c.FrameSpec));
        Assert.Null(names[4].FrameSpec);
        // The entry's symbol stops at its '(' — the name is the symbol's, not the shape's.
        var entry = tree.GetRoot().DescendantNodes().OfType<ChordEntrySyntax>().First();
        Assert.Equal("C", entry.SymbolText);
        Assert.Equal("x32010", Assert.Single(entry.ShapeWords).Text);
        Assert.Equal(tree.Text, tree.GetRoot().ToFullString());
    }

    [Fact]
    public void AnAtChord_DrawsOnlyTheShapeItWrites()
    {
        string With(string layout) => layout + """
            octave absolute
            part gt { clef treble }
            section A { gt { c'4@chord(Cm7) c'4@chord(Cm7 x3x546) c'4@chord(x32010) c'4@diagram(xx0232) | } }
            form main { A }
            score main { staff gt }
            """;
        // The name alone draws none; with no layout the written shapes draw on the guitar.
        foreach (string layout in new[] { Guitar, "" })
            Assert.Equal(new[] { "frame:x3x546", "frame:x32010", "frame:xx0232" },
                Laid(With(layout)).ArticulationLayouts.OrderBy(a => a.X).Select(a => a.Glyph));
        // chordDiagrams none: @diagram alone draws; the names still print, x32010 named C.
        var plain = Laid(With(NoDiagrams));
        Assert.Equal("frame:xx0232", Assert.Single(plain.ArticulationLayouts).Glyph);
        Assert.Equal(new[] { "Cm7", "Cm7", "C" }, plain.ChordNameLayouts.OrderBy(c => c.X).Select(c => c.ChordText));
    }

    /// <summary>An <c>@chord</c> draws on the instrument of the part whose note carries it: on a
    /// ukulele part a four-string diagram with no layout; the layout's word wins.</summary>
    [Fact]
    public void AnAtChordOnAUkulelePart_DrawsFourStrings()
    {
        string With(string layout) => layout + """
            octave absolute
            part uk { instrument ukulele }
            section A { uk { c'1@chord(C x32010 0003) | } }
            form main { A }
            score main { staff uk }
            """;
        Assert.Equal("frame:0003", Assert.Single(Laid(With("")).ArticulationLayouts).Glyph);
        Assert.Equal("frame:x32010", Assert.Single(Laid(With(Guitar)).ArticulationLayouts).Glyph);
        Assert.Empty(Laid(With(NoDiagrams)).ArticulationLayouts);
    }

    /// <summary>A bare <c>@chord</c> names its notes and writes no shape: no diagram.</summary>
    [Fact]
    public void ABareAtChord_DrawsNoDiagram()
    {
        const string book = Guitar + """
            octave absolute
            part gt { clef treble }
            section A { gt { <c' e' g'>1@chord | } }
            form main { A }
            score main { staff gt }
            """;
        var lay = Laid(book);
        Assert.Empty(lay.ArticulationLayouts);
        Assert.Equal("C", Assert.Single(lay.ChordNameLayouts).ChordText);
    }

    // ================================================================ no collisions

    private static readonly LilySharp.Core.Rendering.ScoreTextMetrics Fonts =
        LilySharp.Core.Rendering.ScoreTextMetrics.Bundled;

    /// <summary>
    /// Under a row placed over a staff: every diagram stands 0.5 under the names' ink
    /// (ChordNames nonstaff-nonstaff-spacing), on one level; neighbours never overlap.
    /// </summary>
    [Fact]
    public void RowDiagrams_StandBetweenTheNamesAndTheStaff_SideBySide()
    {
        var lay = Laid(Song(Guitar,
            "C(x32010) F(133211) G(320003) Am(x02210) | Dm7(xx0211) G7(320001) C(x32010) . |",
            "c'4 d' e' f' | g' a' b' c'' |"));
        var names = lay.ChordNameLayouts.OrderBy(c => c.MeasureIndex).ThenBy(c => c.X).ToList();
        Assert.Equal(7, names.Count);
        Assert.All(names, n => Assert.NotNull(n.FrameSpec));
        // One level for the line (the two bars stand on one system).
        Assert.Single(names.Select(n => Math.Round(n.YUp + n.FrameBottom, 6)).Distinct());
        foreach (var n in names)
        {
            var box = ChordNameEngraver.DiagramBox(Fonts, n.FrameSpec!);
            double inkBottom = ChordNameEngraver.SymbolInk(Fonts, n).Bottom;
            Assert.True(n.FrameBottom + box.Top <= inkBottom - 0.5 + 1e-6, $"{n.ChordText}: diagram into its name");
        }
        for (int i = 0; i + 1 < names.Count; i++)
        {
            double right = names[i].X + ChordNameEngraver.DiagramBox(Fonts, names[i].FrameSpec!).Width;
            Assert.True(names[i + 1].X >= right - 1e-6, $"{names[i].ChordText}/{names[i + 1].ChordText} overlap");
        }
    }

    /// <summary>The diagrams push the row up: the same row with its diagrams stands higher
    /// over the staff by (about) a diagram's height than with <c>chordDiagrams none</c>.</summary>
    [Fact]
    public void TheRowsSpacing_AccountsForTheDiagrams()
    {
        double Rise(string layout)
        {
            string book = Song(layout, WrittenRow);
            string svg = SvgGenerator.Generate(SyntaxTree.Parse(book));
            return StaffTop(svg) - NameBaseline(svg, book.IndexOf("C(x32010)", StringComparison.Ordinal));
        }
        double lift = Rise(Guitar) - Rise(NoDiagrams);
        double height = ChordNameEngraver.DiagramBox(Fonts, "x32010").Height;
        Assert.True(lift >= height, $"the names rose {lift:0.00}, a diagram is {height:0.00} tall");
    }

    /// <summary>The page's staff top line (device Y): the highest long horizontal line.</summary>
    private static double StaffTop(string svg)
        => Regex.Matches(svg, "<line x1=\"([-0-9.]+)\" y1=\"([-0-9.]+)\" x2=\"([-0-9.]+)\" y2=\"([-0-9.]+)\"")
            .Where(m => m.Groups[2].Value == m.Groups[4].Value
                        && Num(m.Groups[3].Value) - Num(m.Groups[1].Value) > 20)
            .Min(m => Num(m.Groups[2].Value));

    private static double NameBaseline(string svg, int dataPos)
        => Num(Regex.Match(svg, $"<text x=\"[-0-9.]+\" y=\"([-0-9.]+)\"[^>]*sans-serif[^>]*data-pos=\"{dataPos}\"").Groups[1].Value);

    private static double Num(string s) => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>On the drawn page: every diagram's lowest ink stands at least the padding
    /// over the staff's top line, and under its name's baseline.</summary>
    [Fact]
    public void OnThePage_TheDiagramIsBetweenItsNameAndTheStaff()
    {
        string book = Song(Guitar, WrittenRow, "c'1 | a''1 | c'1 |");
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(book));
        double staffTop = StaffTop(svg);
        foreach (string entry in new[] { "C(x32010) |", "F(133211) |", "G(320003) |" })
        {
            int pos = book.IndexOf(entry, StringComparison.Ordinal);
            var ys = Regex.Matches(svg, $"<(?:line|circle)[^>]*data-pos=\"{pos}\"[^>]*>")
                .SelectMany(m => Regex.Matches(m.Value, " (?:y1|y2|cy)=\"([-0-9.]+)\"").Select(g => Num(g.Groups[1].Value)))
                .ToList();
            Assert.NotEmpty(ys);
            Assert.True(ys.Max() <= staffTop - 0.5 + 0.05, $"{entry}: diagram bottom {ys.Max():0.00}, staff top {staffTop:0.00}");
            Assert.True(ys.Min() > NameBaseline(svg, pos), $"{entry}: diagram over its name");
        }
    }

    /// <summary>A lead sheet with no staff: the diagrams (on the guitar, with no layout) hang
    /// under the row's names and are part of the page's ink.</summary>
    [Fact]
    public void ALeadSheetRow_DrawsItsDiagrams()
    {
        string book = Song("", WrittenRow, score: "chords prog");
        var lay = Laid(book);
        Assert.Equal(3, lay.ChordNameLayouts.Count(c => c.FrameSpec != null));
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(book));
        string plain = SvgGenerator.Generate(SyntaxTree.Parse(Song(NoDiagrams, WrittenRow, score: "chords prog")));
        Assert.True(Regex.Matches(svg, "<circle").Count > Regex.Matches(plain, "<circle").Count);
    }

    /// <summary>The row's diagram is widened for: a bar of four quarter-note chords is wider
    /// with diagrams than without.</summary>
    [Fact]
    public void TheBars_WidenForTheDiagrams()
    {
        double Width(string layout)
        {
            var lay = Laid(Song(layout, "C(x32010) F(133211) G(320003) Am(x02210) |", "c'4 d' e' f' |"));
            return lay.Systems[0].Measures[0].Width;
        }
        Assert.True(Width(Guitar) > Width(NoDiagrams) + 1.0);
    }

    // ================================================================ diagnostics

    private static IReadOnlyList<LilySharp.Core.Syntax.Diagnostic> Warnings(string book)
        => SemanticValidation.Run(SyntaxTree.Parse(book))
            .Where(d => d.Code == DiagnosticCodes.ChordDiagramNotDrawn).ToList();

    [Fact]
    public void RowShapes_WarnAtTheWord()
    {
        string book = Song(Guitar, "C(x3201) | F(133211 xx3211) | G(guitr 320003) |");
        var w = Warnings(book);
        Assert.Equal(2, w.Count);
        Assert.Contains(w, d => d.Message.Contains("two shapes of 6 strings"));
        Assert.Contains(w, d => d.Message.Contains("'guitr' is neither"));
        // x3201 is five strings: a bass5 or a banjo's, not this score's — silently unused.
        Assert.DoesNotContain(w, d => d.Message.Contains("'x3201'"));
        var two = w.First(d => d.Message.Contains("two shapes"));
        Assert.Equal("xx3211", book.Substring(two.Span.Start, two.Span.Length));
    }

    /// <summary>A name with no shape written draws no diagram by design (owner's decision
    /// 2026-09-28), so there is nothing to warn about — not even a chord the tuning has no
    /// default for (C13 on the ukulele). 9cf95fab warned "no chord diagram on 'ukulele'".</summary>
    [Fact]
    public void ANameWithNoShape_IsNotWarned()
    {
        Assert.Empty(Warnings(Song(Ukulele, "C13 | C13 | G |")));
        Assert.Empty(Warnings(Song("", "C13 | G |")));
        Assert.Empty(Warnings(Song(Ukulele, "C13(0000) | G |")));
    }

    [Fact]
    public void AnAtChordsWords_WarnWithOrWithoutTheSwitch()
    {
        string Book(string layout, string music) => layout + $$"""
            octave absolute
            part gt { clef treble }
            section A { gt { {{music}} } }
            form main { A }
            score main { staff gt }
            """;
        Assert.Contains("no tuning has strings for", Assert.Single(Warnings(Book("", "c'1@chord(C 7) |"))).Message);
        Assert.Contains("'mute' is neither", Assert.Single(Warnings(Book("", "c'1@chord(D mute 5) |"))).Message);
        Assert.Contains("name no chord Lily# knows", Assert.Single(Warnings(Book("", "c'1@chord(x0x00x) |"))).Message);
        Assert.Empty(Warnings(Book(Guitar, "c'4@chord(Cm7) c'4@chord(Cm7 x3x546) c'4@chord(x32010) <c' e' g'>4@chord |")));
        // No shape written: nothing drawn, nothing said.
        Assert.Empty(Warnings(Book(Ukulele, "c'2@chord(C13) c'2@chord(C13) |")));
    }

    // ================================================================ the exporters

    private static string Twin(string book) => new LilyPondExporter().Export(SyntaxTree.Parse(book));

    /// <summary>The FretBoards track of a row: each written shape through a one-shape table,
    /// every unwritten chord a silent slot (LilyPond would compute a diagram for it).</summary>
    private static string FretTrack(string ly)
        => ly.Substring(ly.IndexOf("progFrets = ", StringComparison.Ordinal)).Split("\\score")[0];

    [Fact]
    public void TheTwin_WritesAFretBoardsContextUnderTheRow_ForTheWrittenShapesOnly()
    {
        string ly = Twin(Song(Guitar, "C | F(xx3211) | Cmaj9 | C13 |"));
        Assert.Matches(@"\\new ChordNames \\\w+\s+\\new FretBoards \\\w+Frets", ly);
        Assert.Contains("\\storePredefinedDiagram #lysFretsA \\chordmode { f } #guitar-tuning \"x;x;3;2;1;1;\"", ly);
        Assert.Contains("\\once \\set predefinedDiagramTable = #lysFretsA f1", ly);
        // C, Cmaj9 and C13 write no shape: silent slots, and no LilyPond table is relied on.
        Assert.Equal(3, Regex.Matches(FretTrack(ly), @"\bs1\b").Count);
        Assert.DoesNotContain("c1:maj9", FretTrack(ly));
        Assert.DoesNotContain("\\include \"predefined-", ly);
        // No entry writes a shape: no FretBoards context at all, whatever the layout.
        Assert.DoesNotContain("FretBoards", Twin(Song(Guitar, "C | F |")));
        // No layout: the written shape draws on the guitar (FretBoards' own default);
        // chordDiagrams none: nothing.
        Assert.Contains("\\new FretBoards \\progFrets", Twin(Song("", "F(xx3211) |", "c'1 |")));
        Assert.DoesNotContain("FretBoards", Twin(Song(NoDiagrams, "F(xx3211) |", "c'1 |")));
    }

    [Fact]
    public void TheTwin_TellsFretBoardsTheTuning_AndOmitsWhatThePageDoesNotDraw()
    {
        string ly = Twin(Song(Ukulele, "C(0003) | F | C13 |"));
        Assert.Contains("\\new FretBoards \\with { stringTunings = #ukulele-tuning }", ly);
        Assert.Contains("#ukulele-tuning \"o;o;o;3;\"", ly);
        Assert.Matches(@"progFrets = \\chordmode \{[^}]*\bs1 \|", ly);
        Assert.DoesNotContain("c1:13", FretTrack(ly));
        // A row over a ukulele staff, with no layout, draws on the ukulele too.
        Assert.Contains("stringTunings = #ukulele-tuning",
            Twin(Song("", "C(x32010 0003) |", "c'1 |", "chords prog  staff uk")));
    }

    [Fact]
    public void TheTwin_WritesAnAtChordsWrittenShapeOnTheNote()
    {
        string Book(string layout, string part = "gt { clef treble }") => layout + $$"""
            octave absolute
            part {{part}}
            section A { gt { c'2@chord(Cm7) c'2@chord(C x32010 0003) | } }
            form main { A }
            score main { staff gt }
            """;
        string ly = Twin(Book(Guitar));
        Assert.Contains("^\\markup \\fret-diagram-terse \"x;3;2;o;1;o;\"", ly);
        Assert.DoesNotContain("x;3;5;3;4;3;", ly);                  // the name alone: none
        Assert.Contains("\\fret-diagram-terse \"x;3;2;o;1;o;\"", Twin(Book("")));
        Assert.Contains("\\fret-diagram-terse \"o;o;o;3;\"", Twin(Book("", "gt { instrument ukulele }")));
        Assert.DoesNotContain("fret-diagram", Twin(Book(NoDiagrams)));
    }

    private static string Xml(string book) =>
        new MusicXmlExporter().Export(SyntaxTree.Parse(book)).ToXml().ToString();

    [Fact]
    public void MusicXml_NestsTheWrittenShapeInTheHarmony()
    {
        string Book(string layout, string chord = "Cm7 x35343", string part = "clef treble") => layout + $$"""
            octave absolute
            part gt { {{part}} }
            section A { gt { c'1@chord({{chord}}) | } }
            form main { A }
            score main { staff gt }
            """;
        foreach (string layout in new[] { Guitar, "" })
        {
            var harmony = Regex.Match(Xml(Book(layout)), "<harmony>.*?</harmony>", RegexOptions.Singleline).Value;
            Assert.Contains("<kind>minor-seventh</kind>", harmony);
            Assert.Contains("<frame-strings>6</frame-strings>", harmony);
            Assert.Contains("<first-fret>3</first-fret>", harmony);
            Assert.Equal(5, Regex.Matches(harmony, "<frame-note>").Count);   // x35343
        }
        // The name alone, or chordDiagrams none: a harmony with no frame.
        Assert.DoesNotContain("<frame>", Xml(Book(Guitar, "Cm7")));
        Assert.DoesNotContain("<frame>", Xml(Book(NoDiagrams)));
        Assert.Contains("<harmony>", Xml(Book(NoDiagrams)));
        // On a ukulele part the four-string shape.
        Assert.Contains("<frame-strings>4</frame-strings>", Xml(Book("", "C x32010 0003", "instrument ukulele")));
    }

    // ================================================================ the editor

    private static string? HoverAt(string doc, string needle, int into = 2)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new Uri("file:///chord-diagram.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = doc, Version = 1, LanguageId = "lilysharp" },
        });
        int at = doc.IndexOf(needle, StringComparison.Ordinal) + into;
        int line = doc[..at].Count(ch => ch == '\n');
        int col = at - (doc.LastIndexOf('\n', at - 1) + 1);
        return server.Hover(new TextDocumentPositionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(line, col),
        })?.Contents.Value;
    }

    [Fact]
    public void Hover_ShowsTheWrittenShapePerTuning()
    {
        string doc = """
            layout gtr { chordDiagrams guitar }
            layout uke { chordDiagrams ukulele }
            octave absolute
            part gt { clef treble }
            section A {
              gt { c'2@chord(C 0003) c'2@chord(Cm7 x3x546) | }
              chords prog { F(xx3211) | }
            }
            form main { A }
            score g { layout gtr  chords prog  staff gt }
            score u { layout uke  chords prog  staff gt }
            """;
        string? c = HoverAt(doc, "@chord(C ");
        Assert.NotNull(c);
        Assert.Contains("guitar: no diagram", c);
        Assert.Contains("ukulele: `0003` (written)", c);
        string? cm7 = HoverAt(doc, "@chord(Cm7");
        Assert.Contains("guitar: `x3x546` (written)", cm7);
        Assert.Contains("ukulele: no diagram", cm7);
        string? f = HoverAt(doc, "F(xx3211)", 0);
        Assert.Contains("guitar: `xx3211` (written)", f);
        Assert.Contains("ukulele: no diagram", f);
    }

    /// <summary>Discoverability (owner's decision 2026-09-28): with no shape written the hover
    /// says how to add a diagram and which it would be — on the tuning the step writes on (the
    /// part's instrument with no layout).</summary>
    [Fact]
    public void Hover_WithNoShape_SaysHowToAddADiagram()
    {
        string doc = """
            octave absolute
            part gt { clef treble }
            part uk { instrument ukulele }
            section A {
              gt { c'1@chord(G) | }
              uk { c'1@chord(C) | }
              chords prog { G | }
            }
            form main { A }
            score main { chords prog  staff gt  staff uk }
            """;
        Assert.Equal("Ctrl+Shift+↑ adds a chord diagram (guitar: 320003)", HoverAt(doc, "@chord(G)"));
        Assert.Equal("Ctrl+Shift+↑ adds a chord diagram (ukulele: 0003)", HoverAt(doc, "@chord(C)"));
        Assert.Contains("Ctrl+Shift+↑ adds a chord diagram (guitar: 320003)", HoverAt(doc, "G |", 0));
    }

    /// <summary>
    /// The first tuning's line also says where the shape stands in the editor's order, with
    /// both counts — a hover carries no setting (owner's decision 2026-09-28,
    /// <c>lilysharp.chordShapes.includeStretch</c>); a stretch shape counts in the wider order.
    /// </summary>
    [Fact]
    public void Hover_SaysWhereTheShapeStands_WithAndWithoutStretch()
    {
        string doc = """
            layout gtr { chordDiagrams guitar }
            octave absolute
            part gt { clef treble }
            section A { gt { c'2@chord(Cm7 x35343) c'2@chord(Cm7 8xx546) | } }
            form main { A }
            score g { layout gtr  staff gt }
            """;
        var tree = SyntaxTree.Parse(doc);
        var mark = tree.GetNodes<MusicMarkSyntax>().First();
        var chord = ChordAnnotation.Of(mark)!.Structure!;
        int normal = LilySharp.Core.Editing.NoteStepper.ShapeOrder(mark, chord, false).Order.Count;
        var wide = LilySharp.Core.Editing.NoteStepper.ShapeOrder(mark, chord, true).Order
            .Select(o => ChordVoicings.Spell(o)).ToList();
        Assert.True(wide.Count > normal);
        Assert.Contains($"guitar: `x35343` (written) — shape 1 of {normal} ({wide.Count} with stretch)",
            HoverAt(doc, "@chord(Cm7 x"));
        Assert.Contains($"guitar: `8xx546` (written) — stretch shape {wide.IndexOf("8xx546") + 1} of {wide.Count}",
            HoverAt(doc, "@chord(Cm7 8"));
    }

    [Fact]
    public void Hover_UnderChordDiagramsNone_SaysAShapeIsNotDrawn()
    {
        string doc = """
            layout { chordDiagrams none }
            octave absolute
            part gt { clef treble }
            section A { gt { c'1@chord(Cm7 x3x546) | } }
            form main { A }
            score main { staff gt }
            """;
        Assert.Contains("`chordDiagrams none`", HoverAt(doc, "@chord(Cm7"));
    }

    // ================================================================ chordDiagrams … all
    // Owner's decision 2026-09-28: the scope word `all` (after the tuning, or alone) makes EVERY
    // chord name draw a diagram — the written shape, else ChordShapes.Default — and a chord with
    // no shape at all on the tuning is warned once per symbol and tuning. Without it, nothing moves.

    private const string All = "layout { chordDiagrams all }\n";
    private const string GuitarAll = "layout { chordDiagrams guitar all }\n";
    private const string UkuleleAll = "layout { chordDiagrams ukulele all }\n";

    [Theory]
    [InlineData("chordDiagrams all", null, true)]
    [InlineData("chordDiagrams guitar all", "guitar", true)]
    [InlineData("chordDiagrams ukulele all", "ukulele", true)]
    [InlineData("chordDiagrams guitardropd all", "guitardropd", true)]
    [InlineData("chordDiagrams guitar", "guitar", false)]
    [InlineData("chordDiagrams none", "none", false)]
    [InlineData("", null, false)]
    public void TheKey_TakesAllAloneOrAfterTheTuning(string entries, string? word, bool all)
    {
        var (plan, problems) = Layout(entries);
        Assert.Empty(problems);
        Assert.Equal(word, plan.ChordDiagrams);
        Assert.Equal(all, plan.ChordDiagramsAll);
    }

    [Theory]
    [InlineData("chordDiagrams none all", "'none' draws no diagram, so it takes no 'all'")]
    [InlineData("chordDiagrams all guitar", "the tuning comes first: write 'chordDiagrams guitar all'")]
    [InlineData("chordDiagrams all all", "'all' is written twice")]
    [InlineData("chordDiagrams guitar all all", "'all' is written twice")]
    [InlineData("chordDiagrams guitar guitar", "'guitar' is written twice")]
    [InlineData("chordDiagrams guitar alll", "after the tuning only 'all' may follow")]
    [InlineData("chordDiagrams guitar All", "Values are case-sensitive: write 'all'")]
    [InlineData("chordDiagrams ALL", "Values are case-sensitive: write 'all'")]
    [InlineData("chordDiagrams Guitar all", "Values are case-sensitive: write 'guitar'")]
    [InlineData("chordDiagrams all x", "'all' takes nothing after it")]
    [InlineData("chordDiagrams none guitar", "'none' takes nothing after it")]
    public void AWrongAll_IsRefused_NamingTheFix(string entries, string message)
    {
        var (plan, problems) = Layout(entries);
        Assert.Contains(message, Assert.Single(problems).Message);
        Assert.False(plan.ChordDiagramsAll);
        Assert.Null(plan.ChordDiagrams);
    }

    /// <summary>A key written again is written whole: a score's override <c>chordDiagrams
    /// guitar</c> drops the named block's <c>all</c>.</summary>
    [Fact]
    public void AnOverride_WithoutAll_DropsTheNamedBlocksAll()
    {
        const string book = """
            layout every { chordDiagrams ukulele all }
            part m { }
            section A { m { c'1 | } }
            form main { A }
            score a { layout every  staff m }
            score b { layout every { chordDiagrams guitar }  staff m }
            """;
        var tree = SyntaxTree.Parse(book);
        var renders = TopLevelNodes.OfRoot<RenderDeclarationSyntax>(tree.GetRoot()).ToList();
        var a = LayoutPlanReader.Resolve(tree.GetRoot(), renders[0]);
        var b = LayoutPlanReader.Resolve(tree.GetRoot(), renders[1]);
        Assert.Equal(("ukulele", true), (a.ChordDiagrams, a.ChordDiagramsAll));
        Assert.Equal(("guitar", false), (b.ChordDiagrams, b.ChordDiagramsAll));
    }

    /// <summary>In an <c>all</c> score every row entry draws: the written shape wins, a name
    /// alone draws the default (predefined, else the first of the order); without <c>all</c>
    /// only the written one draws.</summary>
    [Fact]
    public void AllScore_ARowDrawsEveryEntry_TheWrittenShapeWinning()
    {
        string cmaj9 = DefaultShape("Cmaj9", TuningType.Guitar)!;
        Assert.Equal(new string?[] { "x32010", "xx3211", cmaj9 }, RowFrames(Song(All, "C | F(xx3211) | Cmaj9 |")));
        Assert.Equal(new string?[] { "x32010", "xx3211", cmaj9 }, RowFrames(Song(GuitarAll, "C | F(xx3211) | Cmaj9 |")));
        Assert.Equal(new string?[] { null, "xx3211", null }, RowFrames(Song(Guitar, "C | F(xx3211) | Cmaj9 |")));
        // A degree draws the default of the chord it resolves to (IV in C = F).
        Assert.Equal(new string?[] { "133211" }, RowFrames(Song(All, "IV |", "c'1 |")));
        // `all` alone keeps today's tuning rule: over a ukulele staff, the ukulele's.
        Assert.Equal(new string?[] { "0003" }, RowFrames(Song(All, "C |", "c'1 |", "chords prog  staff uk")));
        Assert.Equal(new string?[] { "0003" }, RowFrames(Song(UkuleleAll, "C |", "c'1 |")));
        // A chord with no shape on the tuning draws none (C13: no ukulele table entry, and no
        // enumeration on the re-entrant ukulele).
        Assert.Equal(new string?[] { null, "0003" }, RowFrames(Song(UkuleleAll, "C13 | C |", "c'1 | c'1 |")));
    }

    [Fact]
    public void AllScore_AnAtChordDrawsEveryName_BareIncluded()
    {
        string With(string layout) => layout + """
            octave absolute
            part gt { clef treble }
            section A { gt { c'4@chord(Cm7) c'4@chord(Cm7 x3x546) <c' e' g'>4@chord c'4@chord("N.C.") | } }
            form main { A }
            score main { staff gt }
            """;
        // Cm7's default (predefined x35343), the written x3x546 winning, the bare chord's
        // derived C (x32010); quoted text names no chord.
        Assert.Equal(new[] { "frame:x35343", "frame:x3x546", "frame:x32010" },
            Laid(With(All)).ArticulationLayouts.OrderBy(a => a.X).Select(a => a.Glyph));
        Assert.Equal("frame:x3x546", Assert.Single(Laid(With(Guitar)).ArticulationLayouts).Glyph);
    }

    /// <summary>The "no shape" warning is only an <c>all</c> score's, once per symbol and
    /// tuning in the file (rows and <c>@chord</c> together), at the first, naming the fix.</summary>
    [Fact]
    public void AllScore_WarnsAChordWithNoShape_OncePerSymbolAndTuning()
    {
        string Book(string layout) => layout + """
            octave absolute
            part gt { clef treble }
            section A {
              gt { c'2@chord(C13) c'2@chord(C13) | c'1 | c'1 | }
              chords prog { C13 | C13 | G |  }
            }
            form main { A }
            score main { chords prog  staff gt }
            """;
        var w = Warnings(Book(UkuleleAll));
        var only = Assert.Single(w);
        Assert.Contains("C13 has no chord diagram on 'ukulele'", only.Message);
        Assert.Contains("write the shape: C13(xxxx)", only.Message);
        string book = Book(UkuleleAll);
        Assert.Equal(book.IndexOf("@chord(C13)", StringComparison.Ordinal), only.Span.Start);
        // Not without `all`; not where the shape is written.
        Assert.Empty(Warnings(Book(Ukulele)));
        Assert.Empty(Warnings(Song(UkuleleAll, "C13(0000) | G |")));
        // On the guitar C13 has a default: nothing to say.
        Assert.Empty(Warnings(Book(GuitarAll)));
    }

    [Fact]
    public void AllScore_TheTwinWritesAFretBoardsEntryForEveryChord()
    {
        string ly = Twin(Song(All, "C | F(xx3211) | Cmaj9 | G |"));
        Assert.Matches(@"\\new ChordNames \\\w+\s+\\new FretBoards \\\w+Frets", ly);
        string track = FretTrack(ly);
        // One-shape tables, the predefined fingers kept; no silent slot; no LilyPond table relied on.
        Assert.Matches(@"\\chordmode \{ c \} #guitar-tuning ""x;3(-\d)?;2(-\d)?;o;1(-\d)?;o;""", ly);
        Assert.Contains("\\chordmode { f } #guitar-tuning \"x;x;3;2;1;1;\"", ly);
        Assert.Equal(4, Regex.Matches(ly, @"\\storePredefinedDiagram").Count);
        Assert.DoesNotMatch(@"\bs1\b", track);
        Assert.DoesNotContain("\\include \"predefined-", ly);
        // Without `all`, the written F alone (as before).
        Assert.Single(Regex.Matches(Twin(Song(Guitar, "C | F(xx3211) | Cmaj9 | G |")), @"\\storePredefinedDiagram"));
    }

    [Fact]
    public void AllScore_TheTwinAndMusicXmlDrawEveryAtChord()
    {
        string Book(string layout) => layout + """
            octave absolute
            part gt { clef treble }
            section A { gt { c'2@chord(Cm7) <c' e' g'>2@chord | } }
            form main { A }
            score main { staff gt }
            """;
        string ly = Twin(Book(All));
        Assert.Contains("\\fret-diagram-terse \"x;3;5;3;4;3;\"", ly);        // Cm7's default
        Assert.Contains("\\fret-diagram-terse \"x;3;2;o;1;o;\"", ly);        // the bare chord's C
        Assert.DoesNotContain("fret-diagram", Twin(Book(Guitar)));
        var harmony = Regex.Match(Xml(Book(All)), "<harmony>.*?</harmony>", RegexOptions.Singleline).Value;
        Assert.Contains("<frame-strings>6</frame-strings>", harmony);
        Assert.Contains("<first-fret>3</first-fret>", harmony);
        Assert.DoesNotContain("<frame>", Xml(Book(Guitar)));
    }

    /// <summary>The editor: <c>all</c> is offered first-word (with the tuning words) and after a
    /// tuning word — not after <c>none</c> or <c>all</c> — and the grammar colours it.</summary>
    [Fact]
    public void TheEditor_OffersAndColoursAll()
    {
        static string Ctx(string text) => LilySharpLanguageServer.GetCompletionContext(text, text.Length).ToString();
        Assert.Equal("AfterLayoutChordDiagrams", Ctx("layout {\n  chordDiagrams "));
        Assert.Contains(LilySharpLanguageServer.GetChordDiagramCompletions().Items, i => i.Label == "all");
        Assert.Equal("AfterLayoutChordDiagramsTuning", Ctx("layout {\n  chordDiagrams ukulele "));
        Assert.Equal("AfterLayoutChordDiagramsTuning", Ctx("layout {\n  chordDiagrams guitar a"));
        Assert.Equal("all", Assert.Single(LilySharpLanguageServer.GetChordDiagramScopeCompletions().Items).Label);
        Assert.Equal("LayoutBlock", Ctx("layout {\n  chordDiagrams none "));
        Assert.Equal("LayoutBlock", Ctx("layout {\n  chordDiagrams all "));

        string grammar = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "editors", "vscode", "syntaxes", "lilysharp.tmLanguage.json"));
        string rule = Regex.Match(grammar, "\"match\": \"(\\\\\\\\b\\(chordDiagrams\\)[^\"]+)\"").Groups[1].Value
            .Replace("\\\\", "\\");
        var match = Regex.Match("chordDiagrams guitar all", rule);
        Assert.Equal("all", match.Groups[3].Value);
        Assert.Equal("all", Regex.Match("chordDiagrams all", rule).Groups[2].Value);
        Assert.Equal("", Regex.Match("chordDiagrams none all", rule).Groups[3].Value);
    }

    /// <summary>In an <c>all</c> score a name alone shows the default it draws, where it stands
    /// in the editor's order, instead of the add hint.</summary>
    [Fact]
    public void Hover_InAnAllScore_ShowsTheDefaultANameDraws()
    {
        string doc = All + """
            octave absolute
            part gt { clef treble }
            section A {
              gt { c'1@chord(G) | }
              chords prog { G | }
            }
            form main { A }
            score main { chords prog  staff gt }
            """;
        string? atChord = HoverAt(doc, "@chord(G)");
        Assert.Contains("guitar: `320003` (default) — shape 1 of ", atChord);
        Assert.DoesNotContain("adds a chord diagram", atChord);
        string? row = HoverAt(doc, "G |", 0);
        Assert.Contains("guitar: `320003` (default) — shape 1 of ", row);
        Assert.DoesNotContain("adds a chord diagram", row);
        // A chord with no shape there says how to give it one.
        string uke = UkuleleAll + doc[All.Length..].Replace("@chord(G)", "@chord(C13)");
        Assert.Contains("ukulele: no diagram - no shape on ukulele", HoverAt(uke, "@chord(C13)"));
    }
}
