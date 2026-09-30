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
    /// chords, the ukulele's 306, the mandolin's 204 — less the nine entries Lily# leaves out
    /// (LILYSHARP-OWN 2026-09-28: they sound a note outside their chord or lack a required tone
    /// — guitar 4, ukulele 1, mandolin 4; <c>ChordShapeCheckTests</c> pins which).</summary>
    [Fact]
    public void TheTables_HoldWhatLilyPondStored_LessTheLeftOut()
    {
        Assert.Equal(153 - 4, PredefinedFretboards.Count(PredefinedFretboards.Table.Guitar));
        Assert.Equal(306 - 1, PredefinedFretboards.Count(PredefinedFretboards.Table.Ukulele));
        Assert.Equal(204 - 4, PredefinedFretboards.Count(PredefinedFretboards.Table.Mandolin));
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

    /// <summary>The ukulele (re-entrant) is enumerated by the rules less V4 since 2026-09-29
    /// (K5 ⑥): a chord its table lacks takes the first of that order; one no rule can voice on
    /// four strings has none; a written shape still draws.</summary>
    [Fact]
    public void OnTheUkulele_TheDefaultIsTheTables_ThenTheOrder()
    {
        Assert.Null(DefaultShape("C13", TuningType.Ukulele));
        Assert.Equal("0003", DefaultShape("C", TuningType.Ukulele));
        Assert.Equal("4203", DefaultShape("Cmaj9", TuningType.Ukulele));
        Assert.Equal(ShapeSource.FirstOfOrder, ChordShapes.Default(TuningType.Ukulele, Parse("Cmaj9"))!.Source);
        Assert.Equal("0000", Chosen("C13", TuningType.Ukulele, "0000"));
    }

    /// <summary>MEASURED 2026-09-29, the owner's rule for the ukulele's shapes beyond the table
    /// (K5 ⑥): the same rules less V4 (the root lowest), and with that the order's FIRST shape is
    /// LilyPond's predefined ukulele shape for every chord tried — the table and the order agree.</summary>
    [Theory]
    [InlineData("C", "0003", 39)]
    [InlineData("Am", "2000", 38)]
    [InlineData("F", "2010", 23)]
    [InlineData("G7", "0212", 19)]
    [InlineData("Dm", "2210", 11)]
    [InlineData("Em", "0402", 24)]
    [InlineData("Bb", "3211", 9)]
    [InlineData("E", "1402", 13)]
    [InlineData("A7", "0100", 37)]
    [InlineData("F#m7-5", "2423", 14)]
    public void OnTheUkulele_TheOrderOpensOnLilyPondsShape(string symbol, string predefined, int bases)
    {
        var uke = Tunings.GetTuning(TuningType.Ukulele);
        Assert.Equal(predefined, Predefined(symbol, TuningType.Ukulele));
        var set = ChordVoicings.For(uke, Parse(symbol), includeStretch: false);
        Assert.Equal(bases, set.Bases.Length);
        Assert.Equal(predefined, ChordVoicings.Spell(set.Bases[0]));
        // Every listed shape sounds the chord's tones (V2, V3): the check passes them all.
        Assert.All(set.Bases, b => Assert.Null(ChordShapes.Mismatch(b, uke, Parse(symbol))));
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
    [InlineData("Cm x-x-16-12-13-11", "fret 16 is beyond the highest a shape can write (15)")]
    [InlineData("Cm x--3-5-5-4-3", "has an empty item ('--')")]
    [InlineData("Cm x-3-5-5-4-3-", "write 'x-3-5-5-4-3'")]
    [InlineData("Cm -x-3-5-5-4-3", "write 'x-3-5-5-4-3'")]
    [InlineData("Cm x-3-5", "has 3 item(s), which no tuning has strings for")]
    [InlineData("Cm guitar x-3-5-5", "has 4 item(s) but 'guitar' has 6 strings")]
    [InlineData("Cm x-3-5-5-4-q", "'q' is not a fret")]
    [InlineData("Cm X-3-5-5-4-3", "Values are case-sensitive: write 'x-3-5-5-4-3'")]
    [InlineData("X-3-5-5-4-3", "Values are case-sensitive: write 'x-3-5-5-4-3'")]
    [InlineData("C X32010", "Values are case-sensitive: write 'x32010'")]
    [InlineData("Cm x35543 x-3-5-5-4-3", "two shapes of 6 strings")]
    // The compact form's two-digit segments (owner's decision 2026-09-28): one fret, always.
    [InlineData("Cm x-09-10-12-11", "a two-digit segment is one fret, and '09' is none - separate single frets with '-': write 'x-0-9-10-12-11'")]
    [InlineData("C 10-99-9-9", "fret 99 is beyond the highest a shape can write (15)")]
    [InlineData("C 10-99-9-9", "for two strings write '10-9-9-9-9'")]
    [InlineData("Cm x-10-12", "has 3 item(s), which no tuning has strings for")]
    [InlineData("Cm guitar 8xx8-11", "has 5 item(s) but 'guitar' has 6 strings")]
    [InlineData("Cm 8xq88-11", "'q' is not a fret")]
    public void EachProblem_NamesTheFix(string argument, string fix)
        => Assert.Contains(fix, Words(argument).Problems[0].Message);

    /// <summary>Owner's decision 2026-09-28: a shape holding '-' is DASH-SEPARATED, one item per
    /// string (<c>x</c>, <c>o</c> or a fret 0–15) — the only way to write frets 10–15; it routes
    /// by its ITEM count, and a shape without '-' is one character per string as before.</summary>
    [Fact]
    public void ADashSeparatedShape_ReadsItsItems_AndRoutesByTheirCount()
    {
        var cm = Words("Cm x-x-10-12-13-11");
        Assert.Empty(cm.Problems);
        Assert.Equal(new WrittenShape(null, "x-x-10-12-13-11"), Assert.Single(cm.Shapes));
        Assert.Equal(new[] { -1, -1, 10, 12, 13, 11 }, ChordShapes.Frets("x-x-10-12-13-11"));
        Assert.Equal(new[] { 0, 0, 0, 3 }, ChordShapes.Frets("o-0-o-3"));
        Assert.Equal(6, ChordShapes.StringCount("x-x-10-12-13-11"));
        Assert.Equal(6, ChordShapes.StringCount("x32010"));
        Assert.True(Words("x-x-10-12-13-11").NamesFromDiagram);
        var two = Words("F guitar 1-3-3-2-1-1 ukulele 2010");
        Assert.Empty(two.Problems);
        Assert.Equal(new[] { new WrittenShape("guitar", "1-3-3-2-1-1"), new WrittenShape("ukulele", "2010") }, two.Shapes);
        // Routing counts items: six go to the guitar, four to the ukulele.
        Assert.Equal("8-10-10-888", Chosen("Cm", TuningType.Guitar, "8-10-10-8-8-8", "0-3-3-3"));
        Assert.Equal("0333", Chosen("Cm", TuningType.Ukulele, "8-10-10-8-8-8", "0-3-3-3"));
        // The writer spells one character per string whenever every fret is 9 or less.
        Assert.Equal("x35543", Chosen("Cm", TuningType.Guitar, "x-3-5-5-4-3"));
        Assert.Equal("8xaa88", ChordShapes.Drawn(TuningType.Guitar, [new WrittenShape(null, "8-x-10-10-8-8")])!.FrameSpec);
    }

    /// <summary>
    /// Owner's decision 2026-09-28, the COMPACT form: the word split on '-' into SEGMENTS; a
    /// segment of exactly two digits is ONE fret (10–15), any other one character per string.
    /// The full-dash words read exactly as before; the item count is the string count.
    /// </summary>
    [Theory]
    [InlineData("8xx88-11", "8 x x 8 8 11")]
    [InlineData("xx-10-12-13-11", "x x 10 12 13 11")]
    [InlineData("8-10-10-888", "8 10 10 8 8 8")]
    [InlineData("x-15-13-12-13-x", "x 15 13 12 13 x")]
    [InlineData("x-x-10-12-13-11", "x x 10 12 13 11")]
    [InlineData("8-x-x-8-8-11", "8 x x 8 8 11")]
    [InlineData("10-9-9-988", "10 9 9 9 8 8")]
    [InlineData("o0-12-x3", "0 0 12 x 3")]
    [InlineData("x-3-5-5-4-3", "x 3 5 5 4 3")]
    [InlineData("x35-543", "x 3 5 5 4 3")]
    [InlineData("x32010", "x 3 2 0 1 0")]
    public void ACompactShape_ReadsByItsSegments(string written, string frets)
    {
        var want = frets.Split(' ').Select(f => f == "x" ? -1 : int.Parse(f)).ToArray();
        Assert.True(ChordShapes.TryRead(written, out var read, out var problem), problem);
        Assert.Equal(want, read);
        Assert.Equal(want, ChordShapes.Frets(written));
        Assert.Equal(want.Length, ChordShapes.StringCount(written));
    }

    /// <summary>A two-digit segment is always one fret: <c>00</c>–<c>09</c> and 16–99 are errors
    /// that name the split (the pitfall: frets 10, 9, 9 are <c>10-9-9</c>, never <c>10-99</c>).</summary>
    [Theory]
    [InlineData("10-99-988", "write '10-9-9-988'")]
    [InlineData("8xx88-09", "'09' is none - separate single frets with '-': write '8xx88-0-9'")]
    [InlineData("00-10-12-13", "write '0-0-10-12-13'")]
    [InlineData("x-x-16-12-13-11", "fret 16 is beyond the highest")]
    [InlineData("xx--10-12-13-11", "has an empty item")]
    [InlineData("xx-10-12-13-11-", "write 'xx-10-12-13-11'")]
    public void ATwoDigitSegment_IsOneFret_ElseAnErrorNamingTheFix(string written, string fix)
    {
        Assert.False(ChordShapes.TryRead(written, out _, out var problem));
        Assert.Contains(fix, problem);
    }

    /// <summary>The writer (owner's decision 2026-09-28): one character per string when every
    /// fret is 9 or less; else the compact form — single characters run together, a '-' on each
    /// side of every two-digit fret, none at the ends, and two lone single digits between dashes
    /// split (they would read as one fret).</summary>
    [Theory]
    [InlineData("8-x-x-8-8-11", "8xx88-11")]
    [InlineData("x-x-10-12-13-11", "xx-10-12-13-11")]
    [InlineData("8-10-10-8-8-8", "8-10-10-888")]
    [InlineData("x-15-13-12-13-x", "x-15-13-12-13-x")]
    [InlineData("10-9-9", "10-9-9")]
    [InlineData("12-1-1-12", "12-1-1-12")]
    [InlineData("x-x-10-8-8-11", "xx-10-8-8-11")]
    [InlineData("8-x-10-0-8-11", "8x-10-0-8-11")]
    [InlineData("x-3-5-5-4-3", "x35543")]
    public void TheWriter_SpellsTheCompactForm(string frets, string spelled)
        => Assert.Equal(spelled, ChordVoicings.Spell(ChordShapes.Frets(frets)));

    /// <summary>What the writer spells always reads back to the same frets: every base of
    /// several chords (both rules), and random frets 0–15 / muted on 4–7 strings.</summary>
    [Fact]
    public void TheWritersSpelling_AlwaysReadsBack()
    {
        void RoundTrip(IReadOnlyList<int> frets)
        {
            string spelled = ChordVoicings.Spell(frets);
            Assert.True(ChordShapes.TryRead(spelled, out var read, out var problem),
                $"{string.Join(" ", frets)} spelled '{spelled}': {problem}");
            Assert.True(read.SequenceEqual(frets), $"{string.Join(" ", frets)} spelled '{spelled}' reads {string.Join(" ", read)}");
            Assert.Equal(frets.Count, ChordShapes.StringCount(spelled));
        }
        foreach (var symbol in new[] { "C", "Cm", "Cm7", "D", "G7", "F#m7-5", "C9", "Bbmaj7", "E" })
            foreach (bool stretch in new[] { false, true })
                foreach (var b in ChordVoicings.For(Tunings.Guitar, Parse(symbol), includeStretch: stretch).Bases)
                    RoundTrip(b);
        var random = new Random(20260928);
        for (int n = 0; n < 20000; n++)
        {
            var frets = new int[random.Next(4, 8)];
            for (int i = 0; i < frets.Length; i++)
                frets[i] = random.Next(-1, 16);
            RoundTrip(frets);
        }
        // Dense two-digit neighbourhoods the random walk may miss.
        RoundTrip([10, 9, 9, 10]);
        RoundTrip([12, 1, 1, 12]);
        RoundTrip([1, 1, 10, 1, 1, 1, 1]);
        RoundTrip([0, 9, 15, 0, 0]);
    }

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

    /// <summary>A row group's dash-separated shapes (owner's decision 2026-09-28): the tokens
    /// glue into one word, the chord symbol before the '(' keeps its own '-' (a quality's
    /// <c>-5</c>), and the page's spec carries frets 10–15 as a–f.</summary>
    [Fact]
    public void TheRowForms_ReadDashSeparatedShapes()
    {
        const string row = "Cm(8-10-10-8-8-8) | F(1-3-3-2-1-1 2-0-1-0) | Cm7-5(x-3-4-3-4-x) | Cm(x-x-10-12-13-11) |";
        var tree = SyntaxTree.Parse(Song(Guitar, row));
        Assert.DoesNotContain(tree.Diagnostics, d => d.Severity == LilySharp.Core.Syntax.DiagnosticSeverity.Error);
        Assert.Empty(Warnings(Song(Guitar, row)));
        var entries = tree.GetRoot().DescendantNodes().OfType<ChordEntrySyntax>().ToList();
        Assert.Equal("Cm7-5", entries[2].SymbolText);
        Assert.Equal(new[] { "1-3-3-2-1-1", "2-0-1-0" }, entries[1].ShapeWords.Select(w => w.Text));
        Assert.Equal(tree.Text, tree.GetRoot().ToFullString());
        Assert.Equal(new string?[] { "8aa888", "133211", "x3434x", "xxacdb" }, RowFrames(Song(Guitar, row)));
        Assert.Equal(new string?[] { null, "2010", null, null }, RowFrames(Song(Ukulele, row)));
        // The compact form (owner's decision 2026-09-28) reads the same in a row.
        const string compact = "Cm(8-10-10-888) | Cm(8xx88-11 0-3-3-3) | Cm(xx-10-12-13-11) |";
        Assert.Empty(Warnings(Song(Guitar, compact)));
        Assert.Equal(new string?[] { "8aa888", "8xx88b", "xxacdb" }, RowFrames(Song(Guitar, compact)));
        Assert.Equal(new string?[] { null, "0333", null }, RowFrames(Song(Ukulele, compact)));
    }

    /// <summary>The same in an <c>@chord</c> (with a name, and alone — the name derived from
    /// the frets) and a nameless <c>@diagram</c>; the errors name the fix.</summary>
    [Fact]
    public void AnAtChordAndADiagram_ReadDashSeparatedShapes()
    {
        const string book = Guitar + """
            octave absolute
            part gt { clef treble }
            section A { gt { c'4@chord(Cm x-x-10-12-13-11) c'4@chord(8-10-10-8-8-8) c'4@diagram(x-15-13-12-13-x) c'4@diagram(x-x-10-12-13-11).down | } }
            form main { A }
            score main { staff gt }
            """;
        var tree = SyntaxTree.Parse(book);
        Assert.DoesNotContain(tree.Diagnostics, d => d.Severity == LilySharp.Core.Syntax.DiagnosticSeverity.Error);
        Assert.Empty(Warnings(book));
        var lay = Laid(book);
        Assert.Equal(new[] { "frame:xxacdb", "frame:8aa888", "frame:xfdcdx", "frame:xxacdb" },
            lay.ArticulationLayouts.OrderBy(a => a.X).Select(a => a.Glyph));
        Assert.Equal(new[] { "Cm", "Cm" }, lay.ChordNameLayouts.OrderBy(c => c.X).Select(c => c.ChordText));
        // The compact form (owner's decision 2026-09-28): the same frames.
        string compact = book.Replace("x-x-10-12-13-11", "xx-10-12-13-11").Replace("8-10-10-8-8-8", "8-10-10-888");
        Assert.Empty(Warnings(compact));
        Assert.Equal(new[] { "frame:xxacdb", "frame:8aa888", "frame:xfdcdx", "frame:xxacdb" },
            Laid(compact).ArticulationLayouts.OrderBy(a => a.X).Select(a => a.Glyph));
    }

    [Theory]
    [InlineData("c'1@chord(Cm x-x-16-12-13-11) |", "fret 16 is beyond")]
    [InlineData("c'1@chord(Cm x--3-5-5-4-3) |", "has an empty item")]
    [InlineData("c'1@chord(Cm x-3-5) |", "has 3 item(s)")]
    [InlineData("c'1@chord(Cm X-3-5-5-4-3) |", "Values are case-sensitive: write 'x-3-5-5-4-3'")]
    [InlineData("c'1@chord(X-3-5-5-4-3) |", "Values are case-sensitive: write 'x-3-5-5-4-3'")]
    [InlineData("c'1@chord(x-x-16-12-13-11) |", "fret 16 is beyond")]
    [InlineData("c'1@diagram(X-3-5-5-4-3) |", "Values are case-sensitive: write '@diagram(x-3-5-5-4-3)'")]
    [InlineData("c'1@chord(Cm 10-99-988) |", "write '10-9-9-988'")]
    [InlineData("c'1@chord(Cm 8xx88-01) |", "write '8xx88-0-1'")]
    public void AMiswrittenDashShape_IsWarnedWithTheFix(string music, string fix)
    {
        string book = $$"""
            octave absolute
            part gt { clef treble }
            section A { gt { {{music}} } }
            form main { A }
            score main { staff gt }
            """;
        Assert.Contains(fix, Assert.Single(SemanticValidation.Run(SyntaxTree.Parse(book)), d =>
            d.Code == DiagnosticCodes.ChordDiagramNotDrawn || d.Code == DiagnosticCodes.UnknownAnnotation).Message);
    }

    [Theory]
    [InlineData("Cm(x-x-16-12-13-11) |", "fret 16 is beyond")]
    [InlineData("Cm(x--3-5-5-4-3) |", "has an empty item")]
    [InlineData("Cm(X-3-5-5-4-3) |", "Values are case-sensitive: write 'x-3-5-5-4-3'")]
    public void AMiswrittenDashShapeInARow_IsWarnedWithTheFix(string row, string fix)
        => Assert.Contains(fix, Assert.Single(Warnings(Song(Guitar, row))).Message);

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

    // ------------------------------------------------ an @chord's diagram over its staff's ink

    /// <summary>The owner's "Shape chords" book (2026-09-28): the Cm diagram's grid stood in the
    /// ♭ of its top note.</summary>
    private const string ShapeChords = """
        title "Shape chords"
        part gt { instrument guitar }
        section A { gt {
          chord(C x32013)2@chord e | chord(Am x02210)2@chord a | chord(G 320003)1@chord | chord(Cm xx-10-12-13-11)2@chord d2 |
        } }
        form main { A }
        score main { staff gt  tab gt }
        """;

    /// <summary>One treble staff (<c>octave absolute</c>) with <paramref name="music"/>.</summary>
    private static string OnAStaff(string music) => $$"""
        octave absolute
        part gt { clef treble }
        section A { gt { {{music}} } }
        form main { A }
        score main { staff gt }
        """;

    /// <summary>
    /// For every diagram an <c>@chord</c> drew on the top staff: how far its grid bottom stands
    /// over the staff's ink under the grid's width — the staff's inside profile (heads,
    /// accidentals, stems, ledgers, beams, flags, the other scripts, fingerings, slurs, ties,
    /// tuplet brackets) rebuilt here from the drawn layout, the diagrams left out. Staff-middle
    /// frame; a one-system book whose top staff is the system's first (its spanners' frame).
    /// </summary>
    private static List<(string Glyph, double Clearance)> DiagramClearances(string book)
    {
        var tree = SyntaxTree.Parse(book);
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var lay = new LayoutEngine().Layout(score);
        Assert.Single(lay.Systems);
        var system = lay.Systems[0];
        var staff = score.StaffGroups[0].Staves[0];
        var scripts = lay.ArticulationLayouts.Where(a => a.StaffIndex == 0).ToList();
        var diagrams = scripts.Where(a => ArticulationEngraver.IsDiagramUnderName(a)).ToList();
        Assert.NotEmpty(diagrams);
        var up = new SkylineBuilder(4.0, score.TextMetrics).BuildInsideStaffSkylines(
            staff, system.Measures,
            articulationLayouts: [.. scripts.Where(a => !ArticulationEngraver.IsDiagramUnderName(a))],
            tupletBrackets: [.. lay.TupletBracketLayouts.Where(t => t.StaffIndex == 0)],
            slurs: [.. lay.SlurLayouts.Where(s => s.StaffIndex == 0)],
            ties: [.. lay.TieLayouts.Where(t => t.StaffIndex == 0)],
            beams: [.. lay.BeamLayouts.Where(b => b.StaffIndex == 0)],
            systemLeft: system.Indent,
            fingerings: [.. lay.FingeringLayouts.Where(f => f.StaffIndex == 0)]).Up;
        return [.. diagrams.Select(d => (d.Glyph,
            d.YUp + d.Ink.Bottom - up.MaxHeightInRange(d.X + d.Ink.Left, d.X + d.Ink.Right)))];
    }

    /// <summary>
    /// ★ An <c>@chord</c>'s diagram clears EVERYTHING on the staff under its grid by TextScript's
    /// padding 0.3 — not only its own note's heads and stem (2026-09-28: the Cm grid of the
    /// owner's book stood 0.4 into its top note's ♭). A high note with an accidental, a high
    /// chord either way up, a high neighbour under the wide grid, a script over the note, a
    /// slur over a high run, a beam.
    /// </summary>
    [Theory]
    [InlineData("shape-chords")]
    [InlineData("ais''1@chord(A# x13331) |")]
    [InlineData("<ees'' g'' bes''>2@chord(Eb x68886) r2 |")]
    [InlineData("<g' b' d'' g''>2@chord(G 320003) r2 |")]
    [InlineData("c'4@chord(C x32010) a''4 bes''2 |")]
    [InlineData("a''2@accent@chord(Am x02210) b''2@marcato@chord(Bm x24432) |")]
    [InlineData("c''4(@chord(C x32010) e'' g'' c''') |")]
    [InlineData("c''8@chord(C x32010) e'' g'' c''' e''' c''' g'' e'' |")]
    public void AnAtChordsDiagram_ClearsTheStaffsInkUnderIt(string music)
    {
        string book = music == "shape-chords" ? ShapeChords : OnAStaff(music);
        foreach (var (glyph, clearance) in DiagramClearances(book))
            Assert.True(clearance >= 0.3 - 1e-6,
                $"{music}: {glyph}'s grid bottom stands {clearance:0.000} over the staff's ink (0.3 wanted)");
    }

    /// <summary>
    /// A ROW's diagrams over a high passage (a ♯ over ledger lines, a ♭ chord, a beamed run, a
    /// high neighbour, a note far over the staff) clear the staff's ink under each grid: the row
    /// is its own line, stacked over the staff's whole skyline with its diagrams in its own
    /// (ChordNameEngraver's row skyline) — measured 2026-09-28 when the @chord diagram was
    /// found in a ♭; this pins that the row never was.
    /// </summary>
    [Fact]
    public void RowDiagrams_OverAHighPassage_ClearTheStaffsInk()
    {
        string book = Song(Guitar,
            "Bb(x13331) | Cm(x35543) C(x32010) | C(x32010) F(133211) | Am(x02210) |",
            "ais''1 | <ees'' g'' c'''>2 c''8 e'' g'' c''' | c'4 a''4 bes''2 | e'''1 |");
        var tree = SyntaxTree.Parse(book);
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var lay = new LayoutEngine().Layout(score);
        Assert.Single(lay.Systems);
        var system = lay.Systems[0];
        const int staffIndex = 1;   // `chords prog  staff gt`: the row is 0
        var staff = score.StaffGroups.SelectMany(g => g.Staves).ElementAt(staffIndex);
        var up = new SkylineBuilder(4.0, score.TextMetrics).BuildInsideStaffSkylines(
            staff, system.Measures,
            articulationLayouts: [.. lay.ArticulationLayouts.Where(a => a.StaffIndex == staffIndex)],
            beams: [.. lay.BeamLayouts.Where(b => b.StaffIndex == staffIndex)],
            systemLeft: system.Indent).Up;
        double middle = LayoutUtilities.StaffMiddleUpInSystem(system, staffIndex);
        var names = lay.ChordNameLayouts.Where(n => n.FrameSpec != null).ToList();
        Assert.Equal(6, names.Count);
        foreach (var n in names)
        {
            var (l, r, b, _) = ChordNameEngraver.PlacedDiagram(Fonts, n.FrameSpec!, n.X, n.YUp + n.FrameBottom);
            double clearance = b - (middle + up.MaxHeightInRange(l, r));
            Assert.True(clearance > 0.3, $"{n.ChordText}: the row's grid stands {clearance:0.000} over the staff's ink");
        }
    }

    /// <summary>...and the name still stands over its diagram: on the page every stroke of the
    /// diagram is below the baseline of the name the same <c>@chord</c> printed.</summary>
    [Theory]
    [InlineData("ais''1@chord(A# x13331) |", "@chord(A#")]
    [InlineData("c'4@chord(C x32010) a''4 bes''2 |", "@chord(C")]
    [InlineData("<ees'' g'' bes''>2@chord(Eb x68886) r2 |", "@chord(Eb")]
    public void AnAtChordsDiagram_OverHighNotes_StaysUnderItsName(string music, string mark)
    {
        string book = OnAStaff(music);
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(book));
        int pos = book.IndexOf(mark, StringComparison.Ordinal);
        var ys = Regex.Matches(svg, $"<(?:line|circle)[^>]*data-pos=\"{pos}\"[^>]*>")
            .SelectMany(m => Regex.Matches(m.Value, " (?:y1|y2|cy)=\"([-0-9.]+)\"").Select(g => Num(g.Groups[1].Value)))
            .ToList();
        Assert.NotEmpty(ys);
        Assert.True(ys.Min() > NameBaseline(svg, pos), $"{music}: diagram top {ys.Min():0.00} over its name");
    }

    /// <summary>The page's staff top line (device Y): the highest long horizontal line.</summary>
    private static double StaffTop(string svg)
        => Regex.Matches(svg, "<line x1=\"([-0-9.]+)\" y1=\"([-0-9.]+)\" x2=\"([-0-9.]+)\" y2=\"([-0-9.]+)\"")
            .Where(m => m.Groups[2].Value == m.Groups[4].Value
                        && Num(m.Groups[3].Value) - Num(m.Groups[1].Value) > 20)
            .Min(m => Num(m.Groups[2].Value));

    /// <summary>The chord name's baseline (device Y): of the sans texts carrying the mark's
    /// data-pos — the name, and since 2026-09-29 the diagram's own X / O and finger numbers,
    /// set smaller — the one at the largest font size.</summary>
    private static double NameBaseline(string svg, int dataPos)
        => Regex.Matches(svg, $"<text x=\"[-0-9.]+\" y=\"([-0-9.]+)\" font-size=\"([-0-9.]+)\"[^>]*sans-serif[^>]*data-pos=\"{dataPos}\"")
            .OrderByDescending(m => Num(m.Groups[2].Value))
            .Select(m => Num(m.Groups[1].Value))
            .First();

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
    /// default for (C13 on the ukulele). The first cut of 2026-09-28 warned "no chord diagram on 'ukulele'".</summary>
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
        var unnamed = Assert.Single(Warnings(Book("", "c'1@chord(x0x00x) |"))).Message;
        Assert.Contains("name no chord Lily# knows", unnamed);
        // The two fixes (owner, 2026-09-28): a name, or the nameless diagram.
        Assert.Contains("@chord(NAME x0x00x)", unnamed);
        Assert.Contains("@diagram(x0x00x)", unnamed);
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

    /// <summary>
    /// A predefined shape's fingers and barre (HANDOFF §2 K5 ①'s tail, 2026-09-29): the page
    /// draws the curved barre on every diagram of the shape, and the finger numbers under a
    /// chords row's diagram only — a FretBoard's finger-code is below-string, a markup
    /// diagram's none (scm/define-grobs.scm FretBoard; scm/fret-diagrams.scm make-fret-diagram);
    /// the twin's terse strings say the same, and MusicXML's frame carries both as data.
    /// </summary>
    [Fact]
    public void APredefinedShapesFingersAndBarre_DrawAsLilyPondDraws_AndReachTheExporters()
    {
        string book = GuitarAll + """
            octave absolute
            part gt { clef treble }
            section A {
              gt { c'1@chord(F) | }
              chords prog { F | }
            }
            form main { A }
            score main { chords prog  staff gt }
            """;
        var tree = SyntaxTree.Parse(book);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        string svg = SvgGenerator.Generate(tree);
        // Two diagrams (the row's, the @chord's), two barres: a closed two-curve path each.
        Assert.Equal(2, Regex.Matches(svg, "<path[^>]*d=\"M[^\"]*C[^\"]*C[^\"]*Z\"").Count);
        // The fingers 1 3 4 2 1 1 under the row's diagram only: one "4" and one "3" on the page.
        Assert.Single(Regex.Matches(svg, "<text[^>]*>4</text>"));
        Assert.Single(Regex.Matches(svg, "<text[^>]*>3</text>"));
        string ly = new LilyPondExporter().Export(tree);
        Assert.Contains("\"1-1-(;3-3;3-4;2-2;1-1;1-1-);\"", ly);            // the row: a FretBoard
        Assert.Contains("\\fret-diagram-terse \"1-(;3;3;2;1;1-);\"", ly);   // the @chord: markup
        string xml = Xml(book);
        var harmony = Regex.Match(xml, "<harmony>.*?</harmony>", RegexOptions.Singleline).Value;
        Assert.Contains("<fingering>4</fingering>", harmony);
        Assert.Matches("<string>6</string>\\s*<fret>1</fret>\\s*<fingering>1</fingering>\\s*<barre type=\"start\" />", harmony);
        Assert.Contains("<barre type=\"stop\" />", harmony);
        // Without the row: no finger number on the page, the barre still.
        string alone = SvgGenerator.Generate(SyntaxTree.Parse(book.Replace("chords prog { F | }", "").Replace("chords prog  staff gt", "staff gt")));
        Assert.Empty(Regex.Matches(alone, "<text[^>]*>4</text>"));
        Assert.Single(Regex.Matches(alone, "<path[^>]*d=\"M[^\"]*C[^\"]*C[^\"]*Z\""));
    }

    /// <summary>A dash-separated shape at frets 12–15 (owner's decision 2026-09-28) reaches the
    /// twin — the terse markup, the FretBoards table — and MusicXML with its two-digit frets.</summary>
    [Fact]
    public void TheExporters_WriteADashShapesHighFrets()
    {
        string Book(string music) => Guitar + $$"""
            octave absolute
            part gt { clef treble }
            section A { gt { {{music}} } }
            form main { A }
            score main { staff gt }
            """;
        string ly = Twin(Book("c'2@chord(Cm x-15-13-12-13-x) c'2@diagram(x-x-10-12-13-11) |"));
        Assert.Contains("\\fret-diagram-terse \"x;15;13;12;13;x;\"", ly);
        Assert.Contains("\\fret-diagram-terse \"x;x;10;12;13;11;\"", ly);
        string row = Twin(Song(Guitar, "Cm(x-15-13-12-13-x) |", "c'1 |"));
        Assert.Contains("\\storePredefinedDiagram #lysFretsA \\chordmode { c:m } #guitar-tuning \"x;15;13;12;13;x;\"", row);

        var harmony = Regex.Match(Xml(Book("c'1@chord(Cm x-15-13-12-13-x) |")), "<harmony>.*?</harmony>",
            RegexOptions.Singleline).Value;
        Assert.Contains("<first-fret>12</first-fret>", harmony);
        Assert.Equal(new[] { "15", "13", "12", "13" },
            Regex.Matches(harmony, "<fret>(\\d+)</fret>").Select(m => m.Groups[1].Value));
        // A @diagram nests in the harmony of a name on the same note (MusicXML's frame is a
        // harmony child).
        var diagram = Xml(Book("c'1@chord(Cm)@diagram(x-x-10-12-13-11) |"));
        Assert.Contains("<first-fret>10</first-fret>", diagram);
        Assert.Equal(new[] { "10", "12", "13", "11" },
            Regex.Matches(diagram, "<fret>(\\d+)</fret>").Select(m => m.Groups[1].Value));
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

    /// <summary>The hover spells a written shape the way the step writes it (2026-09-28): one
    /// character per string when every fret is 9 or less, else the compact form — a '-' on each
    /// side of each two-digit fret (owner's decision 2026-09-28), whichever way it was written.</summary>
    [Fact]
    public void Hover_SpellsADashShapeInTheWritersForm()
    {
        string doc = """
            layout gtr { chordDiagrams guitar }
            octave absolute
            part gt { clef treble }
            section A { gt { c'2@chord(Cm x-3-5-5-4-3) c'4@chord(Cm 8-x-x-8-8-11) c'4@chord(Cm x-x-10-12-13-11) | } }
            form main { A }
            score g { layout gtr  staff gt }
            """;
        Assert.Contains("guitar: `x35543` (written) — shape 1 of 29", HoverAt(doc, "@chord(Cm x-3"));
        Assert.Contains("guitar: `8xx88-11` (written) — shape 19 of 29", HoverAt(doc, "@chord(Cm 8"));
        Assert.Contains("guitar: `xx-10-12-13-11` (written) — shape 26 of 29", HoverAt(doc, "@chord(Cm x-x"));
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
    [InlineData("chordDiagrams guitar alll", "after the tuning only 'capo N' and 'all' may follow")]
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
        // A predefined shape's spec carries its fingers (and barre) after a '|' — the row draws
        // them; a written shape's carries none.
        Assert.Equal(new string?[] { "x32010|032010", "xx3211", cmaj9 }, RowFrames(Song(All, "C | F(xx3211) | Cmaj9 |")));
        Assert.Equal(new string?[] { "x32010|032010", "xx3211", cmaj9 }, RowFrames(Song(GuitarAll, "C | F(xx3211) | Cmaj9 |")));
        Assert.Equal(new string?[] { null, "xx3211", null }, RowFrames(Song(Guitar, "C | F(xx3211) | Cmaj9 |")));
        // A degree draws the default of the chord it resolves to (IV in C = F).
        Assert.Equal(new string?[] { "133211|134211|6-1@1" }, RowFrames(Song(All, "IV |", "c'1 |")));
        // `all` alone keeps today's tuning rule: over a ukulele staff, the ukulele's.
        Assert.Equal(new string?[] { "0003|0003" }, RowFrames(Song(All, "C |", "c'1 |", "chords prog  staff uk")));
        Assert.Equal(new string?[] { "0003|0003" }, RowFrames(Song(UkuleleAll, "C |", "c'1 |")));
        // A chord with no shape on the tuning draws none (C13: no ukulele table entry, and no
        // enumeration on the re-entrant ukulele).
        Assert.Equal(new string?[] { null, "0003|0003" }, RowFrames(Song(UkuleleAll, "C13 | C |", "c'1 | c'1 |")));
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
        Assert.Equal(new[] { "frame:x35343|013121|5-1@3", "frame:x3x546", "frame:x32010|032010" },
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
        Assert.Contains("\\fret-diagram-terse \"x;3-(;5;3;4;3-);\"", ly);    // Cm7's default, its barre
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
        // After a tuning: `capo` and `all` (2026-09-29); after `capo N`: `all` alone.
        Assert.Equal(new[] { "capo", "all" }, LilySharpLanguageServer.GetChordDiagramScopeCompletions().Items.Select(i => i.Label));
        Assert.Equal("all", Assert.Single(LilySharpLanguageServer.GetChordDiagramScopeCompletions(capoAllowed: false).Items).Label);
        Assert.Equal("LayoutBlock", Ctx("layout {\n  chordDiagrams none "));
        Assert.Equal("LayoutBlock", Ctx("layout {\n  chordDiagrams all "));

        string grammar = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "editors", "vscode", "syntaxes", "lilysharp.tmLanguage.json"));
        string rule = Regex.Match(grammar, "\"match\": \"(\\\\\\\\b\\(chordDiagrams\\)[^\"]+)\"").Groups[1].Value
            .Replace("\\\\", "\\");
        var match = Regex.Match("chordDiagrams guitar all", rule);
        Assert.Equal("all", match.Groups[5].Value);
        Assert.Equal("all", Regex.Match("chordDiagrams all", rule).Groups[2].Value);
        Assert.Equal("", Regex.Match("chordDiagrams none all", rule).Groups[5].Value);
        var capo = Regex.Match("chordDiagrams guitar capo 3 all", rule);
        Assert.Equal(("guitar", "capo", "3", "all"), (capo.Groups[2].Value, capo.Groups[3].Value, capo.Groups[4].Value, capo.Groups[5].Value));
        Assert.Equal("capo", Regex.Match("chordDiagrams capo 3", rule).Groups[2].Value);
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

    // ================================================================ on a rest or a spacer

    /// <summary>The owner's report 2026-09-28, verbatim: <c>r1@chord(C x32013)</c>,
    /// <c>s1@chord(G)</c> drew nothing — not even the name — while <c>@diagram</c> drew.</summary>
    private static string RestBook(string layout, string music =
        "c'1@chord(C x32013) | r1@chord(C x32013) | s1@chord(C x32013) | r1@chord(G) | s1@chord(G) | r1@diagram(x32010) | s1@diagram(x32010) |")
        => layout + $$"""
        title "Rests and spacers"
        part gt { clef treble }
        section A { gt {
          {{music}}
        } }
        form main { A }
        score main { staff gt }
        """;

    /// <summary>Owner's decision 2026-09-28: a chord symbol belongs to the BEAT, not to a note —
    /// on a rest or a spacer it draws exactly what it draws on a note: the name at that moment,
    /// and the diagram under it by the usual rules (written shape; the layout's tuning, else the
    /// part's, else the guitar; <c>none</c> draws none).</summary>
    [Fact]
    public void OnARestOrASpacer_AnAtChordDrawsItsNameAndItsDiagram()
    {
        foreach (string layout in new[] { "", Guitar })
        {
            var names = Collected(RestBook(layout)).ChordNames.OrderBy(c => c.MeasureIndex).ToList();
            Assert.Equal(new[] { "C", "C", "C", "G", "G" }, names.Select(c => c.ChordText));
            Assert.Equal(new[] { 0, 1, 2, 3, 4 }, names.Select(c => c.MeasureIndex));
            var lay = Laid(RestBook(layout));
            Assert.Equal(5, lay.ChordNameLayouts.Count());
            Assert.Equal(3, lay.ArticulationLayouts.Count(a => a.Glyph == "frame:x32013"));
            Assert.Equal(2, lay.ArticulationLayouts.Count(a => a.Glyph == "frame:x32010"));
        }
        // chordDiagrams none: the names still print; only the @diagrams draw.
        var plain = Laid(RestBook(NoDiagrams));
        Assert.Equal(5, plain.ChordNameLayouts.Count());
        Assert.All(plain.ArticulationLayouts, a => Assert.Equal("frame:x32010", a.Glyph));
        // chordDiagrams all: a name alone draws the default, on a rest as on a note.
        Assert.Equal(new[] { "frame:320003|210003" },
            Laid(RestBook(All, "r1@chord(G) |")).ArticulationLayouts.Select(a => a.Glyph));
        // A symbol-less shape on a spacer names its chord from the frets, as on a note.
        Assert.Equal("C", Assert.Single(Collected(RestBook("", "s1@chord(x32010) |")).ChordNames).ChordText);
        // A multi-measure rest, a tuplet's rest and a pitched rest carry one too.
        Assert.Equal(new[] { "C", "F", "G" },
            Collected(RestBook("", "R1*2@chord(C) | tuplet 3/2 { r2@chord(F) c'2 c'2 } | c'1@rest@chord(G) |"))
                .ChordNames.OrderBy(c => c.MeasureIndex).Select(c => c.ChordText));
        Assert.Empty(SemanticValidation.Run(SyntaxTree.Parse(RestBook(""))).Where(d => d.Severity != LilySharp.Core.Syntax.DiagnosticSeverity.Info));
    }

    /// <summary>A bare <c>@chord</c> on a rest or a spacer has no notes to name: it draws nothing
    /// and says so, naming the fix (write the name).</summary>
    [Theory]
    [InlineData("r1@chord |", "rest")]
    [InlineData("s1@chord |", "spacer")]
    [InlineData("s1@chord() |", "spacer")]
    public void ABareAtChordOnARest_Warns_AndDrawsNothing(string music, string what)
    {
        string book = RestBook("", music);
        var only = Assert.Single(SemanticValidation.Run(SyntaxTree.Parse(book)), d => d.Code == DiagnosticCodes.ChordNotRecognized);
        Assert.Contains($"@chord on a {what} has no notes", only.Message);
        Assert.Contains("write the name, e.g. @chord(C)", only.Message);
        Assert.Empty(Collected(book).ChordNames);
        // A named one is not warned.
        Assert.DoesNotContain(SemanticValidation.Run(SyntaxTree.Parse(RestBook("", "r1@chord(C) |"))),
            d => d.Code == DiagnosticCodes.ChordNotRecognized);
    }

    /// <summary>The validators that read an <c>@chord</c> read it on a rest the same (LYS1039, LYS1038).</summary>
    [Fact]
    public void OnARest_TheShapeWarningsApply()
    {
        var mismatch = Assert.Single(SemanticValidation.Run(SyntaxTree.Parse(RestBook("", "r1@chord(Cm x32010) |"))),
            d => d.Code == DiagnosticCodes.ChordShapeMismatch);
        Assert.Contains("x32010", mismatch.Message);
        Assert.Contains("'mute' is neither", Assert.Single(Warnings(RestBook("", "s1@chord(D mute 5) |"))).Message);
    }

    [Fact]
    public void OnARestOrASpacer_TheTwinWritesTheNameAtItsMomentAndTheDiagram()
    {
        string ly = Twin(RestBook(""));
        string inline = Regex.Match(ly, @"gtInlineChords = \\chordmode \{[^}]*\}").Value;
        Assert.Equal(new[] { "c1", "c1", "c1", "g1", "g1" },
            Regex.Matches(inline, @"\b[cg]1\b").Select(m => m.Value));
        Assert.Equal(3, Regex.Matches(ly, "\\\\fret-diagram-terse \"x;3;2;o;1;3;\"").Count);
        // A half-bar spacer's name stands at its moment: the silent half first.
        string half = Regex.Match(Twin(RestBook("", "c'2 s2@chord(G) |")), @"gtInlineChords = \\chordmode \{[^}]*\}").Value;
        Assert.Matches(@"s2\s+g2", half);
    }

    [Fact]
    public void OnARestOrASpacer_MusicXmlWritesAHarmonyBeforeIt()
    {
        string xml = Xml(RestBook(""));
        Assert.Equal(5, Regex.Matches(xml, "<harmony>").Count);
        Assert.Equal(3, Regex.Matches(xml, "<frame>").Count);
        // Bar 2: the harmony, with its frame, then the rest.
        var bar2 = Regex.Match(xml, "<measure number=\"2\".*?</measure>", RegexOptions.Singleline).Value;
        Assert.True(bar2.IndexOf("<harmony>", StringComparison.Ordinal) < bar2.IndexOf("<rest", StringComparison.Ordinal), bar2);
        Assert.Contains("<frame-strings>6</frame-strings>", bar2);
        // Mid-bar: the harmony stands between the note and the rest, at the rest's offset.
        var half = Regex.Match(Xml(RestBook("", "c'2 r2@chord(G) |")), "<measure number=\"1\".*?</measure>",
            RegexOptions.Singleline).Value;
        int pitch = half.IndexOf("<pitch>", StringComparison.Ordinal), harmony = half.IndexOf("<harmony>", StringComparison.Ordinal);
        Assert.True(pitch >= 0 && pitch < harmony && harmony < half.IndexOf("<rest", StringComparison.Ordinal), half);
    }

    // ================================================================ chord rows in MusicXML
    // Owner's decisions 2026-09-29 (HANDOFF §2 K5 ①'s last item): a chords row's symbols are
    // <harmony> elements in the part of the staff directly under the row (the next staff in the
    // score's order, else its first), each at its slot's <offset>; a lead sheet with no staff
    // gets a part of whole rests named "ROW (chords)"; an @chord of the same chord at the same
    // moment is dropped; N.C. writes nothing, as a rest's @chord path writes only a symbol.

    private static string Measure(string xml, int number)
        => Regex.Match(xml, $"<measure number=\"{number}\".*?</measure>", RegexOptions.Singleline).Value;

    [Fact]
    public void ARow_WritesItsSymbolsAsHarmonies_InTheStaffUnderIt_AtTheirOffsets()
    {
        string xml = Xml(Song(Guitar, "C G7 | F . Am . | r Dm |", "c'1 | c'1 | c'1 |"));
        Assert.DoesNotContain("(chords)", xml);   // the row is no part of its own: gt holds it
        string m1 = Measure(xml, 1), m2 = Measure(xml, 2), m3 = Measure(xml, 3);
        Assert.Equal(2, Regex.Matches(m1, "<harmony>").Count);
        // The bar's head: no offset. Beat 3 of 4/4: two quarters of 24 divisions.
        Assert.Matches("<root-step>C</root-step>\\s*</root>\\s*<kind>major</kind>\\s*</harmony>", m1);
        Assert.Matches("<root-step>G</root-step>\\s*</root>\\s*<kind>dominant</kind>\\s*<offset>48</offset>", m1);
        Assert.Matches("<root-step>F</root-step>\\s*</root>\\s*<kind>major</kind>\\s*</harmony>", m2);
        Assert.Matches("<root-step>A</root-step>\\s*</root>\\s*<kind>minor</kind>\\s*<offset>48</offset>", m2);
        // N.C. writes nothing; the Dm after it keeps its offset.
        Assert.Single(Regex.Matches(m3, "<harmony>"));
        Assert.Matches("<root-step>D</root-step>\\s*</root>\\s*<kind>minor</kind>\\s*<offset>48</offset>", m3);
        // The harmonies stand at the head of the bar's stream.
        Assert.True(m1.LastIndexOf("</harmony>", StringComparison.Ordinal) < m1.IndexOf("<note", StringComparison.Ordinal), m1);
        // A row the score does not place writes nothing.
        Assert.DoesNotContain("<harmony>", Xml(Song(Guitar, "C G7 |", "c'1 |", "staff gt")));
    }

    [Fact]
    public void ARowsHarmony_DropsTheSameChordsAtChord_AndCarriesTheDrawnFrame()
    {
        // @chord(C) at beat 1 beside the row's C: one harmony; the row's G7 at beat 3 and an
        // @chord(Am) there: both stand.
        string m1 = Measure(Xml(Song(GuitarAll, "C G7 |", "c'2@chord(C) a'2@chord(Am) |")), 1);
        Assert.Equal(3, Regex.Matches(m1, "<harmony>").Count);
        Assert.Single(Regex.Matches(m1, "<root-step>C</root-step>"));
        Assert.Single(Regex.Matches(m1, "<root-step>A</root-step>"));
        // Under `all` the row's F draws LilyPond's shape with its fingers and barre; a written
        // shape has neither; under `chordDiagrams none` no frame at all.
        string xml = Xml(Song(GuitarAll, "F | F(xx3211) |", "c'1 | c'1 |"));
        Assert.Contains("<fingering>", Measure(xml, 1));
        Assert.Contains("<barre type=\"start\" />", Measure(xml, 1));
        Assert.Contains("<frame-strings>6</frame-strings>", Measure(xml, 2));
        Assert.DoesNotContain("<fingering>", Measure(xml, 2));
        Assert.DoesNotContain("<frame>", Xml(Song(NoDiagrams, "F |", "c'1 |")));
        // A degree names its chord in the key: IV in G is C.
        Assert.Contains("<root-step>C</root-step>", Xml(Song("key g major\n" + Guitar, "IV |", "c'1 |")));
    }

    [Fact]
    public void ALeadSheetsRow_GetsAPartOfRests_AndTheByPartFormReadsTheSame()
    {
        const string sheet = """
            octave absolute
            section A { chords prog { C | G7 . Am . | } lyrics words { la | la la | } }
            form main { A }
            score main "sheet" { chords prog lyrics words }
            """;
        string xml = Xml(sheet);
        Assert.Contains("prog (chords)", xml);
        Assert.DoesNotContain("Part 1", xml);
        Assert.Equal(2, Regex.Matches(xml, "<measure number=").Count);
        Assert.Equal(2, Regex.Matches(xml, "<rest").Count);
        Assert.Equal(3, Regex.Matches(xml, "<harmony>").Count);
        Assert.Contains("<offset>48</offset>", Measure(xml, 2));
        const string byPart = """
            octave absolute
            part gt { clef treble  section A { c'1 | c'1 | } }
            chords prog { section A { C | G7 . Am . | } }
            form main { A }
            score main { chords prog  staff gt }
            """;
        string xml2 = Xml(byPart);
        Assert.DoesNotContain("(chords)", xml2);
        Assert.Equal(3, Regex.Matches(xml2, "<harmony>").Count);
        Assert.Contains("<offset>48</offset>", Measure(xml2, 2));
        Assert.Contains("<pitch>", Measure(xml2, 2));   // the row's harmonies sit in gt's bars
    }

    [Fact]
    public void OnARest_TheHoverShowsTheDiagram()
    {
        string doc = RestBook("", "r1@chord(C x32013) | s1@chord(G) |");
        Assert.Contains("guitar: `x32013` (written)", HoverAt(doc, "@chord(C"));
        Assert.Contains("adds a chord diagram", HoverAt(doc, "@chord(G"));
    }

    // ================================================================ the layout's shape table
    // Owner's design (HANDOFF §2 K5 ③; built 2026-09-29): `chordDiagrams [TUNING] [all] { Cm7
    // x35343  G  section B { C x35553 } }` lists the chords that draw a diagram wherever they are
    // named. Strongest first: the shape written at the chord, the entry of the section the chord
    // is written in, the song's entry, then — under `all` — the default. A name listed alone
    // draws the default; an entry whose shapes fit no tuning of the score is not used there.

    /// <summary>C listed alone (the default draws), F with a shape of its own.</summary>
    private const string Table = "layout { chordDiagrams guitar { C  F xx3211 } }\n";

    /// <summary>Every LYS diagnostic of the standard one-section book with these layout entries.</summary>
    private static IReadOnlyList<LilySharp.Core.Syntax.Diagnostic> LayoutDiagnostics(string entries)
    {
        var tree = SyntaxTree.Parse($"layout {{ {entries} }}\npart m {{ }}\nsection A {{ m {{ c'1 | }} }}\nform main {{ A }}\nscore main {{ staff m }}\n");
        return [.. tree.Diagnostics.Concat(SemanticValidation.Run(tree)).Where(d => d.Code.StartsWith("LYS", StringComparison.Ordinal))];
    }

    [Fact]
    public void TheKey_TakesAShapeTable()
    {
        var (plan, problems) = Layout("chordDiagrams guitar { Cm7 x35343  G  F guitar 133211 ukulele 2010  section A { C#m7 x46654 } }");
        Assert.Empty(problems);
        Assert.Equal("guitar", plan.ChordDiagrams);
        Assert.False(plan.ChordDiagramsAll);
        var table = plan.ChordDiagramTable!;
        Assert.Equal(new[] { "Cm7", "G", "F" }, table.Song.Select(e => e.Symbol));
        Assert.Equal("x35343", Assert.Single(table.Song[0].Shapes).Shape);
        Assert.Empty(table.Song[1].Shapes);
        Assert.Equal(new[] { ("guitar", "133211"), ("ukulele", "2010") },
            table.Song[2].Shapes.Select(s => (s.TuningName!, s.Shape)));
        var (section, entries) = Assert.Single(table.Sections);
        Assert.Equal("A", section);
        Assert.Equal("C#m7", Assert.Single(entries).Symbol);
        Assert.Equal(new[] { -1, 4, 6, 6, 5, 4 }, ChordShapes.Frets(Assert.Single(entries).Shapes[0].Shape));

        // After `all`, alone (the tuning as when unset), over several lines; an empty table is one.
        Assert.True(Layout("chordDiagrams all { C }").Plan is { ChordDiagramsAll: true, ChordDiagramTable.Song.Length: 1 });
        Assert.True(Layout("chordDiagrams { C }").Plan is { ChordDiagrams: null, ChordDiagramsAll: false, ChordDiagramTable.Song.Length: 1 });
        Assert.True(Layout("chordDiagrams guitar all {\n  C x32013\n  section A {\n    G\n  }\n}").Plan
            is { ChordDiagramsAll: true, ChordDiagramTable: { Song.Length: 1, Sections.Length: 1 } });
        Assert.True(Layout("chordDiagrams guitar { }").Plan.ChordDiagramTable!.IsEmpty);
        Assert.Null(Layout("chordDiagrams guitar").Plan.ChordDiagramTable);
        // Compared by value: a re-read is no change (the incremental compiler's contract).
        Assert.Equal(Layout("chordDiagrams guitar { C  F xx3211  section A { G } }").Plan,
            Layout("chordDiagrams   guitar {  C   F xx3211   section  A  { G } }").Plan);
        Assert.NotEqual(Layout("chordDiagrams guitar { C  F xx3211 }").Plan, Layout("chordDiagrams guitar { C  F xx3212 }").Plan);
    }

    [Theory]
    [InlineData("chordDiagrams none { C }", "'none' draws no diagram, so it takes no shape table")]
    [InlineData("chordDiagrams guitar { section { C } }", "'section' takes the section's name and a block")]
    [InlineData("chordDiagrams guitar { section A }", "'section' takes the section's name and a block")]
    [InlineData("chordDiagrams guitar { { C } }", "'{' here opens nothing")]
    [InlineData("chordDiagrams guitar { section A { section A { } } }", "holds no 'section' of its own")]
    [InlineData("chordDiagrams guitar { C } x", "comes after the shape table's closing '}'")]
    [InlineData("chordDiagrams guitar all all { C }", "'all' is written twice")]
    public void ABrokenTable_IsRefused_NamingTheFix(string entries, string message)
    {
        var (plan, problems) = Layout(entries);
        Assert.Contains(message, Assert.Single(problems.Where(p => p.Severity == LilySharp.Core.Syntax.DiagnosticSeverity.Error)).Message);
        Assert.Null(plan.ChordDiagramTable);
        Assert.Null(plan.ChordDiagrams);
    }

    /// <summary>An entry with a problem is left out with a warning and the rest stand — a row's
    /// rule (LYS1038); a chord listed twice warns and the last one wins; a section nothing
    /// declares warns; a table shape is checked against its chord (LYS1039).</summary>
    [Fact]
    public void ATablesEntryProblems_AreWarned_AndTheRestStand()
    {
        // A symbol that is none takes its shapes with it, silently; a bad shape is a row's LYS1038.
        var d = LayoutDiagnostics("chordDiagrams guitar { Xm7 x35343  x32010  C x32  F xx3211 }");
        Assert.Equal(2, d.Count);
        Assert.All(d, p => Assert.Equal(DiagnosticCodes.ChordDiagramNotDrawn, p.Code));
        Assert.Contains("'Xm7' is not a chord symbol", d[0].Message);
        Assert.Contains("'x32' has 3 character(s)", d[1].Message);
        var table = Layout("chordDiagrams guitar { Xm7 x35343  x32010  C x32  F xx3211 }").Plan.ChordDiagramTable!;
        Assert.Equal(new[] { "C", "F" }, table.Song.Select(e => e.Symbol));
        Assert.Empty(table.Song[0].Shapes);
        Assert.Contains("'x32010' comes before any chord name",
            Assert.Single(LayoutDiagnostics("chordDiagrams guitar { x32010  C }")).Message);

        var twice = LayoutDiagnostics("chordDiagrams guitar { C x32010  G  C x32013 }");
        var only = Assert.Single(twice);
        Assert.Contains("'C' is listed twice in this table; the last one wins", only.Message);
        Assert.Equal(LilySharp.Core.Syntax.DiagnosticSeverity.Warning, only.Severity);
        Assert.Equal("x32013", ChordShapes.Drawn(TuningType.Guitar, [], chord: Parse("C"),
            table: Layout("chordDiagrams guitar { C x32010  G  C x32013 }").Plan.ChordDiagramTable)!.Spelled);

        var unknown = Assert.Single(LayoutDiagnostics("chordDiagrams guitar { section Z { C } }"));
        Assert.Contains("No section is named 'Z', so its entries apply nowhere. Sections: A.", unknown.Message);
        Assert.Equal(LilySharp.Core.Syntax.DiagnosticSeverity.Warning, unknown.Severity);

        var mismatch = Assert.Single(LayoutDiagnostics("chordDiagrams guitar { C x02210 }"));
        Assert.Equal(DiagnosticCodes.ChordShapeMismatch, mismatch.Code);
        Assert.Contains("'x02210' sounds A C E, which is Am, not C (A is not a tone of C) - write Am x02210 or another shape.", mismatch.Message);
        // Unset tuning: checked on the guitar and on each fretted instrument the parts play.
        Assert.Empty(LayoutDiagnostics("chordDiagrams { F 2010 }"));
    }

    [Fact]
    public void TheTable_DrawsTheChordsItLists_WhereverTheyAreNamed()
    {
        // A row: C (listed alone) draws the default, F the table's shape, G nothing.
        Assert.Equal(new string?[] { "x32010|032010", "xx3211", null }, RowFrames(Song(Table, "C | F | G |")));
        // A written shape wins over the table.
        Assert.Equal(new string?[] { "x32013" }, RowFrames(Song(Table, "C(x32013) |", "c'1 |")));
        // A degree resolves to its chord first (IV in C is F).
        Assert.Equal(new string?[] { "xx3211" }, RowFrames(Song(Table, "IV |", "c'1 |")));
        // With `all`: the table's shape for a listed chord, the default for the rest.
        Assert.Equal(new string?[] { "xx3211", "320003|210003" },
            RowFrames(Song("layout { chordDiagrams all { F xx3211 } }\n", "F | G |", "c'1 | c'1 |")));
        // An entry whose shapes fit no tuning of the score is not used there.
        Assert.Equal(new string?[] { null }, RowFrames(Song("layout { chordDiagrams guitar { F 2010 } }\n", "F |", "c'1 |")));
        Assert.Equal(new string?[] { "2010" }, RowFrames(Song("layout { chordDiagrams ukulele { F 2010 } }\n", "F |", "c'1 |")));
        // The tuning as when unset: over a ukulele staff, the ukulele's shape of the entry.
        Assert.Equal(new string?[] { "2010" }, RowFrames(Song("layout { chordDiagrams { F xx3211 2010 } }\n", "F |", "c'1 |", "chords prog  staff uk")));

        // An @chord: the listed F draws, G does not; a bare @chord's derived C draws its default.
        string book = Table + """
            octave absolute
            part gt { clef treble }
            section A { gt { c'4@chord(F) c'4@chord(G) <c' e' g'>4@chord c'4@chord(F 133211) | } }
            form main { A }
            score main { staff gt }
            """;
        Assert.Equal(new[] { "frame:xx3211", "frame:x32010|032010", "frame:133211" },
            Laid(book).ArticulationLayouts.OrderBy(a => a.X).Select(a => a.Glyph));
    }

    /// <summary>A section's entry applies to the chords WRITTEN in that section — an
    /// <c>@chord</c>'s note, a by-part row's inner section — and the song's elsewhere.</summary>
    [Fact]
    public void TheTable_ASectionsEntryWins_InThatSectionOnly()
    {
        const string layout = "layout { chordDiagrams guitar { F xx3211  section B { F 133211  G } } }\n";
        const string byPart = layout + """
            octave absolute
            part gt { clef treble }
            section A { gt { c'1 | c'1 | } }
            section B { gt { c'1 | c'1 | } }
            chords prog { section A { F | G | }  section B { F | G | } }
            form main { A B }
            score main { chords prog  staff gt }
            """;
        Assert.Equal(new string?[] { "xx3211", null, "133211", "320003|210003" }, RowFrames(byPart));
        const string marks = layout + """
            octave absolute
            part gt { clef treble }
            section A { gt { c'1@chord(F) | c'1@chord(G) | } }
            section B { gt { c'1@chord(F) | c'1@chord(G) | } }
            form main { A B }
            score main { staff gt }
            """;
        Assert.Equal(new[] { "frame:xx3211", "frame:133211", "frame:320003|210003" },
            Laid(marks).ArticulationLayouts.OrderBy(a => a.X).Select(a => a.Glyph));
        // A row written inside the section takes the section's entry too.
        const string flat = """
            layout { chordDiagrams guitar { section B { F 133211 } } }
            octave absolute
            part gt { clef treble }
            section A { gt { c'1 | }  chords prog { F | } }
            section B { gt { c'1 | }  chords prog { F | } }
            form main { A B }
            score main { chords prog  staff gt }
            """;
        Assert.Equal(new string?[] { null, "133211" }, RowFrames(flat));
    }

    /// <summary>A chord the table lists by name alone but that has no shape on the tuning is
    /// warned like an <c>all</c> score's (it is meant to draw); an unlisted one is not.</summary>
    [Fact]
    public void TheTable_WarnsAListedChordWithNoShapeOnTheTuning()
    {
        var w = Warnings(Song("layout { chordDiagrams ukulele { C13  G } }\n", "C13 | G |", "c'1 | c'1 |"));
        Assert.Contains("C13 has no chord diagram on 'ukulele'", Assert.Single(w).Message);
        Assert.Empty(Warnings(Song("layout { chordDiagrams ukulele { G } }\n", "C13 | G |", "c'1 | c'1 |")));
    }

    [Fact]
    public void TheTable_TheTwinAndMusicXmlDrawTheListedChords()
    {
        string ly = Twin(Song(Table, "C | F | G |"));
        Assert.Matches(@"\\new ChordNames \\\w+\s+\\new FretBoards \\\w+Frets", ly);
        Assert.Contains("\\chordmode { f } #guitar-tuning \"x;x;3;2;1;1;\"", ly);
        // C's default and F's table shape; G a silent slot.
        Assert.Equal(2, Regex.Matches(ly, @"\\storePredefinedDiagram").Count);
        Assert.Matches(@"\bs1\b", FretTrack(ly));
        // A row none of whose chords the table lists gets no FretBoards context at all.
        Assert.DoesNotContain("FretBoards", Twin(Song(Table, "G | Am |", "c'1 | c'1 |")));

        string book = Table + """
            octave absolute
            part gt { clef treble }
            section A { gt { c'2@chord(F) c'2@chord(G) | } }
            form main { A }
            score main { staff gt }
            """;
        string twin = Twin(book);
        Assert.Contains("\\fret-diagram-terse \"x;x;3;2;1;1;\"", twin);
        Assert.Single(Regex.Matches(twin, "fret-diagram-terse"));
        var harmonies = Regex.Matches(Xml(book), "<harmony>.*?</harmony>", RegexOptions.Singleline);
        Assert.Equal(2, harmonies.Count);
        Assert.Contains("<frame>", harmonies[0].Value);
        Assert.DoesNotContain("<frame>", harmonies[1].Value);
    }

    [Fact]
    public void Hover_InATableScore_ShowsTheShapeANameDraws()
    {
        string doc = Table + """
            octave absolute
            part gt { clef treble }
            section A {
              gt { c'2@chord(F) c'2@chord(G) | }
              chords prog { C . F G | }
            }
            form main { A }
            score main { chords prog  staff gt }
            """;
        Assert.Contains("guitar: `xx3211` (layout)", HoverAt(doc, "@chord(F)"));
        Assert.Contains("adds a chord diagram (guitar: 320003)", HoverAt(doc, "@chord(G)"));
        Assert.Contains("guitar: `x32010` (default) — shape 1 of ", HoverAt(doc, "C . F", 0));
        Assert.Contains("guitar: `xx3211` (layout)", HoverAt(doc, "F G |", 0));
        Assert.Contains("adds a chord diagram", HoverAt(doc, "G |", 0));
    }

    // ================================================================ the capo (K5 ④)
    // Owner's design (HANDOFF §2 K2, 2026-09-28; built 2026-09-29): `chordDiagrams [TUNING] capo N
    // [all] [{ … }]` — the diagrams are the shapes PRESSED above the capo, the names the pressed
    // chords' (`chordNames shape` — the default — | sounding | both: "E♭ (C)"), "Capo N" on the
    // header's instrument line; a pressed name is spelled in the key N semitones below the key at
    // the bar (owner's decision 2026-09-29). The editor ranks the frets by their barre chords.

    private const string Capo3 = "layout { chordDiagrams guitar capo 3 }\n";
    private const string Capo3All = "layout { chordDiagrams guitar capo 3 all }\n";

    [Fact]
    public void TheKey_TakesACapo()
    {
        var (plan, problems) = Layout("chordDiagrams guitar capo 3");
        Assert.Empty(problems);
        Assert.Equal(("guitar", 3, false), (plan.ChordDiagrams, plan.Chords.Capo, plan.ChordDiagramsAll));
        Assert.True(Layout("chordDiagrams capo 3").Plan is { ChordDiagrams: null, Chords.Capo: 3 });
        Assert.True(Layout("chordDiagrams guitar capo 3 all { Eb x32010 }").Plan
            is { ChordDiagrams: "guitar", Chords.Capo: 3, ChordDiagramsAll: true, ChordDiagramTable.Song.Length: 1 });
        Assert.True(Layout("chordDiagrams capo 11 all").Plan is { Chords.Capo: 11, ChordDiagramsAll: true });
        // A key written again is written whole: no capo in the override drops the named block's.
        Assert.Equal(0, Layout("chordDiagrams guitar").Plan.Chords.Capo);
        // chordNames: what a name shows under the capo, the default `shape`.
        Assert.Equal(ChordNameMode.Shape, Layout("chordDiagrams guitar capo 3").Plan.Chords.Names);
        Assert.Equal(ChordNameMode.Both, Layout("chordNames both").Plan.Chords.Names);
        Assert.Equal(ChordNameMode.Sounding, Layout("chordDiagrams capo 2  chordNames sounding").Plan.Chords.Names);
    }

    [Theory]
    [InlineData("chordDiagrams guitar capo", "'capo' takes the fret the capo is on, 1 to 11")]
    [InlineData("chordDiagrams guitar capo x", "'capo' takes the fret the capo is on, 1 to 11")]
    [InlineData("chordDiagrams guitar capo 12", "'capo' takes the fret the capo is on, 1 to 11")]
    [InlineData("chordDiagrams guitar capo 0", "'capo 0' is no capo - leave the 'capo' out")]
    [InlineData("chordDiagrams none capo 3", "'none' draws no diagram, so it takes no 'capo'")]
    [InlineData("chordDiagrams guitar all capo 3", "the capo comes before 'all': write 'chordDiagrams guitar capo N all'")]
    [InlineData("chordDiagrams guitar capo 3 capo 3", "'capo' is written twice")]
    [InlineData("chordDiagrams capo 3 guitar", "the tuning comes first: write 'chordDiagrams guitar capo 3'")]
    [InlineData("chordDiagrams guitar capo 3 all all", "'all' is written twice - write 'chordDiagrams guitar capo 3 all'")]
    [InlineData("chordNames Both", "is not a value of 'chordNames'")]
    public void AWrongCapo_IsRefused_NamingTheFix(string entries, string message)
    {
        var (plan, problems) = Layout(entries);
        Assert.Contains(message, Assert.Single(problems).Message);
        Assert.Equal(0, plan.Chords.Capo);
        Assert.Null(plan.ChordDiagrams);
        Assert.Equal(ChordNameMode.Shape, plan.Chords.Names);
    }

    /// <summary>The pressed chord: this chord N semitones down, spelled in the key N semitones
    /// below the key at the bar — the key's own letter, else a natural, else the key's side.</summary>
    [Theory]
    [InlineData("Eb", 3, -3, "C")]        // E♭ major at capo 3: C major
    [InlineData("Bb", 3, -3, "G")]
    [InlineData("Ab/C", 3, -3, "F/A")]
    [InlineData("G#m", 3, 4, "Fm")]       // E major at capo 3: D♭ major — F, not E♯
    [InlineData("Db", 3, 0, "A♯")]        // C major at capo 3: A major — the sharp side
    [InlineData("C/E", 1, 0, "B/D♯")]     // C major at capo 1: B major
    [InlineData("Gb", 6, 0, "C")]         // C major at capo 6: F♯ major (the sharp side of the tritone)
    [InlineData("F", 0, -1, "F")]         // no capo: the chord itself
    public void ThePressedChord_IsSpelledInThePressedKey(string symbol, int capo, int keySharps, string pressed)
        => Assert.Equal(pressed, Parse(symbol).Pressed(capo, keySharps).PrintedSymbol(ChordSpelling.Canonical).Text);

    [Theory]
    [InlineData(-3, 3, 0)]   // E♭ → C
    [InlineData(4, 3, -5)]   // E → D♭
    [InlineData(0, 1, 5)]    // C → B
    [InlineData(0, 6, 6)]    // C → F♯ (not G♭)
    [InlineData(2, 2, 0)]    // D → C
    public void ThePressedKey_IsSevenFifthsDownPerSemitone(int keySharps, int capo, int pressedSharps)
        => Assert.Equal(pressedSharps, ChordStructure.PressedKeySharps(keySharps, capo));

    /// <summary>Under the capo the page names the PRESSED chords (the default), the sounding
    /// ones under <c>chordNames sounding</c>, both under <c>both</c>; in the key at the bar.</summary>
    [Fact]
    public void UnderACapo_TheNamesAreThePressedChords()
    {
        string[] Names(string book) => [.. Collected(book).ChordNames.OrderBy(c => c.MeasureIndex).Select(c => c.ChordText)];
        Assert.Equal(new[] { "C", "G", "F/A" }, Names(Song(Capo3, "Eb | Bb | Ab/C |")));
        Assert.Equal(new[] { "E♭", "B♭", "A♭/C" }, Names(Song("layout { chordDiagrams guitar capo 3  chordNames sounding }\n", "Eb | Bb | Ab/C |")));
        Assert.Equal(new[] { "E♭ (C)", "B♭ (G)", "A♭/C (F/A)" }, Names(Song("layout { chordDiagrams guitar capo 3  chordNames both }\n", "Eb | Bb | Ab/C |")));
        // The key at the bar spells the pressed name: in E major a G♯m at capo 3 is Fm; a Roman
        // degree is the sounding key's and is not moved.
        Assert.Equal(new[] { "Fm", "Fm" }, Names(Song("key e major\n" + Capo3, "G#m | IIIm |", "c'1 | c'1 |")));
        // An @chord too, and a bare @chord's derived name.
        string book = Capo3 + """
            octave absolute
            part gt { clef treble }
            section A { gt { c'2@chord(Eb) <c' e' g'>2@chord | } }
            form main { A }
            score main { staff gt }
            """;
        Assert.Equal(new[] { "C", "A" }, Names(book));
        Assert.Equal("Capo 3", Collected(book).Instrument);
        Assert.Null(Collected(Song(Guitar, "C |", "c'1 |")).Instrument);
    }

    /// <summary>A <c>both</c> name raises each name's quality — "E♭m7 (Cm7)" has two raised
    /// runs — and the header band makes room for the instrument line.</summary>
    [Fact]
    public void UnderACapo_ABothNameRaisesBothQualities()
    {
        string book = "layout { chordDiagrams guitar capo 3  chordNames both }\n"
            + "part m { clef treble }\nsection A { m { c4 d e f | } chords prog { Ebm7 | } }\nform main { A }\nscore main { chords prog  staff m }\n";
        var score = SvgGenerator.CollectScore(SyntaxTree.Parse(book), RenderSpecParser.FindFirst(SyntaxTree.Parse(book)));
        var item = Assert.Single(score.ChordNames);
        Assert.Equal("E♭m7 (Cm7)", item.ChordText);
        Assert.Equal(3, item.SuperFrom);
        Assert.Equal(8, item.BracketSuperFrom);
        var pieces = ChordNameGlyphRun.Pieces(score.TextMetrics, item.ChordText, item.SuperFrom, item.BracketSuperFrom);
        double up = ChordNameGlyphRun.SuperRaise(score.TextMetrics);
        Assert.Equal(new[] { ("E", false), ("", false), ("m", false), ("7", true), (" (Cm", false), ("7", true), (")", false) },
            pieces.Select(p => (p.Text, p.Raise > up / 2)));
        Assert.NotNull(HeaderBand.Build(null, null, score.TextMetrics, instrument: "Capo 3")?.ComposerBaseline);
    }

    /// <summary>The diagrams are the pressed shapes: a written or listed shape as it stands, the
    /// default of the pressed chord; a written shape is checked against the pressed chord.</summary>
    [Fact]
    public void UnderACapo_TheDiagramsAreThePressedShapes()
    {
        Assert.Equal(new string?[] { "x32010|032010", "320003|210003" }, RowFrames(Song(Capo3All, "Eb | Bb |", "c'1 | c'1 |")));
        Assert.Equal(new string?[] { "x32013", null }, RowFrames(Song(Capo3, "Eb(x32013) | Bb |", "c'1 | c'1 |")));
        Assert.Equal(new string?[] { "133211" }, RowFrames(Song("layout { chordDiagrams guitar capo 3 { Ab 133211 } }\n", "Ab |", "c'1 |")));
        // LYS1039 reads the pressed chord: x32010 is the pressed E♭ (C); 320003 is not.
        Assert.Empty(Mismatches(Song(Capo3, "Eb(x32010) |", "c'1 |")));
        Assert.Contains("'320003' sounds G B D, which is G, not Eb", Assert.Single(Mismatches(Song(Capo3, "Eb(320003) |", "c'1 |"))).Message);
        Assert.Empty(Mismatches("layout { chordDiagrams guitar capo 3 { Eb x32010 } }\npart m { }\nsection A { m { c'1 | } }\nform main { A }\nscore main { staff m }\n"));
        // A listed chord with no pressed shape on the tuning warns (C13 → A13 on the ukulele).
        Assert.Contains("C13 has no chord diagram on 'ukulele'",
            Assert.Single(Warnings(Song("layout { chordDiagrams ukulele capo 3 { C13 } }\n", "C13 |", "c'1 |"))).Message);
    }

    private static IReadOnlyList<LilySharp.Core.Syntax.Diagnostic> Mismatches(string book)
        => SemanticValidation.Run(SyntaxTree.Parse(book)).Where(d => d.Code == DiagnosticCodes.ChordShapeMismatch).ToList();

    [Fact]
    public void UnderACapo_TheTwinAndMusicXmlFollowThePage()
    {
        var exporter = new LilyPondExporter();
        string ly = exporter.Export(SyntaxTree.Parse(Song(Capo3All, "Eb | Bb |", "c'1 | c'1 |")));
        // The pressed chords in \chordmode (LilyPond names them as the page does), the FretBoards
        // tables keyed by them, "Capo 3" on the header's instrument line.
        Assert.Matches(@"progChords = \\chordmode \{\s+c1 \|\s+g1 \|", ly);
        Assert.Contains("\\storePredefinedDiagram #lysFretsA \\chordmode { c } #guitar-tuning", ly);
        Assert.Contains("instrument = \"Capo 3\"", ly);
        Assert.Empty(exporter.Warnings.Where(w => w.Contains("chordNames", StringComparison.Ordinal)));
        // sounding: the sounding chords are named, the diagrams stay pressed.
        string sounding = Twin(Song("layout { chordDiagrams guitar capo 3 all  chordNames sounding }\n", "Eb | Bb |", "c'1 | c'1 |"));
        Assert.Matches(@"progChords = \\chordmode \{\s+ees1 \|\s+bes1 \|", sounding);
        Assert.Contains("\\chordmode { c } #guitar-tuning", sounding);
        // both: the sounding chord, named "E♭ (C)" by lysCapoBoth from the pressed chord each entry
        // carries (LilyPond 2.26 prints the page's names — Lab sessions/p709/both); the FretBoards
        // track keeps the pressed chords bare. Until 2026-09-30 named sounding alone, and warned.
        var both = new LilyPondExporter();
        string bothLy = both.Export(SyntaxTree.Parse(Song("layout { chordDiagrams guitar capo 3 all  chordNames both }\n",
            "Eb | Ab/C |", "c'1 | c'1 |")));
        Assert.Single(Regex.Matches(bothLy, @"#\(define \(lysCapoBoth pressed\)"));
        Assert.Matches(@"progChords = \\chordmode \{\s+"
            + @"\\once \\set chordNameFunction = #\(lysCapoBoth #\{ \\chordmode \{ c \} #\}\) ees1 \|\s+"
            + @"\\once \\set chordNameFunction = #\(lysCapoBoth #\{ \\chordmode \{ f/a \} #\}\) aes1/c \|", bothLy);
        Assert.Equal(2, Regex.Matches(bothLy, @"lysCapoBoth #\{").Count);   // the ChordNames entries alone
        Assert.Contains("\\chordmode { c } #guitar-tuning", bothLy);
        Assert.DoesNotContain(both.Warnings, w => w.Contains("chordNames", StringComparison.Ordinal));
        // No capo: `both` has nothing to add.
        Assert.DoesNotContain("lysCapoBoth", Twin(Song("layout { chordNames both }\n", "Eb |", "c'1 |")));
        // MusicXML: the <harmony> is the sounding chord (data), the <frame> the pressed shape.
        string book = Capo3All + """
            octave absolute
            part gt { clef treble }
            section A { gt { c'1@chord(Eb) | } }
            form main { A }
            score main { staff gt }
            """;
        var harmony = Regex.Match(Xml(book), "<harmony>.*?</harmony>", RegexOptions.Singleline).Value;
        Assert.Contains("<root-step>E</root-step>", harmony);
        Assert.Contains("<root-alter>-1</root-alter>", harmony);
        Assert.Contains("<frame>", harmony);
        Assert.Contains("\\fret-diagram-terse \"x;3;2;o;1;o;\"", Twin(book));
    }

    /// <summary>
    /// The capo reaches MusicXML as <c>&lt;staff-details&gt;&lt;capo&gt;</c> in the opening
    /// attributes of every part whose harmonies carry a <c>&lt;frame&gt;</c> — the frames are the
    /// PRESSED shapes, and this is what tells a reader so (left open by 第665; 2026-09-29). A
    /// part with no frame under it says nothing, and a score without a capo writes none.
    /// </summary>
    [Fact]
    public void UnderACapo_MusicXmlWritesTheCapo_OnEveryPartWithAFrame()
    {
        string Book(string layout) => layout + """
            octave absolute
            part gt { clef treble }
            part bs { clef bass }
            section A { gt { c'1@chord(Eb) | } bs { c1 | } chords prog { Eb | } }
            form main { A }
            score main { chords prog  staff gt  staff bs }
            """;
        var doc = new MusicXmlExporter().Export(SyntaxTree.Parse(Book(Capo3All)));
        var gt = doc.Parts.Single(p => p.Name == "gt");
        var bs = doc.Parts.Single(p => p.Name == "bs");
        Assert.Equal(3, gt.Measures[0].Attributes?.Capo);
        Assert.Null(bs.Measures[0].Attributes?.Capo);
        // In the document: one <staff-details><capo> per framed part, after the clef and before
        // any <transpose> (the schema's order), in the first measure's attributes.
        string xml = doc.ToXml().ToString();
        Assert.Single(Regex.Matches(xml, @"<staff-details>\s*<capo>3</capo>\s*</staff-details>"));
        var gtAttrs = Regex.Match(xml, @"<part id=""P1"">.*?</attributes>", RegexOptions.Singleline).Value;
        Assert.Matches(@"</clef>\s*<staff-details>", gtAttrs);
        // The row's part on a lead sheet (no staff) carries frames too, so it carries the capo.
        var sheet = new MusicXmlExporter().Export(SyntaxTree.Parse(Capo3All + """
            section A { chords prog { Eb | Bb | } }
            form main { A }
            score main { chords prog }
            """));
        Assert.Equal(3, sheet.Parts.Single().Measures[0].Attributes?.Capo);
        // No capo, no element — with or without frames.
        Assert.DoesNotContain("<capo>", Xml(Book(All)));
        Assert.DoesNotContain("<capo>", Xml(Book(Guitar)));
    }

    /// <summary>The capo suggestion: each fret 0–7 with the barre chords the file's chords take
    /// there, fewest first — F and C on the guitar: capo 3 (D and A) needs none.</summary>
    [Fact]
    public void TheCapoSuggestion_RanksTheFretsByTheirBarreChords()
    {
        var root = SyntaxTree.Parse(Song(Guitar, "F | C | Bb |", "c'1 | c'1 | c'1 |")).GetRoot();
        Assert.Equal(new[] { "F", "C", "Bb" }, CapoAdvisor.ChordsOf(root).Select(c => c.Symbol));
        var ranking = CapoAdvisor.Rank(root, TuningType.Guitar);
        Assert.Equal(8, ranking.Count);
        var best = ranking[0];
        Assert.Equal(3, best.Capo);          // D A G: no barre
        Assert.Equal(0, best.Barres);
        Assert.Equal("capo 3: 0 barre chords of 3", CapoAdvisor.Describe(best));
        var open = ranking.Single(c => c.Capo == 0);
        Assert.Equal(2, open.Barres);         // F 133211 and B♭ x13331
        Assert.Contains(("F", "133211"), open.BarreChords);
        Assert.Contains("(F 133211, Bb x13331)", CapoAdvisor.Describe(open));
        Assert.True(ranking.Select(c => c.Barres).SequenceEqual(ranking.Select(c => c.Barres).OrderBy(b => b)));

        // The editor: the ranked frets after `capo`, `all` after `capo N`, and the hover.
        static string Ctx(string text) => LilySharpLanguageServer.GetCompletionContext(text, text.Length).ToString();
        Assert.Equal("AfterLayoutChordDiagramsCapo", Ctx("layout {\n  chordDiagrams guitar capo "));
        Assert.Equal("AfterLayoutChordDiagramsCapo", Ctx("layout {\n  chordDiagrams capo "));
        Assert.Equal("AfterLayoutChordDiagramsTuning", Ctx("layout {\n  chordDiagrams guitar capo 3 "));
        Assert.Equal("AfterLayoutChordNames", Ctx("layout {\n  chordNames "));
        string doc = Song(Guitar, "F | C | Bb |", "c'1 | c'1 | c'1 |");
        var items = LilySharpLanguageServer.GetChordDiagramCapoCompletions(doc, "guitar").Items;
        Assert.Equal("3", items[0].Label);
        Assert.Contains("0 barre chords of 3", items[0].Detail);
        Assert.Contains("no capo", items.Single(i => i.Label == "0").Detail);
        string capoDoc = Song(Capo3, "F | C | Bb |", "c'1 | c'1 | c'1 |");
        string? hover = HoverAt(capoDoc, "capo 3", 1);
        Assert.Contains("**Capo**", hover);
        Assert.Contains("capo 3: 0 barre chords of 3", hover);
        Assert.Contains("**Capo**", HoverAt(capoDoc, "capo 3", 6));
    }

    /// <summary>The editor inside a shape table (2026-09-30): the chords the file names that the
    /// scope does not list yet, then the key's, then <c>section</c> — not the layout's keys, which
    /// it offered until then; after <c>section</c> the file's section names.</summary>
    [Fact]
    public void TheEditor_InsideTheShapeTable_OffersTheFilesChords()
    {
        static string Ctx(string text) => LilySharpLanguageServer.GetCompletionContext(text, text.Length).ToString();
        Assert.Equal("ChordDiagramTable", Ctx("layout {\n  chordDiagrams guitar { "));
        Assert.Equal("ChordDiagramTable", Ctx("layout {\n  chordDiagrams { Cm7 x35343 "));
        Assert.Equal("ChordDiagramTable", Ctx("layout chart { chordDiagrams ukulele capo 2 all {\n  C "));
        Assert.Equal("ChordDiagramTable", Ctx("layout { chordDiagrams guitar { C  section A { F "));
        Assert.Equal("ChordDiagramTableSection", Ctx("layout { chordDiagrams guitar { C  section "));
        // Out of the table the layout is the layout again; a brace elsewhere is no table.
        Assert.Equal("LayoutBlock", Ctx("layout { chordDiagrams guitar { C } "));
        Assert.Equal("LayoutBlock", Ctx("layout { chordDiagrams guitar { C  section A { F } } "));
        Assert.NotEqual("ChordDiagramTable", Ctx("section A { gt { "));

        (string Doc, int Caret) At(string layout)
        {
            string doc = Song(layout, "F | C | Bb |");
            int caret = doc.IndexOf('▮');
            return (doc.Remove(caret, 1), caret);
        }
        // The file's chords first, less the ones this scope lists; `section` at the table's level.
        var (doc, caret) = At("layout { chordDiagrams guitar { C x32010  ▮ } }\n");
        Assert.Equal("ChordDiagramTable", LilySharpLanguageServer.GetCompletionContext(doc, caret).ToString());
        var items = LilySharpLanguageServer.GetChordDiagramTableCompletions(doc, caret).Items;
        Assert.Equal(new[] { "F", "Bb" }, items.Take(2).Select(i => i.Label));
        Assert.DoesNotContain(items, i => i.Label == "C" && i.Detail!.Contains("file"));
        Assert.Contains(items, i => i.Label == "Dm");                   // the key's (C major)
        Assert.Contains(items, i => i.Label == "section");
        Assert.DoesNotContain(items, i => LanguageVocabulary.LayoutKeys.Contains(i.Label));
        Assert.Equal(items.Length, items.Select(i => i.Label).Distinct().Count());
        // In a section's table: that scope's own list, and no `section`.
        (doc, caret) = At("layout { chordDiagrams guitar { C x32010  section A { F 133211  ▮ } } }\n");
        items = LilySharpLanguageServer.GetChordDiagramTableCompletions(doc, caret).Items;
        Assert.Equal(new[] { "C", "Bb" }, items.Take(2).Select(i => i.Label));
        Assert.DoesNotContain(items, i => i.Label == "section");
        // After `section`: the sections, less the ones the table has; the block comes with it.
        (doc, caret) = At("layout { chordDiagrams guitar { section ▮ } }\n");
        Assert.Equal("ChordDiagramTableSection", LilySharpLanguageServer.GetCompletionContext(doc, caret).ToString());
        var section = Assert.Single(LilySharpLanguageServer.GetChordDiagramTableSectionCompletions(doc, caret).Items);
        Assert.Equal(("A", "A {\n\t$0\n}"), (section.Label, section.InsertText));
        (doc, caret) = At("layout { chordDiagrams guitar { section A { F }  section ▮ } }\n");
        Assert.Empty(LilySharpLanguageServer.GetChordDiagramTableSectionCompletions(doc, caret).Items);
    }

    /// <summary>The hover and the step read the pressed chord: under capo 3 an E♭ alone shows C's
    /// default in an <c>all</c> score, and the add hint names it in a plain one.</summary>
    [Fact]
    public void UnderACapo_TheHoverAndTheStepUseThePressedChord()
    {
        string doc = Capo3All + """
            octave absolute
            part gt { clef treble }
            section A { gt { c'1@chord(Eb) | } }
            form main { A }
            score main { staff gt }
            """;
        Assert.Contains("guitar: `x32010` (default) — shape 1 of ", HoverAt(doc, "@chord(Eb)"));
        string plain = Capo3 + doc[Capo3All.Length..];
        Assert.Contains("adds a chord diagram (guitar: x32010)", HoverAt(plain, "@chord(Eb)"));
    }

    // ================================================================ the chord list (K5 ⑤)
    // Owner's design 2026-09-29: `layout { chordList true }` lists every chord the score names,
    // each once in order of first appearance, with the diagram it draws (its usual shape when it
    // draws none), under the title — rows of even counts, each centred (the owner's choice after
    // seeing a left-set row and a 12 + 4 split).

    private const string List = "layout { chordDiagrams guitar  chordList true }\n";

    [Fact]
    public void TheKey_TakesTrueOrFalse()
    {
        Assert.True(Layout("chordList true").Plan.ChordList);
        Assert.False(Layout("chordList false").Plan.ChordList);
        Assert.False(Layout("chordDiagrams guitar").Plan.ChordList);
        var (plan, problems) = Layout("chordList yes");
        Assert.Contains("'yes' is not a value of 'chordList'", Assert.Single(problems).Message);
        Assert.False(plan.ChordList);
    }

    private static (string Text, string? Spec)[] Listed(string book)
        => [.. ChordListBand.EntriesOf(Collected(book)).Select(e => (e.Text, e.Spec))];

    [Fact]
    public void TheChordList_ListsEachChordOnce_InOrderOfFirstAppearance()
    {
        // A row: each chord once, the written shape where one is written, else the usual shape.
        Assert.Equal(new (string, string?)[] { ("C", "x32010|032010"), ("F", "xx3211"), ("G", "320003|210003"), ("Am", "x02210|002310") },
            Listed(Song(List, "C | F(xx3211) | G | C | Am |", "c'1 | c'1 | c'1 | c'1 | c'1 |")));
        // "N.C." names no chord; a degree lists as the chord it resolves to.
        Assert.Equal(new (string, string?)[] { ("C", "x32010|032010"), ("F", "133211|134211|6-1@1") },
            Listed(Song(List, "C | r | IV |", "c'1 | c'1 | c'1 |")));
        // An @chord joins the row's chords in order of appearance; a bare @chord by its derived name.
        string book = List + """
            octave absolute
            part gt { clef treble }
            section A {
              gt { c'2@chord(Dm7) <e' g' b'>2@chord | c'1 | }
              chords prog { C | G |  }
            }
            form main { A }
            score main { chords prog  staff gt }
            """;
        Assert.Equal(new[] { "C", "Dm7", "Em", "G" }, Listed(book).Select(e => e.Text));
        // Under `chordDiagrams none` the names alone; under a capo the pressed names and shapes.
        Assert.All(Listed(Song("layout { chordDiagrams none  chordList true }\n", "C | F |", "c'1 | c'1 |")), e => Assert.Null(e.Spec));
        Assert.Equal(new (string, string?)[] { ("C", "x32010|032010"), ("G", "320003|210003") },
            Listed(Song("layout { chordDiagrams guitar capo 3  chordList true }\n", "Eb | Bb |", "c'1 | c'1 |")));
        // Without the key, nothing.
        Assert.Empty(Listed(Song(Guitar, "C | F |", "c'1 | c'1 |")));
    }

    /// <summary>The rows hold as nearly equal counts as the page's width allows, and each is
    /// centred on the page: 16 six-string shapes on A4 make 8 + 8 (12 would fit a row), 6 one row.</summary>
    [Fact]
    public void TheChordList_RowsAreEvenAndCentred()
    {
        var fonts = Collected(Song(List, "C |", "c'1 |")).TextMetrics;
        var options = new LayoutOptions();
        static ChordListEntry Cell(int i) => new($"C{i}", -1, -1, "x32010");
        var band = HeaderBand.WithChordList(null, [.. Enumerable.Range(0, 16).Select(Cell)], fonts,
            options.PageWidth, options.MarginLeft, options.ContentWidth)!;
        var rows = band.ChordList!.Cells.GroupBy(c => c.NameBaseline).OrderBy(g => g.Key).Select(g => g.OrderBy(c => c.X).ToList()).ToList();
        Assert.Equal(new[] { 8, 8 }, rows.Select(r => r.Count));
        double centre = options.MarginLeft + options.ContentWidth / 2;
        foreach (var row in rows)
            Assert.Equal(centre, (row[0].X + row[^1].X + row[^1].Width) / 2, 5);
        Assert.True(rows[1][0].NameBaseline > rows[0][0].GridBottom);
        Assert.Equal(band.ChordList.Depth, band.Depth);
        Assert.True(band.Width > 0 && band.Width <= options.ContentWidth);
        // 6 chords: one row; 13 (one over a row): 7 + 6.
        Assert.Single(HeaderBand.WithChordList(null, [.. Enumerable.Range(0, 6).Select(Cell)], fonts,
            options.PageWidth, options.MarginLeft, options.ContentWidth)!.ChordList!.Cells.GroupBy(c => c.NameBaseline));
        Assert.Equal(new[] { 7, 6 }, HeaderBand.WithChordList(null, [.. Enumerable.Range(0, 13).Select(Cell)], fonts,
            options.PageWidth, options.MarginLeft, options.ContentWidth)!.ChordList!.Cells
            .GroupBy(c => c.NameBaseline).OrderBy(g => g.Key).Select(g => g.Count()));
    }

    /// <summary>The list is part of the header band: the page's chain and its draw see one
    /// column, a title-less book gets a band for the list alone, and the twin writes it as a
    /// markup before the score.</summary>
    [Fact]
    public void TheChordList_IsPartOfTheHeader_AndTheTwinWritesIt()
    {
        string book = Song(List, "C | F(xx3211) |", "c'1 | c'1 |");
        var page = Laid(book).Pages[0];
        Assert.NotNull(page.Header?.ChordList);
        Assert.Equal(2, page.Header!.ChordList!.Cells.Length);
        Assert.Null(page.Header.TitleBaseline);
        Assert.Null(Laid(Song(Guitar, "C |", "c'1 |")).Pages[0].Header);
        var titled = Laid("title \"T\"\n" + book).Pages[0].Header!;
        Assert.NotNull(titled.TitleBaseline);
        Assert.True(titled.ChordList!.Cells[0].NameBaseline > titled.TitleBaseline);

        string ly = Twin(book);
        int markup = ly.IndexOf("\\markup \\fill-line { \\line { \\center-column { \"C\" \\fret-diagram-terse #\"x;3;2;o;1;o;\" } \\center-column { \"F\" \\fret-diagram-terse #\"x;x;3;2;1;1;\" } } }", StringComparison.Ordinal);
        Assert.True(markup >= 0, ly);
        Assert.True(markup < ly.IndexOf("\\score {", StringComparison.Ordinal));
        Assert.DoesNotContain("\\fill-line", Twin(Song(Guitar, "C |", "c'1 |")));
    }

    /// <summary>The hover on a ukulele part counts the shape among the ukulele's order (K5 ⑥).</summary>
    [Fact]
    public void Hover_OnTheUkulele_CountsTheOrder()
    {
        string doc = """
            octave absolute
            part uk { instrument ukulele }
            section A { uk { c'2@chord(C 0003) c'2@chord(Cmaj9) | } }
            form main { A }
            score main { staff uk }
            """;
        Assert.Contains("ukulele: `0003` (written) — shape 1 of 39", HoverAt(doc, "@chord(C 0003)"));
        Assert.Contains("adds a chord diagram (ukulele: 4203)", HoverAt(doc, "@chord(Cmaj9)"));
    }

    /// <summary>The block form is the shape table's alone: any other key's brace is refused
    /// where it stands, as before (the parser's rule).</summary>
    [Fact]
    public void OnlyChordDiagrams_OpensABlock()
    {
        var tree = SyntaxTree.Parse("layout { barNumbers every 4 { x }  markTempo beside }\n");
        var refusal = Assert.Single(tree.Diagnostics, d => d.Message.Contains("does not open a block", StringComparison.Ordinal));
        Assert.Contains("only 'chordDiagrams' takes one", refusal.Message);
        Assert.Empty(SyntaxTree.Parse("layout { chordDiagrams guitar { C#m7 x46654  section A { F/A x03211 } } }\n").Diagnostics);
    }

    /// <summary>
    /// A diagram at the END of a line — a chords row's last symbol, on the bar's head or on a
    /// later beat, on a lead sheet or a chords-only grid; an <c>@chord</c> on the line's last
    /// note — keeps its whole box, the "Nfr" label included, inside the line: the row's
    /// footprint (<see cref="ChordNameEngraver.FootprintWidth"/>) and the diagram's rod
    /// (<c>SpacingRules.ApplyFretFrameSpacing</c>) both price it to the bar's edge.
    /// </summary>
    /// <remarks>
    /// The HANDOFF carried "the 5fr of a diagram at the line end sticks out to the right"
    /// from session 663 (the first diagrams). Session 683 could not reproduce it on these
    /// three books nor on the site's own chord-shapes example — session 668's port of
    /// fret-diagrams.scm moved the label to LilyPond's position — so this pins the claim
    /// instead: on every line of every book here the label ends before the line does.
    /// </remarks>
    [Fact]
    public void ADiagramAtTheLineEnd_KeepsItsFretLabelInsideTheLine()
    {
        foreach (var book in new[]
        {
            "layout { chordDiagrams guitar all }\noctave absolute\npart m { clef treble }\n"
            + "section A { m { c'1 | c'1 | c'1 | c'2 c'2 | } chords prog { C | G | F | C A(x57765) | } }\n"
            + "form main { A }\nscore main { chords prog  staff m }\n",
            "layout { chordDiagrams guitar all }\n"
            + "section A { chords prog { C | G | F | C A(x57765) | C | G | F | C A(x57765) | C | G | F | C A(x57765) | C | G | F | C A(x57765) | } }\n"
            + "form main { A }\nscore main { chords prog }\n",
            "layout { chordDiagrams guitar all }\noctave absolute\npart m { clef treble }\n"
            + "section A { m { c'4 d' e' f'@chord(A x57765) | } }\nform main { A }\nscore main { staff m }\n",
        })
        {
            var tree = SyntaxTree.Parse(book);
            Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
            var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
            var lay = new LayoutEngine().Layout(score);
            int seen = 0;
            foreach (var system in lay.Systems)
            {
                var (barlineRight, notationRight, tabRight) = Core.Rendering.SharedRenderer.StaffRightEdges(score, system);
                double lineRight = Math.Max(barlineRight, Math.Max(notationRight, tabRight));
                var measures = system.Measures.Select(m => m.MeasureIndex).ToHashSet();
                // The row's diagram: its box stands with its left edge on the symbol's column.
                foreach (var c in lay.ChordNameLayouts.Where(c => c.FrameSpec != null && measures.Contains(c.MeasureIndex)))
                {
                    double right = c.X + ChordNameEngraver.DiagramBox(score.TextMetrics, c.FrameSpec!).Width;
                    Assert.True(right <= lineRight + 1e-6, $"the row's {c.ChordText} diagram ends at {right}, the line at {lineRight}");
                    seen++;
                }
                // The @chord's diagram: a script centred on its note, its ink about that X.
                foreach (var a in lay.ArticulationLayouts.Where(a => ArticulationEngraver.IsDiagramUnderName(a) && measures.Contains(a.MeasureIndex)))
                {
                    double right = a.X + a.Ink.Right;
                    Assert.True(right <= lineRight + 1e-6, $"the @chord diagram ends at {right}, the line at {lineRight}");
                    seen++;
                }
            }
            Assert.True(seen > 0, "the book must draw a diagram");
        }
    }
}
