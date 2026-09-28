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
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

namespace LilySharp.Core.Music;

/// <summary>
/// LilyPond's predefined fret diagrams — the "standard shape" of a chord on the guitar, the
/// ukulele and the mandolin — looked up by (tuning, root pitch class, quality).
/// </summary>
/// <remarks>
/// <para>
/// Owner's decision (HANDOFF §2 K0, 2026-09-28): a chord with no shape written draws the
/// predefined shape, so a songbook that writes no shapes gets the familiar open chords. The
/// data is LilyPond's own (<see cref="Entries"/>, generated — see that file's header), taken
/// from what LilyPond STORED, not from the .ly text, because many entries are barre shapes
/// moved up the neck by Scheme (<c>chord-shape</c>, <c>offset-fret</c>).
/// LILYSHARP-OWN: NINE of LilyPond's entries are LEFT OUT (owner's decisions 2026-09-28) — seven
/// sound a note that is no tone of the chord they are stored for (guitar D♯m/E♭m <c>xx4341</c>,
/// Faug <c>xx1443</c>, Baug <c>x3200x</c>; ukulele Bsus2 <c>5122</c>; mandolin C♯aug/D♭aug
/// <c>x630</c>) and two lack a required tone (mandolin C♯dim7/D♭dim7 <c>3210</c>: only B♭ and
/// E, no diminished fifth), so the editor's step would write a shape LYS1039 then warns. Those chords take
/// the first shape of Lily#'s order instead (non-stretch first) — save the ukulele's Bsus2,
/// which then has no usual shape (Lily# lists no shapes on the re-entrant ukulele) — on the
/// page and in the twin alike (its one-shape tables write Lily#'s choice). The list, with each entry's file:line and
/// reason, is the generator's (audit/fretboards/Generate-PredefinedFretboards.ps1);
/// <c>ChordShapeCheckTests</c> pins it.
/// LILYPOND-REF: ly/predefined-fretboards-init.ly storePredefinedDiagram (lines 65-82) — the key is
///   (tuning . pitches), the value the verbose placement list.
/// </para>
/// <para>
/// ⚠️ LILYPOND KEYS BY SPELLED PITCHES, Lily# by PITCH CLASS. LilyPond stores <c>cis</c> and
/// <c>des</c> as two entries (and looks a chord up by its exact pitches, an octave either way
/// — scm/translation-functions.scm:798-822 get-predefined-fretboard); Lily# collapses them,
/// because a chord symbol's spelling is the writer's and the fingers do not change with it.
/// MEASURED on 2.26.0: the 663 entries collapse to 468 keys, and every collapsed pair has the
/// SAME placement list, barres and fingers included — so no rule has to choose between them.
/// The collapse keeps the FIRST entry in file order all the same, and
/// <c>ChordDiagramTests</c> asserts the pairs agree, so a LilyPond that changes one side is
/// seen rather than silently resolved.
/// </para>
/// <para>
/// THE QUALITY is matched at run time: an entry's semitones above its root are compared with
/// each <see cref="ChordQuality"/>'s tone set (<see cref="ChordQualityRegistry.GetTones"/>).
/// LilyPond's <c>:m</c> is <see cref="ChordQuality.Minor"/>, <c>:m7.5-</c>
/// <see cref="ChordQuality.HalfDiminished7"/>, <c>:m7+</c> <see cref="ChordQuality.MinorMajor7"/>
/// and so on; the one LilyPond quality no Lily# quality spells is the ukulele's <c>:m6-</c>
/// (minor with a minor sixth — 17 entries, <see cref="Unmapped"/>), which is therefore never
/// found. A slash chord and a chord with an unregistered quality have no entry either.
/// </para>
/// </remarks>
public static partial class PredefinedFretboards
{
    /// <summary>The three tables, by the tuning they were written for.</summary>
    public enum Table
    {
        /// <summary>ly/predefined-guitar-fretboards.ly (and its ninth-chord include):
        /// <c>guitar-tuning</c>.</summary>
        Guitar,
        /// <summary>ly/predefined-ukulele-fretboards.ly: <c>ukulele-tuning</c> (re-entrant G).</summary>
        Ukulele,
        /// <summary>ly/predefined-mandolin-fretboards.ly: <c>mandolin-tuning</c>
        /// (g d' a' e'', which is also the violin's).</summary>
        Mandolin,
    }

    /// <summary>One generated entry — see <see cref="Entries"/>.</summary>
    internal readonly record struct Entry(
        Table Table, int Line, string LilyPondRoot, int RootStep, int RootAlter,
        string Intervals, string Frets, string Fingers, string Barres);

    /// <summary>A barre as LilyPond stores it: from string to string (1 = the highest) at a fret.</summary>
    public readonly record struct Barre(int FirstString, int LastString, int Fret);

    /// <summary>A predefined shape.</summary>
    /// <param name="Frets">One per string, LOW string first; −1 muted, 0 open.</param>
    /// <param name="Fingers">One per string, LOW string first; 0 where LilyPond gives none.</param>
    /// <param name="Barres">The barres, in LilyPond's string numbers.</param>
    /// <param name="Table">The table it came from.</param>
    /// <param name="Line">The line of that table's file that stored it.</param>
    /// <param name="LilyPondRoot">The root as that entry spells it (<c>cis</c>, <c>des</c>).</param>
    /// <param name="File">The ly/ file that entry was written in (the guitar's ninth chords
    /// have their own).</param>
    public sealed record Shape(
        ImmutableArray<int> Frets, ImmutableArray<int> Fingers, ImmutableArray<Barre> Barres,
        Table Table, int Line, string LilyPondRoot, string File);

    /// <summary>The open strings (MIDI, LOW string first) a table was written for.</summary>
    public static IReadOnlyList<int> TuningOf(Table table) => table switch
    {
        Table.Guitar => Tablature.Tunings.GetTuning(Syntax.TuningType.Guitar),
        Table.Ukulele => Tablature.Tunings.GetTuning(Syntax.TuningType.Ukulele),
        _ => Tablature.Tunings.GetTuning(Syntax.TuningType.Violin),   // mandolin-tuning
    };

    /// <summary>The table written for exactly these open strings, or null — a table applies
    /// ONLY to the tuning it was made for (drop D has none: its low string moves every shape).</summary>
    public static Table? TableFor(IReadOnlyList<int> tuning)
    {
        foreach (var t in new[] { Table.Guitar, Table.Ukulele, Table.Mandolin })
            if (TuningOf(t).SequenceEqual(tuning))
                return t;
        return null;
    }

    /// <summary>
    /// The predefined shape of <paramref name="chord"/> on <paramref name="tuning"/>, or null
    /// when that tuning has no table, the chord has a slash bass or an unregistered quality,
    /// or the table has no entry for it.
    /// </summary>
    public static Shape? Find(IReadOnlyList<int> tuning, ChordStructure chord)
    {
        if (chord.RawSuffix != null || chord.BassStep != null || TableFor(tuning) is not { } table)
            return null;
        int pc = Mod12(Semantics.RelativeOctave.StepSemitoneOf(Mod7(chord.RootStep)) + chord.RootAlter);
        return Index.TryGetValue((table, pc, chord.Quality), out var shape) ? shape : null;
    }

    /// <summary>
    /// Whether LilyPond's table holds this chord under exactly this SPELLING of its root —
    /// what the LilyPond twin asks before trusting LilyPond's own lookup, which keys by
    /// spelled pitches (<c>cis</c> is not <c>des</c> there).
    /// </summary>
    public static bool HasEntrySpelled(Table table, int rootStep, int rootAlter, ChordQuality quality)
        => Entries.Any(e => e.Table == table && e.RootStep == Mod7(rootStep) && e.RootAlter == rootAlter
                            && QualityOf(e.Intervals) == quality);

    /// <summary>How many entries a table has as LilyPond stores them (before the collapse).</summary>
    public static int Count(Table table) => Entries.Count(e => e.Table == table);

    /// <summary>The entries no Lily# quality matches, as (table, root, semitones).</summary>
    public static IReadOnlyList<(Table Table, string Root, string Intervals)> Unmapped()
        => [.. Entries.Where(e => QualityOf(e.Intervals) is null)
                      .Select(e => (e.Table, e.LilyPondRoot, e.Intervals))];

    /// <summary>The enharmonic pairs the collapse merged whose shapes DIFFER — empty on
    /// 2.26.0 (the remark above).</summary>
    internal static IReadOnlyList<(Entry Kept, Entry Dropped)> CollapseConflicts()
    {
        var conflicts = new List<(Entry, Entry)>();
        var first = new Dictionary<(Table, int, string), Entry>();
        foreach (var e in Entries)
        {
            var key = (e.Table, Mod12(Semantics.RelativeOctave.StepSemitoneOf(e.RootStep) + e.RootAlter), e.Intervals);
            if (!first.TryGetValue(key, out var kept))
                first[key] = e;
            else if (kept.Frets != e.Frets || kept.Fingers != e.Fingers || kept.Barres != e.Barres)
                conflicts.Add((kept, e));
        }
        return conflicts;
    }

    // Lazy, not a field initializer: Entries lives in the generated partial file, and the
    // order in which two files' static initializers run is not the language's to promise.
    private static readonly System.Lazy<Dictionary<(Table, int, ChordQuality), Shape>> LazyIndex =
        new(BuildIndex);

    private static Dictionary<(Table, int, ChordQuality), Shape> Index => LazyIndex.Value;

    private static Dictionary<(Table, int, ChordQuality), Shape> BuildIndex()
    {
        var index = new Dictionary<(Table, int, ChordQuality), Shape>();
        foreach (var e in Entries)
        {
            if (QualityOf(e.Intervals) is not { } quality)
                continue;
            int pc = Mod12(Semantics.RelativeOctave.StepSemitoneOf(e.RootStep) + e.RootAlter);
            // The FIRST in file order wins (the remark above: the pairs agree anyway).
            index.TryAdd((e.Table, pc, quality), ToShape(e));
        }
        return index;
    }

    /// <summary>The registered quality whose tones are exactly these semitones above the root.</summary>
    internal static ChordQuality? QualityOf(string intervals)
    {
        var wanted = intervals.Split(' ').Select(s => int.Parse(s, CultureInfo.InvariantCulture))
            .OrderBy(s => s).ToArray();
        foreach (var q in System.Enum.GetValues<ChordQuality>())
            if (ChordQualityRegistry.GetTones(q).Select(t => t.Semitone).OrderBy(s => s).SequenceEqual(wanted))
                return q;
        return null;
    }

    private static Shape ToShape(Entry e)
    {
        var frets = e.Frets.Split(' ').Select(f => f == "x" ? -1 : int.Parse(f, CultureInfo.InvariantCulture));
        var fingers = e.Fingers.Split(' ').Select(f => f == "-" ? 0 : int.Parse(f, CultureInfo.InvariantCulture));
        var barres = e.Barres.Length == 0
            ? []
            : e.Barres.Split(' ').Select(b =>
            {
                // FROM-TO@FRET
                int at = b.IndexOf('@');
                int dash = b.IndexOf('-');
                return new Barre(
                    int.Parse(b[..dash], CultureInfo.InvariantCulture),
                    int.Parse(b[(dash + 1)..at], CultureInfo.InvariantCulture),
                    int.Parse(b[(at + 1)..], CultureInfo.InvariantCulture));
            });
        return new Shape([.. frets], [.. fingers], [.. barres], e.Table, e.Line, e.LilyPondRoot, FileOf(e));
    }

    /// <summary>The ly/ file an entry comes from. The guitar's ninth chords live in their own
    /// file, which holds exactly the guitar's <c>:9</c> chords (the main file has none).</summary>
    private static string FileOf(Entry e) => e.Table switch
    {
        Table.Guitar => e.Intervals == "0 4 7 10 14"
            ? "ly/predefined-guitar-ninth-fretboards.ly"
            : "ly/predefined-guitar-fretboards.ly",
        Table.Ukulele => "ly/predefined-ukulele-fretboards.ly",
        _ => "ly/predefined-mandolin-fretboards.ly",
    };

    private static int Mod12(int a) => ((a % 12) + 12) % 12;
    private static int Mod7(int a) => ((a % 7) + 7) % 7;
}
