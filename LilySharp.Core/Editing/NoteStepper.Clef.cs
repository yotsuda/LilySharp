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
using System.Linq;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// The step keys on a clef (owner's request 2026-10-16) — a part header's <c>clef bass</c>
/// (<see cref="PropertyAssignmentSyntax"/>, where most books write it) or a <c>clef</c> inside
/// music (<see cref="ClefDeclarationSyntax"/>): Ctrl+Shift+Up / Down — the semitone pair — step
/// it to the clef for higher / lower notes, <c>bass</c> → <c>tenor</c> → <c>alto</c> →
/// <c>treble</c>.
/// </summary>
/// <remarks>
/// Lily#'s choices: the walk is those four clefs, ordered by the pitch of the staff's middle
/// line, and stops at either end; any other clef steps from where its middle line sorts
/// (<c>treble_8</c>, B3: Up <c>alto</c>, Down <c>tenor</c>; <c>bass_8</c> Up <c>bass</c>). The
/// octave pair (PageUp / PageDown) moves <c>treble</c> and <c>bass</c> an octave —
/// <c>treble_8</c> ⇄ <c>treble</c> ⇄ <c>treble^8</c>, <c>bass_8</c> ⇄ <c>bass</c> — as far as
/// the place takes the name (a clef inside music takes <c>treble_8</c> only,
/// <see cref="SyntaxFacts.IsClefKeyword"/>). Only the clef's name changes, never the notes.
/// </remarks>
public static partial class NoteStepper
{
    /// <summary>Every clef the step reads, with the MIDI number of its staff's middle line.</summary>
    private static readonly (string Name, int MiddleLine)[] ClefMiddleLines =
    [
        ("bass_8", 38), ("bass", 50), ("baritone", 53), ("tenor", 57), ("treble_8", 59),
        ("alto", 60), ("mezzosoprano", 64), ("soprano", 67), ("treble", 71), ("treble^8", 83),
    ];

    /// <summary>The clefs the semitone pair walks to, lowest staff first.</summary>
    private static readonly string[] ClefWalk = ["bass", "tenor", "alto", "treble"];

    /// <summary>The clef the caret at <paramref name="offset"/> is on — a <c>clef</c> inside
    /// music or a part header's <c>clef</c>, from its keyword to its name — or null.</summary>
    private static SyntaxNode? ClefAt(SyntaxTree tree, int offset)
    {
        foreach (var clef in tree.GetNodes<ClefDeclarationSyntax>())
            if (clef.SourceStart <= offset && offset <= clef.Span.End)
                return clef;
        foreach (var property in tree.GetNodes<PropertyAssignmentSyntax>())
            if (property.NameToken.Text == "clef" && property.SourceStart <= offset && offset <= property.Span.End)
                return property;
        return null;
    }

    /// <summary>The edit one step of a clef makes (null when it cannot move) and the status-bar
    /// message: the semitone pair walks <see cref="ClefWalk"/>, the octave pair moves
    /// <c>treble</c> or <c>bass</c> an octave.</summary>
    private static (Edit? Edit, string Message) ClefEdit(SyntaxNode clef, int dir, bool semitone)
    {
        bool inMusic = clef is ClefDeclarationSyntax;
        SyntaxTokenNode? token = clef switch
        {
            ClefDeclarationSyntax d => d.ClefName,
            PropertyAssignmentSyntax p when p.Values.ToList() is [SyntaxTokenNode only] => only,
            _ => null,
        };
        string name = token?.Text ?? "";
        if (name.Length == 0)
            return (null, "clef: write its name first (clef treble)");
        int at = Array.FindIndex(ClefMiddleLines, c => c.Name == name);
        if (at < 0)
            return (null, $"clef {name}: not a clef the step walks");
        string? next;
        if (semitone)
        {
            int middle = ClefMiddleLines[at].MiddleLine;
            next = dir > 0
                ? ClefWalk.FirstOrDefault(c => MiddleLineOf(c) > middle)
                : ClefWalk.LastOrDefault(c => MiddleLineOf(c) < middle);
            if (next == null)
                return (null, $"clef {name}: already the {(dir > 0 ? "highest" : "lowest")} "
                    + "(bass, tenor, alto, treble)");
        }
        else
        {
            next = (name, dir > 0) switch
            {
                ("treble_8", true) or ("treble^8", false) => "treble",
                ("treble", true) => "treble^8",
                ("treble", false) => "treble_8",
                ("bass_8", true) => "bass",
                ("bass", false) => "bass_8",
                _ => null,
            };
            if (next == null)
                return (null, name is "treble" or "treble_8" or "treble^8" or "bass" or "bass_8"
                    ? $"clef {name}: no clef an octave {(dir > 0 ? "higher" : "lower")}"
                    : $"clef {name}: only treble and bass move an octave");
            if (inMusic && next != "treble" && next != "treble_8")
                return (null, $"clef {name}: a clef inside music takes no {next} - write it in the part's header");
        }
        return (new Edit(token!.Span.Start, token.Span.End, next), $"clef {next}: middle line "
            + SpellMidi(MiddleLineOf(next)));
    }

    private static int MiddleLineOf(string clef) => Array.Find(ClefMiddleLines, c => c.Name == clef).MiddleLine;

    /// <summary>A middle line's MIDI number as a name: 59 → "B3".</summary>
    private static string SpellMidi(int midi)
        => new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" }[midi % 12] + (midi / 12 - 1);
}
