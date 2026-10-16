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
/// The step keys on an articulation (owner's request 2026-10-16: "@upBow and @downBow should
/// switch on Ctrl+Shift+Up / Down; @hammerOn and @pullOff too; and any other meaningful group
/// should rotate"). Either pair rotates the mark through its group
/// (<see cref="ArticulationGroups"/>): Up to the next name, Down to the one before, the ends
/// wrapping round — so from any member every other is one or two presses away. Only the name
/// changes: a <c>.up</c> / <c>.down</c> placement stays, and nothing sounds.
/// </summary>
/// <remarks>
/// The groups are Lily#'s reading of "meaningful": the two the owner named, and the marks that
/// are the same kind of sign in another degree or direction — length (staccatissimo, staccato,
/// portato, tenuto, shortest first), weight (accent, marcato), fermatas (short, plain, long),
/// the mordent pair (mordent below, prall above), the turn pair, the trill pair (trill,
/// pralltriller), the organ pedal pair (heel, toe), the jazz approaches (scoop from below, plop
/// from above) and departures (fall down, doit up), the breathing pair (breath, caesura).
/// <c>@harmonic</c> / <c>@flageolet</c> draw the same circle, so they are not a group;
/// <c>@tap</c>, <c>@snapPizz</c>, <c>@stopped</c> and <c>@thumb</c> stand alone, and the status
/// bar says so. LILYSHARP-OWN: an editor convenience with no LilyPond counterpart.
/// </remarks>
public static partial class NoteStepper
{
    /// <summary>The groups the step rotates through, each in Up order; every name is one the
    /// registry knows (<see cref="ArticulationRegistry.IsKnown"/>), so a retired spelling drops
    /// out of its group instead of being written.</summary>
    private static readonly string[][] ArticulationGroups =
        [.. new[]
        {
            new[] { "upBow", "downBow" },
            new[] { "hammerOn", "pullOff" },
            new[] { "staccatissimo", "staccato", "portato", "tenuto" },
            new[] { "accent", "marcato" },
            new[] { "shortFermata", "fermata", "longFermata" },
            new[] { "mordent", "prall" },
            new[] { "turn", "reverseTurn" },
            new[] { "trill", "pralltriller" },
            new[] { "heel", "toe" },
            new[] { "scoop", "plop" },
            new[] { "fall", "doit" },
            new[] { "breath", "caesura" },
        }.Select(g => g.Where(ArticulationRegistry.IsKnown).ToArray()).Where(g => g.Length > 1)];

    /// <summary>An articulation the caret at <paramref name="offset"/> is on — strictly after
    /// its <c>@</c>, which also touches the note before it — or null.</summary>
    private static ArticulationSyntax? ArticulationAt(SyntaxTree tree, int offset)
    {
        foreach (var a in tree.GetNodes<ArticulationSyntax>())
            if (a.SourceStart < offset && offset <= a.Span.End)
                return a;
        return null;
    }

    /// <summary>One step of an articulation: the edit (null when it has no group) and the
    /// message — the new name, with the group it was chosen from.</summary>
    private static (Edit? Edit, string Message) ArticulationEdit(ArticulationSyntax articulation, int dir)
    {
        var token = articulation.NameToken;
        string name = token.Text;
        var group = Array.Find(ArticulationGroups, g => Array.IndexOf(g, name) >= 0);
        if (group == null)
            return (null, ArticulationRegistry.IsKnown(name)
                ? $"@{name}: in no group of marks to rotate through"
                : $"@{name}: not an articulation Lily# knows");
        int at = Array.IndexOf(group, name);
        string next = group[((at + dir) % group.Length + group.Length) % group.Length];
        return (new Edit(token.Span.Start, token.Span.End, next), $"@{next} ({string.Join(" / ", group)})");
    }
}
