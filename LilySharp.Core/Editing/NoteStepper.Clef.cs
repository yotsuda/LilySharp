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
using System.Linq;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// The step keys on a clef — a part header's <c>clef bass</c> (<see cref="PropertyAssignmentSyntax"/>,
/// where most books write it) or a <c>clef</c> inside music (<see cref="ClefDeclarationSyntax"/>).
/// Owner's decisions 2026-10-24, replacing the first one's (2026-10-16: four clefs on the semitone
/// pair, an octave on the other): Ctrl+Shift+Up / Down step ONE CLEF AT A TIME through
/// <see cref="ClefOrder"/> — the order Ctrl+Space lists them in, <c>treble</c> at the top — Up
/// up the list and Down down it; Ctrl+Shift+PageUp / PageDown JUMP A GROUP — the G clefs, the C
/// clefs, the F clefs, percussion — to its usual clef (<c>treble</c>, <c>alto</c>, <c>bass</c>,
/// <c>percussion</c>).
/// </summary>
/// <remarks>
/// The walk stops at either end. Only the names the place takes are walked — a clef inside music
/// and a score row's clef word (<c>condensedStaff alto { … }</c>, <c>staff bass gt</c>) take five
/// (<see cref="SyntaxFacts.ClefNameVocabulary"/>), a part header all eleven. Only the clef's name
/// changes, never the notes.
/// </remarks>
public static partial class NoteStepper
{
    /// <summary>Every clef the step reads, with the MIDI number of its staff's middle line
    /// (none for percussion).</summary>
    private static readonly (string Name, int MiddleLine)[] ClefMiddleLines =
    [
        ("bass_8", 38), ("bass", 50), ("baritone", 53), ("tenor", 57), ("treble_8", 59),
        ("alto", 60), ("mezzosoprano", 64), ("soprano", 67), ("treble", 71), ("treble^8", 83),
        ("percussion", 71),
    ];

    /// <summary>The clef groups, in the order of <see cref="ClefOrder"/>, with each one's usual
    /// clef — where PageUp / PageDown land.</summary>
    private static readonly (string Usual, string[] Clefs)[] ClefGroups =
    [
        ("treble", ["treble", "treble^8", "treble_8"]),
        ("alto", ["soprano", "mezzosoprano", "alto", "tenor", "baritone"]),
        ("bass", ["bass", "bass_8"]),
        ("percussion", ["percussion"]),
    ];

    /// <summary>
    /// Every clef in the order the editor lists them — the completion after <c>clef</c> and the
    /// step keys read this one list (owner's decision 2026-10-24: the keys switch in the order
    /// Ctrl+Space lists): high to low sounding staff, <c>treble</c> first, each group together.
    /// </summary>
    public static IReadOnlyList<string> ClefOrder { get; } = [.. ClefGroups.SelectMany(g => g.Clefs)];

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
        // A score row's clef word — `condensedStaff alto { … }`, `combinedStaff bass { … }`,
        // `staff bass gt` (owner's request 2026-10-24): the word itself, which takes the five a
        // clef inside music takes.
        foreach (var node in tree.GetNodes<SyntaxNode>())
        {
            var word = node switch
            {
                CondensedStaffRenderSyntax condensed => condensed.ClefToken,
                CombinedStaffRenderSyntax combined => combined.ClefToken,
                StaffRenderSyntax staff => staff.ClefToken,
                _ => null,
            };
            if (word != null && word.Span.Start <= offset && offset <= word.Span.End)
                return word;
        }
        return null;
    }

    /// <summary>The edit one step of a clef makes (null when it cannot move) and the status-bar
    /// message: the semitone pair walks <see cref="ClefOrder"/> one clef at a time (Up towards
    /// its top), the octave pair jumps to the usual clef of the previous / next group
    /// (<see cref="ClefGroups"/>).</summary>
    private static (Edit? Edit, string Message) ClefEdit(SyntaxNode clef, int dir, bool semitone)
    {
        // A clef inside music and a score row's clef word take the same five names.
        bool inMusic = clef is ClefDeclarationSyntax or SyntaxTokenNode;
        SyntaxTokenNode? token = clef switch
        {
            SyntaxTokenNode word => word,
            ClefDeclarationSyntax d => d.ClefName,
            PropertyAssignmentSyntax p when p.Values.ToList() is [SyntaxTokenNode only] => only,
            _ => null,
        };
        string name = token?.Text ?? "";
        if (name.Length == 0)
            return (null, "clef: write its name first (clef treble)");
        // The clefs this place takes: inside music five, in a part header all of them.
        string[] walk = [.. ClefOrder.Where(c => !inMusic || SyntaxFacts.ClefNameVocabulary.Contains(c))];
        int at = Array.IndexOf(walk, name);
        int toward = -dir;   // +1: down the list, towards bass
        string? next;
        if (semitone)
            next = at < 0 ? (toward > 0 ? walk[0] : walk[^1])
                : at + toward >= 0 && at + toward < walk.Length ? walk[at + toward] : null;
        else
        {
            // The group's usual clef, else (inside music) its first the place takes.
            string? UsualOf(int g) => walk.Contains(ClefGroups[g].Usual) ? ClefGroups[g].Usual
                : ClefGroups[g].Clefs.FirstOrDefault(walk.Contains);
            int group = at < 0 ? (toward > 0 ? -1 : ClefGroups.Length)
                : Array.FindIndex(ClefGroups, g => g.Clefs.Contains(name));
            next = null;
            for (int g = group + toward; g >= 0 && g < ClefGroups.Length && next == null; g += toward)
                next = UsualOf(g);
        }
        if (next == null)
            return (null, $"clef {name}: already the {(toward > 0 ? "last" : "first")} "
                + $"({string.Join(", ", semitone ? walk : walk.Where(c => ClefGroups.Any(g => g.Usual == c)))})");
        return (new Edit(token!.Span.Start, token.Span.End, next),
            next == "percussion" ? "clef percussion" : $"clef {next}: middle line {SpellMidi(MiddleLineOf(next))}");
    }

    private static int MiddleLineOf(string clef) => Array.Find(ClefMiddleLines, c => c.Name == clef).MiddleLine;

    /// <summary>A middle line's MIDI number as a name: 59 → "B3".</summary>
    private static string SpellMidi(int midi)
        => new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" }[midi % 12] + (midi / 12 - 1);
}
