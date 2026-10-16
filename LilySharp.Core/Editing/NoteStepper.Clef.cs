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
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// The step keys on a <c>clef</c> (owner's request 2026-10-16): Ctrl+Shift+Up / Down — the
/// semitone pair — step it to the clef for higher / lower notes, <c>bass</c> → <c>tenor</c> →
/// <c>alto</c> → <c>treble</c>.
/// </summary>
/// <remarks>
/// Lily#'s choices: the walk is the four clefs a clef inside music takes besides
/// <c>treble_8</c> (<see cref="SyntaxFacts.IsClefKeyword"/>), ordered by the pitch of the
/// staff's middle line (D3, A3, C4, B4), and stops at either end; <c>treble_8</c> (middle line
/// B3) steps from where it sorts — Up to <c>alto</c>, Down to <c>tenor</c>. The octave pair
/// (PageUp / PageDown) moves <c>treble</c> and <c>treble_8</c> an octave apart. Only the clef's
/// name changes, never the notes after it.
/// </remarks>
public static partial class NoteStepper
{
    /// <summary>The clefs the semitone pair walks, lowest staff first, with the staff's middle
    /// line.</summary>
    private static readonly (string Name, string MiddleLine)[] ClefWalk =
        [("bass", "D3"), ("tenor", "A3"), ("alto", "C4"), ("treble", "B4")];

    /// <summary>The <c>clef</c> the caret at <paramref name="offset"/> is on — from its keyword
    /// to its name — or null.</summary>
    private static ClefDeclarationSyntax? ClefAt(SyntaxTree tree, int offset)
    {
        foreach (var clef in tree.GetNodes<ClefDeclarationSyntax>())
            if (clef.SourceStart <= offset && offset <= clef.Span.End)
                return clef;
        return null;
    }

    /// <summary>The edit one step of a clef makes (null when it cannot move) and the status-bar
    /// message: the semitone pair walks <see cref="ClefWalk"/>, the octave pair moves
    /// <c>treble</c> an octave.</summary>
    private static (Edit? Edit, string Message) ClefEdit(ClefDeclarationSyntax clef, int dir, bool semitone)
    {
        var token = clef.ClefName;
        string name = token.Text;
        if (name.Length == 0)
            return (null, "clef: write its name first (clef treble)");
        string? next;
        if (!semitone)
        {
            next = (name, dir > 0) switch
            {
                ("treble_8", true) => "treble",
                ("treble", false) => "treble_8",
                _ => null,
            };
            if (next == null)
                return (null, name is "treble" or "treble_8"
                    ? $"clef {name}: no clef an octave {(dir > 0 ? "higher" : "lower")} in music"
                    : $"clef {name}: only treble moves an octave (treble_8)");
        }
        else
        {
            int at = Array.FindIndex(ClefWalk, c => c.Name == name);
            int to = name == "treble_8" ? (dir > 0 ? 2 : 1) : at < 0 ? -1 : at + dir;
            if (at < 0 && name != "treble_8")
                return (null, $"clef {name}: not a clef the step walks");
            if (to < 0 || to >= ClefWalk.Length)
                return (null, $"clef {name}: already the {(dir > 0 ? "highest" : "lowest")} "
                    + "(bass, tenor, alto, treble)");
            next = ClefWalk[to].Name;
        }
        string middle = Array.Find(ClefWalk, c => c.Name == next).MiddleLine ?? "B3";
        return (new Edit(token.Span.Start, token.Span.End, next), $"clef {next}: middle line {middle}");
    }
}
