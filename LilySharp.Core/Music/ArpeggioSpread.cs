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
/// A <c>&lt;&lt; … &gt;&gt;</c> group's members with every <c>chord(…)</c> member SPREAD into
/// its notes, lowest sounding first — each note one member of the broken chord.
/// </summary>
/// <remarks>
/// LILYSHARP-OWN: the owner's decision (2026-09-30) — <c>&lt;&lt; chord(C x32010) &gt;&gt;2</c>
/// plays the shape's notes one after another, equally dividing the half; mixed with other
/// members (<c>&lt;&lt; c chord(C x32010) &gt;&gt;</c>) the shape's notes take their places in
/// the sequence. LilyPond has neither the group nor the item.
/// <para>
/// The notes depend on the part's tuning, the capo and the key, so the spread is not a syntax
/// fact: each walker passes the function it plays a <c>chord(…)</c> item with, and reads the
/// group's share count off the spread (<see cref="ShareCount"/>), never off
/// <see cref="ArpeggioSyntax.ShareCount"/>. A spread note is handed to the walker's own chord
/// path as the item narrowed to that one note (<see cref="Entry.Note"/>), so its spelling,
/// string number and absolute octave are the item's.
/// </para>
/// <para>
/// A member's marks land where a reader expects them on a run of notes: its share dots on its
/// LAST note (they extend what is held), a <c>(</c> after it on its FIRST note (the bow covers
/// the spread), a <c>)</c> on its LAST. A shape with no notes on the tuning is not spread: it
/// stays one member and plays as the item does — a silence of its share (LYS1040 warns).
/// </para>
/// </remarks>
public static class ArpeggioSpread
{
    /// <summary>One member of the spread sequence: <see cref="Member"/> as
    /// <see cref="ArpeggioSyntax.Sequence"/> hands it out (its node is the <c>chord(…)</c> item
    /// for a spread note), and the one note it plays when it comes from a spread item.</summary>
    public readonly record struct Entry(ArpeggioMember Member, ShapeNote? Note);

    /// <summary>The spread sequence of <paramref name="group"/>, reading each <c>chord(…)</c>
    /// member's notes with <paramref name="notesOf"/>.</summary>
    public static List<Entry> Of(ArpeggioSyntax group, Func<ChordSyntax, ImmutableArray<ShapeNote>> notesOf)
    {
        var list = new List<Entry>();
        foreach (var m in group.Sequence)
        {
            if (m.Node is not ChordSyntax { IsShapeChord: true } item)
            {
                list.Add(new Entry(m, null));
                continue;
            }
            var notes = ShapeChords.Ascending(notesOf(item)).ToList();
            if (notes.Count == 0)
            {
                list.Add(new Entry(m, null));
                continue;
            }
            for (int i = 0; i < notes.Count; i++)
            {
                bool first = i == 0, last = i == notes.Count - 1;
                list.Add(new Entry(new ArpeggioMember(item, last ? m.Shares : 1,
                    first && m.SlurStart, last && m.SlurEnd,
                    m.SlurStartSource, m.SlurEndSource), notes[i]));
            }
        }
        return list;
    }

    /// <summary>The shares the spread members divide the group's total into.</summary>
    public static int ShareCount(List<Entry> spread)
    {
        int n = 0;
        foreach (var e in spread)
            n += e.Member.Shares;
        return n;
    }
}
