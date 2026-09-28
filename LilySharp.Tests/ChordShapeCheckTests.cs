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
using System.Globalization;
using System.Linq;
using LilySharp.Core.Music;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// LYS1039 — a WRITTEN chord shape checked against its symbol (owner's decision 2026-09-28,
/// reversing "a written shape is the writer's call"): ⑴ a note that is not a chord tone,
/// ⑵ a required tone missing (all but the root and the perfect fifth; for X/Y, a Y that is
/// neither) — the bass is not checked (the owner's second decision that day, after the audit) —
/// one warning per shape listing every problem, on the tuning each score routes the shape to (<see cref="ChordShapes.Mismatch"/>,
/// <see cref="ChordDiagramValidator"/>).
/// </summary>
[Trait("Category", "Unit")]
public class ChordShapeCheckTests
{
    private static ChordStructure Parse(string symbol)
    {
        Assert.True(ChordStructure.TryParseChordEntry(symbol, out var chord), symbol);
        return chord;
    }

    /// <summary>The mismatch of a shape on a tuning, as the message a row entry gets.</summary>
    private static string? Message(string symbol, string shape, TuningType tuning = TuningType.Guitar, bool inRow = false)
        => ChordShapes.Mismatch(ChordShapes.Frets(shape), Tunings.GetTuning(tuning), Parse(symbol)) is { } m
            ? ChordShapes.MismatchMessage(m, shape, null, symbol, inRow)
            : null;

    // ================================================================ the audit

    /// <summary>The predefined entries (every one a Lily# quality spells) that the checker
    /// warns, as "table line" — see <see cref="EveryPredefinedShape_Passes"/>.</summary>
    private static List<string> PredefinedFailures(out int checkedCount, out List<string> messages)
    {
        var failures = new List<string>();
        messages = [];
        checkedCount = 0;
        foreach (var e in PredefinedFretboards.Entries)
        {
            if (PredefinedFretboards.QualityOf(e.Intervals) is not { } quality)
                continue;   // the ukulele's :m6- - no Lily# quality, never found
            var chord = new ChordStructure(e.RootStep, e.RootAlter, quality);
            var frets = e.Frets.Split(' ').Select(f => f == "x" ? -1 : int.Parse(f, CultureInfo.InvariantCulture)).ToArray();
            checkedCount++;
            if (ChordShapes.Mismatch(frets, PredefinedFretboards.TuningOf(e.Table), chord) is { } m)
            {
                failures.Add($"{e.Table} {e.Line}");
                messages.Add($"{e.Table} line {e.Line} {e.LilyPondRoot}:{quality}: "
                             + ChordShapes.MismatchMessage(m, ChordVoicings.Spell(frets), null,
                                 ChordShapes.SourceSymbol(chord) ?? "?", inRow: true));
            }
        }
        return failures;
    }

    /// <summary>
    /// Every shape of Lily#'s predefined tables (guitar, ukulele, mandolin) passes the checker —
    /// the editor's step writes these first, so none may be warned.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The history (owner's decisions 2026-09-28): the first version of the check also asked for
    /// the root lowest and the root sounding, and warned 178 of LilyPond's 646 mappable shapes —
    /// 135 inversions (C7 <c>032310</c>, most diminished, augmented and mandolin shapes) and the
    /// ukulele's 34 rootless ninths. The owner dropped the bass rule and made the root optional;
    /// the seven shapes left warning sounded a note outside their chord, and two more — the
    /// mandolin's C♯dim7/D♭dim7 <c>3210</c>, only B♭ and E — lacked the required diminished
    /// fifth; all nine are left out of Lily#'s table by the generator
    /// (<see cref="TheLeftOutEntries_AreGone_AndTheirChordsFallBack"/>).
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryPredefinedShape_Passes()
    {
        var failures = PredefinedFailures(out int checkedCount, out var messages);
        Assert.Equal(663 - 17 - 9, checkedCount);
        Assert.True(failures.Count == 0, $"{failures.Count} predefined shape(s) warned:\n" + string.Join("\n", messages));
    }

    /// <summary>
    /// LILYSHARP-OWN (owner's decisions 2026-09-28): the nine LilyPond entries that sound a note
    /// outside their chord or lack a required tone are left out of Lily#'s table (audit/fretboards/Generate-PredefinedFretboards.ps1,
    /// the list with file:line and reason) — those chords fall back to the first shape of Lily#'s
    /// order, on the page and in the twin.
    /// </summary>
    [Fact]
    public void TheLeftOutEntries_AreGone_AndTheirChordsFallBack()
    {
        var gone = new (PredefinedFretboards.Table Table, int Line, string Chord, string Frets)[]
        {
            (PredefinedFretboards.Table.Guitar, 160, "D#m", "x x 4 3 4 1"),     // an F
            (PredefinedFretboards.Table.Guitar, 187, "Ebm", "x x 4 3 4 1"),     // an F
            (PredefinedFretboards.Table.Guitar, 244, "Faug", "x x 1 4 4 3"),    // Eb G B
            (PredefinedFretboards.Table.Guitar, 487, "Baug", "x 3 2 0 0 x"),    // C E G B
            (PredefinedFretboards.Table.Ukulele, 279, "Bsus2", "5 1 2 2"),      // a C
            (PredefinedFretboards.Table.Mandolin, 319, "C#aug", "x 6 3 0"),     // G# C E
            (PredefinedFretboards.Table.Mandolin, 369, "Dbaug", "x 6 3 0"),     // Ab C E
            (PredefinedFretboards.Table.Mandolin, 299, "C#dim7", "3 2 1 0"),    // Bb E: no G
            (PredefinedFretboards.Table.Mandolin, 349, "Dbdim7", "3 2 1 0"),    // Bb E: no Abb
        };
        foreach (var (table, line, chord, frets) in gone)
        {
            Assert.DoesNotContain(PredefinedFretboards.Entries, e => e.Table == table && e.Line == line);
            var tuning = PredefinedFretboards.TuningOf(table);
            Assert.Null(PredefinedFretboards.Find(tuning, Parse(chord)));
            var tuningType = table switch
            {
                PredefinedFretboards.Table.Guitar => TuningType.Guitar,
                PredefinedFretboards.Table.Ukulele => TuningType.Ukulele,
                _ => Tunings.Parse("mandolin"),
            };
            // The ukulele's too, since the order reached the re-entrant tunings (2026-09-29, K5 ⑥).
            var fallback = ChordShapes.Default(tuningType, Parse(chord));
            Assert.NotNull(fallback);
            Assert.Equal(ShapeSource.FirstOfOrder, fallback!.Source);
            Assert.NotEqual(frets.Replace(" ", ""), fallback.Spelled);
            Assert.Null(ChordShapes.Mismatch(fallback.Frets, tuning, Parse(chord)));
        }
        Assert.Equal(ChordVoicings.Spell(ChordVoicings.For(Tunings.Guitar, Parse("D#m"), includeStretch: false).Bases[0]),
            ChordShapes.Default(TuningType.Guitar, Parse("D#m"))!.Spelled);
    }

    /// <summary>The twin writes Lily#'s choice, not LilyPond's left-out shape: in a
    /// <c>chordDiagrams all</c> score D♯m's one-shape table holds the fallback.</summary>
    [Fact]
    public void TheTwin_WritesTheFallback_NotTheLeftOutShape()
    {
        string book = """
            layout { chordDiagrams guitar all }
            octave absolute
            part gt { clef treble }
            section A { gt { c'1 | } chords prog { D#m | } }
            form main { A }
            score main { chords prog  staff gt }
            """;
        string ly = new LilySharp.Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(book));
        var fallback = ChordShapes.Default(TuningType.Guitar, Parse("D#m"))!;
        string terse = string.Concat(fallback.Frets.Select(f => f < 0 ? "x;" : f == 0 ? "o;" : $"{f};"));
        Assert.Contains($"#guitar-tuning \"{terse}\"", ly);
        Assert.DoesNotContain("x;x;4;3;4;1;", ly);
    }

    /// <summary>The bass is not checked, on any tuning: F <c>2010</c> on the ukulele (C4 under
    /// F4) and <c>x33211</c> on the guitar pass; the tones are still checked.</summary>
    [Fact]
    public void TheBassIsNotChecked_TheTonesAre()
    {
        Assert.Null(Message("F", "2010", TuningType.Ukulele));
        Assert.Null(Message("F", "x33211"));
        Assert.Null(Message("C7", "032310"));
        Assert.Equal("'x013' for F lacks A (the 3rd) - fret A or write another shape.",
            Message("F", "x013", TuningType.Ukulele, inRow: true));
    }

    /// <summary>Every base of Lily#'s enumeration, both rule sets, passes (the enumeration's V2–V4
    /// are the same or stricter) — the sample the owner named.</summary>
    [Theory]
    [InlineData("C")]
    [InlineData("Cm7")]
    [InlineData("G7")]
    [InlineData("D")]
    [InlineData("F#m7-5")]
    [InlineData("C9")]
    [InlineData("C/G")]
    public void EveryEnumeratedShape_Passes(string symbol)
    {
        foreach (bool stretch in new[] { false, true })
        {
            var bases = ChordVoicings.For(Tunings.Guitar, Parse(symbol), stretch).Bases;
            Assert.NotEmpty(bases);
            foreach (var b in bases)
                Assert.Null(ChordShapes.Mismatch(b, Tunings.Guitar, Parse(symbol)));
        }
    }

    /// <summary>The same on the other guitar-type tunings the step enumerates on.</summary>
    [Theory]
    [InlineData(TuningType.GuitarDropD)]
    [InlineData(TuningType.Guitar7)]
    [InlineData(TuningType.Bass)]
    public void EveryEnumeratedShape_PassesOnOtherTunings(TuningType tuningType)
    {
        var tuning = Tunings.GetTuning(tuningType);
        foreach (string symbol in new[] { "C", "Cm7", "G7", "D", "F#m7-5", "C9" })
            foreach (bool stretch in new[] { false, true })
                foreach (var b in ChordVoicings.For(tuning, Parse(symbol), stretch).Bases)
                    Assert.Null(ChordShapes.Mismatch(b, tuning, Parse(symbol)));
    }

    // ================================================================ the rules, in the validator

    private static IReadOnlyList<Diagnostic> Mismatches(string book)
        => SemanticValidation.Run(SyntaxTree.Parse(book)).Where(d => d.Code == DiagnosticCodes.ChordShapeMismatch).ToList();

    /// <summary>A guitar part (no layout: the guitar) with <paramref name="music"/>.</summary>
    private static string OnGuitar(string music, string layout = "") => layout + $$"""
        octave absolute
        part gt { clef treble }
        section A { gt { {{music}} } }
        form main { A }
        score main { staff gt }
        """;

    private static string Single(string book) => Assert.Single(Mismatches(book)).Message;

    /// <summary>⑴ A shape whose notes are another chord names it — the fix, written out.</summary>
    [Fact]
    public void Rule1_AShapeOfAnotherChord_NamesIt()
    {
        Assert.Equal("'x02210' sounds A C E, which is Am, not C (A is not a tone of C) "
                     + "- write @chord(Am x02210) or another shape.",
            Single(OnGuitar("c'1@chord(C x02210) |")));
        // An inversion is named with its slash bass.
        Assert.Equal("'032010' sounds E G C, which is C/E, not Am (G is not a tone of Am) "
                     + "- write @chord(C/E 032010) or another shape.",
            Single(OnGuitar("c'1@chord(Am 032010) |")));
        // Notes no chord names: the foreign ones are listed.
        Assert.Equal("'x32011' for C sounds F, which is not a tone of C - write a shape of C's tones.",
            Single(OnGuitar("c'1@chord(C x32011) |")));
        // A tuning word binding the shape is kept in the fix.
        Assert.Contains("write @chord(Am guitar x02210) or another shape.",
            Single(OnGuitar("c'1@chord(C guitar x02210) |")));
    }

    /// <summary>⑵ A required tone missing — every tone but the root and the perfect fifth.</summary>
    [Fact]
    public void Rule2_AMissingThirdOrSeventh()
    {
        Assert.Equal("'x3201x' for C7 lacks B♭ (the 7th) - fret B♭ or write another shape.",
            Single(OnGuitar("c'1@chord(C7 x3201x) |")));
        Assert.Equal("'x355xx' for Cm lacks E♭ (the 3rd) - fret E♭ or write another shape.",
            Single(OnGuitar("c'1@chord(Cm x355xx) |")));
        // The perfect fifth may go (C x32x1x: C E C), an altered one may not.
        Assert.Empty(Mismatches(OnGuitar("c'1@chord(C x32x1x) |")));
        Assert.Contains("lacks G♭ (the 5th)", Single(OnGuitar("c'1@chord(Cm7-5 x3x34x) |")));
        // Fewer than three strings: the same rules.
        Assert.Contains("lacks E (the 3rd)", Single(OnGuitar("c'1@chord(C x3xx1x) |")));
        // The root may go too (C xx20x0: E G E), and two tones missing are listed together.
        Assert.Empty(Mismatches(OnGuitar("c'1@chord(C xx20x0) |")));
        Assert.Equal("'x3x01x' for C7 lacks E (the 3rd) and B♭ (the 7th) - fret E and B♭ or write another shape.",
            Single(OnGuitar("c'1@chord(C7 x3x01x) |")));
    }

    /// <summary>The bass is not checked (owner's decision 2026-09-28): an inversion is an
    /// ordinary shape. For X/Y, Y is required only when it is neither X's root nor its perfect
    /// fifth — a bass outside the chord is the reason for the slash.</summary>
    [Fact]
    public void TheBass_IsNotChecked_AndYIsRequiredOnlyOutsideRootAndFifth()
    {
        Assert.Empty(Mismatches(OnGuitar("c'2@chord(C 032010) c'2@chord(C7 032310) |")));
        Assert.Empty(Mismatches(OnGuitar("c'2@chord(C/G x32010) c'2@chord(C/E 032010) |")));
        Assert.Empty(Mismatches(OnGuitar("c'1@chord(C/G x32x1x) |")));     // Y = the fifth: optional
        Assert.Equal("'x32010' for C/F# lacks F♯ (the bass) - fret F♯ or write another shape.",
            Single(OnGuitar("c'1@chord(C/F# x32010) |")));
        Assert.Equal("'x3x01x' for C/E lacks E (the 3rd) - fret E or write another shape.",
            Single(OnGuitar("c'1@chord(C/E x3x01x) |")));
    }

    /// <summary>Several problems: one warning listing them all.</summary>
    [Fact]
    public void SeveralProblems_OneMessage()
    {
        Assert.Equal("'x32011' for C7 sounds F, which is not a tone of C7, and lacks B♭ (the 7th) "
                     + "- write a shape of C7's tones.",
            Single(OnGuitar("c'1@chord(C7 x32011) |")));
        Assert.Equal("'002010' sounds E G A C, which is Am7/E, not G (E A C are not tones of G; it lacks B (the 3rd)) "
                     + "- write @chord(Am7/E 002010) or another shape.",
            Single(OnGuitar("c'1@chord(G 002010) |")));
    }

    private static string Song(string row, string score = "chords prog  staff gt", string layout = "") => layout + $$"""
        octave absolute
        part gt { clef treble }
        part uk { instrument ukulele }
        section A {
          gt { c'1 | c'1 | }
          uk { c'1 | c'1 | }
          chords prog { {{row}} }
        }
        form main { A }
        score main { {{score}} }
        """;

    /// <summary>A row entry is checked the same, the fix in the row's form, at the shape word.</summary>
    [Fact]
    public void ARowEntry_IsChecked_AtItsShape()
    {
        string book = Song("C(x02210) | G(320003) |");
        var d = Assert.Single(Mismatches(book));
        Assert.EndsWith("- write Am(x02210) or another shape.", d.Message);
        Assert.Equal("x02210", book.Substring(d.Span.Start, d.Span.Length));
        // A Roman degree is not checked: its chord is the key's at its bar, the page's to resolve.
        Assert.Empty(Mismatches(Song("IV(x02210) | G |")));
    }

    /// <summary>Each shape on the tuning it is routed to in each score drawing it; a shape no
    /// score uses is not checked; one warning however many scores repeat it.</summary>
    [Fact]
    public void AShape_IsCheckedOnTheTuningItIsRoutedTo()
    {
        // 0000 on the ukulele is C E G A (C6). In a guitar-only score it is unused.
        Assert.Empty(Mismatches(Song("C(x32010 0000) | G |")));
        Assert.Equal("'0000' sounds C E G A, which is C6, not C (A is not a tone of C) - write C6(0000) or another shape.",
            Single(Song("C(x32010 0000) | G |", "chords prog  staff uk")));
        Assert.Single(Mismatches(Song("C(x32010 0000) | G |", layout: "layout { chordDiagrams ukulele }\n")));
        Assert.Empty(Mismatches(Song("C(x32010 0000) | G |", "chords prog  staff uk", "layout { chordDiagrams none }\n")));
        // An @chord: on a ukulele part checked on the ukulele, on a guitar part unused.
        string Uke(string part) => $$"""
            octave absolute
            part {{part}}
            section A { pt { c'1@chord(C 0000) | } }
            form main { A }
            score main { staff pt }
            """;
        Assert.Contains("which is C6, not C", Single(Uke("pt { instrument ukulele }")));
        Assert.DoesNotContain(SyntaxTree.Parse(Uke("pt { clef treble }")).Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(Mismatches(Uke("pt { clef treble }")));
        // Two guitar scores: said once.
        string two = """
            octave absolute
            part gt { clef treble }
            section A { gt { c'1@chord(C x02210) | } }
            form main { A }
            score a { staff gt }
            score b { staff gt }
            """;
        Assert.Single(Mismatches(two));
    }

    /// <summary>A symbol-less <c>@chord</c> names itself from its shape, and <c>@diagram</c> names
    /// nothing: neither is checked.</summary>
    [Fact]
    public void ASymbolLessShape_AndADiagram_AreNotChecked()
    {
        Assert.Empty(Mismatches(OnGuitar("c'2@chord(x02210) c'2@diagram(032011) |")));
        Assert.Empty(Mismatches(OnGuitar("c'2@chord(Cm7) <c' e' g'>2@chord |")));
    }
}
