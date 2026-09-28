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
using System.Linq;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;

namespace LilySharp.Core.Music;

/// <summary>A shape written after a chord symbol: the tuning it is bound to by name
/// (<c>guitar 133211</c>), or none (bound by its length), and the shape itself.</summary>
/// <param name="TuningName">The tuning word written before it, or null.</param>
/// <param name="Shape">One character per string, LOW string first: <c>x</c> muted, <c>o</c>
/// or <c>0</c> open, a digit the fret.</param>
public readonly record struct WrittenShape(string? TuningName, string Shape);

/// <summary>Where a shape came from: written at the chord (the only one a diagram draws since
/// 2026-09-28, save in a <c>chordDiagrams … all</c> score), or the default the editor's step
/// writes first and an <c>all</c> score draws for a name alone (predefined, else the first of
/// the order).</summary>
public enum ShapeSource
{
    /// <summary>Written at the chord (<c>@chord(F 133211)</c>, <c>F(133211)</c>).</summary>
    Written,
    /// <summary>LilyPond's predefined table for the tuning (<see cref="PredefinedFretboards"/>).</summary>
    Predefined,
    /// <summary>The first shape of Lily#'s enumeration order (<see cref="ChordVoicings"/>): the
    /// normal rule's, else — no shape without a stretch — the stretch-inclusive rule's
    /// (<see cref="ChordVoicings.Fallback(IReadOnlyList{int}, ChordStructure)"/>).</summary>
    FirstOfOrder,
}

/// <summary>A chord's shape on one tuning, and where it came from.</summary>
/// <param name="Frets">One per string, LOW string first; −1 muted, 0 open.</param>
/// <param name="Source">Written, predefined, or the first of the order.</param>
/// <param name="Predefined">The predefined entry, when <paramref name="Source"/> is
/// <see cref="ShapeSource.Predefined"/> (its fingers and barres; its LilyPond spelling).</param>
public sealed record ChosenShape(ImmutableArray<int> Frets, ShapeSource Source,
    PredefinedFretboards.Shape? Predefined = null)
{
    /// <summary>The shape as a player writes it (<c>x32010</c>, <c>8-10-10-9-8-8</c>).</summary>
    public string Spelled => ChordVoicings.Spell(Frets);

    /// <summary>The page's diagram spec (<see cref="ChordVoicings.ToFrameSpec"/>).</summary>
    public string FrameSpec => ChordVoicings.ToFrameSpec(Frets);

    /// <summary>The source as the editor's hover words it.</summary>
    public string SourceWord => Source switch
    {
        ShapeSource.Written => "written",
        ShapeSource.Predefined => "predefined",
        _ => "first of the order",
    };
}

/// <summary>
/// Which shape a chord diagram draws — the one WRITTEN at the chord for the diagram's tuning,
/// or none — the default shape the editor's step writes first, and the grammar of the written
/// shapes.
/// </summary>
/// <remarks>
/// <para>
/// Owner's design (HANDOFF §2 K, 2026-09-28): the SOURCE holds a shape or nothing — never an
/// index — and a diagram draws ONLY where a shape is written (<c>G(320003)</c>,
/// <c>@chord(G 320003)</c>, <c>@chord(x32010)</c>); a name alone draws none, in every score.
/// It draws on ONE tuning (<see cref="Semantics.ChordDiagramsKey.Resolve"/>: the layout's
/// <c>chordDiagrams</c>, else the part's fretted instrument, else the guitar). One chord can
/// carry shapes for several instruments: an unnamed shape applies to the tuning whose STRING
/// COUNT is its length (<c>F(133211 2010)</c>: six strings, four strings), and a tuning word
/// binds the next shape to that tuning by name (<c>F(guitar 133211 ukulele 2010)</c>) — needed
/// only when two tunings of one length are used. A shape the resolved tuning cannot take is
/// simply unused, silently.
/// </para>
/// <para>
/// The DEFAULT (<see cref="Default"/>: LilyPond's predefined shape, else the first of Lily#'s
/// order) is not drawn by itself (commit 9cf95fab drew it for every name; the owner reversed
/// that the same day) — save in a score that asks for it, <c>chordDiagrams … all</c> (owner's
/// decision 2026-09-28, the scope word); it is what
/// the editor's step writes when it adds a shape, and what its hover offers.
/// ⚠️ THE ENUMERATION ANSWERS ONLY ON A GUITAR-TYPE TUNING (strings rising in pitch,
/// <see cref="ChordVoicings.IsGuitarType"/>): its rules were written for and checked on the
/// guitar (owner's decision 2026-09-27). On the re-entrant ukulele a chord the predefined
/// table lacks has no default.
/// </para>
/// <para>
/// LILYSHARP-OWN, the rule and the routing: LilyPond's FretBoards context draws a diagram for
/// every chord it is given — the predefined shape of the chord's pitches or, lacking one, one
/// it computes (scm/translation-functions.scm determine-frets (lines 829-880)); a shape for
/// one diagram is written in its own <c>\fret-diagram</c> markup, and nothing binds a shape to
/// a chord symbol.
/// </para>
/// </remarks>
public static class ChordShapes
{
    /// <summary>The word that turns diagrams off (<c>chordDiagrams none</c>).</summary>
    public const string NoneWord = "none";

    /// <summary>
    /// The shape a diagram draws on <paramref name="diagramTuning"/>: the one written for it
    /// (<see cref="WrittenFor"/>); else, in a score whose layout writes <c>chordDiagrams … all</c>
    /// (<paramref name="all"/>), the <see cref="Default"/> of <paramref name="chord"/>; else null
    /// — no diagram.
    /// </summary>
    /// <param name="diagramTuning">The resolved tuning (<see cref="Semantics.ChordDiagramsKey.Resolve"/>).</param>
    /// <param name="written">The shapes written at the chord, in source order.</param>
    /// <param name="all">The score draws EVERY chord name (<see cref="Semantics.LayoutPlan.ChordDiagramsAll"/>).</param>
    /// <param name="chord">The chord the name spells (a bare <c>@chord</c>'s, the one it derives),
    /// or null (quoted text, a symbol that does not parse): with no written shape, no diagram.</param>
    /// <remarks>
    /// Owner's decision 2026-09-28: with <c>all</c> a WRITTEN shape still wins, and a chord with
    /// no shape at all on the tuning (an eleventh on the ukulele) draws none — the validator
    /// warns once per symbol and tuning.
    /// ⚠️ THE NEXT STEP (HANDOFF §2 K5 ③, not built): a per-song / per-section table in
    /// <c>layout</c> that turns diagrams on for the chords it lists. It slots in HERE, after the
    /// written shape and before the default — a written shape stays the strongest.
    /// </remarks>
    public static ChosenShape? Drawn(TuningType diagramTuning, IReadOnlyList<WrittenShape> written,
        bool all = false, ChordStructure? chord = null)
        => WrittenFor(diagramTuning, written) is { } shape
            ? new ChosenShape(Frets(shape), ShapeSource.Written)
            // A raw-suffix chord (a row's Cm13: a root, its tones unknown) has no shape to find.
            : all && chord is { RawSuffix: null } ? Default(diagramTuning, chord)
            : null;

    /// <summary>
    /// The DEFAULT shape of <paramref name="chord"/> on <paramref name="tuning"/> — LilyPond's
    /// predefined one, else the first of Lily#'s NORMAL order, else of the stretch-inclusive one
    /// (owner's decision 2026-09-28: a stretch shape only when nothing else plays the chord) —
    /// or null (a chord the ukulele's table lacks, an eleventh on no shape). Drawn by itself only
    /// in a <c>chordDiagrams … all</c> score (<see cref="Drawn"/>); elsewhere the editor's step
    /// writes it, and its hover offers it.
    /// </summary>
    public static ChosenShape? Default(TuningType tuning, ChordStructure chord)
    {
        int[] strings = Tunings.GetTuning(tuning);
        if (PredefinedFretboards.Find(strings, chord) is { } predefined)
            return new ChosenShape(predefined.Frets, ShapeSource.Predefined, predefined);
        if (ChordVoicings.IsGuitarType(strings) && ChordVoicings.Fallback(strings, chord) is { } first)
            return new ChosenShape(first, ShapeSource.FirstOfOrder);
        return null;
    }

    /// <summary>
    /// The written shape that applies to <paramref name="diagramTuning"/>: the one bound to it
    /// by name, else the unnamed one whose length is its string count; null when neither.
    /// </summary>
    public static string? WrittenFor(TuningType diagramTuning, IReadOnlyList<WrittenShape> written)
    {
        int strings = Tunings.GetStringCount(diagramTuning);
        foreach (var w in written)
            if (w.TuningName != null && Tunings.Parse(w.TuningName) == diagramTuning
                && Tunings.Names.Contains(w.TuningName) && w.Shape.Length == strings)
                return w.Shape;
        foreach (var w in written)
            if (w.TuningName == null && w.Shape.Length == strings)
                return w.Shape;
        return null;
    }

    /// <summary>A written shape's frets, LOW string first (−1 muted).</summary>
    public static ImmutableArray<int> Frets(string shape)
    {
        var b = ImmutableArray.CreateBuilder<int>(shape.Length);
        foreach (char ch in shape)
            b.Add(ch switch { 'x' => -1, 'o' => 0, _ => ch - '0' });
        return b.MoveToImmutable();
    }

    /// <summary>A word that sets out to be a shape: it starts with <c>x</c>, <c>o</c> or a
    /// digit (a chord symbol starts with a capital, a tuning word with another letter).</summary>
    public static bool StartsShape(string w)
        => w.Length > 0 && (w[0] is 'x' or 'o' || char.IsAsciiDigit(w[0]));

    /// <summary>One of <c>x</c>, <c>o</c>, <c>0</c>–<c>9</c> per string (the alphabet
    /// <c>@diagram</c> takes).</summary>
    public static bool IsShape(string w)
        => w.Length > 0 && w.All(ch => ch is 'x' or 'o' || char.IsAsciiDigit(ch));

    /// <summary>The string counts the tuning vocabulary has (4, 5, 6, 7) — an unnamed shape of
    /// any other length can be no instrument's.</summary>
    public static IReadOnlySet<int> StringCounts { get; } =
        Tunings.Names.Select(n => Tunings.GetStringCount(Tunings.Parse(n))).ToHashSet();

    /// <summary>A problem with written shapes, and the word it concerns (for the span).</summary>
    public readonly record struct Problem(int WordIndex, string Message);

    /// <summary>
    /// Reads the words after a chord symbol — shapes, each optionally preceded by a tuning
    /// word — into <paramref name="shapes"/>, and says what is wrong with them. A shape with a
    /// problem is left out; the others still apply.
    /// </summary>
    /// <param name="words">The words, in order.</param>
    /// <param name="symbol">The chord symbol they follow, for the messages (or null).</param>
    /// <param name="shapes">The shapes read.</param>
    public static IReadOnlyList<Problem> ParseWords(IReadOnlyList<string> words, string? symbol,
        out ImmutableArray<WrittenShape> shapes)
    {
        var problems = new List<Problem>();
        var list = ImmutableArray.CreateBuilder<WrittenShape>();
        var unnamedLengths = new Dictionary<int, int>();
        var named = new HashSet<TuningType>();
        for (int i = 0; i < words.Count; i++)
        {
            string w = words[i];
            string? tuningName = null;
            int shapeAt = i;
            if (!StartsShape(w))
            {
                if (!Tunings.Names.Contains(w))
                {
                    problems.Add(new Problem(i, NotShapeNorTuning(w)));
                    continue;
                }
                tuningName = w;
                if (i + 1 >= words.Count || !StartsShape(words[i + 1]))
                {
                    problems.Add(new Problem(i, TuningWithoutShape(w)));
                    continue;
                }
                shapeAt = ++i;
            }
            string shape = words[shapeAt];
            if (!IsShape(shape))
            {
                problems.Add(new Problem(shapeAt, NotAShape(shape)));
                continue;
            }
            if (tuningName != null)
            {
                var type = Tunings.Parse(tuningName);
                int n = Tunings.GetStringCount(type);
                if (shape.Length != n)
                {
                    problems.Add(new Problem(shapeAt, WrongLengthNamed(shape, tuningName, n)));
                    continue;
                }
                if (!named.Add(type))
                {
                    problems.Add(new Problem(i - 1, TuningTwice(tuningName)));
                    continue;
                }
            }
            else
            {
                if (!StringCounts.Contains(shape.Length))
                {
                    problems.Add(new Problem(shapeAt, WrongLength(shape)));
                    continue;
                }
                if (unnamedLengths.ContainsKey(shape.Length))
                {
                    problems.Add(new Problem(shapeAt, TwoOfOneLength(shape, symbol)));
                    continue;
                }
                unnamedLengths[shape.Length] = shapeAt;
            }
            list.Add(new WrittenShape(tuningName, shape));
        }
        shapes = list.ToImmutable();
        return problems;
    }

    // ---------------------------------------------------------------- the messages
    // Each names the fix. ASCII punctuation only (they reach the CLI).

    internal static string NotShapeNorTuning(string w)
        => $"'{w}' is neither a shape such as 'x32010' nor a tuning name (guitar, ukulele, ...) - "
           + "it is not used.";

    internal static string TuningWithoutShape(string w)
        => $"'{w}' names a tuning, so a shape must follow it: '{w} x32010'.";

    internal static string NotAShape(string w)
        => $"'{w}' is not a shape: write one character per string, low string first - "
           + "'x' muted, 'o' or '0' open, a digit for the fret (x32010). It is not used.";

    internal static string WrongLength(string shape)
        => $"'{shape}' has {shape.Length} character(s), which no tuning has strings for "
           + $"({string.Join(", ", StringCounts.OrderBy(n => n))}) - a shape is one character per "
           + "string, low string first (x32010 on a guitar, 0003 on a ukulele). It is not used.";

    internal static string WrongLengthNamed(string shape, string tuning, int strings)
        => $"'{shape}' has {shape.Length} character(s) but '{tuning}' has {strings} strings - "
           + "a shape is one character per string, low string first. It is not used.";

    internal static string TwoOfOneLength(string shape, string? symbol)
        => $"two shapes of {shape.Length} strings - name the tuning each is for: "
           + $"{symbol ?? "F"}(guitar ... guitardropd ...). '{shape}' is not used.";

    internal static string TuningTwice(string tuning)
        => $"'{tuning}' is given two shapes - write one per tuning. The second is not used.";

    /// <summary>A chord of a <c>chordDiagrams … all</c> score with no shape on its tuning
    /// (<see cref="Default"/> is null and none is written).</summary>
    internal static string NoShape(string symbol, string tuningWord, bool enumerates, int strings)
        => $"{symbol} has no chord diagram on '{tuningWord}': LilyPond's predefined table has no "
           + "shape for it and "
           + (enumerates
               ? "Lily#'s shape rules find none"
               : "Lily# finds shapes by rule only on a tuning whose strings rise in pitch")
           + $" - write the shape: {symbol}({new string('x', strings)}) in a chords row, "
           + $"@chord({symbol} {new string('x', strings)}) on a note, with the frets filled in. "
           + "No diagram is drawn.";
}
