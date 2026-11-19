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

using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Music;

/// <summary>
/// <c>@struck</c> on a member of a <c>&lt;…&gt;</c> chord: a note struck with the chord and let go
/// while the rest is held (owner's decision 2026-11-19, Calamo request 31:
/// <c>&lt;gis cis' gis'@struck&gt;</c>). Old piano prints write it as a small black head on the
/// chord's stem — Kücken Op.47-3 bar 70, a two-note tremolo whose first chord carries one, the
/// pitch struck once at the <i>ffp</i>.
/// <list type="bullet">
/// <item>THE PAGE: the member's head is the black one (a quarter's) at the cue size, on the
/// chord's stem, with no dot of its own.</item>
/// <item>THE SOUND: it begins with the chord and lasts a quarter, or as long as the chord where
/// that is shorter (<see cref="Longest"/>); in a <c>repeat tremolo</c> it sounds at the first
/// stroke only.</item>
/// </list>
/// The one question the page, the MIDI, the MusicXML and the twin ask, so the four cannot answer
/// differently.
/// </summary>
/// <remarks>LILYSHARP-OWN in what it means. LilyPond can draw the picture
/// (<c>\tweak font-size #-4 \tweak duration-log #2</c> on the chord's note, which the twin
/// writes) and sounds that note as long as the chord, at every stroke of a tremolo.</remarks>
public static class StruckNotes
{
    /// <summary>The annotation's name: <c>@struck</c>.</summary>
    public const string Name = "struck";

    /// <summary>The head a struck member draws: a quarter's, black whatever the chord's is.</summary>
    public const int HeadNoteValue = 4;

    /// <summary>Whether <paramref name="pitch"/> carries an <c>@struck</c>, wherever it stands.</summary>
    public static bool IsMarked(PitchSyntax pitch)
    {
        foreach (var a in pitch.Articulations)
            if (a is ArticulationSyntax { Name: Name })
                return true;
        return false;
    }

    /// <summary>Whether <paramref name="pitch"/> is a STRUCK member: marked, in a
    /// <c>&lt;…&gt;</c> chord that holds at least one member besides the struck ones — the chord
    /// it is struck with. A chord whose every member is marked has nothing held, and none of
    /// them is struck (LYS4084 says so).</summary>
    public static bool IsStruck(PitchSyntax pitch)
        => pitch.Parent is ChordSyntax chord && IsMarked(pitch) && HoldsSomething(chord);

    /// <summary>Whether <paramref name="chord"/> has a member that is not marked
    /// <c>@struck</c>: a pitch, a scale degree, a shape's notes.</summary>
    public static bool HoldsSomething(ChordSyntax chord)
    {
        if (chord.IsShapeChord)
            return true;
        foreach (var _ in chord.Degrees)
            return true;
        foreach (var member in chord.Pitches)
            if (!IsMarked(member))
                return true;
        return false;
    }

    /// <summary>The longest a struck member sounds: a quarter, the head it draws. In a shorter
    /// chord it ends with the chord.</summary>
    public static Fraction Longest => Fraction.Quarter;
}
