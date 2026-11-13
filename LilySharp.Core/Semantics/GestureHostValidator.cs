// This file is part of Lily#.
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Lily# is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// Lily# is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Collections.Generic;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// The gestures that bend a sounding pitch — <c>@scoop</c> and <c>@plop</c> into a note,
/// <c>@fall</c> and <c>@doit</c> out of it, the guitar's <c>@bend(…)</c> — and the one thing all
/// of them need: a note. THE question the validator, the page and the three exporters ask, so a
/// gesture on a rest is refused once and drawn and written nowhere.
/// </summary>
/// <remarks>
/// Until 2026-11-14 (leftover of session 1227) none of the five was checked: <c>r4@scoop</c>
/// passed <c>lysc check</c> and drew its curve in front of the rest, <c>R1@fall</c> off the
/// whole-bar rest, and MusicXML wrote <c>&lt;scoop/&gt;</c> on a <c>&lt;rest/&gt;</c>.
/// </remarks>
public static class GestureHosts
{
    /// <summary>The gesture's name as written (<c>scoop</c>, <c>bend</c> …), or null when
    /// <paramref name="annotation"/> is not one of the family.</summary>
    public static string? NameOf(SyntaxNode annotation) => annotation switch
    {
        ArticulationSyntax
        {
            Type: ArticulationType.Scoop or ArticulationType.Plop
                or ArticulationType.Fall or ArticulationType.Doit,
        } articulation => articulation.NameToken.Text,
        MusicMarkSyntax mark when AnnotationValues.Bend(mark) is not null => mark.Name,
        _ => null,
    };

    /// <summary>Whether <paramref name="annotation"/> is a gesture written on a rest — <c>r</c>,
    /// the spacer <c>s</c>, the whole-bar <c>R</c>: no pitch to bend.</summary>
    public static bool IsOnARest(SyntaxNode annotation)
        => annotation.Parent is RestSyntax && NameOf(annotation) is not null;
}

/// <summary>
/// A scoop, plop, fall, doit or bend written on a rest
/// (<see cref="DiagnosticCodes.GestureNotOnANote"/>; <see cref="GestureHosts"/>).
/// </summary>
internal sealed class GestureHostValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        foreach (var node in tree.GetRoot().DescendantNodesOfKinds(AnnotationNameValidator.AnnotationKinds))
            if (GestureHosts.IsOnARest(node))
            {
                string name = GestureHosts.NameOf(node)!;
                _diagnostics.Error(node.Span, DiagnosticCodes.GestureNotOnANote,
                    $"'@{name}' bends the pitch of a note, and this is written on a rest - so nothing is drawn "
                    + $"for it. Write it on the note the gesture belongs to (c''4@{(name == "bend" ? "bend(full)" : name)}).");
            }
    }
}
