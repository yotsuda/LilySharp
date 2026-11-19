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
using LilySharp.Core.Music;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// An <c>@struck</c> with no chord held under it
/// (<see cref="DiagnosticCodes.StruckWithNothingHeld"/>; <see cref="StruckNotes"/>): the mark is
/// one member's of a <c>&lt;…&gt;</c> chord, the note struck with the chord and let go, so a single
/// note, a whole chord, a member of a written-out arpeggio and a chord marked on every member
/// have nothing to be struck with.
/// </summary>
internal sealed class StruckNoteValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        foreach (var node in tree.GetRoot().DescendantNodesOfKinds(AnnotationNameValidator.AnnotationKinds))
        {
            if (node is not ArticulationSyntax { Name: StruckNotes.Name } struck)
                continue;
            if (struck.Parent is PitchSyntax pitch && StruckNotes.IsStruck(pitch))
                continue;
            string why = struck.Parent is PitchSyntax { Parent: ChordSyntax }
                ? "every member of this chord is marked, so none of it is held"
                : "this is not a member of a <...> chord";
            _diagnostics.Error(struck.Span, DiagnosticCodes.StruckWithNothingHeld,
                $"'@struck' is a chord member's - the note struck with the chord and let go while the rest is held, "
                + $"drawn as a small black head on the chord's stem - and {why}: the note is drawn and played as "
                + "written. Write it on one member, inside the brackets (<c e g@struck>2).");
        }
    }
}
