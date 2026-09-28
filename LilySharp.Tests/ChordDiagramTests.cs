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
        Assert.Equal(new[] { "frame:320003" },
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

    [Fact]
    public void OnARest_TheHoverShowsTheDiagram()
    {
        string doc = RestBook("", "r1@chord(C x32013) | s1@chord(G) |");
        Assert.Contains("guitar: `x32013` (written)", HoverAt(doc, "@chord(C"));
        Assert.Contains("adds a chord diagram", HoverAt(doc, "@chord(G"));
    }
}
