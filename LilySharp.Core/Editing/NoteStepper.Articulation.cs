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
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// The step keys on an articulation (owner's request 2026-10-16: "@upBow and @downBow should
/// switch on Ctrl+Shift+Up / Down; @hammerOn and @pullOff too; and any other meaningful group
/// should rotate"). Either pair steps the mark along its group
/// (<see cref="ArticulationGroups"/>): Up to the next name, Down to the one before. A group
/// of up to four STOPS at its ends (owner's decision, same day: "ふたつかみっつ程度なら
/// ローテートしない方が使いやすい" — so Up always means the same thing: up-bow, hammer-on,
/// longer, stronger, above, away upward); only a group of <see cref="RotatesFrom"/> or more
/// members would wrap round, and none does today. Only the name changes: a <c>.up</c> /
/// <c>.down</c> placement stays, and nothing sounds.
/// </summary>
/// <remarks>
/// The groups are Lily#'s reading of "meaningful": the two the owner named, and the marks that
/// are the same kind of sign in another degree or direction, each written in Up order — bowing
/// (downBow, upBow), legato (pullOff, hammerOn — a hammer-on usually climbs), length
/// (staccatissimo, staccato, portato, tenuto), weight (accent, marcato), fermatas (short, plain,
/// long), the mordent pair (mordent below, prall above), the turn pair (reverseTurn starts
/// below, turn above), the trill pair (trill, pralltriller), the organ pedal pair (heel, toe),
/// the jazz approaches (scoop from below, plop from above) and departures (fall down, doit up),
/// the breathing pair (breath, caesura), and the stem's direction (stemDown, stemUp — owner's
/// request 2026-10-17). <c>@harmonic</c> / <c>@flageolet</c> draw the same
/// circle, so they are not a group; <c>@tap</c>, <c>@snapPizz</c>, <c>@stopped</c> and
/// <c>@thumb</c> stand alone, and the status bar says so. LILYSHARP-OWN: an editor convenience
/// with no LilyPond counterpart.
/// </remarks>
public static partial class NoteStepper
{
    /// <summary>The groups the step walks, each in Up order; every name is one Lily# consumes
    /// (<see cref="IsSteppable"/>), so a retired spelling drops out of its group instead of
    /// being written.</summary>
    private static readonly string[][] ArticulationGroups =
        [.. new[]
        {
            new[] { "downBow", "upBow" },
            new[] { "pullOff", "hammerOn" },
            new[] { "staccatissimo", "staccato", "portato", "tenuto" },
            new[] { "accent", "marcato" },
            new[] { "shortFermata", "fermata", "longFermata" },
            new[] { "mordent", "prall" },
            new[] { "reverseTurn", "turn" },
            new[] { "trill", "pralltriller" },
            new[] { "heel", "toe" },
            new[] { "scoop", "plop" },
            new[] { "fall", "doit" },
            new[] { "breath", "caesura" },
            new[] { "stemDown", "stemUp" },
        }.Select(g => g.Where(IsSteppable).ToArray()).Where(g => g.Length > 1)];

    /// <summary>A name the step may write: one the registry draws, or a plain annotation the
    /// collector consumes by name — <c>@stemUp</c> / <c>@stemDown</c> are a stem's direction, not
    /// an articulation (MeasureCollector reads them; AnnotationNameValidator names them).</summary>
    private static bool IsSteppable(string name)
        => ArticulationRegistry.IsKnown(name) || AnnotationNameValidator.PlainFeatureNames.Contains(name);

    /// <summary>The group a mark's name is in, or null: a mark in no group has nothing of its own
    /// to step, so the keys step the note it is written on (<see cref="CarrierOf"/>).</summary>
    private static string[]? GroupOf(string name)
        => Array.Find(ArticulationGroups, g => Array.IndexOf(g, name) >= 0);

    /// <summary>The size from which a group wraps round instead of stopping at its ends (owner's
    /// decision 2026-10-16: two or three — and four, Lily#'s reading — are easier to use when
    /// Up and Down keep their meaning). No group is that large today.</summary>
    private const int RotatesFrom = 5;

    /// <summary>An articulation the caret at <paramref name="offset"/> is on — strictly after
    /// its <c>@</c>, which also touches the note before it — or null.</summary>
    private static ArticulationSyntax? ArticulationAt(SyntaxTree tree, int offset)
    {
        foreach (var a in tree.GetNodes<ArticulationSyntax>())
            if (a.SourceStart < offset && offset <= a.Span.End)
                return a;
        return null;
    }

    /// <summary>One step of an articulation: the edit (null when it has no group, or stands at
    /// an end) and the message — the group's names, nothing more (owner's decision 2026-10-16:
    /// repeating the current name is redundant, and "already the last" was noise), or why
    /// nothing moved.</summary>
    private static (Edit? Edit, string Message) ArticulationEdit(ArticulationSyntax articulation, int dir)
    {
        var token = articulation.NameToken;
        string name = token.Text;
        var group = GroupOf(name);
        if (group == null)
            return (null, IsSteppable(name)
                ? $"@{name}: in no group of marks to rotate through"
                : $"@{name}: not an articulation Lily# knows");
        string listed = string.Join(" / ", group);
        int at = Array.IndexOf(group, name);
        int to = at + dir;
        if (to < 0 || to >= group.Length)
        {
            if (group.Length < RotatesFrom)
                return (null, listed);
            to = (to + group.Length) % group.Length;
        }
        return (new Edit(token.Span.Start, token.Span.End, group[to]), listed);
    }
}
