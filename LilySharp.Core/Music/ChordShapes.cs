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
/// <param name="Shape">As written, LOW string first: one character per string (<c>x</c> muted,
/// <c>o</c> or <c>0</c> open, a digit the fret), or — since 2026-09-28, for frets 10–15 — with
/// a <c>-</c> on each side of each two-digit fret (<c>xx-10-12-13-11</c>, the full-dash
/// <c>x-x-10-12-13-11</c> too; the segment rule of <see cref="ChordShapes.TryRead"/>).
/// Its string count is <see cref="ChordShapes.StringCount"/>, never its length.</param>
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
    /// <summary>Written in the layout's shape table for the chord
    /// (<c>chordDiagrams guitar { Cm7 x35343 }</c>, <see cref="ChordShapeTable"/>; 2026-09-29).</summary>
    Layout,
}

/// <summary>A chord's shape on one tuning, and where it came from.</summary>
/// <param name="Frets">One per string, LOW string first; −1 muted, 0 open.</param>
/// <param name="Source">Written, predefined, or the first of the order.</param>
/// <param name="Predefined">The predefined entry, when <paramref name="Source"/> is
/// <see cref="ShapeSource.Predefined"/> (its fingers and barres; its LilyPond spelling).</param>
public sealed record ChosenShape(ImmutableArray<int> Frets, ShapeSource Source,
    PredefinedFretboards.Shape? Predefined = null)
{
    /// <summary>The shape as a player writes it (<c>x32010</c>, <c>8-10-10-988</c>).</summary>
    public string Spelled => ChordVoicings.Spell(Frets);

    /// <summary>The page's diagram spec (<see cref="ChordVoicings.ToFrameSpec"/>) — with the
    /// predefined entry's fingers and barres as its detail suffix
    /// (<c>Svg.Layout.FretFrameGeometry.Detailed</c>), which the page's diagram draws (the
    /// barre everywhere, the fingers under a chords row's), the twin's terse string carries,
    /// and MusicXML's <c>&lt;frame&gt;</c> writes.</summary>
    public string FrameSpec => Predefined is { } p
        ? Svg.Layout.FretFrameGeometry.Detailed(ChordVoicings.ToFrameSpec(Frets), p.Fingers, p.Barres)
        : ChordVoicings.ToFrameSpec(Frets);

    /// <summary>The source as the editor's hover words it.</summary>
    public string SourceWord => Source switch
    {
        ShapeSource.Written => "written",
        ShapeSource.Layout => "layout",
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
/// order) is not drawn by itself (the first cut of 2026-09-28 drew it for every name; the owner
/// reversed that the same day) — save in a score that asks for it, <c>chordDiagrams … all</c> (owner's
/// decision 2026-09-28, the scope word); it is what
/// the editor's step writes when it adds a shape, and what its hover offers.
/// The enumeration answers on EVERY tuning since 2026-09-29 (HANDOFF §2 K5 ⑥): on a
/// guitar-type tuning (strings rising in pitch, <see cref="ChordVoicings.IsGuitarType"/>) by
/// the six rules, on a re-entrant one (the ukulele) by the same rules less V4 — measured to
/// put LilyPond's predefined ukulele shape first for every chord tried. A chord no rule can
/// voice (C13 on four strings) has no default.
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
    /// The shape a diagram draws on <paramref name="diagramTuning"/>, strongest first: the one
    /// written for it at the chord (<see cref="WrittenFor"/>); else the one the score's layout
    /// TABLE lists for the chord (<paramref name="table"/>, <see cref="ChordShapeTable.Find"/>:
    /// the entry of the <paramref name="section"/> the chord is written in, else the song's — its
    /// shape for the tuning, or the <see cref="Default"/> for a name listed alone); else, in a
    /// score whose layout writes <c>chordDiagrams … all</c> (<paramref name="all"/>), the
    /// <see cref="Default"/> of <paramref name="chord"/>; else null — no diagram.
    /// </summary>
    /// <param name="diagramTuning">The resolved tuning (<see cref="Semantics.ChordDiagramsKey.Resolve"/>).</param>
    /// <param name="written">The shapes written at the chord, in source order.</param>
    /// <param name="all">The score draws EVERY chord name (<see cref="Semantics.LayoutPlan.ChordDiagramsAll"/>).</param>
    /// <param name="chord">The chord the name spells (a bare <c>@chord</c>'s, the one it derives),
    /// or null (quoted text, a symbol that does not parse): with no written shape, no diagram.</param>
    /// <param name="table">The score's layout shape table (<see cref="Semantics.LayoutPlan.ChordDiagramTable"/>), or null.</param>
    /// <param name="section">The section the chord is written in (<see cref="Semantics.ChordDiagramScores.SectionNameOf"/>), or null.</param>
    /// <param name="capo">The fret the score's capo is on (<see cref="Semantics.ChordSpelling.Capo"/>), 0 for
    /// none: a written or listed shape is the PRESSED shape as it stands, and the default is
    /// the pressed chord's (<see cref="ChordStructure.Pressed"/>); the table is read by the
    /// sounding chord, the name the music writes.</param>
    /// <remarks>
    /// Owner's decision 2026-09-28: with <c>all</c> a WRITTEN shape still wins, and a chord with
    /// no shape at all on the tuning (an eleventh on the ukulele) draws none — the validator
    /// warns once per symbol and tuning. The table (HANDOFF §2 K5 ③, 2026-09-29) slots in
    /// between: a written shape stays the strongest, and a listed chord draws whether or not the
    /// score writes <c>all</c>.
    /// </remarks>
    public static ChosenShape? Drawn(TuningType diagramTuning, IReadOnlyList<WrittenShape> written,
        bool all = false, ChordStructure? chord = null, ChordShapeTable? table = null, string? section = null,
        int capo = 0)
    {
        if (WrittenFor(diagramTuning, written) is { } shape)
            return new ChosenShape(Frets(shape), ShapeSource.Written);
        // A raw-suffix chord (a row's Cm13: a root, its tones unknown) has no shape to find.
        if (chord is not { RawSuffix: null })
            return null;
        if (table?.Find(section, chord, diagramTuning) is { } listed)
            return listed.Shapes.IsEmpty
                ? Default(diagramTuning, chord.Pressed(capo, 0))
                : new ChosenShape(Frets(WrittenFor(diagramTuning, listed.Shapes)!), ShapeSource.Layout);
        return all ? Default(diagramTuning, chord.Pressed(capo, 0)) : null;
    }

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
        if (ChordVoicings.Fallback(strings, chord) is { } first)
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
                && Tunings.Names.Contains(w.TuningName) && StringCount(w.Shape) == strings)
                return w.Shape;
        foreach (var w in written)
            if (w.TuningName == null && StringCount(w.Shape) == strings)
                return w.Shape;
        return null;
    }

    /// <summary>A written shape's frets, LOW string first (−1 muted) — either form
    /// (<see cref="TryRead"/>). A word that is not a shape reads its items as best it can
    /// (an unreadable one muted); callers read only shapes <see cref="TryRead"/> took.</summary>
    public static ImmutableArray<int> Frets(string shape)
    {
        if (TryRead(shape, out var frets, out _))
            return frets;
        var items = Items(shape);
        var b = ImmutableArray.CreateBuilder<int>(items.Count);
        foreach (var item in items)
            b.Add(ReadItem(item) ?? -1);
        return b.MoveToImmutable();
    }

    /// <summary>
    /// A written word's ITEMS, one per string, by the segment rule (<see cref="TryRead"/>): a
    /// word without <c>-</c> is one item per character; else each <c>-</c>-separated segment
    /// that is exactly two digits is one item, any other segment one item per character (an
    /// empty segment none). Items are not checked here.
    /// </summary>
    private static List<string> Items(string w)
    {
        var items = new List<string>(w.Length);
        foreach (var segment in IsDashed(w) ? w.Split('-') : [w])
        {
            if (IsTwoDigitSegment(segment) && IsDashed(w))
                items.Add(segment);
            else
                foreach (char ch in segment)
                    items.Add(ch.ToString());
        }
        return items;
    }

    /// <summary>A segment of exactly two digits — ONE fret in a word holding <c>-</c>.</summary>
    private static bool IsTwoDigitSegment(string segment)
        => segment.Length == 2 && char.IsAsciiDigit(segment[0]) && char.IsAsciiDigit(segment[1]);

    /// <summary>A word that sets out to be a shape: it starts with <c>x</c>, <c>o</c> or a
    /// digit (a chord symbol starts with a capital, a tuning word with another letter) — or
    /// with <c>-</c>, a dash-separated shape begun wrongly (<see cref="TryRead"/> names it).</summary>
    public static bool StartsShape(string w)
        => w.Length > 0 && (w[0] is 'x' or 'o' or '-' || char.IsAsciiDigit(w[0]));

    /// <summary>A shape in either written form (<see cref="TryRead"/>).</summary>
    public static bool IsShape(string w) => TryRead(w, out _, out _);

    /// <summary>Whether a word is written in the DASH-SEPARATED form (it holds a <c>-</c>).</summary>
    public static bool IsDashed(string w) => w.Contains('-');

    /// <summary>The number of strings a written shape covers: its items by the segment rule
    /// (<see cref="TryRead"/>: a two-digit segment one, any other segment its characters —
    /// <c>8xx88-11</c> is six), else its characters.</summary>
    public static int StringCount(string shape)
        => IsDashed(shape) ? Items(shape).Count : shape.Length;

    /// <summary>The highest fret a shape can write (the page's alphabet stops at <c>f</c>, and
    /// Lily#'s order searches frets 0–15).</summary>
    public const int MaxFret = 15;

    /// <summary>
    /// Reads a written shape, LOW string first, in either of its two forms: ONE CHARACTER PER
    /// STRING — <c>x</c> muted, <c>o</c> or <c>0</c> open, a digit the fret (<c>x32010</c>) —
    /// or, holding <c>-</c>, by SEGMENTS: the word split on <c>-</c>, a segment of exactly two
    /// digits is ONE fret 10–15, any other segment one character per string as above
    /// (<c>8xx88-11</c>, <c>xx-10-12-13-11</c>, <c>8-10-10-888</c>; the full-dash
    /// <c>x-x-10-12-13-11</c> reads the same way, each segment one item). Lower case only. The
    /// item count is not checked here (the tuning decides it).
    /// </summary>
    /// <remarks>
    /// Owner's decision 2026-09-28 (HANDOFF §2 K1): the dash form is the common web chord-chart
    /// notation and the only way to write frets 10–15; a word holding a <c>-</c> is dash
    /// separated, any other is one character per string exactly as before. Same day, the
    /// COMPACT form (owner's decision): the dashes need only surround the two-digit frets — the
    /// segment rule, which reads every full-dash word identically. ⚠️ The pitfall it brings: a
    /// lone two-digit segment is always one fret, so frets 10, 9, 9 are <c>10-9-9</c> (a
    /// <c>99</c> segment is fret 99 — an error naming the fix, as is <c>00</c>–<c>09</c>). A
    /// writer (the editor's step, the hover) spells one character per string whenever every
    /// fret is 9 or less, the compact form otherwise (<see cref="ChordVoicings.Spell"/>).
    /// </remarks>
    /// <param name="w">The word.</param>
    /// <param name="frets">Its frets (−1 muted), when it is a shape.</param>
    /// <param name="problem">What is wrong, naming the fix, when it is not.</param>
    public static bool TryRead(string w, out ImmutableArray<int> frets, out string? problem)
    {
        frets = [];
        problem = null;
        if (w.Length == 0)
        {
            problem = NotAShape(w);
            return false;
        }
        if (!IsDashed(w))
        {
            if (!w.All(ch => ch is 'x' or 'o' || char.IsAsciiDigit(ch)))
            {
                problem = NotAShape(w);
                return false;
            }
            var one = ImmutableArray.CreateBuilder<int>(w.Length);
            foreach (char ch in w)
                one.Add(ch switch { 'x' => -1, 'o' => 0, _ => ch - '0' });
            frets = one.MoveToImmutable();
            return true;
        }
        if (w[0] == '-' || w[^1] == '-')
        {
            problem = DashAtEnd(w);
            return false;
        }
        var b = ImmutableArray.CreateBuilder<int>(w.Length);
        foreach (var segment in w.Split('-'))
        {
            if (segment.Length == 0)
            {
                problem = EmptyItem(w);
                return false;
            }
            if (IsTwoDigitSegment(segment))
            {
                // Exactly two digits: ONE fret — never two strings (owner's decision 2026-09-28).
                int n = (segment[0] - '0') * 10 + (segment[1] - '0');
                if (n < 10)
                {
                    problem = LeadingZero(w, segment);
                    return false;
                }
                if (n > MaxFret)
                {
                    problem = FretTooHigh(w, segment);
                    return false;
                }
                b.Add(n);
                continue;
            }
            foreach (char ch in segment)
            {
                if (ReadItem(ch.ToString()) is not { } fret)
                {
                    problem = BadItem(w, ch.ToString());
                    return false;
                }
                b.Add(fret);
            }
        }
        frets = b.ToImmutable();
        return true;
    }

    /// <summary>One item: <c>x</c> −1, <c>o</c> 0, a number 0–15; else null.</summary>
    private static int? ReadItem(string item)
    {
        if (item == "x")
            return -1;
        if (item == "o")
            return 0;
        if (item.Length is < 1 or > 2 || !item.All(char.IsAsciiDigit))
            return null;
        int n = int.Parse(item, System.Globalization.CultureInfo.InvariantCulture);
        return n <= MaxFret ? n : null;
    }

    /// <summary>
    /// The lower-case spelling of a word that is a shape only once lowered (<c>X32010</c>,
    /// <c>X-3-5-5-4-3</c>), or null — values are case-sensitive, and this names the fix.
    /// </summary>
    public static string? CaseCorrected(string w)
    {
        if (w.Length == 0 || w[0] is not ('X' or 'O'))
            return null;
        string lowered = w.ToLowerInvariant();
        return lowered != w && IsShape(lowered) ? lowered : null;
    }

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
                if (CaseCorrected(w) is { } lowered)
                {
                    problems.Add(new Problem(i, WrongCase(lowered)));
                    continue;
                }
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
            if (!TryRead(shape, out _, out var unreadable))
            {
                problems.Add(new Problem(shapeAt, unreadable!));
                continue;
            }
            int count = StringCount(shape);
            if (tuningName != null)
            {
                var type = Tunings.Parse(tuningName);
                int n = Tunings.GetStringCount(type);
                if (count != n)
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
                if (!StringCounts.Contains(count))
                {
                    problems.Add(new Problem(shapeAt, WrongLength(shape)));
                    continue;
                }
                if (unnamedLengths.ContainsKey(count))
                {
                    problems.Add(new Problem(shapeAt, TwoOfOneLength(shape, symbol)));
                    continue;
                }
                unnamedLengths[count] = shapeAt;
            }
            list.Add(new WrittenShape(tuningName, shape));
        }
        shapes = list.ToImmutable();
        return problems;
    }

    // ---------------------------------------------------------------- the shape against its symbol

    /// <summary>
    /// How a written shape disagrees with its chord (<see cref="Mismatch"/>): the notes it sounds
    /// that are not chord tones, the required tones it lacks, and the chord its notes do name, if
    /// any.
    /// </summary>
    /// <param name="Sounding">The pitch classes it sounds, each once, upward from its lowest note.</param>
    /// <param name="Foreign">⑴ The sounded pitch classes that are no tone of the chord (for X/Y, nor Y).</param>
    /// <param name="Missing">⑵ The required tones it lacks, in the chord's tone order (then Y), as
    /// (pitch class, what the tone is: "the 3rd", "the bass").</param>
    /// <param name="Recognized">The chord the sounded notes name (<see cref="ChordStructure.TryRecognize(IReadOnlyList{ValueTuple{int, int}}, out ChordStructure?)"/>,
    /// the recognizer a bare <c>@chord</c> uses — a slash chord when its root is not the lowest
    /// note), asked only when ⑴ applies; else null.</param>
    /// <param name="Spell">Spells a pitch class the way the message names it.</param>
    public sealed record ShapeMismatch(
        ImmutableArray<int> Sounding, ImmutableArray<int> Foreign,
        ImmutableArray<(int Pc, string Role)> Missing,
        ChordStructure? Recognized, System.Func<int, string> Spell);

    /// <summary>
    /// Whether the shape <paramref name="frets"/> (LOW string first, −1 muted) played on
    /// <paramref name="tuning"/> (open strings, MIDI) agrees with <paramref name="chord"/>; null
    /// when it does, or when the chord has no registered tone set (a raw suffix).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Owner's decision 2026-09-28, REVERSING "a written shape is the writer's call, it is not
    /// validated": a written shape that disagrees with its symbol is warned (LYS1039). The
    /// sounding notes are each non-muted string's open pitch plus its fret. TWO rules:
    /// ⑴ only chord tones sound (for X/Y, X's tones plus Y) — the enumeration's V2
    /// (<see cref="ChordVoicings.TryTones"/>); ⑵ every REQUIRED tone sounds: all of the chord's
    /// tones but the ROOT and the PERFECT FIFTH (an altered fifth, the 3rd, the 7th and the
    /// tensions are required), and for X/Y, Y when it is neither X's root nor its perfect fifth
    /// (a bass outside the chord is the reason for the slash). The enumeration's V3/V4 are
    /// stricter, so every shape the editor's step writes from the order passes. V1 (three
    /// strings) is NOT asked — a two-string shape is checked by the same rules.
    /// </para>
    /// <para>
    /// ⚠️ THE BASS IS NOT CHECKED (owner's decision 2026-09-28, after the audit): an inversion is
    /// an ordinary shape — LilyPond's own C7 <c>032310</c> has E lowest, and most of its
    /// diminished, augmented and mandolin shapes are inversions. A rule "the root lowest" (the
    /// first version of this check, the same day) warned 135 predefined shapes for the bass alone; with it went its
    /// re-entrant-tuning exemption. And the ROOT may be left out: the ukulele's ninths are
    /// rootless (<c>ChordShapeCheckTests</c> audits every predefined shape).
    /// </para>
    /// <para>
    /// SPELLING: a chord tone (and the slash bass) is named as the chord spells it
    /// (<see cref="ChordStructure.Tones"/> — the spelling the chord's note expansion and hover
    /// use); any other note, and the recognized name, in the key the chord's root suggests (its
    /// major key, or minor with a minor third: C sharps, B♭ flats) by <see cref="SpellInKey"/> —
    /// the bare <c>@chord</c>'s spelling with naturals preferred. The validator reads the tree,
    /// which does not say the key at a bar.
    /// </para>
    /// </remarks>
    public static ShapeMismatch? Mismatch(IReadOnlyList<int> frets, IReadOnlyList<int> tuning, ChordStructure chord)
    {
        if (!ChordVoicings.TryTones(chord, out int allowed, out _, out int bassPc))
            return null;
        var pitches = new List<int>();
        for (int i = 0; i < frets.Count && i < tuning.Count; i++)
            if (frets[i] >= 0)
                pitches.Add(tuning[i] + frets[i]);
        pitches.Sort();
        int sounded = 0;
        foreach (int p in pitches)
            sounded |= 1 << Mod12(p);

        int lowestPc = pitches.Count > 0 ? Mod12(pitches[0]) : bassPc;
        var sounding = Enumerable.Range(0, 12).Select(k => Mod12(lowestPc + k))
            .Where(pc => (sounded & (1 << pc)) != 0).ToImmutableArray();
        var foreign = sounding.Where(pc => (allowed & (1 << pc)) == 0).ToImmutableArray();

        // ⑵ The required tones: all but the root and the perfect fifth — which may be left out
        // (optional), so a slash bass Y on either of them is optional too.
        var missing = ImmutableArray.CreateBuilder<(int, string)>();
        int rootPc = Mod12(Semantics.RelativeOctave.StepSemitoneOf(Mod7(chord.RootStep)) + chord.RootAlter);
        int optional = 1 << rootPc, listed = 0;
        foreach (var tone in ChordQualityRegistry.GetTones(chord.Quality))
            if (ChordVoicings.IsPerfectFifth(tone))
                optional |= 1 << Mod12(rootPc + tone.Semitone);
        foreach (var tone in ChordQualityRegistry.GetTones(chord.Quality))
        {
            int pc = Mod12(rootPc + tone.Semitone);
            if (tone.DiatonicStep == 0 || ChordVoicings.IsPerfectFifth(tone)
                || (sounded & (1 << pc)) != 0 || (listed & (1 << pc)) != 0)
                continue;
            listed |= 1 << pc;
            missing.Add((pc, ToneRole(tone.DiatonicStep)));
        }
        if (chord.BassStep != null && (optional & (1 << bassPc)) == 0
            && (sounded & (1 << bassPc)) == 0 && (listed & (1 << bassPc)) == 0)
            missing.Add((bassPc, "the bass"));

        if (foreign.IsEmpty && missing.Count == 0)
            return null;

        int keySharps = KeyOf(chord);
        var spelled = new Dictionary<int, string>();
        foreach (var t in chord.Tones)
            spelled.TryAdd(Mod12(Semantics.RelativeOctave.StepSemitoneOf(t.Step) + t.Alter),
                ChordStructure.SpellPitch(t.Step, t.Alter));
        if (chord.BassStep is int bs)
            spelled.TryAdd(bassPc, ChordStructure.SpellPitch(bs, chord.BassAlter ?? 0));
        string Spell(int pc)
        {
            if (spelled.TryGetValue(pc, out var s))
                return s;
            var (step, alter) = SpellInKey(pc, keySharps);
            return ChordStructure.SpellPitch(step, alter);
        }
        ChordStructure? recognized = null;
        if (!foreign.IsEmpty && pitches.Count > 0)
            ChordStructure.TryRecognize([.. pitches.Select(p => SpellInKey(Mod12(p), keySharps))], out recognized);
        return new ShapeMismatch(sounding, foreign, missing.ToImmutable(), recognized, Spell);
    }

    /// <summary>
    /// A pitch class in the key of <paramref name="keySharps"/>: the key's own letter for it,
    /// else a NATURAL, else <see cref="Semantics.ChordAnnotation.SpellPitchClass"/>'s sharp or
    /// flat. The natural step is Lily#'s addition for the message (2026-09-28): the bare
    /// <c>@chord</c>'s spelling goes straight to the sharp or flat, which in a chord's own key
    /// names B natural C♭ in F and C natural B♯ in B.
    /// </summary>
    private static (int Step, int Alter) SpellInKey(int pc, int keySharps)
        => ChordStructure.SpellInKey(pc, keySharps);   // one home, shared with the capo's pressed name

    /// <summary>What a chord tone is, by its diatonic step above the root.</summary>
    private static string ToneRole(int diatonicStep) => diatonicStep switch
    {
        0 => "the root",
        1 => "the 2nd",
        2 => "the 3rd",
        3 => "the 4th",
        4 => "the 5th",
        5 => "the 6th",
        6 => "the 7th",
        8 => "the 9th",
        10 => "the 11th",
        12 => "the 13th",
        _ => $"step {diatonicStep + 1}",
    };

    /// <summary>The key a chord's own spelling suggests, as sharps (+) / flats (−): its root's
    /// major key, the relative minor's signature for a chord with a minor third.</summary>
    private static int KeyOf(ChordStructure chord)
    {
        int[] fifths = [0, 2, 4, -1, 1, 3, 5];   // C D E F G A B on the circle of fifths
        int k = fifths[Mod7(chord.RootStep)] + 7 * chord.RootAlter
                - (chord.RawSuffix == null && ChordQualityRegistry.HasMinorThird(chord.Quality) ? 3 : 0);
        return System.Math.Clamp(k, -7, 7);
    }

    /// <summary>A chord as a chords row or <c>@chord</c> writes it (<c>F#m7-5/C</c>) — ASCII
    /// accidentals, the first entry token of its quality — or null when a double accidental
    /// has no spelling in that grammar.</summary>
    public static string? SourceSymbol(ChordStructure chord)
    {
        if (chord.RawSuffix != null || System.Math.Abs(chord.RootAlter) > 1 || System.Math.Abs(chord.BassAlter ?? 0) > 1)
            return null;
        string token = chord.Quality == ChordQuality.Major ? ""
            : ChordQualityRegistry.Tokens.First(t => ChordQualityRegistry.TryResolve(t, out var q) && q == chord.Quality);
        string symbol = SourcePitch(chord.RootStep, chord.RootAlter) + token;
        if (chord.BassStep is int bs)
            symbol += "/" + SourcePitch(bs, chord.BassAlter ?? 0);
        return symbol;
    }

    private static string SourcePitch(int step, int alter)
        => "CDEFGAB"[Mod7(step)] + (alter > 0 ? "#" : alter < 0 ? "b" : "");

    private static int Mod12(int a) => ((a % 12) + 12) % 12;
    private static int Mod7(int a) => ((a % 7) + 7) % 7;

    /// <summary>
    /// The LYS1039 message for <paramref name="m"/>: every problem in one sentence, naming the
    /// fix. <paramref name="shape"/> is the shape word, <paramref name="tuningName"/> the tuning
    /// word binding it (or null), <paramref name="symbol"/> the symbol as written, <paramref name="inRow"/> whether it is a
    /// chords-row entry (<c>Am(x02210)</c>) or an <c>@chord</c> (<c>@chord(Am x02210)</c>).
    /// </summary>
    /// <param name="item">A <c>chord(…)</c> item's shape (<see cref="ShapeChords"/>): the fix is
    /// spelled <c>chord(X shape)</c>.</param>
    /// <param name="inTable">A layout table entry's shape (<see cref="ChordShapeTable"/>): the fix
    /// is spelled as the table writes it, <c>X shape</c>.</param>
    internal static string MismatchMessage(ShapeMismatch m, string shape, string? tuningName,
        string symbol, bool inRow, bool item = false, bool inTable = false)
    {
        string Notes(IEnumerable<int> pcs) => string.Join(" ", pcs.Select(m.Spell));
        string written = tuningName == null ? shape : $"{tuningName} {shape}";
        string Form(string sym) => inRow ? $"{sym}({written})"
            : item ? $"chord({sym} {written})"
            : inTable ? $"{sym} {written}"
            : $"@chord({sym} {written})";
        string missingList = AndList(m.Missing.Select(t => $"{m.Spell(t.Pc)} ({t.Role})").ToList());
        string foreignWords = m.Foreign.Length == 1 ? "is not a tone" : "are not tones";

        if (m.Recognized is { } rec && SourceSymbol(rec) is { } named)
        {
            var details = new List<string> { $"{Notes(m.Foreign)} {foreignWords} of {symbol}" };
            if (m.Missing.Length > 0)
                details.Add($"it lacks {missingList}");
            return $"'{shape}' sounds {Notes(m.Sounding)}, which is {named}, not {symbol} "
                   + $"({string.Join("; ", details)}) - write {Form(named)} or another shape.";
        }

        var clauses = new List<string>();
        if (m.Foreign.Length > 0)
            clauses.Add($"sounds {Notes(m.Foreign)}, which {(m.Foreign.Length == 1 ? "is" : "are")} "
                        + $"not {(m.Foreign.Length == 1 ? "a tone" : "tones")} of {symbol}");
        if (m.Missing.Length > 0)
            clauses.Add($"lacks {missingList}");
        string fix = m.Foreign.Length > 0
            ? $"write a shape of {symbol}'s tones"
            : $"fret {AndList(m.Missing.Select(t => m.Spell(t.Pc)).ToList())} or write another shape";
        // The clauses take a serial comma: a clause holds its own "and" ("lacks E and B♭").
        string joined = clauses.Count == 1 ? clauses[0]
            : string.Join(", ", clauses.Take(clauses.Count - 1)) + ", and " + clauses[^1];
        return $"'{shape}' for {symbol} {joined} - {fix}.";
    }

    /// <summary>"A", "A and B", "A, B and C".</summary>
    private static string AndList(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1],
    };

    // ---------------------------------------------------------------- the messages
    // Each names the fix. ASCII punctuation only (they reach the CLI) — save the ♭ and ♯ of a
    // note LYS1039 names (the owner's own wording, 2026-09-28: "lacks B♭ (the 7th)").

    internal static string NotShapeNorTuning(string w)
        => $"'{w}' is neither a shape such as 'x32010' nor a tuning name (guitar, ukulele, ...) - "
           + "it is not used.";

    internal static string TuningWithoutShape(string w)
        => $"'{w}' names a tuning, so a shape must follow it: '{w} x32010'.";

    internal static string NotAShape(string w)
        => $"'{w}' is not a shape: write one character per string, low string first - "
           + "'x' muted, 'o' or '0' open, a digit for the fret (x32010) - and, for frets 10-15, "
           + "a '-' on each side of each two-digit fret (xx-10-12-13-11). It is not used.";

    /// <summary>"6 character(s)" / "5 item(s)" — how a shape's length is counted in its form.</summary>
    private static string Counted(string shape)
        => IsDashed(shape) ? $"{StringCount(shape)} item(s)" : $"{shape.Length} character(s)";

    internal static string WrongLength(string shape)
        => $"'{shape}' has {Counted(shape)}, which no tuning has strings for "
           + $"({string.Join(", ", StringCounts.OrderBy(n => n))}) - a shape is one character per "
           + "string, low string first (x32010 on a guitar, 0003 on a ukulele), a two-digit fret "
           + "with a '-' on each side (xx-10-12-13-11). It is not used.";

    internal static string WrongLengthNamed(string shape, string tuning, int strings)
        => $"'{shape}' has {Counted(shape)} but '{tuning}' has {strings} strings - "
           + "a shape is one character (or one '-'-separated two-digit fret) per string, low "
           + "string first. It is not used.";

    internal static string DashAtEnd(string w)
        => $"'{w}' starts or ends with '-' - '-' goes only BETWEEN the frets "
           + $"(xx-10-12-13-11): write '{w.Trim('-')}'. It is not used.";

    internal static string EmptyItem(string w)
        => $"'{w}' has an empty item ('--') - separate the frets by single '-' "
           + $"('{CollapseDashes(w)}'). It is not used.";

    /// <param name="segment">The digits read as one fret: a two-digit segment when the word
    /// holds '-', whose fix may be that the writer meant two strings.</param>
    internal static string FretTooHigh(string w, string segment)
        => $"'{w}': fret {segment} is beyond the highest a shape can write ({MaxFret}) - write a fret "
           + $"0-{MaxFret}; a two-digit segment is one fret, so for two strings write "
           + $"'{SplitSegment(w, segment)}'. It is not used.";

    /// <summary>A two-digit segment <c>00</c>–<c>09</c>: one fret by the segment rule, and no
    /// fret is written so — the writer meant two strings.</summary>
    internal static string LeadingZero(string w, string segment)
        => $"'{w}': a two-digit segment is one fret, and '{segment}' is none - separate single "
           + $"frets with '-': write '{SplitSegment(w, segment)}'. It is not used.";

    internal static string BadItem(string w, string item)
        => $"'{w}': '{item}' is not a fret - a shape is 'x' (muted), 'o' or a digit per string, "
           + $"a two-digit fret 10-{MaxFret} with a '-' on each side (xx-10-12-13-11). It is not used.";

    /// <summary><paramref name="w"/> with each <paramref name="segment"/> split into its two
    /// strings (<c>10-99</c> → <c>10-9-9</c>).</summary>
    private static string SplitSegment(string w, string segment)
        => string.Join('-', w.Split('-').Select(s => s == segment ? $"{s[0]}-{s[1]}" : s));

    internal static string WrongCase(string lowered)
        => $"Values are case-sensitive: write '{lowered}' ('x' muted, 'o' open are lower case). "
           + "It is not used.";

    private static string CollapseDashes(string w)
    {
        var sb = new System.Text.StringBuilder(w.Length);
        foreach (char ch in w)
            if (ch != '-' || (sb.Length > 0 && sb[^1] != '-'))
                sb.Append(ch);
        return sb.ToString().TrimEnd('-');
    }

    internal static string TwoOfOneLength(string shape, string? symbol)
        => $"two shapes of {StringCount(shape)} strings - name the tuning each is for: "
           + $"{symbol ?? "F"}(guitar ... guitardropd ...). '{shape}' is not used.";

    internal static string TuningTwice(string tuning)
        => $"'{tuning}' is given two shapes - write one per tuning. The second is not used.";

    /// <summary>A chord of a <c>chordDiagrams … all</c> score with no shape on its tuning
    /// (<see cref="Default"/> is null and none is written).</summary>
    internal static string NoShape(string symbol, string tuningWord, int strings)
        => $"{symbol} has no chord diagram on '{tuningWord}': LilyPond's predefined table has no "
           + "shape for it and Lily#'s shape rules find none"
           + $" - write the shape: {symbol}({new string('x', strings)}) in a chords row, "
           + $"@chord({symbol} {new string('x', strings)}) on a note, with the frets filled in. "
           + "No diagram is drawn.";
}
