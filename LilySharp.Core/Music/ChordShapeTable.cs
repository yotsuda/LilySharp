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
using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Music;

/// <summary>
/// One entry of a layout's shape table (<see cref="ChordShapeTable"/>): a chord symbol and the
/// shapes written after it — <c>Cm7 x35343</c>, <c>F guitar 133211 ukulele 2010</c> — or none
/// (<c>G</c> alone: the chord draws its usual shape, <see cref="ChordShapes.Default"/>).
/// </summary>
/// <param name="Symbol">The symbol as written.</param>
/// <param name="Chord">The chord it names.</param>
/// <param name="Shapes">The shapes written after it, in source order (those with a problem
/// left out — the reader reports them); empty for a name alone.</param>
public sealed record ChordShapeEntry(string Symbol, ChordStructure Chord, ImmutableArray<WrittenShape> Shapes)
{
    /// <summary>By value: the same symbol, chord and shapes (an ImmutableArray compares by
    /// reference on its own, which would make every re-read plan a "change").</summary>
    public bool Equals(ChordShapeEntry? other)
        => other != null && Symbol == other.Symbol && Chord == other.Chord && Shapes.SequenceEqual(other.Shapes);

    public override int GetHashCode() => HashCode.Combine(Symbol, Chord, Shapes.Length);
}

/// <summary>
/// The shape table a score's layout writes after the <c>chordDiagrams</c> words —
/// <c>layout { chordDiagrams guitar { Cm7 x35343  G  section Chorus { C x35553 } } }</c>: the
/// chords it lists draw a diagram wherever they are named (where a name alone drew none), the
/// song's entries for the whole score and a <c>section NAME { … }</c>'s for the chords written
/// in that section.
/// </summary>
/// <remarks>
/// <para>
/// Owner's design (HANDOFF §2 K, 2026-09-28; built 2026-09-29, K5 ③): the shape a diagram draws
/// is decided strongest first — the shape WRITTEN at the chord (<c>@chord(C x32013)</c>, a row's
/// <c>C(x32013)</c>), else the SECTION's table entry, else the SONG's, else — in a
/// <c>chordDiagrams … all</c> score — the usual shape (<see cref="ChordShapes.Drawn"/>). The
/// section a chord belongs to is the <c>section</c> it is WRITTEN in (an <c>@chord</c>'s note,
/// a row's bar — a by-part row's inner <c>section A { }</c> included); a phrase outside every
/// section takes the song's entries alone. The section names are wrapped in the keyword so a
/// section named <c>A</c> or <c>C</c> cannot be read as a chord.
/// </para>
/// <para>
/// An entry APPLIES on a tuning when it writes no shape (the usual shape then) or one of its
/// shapes fits the tuning by the written-shape rules (<see cref="ChordShapes.WrittenFor"/>: bound
/// by name, else by string count); an entry whose shapes all belong to other tunings is not
/// used there, silently, and the search goes on to the song's entry (a table can carry shapes
/// for two instruments as a chord can). A chord matches an entry by its spelling — root letter
/// and accidental, quality, slash bass: <c>Db</c> does not cover <c>C#</c>, <c>C</c> does not
/// cover <c>C/E</c>.
/// </para>
/// <para>
/// Compared by value, like the <see cref="Semantics.LayoutPlan"/> that carries it (the
/// incremental compiler's contract: a trivia edit is no change).
/// </para>
/// LILYSHARP-OWN: LilyPond's FretBoards context draws every chord it is given, from its
/// predefined tables (<c>\storePredefinedDiagram</c> adds an entry, file-wide); nothing in it
/// lists the chords that draw, nor binds a table to a section.
/// </remarks>
public sealed class ChordShapeTable : IEquatable<ChordShapeTable>
{
    /// <summary>The entries written outside any <c>section</c>: the whole score's.</summary>
    public ImmutableArray<ChordShapeEntry> Song { get; }

    /// <summary>The <c>section NAME { … }</c> blocks, in source order: each section's entries.</summary>
    public ImmutableArray<(string Section, ImmutableArray<ChordShapeEntry> Entries)> Sections { get; }

    public ChordShapeTable(ImmutableArray<ChordShapeEntry> song,
        ImmutableArray<(string Section, ImmutableArray<ChordShapeEntry> Entries)> sections)
    {
        Song = song;
        Sections = sections;
    }

    /// <summary>A table that lists nothing (<c>chordDiagrams guitar { }</c>).</summary>
    public bool IsEmpty => Song.IsEmpty && Sections.All(s => s.Entries.IsEmpty);

    /// <summary>
    /// The entry that applies to <paramref name="chord"/> named in <paramref name="section"/>
    /// (null: outside every section) on <paramref name="tuning"/>: the section's, else the
    /// song's — the first that APPLIES there (the class remarks); null when none does.
    /// </summary>
    public ChordShapeEntry? Find(string? section, ChordStructure chord, TuningType tuning)
    {
        if (section != null)
            foreach (var (name, entries) in Sections)
                if (name == section && Applying(entries, chord, tuning) is { } inSection)
                    return inSection;
        return Applying(Song, chord, tuning);
    }

    /// <summary>The LAST entry of <paramref name="entries"/> for <paramref name="chord"/> that
    /// applies on <paramref name="tuning"/> (a chord listed twice: the last one wins, like a key
    /// set twice — the reader warns).</summary>
    private static ChordShapeEntry? Applying(ImmutableArray<ChordShapeEntry> entries, ChordStructure chord, TuningType tuning)
    {
        for (int i = entries.Length - 1; i >= 0; i--)
        {
            var e = entries[i];
            if (SameChord(e.Chord, chord)
                && (e.Shapes.IsEmpty || ChordShapes.WrittenFor(tuning, e.Shapes) != null))
                return e;
        }
        return null;
    }

    /// <summary>Whether two chords are one chord as WRITTEN: root letter and accidental, quality,
    /// slash bass (the added-bass flag and a raw suffix aside — a raw suffix never parses into a
    /// table).</summary>
    public static bool SameChord(ChordStructure a, ChordStructure b)
        => a.RootStep == b.RootStep && a.RootAlter == b.RootAlter && a.Quality == b.Quality
           && a.BassStep == b.BassStep && a.BassAlter == b.BassAlter && a.RawSuffix == b.RawSuffix;

    public bool Equals(ChordShapeTable? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        if (!Song.SequenceEqual(other.Song) || Sections.Length != other.Sections.Length)
            return false;
        for (int i = 0; i < Sections.Length; i++)
            if (Sections[i].Section != other.Sections[i].Section
                || !Sections[i].Entries.SequenceEqual(other.Sections[i].Entries))
                return false;
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as ChordShapeTable);

    public override int GetHashCode()
    {
        var h = new HashCode();
        foreach (var e in Song)
            h.Add(e);
        foreach (var (name, entries) in Sections)
        {
            h.Add(name);
            h.Add(entries.Length);
        }
        return h.ToHashCode();
    }
}
